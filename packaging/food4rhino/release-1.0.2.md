# Moyu 1.0.2 — release notes and upload steps

## What's new (paste into food4rhino's release / change notes; English only there)

- **Rhino 8 for Mac** (about 40 min): the whole plugin now loads on Rhino for Mac, the four games included. The games read the keyboard through AppKit there; the boss key is the key left of 1 (` or §) and ⌘ shortcuts go to Rhino. In-game text follows the Mac's preferred language, and high scores are kept in `~/Library/Application Support/Moyu/`.
- How the Mac version was checked: built and tested on GitHub's macOS runners (468 core tests, 39 keyboard checks with real key events), and every Rhino, Grasshopper and .NET API the plugin calls was matched against the Rhino 8.36 for Mac installer. It has not yet been play-tested in Rhino for Mac; please report problems on GitHub.
- **Windows**: nothing changes in use. All four games were re-played in Rhino 8 for Windows on this build.
- The Package Manager now offers a Windows and a Mac package; Rhino picks the right one.

Source and the Mac test notes: https://github.com/ChenmingHe0126/moyu

## 中文对照（不上传）

- **支持 Rhino 8 for Mac**（约 40 分钟）：整个插件能在 Mac 版 Rhino 里加载，四个游戏也在内。游戏在 Mac 上通过 AppKit 读键盘；老板键是数字 1 左边的键（` 或 §），⌘ 组合键交给 Rhino。游戏内文字跟随 Mac 的首选语言，最高分存在 `~/Library/Application Support/Moyu/`。
- Mac 版怎么验证的：在 GitHub 的 macOS 机器上编译并测试（468 项核心测试、39 项用真实按键事件的键盘检查），插件调用的每个 Rhino、Grasshopper 和 .NET 接口都对照 Rhino 8.36 for Mac 安装包核对过。还没有在 Rhino for Mac 里实际试玩，遇到问题请到 GitHub 提。
- **Windows**：用法不变。四个游戏在这一版上重新玩过一遍。
- 包管理器现在有 Windows 和 Mac 两个包，Rhino 自己选。

## Files

- `dist/moyu-1.0.2-rh8_0-win.yak`: the Package Manager package for Windows (net48 and net7.0 builds).
- `dist/moyu-1.0.2-rh8_0-mac.yak`: the Package Manager package for Mac (the plain net7.0 build).
- `dist/Moyu-1.0.2-food4rhino.zip`: both `.yak` files, the three `.gha` builds for installing by hand (`windows/net48`, `windows/net7.0`, `mac`), examples, both guide PDFs.
- `dist/SHA256SUMS.txt`.

## Upload route (an update, not a new app)

1. `yak push dist\moyu-1.0.2-rh8_0-win.yak`, then `yak push dist\moyu-1.0.2-rh8_0-mac.yak` (same version, two distributions). A pushed version can never be replaced, only yanked. The food4rhino page is linked to the Yak package, so its Install row moves to 1.0.2 by itself.
2. On food4rhino (node 5994, Edit): add a row "Moyu 1.0.2 - Examples, user guide and manual install (zip)" with `dist/Moyu-1.0.2-food4rhino.zip`, listed first; retitle nothing else, but prefix the 1.0.1 row's description with "Previous version 1.0.1 (2026-11-08)."; add Mac to the platforms; add the Mac line to the description (see `listing.md`).

Row description for 1.0.2: "Version 1.0.2 (2026-11-15). Six example .gh files, the illustrated user guide (PDF, English and Chinese) and the Moyu.gha builds for installing by hand on Windows and Mac. New in 1.0.2: Rhino 8 for Mac. The Install button above gives you the same plugin, examples and guide through Rhino's Package Manager."

## Status

Not published yet.
