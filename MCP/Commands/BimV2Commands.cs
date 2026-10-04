using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>Short-lived evidence anchor for an AI turn / BIM production step.</summary>
internal static class BimContextStore
{
    private sealed class Entry { public string Fingerprint { get; set; } = string.Empty; public DateTimeOffset ExpiresAtUtc { get; set; } }
    private static readonly Dictionary<string, Entry> Entries = new();
    private static readonly object Gate = new();
    public static JObject Create(UIApplication app)
    {
        var doc = app.ActiveUIDocument?.Document ?? throw new CommandResultException(ErrorCodes.NoDocument, "Open a Revit document first.");
        var id = Guid.NewGuid().ToString("N"); var expires = DateTimeOffset.UtcNow.AddMinutes(15);
        lock (Gate) { Entries[id] = new Entry { Fingerprint = MepSafety.DocumentFingerprint(doc), ExpiresAtUtc = expires }; }
        return new JObject { ["context_id"] = id, ["expires_at_utc"] = expires, ["document"] = doc.Title, ["document_fingerprint"] = MepSafety.DocumentFingerprint(doc), ["active_view"] = new JObject { ["id"] = doc.ActiveView.Id.Val(), ["name"] = doc.ActiveView.Name }, ["selection_element_ids"] = new JArray(app.ActiveUIDocument!.Selection.GetElementIds().Select(x => x.Val())), ["note"] = "Pass context_id to V2 write tools. It expires after 15 minutes or is invalidated when the document/view state changes." };
    }
    public static void Require(Document doc, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new CommandResultException(ErrorCodes.ContextInvalid, "context_id from bim_context_snapshot is required for V2 write operations.");
        Entry? entry;
        lock (Gate) { Entries.TryGetValue(id!, out entry); if (entry != null && entry.ExpiresAtUtc < DateTimeOffset.UtcNow) Entries.Remove(id!); }
        if (entry == null || entry.ExpiresAtUtc < DateTimeOffset.UtcNow) throw new CommandResultException(ErrorCodes.ContextInvalid, "context_id is unknown or expired. Call bim_context_snapshot again.");
        if (!string.Equals(entry.Fingerprint, MepSafety.DocumentFingerprint(doc), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.ContextInvalid, "Revit document or active view changed after the context snapshot. Re-anchor with bim_context_snapshot.");
    }
}

internal sealed class BimContextSnapshotCommand : ReadCommand
{
    public BimContextSnapshotCommand() : base("bim_context_snapshot", "Short-lived document/view/selection context anchor for the BIM production pipeline.") { }
    public override JObject Execute(UIApplication app, JObject args) => BimContextStore.Create(app);
}

internal sealed class BimModelCatalogCommand : ReadCommand
{
    public BimModelCatalogCommand() : base("bim_model_catalog", "Read Level, MEP type/system type, view template/type and title block IDs from the active document.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var limit = Math.Min(2000, Math.Max(1, args.Value<int?>("limit") ?? 500));
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(x => x.Elevation).Take(limit).Select(x => new JObject { ["id"] = x.Id.Val(), ["name"] = x.Name, ["elevation_mm"] = Math.Round(x.Elevation * 304.8, 2) });
        var typeCategories = new HashSet<int>
        {
            (int)BuiltInCategory.OST_PipeCurves, (int)BuiltInCategory.OST_DuctCurves,
            (int)BuiltInCategory.OST_Conduit, (int)BuiltInCategory.OST_CableTray,
            (int)BuiltInCategory.OST_PipingSystem, (int)BuiltInCategory.OST_DuctSystem,
            (int)BuiltInCategory.OST_PipeInsulations, (int)BuiltInCategory.OST_DuctInsulations,
            (int)BuiltInCategory.OST_DuctLinings
        };
        var mepTypes = new FilteredElementCollector(doc).WhereElementIsElementType().Where(e => e.Category != null && typeCategories.Contains(e.Category.Id.IntVal())).Take(limit).Select(e => new JObject { ["id"] = e.Id.Val(), ["name"] = e.Name, ["category"] = e.Category?.Name, ["class"] = e.GetType().Name });
        var viewTypes = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().Take(limit).Select(x => new JObject { ["id"] = x.Id.Val(), ["name"] = x.Name, ["view_family"] = x.ViewFamily.ToString() });
        var templates = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(x => x.IsTemplate).Take(limit).Select(x => new JObject { ["id"] = x.Id.Val(), ["name"] = x.Name, ["view_type"] = x.ViewType.ToString() });
        var titleBlocks = DocumentationSheetSupport.DiscoverTitleBlocks(doc, limit);
        var mepSymbolCategories = new HashSet<int>
        {
            (int)BuiltInCategory.OST_MechanicalEquipment, (int)BuiltInCategory.OST_DuctTerminal,
            (int)BuiltInCategory.OST_DuctAccessory, (int)BuiltInCategory.OST_PipeAccessory,
            (int)BuiltInCategory.OST_PipeFitting, (int)BuiltInCategory.OST_DuctFitting,
            (int)BuiltInCategory.OST_PlumbingFixtures, (int)BuiltInCategory.OST_Sprinklers,
            (int)BuiltInCategory.OST_ElectricalEquipment, (int)BuiltInCategory.OST_LightingFixtures
        };
        var mepFamilySymbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Where(x => x.Category != null && mepSymbolCategories.Contains(x.Category.Id.IntVal())).Take(limit)
            .Select(x => new JObject { ["id"] = x.Id.Val(), ["name"] = x.Name, ["family"] = x.FamilyName, ["category"] = x.Category?.Name, ["active"] = x.IsActive, ["placement_type"] = x.Family.FamilyPlacementType.ToString() });
        return new JObject { ["levels"] = new JArray(levels), ["mep_types_and_system_types"] = new JArray(mepTypes), ["mep_family_symbols"] = new JArray(mepFamilySymbols), ["view_family_types"] = new JArray(viewTypes), ["view_templates"] = new JArray(templates), ["title_blocks"] = titleBlocks, ["note"] = "Show the available title block choices and measured sheet sizes to the learner before planning a Sheet. Use these IDs only with a fresh bim_context_snapshot and the matching active document." };
    }
}

internal sealed class MepNetworkExploreCommand : ReadCommand
{
    public MepNetworkExploreCommand() : base("mep_network_explore", "Read MEP network topology from seed elements without changing the model.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var seeds = MepData.Ids(args).Distinct().ToList();
        if (seeds.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "element_ids is required.");
        var maxDepth = Math.Min(20, Math.Max(1, args.Value<int?>("max_depth") ?? 5));
        var limit = Math.Min(2000, Math.Max(1, args.Value<int?>("limit") ?? 500));
        var queue = new Queue<(ElementId Id, int Depth)>(); var visited = new HashSet<long>(); var edges = new JArray();
        foreach (var seed in seeds) queue.Enqueue((seed, 0));
        while (queue.Count > 0 && visited.Count < limit)
        {
            var current = queue.Dequeue(); if (!visited.Add(current.Id.Val())) continue;
            var element = doc.GetElement(current.Id); if (element == null || !MepData.IsMep(element)) continue;
            if (current.Depth >= maxDepth) continue;
            foreach (var connector in MepData.Connectors(element))
            foreach (var other in connector.AllRefs.Cast<Connector>())
            {
                var owner = other.Owner; if (owner == null || owner.Id == element.Id) continue;
                edges.Add(new JObject { ["from_element_id"] = element.Id.Val(), ["to_element_id"] = owner.Id.Val(), ["from_connector"] = MepData.ConnectorDetail(connector), ["to_connector"] = MepData.ConnectorDetail(other), ["depth"] = current.Depth + 1 });
                if (!visited.Contains(owner.Id.Val())) queue.Enqueue((owner.Id, current.Depth + 1));
            }
        }
        var elements = visited.Select(RevitIdCompatibility.Eid).Select(doc.GetElement).Where(e => e != null).Cast<Element>().ToList();
        return new JObject { ["seed_element_ids"] = new JArray(seeds.Select(x => x.Val())), ["max_depth"] = maxDepth, ["element_count"] = elements.Count, ["truncated"] = queue.Count > 0, ["elements"] = new JArray(elements.Select(MepData.ElementDetail)), ["connections"] = edges, ["disclaimer"] = "Network topology is read from current connector references. Review engineering intent before any change." };
    }
}

internal sealed class CoordinationLinksCommand : ReadCommand
{
    public CoordinationLinksCommand() : base("coordination_links", "List loaded Revit links available for Architecture/Structure coordination.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app);
        var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().Select(link =>
        {
            var linked = link.GetLinkDocument(); var transform = link.GetTransform();
            return new JObject { ["link_instance_id"] = link.Id.Val(), ["name"] = link.Name, ["loaded"] = linked != null, ["linked_document"] = linked?.Title, ["transform_origin_mm"] = MepData.Point(transform.Origin) };
        });
        return new JObject { ["links"] = new JArray(links), ["note"] = "Only loaded links can be scanned. This tool never unloads, reloads or modifies a link." };
    }
}

internal sealed class CoordinationScanCommand : ReadCommand
{
    public CoordinationScanCommand() : base("coordination_scan", "Read-only MEP-to-linked-model bounding-box coordination scan.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var requestedIds = MepData.Ids(args).Distinct().ToList();
        var limit = Math.Min(1000, Math.Max(1, args.Value<int?>("limit") ?? 200));
        var clearance = Math.Max(0, args.Value<double?>("clearance_mm") ?? 0) / 304.8;
        var categories = new HashSet<string>((args["linked_categories"] as JArray ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x))!, StringComparer.OrdinalIgnoreCase);
        var sources = (requestedIds.Count > 0 ? requestedIds.Select(doc.GetElement).Where(x => x != null).Cast<Element>() : new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(MepData.IsMep)).Where(MepData.IsMep).Take(Math.Min(1000, limit * 4)).ToList();
        var findings = new JArray(); var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().Where(x => x.GetLinkDocument() != null).ToList();
        foreach (var link in links)
        {
            var linked = link.GetLinkDocument()!; var transform = link.GetTransform();
            var candidates = new FilteredElementCollector(linked).WhereElementIsNotElementType().Where(e => e.Category != null && (categories.Count == 0 || categories.Contains(e.Category.Name))).Take(5000).ToList();
            foreach (var source in sources)
            {
                var sourceBounds = Bounds.From(source.get_BoundingBox(null), null); if (sourceBounds == null) continue;
                foreach (var candidate in candidates)
                {
                    var linkedBounds = Bounds.From(candidate.get_BoundingBox(null), transform); if (linkedBounds == null || !sourceBounds.Expand(clearance).Intersects(linkedBounds)) continue;
                    findings.Add(new JObject { ["code"] = clearance > 0 ? "ClearanceReview" : "BoundingBoxOverlap", ["severity"] = clearance > 0 ? "review" : "warning", ["source_element_id"] = source.Id.Val(), ["source_category"] = source.Category?.Name, ["linked_element_id"] = candidate.Id.Val(), ["linked_category"] = candidate.Category?.Name, ["link_instance_id"] = link.Id.Val(), ["link_document"] = linked.Title, ["clearance_mm"] = Math.Round(clearance * 304.8, 2), ["message"] = "Bounding boxes overlap in host coordinates. Confirm actual geometry and engineering clearance before rerouting." });
                    if (findings.Count >= limit) break;
                }
                if (findings.Count >= limit) break;
            }
            if (findings.Count >= limit) break;
        }
        return new JObject { ["source_element_count"] = sources.Count, ["loaded_link_count"] = links.Count, ["finding_count"] = findings.Count, ["truncated"] = findings.Count >= limit, ["findings"] = findings, ["method"] = "transformed bounding-box evidence", ["disclaimer"] = "This is a conservative coordination triage, not a solid-geometry clash certificate and never auto-reroutes." };
    }

    private sealed class Bounds
    {
        public XYZ Min { get; set; } = XYZ.Zero; public XYZ Max { get; set; } = XYZ.Zero;
        public static Bounds? From(BoundingBoxXYZ? box, Transform? transform)
        {
            if (box == null) return null; transform ??= Transform.Identity;
            var corners = new[] { new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z), new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z), new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z), new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z) }.Select(transform.OfPoint).ToList();
            return new Bounds { Min = new XYZ(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z)), Max = new XYZ(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z)) };
        }
        public Bounds Expand(double amount) => new() { Min = new XYZ(Min.X - amount, Min.Y - amount, Min.Z - amount), Max = new XYZ(Max.X + amount, Max.Y + amount, Max.Z + amount) };
        public bool Intersects(Bounds other) => Min.X <= other.Max.X && Max.X >= other.Min.X && Min.Y <= other.Max.Y && Max.Y >= other.Min.Y && Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
    }
}

internal sealed class QuantityTakeoffCommand : ReadCommand
{
    public QuantityTakeoffCommand() : base("quantity_takeoff", "Read model quantities grouped by category, system, level or type.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var categories = new HashSet<string>((args["categories"] as JArray ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x))!, StringComparer.OrdinalIgnoreCase);
        var system = args.Value<string>("system_name") ?? string.Empty; var level = args.Value<string>("level_name") ?? string.Empty; var groupBy = args.Value<string>("group_by") ?? "category";
        var limit = Math.Min(10000, Math.Max(1, args.Value<int?>("limit") ?? 5000));
        var elements = new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(MepData.IsMep).Where(e => categories.Count == 0 || (e.Category != null && categories.Contains(e.Category.Name))).Where(e => string.IsNullOrWhiteSpace(system) || MepData.SystemName(e).IndexOf(system, StringComparison.OrdinalIgnoreCase) >= 0).Where(e => string.IsNullOrWhiteSpace(level) || string.Equals(doc.GetElement(e.LevelId)?.Name, level, StringComparison.OrdinalIgnoreCase)).Take(limit).ToList();
        var groups = elements.GroupBy(e => GroupKey(doc, e, groupBy)).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(group => Summary(doc, group.Key, group));
        return new JObject { ["unit_system"] = "mm, m2, m3", ["group_by"] = groupBy, ["element_count"] = elements.Count, ["truncated"] = elements.Count >= limit, ["groups"] = new JArray(groups), ["total"] = Summary(doc, "TOTAL", elements), ["disclaimer"] = "Quantities are current model takeoff evidence. Validate project-specific measurement, waste and procurement rules before issuing a BOQ." };
    }
    private static string GroupKey(Document doc, Element e, string groupBy) => groupBy.ToLowerInvariant() switch { "system" => string.IsNullOrWhiteSpace(MepData.SystemName(e)) ? "<no system>" : MepData.SystemName(e), "level" => doc.GetElement(e.LevelId)?.Name ?? "<no level>", "type" => doc.GetElement(e.GetTypeId())?.Name ?? "<no type>", _ => e.Category?.Name ?? "<no category>" };
    private static JObject Summary(Document doc, string key, IEnumerable<Element> elements)
    {
        var list = elements.ToList(); var lengthFt = list.Sum(e => (e.Location as LocationCurve)?.Curve.Length ?? 0); var areaFt2 = list.Sum(e => ParameterDouble(e, "Area")); var volumeFt3 = list.Sum(e => ParameterDouble(e, "Volume"));
        return new JObject { ["group"] = key, ["count"] = list.Count, ["length_mm"] = Math.Round(lengthFt * 304.8, 2), ["area_m2"] = Math.Round(areaFt2 * 0.09290304, 4), ["volume_m3"] = Math.Round(volumeFt3 * 0.028316846592, 4), ["element_ids"] = new JArray(list.Take(1000).Select(e => e.Id.Val())) };
    }
    private static double ParameterDouble(Element element, string name) { var parameter = element.LookupParameter(name); return parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double ? parameter.AsDouble() : 0; }
}

internal sealed class DocumentationPlanCommand : ReadCommand
{
    public DocumentationPlanCommand() : base("documentation_plan", "Discover available schedule fields, then validate learner-confirmed columns, sorting/grouping, totals, views and sheets without changing the model.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var errors = new JArray();
        var topLevelViewIds = (args["view_ids"] as JArray ?? new JArray()).Values<long>();
        var sheetViewIds = (args["sheets"] as JArray ?? new JArray()).OfType<JObject>()
            .SelectMany(sheet => (sheet["placements"] as JArray ?? new JArray()).OfType<JObject>().Select(placement => placement.Value<long>("view_id")));
        var viewIds = topLevelViewIds.Concat(sheetViewIds).Distinct().Select(RevitIdCompatibility.Eid).ToList();
        foreach (var id in viewIds)
        {
            if (doc.GetElement(id) is not View view) errors.Add("view_id " + id.Val() + " does not resolve to a View.");
            else if (view.IsTemplate) errors.Add("view_id " + id.Val() + " is a View Template and cannot be placed as a regular view.");
        }
        if (args.Value<long?>("view_template_id") is long templateId)
        {
            var template = doc.GetElement(RevitIdCompatibility.Eid(templateId)) as View;
            if (template == null || !template.IsTemplate) errors.Add("view_template_id must resolve to a View Template.");
        }
        if (args.Value<long?>("title_block_type_id") is long titleBlockId)
        {
            var titleBlock = doc.GetElement(RevitIdCompatibility.Eid(titleBlockId));
            if (titleBlock is not ElementType || titleBlock.Category?.Id.IntVal() != (int)BuiltInCategory.OST_TitleBlocks) errors.Add("title_block_type_id must resolve to a Title Block type.");
        }
        var existingNumbers = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(x => x.SheetNumber), StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in (args["sheets"] as JArray ?? new JArray()).OfType<JObject>()) { var number = sheet.Value<string>("number") ?? string.Empty; if (string.IsNullOrWhiteSpace(number)) errors.Add("Sheet number is required."); else if (existingNumbers.Contains(number)) errors.Add("Sheet number already exists: " + number); }
        DocumentationSheetSupport.ValidatePlans(doc, (args["sheets"] as JArray ?? new JArray()).OfType<JObject>(), errors);
        DocumentationScheduleSupport.ValidatePlans(doc, (args["schedules"] as JArray ?? new JArray()).OfType<JObject>(), errors);
        var discoveryCategories = (args["schedule_discovery_categories"] as JArray ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!);
        var fieldCatalog = DocumentationScheduleSupport.Discover(doc, discoveryCategories);
        return new JObject { ["valid"] = errors.Count == 0, ["errors"] = errors, ["schedule_field_catalog"] = fieldCatalog, ["title_block_catalog"] = args.Value<bool?>("discover_title_blocks") == true ? DocumentationSheetSupport.DiscoverTitleBlocks(doc, 200) : new JArray(), ["requested_view_ids"] = new JArray(viewIds.Select(x => x.Val())), ["existing_sheet_count"] = existingNumbers.Count, ["existing_schedule_count"] = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).GetElementCount(), ["model_changed"] = false, ["learner_workflow"] = "Before a Schedule, confirm columns, sort/group and totals from the available field catalog. Before a Sheet, show the available title blocks and sizes, ask which one to use, propose crop scope/reserved title-block area/allowed scales, then preview auto-fit and obtain learner confirmation before apply.", ["note"] = "This tool only discovers resources and validates a plan. Use documentation_apply with a fresh context_id to change the model." };
    }
}

internal sealed class BimChangeSetPreviewCommand : ReadCommand
{
    public BimChangeSetPreviewCommand() : base("bim_changeset_preview", "Preview a multi-operation BIM Change Set with complete rollback.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); BimContextStore.Require(doc, args.Value<string>("context_id"));
        var operations = args["operations"] as JArray ?? new JArray(); if (operations.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "operations is required.");
        var validation = MepOperation.ValidatePreview(doc, "bim_changeset", args); var targets = MepSafety.Targets(doc, "bim_changeset", args).ToList();
        var token = new PreviewToken { PreviewId = Guid.NewGuid().ToString("N"), Operation = "bim_changeset", ArgumentsJson = args.ToString(Formatting.None), DocumentFingerprint = MepSafety.DocumentFingerprint(doc), TargetFingerprint = MepSafety.TargetFingerprint(targets), ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(McpConstants.PreviewLifetimeSeconds) };
        BridgeServer.Current?.Store(token);
        return new JObject { ["preview_id"] = token.PreviewId, ["name"] = args.Value<string>("name") ?? "BIM Change Set", ["operation_count"] = operations.Count, ["expires_at_utc"] = token.ExpiresAtUtc, ["model_changed"] = false, ["validation_level"] = "revit_transactiongroup_rollback", ["validation"] = validation, ["next"] = "Call bim_changeset_apply with this preview_id." };
    }
}

internal sealed class BimChangeSetApplyCommand : ReadCommand
{
    public BimChangeSetApplyCommand() : base("bim_changeset_apply", "Apply an existing valid BIM Change Set preview atomically.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var id = args.Value<string>("preview_id"); if (string.IsNullOrWhiteSpace(id) || BridgeServer.Current == null || !BridgeServer.Current.TryTake(id!, out var token)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Change Set preview does not exist, was already used, or belongs to another Revit session.");
        if (!string.Equals(token.Operation, "bim_changeset", StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "preview_id does not belong to a BIM Change Set.");
        return MepOperation.Apply(Document(app), token, false);
    }
}
