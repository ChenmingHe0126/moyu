"""Reproduce the audited 1.0.1 timing from the local Claude Code session.

Run: python tools/dev_time_101.py [--session-log PATH]
The public JSON contains timestamps and event IDs, never conversation text.
Background intervals end at the completion notification, so this measures
observed task elapsed time, not CPU time or the sum of agents' runtimes.
"""

import argparse
import json
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path

from dev_time import content, is_request, load, text_of


ROOT = Path(__file__).resolve().parents[1]
SESSION = "6bd43864-dad5-4f28-91f3-28bf914a1a93"
LAST_MESSAGE = "4abe9626-9684-43e1-aaf9-3ef6abd8bffd"
# Both audited dates are in EST; explicit offset avoids the host's timezone.
TZ = timezone(timedelta(hours=-5))
BACKGROUND = [
    ("第一轮审查与核查", "toolu_01TM5zNqoGsrUEhnoKkxEnMt", 0),
    ("第二轮审查与核查", "toolu_01DqJiq4b2ZEXWawA4eX4pdL", 0),
    ("第一轮修复与复查", "toolu_01FpYAsNvubJDxhLT1wSDQVd", 0),
    ("第二轮修复与复查", "toolu_01MnqoQrBqZXDoY8TY3gYR38", 0),
    ("最终集成审查", "toolu_019UznjJKEUR6YqbvbXSvSnt", 0),
    ("Examples 图标生成", "toolu_014bhu2NFE2XrKdnVAKEwfF3", 1),
]
STAGES = [
    ("语言菜单、双语示例与审查修复", "含全仓审查、61 项问题修复、实测与报告", range(0, 8)),
    ("Examples 组件、图标与发布包", "一键打开案例；更新手册、生成图标并验证发布包", range(8, 11)),
    ("提交重建与本机安装", "提交 1.0.1、重建发布包、安装并核验", range(11, 13)),
    ("面板编号、命名与排版", "Examples 排首位；Topo2D / Topo3D 命名与面板加宽", range(13, 15)),
    ("双平台发布与合并", "Yak、food4rhino 发布操作、验证与合并推送", range(15, 26)),
]


def union(intervals):
    merged = []
    for start, end in sorted(intervals):
        if end < start:
            raise ValueError("Interval ends before it starts")
        if start == end:
            continue
        if merged and start <= merged[-1][1]:
            merged[-1] = (merged[-1][0], max(end, merged[-1][1]))
        else:
            merged.append((start, end))
    return merged


def minutes(intervals):
    return sum((end - start).total_seconds() for start, end in union(intervals)) / 60


def interval_json(interval):
    start, end = interval
    return {"start": start.isoformat(timespec="milliseconds"),
            "end": end.isoformat(timespec="milliseconds"),
            "minutes": (end - start).total_seconds() / 60}


def audit(path):
    turns, launches, completions = [], {}, {}
    found_last = False
    for raw_time, message in load(path):
        time = raw_time.astimezone(TZ)
        if is_request(message):
            turns.append({"start": time, "end": time,
                          "start_message_id": message.get("uuid"),
                          "end_message_id": message.get("uuid")})
        elif message.get("type") == "assistant" and turns:
            turns[-1]["end"] = time
            turns[-1]["end_message_id"] = message.get("uuid")
        body = content(message)
        if message.get("type") == "assistant" and isinstance(body, list):
            for block in body:
                if isinstance(block, dict) and block.get("type") == "tool_use":
                    launches[block["id"]] = (time, message.get("uuid"))
        if message.get("type") == "user":
            text = text_of(message)
            tool_match = re.search(r"<tool-use-id>([^<]+)</tool-use-id>", text)
            task_match = re.search(r"<task-id>([^<]+)</task-id>", text)
            if text.lstrip().startswith("<task-notification>") and tool_match:
                completions[tool_match[1]] = (time, message.get("uuid"),
                                            task_match[1] if task_match else None)
        if message.get("uuid") == LAST_MESSAGE:
            found_last = True
            break
    if not found_last or len(turns) != 26:
        raise ValueError("Session does not match the audited 1.0.1 record")

    backgrounds = []
    for label, tool_id, stage_index in BACKGROUND:
        start, start_id = launches[tool_id]
        end, end_id, task_id = completions[tool_id]
        backgrounds.append({"label": label, "tool_use_id": tool_id,
                            "task_id": task_id, "stage_index": stage_index,
                            "start_message_id": start_id, "end_message_id": end_id,
                            "interval": (start, end)})

    primary = [(turn["start"], turn["end"]) for turn in turns]
    all_intervals = primary + [item["interval"] for item in backgrounds]
    stages = []
    for index, (label, description, indices) in enumerate(STAGES):
        stage_primary = [primary[i] for i in indices]
        stage_all = stage_primary + [item["interval"] for item in backgrounds
                                     if item["stage_index"] == index]
        stages.append({"label": label, "description": description,
                       "primary_turn_indices": list(indices),
                       "primary_minutes": minutes(stage_primary),
                       "active_minutes": minutes(stage_all),
                       "active_intervals": [interval_json(i) for i in union(stage_all)]})
    active, primary_minutes = minutes(all_intervals), minutes(primary)
    assert abs(sum(stage["active_minutes"] for stage in stages) - active) < 1e-8
    assert abs(active - 207.03633333333332) < 1e-8
    assert abs(primary_minutes - 73.25791666666666) < 1e-8
    return {
        "version": "1.0.1",
        "source": {"session_id": SESSION, "timezone": "America/New_York",
                   "utc_offset": "-05:00", "dates": ["2026-11-07", "2026-11-08"],
                   "last_message_id": LAST_MESSAGE,
                   "method": "Union of main-response intervals and six audited background task intervals; background ends at completion notification.",
                   "scope": "Local desktop session only; earlier laptop development logs unavailable."},
        "summary": {"active_minutes": active, "primary_minutes": primary_minutes,
                    "background_only_minutes": active - primary_minutes,
                    "previous_recorded_minutes": 588,
                    "wall_span_minutes": (turns[-1]["end"] - turns[0]["start"]).total_seconds() / 60},
        "stages": stages,
        "exclusions": ["用户等待、登录、手动上传、验收及过夜空闲不计。",
                       "不含笔记本上先前的按键修复与语言初版（缺少日志）。",
                       "本次统计与制图不计；并行代理及图标重叠区间只计一次。"],
        "primary_turns": [{"index": i,
                           "start_message_id": turn["start_message_id"],
                           "end_message_id": turn["end_message_id"],
                           **interval_json(primary[i])} for i, turn in enumerate(turns)],
        "background_tasks": [{key: value for key, value in item.items() if key != "interval"}
                             | interval_json(item["interval"]) for item in backgrounds],
        "active_intervals": [interval_json(i) for i in union(all_intervals)],
        "notes": ["The six Codex code reviews launched at 22:57:38 and confirmed complete by 23:05:22 fall entirely inside the night interval; they add no further time.",
                  "The original dev_time.py treats completion notifications as turn starts. Preserve that behavior for the 73.3-minute comparison; do not treat every response as a separate human request.",
                  "1.0.0's 588-minute figure uses the prior documented main-response method and has not been re-audited for background gaps. Do not directly add it to the 207.0-minute union figure."]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--session-log", type=Path,
                        default=Path.home() / ".claude/projects/G--Git-moyu" / f"{SESSION}.jsonl")
    parser.add_argument("--output", type=Path, default=ROOT / "docs/dev-time-1.0.1.json")
    args = parser.parse_args()
    result = audit(args.session_log)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result["summary"], indent=2))
    for stage in result["stages"]:
        print(f'{stage["active_minutes"]:.6f} min')
    print(args.output)


if __name__ == "__main__":
    main()
