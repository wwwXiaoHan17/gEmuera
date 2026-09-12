# M0-DIA 方言库存与静态证据工具

该工具只读扫描当前 `Scripts/**/*.cs`，生成 profile/setting 分支与指令、表达式函数注册的机器可读库存。它服务于 `DialectExtensionSystem` 的 D0，不创建 Dialect resolver，不修改 Parser/VM，也不启动 Godot 或游戏。

## 运行

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectInventory.ps1 -ProjectRoot .
```

默认输出到 `docs/NewFrameworkDesign/generated/dialect-inventory.json`。报告固定为 `executionStatus=InProgress`、`gateStatus=Blocked`、`blockerCode=EvidenceMissing`、`result=Partial`；静态库存不能升级为行为兼容结论。

## 失败语义

- 已知 profile/setting marker 没有唯一分类：exit 1。
- 同一指令贡献集合或表达式函数字典出现重复公开键：exit 1。
- 分类 catalog 版本、项目路径或必需源码块无效：exit 1。
- 指令与表达式函数同名不被静默删除；报告在 `crossRegistryCollisions` 中记录当前 `ContainsKey` 指令优先规则。

`canonicalHash` 不包含生成时间和绝对路径，包含相关源码 hash、逐分支分类、注册贡献及跨注册表碰撞。相同源码字节与 catalog 在不同文件创建/枚举顺序下必须得到相同 hash。

## 验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectInventory.ps1 -ProjectRoot .
```

契约测试覆盖真实仓库扫描、重复键拒绝、未分类命中拒绝、枚举顺序不变性和源码语义变化导致 hash 变化。`dialect-classification.json` 是版本化的 D0 人工裁决层；新增 marker 命中必须补 owner、目标模块/typed policy、BehaviorKey 或 CapabilityId 以及 fixture ID。

## 注册快照测试投影

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectRegistrySnapshot.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectRegistrySnapshot.ps1 -ProjectRoot .
```

`M0-DIA-02` 把原始库存深复制为两个测试 DTO：

- `v24-projection`：当前公共暂存桶 + v24，排除 `game.snake` 和 candidate。
- `snake-projection`：公共暂存桶 + v24 + Snake/candidate。

snapshot hash 只包含已选择 module 和规范化注册描述，不包含 available-but-unselected module、生成时间、绝对路径或 provenance warning 文本。加入但不选择 `game.snake` 时 v24 hash 必须不变；选择集合内重复公开键必须失败。

报告会同时写出两个不同结论：`testProjectionInvariant=Passed` 只证明测试构建器的元属性；`currentRuntimeIsolation=Passed` 表示 legacy parser 与表达式 lookup 已在解析前绑定到不可变的 `LegacyCompatibilityProfile` surface。它不证明 Core 已接管 legacy VM，也不替代真实游戏、lazy-load 或设备行为证据。


## 双上游接口差异报告

`powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-LegacyDialectUpstreamDiff.ps1 -ProjectRoot . -V24ProjectRoot 'E:\MyCode\Era\emuera.em-master' -SnakeProjectRoot 'E:\MyCode\Era\emuera_lazyloading_selfmodified_version-develop-skiasharp (2)\emuera_lazyloading_selfmodified_version-develop-skiasharp'
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-LegacyDialectUpstreamDiff.ps1 -ProjectRoot . -V24ProjectRoot 'E:\MyCode\Era\emuera.em-master' -SnakeProjectRoot 'E:\MyCode\Era\emuera_lazyloading_selfmodified_version-develop-skiasharp (2)\emuera_lazyloading_selfmodified_version-develop-skiasharp'
`

- 公钥集合、profile 可见性声明、参数/返回类型与运行时执行四类门禁全部通过时报告 esult=Passed。
- 参数/返回类型门禁的权威来源是运行时反射差异证据（docs/NewFrameworkDesign/generated/legacy-dialect-reflection-diff.json），它在实际程序集上比较 v24/Snake 上游与当前工程的 ArgumentBuilder 形状、函数声明与行为矩阵；静态源码文本公式只作为诊断保留，不再单独判定语义差异。
- 先运行 LegacyDialectReflectionDiff 再生成该报告；若反射证据缺失或存在差异，报告 fail-fast。

## 移动端验证边界

- Android 调试 APK 通过 Godot 4.7 mono 导出并签名（arm64-v8a，v2/v3 签名方案验证通过），产物位于 ndroid/build/gemuera-debug.apk。
- 仓库内的接口证据链（运行时 smoke、反射差异、上游差异报告）证明 v24/Snake 的指令/函数参数与返回类型一致；untimeExecution=Passed 表示反射行为矩阵执行完成。
- 真机/设备上的 FPS、内存占用与耗电性能报告需要连接 Android 设备后补充；没有设备日志前不把 APK 导出声明为性能完成证据。
## Legacy 运行时查表与解析 smoke

```powershell
dotnet run --project tools\dialect-inventory\LegacyDialectRuntimeSmoke\LegacyDialectRuntimeSmoke.csproj -c Release --no-restore
```

该 smoke 加载主程序集并验证 profile 运行时的实际注册表和 `LogicalLineParser`：v24/Snake 的指令、表达式函数及表达式函数投影的 `METHOD` 指令遵循各自可见面；未选择 profile 的名称不会经 `METHOD` 路径泄漏。它还锁定浮点插件参数保持 `double` 类型和值。

该 smoke 不加载真实游戏数据，因而不替代 `CalledFunction` 的 lazy-load、handler 执行语义、存档、渲染或 Android APK/真机验证。

## 签名与完成模式库存

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectSignatureInventory.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectSignatureInventory.ps1 -ProjectRoot .
```

`M0-DIA-03` 扫描 `FunctionIdentifier` 注册表达式、`AbstractInstruction` 的 `ArgBuilder/flag` 和五个 `Creator.Method*.cs` 的 `ReturnType/argumentTypeArray`。扫描器只读源码文本，不反射加载或执行 handler；C# class block 识别会忽略字符串、字符和注释中的花括号。

报告状态分三层：

- `Resolved`：静态来源只有一个明确候选。
- `Conditional`：同一 handler 存在多个构造/分支候选，必须由注册构造参数或 fixture 继续裁决。
- `Unresolved`：静态来源缺失或歧义，不允许填默认值造完整。

completion/effect 字段一律带 `Candidate`，例如发现 `WaitInput` 只写 `InputWaitCandidate`。这些字段用于决定未来 descriptor 和 fixture 工作量，不是行为兼容证明，也不进入 M0-DIA-02 测试 snapshot hash。

## 边界

- `SNAKE_*`/`Snake*` handler 只作为 provenance 警告；类型名不能代替行为 fixture。
- `legacy.*.unresolved` 和 `game.snake.candidate` 表示尚未完成模块归属裁决。
- D1 Core 会话 plan 不在本工具范围内；M0-DIA-02 只覆盖测试投影。legacy runtime 注册表选择已由 `LegacyCompatibilityProfile` 和 runtime smoke 验证，但完整会话隔离与行为 fixture 仍需独立证据。
- M0-DIA-03 的 signature/completion/effect 是静态候选；正式 descriptor 必须等待 module ownership、构造参数求值和两侧行为 fixture。
- `E:\MyCode\Era` 等游戏库不会被扫描或写入。

## 条件指令签名解析

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectSignatureResolution.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectSignatureResolution.ps1 -ProjectRoot .
```

`M0-DIA-04` 使用版本化 `instruction-signature-resolution.json`，把 DIA-03 的 112 条条件指令参数逐公开键解析为静态签名。规则作用域由 handler 和精确键/锚定正则共同限定；每条规则固定预期匹配数，且选择值必须属于 DIA-03 候选。

漏匹配、双重匹配、重复规则、陈旧 DIA-03 hash、匹配数量漂移、规则误命中已解析 descriptor 或选择候选外值都会 exit 1。报告中的 `ResolvedStaticByRule` 只表示构造参数/公开键的静态求值，不覆盖默认值、错误、completion/effect 或行为兼容；旧 Parser/VM 不读取该 catalog。

## 表达式函数返回类型解析

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectFunctionSignatureResolution.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectFunctionSignatureResolution.ps1 -ProjectRoot .
```

`M0-DIA-05` 使用 `function-return-resolution.json`，依据 Creator 构造参数把 `GETCONFIG`/`GETCONFIGS` 分别解析为 `EraType.Integer/String`。报告深复制全部 360 个函数，将已有 358 条返回类型标为 `PreservedStatic`、两条条件项标为 `ResolvedStaticByRule`，并与已解析参数组合成 360 条 `CompleteStatic` signature。

`CompleteStatic` 只表示参数与返回类型的静态来源完整；`CheckArgumentType`、默认参数、错误、`CanRestructure`、completion/effect 和运行时结果仍需两侧 fixture。DIA-05 与 DIA-04 使用相同的 stale/ambiguous/duplicate/out-of-candidate fail-fast 语义，且旧 Parser/VM 不读取报告。

## 指令有效 Flags 解析

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectInstructionFlagResolution.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectInstructionFlagResolution.ps1 -ProjectRoot .
```

`M0-DIA-06` 依据旧 `FunctionIdentifier` 的 17 个公开 flag 常量，合并 registration additional flags 与 handler 构造贡献。67 个直接 ArgumentBuilder 注册只取 registration flags，136 个单候选 handler 合并唯一构造表达式，123 个多候选 handler 由 35 条可叠加规则逐公开键求值。

规则贡献使用集合并集而非 last-wins；每条规则仍固定 `expectedMatchCount`，且贡献 flag 必须同时属于 known flags 和对应 DIA-03 handler 候选。这样可排除 `TWAIT.DoInstruction` 的局部 `flag`，以及 AWAIT/INPUTMOUSEKEY 注释中的旧赋值。报告同时携带 DIA-04 已解析参数，但 completion/effect 与实际 flag 行为仍为 Candidate/Uncovered。

## 上游公开键归属证据

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectOwnershipEvidence.ps1 -ProjectRoot . -UpstreamProjectRoot E:\path\to\XEmuera
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectOwnershipEvidence.ps1 -ProjectRoot . -UpstreamProjectRoot E:\path\to\XEmuera
```

`M0-DIA-07` 读取 DIA-01～06 的版本化输出，并按 `dialect-ownership-evidence.json` 固定的相对源码后缀、SHA-256 和预期键数，只读扫描显式绑定的上游源码。默认输出为 `docs/NewFrameworkDesign/generated/dialect-ownership-evidence.json`。上游目录缺失、源码 hash/键数漂移、重复上游公开键、DIA-01～06 hash 链不一致都会 exit 1。

当前报告覆盖 326 个指令和 360 个表达式函数：284/243 项分别得到 `UpstreamNameMatch`，28 个指令得到 `ExplicitCurrentModuleCandidate`，14 个指令与 117 个函数仍为 `Unresolved`。8 项 `CurrentTargetDiffersFromUpstreamCandidate` 是需要后续裁决的证据冲突，不是自动 replacement。catalog SHA-256 为 `efd15e50e42efcbc8cff9563b3c184a5e16ed114f1d13e04446b32ee9009c87c`，当前 evidence set SHA-256 为 `a665945d7910e704de9d13e71c812bc7bc32af6781a3ba6e61ad880ee9a8e749`。

DIA-07 的边界必须保持明确：

- 公开键在上游出现只提供 provenance 候选，不证明参数、错误、时序或运行结果兼容，也不等于 ownership 已裁决。
- `SNAKE_*` 类型名、当前 target module 或上游同名都不能自动生成 alias/replacement。
- 全部 686 项的 name comparer、alias 和 replacement 仍为 `Unresolved`，behavior 与 completion/effect 仍为 `Uncovered`。
- 报告不实例化 handler，不修改 `Scripts`，不启动 Godot/游戏，也不被旧 Parser/VM 读取。
- `currentRuntimeIsolation=Failed` 必须保留；DIA-07 不创建 D1 `CompatibilityPlan`，也不实现 D2 runtime frozen registry switch。

## 名称查找契约

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectNameLookupContract.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectNameLookupContract.ps1 -ProjectRoot .
```

`M0-DIA-08` 读取 DIA-01 与最终 DIA-07 报告，按 catalog 锁定 8 个 lookup 源文件的路径和 SHA-256，默认生成 `docs/NewFrameworkDesign/generated/dialect-name-lookup-contract.json`。报告固定 326 个指令注册、360 个表达式函数、9 个跨表碰撞；表达式函数向指令表投影 351 项，9 个同名项按旧 `ContainsKey` 行为由已有指令优先，形成 677 项旧指令 lookup surface。326+360 共 686 个公开键的 lookup contract 均有静态证据，key domain 为 684 个 ASCII uppercase 与 2 个非 ASCII 或 mixed-case key。

旧指令字典的 comparer 在静态初始化时按 `Config.ICVariable` 捕获为 `OrdinalIgnoreCase` 或 `Ordinal`，之后切换配置不会重建 comparer。旧表达式字典本身使用 `Ordinal`，但 `Config.ICFunction=true` 时调用方先执行 current-culture `ToUpper`；报告将其标为 `CurrentCultureDependent` 风险，而不是批准未来 D2 沿用该策略。`_Rename.csv` 的 `[[name]]` 替换发生在 `EraStreamReader`、词法分析之前，分类为 `SourceTextRewrite`，不能伪装成 registry `AliasOf` 或 `ReplacementDeclaration`。

catalog SHA-256 为 `d2980de34652c3932192b16bce95f38475e6f2f5168151ec73c30e6455dde2f6`，当前 contract set SHA-256 为 `169ca8161f8141351a25c665ca08cf176eb91c8a30920d6b9e4878e70eaef65c`。这些哈希只固定当前旧 lookup 证据；全部 686 项 semantic alias 和 semantic replacement 仍分别为 `Unresolved`，behavior/completion/effect 仍为 `Uncovered`。静态报告保持 `currentRuntimeIsolation=Failed` 与 `parserVmConsumption=NotConsumed`；独立 startup/parser descriptor presence/ownership guard 仅校验绑定计划和 legacy registry，不修改旧 handler、不创建 D1 plan，也不实现 D2 frozen registry。

## 未决归属的模块可见性

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectModuleVisibility.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectModuleVisibility.ps1 -ProjectRoot .
```

`M0-DIA-09` 只读取 DIA-02 的 v24/Snake 测试投影、DIA-07 的归属证据和 DIA-08 的 lookup contract，默认生成 `docs/NewFrameworkDesign/generated/dialect-module-visibility.json`。它对 DIA-07 的 131 项 `Unresolved` 做机械的 projection-membership 分区：123 项在 v24 与 Snake 投影中都可见，8 项只在 Snake 投影中可见（指令为 8/6，表达式函数为 115/2）；所有未决 key 均必须存在于 Snake 投影，缺失或重复 key、hash 链不一致、枚举顺序影响 canonical hash 都会失败。

`V24VisibleCandidate` 只表示该公开键存在于 v24 测试投影，`SnakeOnlyCandidate` 只表示该键缺席 v24 而存在于 Snake 投影。两者不是 module owner、`AliasOf`、`ReplacementDeclaration` 或行为兼容结论：例如候选的当前贡献来源仍可能是 legacy common/v24 method，必须保留在报告中供后续 fixture 裁决。catalog SHA-256 为 `4fffa2541a39b5f4d62ec3f5aa04dfab6f8a2b04e8af5a63b7019d08c6d9574e`，当前 visibility set SHA-256 为 `bc2b54704d2c4252f4d910eca253f9ec5613bc16105266383ad0c901e278bfdf`；静态报告继续保持 `currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed` 与 ownership=`Unresolved`。独立 descriptor presence/ownership guard 不消费该报告，也不创建 D1 plan、D2 frozen registry、handler 或 typed policy。

Route-adapter refresh: the generated DIA-08 contract set is now `e1f84151c6c238b4ad22e5b69828e4ae3176bb4a41cf8d469a405b21e0898056`, and DIA-09 visibility set is `755289bb95538bf77e90b5ae052aea4d756fe85b62b4165fe753e2da040ba29a`; earlier shorthand hashes in historical entries are superseded.

## 会话计划静态预检

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectPlanPreflight.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectPlanPreflight.ps1 -ProjectRoot .
```

`M0-DIA-10` 仅消费 `generated/dialect-registry-snapshots.json` 和 `generated/legacy-session-state-inventory.json`，再依据版本化 catalog 生成 `generated/dialect-plan-preflight.json`。它只暴露两个有静态证据的离线映射：`v24pure`→`V24Pure`/`v24`=290/358，`snake`→`Snake`/`snake`=326/360；`SnakeModernMobile` 必须显式保持 `Uncovered`，没有独立 DIA-02 projection 时会 fail-fast。

每个 profile 的 `planSemanticHash` 只覆盖 profile id、legacy enum、registry projection、投影 canonical hash、已选择 module 和静态计数，因此 selection source、requested generation、请求顺序和报告生成时间不能改变同一语义 hash。`preflightSetHash` 则覆盖 catalog、DIA-02/M0-SES-01 来源 hash、两个预检 DTO 和状态证据；catalog 枚举顺序也不能改变它。测试还验证源投影数组在生成后变异不会回写 DTO、陈旧 DIA-02 hash、错误计数、提前 M1 状态和未知 profile 均被拒绝。

这不是运行时 `CompatibilityPlan` 或 `DialectPlan`。报告固定为 `currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`、`compatibilityPlanRuntime=NotImplemented`、`m1Eligibility=Blocked`；它不创建 `LegacySessionFacade`、feature flag、resolver、D2 frozen registry 或任何 Parser/VM 接入，也不启动 Godot 或游戏。

## 旧 CoreProfile 选择证据

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectProfileSelection.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectProfileSelection.ps1 -ProjectRoot .
```

`M0-DIA-11` 只读 `FirstWindow.cs`、`Program.cs`、`LegacyRunnerConfig.cs` 和 DIA-10 预检报告，生成 `generated/dialect-profile-selection.json`。它将旧选择事实固定为：空游戏根目录返回 `V24Pure`；launcher `snake` 优先返回 `Snake`；随后 modern marker（`modern_core.txt`、`snake_modern_core.txt`）返回 `SnakeModernMobile`；legacy marker（`snake_core.txt`、`legacy_snake_core.txt`）返回 `Snake`；最后默认 `V24Pure`。

启动器 normalizer 和 M0 runner 只允许 `v24pure`、`snake`，未知 launcher 值回退到 `v24pure`。因此 `SnakeModernMobile` 只是当前源码的 marker-only 分支：它没有 launcher profile id，DIA-10 仍为 `Uncovered`，runner 仍为 `Unsupported`。这些 marker 是历史选择线索，不是内容身份、签名、可信 manifest 或自动兼容授权。

`selectionSetHash` 覆盖被锁定的源文件、DIA-10 hash、catalog、enum、normalizer、marker 和 precedence；它不包含生成时间，catalog 枚举顺序不影响结果。该工具不读取游戏目录、不调用 marker、不创建 runtime resolver、`CompatibilityPlan`、`LegacySessionFacade` 或 D2 frozen registry，也不接入 Parser/VM。

## CompatibilityPack 静态声明契约

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectCompatibilityPack.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectCompatibilityPack.ps1 -ProjectRoot .
```

`M0-DIA-12` 只消费 `generated/dialect-plan-preflight.json` 与 `generated/dialect-profile-selection.json`，并用两者的 set hash 钉扎 versioned `emuera.compatibility-pack/v1` catalog。当前它只允许两个 evidence-backed 声明：`v24pure` 使用内置 `gemuera.v24`，`snake` 使用内置 `gemuera.v24` 与 `game.snake`。DIA-02 中的 `legacy.current.common/expression` 只保留为 static projection support，绝不能作为正式分发 module id。

每份 pack 都必须显式写出 required/optional capability arrays；在 M0 尚无 runtime capability 绑定证据时，两者可以为空但不能省略。catalog 还固定 `contentBindingStatus=NotBound`、save/fixture=`Uncovered`、distribution=`Blocked`，并 fail-fast 拒绝未知 module、`SnakeModernMobile`、DLL/assembly/type/script/path/URL 等可执行载荷、缺失 capability 字段和 DIA-10/11 hash 漂移。

这仍不是游戏 manifest parser、content fingerprint、user pin、runtime resolver 或 `CompatibilityPlan`。工具不读取游戏目录、不加载程序集、不启动 Godot/游戏，也不创建 `LegacySessionFacade`、feature flag、D2 frozen registry 或 Parser/VM 输入。

## BehaviorKey / CapabilityId 声明词汇表

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectDeclarationVocabulary.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectDeclarationVocabulary.ps1 -ProjectRoot .
```

`M0-DIA-13` 只消费 `generated/dialect-inventory.json` 与 DIA-12 CompatibilityPack report。它把当前 DIA-01 的 30 个 classification、97 个 branch hit 收敛为 10 个 `BehaviorKey` 和 4 个 `CapabilityId`；每项锁定 branch hit 数、source classification、target module 与 fixture。catalog/source 枚举顺序不改变 hash，未知声明、集合/计数/hash 漂移都会 fail-fast。

所有词汇项都是 `StaticCandidate`、`currentPackEligibility=NotEligible`、`runtimeStatus=NotImplemented`。工具还检查 DIA-12 的 allowed/required/optional capability arrays 仍为空，因此任何 source candidate 都不能被误写进当前静态 pack 或当作可运行的脚本能力。

这不是 typed policy 的实现、默认值决定、runtime module catalog、manifest allowlist 或 resolver。它不读取游戏目录、不运行 Godot/游戏、不创建 `CompatibilityPlan`、policy manager、`LegacySessionFacade`、D2 frozen registry 或 Parser/VM 输入。

## BehaviorKey 消费边界静态审计

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectPolicyConsumerBoundary.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectPolicyConsumerBoundary.ps1 -ProjectRoot .
```

`M0-DIA-14` 只读取 DIA-01 inventory 与 DIA-13 declaration vocabulary，并以两者 hash 钉扎 10 个 `BehaviorKey` 的 future consumer boundary。报告固定 10 个唯一 `consumerContractId`/`decisionOwner`、14 个 source classification binding、16 个 source-file binding、current/intended owner、target module 与 fixture；catalog 和 source 枚举顺序不改变 boundary set hash。陈旧 hash、未知行为键、重复 contract/owner、错误 source role 和提前填入 DIA-13 runtime capability exposure 都会 fail-fast。

source role 只能是 `DecisionConsumer` 或 `ConfigurationInput`。`instruction.scoped-variable-registration.v1` 的 config schema 必须是后者，`FunctionIdentifier` registration guard 才是前者，因而未来 configuration snapshot 不能与 frozen catalog builder 形成 sibling direct call。所有输出固定为 `BoundaryDraftOnly`/`NotImplemented`：该工具不实现 C# policy interface/manager、resolver、runtime `CompatibilityPlan`、`LegacySessionFacade`、feature flag、D2 frozen registry 或 Parser/VM 输入，也不读取游戏目录或启动 Godot/游戏。

## 策略接口表面静态契约

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectPolicySurface.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectPolicySurface.ps1 -ProjectRoot .
```

`M0-DIA-15` 只读取 DIA-14 policy consumer boundary report，并保留其 DIA-13 provenance hash。它为全部 10 个 BehaviorKey 分配 future `portTypeId` 和 contract family：6 个 `PolicyDecision`、2 个 `BridgeProjection`、2 个 `FrozenCatalogContribution`。catalog 强制唯一 port type，并与 DIA-14 的 `consumerContractId`/`decisionOwner` 精确一致；hash、BehaviorKey、owner、port、source capability exposure 或顺序漂移都会 fail-fast。

`portTypeId` 不是已存在的 C# interface，而是后续 D3 API review 可引用的静态名字。所有 contract 强制 `InterfaceDraftOnly`、`policyValueStatus=Unspecified`、`runtimeStatus=NotImplemented`，因此不包含 method signature、DTO、default、error、completion、effect、timing 或脚本行为。工具不实现 policy manager、resolver、runtime plan、`LegacySessionFacade`、D2 frozen registry 或 Parser/VM 输入，也不读取游戏目录或启动 Godot/游戏。

## 模块组合静态契约

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectModuleComposition.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectModuleComposition.ps1 -ProjectRoot .
```

`M0-DIA-16` 只读取 DIA-12 CompatibilityPack 与 DIA-15 policy surface 的 generated JSON，并以两个来源 set hash 钉扎 versioned module composition catalog。它固定 `gemuera.v24@1.0.0` 为零依赖/零 port 的基础模块；`game.snake@1.0.0` 以 `gemuera.v24 [1.0.0,2.0.0)` 为唯一依赖，并声明全部 10 个 future port。报告固定 2 module、1 dependency edge、10 port declaration、2 profile closure；`v24pure` closure 仅为 `gemuera.v24`，`snake` closure 按 dependency-first 顺序为 `gemuera.v24,game.snake`。

catalog、dependency、port 的枚举顺序不能改变 hash，来源对象后续变异也不能回写 report；陈旧 DIA-12/15 hash、未知 dependency、重复 port、dependency cycle、错误计数和提前 runtime capability exposure 都会 fail-fast。descriptor 必须保持 `StaticCandidate`/`NotImplemented`/`Blocked`。这不是 module assembly、reflection scan、runtime module catalog、resolver、`CompatibilityPlan`、`LegacySessionFacade`、D2 frozen registry 或 Parser/VM 输入；工具不读取游戏目录、不启动 Godot/游戏，也不执行 version-range resolution、content/manifest/pin 或 capability/save/fixture binding。

## 行为 fixture 契约

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Invoke-DialectBehaviorFixtureContracts.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dialect-inventory\Test-DialectBehaviorFixtureContracts.ps1 -ProjectRoot .
```

`M0-DIA-17` 只消费 DIA-13 declaration vocabulary 与 DIA-15 policy surface。它为每个已命名的 `BehaviorKey` 锁定同一个 source fixture ID、`v24pure`/`snake` 双侧 profile、`baseline`/`extension`/`undeclared` 三种证据情形，以及 input、observable-result、error、completion、effect 五个 trace facet。它不写任何 expected policy value；未来 fixture 必须记录“无 completion/effect”而不是省略对应字段。

catalog 对每项强制 `Planned` / `Uncovered` / `NotImplemented` / `BlockedByFixture`。未知或重复行为键、DIA-13/15 的 fixture/source hash 漂移、提前将 fixture 写成 Captured、或附带 policy value、method/DTO 等运行时载荷都会失败。生成报告 `docs/NewFrameworkDesign/generated/dialect-behavior-fixture-contracts.json` 只是一份实现前的 evidence gate：它不运行游戏，不创建 C# interface、policy manager、resolver、`CompatibilityPlan`、frozen registry 或 Parser/VM 输入。
