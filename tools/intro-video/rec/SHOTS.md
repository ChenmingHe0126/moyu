# Recorded clips (2026-11-09)

Every clip also exists as `<name>_en.mp4`: the same shot with `lang=en` (English game HUD). The seeds are fixed, so the
English takes match the Chinese ones frame for frame; the edit's English cut (`IntroEn`) picks them up automatically.

All 1080×1920, 60 fps, recorded in Rhino 8.36 with the installed Moyu 1.0.1 (`%APPDATA%\McNeel\Rhinoceros\packages\8.0\moyu\1.0.1`),
display scale 175 % (HUD `k = 1.75`). Output: `dist/intro-video/footage/<subject>/` (git-ignored). Times are seconds
from the clip start; a one-frame switch at `t` means frame `t × 60` is the first frame of the new state.

| Clip | Shot class, args | Length | Events |
|---|---|---|---|
| `rebound/rebound_hook.mp4` | `Rebound`, `bx=35;by=2` (defaults: seed 11, lens 39, pre-roll 3.2 s with two multi-ball splits) | 12 s | 0: six balls in play · 1.5: pierce (穿透) · **5.000: boss key → tower elevation** (camera shifted 35 m right, 2 m up, lens 77) held to the end |
| `voxstack/voxstack_play_boss.mp4` | `VoxStack`, `lens=47;tz=0.27;bzoom=1.45` (camera lowered so the well clears the captions) (seed 5, level 4 多层 Low-rise) | 12 s | 0: custom unit "Gate 门楼" falling over a prepared stack · 1.7: gumball blue ring hot · 1.95–2.45: drag, the gate turns 90° about Z at ≈2.32 · 2.55–2.95: two moves · **3.2: hard drop, FLOOR CLEARED +840** · ≈3.5: O4 dealt · 3.85: stood up · 4.0–4.6: moves · **4.85: hard drop, DOUBLE FLOOR +1,400, 连消 ×2** · **5.500: boss key → massing study** |
| `rhinoui/rhino_ui.mp4` | `RhinoUi` (defaults: click 1.0, boss 6.5; Grasshopper window 1400 px tall) | 10 s, 1920×2480 | Rhino main window (1920×1080) above the Grasshopper window (1920×1400), both taken with `PrintWindow` every frame. 0–1.0: four views with the example's sketched gate units, GH canvas on the custom-unit group, ribbon on the Moyu tab · **1.0: the Massing Study's Run is pressed** (cursor drawn in the edit at frame (1188, 2191)) · 1.0–4.0: real 3-2-1 in the maximised Perspective view · 4.0–6.5: bot drops pieces · **6.5: boss key → massing study; command line `MassingStudy: Option B updated · 12F …`; Metrics panel (frame 1504, 1938, 332 × 244) shows Option B** |
| `serpentine/serpentine_torus.mp4` | `Serpentine`, `site=1;cam=0;boss=-1;frames=330;ozoom=1.3;oel=48` (seed 3, level 4 草坡 Meadow, level locked) | 5.5 s | free view of the whole torus from frame 0 · fruit along the route, combo ×2 → ×5 · melon +400 near the end |
| `serpentine/serpentine_terrain_boss.mp4` | `Serpentine`, `site=0;cam=-1;boss=5;frames=600` | 10 s | first person on the rolling terrain, square route round the middle of the site, combo ×5 by 3.0 · **5.000: boss key → landscape plan (Route A, contours, trees, N1 Lookout / N2 Pavilion)** |
| `swift/swift_play_boss_return.mp4` | `Swift`, `exposure=2;preroll=14;blens=44` (seed 4, the test suite's pilot) | 17 s | 0: score 7, one wall a second · 4.5: LEVEL 02 card · **5.000: boss key → Section A–A (daylight, airflow)** · **9.000: boss key again → real 3-2-1** · 12.0: flying again, score 11 → 13 |
| `topology/topo2d.mp4` | `Topo2D`, `start=0.6;end=2.6;frames=240` (case treevoid: 150×175 pixels, foot in the middle of the base, line load on a solid top deck, round void, V 0.3, R 2.5) | 4 s | 0–0.6: grey domain with loads and supports · 0.6–2.6: iterations 1–75 (converged) · then the result with contours |
| `topology/topo3d.mp4` | `Topo3D`, `start=0.5;end=3.0;smooth=3.4;frames=360;sweep=55` (case table: 40×40×44 voxels, surface load on a solid slab, four pinned corners, V 0.15) | 6 s | 0–0.5: full block · 0.5–3.0: iterations 1–37 (converged) with an eased orbit · 3.4: smooth isosurface (Visualize 3D mode 1) |

## Real solve times (for the speed labels in the edit)

Measured on this machine while both cases were solving at the same time, so they are, if anything, slower than a
solve on its own:

- 2D treevoid: 75 iterations, 52.6 s (≈ 0.70 s per iteration). Shown in 2.0 s → ×26; the edit plays these iterations at 0.8× (2.5 s) → **×21**.
- 3D table: 37 iterations, 19.9 s (≈ 0.54 s per iteration). Shown in 2.5 s → **×8**.

## Not used in the cut

`Serpentine` on the torus in first person (the follow camera sits close over the tube and hides the shape) and its
torus boss screen (a slope map more than a landscape plan) were tried and dropped in favour of the two clips above.
The level-up card is suppressed in Serpentine (`lock`) because in portrait it overlaps the status line.
