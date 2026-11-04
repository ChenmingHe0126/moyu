# 开发流程

给改代码的人看的：构建调试、做新游戏的流程、图标怎么生成、开发用时怎么统计。安装和使用见 [README](../README.md)。

## 构建与调试

- `dotnet build src/Moyu -c Release`，编译前关掉 Rhino（它锁着 `Moyu.gha`）。
- `src/Moyu/Properties/launchSettings.json` 的两个启动配置（**Rhino 8 - netcore** / **netfx**）启动 MSBuild 属性 `RhinoExe` 指向的 Rhino，默认 `C:\Program Files\Rhino 8\System\Rhino.exe`，并通过 `RHINO_PACKAGE_DIRS` 加载构建目录。Rhino 装在别处时在 `src/Moyu/Moyu.csproj.user`（git 忽略）里设 `<RhinoExe>`。VS Code 的 `src/Moyu/.vscode/launch.json` 不读 `RhinoExe`，固定写默认路径；Rhino 装在别处时在 VS Code 用户设置的 `"launch"` 里加一份带自己路径的配置，不要提交改过的 launch.json。
- 改了代码要退出 Rhino 再编译再开。自动化时退出用 `_-Exit _No`；不要用 `open_application` 唤起 Rhino（会多开一个安全模式实例）。
- 核心测试 `dotnet run -c Release --project tests/Moyu.Core.Tests`，每个规则改动都在这里加检查。Rhino.Inside 集成测试和崩溃排查见 [testing.md](testing.md)。
- 发布包：`python tools/package.py`，见 README 的"发布"。`Moyu.gha` 不是用当前 `<Version>` 编出来的（比如 `--no-build` 碰上改过版本号）时脚本拒绝打包，先构建。

## 做新游戏

照 [game-workflow.md](game-workflow.md)：方案（`docs/<游戏>-plan.md`）→ v1（规则引擎放 `Games/<游戏>/Core/`，不依赖 Rhino；会话继承 `GameSession`，绘制继承 `GameConduit`）→ 真实按键实测 → 图标 → README 和方案文档补"v1 状态"→ 计时。画面风格照 [visual-style.md](visual-style.md)。

HUD 尺寸用 `GameConduit.Ui(d)`（每 HUD 单位的像素数：系统缩放，最多视口高度 / 720，最少 1）统一缩放，像素常数、字号、偏移都乘它；3D 几何不缩放。

## 图标

图标是 `src/Moyu/Icons/` 里按组件类名命名的 24×24 透明 PNG（`Moyu.png` 兼作插件和页签图标），由 Codex 的生图功能按一张图标表生成，再用 `tools/make_icons.py` 切开：

```bash
python tools/make_icons.py docs/images/moyu-icons-blue-violet-source.png                                   # 2D，默认 4×2
python tools/make_icons.py docs/images/moyu-icons-3d-blue-violet-source.png --set 3d
python tools/make_icons.py docs/images/moyu-icons-massing-source.png --set massing --cols 2 --rows 2
python tools/make_icons.py docs/images/moyu-icons-landscape-source.png --set landscape --cols 2 --rows 1 --source-out docs/images/landscape-icons-hd --preview docs/images/moyu-icons-landscape-preview.png
python tools/make_icons.py docs/images/moyu-icons-facade-source.png --set facade --cols 2 --rows 1 --source-out docs/images/facade-icons-hd --preview docs/images/moyu-icons-facade-preview.png
python tools/make_icons.py docs/images/moyu-icons-section-source.png --set section --cols 2 --rows 1 --source-out docs/images/section-icons-hd --preview docs/images/moyu-icons-section-preview.png
```

`--source-out` 另存原分辨率切图（Landscape / Facade / Section 各保留了一份 887×887 高清切图），`--preview` 出实际尺寸预览。每个面板的提示词、文件映射和接入状态：[2D](icon-prompts.md)、[3D](icon-prompts-3d.md)、[Massing](icon-prompts-massing.md)、[Landscape](icon-prompts-landscape.md)、[Facade](icon-prompts-facade.md)、[Section](icon-prompts-section.md)。提示词的格式：一段游戏背景、每个组件只写作用（设计要求另给）、一行输出排版（几列几行、哪格留空、透明背景）。

新面板或换图标，在 Claude Code 里说"生成图标"：项目技能 `moyu-icons`（`.claude/skills/moyu-icons/`）会按仓库里的格式写提示词、用 `tools/codex_image.py` 调本机 Codex 出图（`--check` 看用的是哪个 codex、是否已登录）、切图、出预览、编译并跑图标检查。需要本机装好并登录 Codex（`npm install -g @openai/codex`，或用 Codex 桌面版自带的命令行）；Claude Code 和 Codex 怎么配合的调研见 [claude-codex-bridge.md](claude-codex-bridge.md)。

编译后检查 23 个组件和 Moyu 页签都拿到了图标（无界面 Rhino）：

```bash
dotnet run -c Release --project tests/Moyu.RhinoTests -- --icons
```

实际显示效果要重启 Rhino / Grasshopper 看一眼。

## 开发用时

每个游戏做完都统计一次，写进 [dev-time-stats.md](dev-time-stats.md)：

```bash
python tools/dev_time.py --detail -o dev_time_timeline.txt
```

它读本项目的 Claude Code 会话记录，列出每一轮（用户发请求 → 这一轮最后一条回复）的起止时间、时长和用到的工具；按时间线切成阶段（方案 / 开发到 v1 / 实机测试 / 后续），剔除 Rhino 本身或第三方插件的故障时间，汇总实际开发用时、墙钟时间和产出。细则见 [game-workflow.md](game-workflow.md) 第 6 节。

各轮修改的记录：[second-round-report.md](second-round-report.md)、[third-round-report.md](third-round-report.md)、[fourth-round-report.md](fourth-round-report.md)、[serpentine-report.md](serpentine-report.md)、[rebound-inkline-report.md](rebound-inkline-report.md)、[swift-report.md](swift-report.md)。
