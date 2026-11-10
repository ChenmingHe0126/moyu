# Swift (Section panel) — offline-capture cheat sheet

All paths are under `src\Moyu\`. Nothing was built or run; everything below is from reading the source. Points I could not confirm from source are marked **verify**.

Type names for reflection (`asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Moyu")`):

| Type | Access | File |
|---|---|---|
| `Moyu.Games.Common.GameSession` | public abstract | `Games\Common\GameSession.cs` |
| `Moyu.Games.Common.GameClock` | public sealed | `Games\Common\GameClock.cs` |
| `Moyu.Games.Common.KeyboardHook` | public sealed | `Games\Common\KeyboardHook.cs` |
| `Moyu.Games.Common.GameConduit` | public abstract | `Games\Common\GameConduit.cs` |
| `Moyu.Games.Common.Core.UiLanguage` | public enum `{ Chinese=0, English=1 }` | `Games\Common\Core\UiLanguage.cs` |
| `Moyu.Games.Swift.SwiftSession` | public sealed | `Games\Swift\SwiftSession.cs` |
| `Moyu.Games.Swift.SwiftConduit` | **internal** sealed, public ctor `(SwiftSession)` | `Games\Swift\SwiftConduit.cs` |
| `Moyu.Games.Swift.SectionDisguise` | public sealed | `Games\Swift\SectionDisguise.cs` |
| `Moyu.Games.Swift.Core.SwiftGame`, `SwiftSettings`, `Wall`, `Feather`, `V2` | public | `Games\Swift\Core\SwiftGame.cs` |
| `SwiftMode { Classic=0, Zen=1 }`, `Exposure { Sheltered=0, Normal=1, Exposed=2 }`, `SwiftState { Ready=0, Flying=1, Crashed=2, Over=3, Paused=4 }` | public enums | same |
| `Moyu.Components.Section.AirflowStudyComponent` | public | `Components\Section\AirflowStudyComponent.cs` |

---

## 1. START

**Factory** (`SwiftSession.cs` L55–62); the constructor is private (L36):

```csharp
public static SwiftSession Start(SwiftSettings settings, int fixedSeed, Plane plane, double size,
                                 object owner, Action<GameSession> changed, out string error)
```

It runs `PickView` → `StopCurrent()` → `new SwiftSession(...)` → `session.Begin(new SwiftConduit(session), out error)`. It returns null with `error` set on failure.

**Arguments and the component's defaults** (`AirflowStudyComponent.Run` L118–132):

- **`settings`**: `new SwiftSettings()` has public fields `Mode=Classic`, `Exposure=Normal`, `Opening=1`, `Bay=1`, `Seed=1`. The session overwrites `Seed` (ctor L46): `Settings.Seed = fixedSeed != 0 ? fixedSeed : Environment.TickCount`.
- **`fixedSeed`**: 0 means a random course. Pass a non-zero value for a repeatable one.
- **`plane`**: `AirflowStudyComponent.DefaultPlane` (public static, L47) is `new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis)`. Course X is world X, up is world Z, and "towards the viewer" (`Plane.ZAxis`) is world −Y.
- **`size`**: `AirflowStudyComponent.DefaultSize()` (public static, L50) is 0.75 m expressed in model units. `session.Metres = Size * ToMetres` feeds the SPEED figure and the boss metrics.
- **`owner`**: may be `null`. The language then comes from `GameSession.SystemLanguage()` (ctor L106), and `IsRunning` on the GH components stays false.
- **`changed`**: may be `null`, since `Changed()` is `_changed?.Invoke(this)` (L222). With null there are no Grasshopper side effects at all.

**View picking** — `GameSession.PickView(out RhinoDoc doc, out string error)` (public static, L121–130) takes `RhinoDoc.ActiveDoc.Views.ActiveView`. If that is a `RhinoPageView` it takes the first perspective standard view instead. There is no view parameter.

To target a floating view, either:

- set `doc.Views.ActiveView = floatingView` immediately before `Start`, or
- bypass `PickView` by reflection:

```csharp
var ctor = tSwiftSession.GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance)[0];
// (SwiftSettings, int, Plane, double, object owner, Action<GameSession> changed, RhinoDoc, RhinoView)
tGameSession.GetMethod("StopCurrent").Invoke(null, null);
var s = ctor.Invoke(new object[]{ settings, seed, plane, size, null, null, doc, view });
var conduit = Activator.CreateInstance(tSwiftConduit, s);
var args = new object[]{ conduit, null };
bool ok = (bool)tGameSession.GetMethod("Begin", BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s, args); // args[1] = error
```

**`protected bool Begin(GameConduit conduit, out string error)`** (`GameSession.cs` L139–184), in order:

1. `Current?.Stop(); Current = this;`
2. `_conduit.Enabled = true`
3. `_hook = new KeyboardHook(OnKey)`, a `WH_KEYBOARD` hook on the calling thread. If it is not installed, `Stop()` runs and `Begin` returns false.
4. `_mouse = new OrbitMouse(this){Enabled=true}`, and subscribes `RhinoDoc.CloseDocument` and `RhinoApp.Closing`.
5. If the viewport is parallel: `ChangeToPerspectiveProjection(true, 28)`.
6. **`View.Maximized = true`**; construction grid, construction axes and world axes are turned off.
7. `SetStartCamera(vp)`.
8. `StartCountdown(true)`: `PauseGame()`, `Countdown = 3`, `CountdownIsStart = true`, `PlayTime = 0`.
9. `_clock = new GameClock(Tick)`, a WinForms timer at 15 ms.
10. `RhinoApp.SetFocusToMainWindow(); View.Redraw(); Changed();`

**End**: `session.Stop()` (public, L487) or `GameSession.StopCurrent()`. It restores the user's camera, grid, axes and `Maximized`, disposes the hook and clock, and releases the conduit.

Other useful public members:
- `GameSession.Current` (static getter)
- `session.Game` (the `SwiftGame`; replaced on `Restart`)
- `session.Settings`, `FixedSeed`, `Plane`, `Size`, `Metres`
- `session.At(x, y, z=0)` (view units → world) and `session.AtCourse(x, y, z=0)`

---

## 2. FRAME LOOP

**Real clock**: `GameClock.OnTick` (L26–39) computes `dt = Math.Min(0.1, now - _last)` from a `Stopwatch` and calls `GameSession.Tick(double dt)`, which is **private** (L234–293).

**What `Tick(dt)` does:**

1. Returns if `Stopped`. If `!ViewAlive()` (`Doc.Views.Find(ViewportId) == null`) it calls `Stop()`.
2. `Time += dt`.
3. `if (AutoBoss && !Boss && !GameClock.AppIsForeground()) EnterBoss();` — this compares the foreground window's process id with Rhino's.
4. If `Boss`: `BossTime += dt`, redraw only when `(int)(BossTime/1.2)` changes, then return.
5. `command = Rhino.Commands.Command.InCommand()`. If `!IsOver && (command || !OwnsFocus())`:
   - during a countdown it only redraws and returns, so **the countdown stalls**;
   - otherwise, if the game is active, it calls `Pause()`.
   - `OwnsFocus()` (L299) requires the Win32 `GetFocus()` root to be Rhino's main window or the game view's own top-level frame.
6. If not in a command and the viewport is parallel: `ResetCamera()`.
7. `TickAlways(dt)` (a no-op for Swift).
8. If `Countdown > 0`: `Countdown -= dt`; on reaching 0 it calls `ResumeGame()` and `Changed()`; then redraw and return.
9. `if (!IsGamePaused) PlayTime += dt;`
10. `TickGame(dt)`, which for Swift is `Game.Tick(dt); KeepCameraAbove();` (`SwiftSession.cs` L82–86).
11. `NoteGameOver()`: once per round it sets `OverTime = 0` and calls `OnGameOver()`, which **appends to `%APPDATA%\Moyu\swift-scores.txt`**.
12. `if (IsOver) OverTime += dt; if (NeedsRedraw) Redraw();`
13. Any exception prints `"Swift stopped: …"` and calls `Stop()`.

**Core stepping**: `SwiftGame.Tick(double dt)` (public, L252) does `_pending += Math.Min(dt, 0.25)` and runs `StepOnce(1/240)` while `_pending >= Step`. The core has no wall-clock dependence. To get exactly 4 sub-steps every frame, pass `dt = 1.0/60 + 1e-9`; a plain `1.0/60` can occasionally give a 3-step then 5-step frame from rounding.

**Every wall-clock dependence:**

| Where | What | Effect offline |
|---|---|---|
| `GameClock._watch` (Stopwatch) | real dt | Gone once the timer is stopped |
| `KeyboardHook._clock` (Stopwatch) | lost-release test (250 ms) | Real keys only |
| `GameConduit._backgroundClock` (Stopwatch, L45, used L135) | During a palette blend the sky bitmap is repainted at most every 120 ms of real time | Cosmetic only; sky steps slightly unevenly during the 1.6 s blend |
| `Environment.TickCount` (`SwiftSession` L46, L90) | seed when `FixedSeed == 0` | Pass a non-zero seed |
| `DateTime.Now` (`GameScores.Record`) | date in the score file line | Only on game over |
| `GameClock.AppIsForeground()`, `GetFocus()`, `Command.InCommand()` | live Win32 / Rhino state inside `Tick` | See below |

**Stopping the real clock** (keeps `Stop()` working later):

```csharp
var clock = tGameSession.GetField("_clock", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(s);
((System.Windows.Forms.Timer)clock.GetType().GetField("_timer", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(clock)).Stop();
```

**Option A — call the real method**:

```csharp
s.AutoBoss = false;
tGameSession.GetMethod("Tick", BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s, new object[]{ dt });
```

This works only while `Command.InCommand()` is false and Rhino's main window (or the floating view's frame) has keyboard focus. If another app is in front, `GetFocus()` returns 0 on Rhino's thread, so the countdown never ends and play auto-pauses. It also redraws on screen every frame.

**Option B — mirror `Tick` (recommended; no focus, command or foreground dependence).** Per frame:

```
Time += dt
if (Boss)                BossTime += dt
else if (Countdown > 0)  { Countdown -= dt; if (Countdown <= 0) { Countdown = 0; game.Resume(); } }
else                     { if (game.State != Paused) PlayTime += dt;
                           game.Tick(dt);                 // public
                           if (game.IsOver) OverTime += dt; }
capture
```

`Time`, `BossTime`, `Countdown`, `PlayTime` and `OverTime` are public auto-properties with `private set` on `GameSession`. Set them with `tGameSession.GetProperty("Time").SetValue(s, v)`. The `PropertyInfo` must come from **`GameSession`**, not `SwiftSession`, or the private setter is not found. The backing field `<Time>k__BackingField` also works.

**Conduit animation time sources** (all deterministic except the sky throttle):

- **`session.Time`**: chapter palette blend (`SwiftConduit._blendStart`, `BlendSeconds = 1.6`, L78–98), chapter card (`_levelAt`, 2.4 s, L448–460), and the take-off prompt pulse (L421).
- **`session.PlayTime`**: key-hint fade, full for 7 s then gone by 8.5 s (`GameConduit` L354); take-off prompt shown when `PlayTime > 0.2`.
- **`session.Countdown`**: ring and number.
- **`session.OverTime`**: result wash and fades, crash age.
- **`session.BossTime`**: the "iteration N / 400" counter.
- **`game.Clock`, `LastFlap`, `Vy`**: wings, tilt, flap puff, feather bob.
- **`game.Elapsed`**: shutters.
- **Position**: the "+1" pops use `(BirdCourseX − w.Middle)/Speed`.

`_blendStart` and `_levelAt` are latched at draw time, so draw every frame.

---

## 3. INPUT

**Chain**: Windows → `KeyboardHook.Proc` (L108) → `KeyTransitions.Fresh` → `Handler(Keys key, bool down, bool repeat, bool act)` = private `GameSession.OnKey` (L311–379) → `protected override void GameKey(Keys key, bool down, bool repeat)` (`SwiftSession` L104–108).

The hook recovers IME keys from the scan code, folds left/right Shift/Ctrl/Alt to the generic key, and maps scan code 0x29 to `Keys.Oemtilde`.

**`OnKey` gates, in order:**

1. `Held` add/remove when `act`.
2. Win / Ctrl / Alt are passed on.
3. `!OwnsFocus()` → passed on.
4. Boss key press → `ToggleBoss()`.
5. In boss mode nothing is swallowed.
6. In a Rhino command → passed on.
7. Game over: Enter or Space restarts once `OverTime > 0.8`; Esc stops.
8. During a countdown everything is swallowed.
9. Paused: Esc resumes, Shift+Esc stops.
10. Playing: Esc or P pauses, Home calls `ResetCamera()`, anything else goes to `GameKey`.

**Swift keys**: `Keys.Space`, `Keys.Up`, `Keys.W` → `Game.Flap()`, on a fresh press only (`if (!down || repeat) return;`). There is no hold or repeat behaviour, and `Held` is unused by Swift apart from Shift+Esc.

**Mouse** (`GameMouse`, L111–116): a left-button Down (or double-click) calls `Game.Flap()` and takes the stroke. Right-drag orbits.

**Injection, simplest first:**

- **Flap**: `session.Game.Flap()` (public). From `Ready` it takes off; it is ignored unless `Ready` or `Flying`.
- **Via `GameKey`**: `tSwiftSession.GetMethod("GameKey", NonPublic|Instance).Invoke(s, new object[]{ Keys.Space, true, false })`.
- **Full path**: `tGameSession.GetMethod("OnKey", NonPublic|Instance).Invoke(s, new object[]{ Keys.Space, true, false, true })`, then the same with `down=false`. This is subject to the focus and Ctrl/Alt gates, so it is not reliable offline.
- **Boss**: `session.ToggleBoss()` (public).
- **Pause screen**: `game.Pause()` / `game.Resume()` (public), or private `GameSession.Pause()` / `Resume()`.
- **Restart**: `session.Restart()` (public). It makes a new `Game` object, resets the camera and starts a fresh countdown.

---

## 4. STATE FOR A BOT

**Coordinates**: course units; floor y = 0, ceiling y = `SwiftGame.Height` = 12. The bird's view x is fixed at `BirdX` = 5, and its course x is `Distance + 5`. World point = `session.At(viewX, y)` or `session.AtCourse(courseX, y)`.

**Constants** (public const, L94–98):

| Name | Value |
|---|---|
| `Radius` | 0.38 |
| `Gravity` | 27 |
| `FlapSpeed` | 9 (one flap climbs v²/2g = 1.5 units; hover period 0.667 s) |
| `MaxFall` | 13 |
| `WallThickness` | 1.4 |
| `FirstWall` | 16 (first wall at course x 21) |
| `Ahead` | 42 |
| `ShutterOmega` | 2π/4 |
| `FeatherRadius` | 0.6 |
| `CrashSeconds` | 1.2 |
| `Step` | 1/240 |

**`SwiftGame` public read-only state:**

- `State`, `Y`, `Vy` (units/s)
- `Distance`, `BirdCourseX`
- `Elapsed` (flight seconds), `Clock`
- `Score`, `Level` (= 1 + Score/10), `Speed` (= `SpeedAt(Score)`)
- `Feathers`, `Flaps`, `Bumps`
- `IsOver`, `Zen`, `HitWall`
- `Walls` (`IReadOnlyList<Wall>`), `FeatherList`, `Trail`
- `NextWall` (first wall with `Right + Radius > BirdCourseX`)

**`Wall`**: `Index`, `X` (left face, course x), `Right`, `Middle`, `Centre`, `Gap`, `Swing`, `Phase`, `Moving`, `Passed`, `Hit`, and `CentreAt(t)` = `Centre + Swing*sin(t*ShutterOmega + Phase)`; call it with `game.Elapsed + lookahead`.

**`Feather`**: `X`, `Y`, `Taken`, `TakenAt`. A feather is taken within 0.98 units of the bird's centre.

**Difficulty curves** (public methods):

| | Sheltered | Normal | Exposed |
|---|---|---|---|
| `SpeedAt(passed)` = base + min(2, 0.04·passed) | 3.9 | 4.4 | 5.2 |
| `GapAt(i)` = max(2.8, (base − min(1.4, 0.03·i))·Opening) | 5.6 | 5.0 | 4.2 |
| `PitchAt(i)` = max(SpeedAt(i)·0.95 + 1.4, (base − min(1.2, 0.02·i))·Bay) | 9.6 | 9.0 | 8.4 |
| `ShuttersFrom` (wall index) | 40 | 25 | 15 |

**Bot**: port the repo's own `Pilot` (`tests\Moyu.Core.Tests\TestsSwift.cs` L28–50). It uses only public members, is stateless, and the suite asserts it clears at least 60 walls in 150 s on seeds 1–5 in every exposure.

```
w = g.NextWall; target = 6
if (w != null) {
  enter = max(0, (w.X - 0.38 - g.BirdCourseX)/g.Speed); leave = max(0, (w.Right + 0.38 - g.BirdCourseX)/g.Speed)
  sill = max, head = min over t in [enter, leave] step 0.02 of  w.CentreAt(g.Elapsed+t) ∓ w.Gap/2
  lo = sill + 0.38 + 0.15; hi = head - 0.38 - 0.15; climb = 1.5
  target = lo + climb < hi ? lo + min(0.3, (hi-lo-climb)/2) : (lo+hi-climb)/2
}
flap = (g.Y < target - 1.2) ? g.Vy < 3 : (g.Y < target && g.Vy <= 0)
```

- **Per-frame order**, as in the test: `if (Pilot(g)) g.Flap(); g.Tick(dt);`.
- **Take-off**: the pilot may never take off from `Ready` (the bob stays at 5.75–6.25), so force one `Flap()` about 0.3–0.5 s after the countdown ends. That also shows the "空格 / 点一下 起飞" prompt briefly.
- **Look**: it bounces in the lower part of each opening with a steady flappy rhythm and bursts of quick flaps on climbs.
- **Optional variants** (not validated by the tests): aim at the centre with `target = (lo+hi-climb)/2`; or chase feathers by using `target = f.Y - 0.75` while an untaken feather lies before `w.X`.
- **Dry-run first**: `new SwiftGame(settings)` is Rhino-free and simulates 3600 frames in milliseconds. Run the same bot on it to confirm survival, pick a seed with many feathers, and get the frame numbers of level-ups, shutters and pickups.
- **Reproducing the dry-run in the session**: the flight repeats exactly if the same number of `Ready` ticks precede the first flap (Y at take-off depends on the bob) and the same staging edits are applied. Paused frames (countdown, boss) do not advance the core.

**Staging highlights** (reflection on `SwiftGame`; do it during the start countdown while `Distance == 0`):

- **Deterministic course**: non-zero `fixedSeed`. RNG draws happen only in `Fill()`, in wall order, so the course does not depend on input timing.
- **Start at a later level, speed or palette**:
  1. Clear `_walls` (private `List<Wall>`) and `_feathers` (private `List<Feather>`).
  2. Set `_nextIndex` (private int) to S.
  3. Set `Score` (property, `private set`) to S.
  4. Invoke private `Fill()`.
  - Level, speed, gap, pitch and shutters then behave as if S walls were already passed.
- **Chapter change on camera**: palette band is `Palette.Band(Level)`. Levels 1–3 are 门厅 Foyer, 4–6 回廊 Cloister, 7–9 中庭 Atrium, 10–12 天井 Lightwell, 13+ 黄昏 Dusk, so the palette changes at Score 30, 60, 90 and 120. Start at S = 27: three walls later (about 5 s) you get the "LEVEL 04 / 回廊 Cloister" card for 2.4 s plus the 1.6 s palette glide. A plain level card appears every 10 walls.
- **Avoid the start-up blend or card after staging**: on the conduit (`tGameSession.GetField("_conduit", NonPublic|Instance)`, concrete type `SwiftConduit`) set `_band = -1`, and set `_levelGame = game; _levelSeen = game.Level` (private fields). Alternatively let them finish under the 3 s countdown.
- **Shutters**: use `Exposure.Exposed` (from wall 15), or S ≥ 25 on Normal (30–50 % of walls), or insert your own `new Wall(index, x, centre, gap, swing, phase)` (public ctor) into `_walls`. The HUD line "滑动窗扇 · 看准再过" shows when the next wall moves and is within 14 units.
- **Feathers**: 30 % of bays naturally. Add your own with `_feathers.Add(new Feather(courseX, y))`. A pickup shows a rising "羽毛 +1".
- **Zen mode**: `settings.Mode = Zen`. Walls bump (tinted wall, pink halo, `Bumps++`), the floor bounces, the third HUD figure becomes BUMPS, and a "Zen · 撞墙不结束" status line appears.
- **Pair / two-player variant**: none exists. The variants are Classic/Zen × three exposures × the Opening and Bay factors.
- **BEST figure**: the HUD shows `max(session.Best, Score)`. `Best` (`long`, `private set` on `SwiftSession`) comes from the user's real score file; set it by reflection for a tidy number.
- **Result screen**: let the bot stop flapping. You get a 0.3 s red flash, a fall of up to 1.2 s, then the wash, a 112k-px score, the title "撞墙了 / 落地了" and hints after 0.8 s. For the "NEW BEST" badge set `NewBest` (`private set` on `SwiftSession`) to true.
- **Hide key hints during play**: set `PlayTime` to 100. The countdown and pause screens still show them.

---

## 5. CAMERA & PORTRAIT LAYOUT

**`protected override void SetStartCamera(RhinoViewport vp)`** (`SwiftSession.cs` L120–129):

```csharp
var target = At(14, 7.4);
var location = target + (Plane.ZAxis * 40 + Plane.XAxis * -3 + Plane.YAxis * 5.7) * Size;
vp.SetCameraLocations(target, location); vp.CameraUp = Plane.YAxis; vp.Camera35mmLensLength = 40;
```

- It does not depend on viewport size or aspect. Distance is 40.5 units and elevation 8.09°, just above the 8° floor that `KeepCameraAbove()` enforces on every `TickGame`.
- It is re-applied by `ResetCamera()`: the Home key, `Restart()`, or a parallel projection detected in the real `Tick`.
- A custom camera set after `Begin` persists, and boss exit restores whatever camera was active at boss entry.
- With Option A a custom camera must keep elevation ≥ 8°. Option B never calls `KeepCameraAbove`.

**Portrait framing (verify with one test frame).** In openNURBS the 35 mm lens length is measured across the smaller frustum dimension, so 40 mm gives 0.6 × distance.

- **Landscape 16:9**: 24.3 units high × 43.2 wide, which is exactly the course range −8…36 the meshes are built for.
- **Portrait 9:16, default camera**: 24.3 units wide (x ≈ 1.9…26.1) × 43.2 high.
  - The 12-unit hall is a band about 530 px tall in the middle of 1920 (≈ y 756–1289 px).
  - The bird sits about 140 px from the left edge.
  - Above and below is plain sky gradient; nothing is drawn below the floor slab at y = −1.3.
  - It works but the hall is small.
- If the lens rule were tied to height instead, the bird at x = 5 would be off-screen. Measure with `vp.WorldToClient(s.At(0,0))` and `s.At(1,0)`.

**Suggested tweak**: keep the same direction and shorten the distance (or lengthen the lens).

- Visible width W = 0.6·d at 40 mm; visible height = W·16/9.
- W ≈ 18 (d = 30) gives 60 px/unit at 1080 wide; the hall plus slabs is then about 830 px tall.
- Target x ≈ 10 puts the bird about 22 % from the left with 1–2 walls ahead.
- Choose target y so the hall sits below about y = 520 px at k = 2, clear of the HUD.

```csharp
var t = s.At(10, 7.0);
vp.SetCameraLocations(t, t + (plane.ZAxis*40 + plane.XAxis*-3 + plane.YAxis*5.7) * (size * 30/40.51));
vp.CameraUp = plane.YAxis; vp.Camera35mmLensLength = 40;
```

**HUD scale** (`GameConduit.UiScale`, L34):

```csharp
public static double UiScale(double dpiScale, Size frame) => Math.Max(1, Math.Min(dpiScale, frame.Height / 720.0));
```

- Everything is laid out in 96-dpi units times `k = UiScale(e.Display.DpiScale, e.Display.FrameSize)`. The 3D scene is not scaled. There is no override.
- In a 1080×1920 capture, k = clamp(DpiScale, 1, 2.67):
  - 100 % display → k = 1: tiny HUD (score digits 68 px, labels 11–12 px in a 1920-tall frame).
  - 150 % → k = 1.5.
  - 200 % → k = 2: score 136 px, labels 24 px, chapter name 44 px, countdown ring 480 px across.
- A 540×960 on-screen view at 100 % also gives k = 1, so its HUD looks twice as large relative to the frame as a k = 1 capture at 1080×1920.
- **Verify** what `DpiScale` is inside `CaptureToBitmap` with one test capture. The code comment implies it stays the display's scale.
- If k = 1 is all you get: capture 540×960 or 720×1280 and upscale, or rebuild the HUD in post.

**HUD positions** (HUD units × k):

| Element | Position |
|---|---|
| Score block | Top-left, x 48–52, y 44–196; three figures at x = 52 + i·108 |
| Level block | Right-aligned at `Width − 52`, y 44–104 |
| Status lines | From y = 222, left |
| Chapter card | Centred at `Height × 0.2` |
| Countdown / pause / result | Centred on the frame |
| Key hints | Bottom, `Height − 40`; wrap to 2 rows at 1080 wide with k = 2, stacking upward |
| "+1", "羽毛 +1", take-off prompt | World-anchored through `e.Viewport.WorldToClient` |

At 1080 wide with k = 2 the top blocks fit. A status line and a chapter card can touch around x 350–470, y ≈ 450–520 px.

**Language**: `session.Language` (public get/set, L54) redraws on set. Force it with:

```csharp
tGameSession.GetProperty("Language").SetValue(s, Enum.ToObject(tUiLanguage, 0)); // 0 Chinese, 1 English
```

- With `owner == null` it starts as the Windows display language.
- Always English: "SWIFT", FEATHERS, SPEED, BEST/BUMPS, "LEVEL 01", PAUSED, NEW BEST, "+1", and the whole boss screen.
- Chinese mode shows chapter names as "门厅 Foyer".
- Fonts used: Bahnschrift (Light / SemiLight) and Microsoft YaHei UI (Light).

---

## 6. BOSS KEY

**API** (`GameSession.cs`):

- `public void ToggleBoss()` (L441): `ExitBoss()` if in boss mode, else private `EnterBoss(false)`.
- `public void EnterBoss()` (L448) = `EnterBoss(true)`, the focus-loss path; it also calls `_hook.Reset()`.
- `public void ExitBoss()` (L472).

**`EnterBoss`** (L451–470), all in one call so the next drawn frame is the disguise:

1. `_pausedByBoss = Countdown > 0 || IsGameActive`
2. `PauseGame(); Countdown = 0; ClearInput(); Held.Clear();`
3. `_gameCamera = new ViewportInfo(vp)`
4. `EnterDisguise(vp)`
5. `Boss = true; BossTime = 0; Redraw();`
6. `RhinoApp.WriteLine(line); Changed();`

**`SwiftSession.EnterDisguise`** (L133–142):

- Builds `Disguise = new SectionDisguise(this, new SectionModel(SectionModel.FromGame(Game, Metres)))` from the walls on screen and the trail.
- Sets a straight-on camera from `CameraFor` (L374): target `At(14, 7.0)`, location `target + Plane.ZAxis * 44 * Size`, lens 40.
- Returns the command-line text: `AirflowStudy: Section A-A updated · cross X ach · stack X ach · mean DF X % · P1 X m/s`.

**What is drawn** (conduit only; no document objects or layers, no display-mode change):

- **Background**: light-grey gradient (206,212,218 → 246,247,249).
- **Drawing**: poché floor, roof and cross walls where the game has them; rooflights; back-wall windows beyond; scale figures and benches; ground hatch.
- **Analysis**: daylight-factor strip on the workplane; June and December noon sun rays through the rooflight nearest x = 14; airflow lines coloured by speed, the main one being the flight path; probe P1 at the bird.
- **World labels**: levels at x = 31; a dimension chain above the roof; bay names; "WIND … →" at x = −2.6.
- **Screen overlays** (`DrawOverlay`, L295):
  - Card at top-left, 300k × 244k, "Climate Study · Section A–A", six rows, and "Solving airflow (k-ε)… iteration N / 400" with a bar. N = `(212 + (int)(BossTime/1.2)*3) % 400 + 1`.
  - Legend strip at bottom-left: 560k × 44k at `Height − 58k`.
  - Title block "SECTION A–A 1:100" at `Width − 380k`, `Height − 62k`.

**Portrait problems:**

- The legend and title block collide when `Width < 964k`, which at 1080 wide means k > 1.12. The legend overflows the frame when k > 1.85.
- With the stock boss camera only x ≈ 0.8…27.2 is visible, so the level marks and the WIND label are cut off.
- At k > 2 the card covers the top of the drawing.

**Workarounds**: capture taller (for example 1080 × (1920 + 120k)) and keep the top 1920 rows, which drops the legend, title block and key hints. Alternatively capture wider than 964k and scale down, or shoot the boss frame in landscape and pan across it in the edit.

**Matching the cut**: right after `ToggleBoss()` set your own straight-on camera with the game camera's target and distance. `ExitBoss` restores `_gameCamera` regardless.

**`ExitBoss`**: `Boss = false`, restores the camera, and if `_pausedByBoss && !IsOver` calls `StartCountdown(false)`: `Countdown = 3`, caption "回到游戏 / Back to the game", wash 0.35, ring, hints at full alpha. When the countdown ends, `Game.Resume()` returns to `Flying` with the old `Vy`, so the bot must act on the very next frame.

**Grasshopper side**: only through `changed`. With a real component it sets State "Section A-A", Message "Section A-A · X ach", calls `Instances.RedrawCanvas()` and `ScheduleSolution(20, …)`. With `changed == null` nothing happens.

**Timing at 60 fps (Option B):**

| Frames | Action |
|---|---|
| Play ≥ 3–4 s first | The flight-path streamline needs a trail; trigger mid-bay |
| N | `s.ToggleBoss()` before the capture; that frame is already the disguise |
| N … N+89 | `BossTime += dt` (1.5 s; the iteration counter steps 213 → 216 at 1.2 s) |
| N+90 | `s.ToggleBoss()` |
| next 180 | `Countdown -= dt` (3-2-1); to shorten, subtract 2·dt per frame or set `Countdown` lower after `ExitBoss` |
| after | `game.Resume()` happens at zero; the bot continues |

---

## 7. RISKS

- **Focus, foreground and command gates in the real `Tick`**: if another app is in front, or `run_csharp` runs inside a Rhino command, the countdown stalls, the game auto-pauses, and auto-boss fires. Use Option B, or at least set `AutoBoss = false` and check `Command.InCommand()` and `KeyboardHook.FocusRoot()` (public static) against `RhinoApp.MainWindowHandle()`.
- **Real timer still running**: the WinForms timer fires whenever messages pump, including between chunked `run_csharp` calls, and would advance the game with real dt. Stop `_clock._timer` right after `Start`.
- **Keyboard hook and mouse callback stay live**: the hook swallows every plain key while Rhino has focus, and a left click in the view flaps. After `Start`, call `Dispose()` on `_hook` (idempotent; `Stop()` copes) and set `_mouse.Enabled = false`. Do not null either field, because `EnterBoss(true)` calls `_hook.Reset()`.
- **Threading**: the hook installs on the calling thread, and view calls need the UI thread. If `run_csharp` is not on the UI thread, wrap the work in `RhinoApp.InvokeOnUiThread`.
- **Score file pollution**: game over through the real `Tick` or `OnKey` appends to `%APPDATA%\Moyu\swift-scores.txt` and can raise the user's real best. Option B never calls `NoteGameOver`. With Option A, set private `_overNoted = true` (it is reset by `Restart()`), or use a distinct score key such as `Opening = 1.01`.
- **Active view**: `Start` uses the active view only at that moment. Set `doc.Views.ActiveView` first or use the constructor path. Drawing is keyed to `ViewportId`; if that view closes, the real `Tick` stops the session.
- **`View.Maximized = true` in `Begin`**: its effect on a floating view is not determinable from source; check the view size after `Start` (**verify**).
- **Capture aspect versus on-screen aspect**: make the floating view exactly 9:16 (for example a 540×960 client area) so the frustum and `WorldToClient` behave the same as in the 1080×1920 capture. The repo's workflow doc confirms `view.CaptureToBitmap` draws the conduit.
- **Sky bitmap cache is keyed by frame size**: alternating on-screen redraws and captures at another size rebuilds a full-frame GDI bitmap twice per frame. Option B avoids this, since it has no per-frame on-screen redraw.
- **Document content shows through**: the conduit paints a background and its own geometry, but Rhino still draws document objects. Use an empty document. Grid and axes are switched off by `Begin` and restored by `Stop`.
- **`GameSession.Current` is a singleton**: pressing Run on any Moyu game component, or a second `Start`, stops your session. `Stop()` also changes the camera and the view's maximised state.
- **`Restart()` replaces `session.Game`** and resets the camera: re-fetch `Game`, then re-apply staging and the custom camera.
- **Long loops block Rhino's UI thread**: render in chunks of a few hundred frames per call. State survives through `GameSession.Current`, and the bot is stateless.
- **Exceptions in the real `Tick`** stop the session and print "Swift stopped: …". Draw exceptions are swallowed and printed as "Moyu display/HUD/background: …", so check the command history if frames look wrong.
- **Unverified Rhino behaviours to test with one frame first**: the lens rule in portrait, `DpiScale` during capture, and `Maximized` on a floating view.