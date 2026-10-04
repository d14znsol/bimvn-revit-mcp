"""Offline guardrails for the Combine & Shop Copilot v1 boundary."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOOLS = (ROOT / "MCP-Server/src/tools/combine-tools.ts").read_text(encoding="utf-8")
REGISTRY = (ROOT / "MCP/Core/CommandRegistry.cs").read_text(encoding="utf-8")
COMMANDS = (ROOT / "MCP/Commands/CombineCommands.cs").read_text(encoding="utf-8")
INDEX = (ROOT / "MCP-Server/src/index.ts").read_text(encoding="utf-8")
KNOWLEDGE = (ROOT / "MCP-Server/src/knowledge-search.ts").read_text(encoding="utf-8")

TOOLS_EXPECTED = [
    "dscons_knowledge_search", "coordination_solid_scan", "coordination_issue_report_preview",
    "coordination_issue_report_apply", "coordination_section_preview", "coordination_section_apply",
]
REVIT_TOOLS = TOOLS_EXPECTED[1:]
for tool in TOOLS_EXPECTED:
    assert f'"{tool}"' in TOOLS, f"missing Node schema: {tool}"
for tool in REVIT_TOOLS:
    assert f'"{tool}"' in REGISTRY, f"missing Revit registry entry: {tool}"
    assert tool in COMMANDS, f"missing Revit implementation: {tool}"

for marker in [
    "BooleanOperationsUtils.ExecuteBooleanOperation", "GeometryInstance", "SolidUtils.CreateTransformed",
    "clearance_triage", "TransactionGroup", "PreviewExpired", "PreviewInvalid",
    "File.Move", "UTF8Encoding(false)", "LinkFingerprint", "post_commit_read_back",
]:
    assert marker in COMMANDS, f"missing geometry/safety marker: {marker}"
assert "SyncWithCentral" not in COMMANDS
assert ".Save(" not in COMMANDS
assert "XÁC NHẬN TẠO MẶT CẮT COMBINE" in TOOLS
assert "dscons_knowledge_search" in INDEX and "searchKnowledge" in INDEX
assert "local_private_filesystem" in INDEX and "addNodeLocalCapabilities" in INDEX

for marker in ["TỔNG HỢP KHÓA HỌC.md", "_summary.md", "sha256", "MAX_EXCERPT", "isBlockedPath", "realpath"]:
    assert marker in KNOWLEDGE, f"missing knowledge-search safety marker: {marker}"
assert "E:\\ANTIGRAVITY" not in KNOWLEDGE
assert "fetch(" not in KNOWLEDGE and "http://" not in KNOWLEDGE and "https://" not in KNOWLEDGE
print("PASS Combine static contract: 6 additive tools, local-private knowledge boundary, solid/section/report guards")
