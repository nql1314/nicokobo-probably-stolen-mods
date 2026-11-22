"""Package the approved ImageGen originals on the game's 16-pixel inventory grid.

Uses deterministic alpha cropping and nearest-neighbor resizing.
Artwork and prompts live in versioned folders under Assets/IconSources; never paint generated
Textures/Items files directly. Optional keys export only completed selections.
"""
import json
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2] / "mods/food-processing"


def sprite(item, source_path):
    with Image.open(source_path) as source:
        if "A" not in source.getbands():
            raise ValueError(f"ImageGen original has no transparency: {source_path}")
        if "crop" in item:
            source = source.crop(item["crop"])
        source = source.convert("RGBA")
    alpha = source.getchannel("A")
    if alpha.getextrema()[0] != 0:
        raise ValueError(f"ImageGen original has an opaque background: {source_path}")
    bounds = alpha.point(lambda value: 255 if value >= 112 else 0).getbbox()
    if bounds is None:
        raise ValueError(f"Empty ImageGen source: {source_path}")
    source = source.crop(bounds)
    width, height = item["width"] * 16, item["height"] * 16
    scale = min((width - 2) / source.width, (height - 2) / source.height)
    size = (max(1, min(width - 2, round(source.width * scale))),
            max(1, min(height - 2, round(source.height * scale))))
    art = source.resize(size, Image.Resampling.NEAREST)
    art.putalpha(art.getchannel("A").point(lambda value: 255 if value >= 112 else 0))
    bounds = art.getchannel("A").getbbox()
    if bounds is None:
        raise ValueError(f"Sprite lost its silhouette: {source_path}")
    art = art.crop(bounds)
    result = Image.new("RGBA", (width, height))
    result.alpha_composite(art, ((width - art.width) // 2, (height - art.height) // 2))
    validate(item, result)
    return result


def validate(item, image):
    expected = (item["width"] * 16, item["height"] * 16)
    if image.mode != "RGBA" or image.size != expected:
        raise ValueError(f"Sprite size/mode mismatch: {item['key']}; expected={expected}")
    alpha = image.getchannel("A")
    bounds = alpha.getbbox()
    if not bounds or set(alpha.getdata()) - {0, 255}:
        raise ValueError(f"Sprite alpha is not binary: {item['key']}")
    left, top, right, bottom = bounds
    margins = (left, top, image.width - right, image.height - bottom)
    if min(margins) < 1 or abs(margins[0] - margins[2]) > 1 or abs(margins[1] - margins[3]) > 1:
        raise ValueError(f"Sprite lacks a centered transparent border: {item['key']}")


# Later source catalogs can replace a sprite while preserving the earlier artwork.
manifest = {}
for path in sorted((ROOT / 'Assets/IconSources').glob('*/prompts.json')):
    catalog = json.loads(path.read_text(encoding='utf-8'))
    for item in catalog if isinstance(catalog, list) else catalog['sprites']:
        manifest[item['key']] = item
selected = set(sys.argv[1:])
for item in manifest.values():
    if selected and item['key'] not in selected:
        continue
    source = ROOT / item['source']
    image = sprite(item, source)
    destination = ROOT / 'Textures/Items' / (item['key'] + '.png')
    image.save(destination, optimize=True)
    print(f"{item['key']}: {image.width}x{image.height}")
