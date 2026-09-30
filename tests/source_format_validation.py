"""Parse distributable source formats and reject broken local Markdown links."""
import json
import os
import re
import xml.etree.ElementTree as etree
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKIP = {"node_modules", "bin", "obj", "build", "artifacts", ".DSCons", "inputs", "runtime", "audit", "tmp"}


def skipped(relative: Path) -> bool:
    """Exclude non-distributable build and fixture scratch directories.

    The server's atomic build can leave a recoverable `.build-stage-*` or
    `.build-backup-*` directory after an interrupted local build. These, like
    `tmp`, are not source and are excluded by the public exporter; validating
    their partial JSON would make the source-only CI depend on local residue.
    """
    return any(
        part in SKIP or part.startswith(".build-stage-") or part.startswith(".build-backup-")
        for part in relative.parts
    )


def source_files(suffixes):
    # Prune excluded trees during traversal. `Path.rglob` still walks every
    # package below node_modules before `skipped` can reject it, which turns a
    # quick source-format check into a machine-dependent timeout.
    for directory, names, files in os.walk(ROOT):
        parent = Path(directory)
        relative_parent = parent.relative_to(ROOT)
        names[:] = [name for name in names if not skipped(relative_parent / name)]
        for name in files:
            item = parent / name
            if item.suffix.lower() in suffixes and not skipped(item.relative_to(ROOT)):
                yield item


for file in source_files({".json"}):
    json.loads(file.read_text(encoding="utf-8"))
for file in source_files({".xml", ".addin"}):
    etree.fromstring(file.read_bytes())

link_pattern = re.compile(r"(?<!!)\[[^\]]*\]\(([^)\s]+)(?:\s+[^)]*)?\)")
broken = []
for file in source_files({".md"}):
    text = file.read_text(encoding="utf-8")
    for target in link_pattern.findall(text):
        target = target.strip("<>")
        if not target or target.startswith(("#", "http:", "https:", "mailto:", "file:")):
            continue
        local = target.split("#", 1)[0].replace("%20", " ")
        if local and not (file.parent / local).resolve().exists():
            broken.append(f"{file.relative_to(ROOT)} -> {target}")
if broken:
    raise AssertionError("Broken local Markdown links:\n" + "\n".join(broken))

print("PASS source format validation: JSON/XML/addin parse and local Markdown links")
