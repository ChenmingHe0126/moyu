# Moyu 竖屏介绍短片：文案与分镜

> 2026-11-09。成片 `docs/video/moyu-intro.mp4`：1080×1920、60 fps、66.4 秒，字幕＋配乐，无人声。目标平台：抖音、小红书。
> 英文版 `docs/video/moyu-intro-en.mp4`：同一剪辑，字幕英文，游戏和实机画面用英文界面重录。
> 剪辑工程 `tools/intro-video/edit`（Remotion），素材录制工具 `tools/intro-video/rec`（见其 README 和 SHOTS.md）。

## 一页结论

- **结构**：开场 3 秒就演招牌功能（打砖块多球混战 → 老板键一帧变幕墙立面），再交代"它是什么"（一半拓扑优化、一半摸鱼），然后三个游戏各走一遍"高光玩法 → 一帧变图纸"，雨燕那段补上"再按一次 3-2-1 回游戏"和"Alt+Tab 自动伪装、默认静音"；接着"摸够了，干正事"转到 2D / 3D 拓扑优化，最后 AI 开发、安装、片尾。
- **素材全部新录**：不再裁旧的横屏录屏，而是在 Rhino 8 里用脚本驱动游戏、逐帧离线录制 1080×1920 满屏画面（机器人在玩，高光按剪辑需要准时发生，老板键切换是游戏真实的一帧切换）。只有安装那两镜（Package Manager、Examples 菜单）复用了 11-08 的录屏。
- **节奏**（第一版，已换曲）：配乐 Kevin MacLeod《How it Begins》（CC BY 4.0），恰好 96 BPM；取 120.61–180.61 秒这一段（24 小节，从乐句开头起，自带收尾），开头 3 秒渐入，响度 −16 LUFS。所有剪切点、所有老板键切换都落在拍点上。
- **第四版（同日）**：配乐换成 Kevin MacLeod《Pamgaea》（从三首试听里选定），94 BPM，取第 38–63 小节，−19.5 LUFS；剪切点按 94 BPM 重新对拍。去掉说教的句子，全片改成调侃口吻。另出英文版。
- **第三版（同日）**：加 8 秒实机画面（Rhino 和 Grasshopper 窗口原样出现：GH 里点 ▶ Run → 视口里开玩 → 老板键时视口、命令行、电池输出一起伪装），叠楼段改为只演玩法；字幕上移到 67–78% 高度，避开抖音、小红书底部的标题和简介；配乐改取 115.61–180.61 秒（26 小节）。
- **第二版（同日）**：按反馈放慢节奏、开头渐进。总长 47.5 → 60 秒，每条字幕至少停 2.5 秒、多数 3–5 秒；画面从浅色淡入 0.7 秒，字幕晚 0.4 秒出现；音乐 3 秒渐入、整体从 −14 降到 −16 LUFS。
- **待你决定**：要不要配人声旁白（每镜都写好了口播句）；标题用哪一个；是否上传（B 站按你的默认设置先存草稿）。

## 时间线

| 时间 | 段落 | 画面 | 大字幕 |
|---|---|---|---|
| 0.0–3.2 | 钩子 | Rebound 多球混战，吃到穿透 | 上班时间 / 我在 Rhino 里**打砖块** |
| 3.2–6.4 | 老板键 | 一帧切成 24 层幕墙塔楼立面（含 1:50 典型开间节点、指标卡） | 老板来了？/ **一帧**变成幕墙立面 |
| 6.4–9.6 | 片名 | Moyu 图标 + 名称，背景是虚化的立面 | Moyu 摸鱼 / 一半**拓扑优化** · 一半**摸鱼** |
| 9.6–13.4 | 面板 | Grasshopper 的 Moyu 标签栏推近到 3–6 号面板，逐个对应游戏 | 四个面板 / 名字都很**正经** |
| 13.4–18.5 | 实机 | Rhino 8 + Grasshopper 窗口原样：四视口里是用 Rhino 建模的门楼方块；光标点 Massing Study 的 ▶ Run，视口最大化、3-2-1、开局落块 | 真的在 **Rhino** 里 / Grasshopper 里点 ▶ Run 就开玩；顶部标签：实机画面 · Rhino 8 + Grasshopper |
| 18.5–21.7 | 实机老板键 | 视口一帧变体块研究；命令行打出「MassingStudy: Option B updated · 12F · GFA … · FAR …」；GH 的 Metrics 面板变 Option B（两处珊瑚色框标出） | 老板键一按 / 视口、命令行、电池**一起**伪装 |
| 21.7–26.1 | 叠楼 | 拉近：自定义「门楼」方块被 Gumball 蓝环转 90°，落下消一层；立起的点式方块补洞，DOUBLE FLOOR | 方块能**自己建模** / Gumball 转一下，一次消两层 |
| 26.1–29.4 | 蜿蜒 | 圆环面全景，蛇绕着环吃果子，连击 ×5 | 什么曲面**都能爬** / 圆环、管子、地形、球面 |
| 29.4–31.9 | 蜿蜒 | 起伏地形上的第一人称 | 第一人称视角 / 连吃有连击，最高 ×5 |
| 31.9–35.1 | 老板键 | 一帧变景观平面（等高线、树、观景点、路线 A） | **一帧**变成 景观流线图 |
| 35.1–38.3 | 雨燕 | 小鸟穿过柱廊，一秒一道 | 只用一个键：**空格** / 扇翅膀，穿过柱廊 |
| 38.3–40.9 | 老板键 | 一帧变剖面（采光系数、气流线） | **一帧**变成 采光通风剖面 |
| 40.9–46.0 | 回游戏 | 再按一次，真实的 3-2-1 倒数，回到飞行 | 老板走了？**再按一次** / 3-2-1，回到游戏；小标签：Alt+Tab 切走也会自动伪装 · 默认静音 |
| 46.0–49.8 | 2D 拓扑 | 150×175 像素设计域长成一棵"树"（75 次迭代） | 老板真来检查？/ 那就真算个**拓扑优化**；角标 ×21 倍速 |
| 49.8–54.9 | 3D 拓扑 | 40×40×44 体素的桌子，37 次迭代，最后换成光滑网格 | **3D** 体素也行 / 收敛后直接出光滑网格；角标 ×8 倍速 |
| 54.9–58.1 | 怎么做的 | 代码 / 测试 / 文档 三个勾，计数到 9 小时 48 分 | 说出来你可能不信 / 代码几乎全是 **AI 写的**；1.0.0 版 · Claude Code 开发用时；这期间我主要负责提需求，和摸鱼 |
| 58.1–60.7 | 安装 | Package Manager 搜 moyu | PackageManager / 搜 **moyu**，点 Install |
| 60.7–63.2 | 示例 | Examples 电池的六个案例按钮 | Examples 电池 / 一键打开**现成示例** |
| 63.2–66.4 | 片尾 | 图标、口号、搜索提示、配乐署名 | 祝**摸鱼**愉快 / 老板路过？按 ` 就好；PackageManager 搜 moyu |

## 口播稿（如果要配人声，按镜头对齐）

1. 上班时间，我在 Rhino 里打砖块。
2. 老板来了？一帧，变成幕墙立面。
3. 它叫 Moyu：一半拓扑优化，一半摸鱼。
4. 四个面板，名字都很正经。
5. 这是真的在 Rhino 里：Grasshopper 点 Run，视口里就开玩。老板键一按，视口、命令行、电池一起伪装。
6. Massing 其实是叠楼，方块还能自己在 Rhino 里建模。
7. Landscape 是条蛇，什么曲面都能爬。一按，景观流线图。
8. Section 是只鸟，只用空格。一按，采光通风剖面。老板走了，再按一次，三、二、一。
9. 老板真来检查？那就真算个拓扑优化，2D、3D 都行。
10. 说出来你可能不信，代码几乎全是 AI 写的，九个多小时。这期间我主要负责提需求，和摸鱼。
11. Rhino 8 包管理器搜 moyu，Examples 电池一键开示例。
12. 祝摸鱼愉快。老板路过？按一下 ` 就好。

## 发布文案

- 封面标题：**老板一来，变图纸**
- 标题备选：
  1. 同事在认真画幕墙？其实他在 Rhino 里打砖块
  2. 给 Rhino 装了个老板键：一帧从小游戏变回图纸
  3. 一个 Grasshopper 插件：一半拓扑优化，一半摸鱼
- 简介：Moyu 摸鱼：免费开源的 Grasshopper 插件（Rhino 8 · Windows）。一半是 2D / 3D 拓扑优化，一半是藏在视口里的四个小游戏：叠楼、蜿蜒、回弹、雨燕。按 1 左边的 ` 键，一帧变回正经图纸。安装：Rhino 8 运行 PackageManager，搜 moyu。源码 GitHub：ChenmingHe0126/moyu；food4rhino 搜 Moyu。
  配乐："Pamgaea" Kevin MacLeod (incompetech.com)，Licensed under Creative Commons: By Attribution 4.0 License，https://creativecommons.org/licenses/by/4.0/ ，取第 38–63 小节，做了渐入与响度处理。

## 怎么做出来的

1. **文案与分镜**：三个方向（老板键反转、高光快剪、AI 一句话故事）各写一稿，再由"短视频剪辑"和"天天用 Rhino 的建筑师"两个视角打分。两位都选了"反转"结构，但都认为"高光"稿的开场最强。成片取"反转"的主线、"高光"的开场和片名定义句、"故事"稿的"方块能自己建模""Examples 电池"等说法。评审指出的事实问题全部照改：3-2-1 是真实 3 秒，不加速；拓扑优化标倍速；AI 用时写明"1.0.0 版"。
2. **录制**（`tools/intro-video/rec`）：一个在 Rhino 里加载的小库，接管游戏会话的时钟，每帧固定推进 1/60 秒，用 `CaptureToBitmap` 截 1080×1920，管道送 ffmpeg。每个游戏一个镜头脚本：机器人操作、预设局面（预置积木、放置果子、指定道具）、在指定帧按老板键并把伪装画面按竖屏重新取景。拓扑优化先在后台线程把每一次迭代解出来存好，录制时每帧显示真实的某一步，不做插值。
3. **剪辑**（`tools/intro-video/edit`）：Remotion 按拍点排镜头，字幕、面板标签、老板键键帽动画都在这里加。字幕放在画面 72–85% 高度、左对齐，避开顶部游戏 HUD、右侧 12% 和底部 16% 的手机 App 按钮区。

## 已知局限

- 游戏 HUD 是给横屏设计的：竖屏下蜿蜒、雨燕的老板画面指标卡标题被顶边裁掉一行；蜿蜒升级卡片会压到左侧状态行，所以录制时锁住了等级（不出升级卡）。
- 雨燕的老板剖面图在竖屏里只看得到中间三个开间，左右是裁切。
- 倍速是按本机同时求解两个案例时测的用时算的（2D 52.6 秒 / 75 次，3D 19.9 秒 / 37 次），单独求解会更快，所以"×21""×8"只会偏保守。
- 实机段 Rhino 窗口按 1920 宽缩到手机宽，界面字很小，视口里的游戏也小；它的作用是让人认出 Rhino 界面，看清游戏靠后面的满屏镜头。窗口标题栏（显示许可证类型）裁掉了；光标是后期按 Run 按钮的真实坐标画的；3-2-1 中间剪掉 2 秒。
- 安装两镜是 11-08 的旧录屏（分辨率 1000×1000 区域放大），清晰度不如其他镜头。

## 英文版

同一时间线，字幕全部英文（Bahnschrift 字体），游戏 HUD 和实机段用英文界面重录（同一随机种子，画面与中文版逐帧一致）。

| 段落 | 英文字幕 |
|---|---|
| 钩子 | Work hours. / Playing brick breaker in **Rhino** |
| 老板键 | Boss walking by? / **One frame**: a façade elevation |
| 片名 | Moyu 摸鱼 / half topology optimisation · half slacking off（摸鱼 mōyú: slacking off at work） |
| 面板 | Four panels / with very **serious** names |
| 实机 | Yes, it's really **Rhino** / hit ▶ Run in Grasshopper and play；Boss key: the viewport, / command line and GH **play along** |
| 叠楼 | Model **your own** blocks / a gumball turn, two floors gone |
| 蜿蜒 | Crawls on **any** surface / torus, tube, terrain, sphere；First-person mode / fruit combos up to ×5；**One frame**: a landscape plan |
| 雨燕 | One key: **Space** / flap through the colonnade；**One frame**: a daylight section；Boss gone? **Press again** / 3-2-1, back to the game |
| 拓扑 | Boss checking for real? / Fine. **Topology optimisation**.；**3D** voxels too / converges into a smooth mesh |
| AI | You may not believe this / The code? Mostly **AI**. / I wrote the requests. And slacked off. |
| 安装 | Package Manager / search **moyu**, hit Install；Examples component / opens a **ready-made** example |
| 片尾 | Happy **slacking** / Boss walking by? Press ` |
