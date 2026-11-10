"""Collects everything the edit reads into dist/intro-video/public (git-ignored): recorded footage, the music
segment, cropped install clips and stills. Run from anywhere: python tools/intro-video/edit/prepare_assets.py"""
import json, shutil, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PUB = ROOT / "dist/intro-video/public"
FOOT = ROOT / "dist/intro-video/footage"
OLD = ROOT / "dist/content-workflow/GH_Agentic_20261108_process_montage_v1"
MUSIC = ROOT / "dist/intro-video/music-candidates/Pamgaea.mp3"
OLD_CLIPS = OLD / "交付_流程快剪_v1/03_剪映工程/Moyu_Agent开发全流程_竖屏快剪_v1/assets/video"

# "Pamgaea": 94 BPM exactly, first beat at 0.630 s. Bars 38–63 (26 bars, 104 beats): it opens without the bass (a
# gentle start), the bass comes back about 15 s in, and it ends on the track's own ending.
BEAT_S = 60 / 94
MUSIC_START, MUSIC_LEN = 0.630 + 38 * 4 * BEAT_S, 104 * BEAT_S


def run(*args):
    subprocess.run(["ffmpeg", "-v", "error", "-y", *map(str, args)], check=True)


def main():
    for d in ("footage", "stills", "audio"):
        (PUB / d).mkdir(parents=True, exist_ok=True)
    # Music: cut on the beat at a phrase start.
    run("-ss", MUSIC_START, "-t", MUSIC_LEN, "-i", MUSIC,
        # Comes in gently over the first five beats (up to the first boss key) and stays in the background.
        "-af", f"loudnorm=I=-19.5:TP=-2:LRA=11,afade=t=in:d=3.2:curve=qsin,afade=t=out:st={MUSIC_LEN - 0.4}:d=0.4",
        "-ar", "48000", "-ac", "2", PUB / "audio/bgm.wav")
    # Recorded footage: re-encode all-intra so the renderer seeks exactly, whatever the recorder wrote.
    for clip in sorted(FOOT.glob("*/*.mp4")):
        out = PUB / "footage" / clip.name
        if out.exists() and out.stat().st_mtime >= clip.stat().st_mtime:
            continue
        # Two seconds of the last frame on the end, so a scene that runs a little longer holds the final state.
        run("-i", clip, "-an", "-vf", "tpad=stop_mode=clone:stop_duration=2", "-c:v", "libx264", "-preset", "fast", "-crf", "12", "-g", "1",
            "-pix_fmt", "yuv420p", out)
        print("footage", clip.name)
    # Existing screen recordings of the Package Manager and the Examples component (1000x1000 with bands): keep the window.
    for name, crop in (("36_package_manager.mp4", "1000:764:0:96"), ("37_examples.mp4", "1000:872:0:64")):
        src = OLD_CLIPS / name
        if src.exists():
            run("-i", src, "-an", "-vf", f"crop={crop}", "-c:v", "libx264", "-crf", "12", "-g", "1", "-pix_fmt", "yuv420p",
                PUB / "footage" / name)
    for still in ("dist/intro-video/stills/gh_ribbon_moyu.png", "packaging/icon-512.png"):
        if (ROOT / still).exists():
            shutil.copy2(ROOT / still, PUB / "stills" / Path(still).name)
    # The edit shows a placeholder for footage that is not recorded yet: tell it what exists.
    names = sorted(p.name for p in (PUB / "footage").glob("*.mp4"))
    (Path(__file__).parent / "src/assets.json").write_text(json.dumps(names, indent=1) + "\n", encoding="utf-8")
    print("public:", *sorted(p.relative_to(PUB).as_posix() for p in PUB.rglob("*") if p.is_file()), sep="\n  ")


if __name__ == "__main__":
    sys.exit(main())
