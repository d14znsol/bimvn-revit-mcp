from pathlib import Path

root = Path(__file__).resolve().parents[1]


def read(relative: str) -> str:
    return (root / relative).read_text(encoding="utf-8")


ribbon = read("MCP/Core/McpRibbon.cs")
app = read("MCP/Application.cs")
client = read("MCP/Core/EmbeddedChat/CodexAppServerClient.cs")
pane = read("MCP/Core/EmbeddedChat/EmbeddedChatPane.cs")
persistence = read("MCP/Core/EmbeddedChat/ChatPersistence.cs")
approval = read("MCP/Core/EmbeddedChat/EmbeddedApprovalServer.cs")
provider = read("MCP/Core/EmbeddedChat/EmbeddedChatProvider.cs")
claude = read("MCP/Core/EmbeddedChat/ClaudeCliChatProvider.cs")
antigravity = read("MCP/Core/EmbeddedChat/AntigravityCliChatProvider.cs")
ndjson = read("MCP/Core/EmbeddedChat/NdjsonCliChatProvider.cs")
config = read("MCP/Core/EmbeddedChat/EmbeddedChatConfig.cs")
manifest = read("MCP-Server/src/embedded-capabilities.ts")
node_approval = read("MCP-Server/src/embedded-approval.ts")
socket = read("MCP-Server/src/socket.ts")
installer = read("scripts/install-mcp.ps1")

for marker in ['#if REVIT2023 || REVIT2025', 'PushButtonData("DSConsMcpChat"', 'EmbeddedChatPaneHost.Current', 'McpRibbonImages.Chat()']:
    assert marker in ribbon, f"missing Revit 2023/2025 Chat Ribbon contract: {marker}"
assert "EmbeddedChatPaneHost.Register(application)" in app
assert "_chatPane?.Dispose()" in app
assert 'Text = "BETA · R23/25"' in pane

for source in [client, pane, persistence, approval, provider, claude, antigravity, ndjson, config]:
    assert source.startswith('#if REVIT2023 || REVIT2025'), "embedded Chat source must target both Revit 2023 and 2025"

for marker in [
    'interface IEmbeddedChatProvider', 'ProviderCapabilities', 'ChatProviderStatus', 'ChatModelOption',
    'CodexAppServerClient', 'ClaudeCliChatProvider', 'AntigravityCliChatProvider',
    'ProviderId = "codex"', 'ProviderId = "claude"', 'ProviderId = "antigravity"',
    'UsageSnapshot', 'UsageWindow', 'UsageConfidence', 'RefreshUsageAsync', 'UsageChanged'
]:
    assert marker in provider, marker

for marker in [
    '"app-server"', 'thread/start', 'thread/resume', 'thread/read', 'ephemeral = false',
    'turn/interrupt', 'mcpServerStatus/list', 'ToolNames(embedded)', 'SetEquals(observed)',
    'schemaFingerprintSha256', '["shell_tool"] = false', '["web_search"] = "disabled"',
    '["apps"] = false', '["plugins"] = false', '["multi_agent"] = false',
    '["default_tools_approval_mode"] = "approve"', 'DSCONS_MCP_EXPECTED_REVIT_PID',
    'model/list', 'ModelsChanged', 'StartTurnAsync(string text, string? model)',
    'Unexpected server request is blocked', 'ChatDiagnosticLog'
]:
    assert marker in client, marker

assert "ExpectedToolCount" not in client
assert "expectedToolCount" not in config
assert "expectedToolCount" not in installer
for marker in ['EmbeddedCapabilityManifest', 'CapabilityManifestPath', 'SchemaFingerprintSha256', 'canonicalPayload', 'SHA256']:
    assert marker in config, marker
for marker in ['createEmbeddedCapabilityManifest', 'canonicalJson', 'schemaFingerprintSha256', 'isWrite', 'inputSchema']:
    assert marker in manifest, marker

for marker in [
    'MinimumClaudeCliVersion = "2.1.259"', '--input-format stream-json', '--output-format stream-json',
    '--bare', '--strict-mcp-config', '--tools \\"\\"', '--restricted', '--permission-prompts none',
    'không tự nâng cấp', 'không dùng chế độ yếu hơn', 'claude auth login'
]:
    assert marker in claude, marker

for marker in [
    'CreateIsolatedWorkspace', '--input-format stream-json', '--output-format stream-json', '--sandbox',
    'init.tools', 'RequireExactDsconsTools', 'UnsafeBuiltInTools', 'run_command', 'write_to_file',
    'search_web', 'start_subagent', 'chưa hỗ trợ an toàn'
]:
    assert marker in antigravity, marker
assert '--dangerously-skip-permissions' not in antigravity

for marker in [
    'JObject.Parse(line)', 'NDJSON không hợp lệ', 'Task.WhenAny', 'WriteJsonAsync',
    'BuildUserTurn', 'conversation_id', 'UpdateProviderConversation', 'Error?.Invoke',
    'CreateDsconsOnlyMcpConfig', 'DSCONS_EMBEDDED_APPROVAL_SECRET'
]:
    assert marker in ndjson, marker

for marker in [
    'SchemaVersion { get; set; } = 2', 'ProviderId', 'ProviderConversationId',
    'MigrateV1ToV2Locked', 'ActiveKey(context, providerId)', 'NormalizeProviderId',
    'credential', 'tool_args', 'element_id', 'preview_id', 'process id'
]:
    assert marker in persistence, marker

for marker in [
    'ChatProviderOption', 'EmbeddedChatProviderFactory', '_provider', 'SwitchProviderAsync',
    'DisposeProviderOnly', 'Trợ lý AI', 'ApplyResponsiveLayout', 'width >= 620',
    'width >= 390', 'ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled',
    'TryParseApprovalDecision', 'ShowPendingApproval', 'DispatcherTimer'
]:
    assert marker in pane, marker
for marker in ['HẠN MỨC', 'PHIÊN HIỆN TẠI', 'TUẦN', 'CREDITS', 'ApplyUsage', 'FormatUsageWindow', 'Không khả dụng', '_usageGrid.Columns = width >= 390 ? 3 : 1']:
    assert marker in pane, marker

for marker in ['NamedPipeServerStream', 'FixedEquals', 'ExpectedPid', '_usedRequestIds', 'PendingPrompt']:
    assert marker in approval, marker
for marker in ['DSCONS_EMBEDDED_CHAT', 'approvalHash', 'ApprovalDenied', 'WRITE_TOOLS']:
    assert marker in node_approval, marker
assert "Revit PID mismatch" in socket

for marker in ['schemaVersion = 2', 'capabilityManifestPath', 'claudeCliPath', 'antigravityCliPath', "@('2023','2025')"]:
    assert marker in installer, marker
assert "0.154.0-alpha.6.2" in config
assert "0.154.0-alpha.6.2" in installer

print("embedded chat static contract PASS")
