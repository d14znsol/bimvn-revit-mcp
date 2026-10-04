using Autodesk.Revit.UI;
using DSCons.RevitMcp.Core;
#if REVIT2023 || REVIT2025
using DSCons.RevitMcp.Core.EmbeddedChat;
#endif

namespace DSCons.RevitMcp;

/// <summary>
/// Stable Revit loader. Revit loads this assembly once; the command runtime is
/// replaceable through CoreRuntimeManager and is never directly locked by Revit.
/// </summary>
public sealed class App : IExternalApplication
{
    private CoreRuntimeManager? _runtime;
#if REVIT2023 || REVIT2025
    private EmbeddedChatPaneHost? _chatPane;
#endif

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            _runtime = new CoreRuntimeManager();
            _runtime.Initialize(application);
#if REVIT2023 || REVIT2025
            _chatPane = EmbeddedChatPaneHost.Register(application);
#endif
            McpRibbon.Create(application);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("DSCons Revit MCP", "Không thể khởi động MCP: " + ex.Message);
            _runtime?.Dispose();
            _runtime = null;
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
#if REVIT2023 || REVIT2025
        _chatPane?.Dispose();
        _chatPane = null;
#endif
        _runtime?.Dispose();
        _runtime = null;
        return Result.Succeeded;
    }
}
