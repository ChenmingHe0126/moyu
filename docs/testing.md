# 测试排查（开发用）

这页只和开发检出（dev checkout）有关：跑 `tests/Moyu.RhinoTests` 需要本机装有已授权的 Rhino 8 和 Release 构建的插件。只是用 `.gha` 的人不需要看。测试命令本身见 README 的"测试"一节。

## 排查 Rhino 崩溃

Rhino.Inside 测试会加载你在 Grasshopper 里配置的全部库目录。若其中有第三方插件找不到自己的原生库，它的终结器会在某次 GC 时把整个进程杀掉——Rhino 里和测试里都一样。`--probe-gc` 只启动 Grasshopper、打印可疑程序集（Millipede / DeadFish）的加载位置，再强制完整 GC；加 `--with-moyu` 会先打开并跑完测试文件：

```bash
dotnet run -c Release --project tests/Moyu.RhinoTests -- --probe-gc --with-moyu
```

第三方问题修好之前，可以用环境变量 `MOYU_TEST_PRELOAD`（分号分隔的原生 DLL 完整路径）让测试进程先加载这些库，例如 `<那个插件的原生 dll 完整路径>;<第二个 dll>`。

## 真实按键

游戏的键盘钩子只认真实按键：`PostMessage` 注入的按键不经过钩子，测不出东西，还会漏进 Rhino 命令行。实机测试的做法见 [contributing.md](contributing.md) 和 [game-workflow.md](game-workflow.md) 的"实机测试"。
