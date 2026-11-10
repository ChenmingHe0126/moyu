# Moyu topology optimisation: offline-capture cheat sheet (2D pixel / 3D voxel)

Nothing here was run in Rhino: Moyu facts come from the source with line references, Grasshopper member names from the installed Rhino 8.36 `Grasshopper.dll` / `Grasshopper.xml` (reflection-only), and Grasshopper preview behaviour is flagged where it is an assumption.

## 1. Ready-made cases

`packaging/examples/Topo2D.gh` is written by `GhFile.Run` and `Topo3D.gh` by `GhFile3D.Run` (`tests/Moyu.RhinoTests/Program.cs:51`). The same files are embedded in `Moyu.gha` as resources `Moyu.Examples.Topo2D.gh` / `Moyu.Examples.Topo3D.gh`. An installed copy is at `%APPDATA%\McNeel\Rhinoceros\packages\8.0\moyu\1.0.1\examples\`.

Each file holds three groups stacked top to bottom on the canvas (A, B, C by `Attributes.Pivot.Y`). Every group has its own solver, Visualize, `GH_BooleanToggle` "Run" (saved false), `GH_ButtonObject` "Reset" and `GH_Timer` "Trigger" (Interval 50 ms) (`GhBuilder.cs:79-93`).

| Case (generator) | Geometry in world | Supports / loads | Grid, settings | Converges | Time per step |
|---|---|---|---|---|---|
| 2D A Cantilever (`GhFile.cs:62-99`) | XY rectangle 60×30 at origin | Left edge pinned; point load at (60,15), force (0,−1,0) | 90×45, V 0.4, R 2, Mode 0 | 71 iterations | 11 ms |
| 2D B Bridge (`GhFile.cs:102-191`) | XZ plane at y=−60, 100×40 | Top-edge line load (0,0,−10); pin at bottom-left, roller at bottom-right; solid deck z 37–40; void circle r 9 at (50,−60,14); E 210000 | 120×48, V 0.3, R 2.5, Mode 0 | 73 iterations (`docs/images/bridge_history.csv`, same set-up) | about 20 ms (estimate) |
| 2D C Bracket (`GhFile.cs:194-229`) | XY L outline x 120–220, y 0–100, bolt hole r 6 at (140,75) | Line (120,100)–(160,100) pinned; point load (220,45), force (0,−5,0) | 80×80, V 0.35, Mode 2 | not recorded | about 50–60 ms (estimate) |
| 3D A Cantilever (`GhFile3D.cs:68-118`) | Box 40×20×20 at origin | Face x=0 fixed (17×17 nodes); line load on the bottom edge of the free end, (0,0,−1) | 32×16×16, V 0.2, R 1.5, Mode 1 | 91 iterations | about 0.1 s incl. display (core 88 ms) |
| 3D B Canopy (`GhFile3D.cs:121-216`) | Box x 60–100, y 0–40, z 0–16 | Roof surface load (0,0,−1); four pinned corner points; solid slab z 14.5–16; void passage x 73–87, z 0–7 | 32×32×16, V 0.2, Mode 1 | 35 iterations | about 150 ms (estimate) |
| 3D C Bracket (`GhFile3D.cs:219-284`) | L plate extruded 12, capped bolt-hole cylinder | Face y=100 (x 120–160) fixed; point load (220,45,6), force (0,−5,0) | 40×40×8 (about 6000 voxels), V 0.3, Mode 1 | 42 iterations | about 100 ms (estimate) |

Measured numbers are from `docs/2d-workflow.md:11,114-123` and `docs/3d-workflow.md:65-78`.

**Most striking:**
- **3D cantilever** is the best orbit subject: a branching hollow truss (`docs/images/c3d_final.png`).
- **3D canopy** reads instantly as a table with four legs and converges fastest; the solid slab is visible from iteration 0 and the legs grow under it (`canopy_result.png`).
- **2D bridge** is the best 2D shot: an arch with a deck and a row of red load arrows (`bridge_result.png`). It is 2.5:1, so it only fills a band of a 9:16 frame.
- **3D bracket** is a flat plate and the least dramatic.
- **Live edit** is a possible highlight: moving the load mid-run keeps the densities and the structure morphs (`Scenarios.cs:167-177`, `Scenarios3D.cs:128-133`).

## 2. Route A: through Grasshopper

### Component layout

Solver base class: `Moyu.Components.SolverComponentBase<TDomain,TProblem>` (public abstract, `SolverComponentBase.cs`).

- **Solver inputs** (`:64-83`): 0 Domain, 1 Loads, 2 Supports, 3 Areas, 4 Material, 5 Volume Fraction, 6 Penalty, 7 Filter Radius, 8 Max Iterations, 9 Steps, 10 Run, 11 Reset.
- **Solver outputs** (`:85-94`): 0 Result, 1 Iteration (int), 2 Compliance, 3 Volume, 4 Converged (bool), 5 History.
- **Defaults:** 2D V 0.4, R 2, MaxIter 200, Steps 5 (`SolverComponent.cs:22-25`); 3D V 0.2, R 1.5, MaxIter 150, Steps 2, objective tolerance 5e-4 (`Solver3DComponent.cs:22-27`).
- **Wiring in the examples:** only inputs 0–4 (as needed), 5 (Volume slider), 10 (toggle) and 11 (button) have sources. Steps (9) is persistent data, so it can be set directly.

| Component | GUID |
|---|---|
| Solver 2D | `0ccc0783-dd2c-45aa-ade8-96778cbd4815` |
| Visualize 2D | `2a76e4f3-1e42-46e8-94e3-d00c400409f7` |
| Boundary 2D | `bea6c6f3-581f-47c7-859b-ffadd46d11b1` |
| Solver 3D | `ebbb8167-2279-4eaf-a62d-75e2226fb21b` |
| Visualize 3D | `b3991b95-9c98-460d-8cbc-456b09dd6122` |
| Boundary 3D | `c53783f5-76d5-4b4d-a59b-663ac770091e` |

The other GUIDs are in `Canvas.cs:15-29`.

### Opening the file

```csharp
var gh = (Grasshopper.Plugin.GH_RhinoScriptInterface)Rhino.RhinoApp.GetPlugInObject("Grasshopper");
gh.DisableBanner(); gh.LoadEditor();          // editor loaded; it need not be shown
gh.OpenDocument(path);                        // "editor will be loaded if necessary, but not shown"
var doc = Grasshopper.Instances.ActiveCanvas.Document;
```

Alternatives:
- `Moyu.Components.Examples.ExampleFiles.Load(Example)` (public static, `ExampleFiles.cs:53-70`) returns an unnamed `GH_Document`. `ExampleFiles.All[0]` is Topo2D and `[1]` is Topo3D.
- `ExampleFiles.Open(Example, out string error)` (`:73-95`) also adds it to `Instances.DocumentServer`, makes it the canvas document, sets `Enabled = true` and runs `NewSolution(false)`. It needs `Instances.ActiveCanvas != null`.

### How the solver advances (`SolveInstance`, `:96-212`)

- **New or reused optimizer** (`:157-160`): a new one is created on Reset, on first run, or when the grid size changed (`Fits`). Otherwise `_optimizer.Update(problem, settings)` keeps the densities.
- **Step loop** (`:164-170`): `while (run && !reset && done < steps && !opt.IsFinished) opt.Step();`. The 1500 ms budget and the Esc check only apply after the first step, so a solution always does at least one.
- **Timer:** it only expires the solver and schedules a new solution. `Canvas.Tick` (`Canvas.cs:81-85`) is the same thing: `target.ExpireSolution(false); doc.NewSolution(false);`.

### One iteration per call, without the Timer

```csharp
foreach (var t in doc.Objects.OfType<GH_Timer>().ToList()) doc.RemoveObject(t, false);   // or t.Locked = true
var solver = doc.Objects.OfType<GH_Component>().Where(c => c.ComponentGuid == solverId)
               .OrderBy(c => c.Attributes.Pivot.Y).ElementAt(caseIndex);                 // 0=A, 1=B, 2=C
dynamic steps = solver.Params.Input[9];
steps.PersistentData.Clear(); steps.PersistentData.Append(new GH_Integer(1));
solver.Params.Input[9].ExpireSolution(false);
var run = (GH_BooleanToggle)solver.Params.Input[10].Sources[0];
run.Value = true; run.ExpireSolution(false);
doc.NewSolution(false);                                  // iteration 1
// every further iteration:
solver.ExpireSolution(false); doc.NewSolution(false);
```

- **Reading state:**
  - Iteration: `((GH_Integer)solver.Params.Output[1].VolatileData.AllData(true).First()).Value`
  - Converged: output 4 as `GH_Boolean`.
  - `solver.Message` (`:200-204`) is one of `Ready`, `Running @ n`, `Paused @ n`, `Converged @ n`, `Max iter n`, `Unstable`, `Error`, `Waiting`. Stop on `Converged` or `Max iter`.
  - Result object: `solver.Params.Output[0].VolatileData.AllData(true).First().ScriptVariable()` gives a `Result2D` / `Result3D`.
- **Reset:**
  - Button: `((GH_ButtonObject)solver.Params.Input[11].Sources[0]).ButtonDown = true`, expire, solve, then set it back to false.
  - Or null the private field: `solver.GetType().BaseType.GetField("_optimizer", BindingFlags.Instance | BindingFlags.NonPublic)`. It is declared on the generic base (`:30`), so use `BaseType`.
  - A freshly opened file is already at iteration 0.
- **What restarts or resets:**
  - Steps is not part of `SimpSettings`, so changing it resets nothing.
  - Changing V, P, R or N bumps the revision, restarts the iteration budget and clears Converged (`SimpOptimizer.cs:120-128`).
  - Changing the Resolution slider creates a new optimizer.
  - Sliders are the sources of the inputs, e.g. `((GH_NumberSlider)bnd.Params.Input[1].Sources[0]).SetSliderValue(160m)`. Resolution slider max is 300 (2D) and 96 (3D).

### What is drawn in the viewport

- **Visualize 2D** (`VisualizeComponent.cs`)
  - Inputs (`:38-46`): 0 Result, 1 Mode (0 Pixels, 1 Solid, 2 Smooth), 2 Threshold 0.5, 3 Solid Colour (25,25,25), 4 Void Colour (240,240,240), 5 Conditions.
  - Outputs: 0 Mesh, 1 Contour, 2 Density.
  - `DrawViewportMeshes` (`:127-130`): `args.Display.DrawMeshFalseColors(_mesh)`.
  - `DrawViewportWires` (`:132-147`): domain outlines (150 grey, width 1), contours (0,122,204) width 2, red load arrows (220,50,47), blue supports (38,139,210).
  - Contours are always drawn; there is no input to turn them off.
  - At iteration 0, Mode 0 is a uniform grey block.
- **Visualize 3D** (`Visualize3DComponent.cs`)
  - Inputs (`:36-43`): 0 Result, 1 Mode (0 Voxels, 1 Smooth), 2 Threshold 0.5, 3 Colour (70,110,190), 4 Conditions.
  - Outputs: 0 Mesh (current mode), 1 Surface (always the smooth iso-surface), 2 Density.
  - `DrawViewportMeshes` (`:134-138`): `DrawMeshShaded(_mesh, _material)`.
  - Wires (`:140-157`): domain box, up to 32 load arrows, up to 400 support glyphs.
  - Both meshes are rebuilt on every revision (`:104-105`).
- **Condition components** draw themselves through `MoyuComponent.DrawViewportWires` (`MoyuComponent.cs:60-73`). Only the native components were saved with `Hidden = true` (`GhBuilder.cs:68`).
  - Boundary 2D draws the pixel-grid wires.
  - Boundary 3D draws the wire cage of the voxel shell (`VoxelDomain3D.Draw`, `:102-105`).
  - Loads and supports are therefore drawn twice (component plus Visualize).

### What to preview

- Keep only the chosen group's Visualize visible, optionally its Boundary for the first frames.
- Set `Hidden = true` on every other Moyu component, including the other two groups.
- Call `doc.DeselectAll()`: selected components draw in the selection colour.
- `doc.PreviewMode` must be `GH_PreviewMode.Shaded`.
- Assumption: Grasshopper only calls `DrawViewportMeshes` in a shaded Rhino display mode, and only an `Enabled` document previews.

Then capture with `view.CaptureToBitmap(new Size(1080,1920))`. No redraw is needed in between.

## 3. Route B: Core directly

Everything needed is `public` in assembly `Moyu`, so `Activator` / `MethodInfo.Invoke` / `dynamic` work without non-public binding.

### Core (`Moyu.Core`, no Rhino dependency)

- **`Grid2D(int nx, int ny)`**
  - `Element(i,j) = j*Nx + i` (`Grid2D.cs:28`).
  - `Node(i,j)` is column-major when `ny <= nx` (`:30`), so always call it.
  - DOF = `2*node + {0,1}`.
- **`Grid3D(int nx, int ny, int nz)`**
  - `Element(i,j,k) = (k*Ny + j)*Nx + i` (`Grid3D.cs:46`).
  - `Node(i,j,k)` (`:48`); DOF = `3*node + {0,1,2}`.
- **`Problem2D(Grid2D)` / `Problem3D(Grid3D)`**, both deriving from `ProblemBase` (`Simp.cs:24-43`)
  - Public fields: `ElementKind[] Kinds`, `double[] Force`, `bool[] Fixed`, `YoungsModulus`, `PoissonRatio`.
  - `ElementKind : byte` is Design 0, Solid 1, Void 2, Outside 3.
- **`SimpSettings`** (`Simp.cs:45-66`), public fields:
  - `VolumeFraction` 0.4, `Penalty` 3, `FilterRadius` 2, `Filter` (Density), `MoveLimit` 0.2
  - `Tolerance` 0.01, `ObjectiveTolerance` 1e-4, `MaxIterations` 200
  - For 3D, set `ObjectiveTolerance = 5e-4` and `MaxIterations = 150` to match the component.
- **`SimpOptimizer2D(Problem2D, SimpSettings)`** (`SimpOptimizer2D.cs:18`) and **`SimpOptimizer3D(Problem3D, SimpSettings)`** (`SimpOptimizer3D.cs:26`), both public sealed. Base `SimpOptimizer` (`SimpOptimizer.cs`):
  - `public void Step()` (`:132-171`): one FE analysis plus one OC update. Throws `InvalidOperationException` for a mechanism.
  - `public void Update(ProblemBase, SimpSettings)` (`:82`).
  - Public readonly fields `double[] X`, `double[] XPhys` (the densities to draw, in element order), `List<double> History`.
  - Properties `Iteration`, `Compliance`, `Change`, `Volume`, `Converged`, `Revision`, `ReachedMaxIterations`, `IsFinished`.
  - Initial density is `VolumeFraction` on Design cells.
  - Hand-built example problems are in `Tests3D.cs:172-183` and `Program.cs:285-291`.

### Rhino side (`Moyu.TwoD`, `Moyu.ThreeD`)

- **Domains** (private constructors, use the factories):
  - `static PixelDomain2D Create(IList<Curve> curves, int resolution, Plane? plane, out string error)` (`PixelDomain2D.cs:109`).
  - `static VoxelDomain3D Create(IList<Mesh> solids, int resolution, Plane? plane, out string error)` (`VoxelDomain3D.cs:114`). Voxel counts are rounded up to a multiple of 4, or 8 when resolution > 64.
  - Members: `Plane`, `PixelSize` / `VoxelSize`, `Grid`, `Inside`, `ToWorld(gx,gy[,gz])` (corner of the cell), `Bounds`.
  - `PixelDomain2D.PixelMesh`; `VoxelDomain3D.Shell` (outer faces of the block, useful as the iteration-0 grey block).
- **Conditions:**
  - `Anchor2D(Point3d | Curve)`, `Load2D(Anchor2D, Vector3d)`, `Support2D(Anchor2D, bool fixX, bool fixY)`, `Area2D(Curve, ElementKind)`, `Material2D.Default`.
  - `Anchor3D(Point3d | Curve | Mesh)`, `Load3D(Anchor3D, Vector3d)`, `Support3D(Anchor3D, bool, bool, bool)`, `Area3D(Mesh, ElementKind)`, `Material3D.Default`.
  - `Moyu.Components.ThreeD.Inputs3D` is internal; mesh breps yourself with `Mesh.CreateFromBrep`.
- **Problem builders:**
  - `ProblemBuilder2D.Build(PixelDomain2D, IList<Load2D>, IList<Support2D>, IList<Area2D>, Material2D, out string error)` (`ProblemBuilder2D.cs:35`).
  - `ProblemBuilder3D.Build(...)` with the 3D types (`ProblemBuilder3D.cs:33`).
  - Both return a nested `Output` with public fields `Problem`, `Loads`, `Supports`, `Warnings`.
  - The lists must be typed: `typeof(List<>).MakeGenericType(loadType)`.
- **Results:** `Result2D` / `Result3D` are plain classes with public fields. Set `Domain`, `Kinds = problem.Kinds`, `Density = opt.XPhys`.
- **2D meshing:**
  - `ResultMesher.BuildMesh(Result2D, DisplayMode mode, double threshold, Color solid, Color empty)` (`ResultMesher.cs:22`) returns a vertex-coloured quad mesh. Draw it with `DrawMeshFalseColors`.
  - `ResultMesher.BuildContours(Result2D, double threshold)` returns `List<Curve>`.
  - Pass the enum as `Enum.ToObject(asm.GetType("Moyu.TwoD.DisplayMode"), 0)`.
- **3D meshing** (`ResultMesher3D.cs`):
  - `BuildVoxels(Result3D, double threshold)` (`:55`).
  - `VoxelFaces(VoxelDomain3D, Func<int,bool> filled)` (`:31`) for any predicate.
  - `BuildSurface(Result3D, double threshold, int smoothing = 2)` (`:59`): surface nets plus Taubin smoothing, closed, shared vertices. Pass all three arguments through `Invoke`.
  - Lower level: `SurfaceNets.VoxelField / Extract / Smooth` (`SurfaceNets.cs:17,29,96`).
- **Glyphs:** `Moyu.TwoD.Glyphs.Arrow(DisplayPipeline, RhinoViewport, Point3d tip, Vector3d force, Color)` and `Glyphs.Support(...)` are public static, and each condition object has `Draw(DisplayPipeline, RhinoViewport, Color?)`.

### Script skeleton (untested)

```csharp
var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Moyu");
Type T(string n) => asm.GetType(n, true);
var a = new object[] { new List<Mesh> { boxMesh }, 48, null, null };
object domain = T("Moyu.ThreeD.VoxelDomain3D").GetMethod("Create").Invoke(null, a);   // a[3] = error
// build typed lists of Load3D / Support3D / Area3D, then:
var b = new object[] { domain, loads, supports, areas, material, null };
dynamic built = T("Moyu.ThreeD.ProblemBuilder3D").GetMethod("Build").Invoke(null, b);
dynamic s = Activator.CreateInstance(T("Moyu.Core.SimpSettings"));
s.VolumeFraction = 0.2; s.FilterRadius = 1.5; s.MaxIterations = 150; s.ObjectiveTolerance = 5e-4;
dynamic opt = Activator.CreateInstance(T("Moyu.Core.SimpOptimizer3D"), new object[] { (object)built.Problem, (object)s });
// per iteration: opt.Step(); fill a Result3D; mesh = BuildSurface/BuildVoxels; conduit draws it; capture frames
```

## 4. Recommendation

**Route B is more robust for offline capture.**

- **No Timer or scheduled solutions:** `Step()` is synchronous, exactly one iteration.
- **No canvas state:** no stray previews from the other two groups, no selection colours, no dependence on the Grasshopper window or preview mode.
- **Your own drawing:** line widths and glyph sizes can be set for 1080×1920, and you can add effects the components cannot do (threshold ramp, density interpolation between iterations, the `Shell` block at frame 0).
- **Survives chunked calls:** keep the optimizer in `AppDomain.CurrentDomain.SetData`.

Use Route A only if the authentic Grasshopper preview (blue contours, glyphs) or the canvas itself must be on screen.

**Expected time per iteration** (repo benchmarks, Release, this project's dev machine):

| Grid | ms per step |
|---|---|
| 2D 160×80 | 78 |
| 2D 200×100 | 178 (about 5 px per cell at 1080 wide) |
| 2D 250×125 | 446 |
| 3D 32×16×16 | 88 |
| 3D 48×24×24 | 155 |
| 3D 64×32×32 | 346 |

- **Suggested resolutions:** 2D 160–200 on the long side, 3D 48–64. That gives roughly 70–100 iterations, so 10–40 s of solving per shot plus meshing and capture.
- **Portrait framing:** 2D cost follows the shorter grid side, so a 100×180 portrait domain costs about the same as 180×100.
- **Filter radius:** it is in cells, so scale it with resolution to keep the same topology (2D 2 at 90 becomes 4 at 180; 3D 1.5 at 32 becomes 3 at 64), or keep it for finer members.
- **Pacing:** most visible change happens in the first 30–40 iterations. Render several orbit frames per iteration.

**Beauty shot:** after convergence call `ResultMesher3D.BuildSurface(result, 0.5, smoothing)` with smoothing 3–4 (default 2). It already has shared vertices and computed normals, so draw it with `DrawMeshShaded` and a `DisplayMaterial`. In Route A it is Visualize 3D output 1, or set Mode = 1. For a finer final, re-solve once at 64 or 96 off camera (96×48×48 is roughly 1.2 s per step by cubic scaling).

## 5. Risks and gotchas

- **Three cases per file**, all previewing at different world positions (2D B is in the XZ plane at y=−60, so use a Front-type camera). Hide or lock the other groups.
- **Timers are saved in the file** and tick every 50 ms once the document is enabled. With Run false they only re-solve. They need the message loop, so they fire between script calls and on `RhinoApp.Wait` / `DoEvents`. Remove or lock them before setting Run = true. `GH_Timer.GlobalTimerBlock` is read-only.
- **3D at iteration 0 draws no mesh.** The uniform density (0.2–0.3) is below the 0.5 threshold and the move limit is 0.2, so the first voxels appear around iteration 2–3; only Solid areas show from the start. For a block-to-structure look, draw `domain.Shell` first or ramp the threshold from just under V up to 0.5 (clamped to 0.01–0.99, `Visualize3DComponent.cs:99`).
- **Support clutter:** the 3D cantilever's fixed face draws up to 400 blue triangles. Set Conditions = false (3D input 4, 2D input 5) and hide the Support component.
- **Screen-space sizes:** arrow length is 45 px from the on-screen viewport scale (`Glyphs.cs:18-31`), and arrow heads (14), points (7) and line widths (1–2) are in pixels. They look thin in a 2× capture. Use `ViewCapture.ScaleScreenItems` or draw your own.
- **Stops early:** after `Converged` or `Max iter` the solver does nothing and Visualize is cached, so frames are identical. Keep orbiting only.
- **Grid padding:** padded 3D voxels are `Outside`. Pick resolutions that divide the box evenly (canopy 40 gives 40×40×16; 48 gives 48×48×20 with a ragged top layer).
- **Size guards** (`InvalidOperationException`): filter neighbour table over 1e8 entries (`NeighborFilter.cs:12,38`); 3D multigrid above about 2 M voxels (`MultigridSolver3D.cs:42,55`); 2D factorisation above 1.5 GB (short side about 390). The component caps are 600 (2D) and 160 (3D).
- **Closing an edited example:** set `doc.IsModified = false` before removing it, or Grasshopper may show a save prompt. A detached document left `Enabled = true` may keep previewing, so disable it.
- **Unknown run_csharp behaviour:** if it runs off the UI thread, wrap Grasshopper and view calls in `RhinoApp.InvokeOnUiThread`. If `dynamic` is not available to its compiler, use `FieldInfo` / `MethodInfo`. Long loops freeze Rhino, so chunk them (about 20 iterations per call).
- **Installed package vs repo:** the Yak 1.0.1 package is what Grasshopper loads; the working tree has uncommitted changes only in docs, tools and one test `.gh`, so `src` matches.