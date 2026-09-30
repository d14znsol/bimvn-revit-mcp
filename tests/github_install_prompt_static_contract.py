"""Guard the learner's one-prompt GitHub bootstrap contract."""
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
START = (ROOT / "START_HERE.md").read_text(encoding="utf-8")
README = (ROOT / "README.md").read_text(encoding="utf-8")
GUIDE = (ROOT / "docs" / "learning" / "LEARNER_GUIDE.md").read_text(encoding="utf-8")
EXPORT = (ROOT / "scripts" / "export-public.ps1").read_text(encoding="utf-8")
CI = (ROOT / ".github" / "workflows" / "source-ci.yml").read_text(encoding="utf-8")


required_start_markers = (
    "https://github.com/huy17012001huy-cyber/dscons-revit-mcp.git",
    "Git.Git",
    "OpenJS.NodeJS.LTS",
    "Microsoft.DotNet.SDK.10",
    "--accept-source-agreements",
    "--accept-package-agreements",
    "npm.cmd --version",
    "C:\\Program Files\\Git\\cmd",
    "C:\\Program Files\\nodejs",
    "npm.cmd ci --prefix .\\MCP-Server",
    "build-mcp.ps1 -RevitVersion <NĂM_REVIT>",
    "student-setup.ps1 -Mode Check",
    "student-setup.ps1 -Mode Install",
    "tools/list",
    "capability manifest",
    "không cài npm bằng một package hoặc script ngẫu nhiên riêng",
    "không tải installer từ website trung gian",
    "không dùng kiểu `curl | iex`",
    "Đã đóng Revit; xác nhận cài",
)
missing = [marker for marker in required_start_markers if marker not in START]
if missing:
    raise AssertionError(f"START_HERE GitHub bootstrap contract is incomplete: {missing}")

if START.count("```text") != 1:
    raise AssertionError("START_HERE must contain exactly one learner prompt")
if "winget" not in README or "START_HERE.md" not in README:
    raise AssertionError("README must route learners to the automatic prerequisite prompt")
if not all(package in GUIDE for package in ("Git.Git", "OpenJS.NodeJS.LTS", "Microsoft.DotNet.SDK.10")):
    raise AssertionError("Learner guide must document all official prerequisite package IDs")
if "tests\\github_install_prompt_static_contract.py" not in EXPORT:
    raise AssertionError("Public export must include the GitHub install prompt regression")
if "github_install_prompt_static_contract.py" not in CI:
    raise AssertionError("Source CI must run the GitHub install prompt regression")

print("PASS GitHub AI install prompt contract: pre-authorized official prerequisites, safe clone/build/install checkpoints")
