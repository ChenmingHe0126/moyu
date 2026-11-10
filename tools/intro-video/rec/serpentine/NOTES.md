# Serpentine (Landscape panel): offline-recording cheat sheet

All paths are under `src\Moyu\`. This is from source reading only; nothing was run, and items marked **verify** are my assumptions about Rhino behaviour, not facts from the code.

Two things to know first:
- **Only two camera modes exist**: first person (`Follow == true`, the default, which the README screenshot also calls "跟随镜头") and free view. There is no separate "follow" mode; a third look has to be a scripted camera in free view.
- **A recorder already exists** at `<moyu repo>\tools\intro-video\rec\common\Recorder.cs`, with a Rebound shot in `rec\shots\Rebound.cs`. Its `Rec.Adopt / Step / Key / Tap / SGet / SSet / SCall / SetCountdown` already do most of sections 2–3. It references `Moyu.dll`, so public types can be used statically.

## 1. START

**Field** (all public):
- `SiteSurfaces.Create(int type, double size, int seed)` returns a `Brep` (`Games\Snake\SiteSurfaces.cs:15`). Types: 0 terrain, 1 torus, 2 tube, 3 saddle, 4 sphere. Seed only affects terrain and tube.
- Size: use `Moyu.Components.Landscape.SiteSurfaceComponent.DefaultSize()` (40 m in document units). A wrong unit scale breaks the boss screen (1.75 m figures, metres in the card).
- `SurfaceField.Build(BrepFace face, int cells, out string error)` (`SurfaceField.cs:75`); pass `brep.Faces[0]`, `cells` = 28 by default (the component allows 8–80).
- Torus at 28 cells gives about 28 × 12–13, wrapping both ways with no walls. Tube gives about 28 × 13, wrapping round the girth only, with walls at both ends. Read `field.Grid.Nu/Nv/WrapU/WrapV`; `field.Closed` is true for both.

**Settings** (`SnakeSettings`, public fields, `Core\SnakeGame.cs:35`):

| Field | Default | Note |
|---|---|---|
| `Mode` | `Classic` | `Blitz`, `Zen` also exist |
| `StartLevel` | 1 | read live when a fruit is eaten |
| `Seed` | `Environment.TickCount` | set it explicitly |
| `SlopeAffectsSpeed` | true | read live |
| `BlitzSeconds` | 180 | |
| `StartLength` | 4 | use 8 or more so the boss route is real (see section 6) |
| `SpawnItems` | true | read live every tick |

**Session**:
```csharp
public static SnakeSession Start(SurfaceField field, SnakeSettings settings, object owner,
                                 Action<GameSession> changed, out string error)   // SnakeSession.cs:59
```
- `owner` and `changed` may both be null: `Owner` is only tested with `is ILanguageOwner`, and `_changed?.Invoke`.
- The view comes from `GameSession.PickView` (`Common\GameSession.cs:121`), which is `RhinoDoc.ActiveDoc.Views.ActiveView` (or the first perspective view if a layout is active).
- To target a floating view, set `doc.Views.ActiveView = floatingView` just before `Start`, then assert `session.ViewportId == floatingView.ActiveViewportID`. The recorder's `Job.Start` already sets the active view.
- Alternative without the active view: the private ctor `SnakeSession(field, settings, owner, changed, RhinoDoc doc, RhinoView view)` (`:45`), then `Activator.CreateInstance(asm.GetType("Moyu.Games.Snake.SnakeConduit"), session)` (the class is internal sealed with a public ctor), then protected `GameSession.Begin(GameConduit, out string)`.

**`Begin` (`GameSession.cs:139–184`) does, in order:**
1. Stops `GameSession.Current` and sets `Current = this`.
2. Enables the conduit.
3. Installs `new KeyboardHook(OnKey)`, a thread hook on the calling thread; if it fails, the session stops and `Begin` returns false.
4. Enables the `OrbitMouse` callback and subscribes to document-close and Rhino-closing events.
5. Switches a parallel view to perspective and sets `View.Maximized = true`.
6. Turns grid, construction axes and world axes off.
7. Calls `SetStartCamera(vp)` and `StartCountdown(true)` (3 s, game paused).
8. Creates `_clock = new GameClock(Tick)`, a 15 ms WinForms timer.
9. Calls `RhinoApp.SetFocusToMainWindow()`, `View.Redraw()`, `Changed()`.

The ctor also reads the best score from `%APPDATA%\Moyu\serpentine-scores.txt` under the key `"{Mode}-{Nu}x{Nv}"`.

## 2. FRAME LOOP

`GameClock.OnTick` (`Common\GameClock.cs:26`) computes `dt = Math.Min(0.1, stopwatch delta)` and calls private `void GameSession.Tick(double dt)` (`GameSession.cs:234–293`):

1. Returns if `Stopped`; stops the session if the view is gone.
2. `Time += dt`.
3. `if (AutoBoss && !Boss && !GameClock.AppIsForeground()) EnterBoss();`
4. In boss mode: `BossTime += dt`, redraws only when `(int)(BossTime/1.2)` changes, returns.
5. `if (!IsOver && (Command.InCommand() || !OwnsFocus()))`: during a countdown it just redraws and returns (the countdown freezes); otherwise it calls `Pause()` if the game is active.
6. Calls `ResetCamera()` if the view went parallel.
7. `TickAlways(dt)`; for Serpentine this is `if (Follow) FollowCamera(dt)` (`SnakeSession.cs:88`).
8. If `Countdown > 0`: decrements it; at 0 calls `ResumeGame()` and `Changed()`; redraws and returns.
9. `if (!IsGamePaused) PlayTime += dt`.
10. `TickGame(dt)`, which is `Game.Tick(dt)` plus `KeepCameraAbove()` when `!Follow && !Field.Closed`.
11. `NoteGameOver()`; `OverTime += dt` if over; redraws if `NeedsRedraw`.

Any exception inside prints `"Serpentine stopped: …"` and calls `Stop()`, so check `session.Stopped` every frame.

**Going offline:**
- Set `session.AutoBoss = false` (public setter).
- Dispose the private field `GameSession._clock` (`BindingFlags.Instance | NonPublic`, looked up on `typeof(GameSession)`). A second dispose from `Stop()` is harmless.
- Disable `_mouse` (`MouseCallback.Enabled = false`).
- Do all of this in the same UI-thread call as `Start`, so the 15 ms timer never fires. This is `Rec.Adopt`.
- Do not call the real `Tick`: step 5 depends on Win32 focus and `Command.InCommand()`. Replicate steps 2, 4 and 7–11 instead, which is exactly `Rec.Step(dt)`.
- `Time`, `BossTime`, `Countdown`, `PlayTime`, `OverTime` and `Boss` have private setters declared on `GameSession`. Get the `PropertyInfo` from `typeof(GameSession)`, not from `SnakeSession`.
- `SnakeGame.Tick(dt)`, `.Pause()`, `.Resume()` are public; `TickAlways`, `TickGame`, `ResumeGame`, `GameKey` are protected overrides on `SnakeSession`; `NoteGameOver` is private on `GameSession`.

**Wall-clock dependencies (complete list):**

| Where | What | Effect offline |
|---|---|---|
| `GameClock._watch` | Stopwatch for `dt` | gone once the clock is disposed |
| `GameConduit._backgroundClock` (`GameConduit.cs:45,135`) | sky repaint at most every 120 ms real time during a palette glide | frames faster than 120 ms may show the sky updating in uneven steps for the 1.6 s glide; cosmetic. Nulling private `_background` each frame forces a repaint |
| `KeyboardHook._clock` | lost-release detection | irrelevant when injecting |
| `SnakeSettings.Seed` default; `SnakeSession.NewGame()` (`:95`) | `Environment.TickCount` | set `Seed` yourself and never call `Restart()` |
| `GameScores.Record` | `DateTime.Now` in the score line | only on game over |

Everything else is driven by session time or game time:
- **`session.Time`**: item bob, grape spin, ice-cube rotation, bomb pulse/blink/fuse, tongue flick `(int)(Time*4)%4==0`, power-up end blink, dead-head flash, chapter blend (`BlendSeconds = 1.6`), ground repaint throttle (0.1 s).
- **`Game.Elapsed`**: item pop-in (0.35 s) and expiry, burst particles (0.7 s; bomb 1.4 s), score pop-ups (1.2 s), level card (2.6 s), combo and power timers.
- **`Countdown`, `PlayTime`, `OverTime`, `BossTime`**: overlays; key hints show for the first 7 s of `PlayTime` then fade over 1.5 s.
- **`Game.StepProgress`**: the body glides between cells, so motion is smooth at any dt.

A manual loop must advance `Time`, or the ambient animation freezes.

## 3. INPUT

Live path: `KeyboardHook.Proc` calls private `bool GameSession.OnKey(Keys key, bool down, bool repeat, bool act)` (`:311`). It maintains `protected readonly HashSet<Keys> Held` and handles the boss key, Esc/P and Home, then calls `GameKey(key, down, repeat)`. `OnKey` also bails out on `!OwnsFocus()`, on a real Ctrl/Alt being down, and during a countdown, so do not inject through it.

| Key | Action | Semantics |
|---|---|---|
| `Keys.Left` / `A` | `SideTurn(true)`, turn left as the head sees it | `down && !repeat` only |
| `Keys.Right` / `D` | `SideTurn(false)` | same |
| `Keys.ShiftKey` / `Up` / `W` | sprint ×1.8 | level-based: `Game.Boost = SprintKeys.Any(Held.Contains)`; needs `Held` updated first |
| `Keys.C` | `ToggleCamera()` | press only |
| `Keys.Escape` / `P` | pause; Esc resumes; Shift+Esc quits | handled in `OnKey`, not `GameKey` |
| `Keys.Home` | `ResetCamera()` | `OnKey` |
| `Keys.Oemtilde` (scan code 0x29) | `ToggleBoss()` | `OnKey` |
| Enter / Space on the result screen | `Restart()` after 0.8 s | `OnKey` |

**Injection:**
- Keys: update `Held`, then invoke protected `GameKey`. This is `Rec.Key(key, down)` and `Rec.Tap(key)`.
- A bot is better served by the public rules API:
  - `game.Turn(int du, int dv)` takes an absolute grid direction. It returns false if it equals `PlannedHeading`, is a reversal, or the queue of `BufferedTurns = 2` is full. One queued turn is applied per rules step.
  - `game.Boost = true/false`.
  - `TurnLeft()/TurnRight()` are UV-relative and can be mirrored on screen where the normals were flipped; the session's `SideTurn` corrects for that.
- Pause, boss entry and game over clear `Boost` and the sprint keys, so re-press after a countdown.

**Mouse:** Serpentine has no `GameMouse`. `public override void Orbit(int dx, int dy)` only acts in free view: 0.008 rad per pixel around `CameraTarget` with up = Z, elevation clamped to 8–89° only on open sites. For smooth slow moves set the camera yourself (`vp.SetCameraLocations(target, location); vp.CameraUp = …`); integer `dx` gives a minimum of about 27°/s at 60 fps.

## 4. STATE FOR A BOT

**`session.Game`** (`SnakeGame`, all public getters):

| Member | Type / meaning |
|---|---|
| `Grid` | `SurfaceGrid`: `Nu`, `Nv`, `WrapU`, `WrapV`, `TryStep(Cell, du, dv, out Cell)` (wrap-aware; false at a wall or hole), `GridDistance`, `IsBlocked`, `FreeCells()` |
| `Head`, `Body`, `Length` | `Cell` (struct, public `U`, `V`); `Body` is head-first |
| `Heading`, `PlannedHeading` | `(int du, int dv)`, i.e. `ValueTuple<int,int>` |
| `Occupies(Cell)` | body occupancy |
| `Items` | `IReadOnlyList<Item>` with public fields `Cell`, `Kind` (`Apple, Grape, Melon, Bomb, Ice, Ghost`), `Born`, `Expires` (game seconds) |
| `Speed`, `StepProgress` | cells per second now; 0→1 between steps |
| `Level`, `FruitsEaten`, `Score`, `Combo`, `LastEatTime`, `Elapsed` | |
| `GhostActive`, `IceActive`, `GhostUntil`, `IceUntil` | |
| `State`, `Cause`, `IsOver`, `LastEvent`, `LevelUpTime`, `SlopeFactor` | `State` is `Playing, Paused, Dead, TimeUp, Won` |

**Session and field:**
- `session.Follow`, `session.Field`.
- `Field.Step` (cell size in model units), `Field.Bounds`, `Centre(c)`, `Normal(c)`, `Lifted(c, h)`, `Direction(c, du, dv)`.
- `session.SegmentAt(body, i, lift, out normal)` and `session.HeadForward(body)` give drawn positions.

**Speed:** `SpeedFor(level) = min(15, 3.4 + 0.55·(level−1))` cells/s, × 1.8 sprint, × 0.6 ice, × slope factor clamped to 0.6–1.5.
- Level 1 is about 17.6 frames per cell.
- On a torus or tube the slope factor swings hard when moving round the girth, and the HUD shows 上坡/下坡. Set `Settings.SlopeAffectsSpeed = false` for even pacing.

**Bot (decide once per rules step):**
1. Trigger when `game.Head` changed since the last frame and `PlannedHeading == Heading`.
2. Target the nearest non-bomb item by wrapped grid distance, or by BFS over `TryStep`. Treat body cells and bomb cells as blocked.
3. Keep straight if the cell ahead reduces the distance. Otherwise turn onto the axis with the larger remaining wrapped delta; never reverse. This gives L-shaped paths, not staircases.
4. Safety: flood-fill from the chosen next cell with the body blocked. If it reaches fewer than `Length + 2` cells, take the legal move with the largest fill.
5. Issue at most one `game.Turn(du, dv)` per step.
6. For calm first-person footage, enforce 4 or more steps between turns unless safety demands one; each turn swings the whole view in about 0.3 s.

On a torus there are no walls, so with a snake shorter than about 30 this cannot die in 30 s.

**Staging highlights:**
- **Determinism:** `Settings.Seed` feeds `new Random(Seed)` in the game ctor. Same seed, same inputs and same dt sequence give the same game. The seed also drives the boss-screen trees.
- **Item placement:**
  - `Settings.SpawnItems = false` stops all random spawns, including the two standing apples.
  - To suppress only bombs or power-ups, set the private double fields `_nextBomb` / `_nextPower` on `SnakeGame` to `double.PositiveInfinity`.
  - `public void PlaceItem(ItemKind kind, Cell cell, double life = ∞)` (`SnakeGame.cs:222`). It does not check the cell, so pick one where `!grid.IsBlocked(c) && !game.Occupies(c)`. Walk ahead of the head with repeated `Grid.TryStep(c, h.du, h.dv, out c)`.
- **Combo:**
  - A fruit eaten within `ComboWindow = 4` s of the previous one increments `Combo` (max 5). Points are base × combo: apple 10 (+1 segment), grape 30 (+3), melon 80 (+5).
  - Place 5 fruits 3–5 cells apart along the path.
  - Example: apple, grape, apple, grape, melon gives ×1…×5, 620 points, +13 length.
  - The HUD shows `连击 ×N` with a draining hairline, plus `+points` and `×N` pop-ups.
- **Level card and chapter palette:**
  - `Level = StartLevel + FruitsEaten / 5` is recomputed on every fruit.
  - Palette bands (`Palette.Band`): 1–3 庭院, 4–6 草坡, 7–9 丘陵, 10–12 峡谷, 13+ 山脊.
  - Start with `StartLevel = 3` and the fifth fruit gives the card `LEVEL 04 · 草坡 Meadow · 更快了` (2.6 s) and a 1.6 s palette glide (sand ground and teal snake → sage ground and peach snake).
  - To force it instantly, set the private setters `Level` and `LevelUpTime = Elapsed` on `typeof(SnakeGame)`, keeping `StartLevel` / `FruitsEaten` consistent or the next fruit overwrites it.
- **Power-ups:** `PlaceItem(ItemKind.Ghost | Ice, cell)` lasts 6 s. Ghost draws the body translucent and lets it cross itself; ice tints it and slows to ×0.6. Or set `GhostUntil` / `IceUntil` (private setters) to `Elapsed + 6`.
- **Sprint:** `game.Boost = true` shows the `冲刺 ×1.8` line and an accent-coloured SPEED figure.
- **Camera switch:**
  - `Rec.Tap(Keys.C)` or private `ToggleCamera()`. Both directions are instant cuts.
  - The HUD line changes between `第一人称 · 视角跟着蛇头` and `自由视角 · 拖动鼠标转模型`.
  - In free view on a closed surface nothing touches the camera per frame, so a scripted orbit is safe.
  - Optional eased fly-in to first person: before setting `Follow = true`, seed the private fields `_camReady = true`, `_camTarget`, `_camNormal`, `_camUp`, `_camDist` from the current camera. `FollowCamera` then eases them (rates 7, 4, 9 and 2.5–12 per second).
- **Hide key hints:** set `PlayTime` to 9 after the countdown. Skip the opening countdown with `Rec.SetCountdown(0)`.
- **Torus start position (verify):** the snake starts at `(Nu/2, Nv/2)` heading +U. On the torus I believe this is the inner equator, inside the hole, where the follow camera's `MeshRay` limiter pulls it in close. Pre-roll some unrecorded `Step()`s with the bot heading round the tube to the top or outside before capturing.

## 5. CAMERA & PORTRAIT LAYOUT

**`SetStartCamera`** (`SnakeSession.cs:201`): sets `vp.Camera35mmLensLength = 35`, then either snaps the follow camera or calls `Overview(vp)`. It does not look at viewport size or aspect. The lens is only reset by `ResetCamera` (Home, `Restart`, parallel fix), so a lens you set after `Begin` persists, including across the boss screen.

**Follow camera** (`ApplyFollow`, `:268`):
- Target is the head lifted 0.4 Step.
- Direction is `normal·cos(_tilt) − up·sin(_tilt)`, with `_tilt` 0.95 on closed forms and 1.0 on open ones (private field).
- Distance is `Step × 11` (closed) or `× 14` (open), shortened by a mesh ray-cast.
- It looks 2.5 (closed) or 5 (open) Steps ahead.

**Portrait field of view (verify with `vp.GetCameraAngle`):** I believe Rhino ties lens length to the smaller frustum dimension. If so, at 35 mm in 9:16 the horizontal field is about 38° and the vertical about 63°.
- First person then shows only about 7–8 cells across at the head, but a long view ahead up the screen, with the snake about 60 % down the frame. That is usable; for more side context set the lens to 24–28 mm.
- `Overview` (`CirculationDisguise.cs:354`) uses direction (0.32, −0.82, 0.64), distance = bounds diagonal × 1.05, and a target shifted 7 % sideways to clear the boss card. It is tuned for landscape. In portrait the torus is likely clipped on the right, and the terrain and tube overflow sideways.
- Fix for free view: after the toggle, set your own camera on `Field.Bounds`. Centre the target, call `vp.ZoomBoundingBox(...)`, use a steeper elevation, and for the tube or terrain turn the long axis up the screen.

**HUD scale:** `k = max(1, min(DisplayPipeline.DpiScale, FrameSize.Height / 720))` (`GameConduit.cs:34`). Layout is in 96-dpi units × k, positioned from `e.Display.FrameSize`.
- A 1080×1920 capture gives k = DpiScale (up to 2.67); the HUD does not grow with capture size.
- At 100 % scaling the score digits are 68 px and labels 11–15 px, which is tiny on a phone. An earlier report in the repo mentions a 4K screen at 175 %; if that applies, digits are about 119 px and status text 26 px.
- A `scale=0.5` preview (540×960) clamps k to 1.33, so its HUD proportions do not match the full-size render.
- **Verify** what `DpiScale` is during `CaptureToBitmap`: the LEVEL hairline is 150·k px long.

**Positions (units × k):**

| Element | Position |
|---|---|
| Game label | (52, 44) |
| Score | (48, 62), height 68 |
| `LENGTH / SPEED / BEST` | x = 52 + 108·i, labels at y 150, values at y 168 |
| Level block | right-aligned at W − 52: `LEVEL nn` at y 44, chapter name at y 64, hairline (150 long) at y 104 |
| Status lines | from y 222, 38 apart |
| Key hints | centred at H − 40, wrapping upward into rows when wider than W − 48 (expect 2–3 rows at 1080) |
| Countdown | ring radius 120 at frame centre |

Nothing collides at 1080 wide for k ≤ 2.67. Pop-ups and boss plaza labels use `e.Viewport.WorldToClient` — **verify** they land on the fruit when the capture size differs from the on-screen size.

**Language:** `session.Language` is a public get/set of `Moyu.Games.Common.Core.UiLanguage { Chinese, English }`. With `owner == null` it starts as the Windows display language, so set it explicitly. Labels such as `SERPENTINE`, `LENGTH`, `LEVEL 01` stay English; Chinese mode shows "庭院 Courtyard". The boss screen is always English.

## 6. BOSS KEY

**API (public):** `ToggleBoss()`, `EnterBoss()`, `ExitBoss()`; prefer `ToggleBoss()`.

**Entering** (private `EnterBoss(bool)`, `GameSession.cs:451`):
- Records whether the game was running, pauses it, zeroes the countdown, clears input and `Held`.
- Saves the camera in `_gameCamera`.
- Calls `EnterDisguise` (`SnakeSession.cs:295`), which builds `SiteAnalysis` once and a `CirculationDisguise` from the current body and items, then sets the camera to `Overview(Field, 1.15)` with up = Z and a 40 mm lens.
- Sets `Boss = true`, `BossTime = 0`, redraws.
- Prints one command-line line: `CirculationStudy: Route A updated · L = … m · avg slope … %`.

**What the disguise draws:**
- It is conduit-only: no document objects, no layers, no display-mode change; only the camera and lens change.
- Background: pale grey gradient.
- Scene: slope false-colour mesh, contours, site edge, paved route from A (tail) to B (head) with arrowheads, benches, trees with shadows, scale figures. Items become plazas labelled `N1 Lookout`, `N2 Pavilion`, …
- Card, top-left: `Landscape Plan · Route A`, 8 metric rows, and `Analysing slope… pass N / 40` with `N = (11 + (int)(BossTime/1.2)) % 40 + 1`.
- Slope legend bottom-right.
- A snake shorter than 6 cells gets a decoy route (`IsDecoy`), so have a length of 10–25 and 3–5 items on the field first.
- It looks most convincing on terrain (type 0; card subtitle "Landscape walk over site terrain"). On a torus or tube, trees and people stand along the surface normals.
- In portrait, re-frame the camera after `ToggleBoss()`; nothing resets it during boss mode. The card covers about 300k × 288k at the top-left.

**Leaving** (`ExitBoss`, `:472`): restores `_gameCamera` (your boss camera override is discarded safely). If the game was running it starts `StartCountdown(false)`: 3 s, caption `回到游戏` / `Back to the game`, ring and digits, key hints at full alpha, then play resumes. Nothing is printed.

**Grasshopper side:** effects exist only through the `changed` callback (`PathStudyComponent.OnSessionChanged`: message `Route A · N m`, State output, scheduled solution). With `changed = null` there are none.

**Timing at 60 fps:**
1. Call `ToggleBoss()` before a frame's `Step`; that frame is already the disguise.
2. Hold 90 frames. `Step` advances `BossTime`, and the pass counter ticks 12 → 13 at frame 72.
3. Call `ToggleBoss()` again, then 180 frames of countdown (3, 2, 1 at 60 frames each), then play.

Stop bot input while `session.Boss || session.Countdown > 0`.

## 7. RISKS

| Risk | Workaround |
|---|---|
| Real `Tick` / `OnKey` depend on `Command.InCommand()`, Win32 focus and Rhino being foreground; they pause the game, freeze the countdown or auto-enter boss mode | `AutoBoss = false`, dispose `_clock`, step manually (`Rec.Adopt` + `Rec.Step`), inject through `GameKey` or the `SnakeGame` API |
| Threading: hook, timer and viewport calls must be on Rhino's UI thread | `RhinoApp.InvokeOnUiThread`; start and adopt in one call so the 15 ms clock never fires |
| A long blocking loop times out the MCP call | pump a few frames per WinForms-timer tick and poll status, as `Recorder.cs` `Job.Pump` does |
| The hook and `OrbitMouse` stay live; real keys in Rhino (`` ` ``, Esc) or mouse drags change the take | keep hands off Rhino; `Adopt` disables `_mouse`; optionally dispose `_hook` (double dispose and `Reset()` are safe) |
| Death or win calls `OnGameOver`, which appends to `%APPDATA%\Moyu\serpentine-scores.txt` and can raise the user's real best; `Rec.Step` does call `NoteGameOver` | never let the take end (safe bot, bombs off); or back the file up; end with `session.Stop()`, which records nothing |
| The HUD "BEST" figure shows the user's real best for that mode and grid | accept it, or set `Best` (private setter on `SnakeSession`) |
| `Restart()` / `NewGame()` reseed from `TickCount` and refocus the main window | start a fresh session per take |
| `GameSession.Current` is static, one session at a time; a Run click on any game component stops yours | leave the Grasshopper canvas alone; `Start` already stops any previous session |
| `Begin` sets `View.Maximized = true` and focuses Rhino's main window; `Stop()` restores camera, grid, axes and maximised state | harmless on a floating view (the existing Rebound shot relies on it); always call `Stop()` in teardown |
| Private setters inherited from `GameSession` are invisible through `typeof(SnakeSession)` | resolve those properties on `typeof(GameSession)` |
| Capture also draws document objects and Grasshopper previews | use an empty document; build the surface in the script, not from a previewing component |
| HUD scale depends on DPI; preview and final differ; overview and boss framing are landscape-tuned; pop-up projection in an upscaled capture is untested | section 5: check one full-size frame of first person, free view, a fruit pop-up and the boss screen before recording |
| Conduit exceptions are swallowed and printed as `Moyu display:` / `Moyu HUD:` | check the command history after a test frame |
| Two assemblies named `Moyu` could be loaded (Yak and a dev build); Grasshopper must have loaded the .gha | check `Assembly.Location`; open Grasshopper once first |
| Torus start inside the hole; slope speed swings; follow camera pulled in near folds | pre-roll, `SlopeAffectsSpeed = false`, or keep the route on the outer and top side |

**Files read:**
- `src\Moyu\Games\Common\`: `GameSession.cs`, `GameClock.cs`, `GameConduit.cs`, `KeyboardHook.cs`, `Palette.cs`, `GameScores.cs`, `ILanguageOwner.cs`, `Core\KeyTransitions.cs`, `Core\UiLanguage.cs`
- `src\Moyu\Games\Snake\`: `SnakeSession.cs`, `SnakeConduit.cs`, `SurfaceField.cs`, `SiteSurfaces.cs`, `CirculationDisguise.cs`, `Core\SnakeGame.cs`, `Core\SurfaceGrid.cs`, `Core\RouteMetrics.cs`, `Core\LandscapePlan.cs` (first 80 lines)
- `src\Moyu\Components\`: `GameComponent.cs`, `RunButtonAttributes.cs`, `Landscape\PathStudyComponent.cs`, `Landscape\SiteSurfaceComponent.cs`
- `tests\Moyu.Core.Tests\TestsSnake.cs` (lines 1–518), `tests\Moyu.RhinoTests\GhFileSnake.cs`
- `README.md` (lines 60–164), `docs\visual-style.md`
- `<moyu repo>\tools\intro-video\rec\common\Recorder.cs`, `<moyu repo>\tools\intro-video\rec\shots\Rebound.cs`, `<moyu repo>\tools\intro-video\rec\MoyuRec.csproj`