# VoxStack offline-capture cheat sheet

Everything below is read from source; nothing was run in Rhino. Items marked **[verify]** are inferences to confirm with one test frame.

Files (all under `src\Moyu\`):
- `Games\Common\GameSession.cs`, `GameClock.cs`, `KeyboardHook.cs`, `GameConduit.cs`, `Palette.cs`, `GameScores.cs`
- `Games\Stack\StackSession.cs`, `StackConduit.cs`, `Gumball.cs`, `MassingDisguise.cs`
- `Games\Stack\Core\StackGame.cs`, `Well.cs`, `Bag.cs`, `PieceDef.cs`, `Int3.cs`, `Polycube.cs`, `MassingMetrics.cs`
- `Components\Massing\MassingStudyComponent.cs`, `Components\GameComponent.cs`

Assembly name is `Moyu`. Namespaces: `Moyu.Games.Common`, `Moyu.Games.Common.Core` (`UiLanguage`), `Moyu.Games.Stack`, `Moyu.Games.Stack.Core`.

Almost all types are public (`GameSession`, `StackSession`, `StackGame`, `StackSettings`, `Well`, `PieceDef`, `PieceSets`, `Int3`, `Polycube`, `Gumball`, `MassingDisguise`, `GameClock`, `KeyboardHook`, `GameConduit`). Only `StackConduit` is internal, and `GameSession.OrbitMouse` is a private nested class.

## 1. START

**Factory** (StackSession.cs:54), public static:
```csharp
public static StackSession Start(StackSettings settings, Plane plane, double voxelSize,
                                 object owner, Action<GameSession> changed, out string error)
```
- It calls `PickView`, then `StopCurrent()`, then the private constructor, then `Begin(new StackConduit(session), out error)`. It returns null and sets `error` on failure.
- `owner` may be null. It is only used for `owner is ILanguageOwner` (GameSession.cs:106) and by the components' `IsRunning`.
- `changed` may be null (`_changed?.Invoke`, GameSession.cs:222). Null owner plus null callback means no Grasshopper side effects at all.
- `plane`: use `Plane.WorldXY`.
- `voxelSize`: the component default is 7.2 × metres-to-model-units (MassingStudyComponent.cs:156). Any positive value works because the camera scales with it.

**`StackSettings`** (StackGame.cs:11) has public fields, not properties, so use `GetField`:
- `int Nx = 5, Ny = 5, Nz = 12`
- `StackMode Mode = Marathon` (enum `Marathon, Blitz, Zen`)
- `int StartLevel = 1`
- `int Seed = Environment.TickCount` (set it explicitly)
- `IReadOnlyList<PieceDef> Pieces = PieceSets.Get(PieceSets.Standard)`
- `double BlitzSeconds = 180`

The `StackGame` constructor throws if a piece does not fit: its longest side must be ≤ min(Nx, Ny) and its Z size < Nz (StackGame.cs:94-99).

**What the component does** (MassingStudyComponent.cs:163-187): `Run()` copies `_pending`, forces `Seed = Environment.TickCount`, and calls `Start(settings, _plane, _size, this, OnSessionChanged, out error)`. A game started through the component can never be seeded, so call `Start` yourself.

**View choice** — `GameSession.PickView(out RhinoDoc doc, out string error)` (GameSession.cs:121, public static):
- It takes `RhinoDoc.ActiveDoc.Views.ActiveView`. If that is a `RhinoPageView`, it takes the first perspective standard view.
- To target a floating view, create it and make it active before `Start`:
  ```csharp
  var view = doc.Views.Add("Moyu", DefinedViewportProjection.Perspective, rect, true);
  doc.Views.ActiveView = view;
  ```
- Floating views are a supported case: `OwnsFocus` accepts `KeyboardHook.Root(View.Handle)` (GameSession.cs:299-304).
- If `ActiveView` will not stick, bypass `PickView` by reflection:
  ```csharp
  var ctor = tStackSession.GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance)[0];
  // (StackSettings, Plane, double, object, Action<GameSession>, RhinoDoc, RhinoView)  — private, StackSession.cs:40
  var s = ctor.Invoke(new object[]{ settings, Plane.WorldXY, 7.2, null, null, doc, view });
  var conduit = Activator.CreateInstance(asm.GetType("Moyu.Games.Stack.StackConduit"), s); // internal type, public ctor
  var begin = tGameSession.GetMethod("Begin", BindingFlags.NonPublic|BindingFlags.Instance); // protected bool Begin(GameConduit, out string)
  ```

**`Begin`** (GameSession.cs:139-184), in order:
1. `Current?.Stop(); Current = this;` and `conduit.Enabled = true`.
2. `_hook = new KeyboardHook(OnKey)`. This is a `WH_KEYBOARD` hook on the calling thread (`GetCurrentThreadId`). If it is not installed, the session stops and returns false.
3. `_mouse = new OrbitMouse(this){Enabled = true}`, and it subscribes `RhinoDoc.CloseDocument` and `RhinoApp.Closing`.
4. A parallel view is changed with `ChangeToPerspectiveProjection(true, 28)`.
5. `View.Maximized = true`; grid, construction axes and world axes are turned off.
6. `SetStartCamera(vp)`.
7. `StartCountdown(true)`: the game is paused, `Countdown = 3`, `CountdownIsStart = true`, `PlayTime = 0`.
8. `_clock = new GameClock(Tick)`: a WinForms timer at 15 ms that starts immediately.
9. `RhinoApp.SetFocusToMainWindow(); View.Redraw(); Changed();`

**Must be called on Rhino's UI thread** (hook, WinForms timer, view calls).

**Do this in the same script call as `Start`**, before control returns to the message loop:
- `session.AutoBoss = false` (public setter, GameSession.cs:47).
- Stop the real-time timer (section 2).
- `session.Language = …` (section 5).
- Optionally dispose `_hook` and disable `_mouse` (section 7).

**Teardown**: `session.Stop()` (public, GameSession.cs:487) restores the user's camera, grid, axes and maximized state, and disposes the clock, hook and conduit. Then `view.Close()`.

## 2. FRAME LOOP

**Clock** (GameClock.cs): private fields are `System.Windows.Forms.Timer _timer`, `Stopwatch _watch`, `Action<double> _tick`, `double _last`. `OnTick` computes `dt = Math.Min(0.1, now - _last)` and calls `_tick(dt)`, which is the session's private `void Tick(double dt)` (GameSession.cs:234).

**Stop real time and get the per-frame method:**
```csharp
const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
var clock = tGameSession.GetField("_clock", NP).GetValue(session);                  // GameClock
((System.Windows.Forms.Timer)tGameClock.GetField("_timer", NP).GetValue(clock)).Stop();
var tick  = (Action<double>)tGameClock.GetField("_tick", NP).GetValue(clock);       // bound to session.Tick
// per frame: tick(1.0/60); using (var bmp = view.CaptureToBitmap(size)) bmp.Save(...);
```
`_timer.Start()` resumes live play; the 0.1 s clamp prevents a jump. `clock.Dispose()` also works, and `Stop()` disposing it again later is safe.

**`Tick(dt)` step by step** (GameSession.cs:234-293):
1. Return if `Stopped`. If the view is gone (`Doc.Views.Find(ViewportId) == null`), `Stop()`.
2. `Time += dt`.
3. `if (AutoBoss && !Boss && !GameClock.AppIsForeground()) EnterBoss();` — `AppIsForeground` compares the foreground window's process id with Rhino's.
4. If `Boss`: `BossTime += dt`, redraw when `(int)(BossTime/1.2)` changes, return.
5. `command = Rhino.Commands.Command.InCommand()`. If `!IsOver && (command || !OwnsFocus())`:
   - while `Countdown > 0` it only redraws and returns, so the countdown freezes;
   - otherwise, if the game is active, it calls `Pause()` and the PAUSED overlay appears.
   
   `OwnsFocus()` (private, :299) needs `GetFocus()`'s root window to be `RhinoApp.MainWindowHandle()` or the floating view's frame.
6. `if (!command && Viewport.IsParallelProjection) ResetCamera();`
7. `TickAlways(dt)` — in `StackSession` this runs `AnimateTurn` for the Z / X view turn (:90).
8. If `Countdown > 0`: `Countdown -= dt`; at ≤ 0 it calls `ResumeGame()` and `Changed()`. Redraw, return. The countdown is 180 frames at 1/60.
9. `if (!IsGamePaused) PlayTime += dt;`
10. `TickGame(dt)` (StackSession.cs:95) runs `RepeatMoves(); Game.Tick(dt); KeepCameraAbove();`.
11. `NoteGameOver(); if (IsOver) OverTime += dt; if (NeedsRedraw) Redraw();`
12. Any exception is caught: it prints `VoxStack stopped: …` and calls `Stop()`. Check `session.Stopped` after each frame.

**Focus-independent alternative.** Because of steps 3 and 5, `Tick` only works while Rhino's main window (or the floating frame) has focus and no Rhino command is running. To avoid that, replicate it:
```csharp
void Set(string p, object v) => tGameSession.GetProperty(p).SetValue(session, v); // private setters; PropertyInfo must come from GameSession, not StackSession
void Step(double dt) {
  Set("Time", session.Time + dt);
  if (session.Boss) { Set("BossTime", session.BossTime + dt); return; }
  tickAlways.Invoke(session, new object[]{dt});       // protected TickAlways (NonPublic|Instance on StackSession)
  if (session.Countdown > 0) {
    double c = session.Countdown - dt;
    if (c <= 0) { c = 0; game.Resume(); }
    Set("Countdown", c); return;
  }
  if (game.State != StackState.Paused) Set("PlayTime", session.PlayTime + dt);
  tickGame.Invoke(session, new object[]{dt});         // protected TickGame
  noteGameOver.Invoke(session, null);                 // private NoteGameOver on GameSession
  if (game.IsOver) Set("OverTime", session.OverTime + dt);
}
```
If `SetValue` complains about a missing setter, use the backing fields (`<Time>k__BackingField` and so on) on `GameSession`. `CaptureToBitmap` renders its own frame, so no `Redraw` is needed.

**Wall-clock dependencies (complete list):**

| Where | What | Effect offline |
|---|---|---|
| `GameClock._watch` (Stopwatch) | real dt | gone once `_timer` is stopped |
| `KeyboardHook._clock` (Stopwatch) | lost-release test, 250 ms (`KeyTransitions.Fresh`) | only for real key events |
| `GameConduit._backgroundClock` (Stopwatch, GameConduit.cs:45, 135) | during a palette glide the sky bitmap is repainted only if more than 120 ms of real time passed | the sky may update unevenly during the 1.6 s chapter blend; see fix below |
| `StackSettings.Seed` default and `MassingStudyComponent.Run` | `Environment.TickCount` | pass your own Seed to `Start` |
| `StackSession.NewGame()` (:104) | `Settings.Seed = Environment.TickCount` | `Restart()` (and Enter on the result screen) reseeds, so never use it for seeded scenes |
| `GameScores.Record` | `DateTime.Now`, writes `%APPDATA%\Moyu\voxstack-scores.txt` | only at game over (section 7) |

- **Sky fix:** before each capture during a blend, reset the conduit's private `_backgroundKey` to its default value. The conduit is the session's private `_conduit` field.
  ```csharp
  var f = tGameConduit.GetField("_backgroundKey", NP);
  f.SetValue(conduit, Activator.CreateInstance(f.FieldType));
  ```
  The size mismatch then forces a repaint, and the old bitmap is disposed by the existing code.
- **Core:** `StackGame` has no wall clock. `Elapsed` accumulates dt and the RNG is `new Random(Settings.Seed)` (StackGame.cs:101). The same seed, inputs and dt sequence give the same game.

**Animations and their time source** (all advance with the dt you feed):

| Animation | Source | Duration |
|---|---|---|
| Chapter palette blend | `session.Time - _blendStart` (StackConduit.cs:59) | 1.6 s |
| Gumball ring glow after a turn | `session.Time - Gumball.LastTurnTime` | 0.35 s |
| Layer clear (shrinking glow cubes, 26 balls per layer) | `game.ClearProgress` | 0.3 s (18 frames) |
| Clear pop-up ("FLOOR CLEARED +100") | `game.Elapsed - LastClear.Time` | 1.6 s |
| Level card ("LEVEL 04" + chapter name) | `game.Elapsed - game.LevelUpTime`, needs `Level > 1` | 2.6 s |
| 3-2-1 ring | `session.Countdown` | 3 s |
| Key-hint row | `session.PlayTime` | full to 7 s, gone at 8.5 s |
| Gumball how-to text | `session.PlayTime < 60` | fades 55–60 s |
| Z / X view turn | `_turnLeft`, in `TickAlways` | 0.15 s per quarter turn |
| Boss "iter N / 100" | `session.BossTime` | steps every 1.2 s |
| Result screen | `session.OverTime` | — |

## 3. INPUT

**Path:** Windows → `KeyboardHook.Proc` → `GameSession.OnKey(Keys key, bool down, bool repeat, bool act)` (private, :311) → `StackSession.GameKey(Keys key, bool down, bool repeat)` (protected override, StackSession.cs:164).

`OnKey` gates, in order:
- It adds or removes the key in `Held` (`protected readonly HashSet<Keys>` on `GameSession`).
- It passes the key on if Win, Ctrl or Alt is physically down (`GetKeyState`), or if `!OwnsFocus()`.
- The boss key (`GameSession.BossKey = Keys.Oemtilde`, matched by scan code 0x29) calls `ToggleBoss()`.
- In boss mode everything goes to Rhino. In a Rhino command the key is passed on.
- On the result screen, Enter or Space calls `Restart()` once `OverTime > 0.8`; Esc calls `Stop()`.
- During the countdown keys are swallowed and ignored.
- Paused: Esc resumes, Shift+Esc stops.
- Playing: Esc or P pauses, Home calls `ResetCamera()`, everything else goes to `GameKey`, then `NoteGameOver()`.

**`GameKey` map** (only `down && !repeat` acts; OS auto-repeat is ignored):

| Key | Action |
|---|---|
| `Keys.ShiftKey` | `Game.SoftDrop = down` (hold) |
| Up / Down / Left / Right | `StartMove(key, ±forward / ±right)`: `Game.Move(dir)` now, then repeats |
| Q / E | `Game.Rotate(Int3.UnitZ, true / false)` |
| W / S | rotate about screen-right (tip away / towards) |
| A / D | rotate about screen-forward (tip left / right) |
| Space | `Game.HardDrop()` |
| C | `Game.Hold()` |
| Z / X | `_turnLeft += / -= π/2` (animated view turn) |
| V | `ToggleTopView()` |

- **Screen axes:** `Axes(out Int3 forward, out Int3 right)` (public, :221) snaps the camera direction to the grid.
- **At the start camera:** forward = (0,1,0), right = (1,0,0). So Up = +Y, Down = −Y, Right = +X, Left = −X; W = `Rotate(UnitX,false)`, S = `Rotate(UnitX,true)`; A = `Rotate(UnitY,false)`, D = `Rotate(UnitY,true)`.
- **Key repeat:** `Das = 0.17`, `Arr = 0.05` seconds, measured in session `Time` (:20, 199-218). `_moves[key] = (dir, Time + Das)`. `RepeatMoves()` in `TickGame` drops the entry if `!Held.Contains(key)`, otherwise moves and re-arms at `Time + Arr`. The direction is fixed while held.

**Injection, option A (recommended):** call the public `StackGame` methods on `session.Game` directly:
- `Move(Int3 delta)`, `Rotate(Int3 axis, bool positive)`, `HardDrop()`, `Hold()`, and the `SoftDrop` setter.
- No focus gates, no reflection on private members. The next frame's `NoteGameOver` handles a top-out.
- Naming trap: `game.Held` is the held `PieceDef`; the session's `Held` is the key set.

**Injection, option B (to exercise the real key path including repeat):**
```csharp
var held    = (HashSet<Keys>)tGameSession.GetField("Held", NP).GetValue(session);
var gameKey = tStackSession.GetMethod("GameKey", NP);
// press / hold:  held.Add(k);    gameKey.Invoke(session, new object[]{ k, true,  false });
// release:       held.Remove(k); gameKey.Invoke(session, new object[]{ k, false, false });
```
A hold only repeats while the key is in `Held`. Only call this while playing (not in countdown, pause or boss). Going through `OnKey` instead is subject to the focus, Ctrl/Alt and command gates.

**Mouse:**
- Left or right drag orbits: `public virtual void Orbit(int dx, int dy)` at 0.008 rad per pixel, elevation clamped to 8°–89° (:587).
- The gumball is driven through `GameMouse`, which calls the public `Gumball` API (Gumball.cs): `Hover(vp, Point)`, `Begin(vp, Point)`, `Drag(Point)`, `End()`. Pixels are viewport client coordinates (`vp.Size`).
- A quarter turn fires when the drag passes `TurnAt = 0.55` rad along the ring, then every further 90°.
- Public state the conduit draws from: `Hot`, `Dragging`, `GrabAngle`, `DragAngle`, `LastAxis`, `LastTurnTime` (all with private setters).
- A pixel on ring `a` at angle `t`:
  ```csharp
  var (c, r) = gb.Place(vp, vp.Size); var tilt = gb.Tilt(vp, c);
  var p = Gumball.On(c, a, t, r); p.Transform(tilt); p.Transform(session.GridToWorld);
  var px = vp.WorldToClient(p);
  ```
- **Deterministic puppeteering** (no hit test): set the private setters on `Gumball` by reflection.
  1. `Hot = a` for about 15 frames (the ring thickens from 3 to 5).
  2. `Dragging = a`, `GrabAngle = t0`, then ramp `DragAngle` from 0 to about 0.7 over about 25 frames. The progress arc is drawn at 1.12 r.
  3. When it crosses 0.55, call `game.Rotate(Gumball.Axes[a], true)` and set `LastAxis = a`, `LastTurnTime = session.Time` (the ring glows for 0.35 s).
  4. `gb.End()` and `Hot = -1`.
- The mouse cursor is not in captures; composite one in post at the projected point for `GrabAngle + DragAngle`.

## 4. STATE FOR A BOT

**Coordinates:** grid cells `Int3(X, Y, Z)`, x in [0,Nx), y in [0,Ny), z up from the floor. Cells with z ≥ Nz are free (above the rim). World position is `session.WellPoint(x, y, z)`, or `session.GridToWorld` (plane placement × voxel size).

**`StackGame`** (`session.Game`, all public getters):
- `Well` (see below), `State` (`Playing, Clearing, Paused, ToppedOut, TimeUp`), `IsOver`.
- `Current` (`PieceDef`; null during a clear and after the end), `Offsets` (`Int3[]` relative to the pivot), `Position` (pivot cell), `PieceCells`, `DropDistance()`.
- `Upcoming(int i)` (NEXT shows 0–2), `Held`, `HoldUsed`.
- `Score` (long), `Layers`, `Level`, `Combo`, `PiecesPlaced`, `Elapsed` (seconds played).
- `SoftDrop` (get/set), `LastClear` (`ClearEvent { Layers, Points, Combo, Perfect, Level, Time }`), `LevelUpTime`, `ClearingLayers`, `ClearProgress`, `GravityInterval`.
- Statics: `GravityFor(level) = max(0.15, 2.4·0.92^(level−1))` seconds per cell; `LockDelayFor(level) = max(0.6, 1.2 − 0.06·(level−1))`.
- Constants: `ClearDuration 0.3`, `SoftDropInterval 0.035`, `LayersPerLevel 5`, `MaxLockResets 15`.

**`Well`** (Well.cs): `Nx, Ny, Nz`, `Get(x,y,z)` / `Get(Int3)` (0 = empty), `IsFree(Int3)`, `Filled`, `LayerCount(z)`, `LayerSize`, `Height`, `FullLayers()`, `Count`, `Cells()`, `Version`. Mutators are public: `Set(Int3, int value)`, `RemoveLayers`, `Clear()`.

**Spawn** (StackGame.cs:354): `position = ((Nx−sx)/2, (Ny−sy)/2, Nz−sz) + pivot`, where the pivot is the cell nearest the bounding-box centre. In a 5×5×12 well:
- I4 occupies x = 0..3, y = 2, z = 11 with its pivot at (1,2,11). `Rotate(UnitY, ±)` stands it up in column (1,2).
- O4 occupies x, y = 1..2, z = 11.

**Pace:** level 1 gravity is 2.4 s per cell, so a natural fall takes 26 s. The bot must hard-drop, or soft-drop (0.035 s per cell, and the HUD shows "加速下落"). A piece landed by soft drop waits 1.2 s before it locks.

**Bot (plan on public state, act with public calls):**
1. When `State == Playing && Current != null`, enumerate orientations by breadth-first search from `Offsets` over the 6 quarter turns, using `Int3.Rotate(o, axis, positive)`. Key each orientation by its sorted offsets and keep the turn path (at most 24 orientations).
2. For each orientation and each pivot (px, py) with every cell inside the columns, drop from the current `Position.Z` while every cell is `Well.IsFree`. Skip the placement if any landed cell has z ≥ Nz, or if the stack reaches the spawn row.
3. Score each placement: `+1000·(layers where LayerCount(z) + piece cells in z == LayerSize) − 40·(empty cells directly under piece cells) − 3·Σz of piece cells + 1·(faces touching wall, floor or filled cells) − 0.5·(turns + |dx| + |dy|)`. Take the best.
4. Execute on a frame schedule with seeded jitter:
   - wait 14–22 frames after the spawn;
   - one `Rotate` every 8–12 frames;
   - re-read `Position` (kicks can shift it), then one `Move` every 5–6 frames, x first then y;
   - wait 8–14 frames, optionally `SoftDrop = true` for 10–15 frames, then `HardDrop()`;
   - wait until `State == Playing && Current != null` (about 19 frames after a clear).
5. That is about one piece per 1.2–1.6 s, so roughly 20–25 pieces (about 90 cells) in 30 s. In a 300-cell well with holes penalised it cannot top out in 30 s. As a guard, end the scene if `Well.Height ≥ Nz − 4`.
6. The core is Rhino-free, so plan the whole scene on a shadow `new StackGame(sameSettings)` first and replay the action list by game-frame index. Game frames are the frames where `Game.Tick` runs; countdown and boss frames do not count.

**Staging highlights (exact fields):**

- **Seed:** `settings.Seed`. It also drives the boss screen's context blocks and decoy. Cheap search: loop seeds, build `new StackGame(...)`, read `Current.Name` and `Upcoming(i).Name`.
- **Specific piece:**
  - simplest: `settings.Pieces = new[]{ PieceSets.I4 }`;
  - or insert into the bag: `StackGame._bag` (private readonly `Bag`) → `Bag._queue` (private `List<PieceDef>`), where index 0 is the next piece;
  - or replace the falling piece now with the private `StackGame.Spawn(PieceDef)`.
  - Presets are `PieceSets.I3, L3, I4, O4, L4, T4, S4, SpiralL, SpiralR, Tripod, Cross, U5, V5, W5, Tower`, and `PieceSets.Get(0|1|2)`.
  - Levels 1–2 deal only flat presets plus all custom units (`PoolFor`, :121).
- **Layer clears on demand:** pre-fill with `game.Well.Set(new Int3(x,y,z), 1)`. Any non-zero value works, because the locked stack is coloured by storey (`LayerColours[z % 8]`), not by the stored value.
  - Single: fill layer 0 except x = 0..3, y = 2; an I4 with Space from spawn clears it.
  - Four at once ("MEGA FLOOR", 1500 × level): fill layers 0–3 except column (1,2); I4, `Rotate(UnitY, true)`, Space.
  - "DOUBLE FLOOR": a 2×1×2 hole and an O4 stood up.
  - Leave a few cells above the cleared layers. Otherwise `Perfect` is true, the title becomes "SITE CLEARED" and 3000 × level is added.
  - Titles: 1 "FLOOR CLEARED", 2 "DOUBLE FLOOR", 3 "TRIPLE FLOOR", 4+ "MEGA FLOOR".
- **Combo:** consecutive clearing locks. HUD status "连消 ×N" shows at `Combo ≥ 2`. Stage three one-slot layers with I4-only pieces and three Spaces about 0.6 s apart.
- **Hard drop:** instant teleport to the ghost plus lock, +2 per cell. There is no fall animation. The translucent ghost and dotted drop lines are visible before it.
- **Level and chapter palette:** `Palette.Band(level)` (Palette.cs:39):

  | Level | Band | Chapter |
  |---|---|---|
  | 1–3 | 0 | 亭子 Pavilion |
  | 4–6 | 1 | 多层 Low-rise |
  | 7–9 | 2 | 小高层 Mid-rise |
  | 10–12 | 3 | 高层 High-rise |
  | 13+ | 4 | 超高层 Supertall |

  - Level is recomputed only on a clearing lock: `Level = max(1, Settings.StartLevel) + Layers / 5` (:326).
  - Natural trigger: set `game.Settings.StartLevel = 4` (a public mutable field) just before a clearing drop. This gives the level-up card, the 1.6 s blend and the new pool.
  - Instant trigger: set the private setters `Level = 4` and `LevelUpTime = game.Elapsed`, and also `Settings.StartLevel`, or the next clear resets the level.
  - Starting with `StartLevel = 4` opens directly in chapter 2 with no blend.
  - Snap without a blend: set `StackConduit._blendStart = double.NegativeInfinity`.
  - Higher levels fall faster (level 13 is 0.88 s per cell).
- **Custom piece:**
  ```csharp
  new PieceDef("Gate 门楼", Polycube.ParsePattern("#.#/#.#|###"),
               Color.FromArgb(76,201,240).ToArgb(), 1.0, true)
  ```
  - Pass all five arguments when using reflection. This is the unit from `tests\gh\MoyuStack-Test.gh` (7 voxels, 3×2×2).
  - Check it with `PieceDef.Validate(cells, nx, ny, nz)`, which returns null when it is fine.
  - Custom units are dealt from level 1 and score ×2.1 here.
  - Piece names are never drawn in the HUD. The "user-modelled" story needs the GH / Rhino sketch shot.
  - In a 5×5×12 well it spawns with legs at (1,1), (3,1), (1,2), (3,2) on z = 10 and the lintel at x = 1..3, y = 1, z = 11. Fill layer 0 except those four leg cells and one Space clears a floor.
- **Other numbers:** `Score`, `Layers`, `Combo` on `StackGame` and `Best`, `NewBest` on `StackSession` have private setters (use `GetProperty` on the declaring type).
- **Fresh seeded game in the same session:** mutate `session.Settings` (keep Nx, Ny, Nz), then set `Game` (private setter on `StackSession`) to `new StackGame(session.Settings)`, set `Disguise = null` and `_disguiseVersion = -1`. A new game starts in `Playing` with no countdown.

## 5. CAMERA & PORTRAIT LAYOUT

**`SetStartCamera`** (StackSession.cs:79-88) does not depend on viewport size or aspect:
```
target = WellPoint(Nx/2, Ny/2, Nz*0.42)
k      = Nz/12 * VoxelSize * max(1, max(Nx,Ny)/5)
camera = target + XAxis*14k − YAxis*17k + ZAxis*17.5k ;  CameraUp = Plane.ZAxis ;  Camera35mmLensLength = 28
```
It is re-applied by Home, `Restart()` and the parallel-projection guard, so re-apply any tweak after those. `ExitBoss` restores the camera saved at `EnterBoss`, including your tweak.

**Portrait framing [verify].** Rhino's 35 mm lens length applies to the shorter frustum side. In 9:16 the horizontal half-angle is therefore atan(12/28) = 23.2°, and the vertical half-tangent is 0.762.
- Projecting the 5×5×12 well corners at the start camera gives about 372 × 633 px in a 1080×1920 frame: x ≈ 352–724, y ≈ 598–1231.
- That is only about a third of each dimension, and the silhouette is almost exactly 9:16.
- Size scales linearly with lens length. A 40–42 mm lens gives about 540 × 920 px. A 50 mm lens gives about 665 × 1130 px, the same relative height as the landscape default, but it then overlaps the NEXT and HOLD previews.
- Set it after `Begin` with `view.ActiveViewport.Camera35mmLensLength = 40;` and optionally shift the target a little.

**HUD scale** (GameConduit.cs:34-36): every HUD length is `HUD units × k`, with
```csharp
k = UiScale(d.DpiScale, d.FrameSize) = Math.Max(1, Math.Min(dpiScale, frame.Height / 720.0))
```
- The layout uses `e.Display.FrameSize`, so it follows the capture size. Text size follows the monitor DPI scale, not the frame size, with a cap at height/720.
- This machine: the primary display is 3840×2160 with AppliedDPI 168 (175 %). So **k = 1.75** for a 1080×1920 frame **[verify that `DpiScale` is 1.75 inside `CaptureToBitmap`]**.
- A 540×960 on-screen view would get k = 1.33. The second monitor (1920×1080) would give k = 1.

Pixel sizes at k = 1.75 in a 1080×1920 frame:

| Element | HUD units | Pixels |
|---|---|---|
| "VOXSTACK" label | 12 | 21 |
| Score digits | 68 | 119 |
| Figure labels / values (LAYERS, PIECES, BEST) | 11 / 28 | 19 / 49 |
| "LEVEL 01" / chapter name | 12 / 22 | 21 / 38 |
| NEXT, HOLD labels | 11 | 19 |
| Clear pop-up title / points | 14 / 44 | 24 / 77 |
| Level card label / name | 14 / 34 | 24 / 60 |
| Countdown digit / ring radius | 140→116 / 120 | 245→203 / 210 |
| Key caps / hint text | 24 / 14 | 42 / 24 |
| Boss metrics card | 300 × 244 | 525 × 427 |

Positions at k = 1.75:
- Score block top-left from (84, 77); the figures row is at y ≈ 262–340.
- Level block right-aligned at x = 989, y = 77–182.
- NEXT minis at x = 870, y = 376 / 534 / 665. All three show, because the limit is height-based.
- HOLD at (245, 752).
- Gumball centre ≈ (833, 1644), ring radius ≈ 113 px (Gumball.cs:88-93). Its how-to text sits to the left.
- Key hints are centred on y = H − 40k and wrap at `maxW ≈ 992 px`. The 11 hints take about 3 rows in portrait and stack upward, overlapping the gumball's bottom during countdowns and the first 8.5 s of play. Set `PlayTime ≥ 8.5` (private setter) to drop them during play.
- Estimated clearances: a 40 mm lens still clears NEXT and HOLD at k = 1.75; at k = 2, keep the lens at about 31 mm or less.
- For a bigger HUD, the options are a higher Windows scale, or capturing at 720×1280 and upscaling. Above about k = 2.5, a 5-digit score starts to collide with the level block.

**Language:** `session.Language` is a public get/set of `Moyu.Games.Common.Core.UiLanguage { Chinese, English }`.
- With a null owner it starts as `GameSession.SystemLanguage()` (the Windows display language).
- Force it with:
  ```csharp
  tGameSession.GetProperty("Language").SetValue(session, Enum.Parse(tUiLanguage, "Chinese"));
  ```
- Chinese changes the hints, countdown captions, pause and result text, "连消", "加速下落", the gumball how-to, and shows bilingual chapter names ("亭子 Pavilion").
- These stay English in both: VOXSTACK, LAYERS, PIECES, BEST, LEVEL, NEXT, HOLD · C, FLOOR CLEARED, PAUSED, NEW BEST.
- The boss screen is always English.

## 6. BOSS KEY

**API** (GameSession.cs:441-485):
- `public void ToggleBoss()`
- `public void EnterBoss()` — the focus-loss variant; it also calls `_hook.Reset()`
- private `EnterBoss(bool keysLost)`
- `public void ExitBoss()`

`GetMethod("EnterBoss")` with NonPublic flags is ambiguous; use `ToggleBoss`.

**Enter:**
- `_pausedByBoss = Countdown > 0 || IsGameActive`.
- The game is paused, `Countdown = 0`, `ClearInput()`, `Held.Clear()`.
- `_gameCamera = new ViewportInfo(vp)` saves the game camera.
- `EnterDisguise(vp)` runs, then `Boss = true`, `BossTime = 0`, redraw, `RhinoApp.WriteLine(line)`, `Changed()`.

**`EnterDisguise`** (StackSession.cs:278):
- It (re)builds `Disguise = new MassingDisguise(well cells, Nx, Ny, Nz, Plane, VoxelSize, Settings.Seed)` when the well version changed.
- It sets the camera from `Disguise.CameraFor` (two-point aerial: offset (5, −4.55, 3.8)·f from the target, with f = max(Nx, Ny, 0.8·top)), `CameraUp = Z`, and lens 50.
- Command line, one line: `MassingStudy: Option B updated · {Floors}F · GFA {Gfa:N0} m² · FAR {Far:F2}`.

**What is drawn** (conduit only):
- Light-grey gradient background.
- Ground, roads, site, flat sun shadows, a red dashed site boundary.
- Grey context blocks, and the white mass with storey lines and dark crease edges (`MassingDisguise.Draw`, :258).
- Metrics card top-left: "Massing Study · Option B", Site area, GFA, FAR, Floors, Coverage, Height, and "Optimizing… iter N / 100 fitness 0.xxx" with a bar. The iteration is `(23 + (int)(BossTime/1.2)) % 100 + 1`, so it starts at 24.
- North arrow bottom-right.

**What it changes:**
- Only this viewport's camera and lens. Nothing is added to the document, no layers, no display-mode change.
- With fewer than 12 locked voxels (`IsDecoy`) it shows a fake stepped massing instead of the real stack. Have at least 12 voxels locked for the "same voxels" effect.
- The mass uses lit `DrawMeshShaded` materials, so use the Shaded display mode in the floating view **[verify the look]**.

**Grasshopper side** (only with the component as owner): `OnSessionChanged` sets State "Option B", Message "Option B · FAR x.xx", redraws the canvas and schedules a solution. With a null `changed` nothing happens.

**Exit:** `Boss = false`, the saved game camera is restored (`SetViewProjection(_gameCamera, true)`), and if it was paused by boss and the game is not over, `StartCountdown(false)` gives 3 s with the caption "回到游戏 / Back to the game", a light wash and the key hints. The game resumes when the countdown ends.

**Video timing at 60 fps:**

| Frames | Call | On screen |
|---|---|---|
| …N−1 | step | gameplay |
| N | `session.ToggleBoss()` before the step and capture | boss screen in that same frame (the sky repaint is forced by the boss flag) |
| N…N+89 (1.5 s) | step | boss screen; the counter ticks 24→25 at 1.2 s |
| N+90 | `session.ToggleBoss()` | game camera back, countdown "3" |
| +180 frames | step | 3-2-1, then play |

To shorten the countdown, step it with `2·dt`, or set `Countdown` (private setter) to 1.0 right after exit.

## 7. RISKS

| Risk | Workaround |
|---|---|
| Real-time `GameClock` runs between script calls and advances the game | Stop `_timer` in the same call as `Start`. |
| Auto-boss when Rhino is not the foreground app (Tick step 3) | `session.AutoBoss = false` immediately. |
| `Tick` freezes the countdown or shows PAUSED when `OwnsFocus()` is false or `Command.InCommand()` is true (likely if Rhino is in the background, or if `run_csharp` runs inside a command) **[verify both]** | Test `KeyboardHook.FocusRoot() == RhinoApp.MainWindowHandle()` and `Command.InCommand()` from the script. If either fails, use the manual `Step` from section 2. Check `session.IsPaused` each frame. |
| Long blocking loop (1800 frames of 1080×1920 capture plus save) may time out the MCP call | Chunk it (120–300 frames per call, state in `AppDomain.CurrentDomain.SetData`), or step one frame per `RhinoApp.Idle` event and poll. |
| Wrong thread | `Start` must run on the UI thread; use `RhinoApp.InvokeOnUiThread` if `RhinoApp.InvokeRequired`. |
| Real keys typed into Rhino reach the game while it has focus | `((IDisposable)tGameSession.GetField("_hook", NP).GetValue(session)).Dispose();` — idempotent, and later `Reset()` and `Stop()` are still safe. |
| Real mouse over the view lights gumball rings or orbits the camera | `((Rhino.UI.MouseCallback)tGameSession.GetField("_mouse", NP).GetValue(session)).Enabled = false;` |
| Capture size ≠ on-screen viewport size. The HUD uses `FrameSize`, but the gumball, NEXT/HOLD minis, gumball labels and the clear pop-up use `e.Viewport.ClientToWorld` / `WorldToClient`, and the repo's notes say `Viewport.Size` stays the on-screen size in a capture **[verify]** | Make the floating view's client area exactly 1080×1920 physical px on the 4K primary (it fits: about 1990 px with the caption against about 2076 usable). Check `view.ActiveViewport.Size` and resize the frame until it matches, then capture at that exact size. |
| HUD scale depends on monitor DPI | Keep the view on the primary monitor (k = 1.75). Measure one frame. |
| A new floating view defaults to another display mode; document objects and Grasshopper previews also draw in the game view | Use an empty document, set `view.ActiveViewport.DisplayMode = DisplayModeDescription.FindByName("Shaded")`, and turn GH preview off. |
| `View.Maximized = true` in `Begin` on a floating view **[verify harmless]** | If it misbehaves, start in a docked view and capture portrait from it (subject to the size risk above). |
| Game over writes the bot's score to `%APPDATA%\Moyu\voxstack-scores.txt` and may show "NEW BEST" | Never let a scene top out; end with `session.Stop()`, which does not record. Otherwise back up the file. The HUD "BEST" shows the user's real best; override `Best` if wanted. |
| `Restart()` reseeds from `TickCount` and resets the camera and lens | Do not restart; swap `Game` or start a new session. |
| Static `GameSession.Current`: starting stops any running game, and a Run click on any game component stops yours | Do not touch GH during the capture; check `session.Stopped` each frame. |
| An exception inside `Tick` stops the session silently (one command-line line) | Check `session.Stopped`; read the command history. |
| Sky repaint throttle uses real time during palette blends | Reset `_backgroundKey` per frame during the blend (section 2). |
| Hint rows overlap the gumball in portrait during countdowns | Cut the start countdown from the footage; set `PlayTime ≥ 8.5`. |
| IME switching | It only happens on real swallowed IME keys; none occur with direct injection. `Stop()` restores it. |
| Null owner | Safe: `Owner` and `_changed` are never dereferenced without a guard in `GameSession`, `StackSession`, `StackConduit` or `Gumball`. |