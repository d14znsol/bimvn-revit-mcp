using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

internal static class MepData
{
    public static List<ElementId> Ids(JObject args) => (args["element_ids"] as JArray ?? new JArray()).Values<long>().Select(RevitIdCompatibility.Eid).ToList();
    public static bool IsMep(Element element) => element is MEPCurve || element is FamilyInstance instance && instance.MEPModel != null;
    public static string SystemName(Element e) { var p = e.LookupParameter("System Name") ?? e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM); return p?.AsString() ?? p?.AsValueString() ?? string.Empty; }
    public static IEnumerable<Connector> Connectors(Element? e)
    {
        if (e is MEPCurve curve) return curve.ConnectorManager.Connectors.Cast<Connector>();
        if (e is FamilyInstance instance && instance.MEPModel?.ConnectorManager != null) return instance.MEPModel.ConnectorManager.Connectors.Cast<Connector>();
        return Enumerable.Empty<Connector>();
    }
    public static IEnumerable<Element> ConnectedRoutingFittings(Element element)
    {
        return Connectors(element)
            .SelectMany(connector => connector.AllRefs.Cast<Connector>())
            .Select(connector => connector.Owner)
            .Where(owner => owner != null && owner.Id != element.Id && IsRoutingFitting(owner))
            .GroupBy(owner => owner!.Id.Val())
            .Select(group => group.First()!);
    }
    private static bool IsRoutingFitting(Element element)
    {
        var categoryId = element.Category?.Id.IntVal();
        return categoryId == (int)BuiltInCategory.OST_PipeFitting ||
               categoryId == (int)BuiltInCategory.OST_DuctFitting ||
               categoryId == (int)BuiltInCategory.OST_ConduitFitting ||
               categoryId == (int)BuiltInCategory.OST_CableTrayFitting;
    }
    public static JObject ElementDetail(Element element)
    {
        var doc = element.Document; var type = doc.GetElement(element.GetTypeId()); var box = element.get_BoundingBox(null); var parameters = new JObject(); var systemTypeId = SystemTypeId(element);
        foreach (Parameter parameter in element.Parameters) if (parameter.Definition != null && parameter.HasValue) parameters[parameter.Definition.Name] = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        var detail = new JObject { ["id"] = element.Id.Val(), ["category"] = element.Category?.Name, ["class"] = element.GetType().Name, ["name"] = element.Name, ["family_or_type"] = type?.Name, ["type_id"] = element.GetTypeId() == ElementId.InvalidElementId ? null : element.GetTypeId().Val(), ["system_name"] = SystemName(element), ["system_type_id"] = systemTypeId == ElementId.InvalidElementId ? null : systemTypeId.Val(), ["level_id"] = element.LevelId.Val(), ["pinned"] = element.Pinned, ["bounding_box"] = box == null ? null : new JObject { ["min"] = Point(box.Min), ["max"] = Point(box.Max) }, ["parameters"] = parameters };
        if (element is ViewSheet sheet)
        {
            detail["sheet_number"] = sheet.SheetNumber;
            detail["sheet_name"] = sheet.Name;
        }
        if (element is View view)
        {
            detail["view_name"] = view.Name;
            detail["view_type"] = view.ViewType.ToString();
            detail["view_scale"] = view.Scale;
            detail["view_template_id"] = view.ViewTemplateId == ElementId.InvalidElementId ? null : view.ViewTemplateId.Val();
        }
        if (element is ViewSchedule schedule)
            detail["schedule"] = DocumentationScheduleSupport.ReadBack(schedule);
        if (element is Viewport viewport)
        {
            detail["sheet_id"] = viewport.SheetId.Val();
            detail["view_id"] = viewport.ViewId.Val();
            detail["viewport_center"] = Point(viewport.GetBoxCenter());
        }
        return detail;
    }
    private static ElementId SystemTypeId(Element element)
    {
        if (element is MEPCurve curve && curve.MEPSystem != null) return curve.MEPSystem.GetTypeId();
        var parameterId = element.Category?.Id.IntVal() switch
        {
            (int)BuiltInCategory.OST_PipeCurves => BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM,
            (int)BuiltInCategory.OST_DuctCurves => BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM,
            _ => BuiltInParameter.INVALID
        };
        return parameterId == BuiltInParameter.INVALID ? ElementId.InvalidElementId : element.get_Parameter(parameterId)?.AsElementId() ?? ElementId.InvalidElementId;
    }
    public static JObject ConnectorDetail(Connector c)
    {
        var detail = new JObject
        {
            ["domain"] = c.Domain.ToString(),
            ["profile"] = c.Shape.ToString(),
            ["connector_type"] = c.ConnectorType.ToString(),
            ["origin"] = null,
            ["direction"] = null,
            ["radius_mm"] = null,
            ["width_mm"] = null,
            ["height_mm"] = null,
            // Logical connectors have topology evidence but Revit does not
            // expose physical connection status or references for all of them.
            // Leave unavailable values null rather than failing the entire
            // network query for an otherwise valid MEP element.
            ["is_connected"] = null,
            ["connected_element_ids"] = new JArray()
        };

        // Revit exposes shape-specific dimensions. Reading Radius on a
        // rectangular/oval connector (or Width/Height on a round connector)
        // throws and used to make the entire connector query fail.
        // Logical connectors can report a profile/domain but do not expose a
        // physical origin, coordinate system or dimensions. They are still
        // useful topology evidence, so retain them with null geometry.
        try { detail["origin"] = Point(c.Origin); } catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
        try { detail["direction"] = Point(c.CoordinateSystem.BasisZ); } catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
        try { detail["is_connected"] = c.IsConnected; } catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
        try
        {
            detail["connected_element_ids"] = new JArray(c.AllRefs.Cast<Connector>()
                .Where(x => x.Owner != null && x.Owner.Id != c.Owner.Id)
                .Select(x => x.Owner.Id.Val())
                .Distinct());
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
        try
        {
            if (c.Shape == ConnectorProfileType.Round)
                detail["radius_mm"] = Math.Round(c.Radius * 304.8, 2);
            else if (c.Shape == ConnectorProfileType.Rectangular || c.Shape == ConnectorProfileType.Oval)
            {
                detail["width_mm"] = Math.Round(c.Width * 304.8, 2);
                detail["height_mm"] = Math.Round(c.Height * 304.8, 2);
            }
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }

        return detail;
    }
    public static JObject Point(XYZ point) => new() { ["x_mm"] = Math.Round(point.X * 304.8, 2), ["y_mm"] = Math.Round(point.Y * 304.8, 2), ["z_mm"] = Math.Round(point.Z * 304.8, 2) };
    public static XYZ PointMm(JObject point) => new(point.Value<double>("x_mm") / 304.8, point.Value<double>("y_mm") / 304.8, point.Value<double>("z_mm") / 304.8);
}
