# Moyu 1.0.0 发布准备报告

> 网页版报告的源文件（2026-11-04）。

## 结论

- **审查**：11 个方向各一个审查代理，每条发现由三个独立核查者判（反驳 / 是否本意 / 严重度），批评者又补了 8 个探查。78 条核查，确认 70 条（高 7、中 29、低 34），驳回 8 条，另记录 134 条小问题。
- **修复**：11 个修复代理，各在独立 git worktree 里改自己那组文件；合并后三个目标框架 0 警告，核心测试 351 项全过（原来 306 项）。
- **图标**：Codex 画的体素鱼（候选 A），24 px 页签、256 px 包图标、512 px food4rhino 图标；无界面图标检查通过。
- **发布包**：`dist/moyu-1.0.0-rh8_0-win.yak`（560,922 B）、`dist/Moyu-1.0.0-food4rhino.zip`（1,102,471 B），MIT、作者 Ming、1.0.0。

## 7 个高严重度问题

| 问题 | 改成 |
|---|---|
| 运行中调高 Volume Fraction，优化器二分到 0，密度全变 NaN，之后一直报"不稳定"直到 Reset | 二分最多 120 次、密度下限 1e-9；中途调高会每步长一点直到目标；加了回归测试 |
| Filter Radius 开到 20 时邻居表要好几 GB，int 还会溢出 | 2D 最大 12、3D 最大 6；表超过 1 亿项直接报清楚的错 |
| 插件信息作者、联系方式是空的，描述还写着"只有 2D" | Ming、GitHub 地址、MIT、描述覆盖 3D 和四个面板 |
| 游戏运行时弹出另存为对话框或切到 GH 窗口，游戏照样在后台跑 | 别的窗口拿到键盘或 Rhino 命令开始时游戏自动暂停，倒计时停住 |
| 游戏视口里右键单击被 Rhino 当成回车，重复上一个命令 | 右键和左键一样只转视角，中键不弹工具栏 |
| Steps 没有上限，求解循环在界面线程上，一次能卡 Rhino 好几分钟 | Steps 最多 50，每次最多跑约 1.5 秒，按 Esc 马上停 |
| 切换 GH 文档后 Airflow Study 的输出再也不刷新 | 和另外三个研究组件一样在加回文档时复位 |

## 发布包

| 文件 | 大小 | SHA-256 |
|---|---|---|
| `moyu-1.0.0-rh8_0-win.yak` | 560,922 B | `fb9c5ed467ba8e46ed4a7f95d5442a705dad2b2398bf5757d660d09a933ac219` |
| `Moyu-1.0.0-food4rhino.zip` | 1,102,471 B | `7f2b95f77ee25727d72af99740062704820208741d9c508097bfcfcc92b63925` |

.yak 内容：`manifest.yml`、`icon.png`、`LICENSE.txt`、`net48/Moyu.gha`、`net7.0/Moyu.gha`。重新打包：`python tools/package.py`。

## 验证

- Release 编译 0 警告（net48、net7.0、net7.0-windows）；核心测试 351 项全过；无界面图标检查 23 个组件 + 页签通过。
- 第一轮修复后的构建在 Rhino 8 里跑过（插件信息、VoxStack 开局 / 老板键 / 退出、4K 界面缩放与提示换行）。
- 第二轮修复还没在 Rhino 里用真实按键测（Rhino 开着，占着插件文件）。

## 待办

1. 关 Rhino 后重新编译开发目录并实测第二轮改动。
2. `release-prep` 合并到 main 并推送。
3. `Yak.exe login` + `Yak.exe push dist\moyu-1.0.0-rh8_0-win.yak`（你的账号）。
4. food4rhino：Create App from Yak（moyu），按 `packaging/food4rhino/listing.md` 填写，支持邮箱必填。

## 用时

约 92 分钟 Claude 实际工作（不含等你回答图标问题的时间）；审查和修复的代理并行在后台跑。
