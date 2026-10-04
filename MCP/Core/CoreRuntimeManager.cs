using System.Reflection;
using System.IO;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;

#if REVIT2025 || REVIT2026 || REVIT2027
using System.Runtime.Loader;
#endif

namespace DSCons.RevitMcp.Core;

/// <summary>Loads the replaceable CoreRuntime from a copy outside Revit's loader path.</summary>
internal sealed class CoreRuntimeManager : IDisposable
{
    private readonly object _gate = new();
    private IRevitMcpRuntime? _runtime;
    private object? _applicationContext;
    private ExternalEvent? _reloadEvent;
    private ReloadHandler? _reloadHandler;
    private string? _runtimeDirectory;
    private bool _enabled = true;
#if REVIT2025 || REVIT2026 || REVIT2027
    private CollectibleRuntimeLoadContext? _loadContext;
#endif
#if !REVIT2025 && !REVIT2026 && !REVIT2027
    private ResolveEventHandler? _resolveHandler;
#endif

    public static CoreRuntimeManager? Current { get; private set; }

    public bool IsRunning
    {
        get { lock (_gate) return _enabled && (_runtime?.IsRunning ?? false); }
    }

    public void Initialize(UIControlledApplication application)
    {
        Current = this;
        _enabled = true;
        _reloadHandler = new ReloadHandler(this);
        _reloadEvent = ExternalEvent.Create(_reloadHandler);
        LoadRuntime(application);
    }

    public void EnsureLoaded(UIApplication application)
    {
        lock (_gate)
        {
            _applicationContext = application;
            if (!_enabled) return;
            if (_runtime != null) return;
            LoadRuntime(application);
        }
    }

    private void LoadRuntime(object applicationContext)
    {
        var loaderDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        _runtimeDirectory = Path.Combine(loaderDirectory, "runtime");
        var path = Path.Combine(_runtimeDirectory, "DSCons.RevitMcp.CoreRuntime.dll");
        if (!File.Exists(path)) throw new FileNotFoundException("CoreRuntime artifact is missing.", path);

#if REVIT2025 || REVIT2026 || REVIT2027
        _loadContext = new CollectibleRuntimeLoadContext(_runtimeDirectory);
        Assembly assembly = _loadContext.LoadFromAssemblyPath(path);
#else
        _resolveHandler = ResolveRuntimeAssembly;
        AppDomain.CurrentDomain.AssemblyResolve += _resolveHandler;
        Assembly assembly = Assembly.Load(File.ReadAllBytes(path));
#endif
        var type = assembly.GetType("DSCons.RevitMcp.RevitMcpCoreRuntime", throwOnError: true)!;
        _runtime = (IRevitMcpRuntime)Activator.CreateInstance(type)!;
        _runtime.Initialize(applicationContext);
        _runtime.SetReloadCallback(RequestReload);
        _runtime.Start();
    }

#if !REVIT2025 && !REVIT2026 && !REVIT2027
    private Assembly? ResolveRuntimeAssembly(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name);
        if (string.Equals(name.Name, typeof(IRevitMcpRuntime).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
            return typeof(IRevitMcpRuntime).Assembly;
        if (string.IsNullOrWhiteSpace(_runtimeDirectory)) return null;
        var path = Path.Combine(_runtimeDirectory, name.Name + ".dll");
        return File.Exists(path) ? Assembly.Load(File.ReadAllBytes(path)) : null;
    }
#endif

    public void RequestReload()
    {
        if (!_enabled) return;
        try { _reloadEvent?.Raise(); }
        catch { /* Revit is shutting down or a modal state is active. */ }
    }

    public bool ReloadOnUiThread(UIApplication application)
    {
        lock (_gate)
        {
            _applicationContext = application;
            if (!_enabled) return false;
            StopRuntime();
            LoadRuntime(application);
            return _runtime?.IsRunning ?? false;
        }
    }

    public string ToggleOnUiThread(UIApplication application)
    {
        lock (_gate)
        {
            _applicationContext = application;
            if (_enabled)
            {
                _enabled = false;
                StopRuntime();
                return "Đã tắt MCP.\n\nMuốn dùng lại, hãy bấm Bật MCP.";
            }

            _enabled = true;
            try
            {
                LoadRuntime(application);
                if (_runtime?.IsRunning == true)
                    return "MCP đang BẬT. Cầu nối Revit đã sẵn sàng nhận kết nối từ AI client.";
                var detail = _runtime?.StatusText ?? "CoreRuntime không khởi động được.";
                _enabled = false;
                StopRuntime();
                return "Không thể BẬT cầu nối MCP.\n\n" + detail;
            }
            catch
            {
                _enabled = false;
                StopRuntime();
                throw;
            }
        }
    }

    private void StopRuntime()
    {
        try { _runtime?.Stop(); } finally { _runtime = null; }
#if REVIT2025 || REVIT2026 || REVIT2027
        _loadContext?.Unload();
        _loadContext = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
#else
        if (_resolveHandler != null) AppDomain.CurrentDomain.AssemblyResolve -= _resolveHandler;
        _resolveHandler = null;
#endif
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopRuntime();
            _reloadEvent?.Dispose();
            _reloadEvent = null;
            _reloadHandler = null;
            _applicationContext = null;
            _enabled = false;
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }

    private sealed class ReloadHandler : IExternalEventHandler
    {
        private readonly CoreRuntimeManager _owner;
        public ReloadHandler(CoreRuntimeManager owner) => _owner = owner;
        public string GetName() => "DSCons Revit MCP Core Reload";
        public void Execute(UIApplication app) => _owner.ReloadOnUiThread(app);
    }

#if REVIT2025 || REVIT2026 || REVIT2027
    private sealed class CollectibleRuntimeLoadContext : AssemblyLoadContext
    {
        private readonly string _directory;
        public CollectibleRuntimeLoadContext(string directory) : base("DSCons.RevitMcp.CoreRuntime", isCollectible: true) => _directory = directory;
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, typeof(IRevitMcpRuntime).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
                return typeof(IRevitMcpRuntime).Assembly;
            // Revit owns these assemblies in the default load context. The
            // collectible runtime must reuse those exact instances instead of
            // probing/copying another Revit API DLL.
            if (string.Equals(assemblyName.Name, "RevitAPI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assemblyName.Name, "RevitAPIUI", StringComparison.OrdinalIgnoreCase))
                return Assembly.Load(assemblyName);
            var path = Path.Combine(_directory, assemblyName.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
#endif
}
