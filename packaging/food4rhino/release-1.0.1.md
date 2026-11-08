# Moyu 1.0.1 — release notes and upload steps

## What's new (paste into food4rhino's release / change notes; English only there)

- **Examples component** (new first panel, Moyu › 0 | Examples; panels are now numbered): one click opens any of the six examples in a new tab, however Moyu was installed.
- **Language** of the in-game text is now in each game component's right-click menu: Auto (follows Windows), 中文 or English.
- **Fixes** from a full code review (Claude Code and Codex): Rebound no longer stops when the last brick falls with several balls in play; keys released while Ctrl/Alt is held no longer stick; holding the boss key no longer flips back; VoxStack saves the score when a hard drop ends the game; Serpentine counts a full board as a win; Swift takes every fast click; topology fixes for NaN inputs, heavily constrained 3D models and thin 2D outlines. 61 issues in all.
- Bilingual (中文 / English) notes on every example.

Source and the full review: https://github.com/ChenmingHe0126/moyu

## 中文对照（不上传）

- **Examples 组件**（新的第一个面板 Moyu › 0 | Examples；面板现在按编号排）：点一下就在新标签页打开六个示例之一，不管怎么安装的都能用。
- 游戏内文字的**语言**改到每个游戏组件的右键菜单：Auto（跟随 Windows）/ 中文 / English。
- 全面代码审查（Claude Code + Codex）后的**修复**：多球时打掉最后一块砖不再中止；按着 Ctrl/Alt 松键不再卡住；长按老板键不再弹回；叠楼硬降顶死会存分；蜿蜒铺满算赢；雨燕快速连点每下都算；拓扑优化处理 NaN 输入、约束很多的 3D 模型和 2D 细杆轮廓。共 61 个问题。
- 每个示例的说明都改成中英双语。

## Files

- `dist/moyu-1.0.1-rh8_0-win.yak`: the Package Manager package.
- `dist/Moyu-1.0.1-food4rhino.zip`: examples, both guide PDFs and both `.gha` builds for installing by hand.
- `dist/SHA256SUMS.txt`.

## Upload route (an update, not a new app)

1. `"C:\Program Files\Rhino 8\System\Yak.exe" login` (or your Rhino's `Yak.exe`), then `yak push dist\moyu-1.0.1-rh8_0-win.yak`. A pushed version can never be replaced, only yanked. The food4rhino page is linked to the Yak package, so its Install row moves to 1.0.1 by itself.
2. On food4rhino (node 5994, Edit), replace the zip in the "Examples, user guide and manual install (zip)" row with `dist/Moyu-1.0.1-food4rhino.zip`, and paste "What's new" above where the page has release notes.
