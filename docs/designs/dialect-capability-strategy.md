---
design_type: initiative
created_at: 2026-09-04
---

# 方言能力组合战略：兼容全部 Era 游戏

## 问题（Problem）

gEmuera 的方言（dialect/profile）体系当前无法达成"兼容全部 Era 游戏"的目标，具体表现与根因：

1. **游戏选择机制失灵**：启动器完全按目录摆放决定方言（`emuera/` 根 → v24pure、`emuera/snake/` → snake、`emuera/compat/erafl/<游戏>` → erafl），从不读取游戏内容。内容探测器（`GameContentProbe` / `BuiltInGameCompatibilityResolver`）已存在但未接入启动链路，导致用户把游戏统一放进 snake 目录后所有游戏都走 snake，且没有按游戏记忆/手动切换方言的入口。
2. **冻结 profile 模式的结构上限**：体系只有三个写死闭包的 profile（v24pure/snake/erafl），launcher 不能选模块。真实生态需要表达：EE 小版本敏感（eraOCG2 附带 EEv41~v52 全系备用 exe）、EM 特性面（erablue 的 40 个 ERD）、eraFL 闭源 fork（能力清单目前仅 SETANIMETIMER 一条，PLAYSE 缺失）、EvilMask CN fork（魔王，但实体审查证实其魔改代码已需 1824+EM/EE 语法才能跑，随包 1821.8 exe 反而跑不动，见风险节）、EE 插件 DLL（游戏经 CALLSHARP 调用，gemuera 已有 PluginSystem 移植与 CALLSHARP 指令，但真实插件加载尚未验证）。每来一个变体加一个冻结 profile 的模式在结构上追不上生态。
3. **snake 上游（Skiav12.1）未同步**：CHKDATA 返回存档版本（RESULT:1）、多语言编码逐字符往返回退（直接影响 GBK 汉化游戏的 STRLEN/SUBSTR 语义）、用户自定义变量 .als 别名、PLAYSE 等 ERB 语义差异缺失；AGENTS.md 所指旧参考路径已失效（新源码位于带 " (2)" 后缀的目录）。
4. **验收基准缺位**：用户提供了六个代表性 Era 游戏（覆盖 v8.1 / v22 / v24+EMv18+EEv4x~v55 / EE 插件依赖 / eraFL / EvilMask CN），需要一套可重复的兼容性验收门槛。

## 愿景（Vision）

任何 Era 游戏：放入游戏库即被自动识别所需的方言能力组合（探测器给出候选+置信度），或由用户一键选择预置组合/手动微调；选择按游戏记忆。运行中遇到未启用的方言能力时，以"建议启用 XX 能力模块"的提示暴露，而非启动失败或静默错误。六个基准游戏全部通过"无头冒烟到输入等待 + 真机抽测"验收。

## 非目标（Non-goals）

- 不同步 snake/EE 桌面端 DEBUG 与 UI 特性（调试窗口锁定列、ToolTip 修复、GC/内存诊断日志）——用户已明确跳过。
- 不逐条预实现全部 EE 小版本行为差异——EE 小版本差异没有系统文档，按基准游戏实测驱动补充。
- 不整合 Wine/Winlator 原生 exe 路线（独立 spike，另行决策）。
- 不重写 Core 合同体系（`src/Core/Compatibility/` 的模块/计划骨架保留，在其上扩展）。
- 不在本 initiative 内追求 iOS 等无 JIT 平台的插件加载。

## 干系人（Stakeholders）

- **用户**：方向决策、真机 APK 抽测验收、游戏包提供。
- **Agent**：实现、无头冒烟矩阵维护、上游盘点。
- **上游生态**：snake fork（lazyloading skiasarp，CHANGELOG 为同步契约）、emuera.em（v24/EM/EE 参考）、eraFL（闭源，以游戏脚本行为反推能力）、EvilMask CN fork（以随包 exe 与实测反推）。

## 架构（Architecture）

### 1. 方言模型：能力模块组合，profile 退化为预置

- 组合原子是 `DialectModuleDefinition`（能力模块，如 v24 基线、snake 扩展、EM-ERD、EE 小版本补丁、eraFL 能力、EvilMask 差异）。每个游戏一个 `CompatibilityPlan`，由三方合成：**内容探测（自动）+ 目录摆放（默认值）+ 用户覆盖（最高优先）**。
- 现有三个冻结 profile 保留为预置组合（快捷方式），不再扩展清单。
- 解析期"方言切换提示"升级为"检测到 XX 指令/函数，建议启用 XX 能力模块"（含一键启用并重载）。
- 计划校验沿用现有闭包规则（模块必须来自目录、无环、基线依赖满足）。

### 2. 内容探测接入启动链路

- `GameContentProbe` / `BuiltInGameCompatibilityResolver` 接入 FirstWindow：游戏入库/首次启动时执行，产出候选模块集与置信度。
- 探测锚点以 **exe 版本签名、特征文件存在性、指令扫描**为主，config 仅作辅助（`ERD機能:YES` 是引擎写盘默认噪声，与实际 ERD 使用无关；键名还有 `内部で使用する東アジア言語` 等变体；megaten 的 config 为 SJIS，其余为 UTF-8 BOM）。

2026-09-04 实体审查确证的锚点表（Phase A 探测器规格输入）：

| 游戏 | 确定性锚点 | 备注 |
|---|---|---|
| eraMegaten | gemuera 自家 exe（版本串 `0.2.6.0+16221ee...`）+ 同名 `.profile` 文件 + `gemuera_*.log`/`emuera_startup_errors.log`（含 "Snake startup errors"） | 已在 gemuera Android 端 snake 下实跑 |
| erablue resort | exe 版本串 `1824+v24+EMv18+EEv55`；`Plugins/*.dll` + ERB 含 `CALLSHARP`；**40 个 `.ERD` 文件**；config 键 `VARSIZEの次元指定をERD機能に合わせる:YES`（五组独有） | `CALLSHARP LAUNCH_BROWSER(...)`（NEWGAME.ERB）是新建游戏硬依赖 |
| eraOCG2 | exe 版本串 `1824+v22+EMv18+EEv52fix`；`資料/Emuera旧Ver/` 目录存在 | EE 小版本敏感主体 |
| eraFL0.47 | `EmueraFL_v*.exe`（版本串 `FLv...`）；`FLconfig.xml`；CSV 专表（FLconfig/FLTUTOR/VarExtFL）；ERB 含裸 `PLAYSE` | `FL_` 前缀标识符是游戏命名约定（FL_PLAYSE 等包装层），不作锚点 |
| 魔王(伪eramaou_EX (2)) | `eramaou_EX 0.93.exe`（1.821.8、"Emuera 简体中文"）；`lang/emuera.zhs.xml`；`CSV/_fixed.Config` | 推荐引擎是随包 `1824+v18+EMv17+EEv41`（代码用 ++/-- 复合赋值 ×327，1821 跑不动） |

跨游戏规则：插件探测盯 `Plugins/` + `CALLSHARP` + `pluginsAware.txt`（五组 `CALLPLUGIN` 全为 0）；ERD 需求只按 `.ERD` 文件存在性判定；`CUSTOMDRAWLINE`（EM 层）五组全有且高频（176/2941/2058/10/2），可作 EM 指令层的回归语料。

- 探测结果按游戏持久化（launcher 侧配置，不写入游戏目录），用户可改。

### 3. EE 插件/CALLSHARP 兼容（验证补齐，非从零移植）

2026-09-04 实体审查确认：gemuera **已移植** snake 的 `PluginSystem`（`PluginManifestAbstract`/`IPluginMethod`/`PluginMethodParameter`/`PluginManager`，含 `AssemblyLoadContext` 引用重定向与 `LoadFromAssemblyPath` 真加载）与 `CALLSHARP` 指令（SNAKE 模块下，`SNAKE_CALLSHARP_*`），并有 `AgentBridge/AgentLlmMethods` 原生 LLM 方法桥。游戏侧的插件调用指令是 **CALLSHARP**（五个基准游戏 `CALLPLUGIN` 全为 0）。

剩余工作：
- **真实插件加载验证**：桌面 + Android 真机各加载真实插件（erablue 的 LAUNCH_BROWSER.dll、StringBuilderPlugin.dll；eraFL 的 EllesUtil.dll + Scriban.dll），验证类型解析、JIT 与运行。
- **erablue 硬依赖**：NEWGAME.ERB 的 `CALLSHARP LAUNCH_BROWSER(...)` 是新建游戏必经路径——DLL 直载或 Godot shim（`OS.ShellOpen`）至少其一必须跑通；StringBuilderPlugin 在 erablue 实际未使用。
- **降级策略**：未知插件/加载失败时警告继续，不阻断；常用插件保 shim 回退。
- eraFL 的 LLM 集成（CALL_GEMINI/CALL_OLLAMA/CALL_GENERIC_LLM_API）默认关闭（FLconfig.xml `use_service` 全 false），作为可选能力验证。

### 4. snake 同步（Skiav12.1 基线）

- ERB 语义层全量同步（跳过 DEBUG/桌面 UI）：CHKDATA 存档版本、多语言编码往返回退（对 GBK 关键）、用户变量 .als、PLAYSE，以及后续 diff 盘点出的其余语义差异。

**2026-09-04 编码往返项对照结论（Phase B 执行时定案）**：snake Skiav12.1 修复的是"ANSI 代码页（如 GBK）下逐字符字节计数的往返错乱"——其 `LangManager.GetByteCountLang` 以当前代码页编码计长并用 932 回退。gemuera 的对应实现（`Scripts/uEmuera/Utils.cs` GetByteCount，`Scripts/Emuera/_Library/LangManager.cs`）是**显示宽度模型**：零宽字符跳过、半角计 1、其余计 2，完全不依赖代码页。snake 所修的缺陷类（编码不可往返字符的长度错乱）在 gemuera 模型中结构性不存在，故**不移植**该修复。两模型的已知差异点（零宽字符、代理对、既非当前代码页亦非 932 可表示字符的计量）属 gemuera 自有显示语义而非 snake 缺陷；后续若实测 PRINTC/PRINTLC 对齐或 SUBSTR 出问题，按个案对照处理。
- 同步基线记录：本次 CHANGELOG 12.1 盘点结论作为新基线写入治理文档；AGENTS.md/工具脚本的参考路径更新到新源码目录。
- snake 语义对齐继续走既有流程：改 `Scripts/Emuera/` 前对照 develop 源码 + dialect-inventory 门禁。

### 5. 六游戏验收矩阵

- 游戏解压入库 `E:\MyCode\Era`：五游戏已解压就位（版本化目录名，魔王为 `(2)` 后缀新副本）；eratohok 尚未解压，待补。
- 门槛：legacy-runner 按每游戏兼容计划无头跑到"输入等待"，核对关键输出（标题、首屏、无致命解析错误）；APK 真机由用户抽测。
- eraMegaten 在 snake 下已有实测通过记录，作为回归锚点。

## 阶段拆分（Phase Breakdown）

| 阶段 | 内容 | 依赖 | 对应问题 |
|---|---|---|---|
| **A. 方言选择与组合机制** | per-game 计划存储与合成（探测+目录默认+覆盖）、探测器接入 FirstWindow、UI 每游戏选择预置/手动组合、运行时"建议启用能力"提示升级 | 无（先行） | 问题 1 + 问题 2 骨架 |
| **B. snake 12.1 语义同步** | CHKDATA 版本、编码往返回退、用户 .als、PLAYSE、盘点出的其余语义差异、版本签名与参考路径更新 | 无（可与 A 并行） | 问题 3 |
| **C. CALLSHARP/插件兼容验证补齐** | 真实插件加载验证（桌面+Android）、erablue NEWGAME 硬依赖跑通、未知插件降级与 shim 回退 | 无（可与 A/B 并行；PluginSystem 已在库，工作量集中在验证） | 问题 2（插件面） |
| **D. 能力模块扩充与六游戏验收** | eraFL 能力清单扩充（音频等）、EE 小版本按实测补模块、六游戏入库与无头冒烟矩阵、真机抽测清单、megaten 回归 | A、B；erablue 验收项另依赖 C | 全部收口 |

每个阶段独立走 feature/phase 级 brainstorming → writing-plans → 实现 → PR（指向 `dev`）。

## 风险与开放问题（Risks & Open Questions）

- **Android 运行时加载托管 DLL**：Godot .NET 导出的 Android 端是否允许 JIT 加载任意程序集未经验证，是 Phase C 最大不确定性（PluginSystem 已在库，验证前置就是为了它）。
- **EE 小版本差异无文档**：只能实测驱动；eraOCG2 自带多版本备用 exe 即社区对此长期痛苦的证据。
- **加密分发包**：erablue zip 密码已由用户处理（已解压）；eraOCG2 的 "config 加密" 是 zip 层假象，解压后明文可读。config 键有噪声（`ERD機能:YES` 为引擎默认值），探测不依赖 config 内容。
- **EvilMask fork 差异深度**：实体审查证实魔王魔改代码使用 ++/-- 复合赋值（×327，1824+EM/EE 语法），随包 1821.8 CN fork exe 跑不动魔改代码，其 `emuera.log` 留有解析失败记录；正确组合按随包 `1824+v18+EMv17+EEv41` 等价能力即可，EvilMask 1821 差异模块大概率不需要（除非要跑未经魔改的原版 0.93 代码）。
- **eratohok 约 450MB 音频**：手机端存储与 lazy loading 策略需要在 Phase D 实测评估；eratohok 目前尚未解压入库。
- **snake 源码无 git 历史**（zip 解压）：同步只能以 CHANGELOG + 文件对照，本次盘点即基线，后续更新需人工 diff。
- **开放**：per-game 配置落在 `user://launcher.cfg` 还是独立文件（Phase A 设计时定）；探测器对"包内多个引擎 exe"（魔王、OCG2）如何选主方言（建议：以游戏自带主启动器 exe 为准，允许用户改）。

## 治理

- 各阶段 PR 指向 `dev`，每 PR 单一问题；ERB 语义改动必须走 legacy 路径验证（ERBAPI.md 合同）+ dialect-inventory 门禁。
- 真机结论以 APK 实测为准，桌面仅调试。
- 阶段收尾按 `governance/README.md` 写进化记录。
