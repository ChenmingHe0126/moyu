# Moyu {version} — install / 安装

Moyu is a Grasshopper plugin for **Rhino 8 for Windows**. It works in both of Rhino 8's runtimes (.NET Core, the default, and .NET Framework). It has not been tested on Rhino for Mac.

## Option 1 — Package Manager (recommended)

1. In Rhino run `_PackageManager`.
2. Search for **moyu**, click Install.
3. Restart Rhino. The components are on the Grasshopper ribbon under the **Moyu** tab (panels 2D, 3D, Massing, Landscape, Facade, Section).

## Option 2 — the .yak file in this archive

Drag `{yak}` into an open Rhino window (or run `_PackageManager` and use *Install from file*). Restart Rhino.

## Option 3 — copy the .gha by hand

1. Create the folder `%APPDATA%\Grasshopper\Libraries\Moyu\`.
2. Copy **one** `Moyu.gha` into it:
   - `net7.0\Moyu.gha` if Rhino runs on .NET Core (the default; `_SetDotNetRuntime` shows which one is active),
   - `net48\Moyu.gha` if Rhino runs on .NET Framework.
3. If Windows marks the file as blocked (right-click → Properties → *Unblock*), unblock it.
4. Restart Rhino.

## First steps

Open one of the `examples/*.gh` files in Grasshopper. `Moyu-Guide-EN.pdf` is the illustrated guide to every component and game; `QUICKSTART.md` is the one-page version.

## Uninstall

Package Manager: `_PackageManager` → Installed → moyu → Uninstall. Hand copy: delete the `Moyu` folder from `%APPDATA%\Grasshopper\Libraries\`. High scores are kept in `%APPDATA%\Moyu\` and can be deleted too.

## Documentation and source

https://github.com/ChenmingHe0126/moyu — README (Chinese) with every component's inputs and every game's keys. Licence: MIT (see LICENSE.txt).

---

# 安装说明（中文）

Moyu 是 **Windows 版 Rhino 8** 的 Grasshopper 插件，Rhino 8 的两种运行时（默认的 .NET Core 和 .NET Framework）都能用。没有在 Mac 版 Rhino 上测试过。

## 方式一：包管理器（推荐）

1. 在 Rhino 里运行 `_PackageManager`。
2. 搜索 **moyu**，点 Install。
3. 重启 Rhino。组件在 Grasshopper 工具栏的 **Moyu** 页签下（2D、3D、Massing、Landscape、Facade、Section 六个面板）。

## 方式二：压缩包里的 .yak 文件

把 `{yak}` 拖进打开的 Rhino 窗口（或运行 `_PackageManager`，用 *Install from file*）。重启 Rhino。

## 方式三：手动复制 .gha

1. 新建文件夹 `%APPDATA%\Grasshopper\Libraries\Moyu\`。
2. 复制**一个** `Moyu.gha` 进去：
   - Rhino 跑在 .NET Core 上（默认；`_SetDotNetRuntime` 命令能看到当前是哪种）用 `net7.0\Moyu.gha`，
   - 跑在 .NET Framework 上用 `net48\Moyu.gha`。
3. 如果 Windows 把文件标成"已阻止"（右键 → 属性 → *解除锁定*），先解除。
4. 重启 Rhino。

## 上手

在 Grasshopper 里打开 `examples/*.gh` 里的任意一个示例。`Moyu-Guide-ZH.pdf` 是图文手册，每个组件和游戏都有介绍；`QUICKSTART.md` 是一页的简版。

## 卸载

包管理器：`_PackageManager` → Installed → moyu → Uninstall。手动复制的：删掉 `%APPDATA%\Grasshopper\Libraries\` 里的 `Moyu` 文件夹。最高分记录在 `%APPDATA%\Moyu\`，也可以一起删。

## 文档与源码

https://github.com/ChenmingHe0126/moyu — README 里有每个组件的输入和每个游戏的按键。许可证：MIT（见 LICENSE.txt）。
