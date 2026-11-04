# food4rhino listing copy — Moyu 1.0.0

Paste these into the food4rhino app form. The English block is the main description; the Chinese block goes after it (food4rhino has one description field, so both go in the same box). Screenshots to upload are listed at the end.

## App name

Moyu

## One-line summary (Short Description, at most 180 characters)

Live 2D/3D topology optimisation for Grasshopper, plus four Rhino viewport games. A research and teaching project on building a plugin with an AI agent, mostly by Claude Code.

## Description (Body, English: food4rhino takes English only)

**Moyu** is a research and teaching project on building a Rhino plugin quickly with an AI coding agent: almost all of the code was written by Claude Code, in about 9 hours of agent time. It has two parts: live topology optimisation, and four small games that study interaction and display in the Rhino viewport. Each game is disguised as an architecture study and has a boss key that turns it into a serious drawing.

- **2D / 3D topology optimisation** (about 2 h): domain, loads and supports from curves or solids; a SIMP solver iterates live on a Grasshopper Timer while you edit.
- **VoxStack**: 3D falling blocks with a drag-to-rotate gumball (about 1 h 25 min).
- **Serpentine**: a snake on any surface (about 1 h 10 min).
- **Rebound**: brick breaker on real brick bonds (about 25 min).
- **Swift**: one-key flight through a building section (about 55 min).

Open source (MIT): https://github.com/ChenmingHe0126/moyu

## 中文对照（不上传，只作参考）

**Moyu** 是一个研究与教学项目：用 AI 编程代理快速开发一个 Rhino 插件，几乎所有代码由 Claude Code 编写，代理用时约 9 小时。两部分：实时拓扑优化，以及四个研究 Rhino 视口交互与显示的小游戏；每个游戏都伪装成建筑研究，老板键一按就是一张正经的图。

- **2D / 3D 拓扑优化**（约 2 小时）：用曲线或实体定义设计域、荷载和支座，SIMP 求解器接 Timer 实时迭代，边算边改。
- **VoxStack 叠楼**：三维落块，可拖动旋转的 gumball（约 1 小时 25 分）。
- **Serpentine 蜿蜒**：任意曲面上的贪吃蛇（约 1 小时 10 分）。
- **Rebound 回弹**：按真实砌法排砖的打砖块（约 25 分钟）。
- **Swift 雨燕**：在建筑剖面里单键飞行（约 55 分钟）。

开源（MIT）：https://github.com/ChenmingHe0126/moyu

## Downloads table

The page shows two rows:

1. **moyu 1.0.0 · Install**: added by food4rhino from the linked Package Manager package. Install hands over to Rhino's Package Manager, which installs the plugin with the examples and the guide. Its description is `packaging/manifest.yml`'s and can only change with a new pushed version.
2. **Examples, user guide and manual install (zip)**: the uploaded `dist/Moyu-<version>-food4rhino.zip`, platform Grasshopper › Windows › Version 8. Description: "Six example .gh files, the illustrated user guide (PDF, English and Chinese) and both Moyu.gha builds for installing by hand. The Install button above gives you the same plugin, examples and guide through Rhino's Package Manager."

## Form fields

| Field | Value |
|---|---|
| Category | Grasshopper plugin / Analysis & Simulation (structural), Games |
| Licence | MIT (open source) |
| Price | Free |
| Rhino version | 8 |
| Platform | Windows |
| Keywords | topology optimisation, SIMP, structural, voxel, grasshopper, game, viewport, interaction, AI agent, Claude Code, teaching, research |
| Website / source | https://github.com/ChenmingHe0126/moyu |
| Support Email | mandatory on food4rhino: use an address you are willing to publish (it is shown on the app page) |
| Support Forum | https://github.com/ChenmingHe0126/moyu/issues |
| Project Icon | `packaging/icon-512.png` (food4rhino shows it at 100 × 100; custom-drawn, not a screenshot) |

## Upload route

1. Push the package to the Package Manager first: `"C:\Program Files\Rhino 8\System\Yak.exe" login` (or the `Yak.exe` of your Rhino install), then `yak push dist\moyu-1.0.0-rh8_0-win.yak` (rehearse on the test server with `--source https://test.yak.rhino3d.com` if you like; it is wiped nightly). A pushed version can never be replaced, only yanked, so push only the final build; `tools/package.py` refuses to package Release outputs built from another `<Version>`.
2. On food4rhino: My Content → **Create App from Yak**, package name `moyu`. This links the listing to the Package Manager so Rhino's `_PackageManager` and the food4rhino page show the same releases.
3. Fill the mandatory fields (Title, Short Description, Body, Category, License Agreement = MIT, Support Email, Project Icon), add the screenshots below, and attach `dist/Moyu-1.0.0-food4rhino.zip` as a release file for people who install by hand. food4rhino's FAQ lists .rhi, .zip, a single .rhp/.gha as release formats; the .yak reaches users through the Package Manager, not as a download.
4. A person reviews new apps; it usually takes one or two days before the page is public.

## Screenshots to upload (in this order)

1. `docs/images/bracket_result.png` — 2D topology optimisation result (L-bracket with a hole).
2. `docs/images/c3d_final.png` — 3D voxel optimisation result.
3. `docs/images/canopy_result.png` — 3D canopy.
4. `docs/images/voxstack_gumball.jpg` — VoxStack with the gumball.
5. `docs/images/stack_boss.jpg` — VoxStack boss screen (massing study).
6. `docs/images/serpentine_firstperson.jpg` — Serpentine first-person view.
7. `docs/images/serpentine_boss.jpg` — Serpentine boss screen (landscape plan).
8. `docs/images/rebound_game.jpg` — Rebound.
9. `docs/images/rebound_boss.jpg` — Rebound boss screen (tower façade).
10. `docs/images/swift_game.jpg` — Swift.
11. `docs/images/swift_boss.jpg` — Swift boss screen (section with daylight and ventilation).

Icon: `packaging/icon-512.png`.
