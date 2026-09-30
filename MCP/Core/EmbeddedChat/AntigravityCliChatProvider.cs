#if REVIT2023 || REVIT2025
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace DSCons.RevitMcp.Core.EmbeddedChat;

internal sealed class AntigravityCliChatProvider : NdjsonCliChatProvider
{
    private static readonly HashSet<string> UnsafeBuiltInTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "run_command", "write_to_file", "search_web", "read_url_content", "start_subagent", "generate_image", "ask_permission", "shell", "web", "subagent"
    };
    private string? _executable;
    private string? _workspace;
    private string? _mcpConfig;
    private TaskCompletionSource<bool>? _initTools;
    public AntigravityCliChatProvider(EmbeddedChatConfig config, EmbeddedApprovalServer approval) : base(config, approval) { }
    public override string ProviderId => "antigravity";
    public override string DisplayName => "Antigravity";
    public override ProviderCapabilities Capabilities { get; } = new() { ProviderId = "antigravity", SupportsStreaming = true, SupportsConversationResume = true, SupportsCachedCliLogin = true, RequiresStrictMcpIsolation = true, SupportsUsageRefresh = true, SupportsMachineReadableUsage = false, IsExperimental = true };
    protected override string UsageUnavailableMessage => "Antigravity CLI chưa expose quota headless/JSON đã được kiểm chứng cho panel.";
    protected override string UsageUnavailableHelp => "Mở Antigravity và dùng /usage hoặc /quota để xem hạn mức của gói hiện tại.";

    protected override async Task PreflightAsync()
    {
        _executable ??= FindExecutable(Config.AntigravityCliPath, "ANTIGRAVITY_CLI_PATH", "agy.exe", "agy.cmd", "agy");
        if (_executable == null) throw new InvalidOperationException("Chưa tìm thấy Antigravity CLI đã đăng nhập.");
        var help = await ReadCliHelpAsync(_executable).ConfigureAwait(false);
        RequireCliHelpOptions(help, "Antigravity", "--input-format", "--output-format", "--mcp-config", "--sandbox");
        _workspace ??= CreateIsolatedWorkspace(ProviderId); _mcpConfig ??= CreateDsconsOnlyMcpConfig(_workspace);
        _initTools = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // StartProcessAsync is invoked by Connect immediately after this method.
        await Task.CompletedTask;
    }

    protected override ProcessStartInfo CreateProviderStartInfo()
    {
        var resume = CurrentConversation?.ProviderConversationId;
        var args = "--input-format stream-json --output-format stream-json --sandbox --mcp-config " + Quote(_mcpConfig!)
            + (string.IsNullOrWhiteSpace(resume) ? string.Empty : " --conversation " + Quote(resume!));
        return NewProcessStartInfo(_executable!, args, _workspace);
    }

    protected override JObject BuildUserTurn(string text, string? model, string? providerConversationId) => new()
    {
        // Antigravity stream-json accepts only an explicit `user` event with a
        // text content block. The long-running process itself retains context.
        ["event"] = "user",
        ["message"] = new JObject { ["content"] = text }
    };

    public override Task InterruptAsync()
    {
        // The documented stdin protocol has no interrupt event. Writing an
        // invented message would make the CLI reject the stream, so stop the
        // isolated child process and leave the provider ready to reconnect.
        AbortCurrentTurn("Đã dừng lượt Antigravity trong workspace cô lập.");
        return Task.CompletedTask;
    }

    public override async Task ConnectAsync()
    {
        await base.ConnectAsync().ConfigureAwait(false);
        var ready = _initTools ?? throw new InvalidOperationException("Antigravity không trả init.tools; chưa hỗ trợ an toàn.");
        await AwaitWithTimeoutAsync(ready.Task, TimeSpan.FromSeconds(15), "Antigravity CLI không trả init.tools; provider chưa hỗ trợ an toàn.").ConfigureAwait(false);
    }

    protected override void HandleProviderEvent(JObject message)
    {
        var type = message.Value<string>("type") ?? message.Value<string>("event") ?? string.Empty;
        if (string.Equals(type, "init", StringComparison.OrdinalIgnoreCase) || message["init"] is JObject)
        {
            var inventory = message["tools"] as JArray ?? message.SelectToken("init.tools") as JArray;
            try
            {
                var names = inventory?.Select(item => item.Type == JTokenType.String ? item.Value<string>() : item["name"]?.Value<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!).ToArray()
                    ?? throw new InvalidOperationException("Antigravity init.tools bị thiếu.");
                var servers = inventory?.OfType<JObject>().Select(item => item.Value<string>("server") ?? item.Value<string>("server_name") ?? item.Value<string>("mcp_server"))
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!).Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
                if (names.Any(name => UnsafeBuiltInTools.Contains(name))) throw new InvalidOperationException("Antigravity init.tools có capability bị cấm: " + string.Join(", ", names.Where(name => UnsafeBuiltInTools.Contains(name))) + ".");
                if (servers.Any(name => !string.Equals(name, "dscons_embedded", StringComparison.Ordinal))) throw new InvalidOperationException("Antigravity init.tools báo MCP ngoài DSCons: " + string.Join(", ", servers.Where(name => !string.Equals(name, "dscons_embedded", StringComparison.Ordinal))) + ".");
                var permissionMode = message.SelectToken("init.permission_mode")?.Value<string>();
                if (string.Equals(permissionMode, "always-proceed", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Antigravity init.tools báo permission mode always-proceed; provider bị khóa an toàn.");
                RequireExactDsconsTools(names, "Antigravity init.tools"); _initTools?.TrySetResult(true);
                UpdateProviderConversation(message.Value<string>("conversation_id") ?? message.SelectToken("init.conversation_id")?.Value<string>());
            }
            catch (Exception exception) { _initTools?.TrySetException(exception); throw; }
            return;
        }
        base.HandleProviderEvent(message);
    }

    public override Task StartLoginAsync()
    {
        ReportProgress("Antigravity dùng tài khoản CLI hiện có. Hãy đăng nhập theo hướng dẫn chính thức ngoài Revit; panel không dùng API key và không bỏ qua permission checks.");
        return Task.CompletedTask;
    }
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
#endif
