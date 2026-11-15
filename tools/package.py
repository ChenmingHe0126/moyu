"""Build the release packages for Moyu: the Yak package for Rhino's Package Manager and the zip for food4rhino.

    python tools/package.py            # build the plugin (Rhino must be closed), then package
    python tools/package.py --no-build # package the existing Release outputs (refused when they were built from another <Version>)
    python tools/package.py --out DIR  # write to DIR instead of dist/

Outputs go to dist/ (ignored by git); only these are replaced, anything else in dist/ is left alone:
    dist/moyu-<version>-rh8_0-win.yak     the Package Manager package for Windows (net48 + net7.0-windows builds, examples,
                                          guide PDFs, quick start)
    dist/moyu-<version>-rh8_0-mac.yak     the same for Rhino for Mac (the plain net7.0 build, which carries the Mac code)
    dist/Moyu-<version>-food4rhino.zip    both .yak files plus the bare .gha files, examples, guide PDFs, QUICKSTART, INSTALL
                                          and LICENSE
    dist/SHA256SUMS.txt

Every packaged file is checked for this machine's paths and user name before anything is written; examples come from
packaging/examples (regenerate with: dotnet run -c Release --project tests/Moyu.RhinoTests -- --examples packaging/examples).
"""
import argparse
import hashlib
import os
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src" / "Moyu"
PACKAGING = ROOT / "packaging"
DIST = ROOT / "dist"
YAK = Path(os.environ.get("YAK_EXE") or next((p for p in (r"C:\Program Files\Rhino 8\System\Yak.exe", r"E:\Rhino 8\System\Yak.exe") if os.path.exists(p)), r"C:\Program Files\Rhino 8\System\Yak.exe"))
# One Yak distribution per platform. Rhino 8 loads the build that matches its runtime from these folders inside the
# package: on Windows net48 (.NET Framework) or net7.0 (.NET Core, built as net7.0-windows); Rhino for Mac only runs .NET
# Core and gets the plain net7.0 build, which references WinForms and System.Drawing the way Rhino for Mac provides them.
RELEASE = SRC / "bin" / "Release"
PLATFORMS = {
    "win": {"net48": RELEASE / "net48", "net7.0": RELEASE / "net7.0-windows"},
    "mac": {"net7.0": RELEASE / "net7.0"},
}
# Where each build goes in the food4rhino zip, for the hand install INSTALL.md describes.
ZIP_GHA = {("win", "net48"): "windows/net48", ("win", "net7.0"): "windows/net7.0", ("mac", "net7.0"): "mac"}
EXAMPLES = PACKAGING / "examples"


def private_markers():
    """Strings that would identify this machine or its user if they turned up inside a released file."""
    marks = {str(ROOT), str(Path.home()), os.environ.get("TEMP", ""), os.environ.get("APPDATA", "")}
    user = os.environ.get("USERNAME", "")
    if len(user) >= 3:
        marks.add(user)
    return [m for m in marks if len(m) >= 3]


def check_private(name, data):
    low = data.lower()
    for m in private_markers():
        for enc in ("utf-8", "utf-16-le"):
            if m.lower().encode(enc) in low:
                sys.exit(f"{name} contains '{m}' ({enc}); fix the build (PathMap) or the file before releasing")


def check_keywords(manifest):
    """The Yak server rejects a push (400) unless every keyword is letters, digits, dashes and underscores; yak build
    does not check. The guid:<id> keywords that tie a Grasshopper plug-in to its package are the one allowed form."""
    block = re.search(r"^keywords:\n((?:[ \t]+.*\n?)*)", manifest, re.M)
    words = re.findall(r"^[ \t]+-[ \t]*(\S.*?)[ \t]*$", block.group(1), re.M) if block else []
    bad = [w for w in words if not re.fullmatch(r"[A-Za-z0-9_-]+|guid:[0-9A-Fa-f-]{36}", w)]
    if bad:
        sys.exit(f"manifest.yml keywords the Yak server would reject: {bad} (use letters, digits, - and _ only)")


def payload():
    """(archive name, source path) of the extra files every package carries."""
    files = [("QUICKSTART.md", PACKAGING / "QUICKSTART.md"), ("LICENSE.txt", ROOT / "LICENSE")]
    for guide in ("Moyu-Guide-ZH.pdf", "Moyu-Guide-EN.pdf"):
        src = PACKAGING / "guide" / guide
        if not src.exists():
            sys.exit(f"{src.relative_to(ROOT)} is missing; build it with: python tools/make_guide.py")
        files.append((guide, src))
    examples = sorted(EXAMPLES.glob("*.gh"))
    if not examples:
        sys.exit(f"No example files in {EXAMPLES}")
    files += [(f"examples/{p.name}", p) for p in examples]
    return files


def version():
    m = re.search(r"<Version>([^<]+)</Version>", (SRC / "Moyu.csproj").read_text(encoding="utf-8"))
    if not m:
        sys.exit("No <Version> in Moyu.csproj")
    return m.group(1).strip()


def rhino_running():
    out = subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower()
    return "rhino.exe" in out or "compute.geometry" in out


def build():
    if rhino_running():
        sys.exit("Close Rhino first: it (or Hops' compute.geometry) holds Moyu.gha and the build would fail.")
    print("dotnet build src/Moyu -c Release")
    run = subprocess.run(["dotnet", "build", str(SRC), "-c", "Release"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    text = run.stdout + run.stderr
    warnings = re.findall(r"(\d+)\s*(?:个警告|Warning\(s\))", text)
    errors = re.findall(r"(\d+)\s*(?:个错误|Error\(s\))", text)
    if run.returncode != 0 or not errors or int(errors[-1]) != 0:
        print(text[-4000:])
        sys.exit("Build failed")
    if warnings and int(warnings[-1]) != 0:
        print(text[-4000:])
        sys.exit("The release build must have 0 warnings")
    print("built with 0 warnings")


def gha_version(gha):
    """The informational version of a built Moyu.gha without its "+commit" metadata: the <Version> it was built from."""
    path = str(gha).replace("'", "''")
    cmd = ["powershell", "-NoProfile", "-Command", f"[System.Diagnostics.FileVersionInfo]::GetVersionInfo('{path}').ProductVersion"]
    run = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    return run.stdout.strip().split("+")[0]


def check_builds(ver):
    # A pushed yak version can never be replaced, so stale outputs from another <Version> must not be packaged under this one.
    for builds in PLATFORMS.values():
        for src in builds.values():
            gha = src / "Moyu.gha"
            if not gha.exists():
                sys.exit(f"Missing {gha}; build first")
            built = gha_version(gha)
            if built != ver:
                sys.exit(f"{gha.relative_to(ROOT)} is {built or 'unversioned'} but Moyu.csproj says {ver}; build first (run without --no-build)")
            check_private(str(gha.relative_to(ROOT)), gha.read_bytes())
    for name, src in payload():
        check_private(name, src.read_bytes())


def clean():
    """Removes what an earlier run wrote, and nothing else (dist/ also holds other work)."""
    DIST.mkdir(parents=True, exist_ok=True)
    for p in list(DIST.glob("yak")) + list(DIST.glob("yak-*")):
        shutil.rmtree(p)
    for p in list(DIST.glob("moyu-*.yak")) + list(DIST.glob("Moyu-*-food4rhino.zip")) + list(DIST.glob("SHA256SUMS.txt")):
        p.unlink()


def stage(ver, platform):
    yak_dir = DIST / f"yak-{platform}"
    yak_dir.mkdir(parents=True)
    for folder, src in PLATFORMS[platform].items():
        gha = src / "Moyu.gha"
        (yak_dir / folder).mkdir()
        shutil.copy2(gha, yak_dir / folder / "Moyu.gha")
    manifest = (PACKAGING / "manifest.yml").read_text(encoding="utf-8").replace("{version}", ver)
    check_keywords(manifest)
    (yak_dir / "manifest.yml").write_text(manifest, encoding="utf-8")
    shutil.copy2(PACKAGING / "icon.png", yak_dir / "icon.png")
    for name, src in payload():
        (yak_dir / name).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, yak_dir / name)
    return yak_dir


def yak_build(yak_dir, platform):
    if not YAK.exists():
        sys.exit(f"Yak not found at {YAK}; set YAK_EXE")
    cmd = [str(YAK), "build", "--platform", platform]
    print(" ".join(cmd), "in", yak_dir)
    run = subprocess.run(cmd, cwd=yak_dir, capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(run.stdout.strip())
    if run.returncode != 0:
        print(run.stderr.strip())
        sys.exit("yak build failed")
    # Yak only warns (and exits 0) when the assemblies' version differs from the manifest's; a pre-release manifest
    # (1.0.1-beta over 1.0.1 assemblies) is still a match.
    m = re.search(r"\[warn\] Content version doesn't match manifest: '([^']+)' != '([^']+)'", run.stdout + run.stderr)
    if m and m.group(2).split("-")[0] != m.group(1):
        sys.exit(f"Release outputs are {m.group(1)} but Moyu.csproj says {m.group(2)}; build first (run without --no-build)")
    yaks = sorted(yak_dir.glob("*.yak"))
    if len(yaks) != 1:
        sys.exit(f"Expected one .yak, found {yaks}")
    target = DIST / yaks[0].name
    shutil.move(str(yaks[0]), target)
    return target


def food4rhino_zip(ver, yak_files):
    name = f"Moyu-{ver}"
    zpath = DIST / f"{name}-food4rhino.zip"
    install = (PACKAGING / "INSTALL.md").read_text(encoding="utf-8").replace("{version}", ver)
    for platform, yak_file in yak_files.items():
        install = install.replace("{yak_" + platform + "}", yak_file.name)
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for yak_file in yak_files.values():
            z.write(yak_file, f"{name}/{yak_file.name}")
        for platform, builds in PLATFORMS.items():
            for folder, src in builds.items():
                z.write(src / "Moyu.gha", f"{name}/{ZIP_GHA[(platform, folder)]}/Moyu.gha")
        z.writestr(f"{name}/INSTALL.md", install)
        for arc, src in payload():
            z.write(src, f"{name}/{arc}")
    return zpath


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    # Yak and dotnet print UTF-8; a Chinese console defaults to GBK and would choke on it.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--no-build", action="store_true", help="package the existing Release outputs without building")
    ap.add_argument("--out", help="write the packages to this folder instead of dist/")
    args = ap.parse_args()
    global DIST
    if args.out:
        DIST = Path(args.out).resolve()
    ver = version()
    print(f"Moyu {ver}")
    if not args.no_build:
        build()
    check_builds(ver)
    clean()
    yak_files = {platform: yak_build(stage(ver, platform), platform) for platform in PLATFORMS}
    zpath = food4rhino_zip(ver, yak_files)
    sums = []
    for p in list(yak_files.values()) + [zpath]:
        sums.append(f"{sha256(p)}  {p.name}")
        print(f"{p.name}  {p.stat().st_size:,} bytes")
    (DIST / "SHA256SUMS.txt").write_text("\n".join(sums) + "\n", encoding="utf-8")
    for yak_file in yak_files.values():
        with zipfile.ZipFile(yak_file) as z:
            print(f"{yak_file.name}:", ", ".join(z.namelist()))


if __name__ == "__main__":
    main()
