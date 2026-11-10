# Offline recorder for the intro video

Records full-bleed **vertical 1080×1920, 60 fps** footage of Moyu's games and solvers inside a running Rhino 8,
frame by frame and off real time: a shot advances the scene by a fixed 1/60 s, the view is captured with
`RhinoView.CaptureToBitmap`, and the raw frames are piped to ffmpeg. A bot plays, so highlight moments can be staged
exactly where the edit needs them. Nothing here ships with the plugin.

```
rec/
  common/Recorder.cs   IShot, Rec (offline control of a GameSession), Entry (Run/Status/Cancel/List), Views.Portrait
  common/Rec.props     shared build settings: every shot folder compiles common\*.cs + its own *.cs into its own DLL
  ref/Moyu.dll         copy of the installed Moyu.gha 1.0.1 (git-ignored), referenced for its types
  rebound/             worked example: Rebound.cs + MoyuRecRebound.csproj
  voxstack/ serpentine/ swift/ topology/   one folder per subject
  loader.cs.txt        the run_csharp snippet that loads a DLL and calls Entry
../sheet.py            contact sheet of video frames: python sheet.py out.jpg <cols> <thumbWidth> file.mp4@seconds …
```

## Workflow

1. Write or edit the shot class (`IShot`) in your folder. Build: `dotnet build -c Release -v q` in that folder
   (output `bin\MoyuRec<Name>.dll`). Rebuilding while Rhino runs is fine: the loader reads the DLL's bytes, the file is
   never locked.
2. In your own Rhino 8 slot (`spawn_slot` with version "8"), make sure Grasshopper and Moyu are loaded once:
   `Rhino.PlugIns.PlugIn.LoadPlugIn(new Guid("b45a29b1-4343-4035-989e-044e8580d9cf"));` then
   `RhinoApp.RunScript("_-Grasshopper _W _L _Enter", false);` (the Moyu assembly then shows up in
   `AppDomain.CurrentDomain.GetAssemblies()` from `…\packages\8.0\moyu\1.0.1\net7.0\Moyu.gha`).
3. Run the loader snippet (`loader.cs.txt`, with your DLL path, shot class, output path and args) through
   `run_csharp`. `Run` returns at once; the recording runs on Rhino's UI thread a few frames per timer tick. Poll
   `Status()` (or wait for `<out>.mp4.status.txt`, written when the job ends, with notes and any exception).
4. Look at the result: `python tools/intro-video/sheet.py sheet.jpg 6 360 clip.mp4@0.5 clip.mp4@2 …` then Read the
   jpg. Iterate with `scale=0.5` (540×960, about 55 frames/s) and a small `frames=`; record the keeper at full size.

Args are `key=value;key=value`. The recorder's own keys: `frames` (override the shot's length), `scale` (0.5 = half
size preview), `vw` (on-screen width of the floating view, default 540), `ss` (do not use, see below). Everything
else is read by the shot through `rec.Arg("name", fallback)`.

## What is already known to work (Rebound, measured 2026-11-09)

- `#r "…dll"` is not honoured by `run_csharp`; hence the separately built DLL loaded with
  `Assembly.Load(File.ReadAllBytes(path))`. Its references to Moyu / RhinoCommon / Grasshopper resolve to the
  assemblies Rhino already has. Public Moyu types are used directly; private and protected members of the session go
  through `rec.SGet / SSet / SCall` (reflection on the session's type, then on `GameSession`).
- `Views.Portrait(doc)` makes (or reuses) a floating view named `MoyuCam` whose viewport is exactly 540×960.
  `GameSession.Begin` maximises the view, which blows a floating view up to the whole screen; `rec.Adopt(session)`
  undoes that, disposes the real-time clock, switches auto boss mode and mouse steering off.
- `rec.Step()` is `GameSession.Tick` without the focus checks (Rhino need not be the foreground window): countdown,
  `TickAlways`, `TickGame`, game-over bookkeeping, boss-mode time. `rec.SetCountdown(0)` skips the 3-2-1.
  `rec.Key(Keys, down)` / `rec.Tap(Keys)` feed keys as the game sees them while playing. `session.ToggleBoss()` is the
  boss key; `session.EnterBoss()` is what a focus loss (Alt+Tab) does.
- Capture exactly **1080×1920** (the default). On this machine the DPI scale is 1.75, so the HUD is drawn with
  `UiScale = min(1.75, 1920/720) = 1.75` and looks right. Larger captures (`ss=1.33`) are rendered in tiles: the HUD
  is cut off at the top and the speed drops from about 46 to 11 frames/s. Do not use `ss`.
- The session's own redraws are skipped; `CaptureToBitmap` renders each frame itself.
- A 14 s shot (840 frames) takes about 20 s to record at full size.

## Rules

- Stay inside your own folder under `rec/` and your own output folder `dist/intro-video/footage/<name>/` (git-ignored).
  `common/Recorder.cs` is shared: do not edit it; if it lacks something, add a helper in your own folder and say so
  in your report.
- Never modify `src/`, `packaging/`, `tests/` or the installed package. Never change persistent Rhino settings
  (options, display modes, appearance colours); if a shot needs a clean background, draw it in a `DisplayConduit`
  or use document-level settings in your throwaway document.
- Work only in the Rhino slot you spawned and close it when done (`close_slot`). Do not touch other slots.
- A finished game writes high scores to `%APPDATA%\Moyu\*-scores.txt`. Do not let a recorded game reach game over
  unless the shot needs it (a backup of the user's files is in `dist/intro-video/scores-backup/`).
- HUD language: set `session.Language = UiLanguage.Chinese` (boss-mode disguises are always English).
