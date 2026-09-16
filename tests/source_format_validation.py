"""Parse distributable source formats and reject broken local Markdown links."""
import json
import re
import xml.etree.ElementTree as etree
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKIP = {"node_modules", "bin", "obj", "build", "artifacts", ".DSCons", "inputs", "runtime", "audit"}


def source_files(suffixes):
    for item in ROOT.rglob("*"):
        if not item.is_file() or item.suffix.lower() not in suffixes or any(part in SKIP for part in item.relative_to(ROOT).parts):
            continue
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
