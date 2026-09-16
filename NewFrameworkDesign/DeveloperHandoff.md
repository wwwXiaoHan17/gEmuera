# 当前实现与开发接手指南

> **历史快照声明**：本文档是 2026-07-17 的交接快照，仅供历史参考。权威入口为根目录 `AGENTS.md`（项目规则）与 `ERBAPI.md`（解释器扩展）。
>
> 已知过时要点（正文中相关表述以本声明为准）：
>
> - `tests/GEmuera.Core.Tests` xUnit 测试工程已存在（20 个测试，2026-08-15 起）。
> - `src/Core` 目标框架实为 `net8.0;net9.0`（非 `net10.0`）。
> - `tools/m3-m7` 已于 2026-08-07 重命名并入 `tools/governance/`，现 `tools/` 有 11 个工具目录（dialect-inventory、legacy-runner、save-baseline、core-contracts、governance 等）。
> - `CODE_MAP.md`、`IDEAS.md` 已删除。
> - M0-M7 阶段代号已全部清除并禁止使用（M0→LegacyRunner、M6→Experiment、M7→Governance）。

> 快照基线：GitHub `dev` 分支提交 `8e67d392a0c5d3b463d0572f222e4a6ad210a3f4`，2026-07-17。

本文只回答当前项目**已经具备什么、由谁负责、从哪里继续开发**。它不是更新日志，也不以“文件或类型已经存在”推导“迁移阶段已经完成”。阶段放行状态仍以 M0M2ImplementationBaseline（已于 2026-08-07 删除，内容见 git 历史）、KnownLimitations（已于 2026-08-07 删除，内容见 git 历史）和机器报告为准。

## 接手结论

| 维度 | 当前已经具备 | 接手时必须保留的边界 |
| --- | --- | --- |
| 可运行主路径 | Godot 启动器、旧 Emuera Parser/VM、双线程执行、现有显示/输入/资源/存档兼容路径 | 默认行为 owner 仍是 legacy；不能因 `src/Core` 存在而删除或绕过旧路径 |
| Godot 工程 | Godot 4.7 Mono、桌面 `net8.0`、Android 条件目标 `net9.0`、Mobile renderer、启动器和主场景 | Android 可构建声明不等于当前 APK/真机门禁通过 |
| Core 合同程序集 | 独立 `GEmuera.Core`，含兼容计划、会话、解析、变量、显示 DTO、资源、存档、typed ports、实验和治理合同 | 多数类型是合同或 prototype；旧 Parser/VM/View 仍未整体迁移为这些实现 |
| 会话迁移 | `LegacySessionFacade`、generation/operation guard、host route、runner-only canary 和 A/B/A 观察入口 | M1 仍为 `InProgress / Blocked`；不能作为已批准发布路径 |
| Godot prototype sidecar | Core prototype session、Reload/Toggle/Detach、状态面板、input/audio/resource bridge | sidecar 观察和验证候选 Core，不替代 legacy 游戏循环 |
| ERB 扩展治理 | 指令/函数/变量/方言扩展入口、TDD 要求和禁止边界已有统一指南 | 先按 [ERBAPI](../ERBAPI.md) 选择 owner；不得把静态 DIA 报告当成运行时插件 API |
| 证据工具 | identity、fixture manifest、legacy runner、typed trace、显示/存档/会话库存、DIA-01 至 DIA-17、文档和架构守卫 | 报告有 `Partial/Uncovered/Blocked` 时必须保留，不能用编译通过覆盖 |
| 后续 M3-M7 | 对应合同类型、smoke、治理 schema 和目标设计均已存在 | 阶段本身没有获得实施/发布授权；当前默认 feature route 仍为 legacy |

## 当前运行组成

```text
project.godot
  -> first_window.tscn / FirstWindow
     -> 选择游戏目录与 v24pure/snake profile
     -> main.tscn / EmueraMain
        -> EmueraThread -> Program -> legacy Parser/VM -> EmueraConsole/View
        -> PrototypeRuntimeNode -> CoreApplicationRuntime (sidecar)
           -> PrototypeInputBridge
           -> PrototypeAudioBridge
           -> PrototypeResourceBridge
           -> PrototypeStatusView / PrototypeCommandPanel
```

两个分支同时存在，但职责不同：

- `EmueraMain -> EmueraThread -> Program` 是玩家实际运行游戏的默认路径。
- `PrototypeRuntimeNode -> CoreApplicationRuntime` 是候选 Core 的同场景 sidecar，用于验证 session、plan、bridge 和生命周期合同。
- `AppBootstrap` 与 `PlatformGateway` 是 application-scoped Godot Autoload，只持 feature/capability 和平台生命周期信息，不应持有游戏业务状态。
- `project.godot` 中的 `prototype_runtime`、`typed_ports`、`pixel_store` 当前会被 bootstrap/prototype 读取；它们不等同于 `m3-m7-current-status.json` 中默认关闭的生产迁移 feature route。

## 已有的可运行引擎能力

现有 legacy 引擎已承载 `.ERB`/`.CSV` 加载、表达式和变量、指令执行、存档、HTML/图片/形状、按钮输入、声音、SQLite/Map/XML/DataTable 扩展以及 v24/Snake 兼容分支。实际公开功能概览见 [项目 README](../readme/README.md)，代码定位以 CODE_MAP（已于 2026-07-25 提交 d24f656 删除，内容见 git 历史）为准。

主要 owner 如下：

| 能力 | 当前行为 owner | 首要入口 |
| --- | --- | --- |
| 启动器与游戏目录 | Godot launcher | `FirstWindow.cs`、`first_window.tscn` |
| 进程启动和 profile 选择 | legacy bootstrap | `Program.cs`、`GlobalStatic.cs` |
| ERB 加载、label、执行循环 | legacy GameProc | `ErbLoader.cs`、`LogicalLineParser.cs`、`Process*.cs` |
| 表达式、函数和变量 | legacy GameData | `ExpressionParser.cs`、`Creator.Method*.cs`、`Variable*.cs` |
| 控制台显示语义 | legacy GameView | `EmueraConsole*.cs`、`ConsoleDisplayLine.cs`、`HtmlManager.cs` |
| Godot 显示投影 | Godot host/View | `EmueraContent*.cs`；`ConsoleRenderSurface` 定义在 `EmueraContent.Canvas.cs` |
| 资源和图片 | legacy resource path | `AppContents.cs`、`SpriteManager.cs`、`GraphicsImage.cs` |
| 输入唤醒 | Godot + legacy thread bridge | `EmueraContent.cs`、`EmueraThread.cs`、`EmueraConsole.PressEnterKey` |
| 存档协议 | legacy codec | `EraDataStream.cs`、`EraBinaryDataReader.cs`、`EraBinaryDataWriter.cs` |

这些 owner 在对应迁移 gate 通过前都是兼容真相。新代码可以观察、复制 DTO 或建立 candidate，但不能提前改变它们的脚本可观察语义。

## 已有的 Core 合同与原型

[`src/Core/GEmuera.Core.csproj`](../src/Core/GEmuera.Core.csproj) 是不引用 GodotSharp 的独立程序集，面向 Godot host 使用 `net8.0`，本地合同验证还提供 `net10.0`。当前目录已经具备以下边界：

| 目录 | 已有能力 | 当前地位 |
| --- | --- | --- |
| `Application` | `GameSession`、`CoreApplicationRuntime` 组合根 | prototype session，可被 Godot sidecar 使用 |
| `Compatibility` | module/profile catalog、冻结 plan/snapshot、descriptor route、plan consumer | 可构建合同和窄消费边界；不是完整内容 resolver 或动态插件系统 |
| `Session` | generation、candidate、lease、短 commit gate、`LegacySessionFacade` | M1 事务外壳已存在，完整旧状态隔离未关闭 |
| `Parsing` | 基础 ERB 行解析、diagnostic/source span DTO | M3 prototype，不是 legacy Parser 的生产替代 |
| `State` | typed variable scope/value、candidate snapshot | M3 prototype，不是全部 legacy 变量语义迁移 |
| `Display` | line/part/div/style/interaction/barrier/transaction DTO 与 tee | M2/M3 合同库存；默认 renderer 未改走 DTO |
| `Resources` | PixelSurface/PixelStore、catalog、budget、generation/revision ledger | CPU truth 和合同原型；未成为生产图片 owner |
| `Save` | deterministic codec、candidate、atomic blob store | 合同原型；未绑定真实游戏 SaveProfile 和完整 round-trip |
| `Ports` | manifest、adapter scope、completion dispatcher、input/storage/database/audio/lifecycle 合同 | typed port 合同库存；平台生产 adapter 尚未整体切换 |
| `Runtime` | interpreter catalog/descriptor、Step/Resume host、legacy adapter | M3/M6 合同和实验入口；不是新 VM 已发布 |
| `Experiments` | scheduler、cooperative runner、yield audit、metric/identity 合同 | 默认关闭实验能力 |
| `Governance` | runtime lease、removal packet、release/rollback 合同 | M7 治理库存，不表示旧路径可以删除 |

`tools/m3-m7/m3-m7-current-status.json` 将这些内容描述为 `implementationInventory`：合同实现存在，但总状态仍是 `InProgress / Blocked / EvidenceMissing`，`defaultRoute=legacy`。

## 已有的 Godot host 边界

[`main.tscn`](../main.tscn) 已挂载六个 prototype 节点：

- `PrototypeRuntimeNode`：拥有 Core prototype session，串行处理 Reload、v24pure/Snake Toggle 与 Detach。
- `PrototypeInputBridge`：默认观察 `_UnhandledInput` 并入队，但 `ConsumeUnhandledInput=false`，不会阻止后续未处理输入消费者。
- `PrototypeAudioBridge`：有界音频命令队列和 voice pool，只接受当前 attached generation。
- `PrototypeResourceBridge`：把不可变像素快照投影为 Godot texture，拒绝 detached、旧 generation 和旧 revision。
- `PrototypeStatusView`：只读显示 runtime 状态。
- `PrototypeCommandPanel`：只发出用户意图，不拥有 session 或切换逻辑。

切换时 input 先停止接收，audio/resource 随旧 generation 失效；新 session commit 后 resource、audio、input 再挂到新 generation。退出时 runtime 先取消并失效 bridge，再 drain 活动命令，最后释放 Core runtime，避免晚到 completion 写回已退出节点。

Godot host 还具有：

- `EmueraStartupComponent`：解析游戏目录和编码映射，不自行重置 session。
- `EmueraLifecycleComponent`：处理移动端暂停/恢复和帧率策略。
- `EmueraGpuRenderComponent`、`EmueraTextRenderComponent`：拥有主线程离屏渲染节点和后台请求队列。
- `LegacySessionBackend`、`LegacySessionLaunchRegistry`：为默认关闭的 M1 canary 提供 allowlisted host route 和旧会话启动/停止边界。
- `PlatformGateway`：报告平台 capability；其中 `platform.saf=true` 只是 Android capability 声明，不代表 SAF adapter、撤权恢复或真机证据已经存在。

## 已有的 ERB 魔改入口

[`ERBAPI.md`](../ERBAPI.md) 是新增或修改 ERB 语法的统一入口，已经区分：

- 普通指令、特殊参数指令、表达式函数和变量各自的注册与执行 owner。
- v24/Snake profile、方言 module、descriptor 和 legacy handler 之间的边界。
- `_Rename.csv` source rewrite 与 registry alias/replacement 的区别。
- DLL 插件的桌面完全信任属性及移动端限制。
- RED/GREEN 测试、兼容矩阵、生成报告和回退要求。

接手 ERB 工作时，不要从 Core descriptor 名称直接生成 handler。当前 legacy handler 仍是行为 owner；DIA 报告主要提供静态库存、来源和未来接口候选。

当前 DIA 机器基线记录过 326 个指令和 360 个表达式函数，但 Godot host profile marker `profile.selected-name` 尚未在 `dialect-classification.json` 完成 owner 裁决。DIA-01 inventory 与依赖它的 registry snapshot 因此会 fail-fast；旧生成报告中的数量只能作为上一份成功基线，不能写成当前绿色证据。

## 已有的验证与证据工具

| 工具目录 | 已有用途 |
| --- | --- |
| [`baseline-identity`](../tools/baseline-identity) | 固定源码、工具链、游戏和 artifact 身份 |
| [`fixture-manifest`](../tools/fixture-manifest) | 管理 upstream/legacy/game fixture 来源、授权和缺失状态 |
| [`legacy-runner`](../tools/legacy-runner) | 隔离复制游戏、输入 replay、typed trace、报告、A/B/A 与显示基线 |
| [`save-baseline`](../tools/save-baseline) | 静态存档协议、fixture audit 和 round-trip evidence 工具 |
| [`session-state-inventory`](../tools/session-state-inventory) | 固定 `GlobalStatic`/`Program` 会话根状态 |
| [`dialect-inventory`](../tools/dialect-inventory) | DIA-01 至 DIA-17 静态库存、解析、归属、组合和 fixture contract |
| [`core-contracts`](../tools/core-contracts) | Core smoke 与 Core -> Godot 反向依赖守卫 |
| `m3-m7`（2026-08-07 重命名并入 `tools/governance/`，含 work-package schema 等，见 git 历史） | implementation inventory、work-package schema 和治理合同测试 |
| [`doc-guards`](../tools/doc-guards) | 文档结构、链接和稳定条款门禁 |

真实 Era 游戏字节、APK、大报告和设备日志不属于仓库源码。工具可以生成报告，但没有绑定 fixture、artifact hash、设备和签署时，结果必须保持 `Uncovered` 或 `Blocked`。

## 当前测试入口

### Godot/C# 快速门禁

```powershell
dotnet build gemuera-c#.sln -c Debug --no-restore

& '<Godot 4.7 Mono>\Godot_v4.7-stable_mono_win64_console.exe' `
  --headless --editor --path . --build-solutions --quit
```

### Prototype host 回归

```powershell
& '<Godot 4.7 Mono>\Godot_v4.7-stable_mono_win64_console.exe' `
  --headless --path . `
  -s res://addons/gdUnit4/bin/GdUnitCmdTool.gd `
  --ignoreHeadlessMode `
  -a test/GodotHost/PrototypeHostRegressionTest.gd `
  -c -rd user://gdunit-review-report
```

该套件当前覆盖 Reload、Toggle、Detach 和 observational input 不消费事件。仓库已包含 gdUnit4 6.1.3 的 CLI 源文件及 `.gitignore` 反忽略规则。

### Core 和文档

```powershell
dotnet build src\Core\GEmuera.Core.csproj -c Release --no-restore
dotnet build tools\core-contracts\CoreContractSmoke.csproj -c Release --no-restore
dotnet run --project tools\core-contracts\CoreContractSmoke.csproj -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\core-contracts\Test-CoreArchitecture.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\doc-guards\Invoke-DocGuard.ps1
```

当前没有正式的 `tests/Core/*.Tests.csproj` xUnit 测试工程。不要在交付说明中声称已经运行不存在的 xUnit suite。（已过时，见顶部声明）

### 当前已知非绿色门禁

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectInventory.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectRegistrySnapshot.ps1
```

预期阻断为 `profile.selected-name` 未分类。关闭它需要先由 owner 决定 current owner、intended owner、target module/BehaviorKey/CapabilityId 和 fixture，不能只为让测试变绿而随意填 catalog。

## 开发任务从哪里开始

| 要继续的工作 | 首先阅读 | 首先修改/检查 | 最小反馈 |
| --- | --- | --- | --- |
| ERB 指令/函数/变量 | [ERBAPI](../ERBAPI.md) | `FunctionIdentifier.cs`、`ArgumentBuilder.cs`、`Creator.Method*.cs`、变量 descriptor | 对应 RED 测试、build、DIA inventory |
| 方言/profile | DialectExtensionSystem（已于 2026-08-07 删除，内容见 git 历史） | `dialect-classification.json`、Compatibility catalog、plan consumer | DIA 对应 contract，未选择模块不变性 |
| legacy 会话隔离 | M1CoreRuntimeContractSlice（已于 2026-08-07 删除，内容见 git 历史） | `LegacySessionFacade.cs`、`LegacySessionBackend.cs`、static root inventory | Core smoke、A/B/A、memory/rollback |
| Prototype host | 本文 Godot host 章节 | `Scripts/GodotHost/Prototype*.cs`、`main.tscn` | `PrototypeHostRegressionTest.gd` |
| 显示 DTO/M2 | M0M2ImplementationBaseline | legacy display inventory、`DisplayDtos.cs`、tee | tree/timeline golden；默认 renderer 不切换 |
| 资源/PixelStore | M4ResourceGraphics（已于 2026-08-07 删除，内容见 git 历史） | `src/Core/Resources`、resource bridge ledger | pixel diff、generation/revision、Node/RID ledger |
| 输入/平台/SAF | M5PlatformComposition（已于 2026-08-07 删除，内容见 git 历史） | input contracts、PlatformGateway、旧 Android path | input trace、撤权恢复、真机和旧路径回退 |
| 存档 | SaveFormat（已于 2026-08-07 删除，内容见 git 历史） | legacy codec、`src/Core/Save`、save-baseline | profile 明确、只读原件、隔离 round-trip |
| 构建/Android | HowToRun（已于 2026-08-07 删除，内容见 git 历史） | csproj、export preset、ToolchainLock 候选 | export/APK hash、安装启动和设备报告 |
| 文档/阶段状态 | AIDevelopmentWorkflow（已于 2026-08-07 删除，内容见 git 历史） | 对应权威文档和机器状态 | doc guard；不擅自提升 gate |

## 当前阶段与阻断项

| 阶段/范围 | executionStatus | gateStatus | 当前事实 |
| --- | --- | --- | --- |
| M0 | `InProgress` | `Blocked / EvidenceMissing` | runner/trace/部分显示与 fixture 工具存在；三层稳定基线、完整存档、APK/设备和签署仍缺 |
| M1 | `InProgress` | `Blocked / PreviousGate:M0` | facade、generation、canary、首等待 A/B/A 和 100-switch 观察存在；完整 static/异步 owner、rollback 和 memory budget 未关闭 |
| M2 | `NotStarted` | `Blocked / PreviousGate:M1` | Display DTO 合同存在，但默认数据/renderer 路径未获准切换 |
| M3-M7 合同库存 | `InProgress` inventory | `Blocked / EvidenceMissing` | 类型、smoke、schema 和设计已存在；阶段实施和发布 gate 未批准 |

最重要的未关闭事实：

- Snake 100-switch 观察的 working-set peak 约 667 MB，尚无 leak/memory budget 决策。
- M0 显示 repeat3 的 semantic 结果稳定，但 transport trace 仍因 `ui_projection` batching 漂移；nested div/srcb/dynamic-map 和设备证据未关闭。
- 保存协议有 source-only baseline 和类型冲突记录，但真实 profile 绑定、gzip、offset map 与完整 round-trip 仍缺。
- Android SAF 目前只有设计和 capability 表面，没有并行 canary、撤权恢复、APK/真机和旧路径回退证据。
- ToolchainLock、export template hash、Android SDK/JDK/Gradle 和发布 artifact identity 尚未最终锁定。
- DIA `profile.selected-name` 分类未裁决，依赖 DIA-01 的生成链当前应保持红色。

## 接手后不可破坏的不变量

- 默认 legacy 路径必须随时可回退；没有 gate decision 不删除旧 runner、renderer、Parser/VM 或平台路径。
- Core 不引用 GodotSharp；Godot Node/Resource/RID 只由主线程 host/bridge 拥有。
- Session 拥有 Resource/Save/VM/Config/Cache；旧 generation 的 completion 不得写入新 session。
- UI 只发 intent，编排器拥有 session 切换；presentation 不直接修改 Core 数据。
- CompatibilityPlan、module registry 和 port manifest 在 session 内冻结；未选择模块不得改变当前 profile。
- 外部游戏、存档、manifest 和 DLL 都是不可信输入；DLL 仅能作为桌面显式完全信任路径。
- 存档测试不原地写用户文件；所有写入和迁移必须使用 candidate、备份和原子 commit。
- `action_maps/` 是本地日志，不提交 GitHub；游戏库、APK、大报告和本机路径同样不进入源码提交。
- 编译、单个 smoke 或截图都不能把 `Blocked/Uncovered` 改成 `Passed`。

## 推荐的继续开发顺序

1. 裁决并登记 `profile.selected-name`，恢复 DIA-01/DIA-02 及依赖报告的可生成状态。
2. 继续关闭 M0 缺口：三层 fixture、稳定重复、存档、Android/APK/设备、artifact identity 和签署。
3. 在不启动 M2 的前提下关闭 M1：完整 static/异步 owner、输入/资源/音频/保存 stale 与 rollback、Snake memory budget。
4. M0、M1 都有正式 gate decision 后，再按 M2.0 tee/capture -> M2.1 canary adapter 推进 Display DTO。
5. M3-M7 现有合同只作为未来 work package 输入；对应前置 gate 未通过前，不接管生产行为。

新任务若只修复局部 legacy 行为，应在原 owner 内做小范围 TDD；若触及 session、CompatibilityPlan、Display transaction、save、resource revision 或平台 port，则升级为 M3M7EngineeringExecution（已于 2026-08-07 删除，内容见 git 历史）定义的 work package，并记录输入 identity、报告、未覆盖项和回退。
