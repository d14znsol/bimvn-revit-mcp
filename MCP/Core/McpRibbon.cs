using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Media;

namespace DSCons.RevitMcp.Core;

internal static class McpRibbon
{
    private static PushButton? _toggle;

    public static void Create(UIControlledApplication application)
    {
        var panel = application.CreateRibbonPanel("DSCons MCP");
        var assembly = typeof(McpRibbon).Assembly.Location;
        _toggle = panel.AddItem(new PushButtonData("DSConsMcpToggle", "Bật/Tắt\nMCP", assembly, typeof(McpToggleCommand).FullName!)) as PushButton;
        UpdateTogglePresentation(CoreRuntimeManager.Current?.IsRunning ?? false);
        var reload = panel.AddItem(new PushButtonData("DSConsMcpReload", "Cập nhật\nCode", assembly, typeof(McpReloadCommand).FullName!)) as PushButton;
        if (reload != null)
        {
            reload.ToolTip = "Cập nhật code MCP mới.";
            reload.LongDescription = "Nạp code MCP mới sau khi build.";
            reload.Image = McpRibbonImages.Reload();
            reload.LargeImage = McpRibbonImages.Reload();
        }
    }

    public static void UpdateTogglePresentation(bool running)
    {
        if (_toggle == null) return;
        _toggle.ItemText = running ? "Tắt\nMCP" : "Bật\nMCP";
        _toggle.ToolTip = running ? "Tắt cầu nối MCP trong Revit." : "Bật cầu nối MCP trong Revit.";
        _toggle.LongDescription = "Bật/tắt CoreRuntime và cầu nối loopback của Revit. Không khởi động, dừng hoặc kill tiến trình Node do AI client quản lý; không thay đổi model.";
        _toggle.Image = McpRibbonImages.Power(running);
        _toggle.LargeImage = McpRibbonImages.Power(running);
    }
}

[Transaction(TransactionMode.Manual)]
internal sealed class McpToggleCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var manager = CoreRuntimeManager.Current;
        if (manager == null)
        {
            TaskDialog.Show("DSCons Revit MCP", "MCP loader chưa sẵn sàng.");
            return Result.Failed;
        }
        try
        {
            var status = manager.ToggleOnUiThread(commandData.Application);
            McpRibbon.UpdateTogglePresentation(manager.IsRunning);
            TaskDialog.Show("DSCons Revit MCP", status);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            McpRibbon.UpdateTogglePresentation(false);
            message = ex.Message;
            TaskDialog.Show("DSCons Revit MCP", "Không thể đổi trạng thái MCP: " + ex.Message);
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
internal sealed class McpReloadCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var manager = CoreRuntimeManager.Current;
            if (manager == null) throw new InvalidOperationException("MCP loader chưa sẵn sàng.");
            if (!manager.ReloadOnUiThread(commandData.Application))
            {
                McpRibbon.UpdateTogglePresentation(false);
                TaskDialog.Show("DSCons Revit MCP", "MCP đang tắt. Hãy bấm Bật MCP trước khi cập nhật code.");
                return Result.Succeeded;
            }
            McpRibbon.UpdateTogglePresentation(true);
            TaskDialog.Show("DSCons Revit MCP", "Đã reload thành công code mới.");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("DSCons Revit MCP", "Reload CoreRuntime thất bại: " + ex.Message);
            return Result.Failed;
        }
    }
}
