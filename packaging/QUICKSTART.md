# Moyu — quick start / 快速上手

Rhino 8 for Windows. Open an example in Grasshopper, then follow its note at the top of the canvas.
Windows 版 Rhino 8。在 Grasshopper 里打开一个示例，按画布顶部的说明操作。

**Quickest way / 最快的办法:** drop the **Examples** component (the first panel, Moyu › 0 | Examples, or search `example`) on the canvas and click a button; the example opens in a new tab as an unnamed copy.
把 **Examples** 组件（最前面的 Moyu › 0 | Examples 面板，或在画布上搜 `example` / `示例`）拖到画布上，点一个按钮，示例就在新标签页里打开（未命名的副本）。

## Examples / 示例 (`examples/`)

| File / 文件 | What / 内容 |
|---|---|
| `Topo2D.gh` | 2D topology optimisation: cantilever, bridge elevation, L-bracket · 2D 拓扑优化：悬臂、立面桥、L 形支架 |
| `Topo3D.gh` | 3D voxel optimisation: cantilever, canopy, bracket · 3D 体素优化：悬臂、雨棚、支架 |
| `VoxStack.gh` | 3D falling blocks (Massing panel), incl. custom units · 3D 落块（Massing 面板），含自定义积木 |
| `Serpentine.gh` | Snake on a surface (Landscape panel) · 曲面贪吃蛇（Landscape 面板） |
| `Rebound.gh` | Brick breaker on a facade (Facade panel) · 立面打砖块（Facade 面板） |
| `Swift.gh` | Flight through a section (Section panel) · 剖面穿墙（Section 面板） |

## Topology optimisation / 拓扑优化

Set a group's **Run** toggle to True (the Timer is wired), **Reset** starts over; drag sliders while it runs.
把某组的 **Run** 开关拨到 True 开始（Timer 已接好），**Reset** 重来；运行中可以拖滑块。

## Games / 游戏

Click **▶ Run** under the study component; it plays in the active Rhino viewport. Click **■ Stop** to end.
点研究组件下面的 **▶ Run**，在当前 Rhino 视口里玩；点 **■ Stop** 结束。

| Key / 键 | All games / 所有游戏 |
|---|---|
| Esc | pause / resume · 暂停 / 继续 |
| Shift+Esc (paused) | quit · 退出 |
| Enter / Space (game over) | play again · 再来一局 |
| Home | reset the view · 复位视角 |
| key left of 1 (` on US keyboards) | boss key: a serious-looking study drawing, press again to return · 老板键：切成正经的研究图，再按一次回来 |
| left / right drag | orbit · 转视角 |

| Game / 游戏 | Keys / 按键 |
|---|---|
| VoxStack | ← ↑ ↓ → move · drag the gumball rings or Q E / W S / A D to rotate · Space drop · C hold · 移动 / 拖 gumball 或按键旋转 / 落下 / 暂存 |
| Serpentine | ← / A, → / D turn · hold ↑ / Shift to sprint · C camera · 转向 / 冲刺 / 换镜头 |
| Rebound | ← → / A D or the mouse move the paddle · Space serves · 挡板 / 发球 |
| Swift | Space / ↑ / W / left click flap · 扇翅 |

While a dialog, the Grasshopper window or a Rhino command has the keyboard, the game pauses. High scores: `%APPDATA%\Moyu\`.
对话框、Grasshopper 窗口或 Rhino 命令占用键盘时游戏自动暂停。最高分存在 `%APPDATA%\Moyu\`。

Illustrated guide / 图文手册: `Moyu-Guide-EN.pdf`, `Moyu-Guide-ZH.pdf` · More: https://github.com/ChenmingHe0126/moyu · Install / uninstall: `INSTALL.md` · Licence: MIT
