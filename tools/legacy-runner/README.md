# M0 Legacy Runner

`M0-RUN-01` 通过一个仅在显式 `--scene res://tools/legacy-runner/legacy_runner.tscn` 时存在的场景驱动旧 gEmuera。普通 `project.godot -> first_window.tscn` 启动链不加载 runner，也不会持久化 `launcher.cfg`。

runner 支持：指定游戏/profile、等待旧 VM 的输入状态、按顺序提交输入、内部与外部双重超时、进程退出码、内存诊断 ring 导出、Godot stdout/stderr/log 收集、诊断 ZIP、canonical 报告以及连续三次语义 hash 比较。显式 runner 的 Snake `emuera_startup_errors.log` 和 legacy 默认 `emuera.log` 都会重定向到本 run 的报告目录，纳入 artifact manifest 与 diagnostics ZIP；后者只重定向默认日志，自定义 `OUTPUTLOG` 路径仍受旧游戏根限制。普通启动保持旧游戏根目录路径。默认每次运行都把游戏复制到独立的 `runtime-game`，捕获 `fixture-mutations.json` 后校验路径并删除副本，因此不会向原始 fixture 写存档、日志或锁文件。只有显式传入 `-AllowGameDirectoryWrites` 才会关闭隔离，不建议用于正式 M0 报告。

## M0 双后端显示基线

`displayBackend=controls|canvas` 只覆盖本次 runner 的内存状态，不保存普通用户设置。`captureScreenshot=true` 时，使用 `Invoke-LegacyRunner.ps1 -UseDisplayServer` 在 `RenderingServer.frame_post_draw` 后采集 `viewport.png`；默认 headless 不伪造像素结果，而在 `screenshots.json` 写 `headless_display_driver=Uncovered`。

`display.json` 保留 requested/effective backend、固定 viewport 请求、实际 viewport/scroll、retained rows/layout/overlay 计数，以及 div/nested-div/src/srcb/dynamic-map/data-only/scroll 的逐项 `Captured|Uncovered`。`hit-test.json` 的每条证据同时记录原 backend rect/value/generation 和矩形中心经现有真实命中入口得到的 probe 结果。截图仅证明像素，不能替代 hit test。

Controls 与 Canvas 必须由独立进程和独立目录归档：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/legacy-runner/Invoke-LegacyDisplayBaseline.ps1 `
  -GodotPath "E:\Godot_v4.7-stable_mono_win64" `
  -GameRoot "<local-game-root>" `
  -Profile v24pure `
  -OutputDirectory "<artifact-output>" `
  -RepeatCount 3
```

该命令验证每份 `display.json` 的 requested/effective backend；不一致时以 `effective_backend_mismatch` 失败。复杂游戏页面没有被输入 replay 到达时必须保留 `Uncovered`，不能用小型样例或另一后端替代。

## 首个等待点基线

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/legacy-runner/Invoke-LegacyRunner.ps1 `
  -GodotPath "E:\Godot_v4.7-stable_mono_win64" `
  -GameRoot "E:\Godot_v4.6.2-stable_mono_win64\snake\EraTW-Magic_DLC-update_base" `
  -Profile snake `
  -OutputDirectory "E:\artifacts\m0-run-erafl" `
  -RepeatCount 3 `
  -TimeoutSeconds 360
```

输入序列在 JSON 的 `inputs` 中配置；每项包含 `value`、`fromButton`、`skip`、`mouseButton` 和 `waitTimeoutMs`。完整字段见 [legacy-runner-config.schema.json](legacy-runner-config.schema.json)。无输入的 `first-wait.json` 会在旧 VM 第一次进入输入等待后，等待 `settleFrames` 个连续不变的语义快照再采集；快照包含控制台文本 hash、trace 进度、输入状态与只读的旧显示投影计数，不能以固定帧数截断仍在排队的显示事务。

大型游戏的逐文件身份采集可能受冷盘和安全扫描影响。已经归档且 source/game/Godot identity 均未变化时，可传 `-ExistingIdentityDirectory <目录>` 复用清单；harness 会核对项目根、游戏根和 Godot executable hash。源码发生任何变化后必须重新生成身份，不能复用旧报告。

## 验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/legacy-runner/Test-LegacyRunner.ps1 `
  -GodotPath "E:\Godot_v4.7-stable_mono_win64" `
  -GameRoot "E:\Godot_v4.6.2-stable_mono_win64\snake\EraTW-Magic_DLC-update_base"
```

`display.raw.json` 保留旧 Console 的逐字输出；用于比较的 `display.json` 当前只应用具名规则 `legacy_dictionary_load_elapsed_ms`，归一化游戏主动打印的字典加载耗时。不得用宽泛正则删除其它文本或顺序差异。

## Typed trace

runner 配置默认显式启用 `traceEnabled`，容量由 `maxTraceEvents` 限制。普通 `project.godot -> first_window.tscn` 启动链不创建 recorder。`trace.raw.json` 保留观察时间、绝对路径、session 与原始 payload；`trace.json` 只应用 `observer_timestamps`、`absolute_paths`、`session_identifiers` 三条具名规则。两份文件必须具有相同事件数、相同顺序和相同 `sequence`，容量溢出会令 runner 非零退出，不能静默丢弃。

`randomSeed` 默认固定为 `20260712`，仅在 runner 场景中注入 `VariableEvaluator`，正常启动不会改变时钟随机数语义。`semantic-trace.json` 是重复运行比较专用投影：它保留脚本可观察事件的相对顺序并重新编号，只排除已显式标为 `ui_projection` 的 Godot 队列批次。原始和 canonical trace 仍完整保留这些批次；逻辑 `display_commit` 绝不被排除。这样不会把 UI 调度抖动伪装成脚本差异，也不会丢失诊断证据。

`timeline.json`、`effects.json`、`errors.json.traceErrors` 是 `semantic-trace.json` 的分类投影，不再拥有独立顺序计数器。`summary.json` 同时报告 `semanticReportsConsistent` 与 `transportTraceReportsConsistent`；后者为 false 时 raw transport 仍是必须保留的诊断，不会替代前者的脚本语义门。可先运行不依赖游戏的契约测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/legacy-runner/Test-LegacyTrace.ps1 `
  -ProjectRoot "E:\MyCode\GodotCode\gEmuera-future"
```

`summary.json.result=PassedRunnerConsistency` 只表示 runner 本次要求的 canonical 语义报告一致。M0-TRC-01 已建立 schema、recorder 与本地 EraTW 首等待证据；但 Controls/Canvas 显示、APK/真机、upstream/v24/更多游戏 fixture、报告归档和人工签署仍未完成，阶段状态继续保持 `InProgress + Blocked/EvidenceMissing`，不得据此进入 M1。

## M1 runner-only session-isolation canary

`sessionIsolationMode` 是 runner JSON 的可选字段，值只能为 `baseline`（默认）或 `canary`。它只在显式 `legacy_runner.tscn` 中、`main.tscn` 创建前写入已加载的 diagnostics snapshot；普通 `project.godot -> first_window.tscn` 启动不会读取这个字段，仓库 `config.toml` 的默认 `migration.session_isolation=false` 也不会被改写。

可使用以下比较器生成 baseline → canary → baseline 的三个独立进程报告：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/legacy-runner/Invoke-SessionIsolationCanaryBaseline.ps1 `
  -GodotPath "E:\Godot_v4.7-stable_mono_win64" `
  -GameRoot "<local-v24-game-root>" `
  -Profile v24pure `
  -OutputDirectory "<empty-artifact-output>" `
  -RepeatCount 3
```

每段都通过原 `Invoke-LegacyRunner.ps1` 的隔离游戏副本运行。比较器要求每段内部的 semantic report 一致、三段的 hash 相同，并断言 `M1_SESSION_ISOLATION_CANARY` 只出现在 canary 段的诊断中。`trace.json` 的 UI projection transport 抖动仍作为独立诊断保留。

这只是独立进程的 startup/exit 回退比较，**不是**真实同进程 A/B/A 切换、`GlobalStatic` 完整隔离、Android 证据或 M1 放行。报告会固定保持 `InProgress / Blocked / PreviousGate:M0`。

### 同一游戏的 runner-only 同进程 ABA restart

`inProcessSessionCycle=aba` 只能与 `sessionIsolationMode=canary` 且空 `inputs` 一起用于显式 runner。它在三次稳定首等待之间调用两次 internal `EmueraMain.RestartLegacySessionForLegacyRunnerAsync()`，由 `LegacySessionFacade` 完成 stop/start/commit，随后要求三次逻辑 fingerprint 完全相同。普通启动和 UI 不会调用这个入口。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/legacy-runner/Invoke-SessionIsolationInProcessAba.ps1 `
  -GodotPath "E:\Godot_v4.7-stable_mono_win64" `
  -GameRoot "<local-v24-game-root>" `
  -OutputDirectory "<empty-output-directory>" `
  -RepeatCount 3
```

该比较器验证两次 commit（generation 2/3）、三个 fingerprint、每个隔离副本的 `changeCount=0` 和 repeat semantic hash，并在输出根写 `M1-RUN-INPROCESS-ABA-01` summary。Same configured game restart only：它不覆盖不同 game/profile、输入中切换、晚到 completion、100 次泄漏、Android、Parser/VM handler/typed-policy behavior 或 M1 放行；descriptor registry presence guard 已由 Parser 初始化边界覆盖。

### 跨游戏/profile 的 runner-only A→B→A

`inProcessSessionCycle=cross-aba` 同样只允许 canary 和空 `inputs`，但会在 `main.tscn` 创建前将 A/B 的 `(gameId, profileId)` 冻结为 Godot host route allowlist。Core 只接收 opaque id/profile，游戏根目录只在 host 内解析。比较器要求 generation 2/3、A 的第 1/3 个 fingerprint 相同、两份隔离副本均为 0 mutation；B 的 fingerprint 不要求与 A 相同。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/legacy-runner/Invoke-SessionIsolationInProcessCrossAba.ps1 `
  -GodotPath "E:\Godot_v4.7-stable_mono_win64" `
  -GameRoot "<A game root>" `
  -Profile v24pure `
  -AlternateGameRoot "<B game root>" `
  -AlternateProfile snake `
  -OutputDirectory "<empty-output-directory>" `
  -RepeatCount 1 `
  -UseDisplayServer
```

输出为 `M1-RUN-INPROCESS-CROSS-ABA-01`；它是首等待、无输入的局部隔离观察，不覆盖所有 static root、输入中切换、晚到 completion、100 次泄漏、Android、Parser/VM handler/typed-policy behavior 或 M1 gate。默认 `migration.session_isolation=false` 和普通启动链不变。

默认 harness 只构建已准备好的 `gemuera-c#.sln`，并显式使用 `--no-restore`，避免 M0 回放在受限网络中临时改写 Godot SDK 资产；首次准备 C# 资产应由 Godot 4.7 Mono editor 完成。若构建已由独立步骤验证，可对 `Test-LegacyRunner.ps1` 传入 `-SkipBuild`，此时它仍完整验证隔离回放、报告和 `semantic-trace.json`，但不替代独立 build 结论。
