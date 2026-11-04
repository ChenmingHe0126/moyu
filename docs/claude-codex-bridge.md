# Claude Code + Codex：能不能一起干活

> 网页版调研报告的源文件，2026-11-02。网页里每个方案都配了示意图。

## 一页结论

- **有现成的，不用另写"路由器"。** 你电脑上的 Codex 桌面版自带命令行 `codex.exe`（0.166，在 `%LOCALAPPDATA%\OpenAI\Codex\bin\<版本号>\`），和桌面版共用登录，`image_generation` 开着；你刚生成的 Landscape 图标也在 `~/.codex/generated_images/` 里。Claude Code 现在就能直接调 `codex exec` 让 Codex 干活，包括生图。
- **生图**：方案 A（Claude 直接调 `codex exec`），或现成插件 [codex-image-in-cc](https://github.com/KingGyuSuh/codex-image-in-cc)。
- **协作**：OpenAI 官方的 Claude Code 插件 [codex-plugin-cc](https://github.com/openai/codex-plugin-cc)，提供 `/codex:review`、`/codex:adversarial-review`、`/codex:rescue`（转交任务）和可选的评审闸门。
- **并行**：[PAL MCP](https://github.com/beehiveinnovations/zen-mcp-server) 的 clink（一个 CLI 把另一个 CLI 当子代理），或 Vibe Kanban 这类看板编排器（每个代理一个 git worktree）。
- **建议**：先做方案 E（一个专用图标技能，基于 A），再装官方插件 B，用来让 Codex 评审 Claude 写的代码。

## 对比

| 方案 | 让 Codex 生图 | 互相评审 | 转交任务 | 多个代理并行 | Windows | 上手成本 |
|---|---|---|---|---|---|---|
| A 直接调 codex exec | 擅长 | 能做 | 能做 | — | 已核实 | 低 |
| B 官方插件 codex-plugin-cc | — | 擅长 | 擅长 | — | 没写明 | 低 |
| A+ codex-image-in-cc | 擅长 | — | — | — | 没写明 | 低 |
| C PAL MCP · clink | 能做 | 擅长 | 擅长 | 能做 | 没写明 | 中 |
| D Vibe Kanban 等编排器 | — | 能做 | 能做 | 擅长 | 有（Claude Squad 要 WSL，Conductor 只有 macOS） | 高 |
| E 自己做专用技能 | 擅长 | — | — | — | 已核实 | 中 |

## 各方案要点与坑

- **A**：`codex exec` 能附参考图（`-i`）、限定沙盒（`-s workspace-write`）、把最后回复写到文件（`-o`）。生成的图在 `~/.codex/generated_images/`，要自己复制出来（社区摸出来的行为，官方没写进文档）。`codex.exe` 不在 PATH 里，Codex 更新后文件夹名会变。每次调用算 Codex 额度，提示词和参考图会发到 OpenAI。
- **B**：2026 年 3 月底发布，MIT 协议。它走本机的 Codex 命令和 app-server。安装：`/plugin marketplace add openai/codex-plugin-cc` → `/plugin install codex@openai-codex` → `/codex:setup`；需要 Node.js 18.18+，`codex` 要在 PATH 里。说明里没提生图；评审闸门很耗额度。
- **C**：开源，装好后 Claude Code、Codex、Gemini CLI 互相都能调，子代理各开新上下文、只交回结论；配置比 B 多。
- **D**：适合一次推进好几件独立的事；对"一个人做一个小游戏"的节奏偏重。有一处资料说 Vibe Kanban 背后的公司已关、改由社区维护，未能找到第二个来源证实。
- **E**：一个 Claude Code 技能 + 小脚本：读仓库里的生成记录和风格参考图，为新组件写提示词，找到最新的 `codex.exe` 调用，复制出图，切图、编译、跑 `--icons`，写接入状态。十几分钟能做完。不写通用路由器，是因为 B、C、D 已经覆盖了协作和并行。

## 需要你决定的

1. 要不要我做方案 E，并用它把你刚生成的 Landscape 图标切图接入，当作第一次实测。
2. 要不要装官方插件 B（需要把 `codex` 放进 PATH；装软件、改 PATH 前我会先问你）。

## 来源

- [openai/codex-plugin-cc](https://github.com/openai/codex-plugin-cc) · [The Decoder 报道](https://the-decoder.com/openai-launches-a-codex-plugin-that-runs-inside-anthropics-claude-code/)
- [codex-image-in-cc](https://github.com/KingGyuSuh/codex-image-in-cc) · [Codex CLI 生图说明](https://codex.danielvaughan.com/2026/04/27/codex-cli-image-generation-gpt-image-2-visual-development-workflows/) · [codex-image](https://github.com/tksuns12/codex-image)
- [PAL MCP（原 Zen MCP）](https://github.com/beehiveinnovations/zen-mcp-server)
- [Vibe Kanban](https://vibecodinghub.org/tools/vibe-kanban) · [多 CLI 并行编排综述](https://codex.danielvaughan.com/2026/04/11/agentmaxxing-parallel-multi-cli-orchestration/) · [awesome-claude-code](https://github.com/jqueryscript/awesome-claude-code)
