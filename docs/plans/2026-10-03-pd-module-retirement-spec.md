# P-D 内置方言模块退役设计（profile → 第一方兼容包）

> 状态：**Spike 设计稿，待 OWNER 评审**（2026-10-03）。本文只做设计，不含任何运行时改动——
> 本 spec 对应的提交只含文档与示例映射 JSON，零生产代码变更。
> **退役执行前提**：本 spec 通过 OWNER 评审后，另开独立任务逐 profile 执行；任何退役任务
> 开工前必须以本文为基线契约，且不得偏离 §5 的门禁矩阵。
> 上游设计：`docs/designs/compat-pack-interface.md` §8（P-A…P-D 阶段划分）。

## 0. 术语与现状快照

- **profile**：launcher/引擎侧的兼容组合 id，当前六个：`v24pure`/`v18`/`snake`/`erafl`/`erablue`/`megaten`
  （`src/Core/Compatibility/BuiltInDialectCatalog.cs:89-99`；runner 名录 `tools/legacy-runner/profiles.generated.json` 的 `profileIds`）。
- **内置方言模块**：引擎内注册的 `DialectModuleDefinition`（`gemuera.v24`、`gemuera.v18`、`game.snake`、
  `game.erafl`、`game.erablue`、`game.megaten`），注册点 `BuiltInDialectCatalog.CreateLegacyBaseline()`
  （`src/Core/Compatibility/BuiltInDialectCatalog.cs:13-39`）。
- **兼容包（pack）**：程序集即包 + 内嵌 `compatpack.manifest.json`（`docs/designs/compat-pack-interface.md` §3）。
  第一方 v18 数据包已存在：`packs/gemuera.v18/`，packId=**`pack.gemuera.v18`**（不能取 `gemuera.v18`——
  与内置模块 id 撞保留名，`src/Core/Compatibility/Packs/CompatPackRules.cs:26-29` 拒载）。
- **P-D 前提已就绪的机制**（本 spec 直接消费，均已落地）：
  - capability 激活与内置模块解耦：snake/erafl/megaten 三个策略由 `plan.CapabilityIds` 派生，
    社区包在 `v24pure` 基线上声明同一组 capability 即可激活对应行为
    （`Scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs:187-205`，设计 §13.5）；
  - 包会话生命周期：`CompatPackHost.ConfigureForLaunch` 返回 `CompatPackLaunchResult(Plan, Session)`，
    `Program.ConfigureCompatibilityPlan(plan, packSession)` 同源绑定、清理路径确定性 Unload
    （`Scripts/Emuera/Compatibility/CompatPackHost.cs:170/:178-235`、`Scripts/Emuera/Program.cs:87-88/:353/:416/:436`）；
  - launcher 按游戏包选择：`launcher.cfg [compat_packs]`（`scripts/GodotHost/LauncherSettingsStore.cs:9`），
    扫描候选 + 勾选 + 手动路径（`scripts/FirstWindow.CompatPackUi.cs`），平台正确游戏键
    （`src/Core/Compatibility/Packs/CompatPackLauncherConfig.cs:22-53`），注入链
    `ApplyCompatPackEnvironment`（`scripts/FirstWindow.cs:683-705`）；
  - v18 包等价门禁两层：xUnit `V18PackManifestTests.GeneratedManifest_ReproducesBuiltinV18Surface`
    （`tests/xUnitTest/GEmuera.Core.Tests/V18PackManifestTests.cs:24-44`）+ SurfaceSmoke
    `AssertV18PackEquivalence()`（`tools/dialect-inventory/LegacyDialectSurfaceSmoke/Program.cs:293-294/:429`）。

---

## 1. 问题一：profile → 第一方 packId/path 的映射存储位置与加载优先级

### 1.1 映射文件位置（裁定建议：应用内置只读资源）

| 候选 | 位置 | 裁定 |
| --- | --- | --- |
| **A（推荐）** | `assets/text/pack-profile-map.json`，随应用分发，launcher（FirstWindow）启动时读取 | 符合文件管理规范"全部 Godot 运行时资源统一放 `assets/`"（AGENTS.md）；第一方默认值不应放在用户可写目录（`user://launcher.cfg` 可被静默改写，破坏退役语义的可审计性）；示例文件 `packs/pack-profile-map.example.json` 仅作 schema 文档入库，**不参与运行** |
| B | `launcher.cfg` 新节 | 否——launcher.cfg 是用户设置（`user://`），第一方默认值混入用户配置会混淆"默认 vs 用户覆盖"两级语义 |
| C | 引擎侧（Core）内置映射 | 否——违反 STS2"本体只保留 v24 单基线"方向；映射是 launcher 层概念（把 launcher profile 翻译为"v24pure 基线 + 包"），引擎永远只见 `v24pure` 与包路径（经既有 env 通道，`CompatPackHost.cs:24`） |

映射条目的 schema 见 §2；**生效时机**：profile X 的映射条目与"删除 X 内置模块"**同一提交**落地
（见 §4 逐步退役）。在此之前映射为空/不含 X，行为零变化——避免"包注入在 v18/snake 等非 v24pure
基线上被 `baseProfileId` 校验整包拒载"（`src/Core/Compatibility/Packs/CompatPackRules.cs:31-36`）。

### 1.2 加载优先级（四层裁定）

会话绑定时的启用包清单按以下优先级合并（优先级高者存在时，低者整体跳过并记提示）：

| 优先级 | 来源 | 性质 | 锚点 |
| --- | --- | --- | --- |
| P0 | `launcher.cfg [compat_packs]` 按游戏显式选择 | 用户显式输入（最高） | `CompatPackLauncherConfig.TryGetSelectionForGame`（`CompatPackLauncherConfig.cs:143-166`）；消费点 `scripts/FirstWindow.cs:685-688` |
| P1 | 外部 `GEMUERA_COMPAT_PACKS` 环境变量 | 显式诊断输入 | `CompatPackHost.cs:24`；既有合并语义 `MergeSelection`（`CompatPackLauncherConfig.cs:116-125`，E2-R3 裁定 stored > external） |
| P2 | profile→pack 第一方默认映射（本文） | **隐式默认**（最低的"有值"层） | 新增；仅当 P0、P1 皆空且映射含当前 profile 条目时注入 |
| P3 | 无包 | 纯基线 | `CompatPackHost.ReadEnabledPackPaths` 为空时 `ConfigureForLaunch` 原样返回基线（`CompatPackHost.cs:180-182`） |

裁定理由：

1. **显式输入永远压过隐式默认**。映射与"内置模块注册"同属隐式默认（用户没有为该游戏选包时的兜底）；
   P0/P1 都是显式动作，任何一层显式输入存在时映射不得生效——这保证诊断工具可以随时用 env 强制
   指定/替换包（含模拟"包缺失"回退），也保证用户自选包不被默认映射静默叠加。
2. **P1 > P2 与既有 E2-R3 语义同构**：现有 `MergeSelection` 已裁定"stored（显式按游戏）> external
   （显式诊断）"；映射作为新增隐式默认排在两者之后，是同一原则的顺延，不推翻任何既有裁定。
3. P0 与 P2 并存时的冲突文案见 §3.4（用户自选包不含退役方言包时提示，不静默吞掉）。

### 1.3 映射的消费点（退役后的翻译链，设计）

- **翻译函数（新增，launcher 纯函数）**：`ResolveEngineProfileId(launcherProfileId)`——
  映射命中 → 返回 `"v24pure"`；未命中 → 原样返回。插入两处引擎绑定调用之前：
  - `scripts/FirstWindow.cs:1450-1462`（`SetSelectedGamePath`：`:1458` 归一化之后、`:1459`
    `Program.SetLauncherCompatibilityProfile(...)` 之前）；
  - `scripts/FirstWindow.cs:85-109`（`ConfigureLegacyRunnerSession`：`:101-102` 同位置）。
  **注意**：`launcher.cfg [launcher] last_core_profile` 仍持久化 launcher 层 profile id（翻译只发生在
  引擎绑定前最后一刻），保留用户路由意图；`SelectedCoreProfileName` 语义不变（`FirstWindow.cs:72`）。
- **包注入**：`ApplyCompatPackEnvironment(gameRoot)`（`FirstWindow.cs:683-705`）从两输入
  （stored/external）扩为三输入（stored/external/mapped），合并规则 = §1.2 优先级；写入通道不变
  （仍是 `GEMUERA_COMPAT_PACKS` env，`CompatPackHost` 加载管线零改动）。
- **路径解析**：映射的 `relativePath` 按 launcher 根绝对化，复用既有
  `AbsoluteizeCompatPackPath`（`scripts/FirstWindow.CompatPackUi.cs:382-394`；桌面=exe 目录
  `scripts/FirstWindow.CompatPackUi.cs:396-402`，Android=`/storage/emulated/0/emuera`）。
  `relativePathAndroid` 提供平台分目录（缺省回落 `relativePath`）。**不做扫描兜底**：条目路径
  找不到文件即按 §3.1 回退——"显式路径、禁扫描"纪律（设计 §5.1）不因映射破例。

---

## 2. `pack-profile-map` JSON schema

示例文件：`packs/pack-profile-map.example.json`（入库文档示例，不参与运行）。生产文件
`assets/text/pack-profile-map.json` 结构与之相同。字段（UTF-8、LF）：

| 字段 | 类型 | 必填 | 语义 |
| --- | --- | --- | --- |
| `schemaVersion` | string | 是 | 映射文件自身 schema 版本，本文为 `"1.0.0"`；launcher 只认识自己支持的主版本 |
| `profiles` | array | 是 | 映射条目数组；`profileId` 重复即视为映射文件损坏（拒绝整文件 + 诊断日志） |
| `profiles[].profileId` | string | 是 | launcher 层兼容 profile id（`v18`/`megaten`/`erablue`/`erafl`/`snake`）；条目存在即声明"该 profile 已由包供给，内置模块已退役" |
| `profiles[].packId` | string | 是 | 期望的包 id（第一方命名约定 `pack.gemuera.<profile>`，与既有 `pack.gemuera.v18` 一致；不得与内置模块保留名撞——`CompatPackRules.cs:26-29`） |
| `profiles[].relativePath` | string | 是 | 包 dll 相对 launcher 根的路径（桌面惯例 `compat_packs/<packId>.dll`，扫描根定义 `scripts/FirstWindow.CompatPackUi.cs:21/:144-163`） |
| `profiles[].relativePathAndroid` | string | 否 | Android 分目录路径（惯例 `packs/<packId>.dll`）；缺省回落 `relativePath` |
| `profiles[].requiredPackVersion` | string | 是 | 最低包版本（semver）；检测分层见 §3.2 |
| `profiles[].note` | string | 否 | 人类可读备注（示例文件用；生产文件可不带） |

第一方五条目与建议值（示例文件已含）：

| profileId | packId | relativePath | relativePathAndroid | requiredPackVersion | 包状态 |
| --- | --- | --- | --- | --- | --- |
| `v18` | `pack.gemuera.v18` | `compat_packs/pack.gemuera.v18.dll` | `packs/pack.gemuera.v18.dll` | `1.0.0` | **已存在**（`packs/gemuera.v18/`） |
| `megaten` | `pack.gemuera.megaten` | `compat_packs/pack.gemuera.megaten.dll` | `packs/pack.gemuera.megaten.dll` | `1.0.0` | 待 P-D 步骤产出 |
| `erablue` | `pack.gemuera.erablue` | `compat_packs/pack.gemuera.erablue.dll` | `packs/pack.gemuera.erablue.dll` | `1.0.0` | 待 P-D 步骤产出 |
| `erafl` | `pack.gemuera.erafl` | `compat_packs/pack.gemuera.erafl.dll` | `packs/pack.gemuera.erafl.dll` | `1.0.0` | 待 P-D 步骤产出 |
| `snake` | `pack.gemuera.snake` | `compat_packs/pack.gemuera.snake.dll` | `packs/pack.gemuera.snake.dll` | `1.0.0` | 待 P-D 步骤产出 |

---

## 3. 问题二：三类回退路径与用户可见文案

**总不变量（沿设计 §3.3 降级不变量）**：任何失败绝不"半加载"静默继续；回退目标 = 失败时点语义上
最接近的完整会话。两级防线分工：**launcher 预检**（注入前，fail → 不注入 + UI 文案）+
**引擎终审**（既有加载管线，fail → 整体回退会话基线 + `[LOAD]` 日志，`CompatPackHost.cs:190-234`）。

### 3.1 pack 缺失

- **检测**：launcher 预检——映射 `relativePath`（按平台）绝对化后 `File.Exists` 为假 → 不注入该包。
- **回退目标**：
  - 过渡期（映射条目已加但内置模块尚未删——评审误序防护）：不注入，保留内置 profile 行为
    （此时若强行注入，包会在引擎侧因 `baseProfileId` ≠ 会话基线被拒载，`CompatPackRules.cs:31-36`）；
  - 退役后（正式态）：会话以纯 `v24pure` 基线启动（翻译函数已把 profile 翻为 `v24pure`）。
- **用户可见文案**（建议 MultiLanguage key，桌面状态行 / Android 启动提示同源）：
  - key `Launcher.CompatPackMissing`：
    `未找到 {profile} 兼容包：{path}。本次将以 v24 基线启动；请将 {packId}.dll 放入 {dir} 后重试。`

### 3.2 版本不符

- **检测分两档**：
  1. `targetEngineApi` 不匹配：引擎加载段拒载（`CompatPackHost.cs:152` + `CompatPackRules` 校验；
     日志 `[LOAD] CompatPack disabled (load rejected)`，`CompatPackHost.cs:190-194`）。
  2. `requiredPackVersion` 最低版本：v1 设计 = launcher 在预检阶段用只读 PE 资源探测读取包内嵌
     `compatpack.manifest.json`（`System.Reflection.PortableExecutable.PEReader` 读 manifest 资源，
     不执行包代码、不进 ALC）后做 semver 比较；**降级备选**（若探测实现成本超预期）：该字段降级为
     记录性字段，陈旧包由引擎侧 `targetEngineApi` 精确匹配 + `baseSurfaceHash` 精确对账
     （设计 §12.9，`LegacySurfaceHash.ComputeV24SurfaceHash()`，`CompatPackHost.cs:79`）拒载——
     两者已能拦住"基于旧引擎表面编译的包"，仅失去"同 API 但缺新修"的细粒度拦截。**此降级选项
     请 OWNER 在评审时裁定**。
- **回退目标**：同 §3.1（过渡期保留内置 / 退役后纯 v24）。
- **用户可见文案**：
  - key `Launcher.CompatPackVersionMismatch`：
    `{profile} 兼容包版本不符（需要 ≥ {required}，实际 {found}）。本次将以 v24 基线启动；请更新兼容包文件。`

### 3.3 身份不符

- **检测分两档**：
  1. packId 不符（映射承诺 `packId`，实际文件 manifest 声明另一 packId）：launcher 预检（同 §3.2
     的 PE 探测）→ 不注入。防"文件放错/改名的野包顶替第一方包"。
  2. gameIdentity 不符（包绑定特定游戏，与 GameBase.csv 不匹配）：引擎终审已实现——
     `CompatPackHost.VerifyGameIdentity`（`CompatPackHost.cs:242-274`）整体回退基线 + 日志
     `[LOAD] CompatPack disabled (game identity mismatch)`（`CompatPackHost.cs:197-202`）。
- **回退目标**：同 §3.1。
- **用户可见文案**：
  - key `Launcher.CompatPackIdentityMismatch`：
    `兼容包身份不符（期望 {packId}，实际 {foundPackId}）。本次将以 v24 基线启动；请核对文件是否放错。`

### 3.4 第四类提示（选择冲突，非失败但必须可见）

用户已按游戏自选包（P0 生效）但自选清单不含已退役 profile 的方言包：会话照常以用户选择启动
（P0 优先是裁定），但 UI 状态行必须提示，不得静默：

- key `Launcher.CompatPackRetiredProfileNotCovered`：
  `已启用自选兼容包，但未包含 {profile} 方言包（该方言内置模块已退役、由兼容包供给）；如需 {profile} 语义请勾选 {packId}。`

### 3.5 引擎侧失败的用户呈现（既有行为，不改）

引擎终审失败时用户侧表现 = 纯基线可玩 + 状态行"兼容包加载失败已回退"（既有
`UpdateSelectedGameCompatibilityHint` 警告体系，`scripts/FirstWindow.cs:1421-1448`）+ `[LOAD]`
诊断日志（`docs/logging-convention.md` 口径）。本 spec 不新增引擎侧文案。

---

## 4. 问题三：退役顺序与每步验收矩阵

### 4.1 顺序裁定：v18 → megaten → erablue → erafl → snake

| 步 | profile | 先走理由 | 风险点 |
| --- | --- | --- | --- |
| 1 | **v18** | 纯数据模块（只隐藏名单，无 policy/变体/DFC，`LegacyCompatibilityModules.cs:623-676`）；包等价已被两层门禁证明（§6）；包已存在（`packs/gemuera.v18/`） | 等价测试改写为 golden 比对（§4.2 步骤 4） |
| 2 | **megaten** | 纯策略模块、表面零增量（`LegacyMegatenCompatibilityModule.Declare` 为空，`LegacyCompatibilityModules.cs:684-690`）；策略激活已解耦（`LegacyCompatibilityModules.cs:194-195`） | 包 = manifest-only 壳 + 3 个 capability id（`LegacyMegatenCompatibilityPolicy.RequiredCapabilities`，`LegacyCompatibilityModules.cs:824-829`） |
| 3 | **erablue** | 表面增量仅 `SETANIMETIMER` 一条指令（`LegacyEraBlueCompatibilityModule`，`LegacyCompatibilityModules.cs:717-736`），无专策对象 | 外部插件 capability（`EraBlueCompatibilityModule.cs:22`，`plugin.external-assembly.v1`）是否需包声明需退役任务取证 |
| 4 | **erafl** | capability 激活已解耦（`LegacyCompatibilityModules.cs:192-193`）；**注意 Core 侧 `EraFlCompatibilityModule` 类不删**——它是算法实现库（GMap/指针归一化等，`LegacyEraFlCompatibilityPolicy` 继续委托，`LegacyCompatibilityModules.cs:934-982`），退役的只是注册与 profile | 包需携带完整 erafl capability 集；算法实现保留在引擎能力库（设计 §7 债表末行） |
| 5 | **snake** | 最重：变体覆盖（FOR/SETBGIMAGE）+ DFC 重载契约 + 最大差集（snake 会话 670 指令/351 函数（2026-10-18 Skiav16 增量同步后），`tools/legacy-runner/profiles.generated.json`） | v1 包可用 `variantSelections` 的 `builtin:*` 表达变体（`BuiltinCompatPackVariants.Map`，`LegacyCompatibilityModules.cs:49-56`）；DFC 在 capability 完整时自动激活（`LegacyCompatibilityModules.cs:197-205`）；**包 hide 名单必须逐字等于 `SnakeExcludedFunctionNames`（`LegacyCompatibilityModules.cs:594-600`）且不得隐藏 `陥落状態`/`陷落状态`**（eraTW 系口上包硬依赖，`LegacyCompatibilityModules.cs:590-593` 取证） |

### 4.2 每步 red/green 门禁（对每个退役 profile 逐步执行）

**Red（退役前，先建证据）**：

1. 三冒烟 + 全量 xUnit 基线绿（命令见 §7.1）。
2. **golden 固化**：为该 profile 建立"包链路 ≡ 内置链路"逐名等价测试（v18 已有，其余四 profile
   在各自退役任务的第一个提交中按 V18PackManifestTests 同款模式补建——比较
   `v24pure + 包` 组装计划与 `CreateLegacySessionPlan(profile)`（`BuiltInDialectCatalog.cs:108-120`）
   的 `Dialect.Instructions/Functions` 键集合，以及投影到 `LegacyCompatibilityProfile` 后的可见性）。
3. 快照/名录留档：`Invoke-DialectRegistrySnapshot.ps1` 输出与 `profiles.generated.json` 的退役前
   哈希/计数入库到该退役任务的 PR 描述。

**Change（退役提交，单 PR 单 profile）**：

1. 删除清单执行（§5 对应行）；
2. 第一方包落地 + 生产映射文件加入该 profile 条目（§1.1 同一提交纪律）；
3. launcher 翻译接线（`ResolveEngineProfileId` + 三输入合并，§1.3）；
4. 生成物再生：`dotnet run --project tools/dialect-inventory/LegacyDialectInventoryGenerator -- <项目根>`
   → `src/Core/Compatibility/LegacyDialectInventories.Generated.cs` + `tools/legacy-runner/profiles.generated.json`；
   快照再生见 §7.3；
5. 等价测试改写：内置链路消失，测试改为"包链路 ≡ 退役前 golden 快照"（golden 数据来自 Red 步骤 2，
   以只读常量/夹具固化）。
6. **（G3 前置工作项，随对应退役步落地）扩展快照生成器**：`tools/dialect-inventory/DialectInventory.psm1`
   的 profiles 映射（`New-DialectRegistrySnapshotReport`，`:697-700`）现仅产出 `v24`/`snake`/`erafl`
   三键（§7.3）——
   - 新增 v18/erablue/megaten 条目，使 G3 对步 1-3 生效；扩展落地前，这三步的名录零差异证据由
     G4 承担（`profiles.generated.json` 六 profile 覆盖）；
   - snake/erafl 退役步同步处理自身快照条目的移除，及 v24 排除集对 `LegacySnakeCompatibilityModule`
     字段名的读取依赖（`DialectInventory.psm1:621-624`），避免模块删除后生成器失效。

**Green（退役提交验收，全绿方可合入）**：

| # | 门禁 | 通过标准 |
| --- | --- | --- |
| G1 | 三冒烟 | `LegacyDialectSurfaceSmoke`（含 `AssertPackProjection` 与改写后的包等价断言）、`LegacyDialectRuntimeSmoke`、`CoreContractSmoke` 全绿（命令 §7.1） |
| G2 | 全量 xUnit | `tests/xUnitTest/GEmuera.Core.Tests` 全绿（等价测试已改写为 golden 口径） |
| G3 | 快照名录零差异 | `dialect-registry-snapshots.json` 再生后，**快照覆盖集内存活 profile 的 canonicalHash 与退役前逐一相等**——覆盖集 = 快照文件 `profiles` 三键 `v24`/`snake`/`erafl`（键 `v24` 对应 profileId `v24pure`；生成器仅产出这三个，`DialectInventory.psm1:697-700`）；覆盖集内退役 profile（erafl/snake 步）条目消失 = 唯一允许的显式 diff，PR 单独列出。**v18/megaten/erablue 步不在快照覆盖内**，其名录零差异由 G4 承担（§7.3；扩展生成器见 Change 第 6 项） |
| G4 | 生成名录零差异（存活部分） | `profiles.generated.json` 再生后，存活 profile 的 `modules`/`instructionCount`/`functionCount` 逐字节不变；退役 profile 条目消失 |
| G5 | capability 词汇表冻结 | `CompatPackHost.BuildValidationContext` 的 capability 并集（`CompatPackHost.cs:61-63`）在退役前后**集合相等**——词汇表是包校验契约，不随 profile 退役收缩（实现建议：首个退役步把 `AllProfileIds` 驱动的并集（`CompatPackHost.cs:27`）固化为显式 id 常量，哨兵断言由 SurfaceSmoke 承担） |
| G6 | 六游戏启动矩阵 | §7.2 矩阵中依赖该 profile 的游戏经新链路（v24pure + 包 / 映射注入）无头跑到输入等待 + 关键输出核对；其余游戏回归不劣化 |
| G7 | result-review ≥ 98 | 沿设计 §8 验收惯例 |

**顺序硬约束**：任一步 Green 未齐，后续步骤不得开工（v18 的 golden 改写未绿前不得动 megaten）。

---

## 5. 问题四：`BuiltInDialectCatalog` / `LegacyCompatibilityModules` / `expectedModuleClosures` 删除清单

> 通用规则：**策略类与 capability 常量一律保留**（包链路仍消费）；删除的只是
> "注册 + 桥接模块 + 闭包条目 + launcher 专属分支"。每行括号为现行锚点，退役任务执行时以当时行号复核。

### 5.1 逐 profile 删除清单

**v18（步 1）**

- [ ] `BuiltInDialectCatalog.cs`：模块注册 `:27-29`、`V18Contributions()` `:63-67`、profile 注册 `:92-94`。
- [ ] `LegacyCompatibilityModules.cs`：`LegacyV18CompatibilityModule` 类 `:623-676`；modules 数组项 `:103`；
      `V18ModuleId` 常量 `:94`；`expectedModuleClosures["v18"]` `:120`。
- [ ] `LegacyDialectInventories.Generated.cs`：`V18InstructionNames`/`V18Functions` 随再生消失；同步删
      `CompatPackHost.cs:100/:113/:129` 三处并集行（v18 包只 hide，不依赖并集收录 v18 名）。
- [ ] `CompatPackHost.cs`：`AllProfileIds` `:27` 移除 `"v18"`（`CompatibilityProfileCatalog.Resolve` 对未注册
      id 抛异常，`src/Core/Compatibility/CompatibilityProfileCatalog.cs:125-137`，不移除会炸所有包加载）；
      同时按 G5 固化 capability 词汇表。
- [ ] `FirstWindow.cs`：`TryNormalizeCoreProfileName` 的 v18 分支 `:1492-1496` 改为"映射别名"语义
      （仍接受该 id 作为路由/手动选择值，翻译在 §1.3 两点完成）；`GetManualProfileOptionIndex` `:725`
      与高级模式下拉项 `:608` 保留但文案注明"（兼容包）"；`CoreProfileV18` 常量 `:34` 保留（路由别名仍引用）。
- [ ] 目录路由：`CreateDirectoryRouteProfileCatalog`（`FirstWindow.cs:1515-1520`）在引擎 catalog 失去 v18
      后需补映射别名条目，保证 `compat/v18/<游戏>` 目录路由（`TryResolveCompatibilityRouteProfile`，
      `FirstWindow.cs:1177-1202`）继续可用。
- [ ] 测试：`V18PackManifestTests.GeneratedManifest_ReproducesBuiltinV18Surface`
      （`tests/xUnitTest/GEmuera.Core.Tests/V18PackManifestTests.cs:24-44`）改写为 golden 口径
      （或并入 SurfaceSmoke 的 `AssertV18PackEquivalence` 改写版）。
- [ ] 工具：`tools/compat-pack/Generate-V18PackManifest` 保留，但其差集右侧（`LegacyDialectInventories.V18*`）
      改为退役时点冻结的 golden 名单拷贝（引擎生成清单将不再含 V18 数组）；包 manifest 本身是已入库
      生成物，禁止手改（设计 §13.10）。

**megaten（步 2）**

- [ ] `BuiltInDialectCatalog.cs`：模块注册 `:35-37`、`MegatenContributions()` `:75-79`、profile 注册 `:96`。
- [ ] `LegacyCompatibilityModules.cs`：`LegacyMegatenCompatibilityModule` `:681-691`；数组项 `:106`；
      `MegatenModuleId` `:96`；closure `:123`。
- [ ] **保留**：`DisabledMegatenCompatibilityPolicy` `:811-818`、`LegacyMegatenCompatibilityPolicy` `:822-856`、
      `SetMegatenPolicy` `:467-470`、Compose 激活行 `:194-195`、`LegacyCompatibilityProfile` 的 megaten
      policy 参数 `:480-481`、引擎侧 13 处门控消费点（policy 从 capability 派生，与模块在场无关）。
- [ ] Core：`MegatenCompatibilityModule.cs` 删 `CreateDefinition`/`CreateProfile`/plan 注册；capability id
      常量（`LabelLookupCaseBehavior` 等，被 `LegacyMegatenCompatibilityPolicy` 引用）**保留**。
- [ ] `CompatPackHost.cs`：`AllProfileIds` 移除 `"megaten"`；`FirstWindow.cs`：normalize 分支 `:1502-1506`、
      OptionIndex `:727`、路由别名（同 v18 模式）。
- [ ] 包：`pack.gemuera.megaten`（manifest-only 壳，capabilities = 3 个 behavior id，表面零声明）。

**erablue（步 3）**

- [ ] `BuiltInDialectCatalog.cs`：模块注册 `:31-33`、`EraBlueContributions()` `:69-73`、profile 注册 `:95`。
- [ ] `LegacyCompatibilityModules.cs`：`LegacyEraBlueCompatibilityModule` `:717-736`；数组项 `:104`；
      closure `:121`。
- [ ] Core：`EraBlueCompatibilityModule.cs` 删注册方法；`ModuleId`/capability 常量保留。
- [ ] `CompatPackHost.cs` / `FirstWindow.cs`：同前两步模式（normalize 分支 `:1497-1501`、OptionIndex `:726`）。
- [ ] 包：`pack.gemuera.erablue`（surface.addInstructions=["SETANIMETIMER"]；插件 capability 取证后定稿）。

**erafl（步 4）**

- [ ] `BuiltInDialectCatalog.cs`：模块注册 `:22-24`、`EraFlContributions()` `:57-61`、profile 注册 `:98`。
- [ ] `LegacyCompatibilityModules.cs`：`LegacyEraFlCompatibilityModule` `:693-715`；数组项 `:102`；
      `EraFlModuleId` `:93`；closure `:119`。
- [ ] **保留**：`DisabledEraFlCompatibilityPolicy` `:858-890`、`LegacyEraFlCompatibilityPolicy` `:892-983`、
      `SetEraFlPolicy` `:432-435`、Compose 激活 `:192-193`；**Core `EraFlCompatibilityModule.cs` 算法库整文件保留**。
- [ ] `CompatPackHost.cs` / `FirstWindow.cs`：同前模式（normalize 分支 `:1487-1491`、OptionIndex `:724`）。
- [ ] 包：`pack.gemuera.erafl`（capabilities = erafl profile 声明的完整 capability 集）。

**snake（步 5）**

- [ ] `BuiltInDialectCatalog.cs`：模块注册 `:19-21`、`SnakeContributions()` `:51-55`、profile 注册 `:97`。
- [ ] `LegacyCompatibilityModules.cs`：`LegacySnakeCompatibilityModule` `:533-621`；数组项 `:101`；
      `SnakeModuleId` `:92`；closure `:118`。
- [ ] **保留**：`DivergentFunctionContractNames` `:538-545`（capability 完整时 DFC 激活的名单源）、
      snake policy 两类 `:738-761/:763-807`、`SetSnakePolicy` `:424-427`、Compose DFC 激活 `:197-205`；
      `LegacyInstructionVariant` 枚举 `:28-38`、`BuiltinCompatPackVariants` `:46-82`、变体回放 `:210-222`
      （包 manifest `variantSelections` 仍消费）。
- [ ] Core：`SnakeCompatibilityModule.cs` 删注册方法；`SnakeCompatibilityCapabilities.cs` **全文件保留**
      （capability 账本与映射表是包契约词汇）。
- [ ] `CompatPackHost.cs` / `FirstWindow.cs`：同前模式（normalize 分支 `:1482-1486`、OptionIndex `:723`）。
- [ ] 包：`pack.gemuera.snake`（add/hide 由生成清单 snake delta 推导；hideFunctions 必须逐字等于
      `SnakeExcludedFunctionNames`，见 §4.1 风险点）。

### 5.2 终态（五步全完成后）

- [ ] `BuiltInDialectCatalog.CreateLegacyBaseline()` 仅剩 `gemuera.v24`（`:16-18`）；
      `CreateLegacyProfileCatalog()` 仅剩 `v24pure`（`:89-91`）。
- [ ] `expectedModuleClosures`（`LegacyCompatibilityModules.cs:113-124`）仅剩 v24pure 条目——此时按设计
      §7 债表 H2 终态，评估改为单闭包常量或与 `Program` 三 switch（`DetectCoreProfile`
      `Scripts/Emuera/Program.cs:665-680`、`ResolveCompatibilityProfile` `:693-706`、
      `GetCompatibilityProfileId` `:708-721`）一并消灭（后者依赖 `EmueraCoreProfile` 枚举收缩，
      随最后一步 snake 退役同 PR 处理）。
- [ ] `Compose` 的 packModules/内置模块互斥校验（`LegacyCompatibilityModules.cs:142-147`）、
      `ApplyPackSurface`（`:233-286`）、capability 激活路径（`:187-205`）**全部保留**——它们是包链路本体。
- [ ] AGENTS.md 铁律表述按设计 §9 改写："差异必须以兼容包方式实现，禁止直接修改 v24 基线共享路径"。

### 5.3 明确不删（防误删清单）

- `LegacyV24CompatibilityModule`（`:492-531`）与 `gemuera.v24` 模块——引擎原生基线，永不退役。
- 全部 `Disabled*/Legacy*CompatibilityPolicy` 类与 `IMegaten/ISnake/IEraFlCompatibilityPolicy` 接口。
- `BuiltinCompatPackVariants` 与 `LegacyInstructionVariant`。
- `CompatPackHost` 全部校验/身份/组装管线；`CompatPackSession` 生命周期机制。
- `SnakeCompatibilityCapabilities.cs`、`EraFlCompatibilityModule.cs`（算法库）、megaten/erablue 的
  capability 常量。

---

## 6. v18 内置 vs 包的逐名差异（本 Task Step 1 证据，期望为空）

- 测试：`dotnet test tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj -c Release
  --filter FullyQualifiedName~V18PackManifestTests`
  → **已通过！ 失败： 0，通过： 1，已跳过： 0，总计： 1（2026-10-03，146 ms）**。
- 结论：`v24pure + pack.gemuera.v18` 组装计划与内置 `CreateLegacySessionPlan("v18")` 的
  指令/函数键集合**逐名相等（差异为空）**；SurfaceSmoke 的 `AssertV18PackEquivalence()`
  （`tools/dialect-inventory/LegacyDialectSurfaceSmoke/Program.cs:293-294`）在投影层同样闭合。
- 数量对账：内置 v18 = 417 指令 / 160 函数（`tools/legacy-runner/profiles.generated.json`）；
  包 = v24pure 基线（561/266）+ hideInstructions 144 + hideFunctions 106（`packs/gemuera.v18/
  compatpack.manifest.json`）→ 561−144=417、266−106=160，闭合。与手抄名单（39+110，
  `LegacyCompatibilityModules.cs:625-628`）的 105 指令 / −4 函数历史差量的解释见设计 §13.10
  （METHOD 投影双侧声明 + 引擎不存在的名字隐藏为 no-op），行为等价无容差放宽。

---

## 7. 问题五：六游戏启动矩阵与快照名录零差异的验证入口

### 7.1 三冒烟 + 全量测试（每步退役必跑；本 Task Step 5 已验基线绿）

```text
dotnet test tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj -c Release
dotnet run --project tools/dialect-inventory/LegacyDialectSurfaceSmoke/LegacyDialectSurfaceSmoke.csproj -c Release
dotnet run --project tools/dialect-inventory/LegacyDialectRuntimeSmoke/LegacyDialectRuntimeSmoke.csproj -c Release
DOTNET_ROLL_FORWARD=Major dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release
```

注册表/生成清单变化后先再生：
`dotnet run --project tools/dialect-inventory/LegacyDialectInventoryGenerator -- <项目根>`。

### 7.2 六游戏启动矩阵

矩阵与门槛沿 `docs/designs/dialect-capability-strategy.md` §5（游戏库 + 无头跑到"输入等待" +
关键输出核对 + APK 真机抽测）。入口：`tools/legacy-runner/Invoke-LegacyRunner.ps1`
（参数 `-GodotPath`（必填）、`-GameRoot`、`-Profile`、`-OutputDirectory`（必填，**必须指向项目外**，
如 `D:\gemuera-reports\`——AGENTS.md 文件管理规范 7；脚本签名 `Invoke-LegacyRunner.ps1:1-14`）。

| 游戏 | 退役前驱动 profile | 退役后链路 | 所属退役步重跑 |
| --- | --- | --- | --- |
| 魔王（伪 eramaou_EX，v18 等价能力） | `v18` | v24pure + `pack.gemuera.v18` | 步 1 |
| eraMegaten | `megaten`（snake 实测锚点，`dialect-capability-strategy.md` §5） | v24pure + `pack.gemuera.megaten` | 步 2（snake 步亦回归） |
| erablue resort | `erablue` | v24pure + `pack.gemuera.erablue` | 步 3 |
| eraFL0.47 | `erafl` | v24pure + `pack.gemuera.erafl` | 步 4 |
| eraOCG2 | `snake`（EE 小版本敏感主体） | v24pure + `pack.gemuera.snake` | 步 5 |
| eratohok | `snake` | v24pure + `pack.gemuera.snake` | 步 5 |

矩阵运行形态：映射落地前，runner 经 `ConfigureLegacyRunnerSession`（`FirstWindow.cs:85-109`）
显式传 profile + env 注入包路径；映射落地后同一入口自动走 P2 默认映射。每步只重跑"所属步重跑"
列 + 一条 v24pure 回归（防包链路污染基线会话）。

### 7.3 快照名录零差异

- 快照文件真实路径：**`docs/NewFrameworkDesign/generated/dialect-registry-snapshots.json`**
  （生成入口 `tools/dialect-inventory/Invoke-DialectRegistrySnapshot.ps1:15-17`，默认输出即此路径；
  契约自检 `tools/dialect-inventory/Test-DialectRegistrySnapshot.ps1`）。
- **工具实际覆盖（2026-10-03 核实）**：快照生成器 `New-DialectRegistrySnapshotReport`
  （`tools/dialect-inventory/DialectInventory.psm1:619`，profiles 映射 `:697-700`）只产出三个
  profile——快照键 `v24`（内部 profileId 即 `v24pure`，`:634-635` 以 `-ProfileId 'v24pure'` 构造）、
  `snake`、`erafl`；快照文件 `profiles` 键 = `[v24, snake, erafl]`，**没有 v18/erablue/megaten 条目**。
- 零差异口径（G3 的真实范围）：再生后对比退役前留档——**快照覆盖集内未被退役的 profile
  （`v24`→v24pure / snake / erafl）的 `canonicalHash` 逐一相等**；覆盖集内被退役者（erafl/snake 步）
  条目消失是唯一允许的显式 diff（PR 描述单独列出）。键名映射注意：快照键 `v24` ≠ profileId 字面
  `v24`，对应 `v24pure`；且"v24 条目消失"不属于任何退役步（v24 模块永不退役）。
- **覆盖缺口与补偿**：v18/megaten/erablue 三步（步 1-3）的退役 profile 本就不在快照内——G3 对它们
  "无条目可比、也无条目可消失"，该三步的名录零差异证据由 **G4**
  （`tools/legacy-runner/profiles.generated.json`，再生后六 profile 覆盖核验）独自承担，直至快照
  生成器按 §4.2 Change 第 6 项前置工作项扩展。
- 硬证据链：快照零差异（G3，范围如上）+ 生成名录零差异（G4）+ capability 词汇表冻结（G5）共同
  构成"未选择侧不变"的证据，替代逐字节会话回放（引擎注册表在退役步必然变化，逐字节口径只适用
  于存活 profile 的投影）。

---

## 8. 风险与开放问题（请 OWNER 评审时裁定）

1. **`requiredPackVersion` 的 launcher 侧 PE 探测 vs 记录性字段降级**（§3.2）：探测实现干净但引入
   PEReader 依赖；降级则依赖引擎 `targetEngineApi` + `baseSurfaceHash` 双精确对账（已很强）。
2. **P1（env）> P2（映射）的优先级**（§1.2）：若 OWNER 认为第一方默认映射应压过外部 env（例如担心
   用户环境残留 env 变量使退役方言静默缺失），可翻转为 P0 > P2 > P1——但会牺牲"诊断强制覆盖默认"
   的工具能力，且与 E2-R3 的"显式 > 隐式"原则出现表述张力。
3. **映射条目与退役同提交的强约束**（§1.1）：若 OWNER 希望映射先行（提前观察注入行为），需同时放宽
   `baseProfileId` 校验或让映射条目带"观察模式"标志——两者都引入新机制，不建议。
4. **Android 真机包加载**：映射注入依赖既有 env 通道与 ALC 加载；Android 端 CompatPack 实测仍开放
   （设计 §13.9），snake 步（最重包）前必须完成至少一次 Android 真机包加载验证。
5. **`compat_packs` vs `compat/packs` 目录命名分歧**（设计 §10.2 已记录）：映射的 relativePath 采用
   现行启动器实现（`compat_packs/`），分发定稿时如改名需同步映射文件与扫描根。

## 9. 评审裁定清单（OWNER 勾选即视为 spec 通过）

- [ ] §1.1 映射位置 = `assets/text/pack-profile-map.json`
- [ ] §1.2 优先级 P0 > P1 > P2（或裁定翻转 P1/P2）
- [ ] §3.2 requiredPackVersion 实现档位（PE 探测 vs 记录性降级）
- [ ] §4.1 退役顺序 v18 → megaten → erablue → erafl → snake
- [ ] §5 删除清单与 §5.3 不删清单
- [ ] §7 验证入口与零差异口径（快照工具现仅覆盖 v24pure/snake/erafl，v18/erablue/megaten 步暂由 G4 承担，见 §7.3）
