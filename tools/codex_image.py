"""Generate an image with the local Codex CLI (its built-in image tool) and save it to a path.

Usage:
  python tools/codex_image.py --check
  python tools/codex_image.py --prompt-file PROMPT.txt --out docs/images/x.png [--ref style.png ...]

Codex runs non-interactively (`codex exec`) with the user's own Codex login, is asked to save the PNG at --out,
and if it does not, the newest image it wrote under ~/.codex/generated_images/ during this run is copied there
(that cache folder is how Codex keeps generated images; it is not a documented interface, so --check after Codex
updates). The prompt and reference images are sent to OpenAI and the run counts against Codex usage.
"""
import argparse
import glob
import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
GENERATED = Path(os.environ.get("CODEX_HOME", Path.home() / ".codex")) / "generated_images"


def find_codex() -> str:
    """The `codex` on PATH (npm install), else the newest copy bundled with the Codex desktop app."""
    found = shutil.which("codex")
    if found:
        return found
    bundled = glob.glob(os.path.join(os.environ.get("LOCALAPPDATA", ""), "OpenAI", "Codex", "bin", "*", "codex.exe"))
    if bundled:
        return max(bundled, key=os.path.getmtime)
    sys.exit("Codex CLI not found: install it with `npm install -g @openai/codex` or install the Codex app.")


def run(cmd, **kw):
    return subprocess.run(cmd, text=True, encoding="utf-8", errors="replace", **kw)


def check(codex: str) -> None:
    print("codex:", codex)
    print(run([codex, "--version"], capture_output=True).stdout.strip())
    status = run([codex, "login", "status"], capture_output=True)
    print((status.stdout + status.stderr).strip())
    print("generated images folder:", GENERATED, "(exists)" if GENERATED.exists() else "(not yet)")


def snapshot() -> set:
    return {p for p in GENERATED.rglob("*") if p.is_file()} if GENERATED.exists() else set()


def describe(path: Path) -> None:
    try:
        from PIL import Image
    except ImportError:
        print(f"saved {path} ({path.stat().st_size:,} bytes)")
        return
    with Image.open(path) as im:
        alpha = im.getchannel("A").getextrema() if "A" in im.getbands() else None
        print(f"image: {im.size[0]}x{im.size[1]} {im.mode}, alpha range {alpha}")
        if alpha is None or alpha[0] > 8:
            print("warning: no transparent pixels; tools/make_icons.py needs a transparent background")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="show which Codex is used and whether it is logged in")
    ap.add_argument("--prompt-file", help="text file with the image prompt")
    ap.add_argument("--out", help="where to save the PNG (relative to the repo root or absolute)")
    ap.add_argument("--ref", action="append", default=[], help="reference image to attach (repeatable)")
    ap.add_argument("--timeout", type=int, default=1200, help="seconds to wait for Codex (default 1200)")
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")

    codex = find_codex()
    if args.check:
        check(codex)
        return
    if not args.prompt_file or not args.out:
        ap.error("--prompt-file and --out are required (or use --check)")

    out = Path(args.out)
    if not out.is_absolute():
        out = ROOT / out
    out.parent.mkdir(parents=True, exist_ok=True)
    refs = [str((ROOT / r) if not Path(r).is_absolute() else Path(r)) for r in args.ref]
    for r in refs:
        if not Path(r).exists():
            sys.exit(f"reference image not found: {r}")

    prompt = Path(args.prompt_file).read_text(encoding="utf-8")
    # A generation record (docs/moyu-icons-*-generation-prompt.txt) keeps its header above this line; send the prompt only.
    marker = "\nGENERATION PROMPT\n"
    if marker in prompt:
        prompt = prompt.split(marker, 1)[1]
    instructions = (
        prompt.strip()
        + "\n\n---\nUse your built-in image generation tool to create exactly this image"
        + (" (the attached image is the style reference described above)" if refs else "")
        + f". Save the final PNG, with a real transparent alpha background, to:\n{out}\n"
        + "Do not change any other file. When done, reply with one line: SAVED: <absolute path>"
    )

    before = snapshot()
    start = time.time()
    last = Path(tempfile.gettempdir()) / f"codex_image_{os.getpid()}.txt"
    cmd = [codex, "exec", "-s", "workspace-write", "-C", str(ROOT), "--skip-git-repo-check", "-o", str(last)]
    for r in refs:
        cmd += ["-i", r]
    print(f"running Codex ({codex}) … this usually takes a few minutes", flush=True)
    try:
        result = run(cmd, input=instructions, capture_output=True, timeout=args.timeout)
    except subprocess.TimeoutExpired:
        sys.exit(f"Codex did not finish within {args.timeout} s")
    if result.returncode != 0:
        print(result.stdout[-3000:])
        print(result.stderr[-3000:], file=sys.stderr)
        sys.exit(f"codex exec failed with exit code {result.returncode}")
    reply = last.read_text(encoding="utf-8", errors="replace").strip() if last.exists() else ""
    print("codex:", reply[-500:])

    if not (out.exists() and out.stat().st_mtime >= start - 1):
        fresh = sorted((p for p in snapshot() - before if p.suffix.lower() in (".png", ".jpg", ".webp")), key=lambda p: p.stat().st_mtime)
        if not fresh:
            sys.exit("Codex finished but no new image was found (neither at --out nor under generated_images).")
        shutil.copyfile(fresh[-1], out)
        print(f"copied {fresh[-1]} → {out}")
    describe(out)
    print(f"SAVED: {out}")


if __name__ == "__main__":
    main()
