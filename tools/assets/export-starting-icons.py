"""Export the editable integer-grid SVG stencils used by the new-game menu."""
from pathlib import Path
import argparse
import json
import xml.etree.ElementTree as ET
from PIL import Image, ImageColor, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "assets" / "starting-icons"


def export(entry, profile):
    source = ET.parse(SOURCE / (entry["id"] + ".svg")).getroot()
    size = profile["size"]
    if source.attrib.get("viewBox") != f"0 0 {size} {size}" or source.attrib.get("fill") != profile["ink"]:
        raise ValueError(f"Wrong canvas or ink for {entry['id']}")
    image = Image.new("RGBA", (size, size))
    draw = ImageDraw.Draw(image)
    ink = ImageColor.getrgb(profile["ink"]) + (255,)
    for element in source:
        tag = element.tag.rsplit("}", 1)[-1]
        if tag in ("title", "desc"):
            continue
        if tag != "rect" or set(element.attrib) - {"x", "y", "width", "height"}:
            raise ValueError("Menu stencils use only filled integer rectangles")
        x, y, width, height = (int(element.attrib[key]) for key in ("x", "y", "width", "height"))
        if min(x, y) < 0 or min(width, height) <= 0 or x + width > size or y + height > size:
            raise ValueError("Rectangle outside the menu icon canvas")
        draw.rectangle((x, y, x + width - 1, y + height - 1), fill=ink)
    if image.getbbox() is None:
        raise ValueError("Empty menu stencil")
    output = ROOT / entry["output"]
    output.parent.mkdir(parents=True, exist_ok=True)
    image.save(output)
    print(f"{entry['id']}: {size}x{size}, {profile['ink']} -> {entry['output']}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--icon", help="Export one catalog ID; omit to export the whole family")
    selected = parser.parse_args().icon
    catalog = json.loads((SOURCE / "catalog.json").read_text(encoding="utf-8"))
    entries = [entry for entry in catalog["icons"] if selected is None or entry["id"] == selected]
    if not entries:
        parser.error("Unknown menu icon ID")
    for entry in entries:
        export(entry, catalog[entry["kind"]])
