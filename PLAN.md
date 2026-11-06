# gEmuera CompatPack 社区化与解释器轻量化实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> 交接说明：本计划面向没有参与前序会话的模型/工程师。开始前先读 §0（已完成基线）、§1（证据与出处索引）、§2（全局约束）、§3（Review Focus），再按 Task 0 → Task 11 执行。每个 Task 都可独立提交、独立验收；只有明确写 `Depends on` 的任务才必须顺序执行。

**Goal:** 在已完成的 CompatPack v1.1 基础上，把「v24 单基线 + 可安装方言兼容包」推进到可社区分发、可信任加载、可确定释放、可验证第一方模块包化等价的地基，并为 P-D 内置模块退役与 v2 代码贡献留下明确接口与决策记录。

**Architecture:** 维持 v24 单基线事实源；社区包优先采用「manifest-only 数据包」（程序集 + 内嵌 `compatpack.manifest.json`，无 C# 入口类）；capability 驱动行为激活，投影数据随 `CompatibilityPlan` 会话内聚；pack 程序集由会话级绑定对象持有并确定性 Unload；第一方 v18 以数据包复现内置模块表面，作为 P-D 退役的等价证据。

**Tech Stack:** Godot 4.7 mono、C# net8.0/net9.0-android、xUnit、PowerShell 门禁脚本、`dotnet` SDK 10、AGENTS.md 规定的 Godot headless 构建与三冒烟。

**Spec:** `docs/designs/compat-pack-interface.md`（§1–§13，权威设计）；`governance/evolution-log/2026-10-02-compat-pack-data-packs-and-capability-activation.md`（v1.1 已落地基线）；`docs/ERBAPI.md` §3–§5（执行路径与不变量）。

---

## 0. 已完成基线（不要重复实现）

以下能力已在当前工作区落地并通过验证；本计划的所有后续任务都建立在此之上。

| 已落地能力 | 证据 |
| --- | --- |
| 默认路径会话退出 P0 修复：Back/Restart/ERB 重启后无条件清理 plan 绑定 | `scripts/EmueraMain.cs:406-438`；`scripts/GodotHost/LegacySessionBackend.cs:139-147` |
| manifest-only 数据包：无 `ICompatPack` 入口类时按数据包加载 | `src/Core/Compatibility/Packs/CompatPackLoader.cs:231-291`；真实夹具 `tests/xUnitTest/DataOnlyCompatPackFixture/` |
| manifest `surface` add/hide 名单 + `baseProfileId` | `src/EmueraFacade/Compatibility/Packs/CompatPackManifest.cs:37-110,163-166`；`compatpack.manifest.schema.json` |
| `DialectPlan.PackModuleIds` / `VariantSelections` plan 内聚，删除 process-wide 静态投影 | `src/Core/Compatibility/DialectRuntime.cs:283-326`；`src/Core/Compatibility/Packs/CompatPackPlanAssembler.cs:195-221` |
| capability 激活与 module.Apply 解耦（snake/erafl/megaten policy + snake DFC） | `scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs:190-204,538` |
| `saveProfileId` v1 显式拒载（不再是静默 no-op） | `src/Core/Compatibility/Packs/CompatPackRules.cs:46-52` |
| 测试与门禁基线 | GEmuera.Core.Tests 98/98；EmueraFacade.Tests 35/35；SurfaceSmoke / RuntimeSmoke / CoreContractSmoke 通过；`gemuera-c#.csproj` Release 构建 0 错误 |

**当前不在能力范围内（本计划不承诺已完成）：** v2 代码贡献（自定义 handler / policy 实现）尚未接线；`baseSurfaceHash` 尚未生成/校验；ALC 仍允许 `GEmuera.Core` 与默认解析回落；内置方言模块（v18/snake/erafl/erablue/megaten）尚未退役。

---

## 1. 证据与出处索引

执行任务时优先引用本索引，不要重复在仓库里猜测来源。

### E1 当前代码基线

- 兼容包契约与 manifest：`src/EmueraFacade/Compatibility/Packs/`。
- 加载器 / 校验 / 组装 / 会话宿主：`src/Core/Compatibility/Packs/`、`scripts/Emuera/Compatibility/CompatPackHost.cs`。
- 投影与策略：`scripts/Emuera/Compatibility/LegacyCompatibilityProfile.cs`、`LegacyCompatibilityModules.cs`。
- 计划绑定与清理：`scripts/Emuera/Program.cs:345-410`、`scripts/EmueraMain.cs:406-438`、`scripts/GodotHost/LegacySessionBackend.cs:139-147`。
- 解释器宿主契约：`src/Core/Runtime/ErbExecution.cs`（`IErbInterpreterFactory` / `IErbInterpreterCatalog` / `ErbInterpreterDescriptor` / `ErbInterpreterHost`）。
- 上游对照源码：
  - `E:\MyCode\eraCode\emuera.em-master\Emuera\Runtime\Utils\PluginSystem\*`（v24 + EM + EE）。
  - `E:\MyCode\eraCode\emuera_lazyloading_selfmodified_version-develop-skiasharp\Emuera\Runtime\Utils\PluginSystem\*`（snake fork）。
  - 重点对照：`PluginManager.cs`、`IPluginMethod.cs`、`PluginMethodParameter.cs`、`BasePluginManifest.cs`、`Instraction.Child.cs` 的 CALLSHARP 分支。

### E2 前序架构评审确认的缺口（2026-10-02 三路独立评审）

| 编号 | 缺口 | 证据 |
| --- | --- | --- |
| R1 | ALC 可见面超范围：显式绑定 `GEmuera.Core`，未匹配名 `return null` 落回默认解析 | `src/Core/Compatibility/Packs/CompatPackLoadContext.cs:11,33-34,43-46`；设计 §12.6 自认 [约定而非机制] |
| R2 | 成功路径 pack set 无所有者、无确定性 Unload；v2 代码贡献生命周期无基础 | `scripts/Emuera/Compatibility/CompatPackHost.cs:165-206`；`src/Core/Compatibility/Packs/CompatPackHandle.cs:49-76` |
| R3 | `GEMUERA_COMPAT_PACKS` 外部覆盖可绕过 launcher 按游戏选择；UI 不知情 | `scripts/FirstWindow.cs:665-686`；`scripts/FirstWindow.CompatPackUi.cs` |
| R4 | 游戏键小写化在 Android/Linux 大小写敏感文件系统上有碰撞/错配风险；路径不归一 | `src/Core/Compatibility/Packs/CompatPackLauncherConfig.cs:13-18` |
| R5 | pack 先读字节算哈希、再按路径加载，存在 TOCTOU；哈希可能与实际加载内容不一致 | `src/Core/Compatibility/Packs/CompatPackLoader.cs:49,62` |
| R6 | `baseSurfaceHash` 被解析但零消费；表面版本对账缺失 | `CompatPackManifest.cs` 字段；`CompatPackRules.cs` 无消费者 |
| R7 | v2 代码贡献（`IInstructionVariantContribution` / `IPolicyContribution`）v1 拒载，且 `ICompatInstructionFactory` / `EnginePolicyBinding` 没有可实现的宿主桥 | `CompatPackRules.cs:84-142`；`src/EmueraFacade/Compatibility/Packs/CompatPackVariants.cs` |
| R8 | 现有 Emuera 游戏插件通道与上游二进制不兼容：`PluginManager` 在 `gemuera-c#` 而非 `Emuera` facade，缺少 TypeForwardedTo 与大量 public API | `src/EmueraFacade/Emuera.csproj:3-17`；`gemuera-c#.csproj`；上游 `PluginManager.cs` |
| R9 | P-D 内置模块退役没有第一方包等价证据与 profile→pack 映射设计 | 设计 §8 P-C；当前 `BuiltInDialectCatalog` 仍注册 v18 模块 |

### E3 设计文档章节

- `docs/designs/compat-pack-interface.md` §1（三类扩展严格区分）、§3（manifest 字段）、§4（程序集契约）、§5（加载/隔离/校验/组装）、§6（信任边界）、§7（迁移债）、§8（P-A…P-D）、§12（v1 勘误）、§13（v1.1 增补）。
- `docs/designs/dialect-capability-strategy.md`（能力模块组合、profile 退化为预置、六游戏矩阵）。
- `docs/ERBAPI.md` §2（接口边界）、§3（扩展路径）、§4（不变量，尤其会话所有权、descriptor 与 handler 一致）、§5（实施流程）。

### E4 项目规则与验收基线

- `AGENTS.md`：方言语义改动必须跑三冒烟；Android/APK 为最终验收；文件命名/规模限制；构建与验证命令。
- 门禁命令：
  - `dotnet run --project tools/dialect-inventory/LegacyDialectSurfaceSmoke/LegacyDialectSurfaceSmoke.csproj -c Release`
  - `dotnet run --project tools/dialect-inventory/LegacyDialectRuntimeSmoke/LegacyDialectRuntimeSmoke.csproj -c Release`
  - `$env:DOTNET_ROLL_FORWARD='Major'; dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release; Remove-Item Env:DOTNET_ROLL_FORWARD`
  - `dotnet test tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj -c Release`
  - `dotnet test tests/xUnitTest/EmueraFacade.Tests/EmueraFacade.Tests.csproj -c Release`
  - `dotnet build gemuera-c#.csproj -c Release -t:Rebuild -nodeReuse:false -m:1`
  - 若本机存在 Godot 4.7 mono：`Godot_v4.7-stable_mono_win64_console.exe --headless --path . --build-solutions --quit`

---

## 2. Global Constraints（每个 Task 都隐含遵守）

- 必须先锁定基线再改代码：Core 98/98、Facade 35/35、三冒烟通过、宿主 0 错误。
- 任何方言语义/注册表改动都必须同步跑三冒烟；涉及注册名清单漂移时先跑 `tools/dialect-inventory/LegacyDialectInventoryGenerator` 再跑 legacy-runner 三 profile 冒烟。
- 未启用任何包的会话必须与纯 v24 逐字节等价；包加载失败必须整体回退，不得半加载。
- 兼容包契约程序集 `Emuera` 在 Task 10 拆分前保持 `AssemblyName=Emuera`、`AssemblyVersion=1.824.0.0`；不得改变上游插件 ABI 绑定叙事。
- 新增 C# 文件必须带 `.uid` 侧车（若文件位于 Godot res:// 扫描范围）；`.cs` 文件硬上限 1500 行，既有 >2000 行文件只减不增。
- 禁止把游戏内容、DLL 二进制、导出产物写入项目根或 `res://`；测试夹具只放 `tests/`，派生产物放 `tools/` 或 `$env:TEMP`。
- 禁止使用阶段代号命名新目录/类/命名空间（AGENTS.md）；用描述性命名。
- 提交规范：一个 Task 一个 commit；PR 指向 `dev`，标题/说明用中文；不直接推 `dev` 或主分支。
- 本计划所有新增 public API 必须有 XML 文档与至少一个负向/边界测试。

---

## 3. Review Focus（最容易被使用者踩到的五类输入/失败模式）

1. 恶意/错误依赖的包：包程序集引用 `GEmuera.Core`、`gemuera-c#` 或宿主任意已加载程序集，期望被拒载而不是拿到宿主内部类型。
2. 包文件在读取/校验/加载之间被替换：期望 plan 哈希、manifest 与真实加载的程序集内容一致。
3. 同一游戏目录的不同大小写/路径别名：在 Android/Linux 上期望 A 游戏的包选择不会套到 B 游戏。
4. v18 数据包与内置 v18 profile 表面漂移：期望名称集合逐名相等，差异为零；任何未来生成清单变化会先让测试变红。
5. capability 声明与行为激活不一致：声明了完整 snake capability 集但 DFC/policy 未激活，或未声明却激活；期望以 plan.CapabilityIds 为唯一开关。

---

## 4. Task Map

| Task | 依赖 | 交付物 | 主要验收 |
| --- | --- | --- | --- |
| Task 0 | — | 基线锁定 + 分支 | 基线测试/冒烟全绿 |
| Task 1 | 0 | 社区数据包脚手架 + 契约测试夹具 | 新测试通过；`packs/README.md` |
| Task 2 | 1 | v18 第一方数据包 + 等价测试 | SurfaceSmoke 新增等价门禁通过 |
| Task 3 | 1 | ALC allow-list + 正/反向夹具 | 宿主绑定探针包拒载；契约包加载 |
| Task 4 | 2,3 | 会话持有 `CompatPackSession` + 确定性 Unload | Session 单测 + 宿主门禁 |
| Task 5 | 0 | bytes/hash TOCTOU 修复 | 替换文件测试 |
| Task 6 | 0 | `baseSurfaceHash` 生成/校验 | 规则单测 + 冒烟 |
| Task 7 | 0 | env/path/runner 通道硬化 | launcher 测试 + 平台路径单测 |
| Task 8 | 2,4 | P-D profile→pack 退役 spec（spike） | spec 评审通过 |
| Task 9 | 4 | v2 代码贡献契约 spec（spike） | spec 评审通过 |
| Task 10 | 3,4 | 契约程序集拆分 + 插件边界 | 构建/门禁全绿 |
| Task 11 | 0 | P0 静态回归守卫 + 文档/治理同步 | 守卫脚本接入验收清单 |

---

### Task 0: 基线锁定与工作分支

**Depends on:** 无。

**Files:**
- 无代码修改。

**Interfaces:**
- Consumes: 当前工作区代码。
- Produces: 可提交分支与基线记录（写入本计划执行日志或 PR 描述）。

**Evidence / Sources / Solution:**
- E4、E3。
- Solution：当前工作区没有 `.git`（已观察），必须先恢复仓库工作树；按 `AGENTS.md` 从最新 `dev` 建分支 `ai/compatpack-community-foundation`。不要在非 git 环境直接改文件。

**Steps:**
- [ ] **Step 1:** 运行 `git rev-parse --is-inside-work-tree`，期望输出 `true`。若失败，停止并让 owner 恢复 `.git`（bundle/远端 checkout）。
- [ ] **Step 2:** 运行 `git checkout dev`、`git pull --ff-only origin dev`，期望成功。
- [ ] **Step 3:** 运行 `git checkout -b ai/compatpack-community-foundation`，期望成功。
- [ ] **Step 4:** 运行 `dotnet test tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj -c Release`，期望 `已通过! ... 98`，失败 0。
- [ ] **Step 5:** 运行 `dotnet test tests/xUnitTest/EmueraFacade.Tests/EmueraFacade.Tests.csproj -c Release`，期望 `已通过! ... 35`，失败 0。
- [ ] **Step 6:** 运行 E4 中三条冒烟命令，期望全部 `passed`。
- [ ] **Step 7:** 运行 `dotnet build gemuera-c#.csproj -c Release -t:Rebuild -nodeReuse:false -m:1`，期望 `0 个错误`；允许既有警告（KoujouPrefetchCache / AgentLlmMethods）。
- [ ] **Step 8:** 把 Step 4-7 的原始输出摘要写入 PR 描述，不提交任何文件。

---

### Task 1: 社区数据包脚手架与契约测试夹具

**Depends on:** Task 0。

**Files:**
- Create: `packs/README.md`（社区包唯一入口文档）。
- Create: `packs/CommunityPackTemplate/CommunityPackTemplate.csproj`。
- Create: `packs/CommunityPackTemplate/compatpack.manifest.json`。
- Create: `packs/CommunityPackTemplate/CommunityPackTemplateMarker.cs`（public marker，仅用于测试定位程序集，不是 `ICompatPack` 入口）。
- Create: `tests/xUnitTest/CompatPackContractOnlyFixture/CompatPackContractOnlyFixture.csproj`（只引用 `src/EmueraFacade/Emuera.csproj`，用于 Task 3 ALC 正向夹具）。
- Create: `tests/xUnitTest/CompatPackContractOnlyFixture/ContractOnlyPack.cs`（唯一 `ICompatPack` 入口 + 一个 `ISurfaceContribution`）。
- Create: `tests/xUnitTest/CompatPackContractOnlyFixture/compatpack.manifest.json`（嵌入式 `LogicalName=compatpack.manifest.json`）。
- Modify: `tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj`（新增两个 ProjectReference）。
- Create: `tests/xUnitTest/GEmuera.Core.Tests/CommunityPackTemplateTests.cs`。
- Modify: `docs/designs/compat-pack-interface.md` §13.8（若示例与本 Task 不一致则同步）。

**Interfaces:**
- Consumes: `CompatPackLoader.TryLoad(string path, CompatPackValidationContext context, out CompatPackHandle? handle, out IReadOnlyList<string> errors)`；`CompatPackManifest.TryLoadFromAssembly(Assembly, out CompatPackManifest?, out IReadOnlyList<string> errors)`。
- Produces: 可被后续 Task/社区复制的模板工程；`CompatPackContractOnlyFixture` 程序集路径可通过 `typeof(ContractOnlyPack).Assembly.Location` 取得。

**Evidence / Sources / Solution:**
- E1（`CompatPackLoader.cs:231-291` manifest-only 分叉）、E2-R1、E3（§13.1、§13.8）。
- Solution：模板工程只内嵌 manifest，不写入口类；契约夹具写最小入口类，供 ALC 正向用例。`packs/README.md` 必须写清：目录约定、manifest 必填字段、构建方式、放入 `<gameRoot>/compat_packs/` 或 Android `/storage/emulated/0/emuera/packs/`、失败回退与日志。

**Steps:**
- [ ] **Step 1: 写失败测试** `CommunityPackTemplateTests.ManifestOnlyTemplate_LoadsAndValidates()`：加载 `typeof(CommunityPackTemplateMarker).Assembly.Location`，断言 `handle.Pack` 为 `ManifestOnlyCompatPack`、`handle.Surface` 数量 >= 1、`handle.Manifest.PackId == "community.template"`、`errors` 为空。
- [ ] **Step 2: 运行失败测试** `dotnet test tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj -c Release --filter FullyQualifiedName~CommunityPackTemplateTests`，期望编译失败或 FAIL（模板工程不存在）。
- [ ] **Step 3: 创建 `packs/CommunityPackTemplate`**：`csproj` 用 `<EmbeddedResource Include="compatpack.manifest.json" LogicalName="compatpack.manifest.json" />`，加一个 public `CommunityPackTemplateMarker` 类用于定位程序集；manifest 使用 `packId=community.template`、`baseProfileId=v24pure`、`surface.addInstructions=["SETANIMETIMER"]`、`capabilities=["math.times-clamp.v1"]`。
- [ ] **Step 4: 创建 `CompatPackContractOnlyFixture`**：只引用 `Emuera` 契约程序集；入口类实现 `ICompatPack` + `ISurfaceContribution`；manifest 字段使用 `packId=community.contract-fixture`。
- [ ] **Step 5: 写契约夹具测试** `ContractOnlyFixture_LoadsAndSurfaceApplies()`：加载夹具程序集，断言 `handle.Surface` 中包含其 `ISurfaceContribution`，`handle.Pack` 不是 `ManifestOnlyCompatPack`。
- [ ] **Step 6: 运行两条测试**，期望 PASS；再跑 Task 0 基线测试，期望 Core 100/100（98+2）、Facade 35/35。
- [ ] **Step 7: 补 `packs/README.md` 与设计文档同步**，运行 `powershell -File tools/doc-guards/Invoke-DocGuard.ps1`（若存在），期望通过。
- [ ] **Step 8: Commit**：`git add packs tests/xUnitTest docs/designs/compat-pack-interface.md`；`git commit -m "feat: add community compat-pack template and contract-only fixture"`。

---

### Task 2: v18 第一方数据包与等价门禁

**Depends on:** Task 1。

**Files:**
- Create: `packs/gemuera.v18/gemuera.v18.csproj`（无 `ICompatPack` 入口类，只内嵌 manifest + marker）。
- Create: `packs/gemuera.v18/GemueraV18PackMarker.cs`（public marker，仅用于测试定位程序集）。
- Create: `packs/gemuera.v18/compatpack.manifest.json`（生成物，禁止手改）。
- Create: `tools/compat-pack/Generate-V18PackManifest/Generate-V18PackManifest.csproj`。
- Create: `tools/compat-pack/Generate-V18PackManifest/Program.cs`。
- Modify: `tools/dialect-inventory/LegacyDialectSurfaceSmoke/Program.cs`（新增 `AssertV18PackEquivalence()` 与调用点）。
- Create: `tests/xUnitTest/GEmuera.Core.Tests/V18PackManifestTests.cs`。
- Modify: `tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj`（引用 `packs/gemuera.v18`）。
- Modify: `docs/designs/compat-pack-interface.md` §13（记录 v18 包等价证据）。

**Interfaces:**
- Consumes:
  - `LegacyDialectInventories.V24InstructionNames: string[]`、`V24Functions: LegacyFunctionInventoryEntry[]`、`V18InstructionNames: string[]`、`V18Functions: LegacyFunctionInventoryEntry[]`。
  - `BuiltInDialectCatalog.CreateLegacySessionPlan("v18")`、`BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure")`。
  - `CompatPackLoader.TryLoad`、`CompatPackPlanAssembler.TryAssemble`。
- Produces:
  - `packs/gemuera.v18` 产物 DLL 路径；
  - 生成器输出规范化 JSON（排序、LF、UTF-8 无 BOM）；
  - SurfaceSmoke 新增门禁：`v24pure + v18 pack` 的指令/函数键集合与内置 `v18` profile 逐名相等。

**Evidence / Sources / Solution:**
- E1（生成清单 `src/Core/Compatibility/LegacyDialectInventories.Generated.cs`）、E2-R9、E3（§8 P-C）。
- Solution：生成器从 `LegacyDialectInventories` 计算：
  - `hideInstructions = V24InstructionNames.Except(V18InstructionNames)`（Ordinal，排序）。
  - `hideFunctions = V24Functions.Select(Name).Except(V18Functions.Select(Name))`（Ordinal，排序）。
  - `addInstructions = V18InstructionNames.Except(V24InstructionNames)`（预期为空；若非空必须显式处理）。
  - `addFunctions = V18Functions.Select(Name).Except(V24Functions.Select(Name))`（预期为空）。
- 断言：`v18` 内置 profile 的 `Dialect.Instructions.Keys` 与 `Dialect.Functions.Keys`，等于 `v24pure + v18 pack` 组装后的对应键集合。
- 说明：不要硬编码 `LegacyV18CompatibilityModule.V24OnlyInstructionNames`；生成清单是唯一事实源，模块列表仅作交叉诊断。

**Steps:**
- [ ] **Step 1: 写失败核心测试** `V18PackManifestTests.GeneratedManifest_ReproducesBuiltinV18Surface()`：加载 v18 包程序集，`CompatPackLoader.TryLoad`，`CompatPackPlanAssembler.TryAssemble(v24pure, handle)`，比较 `plan.Dialect.Instructions.Keys` / `Functions.Keys` 与 `BuiltInDialectCatalog.CreateLegacySessionPlan("v18")` 对应键集合（`Assert.Equal` 排序数组）。
- [ ] **Step 2: 运行失败测试**，期望因 `packs/gemuera.v18` 工程不存在而编译失败。
- [ ] **Step 3: 实现生成器**，运行 `dotnet run --project tools/compat-pack/Generate-V18PackManifest -- <repo-root>`，生成 `packs/gemuera.v18/compatpack.manifest.json`；生成器退出码非 0 表示发现 `add*` 非空或清单漂移。
- [ ] **Step 4: 创建 v18 包工程**，构建 `dotnet build packs/gemuera.v18/gemuera.v18.csproj -c Release`，期望 0 错误。
- [ ] **Step 5: 运行核心测试**，期望 PASS；再跑 `dotnet test ... --filter FullyQualifiedName~V18PackManifestTests`。
- [ ] **Step 6: 在 SurfaceSmoke 中加等价门禁** `AssertV18PackEquivalence()`：同上比较，但断言的是 `LegacyCompatibilityProfile` 的指令/函数可见集合；在 `Main` 中 `AssertPackProjection()` 之后调用。
- [ ] **Step 7: 跑三冒烟 + Core 测试**，期望全绿；若 `V18InstructionNames` 与模块 hide 列表存在 `OUTPUTLOG` 等历史差异，必须在生成器/测试中显式对齐而不是放宽容差。
- [ ] **Step 8: Commit**：`git add packs/gemuera.v18 tools/compat-pack tests tools/dialect-inventory docs`；`git commit -m "feat: add first-party v18 data pack and surface equivalence gate"`。

---

### Task 3: ALC allow-list 硬化

**Depends on:** Task 1。

**Files:**
- Modify: `src/Core/Compatibility/Packs/CompatPackLoadContext.cs`。
- Create: `tests/xUnitTest/CompatPackHostBindingProbe/CompatPackHostBindingProbe.csproj`（引用 `GEmuera.Core` + `Emuera`，故意引用宿主内部类型）。
- Create: `tests/xUnitTest/CompatPackHostBindingProbe/HostBindingProbePack.cs`（实现 `ICompatPack`，其方法体或 Contributions getter 访问 `GEmuera.Core.Compatibility.CompatibilityPlan` 类型，制造绑定需求）。
- Create: `tests/xUnitTest/CompatPackHostBindingProbe/compatpack.manifest.json`。
- Modify: `tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj`（引用探针工程）。
- Create: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackAlcIsolationTests.cs`。
- Modify: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackLoaderTests.cs`（把正向加载样本从测试程序集自身改为 Task 1 的 `CompatPackContractOnlyFixture`）。
- Modify: `docs/designs/compat-pack-interface.md` §5.2/§12.6（把约定改为机制并记录实测）。

**Interfaces:**
- Consumes: `CompatPackLoadContext.Load(AssemblyName)`；`CompatPackLoader.TryLoad`。
- Produces: 硬化后的绑定规则：
  1. `Emuera`/`emuera` -> 契约程序集（不变）；
  2. `netstandard`/`System.*`/`Microsoft.*` -> 默认 ALC 已加载实例（不变）；
  3. 包目录 `<name>.dll` -> 仅在该 ALC 内加载（不变）；
  4. 其他名字 -> 抛出 `FileLoadException`，不再返回 null 让默认解析接手；`GEmuera.Core` 不再显式返回宿主 Core。

**Evidence / Sources / Solution:**
- E2-R1；`CompatPackLoadContext.cs:11,33-34,43-46`；设计 §6、§12.6。
- Solution：ALC 只暴露契约程序集 + BCL + 包目录依赖；宿主内部程序集不可见。若包确实需要 Core 数据类型，必须通过契约程序集暴露或签新契约，不能靠 ALC 回落。
- 注意：`CompatPackHostBindingProbe` 的构建产物必须能被 Core.Tests 定位（ProjectReference + `typeof(HostBindingProbePack).Assembly.Location`）。

**Steps:**
- [ ] **Step 1: 写失败测试** `Alc_HostBindingProbePack_IsRejected()`：`CompatPackLoader.TryLoad(probePath, RealContext(), out handle, out errors)` 期望 `false`、`handle is null`、`errors` 含 `FileLoadException` 或 `GEmuera.Core` 诊断。
- [ ] **Step 2: 运行失败测试**，期望 FAIL（当前返回 null 落回默认解析，探针可能加载成功）。
- [ ] **Step 3: 写失败测试** `Alc_ContractOnlyFixture_Loads()`：确认 Task 1 契约夹具加载成功、`handle.Surface` 非空。
- [ ] **Step 4: 修改 `CompatPackLoadContext.Load`**，删除 `coreAssembly` 字段与 `GEmuera.Core` 分支，末尾改为抛出 `FileLoadException`（消息说明只允许 Emuera 契约与包目录依赖）。
- [ ] **Step 5: 运行两条新测试 + 全量 Core 测试**，期望 `Alc_*` 通过；原 `CompatPackLoaderTests` 切换到契约夹具后仍全绿。
- [ ] **Step 6: 运行三冒烟 + 宿主构建**，期望全绿；更新设计文档 §5.2/§12.6 与治理记录。
- [ ] **Step 7: Commit**：`git add src/Core tests docs/designs governance`；`git commit -m "feat: harden compat-pack ALC to contract-only allow-list"`。

---

### Task 4: 会话持有 pack set 与确定性 Unload

**Depends on:** Task 2、Task 3。

**Files:**
- Create: `src/Core/Compatibility/Packs/CompatPackSession.cs`。
- Modify: `src/Core/Compatibility/Packs/CompatPackHandle.cs`（`CompatPackHandle : ICompatPackLease`；`CompatPackSet : ICompatPackLease` 同文件）。
- Create: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackSessionTests.cs`。
- Modify: `scripts/Emuera/Compatibility/CompatPackHost.cs`：`ConfigureForLaunch` 返回 `CompatPackLaunchResult`。
- Modify: `scripts/Emuera/Program.cs`（保存/释放 session）。
- Modify: `scripts/EmueraMain.cs`（无包/早绑定/失败路径仍走 `ClearCompatibilityPlan`）。
- Modify: `scripts/GodotHost/LegacySessionBackend.cs`（清理路径统一）。
- Modify: `docs/designs/compat-pack-interface.md` §5.4/§12.6/§13.7。
- Create（可选，若本机有 Godot 4.7）：`tests/GDUnit4Test/GodotHost/CompatPackSessionLifecycleTest.cs`。

**Interfaces:**
- Consumes: `CompatPackLoader.TryLoadSet`、`CompatPackSet`、`CompatPackSession`。
- Produces:
  - `public interface ICompatPackLease { void Unload(); }`
  - `public sealed class CompatPackSession : IDisposable`，构造 `CompatPackSession(CompatibilityPlan plan, IReadOnlyList<CompatPackHandle> handles)`，属性 `Plan`、`Handles`、`IsDisposed`，`Dispose()` 幂等且对每个 handle 调用一次 `Unload`。
  - `internal sealed record CompatPackLaunchResult(CompatibilityPlan Plan, CompatPackSession? Session)`。
- `CompatPackHandle` 与 `CompatPackSet` 实现 `ICompatPackLease`；`CompatPackSet.UnloadAll()` 对每个 handle 调用 `Unload`。
- `Program` 新增：
  - `internal static void ConfigureCompatibilityPlan(CompatibilityPlan plan, CompatPackSession? packSession = null);`
  - `internal static void ClearCompatibilityPlan();` 先释放 session，再清 plan/profile；
  - `internal static CompatPackSession? CurrentCompatPackSession { get; }`。
- `CompatPackHost`：成功后把已加载 `CompatPackSet` 包装为 `CompatPackSession` 一并返回；失败路径 `UnloadAll` 并返回 `new CompatPackLaunchResult(baselinePlan, null)`。

**Evidence / Sources / Solution:**
- E2-R2；设计 §5.4/§12.6；`CompatPackHost.cs:165-206`；`Program.cs:345-410`。
- Solution：plan 是纯数据，但 v2 代码贡献需要 ALC/delegate 存活。把 pack set 的所有权从 GC 猜测改为会话绑定对象，Stop/Restart/退出时确定性 UnloadAll；本 Task 只新增 session 静态，Task 10 可进一步收敛为单一 binding。

**Steps:**
- [ ] **Step 1: 写失败单测** `CompatPackSessionTests.Dispose_UnloadsEachLeaseOnceIdempotently()`：用测试内 `CountingLease : ICompatPackLease` 构造 session，调用 `Dispose()` 两次，断言每个 lease `UnloadCount == 1`。
- [ ] **Step 2: 运行失败测试**，期望编译失败（类型不存在）。
- [ ] **Step 3: 实现 `CompatPackSession`**，接口签名如 Interfaces；`Dispose` 用 `Interlocked.Exchange` 保证幂等。
- [ ] **Step 4: 让 `CompatPackHandle`/`CompatPackSet` 实现 `ICompatPackLease`**（`Unload()` / `UnloadAll()` 已存在，只需接口声明）。
- [ ] **Step 5: 运行单测**，期望 PASS；再跑全量 Core 测试。
- [ ] **Step 6: 改 `CompatPackHost.ConfigureForLaunch` 返回 `CompatPackLaunchResult`**；成功路径把 `set` 包成 session；失败路径显式 `UnloadAll` 后返回 null session。
- [ ] **Step 7: 改 `Program` 持有/释放 session**：`ConfigureCompatibilityPlan` 增加可选参数；`ClearCompatibilityPlan` 先 `session?.Dispose()`，再清 plan/profile；`ResetSessionState` 同理。
- [ ] **Step 8: 改 `Program.Main`/`EmueraMain`/`LegacySessionBackend` 的调用点**，保证 Back/Restart/失败重试都会 `ClearCompatibilityPlan`；补日志 `[LOAD] CompatPack session unloaded: count=N`。
- [ ] **Step 9: 运行三冒烟 + Core/Facade 测试 + 宿主构建**，期望全绿。
- [ ] **Step 10: Commit**：`git add src/Core scripts tests docs`；`git commit -m "feat: own compat-pack assemblies per session and unload deterministically"`。

---

### Task 5: pack 文件读取与哈希 TOCTOU 修复

**Depends on:** Task 0。

**Files:**
- Modify: `src/Core/Compatibility/Packs/CompatPackLoader.cs`。
- Modify: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackLoaderTests.cs`（新增测试）。

**Interfaces:**
- Consumes: 现有 `CompatPackLoader.TryLoad`。
- Produces:
  - `internal static Func<string, byte[]>? BytesReaderOverrideForTest { get; set; }`（仅测试 seam，默认 null 走 `File.ReadAllBytes`）。
  - `TryLoad` 内部只读一次字节，计算 `assemblySha256` 与 `packSha256`，并用 `loadContext.LoadFromStream(new MemoryStream(assemblyBytes))` 加载；`PackAssemblyPath` 仅作诊断/依赖目录来源。

**Evidence / Sources / Solution:**
- E2-R5；`CompatPackLoader.cs:49,62`。
- Solution：同一份 `byte[]` 既是哈希输入也是加载输入，消除哈希旧内容、加载新内容的窗口；`CompatPackLoadContext` 仍用 `packDirectory` 解析包目录依赖，不影响 ALC 规则。

**Steps:**
- [ ] **Step 1: 写失败测试** `TryLoad_HashesAndLoadsSameBytes_EvenIfFileChanges()`：使用 `BytesReaderOverrideForTest` 返回一份合法包字节（从 Task 1 契约夹具读取一次缓存），同时把磁盘测试路径写成另一份非法/不同 manifest 的 DLL；调用 `TryLoad`，断言 handle 的 `Manifest.PackId` 来自 override 字节、`PackSha256` 等于对 override 字节计算的哈希。
- [ ] **Step 2: 运行失败测试**，期望 FAIL（当前先读 override 再按路径加载磁盘文件，manifest 会不一致）。
- [ ] **Step 3: 修改 `TryLoad`**，增加 `ReadBytes` helper；`LoadFromStream` 后校验 `assembly.GetName()` 等不依赖磁盘；确保异常路径仍 `Unload()`。
- [ ] **Step 4: 运行新测试 + 全量 Core 测试**，期望 PASS。
- [ ] **Step 5: 运行三冒烟 + 宿主构建**，期望全绿；`CompatPackLoadContext` 的 `packDirectory` 仍指向原路径的目录，依赖仍在同目录解析。
- [ ] **Step 6: Commit**：`git add src/Core/Compatibility/Packs tests`；`git commit -m "fix: hash and load compat-pack from the same bytes"`。

---

### Task 6: baseSurfaceHash 生成与校验

**Depends on:** Task 0。

**Files:**
- Create: `src/Core/Compatibility/Packs/LegacySurfaceHash.cs`。
- Modify: `src/Core/Compatibility/Packs/CompatPackValidation.cs`（新增 `BaselineSurfaceHash` 属性/构造参数）。
- Modify: `src/Core/Compatibility/Packs/CompatPackRules.cs`（新增 mismatch 拒绝）。
- Modify: `scripts/Emuera/Compatibility/CompatPackHost.cs`（`BuildValidationContext` 传入 v24 表面哈希）。
- Modify: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackRulesTests.cs`。
- Modify: `tests/xUnitTest/EmueraFacade.Tests/CompatPackManifestTests.cs`（保留 BaseSurfaceHash 解析测试）。
- Modify: `docs/designs/compat-pack-interface.md` §3.2/§12（baseSurfaceHash 从提示升级为 v1 精确对账）。

**Interfaces:**
- Consumes: `LegacyDialectInventories.V24InstructionNames`、`V24Functions`。
- Produces:
  - `public static class LegacySurfaceHash { public static string ComputeV24SurfaceHash(); }`，返回 v24 基线表面 SHA256 小写十六进制 64 字符。
  - `CompatPackValidationContext` 构造新增 `string? baselineSurfaceHash = null`，缺省 `LegacySurfaceHash.ComputeV24SurfaceHash()`。
  - `CompatPackRules.Validate`：若 `manifest.BaseSurfaceHash is not null` 且不等于 `context.BaselineSurfaceHash`，加入错误 `baseSurfaceHash 与当前 v24 表面快照不一致`。

**Evidence / Sources / Solution:**
- E2-R6；设计 §3.2/§12 勘误 2；`CompatPackManifest.BaseSurfaceHash`。
- Solution：规范化算法：按 Ordinal 排序 `V24InstructionNames`，再按 `Name|ReturnType` 排序 `V24Functions`，拼 `instruction=...\nfunction=...\n`，SHA256 小写；测试用固定期望 hash 钉住，生成清单变化时测试先红，必须走 generator 流程更新。

**Steps:**
- [ ] **Step 1: 写失败测试** `LegacySurfaceHash_V24_IsStable()`：断言 `ComputeV24SurfaceHash()` 等于一个仓库内固定 64 位十六进制常量（生成清单不变时不变）。
- [ ] **Step 2: 运行失败测试**，期望编译失败。
- [ ] **Step 3: 实现 `LegacySurfaceHash`** 与 Context 属性；先打印/断点得到期望 hash，写入测试常量。
- [ ] **Step 4: 写失败测试** `Validate_BaseSurfaceHashMismatch_Rejects()`：手工 manifest 携带一个非 null 且不匹配的 `baseSurfaceHash`，期望 error 命中 `baseSurfaceHash`。
- [ ] **Step 5: 实现 rules mismatch 拒载**，运行两条测试通过。
- [ ] **Step 6: `CompatPackHost.BuildValidationContext` 传入 v24 hash**；运行三冒烟 + Core/Facade 测试 + 宿主构建。
- [ ] **Step 7: Commit**：`git add src scripts tests docs`；`git commit -m "feat: validate compat-pack baseSurfaceHash against v24 snapshot"`。

---

### Task 7: env / 路径 / runner 通道硬化

**Depends on:** Task 0。

**Files:**
- Modify: `src/Core/Compatibility/Packs/CompatPackLauncherConfig.cs`。
- Modify: `scripts/FirstWindow.cs`、`scripts/FirstWindow.CompatPackUi.cs`。
- Modify: `scripts/Emuera/Compatibility/CompatPackHost.cs`（如需要会话级输入）。
- Modify: `scripts/GodotHost/LegacySessionBackend.cs`（runner/in-process switch 路径）。
- Modify: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackLauncherConfigTests.cs`。
- Modify: `docs/designs/compat-pack-interface.md` §6（无全局生效）。

**Interfaces:**
- Consumes: `CompatPackLauncherConfig.NormalizeGameKey`、`TryGetSelectionForGame`、`FirstWindow.ApplyCompatPackEnvironment`。
- Produces:
  - `public static StringComparer GameKeyComparer { get; }`：Windows 用 `OrdinalIgnoreCase`；非 Windows 用 `Ordinal`。
  - `public static string NormalizeGameKey(string? gameRoot, bool caseSensitiveFilesystem)`：非大小写敏感平台保留大小写、统一路径分隔符、去尾斜杠；大小写敏感平台再 lowercase。
  - `public static string NormalizePackPath(string path)`：`Path.GetFullPath` + 统一分隔符 + 去尾斜杠。
  - `FirstWindow.ApplyCompatPackEnvironment` 不再因外部 env 永久早退；launcher 按游戏选择优先，外部 env 仅在无选择时作为诊断输入并弹出 UI 警告。
  - `LegacySessionBackend.ApplyLaunchConfiguration` / `FirstWindow.ConfigureLegacyRunnerSession` 接收/刷新 per-session pack 路径（至少保证 runner switch 时不沿用上一局 env）。

**Evidence / Sources / Solution:**
- E2-R3、R4；`FirstWindow.cs:665-686`；`CompatPackLauncherConfig.cs:13-18`。
- Solution：Android 游戏目录可能只差大小写，不能共用配置键；先按平台比较器修 key，再修 UI/env 覆盖语义。所有路径比较统一走 `NormalizePackPath`。

**Steps:**
- [ ] **Step 1: 写失败测试** `NormalizeGameKey_CaseSensitivePlatform_PreservesCase()`：断言 `/tmp/GameA` 与 `/tmp/gamea` 在 caseSensitiveFilesystem=true 时产生不同 key。
- [ ] **Step 2: 写失败测试** `TryGetSelectionForGame_RejectsCaseMismatchOnCaseSensitive()`：存储键为 `/tmp/GameA`，查询 `/tmp/gamea` 返回 false。
- [ ] **Step 3: 运行失败测试**，期望 FAIL（当前统一 `ToLowerInvariant`）。
- [ ] **Step 4: 实现平台语义 API**，保留原方法签名为兼容包装（Windows 默认），新增显式 caseSensitive 参数；更新 UI 去重与 `SaveCurrentEditForLoadedGame` 使用 `NormalizePackPath`。
- [ ] **Step 5: 写失败测试** `ExternalEnvOverride_DoesNotPermanentlyShadowLauncherSelection()`：把 env 合并逻辑抽成 `CompatPackLauncherConfig.MergeSelection(stored, external)` 并直测；断言 stored 非空时优先。
- [ ] **Step 6: 实现 env 合并语义与 UI 警告**，运行 launcher 测试通过。
- [ ] **Step 7: runner/in-process switch**：在 `LegacySessionLaunchConfiguration` 或 switch 调用点刷新 per-session pack 路径；补测试断言不沿用上一局。
- [ ] **Step 8: 运行三冒烟 + 全部测试 + 宿主构建**；手机路径行为若无法本机验证，必须在 PR/执行记录标 `待 Android 实测`，不得宣称已通过。
- [ ] **Step 9: Commit**：`git add src scripts tests docs`；`git commit -m "fix: make compat-pack selection path-correct and session-scoped"`。

---

### Task 8: P-D profile→pack 退役设计（Spike，不删模块）

**Depends on:** Task 2、Task 4。

**Files:**
- Create: `docs/plans/2026-10-03-pd-module-retirement-spec.md`。
- Create: `packs/pack-profile-map.example.json`（示例映射，不参与运行）。
- Modify: `docs/designs/compat-pack-interface.md` §8（链接 spec）。

**Interfaces:**
- Consumes: Task 2 的 v18 包等价证据；`CompatibilityProfileCatalog`/`BuiltInDialectCatalog` 的 profile 注册；launcher 的 profile 选择与 pack 选择。
- Produces: 一份可执行 spec，至少回答：
  1. profile（v18/snake/erafl/erablue/megaten）到第一方 packId/path 的映射存储位置与优先级；
  2. 未安装对应 pack 时的回退策略（保留内置模块 vs 纯 v24 + 诊断）；
  3. 退役顺序（建议 v18、megaten、erablue、erafl、snake）与每步验收矩阵；
  4. `BuiltInDialectCatalog`/`LegacyCompatibilityModules`/`expectedModuleClosures` 的删除清单；
  5. 六游戏启动矩阵与快照名录零差异的验证入口。

**Evidence / Sources / Solution:**
- E2-R9；E3 §8；Task 2 等价测试。
- Solution：本 Task 是 spike/设计交付，不修改运行时注册表。只有 spec 通过用户评审后，才另开计划执行退役 Task。这是防止在 launcher 映射与六游戏矩阵未就绪时误删内置模块。

**Steps:**
- [ ] **Step 1:** 基于 Task 2 测试输出，列出 v18 内置 profile 与 pack 的逐名差异（期望为空）。
- [ ] **Step 2:** 在 spec 中定义 `pack-profile-map` 的 JSON schema 与加载优先级；至少覆盖 `profileId`、`packId`、`relativePath`、`requiredPackVersion`。
- [ ] **Step 3:** 定义 pack 缺失/版本不符/身份不符三类回退路径与用户可见文案。
- [ ] **Step 4:** 定义每步退役的 red/green 门禁：三冒烟 + 六游戏矩阵 + `dialect-registry-snapshots.json` 对比零差异。
- [ ] **Step 5:** 运行 `dotnet test tests/xUnitTest/GEmuera.Core.Tests -c Release` 与三冒烟，确认本 Task 未改代码、基线仍绿。
- [ ] **Step 6: Commit**：`git add docs/plans packs/pack-profile-map.example.json docs/designs`；`git commit -m "docs: specify pd module retirement mapping and gates"`。

---

### Task 9: v2 代码贡献契约设计（Spike）

**Depends on:** Task 4。

**Files:**
- Create: `docs/plans/2026-10-03-compatpack-v2-code-contributions-spec.md`。
- 可选：Modify `src/EmueraFacade/Compatibility/Packs/CompatPackVariants.cs` 注释（若 spec 改变公开契约语义）。

**Interfaces:**
- Consumes: `IInstructionVariantContribution` / `ICompatInstructionFactory` / `IPolicyContribution` / `EnginePolicyBinding`；`FunctionIdentifier.CreateProfileInstruction`；`AbstractInstruction` 的 internal 可见性；Task 4 的 `CompatPackSession`。
- Produces: 一份决策 spec，必须回答：
  1. `ICompatInstructionFactory.CreateInstruction()` 返回的 `object` 如何被宿主收窄；`AbstractInstruction` 是否保持 internal，还是提供公开宿主基类/适配器；
  2. 工厂对象生命周期：由 `CompatPackSession` 持有 handle，`FunctionIdentifier` 投影时如何拿 factory map；
  3. `EnginePolicyBinding(string CapabilityId)` 的实现载体如何补全（策略工厂接口、生命周期、异常/失败语义）；
  4. v2 的 fail-closed 校验规则与版本门槛（`targetEngineApi` 递增策略）；
  5. 测试方案：至少一个 legacy-runner fixture 证明自定义 handler 可被 ERB 调用，且禁用包后逐字节回退。

**Evidence / Sources / Solution:**
- E2-R7；E3 §4/§5；`src/EmueraFacade/Compatibility/Packs/CompatPackVariants.cs`；`scripts/Emuera/GameProc/Function/FunctionIdentifier.cs:129-148`。
- Solution：spike 期间禁止修改 `CompatPackRules` 的 v1 拒载（保持 fail-closed）。只有当 spec 决定宿主侧编组 DLL/桥程序集方案后，才另开实现计划。

**Steps:**
- [ ] **Step 1:** 阅读并记录 `FunctionIdentifier.CreateProfileInstruction` 当前枚举替换路径与 `AbstractInstruction` 可见性。
- [ ] **Step 2:** 在 spec 中给出两个候选方案（A：`InternalsVisibleTo` + 宿主桥程序集；B：新增公开宿主适配器接口），列 trade-off、版本影响、安全影响，给出推荐。
- [ ] **Step 3:** 为推荐方案定义精确 signature 与失败/回退语义；为另一方案写为何不选。
- [ ] **Step 4:** 定义 v2 测试清单与 legacy-runner fixture 路径；确认 v1 拒载保持。
- [ ] **Step 5:** 运行 Core/Facade 测试与三冒烟，确认 spike 无生产代码变更；提交 spec。
- [ ] **Step 6: Commit**：`git add docs/plans src/EmueraFacade`；`git commit -m "docs: specify compat-pack v2 code contribution contract"`。

---

### Task 10: 契约程序集拆分与插件边界（架构任务，高风险）

**Depends on:** Task 3、Task 4。

**Files:**
- Create: `src/EmueraCompatPack/EmueraCompatPack.csproj`（暂定名，最终名由实现者按项目命名规范决定；不要用阶段代号）。
- Move: `src/EmueraFacade/Compatibility/Packs/*` 到新契约程序集；`Emuera.csproj` 只保留上游插件 ABI 文件（`IPluginMethod.cs`、`PluginManifestAbstract.cs`、`PluginMethodParameter.cs`）。
- Modify: `src/Core/GEmuera.Core.csproj`、`gemuera-c#.csproj`、所有包/测试 csproj 的 ProjectReference。
- Modify: `CompatPackLoadContext.Load` 的契约程序集绑定名列表。
- Create: `tests/xUnitTest/EmueraPluginAbi.Tests`（验证 facade 不再含 `ICompatPack`，但插件 ABI 仍在）。
- Modify: `docs/designs/compat-pack-interface.md` §4/§10.1、`docs/ERBAPI.md` §3。

**Interfaces:**
- Consumes: `ICompatPack` 及其贡献接口、`CompatPackManifest`、`CompatPackSurface`。
- Produces:
  - 新契约程序集 `AssemblyName`（推荐 `Emuera.CompatPack` 或 `gEmuera.CompatPack.Contracts`；由实现者做 ADR 并写入 spec）。
  - `CompatPackLoadContext.Load` 中 `Emuera`/`emuera` 绑定保持向后兼容（旧数据包若只引用历史 `Emuera` 契约，必要时提供 TypeForwardedTo 转发层）。
  - `EmueraFacade` 仍只承载上游插件 ABI，`AssemblyVersion=1.824.0.0` 不变。

**Evidence / Sources / Solution:**
- E2-R8、R1；设计 §4 推荐落点、§10.1；`src/EmueraFacade/Emuera.csproj:3-17`。
- Solution：拆分后 CompatPack 契约可独立语义版本演进，插件 ABI 不再被包契约演进牵制；ALC allow-list 绑定新契约程序集。这是高风险任务，必须先写 ADR/spec，再分步迁移，每步跑三冒烟 + 插件 ABI 测试。

**Steps:**
- [ ] **Step 1: 写 ADR/spec**（可并入 Task 9/10 的 docs/plans），确定新 AssemblyName、TypeForwardedTo 策略、旧包兼容窗口、版本策略。
- [ ] **Step 2: 写失败测试** `Facade_DoesNotExposeCompatPackContracts()`：反射 `typeof(IPluginMethod).Assembly`，断言不存在 `Emuera.Compatibility.Packs.ICompatPack` 类型。
- [ ] **Step 3: 迁移契约文件** 到新工程，更新所有 ProjectReference；`Emuera` facade 可保留 TypeForwardedTo 到新程序集（过渡期）。
- [ ] **Step 4: 更新 ALC 绑定规则**：`CompatPackLoadContext` 绑定新契约程序集；若保留旧绑定，必须同时支持旧包并加测试。
- [ ] **Step 5: 运行全量测试 + 三冒烟 + 宿主构建**；补 Android export smoke（若本机可导出），确认 JIT/ALC 在移动端无回归。
- [ ] **Step 6: Commit**（建议拆 2-3 个小 commit：契约迁移、引用更新、文档/兼容层）：`git commit -m "refactor: split compat-pack contracts from emuera plugin abi"`。

---

### Task 11: P0 静态回归守卫与治理同步

**Depends on:** Task 0。

**Files:**
- Create: `tools/compat-pack/Test-CompatPackLifecycle.ps1`。
- Modify: `governance/README.md` 或 `tools/governance/Test-Governance.ps1`（把守卫脚本纳入验收清单）。
- Modify: `docs/designs/compat-pack-interface.md` §13.7/§13.9（记录验收命令）。
- Modify: `AGENTS.md`（在构建与验证节加入 CompatPack 守卫脚本）。
- Create: `governance/evolution-log/YYYY-MM-DD-compatpack-foundation-batch-2.md`（任务完成后写）。

**Interfaces:**
- Consumes: `scripts/EmueraMain.cs`、`scripts/GodotHost/LegacySessionBackend.cs` 源文本。
- Produces: PowerShell 脚本，退出码 0/1；检查项：
  1. `StopLegacySession` 函数体中不得出现 `!backend.IsRunning` 或 `backend.IsRunning` 条件早退；
  2. `StopLegacyBaselineAsync` 必须调用 `ClearCompatibilityPlan`；
  3. `CompatPackHost` 不得再出现 `ActivePackModuleIds` / `ActiveVariantSelections` / `ResetActiveSessionProjection`；
  4. `DialectPlan` 必须声明 `PackModuleIds` 与 `VariantSelections`。

**Evidence / Sources / Solution:**
- E1 P0 修复、E2-R2；`scripts/EmueraMain.cs:406-438`；`LegacySessionBackend.cs:139-147`。
- Solution：纯静态守卫不能代替真机 e2e，但能在低成本下防止 P0 回退；脚本接入 `tools/governance/Test-Governance.ps1` 或 PR checklist。

**Steps:**
- [ ] **Step 1: 写失败守卫脚本** `tools/compat-pack/Test-CompatPackLifecycle.ps1 -ProjectRoot .`，对当前已知违例样例期望 exit 1。
- [ ] **Step 2: 在 governance 契约测试中接入**，运行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools/governance/Test-Governance.ps1`，期望新脚本被调用且当前仓库通过（因为 P0 已修）。
- [ ] **Step 3: 在 `AGENTS.md` 构建验证节加入该命令**，并注明真机 e2e 仍为最终验收。
- [ ] **Step 4: 跑全量测试 + 三冒烟 + 宿主构建**，确认无回归。
- [ ] **Step 5: 写本轮 evolution log**，包含实际测试输出、设备/平台未验证项。
- [ ] **Step 6: Commit**：`git add tools governance AGENTS.md docs`；`git commit -m "test: add compat-pack lifecycle regression guard and governance sync"`。

---

## 5. 执行顺序建议

1. 必做且低风险：Task 0 -> Task 1 -> Task 2 -> Task 11。
2. 信任与生命周期：Task 3 -> Task 4 -> Task 5 -> Task 6 -> Task 7。
3. 设计决策：Task 8、Task 9 可并行（都只交付 spec）。
4. 高风险架构：Task 10 最后做，且必须先接受 ADR/spec 评审。
5. 每完成一个 Task，先跑该 Task 的局部验收命令，再跑 Task 0 Step 4-7 的全量门禁；发现回归先回滚该 Task，不要叠加修复。
6. Task 4、Task 6、Task 7 都触碰 `CompatPackHost.cs` 或 launcher 文件，必须串行；Task 10 触碰全部引用，必须最后串行执行。

## 6. 明确不在本计划范围

- 真实 EE/snake `Plugins/*.dll` 二进制兼容修复（E2-R8）：需要独立 spec；本计划只要求 Task 10 不再把 CompatPack 契约与插件 ABI 绑死。
- 第三方解释器实现（`IErbInterpreterHost` 生产接线、engine identity/version 协商）：需要独立 spec；本计划只通过 Task 8/9 记录依赖与阻塞。
- 六游戏真实启动矩阵与 Android 真机抽测：必须由 owner 提供游戏库/设备后按 AGENTS.md 验收；本计划的所有门禁都不得替代 APK 真机结论。
- `baseSurfaceHash` 的发布端/CI 生成流水线：Task 6 只做运行时校验与固定 hash 测试；生成器接入发布流程另开任务。


> 注记（2026-11-07，P-D 退役后）：本计划正文中的"内置方言模块（v18/snake/erafl/erablue/megaten）尚未退役"、
> "六 profile 并集"等历史结论已被 `docs/superpowers/specs/2026-11-07-pd-module-retirement-execution-design.md`
> 与 `governance/evolution-log/2026-11-07-pd-module-retirement.md` 推翻；当前实现为 5 个第一方数据包 +
> v24pure 基线，详见 `packs/README.md`。本文件仅作历史计划保留，不回改正文。
