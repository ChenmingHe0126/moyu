# Moyu 摸鱼游戏开发工作流

以后在 Moyu 里做的每个游戏都照这个流程走。第一个按这个流程做完的是 VoxStack 叠楼（`Massing` 面板），各步的参考文件都在仓库里。

## 1. 方案（先不写代码）

- 先读现有代码，再设计：玩法、键盘 + 鼠标操作、计分、模式、组件、技术架构、测试、路线图。
- **老板键是必须的**：每个游戏一个伪装成建筑设计工具的面板（VoxStack 是 `Massing`，以后可以是 `Facade`、`Site`……），组件也用建筑名字。老板键（1 左边的键，美式键盘上是 `` ` ``）一帧内切成正经的设计画面，Rhino 失焦自动切，默认静音。
- 在单独的 Rhino 实例里用一次性 DisplayConduit 脚本做原型截图（`spawn_slot`，不动你正在用的 Rhino）。
- 交付：`docs/<游戏>-plan.md` + 图文网页（一页结论 → 分节，每节先图后文 → 需要拍板的事），参考 `docs/voxstack-plan.md`。

## 2. 开发 v1

- 规则引擎放 `src/Moyu/Games/<游戏>/Core/`，不依赖 Rhino；测试写进 `tests/Moyu.Core.Tests`（csproj 里加一行 include）。
- Rhino 端复用 `src/Moyu/Games/Common/`：`KeyboardHook`（线程钩子、按键去重、输入法处理）、`GameClock`（60 Hz，不触发 GH 重算）。视口绘制用 DisplayConduit，只画自己的视口，不往文档里加物体，不改显示模式。
- 会话继承 `GameSession`（老板键、失焦、倒计时、暂停、镜头、鼠标转视角都在里面），绘制继承 `GameConduit`。
- **画面要有设计感**（用户 2026-11-01 明确要求）：照 [visual-style.md](visual-style.md) 的极简风格做——柔和渐变背景、分章节配色、平涂着色 + 软投影、Bahnschrift 细体、无方框 HUD、键帽提示、浅色暂停 / 结算画面。不用 Rhino 默认着色、霓虹网格、深色方框面板。
- 组件放 `src/Moyu/Components/<面板>/`，游戏本体组件自带 Run 按钮（`RunButtonAttributes`）。
- 测试 GH 文件用 `tests/Moyu.RhinoTests/GhFile<游戏>.cs` 代码生成并自检，加 `--write-gh-<游戏>` 参数，同时并进 `--write-gh`。
- 新组件数量要同步到 `Scenarios.Icons` 的检查。

## 3. 实机测试

- 必须用**真实按键**测（computer-use）。`PostMessage` 注入的按键不经过键盘钩子，测不出东西，还会漏进 Rhino 命令行。
- 要测：GH 画布有焦点时的按键、老板键（游戏中 / 暂停 / 结束）、失焦自动切、暂停与退出、中文输入法。
- GH 从 `src/Moyu/bin/Release/net7.0-windows` 加载插件，`.gha` 被正在运行的 Rhino 锁住，改代码后要退出 Rhino 再编译再开。退出用 `run_command "_-Exit _No"`；不要用 `open_application` 唤起 Rhino（会多开一个安全模式实例）。
- 用户在用电脑时别抢焦点：computer-use 发的按键会落到前台窗口（可能是用户正在打字的 Claude）。无人值守时可以在 Rhino 进程里用后台线程 `SendInput` 发真实按键，每次发之前确认前台窗口属于 Rhino，不是就跳过。
- 截图用 `view.CaptureToBitmap` 存到 scratchpad 再看（会画出 conduit），比 `get_viewport_image` 稳。
- 成品实拍截图放进 `docs/images/`。

## 4. 图标

- 先用脚本画临时图标，让面板能用。
- 给用户一份图标提示词，格式照 `docs/icon-prompts-massing.md`：
  1. 一段**游戏背景**（这是什么游戏、老板键、面板为什么伪装成建筑工具）；
  2. 每个组件**只写作用**，不写图标设计（设计要求用户在 Codex 那边另给）；
  3. 一行输出排版（几列几行、哪格留空、透明背景），切图脚本靠它。
- 用户用 Codex 生成后：在 `tools/make_icons.py` 里加一个 set → 切图 → 编译 → `--icons` 检查 → 在 GH 界面里看一眼 → 在提示词文档末尾写"接入状态"。

## 5. 文档与交付

- README 加一节（组件表、操作、老板键、测试文件），方案文档补 "v1 状态"，网页重新发布。
- 只有用户要求时才提交 / 推送。

## 6. 计时（每个游戏做完都要）

1. 导出会话时间线：

   ```bash
   python tools/dev_time.py --detail -o dev_time_timeline.txt
   ```

   它读本项目的 Claude Code 会话记录，列出每一轮（用户发请求 → Claude 这一轮最后一条回复）的起止时间、时长和用到的工具。
2. 按时间线把每一轮切成阶段（方案 / 开发到 v1 / 实机测试 / 后续），并标出要**剔除**的段：Rhino 本身或第三方插件的故障（启动崩溃、第三方库崩溃、自动化连接卡死）。我们自己代码的 bug 算开发时间。用户看结果、测试的时间不算；Codex 生成图标的时间不算。
3. 汇总：Claude 实际开发用时、墙钟时间、剔除时间，以及产出（代码行数、测试检查项、工具调用次数、启动 Rhino 次数）。
4. 写进 `docs/dev-time-stats.md`（每个游戏一节）并发布网页（时间轴 + 各阶段柱状图），格式照 VoxStack 那一节。
