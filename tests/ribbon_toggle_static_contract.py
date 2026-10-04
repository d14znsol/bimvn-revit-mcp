from pathlib import Path

root = Path(__file__).resolve().parents[1]
ribbon = (root / "MCP/Core/McpRibbon.cs").read_text(encoding="utf-8")
manager = (root / "MCP/Core/CoreRuntimeManager.cs").read_text(encoding="utf-8")
bridge = (root / "MCP/Core/BridgeServer.cs").read_text(encoding="utf-8")
images = (root / "MCP/Core/McpRibbonImages.cs").read_text(encoding="utf-8")
combined = "\n".join((ribbon, manager, images))

for marker in [
    'PushButtonData("DSConsMcpToggle"',
    'typeof(McpToggleCommand).FullName',
    '"Bật/Tắt\\nMCP"',
    'ToggleOnUiThread',
    'UpdateTogglePresentation',
    '"Tắt\\nMCP"',
    '"Bật\\nMCP"',
    'McpRibbonImages.Power(running)',
    '"Đã tắt MCP.\\n\\nMuốn dùng lại, hãy bấm Bật MCP."',
]:
    assert marker in combined, f"missing Ribbon toggle contract: {marker}"

for marker in [
    "_enabled = false",
    "StopRuntime();",
    "LoadRuntime(application);",
    "if (!_enabled) return false",
]:
    assert marker in manager, f"missing lifecycle/safety guard: {marker}"

for removed in ["DSConsMcpStatus", "McpStatusCommand", "Kiểm tra trạng thái MCP"]:
    assert removed not in ribbon, f"removed status button contract returned: {removed}"

assert "File.Delete(_sessionPath)" in bridge, "bridge stop must remove its owned session file"
for forbidden in ["Process.Kill", ".Kill()", "Process.Start", "node.exe"]:
    assert forbidden not in combined, f"Ribbon toggle must not own/kill Node: {forbidden}"

print("PASS Ribbon MCP toggle: bridge lifecycle, session cleanup, dynamic state, no Node process ownership")
