"""Keep the offline Revit build wrapper deterministic and export-complete."""
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
wrapper = (ROOT / "scripts" / "build-mcp.ps1").read_text(encoding="utf-8")
exporter = (ROOT / "scripts" / "export-public.ps1").read_text(encoding="utf-8")
workflow = (ROOT / ".github" / "workflows" / "source-ci.yml").read_text(encoding="utf-8")

assert "$nodeExecutable" in wrapper
assert "'.\\scripts\\build-server.mjs'" in wrapper
assert "& npm.cmd run build" not in wrapper
assert "MCP-Server\\scripts" in exporter
assert "MCP-Server\\scripts\\build-server.mjs" in exporter
assert "tests\\build_wrapper_static_contract.py" in exporter
assert "node .\\MCP-Server\\scripts\\build-server.mjs" in workflow
assert "npm run build --prefix .\\MCP-Server" not in workflow
assert "Verify public source export boundary" in workflow
assert "export-public.ps1 -OutputRoot" in workflow

print("PASS build-wrapper static contract: direct Node build and public export boundary")
