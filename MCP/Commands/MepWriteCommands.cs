using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Core;
using DSCons.RevitMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

internal sealed class MepPreviewCommand : ReadCommand
{
    public MepPreviewCommand() : base("mep_preview", "Preview MEP write operation without changing the model; token expires after 90 seconds.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var operation = args.Value<string>("operation") ?? string.Empty; var parameters = args["arguments"] as JObject ?? new JObject();
        if (!MepSafety.IsOperation(operation)) throw new CommandResultException(ErrorCodes.Unsupported, "Preview supports only v1 MEP operations.");
        if (operation == "mep_create_route")
        {
            var rawPoints = (parameters["points"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            var planner = MepRoutePlanner.Validate(rawPoints.Select(x => new MepPointMm(x.Value<double>("x_mm"), x.Value<double>("y_mm"), x.Value<double>("z_mm"))).ToList());
            if (!planner.IsValid) throw new CommandResultException(ErrorCodes.InvalidParam, planner.Message);
        }
        var targets = MepSafety.Targets(doc, operation, parameters).ToList(); MepSafety.GuardWrite(doc, targets);
        var validation = MepOperation.ValidatePreview(doc, operation, parameters);
        // The dry-run transaction is rolled back before this token is created,
        // so its fingerprint always represents the model state that will be
        // checked again when the token is applied.
        targets = MepSafety.Targets(doc, operation, parameters).ToList();
        var token = new PreviewToken { PreviewId = Guid.NewGuid().ToString("N"), Operation = operation, ArgumentsJson = parameters.ToString(Formatting.None), DocumentFingerprint = MepSafety.DocumentFingerprint(doc), TargetFingerprint = MepSafety.TargetFingerprint(targets), ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(McpConstants.PreviewLifetimeSeconds) };
        BridgeServer.Current?.Store(token);
        return new JObject { ["preview_id"] = token.PreviewId, ["operation"] = operation, ["expires_at_utc"] = token.ExpiresAtUtc, ["model_changed"] = false, ["validation_level"] = "revit_transaction_rollback", ["validation"] = validation, ["next"] = "Call mep_apply_preview with this preview_id, or call the operation directly for Preview → Auto Apply." };
    }
}
internal sealed class MepApplyPreviewCommand : ReadCommand
{
    public MepApplyPreviewCommand() : base("mep_apply_preview", "Apply an existing valid MEP preview; rolls back on failure.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var id = args.Value<string>("preview_id"); if (string.IsNullOrWhiteSpace(id) || BridgeServer.Current == null || !BridgeServer.Current.TryTake(id!, out var token)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Preview does not exist, was already used, or belongs to another Revit session.");
        return MepOperation.Apply(Document(app), token, false);
    }
}
internal sealed class MepAutoApplyCommand : IRevitCommand
{
    public MepAutoApplyCommand(string operation) { Capability = new ToolCapability { Name = operation, Description = "MEP operation using automatic Preview then Apply.", IsWrite = true }; }
    public ToolCapability Capability { get; }
    public JObject Execute(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument?.Document ?? throw new CommandResultException(ErrorCodes.NoDocument, "Open a Revit document first.");
        var targets = MepSafety.Targets(doc, Capability.Name, args).ToList(); MepSafety.GuardWrite(doc, targets);
        var validation = MepOperation.ValidatePreview(doc, Capability.Name, args);
        targets = MepSafety.Targets(doc, Capability.Name, args).ToList();
        var token = new PreviewToken { PreviewId = Guid.NewGuid().ToString("N"), Operation = Capability.Name, ArgumentsJson = args.ToString(Formatting.None), DocumentFingerprint = MepSafety.DocumentFingerprint(doc), TargetFingerprint = MepSafety.TargetFingerprint(targets), ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(McpConstants.PreviewLifetimeSeconds) };
        var result = MepOperation.Apply(doc, token, true);
        result["preview_validation"] = validation;
        return result;
    }
}
internal static class MepOperation
{
    public static JObject ValidatePreview(Document doc, string operation, JObject args)
    {
        if (operation == "model_create_batch" || operation == "bim_changeset" || operation == "documentation_apply") BimContextStore.Require(doc, args.Value<string>("context_id"));
        var targets = MepSafety.Targets(doc, operation, args).ToList();
        MepSafety.GuardWrite(doc, targets);
        using var revisionSuppression = DocumentRevisionTracker.Suppress();
        using var group = new TransactionGroup(doc, "Validate DSCons MCP Preview");
        group.Start();
        try
        {
            JObject simulated;
            using (var transaction = new Transaction(doc, "Simulate DSCons MCP Preview"))
            {
                transaction.Start();
                var failureOptions = transaction.GetFailureHandlingOptions();
                failureOptions.SetFailuresPreprocessor(new MepFailuresPreprocessor());
                transaction.SetFailureHandlingOptions(failureOptions);
                simulated = Execute(doc, operation, args);
                var status = transaction.Commit();
                if (status != TransactionStatus.Committed)
                    throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected the preview simulation because the route or fitting generated an unresolvable failure.");
            }
            group.RollBack();
            return new JObject
            {
                ["status"] = "passed",
                ["simulated_result"] = simulated,
                ["model_changed"] = false,
                ["note"] = "Revit committed the operation inside a TransactionGroup that was then rolled back. Temporary element IDs in simulated_result are not persistent."
            };
        }
        catch (CommandResultException)
        {
            group.RollBack();
            throw;
        }
        catch (Exception ex)
        {
            group.RollBack();
            throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected the preview simulation: " + ex.Message);
        }
    }

    public static JObject Apply(Document doc, PreviewToken preview, bool auto)
    {
        if (preview.ExpiresAtUtc < DateTimeOffset.UtcNow) throw new CommandResultException(ErrorCodes.PreviewExpired, "Preview expired after 90 seconds; preview again.");
        var args = JObject.Parse(preview.ArgumentsJson);
        if (preview.Operation == "model_create_batch" || preview.Operation == "bim_changeset" || preview.Operation == "documentation_apply") BimContextStore.Require(doc, args.Value<string>("context_id"));
        var targets = MepSafety.Targets(doc, preview.Operation, args).ToList(); MepSafety.GuardWrite(doc, targets);
        var currentDocumentFingerprint = MepSafety.DocumentFingerprint(doc);
        var currentTargetFingerprint = MepSafety.TargetFingerprint(targets);
        var documentMatches = preview.DocumentFingerprint == currentDocumentFingerprint;
        var targetsMatch = preview.TargetFingerprint == currentTargetFingerprint;
        if (!documentMatches || !targetsMatch)
            throw new CommandResultException(ErrorCodes.PreviewInvalid, $"Document, target, type or connector changed after Preview. DocumentMatch={documentMatches}; TargetMatch={targetsMatch}. Preview again.");
        using var group = new TransactionGroup(doc, "DSCons MCP " + preview.Operation); group.Start();
        try
        {
            JObject result; using (var transaction = new Transaction(doc, "Apply DSCons MCP Preview"))
            {
                transaction.Start();
                var failureOptions = transaction.GetFailureHandlingOptions();
                failureOptions.SetFailuresPreprocessor(new MepFailuresPreprocessor());
                transaction.SetFailureHandlingOptions(failureOptions);
                result = Execute(doc, preview.Operation, args);
                var status = transaction.Commit();
                if (status != TransactionStatus.Committed) throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rolled back the transaction because the route or fitting generated an unresolvable failure.");
            }
            result["verification"] = ReadBack(doc, preview.Operation, result);
            group.Assimilate(); result["preview_id"] = preview.PreviewId; result["auto_applied"] = auto; result["rolled_back"] = false; return result;
        }
        catch (CommandResultException) { group.RollBack(); throw; }
        catch (Exception ex) { group.RollBack(); throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rolled back the operation: " + ex.Message); }
    }
    internal static JObject Execute(Document doc, string op, JObject args) => op switch { "mep_create_route" => CreateRoute(doc, args), "mep_connect" => Connect(doc, args, false), "mep_disconnect" => Connect(doc, args, true), "mep_move_route" => Move(doc, args), "mep_change_type" => ChangeType(doc, args), "mep_change_size" => ChangeSize(doc, args), "model_create_batch" => CreateBatch(doc, args), "bim_changeset" => ExecuteChangeSet(doc, args), "documentation_apply" => ApplyDocumentation(doc, args), _ => throw new CommandResultException(ErrorCodes.Unsupported, "Unsupported MEP operation.") };
    private static JObject ReadBack(Document doc, string operation, JObject result)
    {
        var ids = new List<long>();
        foreach (var key in new[] { "created_element_ids", "moved_element_ids", "changed_element_ids", "requested_element_ids", "created_view_ids", "created_schedule_ids", "created_sheet_ids", "created_viewport_ids" })
            if (result[key] is JArray array) ids.AddRange(array.Values<long>());
        foreach (var key in new[] { "first_element_id", "second_element_id" })
            if (result.Value<long?>(key) is long id) ids.Add(id);
        var elements = ids.Distinct().Select(RevitIdCompatibility.Eid).Select(doc.GetElement).Where(element => element != null).Cast<Element>().ToList();
        var schedules = elements.OfType<ViewSchedule>().Select(DocumentationScheduleSupport.ReadBack).ToList();
        return new JObject
        {
            ["verified"] = true,
            ["mode"] = "post_commit_read_back",
            ["operation"] = operation,
            ["elements"] = new JArray(elements.Select(MepData.ElementDetail)),
            ["connector_network"] = new JArray(elements.Select(element => new JObject { ["element_id"] = element.Id.Val(), ["connectors"] = new JArray(MepData.Connectors(element).Select(MepData.ConnectorDetail)) })),
            ["schedules"] = new JArray(schedules)
        };
    }
    private static JObject CreateRoute(Document doc, JObject args)
    {
        var kind = args.Value<string>("kind") ?? string.Empty; var typeId = RevitIdCompatibility.Eid(args.Value<long>("type_id")); var systemId = RevitIdCompatibility.Eid(args.Value<long?>("system_type_id") ?? ElementId.InvalidElementId.Val()); var levelId = RevitIdCompatibility.Eid(args.Value<long?>("level_id") ?? ElementId.InvalidElementId.Val()); var rawPoints = (args["points"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var planner = MepRoutePlanner.Validate(rawPoints.Select(x => new MepPointMm(x.Value<double>("x_mm"), x.Value<double>("y_mm"), x.Value<double>("z_mm"))).ToList());
        if (!planner.IsValid) throw new CommandResultException(ErrorCodes.InvalidParam, planner.Message);
        var points = rawPoints.Select(MepData.PointMm).ToList();
        if (typeId == ElementId.InvalidElementId) throw new CommandResultException(ErrorCodes.InvalidParam, "type_id must reference a valid loaded MEP type.");
        if (levelId == ElementId.InvalidElementId) throw new CommandResultException(ErrorCodes.InvalidParam, "level_id is required for route creation.");
        if ((kind == "pipe" || kind == "duct") && systemId == ElementId.InvalidElementId) throw new CommandResultException(ErrorCodes.InvalidParam, "system_type_id is required for pipe and duct routes.");
        var diameterMm = args.Value<double?>("diameter_mm");
        if (diameterMm.HasValue && diameterMm.Value <= 0) throw new CommandResultException(ErrorCodes.InvalidParam, "diameter_mm must be positive.");
        var inline = args["inline_accessory"] as JObject;
        if (inline != null && !string.Equals(kind, "pipe", StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.InvalidParam, "inline_accessory v1 supports only pipe routes.");

        var curves = new List<Element>(); var fittings = new List<Element>(); var accessories = new List<Element>();
        var segmentStarts = new List<Element>(); var segmentEnds = new List<Element>();
        var inlineSegment = inline?.Value<int>("segment_index") ?? -1;
        if (inline != null && (inlineSegment < 0 || inlineSegment >= points.Count - 1)) throw new CommandResultException(ErrorCodes.InvalidParam, "inline_accessory.segment_index is outside the route segment range.");

        for (var i = 1; i < points.Count; i++)
        {
            if (inline != null && i - 1 == inlineSegment)
            {
                var inserted = CreateInlinePipeAccessory(doc, typeId, systemId, levelId, points[i - 1], points[i], inline, diameterMm);
                curves.AddRange(inserted.Curves); accessories.Add(inserted.Accessory);
                segmentStarts.Add(inserted.Curves[0]); segmentEnds.Add(inserted.Curves[inserted.Curves.Count - 1]);
            }
            else
            {
                Element curve = kind switch { "pipe" => Pipe.Create(doc, systemId, typeId, levelId, points[i - 1], points[i]), "duct" => Duct.Create(doc, systemId, typeId, levelId, points[i - 1], points[i]), "conduit" => Conduit.Create(doc, typeId, points[i - 1], points[i], levelId), "cable_tray" => CableTray.Create(doc, typeId, points[i - 1], points[i], levelId), _ => throw new CommandResultException(ErrorCodes.InvalidParam, "kind must be pipe, duct, conduit or cable_tray.") };
                curves.Add(curve); segmentStarts.Add(curve); segmentEnds.Add(curve);
            }
        }
        if (diameterMm.HasValue)
            foreach (var curve in curves) SetRouteDiameter(curve, diameterMm.Value);
        doc.Regenerate();
        for (var i = 1; i < segmentStarts.Count; i++) fittings.Add(CreateElbow(doc, segmentEnds[i - 1], segmentStarts[i]));
        var demoTag = args.Value<string>("demo_tag");
        if (!string.IsNullOrWhiteSpace(demoTag)) foreach (var element in curves.Concat(fittings).Concat(accessories)) SetComments(element, demoTag!);
        var orderedIds = curves.Concat(fittings).Concat(accessories).Select(x => x.Id.Val());
        return new JObject
        {
            ["created_element_ids"] = new JArray(orderedIds),
            ["created_curve_ids"] = new JArray(curves.Select(x => x.Id.Val())),
            ["created_fitting_ids"] = new JArray(fittings.Select(x => x.Id.Val())),
            ["created_accessory_ids"] = new JArray(accessories.Select(x => x.Id.Val())),
            ["segment_count"] = curves.Count,
            ["route_leg_count"] = points.Count - 1,
            ["diameter_mm"] = diameterMm,
            ["demo_tag"] = string.IsNullOrWhiteSpace(demoTag) ? null : demoTag
        };
    }
    private static JObject CreateBatch(Document doc, JObject args)
    {
        var routes = (args["routes"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (routes.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "routes is required.");
        if (routes.Count > 100) throw new CommandResultException(ErrorCodes.InvalidParam, "A batch is limited to 100 routes.");
        var results = new JArray(); var ids = new List<long>();
        foreach (var route in routes)
        {
            var result = CreateRoute(doc, route); results.Add(new JObject { ["name"] = route.Value<string>("name"), ["result"] = result });
            ids.AddRange((result["created_element_ids"] as JArray ?? new JArray()).Values<long>());
        }
        return new JObject { ["route_count"] = routes.Count, ["created_element_ids"] = new JArray(ids), ["routes"] = results };
    }
    private static JObject ExecuteChangeSet(Document doc, JObject args)
    {
        var operations = (args["operations"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (operations.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "operations is required.");
        if (operations.Count > 50) throw new CommandResultException(ErrorCodes.InvalidParam, "A Change Set is limited to 50 operations.");
        var results = new JArray(); var ids = new List<long>();
        foreach (var item in operations)
        {
            var operation = item.Value<string>("operation") ?? string.Empty; var parameters = item["arguments"] as JObject ?? new JObject();
            if (operation == "bim_changeset" || !MepSafety.IsOperation(operation)) throw new CommandResultException(ErrorCodes.InvalidParam, "Unsupported or nested Change Set operation: " + operation);
            var result = Execute(doc, operation, parameters); results.Add(new JObject { ["operation"] = operation, ["result"] = result });
            foreach (var key in new[] { "created_element_ids", "moved_element_ids", "changed_element_ids", "created_view_ids", "created_schedule_ids", "created_sheet_ids", "created_viewport_ids" }) if (result[key] is JArray resultIds) ids.AddRange(resultIds.Values<long>());
        }
        return new JObject { ["name"] = args.Value<string>("name") ?? "BIM Change Set", ["operation_count"] = operations.Count, ["operation_results"] = results, ["created_element_ids"] = new JArray(ids.Distinct()) };
    }
    private static JObject ApplyDocumentation(Document doc, JObject args)
    {
        var templateId = RevitIdCompatibility.Eid(args.Value<long?>("view_template_id") ?? ElementId.InvalidElementId.Val());
        if (templateId != ElementId.InvalidElementId)
        {
            var templateView = doc.GetElement(templateId) as View;
            if (templateView == null || !templateView.IsTemplate) throw new CommandResultException(ErrorCodes.InvalidParam, "view_template_id must reference a View Template in the active document.");
        }
        var createdViews = new List<long>(); var createdSchedules = new List<long>(); var createdSheets = new List<long>(); var createdViewports = new List<long>();
        foreach (var plan in (args["plan_views"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var level = doc.GetElement(RevitIdCompatibility.Eid(plan.Value<long>("level_id"))) as Level;
            var type = doc.GetElement(RevitIdCompatibility.Eid(plan.Value<long>("view_family_type_id"))) as ViewFamilyType;
            if (level == null || type == null || type.ViewFamily != ViewFamily.FloorPlan) throw new CommandResultException(ErrorCodes.InvalidParam, "Each plan_view requires an existing Level and a FloorPlan ViewFamilyType.");
            var view = ViewPlan.Create(doc, type.Id, level.Id);
            if (templateId != ElementId.InvalidElementId) view.ViewTemplateId = templateId;
            var requestedName = plan.Value<string>("name"); if (!string.IsNullOrWhiteSpace(requestedName)) view.Name = requestedName!;
            createdViews.Add(view.Id.Val());
        }
        var schedulePlans = (args["schedules"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var scheduleErrors = new JArray();
        DocumentationScheduleSupport.ValidatePlans(doc, schedulePlans, scheduleErrors);
        if (scheduleErrors.Count > 0) throw new CommandResultException(ErrorCodes.InvalidParam, string.Join(" ", scheduleErrors.Values<string>()));
        foreach (var schedulePlan in schedulePlans) createdSchedules.Add(DocumentationScheduleSupport.Create(doc, schedulePlan).Id.Val());
        var sheetPlans = (args["sheets"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var sheetErrors = new JArray();
        DocumentationSheetSupport.ValidatePlans(doc, sheetPlans, sheetErrors);
        if (sheetErrors.Count > 0) throw new CommandResultException(ErrorCodes.InvalidParam, string.Join(" ", sheetErrors.Values<string>()));
        var placementReadBack = new JArray();
        var numbers = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(x => x.SheetNumber), StringComparer.OrdinalIgnoreCase);
        foreach (var sheetPlan in sheetPlans)
        {
            var number = sheetPlan.Value<string>("number") ?? string.Empty; var name = sheetPlan.Value<string>("name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(name) || !numbers.Add(number)) throw new CommandResultException(ErrorCodes.InvalidParam, "Every sheet needs a unique non-empty number and name.");
            var titleBlock = RevitIdCompatibility.Eid(sheetPlan.Value<long>("title_block_type_id")); if (doc.GetElement(titleBlock) == null) throw new CommandResultException(ErrorCodes.InvalidParam, "title_block_type_id does not exist in the active document.");
            var sheet = ViewSheet.Create(doc, titleBlock); sheet.SheetNumber = number; sheet.Name = name; createdSheets.Add(sheet.Id.Val());
            foreach (var placement in (sheetPlan["placements"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var readBack = DocumentationSheetSupport.PlaceView(doc, sheet, placement);
                createdViewports.Add(readBack.Value<long>("viewport_id"));
                placementReadBack.Add(readBack);
            }
        }
        return new JObject { ["created_view_ids"] = new JArray(createdViews), ["created_schedule_ids"] = new JArray(createdSchedules), ["created_sheet_ids"] = new JArray(createdSheets), ["created_viewport_ids"] = new JArray(createdViewports), ["placement_read_back"] = placementReadBack, ["note"] = "Views/schedules/sheets were created from explicit learner-confirmed choices. Auto-fit placement reports the selected scale and verified usable-region bounds. This command never saves or synchronizes the document." };
    }
    private static JObject Connect(Document doc, JObject args, bool disconnect)
    {
        var first = doc.GetElement(RevitIdCompatibility.Eid(args.Value<long>("first_element_id"))); var second = doc.GetElement(RevitIdCompatibility.Eid(args.Value<long>("second_element_id")));
        if (first == null || second == null) throw new CommandResultException(ErrorCodes.InvalidParam, "Both element IDs must reference existing elements.");
        Connector? a; Connector? b;
        if (disconnect) { (a, b) = ConnectedPair(first, second); }
        else { (a, b) = BestPair(first, second); }
        if (a == null || b == null) throw new CommandResultException(ErrorCodes.InvalidParam, disconnect ? "Could not find a connector pair that is currently connected." : "Could not find compatible unconnected MEP connectors on both elements.");
        if (disconnect) a.DisconnectFrom(b); else if (a.Origin.DistanceTo(b.Origin) < 0.01) a.ConnectTo(b); else doc.Create.NewElbowFitting(a, b);
        return new JObject { ["first_element_id"] = first!.Id.Val(), ["second_element_id"] = second!.Id.Val(), ["disconnected"] = disconnect };
    }
    private static JObject Move(Document doc, JObject args)
    {
        var ids = MepData.Ids(args); if (!ids.Any()) throw new CommandResultException(ErrorCodes.InvalidParam, "element_ids is required.");
        var moveIds = new HashSet<ElementId>(ids);
        foreach (var element in ids.Select(doc.GetElement).Where(x => x != null)!)
            foreach (var fitting in MepData.ConnectedRoutingFittings(element!)) moveIds.Add(fitting.Id);

        ElementTransformUtils.MoveElements(doc, moveIds.ToList(), new XYZ((args.Value<double?>("dx_mm") ?? 0) / 304.8, (args.Value<double?>("dy_mm") ?? 0) / 304.8, (args.Value<double?>("dz_mm") ?? 0) / 304.8));
        return new JObject { ["moved_element_ids"] = new JArray(moveIds.Select(x => x.Val())), ["requested_element_ids"] = new JArray(ids.Select(x => x.Val())) };
    }
    private static JObject ChangeType(Document doc, JObject args) { var ids = MepData.Ids(args); var type = RevitIdCompatibility.Eid(args.Value<long>("type_id")); foreach (var element in ids.Select(doc.GetElement).Where(x => x != null)) element!.ChangeTypeId(type); return new JObject { ["changed_element_ids"] = new JArray(ids.Select(x => x.Val())), ["type_id"] = type.Val() }; }
    private static JObject ChangeSize(Document doc, JObject args)
    {
        var ids = MepData.Ids(args);
        if (ids.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "element_ids is required.");
        var dimensions = new[] { "diameter_mm", "width_mm", "height_mm" }.Where(name => args[name] != null).ToList();
        if (dimensions.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "Provide diameter_mm or width_mm/height_mm.");
        foreach (var name in dimensions)
            if (args.Value<double>(name) <= 0) throw new CommandResultException(ErrorCodes.InvalidParam, name + " must be positive.");

        var elements = ids.Select(doc.GetElement).Where(element => element != null).Cast<Element>().ToList();
        if (elements.Count != ids.Count) throw new CommandResultException(ErrorCodes.InvalidParam, "Every element_id must resolve to an existing element.");
        foreach (var element in elements)
        {
            SetSize(element, DiameterParameter(element), "diameter_mm", args["diameter_mm"]);
            SetSize(element, WidthParameter(element), "width_mm", args["width_mm"]);
            SetSize(element, HeightParameter(element), "height_mm", args["height_mm"]);
        }
        doc.Regenerate();
        var readBack = new JArray(elements.Select(element => SizeReadBack(element, args)));
        return new JObject { ["changed_element_ids"] = new JArray(ids.Select(x => x.Val())), ["size_read_back"] = readBack };
    }
    private static BuiltInParameter DiameterParameter(Element element) => element switch
    {
        Pipe => BuiltInParameter.RBS_PIPE_DIAMETER_PARAM,
        Conduit => BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM,
        _ => BuiltInParameter.RBS_CURVE_DIAMETER_PARAM
    };
    private static BuiltInParameter WidthParameter(Element element) => element is CableTray ? BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM : BuiltInParameter.RBS_CURVE_WIDTH_PARAM;
    private static BuiltInParameter HeightParameter(Element element) => element is CableTray ? BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM : BuiltInParameter.RBS_CURVE_HEIGHT_PARAM;
    private static void SetSize(Element element, BuiltInParameter builtIn, string field, JToken? value)
    {
        if (value == null) return;
        var parameter = element.get_Parameter(builtIn);
        if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
            throw new CommandResultException(ErrorCodes.InvalidParam, field + " is not writable for element " + element.Id.Val() + ".");
        if (!parameter.Set(value.Value<double>() / 304.8))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected " + field + " for element " + element.Id.Val() + ".");
    }
    private static JObject SizeReadBack(Element element, JObject args)
    {
        var result = new JObject { ["element_id"] = element.Id.Val() };
        foreach (var item in new[]
        {
            (Field: "diameter_mm", Parameter: DiameterParameter(element)),
            (Field: "width_mm", Parameter: WidthParameter(element)),
            (Field: "height_mm", Parameter: HeightParameter(element))
        })
        {
            if (args[item.Field] == null) continue;
            var parameter = element.get_Parameter(item.Parameter);
            if (parameter == null || parameter.StorageType != StorageType.Double)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Cannot read back " + item.Field + " for element " + element.Id.Val() + ".");
            var actual = parameter.AsDouble() * 304.8;
            var expected = args.Value<double>(item.Field);
            result[item.Field] = Math.Round(actual, 2);
            if (Math.Abs(actual - expected) > 0.1)
                throw new CommandResultException(ErrorCodes.VerificationFailed, $"{item.Field} verification failed for element {element.Id.Val()}: requested {expected:0.##} mm, read back {actual:0.##} mm.");
        }
        return result;
    }
    private sealed class InlinePipeResult
    {
        public List<Element> Curves { get; } = new();
        public FamilyInstance Accessory { get; set; } = null!;
    }

    private static InlinePipeResult CreateInlinePipeAccessory(Document doc, ElementId pipeTypeId, ElementId systemTypeId, ElementId levelId, XYZ start, XYZ end, JObject inline, double? diameterMm)
    {
        var symbol = doc.GetElement(RevitIdCompatibility.Eid(inline.Value<long>("type_id"))) as FamilySymbol;
        if (symbol == null || symbol.Category?.Id.IntVal() != (int)BuiltInCategory.OST_PipeAccessory)
            throw new CommandResultException(ErrorCodes.InvalidParam, "inline_accessory.type_id must reference a loaded Pipe Accessory FamilySymbol.");
        var level = doc.GetElement(levelId) as Level ?? throw new CommandResultException(ErrorCodes.InvalidParam, "level_id must reference an existing Level.");
        var segment = end - start; var length = segment.GetLength(); var offset = inline.Value<double>("offset_mm") / 304.8;
        if (offset <= 0 || offset >= length) throw new CommandResultException(ErrorCodes.InvalidParam, "inline_accessory.offset_mm must lie strictly inside the selected route segment.");
        var direction = segment.Normalize();
        if (Math.Abs(direction.Z) > 1e-6) throw new CommandResultException(ErrorCodes.InvalidParam, "inline_accessory v1 supports horizontal level-based pipe segments only.");
        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
        var insertion = start + direction * offset;
        var instance = doc.Create.NewFamilyInstance(insertion, symbol, level, StructuralType.NonStructural);
        doc.Regenerate();
        var connectors = PipeAccessoryConnectors(instance);
        var connectorAxis = (connectors[1].Origin - connectors[0].Origin).Normalize();
        if (Math.Abs(connectorAxis.Z) > 1e-4) throw new CommandResultException(ErrorCodes.InvalidParam, "inline_accessory connectors must form a horizontal axis.");
        var angle = Math.Atan2(connectorAxis.X * direction.Y - connectorAxis.Y * direction.X, connectorAxis.X * direction.X + connectorAxis.Y * direction.Y);
        if (Math.Abs(angle) > 1e-9)
        {
            ElementTransformUtils.RotateElement(doc, instance.Id, Line.CreateBound(insertion, insertion + XYZ.BasisZ), angle);
            doc.Regenerate(); connectors = PipeAccessoryConnectors(instance);
        }
        var midpoint = (connectors[0].Origin + connectors[1].Origin) * 0.5;
        if (!midpoint.IsAlmostEqualTo(insertion))
        {
            ElementTransformUtils.MoveElement(doc, instance.Id, insertion - midpoint);
            doc.Regenerate(); connectors = PipeAccessoryConnectors(instance);
        }
        connectors = connectors.OrderBy(x => (x.Origin - start).DotProduct(direction)).ToList();
        if ((connectors[0].Origin - start).DotProduct(direction) <= 1.0 / 304.8 || (end - connectors[1].Origin).DotProduct(direction) <= 1.0 / 304.8)
            throw new CommandResultException(ErrorCodes.InvalidParam, "inline_accessory.offset_mm leaves insufficient straight pipe on one side of the accessory.");
        if (diameterMm.HasValue)
        {
            var expectedRadius = diameterMm.Value / 2.0;
            foreach (var connector in connectors)
                if (Math.Abs(connector.Radius * 304.8 - expectedRadius) > 0.1)
                    throw new CommandResultException(ErrorCodes.InvalidParam, $"inline_accessory connector diameter does not match diameter_mm {diameterMm.Value:0.##}.");
        }

        var first = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, start, connectors[0].Origin);
        var second = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, connectors[1].Origin, end);
        if (diameterMm.HasValue) { SetRouteDiameter(first, diameterMm.Value); SetRouteDiameter(second, diameterMm.Value); }
        doc.Regenerate();
        NearestUnconnected(first, connectors[0].Origin).ConnectTo(connectors[0]);
        NearestUnconnected(second, connectors[1].Origin).ConnectTo(connectors[1]);
        return new InlinePipeResult { Accessory = instance, Curves = { first, second } };
    }

    private static Connector NearestUnconnected(Element element, XYZ point)
    {
        return MepData.Connectors(element).Where(x => !x.IsConnected).OrderBy(x => x.Origin.DistanceTo(point)).FirstOrDefault()
            ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Created pipe has no unconnected end connector.");
    }

    private static List<Connector> PipeAccessoryConnectors(FamilyInstance instance)
    {
        var connectors = MepData.Connectors(instance).Where(x => x.Domain == Domain.DomainPiping && x.ConnectorType == ConnectorType.End && x.Shape == ConnectorProfileType.Round).ToList();
        if (connectors.Count != 2) throw new CommandResultException(ErrorCodes.InvalidParam, "inline_accessory requires exactly two round Piping End connectors.");
        if (connectors.Any(x => x.IsConnected)) throw new CommandResultException(ErrorCodes.InvalidParam, "inline_accessory connectors must be unconnected before insertion.");
        return connectors;
    }

    private static void SetRouteDiameter(Element curve, double diameterMm)
    {
        var parameter = curve.get_Parameter(DiameterParameter(curve));
        if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double || !parameter.Set(diameterMm / 304.8))
            throw new CommandResultException(ErrorCodes.InvalidParam, "diameter_mm is not writable for route element " + curve.Id.Val() + ".");
    }

    private static void SetComments(Element element, string value)
    {
        var parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String || !parameter.Set(value))
            throw new CommandResultException(ErrorCodes.InvalidParam, "Comments is not writable for created element " + element.Id.Val() + ".");
    }

    private static FamilyInstance CreateElbow(Document doc, Element a, Element b) { var pair = BestPair(a, b); if (pair.First == null || pair.Second == null) throw new CommandResultException(ErrorCodes.InvalidParam, "Route segments do not expose compatible unconnected connectors."); try { return doc.Create.NewElbowFitting(pair.First, pair.Second); } catch (Exception ex) { throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit cannot create the required fitting; check routing preference, fitting family, domain and size. " + ex.Message); } }
    private sealed class MepFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var hasError = false;
            foreach (var failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning) failuresAccessor.DeleteWarning(failure);
                else hasError = true;
            }
            return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
    private static (Connector? First, Connector? Second) BestPair(Element? source, Element? other)
    {
        if (source == null || other == null) return (null, null);
        var pairs = from a in MepData.Connectors(source).Where(x => !x.IsConnected)
                    from b in MepData.Connectors(other).Where(y => !y.IsConnected && y.Domain == a.Domain && y.Shape == a.Shape)
                    select (First: a, Second: b, Distance: a.Origin.DistanceTo(b.Origin));
        var best = pairs.OrderBy(x => x.Distance).FirstOrDefault();
        return best.First == null ? (null, null) : (best.First, best.Second);
    }
    private static (Connector? First, Connector? Second) ConnectedPair(Element? source, Element? other)
    {
        if (source == null || other == null) return (null, null);
        var pairs = from a in MepData.Connectors(source).Where(x => x.IsConnected)
                    from b in MepData.Connectors(other).Where(y => y.IsConnected && y.Domain == a.Domain && y.Shape == a.Shape)
                    where a.IsConnectedTo(b)
                    select (First: a, Second: b, Distance: a.Origin.DistanceTo(b.Origin));
        var best = pairs.OrderBy(x => x.Distance).FirstOrDefault();
        return best.First == null ? (null, null) : (best.First, best.Second);
    }
}
