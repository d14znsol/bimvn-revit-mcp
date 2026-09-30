using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Core;
using DSCons.RevitMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

internal static class MepSafety
{
    public static void GuardWrite(Document doc, IEnumerable<Element> elements)
    {
        if (doc.IsReadOnly) throw new CommandResultException(ErrorCodes.ReadOnly, "Model is read-only.");
        if (IsCentralDirect(doc)) throw new CommandResultException(ErrorCodes.CentralBlocked, "Central model opened directly is blocked. Use a Local file or independent copy.");
        foreach (var element in elements)
        {
            // Types, Levels and Views are immutable lookup resources for these
            // operations. Their pinned/ownership state must still participate
            // in the preview fingerprint, but it must not be treated as an
            // attempt to edit a protected model instance.
            if (element is ElementType || element is Level || element is View) continue;
            if (element.Pinned || element.GroupId != ElementId.InvalidElementId) throw new CommandResultException(ErrorCodes.ProtectedElement, "Pinned and grouped elements are blocked in DSCons MCP v1.");
            if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, element.Id) == CheckoutStatus.OwnedByOtherUser) throw new CommandResultException(ErrorCodes.OwnershipBlocked, "Target elements are owned by another user.");
        }
    }
    public static bool IsCentralDirect(Document doc)
    {
        if (!doc.IsWorkshared || string.IsNullOrWhiteSpace(doc.PathName)) return false;
        try { return string.Equals(doc.PathName, ModelPathUtils.ConvertModelPathToUserVisiblePath(doc.GetWorksharingCentralModelPath()), StringComparison.OrdinalIgnoreCase); } catch { return true; }
    }
    public static string DocumentFingerprint(Document doc) => Hash(doc.Title + "|" + doc.PathName + "|" + doc.IsModified + "|" + doc.ActiveView.Id.Val() + "|" + DocumentRevisionTracker.Revision(doc));
    public static string TargetFingerprint(IEnumerable<Element> elements) => Hash(string.Join("|", elements.OrderBy(e => e.Id.Val()).Select(e => e.Id.Val() + ":" + e.GetTypeId().Val() + ":" + e.Pinned + ":" + string.Join(",", MepData.Connectors(e).Select(ConnectorFingerprint).OrderBy(x => x, StringComparer.Ordinal)))));
    private static string ConnectorFingerprint(Connector connector)
    {
        var dimensions = connector.Shape switch
        {
            ConnectorProfileType.Round => "r=" + Math.Round(connector.Radius, 5),
            ConnectorProfileType.Rectangular or ConnectorProfileType.Oval => "w=" + Math.Round(connector.Width, 5) + ",h=" + Math.Round(connector.Height, 5),
            _ => string.Empty
        };
        return connector.Domain + ":" + connector.Shape + ":" + connector.IsConnected + ":" + Math.Round(connector.Origin.X, 5) + "," + Math.Round(connector.Origin.Y, 5) + "," + Math.Round(connector.Origin.Z, 5) + ":" + dimensions;
    }
    private static string Hash(string source) { using var sha = SHA256.Create(); return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(source))); }
    public static IEnumerable<Element> Targets(Document doc, string operation, JObject args)
    {
        if (operation == "mep_connect" || operation == "mep_disconnect") return new[] { doc.GetElement(RevitIdCompatibility.Eid(args.Value<long>("first_element_id"))), doc.GetElement(RevitIdCompatibility.Eid(args.Value<long>("second_element_id"))) }.Where(x => x != null)!;
        if (operation == "mep_move_route" || operation == "mep_change_type" || operation == "mep_change_size") return MepData.Ids(args).Select(doc.GetElement).Where(x => x != null)!;
        if (operation == "mep_create_route") return RouteTargets(doc, args);
        if (operation == "model_create_batch") return (args["routes"] as JArray ?? new JArray()).OfType<JObject>().SelectMany(route => RouteTargets(doc, route));
        if (operation == "mep_place_equipment_batch") return (args["instances"] as JArray ?? new JArray()).OfType<JObject>().SelectMany(item => new[] { item.Value<long>("symbol_id"), item.Value<long>("level_id") }).Distinct().Select(RevitIdCompatibility.Eid).Select(doc.GetElement).Where(x => x != null)!;
        if (operation == "mep_connect_created_batch") return Enumerable.Empty<Element>();
        if (operation == "mep_apply_created_envelopes_batch") return (args["envelopes"] as JArray ?? new JArray()).OfType<JObject>().Select(item => item.Value<long>("material_type_id")).Distinct().Select(RevitIdCompatibility.Eid).Select(doc.GetElement).Where(x => x != null)!;
        if (operation == "documentation_apply") return DocumentationTargets(doc, args);
        if (operation == "bim_changeset") return (args["operations"] as JArray ?? new JArray()).OfType<JObject>().SelectMany(item => Targets(doc, item.Value<string>("operation") ?? string.Empty, item["arguments"] as JObject ?? new JObject()));
        return Enumerable.Empty<Element>();
    }
    private static IEnumerable<Element> RouteTargets(Document doc, JObject args)
    {
        var ids = new List<long>
        {
            args.Value<long>("type_id"),
            args.Value<long?>("system_type_id") ?? ElementId.InvalidElementId.Val(),
            args.Value<long?>("level_id") ?? ElementId.InvalidElementId.Val()
        };
        if (args["inline_accessory"] is JObject accessory) ids.Add(accessory.Value<long>("type_id"));
        return ids.Where(x => x != ElementId.InvalidElementId.Val()).Distinct().Select(RevitIdCompatibility.Eid).Select(doc.GetElement).Where(x => x != null)!;
    }
    private static IEnumerable<Element> DocumentationTargets(Document doc, JObject args)
    {
        var ids = new List<long>();
        if (args.Value<long?>("view_template_id") is long template) ids.Add(template);
        foreach (var item in (args["plan_views"] as JArray ?? new JArray()).OfType<JObject>()) { ids.Add(item.Value<long>("level_id")); ids.Add(item.Value<long>("view_family_type_id")); }
        foreach (var sheet in (args["sheets"] as JArray ?? new JArray()).OfType<JObject>()) { ids.Add(sheet.Value<long>("title_block_type_id")); foreach (var placement in (sheet["placements"] as JArray ?? new JArray()).OfType<JObject>()) ids.Add(placement.Value<long>("view_id")); }
        return ids.Distinct().Select(RevitIdCompatibility.Eid).Select(doc.GetElement).Where(x => x != null)!;
    }
    public static bool IsOperation(string operation) => operation == "mep_create_route" || operation == "mep_connect" || operation == "mep_disconnect" || operation == "mep_move_route" || operation == "mep_change_type" || operation == "mep_change_size" || operation == "model_create_batch" || operation == "mep_place_equipment_batch" || operation == "mep_connect_created_batch" || operation == "mep_apply_created_envelopes_batch" || operation == "bim_changeset" || operation == "documentation_apply";
}

/// <summary>
/// Session-local document revision used by preview/context fingerprints.
/// Revit's IsModified flag is only a boolean, so it cannot distinguish the
/// second and later changes in an already-modified document.
/// </summary>
internal static class DocumentRevisionTracker
{
    private static readonly ConcurrentDictionary<string, long> Revisions = new(StringComparer.Ordinal);
    [ThreadStatic] private static int _suppressionDepth;

    private static string Key(Document doc) => doc.GetHashCode() + "|" + doc.Title + "|" + doc.PathName;
    public static long Revision(Document doc) => Revisions.TryGetValue(Key(doc), out var revision) ? revision : 0;
    public static void Bump(Document doc)
    {
        if (_suppressionDepth > 0) return;
        Revisions.AddOrUpdate(Key(doc), 1, (_, current) => checked(current + 1));
    }
    public static IDisposable Suppress()
    {
        _suppressionDepth++;
        return new SuppressionScope();
    }
    public static void Reset() => Revisions.Clear();

    private sealed class SuppressionScope : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _suppressionDepth = Math.Max(0, _suppressionDepth - 1);
        }
    }
}
