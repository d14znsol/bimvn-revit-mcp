#if REVIT2023 || REVIT2025
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Core.EmbeddedChat;

/// <summary>Stable provider contract for the one DSCons chat panel.</summary>
internal interface IEmbeddedChatProvider : IDisposable
{
    string ProviderId { get; }
    string DisplayName { get; }
    ProviderCapabilities Capabilities { get; }
    bool IsTurnRunning { get; }
    ChatConversation? CurrentConversation { get; }
    UsageSnapshot CurrentUsage { get; }
    event Action<string>? AgentDelta;
    event Action<string>? Progress;
    event Action<bool, string>? TurnFinished;
    event Action<ChatProviderStatus>? StatusChanged;
    event Action<string>? LoginUrlAvailable;
    event Action<IReadOnlyList<ChatModelOption>, string?>? ModelsChanged;
    event Action<ChatToolActivity>? ToolActivity;
    event Action<ChatConversation>? ConversationChanged;
    event Action<UsageSnapshot>? UsageChanged;
    event Action<string>? Error;
    TurnActivitySnapshot GetTurnActivity();
    void SetProjectContext(ChatProjectContext context);
    IReadOnlyList<ChatConversation> ListConversations();
    IReadOnlyList<ChatMemoryEntry> GetMemories();
    bool Remember(string text, bool global);
    void Forget(string memoryId);
    void RecordMessage(string role, string text);
    Task ConnectAsync();
    Task StartLoginAsync();
    Task StartTurnAsync(string text, string? model);
    Task InterruptAsync();
    Task NewConversationAsync();
    Task SelectConversationAsync(string conversationId);
    Task DeleteConversationAsync(string conversationId);
    Task RefreshUsageAsync(bool force, CancellationToken cancellationToken);
}

internal sealed class ProviderCapabilities
{
    public string ProviderId { get; set; } = string.Empty;
    public bool SupportsStreaming { get; set; }
    public bool SupportsConversationResume { get; set; }
    public bool SupportsCachedCliLogin { get; set; }
    public bool RequiresStrictMcpIsolation { get; set; }
    public bool SupportsUsageRefresh { get; set; }
    public bool SupportsMachineReadableUsage { get; set; }
    public bool IsExperimental { get; set; }
}

internal enum UsageConfidence
{
    Exact,
    Partial,
    Unavailable
}

internal enum UsageWindowKind
{
    Session5Hours,
    Weekly,
    Requests,
    Tokens,
    ProviderSpecific
}

internal sealed class UsageWindow
{
    public UsageWindowKind Kind { get; set; }
    public string Label { get; set; } = string.Empty;
    public double? Used { get; set; }
    public double? Remaining { get; set; }
    public double? Limit { get; set; }
    public double? UsedPercent { get; set; }
    public DateTimeOffset? ResetAtUtc { get; set; }
    public long? WindowMinutes { get; set; }
    public UsageConfidence Confidence { get; set; }
}

internal sealed class UsageCredits
{
    public bool? HasCredits { get; set; }
    public bool? Unlimited { get; set; }
    public string? Balance { get; set; }
}

internal sealed class UsageSnapshot
{
    public string ProviderId { get; set; } = string.Empty;
    public string? Plan { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public UsageConfidence Confidence { get; set; } = UsageConfidence.Unavailable;
    public IReadOnlyList<UsageWindow> Windows { get; set; } = Array.Empty<UsageWindow>();
    public UsageCredits? Credits { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? HelpText { get; set; }

    public static UsageSnapshot Unavailable(string providerId, string message, string? helpText = null) => new()
    {
        ProviderId = providerId,
        Confidence = UsageConfidence.Unavailable,
        Source = "provider_capability",
        Message = message,
        HelpText = helpText
    };
}

/// <summary>
/// Converts only provider-owned, machine-readable quota payloads. It never
/// estimates product quota from local turns, token counts or API rate headers.
/// </summary>
internal static class UsageSnapshotParser
{
    public static UsageSnapshot FromCodex(JToken payload)
    {
        var response = payload as JObject ?? new JObject();
        var byLimit = response["rateLimitsByLimitId"] as JObject;
        var rateLimits = byLimit?.Property("codex", StringComparison.OrdinalIgnoreCase)?.Value
            ?? response["rateLimits"]
            ?? response["rate_limits"]
            ?? (response["primary"] != null || response["secondary"] != null ? response : null);
        if (rateLimits is not JObject snapshot)
            return UsageSnapshot.Unavailable("codex", "Codex không trả dữ liệu hạn mức có cấu trúc.", "Mở trang Usage của tài khoản Codex để kiểm tra.");

        var windows = new List<UsageWindow>();
        AddCodexWindow(windows, snapshot["primary"] as JObject, "Cửa sổ chính");
        AddCodexWindow(windows, snapshot["secondary"] as JObject, "Cửa sổ phụ");
        var credits = ParseCredits(snapshot["credits"] as JObject);
        if (windows.Count == 0 && credits == null)
            return UsageSnapshot.Unavailable("codex", "Codex chưa cung cấp cửa sổ hạn mức cho tài khoản này.", "Mở trang Usage của tài khoản Codex để kiểm tra.");

        var confidence = windows.Count > 0 && windows.All(window => window.Confidence == UsageConfidence.Exact)
            ? UsageConfidence.Exact
            : UsageConfidence.Partial;
        return new UsageSnapshot
        {
            ProviderId = "codex",
            Plan = snapshot.Value<string>("planType") ?? snapshot.Value<string>("plan_type"),
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Confidence = confidence,
            Windows = windows,
            Credits = credits,
            Source = "codex_app_server:account/rateLimits/read",
            Message = confidence == UsageConfidence.Exact ? "Dữ liệu trực tiếp từ Codex app-server." : "Codex chỉ trả một phần dữ liệu hạn mức."
        };
    }

    private static void AddCodexWindow(ICollection<UsageWindow> target, JObject? source, string fallbackLabel)
    {
        if (source == null) return;
        var usedPercent = Number(source["usedPercent"] ?? source["used_percent"]);
        if (!usedPercent.HasValue) return;
        var windowMinutes = Integer(source["windowDurationMins"] ?? source["window_minutes"]);
        var resetSeconds = Integer(source["resetsAt"] ?? source["resets_at"]);
        var kind = windowMinutes switch
        {
            >= 280 and <= 320 => UsageWindowKind.Session5Hours,
            >= 10000 and <= 10200 => UsageWindowKind.Weekly,
            _ => UsageWindowKind.ProviderSpecific
        };
        DateTimeOffset? resetAt = null;
        if (resetSeconds.HasValue)
        {
            try { resetAt = DateTimeOffset.FromUnixTimeSeconds(resetSeconds.Value); }
            catch (ArgumentOutOfRangeException) { }
        }
        target.Add(new UsageWindow
        {
            Kind = kind,
            Label = kind == UsageWindowKind.Session5Hours ? "Phiên 5 giờ" : kind == UsageWindowKind.Weekly ? "Tuần" : fallbackLabel,
            UsedPercent = Math.Max(0, Math.Min(100, usedPercent.Value)),
            Used = Math.Max(0, Math.Min(100, usedPercent.Value)),
            Remaining = Math.Max(0, 100 - Math.Max(0, Math.Min(100, usedPercent.Value))),
            Limit = 100,
            ResetAtUtc = resetAt,
            WindowMinutes = windowMinutes,
            Confidence = windowMinutes.HasValue && resetAt.HasValue ? UsageConfidence.Exact : UsageConfidence.Partial
        });
    }

    private static UsageCredits? ParseCredits(JObject? source)
    {
        if (source == null) return null;
        return new UsageCredits
        {
            HasCredits = source["hasCredits"]?.Value<bool?>() ?? source["has_credits"]?.Value<bool?>(),
            Unlimited = source["unlimited"]?.Value<bool?>(),
            Balance = source.Value<string>("balance")
        };
    }

    private static double? Number(JToken? token) => token?.Type switch
    {
        JTokenType.Integer => token.Value<double>(),
        JTokenType.Float => token.Value<double>(),
        _ => null
    };

    private static long? Integer(JToken? token) => token?.Type == JTokenType.Integer ? token.Value<long>() : null;
}

internal sealed class ChatProviderStatus
{
    public string Account { get; set; } = "Chưa kiểm tra";
    public string Provider { get; set; } = "Chưa kết nối";
    public string Mcp { get; set; } = "Chưa kiểm tra";
    public string Availability { get; set; } = "Chưa kiểm tra";
}

internal sealed class ChatModelOption
{
    public string Model { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public override string ToString() => DisplayName;
}

internal sealed class ChatProviderOption
{
    public string ProviderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}

internal static class EmbeddedChatProviderFactory
{
    public static IReadOnlyList<ChatProviderOption> Options { get; } = new[]
    {
        new ChatProviderOption { ProviderId = "codex", DisplayName = "Codex" },
        new ChatProviderOption { ProviderId = "claude", DisplayName = "Claude" },
        new ChatProviderOption { ProviderId = "antigravity", DisplayName = "Antigravity" }
    };

    public static IEmbeddedChatProvider Create(string providerId, EmbeddedChatConfig config, EmbeddedApprovalServer approval) => providerId switch
    {
        "codex" => new CodexAppServerClient(config, approval),
        "claude" => new ClaudeCliChatProvider(config, approval),
        "antigravity" => new AntigravityCliChatProvider(config, approval),
        _ => throw new ArgumentException("Unknown embedded chat provider.", nameof(providerId))
    };
}
#endif
