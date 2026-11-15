# Moyu {version} — install / 安装

Moyu is a Grasshopper plugin for **Rhino 8 for Windows and Mac**. On Windows it works in both of Rhino 8's runtimes (.NET Core, the default, and .NET Framework).

## Option 1 — Package Manager (recommended)

1. In Rhino run `_PackageManager`.
2. Search for **moyu**, click Install. Rhino picks the Windows or the Mac package by itself.
3. Restart Rhino. The components are on the Grasshopper ribbon under the **Moyu** tab (panels 2D, 3D, Massing, Landscape, Facade, Section).

## Option 2 — the .yak file in this archive

Drag `{yak_win}` (Windows) or `{yak_mac}` (Mac) into an open Rhino window (or run `_PackageManager` and use *Install from file*). Restart Rhino.

## Option 3 — copy the .gha by hand

1. In Grasshopper choose *File › Special Folders › Components Folder*; it opens the Libraries folder.
2. Copy **one** `Moyu.gha` into it:
   - Windows, Rhino on .NET Core (the default; `_SetDotNetRuntime` shows which one is active): `windows/net7.0/Moyu.gha`,
   - Windows, Rhino on .NET Framework: `windows/net48/Moyu.gha`,
   - Mac: `mac/Moyu.gha`.
3. If Windows marks the file as blocked (right-click → Properties → *Unblock*), unblock it.
4. Restart Rhino.

## First steps

Drop the **Examples** component (the first panel, Moyu › 0 | Examples) on the canvas and click a button to open an example, or open one of the `examples/*.gh` files in Grasshopper. `Moyu-Guide-EN.pdf` is the illustrated guide to every component and game; `QUICKSTART.md` is the one-page version.

On a Mac the games use the same keys; the boss key is the key left of 1 (` or §), and ⌘ shortcuts always go to Rhino.

## Uninstall

Package Manager: `_PackageManager` → Installed → moyu → Uninstall. Hand copy: delete `Moyu.gha` from the Components Folder. High scores are kept in `%APPDATA%\Moyu\` (Mac: `~/Library/Application Support/Moyu/`) and can be deleted too.

## Documentation and source

https://github.com/ChenmingHe0126/moyu — README (Chinese) with every component's inputs and every game's keys. Licence: MIT (see LICENSE.txt).

---

# 安装说明（中文）

Moyu 是 **Rhino 8（Windows 和 Mac）** 的 Grasshopper 插件。Windows 上 Rhino 8 的两种运行时（默认的 .NET Core 和 .NET Framework）都能用。

## 方式一：包管理器（推荐）

1. 在 Rhino 里运行 `_PackageManager`。
2. 搜索 **moyu**，点 Install。Rhino 会自己选 Windows 版或 Mac 版。
3. 重启 Rhino。组件在 Grasshopper 工具栏的 **Moyu** 页签下（2D、3D、Massing、Landscape、Facade、Section 六个面板）。

## 方式二：压缩包里的 .yak 文件

把 `{yak_win}`（Windows）或 `{yak_mac}`（Mac）拖进打开的 Rhino 窗口（或运行 `_PackageManager`，用 *Install from file*）。重启 Rhino。

## 方式三：手动复制 .gha

1. 在 Grasshopper 里点 *File › Special Folders › Components Folder*，会打开 Libraries 文件夹。
2. 复制**一个** `Moyu.gha` 进去：
   - Windows，Rhino 跑在 .NET Core 上（默认；`_SetDotNetRuntime` 命令能看到当前是哪种）：`windows/net7.0/Moyu.gha`，
   - Windows，Rhino 跑在 .NET Framework 上：`windows/net48/Moyu.gha`，
   - Mac：`mac/Moyu.gha`。
3. 如果 Windows 把文件标成"已阻止"（右键 → 属性 → *解除锁定*），先解除。
4. 重启 Rhino。

## 上手

把 **Examples** 组件（最前面的 Moyu › 0 | Examples 面板）拖到画布上，点按钮打开示例；或者在 Grasshopper 里打开 `examples/*.gh` 里的任意一个。`Moyu-Guide-ZH.pdf` 是图文手册，每个组件和游戏都有介绍；`QUICKSTART.md` 是一页的简版。

Mac 上游戏按键一样；老板键是数字 1 左边那个键（` 或 §），⌘ 组合键永远交给 Rhino。

## 卸载

包管理器：`_PackageManager` → Installed → moyu → Uninstall。手动复制的：从 Components Folder 里删掉 `Moyu.gha`。最高分记录在 `%APPDATA%\Moyu\`（Mac：`~/Library/Application Support/Moyu/`），也可以一起删。

## 文档与源码

https://github.com/ChenmingHe0126/moyu — README 里有每个组件的输入和每个游戏的按键。许可证：MIT（见 LICENSE.txt）。
