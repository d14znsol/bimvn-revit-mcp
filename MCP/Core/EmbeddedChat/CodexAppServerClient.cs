#if REVIT2023 || REVIT2025
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DSCons.RevitMcp.Core.EmbeddedChat;

internal sealed class TurnActivitySnapshot
{
    public bool IsRunning { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime LastActivityUtc { get; set; }
    public string Stage { get; set; } = string.Empty;
}

internal sealed class ChatToolActivity
{
    public string Tool { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public bool IsError { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class CodexAppServerClient : IEmbeddedChatProvider
{
    public const string ProviderName = "codex";
    public string ProviderId => ProviderName;
    public string DisplayName => "Codex";
    public ProviderCapabilities Capabilities { get; } = new()
    {
        ProviderId = ProviderName,
        SupportsStreaming = true,
        SupportsConversationResume = true,
        SupportsCachedCliLogin = true,
        RequiresStrictMcpIsolation = true,
        SupportsUsageRefresh = true,
        SupportsMachineReadableUsage = true,
        IsExperimental = true
    };
    private readonly EmbeddedChatConfig _config;
    private readonly EmbeddedApprovalServer _approval;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JToken>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly SemaphoreSlim _usageLock = new(1, 1);
    private readonly object _stateGate = new();
    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutTask;
    private string? _threadId;
    private string? _turnId;
    private DateTime _turnStartedUtc;
    private DateTime _lastTurnActivityUtc;
    private string _turnStage = string.Empty;
    private int _nextId;
    private bool _disposed;
    private bool _initialized;
    private bool _sessionReady;
    private bool _accountReady;
    private int _accountRefreshRunning;
    private List<ChatModelOption> _models = new();
    private readonly object _stderrGate = new();
    private readonly Queue<string> _recentStderr = new();
    private ChatProjectContext? _projectContext;
    private ChatConversation? _conversation;
    private UsageSnapshot _currentUsage = UsageSnapshot.Unavailable(ProviderName, "Codex chưa được kiểm tra hạn mức.");

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
    public bool IsTurnRunning { get { lock (_stateGate) return !string.IsNullOrEmpty(_turnId); } }
    public UsageSnapshot CurrentUsage => _currentUsage;

    public TurnActivitySnapshot GetTurnActivity()
    {
        lock (_stateGate)
        {
            return new TurnActivitySnapshot
            {
                IsRunning = !string.IsNullOrEmpty(_turnId),
                StartedUtc = _turnStartedUtc,
                LastActivityUtc = _lastTurnActivityUtc,
                Stage = _turnStage
            };
        }
    }

    public CodexAppServerClient(EmbeddedChatConfig config, EmbeddedApprovalServer approval)
    {
        _config = config;
        _approval = approval;
    }

    public void SetProjectContext(ChatProjectContext context)
    {
        if (_projectContext != null && string.Equals(_projectContext.Key, context.Key, StringComparison.Ordinal)) return;
        _projectContext = context;
        _conversation = null;
        _threadId = null;
        _sessionReady = false;
        ChatDiagnosticLog.Write("project", context.IsPersistable ? "context changed" : "unsaved context");
    }

    public ChatConversation? CurrentConversation => _conversation;
    public IReadOnlyList<ChatConversation> ListConversations() => _projectContext == null ? Array.Empty<ChatConversation>() : ChatPersistence.Instance.List(_projectContext, ProviderName);
    public IReadOnlyList<ChatMemoryEntry> GetMemories() => _projectContext == null ? Array.Empty<ChatMemoryEntry>() : ChatPersistence.Instance.GetMemories(_projectContext);

    public bool Remember(string text, bool global)
    {
        if (_projectContext == null) return false;
        var saved = ChatPersistence.Instance.Remember(_projectContext, text, global);
        if (saved) ChatDiagnosticLog.Write("memory", global ? "global memory saved" : "project memory saved");
        return saved;
    }

    public void Forget(string memoryId) => ChatPersistence.Instance.Forget(memoryId);

    public void RecordMessage(string role, string text)
    {
        if (_projectContext == null || _conversation == null) return;
        ChatPersistence.Instance.AddMessage(_projectContext, ProviderName, _conversation.Id, role, text);
    }

    public async Task ConnectAsync()
    {
        ThrowIfDisposed();
        await EnsureAppServerAsync().ConfigureAwait(false);
        await _sessionLock.WaitAsync().ConfigureAwait(false);
        try
        {
        if (_sessionReady && _process != null && !_process.HasExited && !string.IsNullOrEmpty(_threadId)) return;
        _threadId = null;
        var configResult = await RequestAsync("config/read", new { }).ConfigureAwait(false);
        await RefreshAccountAsync().ConfigureAwait(false);
        _models = await ListModelsAsync().ConfigureAwait(false);
        var defaultModel = _models.FirstOrDefault(model => model.IsDefault)?.Model ?? _models.FirstOrDefault()?.Model;
        ModelsChanged?.Invoke(_models, defaultModel);
        var node = FindExecutable(_config.NodeExecutablePath, "NODE_EXE_PATH", "node.exe");
        if (node == null) throw new InvalidOperationException("Chưa tìm thấy Node.js. Hãy cài Node.js LTS rồi mở lại Revit.");
        var sessionConfig = BuildSessionConfig(configResult, node);
        var context = _projectContext ?? ChatProjectContext.Create(null, "Project chua mo");
        _conversation ??= ChatPersistence.Instance.GetOrCreateActive(context, ProviderName);
        JToken threadResult;
        if (!string.IsNullOrWhiteSpace(_conversation.ProviderConversationId))
        {
            try
            {
                await RequestAsync("thread/read", new { threadId = _conversation.ProviderConversationId, includeTurns = false }).ConfigureAwait(false);
                threadResult = await RequestAsync("thread/resume", new
                {
                    threadId = _conversation.ProviderConversationId,
                    cwd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    approvalPolicy = "never",
                    sandbox = "read-only",
                    developerInstructions = BuildDeveloperInstructions(context),
                    model = _conversation.Model ?? defaultModel,
                    config = sessionConfig
                }).ConfigureAwait(false);
                ChatDiagnosticLog.Write("thread", "resumed stored conversation");
            }
            catch (Exception ex)
            {
                ChatDiagnosticLog.Write("thread", "resume failed: " + ex.GetType().Name);
                _conversation = ChatPersistence.Instance.CreateNew(context, ProviderName);
                threadResult = await StartThreadAsync(defaultModel, sessionConfig, context).ConfigureAwait(false);
            }
        }
        else threadResult = await StartThreadAsync(defaultModel, sessionConfig, context).ConfigureAwait(false);
        _threadId = FindString(threadResult, "id", "threadId") ?? throw new InvalidOperationException("Codex không trả về mã cuộc trò chuyện.");
        _conversation.ProviderConversationId = _threadId;
        _conversation.Model ??= defaultModel;
        ChatPersistence.Instance.UpdateProviderConversation(context, ProviderName, _conversation.Id, _threadId, _conversation.Model);
        ConversationChanged?.Invoke(_conversation);
        await VerifyMcpAsync().ConfigureAwait(false);
        _sessionReady = true;
        StatusChanged?.Invoke(new ChatProviderStatus { Provider = "Đã kết nối", Mcp = "Đã xác minh DSCons", Account = _lastAccount, Availability = "Sẵn sàng" });
        await RefreshUsageAsync(false, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            _threadId = null;
            _sessionReady = false;
            throw;
        }
        finally { _sessionLock.Release(); }
    }

    private Task<JToken> StartThreadAsync(string? model, JObject sessionConfig, ChatProjectContext context) => RequestAsync("thread/start", new
    {
        cwd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        approvalPolicy = "never",
        sandbox = "read-only",
        developerInstructions = BuildDeveloperInstructions(context),
        ephemeral = false,
        model,
        config = sessionConfig
    });

    private string BuildDeveloperInstructions(ChatProjectContext context)
    {
        var memories = ChatPersistence.Instance.GetMemories(context);
        if (memories.Count == 0) return EmbeddedChatInstructions.Text;
        var safe = memories.Take(12).Select(item => "- " + item.Text.Replace('\r', ' ').Replace('\n', ' '));
        return EmbeddedChatInstructions.Text + "\nGhi chu hoc vien da chu dong luu (khong phai du lieu model hien tai):\n" + string.Join("\n", safe);
    }

    private async Task EnsureAppServerAsync()
    {
        ThrowIfDisposed();
        await _connectLock.WaitAsync().ConfigureAwait(false);
        try
        {
        if (_initialized && _process != null && !_process.HasExited) return;
        if (_process != null) DisposeProcess();
        var codex = FindExecutable(_config.CodexCliPath, "CODEX_CLI_PATH", "codex.exe", "codex.cmd");
        if (codex == null) throw new InvalidOperationException("Chưa tìm thấy Codex CLI. Hãy cài Codex CLI rồi mở lại Revit.");
        var version = await ReadVersionAsync(codex).ConfigureAwait(false);
        if (version.IndexOf(_config.VerifiedCodexCliVersion, StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException($"Codex CLI {version} chưa được kiểm chứng. Bản thử yêu cầu {_config.VerifiedCodexCliVersion}; không tự nâng cấp CLI.");
        var start = CreateCodexStartInfo(codex, "app-server");
        start.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        lock (_stderrGate) _recentStderr.Clear();
        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var process = _process;
        process.Exited += (_, _) => HandleProcessExited(process);
        if (!_process.Start()) throw new InvalidOperationException("Không thể khởi động Codex app-server.");
        _stdin = new StreamWriter(_process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
        _stdoutTask = Task.Run(ReadLoopAsync);
        _ = Task.Run(() => DrainErrorsAsync(process));

        await RequestAsync("initialize", new { clientInfo = new { name = "dscons-revit-chat", title = "DSCons Revit Chat AI", version = "0.1.0" } }).ConfigureAwait(false);
        await NotifyAsync("initialized", new { }).ConfigureAwait(false);
        _initialized = true;
        StatusChanged?.Invoke(new ChatProviderStatus { Provider = "Đã khởi động " + version, Mcp = "Đang kiểm tra", Account = _lastAccount, Availability = "Đang kiểm tra" });
        }
        finally { _connectLock.Release(); }
    }

    private string _lastAccount = "Chưa đăng nhập";

    public async Task RefreshAccountAsync()
    {
        var result = await RequestAsync("account/read", new { refreshToken = false }).ConfigureAwait(false);
        var account = result["account"] ?? result.SelectToken("*.account");
        var requiresOpenAiAuth = result["requiresOpenaiAuth"]?.Value<bool>() ?? true;
        _accountReady = account != null && account.Type != JTokenType.Null || !requiresOpenAiAuth;
        _lastAccount = account != null && account.Type != JTokenType.Null
            ? "Đã đăng nhập"
            : requiresOpenAiAuth ? "Chưa đăng nhập" : "Codex CLI đã sẵn sàng";
    }

    public async Task StartLoginAsync()
    {
        await EnsureAppServerAsync().ConfigureAwait(false);
        await RefreshAccountAsync().ConfigureAwait(false);
        if (_accountReady)
        {
            StatusChanged?.Invoke(new ChatProviderStatus { Account = _lastAccount, Provider = "Đã khởi động", Mcp = "Đang kết nối", Availability = "Đang kiểm tra" });
            Progress?.Invoke("Codex CLI đã có thể sử dụng; không cần đăng nhập lại.");
            await ConnectAsync().ConfigureAwait(false);
            return;
        }
        var result = await RequestAsync("account/login/start", new
        {
            type = "chatgpt",
            useHostedLoginSuccessPage = true,
            appBrand = "chatgpt"
        }).ConfigureAwait(false);
        var url = FindString(result, "authUrl", "url");
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("Codex không trả về liên kết đăng nhập.");
        LoginUrlAvailable?.Invoke(url!);
    }

    public async Task RefreshUsageAsync(bool force, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!force && _currentUsage.Confidence != UsageConfidence.Unavailable && DateTimeOffset.UtcNow - _currentUsage.ObservedAtUtc < TimeSpan.FromMinutes(2))
        {
            UsageChanged?.Invoke(_currentUsage);
            return;
        }
        await _usageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureAppServerAsync().ConfigureAwait(false);
            var result = await RequestAsync("account/rateLimits/read", new { excludeResetCreditDetails = true, supportsLunaReserve = false }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            PublishUsage(UsageSnapshotParser.FromCodex(result));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            ChatDiagnosticLog.Write("usage", "Codex quota refresh unavailable: " + ex.GetType().Name);
            if (_currentUsage.Confidence == UsageConfidence.Unavailable)
                PublishUsage(UsageSnapshot.Unavailable(ProviderName, "Tạm thời không đọc được hạn mức Codex.", "Mở trang Usage của tài khoản Codex để kiểm tra."));
        }
        finally { _usageLock.Release(); }
    }

    private void PublishUsage(UsageSnapshot snapshot)
    {
        _currentUsage = snapshot;
        UsageChanged?.Invoke(snapshot);
    }

    public async Task StartTurnAsync(string text, string? model)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await ConnectAsync().ConfigureAwait(false);
        lock (_stateGate)
        {
            if (!string.IsNullOrEmpty(_turnId)) throw new InvalidOperationException("Hãy chờ yêu cầu hiện tại hoàn tất hoặc bấm Dừng.");
            _turnId = "starting";
            _turnStartedUtc = DateTime.UtcNow;
            _lastTurnActivityUtc = _turnStartedUtc;
            _turnStage = "Đang gửi yêu cầu tới Codex";
        }
        try
        {
            var result = await RequestAsync("turn/start", new { threadId = _threadId, input = new[] { new { type = "text", text } }, model }).ConfigureAwait(false);
            var turnId = FindString(result, "id", "turnId") ?? "running";
            // Notifications can arrive before the request response. In
            // particular, an immediately completed turn clears `_turnId` in
            // HandleNotification; never resurrect it as a running turn here.
            lock (_stateGate) if (_turnId == "starting") _turnId = turnId;
        }
        catch { lock (_stateGate) _turnId = null; throw; }
    }

    public async Task InterruptAsync()
    {
        string? turn;
        lock (_stateGate) turn = _turnId;
        if (string.IsNullOrEmpty(turn) || turn == "starting" || string.IsNullOrEmpty(_threadId)) return;
        await RequestAsync("turn/interrupt", new { threadId = _threadId, turnId = turn }).ConfigureAwait(false);
        Progress?.Invoke("Đã yêu cầu Codex dừng. Nếu Revit đang commit, hệ thống sẽ chờ kết quả đọc lại.");
    }

    public async Task NewConversationAsync()
    {
        if (IsTurnRunning) throw new InvalidOperationException("Hãy dừng hoặc chờ lượt hiện tại hoàn tất.");
        if (_projectContext != null) _conversation = ChatPersistence.Instance.CreateNew(_projectContext, ProviderName);
        _threadId = null;
        _sessionReady = false;
        await ConnectAsync().ConfigureAwait(false);
    }

    public async Task SelectConversationAsync(string conversationId)
    {
        if (IsTurnRunning) throw new InvalidOperationException("Cannot switch conversation while a turn is running.");
        if (_projectContext == null) return;
        var selected = ChatPersistence.Instance.Find(_projectContext, ProviderName, conversationId);
        if (selected == null) throw new InvalidOperationException("Conversation is no longer available.");
        ChatPersistence.Instance.Activate(_projectContext, ProviderName, selected.Id);
        _conversation = selected;
        _threadId = null;
        _sessionReady = false;
        await ConnectAsync().ConfigureAwait(false);
    }

    public async Task DeleteConversationAsync(string conversationId)
    {
        if (IsTurnRunning || _projectContext == null) return;
        ChatPersistence.Instance.Delete(_projectContext, ProviderName, conversationId);
        _conversation = null;
        _threadId = null;
        _sessionReady = false;
        await ConnectAsync().ConfigureAwait(false);
    }

    private async Task<List<ChatModelOption>> ListModelsAsync()
    {
        var result = new List<ChatModelOption>();
        string? cursor = null;
        do
        {
            var page = await RequestAsync("model/list", new { cursor, limit = 100, includeHidden = false }).ConfigureAwait(false);
            if (page["data"] is JArray data)
                foreach (var item in data.OfType<JObject>())
                {
                    var model = item["model"]?.Value<string>() ?? item["id"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(model)) continue;
                    var modalities = item["inputModalities"] as JArray;
                    if (modalities != null && !modalities.Values<string>().Any(value => string.Equals(value, "text", StringComparison.OrdinalIgnoreCase))) continue;
                    if (result.Any(existing => string.Equals(existing.Model, model, StringComparison.Ordinal))) continue;
                    result.Add(new ChatModelOption
                    {
                        Model = model!,
                        DisplayName = item["displayName"]?.Value<string>() ?? model!,
                        IsDefault = item["isDefault"]?.Value<bool>() ?? false
                    });
                }
            cursor = page["nextCursor"]?.Value<string>();
        } while (!string.IsNullOrWhiteSpace(cursor));
        if (result.Count == 0) throw new InvalidOperationException("Codex không trả về model nào khả dụng cho tài khoản hiện tại.");
        return result;
    }

    private JObject BuildSessionConfig(JToken configRead, string node)
    {
        var servers = new JObject();
        var configured = configRead.SelectToken("config.mcp_servers") as JObject ?? configRead.SelectToken("mcp_servers") as JObject;
        if (configured != null)
            foreach (var property in configured.Properties()) servers[property.Name] = new JObject { ["enabled"] = false };
        servers["dscons_embedded"] = new JObject
        {
            ["command"] = node,
            ["args"] = new JArray(_config.McpServerEntrypoint),
            ["env"] = new JObject
            {
                ["DSCONS_EMBEDDED_CHAT"] = "1",
                ["DSCONS_EMBEDDED_APPROVAL_PIPE"] = _approval.PipeName,
                ["DSCONS_EMBEDDED_APPROVAL_SECRET"] = _approval.Secret,
                ["DSCONS_MCP_EXPECTED_REVIT_PID"] = Process.GetCurrentProcess().Id.ToString()
            },
            ["startup_timeout_sec"] = 20,
            ["tool_timeout_sec"] = 600,
            ["default_tools_approval_mode"] = "approve",
            ["enabled"] = true,
            ["required"] = true
        };
        return new JObject
        {
            ["mcp_servers"] = servers,
            ["web_search"] = "disabled",
            ["memories"] = new JObject
            {
                ["generate_memories"] = false,
                ["use_memories"] = false,
                ["disable_on_external_context"] = true
            },
            ["features"] = new JObject
            {
                ["shell_tool"] = false,
                ["apps"] = false,
                ["plugins"] = false,
                ["browser_use"] = false,
                ["in_app_browser"] = false,
                ["computer_use"] = false,
                ["image_generation"] = false,
                ["multi_agent"] = false,
                ["skill_search"] = false,
                ["goals"] = false
            },
            ["agents"] = new JObject { ["enabled"] = false }
        };
    }

    private async Task VerifyMcpAsync()
    {
        var result = await RequestAsync("mcpServerStatus/list", new { threadId = _threadId, detail = "toolsAndAuthOnly", limit = 100 }).ConfigureAwait(false);
        var entries = FindServerEntries(result).ToList();
        var embedded = entries.FirstOrDefault(entry => string.Equals(ServerName(entry), "dscons_embedded", StringComparison.Ordinal));
        if (embedded == null) throw new InvalidOperationException("Phiên Chat AI không thấy DSCons MCP duy nhất đã được cấu hình.");
        var expected = new HashSet<string>(_config.CapabilityManifest.ToolNames, StringComparer.Ordinal);
        var observed = new HashSet<string>(ToolNames(embedded), StringComparer.Ordinal);
        if (!expected.SetEquals(observed))
        {
            var missing = expected.Except(observed).Take(8).ToArray(); var extra = observed.Except(expected).Take(8).ToArray();
            throw new InvalidOperationException("DSCons MCP inventory không khớp capability manifest (thiếu: " + string.Join(", ", missing) + "; thừa: " + string.Join(", ", extra) + ").");
        }
        var reportedFingerprint = FindString(embedded, "schemaFingerprintSha256", "capabilityFingerprint", "schema_fingerprint_sha256");
        if (!string.IsNullOrWhiteSpace(reportedFingerprint) && !string.Equals(reportedFingerprint, _config.CapabilityManifest.SchemaFingerprintSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DSCons MCP báo capability fingerprint không khớp. Quyền ghi model đã bị chặn.");
        // app-server retains disabled global MCP entries in its status list, but
        // they expose no inventory and cannot be called by this session. Keep
        // the isolation boundary strict for any foreign server that does expose
        // tools; treating disabled zero-tool records as foreign is a false
        // positive that blocks an otherwise isolated embedded session.
        var foreign = entries.Where(entry => ToolNames(entry).Any())
            .Select(ServerName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, "dscons_embedded", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (foreign.Length > 0)
            throw new InvalidOperationException("Phiên Chat AI còn thấy MCP ngoài DSCons: " + string.Join(", ", foreign) + ". Quyền ghi model đã bị chặn.");
    }

    private static IEnumerable<JToken> FindServerEntries(JToken token)
    {
        if (token is JObject obj)
        {
            // A tool descriptor also has a `name`. Only a server-status entry
            // carries the inventory that VerifyMcpAsync must validate; never
            // recurse into that inventory as though each tool were a server.
            if ((obj["name"] != null || obj["serverName"] != null) && obj["tools"] != null)
            {
                yield return obj;
                yield break;
            }
        }
        if (token is JArray array)
        {
            foreach (var item in array)
                foreach (var entry in FindServerEntries(item)) yield return entry;
            yield break;
        }
        foreach (var child in token.Children())
            foreach (var entry in FindServerEntries(child)) yield return entry;
    }

    private static string ServerName(JToken? token) => token?["name"]?.Value<string>() ?? token?["serverName"]?.Value<string>() ?? string.Empty;
    private static IEnumerable<string> ToolNames(JToken? token) => token?["tools"] switch
    {
        JArray tools => tools.Select(tool => tool.Type == JTokenType.String ? tool.Value<string>() : tool["name"]?.Value<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!),
        JObject tools => tools.Properties().Select(property => property.Value.Type == JTokenType.Object ? property.Value["name"]?.Value<string>() ?? property.Name : property.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!),
        _ => Array.Empty<string>()
    };

    private async Task<JToken> RequestAsync(string method, object parameters)
    {
        if (_stdin == null) throw new InvalidOperationException("Codex app-server chưa khởi động.");
        var id = Interlocked.Increment(ref _nextId).ToString();
        var completion = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        await SendAsync(new JObject { ["id"] = id, ["method"] = method, ["params"] = JToken.FromObject(parameters) }).ConfigureAwait(false);
        var timeout = Task.Delay(TimeSpan.FromSeconds(method == "turn/start" ? 120 : 30));
        var done = await Task.WhenAny(completion.Task, timeout).ConfigureAwait(false);
        if (done != completion.Task) { _pending.TryRemove(id, out _); throw new TimeoutException($"Codex không phản hồi phương thức {method}."); }
        return await completion.Task.ConfigureAwait(false);
    }

    private Task NotifyAsync(string method, object parameters) => SendAsync(new JObject { ["method"] = method, ["params"] = JToken.FromObject(parameters) });

    private async Task SendAsync(JObject message)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try { await _stdin!.WriteLineAsync(message.ToString(Formatting.None)).ConfigureAwait(false); }
        finally { _writeLock.Release(); }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (_process != null && !_process.HasExited)
            {
                var line = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;
                JObject message;
                try { message = JObject.Parse(line); }
                catch
                {
                    const string invalidMessage = "Codex gửi dữ liệu không hợp lệ; lượt hiện tại đã bị khóa an toàn.";
                    Progress?.Invoke(invalidMessage);
                    FailAll(invalidMessage);
                    DisposeProcess();
                    return;
                }
                var id = message["id"]?.ToString();
                if (!string.IsNullOrEmpty(id) && (message["result"] != null || message["error"] != null))
                {
                    if (_pending.TryRemove(id!, out var completion))
                    {
                        if (message["error"] != null) completion.TrySetException(new InvalidOperationException(message["error"]!.ToString(Formatting.None)));
                        else completion.TrySetResult(message["result"]!);
                    }
                    continue;
                }
                if (!string.IsNullOrEmpty(id) && message["method"] != null)
                {
                    var method = message["method"]?.Value<string>() ?? "unknown";
                    ChatDiagnosticLog.Write("server-request", method);
                    Progress?.Invoke("Codex yêu cầu một quyền ngoài luồng DSCons; thao tác đã được khóa an toàn. Hãy dừng và gửi lại sau khi kiểm tra kết nối.");
                    ToolActivity?.Invoke(new ChatToolActivity { Tool = method, Text = "Đã chặn yêu cầu ngoài chính sách", IsComplete = true, IsError = true });
                    await SendAsync(new JObject { ["id"] = id, ["error"] = new JObject { ["code"] = -32010, ["message"] = "Unexpected server request is blocked by DSCons embedded-chat policy." } }).ConfigureAwait(false);
                    continue;
                }
                HandleNotification(message);
            }
        }
        catch (Exception ex) { FailAll("Mất kết nối Codex: " + ex.Message); }
    }

    private void HandleNotification(JObject message)
    {
        var method = message["method"]?.Value<string>() ?? string.Empty;
        var parameters = message["params"];
        if (method == "item/agentMessage/delta")
        {
            var delta = parameters?["delta"]?.Value<string>();
            if (!string.IsNullOrEmpty(delta))
            {
                TouchTurnActivity("Codex đang trả lời");
                AgentDelta?.Invoke(delta!);
            }
        }
        else if (method == "turn/started")
        {
            var id = FindString(parameters, "id", "turnId");
            lock (_stateGate)
            {
                if (!string.IsNullOrEmpty(id)) _turnId = id;
                if (_turnStartedUtc == default) _turnStartedUtc = DateTime.UtcNow;
                _lastTurnActivityUtc = DateTime.UtcNow;
                _turnStage = "Codex đang phân tích yêu cầu";
            }
            Progress?.Invoke("Codex đang xử lý yêu cầu…");
        }
        else if (method == "item/started" || method == "item/completed")
        {
            var item = parameters?["item"] ?? parameters?.SelectToken("$..item");
            var type = item?["type"]?.Value<string>() ?? string.Empty;
            var completed = method == "item/completed";
            if (string.Equals(type, "mcpToolCall", StringComparison.OrdinalIgnoreCase))
            {
                var server = item?["server"]?.Value<string>() ?? string.Empty;
                var tool = item?["tool"]?.Value<string>() ?? "công cụ MCP";
                if (server.IndexOf("dscons", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TouchTurnActivity((completed ? "MCP đã trả kết quả: " : "Đang gọi MCP: ") + tool);
                    ToolActivity?.Invoke(new ChatToolActivity
                    {
                        Tool = tool,
                        Text = completed ? "Đã hoàn tất: " + tool : "Đang gọi: " + tool,
                        IsComplete = completed,
                        IsError = completed && string.Equals(item?["status"]?.Value<string>(), "failed", StringComparison.OrdinalIgnoreCase)
                    });
                }
                else TouchTurnActivity(completed ? "Công cụ đã hoàn tất" : "Codex đang gọi công cụ");
            }
            else if (string.Equals(type, "reasoning", StringComparison.OrdinalIgnoreCase))
                TouchTurnActivity(completed ? "Codex đã phân tích xong" : "Codex đang phân tích");
            else if (!string.Equals(type, "agentMessage", StringComparison.OrdinalIgnoreCase))
                TouchTurnActivity(completed ? "Codex đã hoàn tất một bước" : "Codex đang thực hiện: " + type);
        }
        else if (method == "item/reasoning/summaryTextDelta" || method == "item/reasoning/textDelta")
        {
            TouchTurnActivity("Codex đang phân tích");
        }
        else if (method == "turn/completed")
        {
            var status = FindString(parameters, "status") ?? "completed";
            lock (_stateGate)
            {
                _lastTurnActivityUtc = DateTime.UtcNow;
                _turnStage = "Lượt chat đã kết thúc";
                _turnId = null;
            }
            TurnFinished?.Invoke(string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase), status);
            _ = RefreshUsageAsync(true, CancellationToken.None);
        }
        else if (method == "account/login/completed")
        {
            var success = parameters?["success"]?.Value<bool>() == true;
            if (!success)
            {
                _accountReady = false;
                _lastAccount = "Chưa đăng nhập";
                var error = parameters?["error"]?.Value<string>();
                StatusChanged?.Invoke(new ChatProviderStatus { Account = _lastAccount, Provider = "Đã khởi động", Mcp = "Chưa kết nối", Availability = "Chưa đăng nhập" });
                Progress?.Invoke(string.IsNullOrWhiteSpace(error)
                    ? "Đăng nhập Codex chưa hoàn tất. Hãy bấm Đăng nhập và thử lại."
                    : "Đăng nhập Codex không thành công: " + SanitizeDiagnostic(error!));
                return;
            }
            BeginAccountRefresh();
        }
        else if (method == "account/updated")
        {
            BeginAccountRefresh();
        }
        else if (method == "account/rateLimits/updated")
        {
            var snapshot = UsageSnapshotParser.FromCodex(parameters ?? new JObject());
            snapshot.Source = "codex_app_server:account/rateLimits/updated";
            PublishUsage(snapshot);
        }
        else if (method.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) Progress?.Invoke(parameters?.ToString(Formatting.None) ?? method);
    }

    private void TouchTurnActivity(string stage)
    {
        lock (_stateGate)
        {
            if (string.IsNullOrEmpty(_turnId)) return;
            _lastTurnActivityUtc = DateTime.UtcNow;
            _turnStage = stage;
        }
    }

    private void BeginAccountRefresh()
    {
        if (Interlocked.Exchange(ref _accountRefreshRunning, 1) == 1) return;
        _sessionReady = false;
        _threadId = null;
        StatusChanged?.Invoke(new ChatProviderStatus { Account = "Đang xác minh", Provider = "Đã khởi động", Mcp = "Đang nạp model và MCP", Availability = "Đang kiểm tra" });
        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshAccountAsync().ConfigureAwait(false);
                if (!_accountReady)
                    throw new InvalidOperationException("Codex chưa ghi nhận tài khoản sau khi trình duyệt hoàn tất.");
                await ConnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Progress?.Invoke("Chưa kết nối được Codex/MCP: " + ex.GetBaseException().Message);
            }
            finally { Interlocked.Exchange(ref _accountRefreshRunning, 0); }
        });
    }

    private static string? FindString(JToken? token, params string[] names)
    {
        if (token == null) return null;
        foreach (var name in names)
        {
            var direct = token[name]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(direct)) return direct;
            var nested = token.SelectTokens("$.." + name).Select(value => value.Value<string>()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(nested)) return nested;
        }
        return null;
    }

    private static async Task<string> ReadVersionAsync(string codex)
    {
        using var process = new Process { StartInfo = CreateCodexStartInfo(codex, "--version") };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        if (!process.WaitForExit(10_000)) { try { process.Kill(); } catch { } throw new TimeoutException("Codex CLI không trả về phiên bản."); }
        if (process.ExitCode != 0) throw new InvalidOperationException("Không đọc được phiên bản Codex CLI: " + stderr.Trim());
        return stdout.Trim();
    }

    private static ProcessStartInfo CreateCodexStartInfo(string codex, string arguments)
    {
        var isCommand = codex.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || codex.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        return new ProcessStartInfo
        {
            FileName = isCommand ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : codex,
            Arguments = isCommand ? $"/d /s /c \"\"{codex}\" {arguments}\"" : arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = arguments == "app-server",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
    }

    private static string? FindExecutable(string? configuredPath, string overrideVariable, params string[] names)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)) return Path.GetFullPath(configuredPath);
        var explicitPath = Environment.GetEnvironmentVariable(overrideVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return Path.GetFullPath(explicitPath);
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            foreach (var name in names)
            {
                try { var candidate = Path.Combine(directory.Trim().Trim('"'), name); if (File.Exists(candidate)) return candidate; } catch { }
            }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var patternRoot in new[] { Path.Combine(local, "OpenAI", "Codex", "bin"), Path.Combine(local, "Programs") })
            if (Directory.Exists(patternRoot))
                foreach (var name in names)
                {
                    var match = Directory.GetFiles(patternRoot, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (match != null) return match;
                }
        foreach (var extensionRoot in new[] { Path.Combine(user, ".antigravity-ide", "extensions"), Path.Combine(user, ".vscode", "extensions"), Path.Combine(user, ".cursor", "extensions") })
        {
            if (!Directory.Exists(extensionRoot)) continue;
            foreach (var extension in Directory.GetDirectories(extensionRoot, "openai.chatgpt-*", SearchOption.TopDirectoryOnly).OrderByDescending(Directory.GetLastWriteTimeUtc))
                foreach (var name in names)
                    foreach (var relative in new[] { Path.Combine("bin", "windows-x86_64", name), Path.Combine("bin", "windows-arm64", name), Path.Combine("bin", name) })
                    {
                        var candidate = Path.Combine(extension, relative);
                        if (File.Exists(candidate)) return candidate;
                    }
        }
        return null;
    }

    private async Task DrainErrorsAsync(Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                var safe = SanitizeDiagnostic(line);
                if (string.IsNullOrWhiteSpace(safe)) continue;
                lock (_stderrGate)
                {
                    _recentStderr.Enqueue(safe);
                    while (_recentStderr.Count > 8) _recentStderr.Dequeue();
                }
            }
        }
        catch { }
    }

    private void HandleProcessExited(Process process)
    {
        // Ignore an intentionally disposed or superseded process. Its Exited
        // callback can arrive after a replacement app-server has already
        // started; failing the shared pending-request table in that case would
        // tear down the new connection during resume/reconnect.
        if (!ReferenceEquals(_process, process)) return;
        _initialized = false;
        _sessionReady = false;
        _threadId = null;
        _ = Task.Run(async () =>
        {
            await Task.Delay(100).ConfigureAwait(false);
            if (!ReferenceEquals(_process, process)) return;
            var exitCode = "không rõ";
            try { exitCode = process.ExitCode.ToString(); } catch { }
            string details;
            lock (_stderrGate) details = string.Join(" | ", _recentStderr.Reverse().Take(3).Reverse());
            var message = "Tiến trình Codex đã dừng (mã " + exitCode + ").";
            if (!string.IsNullOrWhiteSpace(details)) message += " Chi tiết: " + details;
            FailAll(message);
            Progress?.Invoke(message + " Bấm Gửi hoặc Đăng nhập để kết nối lại.");
        });
    }

    private static string SanitizeDiagnostic(string line)
    {
        var safe = Regex.Replace(line, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);
        safe = Regex.Replace(safe, @"https?://\S+", "[URL đã ẩn]", RegexOptions.IgnoreCase);
        safe = Regex.Replace(safe, @"(?i)(token|secret|authorization|api[_-]?key)\s*[:=]\s*\S+", "$1=[đã ẩn]");
        safe = Regex.Replace(safe, @"[A-Za-z0-9_\-]{64,}", "[dữ liệu đã ẩn]");
        return safe.Length <= 500 ? safe : safe.Substring(0, 500) + "…";
    }

    private void FailAll(string message)
    {
        foreach (var item in _pending) if (_pending.TryRemove(item.Key, out var completion)) completion.TrySetException(new InvalidOperationException(message));
        lock (_stateGate) _turnId = null;
        Error?.Invoke(message);
        TurnFinished?.Invoke(false, message);
    }

    private void DisposeProcess()
    {
        var process = _process;
        _process = null;
        _initialized = false;
        _sessionReady = false;
        _threadId = null;
        try { _stdin?.Dispose(); } catch { }
        _stdin = null;
        if (process != null)
        {
            try { if (!process.HasExited && !process.WaitForExit(1_000)) process.Kill(); } catch { }
            process.Dispose();
        }
    }

    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(CodexAppServerClient)); }
    public void Dispose() { if (_disposed) return; _disposed = true; DisposeProcess(); }
}
#endif
