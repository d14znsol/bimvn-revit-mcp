using Autodesk.Revit.UI;
using DSCons.RevitMcp.Core;

namespace DSCons.RevitMcp;

/// <summary>
/// Stable Revit loader. Revit loads this assembly once; the command runtime is
/// replaceable through CoreRuntimeManager and is never directly locked by Revit.
/// </summary>
public sealed class App : IExternalApplication
{
    private CoreRuntimeManager? _runtime;

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            _runtime = new CoreRuntimeManager();
            _runtime.Initialize(application);
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
        _runtime?.Dispose();
        _runtime = null;
        return Result.Succeeded;
    }
}
