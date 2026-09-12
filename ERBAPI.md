# ERB 扩展接口与魔改安全指南

本文供修改 gEmuera 的 ERB 语法、指令、内置函数、变量、兼容 profile 或脚本可见运行时能力时使用。它描述的是**当前工作区在 2026-07-19 的真实接口边界**，不是第三方插件规范，也不代表新的 Core 已替换 legacy ERB 引擎。

最重要的规则：**先确定改动属于哪一条执行路径；先证明未选择侧不变，再实现选择侧。**

> 当前工作区没有 `OriginalFrameworkDesign/` 或 `CODE_MAP.md`；`NewFrameworkDesign/` 仅保留历史交接快照 `DeveloperHandoff.md`。不要把这些缺失文件、旧 generated 报告或历史计数当成当前架构的权威。当前权威顺序为：C# 公共签名与调用点 → `tools/core-contracts` 合同 → `tools/dialect-inventory` 静态合同 → 针对实际 ERB 行为的可重复 fixture/trace。

## 1. 当前架构结论

### 1.1 三条并存路径，不能混为一条

```text
生产 legacy 路径（当前实际执行 ERB）
SessionSelection
  -> Scripts/GodotHost/LegacySessionBackend
  -> Program.ConfigureCompatibilityPlan(...)
  -> MinorShift.Emuera.Program / Process
  -> legacy parser、IdentifierDictionary、FunctionIdentifier、FunctionMethod
  -> legacy state / EmueraConsole
  -> Godot UI 投影

Core 候选解析路径（当前只做 Core 侧解析/会话验证）
CoreApplicationRuntime -> GameSession
  -> LegacyCoreAdapter -> ErbParser
  -> ErbParseResult（Empty / Comment / Label / Instruction + diagnostics）

Core 可恢复解释器合同（已存在，但尚未成为生产 ERB 引擎）
冻结的 IErbInterpreterCatalog
  -> IErbInterpreterFactory + ErbInterpreterDescriptor
  -> IErbInterpreterHost.StepAsync / ResumeAsync
  -> VmStepResult + ordered VmEffect / VmCompletion
  -> host bridge 投影到 Godot 或平台 port
```

因此，以下表述都不正确：

- “在 `src/Core` 添加 descriptor 后，生产 ERB 自动拥有新指令。”
- “`IErbInterpreterHost` 已是当前游戏的生产解释器实现。”
- “Core 的简化 `ErbParser` 已覆盖 legacy 的完整 ERB grammar、lazy loading、label/变量或执行语义。”
- “CompatibilityPlan 已完成 legacy v24/Snake 的运行时隔离。”

### 1.2 本次审查确认的当前状态

| 等级 | 当前事实 | 代码/合同证据 | 对扩展任务的约束 |
| --- | --- | --- | --- |
| 阻断 | 生产 ERB 执行仍由 `Scripts/Emuera` 的 legacy 注册表、parser 与 `Process` 拥有；Core `LegacyCoreAdapter` 是 candidate-only 的解析桥 | `Scripts/GodotHost/LegacySessionBackend.cs`、`src/Core/Runtime/LegacyCoreAdapter.cs`、`src/Core/Application/GameSession.cs` | 改动生产 ERB 语义时，必须实现并验证 legacy 路径；仅改 Core 不能宣称游戏可用 |
| 阻断 | `IErbInterpreterHost`、factory/catalog、step/resume、effect/completion 合同已存在，但仓库内非测试代码没有具体 factory 或 host 实现 | `src/Core/Runtime/ErbExecution.cs`；`tools/core-contracts/TestModules.cs` 是当前实现者 | 新 host 只能作为受合同保护的 Core/实验工作；接入启动路径和真实 ERB 语义前不得宣称替代 legacy |
| 阻断 | 当前 DIA 静态证据不能生成可信基线：DIA-01、DIA-02 与 signature inventory 均在 `Scripts/Emuera/Program.cs` 发现未分类的 `profile.v24-id` 命中而失败 | `tools/dialect-inventory/Test-DialectInventory.ps1` 等；`dialect-classification.json` 缺少该文件的对应 marker 分类 | 先由 owner 为该分支补充/裁决分类，再按依赖顺序重跑；不能沿用旧 hash、旧计数或手工改期望值 |
| 高 | legacy v24 与 Snake 指令/函数仍被同一静态注册流程加入，当前 runtime isolation 未完成 | `Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs` 的 `addV24CompatibilityFunctions()`、`addSnakeCompatibilityFunctions()` | 不得把“注册在 Snake 相关方法附近”写成“仅 Snake 可见”；需要完整可见面与两侧行为证据 |
| 高 | Core module contribution 能构建 `InstructionDescriptor` / `FunctionDescriptor`，但 built-in module 目前仍是空 contribution；它不提供 legacy handler | `src/Core/Compatibility/DialectRuntime.cs`、`src/Core/Compatibility/BuiltInDialectCatalog.cs` | `IDialectContribution` 不是目前可单独投产的“新增可执行 ERB 语法”接口 |
| 高 | non-empty descriptor plan 是完整可见面：route 只能选择已有 legacy handler，且只暴露 plan 中的 key | `src/Core/Compatibility/CompatibilityDescriptorRoute.cs`、`Scripts/Emuera/GameData/IdentifierDictionary.cs` | 禁止向 built-in plan 单独塞一个增量 descriptor；必须先有完整 registry、collision 与行为证据 |
| 高 | legacy 注册表仍直接暴露可变 `Dictionary`，并保留依赖静态初始化和 comparer 的兼容事实 | `FunctionIdentifier.GetInstructionNameDic()`、`FunctionMethodCreator.GetMethodList()` | 不得在启动后修改返回字典，也不得把现状当作新的稳定扩展 API |
| 高 | Godot 只应承担 session/bridge/UI 投影；ERB state、关键字、错误与执行顺序不能搬入 Node、Autoload 或 signal | `GameSession` 是 CLR 对象；`PrototypeRuntimeNode`/bridges 只管理 prototype 会话、bridge 与 UI 投影，不持有 ERB 语义 owner | 新的 Core/host 语义必须无 Godot 类型；平台完成通过 immutable DTO/effect/completion 进入 VM |

DIA 静态测试在分类修复后，仍只应得到结构性证据。现有合同的设计目标是 `InProgress`、`Blocked`、`EvidenceMissing`、`Partial`，并明确 legacy runtime isolation 仍为 `Failed`；它们不是“方言隔离已完成”的证明。

### 1.3 本次验证基线

- 在 restore 成功后，`dotnet build src/Core/GEmuera.Core.csproj -c Release --no-restore` 与 `CoreContractSmoke` 已通过，说明当前 Core 的编译和公开合同 smoke 可运行。
- `tools/core-contracts/Test-CoreArchitecture.ps1` 当前仍失败：脚本固定要求 `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`，而当前 `src/Core/GEmuera.Core.csproj` 声明的是 `net8.0;net9.0`。因此不能把 Core architecture gate 写成已通过；应由其 owner 统一 target-framework 合同后再恢复绿色。
- 这两个结论都不改变生产 ERB 仍由 legacy 路径执行的事实，也不解除 DIA-01 的静态证据阻断。

## 2. 当前可用的接口边界

### 2.1 Legacy 生产扩展面

| 能力 | 当前 owner / 入口 | 关键联动 |
| --- | --- | --- |
| 表达式内置函数 | `Scripts/Emuera/GameData/Function/Creator*.cs`、`FunctionMethod` | `FunctionMethodCreator.GetMethodList()`、`IdentifierDictionary.GetFunctionMethod`、同名 METHOD 指令投影、`CanRestructure` |
| 语句指令 | `Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs`、`Instraction.Child.cs`、`AbstractInstruction` | `FunctionCode`、`ArgumentBuilder`、`ArgumentParser`、flags、flow/wait、`Process` |
| `#` directive / label | `Scripts/Emuera/GameProc/LogicalLineParser.cs`、`LogicalLine.cs` | `ErbLoader`、`LabelDictionary`、analysis、lazy load、reload |
| lexer / expression | `Scripts/Emuera/Sub/LexicalAnalyzer.cs`、`Scripts/Emuera/GameData/Expression/` | macro/rename、source position、错误恢复、大小写比较规则 |
| legacy 系统变量与存档 | `Scripts/Emuera/GameData/Variable/`、legacy save reader/writer | descriptor、token、scope/reset、SAVE/GLOBAL/STATIC、旧档兼容 |
| legacy profile 绑定 | `Scripts/Emuera/Program.cs`、`Scripts/GodotHost/LegacySessionBackend.cs` | 兼容计划必须在启动 legacy worker 前绑定；这不是完整 registry 隔离 |

### 2.2 Core 迁移合同面

| 合同 | 当前类型 | 真实含义 / 限制 |
| --- | --- | --- |
| session compatibility | `CompatibilityPlan`、`DialectPlan`、`DialectModuleCatalog` | 生成不可变 module/port/capability/save-profile 计划与 hash；不能自行执行 legacy handler |
| descriptor contribution | `IInstructionContribution`、`IFunctionContribution`、`InstructionDescriptor`、`FunctionDescriptor` | `Apply` 到 builder，重复 key 会失败；当前 built-in module 没有可执行 contribution |
| interpreter identity | `ErbInterpreterKey`、`ErbInterpreterDescriptor`、`IErbInterpreterFactory`、`IErbInterpreterCatalog` | engine id **和** semantic version 都是选择身份；factory 必须使用冻结 descriptor 创建 host |
| host context | `ErbInterpreterContext` | 只携带不可变 `CompatibilityPlan`、source identity 与 `SessionStamp` |
| 执行与恢复 | `IErbInterpreterHost.StepAsync`、`ResumeAsync` | host 拥有可变 VM state；调用方只交换 immutable DTO |
| step 结果 | `VmStepBudget`、`VmStepResult`、`VmExecutionState`、`VmStepStopReason`、`VmFault` | result 显式报告预算、状态、停因、effects 与 fault |
| effects | `VmDisplayEffect`、`VmInputEffect`、`VmPortEffect`、`VmApplicationEffect` | 每个 effect 有严格递增 sequence 与 completion mode；wait 状态必须只有一个匹配的 wait effect |
| completion | `VmCompletion` | session stamp 与 `VmOperationId` 必须匹配 pending request；失败 completion 必须携带 `VmFault` |
| 可恢复基类 | `ResumableErbInterpreterHost` | 统一检查 step/resume 合法性、并发执行、effect 顺序、等待状态、终态和 dispose；子类只提供具体执行 |
| Core 解析 | `ErbParser`、`ErbLogicalLine`、`ErbParseResult` | 当前是确定性的行边界/诊断合同，不是完整 legacy parser 或执行器 |
| Core state/save | `VariableStore`、`SaveService`、`DeterministicSaveCodec` | 已有 session-scoped candidate/atomic-commit 风格的数据合同；尚未替代 legacy `VariableDescriptor`、VarExt 或真实旧档 ABI |

### 2.3 Descriptor route 的精确语义

`CompatibilityDescriptorRoute.Create(...)` 从**已经构建完成**的 legacy instruction/function 字典中查找 descriptor key：

1. descriptor key 不在 legacy 字典中会立即失败；route 不会创建 handler；
2. non-empty `plan.Dialect.Instructions/Functions` 会成为可见面的完整列表，而不是“在旧表上加几项”；
3. `IdentifierDictionary.BindCompatibilityPlan(...)` 只有在两个 descriptor 表都为空时才退回完整 legacy 字典；
4. 因此新 key 需要同时拥有真实 handler、完整 profile registry、跨表 collision 规则和两侧行为测试，不能只加 descriptor。

## 3. 允许扩展什么，以及应走哪一条路径

| 扩展种类 | 首选方式 | 当前适用路径 | 风险 |
| --- | --- | --- | --- |
| ERB/ERH 脚本层能力 | 用户函数、宏、用户变量、CSV 或现有指令组合 | 游戏脚本，不改解释器 | 低 |
| 表达式内置函数 | `FunctionMethod` + `Creator*.cs` 注册 | legacy 生产路径 | 中 |
| 语句指令 | `AbstractInstruction` + 参数 builder + `FunctionIdentifier` 注册 | legacy 生产路径 | 中高 |
| `#` directive / label 语法 | legacy logical-line 模型与 loader | legacy 生产路径 | 高 |
| lexer / 运算符 / grammar | legacy lexer/parser/AST 相关模型 | legacy 生产路径 | 很高 |
| Core 解释器实现 | 明确 factory、冻结 descriptor、`IErbInterpreterHost` 与 step/resume 合同 | Core 实验/迁移路径；尚未生产接线 | 很高 |
| Core descriptor / module | 贡献 descriptor、依赖与 port 声明 | Core compatibility 计划；不能单独带来 legacy 行为 | 中高 |
| 系统变量 / 保存 ABI | legacy 全链路；必要时同步 Core candidate contract | legacy 生产路径 + Core 验证 | 极高 |
| 平台能力 | typed Core DTO/effect + bridge adapter | Core/bridge；legacy facade 必须另行实现 | 高 |
| 外部 DLL | 用户明确授权的桌面 trusted plugin 路径 | 非移动端、非方言 module | 极高 |

以下都不属于可接受的 ERB 扩展：

- 在 Godot `Node`、`Control`、Autoload 或 signal 中保存 ERB state、等待状态或关键字语义；
- 在 Parser/VM hot path 新增按游戏目录名、函数名或 `Program.IsSnakeProfile` 的业务分支；
- 让 CompatibilityPack 指定 assembly、任意 C# 类型、脚本回调、绝对路径或 URL；
- 用 `_Rename.csv` 文本替换冒充 registry alias/replacement；
- 用反射扫描程序集发现移动端方言 module；
- 仅增加 Core descriptor、`FunctionCode` 或 `VariableCode` 就宣称完整语义已实现。

## 4. 分层与不变量

ERB 可观察行为必须保持下列方向：

```text
ERB source
  -> parser / logical line / expression term
  -> legacy instruction/function owner 或 Core interpreter host
  -> state mutation + typed result/effect/fault
  -> bridge / platform port
  -> Godot projection
```

Godot 可以投影 effect、完成 port 请求或管理 UI 生命周期；它不能反向拥有关键字、默认参数、错误、VM state 或执行顺序。当前 Core 的 `GameSession` 也被设计成 CLR session 对象，而不是 Godot Node。

每项扩展必须维护：

1. **未选择侧不变**：未选择 module/profile 的 registry、plan hash 和行为 fixture 不能被改变。
2. **会话所有权**：新 Core 状态属于带 generation 的 session；不能写入 process-wide 可变 truth。legacy 的静态事实必须被显式识别为迁移风险，而非复制为新设计。
3. **重复即失败**：Core builders/catalogs 采用明确重复拒绝；legacy 的静态表也不得依赖注册顺序或覆盖式赋值。
4. **公开合同完整**：public key、参数、默认值、返回、flags、错误、completion、effect、时序与 `CanRestructure` 都是语义。
5. **descriptor 与 handler 一致**：descriptor 不是文档；它必须与实际 handler 的 key/signature/owner/completion 一致。当前没有 handler 时不能加入生产 plan。
6. **名称规则显式**：legacy comparer、culture 与 `ToUpper` 是兼容事实；新增 alias 或 ignore-case 必须带 collision/culture fixture。
7. **等待可恢复**：wait effect、VM state、operation id、completion session stamp 与取消/late completion 必须成套验证。
8. **存档 ABI 显式**：变量 code、kind、维度、scope、wire tag 和 save profile 影响旧档；不能因为类型相似而复用编号。
9. **Core 无 Godot 依赖**：Core host/contract 不引用 Godot Node、场景路径、反射发现或 UI state。
10. **证据不夸大**：inventory/hash 只证明结构；Core smoke 只证明合同；二者都不能替代 parse/execute/error/effect/保存行为 fixture。

## 5. Agent 实施流程

### 5.1 先分类改动

在编辑代码前，明确写下 `route`：

```yaml
featureKey: stable.task.key
route: legacy-production | core-candidate | interpreter-host-experiment | bridge-only
publicKeys: [PUBLIC_KEY]
kind: expression-function | instruction | directive | grammar | variable | capability | interpreter
ownerModule: gemuera.v24 | game.snake | erafl | reviewed-new-module
profiles: [v24pure, snake]
signature: arguments, optional/default rules, return type
stateMutation: exact owner and ordering
errors: parse/load/runtime faults and source position
completion: core-immediate | commit-then-project | fire-and-continue | wait-port | vm-thread-blocking-bounded
effects: ordered typed effects, including explicit none
saveImpact: none | schema | codec-profile | migration
fixtures: baseline, extension, undeclared
rollback: registry/flag/adapter and expected old snapshot
```

该 YAML 是任务记录模板，不是 CompatibilityPack schema，也不能被游戏包加载。

- `legacy-production`：必须进入真实 legacy parser/handler/fixture 链。
- `core-candidate`：只能修改 Core 解析、state 或 save 合同；不得声称 production ERB 已改变。
- `interpreter-host-experiment`：必须覆盖 factory/catalog/host/effect/completion 合同；未接线前不得取代 legacy。
- `bridge-only`：不得偷偷改变 ERB 关键字、参数、执行顺序或 Core state。

### 5.2 先做库存、分类和冲突检查

```powershell
rg -n --fixed-strings "<PUBLIC_KEY>" Scripts src tools
rg -n "addV24CompatibilityFunctions|addSnakeCompatibilityFunctions|GetMethodList|GetInstructionNameDic" Scripts/Emuera
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectInventory.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectRegistrySnapshot.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectSignatureInventory.ps1 -ProjectRoot .
```

若 DIA-01 失败，先处理分类/source-identity 问题，**不要**生成或接受新 hash、报告或计数。审查当天的实际 blocker 是 `Scripts/Emuera/Program.cs` 的 `profile.v24-id` 未映射；不要再沿用旧文档中 “`PrototypeRuntimeNode.cs` 的 `profile.selected-name`” 的说法。

名称相同不等于语义相同，`SNAKE_*` 类名也不等于 owner。以 `tools/dialect-inventory/dialect-classification.json`、相关静态合同和真正的 fixture 为准。

### 5.3 先得到 RED

| 场景 | 必须断言 |
| --- | --- |
| baseline profile 未选择扩展 | 原 registry/hash/行为不变；扩展 key 不可见或按既有规则处理 |
| extension profile 选择扩展 | parse、参数绑定、执行结果/state、error、completion、effect 顺序符合合同 |
| undeclared / 缺依赖 / 重复 key | catalog/候选构建或加载稳定失败，当前 session 不变 |
| boundary 参数 | 空值、省略、类型错、最小/最大、溢出、无效资源、取消与 late completion 都有确定结果 |

静态 inventory 不能单独作为 TDD 的 GREEN。纯 Core 合同可以先进入 `tools/core-contracts`；legacy ERB 语义应使用可重复的特征化 fixture/trace。只有涉及 Node、Godot 输入或场景生命周期时才使用 GDUnit4。

### 5.4 按最小 owner 实现

#### 新增 legacy 表达式函数

1. 在职责相符的 `Creator.Method*.cs` 中实现 `FunctionMethod`，不要把所有函数堆到无关文件。
2. 显式设置 `ReturnType`、`argumentTypeArray` 与 `CanRestructure`；可选/可变参数应锁定 `CheckArgumentType` 的错误语义。
3. 只 override 与 return type 匹配的取值方法；读取 RNG、时间、变量、资源、配置或产生副作用时不得允许常量重构。
4. 在 `Creator.cs` 以唯一 public key 注册，并检查它与 instruction 表的跨表 collision；表达式函数可能投影为 METHOD instruction。

#### 新增 legacy 语句指令

1. 仅在稳定 code identity 确有必要时增加 `FunctionCode`。
2. 在 `Instraction.Child.cs` 或职责明确的新 partial 中实现 `AbstractInstruction`。
3. 优先复用 `ArgumentBuilder`；新参数形状才增补 builder/`Argument`，用 `EraType` 表示脚本类型。
4. 明确设置所有实际 flags（flow/jump/try/method-safe/partial/print/input/wait 等）。
5. `DoInstruction` 必须锁定 state mutation、错误、输出/effect 与推进点；等待指令还要覆盖 request、resume、cancel、timeout 与 late completion。
6. 当前 runtime isolation 未完成，因此“只在 Snake 可见”的需求不能只在 Snake 注册函数中新增 key；必须同时提供完整可见面和两侧行为证据。

#### 新增 directive / grammar

1. 先证明已有函数、指令或 directive 不能表达需求。
2. directive 同步检查 `ParseSharpLine`、logical-line 模型、loader、analysis、reload、full/lazy load 与内存脚本。
3. grammar 同步检查 lexer token、expression parser、macro/rename、source span、错误恢复和大小写策略。
4. `_Rename.csv` 仍是 source rewrite，不可替代 alias、replacement 或 parser production。

#### 新增 Core interpreter host（实验/迁移）

1. 定义精确的 `ErbInterpreterDescriptor`：engine id、engine version、API version、支持 module version range 与 required module ids 都要可重复。
2. 由编译期明确注册的 factory 创建 host；冻结 catalog 后不得再从可变注册状态读取语义。
3. 优先继承 `ResumableErbInterpreterHost`；若不继承，必须实现同等的 step/resume、终态、并发、sequence、wait-effect 与 completion 校验。
4. `WaitingInput` 只能对应一个 `VmInputEffect`；`WaitingPort` 只能对应一个 `VmPortEffect` 或 `VmApplicationEffect`；effect sequence 必须跨 resume 严格递增。
5. `ResumeAsync` 必须拒绝不同 session、不同 operation id、缺少 fault 的失败 completion，以及非等待态 resume。
6. host 不得引用 Godot 类型、路径或 Node；bridge 接收 immutable effect/DTO 并把 completion 回传给 host。
7. 在启动/选择/端到端行为都接线并回归通过前，标记为 experimental，不得声称替换 legacy。

#### 新增系统变量 / 保存能力

1. 先证明用户变量、local/private 变量或 runtime store 无法满足。
2. legacy 变量不得只改 `VariableCode`：descriptor、identifier、token、store、scope/reset 和读写路径必须同步。
3. Core `VariableStore` 的 candidate/commit 模式可用于验证新设计，但不能自动代表 legacy VarExt/旧档兼容。
4. 保存变化必须有显式 save profile、candidate parse、atomic commit、旧档/未知 type/dimension/sparse/round-trip 证据。

#### 新增平台能力

1. Core facade 只形成 typed request/effect，port 接受受限 token/DTO，不接受 Godot Node 或任意路径。
2. bridge/platform adapter 负责输入、资源、音频、文件或 SQL，并处理 generation、cancel、budget 和 fault。
3. Android/iOS 能力必须由最终导出验证；桌面 DLL/反射结果不能外推到移动端。

### 5.5 同步 descriptor 时避免“半张表”

1. 不要为单个 legacy 新函数/指令立即向 `BuiltInDialectCatalog` 加孤立 contribution。
2. non-empty plan 必须一次提供该 profile 的完整 instruction/function surface，不能只提供差集。
3. 同步验证 lookup、名称冲突表、跨表 collision、comparer/normalizer 和 hidden-key 不可见性。
4. descriptor 的 signature、module、completion/return 必须来自已求值 inventory 和行为 fixture，而不是类名推断。
5. 只有完整 registry snapshot、两侧行为和 undeclared-side rejection 都通过后，才可以考虑把 non-empty descriptor route 用作实际运行路由。

## 6. 验证与文档

### 6.1 先 restore，再使用 `--no-restore`

当前 `tools/core-contracts/CoreContractSmoke.csproj` 是 `net9.0`，而 `src/Core/GEmuera.Core.csproj` 是 `net8.0;net9.0`。首次运行、修改 `TargetFramework(s)`、项目引用或清理 `obj/` 后，必须先 restore；否则旧资产文件会让 `--no-restore` 报 NETSDK1005。

```powershell
# 首次运行或 TFM / 项目引用变化后
dotnet restore gemuera-c#.sln
dotnet restore tools/core-contracts/CoreContractSmoke.csproj

# 随后的定向合同检查
dotnet build src/Core/GEmuera.Core.csproj -c Release --no-restore
dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools/core-contracts/Test-CoreArchitecture.ps1 -ProjectRoot .
```

仅改 legacy ERB C# 时，最低 FastLoop 为：

```powershell
dotnet build gemuera-c#.sln -c Debug --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectInventory.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectRegistrySnapshot.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectSignatureInventory.ps1 -ProjectRoot .
```

按变更追加：

| 变更 | 追加验证 |
| --- | --- |
| 指令参数 / flags | `Test-DialectSignatureResolution.ps1`、`Test-DialectInstructionFlagResolution.ps1` |
| 函数参数 / 返回 | `Test-DialectFunctionSignatureResolution.ps1` |
| 名称 / comparer / collision | `Test-DialectNameLookupContract.ps1` |
| module / profile / behavior key | 对应 `tools/dialect-inventory/Test-Dialect*.ps1` 合同 |
| Core module / host / plan | Core build、CoreContractSmoke、`Test-CoreArchitecture.ps1`，以及 host 的 step/resume/effect fixture |
| legacy 行为 | 任务专用 v24/Snake/erafl fixture 或 trace；入口见 `tools/legacy-runner/README.md` |
| 保存 | save baseline、candidate-commit、旧档和 round-trip 测试，且原档 hash 不变 |
| Godot 场景 | 单一相关 GDUnit4 suite |
| Android 能力 | APK 与真机/目标设备报告 |

`tools/dialect-inventory/README.md` 规定静态合同的依赖关系。任何 source/profile/registration 变更都会使下游 hash 失效；不能只改期望计数、只重生成最后一份报告，或把 `Partial`/`Failed`/`Blocked` 写成通过。

### 6.2 评审、日志与回退

- 修改 `Scripts/**/*.cs` 时，若工作区存在 `CODE_MAP.md`，按其约定判断并同步更新；若不存在，必须在 action log 中明确记录“代码地图在当前工作区不可用”，不能凭空重建或声称已更新。
- 每次文件修改在被忽略的 `action_maps/` 写中文日志，记录 RED/GREEN、命令与 exit code、artifact/hash、未覆盖项、风险和回退。
- 回退必须恢复旧 registry/descriptor/adapter 与行为证据；保存改动还要保留旧 codec 和原档备份。
- 不删除正式回归测试；只删除一次性探针和无复用价值的临时文件。
- build、inventory、单个游戏启动、Core smoke 或截图都不是“完整兼容”的单独证明。

## 7. Definition of Done

结束一个 ERB 扩展任务前，至少确认：

- [ ] 已明确 `route`、public key、kind、owner module/profile、依赖和版本。
- [ ] 已检查同表和跨表 collision，且没有依赖静默覆盖或注册顺序。
- [ ] 参数、默认值、return、flags、错误、completion、effects 与 `CanRestructure` 已固定。
- [ ] baseline、extension、undeclared 三类场景先 RED 后 GREEN。
- [ ] 未选择 module/profile 的 registry/hash/行为不变；如果 runtime isolation 仍未实现，已明确标出限制而不是夸大。
- [ ] parser 改动覆盖 full/lazy/reload；wait 改动覆盖 resume/cancel/late completion。
- [ ] variable/save 改动覆盖 schema、reset、candidate commit、旧档和 round-trip。
- [ ] Core host 不含 Godot 类型、路径、反射发现或可变 session truth。
- [ ] inventory/signature/lookup/module 报告按依赖关系更新，或者因当前 gate 阻断而诚实保留失败状态。
- [ ] 定向 build/test、行为 fixture 和必要平台门已记录。
- [ ] `CODE_MAP.md` 可用性判断、action log、风险与回退步骤已完成。

## 8. 禁止合并的快捷方式

- 为新增语义在 Parser/VM hot path 加 `Program.IsSnakeProfile`、`CoreProfile ==` 或游戏 id 分支。
- 启动后修改 `GetInstructionNameDic()` 或 `GetMethodList()` 返回的字典。
- 只加 `FunctionCode`、`VariableCode`、descriptor 或 catalog 注册，不实现并测试完整链路。
- 把一个增量 descriptor 当作完整 plan，或用空表 fallback 掩盖缺失 registry。
- 依赖 `Dictionary[key] = value`、注册顺序或函数向指令投影实现覆盖。
- 未经 comparer/culture fixture 直接新增 `ToUpper()`、ignore-case 或 Unicode alias。
- 为能由现有函数/指令表达的能力修改 lexer grammar。
- 在 expression function 中允许常量折叠，却读取时间、RNG、变量、资源或产生副作用。
- 让 Godot signal、Node 或 Autoload 保存 ERB state/等待状态。
- 将反射 DLL、CompatibilityPack 或 `_Rename.csv` 当成内置方言注册机制。
- 在 restore 尚未完成或 TFM 已变化时，用一次 `--no-restore` 失败掩盖合同验证未执行的事实。

## 9. 当前参考入口

- `src/Core/Runtime/ErbExecution.cs`：解释器选择、host、step/resume、effect/completion 合同。
- `src/Core/Runtime/LegacyCoreAdapter.cs`：Core candidate-only 解析边界。
- `src/Core/Parsing/ErbParsing.cs`：当前 Core 简化解析模型与 diagnostics。
- `src/Core/Compatibility/DialectRuntime.cs`：module、descriptor、plan 与 hash 合同。
- `src/Core/Compatibility/BuiltInDialectCatalog.cs`：当前 built-in 声明与空 contribution 的限制。
- `src/Core/Compatibility/CompatibilityDescriptorRoute.cs`：descriptor 到已有 legacy handler 的只读路由。
- `src/Core/Application/GameSession.cs`：Core session/prototype composition。
- `Scripts/GodotHost/LegacySessionBackend.cs`：生产 legacy 启动和 compatibility plan 绑定。
- `Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs`：legacy 指令注册。
- `Scripts/Emuera/GameData/Function/Creator.cs`：legacy 表达式函数注册。
- `Scripts/Emuera/GameData/IdentifierDictionary.cs`：legacy lookup 与 descriptor route 绑定。
- `tools/dialect-inventory/README.md`：静态方言合同与执行顺序。
- `tools/core-contracts/`：Core 公共合同 smoke/architecture gates。
- `AGENTS.md`：协作、日志与项目级验证规则。
