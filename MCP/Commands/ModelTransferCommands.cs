using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>
/// The source adapter intentionally exports a bounded semantic snapshot, not an
/// RVT clone.  It is therefore safe to move between two sequential Revit
/// sessions without passing Revit API objects or silently using IFC/DirectShape.
/// </summary>
internal sealed class ModelTransferExtractCommand : ReadCommand
{
    public ModelTransferExtractCommand() : base("model_transfer_extract", "Read a bounded native MEPF transfer snapshot from a Revit 2025 source document without changing it.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app);
        if (!string.Equals(args.Value<string>("expected_source_revit_version"), "2025", StringComparison.Ordinal) || !string.Equals(app.Application.VersionNumber, "2025", StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.Unsupported, "Model transfer V1 source must be the currently active Revit 2025 document.");
        var requested = (args["scope_element_ids"] as JArray ?? new JArray()).Values<long>().Select(RevitIdCompatibility.Eid).Distinct().ToList();
        if (requested.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "scope_element_ids is required.");
        var max = Math.Min(500, Math.Max(1, args.Value<int?>("max_elements") ?? 200));
        var includeConnected = args.Value<bool?>("include_connected_network") != false;
        var selected = Expand(doc, requested, includeConnected, max);
        if (selected.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "No requested MEP element resolves in the active source document.");
        var resources = new ResourceCollector(doc); var elements = new Dictionary<long, JObject>();
        foreach (var element in selected) elements[element.Id.Val()] = Describe(element, resources);
        var physical = PhysicalConnections(selected, elements); MarkExternalPhysicalConnections(selected, elements); var groups = DependencyGroups(elements, physical);
        var transform = CoordinateTransform(doc);
        return new JObject
        {
            ["schema_version"] = "1.1", ["record_kind"] = "model_transfer_package_v1",
            ["source"] = new JObject
            {
                ["revit_version"] = app.Application.VersionNumber, ["document_title"] = doc.Title,
                ["document_fingerprint"] = MepSafety.DocumentFingerprint(doc), ["document_revision"] = DocumentRevisionTracker.Revision(doc),
                ["modified_at_extract"] = doc.IsModified, ["coordinate_transform"] = transform,
                ["snapshot_stable"] = true, ["source_pid"] = System.Diagnostics.Process.GetCurrentProcess().Id
            },
            ["resources"] = resources.ToJson(), ["elements"] = new JArray(elements.Values.OrderBy(item => item.Value<string>("source_key"), StringComparer.Ordinal)),
            ["physical_connections"] = physical, ["dependency_groups"] = groups,
            ["model_changed"] = false,
            ["boundary"] = "Read-only semantic package. It is not an RVT/RFA down-save, IFC fallback, direct Revit API object transfer, or authorization to modify a target model."
        };
    }

    private static List<Element> Expand(Document doc, IReadOnlyCollection<ElementId> seeds, bool includeConnected, int maximum)
    {
        var queue = new Queue<ElementId>(seeds); var seen = new HashSet<long>(); var result = new List<Element>();
        while (queue.Count > 0 && result.Count < maximum)
        {
            var id = queue.Dequeue(); if (!seen.Add(id.Val())) continue; var element = doc.GetElement(id);
            if (element == null || !MepData.IsMep(element)) continue; result.Add(element);
            if (!includeConnected) continue;
            foreach (var connector in StableConnectors(element))
            foreach (var peer in PhysicalPeers(connector)) if (!seen.Contains(peer.Owner.Id.Val())) queue.Enqueue(peer.Owner.Id);
        }
        return result;
    }

    private static JObject Describe(Element element, ResourceCollector resources)
    {
        var key = SourceKey(element); var common = new JObject { ["source_key"] = key, ["source_element_id"] = element.Id.Val(), ["class"] = element.GetType().Name, ["category"] = CategoryKey(element.Category?.Id.IntVal() ?? 0), ["native_detail"] = MepData.ElementDetail(element) };
        if (element is MEPCurve curve)
        {
            if (curve.Location is not LocationCurve location || location.Curve is not Line line)
            {
                common["kind"] = "unsupported"; common["reason"] = "Only straight native MEP curve centerlines are eligible in V1; flex/arc/spline/slope paths are blocked."; return common;
            }
            var routeKind = curve switch { Pipe => "pipe", Duct => "duct", Conduit => "conduit", CableTray => "cable_tray", _ => "unsupported" };
            if (routeKind == "unsupported") { common["kind"] = "unsupported"; common["reason"] = "MEP curve class is not a V1 transfer route."; return common; }
            common["kind"] = "route"; common["route_kind"] = routeKind; common["type_source_key"] = resources.RouteType(curve.GetTypeId(), routeKind); common["level_source_key"] = resources.Level(curve.LevelId);
            if (routeKind is "pipe" or "duct") common["system_source_key"] = resources.SystemType(MepData.SystemTypeId(curve), routeKind);
            common["points_mm"] = new JArray(MepData.Point(line.GetEndPoint(0)), MepData.Point(line.GetEndPoint(1))); AddCurveSize(common, curve); return common;
        }
        if (element is FamilyInstance instance)
        {
            var category = CategoryKey(instance.Category?.Id.IntVal() ?? 0);
            if (category is "pipe_fitting" or "duct_fitting" or "conduit_fitting" or "cable_tray_fitting")
            {
                var partType = MepData.PartTypeKey(instance.Symbol.Family); var connectors = MepData.PhysicalConnectors(instance).OrderBy(PointKey).ToList();
                common["kind"] = "fitting"; common["fitting_role"] = partType; common["route_kind"] = FittingRouteKind(category);
                common["symbol_source_key"] = resources.Symbol(instance.Symbol); common["connector_signature"] = new JArray(connectors.Select(MepData.ConnectorDetail));
                if (partType != "elbow" || connectors.Count != 2) common["reason"] = "Only a native two-port elbow with explicit symbol mapping and runtime connector/geometry comparison can enter the first connected-network pilot.";
                return common;
            }
            if (!AllowedEquipment.Contains(category) || instance.Location is not LocationPoint point)
            {
                common["kind"] = "unsupported"; common["reason"] = "Only non-hosted Level-based MEP equipment categories are eligible in V1."; return common;
            }
            common["kind"] = "equipment"; common["placement_type"] = instance.Symbol.Family.FamilyPlacementType.ToString(); common["symbol_source_key"] = resources.Symbol(instance.Symbol); common["level_source_key"] = resources.Level(instance.LevelId); common["point_mm"] = MepData.Point(point.Point); common["rotation_degrees"] = Math.Round(point.Rotation * 180.0 / Math.PI, 6); common["connector_signature"] = new JArray(StableConnectors(instance).Select(MepData.ConnectorDetail)); return common;
        }
        common["kind"] = "unsupported"; common["reason"] = "MEP element is not a native route or supported non-hosted equipment instance."; return common;
    }

    private static void AddCurveSize(JObject target, MEPCurve curve)
    {
        var connector = StableConnectors(curve).FirstOrDefault(); if (connector == null) return;
        try
        {
            if (connector.Shape == ConnectorProfileType.Round) target["diameter_mm"] = Math.Round(connector.Radius * 2 * 304.8, 3);
            else if (connector.Shape is ConnectorProfileType.Rectangular or ConnectorProfileType.Oval) { target["width_mm"] = Math.Round(connector.Width * 304.8, 3); target["height_mm"] = Math.Round(connector.Height * 304.8, 3); }
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException) { target["size_unavailable"] = true; }
    }

    private static JArray PhysicalConnections(IEnumerable<Element> elements, IReadOnlyDictionary<long, JObject> details)
    {
        var included = new HashSet<long>(details.Keys); var edges = new JArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            var connectors = StableConnectors(element).ToList();
            for (var index = 0; index < connectors.Count; index++)
            foreach (var peer in PhysicalPeers(connectors[index]))
            {
                if (!included.Contains(peer.Owner.Id.Val())) continue; var peerConnectors = StableConnectors(peer.Owner).ToList(); var peerIndex = peerConnectors.FindIndex(item => SameConnector(item, peer)); if (peerIndex < 0) continue;
                var first = SourceKey(element) + ":" + index; var second = SourceKey(peer.Owner) + ":" + peerIndex; var signature = string.CompareOrdinal(first, second) < 0 ? first + "|" + second : second + "|" + first;
                if (!seen.Add(signature)) continue;
                edges.Add(new JObject { ["from"] = new JObject { ["element_key"] = SourceKey(element), ["connector_index"] = index }, ["to"] = new JObject { ["element_key"] = SourceKey(peer.Owner), ["connector_index"] = peerIndex }, ["connection_kind"] = "physical", ["from_connector"] = MepData.ConnectorDetail(connectors[index]), ["to_connector"] = MepData.ConnectorDetail(peer) });
            }
        }
        return edges;
    }

    private static void MarkExternalPhysicalConnections(IEnumerable<Element> elements, IReadOnlyDictionary<long, JObject> details)
    {
        var included = new HashSet<long>(details.Keys);
        foreach (var element in elements)
        {
            var external = StableConnectors(element).SelectMany(PhysicalPeers).Where(peer => peer.Owner != null && !included.Contains(peer.Owner.Id.Val())).Select(peer => peer.Owner.Id.Val()).Distinct().Count();
            if (external > 0) details[element.Id.Val()]["external_physical_connection_count"] = external;
        }
    }

    private static JArray DependencyGroups(IReadOnlyDictionary<long, JObject> elements, JArray edges)
    {
        var byKey = elements.Values.ToDictionary(item => item.Value<string>("source_key")!, item => item, StringComparer.Ordinal); var adjacency = byKey.Keys.ToDictionary(key => key, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var edge in edges.OfType<JObject>()) { var from = (edge["from"] as JObject)?.Value<string>("element_key"); var to = (edge["to"] as JObject)?.Value<string>("element_key"); if (from != null && to != null && adjacency.ContainsKey(from) && adjacency.ContainsKey(to)) { adjacency[from].Add(to); adjacency[to].Add(from); } }
        var visited = new HashSet<string>(StringComparer.Ordinal); var groups = new JArray(); var ordinal = 0;
        foreach (var start in byKey.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            if (!visited.Add(start)) continue; var queue = new Queue<string>(); queue.Enqueue(start); var keys = new List<string>();
            while (queue.Count > 0) { var current = queue.Dequeue(); keys.Add(current); foreach (var next in adjacency[current]) if (visited.Add(next)) queue.Enqueue(next); }
            var physicalCount = edges.OfType<JObject>().Count(edge =>
            {
                var from = (edge["from"] as JObject)?.Value<string>("element_key"); var to = (edge["to"] as JObject)?.Value<string>("element_key");
                return from != null && to != null && keys.Contains(from) && keys.Contains(to);
            });
            groups.Add(new JObject { ["group_key"] = "network_" + (++ordinal).ToString("D4"), ["element_keys"] = new JArray(keys.OrderBy(key => key, StringComparer.Ordinal)), ["physical_connection_count"] = physicalCount });
        }
        return groups;
    }

    private static JObject CoordinateTransform(Document doc)
    {
        try { var transform = doc.ActiveProjectLocation.GetTotalTransform(); var identity = transform.Origin.GetLength() * 304.8 <= .1 && Math.Abs(transform.BasisX.DotProduct(XYZ.BasisX) - 1) < 1e-9 && Math.Abs(transform.BasisY.DotProduct(XYZ.BasisY) - 1) < 1e-9 && Math.Abs(transform.BasisZ.DotProduct(XYZ.BasisZ) - 1) < 1e-9; return new JObject { ["is_identity"] = identity, ["origin_mm"] = MepData.Point(transform.Origin), ["basis_x"] = MepData.Point(transform.BasisX), ["basis_y"] = MepData.Point(transform.BasisY), ["basis_z"] = MepData.Point(transform.BasisZ) }; }
        catch { return new JObject { ["is_identity"] = false, ["unavailable"] = true }; }
    }

    private static IEnumerable<Connector> StableConnectors(Element element) => MepData.Connectors(element).OrderBy(item => item.Domain.ToString(), StringComparer.Ordinal).ThenBy(item => PointKey(item)).ToList();
    private static string PointKey(Connector connector) { try { return Math.Round(connector.Origin.X, 9) + "," + Math.Round(connector.Origin.Y, 9) + "," + Math.Round(connector.Origin.Z, 9); } catch { return "unavailable"; } }
    private static bool SameConnector(Connector left, Connector right) { if (ReferenceEquals(left, right)) return true; try { return left.Owner.Id == right.Owner.Id && left.Origin.DistanceTo(right.Origin) < 1e-8 && left.ConnectorType == right.ConnectorType; } catch { return false; } }
    private static IEnumerable<Connector> PhysicalPeers(Connector connector)
    {
        try { if (!connector.IsConnected || connector.Owner == null || connector.ConnectorType is not (ConnectorType.End or ConnectorType.Curve or ConnectorType.Physical)) return Enumerable.Empty<Connector>(); return connector.AllRefs.Cast<Connector>().Where(peer => peer.Owner != null && peer.Owner.Id != connector.Owner.Id && (peer.ConnectorType is ConnectorType.End or ConnectorType.Curve or ConnectorType.Physical)); }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException) { return Enumerable.Empty<Connector>(); }
    }
    private static string SourceKey(Element element) => (element is MEPCurve ? "route" : element is FamilyInstance ? "family" : "element") + "_" + element.Id.Val();
    internal static string FittingRouteKind(string category) => category switch { "pipe_fitting" => "pipe", "duct_fitting" => "duct", "conduit_fitting" => "conduit", "cable_tray_fitting" => "cable_tray", _ => "unsupported" };
    private static readonly HashSet<string> AllowedEquipment = new(StringComparer.Ordinal) { "mechanical_equipment", "duct_terminal", "duct_accessory", "pipe_accessory", "plumbing_fixture", "sprinkler", "electrical_equipment", "electrical_fixture", "lighting_fixture" };
    internal static string CategoryKey(int category) => category switch { (int)BuiltInCategory.OST_MechanicalEquipment => "mechanical_equipment", (int)BuiltInCategory.OST_DuctTerminal => "duct_terminal", (int)BuiltInCategory.OST_DuctAccessory => "duct_accessory", (int)BuiltInCategory.OST_PipeAccessory => "pipe_accessory", (int)BuiltInCategory.OST_PlumbingFixtures => "plumbing_fixture", (int)BuiltInCategory.OST_Sprinklers => "sprinkler", (int)BuiltInCategory.OST_ElectricalEquipment => "electrical_equipment", (int)BuiltInCategory.OST_ElectricalFixtures => "electrical_fixture", (int)BuiltInCategory.OST_LightingFixtures => "lighting_fixture", (int)BuiltInCategory.OST_PipeFitting => "pipe_fitting", (int)BuiltInCategory.OST_DuctFitting => "duct_fitting", (int)BuiltInCategory.OST_ConduitFitting => "conduit_fitting", (int)BuiltInCategory.OST_CableTrayFitting => "cable_tray_fitting", _ => "unsupported" };
    internal static string NativeSystemClassification(Element item) => item switch { PipingSystemType piping => piping.SystemClassification.ToString(), MechanicalSystemType mechanical => mechanical.SystemClassification.ToString(), _ => "unsupported" };

    private sealed class ResourceCollector
    {
        private readonly Document document; private readonly Dictionary<long, JObject> levels = new(); private readonly Dictionary<long, JObject> routeTypes = new(); private readonly Dictionary<long, JObject> systemTypes = new(); private readonly Dictionary<long, JObject> symbols = new();
        public ResourceCollector(Document source) { document = source; }
        public string Level(ElementId id) { var level = document.GetElement(id) as Level ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Source MEP element has no resolvable Level."); if (!levels.ContainsKey(id.Val())) levels[id.Val()] = new JObject { ["source_key"] = "level_" + id.Val(), ["kind"] = "level", ["source_id"] = id.Val(), ["name"] = level.Name, ["elevation_mm"] = Math.Round(level.Elevation * 304.8, 3) }; return "level_" + id.Val(); }
        public string RouteType(ElementId id, string routeKind) { var type = document.GetElement(id) as ElementType ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Source MEP route has no resolvable Type."); if (!routeTypes.ContainsKey(id.Val())) routeTypes[id.Val()] = new JObject { ["source_key"] = "route_type_" + id.Val(), ["kind"] = "route_type", ["source_id"] = id.Val(), ["route_kind"] = routeKind, ["name"] = type.Name, ["category"] = type.Category?.Name }; return "route_type_" + id.Val(); }
        public string SystemType(ElementId id, string routeKind) { var type = document.GetElement(id) as ElementType ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Source Pipe/Duct route has no resolvable System Type."); if (!systemTypes.ContainsKey(id.Val())) systemTypes[id.Val()] = new JObject { ["source_key"] = "system_type_" + id.Val(), ["kind"] = "system_type", ["source_id"] = id.Val(), ["route_kind"] = routeKind, ["name"] = type.Name, ["system_classification"] = NativeSystemClassification(type) }; return "system_type_" + id.Val(); }
        public string Symbol(FamilySymbol symbol)
        {
            if (!symbols.ContainsKey(symbol.Id.Val()))
            {
                var category = CategoryKey(symbol.Category?.Id.IntVal() ?? 0);
                symbols[symbol.Id.Val()] = new JObject
                {
                    ["source_key"] = "family_symbol_" + symbol.Id.Val(), ["kind"] = "family_symbol", ["source_id"] = symbol.Id.Val(),
                    ["name"] = symbol.Name, ["family"] = symbol.FamilyName, ["category"] = category,
                    ["placement_type"] = symbol.Family.FamilyPlacementType.ToString(), ["part_type"] = MepData.PartTypeKey(symbol.Family),
                    ["route_kind"] = FittingRouteKind(category)
                };
            }
            return "family_symbol_" + symbol.Id.Val();
        }
        public JObject ToJson() => new() { ["levels"] = new JArray(levels.Values), ["route_types"] = new JArray(routeTypes.Values), ["system_types"] = new JArray(systemTypes.Values), ["family_symbols"] = new JArray(symbols.Values) };
    }
}

internal sealed class ModelTransferDestinationCatalogCommand : ReadCommand
{
    public ModelTransferDestinationCatalogCommand() : base("model_transfer_destination_catalog", "Read only compatible destination resources from a Revit 2023 staging document for explicit model-transfer mapping.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); if (!string.Equals(args.Value<string>("expected_target_revit_version"), "2023", StringComparison.Ordinal) || !string.Equals(app.Application.VersionNumber, "2023", StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.Unsupported, "Model transfer V1 target must be the currently active Revit 2023 document.");
        var limit = Math.Min(5000, Math.Max(1, args.Value<int?>("limit") ?? 1000)); var resources = new List<JObject>();
        resources.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Select(item => new JObject { ["destination_id"] = item.Id.Val(), ["kind"] = "level", ["name"] = item.Name, ["elevation_mm"] = Math.Round(item.Elevation * 304.8, 3) }));
        resources.AddRange(new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<Element>().Select(item => RouteTypeResource(item)!).Concat(new FilteredElementCollector(doc).OfClass(typeof(DuctType)).Cast<Element>().Select(item => RouteTypeResource(item)!)).Concat(new FilteredElementCollector(doc).OfClass(typeof(ConduitType)).Cast<Element>().Select(item => RouteTypeResource(item)!)).Concat(new FilteredElementCollector(doc).OfClass(typeof(CableTrayType)).Cast<Element>().Select(item => RouteTypeResource(item)!)));
        resources.AddRange(new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<Element>().Select(item => SystemTypeResource(item)!).Concat(new FilteredElementCollector(doc).OfClass(typeof(MechanicalSystemType)).Cast<Element>().Select(item => SystemTypeResource(item)!)));
        resources.AddRange(new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().Select(FamilySymbolResource).Where(item => item != null).Cast<JObject>());
        var bounded = resources.OrderBy(item => item.Value<string>("kind"), StringComparer.Ordinal).ThenBy(item => item.Value<string>("name"), StringComparer.Ordinal).ThenBy(item => item.Value<long>("destination_id")).Take(limit).ToList();
        return new JObject { ["schema_version"] = "1.1", ["record_kind"] = "model_transfer_destination_catalog_v1", ["destination"] = new JObject { ["revit_version"] = app.Application.VersionNumber, ["document_title"] = doc.Title, ["document_fingerprint"] = MepSafety.DocumentFingerprint(doc), ["document_revision"] = DocumentRevisionTracker.Revision(doc), ["source_pid"] = System.Diagnostics.Process.GetCurrentProcess().Id }, ["resources"] = new JArray(bounded), ["resource_count"] = bounded.Count, ["resource_limit"] = limit, ["catalog_truncated"] = resources.Count > bounded.Count, ["model_changed"] = false, ["boundary"] = "Read-only destination resource catalog. The user must make explicit mappings; same display name never implies compatible Type or Family. Family symbols remain unverified until an exact compatibility signature exists." };
    }
    private static JObject? RouteTypeResource(Element item) => item switch { PipeType => Resource(item, "pipe"), DuctType => Resource(item, "duct"), ConduitType => Resource(item, "conduit"), CableTrayType => Resource(item, "cable_tray"), _ => null };
    private static JObject Resource(Element item, string routeKind) => new() { ["destination_id"] = item.Id.Val(), ["kind"] = "route_type", ["route_kind"] = routeKind, ["name"] = item.Name, ["category"] = item.Category?.Name };
    private static JObject? SystemTypeResource(Element item) => item switch { PipingSystemType => new JObject { ["destination_id"] = item.Id.Val(), ["kind"] = "system_type", ["route_kind"] = "pipe", ["name"] = item.Name, ["system_classification"] = ModelTransferExtractCommand.NativeSystemClassification(item) }, MechanicalSystemType => new JObject { ["destination_id"] = item.Id.Val(), ["kind"] = "system_type", ["route_kind"] = "duct", ["name"] = item.Name, ["system_classification"] = ModelTransferExtractCommand.NativeSystemClassification(item) }, _ => null };
    private static JObject? FamilySymbolResource(FamilySymbol item)
    {
        var category = ModelTransferExtractCommand.CategoryKey(item.Category?.Id.IntVal() ?? 0); if (category == "unsupported") return null;
        var fittingRouteKind = ModelTransferExtractCommand.FittingRouteKind(category); var partType = MepData.PartTypeKey(item.Family);
        var isFitting = fittingRouteKind != "unsupported"; var isEquipment = !isFitting && item.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased;
        if (!isEquipment && !(isFitting && partType == "elbow")) return null;
        return new JObject
        {
            ["destination_id"] = item.Id.Val(), ["kind"] = "family_symbol", ["name"] = item.Name, ["family"] = item.FamilyName,
            ["category"] = category, ["placement_type"] = item.Family.FamilyPlacementType.ToString(), ["part_type"] = partType,
            ["route_kind"] = fittingRouteKind, ["compatibility_status"] = isFitting ? "requires_rollback_instance_verification" : "unverified"
        };
    }
}

/// <summary>Independent read-back after the user has saved and reopened the staging target in Revit 2023.</summary>
internal sealed class ModelTransferReopenVerifyCommand : ReadCommand
{
    public ModelTransferReopenVerifyCommand() : base("model_transfer_reopen_verify", "Read back a saved/reopened Revit 2023 staging target against the exact native MEPF transfer mapping without changing it.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app);
        if (!string.Equals(args.Value<string>("expected_target_revit_version"), "2023", StringComparison.Ordinal) || !string.Equals(app.Application.VersionNumber, "2023", StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.Unsupported, "Model transfer V1 reopen verification must run in the saved Revit 2023 target.");
        if (doc.IsModified) throw new CommandResultException(ErrorCodes.ContextInvalid, "Save/reopen the staging target before persistence verification; an unsaved target cannot be certified.");
        if (string.IsNullOrWhiteSpace(doc.PathName)) throw new CommandResultException(ErrorCodes.ContextInvalid, "Reopen verification requires a separately saved R23 staging file with a real path.");
        var expected = args["target_elements"] as JArray ?? throw new CommandResultException(ErrorCodes.InvalidParam, "target_elements from the ModelTransferReport is required.");
        if (expected.Count == 0 || expected.Count > 500) throw new CommandResultException(ErrorCodes.InvalidParam, "target_elements must contain 1..500 elements.");
        var readBack = new JArray();
        foreach (var item in expected.OfType<JObject>()) readBack.Add(Verify(doc, item));
        return new JObject
        {
            ["schema_version"] = "1.0", ["record_kind"] = "model_transfer_reopen_verification_v1", ["verified"] = true,
            ["destination"] = new JObject { ["revit_version"] = app.Application.VersionNumber, ["document_title"] = doc.Title, ["document_path"] = doc.PathName, ["document_fingerprint"] = MepSafety.DocumentFingerprint(doc), ["document_revision"] = DocumentRevisionTracker.Revision(doc) },
            ["elements"] = readBack, ["model_changed"] = false,
            ["boundary"] = "Read-only persistence comparison. It verifies only native V1 route/equipment primitives and does not certify systems, circuits, hosted/nested behavior, views or worksharing."
        };
    }

    private static JObject Verify(Document doc, JObject expected)
    {
        var sourceKey = expected.Value<string>("source_key") ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Each target element requires source_key.");
        var element = doc.GetElement(RevitIdCompatibility.Eid(expected.Value<long>("target_element_id"))) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred target " + sourceKey + " is absent after reopen.");
        var kind = expected.Value<string>("kind") ?? string.Empty;
        if (kind == "route") VerifyRoute(element, expected, sourceKey);
        else if (kind == "fitting") VerifyFitting(element, expected, sourceKey);
        else if (kind == "equipment") VerifyEquipment(element, expected, sourceKey);
        else throw new CommandResultException(ErrorCodes.InvalidParam, "Unsupported transfer read-back kind for " + sourceKey + ".");
        return new JObject { ["source_key"] = sourceKey, ["target_element_id"] = element.Id.Val(), ["kind"] = kind, ["detail"] = MepData.ElementDetail(element), ["connectors"] = new JArray(MepData.Connectors(element).Select(MepData.ConnectorDetail)) };
    }

    private static void VerifyRoute(Element element, JObject expected, string sourceKey)
    {
        if (element is not MEPCurve curve || curve.Location is not LocationCurve location || location.Curve is not Line line)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred route " + sourceKey + " is no longer a straight native MEP curve after reopen.");
        VerifyIdentity(element, expected, sourceKey);
        var points = expected["points_mm"] as JArray ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Route " + sourceKey + " expected points are required.");
        if (points.Count != 2) throw new CommandResultException(ErrorCodes.InvalidParam, "V1 route " + sourceKey + " must have exactly two expected points.");
        var first = MepData.PointMm(points[0] as JObject ?? new JObject()); var second = MepData.PointMm(points[1] as JObject ?? new JObject());
        var direct = line.GetEndPoint(0).DistanceTo(first) * 304.8 <= .1 && line.GetEndPoint(1).DistanceTo(second) * 304.8 <= .1;
        var reversed = line.GetEndPoint(0).DistanceTo(second) * 304.8 <= .1 && line.GetEndPoint(1).DistanceTo(first) * 304.8 <= .1;
        if (!direct && !reversed) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred route " + sourceKey + " centerline differs by more than 0.1 mm after reopen.");
        VerifySize(curve, expected, sourceKey);
        VerifyConnectorTopology(curve, expected, sourceKey);
    }

    private static void VerifyFitting(Element element, JObject expected, string sourceKey)
    {
        if (element is not FamilyInstance instance || instance.MEPModel == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " is no longer a native MEP FamilyInstance after reopen.");
        if (!string.Equals(MepData.PartTypeKey(instance.Symbol.Family), expected.Value<string>("fitting_role"), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " Part Type differs after reopen.");
        VerifyIdentity(instance, expected, sourceKey); VerifyBoundingBox(instance, expected, sourceKey); VerifyConnectorSignature(instance, expected, sourceKey); VerifyConnectorTopology(instance, expected, sourceKey);
    }

    private static void VerifyEquipment(Element element, JObject expected, string sourceKey)
    {
        if (element is not FamilyInstance instance || instance.Location is not LocationPoint point) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred equipment " + sourceKey + " is no longer a point-based native FamilyInstance after reopen.");
        VerifyIdentity(instance, expected, sourceKey);
        var expectedPoint = MepData.PointMm(expected["point_mm"] as JObject ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Equipment " + sourceKey + " expected point is required."));
        if (point.Point.DistanceTo(expectedPoint) * 304.8 > .1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred equipment " + sourceKey + " position differs by more than 0.1 mm after reopen.");
        var expectedRadians = (expected.Value<double?>("rotation_degrees") ?? 0) * Math.PI / 180.0; var delta = Math.Atan2(Math.Sin(point.Rotation - expectedRadians), Math.Cos(point.Rotation - expectedRadians));
        if (Math.Abs(delta) * 180.0 / Math.PI > .1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred equipment " + sourceKey + " rotation differs by more than 0.1 degrees after reopen.");
    }

    private static void VerifyIdentity(Element element, JObject expected, string sourceKey)
    {
        if (element.GetTypeId().Val() != expected.Value<long>("type_id") || element.LevelId.Val() != expected.Value<long>("level_id")) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred " + sourceKey + " Type or Level differs after reopen.");
        if (expected.Value<long?>("system_type_id") is long systemType && MepData.SystemTypeId(element).Val() != systemType) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred route " + sourceKey + " System Type differs after reopen.");
        var marker = expected.Value<string>("provenance_marker") ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Transferred " + sourceKey + " requires a checksum-bound provenance_marker.");
        if (!string.Equals(element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString(), marker, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred " + sourceKey + " provenance marker is absent or changed after reopen.");
    }

    private static void VerifySize(MEPCurve curve, JObject expected, string sourceKey)
    {
        foreach (var expectedSize in new[] { ("diameter_mm", curve is Pipe or Conduit ? BuiltInParameter.RBS_PIPE_DIAMETER_PARAM : BuiltInParameter.RBS_CURVE_DIAMETER_PARAM), ("width_mm", curve is CableTray ? BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM : BuiltInParameter.RBS_CURVE_WIDTH_PARAM), ("height_mm", curve is CableTray ? BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM : BuiltInParameter.RBS_CURVE_HEIGHT_PARAM) })
        {
            if (expected[expectedSize.Item1] == null) continue;
            var parameter = curve.get_Parameter(expectedSize.Item2); if (parameter == null || parameter.StorageType != StorageType.Double || Math.Abs(parameter.AsDouble() * 304.8 - expected.Value<double>(expectedSize.Item1)) > .1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred route " + sourceKey + " " + expectedSize.Item1 + " differs by more than 0.1 mm after reopen.");
        }
    }

    private static void VerifyBoundingBox(Element element, JObject expected, string sourceKey)
    {
        if (expected["expected_bounding_box"] is not JObject expectedBox) return;
        var actual = element.get_BoundingBox(null) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " has no bounding box after reopen.");
        var expectedMin = MepData.PointMm(expectedBox["min"] as JObject ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Expected fitting bounding-box min is missing."));
        var expectedMax = MepData.PointMm(expectedBox["max"] as JObject ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Expected fitting bounding-box max is missing."));
        if (actual.Min.DistanceTo(expectedMin) * 304.8 > .1 || actual.Max.DistanceTo(expectedMax) * 304.8 > .1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " bounding box differs by more than 0.1 mm after reopen.");
    }

    private static void VerifyConnectorSignature(Element element, JObject expected, string sourceKey)
    {
        if (expected["expected_connector_signature"] is not JArray expectedArray) return;
        var actual = MepData.PhysicalConnectors(element).ToList();
        if (actual.Count != expectedArray.Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " physical connector count differs after reopen.");
        var remaining = new List<Connector>(actual);
        foreach (var token in expectedArray.OfType<JObject>())
        {
            var expectedOrigin = MepData.PointMm(token["origin"] as JObject ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Expected fitting connector origin is missing."));
            var match = remaining.OrderBy(connector => connector.Origin.DistanceTo(expectedOrigin)).FirstOrDefault();
            if (match == null || match.Origin.DistanceTo(expectedOrigin) * 304.8 > .1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " connector origin differs by more than 0.1 mm after reopen.");
            if (!string.Equals(match.Domain.ToString(), token.Value<string>("domain"), StringComparison.Ordinal) || !string.Equals(match.Shape.ToString(), token.Value<string>("profile"), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " connector domain/profile differs after reopen.");
            if (!string.Equals(match.ConnectorType.ToString(), token.Value<string>("connector_type"), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " physical connector type differs after reopen.");
            if (token["direction"] is JObject direction)
            {
                // PointMm applies one uniform scale factor. AngleTo normalizes both
                // operands, so this preserves the source direction while reusing
                // the bounded x_mm/y_mm/z_mm parser used for connector coordinates.
                var expectedDirection = MepData.PointMm(direction); var angle = match.CoordinateSystem.BasisZ.AngleTo(expectedDirection);
                if (angle * 180.0 / Math.PI > .1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " connector direction differs by more than 0.1 degrees after reopen.");
            }
            VerifyConnectorSize(match, token, sourceKey); remaining.Remove(match);
        }
    }

    private static void VerifyConnectorSize(Connector actual, JObject expected, string sourceKey)
    {
        try
        {
            if (expected.Value<double?>("radius_mm") is double radius && Math.Abs(actual.Radius * 304.8 - radius) > .1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " connector radius differs after reopen.");
            if (expected.Value<double?>("width_mm") is double width && Math.Abs(actual.Width * 304.8 - width) > .1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " connector width differs after reopen.");
            if (expected.Value<double?>("height_mm") is double height && Math.Abs(actual.Height * 304.8 - height) > .1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " connector height differs after reopen.");
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException) { throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred fitting " + sourceKey + " connector size is unreadable after reopen."); }
    }

    private static void VerifyConnectorTopology(Element element, JObject expected, string sourceKey)
    {
        var connectors = MepData.PhysicalConnectors(element).ToList(); var expectedCount = expected.Value<int?>("expected_physical_connector_count") ?? 2;
        if (connectors.Count != expectedCount) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred " + sourceKey + " physical connector count differs after reopen.");
        var expectedOpen = expected.Value<int?>("expected_open_connector_count") ?? (expected.Value<string>("kind") == "route" ? 2 : 0);
        if (connectors.Count(connector => !connector.IsConnected) != expectedOpen) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred " + sourceKey + " open physical connector count differs after reopen.");
        var expectedPeers = new HashSet<long>((expected["expected_connected_target_element_ids"] as JArray ?? new JArray()).Values<long>());
        var actualPeers = new HashSet<long>();
        foreach (var connector in connectors.Where(connector => connector.IsConnected))
        {
            try
            {
                foreach (var peer in connector.AllRefs.Cast<Connector>().Where(peer => peer.Owner != null && peer.Owner.Id != element.Id && peer.ConnectorType is ConnectorType.End or ConnectorType.Curve or ConnectorType.Physical)) actualPeers.Add(peer.Owner.Id.Val());
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred " + sourceKey + " connector topology is unreadable after reopen."); }
        }
        if (!actualPeers.SetEquals(expectedPeers)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Transferred " + sourceKey + " physical peer mapping differs after reopen.");
    }
}
