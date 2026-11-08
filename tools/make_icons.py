"""Slice an icon sprite sheet into the plugin's 24x24 icons.

Usage: python tools/make_icons.py sheet.png [--set 2d|3d|massing|landscape|facade|section|plugin] [--cols 4 --rows 2]
       Add --source-out <directory> to also save each original-resolution cell without resizing.
       Add --preview <file.png> to also write every icon at 24 px and 8x on light and dark strips.
Writes src/Moyu/Icons/<Name>.png in reading order: the 2D set is listed in
docs/icon-prompts.md, the 3D set in docs/icon-prompts-3d.md (its 8th cell stays empty),
the Massing set in docs/icon-prompts-massing.md (a 2x2 sheet, so pass --cols 2 --rows 2;
its 4th cell stays empty), the Landscape set in docs/icon-prompts-landscape.md (a 2x1 sheet,
so pass --cols 2 --rows 1).
"""
import argparse
from pathlib import Path

from PIL import Image

SETS = {
    # The 2D sheet's first cell was the old tab icon; it is skipped so re-exporting 2D keeps the fish (--set plugin).
    "2d": [
        None, "BoundaryComponent", "LoadComponent", "SupportComponent",
        "AreaComponent", "MaterialComponent", "SolverComponent", "VisualizeComponent",
    ],
    "3d": [
        "Boundary3DComponent", "Load3DComponent", "Support3DComponent", "Area3DComponent",
        "Material3DComponent", "Solver3DComponent", "Visualize3DComponent",
    ],
    "massing": ["MassingStudyComponent", "VoxelUnitComponent", "UnitLibraryComponent"],
    "landscape": ["PathStudyComponent", "SiteSurfaceComponent"],
    "facade": ["FacadeStudyComponent", "BrickBondComponent"],
    "section": ["AirflowStudyComponent", "StoreyGridComponent"],
    "examples": ["ExamplesComponent"],
    # The plug-in's own icon (ribbon tab, package manager, food4rhino): a 1x1 sheet.
    "plugin": ["Moyu"],
}
OUT = Path(__file__).resolve().parent.parent / "src" / "Moyu" / "Icons"


def to_icon(cell: Image.Image) -> Image.Image:
    # Preserve white artwork and the supplied transparency. Generated sheets can
    # contain almost invisible alpha noise that otherwise defeats tight cropping.
    cell = cell.convert("RGBA")
    cell.putalpha(cell.getchannel("A").point(lambda a: 0 if a <= 8 else a))
    box = cell.getchannel("A").getbbox()
    if box is None:
        raise ValueError("An icon cell is empty.")
    art = cell.crop(box)
    side = max(art.width, art.height)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(art, ((side - art.width) // 2, (side - art.height) // 2))
    # 22 px of artwork keeps the 1 px margin Grasshopper icons usually have.
    icon = Image.new("RGBA", (24, 24), (0, 0, 0, 0))
    # Filter premultiplied color so transparent RGB cannot create dark fringes.
    small = square.convert("RGBa").resize((22, 22), Image.Resampling.LANCZOS).convert("RGBA")
    icon.paste(small, (1, 1))
    return icon


def preview(names, path: Path) -> None:
    """Each icon at real size and 8x (nearest neighbour), on a light and a dark strip."""
    pad, big = 16, 24 * 8
    cell = pad + 24 + pad + big + pad
    sheet = Image.new("RGBA", (cell * len(names), 2 * (big + 2 * pad)), (0, 0, 0, 0))
    for row, back in enumerate(((243, 243, 243, 255), (43, 43, 48, 255))):
        y0 = row * (big + 2 * pad)
        sheet.paste(Image.new("RGBA", (sheet.width, big + 2 * pad), back), (0, y0))
        for k, name in enumerate(names):
            if name is None:
                continue
            icon = Image.open(OUT / f"{name}.png").convert("RGBA")
            x0 = k * cell + pad
            sheet.alpha_composite(icon, (x0, y0 + pad + big // 2 - 12))
            sheet.alpha_composite(icon.resize((big, big), Image.Resampling.NEAREST), (x0 + 24 + pad, y0 + pad))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)
    print(f"wrote {path}")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("sheet")
    ap.add_argument("--set", choices=sorted(SETS), default="2d")
    ap.add_argument("--cols", type=int, default=4)
    ap.add_argument("--rows", type=int, default=2)
    ap.add_argument("--source-out", type=Path, help="Also save unscaled source-cell PNGs in this directory.")
    ap.add_argument("--preview", type=Path, help="Also write a real-size + 8x preview sheet (light and dark) to this path.")
    args = ap.parse_args()

    names = SETS[args.set]
    if args.cols <= 0 or args.rows <= 0 or args.cols * args.rows < len(names):
        ap.error(f"The {args.set} set needs at least {len(names)} cells (default: 4 columns, 2 rows).")
    if args.source_out is not None and args.source_out.resolve() == OUT.resolve():
        ap.error("Source cells must be saved separately from the plugin's 24x24 Icons directory.")

    sheet = Image.open(args.sheet).convert("RGBA")
    if sheet.getchannel("A").getextrema()[0] == 255:
        ap.error("The sheet must already have a transparent background; white artwork is preserved.")
    cw, ch = sheet.width / args.cols, sheet.height / args.rows
    OUT.mkdir(parents=True, exist_ok=True)
    if args.source_out is not None:
        args.source_out.mkdir(parents=True, exist_ok=True)
    for k, name in enumerate(names):
        if name is None:
            continue
        r, c = divmod(k, args.cols)
        cell = sheet.crop((round(c * cw), round(r * ch), round((c + 1) * cw), round((r + 1) * ch)))
        if args.source_out is not None:
            source_path = args.source_out / f"{name}.png"
            cell.save(source_path)
            print(f"wrote source {source_path} ({cell.width}x{cell.height})")
        path = OUT / f"{name}.png"
        to_icon(cell).save(path)
        print(f"wrote {path}")
    if args.preview is not None:
        preview(names, args.preview)


if __name__ == "__main__":
    main()
