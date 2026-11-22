"""Normalize the generated occupation cutouts and update only their foot anchors."""

import json
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "mods/definitely-stolen"
MANIFEST = MOD / "Assets/Portraits/new-occupations.json"


def main():
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    anchors_path = MOD / "Assets/Portraits/foot-anchors.json"
    anchors = json.loads(anchors_path.read_text(encoding="utf-8"))
    width, height = manifest["Canvas"]
    foot_x, foot_y = manifest["Foot"]
    preview = Image.new("RGB", (900, 765), (46, 49, 54))
    draw = ImageDraw.Draw(preview)
    for index, portrait in enumerate(manifest["Portraits"]):
        source = Image.open(portrait["SourceImage"]).convert("RGBA")
        alpha = source.getchannel("A")
        if alpha.getextrema()[0] != 0:
            raise ValueError("Generated portrait has no transparent background: " + portrait["Role"])
        mask = alpha.point(lambda value: 255 if value >= 24 else 0)
        bounds = mask.getbbox()
        if bounds is None:
            raise ValueError("Empty portrait: " + portrait["Role"])
        cropped = source.crop(bounds)
        shape = mask.crop(bounds)
        lower = shape.crop((0, int(shape.height * .9), shape.width, shape.height)).getbbox()
        if lower is None:
            raise ValueError("Portrait has no foot contact: " + portrait["Role"])
        local_foot_x = (lower[0] + lower[2]) / 2
        scale = min((foot_y - 16) / cropped.height, (foot_x - 12) / local_foot_x,
                    (width - foot_x - 12) / (cropped.width - local_foot_x))
        resized = cropped.resize((round(cropped.width * scale), round(cropped.height * scale)), Image.Resampling.LANCZOS)
        output = Image.new("RGBA", (width, height))
        output.alpha_composite(resized, (round(foot_x - local_foot_x * scale), foot_y - resized.height))
        destination = MOD / portrait["Output"]
        output.save(destination, optimize=True)
        anchors[destination.stem] = {"X": foot_x / width, "Y": foot_y / height}
        x, y = index % 6 * 150, index // 6 * 255
        thumb = output.resize((150, 225), Image.Resampling.LANCZOS)
        preview.paste(thumb, (x, y + 20), thumb)
        draw.text((x + 2, y + 3), portrait["Role"][:18] + " " + str(portrait["Variant"]), fill="white")
    anchors_path.write_text(json.dumps(anchors, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    preview_path = ROOT / "output/theft-1.2.0/new-occupations-preview.jpg"
    preview_path.parent.mkdir(parents=True, exist_ok=True)
    preview.save(preview_path, quality=90)
    print(f"Exported {len(manifest['Portraits'])} portraits; preview {preview.size}, {preview_path.stat().st_size} bytes.")


if __name__ == "__main__":
    main()
