using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Core;
using DSCons.RevitMcp.Contracts;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace DSCons.RevitMcp.Commands;

internal abstract class ReadCommand : IRevitCommand
{
    protected ReadCommand(string name, string description) { Capability = new ToolCapability { Name = name, Description = description }; }
    public ToolCapability Capability { get; }
    public abstract JObject Execute(UIApplication application, JObject args);
    protected static Document Document(UIApplication app) => app.ActiveUIDocument?.Document ?? throw new CommandResultException(ErrorCodes.NoDocument, "Open a Revit document first.");
}

internal sealed class SystemStatusCommand : ReadCommand
{
    public SystemStatusCommand() : base("system_status", "Trạng thái bridge, document và worksharing.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument?.Document;
        return new JObject { ["server"] = McpConstants.ServerName, ["port"] = McpConstants.DefaultPort, ["document_open"] = doc != null, ["document"] = doc?.Title, ["read_only"] = doc?.IsReadOnly ?? false, ["workshared"] = doc?.IsWorkshared ?? false, ["central_direct"] = doc != null && MepSafety.IsCentralDirect(doc), ["safety_mode"] = "preview-auto-apply" };
    }
}
internal sealed class DocumentInfoCommand : ReadCommand
{
    public DocumentInfoCommand() : base("document_info", "Thông tin document đang active.") { }
    public override JObject Execute(UIApplication app, JObject args) { var doc = Document(app); return new JObject { ["title"] = doc.Title, ["path"] = doc.PathName, ["read_only"] = doc.IsReadOnly, ["workshared"] = doc.IsWorkshared, ["central_direct"] = MepSafety.IsCentralDirect(doc), ["modified"] = doc.IsModified }; }
}
internal sealed class ActiveViewCommand : ReadCommand
{
    public ActiveViewCommand() : base("get_active_view", "Thông tin active view.") { }
    public override JObject Execute(UIApplication app, JObject args) { var view = Document(app).ActiveView; return new JObject { ["id"] = view.Id.Val(), ["name"] = view.Name, ["view_type"] = view.ViewType.ToString(), ["scale"] = view.Scale }; }
}
internal sealed class SelectionCommand : ReadCommand
{
    public SelectionCommand() : base("get_selection", "Element IDs đang được chọn.") { }
    public override JObject Execute(UIApplication app, JObject args) => new() { ["element_ids"] = new JArray(app.ActiveUIDocument?.Selection.GetElementIds().Select(x => x.Val()) ?? Enumerable.Empty<long>()) };
}
internal sealed class CapabilitiesCommand : ReadCommand
{
    public CapabilitiesCommand() : base("get_capabilities", "Danh sách tool đang có cùng read/write classification.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var serializer = JsonSerializer.Create(new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
        return new JObject { ["tools"] = new JArray(CommandRegistry.Capabilities.Select(capability => JObject.FromObject(capability, serializer))) };
    }
}
