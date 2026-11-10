"""Render the audited 1.0.1 development-time record as a shareable PNG/SVG.

Run after generating docs/dev-time-1.0.1.json:
    python tools/render_dev_time_101_chart.py

Bars show the union of primary-agent turns and background task intervals.
Overlapping agents are counted once; values are not summed agent-hours.
"""

import json
import math
from pathlib import Path
import textwrap

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.font_manager import FontProperties
from matplotlib.patches import Rectangle


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs" / "dev-time-1.0.1.json"
OUTPUT = ROOT / "docs" / "images"
REGULAR = FontProperties(fname="C:/Windows/Fonts/msyh.ttc")
BOLD = FontProperties(fname="C:/Windows/Fonts/msyhbd.ttc")

BG = "#F7F6F2"
INK = "#223936"
MUTED = "#66716D"
GRID = "#DFE4DD"
TEAL = "#287F7D"
SAGE = "#A7C8BD"
OCHRE = "#96633D"
NOTE_BG = "#EFE9DE"


def render():
    data = json.loads(SOURCE.read_text(encoding="utf-8"))
    stages = data["stages"]
    summary = data["summary"]
    total = summary["active_minutes"]
    primary = summary["primary_minutes"]
    background = summary["background_only_minutes"]
    if not math.isclose(sum(s["active_minutes"] for s in stages), total, abs_tol=0.02):
        raise ValueError("Stage durations do not add up to the audited total")
    if not math.isclose(primary + background, total, abs_tol=0.02):
        raise ValueError("Primary and background-only durations do not add up")
    if len(stages) != 5:
        raise ValueError("This layout expects five audited release stages")
    for stage in stages:
        if not 0 <= stage["primary_minutes"] <= stage["active_minutes"] + 0.001:
            raise ValueError("Invalid primary/active stage duration")

    hours, minutes = divmod(round(total), 60)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    plt.rcParams["svg.fonttype"] = "path"
    fig = plt.figure(figsize=(16, 11.5), dpi=200, facecolor=BG)
    ax = fig.add_axes((0, 0, 1, 1), xlim=(0, 16), ylim=(0, 11.5))
    ax.axis("off")

    def text(x, y, value, size=12, color=INK, bold=False, align="left", **kw):
        return ax.text(
            x, y, value, fontsize=size, color=color,
            fontproperties=BOLD if bold else REGULAR,
            ha=align, va="center", **kw,
        )

    def rect(x, y, width, height, color, **kw):
        ax.add_patch(Rectangle(
            (x, y), width, height, facecolor=color, edgecolor="none", **kw,
        ))

    text(0.72, 10.96, "MOYU  /  RELEASE 1.0.1", 10, TEAL, True)
    text(0.72, 10.42, "1.0.1 更新，花了多少时间？", 28, INK, True)
    text(0.72, 9.91, "仅本机可核实记录 · 2026.11.07 — 11.08 · America/New_York", 11, MUTED)
    text(15.28, 10.96, "任务用时 · 含后台任务 · 约", 10, MUTED, align="right")
    text(15.28, 10.39, f"{hours} 小时 {minutes} 分", 28, TEAL, True, "right")
    text(15.28, 9.91, f"{total:.2f} 分钟 / 活动区间去重后累计", 10, MUTED, align="right")

    # These numbers partition the total, without adding concurrent agent time.
    for x, value, label, color in (
        (0.72, primary, "主代理轮次", TEAL),
        (5.60, background, "补计后台任务区间", SAGE),
    ):
        rect(x, 8.81, 0.045, 0.66, color)
        text(x + 0.20, 9.28, f"{value:.2f} 分钟", 22, TEAL, True)
        text(x + 0.20, 8.83, label, 11, MUTED)
    text(4.71, 9.10, "+", 24, MUTED)
    text(10.46, 9.26, "并行运行，只计一次", 16, INK, True)
    text(10.46, 8.84, "补计值仅指主代理轮次之外的后台时间", 10, MUTED)
    ax.plot([0.72, 15.28], [8.40, 8.40], color=GRID, lw=0.8)

    bar_start, bar_width = 5.72, 8.42
    maximum = max(s["active_minutes"] for s in stages)
    axis_max = math.ceil(maximum / 30) * 30
    scale = bar_width / axis_max
    text(0.72, 8.03, "开发内容 / 统计范围", 10, MUTED)
    text(15.28, 8.03, "分钟", 10, MUTED, align="right")
    for tick in range(0, axis_max + 1, 30):
        x = bar_start + tick * scale
        text(x, 8.03, str(tick), 9, MUTED, align="center")
        ax.plot([x, x], [3.33, 7.72], color=GRID, lw=0.65, zorder=0)

    for index, stage in enumerate(stages):
        y = 7.37 - index * 0.94
        value = stage["active_minutes"]
        front = stage["primary_minutes"]
        back = max(0, value - front)
        text(0.72, y + 0.10, stage["label"], 12.5, INK, True)
        description = "\n".join(textwrap.wrap(stage["description"], width=34))
        text(0.72, y - 0.20, description, 9.2, MUTED, linespacing=1.5)
        rect(bar_start, y - 0.045, front * scale, 0.30, TEAL)
        if back > 0:
            rect(bar_start + front * scale, y - 0.045, back * scale, 0.30, SAGE)
        text(15.28, y + 0.10, f"{value:.1f}", 16, TEAL, True, "right")

    ax.plot([0.72, 15.28], [2.99, 2.99], color=GRID, lw=0.8)
    text(0.72, 2.62, "怎样计算", 11, INK, True)
    text(2.05, 2.62, "主代理请求至最后回复 + 可确认的后台审查、修复和图标任务；重叠区间合并。", 10.2, MUTED)
    text(2.05, 2.23, "剔除用户等待和过夜空闲；不是人工工时，也不累加每个子代理的运行时长。", 10.2, MUTED)
    text(2.05, 1.84, "阶段值保留 1 位小数，含实现、测试与交付；1.0.0 旧统计未按后台区间重新核算。", 10.2, MUTED)

    rect(0.72, 0.84, 14.56, 0.66, NOTE_BG)
    rect(0.72, 0.84, 0.045, 0.66, OCHRE)
    text(0.93, 1.17, "未计入：笔记本上先前的按键修复与语言初版（缺少原始日志），因此这不是 1.0.1 的完整总成本。", 10.2, OCHRE)
    text(0.72, 0.42, "来源：本机 Claude Code 会话时间戳 · docs/dev-time-1.0.1.json", 9, MUTED)
    text(15.28, 0.42, "MOYU · 1.0.1", 9, MUTED, align="right")

    for suffix in ("png", "svg"):
        path = OUTPUT / f"moyu-1.0.1-development-time.{suffix}"
        fig.savefig(path, dpi=200, facecolor=BG, metadata={"Title": "Moyu 1.0.1 更新用时"})
        print(path)
    plt.close(fig)
    print(f"Active: {total:.3f} min; primary: {primary:.3f} min; background-only: {background:.3f} min")


if __name__ == "__main__":
    render()
