"""Per-turn timeline of this project's Claude Code sessions, for the development-time stats.

Usage: python tools/dev_time.py [--detail] [--session ID_PREFIX ...] [--projects DIR] [-o FILE]

A turn runs from a user request to Claude's last reply before the next request; its length is the
time Claude actually worked on it. --detail also lists every tool call and progress message with its
time, which is what you need to split turns into phases and mark the Rhino-trouble stretches to
exclude (see docs/game-workflow.md, step 6). Times are shown in the computer's local time zone.
"""
import argparse
import json
import sys
from collections import Counter
from datetime import datetime
from pathlib import Path

DEFAULT_PROJECTS = Path.home() / ".claude" / "projects" / "G--Git-moyu"


def load(path: Path):
    rows = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            try:
                d = json.loads(line)
            except ValueError:
                continue
            ts = d.get("timestamp")
            if ts:
                rows.append((datetime.fromisoformat(ts.replace("Z", "+00:00")).astimezone(), d))
    rows.sort(key=lambda r: r[0])
    return rows


def content(d):
    return (d.get("message") or {}).get("content")


def text_of(d) -> str:
    c = content(d)
    if isinstance(c, str):
        return c
    if isinstance(c, list):
        return " ".join(p.get("text", "") for p in c if isinstance(p, dict) and p.get("type") == "text")
    return ""


def is_request(d) -> bool:
    """A message typed by the person (not a tool result, reminder or meta entry)."""
    if d.get("type") != "user" or d.get("isMeta"):
        return False
    c = content(d)
    if isinstance(c, list) and any(isinstance(p, dict) and p.get("type") == "tool_result" for p in c):
        return False
    t = text_of(d).strip()
    return bool(t) and not t.startswith("<system-reminder>")


def tool_calls(d):
    c = content(d)
    if not isinstance(c, list):
        return []
    calls = []
    for p in c:
        if isinstance(p, dict) and p.get("type") == "tool_use":
            inp = p.get("input") or {}
            hint = inp.get("description") or inp.get("command") or inp.get("file_path") or str(inp.get("script", ""))[:60] or inp.get("url") or ""
            calls.append((p.get("name", "?"), " ".join(str(hint).split())[:90]))
    return calls


def short(s: str, n: int) -> str:
    s = " ".join(s.split())
    return s if len(s) <= n else s[: n - 1] + "…"


def report(path: Path, detail: bool, out):
    rows = load(path)
    if not rows:
        return
    turns, tools = [], Counter()
    for t, d in rows:
        if is_request(d):
            turns.append({"start": t, "end": t, "text": text_of(d), "events": []})
        elif d.get("type") == "assistant":
            for name, hint in tool_calls(d):
                tools[name] += 1
                if turns:
                    turns[-1]["events"].append((t, f"{name}: {hint}"))
            if turns:
                turns[-1]["end"] = t
                said = text_of(d).strip()
                if said:
                    turns[-1]["events"].append((t, "SAY: " + short(said, 110)))

    print("=" * 100, file=out)
    print(f"{path.stem}  {rows[0][0]:%Y-%m-%d %H:%M} → {rows[-1][0]:%H:%M}", file=out)
    rhino = sum(v for k, v in tools.items() if k.startswith("mcp__rhino"))
    print(f"tool calls {sum(tools.values())} (Rhino MCP {rhino}, Rhino launches {tools['mcp__rhino__spawn_slot']}, "
          f"computer use {sum(v for k, v in tools.items() if k.startswith('mcp__computer-use'))})", file=out)
    worked = 0.0
    for k, tr in enumerate(turns):
        minutes = (tr["end"] - tr["start"]).total_seconds() / 60
        worked += minutes
        print(f"[{k}] {tr['start']:%H:%M:%S} → {tr['end']:%H:%M:%S}  {minutes:6.1f} min | {short(tr['text'], 110)}", file=out)
        if detail:
            for t, e in tr["events"]:
                print(f"        {t:%H:%M:%S}  {e}", file=out)
    span = (rows[-1][0] - rows[0][0]).total_seconds() / 60
    print(f"Claude worked {worked:.1f} min of a {span:.1f} min session", file=out)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--projects", type=Path, default=DEFAULT_PROJECTS, help="folder holding the session .jsonl files")
    ap.add_argument("--session", action="append", default=[], help="only sessions whose id starts with this (repeatable)")
    ap.add_argument("--detail", action="store_true", help="list tool calls and progress messages inside each turn")
    ap.add_argument("-o", "--output", type=Path, help="write to this file (UTF-8) instead of the console")
    args = ap.parse_args()

    files = sorted(args.projects.glob("*.jsonl"), key=lambda p: p.stat().st_mtime)
    if args.session:
        files = [f for f in files if any(f.stem.startswith(s) for s in args.session)]
    if not files:
        ap.error(f"No session files found in {args.projects}.")
    out = open(args.output, "w", encoding="utf-8") if args.output else sys.stdout
    if out is sys.stdout:
        sys.stdout.reconfigure(encoding="utf-8")
    try:
        for f in files:
            report(f, args.detail, out)
    finally:
        if out is not sys.stdout:
            out.close()


if __name__ == "__main__":
    main()
