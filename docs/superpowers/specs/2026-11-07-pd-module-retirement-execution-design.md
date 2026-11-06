---
design_type: initiative
created_at: 2026-11-07
status: 待 OWNER 批准（builder-check 已回填；writing-plans 前置门）
---

# P-D 内置方言模块退役执行设计（第一方兼容包 + 分发链路）

> **上位契约**：`docs/plans/2026-10-03-pd-module-retirement-spec.md`（下称 P-D spec，2026-10-03 Spike 设计稿，本次由 OWNER 批准执行）。
> **相关设计**：`docs/designs/compat-pack-interface.md`（包契约）、`docs/compat-pack-release-gates.md`（发布门禁）、`packs/README.md`（开发指南）。
> **本文性质**：执行设计（execution design），只补 P-D spec 未覆盖或已被后续实现推翻的部分，不重写 spec；冲突处以本文 + 实施计划为准，并在执行记录中注明。
> **流程**：本文件获 OWNER 批准 → `superpowers:writing-plans` 产出实施计划 → 批准后按 subagent-driven / native 执行。

## 1. 意图与验收（Goal）

一句话：按 P-D spec 把五个内置方言模块（v18 / megaten / erablue / erafl / snake）退役成**可安装的第一方兼容包**，统一收拢到 `packs/` 一个文件夹，并补齐「第一方包随应用分发」与「指导文件 / Template」两个闭环，使运行时只剩「v24 单基线 + 兼容包」一条路径。

验收（缺一不可）：

1. `packs/` 下有 5 个第一方包工程（`pack.gemuera.{v18,megaten,erablue,erafl,snake}`）+ 社区模板；每个包有内嵌 `compatpack.manifest.json`，由生成器产出、禁止手改。
2. 内置方言模块只剩 `gemuera.v24` 与 `v24pure` profile：`BuiltInDialectCatalog` / `LegacyCompatibilityModules` / `expectedModuleClosures` / `CompatPackHost.AllProfileIds` / launcher 分支按 P-D spec §5 删除，按 v18 → megaten → erablue → erafl → snake 逐步落地。
3. launcher 走「profile → 第一方 pack」默认映射 + 缺失/失败回退（P-D spec §1–§3），生产映射 `assets/text/pack-profile-map.json` 与退役同提交。
4. 第一方包随应用分发：导出物内置 5 包 DLL，启动时按 packId 释放到安装根一包一目录位置，SHA256 幂等；释放失败 = 该 profile 回退提示（不静默半加载）。
5. `packs/README.md`（指导文件）与 `packs/CommunityPackTemplate/`（Template）按 §8 清单补齐并复查通过。
6. 五步证据链：每步 G1–G5 全绿 + 独立 review；G6/真机 e2e 明确标 `[未验证]`；最终 G7 result-review ≥ 98。
7. 治理收尾：`governance/evolution-log/2026-11-07-pd-module-retirement.md`、AGENTS.md / release-gates / design 文档同步。

## 2. 非目标 / 反目标

**非目标**：

- 不新增方言语义、不改 v24 基线行为、不动游戏内容与游戏目录。
- 不做 Android 真机 e2e、不做六游戏矩阵证据、不发布 APK/Release（环境不具备）。
- 不接 v2 代码贡献（`IInstructionVariantContribution`/`IPolicyContribution` 保持未接线）。
- 不改 ALC 允许清单、包哈希算法、`baseSurfaceHash` 语义（**唯一例外**：§4.9 的 manifest 表面字段增量扩展——只加可选字段、升 `contractAbi` minor，`engineApi` 与 `baseSurfaceHash` 输入不变）。
- 本轮不推送、不开 PR（OWNER 选定本地交付）。

**反目标（做到也算失败）**：

- 退役后 APK/桌面导出「未装包即静默掉能力」却没有任何分发或用户可见回退。
- 任一步「半加载」或让未选择侧（v24pure）逐字节不等价。
- 把无法验证的六游戏/真机项写成 PASS。
- 为通过门禁放宽/跳过既有测试（改口径必须写进设计或计划并说明理由）。

## 3. 基线与环境事实（2026-11-07 实测）

| 项 | 事实 | 对执行的影响 |
| --- | --- | --- |
| Git | 主工作区 `dev` @ `a375a57`；主工作区已有其他会话的**纯空白改动**（`git diff -w --stat` 为空），基线已含，不纳入本任务 | 本任务在独立 worktree/branch 进行 |
| 工作区 | worktree `...\.agents\worktrees\pd-module-retirement`，branch `ai/pd-module-retirement`（从 `a375a57` 创建） | 交付 = 该分支上的逐步 commit |
| .NET | `dotnet 10.0.301` 可用 | xUnit / 冒烟 / 生成器可跑 |
| Godot | 本机未找到 Godot 4.7（只有 4.5.1/4.6）；无 Android 设备；`E:\MyCode\Era` 游戏库不存在；无 `gh`/`GITHUB_TOKEN` | 宿主构建走 AGENTS.md 的 `dotnet build` 逃生通道；真机/六游戏/推送均 `[未验证]` |
| PowerShell | 5.1（中文脚本必须 UTF-8 BOM） | 新增/修改 .ps1 走 BOM 校验 |

**触点盘点**：独立只读盘点报告 `reports/compatpack/pd-recon-inventory.md`（2026-11-07，775 行 / 531 表格行 / ~475 处 file:line，HEAD a375a57；reports/ 不入库）；实施计划的行号锚点以该报告 + 当前 HEAD 为准，spec 行号仅线索。

### 3.1 基线证据（2026-11-07 实测，worktree @ a375a57）

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 宿主构建 | `dotnet build gemuera-c#.csproj -c Release -nodeReuse:false -m:1` | 0 error / 4 warning；`gemuera-c#.dll` 更新（02:37:30） |
| Core xUnit | `dotnet test tests/xUnitTest/GEmuera.Core.Tests -c Release` | 通过 303 / 失败 0 / 跳过 1 / 总计 304 |
| Facade xUnit | `dotnet test tests/xUnitTest/EmueraFacade.Tests -c Release` | 通过 78 / 失败 0 |
| ABI xUnit | `dotnet test tests/xUnitTest/EmueraPluginAbi.Tests -c Release` | 通过 4 / 失败 0 |
| SurfaceSmoke | `dotnet run --project tools/dialect-inventory/LegacyDialectSurfaceSmoke -c Release` | 通过（"Legacy dialect surface smoke passed."） |
| RuntimeSmoke | `dotnet run --project tools/dialect-inventory/LegacyDialectRuntimeSmoke -c Release` | 通过（v24MergedInstructions=561; snakeMergedInstructions=670; v24Functions=266; snakeFunctions=351） |
| CoreContractSmoke | `DOTNET_ROLL_FORWARD=Major dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release` | 通过（stderr 的 `UNDECLARED_LEGACY` 是负例探针预期输出） |
| 发布门禁（桌面档） | `Test-CompatPackReleaseGate.ps1 -ProjectRoot .` | PASS（7 项检查；桌面命令 7/7 通过） |
| governance | `Test-Governance.ps1` | PASS（M3-M7 governance contract passed） |

红基线不进入退役；执行计划 Task 0 复核上述命令。

## 4. 方案取舍（Approaches）

### 4.1 退役方式

- **A（采纳）逐 profile 五步**：每步 Red（等价门禁先红）→ Change（删模块 + 包 + 映射 + 再生清单）→ Green（G1–G5 + 桌面门禁）→ 独立 commit；顺序 v18 → megaten → erablue → erafl → snake（P-D spec §4.1 硬约束）。
  理由：spec 顺序已被 OWNER 认可；每步可独立回退/审查；失败面小。
- B 五 profile 一次性切换：失败面大、门禁无法定位、违反 spec「单 PR 单 profile」精神。否决。
- C 只包化不退役：OWNER 已明确要求「完整执行 P-D 退役」。否决。

### 4.2 第一方包清单生成

- **A（采纳）扩展现有生成器**：把 `tools/compat-pack/Generate-V18PackManifest` 泛化为 `Generate-FirstPartyPackManifests`，从引擎权威清单（`LegacyDialectInventories.Generated.cs`）、内置 `DialectModuleDefinition`/`CompatibilityProfileDefinition`、`EngineCapabilityCatalog` 推导每 profile 的 `surface.add/hide`、`capabilities`、`variantSelections`；生成物入库，门禁断言「生成器可复算 = 入库清单」。
  - **surface golden 冻结顺序**：生成器必须在退役提交**之前**（Red 阶段）把该 profile 的 `add/hide` 名单落成只读 fixture（禁止手改）；删除内置模块后生成器只读 fixture + 当前 v24 基线计算 manifest。语义上与内置一致：v24 后续增名只要不在 hide fixture 里就自然透出；不允许冻结「会话全集」再反推 hide（那会把未来 v24 增名错误隐藏）。
  - **capability 子集规则**：包只能声明 `CapabilityStatus.Wired` 的 id（`CompatPackRules` 对 DiagnosticOnly 拒载）。内置 profile 的 `RequiredCapabilityIds` 含 DiagnosticOnly 账本项（如 eraFL 的 `markup.div-v2` 三项、erablue 的 `plugin.external-assembly.v1`、megaten 的 `ui.standard-color-name.v1`）——它们没有运行期消费者，退役后仅从账本消失，禁止为「凑等价」把它们塞进包或改 Wired 状态。
  - **saveProfileId 不在包通道里**：v1 包禁带 `saveProfileId`；内置 erafl/megaten profile 的 `SaveProfileId` 由宿主侧（`scripts/GodotHost/PrototypeRuntimeNode.cs` 一类 launcher profile→save profile 映射）与 plan 分别携带，退役时**必须保留宿主映射**，不能随模块删除而丢失；等价测试包含该断言。
  - **defaultPorts 只作装饰账本**：内置 profile 的 DefaultPorts 不进入包通道，属允许差异（设计 §H1）；等价断言以行为投影为准。
  理由：v18 已有先例；清单与引擎事实同源，防手抄漂移；`baseSurfaceHash` 系统一来源。
- B 手写 5 份 manifest + 测试锁：易漂移、与 v18 纪律不一致。否决。

### 4.3 第一方包分发链路

- **A（采纳）构建内置 + 启动释放**：构建期把 5 个 pack DLL staging 到工程根 `compat_packs/<packId>/<packId>.dll`（`/compat_packs/` 加入 .gitignore，不入库）；`export_presets.cfg` 的 `include_filter` 把该目录的 DLL 打进导出物（PCK 内非资源文件）；启动早期宿主读取 `res://compat_packs/...` 字节，释放到安装根 `compat_packs/<packId>/<packId>.dll`（Android：`/storage/emulated/0/emuera/packs/<packId>/<packId>.dll`），SHA256 相同则跳过；失败不阻断启动，按 spec 回退并给用户可见提示。
  理由：两端（桌面/Android）同一套语义；开箱即用；保留「包可替换」契约（用户可手动更新/覆盖）。
- B 只做桌面导出脚本拷贝：Android 是首要目标，失效。否决。
- C 把包字节编进引擎资源直接加载：破坏包文件契约、哈希与可替换性。否决。

### 4.4 目录与命名

- `packs/` 是**唯一**兼容包源码父目录（社区 + 第一方）。第一方包目录名 = packId：`packs/pack.gemuera.<profile>/`；程序集名 = `pack.gemuera.<profile>`（部署 DLL 名与映射表一致）。现有 `packs/gemuera.v18/` 在步 1 重命名，`packId` 不变（`pack.gemuera.v18`）。
- 社区模板目录保持 `packs/CommunityPackTemplate/`（描述性命名，不作为部署单元）。
- 目录约定、部署单元（一包一目录）与源码目录的区别写进 `packs/README.md`。

### 4.5 profile 映射与回退

- 生产映射文件：`assets/text/pack-profile-map.json`（P-D spec §1.1 裁定 A），与每个 profile 退役**同一提交**加入对应条目；示例文件 `packs/pack-profile-map.example.json` 仅作文档，不参与运行。
- 优先级沿用 spec §1.2：P0 `launcher.cfg [compat_packs]`（按游戏显式）> P1 外部 `GEMUERA_COMPAT_PACKS`（诊断）> P2 映射默认 > P3 纯基线。翻译函数 `ResolveEngineProfileId` 只把已退役 profile 翻成 `v24pure`，在绑定引擎 profile 前最后一刻执行。
- **路径形态修正（builder's call）**：P-D spec §2 示例的扁平路径（`compat_packs/pack.gemuera.v18.dll`）与 2026-11-03 I1 硬化「一包一目录、目录内未被引用 DLL 拒载」冲突——映射一律用 `compat_packs/<packId>/<packId>.dll`（Android `packs/<packId>/<packId>.dll`）。
- **`requiredPackVersion` 采用记录性降级（builder's call）**：launcher 只做存在性预检；版本/ABI/表面哈希由引擎 `contractAbi`/`engineApi`/`baseSurfaceHash` 终审，拒载原因进 `[LOAD]` 日志；不引入 PEReader 依赖。若后续需要 UI 级版本提示，另行提案。

### 4.6 门禁覆盖（OWNER 显式覆盖）

`docs/compat-pack-release-gates.md` §7 规定「Gate 1-7 全绿前 P-D 保持锁定」，其中 Gate 1（Android APK e2e）、Gate 2（六游戏矩阵）、Gate 7 的 legacy-runner 三 profile 冒烟在本机不可满足。

**OWNER 于 2026-11-07 显式覆盖该锁定**：本轮按 P-D spec §4.2 的 G1–G5 + G7（result-review ≥ 98）执行；桌面可跑的发布门禁（`Test-CompatPackReleaseGate.ps1` 桌面档、`Test-Governance.ps1`、三冒烟、全量 xUnit）每步必跑。**本分支不可发布**：交付消息、最终 commit 与 evolution log 必须写明「Gate 1/2/7 未满足，需真机 + 六游戏证据补齐后才可发布」。

### 4.7 「指导文件 / Template 齐全」的定义

齐全 = 同时满足：

1. 覆盖所有包类型与工作流（社区 manifest-only 包、入口类包、第一方包、部署/更新/移除、失败回退、验证命令）；
2. 与当前代码事实一致（路径、命令、字段、常量、profile 名单）且无悬空引用；
3. 文档里的命令可在干净工作区复现（本轮实测过的命令）；
4. Template 工程可直接复制、改名、构建、加载（有 README 说明改哪些字段）。

逐项清单见 §8。

### 4.8 冻结「包校验表面宇宙」（OWNER 2026-11-07 裁定）

**问题**：退役后 `LegacyDialectInventoryGenerator` 不能再为已删 profile 建 plan（`CreateLegacySessionPlan` 抛），而 `LegacyDialectInventories.Generated.cs` 的六 profile 并集是两处运行期输入：`CompatPackHost.BuildValidationContext` 的 KnownInstruction/FunctionHandlers + ReturnTypes（校验 pack `add*` 是否真实 handler）与 `LegacySurfaceHash` 的 METHOD 投影集合（进 v24 `baseSurfaceHash`）。并集缩小会导致 snake 包 add 拒载、v24 哈希漂移（既有官方包/外部社区包全部失配）。

**裁定**：冻结包校验表面宇宙。

1. **冻结时点**：步 1（v18）开工前、任何模块删除之前，从当前 HEAD 用现生成器一次性产出只读冻结工件 `tools/dialect-inventory/retired-dialect-surface-universe.json`（入库、评审、之后只读）：五个退役 profile 的指令名清单、函数（名字+返回类型）清单、METHOD 投影并集、handler 名并集、返回类型并集。工件头记录来源 commit 与生成命令。
2. **生成器改造**：`LegacyDialectInventoryGenerator` 在冻结后改为「live v24pure profile + 冻结工件」，继续产出 `LegacyDialectInventories.Generated.cs`（V24 数组 live 再生；退役 profile 数组/并集/METHOD 投影从冻结工件抄写并带 frozen 注释）。退役步删除模块后仍可重跑且输出稳定。
3. **哈希不变量**：v24 `baseSurfaceHash` 必须保持既有值 `950dbeb9028a8a2fc73ac3699411a05b8e367458219b40bd862066c8902888c9`（步 1 起每步用钉扎测试断言；`LegacySurfaceHashTests` 既有稳定性用例同步更新）。冻结工件与「退役前 live 生成结果」的一致性在步 1 用一次性 golden 断言证明，之后靠工件只读 + 钉扎哈希。
4. **保留名单**：`CompatPackHost.ReservedModuleIds`（六模块 id）**整份保留**——它是 packId 撞名拒载历史面，不是模块在场证据；收缩会让 `packId == "game.erafl"` 之类负例重新可加载（R4）。
5. **验收表述收窄**：运行时模块/行为/profile 只剩 v24；冻结工件与 `LegacyDialectInventories` 中的退役 profile 数据定位为**包 ABI 表面事实**（不是方言模块、不参与任何会话行为），并在 AGENTS/设计文档写明，避免"Core 里还有方言数据"的误读。
5. **G3/G4 口径**：`profiles.generated.json` 与快照的退役 profile 条目随步消失（spec 允许的唯一 diff）；存活 `v24pure` 的计数/哈希逐字节不变。`profiles.generated.json` 的消费方（legacy-runner 等）按 §6.4 适配。

### 4.9 snake 名称碰撞的包表面契约扩展（OWNER 2026-11-07 裁定）

**问题**：`BITMAP_CACHE_ENABLE` 在内置 snake 会话是「真实指令可见 + v24 表达式函数隐藏」。manifest 现有模型表达不了：把函数放进 `hideFunctions` 会被双形态闭包镜像成指令也隐藏；`addInstructions` 对 METHOD 投影名拒载。`LegacyCompatibilityModules.cs:503/514` 与 SurfaceSmoke 断言是现状证据；引擎共享 handler 存有 snake 的 `SNAKE_UI_SETTING_Instruction`（`FunctionIdentifier.cs:218`）。

**裁定**：正式扩展包表面契约（增量、可版本化）。

1. **新字段**（`surface` 内，可选数组）：`addInstructionCollisions: ["<NAME>"]`。语义：为该名暴露**引擎真实指令 handler**，同时要求同名 v24 表达式函数由 `hideFunctions` 隐藏；即「指令 present + 函数 hidden」的碰撞态。
2. **校验规则**（全部 fail-closed）：
   - 名字规范化 Trim+Upper、去重；必须出现在 `KnownInstructionHandlers`（真实 raw handler，冻结宇宙已含）；
   - 必须出现在 `MethodProjectedInstructionNames`（确认存在 v24 函数碰撞面），且**不在** v24pure baseline instructions；
   - 同一名字必须**恰好**出现在 `hideFunctions` 中（成对声明才成立），且不得出现在 `addInstructions`/`hideInstructions`；
   - 组合级：同名状态与其他包不一致时仍走既有 override/conflict 规则；不增加新仲裁语义。
3. **回放**：规则层为该名产生 `Instruction Present` + `Function Hidden` 两个 symbol state，且**跳过双形态镜像**；组装段按现有 Present/Hidden 折叠，`ApplyPackSurface` 用「新增指令 + 隐藏函数」还原（不新增宿主分支）。
4. **版本轴**：新增可选字段 = 契约面增量 → `contractAbi` minor 从 `1.0` 升至 `1.1`（`Emuera.CompatPack` 解析器 + `CompatPackHost.CurrentContractAbi` + 文档同步）；`engineApi` 保持 `1.0`、`baseSurfaceHash` 输入不变 → 既有官方包（contractAbi 1.0）与外部社区包继续加载；新字段包声明 1.1，旧引擎按 minor 规则拒载（符合预期）。
5. **落地物**：`compatpack.manifest.schema.json`；`src/EmueraCompatPack/CompatPackManifest.cs` 解析；`CompatPackRules` 校验+镜像豁免；`CompatPackPlanAssembler`/`ApplyPackSurface` 验证；`packs/README.md` 与设计文档 §14 增补；xUnit（规则正/负例 + 组装计划断言）；snake 步 SurfaceSmoke/RuntimeSmoke 等价断言。
6. **snake hide 名单口径修正**：`hideFunctions` 只需包含「在 v24 基线中真实存在」的名字（v18 先例：no-op 名剔除）；`SnakeExcludedFunctionNames` 13 名中 11 名不在 v24/差集内，按 no-op 剔除并记录；`陥落状態`/`陷落状态` 一律不得隐藏。例外：`BITMAP_CACHE_ENABLE` 走 §4.9 碰撞字段。

## 5. 五步退役执行设计

### 5.1 每步通用结构（Task N）

1. **Red**：
   - 该 profile 的「v24pure + pack ≡ 内置 profile」等价门禁先写、先红：surface 键集合 + 投影可见性 + **Wired capability 集合**（行为/策略激活）+ 变体映射 + launcher 映射解析 + 宿主 save profile 映射；**允许差异**仅限：DiagnosticOnly 账本项、DefaultPorts、plan hash（模块方案不同）。
   - 固化 golden：退役前把该 profile 的 surface 名录/可见性/行为标记 / `add+hide` 名单存为只读 fixture（提交入库），供退役后对照与生成器复算。
2. **Change**（单 commit）：
   - 生成/更新该 profile 的 manifest（生成器）与 pack 工程；重命名/新增目录；
   - 删除 P-D spec §5 对应行（模块注册、桥接模块、闭包、`AllProfileIds`、launcher normalize/route/option、`Program` 三 switch 中受影响项）；
   - 加入生产映射条目 + `ResolveEngineProfileId` 翻译 + 三输入合并（步 1 落地，后续复用）；
   - 再生 `LegacyDialectInventories.Generated.cs`、`tools/legacy-runner/profiles.generated.json`、快照；
   - 测试改 golden 口径；受影响 tool（legacy-runner / SurfaceSmoke / RuntimeSmoke / dialect-compatibility-pack 目录数据）同步。
3. **Green**：G1 三冒烟、G2 全量 xUnit（Core/Facade/ABI）、G3 快照零差异（覆盖集口径）、G4 生成名录零差异（存活 profile）、G5 capability 词汇表冻结、桌面发布门禁 + governance；证据留档到 `reports/` 之外（项目内产物按规范不入库，日志摘录进 evolution log）。
4. **Commit**：`feat(pd): retire <profile> builtin module into pack.gemuera.<profile>`。
5. **交接**：更新「下一步」检查点；任一步 Green 未齐不得开工下一步。

### 5.2 各 profile 差异（来自 P-D spec §4.1/§5，执行时按当前 HEAD 重定位锚点）

| 步 | profile | 表面 | capability / 变体（包可声明的 Wired 子集） | 删除重点 |
| --- | --- | --- | --- | --- |
| 1 | v18 | 纯 hide（v18 ⊆ v24） | 无 | `gemuera.v18` 注册/闭包、`LegacyV18CompatibilityModule`、launcher v18 分支、`V18PackManifestTests` 改 golden；含 `packs/gemuera.v18` 重命名 |
| 2 | megaten | 零增量 | 5 个 Wired（启动容错 + 4 behavior；`ui.standard-color-name.v1` 是 DiagnosticOnly，不进包） | 「策略/能力常量全保留」，只删注册/桥接/闭包/profile |
| 3 | erablue | `SETANIMETIMER` 新增 | 1 个 Wired（启动容错；`plugin.external-assembly.v1` DiagnosticOnly 不进包） | 同前；插件 capability 是否需包声明由本步取证（spec 风险点） |
| 4 | erafl | erafl 差量（生成清单） | 10 个 Wired（3 个 markup 表面项为 DiagnosticOnly，不进包） | Core `EraFlCompatibilityModule` 算法库**保留**；只删注册/桥接/闭包/profile |
| 5 | snake | 最大差集（add/hide） | 19 个 Wired（DFC 需全 19 在场，LegacyCompatibilityModules.cs:199-201）+ `variantSelections builtin:snake`（FOR/SETBGIMAGE 等）；`BITMAP_CACHE_ENABLE` 走 §4.9 `addInstructionCollisions` + `hideFunctions` | 变体枚举/映射/回放**保留**；`hideFunctions` = `SnakeExcludedFunctionNames` 中「真实存在于 v24 基线」的名字（no-op 剔除，见 §4.9.6），不得含 `陥落状態`/`陷落状态` |

### 5.3 快照与生成器前置

- 步 1 同时扩展 `tools/dialect-inventory/DialectInventory.psm1` 的 profiles 映射，给 v18/erablue/megaten 增加快照条目（spec §4.2 Change 6），否则 G3 对步 1–3 的覆盖缺口只能由 G4 承担；扩展后按「退役前留档 → 删除 → 再生」对比存活 profile 的 canonicalHash 零差异。
- snake/erafl 退役步同步移除自身快照条目，并处理生成器对 `LegacySnakeCompatibilityModule` 字段名的读取依赖（spec §4.2 Change 6）。

## 6. 第一方包分发链路设计（Task D）

### 6.1 组件

1. **Core 纯逻辑** `src/Core/Compatibility/Packs/CompatPackBundleInstaller.cs`
   - 输入：`sourceRoot`（res:// 内 staging 根，按 `<packId>/<packId>.dll` 枚举）、`targetRoot`（安装根）、目标平台路径策略；
   - 行为：逐包 SHA256 比较（相同跳过）、原子写（临时文件 + rename）、创建 `<targetRoot>/<packId>/`、失败聚合返回结果；不依赖 Godot；
   - 安全：只接受 `<packId>/<packId>.dll` 形态与合法 packId 正则，拒绝路径穿越；单测覆盖幂等/损坏/权限不足/并发半成品。
2. **宿主接线**（新组件，挂在 launcher 的包扫描链上；注意 `AppBootstrap` 在 HEAD 已不存在，AGENTS/DeveloperHandoff 中的引用是过期文档，本任务不复活它）
   - 用 Godot `FileAccess` 读 `res://compat_packs/<packId>/<packId>.dll`，转交 installer；
   - **Android 时机（builder 裁定）**：`/storage/emulated/0/emuera` 只有在用户授予 All-files-access 后才可写；不得把释放固定放在 AppBootstrap/`_Ready`。释放入口挂在首次包扫描路径上——`ScanCompatPackCandidates` **之前**（此时权限已解析）、以及权限授予回调/应用 resume 后重试一次；未授权时不释放、不写错误日志，只记 `[LOAD]` Info（"等待存储权限后释放内置兼容包"）。这样避免首次启动全部回退且直到重启才恢复；
   - 目标根：桌面导出 = `OS.GetExecutablePath()` 同级 `compat_packs/`；Android = `/storage/emulated/0/emuera/packs/`；编辑器模式跳过释放（编辑器直接扫 `res://compat_packs`，见 6.3）；
   - 日志：成功 `[LOAD]` 摘要（释放/跳过/保留用户版本计数），失败 Warn + 回退提示；不得阻断启动。
3. **构建 staging** `tools/compat-pack/Build-FirstPartyPacks.ps1`
   - `dotnet build` 5 个 pack 工程（Release/net8.0）→ 拷贝到 `compat_packs/<packId>/<packId>.dll`；校验自包含（目录内只有本包 DLL）；
   - 发布/导出流程（人工或脚本）先跑本脚本再导出；`Test-CompatPackReleaseGate.ps1` 桌面档加一条静态检查（staging 缺包/版本与源不符即 FAIL），防止导出空包。
4. **导出配置**：`export_presets.cfg` 的 `include_filter` 增加 `compat_packs/*/*.dll`；`.gitignore` 增加 `/compat_packs/`。

### 6.2 与映射的配合

- 释放完成后，映射表里的 `relativePath` 必然指向已存在文件 → launcher 预检通过 → 注入 `GEMUERA_COMPAT_PACKS`；释放失败 → 映射预检失败 → spec §3.1 回退文案（「未找到 {profile} 兼容包…本次将以 v24 基线启动」）。
- **覆盖判定（builder 裁定）**：installer 在安装根维护 `.first-party-packs.json`（packId → 上次由内置释放写入的 SHA256）。规则：目标缺失 → 写；目标 SHA == 记录值 → 覆盖（接受内置升级）；目标存在但 SHA != 记录值 → 视为用户手装/改过，**不覆盖**并记 `[LOAD]` Info；本次写入成功后更新记录。这样既让内置修订能到达"没动过"的安装，又保留用户替换语义。单测覆盖：全新安装、同字节跳过、内置升级、用户改过不覆盖、记录缺失（旧版本升级路径，按"目标存在即保留"处理）。

### 6.3 编辑器模式

- **编辑器/runner 模式（builder 裁定，二者都做）**：
  1. 映射路径解析增加 editor 分支：`AbsoluteizeCompatPackPath` 在 `OS.HasFeature("editor")` 时以项目根（`ProjectSettings.GlobalizePath("res://")`）为 launcher 根解析 `relativePath`，使 `compat_packs/<packId>/<packId>.dll` 在编辑器/legacy-runner 下可直接命中；导出端仍按 exe 目录/Android 主存储根。
  2. 候选列表在 editor 模式额外枚举 `res://compat_packs` 的一级子目录（只读扫描，不改变导出端"只扫顶层"契约）。
  3. **legacy-runner 不得假绿**：`-Profile <退役 profile>` 必须翻译成 v24pure + 显式注入 staging 包路径（等价 P2 映射），并在报告中断言实际加载了期望 packId（日志 `[LOAD] CompatPack enabled` + plan hash）；staging 缺包或包未加载即 FAIL，禁止出现 state.json 写 profile=snake 但引擎跑 v24pure 的假证据。

### 6.4 诊断工具适配（legacy-runner / 生成器 / 快照）

- `tools/legacy-runner/Invoke-LegacyRunner.ps1` 现在从 `profiles.generated.json` 校验 `-Profile`；退役后该文件不再含被退役 profile。需在脚本（及 `Test-LegacyRunner.ps1` / display / session-isolation 可传 profile 的入口）加「退役 profile → v24pure + staging 包」翻译：读取生产映射，注入 `GEMUERA_COMPAT_PACKS=<staging>/<packId>/<packId>.dll`，并把实际引擎 profile 传 `v24pure`；staging 缺失时给出明确错误（提示先跑 `Build-FirstPartyPacks.ps1`），不得假跑。
- `tools/dialect-inventory/DialectInventory.psm1` 的 profiles 映射、`Invoke-DialectRegistrySnapshot.ps1`、表面/运行时冒烟与 `dialect-*.json` 目录数据中的 profile/module 引用随每步退役同步更新；任何工具冒烟红即阻塞该步。
- AGENTS.md 的「三 profile 无头冒烟」表述改为「v24pure + 第一方包（或映射）链路」，并注明真机 e2e 仍是最终验收。

## 7. 验证矩阵

| 门禁 | 内容 | 命令/入口 | 本轮状态 |
| --- | --- | --- | --- |
| G1 | 三冒烟 | `LegacyDialectSurfaceSmoke`、`LegacyDialectRuntimeSmoke`、`CoreContractSmoke`（`DOTNET_ROLL_FORWARD=Major`） | 每步必跑 |
| G2 | 全量 xUnit | `GEmuera.Core.Tests`、`EmueraFacade.Tests`、`EmueraPluginAbi.Tests` | 每步必跑 |
| G3 | 快照名录零差异 | **HEAD 现状不可直接用**：`dialect-registry-snapshots.json` 只有 v24/snake/erafl 三键且状态 Partial/Blocked，生成器 v24 快照仍读 `LegacySnakeCompatibilityModule`/`LegacyEraFlCompatibilityModule` 字段（删类即断）。步 1 先修生成器（v24 快照改读冻结表面宇宙/生成清单），保留既有 v24 条目 canonicalHash 逐字不变；snake/erafl 条目在各自退役步消失=允许 diff；v18/megaten/erablue 由 G4 承担零差异 | 步 1 修复 + 每步按覆盖集口径 |
| G4 | 生成名录零差异 | `LegacyDialectInventoryGenerator` → `profiles.generated.json` 存活 profile 逐字节不变 | 每步必跑 |
| G5 | capability 词汇表冻结 | **已既成事实**：HEAD 的 `CompatPackHost.BuildValidationContext` 已用 `EngineCapabilityCatalog.All`（CompatPackHost.cs:63），spec 中 `AllProfileIds` 已不存在；本任务只需保留/回归该项（ReservedModuleIds，CompatPackHost.cs:29-32） | 每步校验 |
| G6 | 桌面发布门禁 + governance | `Test-CompatPackReleaseGate.ps1`（桌面档）、`Test-Governance.ps1`、`dotnet build gemuera-c#.csproj -c Release -nodeReuse:false -m:1` | 每步必跑；**六游戏/Android/legacy-runner 三 profile = [未验证]** |
| G7 | 独立 review | 独立 reviewer 子代理按 spec §8 惯例打分 ≥ 98；P0/P1 清零 | 最终整支 review |
| G8 | v24 基线哈希钉扎 | `LegacySurfaceHashTests` / SurfaceSmoke 断言 `baseSurfaceHash == 950dbeb9…` | 步 1 起每步必跑（冻结宇宙不漂移的硬证据） |
| G9 | 契约增量兼容 | 旧 `contractAbi 1.0` 包（v18/模板/外部样例）继续加载；新字段负例全部拒载；packed snake plan = 指令 present + 函数 hidden | 步 5 前落地并每步回归 |
| G10 | runner 不假绿 | 退役 profile 经 legacy-runner 必须"翻译 + 显式 pack + 断言 enabled"；缺包/未加载即 FAIL。**本机无 Godot 4.7 → 只能做脚本/静态与单元级验证，执行级 `[未验证]`**（不得声称已跑） | 工具改造随计划落地；执行证据缺失记入遗留 |

补充：

- `dotnet build` 判定口径：`error CS/MSB` 计 0 且输出 DLL 时间戳更新（AGENTS.md）；若逃生通道失败，升级为阻塞并由 OWNER 决定。
- 每一步的 Green 证据（命令、退出码、测试计数、生成物 diff 摘要）记录到最终 evolution log；不得手写未实测数字。

## 8. 指导文件与 Template 补齐清单（"齐全"落点）

| # | 文件 | 动作 |
| --- | --- | --- |
| 1 | `packs/README.md` | 开篇从「社区兼容包唯一入口」改为「社区 + 第一方兼容包入口」；新增：第一方 5 包清单与 packId/目录命名、清单生成器命令、等价门禁命令、分发链路（staging/导出释放/安装根一包一目录）、profile 映射与回退文案、用户手动更新包语义；同步目录约定与失败排查 |
| 2 | `packs/CommunityPackTemplate/README.md` | 新增：复制改名步骤、要改哪些字段、构建、放置位置（一包一目录）、加载/拒绝排查、可选字段示例（gameIdentity/requires/conflicts/overrides/variantSelections，保持模板 manifest 合法） |
| 3 | `packs/pack.gemuera.<profile>/README.md`（×5） | 每个第一方包 ≤30 行：packId、来源内置模块、适用 profile/游戏、清单生成命令、等价门禁、退役提交 |
| 4 | `docs/designs/compat-pack-interface.md` | 追加 §14「P-D 执行记录（2026-11-07）」：退役结果、分发路径裁定（一包一目录）、与 I1 硬化的关系；不改既有章节 |
| 5 | `docs/compat-pack-release-gates.md` | Gate 2 矩阵「退役前/后」措辞更新；新增第一方包分发静态检查与 `[未验证]` 状态说明；§9 状态更新 |
| 6 | `docs/plans/2026-10-03-pd-module-retirement-spec.md` | 文末追加「执行记录（2026-11-07）」指针（指向本设计与 evolution log），正文不改 |
| 7 | `AGENTS.md` | 游戏适配铁律改为「差异必须以兼容包（生成清单）方式实现，禁止直接改 v24 基线共享路径」；构建与验证节更新门禁集（legacy-runner 三 profile → v24pure+包/映射链路；补分发 staging 检查与未验证声明）；文件管理规范补 `packs/` 源码与 `compat_packs/` staging 规则 |
| 8 | `readme/README.md`（及 en/ja 若含结构/方言叙述） | 核对并同步 packs/兼容包结构、方言获取方式 |
| 9 | `governance/evolution-log/2026-11-07-pd-module-retirement.md` | 收尾记录（6 要素 + 覆盖声明 + 环境障碍 + G6 未验证 + 遗留） |

## 9. 风险与缓解

| 风险 | 缓解 |
| --- | --- |
| 本机无 Godot 4.7 / 无真机 / 无游戏库 | Task 0 先跑基线构建与测试；构建走 dotnet 逃生；G6/真机 `[未验证]` 并在交付与日志写明；不宣称发布就绪 |
| P-D spec 行号锚点漂移（2026-10-03 → 当前 HEAD） | 每步开工用 grep/CodeGraph 重新定位锚点；spec 行号只作线索；删除清单逐项勾选 |
| capability/variant 等价难判定 | 等价断言用行为投影（`LegacyCompatibilityProfile` 可见性/策略/变体）而非 plan hash；差异即失败 |
| DiagnosticOnly 账本、DefaultPorts、saveProfileId 在包通道不可表达 | 等价测试显式白名单允许差异；saveProfileId 由宿主 launcher profile 映射保留（`PrototypeRuntimeNode` 等），列入每步待办；禁止把 DiagnosticOnly 改 Wired 来凑等价 |
| 删除漏项（隐藏注册/工具依赖） | 全仓 grep profile/module id + 编译 + 测试 + 快照/名录双向拉网；每步 Green 前复查 §5 清单 |
| 分发链路在 PCK/Android 路径上翻车 | installer 纯逻辑单测 + 桌面编辑器实测；Android 真机 `[未验证]`；失败必须回退且用户可见 |
| legacy-runner / 生成器 / 快照工具随退役失效 | 步 1 起同步升级工具；每步后跑工具冒烟；失效即阻塞 |
| 文档漂移（旧 profile 名/旧命令） | 最终文档任务用 grep 校验悬空引用；release-gates 与 AGENTS 单独复跑 |
| 契约扩展（§4.9）评审面大 | 只加可选字段、不改既有字段语义；`contractAbi` 1.1 + 负例测试 + 旧包兼容回归；设计文档 §14 记录动机与验收；不触碰 engineApi/baseSurfaceHash |
| 冻结工件与 live 生成漂移 | 工件只读 + 来源 commit 记录；步 1 一次性 golden 对账（live==frozen）；每步哈希钉扎；重跑生成器必须零 diff |
| 引擎绑定/翻译点遗漏（盘点确认 6 处，非 spec 的 2 处） | `FirstWindow.cs:1477/:102/:1418`、`EmueraMain.cs:151-159`（早期绑定）、`Program.cs:81-82/:397`；每步用 grep 复核并以"未翻译 id 会直接抛"的冒烟兜底 |
| **P0** `Program.ConfigureCompatibilityPlan` catch 内二次 `CreateLegacySessionPlan(plan.ProfileId)`（Program.cs:389-400/:397）在 profile 注销后二次抛出 → 启动崩 | 步 1 同时修：catch 内先归一为 v24pure 再建 plan；加回归测试（退役 id 启动失败路径仍回到基线） |
| **P1** launcher 手动 profile 条目在 normalize 失败时被静默丢弃并回写 cfg（FirstWindow.GameBrowseUi.cs:164-170） | 退役顺序硬约束：先加别名/映射与 normalize 分支，再删 catalog 条目；每步顺序写进计划步骤 |
| **P1** G3 快照工具链在 HEAD 已红/过期（DIA-10/11/12 链缺文件；生成器读将删除的类） | 步 1 修复 v24 快照来源并只做"存活 v24 条目哈希不变"口径；DIA 全链不作为本轮门禁（见 §4.6） |
| P-D spec 中 `AllProfileIds`、`AppBootstrap` 等锚点已不存在 | 以盘点报告 `reports/compatpack/pd-recon-inventory.md` + 当前 HEAD grep 为准；spec 行号仅线索 |
| 映射生产文件 `assets/text/pack-profile-map.json` 尚不存在；示例扁平路径与一包一目录冲突；v18 产物名是 `gemuera.v18.dll` | 步 1 创建生产映射（一包一目录路径）、更新示例与产物名（改名+AssemblyName=packId）；`export_presets.cfg` include_filter 需加 `compat_packs/*/*.dll`、`.gitignore` 加 `/compat_packs/` |
| 误提交 staging/编译产物 | `.gitignore` 加 `/compat_packs/`；提交前 `git status` 检查；staging 只在导出前生成 |

## 10. 独立审查（builder-check）处置记录（2026-11-07）

| # | 审查问题 | 处置 |
| --- | --- | --- |
| Q1 | 退役后六 profile 并集来源 / v24 哈希稳定性 | OWNER 裁定「冻结包校验表面宇宙」→ §4.8（冻结工件 + 生成器改读 + 哈希钉扎） |
| Q2 | snake `BITMAP_CACHE_ENABLE` 指令可见/函数隐藏不可表达；`SnakeExcludedFunctionNames` 逐字进 hide 被规则拒绝 | OWNER 裁定「正式扩展包表面契约」→ §4.9（`addInstructionCollisions` + contractAbi 1.1 + 镜像豁免）；no-op hide 名按 v18 先例剔除 |
| Q3 | 编辑器/runner 下映射路径按 exe 目录解析、runner 可能假报 profile | builder 裁定：editor 映射根 + 候选一级子目录 + runner 显式 pack/断言 → §6.3、§6.4、G10 |
| Q4 | 内置包是否覆盖用户手装包（§4.3 与 §6.2 自相矛盾） | builder 裁定：安装根记录 last-installed SHA，仅覆盖"未被用户改过"的目标 → §6.2 |
| Q5 | Android 首启在 All-files-access 授权前释放，导致首次全回退 | builder 裁定：释放挂在权限解析后的首次包扫描前 + resume 重试；未授权只记 Info → §6.1 |
| — | erafl `SETANIMETIMER` 的 `SharedTable` 变体绑定不可经 `variantSelections` 表达但行为等价（v24 与共享表在该名下同态） | builder 裁定：仅在等价门禁登记 no-op 豁免并注释，不写 manifest |
| — | P-D spec 行号锚点漂移 | 已定：每步按当前 HEAD 重新 grep，不引用旧行号 |

## 11. 交付物

- worktree 分支 `ai/pd-module-retirement`，逐步 commit（建议：Task 0 基线、5 × 退役、分发链路、文档补齐；可合并相邻 task 的 commit，但每步退役必须独立可审查）。
- 5 个第一方包 + 社区模板 + 指导文件 + 映射文件 + 分发链路。
- 每步 Green 证据（命令/计数/生成物 diff 摘要）与最终 `result-review` 结论。
- evolution log 一条；交付消息列明**不可发布**状态与六游戏/真机后续项。

## 12. 已裁定决策记录（2026-11-07 grill 七问 + builder-check 裁定）

| # | 决策 | 裁定 |
| --- | --- | --- |
| 1 | 收拢范围 | 真实包 + 五内置方言全部包化 |
| 2 | 交付深度 | 完整执行 P-D 退役（五步） |
| 3 | 交付方式 | 本地分支逐步提交，不推送/不开 PR |
| 4 | 验证范围 | G1–G5 + G7 review；G6/真机 `[未验证]` |
| 5 | 推进节奏 | 连续跑完，最后统一汇报 |
| 6 | 第一方包分发 | 加最小分发链路（内置 + 启动释放） |
| 7 | 发布门禁锁定 | OWNER 显式覆盖，继续退役；分支不可发布 |
| 8 | 冻结包校验表面宇宙（Q1） | 冻结只读工件 + 生成器改读 + v24 baseSurfaceHash 钉扎不变 |
| 9 | snake 名称碰撞（Q2） | 正式扩展包表面契约：`addInstructionCollisions` 字段 + `contractAbi` 1.1 |
| 10 | 审查 Q3/Q4/Q5 | builder 裁定并折入 §6.1/§6.2/§6.3/§6.4 与 G10 |
