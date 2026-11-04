# food4rhino listing copy — Moyu 1.0.0

Paste these into the food4rhino app form. The English block is the main description; the Chinese block goes after it (food4rhino has one description field, so both go in the same box). Screenshots to upload are listed at the end.

## App name

Moyu

## One-line summary

A research and teaching project on building a Rhino plugin quickly with an AI coding agent: live 2D/3D topology optimisation for Grasshopper, plus four small viewport games that explore interaction and display in Rhino. Written mostly by Claude Code; open source (MIT).

## Description (English)

**What this is.** Moyu is a research and teaching project about developing a Rhino / Grasshopper plugin quickly with an AI coding agent. Almost all of the code, tests and documentation were written by **Claude Code** (Anthropic's coding agent), directed and play-tested by the author; the icons were drawn with OpenAI Codex image generation. The plans, development logs and a time log are public on GitHub, so you can follow how each part was asked for, built, tested and fixed. It is not a production engineering tool.

**The parts, and roughly how long each took** (Claude Code's working time; the author's reviewing and play-testing are not counted):

- Topology optimisation, 2D and 3D, 14 components: about 1 h 55 min
- VoxStack: about 1 h 25 min
- Serpentine: about 1 h 10 min
- Rebound: about 25 min (built in one session together with a game that was later replaced by Swift)
- Swift, plus redrawing Rebound's boss screen as a tower façade: about 55 min
- Three rounds of play-testing fixes (difficulty, power-ups, cameras, gumball): about 1 h 35 min
- Release: a multi-agent code review with 70 confirmed issues fixed, the icon, the packages and the illustrated guide: about 2 h
- In total about 9 hours, spread over four days.

**Topology optimisation (Ribbon `Moyu` › `2D` and `3D`): the numerical part.** It tests whether an agent can write real engineering numerics with tests (finite elements, SIMP with optimality criteria, a density filter, a banded Cholesky solver in 2D and a multigrid-preconditioned conjugate-gradient solver in 3D), and how to make an optimisation interactive in Grasshopper.

- 2D: draw a boundary (with holes), loads, supports and keep / remove areas as curves; pick a material; the solver runs a pixel SIMP optimisation and the visualiser draws the density field and a smoothed outline.
- 3D: the same workflow on a voxel grid of a closed solid; the result comes back as voxels or a smooth mesh.
- Both solvers iterate live: wire a Grasshopper Timer, switch Run on, and drag a load or a support while it converges.
- The results are for study and concept design. They are not a substitute for engineering analysis and carry no warranty.

**Four viewport games (Ribbon `Moyu` › `Massing`, `Landscape`, `Facade`, `Section`): the interaction part.** Each game is a small study of how far Rhino's viewport and input can be taken from a Grasshopper component: drawing a whole scene and a HUD with a display conduit at a steady frame rate, taking the keyboard and mouse safely and handing them back to dialogs, Grasshopper and Rhino commands, driving the camera, and redrawing the same data as a different drawing in one frame (the "boss key"). Each one looks at a different question:

- **VoxStack** (3D falling blocks): moving and turning pieces in 3D while the view turns, with a drag-to-rotate gumball like Rhino's own. Boss screen: a massing study with floor-area ratio and sun shadows.
- **Serpentine** (snake): turning any surface into a playing field through its UV grid (closed directions wrap, trims are walls, slope changes the speed), with a first-person follow camera. Boss screen: a landscape plan with a slope analysis, paths and trees.
- **Rebound** (brick breaker): real-time collision and mouse control on a plane, with real brick bonds as levels. Boss screen: a curtain-wall tower elevation whose panels come from the bricks that are left.
- **Swift** (one-key flight): timing and procedurally generated levels, checked by an autopilot. Boss screen: the same building section as a daylight and ventilation analysis, with the bird's flight as the main airflow line.

Press ▶ Run on the component to start. While a game runs the keyboard belongs to it; Ctrl/Alt shortcuts still reach Rhino, and when a dialog, the Grasshopper window or a Rhino command takes the keyboard the game pauses and hands it over. The key left of 1 (` on a US keyboard) is the boss key; the game also switches by itself when Rhino loses focus. Every game starts easy and ramps up. High scores are kept in `%APPDATA%\Moyu\`.

**What it does not do**

- No network access, no telemetry. The only file it writes is the scores file above.
- Windows only: Rhino 8 for Windows, in both the .NET Framework and the .NET Core runtime. Not tested on Rhino for Mac (the games use Windows keyboard APIs).

**In the download**: the .yak, both `Moyu.gha` builds, six example definitions (`examples/`), an illustrated user guide (`Moyu-Guide-EN.pdf`, `Moyu-Guide-ZH.pdf`), a one-page `QUICKSTART.md` and `INSTALL.md`.

**Install**

- Rhino's Package Manager: run `_PackageManager`, search for *moyu*, install, restart Rhino.
- Or download the `.yak` file here and drag it into a Rhino window.
- Or unzip the archive and copy `Moyu.gha` to `%APPDATA%\Grasshopper\Libraries\Moyu\` (use the `net48` build for the .NET Framework runtime and the `net7.0` build for the .NET Core runtime, which is Rhino 8's default).

**Source, issues and documentation:** https://github.com/ChenmingHe0126/moyu (README in Chinese; the component inputs and keys are documented there, and `docs/dev-time-stats.md` is the time log). Licence: MIT.

## Description (中文)

**这是什么。** Moyu 是一个研究性、教学性的项目，研究怎样用 AI 编程代理快速开发一个 Rhino / Grasshopper 插件。几乎所有代码、测试和文档都由 **Claude Code**（Anthropic 的编程代理）编写，作者负责提需求、看结果和试玩；图标用 OpenAI Codex 的图像生成画出。方案、开发记录和用时统计都在 GitHub 上公开，可以看到每一部分是怎么提出、实现、测试和修好的。它不是面向工程生产的工具。

**各部分和大概用时**（Claude Code 的工作时间，作者看结果和试玩的时间不算）：

- 拓扑优化 2D + 3D，14 个组件：约 1 小时 55 分
- VoxStack 叠楼：约 1 小时 25 分
- Serpentine 蜿蜒：约 1 小时 10 分
- Rebound 回弹：约 25 分钟（和一个后来被 Swift 替换的游戏在同一次会话里做）
- Swift 雨燕，外加把 Rebound 的老板画面重画成塔楼幕墙：约 55 分钟
- 三轮试玩后的调整（难度、道具、视角、gumball）：约 1 小时 35 分
- 发布：多代理代码审查（70 条确认问题全部修复）、插件图标、发布包和图文手册：约 2 小时
- 合计约 9 小时，分布在四天里。

**拓扑优化（工具栏 `Moyu` › `2D`、`3D`）：数值部分。** 检验代理能不能写出带测试的真正工程数值代码（有限元、SIMP + 最优准则法、密度过滤、2D 带状 Cholesky 求解、3D 多重网格预条件共轭梯度），以及怎样在 Grasshopper 里把优化做成交互式的。

- 2D：用曲线画边界（可带孔）、荷载、支座和保留 / 挖空区域，选材料，求解器做像素 SIMP 优化，可视化组件画密度场和平滑轮廓。
- 3D：同一套流程，在封闭实体的体素网格上做，结果是体素块或光滑网格。
- 两个求解器都能实时迭代：接一个 Timer，把 Run 打开，收敛过程中可以拖动荷载或支座看响应。
- 结果用于学习和概念设计，不能替代工程分析，不提供任何担保。

**四个视口小游戏（工具栏 `Moyu` › `Massing`、`Landscape`、`Facade`、`Section`）：交互部分。** 每个游戏都是一个小研究：从一个 Grasshopper 组件出发，Rhino 的视口和输入能做到什么程度。用显示管线（display conduit）以稳定帧率画出整个场景和 HUD；安全地接管键盘鼠标，并在对话框、Grasshopper 窗口和 Rhino 命令需要时让出去；控制镜头；一帧之内把同一份数据换一种画法（"老板键"）。四个游戏各看一个问题：

- **VoxStack 叠楼**（3D 落块）：视角在转时怎样在三维里移动、旋转积木；一个和 Rhino 一样可以拖动旋转的 gumball。老板画面：带容积率和日照阴影的体块研究。
- **Serpentine 蜿蜒**（贪吃蛇）：通过 UV 网格把任意曲面变成场地（闭合方向能绕过去，修剪边是墙，坡度改变速度）；第一人称跟随镜头。老板画面：带坡度分析、步道和树的景观平面。
- **Rebound 回弹**（打砖块）：平面上的实时碰撞和鼠标控制，用真实砌法当关卡。老板画面：塔楼幕墙立面，每块幕墙板取自剩下的砖。
- **Swift 雨燕**（单键飞行）：节奏和程序生成的关卡，用自动驾驶检验每关都能过。老板画面：同一个建筑剖面画成采光和通风分析，小鸟的飞行路线就是主气流线。

点组件上的 ▶ Run 开始。游戏运行时键盘归游戏，Ctrl / Alt 组合键照常给 Rhino；对话框、Grasshopper 窗口或 Rhino 命令拿走键盘时，游戏自动暂停并把键盘让出去。1 左边的键（美式键盘上是 `）是老板键；Rhino 失去焦点时游戏也会自己切过去。每个游戏开局都很容易，逐渐变难。最高分记在 `%APPDATA%\Moyu\`。

**它不做的事**

- 不联网，不收集任何数据；唯一写入的文件是上面的分数文件。
- 只支持 Windows 版 Rhino 8（.NET Framework 和 .NET Core 两种运行时都可以）。没有在 Mac 版 Rhino 上测试过（游戏用了 Windows 的键盘 API）。

**下载包里有**：.yak、两份 `Moyu.gha`、6 个示例（`examples/`）、图文用户手册（`Moyu-Guide-ZH.pdf`、`Moyu-Guide-EN.pdf`）、一页的 `QUICKSTART.md` 和 `INSTALL.md`。

**安装**

- Rhino 包管理器：运行 `_PackageManager`，搜索 *moyu*，安装后重启 Rhino。
- 或者在这里下载 `.yak` 文件，拖进 Rhino 窗口。
- 或者解压压缩包，把 `Moyu.gha` 复制到 `%APPDATA%\Grasshopper\Libraries\Moyu\`（.NET Framework 运行时用 `net48` 目录里的，.NET Core 运行时（Rhino 8 的默认）用 `net7.0` 目录里的）。

**源码、问题反馈和文档：** https://github.com/ChenmingHe0126/moyu（README 是中文的，组件输入和按键都写在里面；用时统计在 `docs/dev-time-stats.md`）。许可证：MIT。

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
