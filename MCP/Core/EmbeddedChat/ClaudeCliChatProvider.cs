#if REVIT2023 || REVIT2025
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DSCons.RevitMcp.Core.EmbeddedChat;

internal sealed class ClaudeCliChatProvider : NdjsonCliChatProvider
{
    public const string MinimumClaudeCliVersion = "2.1.259";
    private string? _executable;
    private string? _workspace;
    private string? _mcpConfig;
    public ClaudeCliChatProvider(EmbeddedChatConfig config, EmbeddedApprovalServer approval) : base(config, approval) { }
    public override string ProviderId => "claude";
    public override string DisplayName => "Claude";
    public override ProviderCapabilities Capabilities { get; } = new() { ProviderId = "claude", SupportsStreaming = true, SupportsConversationResume = true, SupportsCachedCliLogin = true, RequiresStrictMcpIsolation = true, SupportsUsageRefresh = true, SupportsMachineReadableUsage = false, IsExperimental = true };
    protected override string UsageUnavailableMessage => "Claude Code CLI chưa expose quota machine-readable đã được kiểm chứng cho panel.";
    protected override string UsageUnavailableHelp => "Mở Settings → Usage của Claude để xem phiên 5 giờ và giới hạn tuần.";

    protected override async Task PreflightAsync()
    {
        _executable ??= FindExecutable(Config.ClaudeCliPath, "CLAUDE_CLI_PATH", "claude.exe", "claude.cmd", "claude");
        if (_executable == null) throw new InvalidOperationException("Chưa tìm thấy Claude Code CLI. Hãy đăng nhập bằng lệnh chính thức `claude auth login` ngoài Revit.");
        var installed = await ReadCliVersionAsync(_executable).ConfigureAwait(false);
        if (!IsAtLeast(installed, MinimumClaudeCliVersion)) throw new InvalidOperationException("Claude Code " + installed + " cần nâng cấp lên tối thiểu " + MinimumClaudeCliVersion + " để dùng restricted mode an toàn. Panel không tự nâng cấp và không dùng chế độ yếu hơn.");
        var help = await ReadCliHelpAsync(_executable).ConfigureAwait(false);
        RequireCliHelpOptions(help, "Claude Code", "--input-format", "--output-format", "--strict-mcp-config", "--restricted", "--permission-prompts");
        _workspace ??= CreateIsolatedWorkspace(ProviderId); _mcpConfig ??= CreateDsconsOnlyMcpConfig(_workspace);
    }

    protected override ProcessStartInfo CreateProviderStartInfo()
    {
        // The version floor above is required because this policy depends on
        // restricted mode and unattended denial of permission prompts.
        var args = "-p --input-format stream-json --output-format stream-json --bare --strict-mcp-config --mcp-config " + Quote(_mcpConfig!) + " --tools \"\" --restricted --permission-prompts none";
        return NewProcessStartInfo(_executable!, args, _workspace);
    }

    public override Task StartLoginAsync()
    {
        ReportProgress("Claude dùng tài khoản Claude Code CLI hiện có. Hãy chạy `claude auth login` trong terminal ngoài Revit; panel không đọc hay lưu API key.");
        return Task.CompletedTask;
    }

    private static bool IsAtLeast(string installed, string minimum)
    {
        static int[] Parts(string value) => Regex.Match(value, @"(\d+)\.(\d+)\.(\d+)").Groups.Cast<Group>().Skip(1).Select(group => int.Parse(group.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var left = Parts(installed); var right = Parts(minimum);
        if (left.Length != 3) return false;
        for (var index = 0; index < 3; index++) { if (left[index] != right[index]) return left[index] > right[index]; }
        return true;
    }
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
#endif
