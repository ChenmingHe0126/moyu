"""Build the illustrated user guide: packaging/guide/Moyu-Guide-ZH.pdf and packaging/guide/Moyu-Guide-EN.pdf.

Usage: python tools/make_guide.py [--preview]

The text and the layout are in packaging/guide/guide.html, with both languages side by side in <zh>…</zh> and
<en>…</en> tags. This script copies the screenshots (docs/images) and the component icons into dist/guide-build,
draws the wiring diagrams, the ribbon, the keyboard and the charts as inline SVG, keeps one language, and prints
each page to PDF with headless Microsoft Edge or Google Chrome. Every PDF goes through the same privacy check as the
packages. --preview also writes a PNG of every page to dist/guide-build/preview/ to check the layout.
Needs Pillow and pypdf (and pypdfium2 for --preview).
"""
import argparse
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

from PIL import Image

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
import package  # noqa: E402  version() and the privacy check
from make_icons import SETS  # noqa: E402

ROOT = TOOLS.parent
IMAGES = ROOT / "docs" / "images"
GUIDE = ROOT / "packaging" / "guide"
BUILD = ROOT / "dist" / "guide-build"
PROFILE = ROOT / "dist" / "guide-browser-profile"
LANGS = {"zh": ("zh-CN", "Moyu-Guide-ZH.pdf"), "en": ("en", "Moyu-Guide-EN.pdf")}

# Claude Code's working time per part, in minutes (docs/dev-time-stats.md). Rebound was built in one 56-minute
# session together with a game that was later replaced; its share is estimated from that session's rows.
TIME_LOG = [
    ("拓扑优化 2D + 3D", "Topology optimisation 2D + 3D", 114),
    ("VoxStack 叠楼", "VoxStack", 83),
    ("Serpentine 蜿蜒", "Serpentine", 70),
    ("Rebound 回弹（估算）", "Rebound (estimate)", 25),
    ("Swift 雨燕 + 塔楼老板画面", "Swift + tower boss screen", 53),
    ("三轮试玩后的调整", "Three play-test rounds", 93),
    ("发布：审查、图标、发布包、手册", "Release: review, icon, packages, guide", 119),
]

# Screenshots: name -> (file in docs/images, crop box or None, width in px).
SHOTS = {
    "vis_pixels": ("vis_pixels.png", None, 1000), "vis_smooth": ("vis_smooth.png", None, 700),
    "vis_solid": ("vis_solid.png", None, 700), "iter000": ("solver_iter000.png", None, 700),
    "iter005": ("solver_iter005.png", None, 700), "iter020": ("solver_iter020.png", None, 700),
    "bridge": ("bridge_result.png", None, 800), "bracket": ("bracket_result.png", None, 700),
    "live": ("live_moved_load.png", None, 700),
    "c3d000": ("c3d_iter000.png", None, 700), "c3d010": ("c3d_iter010.png", None, 700),
    "c3d_final": ("c3d_final.png", None, 1000), "c3d_voxels": ("c3d_voxels.png", None, 700),
    "canopy": ("canopy_result.png", None, 700), "bracket3d": ("bracket3d_result.png", None, 700),
    "vox": ("voxstack_gumball.jpg", None, 1100), "vox_gate": ("voxstack_gate.jpg", None, 900),
    "vox_boss": ("stack_boss.jpg", None, 1000), "vox_canvas": ("stack_gh_canvas.jpg", None, 1100),
    "run_button": ("stack_gh_canvas.jpg", (905, 535, 1015, 795), 220),
    "stop_button": ("stack_gh_canvas.jpg", (0, 15, 112, 268), 224),
    "snake": ("serpentine_firstperson.jpg", None, 1100), "snake_free": ("serpentine_freeview.jpg", None, 900),
    "snake_torus": ("serpentine_torus.jpg", None, 900), "snake_boss": ("serpentine_boss.jpg", None, 1000),
    "rebound": ("rebound_buffs.jpg", None, 1100), "rebound_arch": ("rebound_arch.jpg", None, 900),
    "rebound_game": ("rebound_game.jpg", None, 900), "rebound_boss": ("rebound_boss.jpg", None, 1000),
    "swift": ("swift_game.jpg", None, 1100), "swift_result": ("swift_result.jpg", None, 900),
    "swift_boss": ("swift_boss.jpg", None, 1100),
    "vox_proto": ("stack_mock_game.jpg", None, 800), "snake_proto": ("snake_mock_game.jpg", None, 800),
    "snake_pause": ("serpentine_pause.jpg", None, 800), "snake_count": ("serpentine_countdown.jpg", None, 800),
}

# Icon sheets that have no high-resolution cells in docs/images (columns, rows); the other sets do.
SHEETS = {"2d": ("moyu-icons-blue-violet-source.png", 4, 2), "3d": ("moyu-icons-3d-blue-violet-source.png", 4, 2),
          "massing": ("moyu-icons-massing-source.png", 2, 2)}
HD = {"landscape": "landscape-icons-hd", "facade": "facade-icons-hd", "section": "section-icons-hd"}

INK, BLUE, IRIS, ICE, CORAL, MUTED, LINE = "#263867", "#477DDA", "#8067C7", "#EFF4FF", "#E8735F", "#687085", "#D9DEE8"
FONT = "Segoe UI, Microsoft YaHei UI, Microsoft YaHei, sans-serif"


def prepare_images():
    out = BUILD / "img"
    out.mkdir(parents=True)
    for name, (src, box, width) in SHOTS.items():
        im = Image.open(IMAGES / src).convert("RGB")
        if box:
            im = im.crop(box)
        if im.width != width:
            im = im.resize((width, round(im.height * width / im.width)), Image.Resampling.LANCZOS)
        im.save(out / f"{name}.jpg", quality=80, optimize=True, progressive=True)


def icon_art(im, size=160):
    im = im.convert("RGBA")
    im.putalpha(im.getchannel("A").point(lambda a: 0 if a <= 8 else a))
    art = im.crop(im.getchannel("A").getbbox())
    side = max(art.size)
    square = Image.new("RGBA", (side, side))
    square.paste(art, ((side - art.width) // 2, (side - art.height) // 2))
    return square.resize((size, size), Image.Resampling.LANCZOS)


def prepare_icons():
    out = BUILD / "icons"
    out.mkdir(parents=True)
    for key, (src, cols, rows) in SHEETS.items():
        sheet = Image.open(IMAGES / src)
        w, h = sheet.width // cols, sheet.height // rows
        for i, name in enumerate(SETS[key]):
            if name:
                x, y = i % cols, i // cols
                icon_art(sheet.crop((x * w, y * h, (x + 1) * w, (y + 1) * h))).save(out / f"{name}.png")
    for folder in HD.values():
        for p in (IMAGES / folder).glob("*.png"):
            icon_art(Image.open(p)).save(out / p.name)
    icon_art(Image.open(ROOT / "packaging" / "icon-512.png"), 256).save(out / "Moyu.png")


def t(lang, zh, en):
    return zh if lang == "zh" else en


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def text(x, y, s, size=12, fill=INK, weight=400, anchor="start", extra=""):
    return (f'<text x="{x}" y="{y}" font-size="{size}" fill="{fill}" font-weight="{weight}" text-anchor="{anchor}" '
            f'font-family="{FONT}" {extra}>{esc(s)}</text>')


def wire(x1, y1, x2, y2, dash=False):
    dx = max(40, (x2 - x1) * 0.5)
    d = ' stroke-dasharray="5 4"' if dash else ""
    return (f'<path d="M{x1},{y1} C{x1 + dx},{y1} {x2 - dx},{y2} {x2},{y2}" fill="none" stroke="#5B6275" '
            f'stroke-width="1.8"{d}/>')


def card(x, y, w, h, icon=None, label="", fill="#FFFFFF", stroke="#C3CAD8", size=12.5):
    s = f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="7" fill="{fill}" stroke="{stroke}" stroke-width="1.2"/>'
    if icon:
        s += f'<image href="icons/{icon}.png" x="{x + 6}" y="{y + (h - 26) / 2}" width="26" height="26"/>'
    s += text(x + (38 if icon else 10), y + h / 2 + 4.5, label, size)
    return s


def wiring(lang, dim):
    """The typical wiring: five condition components, the solver with Timer, Toggle and Button, then the visualiser."""
    sfx = "3D" if dim == "3D" else ""
    conds = [("Boundary", f"Boundary {dim}"), ("Load", f"Load {dim}"), ("Support", f"Support {dim}"),
             ("Area", f"Area {dim}"), ("Material", f"Material {dim}")]
    out = ['<svg viewBox="0 0 760 286" xmlns="http://www.w3.org/2000/svg">']
    sx, sy, sw, sh = 300, 14, 176, 196
    ports = ["D", "L", "S", "A", "M", "V · P · R …", "Run", "Rst"]
    py = [sy + 30 + k * 21 for k in range(len(ports))]
    for i, (cls, label) in enumerate(conds):
        y = 6 + i * 44
        out.append(card(8, y, 176, 34, f"{cls}{sfx}Component", label))
        out.append(wire(184, y + 17, sx, py[i]))
    out.append(f'<rect x="{sx}" y="{sy}" width="{sw}" height="{sh}" rx="9" fill="#FFFFFF" stroke="{BLUE}" '
               f'stroke-width="1.8"/>')
    for k, p in enumerate(ports):
        out.append(f'<circle cx="{sx}" cy="{py[k]}" r="3.4" fill="#FFFFFF" stroke="#5B6275" stroke-width="1.4"/>')
        out.append(text(sx + 9, py[k] + 4, p, 10.5, MUTED))
    out.append(f'<image href="icons/Solver{sfx}Component.png" x="{sx + sw - 64}" y="{sy + 34}" width="52" '
               f'height="52"/>')
    out.append(text(sx + sw - 38, sy + 112, f"SIMP Solver {dim}", 12.5, INK, 600, "middle"))
    out.append(text(sx + sw - 38, sy + 128, t(lang, "求解器", "the solver"), 10.5, MUTED, 400, "middle"))
    # Toggle, button, timer.
    out.append(card(196, 222, 92, 26, None, "Run  ● True", "#F1F3F7", "#AEB6C6", 11))
    out.append(card(196, 254, 92, 26, None, "Reset  ○", "#F1F3F7", "#AEB6C6", 11))
    out.append(wire(288, 235, sx, py[6]))
    out.append(wire(288, 267, sx, py[7]))
    out.append(f'<g transform="translate(392,236)"><circle r="15" fill="#F1F3F7" stroke="#AEB6C6" stroke-width="1.2"/>'
               f'<path d="M0,-8 L0,0 L6,4" fill="none" stroke="{INK}" stroke-width="1.8" stroke-linecap="round"/></g>')
    out.append(f'<path d="M392,221 L392,{sy + sh}" stroke="#5B6275" stroke-width="1.6" stroke-dasharray="4 4"/>')
    out.append(text(414, 233, "Timer", 11.5, INK, 600))
    out.append(text(414, 249, "Params › Util › Trigger", 10, MUTED))
    out.append(text(414, 265, t(lang, "每跳推动一次求解", "expires the solver on every tick"), 10, MUTED))
    # Visualiser and the viewport.
    out.append(wire(sx + sw, sy + 40, 556, 40))
    out.append(f'<circle cx="{sx + sw}" cy="{sy + 40}" r="3.4" fill="#FFFFFF" stroke="#5B6275" stroke-width="1.4"/>')
    out.append(card(556, 23, 196, 34, f"Visualize{sfx}Component", f"Visualize {dim}"))
    thumb = "c3d_final" if dim == "3D" else "vis_pixels"
    out.append(f'<rect x="556" y="70" width="196" height="122" rx="7" fill="#D7D7D7"/>')
    out.append(f'<image href="img/{thumb}.jpg" x="560" y="74" width="188" height="114" '
               f'preserveAspectRatio="xMidYMid slice"/>')
    out.append(text(654, 208, t(lang, "Rhino 视口里的结果", "the result in the Rhino viewport"), 10.5, MUTED, 400,
                    "middle"))
    out.append('</svg>')
    return "".join(out)


def ribbon(lang):
    groups = [("2D", SETS["2d"][1:], 4), ("3D", SETS["3d"], 4), ("Massing", SETS["massing"], 2),
              ("Landscape", SETS["landscape"], 1), ("Facade", SETS["facade"], 1), ("Section", SETS["section"], 1)]
    cell, pad, minw = 42, 14, 70
    out = ['<svg viewBox="0 0 760 160" xmlns="http://www.w3.org/2000/svg">',
           '<rect x="0.5" y="0.5" width="759" height="159" rx="9" fill="#F4F5F8" stroke="#D3D8E2"/>']
    x = 12
    for tab in ["Params", "Maths", "Sets", "Vector", "Curve", "Surface", "Mesh", "…"]:
        out.append(text(x, 22, tab, 11.5, "#8A90A0"))
        x += len(tab) * 7 + 18
    out.append(f'<rect x="{x - 6}" y="6" width="84" height="24" rx="5" fill="#FFFFFF" stroke="{BLUE}"/>')
    out.append(f'<image href="icons/Moyu.png" x="{x}" y="9" width="18" height="18"/>')
    out.append(text(x + 24, 23, "Moyu", 12, INK, 700))
    out.append('<line x1="0" y1="34" x2="760" y2="34" stroke="#D3D8E2"/>')
    x = 14
    for name, icons, cols in groups:
        rows = (len(icons) + cols - 1) // cols
        w = max(minw, cols * cell)
        x0 = x + (w - cols * cell) / 2
        for i, ic in enumerate(icons):
            cx, cy = x0 + (i % cols) * cell, 42 + (i // cols) * cell + (2 - rows) * cell / 2
            out.append(f'<image href="icons/{ic}.png" x="{cx + 3}" y="{cy}" width="36" height="36"/>')
        out.append(text(x + w / 2, 146, name, 11.5, MUTED, 600, "middle"))
        x += w + pad
        out.append(f'<line x1="{x - pad / 2}" y1="42" x2="{x - pad / 2}" y2="150" stroke="#D3D8E2"/>')
        x += 2
    out.append('</svg>')
    return "".join(out)


def fmt_minutes(lang, m):
    h, mm = divmod(m, 60)
    if lang == "zh":
        return f"{h} 小时 {mm} 分" if h else f"{mm} 分钟"
    return f"{h} h {mm:02d} min" if h else f"{mm} min"


def time_chart(lang):
    rows = TIME_LOG
    top, rh, lx, bx, bw, vmax = 14, 27, 0, 250, 400, 120
    h = top + rh * len(rows) + 34
    out = [f'<svg viewBox="0 0 760 {h}" xmlns="http://www.w3.org/2000/svg">']
    for v in range(0, vmax + 1, 30):
        x = bx + bw * v / vmax
        out.append(f'<line x1="{x}" y1="{top - 6}" x2="{x}" y2="{top + rh * len(rows)}" stroke="#E6E9F0"/>')
        out.append(text(x, top + rh * len(rows) + 15, f"{v}", 10, MUTED, 400, "middle"))
    out.append(text(bx + bw + 22, top + rh * len(rows) + 15, t(lang, "分钟", "minutes"), 10, MUTED))
    for i, (zh, en, m) in enumerate(rows):
        y = top + i * rh
        out.append(text(bx - 10, y + 13, t(lang, zh, en), 11.5, INK, 400, "end"))
        out.append(f'<rect x="{bx}" y="{y + 2}" width="{bw * m / vmax:.1f}" height="16" rx="3" fill="{BLUE}"/>')
        out.append(text(bx + bw * m / vmax + 6, y + 14.5, fmt_minutes(lang, m), 10.5, INK))
    out.append('</svg>')
    return "".join(out)


def voxel_chart(lang):
    rows = [(r, r * (r // 2) * (r // 2)) for r in (32, 64, 96, 128, 160)]
    top, rh, bx, bw, vmax = 22, 24, 120, 520, 1_100_000
    h = top + rh * len(rows) + 26
    out = [f'<svg viewBox="0 0 760 {h}" xmlns="http://www.w3.org/2000/svg">']
    for v, label in ((150_000, t(lang, "15 万：每步要几秒", "150k: seconds per step")),
                     (1_000_000, t(lang, "100 万：警告", "1M: warning"))):
        x = bx + bw * v / vmax
        out.append(f'<line x1="{x}" y1="{top - 8}" x2="{x}" y2="{top + rh * len(rows)}" stroke="{CORAL}" '
                   f'stroke-dasharray="4 3"/>')
        out.append(text(x + 4, top - 10, label, 10, CORAL))
    for i, (r, n) in enumerate(rows):
        y = top + i * rh
        out.append(text(bx - 10, y + 13, f"Resolution {r}", 11, INK, 400, "end"))
        out.append(f'<rect x="{bx}" y="{y + 2}" width="{max(2, bw * n / vmax):.1f}" height="15" rx="3" fill="{IRIS}"/>')
        out.append(text(bx + max(2, bw * n / vmax) + 6, y + 14, f"{n:,}", 10.5, INK))
    out.append(text(bx, top + rh * len(rows) + 16,
                    t(lang, "体素数，按 2 : 1 : 1 的长方体算（长边 = Resolution）", "voxels for a 2 : 1 : 1 box (long side = Resolution)"),
                    10, MUTED))
    out.append('</svg>')
    return "".join(out)


def keyboard(lang):
    """A compact keyboard: coral keys work in every game, blue keys in some games."""
    shared = {"Esc", "`", "P", "Enter", "Space", "Shift", "Home"}
    some = {"Q", "W", "E", "A", "S", "D", "C", "Z", "X", "V", "←", "↑", "↓", "→"}
    u, g = 34, 4
    rows = [
        [("Esc", 1.0), ("", 0.5)] + [(k, 1) for k in "F1 F2 F3 F4".split()] + [("", 0.5)] + [(k, 1) for k in "F5 F6 F7 F8".split()],
        [("`", 1)] + [(k, 1) for k in "1234567890-="] + [("⌫", 1.6)],
        [("Tab", 1.4)] + [(k, 1) for k in "QWERTYUIOP[]"] + [("\\", 1.2)],
        [("Caps", 1.7)] + [(k, 1) for k in "ASDFGHJKL;'"] + [("Enter", 1.9)],
        [("Shift", 2.2)] + [(k, 1) for k in "ZXCVBNM,./"] + [("Shift ", 2.4)],
        [("Ctrl", 1.3), ("Win", 1.2), ("Alt", 1.2), ("Space", 6.4), ("Alt ", 1.2), ("Ctrl ", 1.3)],
    ]
    out = ['<svg viewBox="0 0 760 236" xmlns="http://www.w3.org/2000/svg">']

    def key(x, y, w, label):
        k = label.strip()
        fill, ink, stroke = ("#FFFFFF", "#7A8194", "#CDD3DF")
        if label in shared:
            fill, ink, stroke = CORAL, "#FFFFFF", CORAL
        elif k in some:
            fill, ink, stroke = "#DCE7FB", INK, "#9DB8EA"
        if not k:
            return ""
        return (f'<rect x="{x}" y="{y}" width="{w}" height="{u}" rx="5" fill="{fill}" stroke="{stroke}"/>'
                + text(x + w / 2, y + u / 2 + 4.5, k, 11.5 if len(k) < 4 else 10, ink, 600, "middle"))

    y = 4
    for r, row in enumerate(rows):
        x = 4
        for label, w in row:
            out.append(key(x, y, w * u + (w - 1) * g, label))
            x += w * u + w * g
        y += u + g + (6 if r == 0 else 0)
    hx = 640
    out.append(key(hx, 52, u, "Home"))
    out.append(key(hx + u + g, 52, u, ""))
    ay = 4 + 5 * (u + g) + 6
    out.append(key(hx + u + g, ay - u - g, u, "↑"))
    for i, k in enumerate("←↓→"):
        out.append(key(hx + i * (u + g), ay, u, k))
    out.append('</svg>')
    return "".join(out)


ITEMS = [  # Serpentine: colour, zh, en
    ("#E8574F", "苹果 +1 段", "Apple +1"), ("#9B7FD4", "葡萄 +3 段", "Grape +3"),
    ("#86D093", "西瓜 +5 段，8 秒后消失", "Melon +5, gone after 8 s"),
    ("#3B3A4A", "炸弹 −3 段，第 3 级起", "Bomb −3, from level 3"),
    ("#A8DCF0", "冰块：6 秒减速", "Ice: 6 s slower"), ("#F4F1FF", "幽灵：6 秒穿过自己", "Ghost: 6 s through yourself"),
]


def items(lang):
    cells = []
    for colour, zh, en in ITEMS:
        stroke = "#9AA1B2" if colour == "#F4F1FF" else colour
        cells.append(f'<div class="item"><svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9" fill="{colour}" '
                     f'stroke="{stroke}" stroke-width="1.5"/></svg><span>{t(lang, zh, en)}</span></div>')
    return '<div class="items">' + "".join(cells) + "</div>"


def page_html(lang):
    src = (GUIDE / "guide.html").read_text(encoding="utf-8")
    drop = "en" if lang == "zh" else "zh"
    src = re.sub(rf"<{drop}>.*?</{drop}>", "", src, flags=re.S)
    src = re.sub(rf"</?{lang}>", "", src)
    marks = {"VERSION": package.version(), "LANG": LANGS[lang][0], "WIRING2D": wiring(lang, "2D"),
             "WIRING3D": wiring(lang, "3D"), "RIBBON": ribbon(lang), "TIMECHART": time_chart(lang),
             "VOXCHART": voxel_chart(lang), "KEYBOARD": keyboard(lang), "ITEMS": items(lang),
             "TOTAL": fmt_minutes(lang, sum(m for *_, m in TIME_LOG))}
    for k, v in marks.items():
        src = src.replace(f"@@{k}@@", v)
    left = re.findall(r"@@[A-Z0-9]+@@", src)
    if left:
        sys.exit(f"Unknown markers in guide.html: {sorted(set(left))}")
    return src


def browser():
    for p in (r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
              r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
              r"C:\Program Files\Google\Chrome\Application\chrome.exe"):
        if Path(p).exists():
            return p
    sys.exit("Needs Microsoft Edge or Google Chrome to print the guide")


def print_pdf(html_file, pdf):
    # msedge.exe returns at once and a child process writes the PDF a few seconds later, so wait for the file to
    # appear and stop growing. The profile is kept between builds (outside BUILD); a brand-new one is retried.
    if pdf.exists():
        pdf.unlink()
    for _ in range(2):
        subprocess.run([browser(), "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
                        f"--user-data-dir={PROFILE}", "--no-pdf-header-footer", "--virtual-time-budget=8000",
                        f"--print-to-pdf={pdf}", html_file.as_uri()], check=True, capture_output=True, timeout=180)
        size, still = -1, 0
        for _ in range(240):
            time.sleep(0.5)
            now = pdf.stat().st_size if pdf.exists() else -1
            still = still + 1 if now == size and now > 0 else 0
            size = now
            if still >= 3:
                return
    sys.exit(f"The browser did not write {pdf}")


def finish(raw, pdf, lang, ver):
    from pypdf import PdfReader, PdfWriter
    reader = PdfReader(raw)
    writer = PdfWriter(clone_from=reader)
    writer.metadata = None
    writer.add_metadata({"/Title": t(lang, f"Moyu {ver} 用户手册", f"Moyu {ver} user guide"), "/Author": "Ming",
                         "/Subject": "Grasshopper plugin for Rhino 8", "/Creator": "tools/make_guide.py",
                         "/Producer": "Moyu"})
    with open(pdf, "wb") as f:
        writer.write(f)
    data = pdf.read_bytes()
    package.check_private(pdf.name, data)
    words = "".join(page.extract_text() or "" for page in PdfReader(pdf).pages)
    links = [str(a.get_object().get("/A", {}).get("/URI", "")) for page in PdfReader(pdf).pages
             for a in (page.get("/Annots") or [])]
    package.check_private(pdf.name + " (text)", words.encode("utf-8"))
    bad = [u for u in links if u and not u.startswith("https://")]
    if bad:
        sys.exit(f"{pdf.name} links to {bad}; only https links may go into a released file")
    return len(PdfReader(pdf).pages)


def preview(pdf, lang):
    import pypdfium2 as pdfium
    out = BUILD / "preview"
    out.mkdir(exist_ok=True)
    doc = pdfium.PdfDocument(str(pdf))
    for i in range(len(doc)):
        doc[i].render(scale=1.4).to_pil().save(out / f"{lang}-{i + 1:02d}.png")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--preview", action="store_true", help="also write a PNG of every page")
    args = ap.parse_args()
    if BUILD.exists():
        shutil.rmtree(BUILD)
    prepare_images()
    prepare_icons()
    shutil.copy2(GUIDE / "guide.css", BUILD / "guide.css")
    ver = package.version()
    for lang, (_, name) in LANGS.items():
        html_file = BUILD / f"guide-{lang}.html"
        html_file.write_text(page_html(lang), encoding="utf-8")
        raw = BUILD / f"raw-{lang}.pdf"
        print_pdf(html_file, raw)
        pdf = GUIDE / name
        pages = finish(raw, pdf, lang, ver)
        print(f"{pdf.relative_to(ROOT)}  {pages} pages  {pdf.stat().st_size:,} bytes")
        if args.preview:
            preview(pdf, lang)


if __name__ == "__main__":
    main()
