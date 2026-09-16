"""Static guardrails for V2 BIM-production MCP commands; no Revit required."""
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TOOLS = (ROOT / "MCP-Server" / "src" / "tools" / "bim-tools.ts").read_text(encoding="utf-8")
REGISTRY = (ROOT / "MCP" / "Core" / "CommandRegistry.cs").read_text(encoding="utf-8")
COMMANDS = (ROOT / "MCP" / "Commands" / "BimV2Commands.cs").read_text(encoding="utf-8")
WRITE = (ROOT / "MCP" / "Commands" / "MepWriteCommands.cs").read_text(encoding="utf-8")
READ = (ROOT / "MCP" / "Commands" / "ReadCommands.cs").read_text(encoding="utf-8")
CONNECTORS = (ROOT / "MCP" / "Commands" / "MepData.cs").read_text(encoding="utf-8")
SAFETY = (ROOT / "MCP" / "Commands" / "MepSafety.cs").read_text(encoding="utf-8")
RUNTIME = (ROOT / "MCP.CoreRuntime" / "RevitMcpCoreRuntime.cs").read_text(encoding="utf-8")
RUNTIME_PROJECT = (ROOT / "MCP.CoreRuntime" / "DSCons.RevitMcp.CoreRuntime.csproj").read_text(encoding="utf-8")
BUILD_SCRIPT = (ROOT / "scripts" / "build-mcp.ps1").read_text(encoding="utf-8")
HOT_RELOAD_SCRIPT = (ROOT / "scripts" / "publish-hot-reload.ps1").read_text(encoding="utf-8")
ANTIGRAVITY_SCRIPT = (ROOT / "scripts" / "configure-antigravity.ps1").read_text(encoding="utf-8")

EXPECTED = [
    "bim_context_snapshot", "bim_model_catalog", "mep_network_explore", "coordination_links",
    "coordination_scan", "quantity_takeoff", "documentation_plan",
    "model_create_batch", "bim_changeset_preview", "bim_changeset_apply",
    "documentation_apply",
]
for name in EXPECTED:
    assert f'"{name}"' in TOOLS, f"missing MCP schema: {name}"
    assert f'"{name}"' in REGISTRY, f"missing Revit registry entry: {name}"

for required in ["BimContextStore.Require", "TransactionGroup", "CreateBatch", "ExecuteChangeSet", "ApplyDocumentation"]:
    assert required in COMMANDS or required in WRITE, f"missing V2 safety/implementation: {required}"

for write_tool in ["model_create_batch", "bim_changeset_preview", "documentation_apply"]:
    start = TOOLS.index(f'"{write_tool}"')
    window = TOOLS[start:start + 1200]
    assert '"context_id"' in window and "required:" in window, f"{write_tool} must require fresh context"

assert "bounding-box evidence" in COMMANDS
assert "never saves or synchronizes" in WRITE
assert "CamelCasePropertyNamesContractResolver" in READ
assert '"connector_type"' in CONNECTORS
assert "Autodesk.Revit.Exceptions.InvalidOperationException" in CONNECTORS, "logical connector reads must catch Revit's exception type"
assert "<Reference Include=\"Newtonsoft.Json\"><HintPath>$(NewtonsoftPath)</HintPath><Private>false</Private></Reference>" in RUNTIME_PROJECT
assert "PackageReference Include=\"Newtonsoft.Json\"" not in RUNTIME_PROJECT
assert "Newtonsoft.Json.dll" not in BUILD_SCRIPT, "packaging must not ship a competing Newtonsoft assembly"
assert "$obsoleteNewtonsoft" in HOT_RELOAD_SCRIPT, "hot reload must remove legacy Newtonsoft assemblies"
assert "[switch]$Check" in ANTIGRAVITY_SCRIPT, "Antigravity config needs a read-only validation mode"
assert "UTF8Encoding]::new($false)" in ANTIGRAVITY_SCRIPT, "Antigravity config must be written without a BOM"
assert "ConvertFrom-Json -AsHashtable" not in ANTIGRAVITY_SCRIPT, "script must support Windows PowerShell 5.1"
assert 'args["view_ids"]' in COMMANDS and 'sheet["placements"]' in COMMANDS and 'placement.Value<long>("view_id")' in COMMANDS, "documentation_plan must validate top-level and per-sheet placement view_ids"
assert 'detail["sheet_number"] = sheet.SheetNumber' in CONNECTORS, "documentation read-back must expose the actual Sheet Number"
assert "RBS_PIPE_DIAMETER_PARAM" in WRITE and "VerificationFailed" in WRITE, "Pipe resizing must use the Pipe parameter and verify read-back"
assert "DocumentRevisionTracker.Revision" in SAFETY and "DocumentChanged" in RUNTIME, "context fingerprints must track every document revision, not only IsModified"
assert WRITE.count("DocumentRevisionTracker.Suppress()") >= 1, "rolled-back MEP preview must not invalidate its own context"
assert '"created_schedule_ids"' in WRITE and "DocumentationScheduleSupport" in WRITE
assert '"mep_family_symbols"' in COMMANDS
print(f"PASS V2 static contract: {len(EXPECTED)} tools, context, atomic changeset, coordination, takeoff, documentation")
