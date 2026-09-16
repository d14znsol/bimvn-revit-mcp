using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Core;
using DSCons.RevitMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

internal sealed class MepElementDetailCommand : ReadCommand
{
    public MepElementDetailCommand() : base("mep_element_detail", "Category, class, Family/Type, Level, System, parameter và bounding box MEP.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var ids = MepData.Ids(args); if (ids.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "element_ids is required.");
        return new JObject { ["elements"] = new JArray(ids.Select(doc.GetElement).Where(x => x != null).Select(MepData.ElementDetail)) };
    }
}
internal sealed class MepConnectorNetworkCommand : ReadCommand
{
    public MepConnectorNetworkCommand() : base("mep_connector_network", "Domain, profile, size, direction và network connector MEP.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var ids = MepData.Ids(args); if (ids.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "element_ids is required.");
        return new JObject { ["elements"] = new JArray(ids.Select(doc.GetElement).Where(x => x != null).Select(e => new JObject { ["element_id"] = e!.Id.Val(), ["connectors"] = new JArray(MepData.Connectors(e).Select(MepData.ConnectorDetail)) })) };
    }
}
internal sealed class MepFilterCommand : ReadCommand
{
    public MepFilterCommand() : base("mep_filter_elements", "Lọc MEP theo System Name, Category và Level (AND).") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var categories = new HashSet<string>((args["categories"] as JArray ?? new JArray()).Values<string>().Where(x => x != null)!, StringComparer.OrdinalIgnoreCase); var system = args.Value<string>("system_name") ?? string.Empty; var level = args.Value<string>("level_name") ?? string.Empty; var requested = args.Value<int?>("limit") ?? 200; var limit = Math.Min(1000, Math.Max(1, requested));
        var result = new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(MepData.IsMep).Where(e => categories.Count == 0 || (e.Category != null && categories.Contains(e.Category.Name))).Where(e => string.IsNullOrWhiteSpace(system) || MepData.SystemName(e).IndexOf(system, StringComparison.OrdinalIgnoreCase) >= 0).Where(e => string.IsNullOrWhiteSpace(level) || string.Equals(doc.GetElement(e.LevelId)?.Name, level, StringComparison.OrdinalIgnoreCase)).Take(limit).Select(MepData.ElementDetail);
        return new JObject { ["elements"] = new JArray(result), ["limit"] = limit };
    }
}
