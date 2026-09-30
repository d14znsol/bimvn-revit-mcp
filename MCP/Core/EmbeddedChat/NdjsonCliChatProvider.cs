#if REVIT2023 || REVIT2025
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DSCons.RevitMcp.Core.EmbeddedChat;

/// <summary>
/// Common, deliberately narrow NDJSON process adapter. It has no API-key
/// surface: each provider is expected to use the user's own CLI login.
/// Provider preflight must pass before this class writes any user prompt.
/// </summary>
internal abstract class NdjsonCliChatProvider : IEmbeddedChatProvider
{
    private readonly EmbeddedChatConfig _config;
    private readonly EmbeddedApprovalServer _approval;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Process? _process;
    private StreamWriter? _stdin;
    private ChatProjectContext? _projectContext;
    private ChatConversation? _conversation;
    private string? _turnId;
    private DateTime _turnStartedUtc;
    private DateTime _lastActivityUtc;
    private string _stage = string.Empty;
    private bool _connected;
    private bool _disposed;
    private UsageSnapshot _currentUsage;

    protected NdjsonCliChatProvider(EmbeddedChatConfig config, EmbeddedApprovalServer approval)
    {
        _config = config;
        _approval = approval;
        _currentUsage = UsageSnapshot.Unavailable(ProviderId, "Provider chưa được kiểm tra hạn mức.");
    }

    protected EmbeddedChatConfig Config => _config;
    protected EmbeddedApprovalServer Approval => _approval;
    protected string? IsolatedWorkspace { get; private set; }
    protected string? McpConfigPath { get; private set; }
    public abstract string ProviderId { get; }
    public abstract string DisplayName { get; }
    public abstract ProviderCapabilities Capabilities { get; }
    public bool IsTurnRunning { get { lock (_stateGate) return !string.IsNullOrWhiteSpace(_turnId); } }
    public ChatConversation? CurrentConversation => _conversation;
    public UsageSnapshot CurrentUsage => _currentUsage;
    public event Action<string>? AgentDelta;
    public event Action<string>? Progress;
    public event Action<bool, string>? TurnFinished;
    public event Action<ChatProviderStatus>? StatusChanged;
    public event Action<string>? LoginUrlAvailable;
    public event Action<IReadOnlyList<ChatModelOption>, string?>? ModelsChanged;
    public event Action<ChatToolActivity>? ToolActivity;
    public event Action<ChatConversation>? ConversationChanged;
    public event Action<UsageSnapshot>? UsageChanged;
    public event Action<string>? Error;

    public void SetProjectContext(ChatProjectContext context)
    {
        if (_projectContext != null && string.Equals(_projectContext.Key, context.Key, StringComparison.Ordinal)) return;
        _projectContext = context; _conversation = null;
    }

    public IReadOnlyList<ChatConversation> ListConversations() => _projectContext == null ? Array.Empty<ChatConversation>() : ChatPersistence.Instance.List(_projectContext, ProviderId);
    public IReadOnlyList<ChatMemoryEntry> GetMemories() => _projectContext == null ? Array.Empty<ChatMemoryEntry>() : ChatPersistence.Instance.GetMemories(_projectContext);
    public bool Remember(string text, bool global) => _projectContext != null && ChatPersistence.Instance.Remember(_projectContext, text, global);
    public void Forget(string memoryId) => ChatPersistence.Instance.Forget(memoryId);
    public void RecordMessage(string role, string text)
    {
        if (_projectContext != null && _conversation != null) ChatPersistence.Instance.AddMessage(_projectContext, ProviderId, _conversation.Id, role, text);
    }

    public virtual async Task ConnectAsync()
    {
        ThrowIfDisposed();
        if (_connected && _process != null && !_process.HasExited) return;
        await PreflightAsync().ConfigureAwait(false); // must validate isolation before a prompt
        var context = _projectContext ?? ChatProjectContext.Create(null, "Project chưa mở");
        _conversation ??= ChatPersistence.Instance.GetOrCreateActive(context, ProviderId);
        // Some CLIs emit their init/conversation event immediately at process
        // start. Create the provider-scoped conversation first so that event
        // can be persisted instead of racing a null conversation.
        await StartProcessAsync().ConfigureAwait(false);
        var model = DefaultModels().FirstOrDefault(model => model.IsDefault)?.Model ?? DefaultModels().FirstOrDefault()?.Model;
        ModelsChanged?.Invoke(DefaultModels(), _conversation.Model ?? model);
        _conversation.Model ??= model;
        _connected = true;
        StatusChanged?.Invoke(new ChatProviderStatus { Account = "CLI đã đăng nhập", Provider = "Đã kết nối", Mcp = "Đang xác minh DSCons", Availability = "Sẵn sàng" });
        ConversationChanged?.Invoke(_conversation);
        await RefreshUsageAsync(false, CancellationToken.None).ConfigureAwait(false);
    }

    public virtual Task StartLoginAsync()
    {
        Progress?.Invoke($"{DisplayName} dùng tài khoản CLI đã đăng nhập. Hãy chạy lệnh đăng nhập chính thức của {DisplayName} ngoài Revit rồi thử lại; panel không nhận hoặc lưu API key.");
        StatusChanged?.Invoke(new ChatProviderStatus { Account = "Cần đăng nhập CLI", Provider = "Chưa kết nối", Mcp = "Đã khóa", Availability = "Cần thao tác ngoài Revit" });
        return Task.CompletedTask;
    }

    public async Task StartTurnAsync(string text, string? model)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await ConnectAsync().ConfigureAwait(false);
        lock (_stateGate)
        {
            if (!string.IsNullOrWhiteSpace(_turnId)) throw new InvalidOperationException("Hãy chờ lượt hiện tại hoàn tất hoặc bấm Dừng.");
            _turnId = Guid.NewGuid().ToString("N"); _turnStartedUtc = DateTime.UtcNow; _lastActivityUtc = _turnStartedUtc; _stage = "Đang gửi yêu cầu";
        }
        if (_conversation != null) _conversation.Model = model ?? _conversation.Model;
        await WriteJsonAsync(BuildUserTurn(text, model, _conversation?.ProviderConversationId)).ConfigureAwait(false);
    }

    public virtual async Task InterruptAsync()
    {
        if (!IsTurnRunning) return;
        await WriteJsonAsync(new JObject { ["type"] = "interrupt" }).ConfigureAwait(false);
        Progress?.Invoke("Đã yêu cầu dừng lượt chat.");
    }

    public async Task NewConversationAsync()
    {
        if (IsTurnRunning) throw new InvalidOperationException("Không thể đổi cuộc trò chuyện khi lượt chat đang chạy.");
        if (_projectContext != null) _conversation = ChatPersistence.Instance.CreateNew(_projectContext, ProviderId);
        ConversationChanged?.Invoke(_conversation!);
        await ConnectAsync().ConfigureAwait(false);
    }

    public async Task SelectConversationAsync(string conversationId)
    {
        if (IsTurnRunning) throw new InvalidOperationException("Không thể đổi cuộc trò chuyện khi lượt chat đang chạy.");
        if (_projectContext == null) return;
        var selected = ChatPersistence.Instance.Find(_projectContext, ProviderId, conversationId) ?? throw new InvalidOperationException("Conversation không còn khả dụng.");
        ChatPersistence.Instance.Activate(_projectContext, ProviderId, selected.Id); _conversation = selected;
        ConversationChanged?.Invoke(selected);
        await ConnectAsync().ConfigureAwait(false);
    }

    public Task DeleteConversationAsync(string conversationId)
    {
        if (IsTurnRunning || _projectContext == null) return Task.CompletedTask;
        ChatPersistence.Instance.Delete(_projectContext, ProviderId, conversationId);
        if (_conversation?.Id == conversationId) _conversation = null;
        return Task.CompletedTask;
    }

    public virtual Task RefreshUsageAsync(bool force, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PublishUsage(UsageSnapshot.Unavailable(ProviderId, UsageUnavailableMessage, UsageUnavailableHelp));
        return Task.CompletedTask;
    }

    public TurnActivitySnapshot GetTurnActivity()
    {
        lock (_stateGate) return new TurnActivitySnapshot { IsRunning = !string.IsNullOrWhiteSpace(_turnId), StartedUtc = _turnStartedUtc, LastActivityUtc = _lastActivityUtc, Stage = _stage };
    }

    protected abstract Task PreflightAsync();
    protected abstract ProcessStartInfo CreateProviderStartInfo();
    protected virtual string UsageUnavailableMessage => DisplayName + " CLI chưa cung cấp giao thức quota machine-readable đã được kiểm chứng.";
    protected virtual string? UsageUnavailableHelp => null;
    protected virtual IReadOnlyList<ChatModelOption> DefaultModels() => new[] { new ChatModelOption { Model = "default", DisplayName = "Mặc định", IsDefault = true } };
    protected virtual JObject BuildUserTurn(string text, string? model, string? providerConversationId) => new()
    {
        ["type"] = "user", ["message"] = text, ["model"] = model, ["conversation_id"] = providerConversationId
    };
    protected virtual void HandleProviderEvent(JObject message)
    {
        var type = message.Value<string>("type") ?? message.Value<string>("event") ?? string.Empty;
        var payload = string.Equals(type, "step_update", StringComparison.OrdinalIgnoreCase) ? message["step_update"] as JObject ?? message
            : string.Equals(type, "result", StringComparison.OrdinalIgnoreCase) ? message["result"] as JObject ?? message
            : message;
        var loginUrl = payload.Value<string>("login_url") ?? payload.Value<string>("auth_url") ?? message.Value<string>("login_url") ?? message.Value<string>("auth_url");
        if (!string.IsNullOrWhiteSpace(loginUrl)) LoginUrlAvailable?.Invoke(loginUrl!);
        var delta = payload.Value<string>("delta") ?? payload.Value<string>("text_delta") ?? payload.SelectToken("message.text")?.Value<string>() ?? payload.SelectToken("text")?.Value<string>();
        if (!string.IsNullOrWhiteSpace(delta) && (type.IndexOf("delta", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(type, "step_update", StringComparison.OrdinalIgnoreCase)))
        {
            Touch("Đang trả lời"); AgentDelta?.Invoke(delta!);
        }
        var tool = payload.Value<string>("tool") ?? payload.Value<string>("tool_name") ?? payload.SelectToken("tool.name")?.Value<string>() ?? payload.SelectToken("tool_info.name")?.Value<string>();
        if (!string.IsNullOrWhiteSpace(tool))
        {
            if (!Config.CapabilityManifest.ToolNames.Any(name => string.Equals(name, tool, StringComparison.Ordinal))) throw new InvalidOperationException("CLI báo công cụ ngoài DSCons: " + tool);
            var complete = type.IndexOf("complete", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("result", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(payload.Value<string>("state"), "DONE", StringComparison.OrdinalIgnoreCase);
            ToolActivity?.Invoke(new ChatToolActivity { Tool = tool!, Text = complete ? "Đã hoàn tất" : "Đang gọi", IsComplete = complete, IsError = string.Equals(payload.Value<string>("status"), "error", StringComparison.OrdinalIgnoreCase) || payload.SelectToken("tool_info.error") != null });
            Touch("Đang gọi DSCons MCP");
        }
        UpdateProviderConversation(payload.Value<string>("conversation_id") ?? payload.Value<string>("conversationId") ?? payload.Value<string>("session_id") ?? message.Value<string>("conversation_id"));
        var status = payload.Value<string>("status") ?? message.Value<string>("status");
        if (type.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase)) { Fail("CLI báo lỗi: " + (payload.Value<string>("error") ?? message.Value<string>("error") ?? status ?? type)); return; }
        if (string.Equals(type, "result", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase)) Finish(true, "completed");
            else Fail("CLI kết thúc không thành công: " + (status ?? "unknown"));
            return;
        }
        if (type.IndexOf("complete", StringComparison.OrdinalIgnoreCase) >= 0) Finish(true, "completed");
    }

    protected void UpdateProviderConversation(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || _conversation == null || _projectContext == null) return;
        var resolvedConversationId = conversationId!;
        _conversation.ProviderConversationId = resolvedConversationId;
        ChatPersistence.Instance.UpdateProviderConversation(_projectContext, ProviderId, _conversation.Id, resolvedConversationId, _conversation.Model);
    }

    protected void RequireExactDsconsTools(IEnumerable<string> tools, string source)
    {
        var observed = new HashSet<string>(tools.Where(tool => !string.IsNullOrWhiteSpace(tool)), StringComparer.Ordinal);
        var expected = new HashSet<string>(Config.CapabilityManifest.ToolNames, StringComparer.Ordinal);
        if (!expected.SetEquals(observed)) throw new InvalidOperationException(source + " không chứng minh inventory công cụ chỉ gồm DSCons; provider bị khóa an toàn.");
    }

    protected void ReportProgress(string text) => Progress?.Invoke(text);
    protected void PublishUsage(UsageSnapshot snapshot)
    {
        _currentUsage = snapshot;
        UsageChanged?.Invoke(snapshot);
    }

    protected void AbortCurrentTurn(string message)
    {
        if (!IsTurnRunning) return;
        Fail(message);
        DisposeProcess();
    }

    protected static async Task AwaitWithTimeoutAsync(Task task, TimeSpan timeout, string timeoutMessage)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) != task) throw new TimeoutException(timeoutMessage);
        await task.ConfigureAwait(false);
    }

    protected string CreateIsolatedWorkspace(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider) || provider.IndexOf(Path.DirectorySeparatorChar) >= 0 || provider.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            throw new ArgumentException("Tên provider cô lập không hợp lệ.", nameof(provider));
        var root = ResolveIsolatedWorkspaceRoot();
        Directory.CreateDirectory(root);
        var workspace = Path.Combine(root, provider + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace); IsolatedWorkspace = workspace; return workspace;
    }

    private static string ResolveIsolatedWorkspaceRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DSCONS_EMBEDDED_CHAT_ISOLATED_ROOT");
        if (string.IsNullOrWhiteSpace(configured))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSCons", "RevitMcp", "Chat", "isolated");
        var configuredRoot = Path.GetPathRoot(configured);
        if (!Path.IsPathRooted(configured) || string.IsNullOrWhiteSpace(configuredRoot) || configuredRoot.Length <= 1)
            throw new InvalidOperationException("Đường dẫn cô lập test phải là đường dẫn tuyệt đối.");

        var root = Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(root) || string.Equals(Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Đường dẫn cô lập test không được là thư mục gốc ổ đĩa.");
        return root;
    }

    private static bool IsIsolatedWorkspaceChild(string workspace, string root)
    {
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(workspace).StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    protected string CreateDsconsOnlyMcpConfig(string workspace)
    {
        var agents = Path.Combine(workspace, ".agents"); Directory.CreateDirectory(agents);
        var path = Path.Combine(agents, "mcp_config.json");
        var payload = new JObject
        {
            ["mcpServers"] = new JObject { ["dscons_embedded"] = new JObject { ["command"] = ResolveNodeExecutable(), ["args"] = new JArray(Config.McpServerEntrypoint) } }
        };
        File.WriteAllText(path, payload.ToString(Formatting.None), new UTF8Encoding(false)); McpConfigPath = path; return path;
    }

    protected void ConfigureDsconsEnvironment(ProcessStartInfo start)
    {
        start.EnvironmentVariables["DSCONS_EMBEDDED_CHAT"] = "1";
        start.EnvironmentVariables["DSCONS_EMBEDDED_APPROVAL_PIPE"] = Approval.PipeName;
        start.EnvironmentVariables["DSCONS_EMBEDDED_APPROVAL_SECRET"] = Approval.Secret;
        start.EnvironmentVariables["DSCONS_MCP_EXPECTED_REVIT_PID"] = Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    protected static string? FindExecutable(string? configured, string environmentVariable, params string[] names)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        var explicitPath = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return Path.GetFullPath(explicitPath);
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            foreach (var name in names)
            {
                try { var candidate = Path.Combine(directory.Trim().Trim('"'), name); if (File.Exists(candidate)) return candidate; } catch { }
            }
        return null;
    }

    protected static async Task<string> ReadCliVersionAsync(string executable)
    {
        using var process = new Process { StartInfo = NewProcessStartInfo(executable, "--version", null, false) };
        process.Start(); var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false); var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        if (!process.WaitForExit(10_000)) { try { process.Kill(); } catch { } throw new TimeoutException("CLI không trả về phiên bản."); }
        if (process.ExitCode != 0) throw new InvalidOperationException("Không đọc được phiên bản CLI: " + Redact(error));
        return output.Trim();
    }

    protected static async Task<string> ReadCliHelpAsync(string executable)
    {
        using var process = new Process { StartInfo = NewProcessStartInfo(executable, "--help", null, false) };
        process.Start(); var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false); var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        if (!process.WaitForExit(10_000)) { try { process.Kill(); } catch { } throw new TimeoutException("CLI không trả về trợ giúp capability."); }
        if (process.ExitCode != 0) throw new InvalidOperationException("Không đọc được capability CLI: " + Redact(error));
        return output + "\n" + error;
    }

    protected static void RequireCliHelpOptions(string help, string provider, params string[] options)
    {
        var missing = options.Where(option => help.IndexOf(option, StringComparison.OrdinalIgnoreCase) < 0).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(provider + " CLI chưa chứng minh capability bắt buộc: " + string.Join(", ", missing) + ". Panel không dùng giao thức suy đoán hoặc chế độ yếu hơn.");
    }

    protected static ProcessStartInfo NewProcessStartInfo(string executable, string arguments, string? workingDirectory, bool redirectInput = true)
    {
        var command = executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        return new ProcessStartInfo
        {
            FileName = command ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : executable,
            Arguments = command ? "/d /s /c \"\"" + executable + "\" " + arguments + "\"" : arguments,
            WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = redirectInput, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
    }

    private async Task StartProcessAsync()
    {
        DisposeProcess();
        var start = CreateProviderStartInfo(); ConfigureDsconsEnvironment(start);
        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var process = _process; process.Exited += (_, _) => Fail(DisplayName + " CLI đã dừng.");
        if (!process.Start()) throw new InvalidOperationException("Không thể khởi động " + DisplayName + " CLI.");
        _stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
        _ = Task.Run(() => ReadLoopAsync(process)); _ = Task.Run(() => DrainErrorAsync(process));
        await Task.Yield();
    }

    private async Task ReadLoopAsync(Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                JObject message;
                try { message = JObject.Parse(line); }
                catch { Fail(DisplayName + " CLI gửi NDJSON không hợp lệ; lượt chat đã bị khóa an toàn."); return; }
                try { HandleProviderEvent(message); }
                catch (Exception exception) { Fail(exception.GetBaseException().Message); return; }
            }
        }
        catch (Exception exception) { Fail("Mất kết nối " + DisplayName + ": " + exception.GetBaseException().Message); }
    }

    private async Task DrainErrorAsync(Process process)
    {
        try { while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line) ChatDiagnosticLog.Write(ProviderId, Redact(line)); } catch { }
    }

    private async Task WriteJsonAsync(JObject message)
    {
        if (_stdin == null) throw new InvalidOperationException(DisplayName + " CLI chưa khởi động.");
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try { await _stdin.WriteLineAsync(message.ToString(Formatting.None)).ConfigureAwait(false); }
        finally { _writeLock.Release(); }
    }

    private string ResolveNodeExecutable() => FindExecutable(Config.NodeExecutablePath, "NODE_EXE_PATH", "node.exe") ?? throw new FileNotFoundException("Không tìm thấy Node.js để chạy DSCons MCP.");
    private void Touch(string stage) { lock (_stateGate) { _lastActivityUtc = DateTime.UtcNow; _stage = stage; } }
    private void Finish(bool success, string status) { lock (_stateGate) _turnId = null; TurnFinished?.Invoke(success, status); }
    protected void Fail(string message) { lock (_stateGate) _turnId = null; Error?.Invoke(message); TurnFinished?.Invoke(false, message); Progress?.Invoke(message); }
    private static string Redact(string text) => Regex.Replace(text, "(?i)(token|secret|password|api[_-]?key)\\s*[:=]\\s*\\S+", "$1=[đã ẩn]");
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(DisplayName); }
    private void DisposeProcess()
    {
        var process = _process; _process = null; _connected = false;
        try { _stdin?.Dispose(); } catch { } _stdin = null;
        if (process != null) { try { if (!process.HasExited && !process.WaitForExit(500)) process.Kill(); } catch { } process.Dispose(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; DisposeProcess();
        if (IsolatedWorkspace is string workspace && !string.IsNullOrWhiteSpace(workspace))
        {
            try { if (IsIsolatedWorkspaceChild(workspace, ResolveIsolatedWorkspaceRoot())) Directory.Delete(workspace, true); } catch { }
        }
    }
}
#endif
