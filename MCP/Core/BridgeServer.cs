using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;
using Newtonsoft.Json.Serialization;
using Autodesk.Revit.DB;
using DSCons.RevitMcp.Commands;
using DSCons.RevitMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Core;

public sealed class BridgeServer : IDisposable
{
    private static readonly JsonSerializerSettings WireJson = new() { ContractResolver = new CamelCasePropertyNamesContractResolver() };
    private readonly string _secret = McpConstants.CreateSessionSecret();
    private TcpListener? _listener;
    private int _port;
    private readonly ExternalEventBridge _bridge = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly string _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSCons", "RevitMcp");
    private readonly Dictionary<string, PreviewToken> _previews = new();
    private readonly object _previewGate = new();
    private readonly string _sessionPath;
    private bool _started;

    public BridgeServer()
    {
        Current = this;
        _sessionPath = Path.Combine(_dataDir, "session.json");
    }

    public void Start()
    {
        if (_started) return;
        Directory.CreateDirectory(_dataDir);
        // Prefer the documented port, but never leave MCP unavailable merely
        // because another local Revit/add-in session already owns it. The
        // selected loopback port is persisted in session.json and is consumed
        // by the Node client for this Revit session.
        _listener = new TcpListener(IPAddress.Loopback, McpConstants.DefaultPort);
        try
        {
            _listener.Start();
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
        }

        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        try
        {
            var session = new SessionInfo { Port = _port, Secret = _secret, ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id, StartedAtUtc = DateTimeOffset.UtcNow };
            File.WriteAllText(_sessionPath, JsonConvert.SerializeObject(session, WireJson));
            _started = true;
            _ = Task.Run(AcceptLoopAsync);
        }
        catch
        {
            _listener.Stop();
            _listener = null;
            throw;
        }
    }
    private async Task AcceptLoopAsync()
    {
        while (!_cancel.IsCancellationRequested)
        {
            try
            {
                var listener = _listener;
                if (listener == null) return;
                var client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleAsync(client));
            }
            catch (ObjectDisposedException) { return; } catch (SocketException) { if (_cancel.IsCancellationRequested) return; }
        }
    }
    private async Task HandleAsync(TcpClient client)
    {
        using (client) using (var stream = client.GetStream()) using (var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true)) using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
        {
            var input = await reader.ReadLineAsync(); BridgeRequest? request;
            try { request = JsonConvert.DeserializeObject<BridgeRequest>(input ?? string.Empty); } catch { await writer.WriteLineAsync(JsonConvert.SerializeObject(Failure(string.Empty, ErrorCodes.InvalidParam, "Bridge JSON is invalid."), WireJson)); return; }
            if (request == null || request.Secret != _secret) { await writer.WriteLineAsync(JsonConvert.SerializeObject(Failure(request?.Id ?? string.Empty, ErrorCodes.Unauthorized, "Invalid MCP bridge session secret."), WireJson)); return; }
            var response = await _bridge.InvokeAsync(request);
            await writer.WriteLineAsync(JsonConvert.SerializeObject(response, WireJson));
        }
    }
    public void Store(PreviewToken preview) { lock (_previewGate) _previews[preview.PreviewId] = preview; }
    public bool TryTake(string id, out PreviewToken preview) { lock (_previewGate) { if (!_previews.TryGetValue(id, out preview!)) return false; _previews.Remove(id); return true; } }
    public void Audit(BridgeRequest request, Document? doc, bool success, string code, IEnumerable<long>? elementIds = null, string? previewId = null)
    {
        try
        {
            var audit = new JObject
            {
                ["timestampUtc"] = DateTimeOffset.UtcNow,
                ["client"] = request.ClientName,
                ["user"] = Environment.UserName,
                ["tool"] = request.Method,
                ["success"] = success,
                ["errorCode"] = code,
                ["previewId"] = previewId,
                ["document"] = doc?.Title,
                ["documentPath"] = doc?.PathName,
                ["readOnly"] = doc?.IsReadOnly ?? false,
                ["workshared"] = doc?.IsWorkshared ?? false,
                ["centralDirect"] = doc != null && MepSafety.IsCentralDirect(doc),
                ["rolledBack"] = !success && code == ErrorCodes.TransactionFailed,
                ["elements"] = new JArray((elementIds ?? Enumerable.Empty<long>()).Distinct())
            };
            File.AppendAllText(Path.Combine(_dataDir, "audit.jsonl"), audit.ToString(Formatting.None) + Environment.NewLine);
        }
        catch { }
    }
    public static BridgeResponse Success(string id, JObject result) => new() { Id = id, Success = true, ResultJson = result.ToString(Formatting.None) };
    public static BridgeResponse Failure(string id, string code, string message) => new() { Id = id, Success = false, ErrorCode = code, ErrorMessage = message };
    public void Dispose()
    {
        _cancel.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _bridge.Dispose();
        _started = false;
        try
        {
            if (File.Exists(_sessionPath))
            {
                var current = JsonConvert.DeserializeObject<SessionInfo>(File.ReadAllText(_sessionPath));
                if (current?.ProcessId == System.Diagnostics.Process.GetCurrentProcess().Id && current.Secret == _secret)
                    File.Delete(_sessionPath);
            }
        }
        catch { }
        if (ReferenceEquals(Current, this)) Current = null;
    }
    internal static BridgeServer? Current { get; private set; }
}
