using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Events;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using DSCons.RevitMcp.Commands;
using System.Net.Sockets;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace DSCons.RevitMcp;

/// <summary>
/// Entry point loaded by the stable Revit loader. It is intentionally tiny so
/// command changes can be built and reloaded without replacing the add-in DLL.
/// </summary>
public sealed class RevitMcpCoreRuntime : IRevitMcpRuntime
{
    private BridgeServer? _server;
    private Action? _reloadCallback;
    private bool _initialized;
    private string _status = "CoreRuntime chưa khởi động cầu nối MCP.";
    private EventHandler<DocumentChangedEventArgs>? _documentChangedHandler;
    private Action? _unsubscribeDocumentChanged;
    private FileSystemWatcher? _runtimeWatcher;
    private Timer? _runtimeWatchDebounce;
    private readonly object _runtimeWatchGate = new();
    private string? _runtimeArtifactPath;
    private string? _runtimeArtifactHash;
    private int _autoReloadRequested;

    public string RuntimeVersion => typeof(RevitMcpCoreRuntime).Assembly.GetName().Version?.ToString() ?? "dev";
    public string StatusText => _server == null
        ? _status
        : "Đã kết nối MCP thành công.";
    public bool IsRunning => _server != null;

    public void Initialize(object uiApplication)
    {
        // BridgeServer creates ExternalEvent while the loader is executing in
        // Revit's startup/UI context. The UIApplication is supplied later to
        // the handler when Revit raises the event.
        _server = new BridgeServer();
        _documentChangedHandler = (_, args) =>
        {
            try { DocumentRevisionTracker.Bump(args.GetDocument()); }
            catch { /* A closing document must not destabilize the bridge. */ }
        };
        if (uiApplication is UIControlledApplication controlledApplication)
        {
            controlledApplication.ControlledApplication.DocumentChanged += _documentChangedHandler;
            _unsubscribeDocumentChanged = () => controlledApplication.ControlledApplication.DocumentChanged -= _documentChangedHandler;
        }
        else if (uiApplication is UIApplication application)
        {
            application.Application.DocumentChanged += _documentChangedHandler;
            _unsubscribeDocumentChanged = () => application.Application.DocumentChanged -= _documentChangedHandler;
        }
        _initialized = true;
    }

    public void SetReloadCallback(Action callback) => _reloadCallback = callback;

    public void Start()
    {
        if (!_initialized || _server == null)
            throw new InvalidOperationException("Runtime must be initialized before Start.");
        try
        {
            _server.Start();
            StartRuntimeWatch();
            _status = "Cầu nối MCP đang hoạt động.";
        }
        catch (SocketException ex)
        {
            _server.Dispose();
            _server = null;
            _status = $"Cầu nối MCP không thể khởi động vì cổng nội bộ {McpConstants.DefaultPort} đang được chương trình khác sử dụng.\nChi tiết: {ex.Message}";
        }
    }

    public void Stop()
    {
        StopRuntimeWatch();
        try { _unsubscribeDocumentChanged?.Invoke(); } catch { }
        _unsubscribeDocumentChanged = null;
        _documentChangedHandler = null;
        DocumentRevisionTracker.Reset();
        _server?.Dispose();
        _server = null;
        _initialized = false;
        _status = "CoreRuntime đã dừng.";
    }

    public void RequestReload() => _reloadCallback?.Invoke();

    // The stable loader stays in place while Revit is running. This watcher
    // requests its existing ExternalEvent reload only after a complete,
    // content-changing CoreRuntime DLL has been published. It never opens a
    // Revit transaction or changes a document.
    private void StartRuntimeWatch()
    {
        var contractsDirectory = Path.GetDirectoryName(typeof(IRevitMcpRuntime).Assembly.Location);
        if (string.IsNullOrWhiteSpace(contractsDirectory)) return;
        var runtimePath = Path.Combine(contractsDirectory, "runtime", "DSCons.RevitMcp.CoreRuntime.dll");
        if (!File.Exists(runtimePath)) return;
        try
        {
            lock (_runtimeWatchGate)
            {
                _runtimeArtifactPath = runtimePath;
                _runtimeArtifactHash = HashFile(runtimePath);
                _runtimeWatchDebounce = new Timer(_ => CheckForPublishedRuntime(), null, Timeout.Infinite, Timeout.Infinite);
                _runtimeWatcher = new FileSystemWatcher(Path.GetDirectoryName(runtimePath)!, Path.GetFileName(runtimePath))
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                _runtimeWatcher.Changed += RuntimeArtifactChanged;
                _runtimeWatcher.Created += RuntimeArtifactChanged;
                _runtimeWatcher.Renamed += RuntimeArtifactRenamed;
            }
        }
        catch
        {
            // Convenience only: manual Cập nhật Code remains the safe fallback.
            StopRuntimeWatch();
        }
    }

    private void RuntimeArtifactChanged(object? sender, FileSystemEventArgs args) => ScheduleRuntimeCheck();
    private void RuntimeArtifactRenamed(object? sender, RenamedEventArgs args) => ScheduleRuntimeCheck();

    private void ScheduleRuntimeCheck()
    {
        lock (_runtimeWatchGate)
        {
            try { _runtimeWatchDebounce?.Change(750, Timeout.Infinite); }
            catch { }
        }
    }

    private void CheckForPublishedRuntime()
    {
        string? runtimePath;
        string? previousHash;
        lock (_runtimeWatchGate) { runtimePath = _runtimeArtifactPath; previousHash = _runtimeArtifactHash; }
        if (string.IsNullOrWhiteSpace(runtimePath) || !File.Exists(runtimePath)) return;
        string currentHash;
        try { currentHash = HashFile(runtimePath!); }
        catch { ScheduleRuntimeCheck(); return; }
        if (string.Equals(previousHash, currentHash, StringComparison.Ordinal)) return;
        lock (_runtimeWatchGate) _runtimeArtifactHash = currentHash;
        if (Interlocked.Exchange(ref _autoReloadRequested, 1) == 0) RequestReload();
    }

    private void StopRuntimeWatch()
    {
        lock (_runtimeWatchGate)
        {
            if (_runtimeWatcher != null)
            {
                _runtimeWatcher.EnableRaisingEvents = false;
                _runtimeWatcher.Changed -= RuntimeArtifactChanged;
                _runtimeWatcher.Created -= RuntimeArtifactChanged;
                _runtimeWatcher.Renamed -= RuntimeArtifactRenamed;
                _runtimeWatcher.Dispose();
                _runtimeWatcher = null;
            }
            _runtimeWatchDebounce?.Dispose();
            _runtimeWatchDebounce = null;
            _runtimeArtifactPath = null;
            _runtimeArtifactHash = null;
            Interlocked.Exchange(ref _autoReloadRequested, 0);
        }
    }

    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToBase64String(sha.ComputeHash(file));
    }

    public void Dispose() => Stop();
}
