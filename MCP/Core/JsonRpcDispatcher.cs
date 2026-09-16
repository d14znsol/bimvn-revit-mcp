using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Core;

internal static class JsonRpcDispatcher
{
    public static BridgeResponse Dispatch(UIApplication application, BridgeRequest request)
    {
        var command = CommandRegistry.Get(request.Method);
        if (command == null) return BridgeServer.Failure(request.Id, ErrorCodes.Unsupported, "Unknown DSCons MCP method: " + request.Method);
        JObject args;
        try { args = JObject.Parse(string.IsNullOrWhiteSpace(request.ParamsJson) ? "{}" : request.ParamsJson); }
        catch { return BridgeServer.Failure(request.Id, ErrorCodes.InvalidParam, "Tool arguments must be a JSON object."); }
        try
        {
            var data = command.Execute(application, args);
            BridgeServer.Current?.Audit(request, application.ActiveUIDocument?.Document, true, string.Empty, AuditElementIds(args, data), AuditPreviewId(args, data));
            return BridgeServer.Success(request.Id, data);
        }
        catch (CommandResultException ex)
        {
            BridgeServer.Current?.Audit(request, application.ActiveUIDocument?.Document, false, ex.Code, AuditElementIds(args, null), AuditPreviewId(args, null));
            return BridgeServer.Failure(request.Id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            BridgeServer.Current?.Audit(request, application.ActiveUIDocument?.Document, false, ErrorCodes.TransactionFailed, AuditElementIds(args, null), AuditPreviewId(args, null));
            return BridgeServer.Failure(request.Id, ErrorCodes.TransactionFailed, ex.Message);
        }
    }

    private static IEnumerable<long> AuditElementIds(JObject args, JObject? result)
    {
        var ids = new List<long>();
        foreach (var key in new[] { "created_element_ids", "moved_element_ids", "changed_element_ids", "requested_element_ids", "element_ids" })
            if (result?[key] is JArray resultIds) ids.AddRange(resultIds.Values<long>());
            else if (args[key] is JArray argumentIds) ids.AddRange(argumentIds.Values<long>());
        foreach (var key in new[] { "first_element_id", "second_element_id" })
        {
            if (result?.Value<long?>(key) is long resultId) ids.Add(resultId);
            else if (args.Value<long?>(key) is long argumentId) ids.Add(argumentId);
        }
        return ids;
    }

    private static string? AuditPreviewId(JObject args, JObject? result) => result?.Value<string>("preview_id") ?? args.Value<string>("preview_id");
}
