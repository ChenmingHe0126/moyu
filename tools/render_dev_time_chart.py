"""Render the recorded Moyu development stages as a shareable PNG and SVG.

Run: python tools/render_dev_time_chart.py
Source: docs/dev-time-stats.md (records through 2026-11-04).
Values follow its summary table; these are approximate session durations,
not person-hours or a sum of concurrent agents' runtimes.
"""

from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.font_manager import FontProperties
from matplotlib.patches import Rectangle


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "docs" / "images"
REGULAR = FontProperties(fname="C:/Windows/Fonts/msyh.ttc")
BOLD = FontProperties(fname="C:/Windows/Fonts/msyhbd.ttc")

BG = "#F7F6F2"
INK = "#223936"
MUTED = "#66716D"
GRID = "#E3E6DF"
TEAL = "#287F7D"
BLUE = "#647F9F"
OCHRE = "#AA744D"

# Each item is counted once. The 73-minute VoxStack v1 is part of its
# 83-minute entry, and is not added again. Later shared work stays separate.
GROUPS = [
    {
        "label": "功能开发与首轮交付",
        "color": TEAL,
        "heading_y": 8.25,
        "rows": [
            ("拓扑优化 · 2D + 3D", "14 个 Grasshopper 组件", 114, 7.80),
            ("VoxStack 叠楼", "含方案、首版、图标接入与提交", 83, 7.23),
            ("Serpentine 蜿蜒", "含共用框架及两款游戏的画面重做", 70, 6.66),
            ("Rebound 回弹 + Inkline 墨线", "两个游戏的方案、首版、图标、实测与交付", 56, 6.09),
            ("Swift 雨燕 + 老板画面重做", "替换 Inkline；重做剖面与塔楼幕墙", 53, 5.52),
        ],
    },
    {
        "label": "后续迭代与实机打磨",
        "color": BLUE,
        "heading_y": 5.02,
        "rows": [
            ("难度、道具与剖面调整", "三个游戏降低开局难度；调整 Rebound 与 Swift", 26, 4.57),
            ("砌法、视角与旋转操作修正", "修复砌法；调整道具、蛇视角、景观画面与 gumball", 27, 4.00),
            ("VoxStack 操作优化与 4K 实测", "更慢的节奏、自定义积木、固定旋转控件", 40, 3.43),
        ],
    },
    {
        "label": "发布准备与文档交付",
        "color": OCHRE,
        "heading_y": 2.93,
        "rows": [
            ("审查修复与发布准备", "多代理审查、修复、插件图标接入与发布包", 82, 2.48),
            ("图文手册与上架文案", "中英手册、上架说明、画面小改与验证打包", 37, 1.91),
        ],
    },
]


def render():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    plt.rcParams["svg.fonttype"] = "path"
    fig = plt.figure(figsize=(16, 12), dpi=200, facecolor=BG)
    ax = fig.add_axes((0, 0, 1, 1), xlim=(0, 16), ylim=(0, 12))
    ax.axis("off")

    def text(x, y, value, size=12, color=INK, bold=False, align="left", **kw):
        return ax.text(
            x, y, value, fontsize=size, color=color,
            fontproperties=BOLD if bold else REGULAR,
            ha=align, va="center", **kw,
        )

    def rect(x, y, w, h, color, **kw):
        ax.add_patch(Rectangle((x, y), w, h, facecolor=color, edgecolor="none", **kw))

    totals = [sum(row[2] for row in group["rows"]) for group in GROUPS]
    total = sum(totals)
    hours, minutes = divmod(total, 60)

    text(0.72, 11.43, "MOYU  /  DEVELOPMENT LOG", 10, TEAL, True)
    text(0.72, 10.91, "各个部分，花了多少开发时间？", 28, INK, True)
    text(0.72, 10.39, "2026.11.01 — 11.04  ·  基于 Claude Code 会话记录", 11, MUTED)
    text(15.28, 11.43, "已记录时长合计 · 约", 10, MUTED, align="right")
    text(15.28, 10.85, f"{hours} 小时 {minutes} 分", 28, TEAL, True, "right")
    text(15.28, 10.38, f"{total} 分钟 / 按统计文档约数汇总", 10, MUTED, align="right")

    # Compact group summaries use the same colors as the detailed bars.
    for x, group, subtotal in zip((0.72, 5.72, 10.72), GROUPS, totals):
        rect(x, 9.34, 0.045, 0.53, group["color"])
        text(x + 0.19, 9.72, f"{subtotal} 分钟", 19, group["color"], True)
        text(x + 1.76, 9.72, f"{subtotal / total:.1%}", 10, MUTED)
        text(x + 0.19, 9.32, group["label"], 10.5, MUTED)
    ax.plot([0.72, 15.28], [8.96, 8.96], color=GRID, lw=0.8)

    bar_start, bar_width = 5.84, 8.13
    scale = bar_width / 120
    text(0.72, 8.64, "开发内容 / 统计范围", 10, MUTED)
    text(15.28, 8.64, "用时（分钟）", 10, MUTED, align="right")
    for tick in (0, 30, 60, 90, 120):
        x = bar_start + tick * scale
        text(x, 8.64, str(tick), 9, MUTED, align="center")
        # Keep group labels clear, drawing grid guides only across bar rows.
        for lower, upper in ((5.29, 8.04), (3.20, 4.81), (1.68, 2.72)):
            ax.plot([x, x], [lower, upper], color=GRID, lw=0.65, zorder=0)

    for group, subtotal in zip(GROUPS, totals):
        color = group["color"]
        text(0.72, group["heading_y"], group["label"], 10.5, color, True)
        text(15.28, group["heading_y"], f"小计 {subtotal} 分钟", 9.5, color, align="right")
        for index, (label, description, value, y) in enumerate(group["rows"]):
            text(0.72, y + 0.10, label, 12.3, INK, True)
            text(0.72, y - 0.15, description, 9, MUTED)
            if group is GROUPS[0] and index == 0:
                # Detailed topology stages sum to 114. Independent integer
                # rounding to 54 + 61 would misleadingly imply 115 minutes.
                split = 53.5 * scale
                rect(bar_start, y - 0.045, split, 0.26, "#70ABA6")
                rect(bar_start + split, y - 0.045, 60.5 * scale, 0.26, color)
                text(bar_start + split / 2, y + 0.085, "2D · 53.5", 9, "#123E3B", True, "center")
                text(bar_start + split + 60.5 * scale / 2, y + 0.085, "3D · 60.5", 9, "#FFFFFF", True, "center")
            else:
                rect(bar_start, y - 0.045, value * scale, 0.26, color)
            text(15.28, y + 0.08, str(value), 15, color, True, "right")

    ax.plot([0.72, 15.28], [1.43, 1.43], color=GRID, lw=0.8)
    text(0.72, 1.10, "口径：按每轮请求至最后回复的工作时长累计，包含方案、实现、测试与交付；并行代理不重复累计。", 9.5, MUTED)
    text(0.72, 0.79, "排除用户等待、休息及 Rhino 外部故障；后台图标生成不另加。跨模块改进保留在原统计阶段。", 9.5, MUTED)
    text(0.72, 0.43, "数据来源：docs/dev-time-stats.md · 已记录阶段汇总，非人工总工时", 9, MUTED)
    text(15.28, 0.43, "MOYU · 1.0.0", 9, MUTED, align="right")

    outputs = []
    for suffix in ("png", "svg"):
        path = OUTPUT / f"moyu-development-time.{suffix}"
        fig.savefig(path, dpi=200, facecolor=BG, metadata={"Title": "Moyu 各部分开发用时"})
        outputs.append(path)
    plt.close(fig)
    print(f"Total: {total} minutes ({hours}h {minutes}m)")
    print(f"Group totals: {totals}")
    for path in outputs:
        print(path)


if __name__ == "__main__":
    render()
