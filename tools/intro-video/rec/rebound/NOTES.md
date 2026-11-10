# Rebound (Facade panel): offline-stepping cheat sheet

Everything below is from reading the source; nothing was run. Items marked **UNVERIFIED** are Rhino/Win32 runtime behaviours the repo cannot confirm and need a one-frame test.

Files (all under `src\Moyu\`):
- `Games\Common\GameSession.cs`, `GameClock.cs`, `GameConduit.cs`, `KeyboardHook.cs`, `Palette.cs`, `GameScores.cs`
- `Games\Breakout\BreakoutSession.cs`, `BreakoutConduit.cs`, `FacadeDisguise.cs`
- `Games\Breakout\Core\BreakoutGame.cs`, `Bonds.cs`, `TowerFacade.cs`
- `Components\Facade\FacadeStudyComponent.cs`, `Components\GameComponent.cs`

Assembly name is `Moyu` (no `AssemblyName` override in `Moyu.csproj`). Safer pick: the assembly where `GetType("Moyu.Games.Breakout.BreakoutSession") != null`.

Type names:
- `Moyu.Games.Common.GameSession` (public abstract), `GameClock`, `KeyboardHook`, `GameConduit` (all public)
- `Moyu.Games.Breakout.BreakoutSession` (public sealed), `BreakoutConduit` (**internal** sealed, public ctor), `FacadeDisguise` (public sealed)
- `Moyu.Games.Breakout.Core.{BreakoutGame, BreakoutSettings, BreakoutMode, BreakoutState, PowerKind, Ball, Power, HitEvent, Brick, BrickMaterial, BondType, Bonds, CustomWall, TowerFacade}` (all public)
- `Moyu.Games.Common.Core.UiLanguage` (enum `Chinese, English`)

Private members declared on `GameSession` must be looked up on `typeof(GameSession)`, not on `BreakoutSession`.

## 1. START

**Factory** (`BreakoutSession.cs` L48):
```csharp
public static BreakoutSession Start(BreakoutSettings settings, Plane plane, double size,
                                    object owner, Action<GameSession> changed, out string error)
```
It does `PickView` → `StopCurrent()` → private ctor → `Begin(new BreakoutConduit(session), out error)`. It returns null plus an error on failure.

**What the component passes** (`FacadeStudyComponent.Run` L161–185):
- `settings`: Mode, StartLevel, Bond, Custom, Courses copied from the inputs, `Seed = Environment.TickCount`.
- `plane`: `FacadeStudyComponent.DefaultPlane` = `new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis)`. X runs along the wall, world Z is up, and the plane normal is world −Y, so the viewer stands on −Y.
- `size`: `FacadeStudyComponent.DefaultSize()` = 2.4 m converted to document units (2.4 in a metre doc, 2400 in mm).
- `owner = this`, `changed = OnSessionChanged`.

**`BreakoutSettings`** (public fields, `BreakoutGame.cs` L39–55):

| Field | Default | Note |
|---|---|---|
| `Mode` | `Classic` | `Blitz`, `Zen` (no lives lost) |
| `StartLevel` | 1 | core only clamps to ≥ 1 |
| `Seed` | `Environment.TickCount` | set a fixed value |
| `Width`, `Height` | 13, 17 | field units |
| `Lives` | 3 | |
| `BlitzSeconds` | 180 | |
| `Bond` (`BondType?`) | null | null = six bonds in turn |
| `Custom` (`CustomWall`) | null | |
| `Courses` | 10 | core accepts any value |
| `PowerChance` | 0.15 | read live on each broken brick |
| `PowerTour` | true | |

**From a script**: pass `owner = null, changed = null`. Then there are no Grasshopper side effects at all: `Changed()` is `_changed?.Invoke(this)`, and the language falls back to `SystemLanguage()`.
```csharp
var st = Activator.CreateInstance(tSettings);
tSettings.GetField("Seed").SetValue(st, 12345);
object[] a = { st, plane, size, null, null, null };
var session = tSession.GetMethod("Start").Invoke(null, a);
string error = (string)a[5];
```

**View picking** (`GameSession.PickView`, public static, L121): uses `RhinoDoc.ActiveDoc.Views.ActiveView`. If that is a page view it takes the first perspective standard view. To target a floating view, set `doc.Views.ActiveView = floatingView` just before `Start`. The session captures `View` and `ViewportId` in its ctor, and the conduit only draws where `e.Viewport.Id == session.ViewportId` (`GameConduit.Mine` L78).

**Alternative without touching ActiveView**:
- Private ctor: `BreakoutSession(BreakoutSettings, Plane, double, object, Action<GameSession>, RhinoDoc, RhinoView)`.
- Conduit: `Activator.CreateInstance(asm.GetType("Moyu.Games.Breakout.BreakoutConduit"), session)`.
- Then protected `bool Begin(GameConduit conduit, out string error)` on `typeof(GameSession)` (NonPublic | Instance).

**`Begin`** (L139–184), in order:
1. `Current?.Stop(); Current = this` and enables the conduit.
2. Installs the keyboard hook (`new KeyboardHook(OnKey)`, a thread hook on the calling thread). It fails with an error if the hook is not installed.
3. Enables `OrbitMouse` and subscribes to `RhinoDoc.CloseDocument` and `RhinoApp.Closing`.
4. Changes a parallel view to perspective (`ChangeToPerspectiveProjection(true, 28)`).
5. Sets **`View.Maximized = true`**; turns grid, construction axes and world axes off.
6. `SetStartCamera(vp)`, then `StartCountdown(true)` (pauses the game, `Countdown = 3`).
7. **`_clock = new GameClock(Tick)`**: the real-time timer starts here.
8. `RhinoApp.SetFocusToMainWindow()`, `View.Redraw()`, `Changed()`.

The ctor also reads `GameScores.Best("rebound", ScoreKey)` and remembers the user's view (camera, maximized, grid, axes) for `Stop()`.

**End**: `session.Stop()` (public) restores the camera, grid, axes and `Maximized`, disposes clock, hook and conduit, and clears `Current`. `GameSession.Current` has a public getter, so you can re-acquire the session between script calls.

## 2. FRAME LOOP

**Clock** (`GameClock.cs`): a WinForms `Timer` at 15 ms. `OnTick` computes `dt = Math.Min(0.1, stopwatchDelta)` and calls the delegate, which is the **private instance `void Tick(double dt)`** on `GameSession` (L234).

**Stop real time** (do it in the same script call as `Start`, before control returns to the message loop):
```csharp
const BindingFlags NP = BindingFlags.Instance | BindingFlags.NonPublic;
var clock = tGameSession.GetField("_clock", NP).GetValue(session);
((System.Windows.Forms.Timer)clock.GetType().GetField("_timer", NP).GetValue(clock)).Stop();
```
`((IDisposable)clock).Dispose()` also works; the second dispose in `Stop()` is harmless.

**`Tick(dt)` step by step** (L234–293):
1. Return if `Stopped`. If the view is gone, `Stop()`.
2. `Time += dt`.
3. `if (AutoBoss && !Boss && !GameClock.AppIsForeground()) EnterBoss();` (the foreground window must belong to the Rhino process).
4. If `Boss`: `BossTime += dt`, redraw only when `(int)(BossTime / 1.2)` changes, **return**. There are no focus checks on this branch.
5. `command = Rhino.Commands.Command.InCommand()`. If `!IsOver && (command || !OwnsFocus())`: during a countdown it redraws and **returns** (the countdown freezes); otherwise, if the game is active, it calls `Pause()`.
6. If the view went parallel (and no command): `ResetCamera()`.
7. `TickAlways(dt)` (a no-op for Rebound).
8. If `Countdown > 0`: `Countdown -= dt`; at ≤ 0 it sets 0, calls `ResumeGame()` (= `Game.Resume()`) and `Changed()`; redraw; return.
9. `if (!IsGamePaused) PlayTime += dt;`
10. `TickGame(dt)` = `Game.Tick(dt); KeepCameraAbove();` (the latter pushes the camera back up to 8° elevation).
11. `NoteGameOver()`; `if (IsOver) OverTime += dt;`
12. `if (NeedsRedraw) Redraw();` (`View.Redraw()`).

Any exception is caught, prints `Rebound stopped: …` and calls `Stop()`, so check `session.Stopped` after each call. The keyboard hook is installed once in `Begin`, not per tick.

`OwnsFocus()` (private, L299) is true only when Win32 `GetFocus()`'s root window is `RhinoApp.MainWindowHandle()` or the root of `View.Handle` (the floating frame).

**Two ways to step.**

- **A. Call the private Tick**: `tGameSession.GetMethod("Tick", NP).Invoke(session, new object[]{ 1.0/60 })`.
  - Requires `session.AutoBoss = false` (public setter).
  - Requires that Rhino is not inside a command and that `OwnsFocus()` is true.
  - **UNVERIFIED**: whether either holds while `run_csharp` executes. If not, the countdown never ends or the game pauses every frame.
  - Pre-check with `Rhino.Commands.Command.InCommand()` and `tGameSession.GetMethod("OwnsFocus", NP)`.

- **B. Manual stepping (recommended; no focus, command or foreground dependence).** The properties `Time`, `Countdown`, `PlayTime`, `BossTime`, `OverTime`, `Boss`, `CountdownIsStart` are public auto-properties with private setters. `PropertyInfo.SetValue` works, or use the backing field `<Time>k__BackingField`.
```csharp
void Step(double dt) {
  Set("Time", Get("Time") + dt);
  if (Boss) { Set("BossTime", Get("BossTime") + dt); return; }
  double c = Get("Countdown");
  if (c > 0) { c -= dt; if (c <= 0) { c = 0; game.Resume(); } Set("Countdown", c); return; }
  if (game.State != Paused) Set("PlayTime", Get("PlayTime") + dt);
  game.Tick(dt);                      // public, BreakoutGame.cs L214
}
// then: bmp = view.CaptureToBitmap(new Size(1080, 1920)); save
```
  `session.Game` is public; re-read it after a `Restart`. B skips `KeepCameraAbove`, so a camera below 8° elevation is allowed. No on-screen `View.Redraw()` is needed; the capture draws the conduit (`docs\game-workflow.md` L28).

**Wall-clock dependencies (complete list):**

| Where | What | Effect offline |
|---|---|---|
| `GameClock._watch` (Stopwatch) | dt source | bypassed once the timer is stopped |
| `KeyboardHook._clock` (Stopwatch) | lost-release detection (250 ms) | only for real keys |
| `GameConduit._backgroundClock` (Stopwatch, L45) | sky bitmap repainted at most every 120 real ms during a palette blend | cosmetic: the sky steps in real time, not video time, during the 1.6 s chapter blend; size and boss changes repaint at once |
| `BreakoutSettings.Seed` default; `FacadeStudyComponent.Run` | `Environment.TickCount` | set `Seed` yourself |
| `BreakoutSession.NewGame()` (L77, used by `Restart`) | reseeds with `TickCount` | avoid `Restart` |
| `GameScores.Record` | `DateTime.Now`, appends to `%APPDATA%\Moyu\rebound-scores.txt` | only at game over |

Core (`BreakoutGame`, `Bonds`, `TowerFacade`) has no wall clock: "same seed and inputs, same game". `TestsBreakout.BlitzAndReplay` confirms an exact replay at dt = 1/60.

**Conduit animation time sources** (all advance with your stepping):
- `session.Time`:
  - palette blend (`BlendSeconds = 1.6`, `_blendStart`);
  - debris physics: `dt = clamp(Time − _lastTime, 0, 0.1)` per draw, lifetime 1.3 s, RNG `new Random(7)`;
  - power capsule bob `Math.Sin(Time*6 + X)`;
  - serve-prompt pulse `Math.Sin(Time*4)`.
- `game.Elapsed`:
  - hit flash 0.1 s, `+points` popup 0.9 s, power-caught text 2.4 s, life-lost red flash 0.3 s;
  - "WALL CLEARED" card, shown during `Cleared`;
  - "LEVEL nn" card, shown 2.4 s after `LevelStartTime`.
- `Countdown`, `PlayTime`, `OverTime`, `BossTime`.
- Ball trails are sampled per draw when the ball has moved more than 0.12 field units (7 points). Drawing twice in one state adds nothing.

## 3. INPUT

**Path**: `KeyboardHook.Proc` → `GameSession.OnKey(Keys key, bool down, bool repeat, bool act)` (private, L311) → `BreakoutSession.GameKey(Keys key, bool down, bool repeat)` (protected override, L93).
- The hook sets `act` true once per press and once per release; auto-repeats arrive with `act = false, repeat = true`.
- `OnKey` adds to or removes from `Held` (protected `HashSet<Keys>` on `GameSession`) when `act`.
- `GameKey` is called only when `act`, so it never sees repeats. Holding a key means membership in `Held`.
- `OnKey` passes keys on (does nothing) when Ctrl or Alt is physically down, when `!OwnsFocus()`, or when Rhino is in a command.

**Keys:**

| Key | Action |
|---|---|
| `Left` / `A`, `Right` / `D` | `Game.TargetX = NaN; Game.Steer = (right?1:0) − (left?1:0)`, computed from `Held` |
| `Space` | `Game.Launch()` on a fresh press (serve, or release a sticky catch) |
| `Oemtilde` (`GameSession.BossKey`, matched by scan code 0x29) | `ToggleBoss()` |
| `Escape` / `P` | pause; paused: Esc resumes, Shift+Esc stops |
| `Home` | `ResetCamera()` |
| `Enter` / `Space` when over | `Restart()` after `OverTime > 0.8`; Esc stops |
| any key during countdown | swallowed |

**Mouse** (`GameMouse`, L112): a hover over the viewport projects the cursor onto `Plane` and sets `Game.TargetX = u / Size; Game.Steer = 0`. Left or right drag orbits the camera.

**Injection, simplest first:**
1. Public core API (recommended, no focus dependence): `game.TargetX = x` (the paddle chases it at up to 25.6 units/s), or `game.TargetX = double.NaN; game.Steer = −1|0|1` (16 units/s, bang-bang), and `game.Launch()`.
2. `GameKey` directly: edit `Held` via `tGameSession.GetField("Held", NP)`, then `tSession.GetMethod("GameKey", NP).Invoke(session, new object[]{ Keys.Left, true, false })`; release with `down = false`.
3. `OnKey`: `tGameSession.GetMethod("OnKey", NP).Invoke(session, new object[]{ Keys.Left, true, false, true })` for a press, `{ key, false, false, true }` for a release. It is subject to the focus and command gates.

## 4. STATE FOR A BOT

**Coordinates**: field units; x ∈ [0, 13] along the wall, y ∈ [0, 17] up. World point: `session.At(x, y, z = 0)` (public) = `Plane.Origin + (X·x + Y·y + Z·z)·Size`. Pixel position: `vp.WorldToClient(session.At(x, y))`.

**Session** (public): `Game`, `Settings`, `Plane`, `Size`, `Best` (private set), `Boss`, `Countdown`, `PlayTime`, `Time`, `Stopped`, `Language`.

**`BreakoutGame`** (public getters; setters private unless noted):
- `State`: `Serving, Playing, Paused, Cleared, Lost, TimeUp`.
- `Level`, `Lives`, `Score`, `Combo`, `Elapsed`, `Remaining`, `Total`, `Progress`.
- `PaddleX` (centre), `PaddleWidthNow` (2.4; ×1.5 wide; ×0.65 narrow), `BallSpeed`.
- `Steer`, `TargetX` (public set).
- `WideLeft`, `NarrowLeft`, `SlowLeft`, `PierceLeft`, `StickyLeft`.
- `LastHit` (`HitEvent`: `Brick, Broken, Points, Combo, Time`), `LastPower`, `WallVersion`.
- `Balls` (`Ball`: public fields `X, Y, Vx, Vy` in units/s, `Held`, `HeldOffset`, `Idle`).
- `Powers` (`Power`: `X, Y, Kind`; falls at 4.2 units/s).
- `Bricks` (`Brick`: public fields `X0, Y0, X1, Y1, Material, Hits, MaxHits`; `Alive`, `Breakable`).

**Constants**: `BallRadius 0.24`, `PaddleY 1.0`, `PaddleHeight 0.35`, `PaddleSpeed 16`, `MaxAngle 60°`, `PowerFall 4.2`, `PowerSize 0.5`, `ClearPause 1.6`, `MaxCombo 8`, `MaxBalls 9`, `MaxLives 5`.

**Physics the bot needs:**
- Ball speed = `min(16, (7.5 + 0.45·(Level−1))·_speedUp)`, ×0.7 while slow. `_speedUp` grows ×1.015 per paddle return up to 1.25.
- Paddle hit: when `Vy < 0`, `ball.Y ≤ 1.415` and `|ball.X − PaddleX| ≤ half + 0.144`.
- Outgoing angle from vertical = `clamp((ball.X − PaddleX)/(half + 0.24), −1, 1) · 60°`.
- A ball is lost at `Y < −0.48`. A life is lost only when no ball is left.
- Level 1 wall bottom is at y ≈ 9.58, top at 15.8.

**Bot (per frame, before stepping):**
1. If `State == Serving` or any ball is `Held`: wait about 0.5 s of game time, then `game.Launch()`.
2. Among balls with `Vy < 0`, take the smallest `t = (Y − 1.415)/(−Vy)`. Predict `xHit = X + Vx·t`, folded by reflection into `[0.24, 12.76]`. Re-predict every frame, since bricks and `KeepSteep`/`Wander` change the path.
3. Aim: pick a target brick (for example the lowest alive breakable one). `f = clamp(atan2(bx − xHit, by − 1.4)/60°, −0.7, 0.7)`, plus a slow wobble such as `0.15·sin(0.9·Elapsed)` so it never loops vertically. `goal = xHit − f·(half + 0.24)`.
4. A helpful power is falling (`Kind != Narrow`): its catch time is `(p.Y − 1.425)/4.2`. Go to `p.X` if no ball lands within about ±0.4 s, or if `|p.X − goal| < half`.
5. No descending ball: drift slowly toward the lowest ball's x or the centre.
6. Smooth: `tx += (goal − tx)·min(1, dt·12)`, with `dt·30` when `t < 0.25 s`; then `game.TargetX = tx`.

The paddle crosses the field in about 0.5 s, and a ball needs at least 0.87 s from the wall bottom to the paddle, so one tracked ball is never missed. The tests use the same idea (`Returns`: `g.TargetX = ball.X`; `BlitzAndReplay`: `Balls[0].X + 0.6·sin(i·0.013)`).

Because the core is deterministic, dry-run a headless `new BreakoutGame(settings)` with the same bot to find seeds and frame numbers, then replay with capture.

**Highlights on demand:**

- **Seed**: `Settings.Seed` before `Start`. The RNG is the private `_rng`; the tour order is shuffled from it in the ctor.
- **Power-ups**:
  - *Looks natural*: set `game.Settings.PowerChance = 0` to mute random drops. When `game.LastHit` is a new object with `Broken == true`, add `new Power { X = brick mid, Y = brick mid, Kind = … }` to the private `List<Power> _powers` (`tGame.GetField("_powers", NP)`). It falls with a label, the bot catches it, and `Apply` runs.
  - *Rig the real drops*: set `PowerChance = 1` just before a break (the chance is ×2.4 on level 1, ×1.6 on level 2, capped at 1) and refill the private `Queue<PowerKind> _tour` in the wanted order.
  - *Instant*: `game.Apply(PowerKind)` is public ("tests and scripted demos use it too"). It shows the 2.4 s name and help text above the paddle.
- **What each one shows**:
  - Multi: every free ball splits in three, HUD line "N 个球".
  - Pierce: 6 bricks straight through, coral ball and a coral line over the paddle.
  - Sticky: 3 catches, lilac paddle, "空格 发球" prompt; the bot must `Launch()`.
  - Slow: ×0.7 for 6 returns.
  - Wide: 8 returns.
  - Life: +1, max 5.
  - Each active use-counted power-up (wide, narrow, slow, sticky, pierce) gets a status line with its uses left; multi shows the ball count.
  - Counters can be extended through the private setters (for example `PierceLeft = 12`).
- **Wall cleared, next level**:
  - A full level 1 wall is about 85 bricks and about 160 hits, which is too long.
  - Options: start with `Settings.Courses = 3` (about 25 bricks); or, off-camera, set `brick.Hits = 0` on all but a few breakable bricks and bump `WallVersion` (private setter) so the wall mesh rebuilds.
  - Bricks zeroed this way also read as clear glass on the boss elevation.
  - The last break gives `State = Cleared`, a "WALL CLEARED +500×level" card for 1.6 s, then `Level++`, a new wall, `Serving`, and a "LEVEL 02 / 通缝 Stack bond" card for 2.4 s. The bot must launch again.
  - `game.SetWall(IEnumerable<Brick>)` (public) also works but resets `Total`, so the progress line restarts.
  - Do not invoke the private `Clear()`: the bricks stay drawn.
- **Bond per level**: `(level−1) % 6` → Stretcher, Stack, English, Flemish, Screen, Arch; or fix it with `Settings.Bond`.
- **Chapter palette**: `Palette.Band(level)` changes only at levels 4, 7, 10 and 13. Level 1→2 keeps the palette. For a visible 1.6 s palette glide on wall clear, use `StartLevel = 3` (clears into level 4; steel bricks start there).
- **Combo**: +1 per brick hit, reset on the paddle, max ×8. The HUD shows "COMBO ×n" and accent-coloured `+points`. Stage it with pierce or multi-ball, or put a ball above the wall with `game.AddBall(x, y, vx, vy)` (public; it replaces all balls and sets `Playing`; only the direction matters).
- **Speed**: `StartLevel`, the private `double _speedUp` (1 to 1.25), `SlowLeft`.
- **Skip the opening countdown**: set `Countdown = 0` and call `game.Resume()`.
- **Hide key hints**: set `PlayTime ≥ 8.5` (they fade after 7 s; the serve prompt needs `PlayTime > 0.3`).
- **BEST figure** (Classic): shows `max(session.Best, Score)` from the user's real score file. Set `Best` via its private setter if you want a clean number.

## 5. CAMERA & PORTRAIT LAYOUT

**`SetStartCamera`** (`BreakoutSession.cs` L124–131) is independent of viewport size:
```csharp
var target = At(Settings.Width / 2, Settings.Height / 2 - 0.5);                         // (6.5, 8.0)
var location = target + (Plane.ZAxis * 31 + Plane.XAxis * 5 + Plane.YAxis * 6) * Size;  // distance ≈ 31.97 units
vp.SetCameraLocations(target, location); vp.CameraUp = Plane.YAxis; vp.Camera35mmLensLength = 35;
```
It is re-applied by `ResetCamera()` (Home, `Restart`, or a parallel view detected in `Tick`). `ExitBoss` restores whatever camera was active when boss mode was entered, so a tweaked camera survives a boss round trip.

**Portrait framing** (computed, **UNVERIFIED**: assumes Rhino's lens length refers to the smaller frustum side with a 12 mm half-frame; check with `vp.GetCameraAngle`):
- At 35 mm in 9:16 the visible width at the target is about 21.9 units and the height about 39.
- The field with its frame is 14.2 × 18.2 units. It fills about 64 % of the width and 46 % of the height, roughly centred.
- So it works, but it is small, with about 27 % empty bands above and below.
- Tweak after `Start`: `vp.Camera35mmLensLength ≈ 48` gives about 90 % of the width and 64 % of the height. Raise the target y a little (about 8.5–9) to push the field down and clear the top HUD.
- Keep the elevation at or above 8° if you step through the session's `TickGame`.

**HUD scaling** (`GameConduit.cs` L34–36):
```csharp
public static double UiScale(double dpiScale, Size frame) => Math.Max(1, Math.Min(dpiScale, frame.Height / 720.0));
protected static double Ui(DisplayPipeline d) => UiScale(d.DpiScale, d.FrameSize);
```
- Everything is laid out in 96-dpi units × k, anchored to `e.Display.FrameSize`.
- For a 1080×1920 frame, k = `min(DpiScale, 2.67)`, never below 1. It cannot be forced without patching.
- `docs\fourth-round-report.md` mentions a 4K screen at 175 %; on that screen k = 1.75. At 100 % k = 1 and the HUD is tiny.
- **UNVERIFIED**: what `DpiScale` reports during `CaptureToBitmap`. Print `view.DisplayPipeline.DpiScale` and measure one test frame.

| HUD element | Units | px at k = 1 | px at k = 1.75 |
|---|---|---|---|
| Score (top-left, x = 48, y = 62) | 68 | 68 | 119 |
| "REBOUND", LIVES/COMBO/BEST labels | 12 / 11 | 12 / 11 | 21 / 19 |
| Figure values (y = 168, pitch 108) | 28 | 28 | 49 |
| LEVEL nn / chapter name (right, inset 52) | 12 / 22 | 12 / 22 | 21 / 38 |
| Status lines (from y = 222, pitch 38) | 15 | 15 | 26 |
| Power label / caught title / help | 13 / 22 / 14 | same | 23 / 38 / 24 |
| `+points` popup | 20 or 26 | same | 35 / 45 |
| Cleared card (at 0.2·H) | 14 + 40 | same | 24 + 70 |
| Countdown number, ring radius | 140, 120 | same | 245, 210 |
| Key hints (caps 24, text 14; bottom, H − 40) | wraps to several rows at 1080 wide | | |

- The layout is designed for landscape. My estimate is that at 1080 px wide the score and the right-aligned chapter name start to collide above k ≈ 2 (Chinese names are longer). k = 1.75 fits.
- For a bigger HUD: capture a smaller 9:16 frame and upscale, or add captions in the edit.
- World-anchored texts (serve prompt, power labels, popups, boss labels) use `e.Viewport.WorldToClient`.
- **UNVERIFIED**: the project memory note says `Viewport.Size` is the on-screen size during a capture. To be safe, make the floating view's client area exactly the capture size (1080×1920 fits a 4K screen) or at least the same 9:16 aspect, then check one frame that a power label sits on its capsule.

**Language**:
- `session.Language` has a public setter (it redraws). Set it with `Enum.Parse(tUiLanguage, "Chinese")` or `"English"`.
- With `owner = null` it starts as the Windows display language.
- Always English: REBOUND, LIVES, COMBO, BEST, LEVEL nn, WALL CLEARED, PAUSED, and the whole boss screen.
- Localised: power names and help, status lines, serve prompt, countdown caption, key hints, result text.
- Chapter names show "顺砌 Stretcher bond" in Chinese mode and only the English part otherwise.

## 6. BOSS KEY

**API** (`GameSession.cs` L441–485): public `ToggleBoss()`, `EnterBoss()` (focus-loss variant; also calls `_hook.Reset()`), `ExitBoss()`; private `EnterBoss(bool keysLost)`. Use `ToggleBoss()` and `ExitBoss()`; neither checks focus.

**Enter**:
1. `_pausedByBoss = Countdown > 0 || IsGameActive`; `Game.Pause()`; `Countdown = 0`; `Steer = 0`; `Held.Clear()`.
2. Saves the camera in `_gameCamera`.
3. `EnterDisguise(vp)` (`BreakoutSession.cs` L135):
   - builds `Disguise = new FacadeDisguise(this, new TowerFacade(Game.Bricks, Settings.Width))`;
   - sets the camera from `Disguise.CameraFor`: target `P(Tower.Width/2 + 12, Tower.Top/2 + 3)` = (30 m, 55.95 m), location 560 m out along `Plane.ZAxis`, `CameraUp = Plane.YAxis`, **`Camera35mmLensLength = 100`**.
4. `Boss = true; BossTime = 0;` redraw.
5. Prints one command-line line: `FacadeStudy: Elevation B updated · WWR nn % · 528 panels · U n.nn W/m²K`.
6. `Changed()` (null from a script; with a component it updates the outputs and schedules a GH solution).

**What is drawn**: conduit only. No document objects or layers are added and the display mode does not change. The sky bitmap switches at once to the grey presentation gradient.
- Geometry (`FacadeDisguise.Draw`): the 24-storey tower, 36 m × 105.9 m, with glass panels sampled from the current bricks (broken = clear glass), podium, crown, two neighbours, trees, people, grid bubbles A–E, level marks, overall dimension, and a 1:50 typical-bay detail at x = 66–87 m with leaders.
- Overlay (`DrawOverlay`): labels at 10–11 units; a metrics card 330 × 288 units at (24, 24); a title block "ELEVATION B — SOUTH 1:500" at (W − 380k, H − 70k).
- Only animation: `iter = (37 + (int)(BossTime/1.2)) % 120 + 1` and its progress bar.
- It is drawn in real metres, independent of `Size`.

**Portrait caveat** (computed): the sheet is landscape, about 178 m × 124 m. At 100 mm in 9:16 the view is about 134 m wide by 239 m tall.
- The tower takes only about 44 % of the height.
- The detail-bay labels are cut off at the right edge.
- The metrics card (578 × 504 px at k = 1.75) covers the grid bubbles and overlaps the crown at the top-left.
- Fix: after `ToggleBoss()` and before capturing that frame, re-aim. For example `vp.SetCameraLocations(P-like target around x ≈ 15 m, y ≈ 80 m, same direction)` with a lens of about 120 mm (visible width ≈ 13440/lens metres at 560 m). That shows the tower and both neighbours below the card; tune by eye. `ExitBoss` still restores the game camera.

**Exit**: `Boss = false`; camera restored with `SetViewProjection(_gameCamera, true)`; if the game was running, `StartCountdown(false)` gives `Countdown = 3` with the caption "回到游戏 / Back to the game", a 0.35 wash, a ring and key hints; at 0 it calls `Game.Resume()`.

**Timeline at 60 fps:**
1. Gameplay frames.
2. Frame F: call `ToggleBoss()` (and the portrait re-aim) before the capture; that frame is already the disguise.
3. Step 90 frames (1.5 s) with `BossTime += dt`.
4. Call `ExitBoss()`, then 180 frames of 3-2-1. To halve it, step the countdown with dt = 1/30, or set `Countdown` lower (the number shown is `ceil(Countdown)`, the ring is `Countdown/3`).
5. Gameplay continues.

## 7. RISKS

| Risk | Workaround |
|---|---|
| `Tick` gates on `AppIsForeground`, `OwnsFocus()` and `Command.InCommand()`; any of them can freeze the countdown or pause the game offline | Step manually (2B); at minimum set `AutoBoss = false` |
| The real-time `GameClock` keeps ticking between script calls | Stop `_timer` in the same call as `Start` |
| Script not on Rhino's UI thread: the hook lands on the wrong thread and view calls are unsafe | Check `RhinoApp.InvokeRequired`; use `RhinoApp.InvokeOnUiThread` |
| A long loop exceeds the `run_csharp` timeout | Render in chunks; re-acquire via `GameSession.Current`; keep bot state in `AppDomain.CurrentDomain.SetData` |
| The live keyboard hook reacts to real keys (the boss key toggles, Esc pauses) and switches the IME | Dispose it early: `((IDisposable)tGameSession.GetField("_hook", NP).GetValue(session)).Dispose()`; a later `Stop()` and `_hook.Reset()` are still safe |
| A real mouse hover over the view overwrites `TargetX`; a drag orbits | `((Rhino.UI.MouseCallback)tGameSession.GetField("_mouse", NP).GetValue(session)).Enabled = false`, and set `TargetX` every frame |
| `Begin` sets `View.Maximized = true`; its effect on a floating view is **UNVERIFIED** | After `Start`, check the view size and reset `Maximized` or the size; re-check the aspect |
| Capture size differs from the on-screen size: text anchors may shift, the sky bitmap is rebuilt on every size flip, and the lens rule flips between landscape and portrait | Floating view with the same pixel size or 9:16 aspect; capture only, no on-screen redraw |
| HUD scale is capped by `DpiScale` | See section 5; measure one frame first |
| Game over appends to the user's real `rebound-scores.txt` and shows the result screen | Never let it end: use `Mode = Zen`, or call `Stop()` first (`Stop` does not record). Zen shows LIVES "∞" and BRICKS instead of BEST |
| `Restart()` reseeds from `TickCount` and resets the camera | Start a new session with a fixed seed instead |
| `GameSession.Current` is a singleton: `Start` stops a running user game, and a component's Run would stop yours | Run in a spare Rhino instance with an empty document |
| Document objects and GH previews draw under the conduit | Empty doc, GH preview off |
| Errors are swallowed and printed (`Moyu display: …`, `Moyu HUD: …`, `Moyu background: …`, `Rebound stopped: …`) | Read the command history after a test frame; check `session.Stopped` |
| With manual stepping nothing checks that the view still exists | Do not close the floating view until after `session.Stop()` |
| Two Moyu assemblies loaded (dev build plus Yak package) | Pick the assembly whose `GameSession.Current` or type lookup you actually use, consistently |