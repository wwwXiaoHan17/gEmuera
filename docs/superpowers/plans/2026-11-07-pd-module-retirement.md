# P-D 内置方言模块退役 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 按已批准的执行设计，把五个内置方言模块（v18 → megaten → erablue → erafl → snake）逐退役为 packs/ 下的第一方兼容包，冻结包校验表面宇宙保证 v24 baseSurfaceHash 不漂移，新增第一方包分发链路，并补齐指导文件/Template 与治理记录。

**Architecture:** 引擎运行时收敛为「v24 单基线 + 兼容包」；内置模块注册/桥接/闭包/profile id 按步删除，表面事实冻结为只读工件继续服务包校验与哈希；launcher 在绑定引擎前把退役 profile 翻译为 v24pure 并按 P0>P1>P2 注入包路径；legacy-runner 走同一翻译链但必须断言包实际加载；导出物内置 5 个包 DLL，启动后按安装根一包一目录落盘。

**Tech Stack:** C# / .NET 8+9、xUnit、PowerShell 5.1（UTF-8 BOM）、Godot 4.7 .NET（本机缺失，构建走 dotnet 逃生）、legacy Emuera bridge、dialect-inventory 工具链。

**Spec:**
- docs/superpowers/specs/2026-11-07-pd-module-retirement-execution-design.md（执行设计，权威；下称执行设计）
- docs/plans/2026-10-03-pd-module-retirement-spec.md（P-D spec；§4 顺序/§5 删除清单/§7 验证口径）
- reports/compatpack/pd-recon-inventory.md（触点盘点，HEAD a375a57；行号以它 + 当前 grep 为准）

## Global Constraints

- **顺序硬约束**：v18 → megaten → erablue → erafl → snake；任一步 Green 未齐（含独立 review 无 P0/P1）不得开工下一步。
- **数据安全顺序**：先加别名/映射/normalize 分支，再删引擎 catalog 条目；否则 launcher 手动条目会被静默删除（FirstWindow.GameBrowseUi.cs:164-170/:186-187）。
- **v24 基线不变**：每步跑哈希钉扎，baseSurfaceHash 必须保持 950dbeb9028a8a2fc73ac3699411a05b8e367458219b40bd862066c8902888c9；v24pure 表面/计数（561/266）与既有 v24pure 行为逐字节不变。
- **未选择侧不变**：任何改动不得改变 v24pure 会话的解析/投影结果；包链路失败必须整组回退（降级不变量）。
- **包通道边界**：v1 包不能声明 saveProfileId、defaultPorts、DiagnosticOnly capability；不得为凑等价把它们改成 Wired 或新增包字段（唯一例外：执行设计 §4.9 的 addInstructionCollisions，contractAbi 1.1）。
- **冻结工件只读**：tools/dialect-inventory/retired-dialect-surface-universe.json 生成后禁止手改；重跑生成器必须零 diff。
- **中文/编码**：.ps1 必须 UTF-8 BOM；源码/文档 UTF-8（无 BOM）；不要用 PowerShell 文本往返改中文文件。
- **门禁命令统一**（每个退役 Task 的 Green 步骤都跑）：
  1. dotnet build gemuera-c#.csproj -c Release -nodeReuse:false -m:1（0 error + DLL 时间戳更新）
  2. dotnet test tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj -c Release
  3. dotnet test tests/xUnitTest/EmueraFacade.Tests/EmueraFacade.Tests.csproj -c Release
  4. dotnet test tests/xUnitTest/EmueraPluginAbi.Tests/EmueraPluginAbi.Tests.csproj -c Release
  5. dotnet run --project tools/dialect-inventory/LegacyDialectSurfaceSmoke -c Release
  6. dotnet run --project tools/dialect-inventory/LegacyDialectRuntimeSmoke -c Release
  7. $env:DOTNET_ROLL_FORWARD='Major'; dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release
  8. powershell -NoProfile -ExecutionPolicy Bypass -File tools/compat-pack/Test-CompatPackReleaseGate.ps1 -ProjectRoot .
  9. powershell -NoProfile -ExecutionPolicy Bypass -File tools/governance/Test-Governance.ps1
- **禁止**：推送/建 PR（OWNER 选择本地交付）；把游戏内容/编译产物/staging 入库；把 reports/ 下产物提交；跳过/放宽既有测试（改口径必须在本计划或设计里写明理由）。
- **产物落点**：临时/证据文件放项目外或 reports/（gitignore）；提交前 git status 只应有本任务预期文件。

## Review Focus

最容易被实现者踩到、且现有测试不一定覆盖的 6 类失败模式（每条对应任务内测试）：

1. **退役 id 未翻译**：6 个引擎绑定/启动点任一遗漏 → CompatibilityProfileCatalog.Resolve 抛异常启动崩（Task 3/4-8 测试）。
2. **v24 哈希漂移**：冻结/并集/METHOD 投影处理错误 → 既有官方包与外部社区包失效（Task 1 钉扎；每步 G8）。
3. **包通道表达不了的内置事实被静默丢失**：saveProfileId/defaultPorts/DiagnosticOnly 被误当行为等价，或宿主 save-profile 映射随类删除丢失（Task 5/7 取证 + 等价测试）。
4. **snake 特殊面错误**：BITMAP_CACHE_ENABLE 碰撞态回放错误（指令/函数可见性反了）、hideFunctions 含 no-op 名被规则拒载、陥落状態 被隐藏、DFC 因缺 capability 不激活、变体选择缺失（Task 2/8）。
5. **分发链路危险行为**：installer 覆盖用户手装包、Android 权限前写入失败后不重试、路径穿越写入包目录外、staging 未打进导出物（Task 9）。
6. **生成物/工具链假绿或假红**：G3 快照生成器读被删类字段、G4 名录未按步收缩、legacy-runner 报 profile 但实际未加载包（Task 1/10 + G10）。

---

### Task 0: 基线锁定（只读验证，无代码改动）

**Files:** 无源码改动；证据输出到 reports/pd-baseline/（gitignore，不入库）。

**Interfaces:** Consumes 无；Produces 基线命令输出与计数，供 Task 12 对比。

- [ ] Step 1: 构建基线：dotnet build gemuera-c#.csproj -c Release -nodeReuse:false -m:1；Expected 0 个错误且 .godot/mono/temp/bin/Release/gemuera-c#.dll 时间戳更新。
- [ ] Step 2: 三个 xUnit 基线；Expected Core 303/0/1、Facade 78/0、ABI 4/0（若 HEAD 已变，实测记录并停止确认）。
- [ ] Step 3: 三冒烟基线（Surface/Runtime/CoreContract，后者 DOTNET_ROLL_FORWARD=Major）；Expected 全 exit 0；RuntimeSmoke 输出 v24MergedInstructions=561; snakeMergedInstructions=670; v24Functions=266; snakeFunctions=351。
- [ ] Step 4: 发布门禁与治理基线；Expected PASS（7 项检查；桌面命令 7/7 通过）与 M3-M7 governance contract passed.。
- [ ] Step 5: 记录五步命令/退出码/计数到 reports/pd-baseline/task0.md；确认 git status --short 无源码改动。

### Task 1: 冻结包校验表面宇宙 + 生成器/快照修复 + v24 哈希钉扎

**Files:**
- Create: tools/dialect-inventory/retired-dialect-surface-universe.json（冻结工件，入库；生成后只读）
- Modify: tools/dialect-inventory/LegacyDialectInventoryGenerator/Program.cs（冻结模式 + 正常模式改写）
- Modify: src/Core/Compatibility/LegacyDialectInventories.Generated.cs（重新生成；本 Task 输出应与现状零 diff）
- Modify: tools/legacy-runner/profiles.generated.json（重新生成；零 diff）
- Modify: tools/dialect-inventory/DialectInventory.psm1（v24 快照来源改读工件/生成清单，去掉对 LegacySnakeCompatibilityModule/LegacyEraFlCompatibilityModule 私有字段依赖）
- Modify: docs/NewFrameworkDesign/generated/dialect-registry-snapshots.json（重新生成；v24 条目 canonicalHash 不变）
- Test: tests/xUnitTest/GEmuera.Core.Tests/LegacySurfaceHashTests.cs（哈希钉扎）
- Test: tools/dialect-inventory/LegacyDialectSurfaceSmoke/Program.cs（G5 词汇表计数 41 哨兵 + G8 哈希钉扎）

**Interfaces:**
- Consumes: 现有 LegacyDialectInventories 数组布局。
- Produces: 冻结 JSON schema = { schemaVersion, sourceCommit, capturedAtUtc, profiles: {id: {instructions, functions:[{name,returnType}]}}, methodProjectedInstructionNames, functionReturnTypes }；五个退役 profile 全冻结，v24 不冻结。
- Produces: 生成器 CLI 不变；新增一次性参数 --freeze-retired-surface。
- Produces: LegacyDialectInventories 暴露的数组名与内容与现状逐字节一致。

- [ ] Step 1: 冻结工件（一次性）。给生成器加 --freeze-retired-surface：仍按六 profile 构建（此步模块全在），把五个退役 profile 的指令名、函数(name+returnType)、METHOD 投影并集、返回类型并集序列化为规范化 JSON（键/数组 Ordinal 排序、LF、UTF-8 无 BOM、末尾单换行、sourceCommit=git rev-parse HEAD）。Run: dotnet run --project tools/dialect-inventory/LegacyDialectInventoryGenerator -- . --freeze-retired-surface。Expected: 工件生成；核对 v18=417/160、snake delta=109/87、erafl=1/0、erablue=1/0、megaten=0/0、METHOD=345 项。
- [ ] Step 2: 生成器改 live v24 + 冻结工件。正常模式：v24pure 仍 live；五个退役 profile 数组/METHOD/返回类型从冻结 JSON 读；profiles.generated.json 仍由 live profile 列表生成（本 Task 仍六项）。保留哨兵断言但对象改冻结数据。同时确认 CompatPackHost.ReservedModuleIds（六模块 id）与 EngineCapabilityCatalog 描述符**一处不改**（R4：历史撞名保留面，不是模块在场证据）。Run 生成器。Expected: git diff --exit-code src/Core/Compatibility/LegacyDialectInventories.Generated.cs tools/legacy-runner/profiles.generated.json 零 diff。
- [ ] Step 3: 哈希钉扎测试。LegacySurfaceHashTests 增加 ComputeV24SurfaceHash(...) == 950dbeb9...；先绿（钉现状），注释说明失败=冻结宇宙或 v24 面漂移、禁止改期望值。Run: dotnet test ... --filter FullyQualifiedName~LegacySurfaceHashTests。Expected: PASS。
- [ ] Step 4: 快照生成器修复。DialectInventory.psm1 的 v24 快照排除名单不再读 LegacySnake/LegacyEraFl 私有字段，改由冻结工件+生成清单推导；snake/erafl 条目保留到各自退役步。Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Invoke-DialectRegistrySnapshot.ps1。Expected exit 0；git diff 中 profiles.v24 的 canonicalHash 不变、snake/erafl 条目不变。记录 v24 hash 到 reports/pd-baseline/g3-v24-hash.txt。
- [ ] Step 5: 冒烟哨兵。SurfaceSmoke 增加 EngineCapabilityCatalog.All.Count == 41（32 Wired + 9 DiagnosticOnly，取实测）与 baseSurfaceHash 钉扎。Run SurfaceSmoke；Expected PASS。
- [ ] Step 6: 全量门禁 + 提交。Run Task 0 全部命令；Expected 全绿。Commit: feat(pd): freeze pack validation surface universe and pin v24 surface hash。
- [ ] Step 7: 独立 review（只读该 commit diff；检查冻结工件只读/来源、生成器零 diff、哈希钉扎、快照 v24 不变、无行为改动；P0/P1 清零再进 Task 2）。

**本轮不做（记录，不扩展范围）**：EmueraCoreProfile.SnakeModernMobile 死枚举值清理（R10）、GameCompatibilityResolver 自动探测接线（R11）、DIA-10/11/12 静态考古链修复（R8 已在本 Task 用 v24-stability 口径替代）。

### Task 2: 包表面契约扩展（addInstructionCollisions，contractAbi 1.1）

**Files:**
- Modify: src/EmueraCompatPack/compatpack.manifest.schema.json
- Modify: src/EmueraCompatPack/CompatPackManifest.cs
- Modify: src/Core/Compatibility/Packs/CompatPackRules.cs
- Modify: src/Core/Compatibility/Packs/CompatPackPlanAssembler.cs（仅当状态折叠需要）
- Modify: scripts/Emuera/Compatibility/CompatPackHost.cs（CurrentContractAbi = 1.1）
- Modify: scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs（仅当 ApplyPackSurface 需显式处理碰撞态可见性）
- Test: tests/xUnitTest/EmueraFacade.Tests/CompatPackManifestTests.cs
- Test: tests/xUnitTest/GEmuera.Core.Tests/CompatPackRulesTests.cs
- Test: tests/xUnitTest/GEmuera.Core.Tests/CompatPackAssemblyTests.cs
- Test: tools/dialect-inventory/LegacyDialectSurfaceSmoke/Program.cs（合成碰撞包投影用例）

**Interfaces:**
- Consumes: CompatPackValidationContext（MethodProjectedInstructionNames、KnownInstructionHandlers、BaselineInstructions/Functions）。
- Produces: CompatPackManifest.Surface.AddInstructionCollisions: IReadOnlyList<string>（缺省空）。
- Produces: 校验语义：名字 ∈ MethodProjectedInstructionNames、∉ BaselineInstructions、∈ KnownInstructionHandlers，且恰好出现在 HideFunctions；产生 Instruction Present + Function Hidden 两状态且跳过镜像。
- Produces: CompatPackHost.CurrentContractAbi = new CompatVersion(1, 1)（engineApi 保持 1.0；baseSurfaceHash 不变）。
- Produces: 旧 contractAbi 1.0 包照常加载（回归测试）。

- [ ] Step 1: 写失败测试（manifest/规则/组装）。manifest 解析新字段；规则负例（不在 MethodProjected、不在 KnownInstructionHandlers、未配对 hideFunctions、同时出现在 addInstructions、重复声明）全部拒载且错误含包 id/字段名；组装正例合成包组装后指令含该名、函数不含，投影指令可见/函数不可见。Run 对应 dotnet test --filter；Expected FAIL。
- [ ] Step 2: 实现 schema/解析/规则/组装。schema 只加可选数组；parser 未知字段仍 fail-closed；规则集中在 ValidateSurface 一侧；CurrentContractAbi 升 1.1 并同步测试中期望 1.0 的断言（v18/模板 manifest 不改，仍 1.0）。Run Core/Facade 相关测试；Expected PASS。
- [ ] Step 3: 旧包兼容与投影冒烟。SurfaceSmoke 增加合成碰撞包投影断言（v24pure 基线 + 合成 manifest，不依赖 snake pack）；断言旧 contractAbi 1.0 官方 v18 包加载仍成功。Run SurfaceSmoke；Expected PASS。
- [ ] Step 4: 全量门禁 + 提交。Run Task 0 命令；Expected 全绿。Commit: feat(compatpack): express instruction/function name collisions in pack surface (contractAbi 1.1)。
- [ ] Step 5: 独立 review（重点：规则误伤正常 add/hide、旧包兼容、哈希未变）。

### Task 3: launcher 映射/翻译 + 6 绑定点 + P0/P1 修复

**Files:**
- Create: src/Core/Compatibility/Packs/CompatPackProfileMap.cs
- Create: assets/text/pack-profile-map.json（本 Task 为空 profiles；每步退役追加一条）
- Create: tests/xUnitTest/GEmuera.Core.Tests/CompatPackProfileMapTests.cs
- Modify: src/Core/Compatibility/Packs/CompatPackLauncherConfig.cs（三输入 MergeSelection）
- Modify: scripts/FirstWindow.cs（6 绑定点翻译、alias、三输入、目录路由别名、状态行、normalize）
- Modify: scripts/FirstWindow.CompatPackUi.cs（editor launcher 根、一级子目录扫描、复用绝对化）
- Modify: scripts/FirstWindow.GameBrowseUi.cs（normalize 失败保留原条目）
- Modify: scripts/EmueraMain.cs（早期绑定翻译）
- Modify: scripts/Emuera/Program.cs（catch 内归一 v24pure；SetLauncherCompatibilityProfile 翻译入口）
- Modify: scripts/GodotHost/LegacySessionBackend.cs（诊断传递实际引擎 id）
- Modify: scripts/GodotHost/PrototypeRuntimeNode.cs（save profile 映射改常量，不引用将删除类）
- Modify: assets/lang/default.txt、en_us.txt、jp.txt、zh_cn.txt（新增 FirstWindow.CompatPackMissing 等 key）
- Test: tests/xUnitTest/GEmuera.Core.Tests/CompatPackLauncherConfigTests.cs
- Test: tools/dialect-inventory/LegacyDialectSurfaceSmoke/Program.cs（P0 catch 回退探针，复用既有 fallback probe 风格）

**Interfaces:**
- Produces: CompatPackProfileMap.TryParse(json, out map, out errors)；bool TryGetEntry(launcherProfileId, out entry)；static string ResolveEngineProfileId(launcherProfileId)（命中→v24pure，否则原样）。
- Produces: CompatPackProfileMapEntry { ProfileId, PackId, RelativePath, RelativePathAndroid, RequiredPackVersion }。
- Produces: CompatPackLauncherConfig.MergeSelection(stored, external, mapped)（P0>P1>P2；stored/external 皆空才用 mapped）。
- Produces: 6 个绑定点全部先经 ResolveEngineProfileId：FirstWindow.cs:1477/:102/:1418、EmueraMain.cs:151-159、Program.cs:81-82、Program.cs:389-400(:397)。
- Produces: launcher 根 = 桌面 exe 目录 / Android /storage/emulated/0/emuera / 编辑器 res://。
- Produces: 宿主 save-profile 映射 helper LauncherProfileSaveProfileId(launcherProfileId)（v24pure/v18/erablue→gemuera.v24；snake→gemuera.snake；erafl→gemuera.erafl；megaten→gemuera.megaten），Task 5/7 用它替换对将删除类常量的直接引用并做回归断言。

- [ ] Step 1: 写失败测试（Core 纯逻辑）。CompatPackProfileMapTests：合法解析、重复 profileId 拒载、缺字段拒载、未知字段拒载、ResolveEngineProfileId 命中/未命中、一包一目录路径示例。CompatPackLauncherConfigTests：三输入优先级、stored 为空时 mapped 生效、stored 与 external 并存与既有 E2-R3 语义一致。Run filter；Expected FAIL。
- [ ] Step 2: 实现 Core 映射与合并。生产映射先写 {schemaVersion:1.0.0, profiles:[]}。Run 测试；Expected PASS。
- [ ] Step 3: launcher/host 接线（RED→GREEN）。6 绑定点、SetSelectedGamePath 顺序（normalize launcher id → ResolveEngineProfileId → 绑定引擎 → 持久化 launcher id → ApplyCompatPackEnvironment 三输入）、mapped 存在性预检 + 回退文案、目录路由 alias、手动条目保留、编辑模式映射根+一级子目录候选。P0 catch 回退探针进 SurfaceSmoke。Run build+测试+SurfaceSmoke；Expected PASS。
- [ ] Step 4: 文案回归。4 个语言 key 用 git grep 确认 4 个 lang 文件均有值。Run: git grep -n FirstWindow.CompatPackMissing assets/lang；Expected 4 文件命中。
- [ ] Step 5: 全量门禁 + 提交。Run Task 0 命令；Expected 全绿（v24 计数/哈希不变）。Commit: feat(pd): map retired launcher profiles to first-party packs and translate at engine bind points。
- [ ] Step 6: 独立 review（重点：6 绑定点遗漏、fail-closed、手动条目不再丢、编辑器/Android 路径）。

### Task 4: v18 退役（纯 hide 包 + 目录改名）

**Files:**
- 改/删（recon §2.1 全表）：src/Core/Compatibility/BuiltInDialectCatalog.cs、scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs、scripts/Emuera/Program.cs、scripts/FirstWindow.cs
- 改名：packs/gemuera.v18/ → packs/pack.gemuera.v18/（csproj 文件名同名化，产物 DLL=pack.gemuera.v18.dll）
- 改：tools/compat-pack/Generate-V18PackManifest/Program.cs（输出目录；差集右侧改冻结工件常量）
- 改：tools/legacy-runner/profiles.generated.json、生成器 live profile 列表
- 改：tests/xUnitTest/GEmuera.Core.Tests/V18PackManifestTests.cs（golden 口径）、CompatPackAssemblyTests.cs、EngineCapabilityCatalogTests.cs
- 改：tools/dialect-inventory/LegacyDialectSurfaceSmoke/Program.cs（:236-249/:406-412/:730-815）、LegacyDialectRuntimeSmoke/Program.cs（:87-96/:102）
- 改：tests/xUnitTest/GEmuera.Core.Tests.csproj、tools/dialect-inventory/LegacyDialectSurfaceSmoke/LegacyDialectSurfaceSmoke.csproj（ProjectReference 路径）
- 新：assets/text/pack-profile-map.json 增加 v18 条目（一包一目录路径）；packs/pack-profile-map.example.json 同步

**Interfaces:** Consumes Task 1 冻结宇宙 + Task 3 映射/翻译；Produces packs/pack.gemuera.v18；v18 在 launcher 为兼容包别名；内置 v18 不再可被 CreateLegacySessionPlan("v18") 解析。

- [ ] Step 1: Red golden 等价测试。改写 V18PackManifestTests：加载改名后包 DLL，断言 hideInstructions/hideFunctions 等于冻结 golden；新增 v24pure+包表面 == 退役前 v18 golden（417/160 键集合），golden 来自冻结工件。Run --filter V18PackManifestTests；Expected FAIL。
- [ ] Step 2: 包改名与生成器。git mv 目录、改 csproj 文件名并把 AssemblyName 显式设为 pack.gemuera.v18（产物 DLL 名与映射/部署路径一致）、改生成器输出目录、重跑生成器；确认 baseSurfaceHash 与 manifest 内容不变（除路径）。Run Generate-V18PackManifest；Expected 成功且 hash 仍 950dbeb9...。
- [ ] Step 3: 先加别名/映射（顺序硬约束）。TryNormalizeCoreProfileName 保留 v18 别名、目录路由 alias、手动条目路径、pack-profile-map.json 加 v18 条目（desktop compat_packs/pack.gemuera.v18/pack.gemuera.v18.dll；android packs/pack.gemuera.v18/pack.gemuera.v18.dll；requiredPackVersion 1.0.0）；Program.DetectCoreProfile/SetLauncherCompatibilityProfile 接受并翻译 v18→v24pure；现有测试仍绿。
- [ ] Step 4: 删除内置 v18（recon §2.1 锚点；Core 注册/contrib/profile、桥接常量/数组/closure/类、Program 三 switch 的 v18 分支、FirstWindow 下拉/索引改别名语义；保留 CoreProfileV18 常量与目录路由别名）。Run build；Expected 0 error。
- [ ] Step 5: 再生与工具/测试同步。重跑生成器（v18 live 项消失、冻结数组保留）→ profiles.generated.json 移除 v18；更新 SurfaceSmoke/RuntimeSmoke/CompatPackAssemblyTests/EngineCapabilityCatalogTests/Generate-V18PackManifest；核对 git diff 中 v24 相关生成物不变。Expected 生成器 exit 0；v24 数组/哈希不变。
- [ ] Step 6: Green 全量门禁（Task 0 全部命令）。Expected 全绿；G8 哈希钉扎 PASS；G9 旧包兼容 PASS。证据写 reports/pd-step1/。
- [ ] Step 7: Commit feat(pd): retire v18 builtin dialect into pack.gemuera.v18 + 独立 review（删除完整性、golden 等价、v24 不变、launcher 别名/回退、无残留 v18 引用）。

### Task 5: megaten 退役（零表面 + 5 Wired capability + saveProfile 取证）

**Files:**
- 删注册/桥接/closure/三 switch/FirstWindow 分支：BuiltInDialectCatalog.cs:35-37/:75-79/:96、LegacyCompatibilityModules.cs:96/:106/:123/:681-691、Program.cs:736/:762/:777、FirstWindow.cs:1520-1524/:661/:745/:628
- 删 src/Core/Compatibility/MegatenCompatibilityModule.cs:101-119/:121-137（保留常量/ports）
- 新 packs/pack.gemuera.megaten/（manifest-only；surface 空；capabilities=5 Wired：startup.continue-after-fault、parser.label-lookup-case、parser.ref-out-name、parser.private-system-shadow、ui.html-rgb-zero-alpha；baseSurfaceHash 可省略，为统一建议生成写入）
- 改 scripts/GodotHost/PrototypeRuntimeNode.cs:90/:208/:259-260（saveProfile 用常量映射保留）
- 改 pack-profile-map.json + packs/pack-profile-map.example.json
- 改生成器 live 列表、profiles.generated.json、SurfaceSmoke:271-296/:406-412、RuntimeSmoke:109-112、EngineCapabilityCatalogTests.cs:165/:226

- [ ] Step 1: Red 等价测试。新测试加载 megaten 包：v24pure+包指令/函数键集合 == v24pure（零增量）；plan.CapabilityIds 的 Wired 子集 == 5 项；Program.Compatibility.Megaten 各 flag 由 capability 激活；plan.SaveProfileId 允许 null 但断言宿主映射 LauncherProfileSaveProfileId("megaten") == "gemuera.megaten"。Expected FAIL。
- [ ] Step 2: R3 消费链取证。git grep SaveProfileId -- scripts src：确认生产 legacy 启动路径是否消费 plan.SaveProfileId（GameSession/LegacySessionFacade/PrototypeRuntimeNode）；结论写证据文件；若生产路径需非 null 则在 SessionSelection 注入，否则记录仅调试/账本差异。
- [ ] Step 3: 包工程 + 映射条目（先别名/映射，再删内置）。创建 packs/pack.gemuera.megaten（csproj+marker+manifest）；映射加 megaten 条目；launcher alias/normalize/目录路由、下拉文案。
- [ ] Step 4: 删除内置 megaten（recon §3.1 锚点；保留策略类/常量/DefaultPorts/DiagnosticOnly 常量与 saveProfile 常量）。Run build；Expected 0 error。
- [ ] Step 5: 再生 + 工具/测试同步。生成器 live 列表、profiles.generated、Surface/Runtime smokes、EngineCapabilityCatalogTests、PrototypeRuntimeNode saveProfile 常量。Run generator；Expected exit 0、v24 不变。
- [ ] Step 6: Green 全量门禁 + 证据（同 Task 4 Step 6）。
- [ ] Step 7: Commit feat(pd): retire megaten builtin dialect into pack.gemuera.megaten + 独立 review。

### Task 6: erablue 退役（+SETANIMETIMER、1 Wired capability）

**Files:**
- 删 BuiltInDialectCatalog.cs:31-33/:69-73/:95、LegacyCompatibilityModules.cs:104/:121/:717-736、EraBlueCompatibilityModule.cs:33-43/:45-55、Program.cs:734/:761/:776、FirstWindow.cs:1515-1519/:660/:744/:627
- 新 packs/pack.gemuera.erablue/：surface.addInstructions=[SETANIMETIMER]、capabilities=[startup.continue-after-fault.v1]；manifest 由生成器产出
- 改映射条目、生成器 live 列表、profiles.generated.json、SurfaceSmoke:251-266/:291-296/:406-412、RuntimeSmoke:103-108、EngineCapabilityCatalogTests.cs:225

- [ ] Step 1: Red 等价测试（v24+SETANIMETIMER 键集合；1 capability；plugin DiagnosticOnly 不得进包）。
- [ ] Step 2: 包工程 + 映射/别名（先于删除）。
- [ ] Step 3: 删除内置 erablue（保留 ModuleId/ExternalPluginCapability/ContinueAfterStartupFaultCapability 常量与 DiagnosticOnly 描述符）。Run build；0 error。
- [ ] Step 4: 再生 + 工具/测试同步；build 0 error。
- [ ] Step 5: Green 全量门禁 + 证据。
- [ ] Step 6: Commit feat(pd): retire erablue builtin dialect into pack.gemuera.erablue + 独立 review。

### Task 7: erafl 退役（算法库保留、10 Wired capability、saveProfile 取证）

**Files:**
- 删 BuiltInDialectCatalog.cs:22-24/:57-61/:98、LegacyCompatibilityModules.cs:93/:102/:119/:693-715、EraFlCompatibilityModule.cs:172-190/:192-200、Program.cs:732/:759/:774、FirstWindow.cs:1505-1509/:658/:742/:625
- 整文件保留 EraFlCompatibilityModule.cs 其余（算法库/常量/RequiredCapabilityIds/RequiredWiredCapabilityIds/Ports）
- 新 packs/pack.gemuera.erafl/：addInstructions=[SETANIMETIMER]、capabilities=10 Wired（markup 3 个 DiagnosticOnly 不进包）
- 改 tools/core-contracts/Program.cs:364-377/:932-960（erafl 会话/policy 断言改包链路或删）；映射条目；生成器 live 列表；profiles.generated.json；SurfaceSmoke:92-232/:406-412、RuntimeSmoke:81-86/:101、EngineCapabilityCatalogTests.cs:165/:183-199；PrototypeRuntimeNode.cs:206（saveProfile 常量映射保留）
- 改 DialectInventory.psm1 erafl 快照条目在删除后移除（G3 允许 diff，记录）

- [ ] Step 1: Red 等价测试（v24+SETANIMETIMER；10 Wired；policy 行为由 capability 激活；saveProfile 宿主映射）。
- [ ] Step 2: R3 取证（erafl 生产 saveProfile 消费链；同 Task 5 Step 2）。
- [ ] Step 3: 包工程 + 映射/别名。
- [ ] Step 4: 删除内置 erafl（保留算法库/常量/策略类）；build 0 error。
- [ ] Step 5: 再生（profiles.generated 去 erafl；快照 erafl 条目移除并记录唯一 diff）+ core-contracts/Surface/Runtime 测试同步。
- [ ] Step 6: Green 全量门禁 + 证据。
- [ ] Step 7: Commit feat(pd): retire erafl builtin dialect into pack.gemuera.erafl + 独立 review。

### Task 8: snake 退役（最大差集 + 变体 + DFC + 碰撞字段）

**Files:**
- 删 BuiltInDialectCatalog.cs:19-21/:51-55/:97、LegacyCompatibilityModules.cs:92/:101/:118/:533-621、SnakeCompatibilityModule.cs:36-47/:49-55、Program.cs:731/:758/:773、FirstWindow.cs:1500-1504/:657/:741/:624
- 迁移保留（删类前）：DivergentFunctionContractNames(:538-545) 与 SnakeExcludedFunctionNames(:594-600，含"不得隐藏 陥落状態/陷落状态"注释) 迁到 LegacyCompatibilityModuleCatalog 内/新 SnakeLegacySurfaceNames.cs；更新 Compose:204 引用
- 保留整文件：SnakeCompatibilityCapabilities.cs、变体枚举/映射、policy/DFC 机制、SnakeCompatibilityModule.cs:16-34
- 新 packs/pack.gemuera.snake/（生成器产出）：add/hide 由冻结 delta 推导；variantSelections{FOR:builtin:snake, SETBGIMAGE:builtin:snake}；capabilities=19 Wired；surface.addInstructionCollisions=[BITMAP_CACHE_ENABLE] + hideFunctions 含 BITMAP_CACHE_ENABLE；hideFunctions 其余=SnakeExcludedFunctionNames 中实际存在于 v24 基线的名字（no-op 剔除并记录）
- 改映射条目；生成器 live 列表；profiles.generated.json（只剩 v24pure）；SurfaceSmoke:58/:209-221/:224-232/:298-307/:406-412/:689-724、RuntimeSmoke:16-18/:24-26/:28-35/:38-56/:66-73/:100、EngineCapabilityCatalogTests.cs:165/:223、tools/core-contracts/Program.cs:318-320/:340-362/:919-930（snake 会话改包链路或删）
- 改 DialectInventory.psm1 snake 快照条目/默认类名依赖（Task 1 已移除读写桥接类；本步删条目）；tools/dialect-inventory/dialect-compatibility-pack.json 等 DIA 静态数据不改（非本轮门禁，记录为历史考古数据）
- 改 tools/legacy-runner 相关 profile 白名单（Task 10 统一；本步至少让生成器/profiles 一致）

- [ ] Step 1: Red 等价测试（surface 670/351 键集合；BITMAP_CACHE_ENABLE 指令可见/函数不可见；19 capability 全在场 → DFC 激活；变体选择生效）。
- [ ] Step 2: 迁移两个静态名单常量（先于删类），保持 Compose 行为不变；build+相关测试绿。
- [ ] Step 3: 包工程 + 映射/别名（snake lane 的 root/snake 必须翻译；先加后删）。
- [ ] Step 4: 删除内置 snake（recon §6.1 锚点）；build 0 error。
- [ ] Step 5: 再生 + 全工具/测试同步（Surface/Runtime/core-contracts/EngineCapabilityCatalogTests/快照 snake 条目移除并记录唯一 diff；profiles.generated 仅 v24pure）。
- [ ] Step 6: Green 全量门禁 + 证据（含 G8 钉扎、G9 碰撞字段回归、G10 静态/脚本验证）。
- [ ] Step 7: Commit feat(pd): retire snake builtin dialect into pack.gemuera.snake + 独立 review（重点 Review Focus #4）。

### Task 9: 第一方包分发链路（staging + installer + 启动释放）

**Files:**
- Create src/Core/Compatibility/Packs/CompatPackBundleInstaller.cs
- Create tests/xUnitTest/GEmuera.Core.Tests/CompatPackBundleInstallerTests.cs
- Create scripts/GodotHost/FirstPartyPackBootstrap.cs（或最小 partial；不复活 AppBootstrap）
- Modify scripts/FirstWindow.cs / FirstWindow.CompatPackUi.cs（扫描前调用释放 + Android 权限后/resume 重试；提升 launcher 根 helper 可见性）
- Create tools/compat-pack/Build-FirstPartyPacks.ps1（UTF-8 BOM）
- Modify export_presets.cfg（include_filter 加 compat_packs/*/*.dll）
- Modify .gitignore（加 /compat_packs/）
- Modify tools/compat-pack/Test-CompatPackReleaseGate.ps1（桌面档新增 staging 完整性静态检查）

**Interfaces:**
- Produces: CompatPackBundleInstaller.Install(sourceFiles, targetRoot, statePath, fs adapter) -> result；幂等、原子写、SHA256、记录 last-installed、路径/包 id 校验；结果记录 Installed/Skipped/KeptUser/Failed 计数。
- Produces: 目标根 = 桌面 exe 目录 compat_packs / Android /storage/emulated/0/emuera/packs / 编辑器跳过释放。

- [ ] Step 1: Red installer 单测（全新安装/同字节跳过/内置升级/用户改过保留/记录缺失/损坏字节/路径穿越/权限失败聚合/原子性）。
- [ ] Step 2: 实现 CompatPackBundleInstaller（纯逻辑，禁止 Godot 依赖）；测试绿。
- [ ] Step 3: staging 脚本 + export/.gitignore + gate 静态检查；跑脚本生成 compat_packs/<packId>/<packId>.dll（gitignored）。
- [ ] Step 4: 宿主接线（扫描前释放 + Android 权限后重试 + 日志）；build 0 error。
- [ ] Step 5: 门禁（桌面 release gate 含新检查；全量测试）；记录 Android 真机 [未验证]。
- [ ] Step 6: Commit feat(pd): bundle and install first-party compat packs on startup + 独立 review（重点 Review Focus #5）。

### Task 10: 诊断工具链适配（legacy-runner 包通道 + 证据字段）

**Files:**
- Modify tools/legacy-runner/Invoke-LegacyRunner.ps1（退役 profile 翻译：读生产映射→注入 GEMUERA_COMPAT_PACKS→引擎 profile 传 v24pure；staging 缺包即 FAIL；报告透出 baseProfile/packIds）
- Modify scripts/LegacyRunner/LegacyRunnerConfig.cs（profile 校验/归一不再只认三值；错误文案更新）
- Modify scripts/LegacyRunner/LegacyRunnerHost.cs（翻译链）
- Modify scripts/LegacyRunner/LegacyRunnerReportWriter.cs（报告写 baseProfile/packIds/plan hash）
- Modify tools/legacy-runner/Invoke-LegacyDisplayBaseline.ps1、Invoke-SessionIsolation*.ps1、Test-LegacyRunner.ps1（ValidateSet/夹具随别名更新）
- Modify tools/compat-pack/Test-CompatPackReleaseGate.ps1（fallback 目标正则六 profile → 新形态）
- Test: PowerShell 静态/单元级验证（无 Godot：参数解析/脚本自检；执行级标 [未验证]）

- [ ] Step 1: Red 脚本层断言（翻译缺失/缺包必须非零退出；报告字段存在）。
- [ ] Step 2: 实现翻译 + env 注入 + 断言 + 报告字段 + 错误文案。
- [ ] Step 3: 静态验证 + 现有非 Godot 测试；记录 [未验证] 执行级（无 Godot 4.7）。
- [ ] Step 4: Commit feat(pd): teach legacy-runner to load retired profiles as packs + 独立 review。

### Task 11: 指导文件 / Template / AGENTS / design / release-gates 补齐与悬空引用清理

**Files:**（recon §8.4 + 执行设计 §8）
- packs/README.md（社区+第一方、5 包清单/packId/命名、生成器与等价门禁命令、分发链路、映射回退、一包一目录、更新语义）
- packs/CommunityPackTemplate/README.md（新增：复制/改名/字段/构建/放置/验证/可选字段示例）
- 5 × packs/pack.gemuera.<profile>/README.md
- docs/designs/compat-pack-interface.md 追加 §14（P-D 执行记录 + contractAbi 1.1 说明）
- docs/compat-pack-release-gates.md（Gate 2 措辞、示例命令、分发检查、状态与限制）
- docs/plans/2026-10-03-pd-module-retirement-spec.md 文末追加执行记录指针
- AGENTS.md（铁律改写、门禁集更新、文件管理：packs/ 与 compat_packs/ staging；修正 AppBootstrap/PlatformGateway 过期组件描述）
- readme/README.md（+ en/ja）兼容性叙述；docs/NewFrameworkDesign/DeveloperHandoff.md（AppBootstrap 过期引用）；PLAN.md（历史计划注记）
- governance/evolution-log/2026-11-07-pd-module-retirement.md

- [ ] Step 1: 写 packs/README.md 增补与 Template README（先内容后校验命令可执行）。
- [ ] Step 2: 写 5 个第一方包 README（每包 ≤30 行）。
- [ ] Step 3: Append-only 更新 design/release-gates/spec 指针（不改历史段落）。
- [ ] Step 4: 更新 AGENTS/readme/DeveloperHandoff；grep 校验无悬空引用（旧 profile 名、旧命令、旧路径、AppBootstrap）。
- [ ] Step 5: evolution log 初稿（六要素 + 覆盖声明 + G6/真机未验证 + 遗留）。
- [ ] Step 6: Commit docs(pd): document first-party packs, distribution and retired profiles + 独立 review。

### Task 12: 最终整支验证 + result-review + 收尾

**Files:** Modify governance/evolution-log/2026-11-07-pd-module-retirement.md（补最终证据与 G7 得分）；无其它源码改动。

- [ ] Step 1: 全量门禁在最终树重跑（Task 0 全部命令 + staging 脚本 + release gate 桌面档 + governance）。
- [ ] Step 2: 生成物一致性：重跑生成器/快照 → git diff 零 diff；哈希钉扎 PASS；v24pure 561/266 不变；profiles.generated.json 仅 v24pure。
- [ ] Step 3: 全仓 grep 检查：无 CreateLegacySessionPlan("<退役id>")、无对已删类引用、无旧映射扁平路径、无 AppBootstrap 悬空引用（文档除外需标注）。
- [ ] Step 4: 独立 result-review：派新 reviewer 子代理按 P-D spec §8 打分 ≥98；P0/P1 清零；结论写 evolution log（复审段）。
- [ ] Step 5: 最终提交 + 交付说明（本地分支、不可发布声明、G6/真机 [未验证]、PR 拆分建议与复跑命令）。
