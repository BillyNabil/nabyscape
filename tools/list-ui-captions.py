"""Read-only catalog inventory. Does not execute NabyScape or request translations.

Run: python tools/list-ui-captions.py
Lists explicit literal captions absent from the central English base catalog.
Dynamic captions must be reviewed by their owner; never extract arbitrary user text.
"""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "src" / "NabyScape"
literal = r'"(?:[^"\\]|\\.)*"'
catalog = (root / "UI" / "Shared" / "LocalizationCatalog.cs").read_text(encoding="utf-8")
keys = set(re.findall(r'\[(' + literal + r')\]\s*=', catalog))
pattern = re.compile(r'(?:Localization\.(?:Text|Format)|UI\.(?:Caption|CaptionButton))\((' + literal + r')(?=\s*[,\)])')
missing = {}
for path in root.rglob("*.cs"):
    if "Tests" in path.parts or "Diagnostics" in path.parts:
        continue
    for match in pattern.finditer(path.read_text(encoding="utf-8")):
        if match[1] != '""' and match[1] not in keys:
            missing.setdefault(match[1][1:-1], []).append(str(path.relative_to(root)))
print(json.dumps({"catalog_entries": len(keys), "english_fallback_captions": missing}, ensure_ascii=True, indent=2))
