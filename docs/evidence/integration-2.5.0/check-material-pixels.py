"""Recompute evidence ROIs: python check-material-pixels.py [capture-directory].

Uses Pillow for offline JPEG decoding only; no application dependency.
Coordinates refer to the unedited 1100 x 780 Windows.Graphics.Capture files.
"""
from pathlib import Path
import hashlib
import json
import sys
from PIL import Image

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent
regions = {
    "settingsLabel": (212, 166, 330, 185),
    "songLabel": (260, 492, 350, 511),
    "glassLabel": (260, 516, 340, 531),
    "swatch": (216, 490, 250, 525),
}
rows = []
for path in sorted(root.glob("*-wgc.jpg")):
    with Image.open(path) as source:
        image = source.convert("RGB")
        assert image.size == (1100, 780), (path, image.size)
        counts = {
            name: sum(min(pixel) > 210 for pixel in image.crop(bounds).getdata())
            for name, bounds in regions.items()
        }
        assert all(count > 80 for count in counts.values()), (path, counts)
        rows.append({"file": path.name,
                     "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                     "brightPixels": counts})
assert len(rows) == 8, f"Expected eight material captures, found {len(rows)}"
print(json.dumps(rows, indent=2))
