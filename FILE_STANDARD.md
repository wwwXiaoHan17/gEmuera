# 文件细分与规模标准

> 适用基线：`451ea19`（2026-08-22）。本文件是 `AGENTS.md`「文件管理规范」第 6 条的展开，约束 C# 源码的文件规模与拆分方式。
> **§6 是硬约束**：本仓库至少有六类机制按"精确文件路径"钉扎或链接源码，拆分前必须逐一核对，否则会静默弄坏工具工程与契约门禁（legacy-runner 的路径钉扎已因 M0→LegacyRunner 改名实际损坏过一次，是真实先例）。
> 行数快照会随存量迁移过时，以附录 A 的"快照日期"为准，任务性更新即可，不必每次提交同步。

## 1. 目的与适用范围

单文件巨型化的实际代价（本项目 2026-08 实测）：`EmueraContent.cs` 9116 行、`Creator.Method.cs` 8306 行——AI 协作时上下文装不下、review 精度下降、多人/多 AI 并行修改时冲突面大、局部编辑容易误伤无关功能域。

适用范围：`Scripts/**/*.cs`、`src/Core/**/*.cs`、`tests/**/*.cs`、`tools/**/*.cs`。
不适用：GDScript、`.tscn`、资源文件（资源布局见 `AGENTS.md` 文件管理规范第 1、2 条）。但注意：拆分 C# 文件会反向影响引用它的 `.tscn`/`.gd`，见 §6.6。

## 2. 规模阈值

| 对象 | 目标 | 软上限 | 硬上限 |
| --- | --- | --- | --- |
| 新建文件 | ≤ 600 行 | 800 行 | 1500 行 |
| 既有 ≤ 2000 行文件 | — | 2000 行 | 超过即进入"待拆"状态 |
| 既有 > 2000 行文件 | 只减不增 | — | — |

- 硬上限的豁免仅限：生成代码、单一注册表/映射表（如 `ConstantData` 类的常量表）、迁移中的原版移植文件（须在 PR 说明中写明豁免理由）。
- **"只减不增"的精确含义**：
  - 新增**成员**（方法/属性/字段）默认写入对应功能域分片（没有分片就新建分片），不写入主文件；
  - 主文件允许**就地修改**（修 bug、调整既有逻辑），但每个 PR 使主文件净增不得超过 50 行；"净增"以与 `dev` 的 merge-base 为比较基线；
  - **紧急 hotfix 豁免**：标注 `[hotfix]` 的 PR 可放宽到净增 ≤ 100 行，但 PR 描述必须登记"待拆分跟进项"，由后续触碰迁移偿还。
  - 小功能域（不足 200 行、切不出去）的新增代码写入主文件时，按上述 50 行净增额度执行，不强迫为几行代码建分片（避免与 §5"≥200 行才值得拆"矛盾）。
- 行数统计口径：按字节 LF 计数。注意 PowerShell `Get-Content` 对大号无 BOM UTF-8 文件会系统性少算（实测 9116 行被算成 8799），附录 A 的再生成命令见 §2.1。
- **⚠️ 行号索引同样不能用 `Get-Content`（2026-09-13 补）**：它不只是少算行数，还会**整体错位行号**。
  后果比少算严重得多——**按行号区间做剪切/统计的脚本会静默算错区间**，不报错、结果错。
  实测两例：`RuntimeDiagnosticsPanel.cs` 2138→`Get-Content` 记 2068（且"575–577 行"落在完全无关的成员体中部）、
  `FirstWindow.cs` 2050→1980。某次侦察因此把 `AddCheck` 计数算成 140（实为 139）、`BeginTab` 算成 4（实为 8）。
  **规则**：行数用 `ReadAllBytes` 数 `0x0A`；行号索引用 `[IO.File]::ReadAllLines($p,[Text.Encoding]::UTF8)`。

### 2.1 行数清单再生成（待拆状态的权威判定）

```powershell
Get-ChildItem Scripts,src -Recurse -Filter *.cs -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { $b=[IO.File]::ReadAllBytes($_.FullName); $n=0; foreach($x in $b){if($x -eq 10){$n++}}; [PSCustomObject]@{Lines=$n;Path=$_.FullName} } |
  Where-Object Lines -gt 2000 | Sort-Object Lines -Descending
```

涉及 >2000 行文件的 PR，描述中引用该清单当前状态（哪些文件在册）。不引入 CI 门禁，靠这条流程纪律兜底。

## 3. 拆分模式（按风险从低到高）

### 模式 A：机械分片（partial class 按功能域）

同目录新建 `Type.Feature.cs`，把该功能域的成员整体搬过去；类型、命名空间、符号、行为全部不变。**这是默认模式**，diff 可机械 review。

既有先例（直接沿用其风格）：`Scripts/EmueraContent.Canvas.cs` / `EmueraContent.AndroidSpriteAnime.cs` / `EmueraContent.LegacyRunner.cs`；`Creator.Method.DT/Map/Sql/Xml.cs`；`EmueraConsole.Print.cs`。

### 模式 B：多类文件按类族拆

一个文件里塞了多个独立类型时，按类族拆成独立文件（不需要 partial）。先例：`Process.CalledFunction.cs`（独立类 `UserDefinedFunctionArgument`）、`Process.State.cs`（独立 enum）。注意：这两个文件名容易被误认为是 `Process` 的分片，实际不是；且它们的既有路径被 `tools/core-contracts/Test-CoreArchitecture.ps1` 钉扎——**新拆文件用 `类名.cs` 命名，且不要改名/移动这两个既有文件**。

### 模式 C：职责抽取（提取协作者类/服务）

改变类型边界与调用关系，风险最高。默认不做；仅当能显著降低复杂度或重复时，作为独立任务执行，并按 `AGENTS.md` 开发方式章节走完整验证。目标文件属于 `Scripts/Emuera/` 解释器路径时，必须先按 `ERBAPI.md` 的"先确定执行路径、先证明未选择侧不变"规则核对边界（模式 B/C 对解释器文件同样适用）。

## 4. 命名与位置约定

1. 分片文件命名 `Type.Feature.cs`，`Feature` 用功能域名（`Canvas`、`Cbg`、`Input`、`SpriteAnime`、`Diagnostics`…）。禁止 `Part1/Part2`、行号段、阶段代号（呼应 `AGENTS.md` 禁令）。
2. 分片与主文件同目录、同命名空间；`Scripts/uEmuera/partial/` 这类目录级归组的既有先例可沿用，但不新建同类目录。
3. 主文件保留：类型声明、构造/生命周期、核心字段与跨功能域共享的成员；单一功能域成员整体迁出，不留一半。
4. 分片文件头注释模板（一行即可）：
   ```csharp
   // EmueraContent.Cbg.cs —— 承载 CBG 背景图层提交/刷新功能域，自 EmueraContent.cs 拆出（原因：主文件超 2000 行只减不增约束）。
   ```
5. **纯移动 PR 允许的机械 diff**：成员搬移、分片头注释、主文件因迁出而失效的 `using` 清理与空 `#region` 删除。这些都属于"零逻辑变化"；除此之外的任何改动（改语句、改签名、调顺序产生的语义差异）都必须拆到后续 PR。
6. `Scripts/Emuera/**` 与 `Scripts/uEmuera/**` 豁免 `.editorconfig` 命名规则的现状不变，新分片沿用所在家族的实际风格。

## 5. 存量迁移策略（防大爆炸重构）

原则：**不做专项大重构，只做"触碰迁移"**——当任务要修改某个 >2000 行文件中的功能域，且该功能域能干净切出（≥200 行、迁出后与主文件的相互引用是单向的）时：

1. 先提一个**纯移动拆分 PR**（§4.5 定义）；
2. 拆分合入后再提功能修改 PR（此时 diff 已变小）；
3. 两者不得混在同一 PR（`AGENTS.md`：每个 PR 只解决一个明确问题）。

存量清单与建议（**已按 2026-09-13 字节 LF 快照更新**；原始 2026-08-22 快照见 git 历史与附录 A）：

| 文件 | 行数 | 模式 | 备注 |
| --- | --- | --- | --- |
| `Scripts/EmueraContent.cs` | 8105 | A | **方言 marker 分类文件**（见 §6.1）——但实测 marker 命中 **0**，那 3 条 fileRegex 是惰性条目，拆分**不需要**改方言目录；不可迁核心 ≈1616 行，单轮不可能脱离 2000，目标是"只减不增 + 每轮 -30%" |
| `Scripts/Emuera/GameData/Function/Creator.Method.cs` | 6282 | A | snake-alignment 钉扎（§6.2）；沿用 DT/Map/Sql/Xml 先例按函数类别继续拆。**注意：本文件 206 个类型声明全部嵌套**，无独立顶层类 → 只能用模式 A |
| `Scripts/Emuera/GameData/Variable/VariableEvaluator.cs` | 3796 | A | **save-baseline SHA + 8 个方法符号钉扎**（§6.3）；`#region File操作`(2482–3610) 硬锁，**结构下限 ≈2057 行，不可达 2000** |
| `Scripts/Emuera/GameData/Variable/VariableToken.cs` | 3524 | A/B | 2026-09-13 已搬出 VD.ref（694 行）。注意该文件装的是 `VariableData` 的实现，分片须命名 `VariableData.*`；实测还有 6 个 ≥200 行类族可切，全搬完 ≈1102 |

**2026-09-13 已完成纯移动、脱离在册清单的 8 个文件**（提交 `8eef88a`、`e8ad27c`、第三批）：

| 文件 | 原 → 现 | 新分片 |
| --- | --- | --- |
| `Scripts/Emuera/GameProc/Function/Instraction.Child.cs` | 4046/4051 → **1856** | `FunctionIdentifier.{FlowControl,Input,Print,Data}Instruction.cs` |
| `Scripts/Emuera/GameView/EmueraConsole.cs` | 2893 → **1947** | `EmueraConsole.Cbg.cs`、`.Input.cs` |
| `Scripts/GenericUtils.cs` | 2926 → **1094** | `GenericUtils.{Audio,Trace,DiagnosticsLogging}.cs` |
| `Scripts/Emuera/GameProc/Function/ArgumentBuilder.cs` | 2371 → **1192** | `ArgumentParser.TypeChecked.cs` |
| `Scripts/Emuera/GameData/ConstantData.cs` | 2633 → **1750** | `ConstantData.CsvFieldParsing.cs`、`.KeywordLookup.cs` |
| `Scripts/Diagnostics/RuntimeDiagnosticsPanel.cs` | 2138 → **882** | `RuntimeDiagnosticsPanel.Options.cs` |
| `Scripts/Emuera/GameView/HtmlManager.cs` | 2042 → **1826** | `HtmlManager.DisplayHtml.cs` |
| `Scripts/FirstWindow.cs` | 2019/2050 → **1653** | `FirstWindow.DiagnosticsSettings.cs` |

> ⚠️ 快照纪律对账：`Instraction.Child.cs` 本表原记 4046，实测已是 4051 —— **该文件在拆分前就违反了 §2「只减不增」**。
> 另：所有行数以字节 LF 口径实测，`Get-Content` 在本仓会**少算并整体错位行号**，索引行号务必用 `ReadAllLines`。

## 6. 钉扎与链接机制（硬约束，拆分前必查）

本仓库至少有以下六类机制按精确路径钉扎/链接源码。**拆分前通用检查**（对目标文件与新建分片路径各跑一遍）：

```powershell
# 1) 方言分类规则与 catalog 是否命中该路径
rg "\.cs" tools/dialect-inventory/dialect-classification.json tools/dialect-inventory/dialect-name-lookup-contract.json tools/dialect-inventory/dialect-profile-selection.json
# 2) tools 工程是否显式链接该文件
rg "<Compile Include" tools -g "*.csproj"
# 3) 契约测试/清单是否按路径或内容钉扎该文件
rg "\.cs" tools -g "*.ps1" -g "*.json"
# 4) 场景是否无 uid 硬引用该脚本路径
rg "res://Scripts" assets/scenes -g "*.tscn"; rg "preload" test -g "*.gd"
```

### 6.1 方言证据链（dialect-inventory）

- **钉扎以文件为准，不凭记忆**：`tools/dialect-inventory/dialect-classification.json` 的全部 `fileRegex`（marker 分类规则）+ `dialect-name-lookup-contract.json` 与 `dialect-profile-selection.json` 的 `sourceFiles`。
- **⚠️ 2026-09-13 实测纠正：marker 是「标识符正则」，不是「文件分类」**（原文此处有误，曾据此白劝退两个低风险目标）。
  机制在 `DialectInventory.psm1:5-16`，共 **10 条**正则（如 `profile.is-snake` → `\bIsSnakeProfile\b`）。
  分类器**逐行扫 `Scripts/**/*.cs`，只有某行真的命中某标识符时**才去查 `fileRegex`（`:230-232`）；
  命中而查不到规则才抛 `Unmapped dialect branch hit`（`:58-61`），**且要求恰好命中 1 条规则**（0 条→throw，>1 条→ambiguous throw）。
  推论（都已实测）：
  - **文件里没有那个标识符 → 规则永不参与判定**，是「声明了但惰性」的条目，拆它**不需要动 `dialect-classification.json`**。
  - 哈希集（`$hashedSources`）**只收「真的命中过 marker 的文件」+「含注册行的 sourceFile」**（`:248`、`:257`），
    所以**不是**"拆分任何被命中的文件都会改变 DIA-01/02 哈希"——没进哈希集的文件，拆了哈希不动。
  - **实测**：`Scripts/EmueraContent.cs`、`Scripts/Emuera/GameView/EmueraConsole.cs`、`Creator.Method.cs` 的 10 条 marker 命中数**全为 0**；
    它们对应的那些 `fileRegex` 规则都是惰性条目 → 拆这三个文件**无需改方言目录**。（`EmueraContent.cs` 有 3 条规则命中它，容易被误读成"marker 分类文件"。）
  - 反例（真的要小心的是这个）：`Scripts/FirstWindow.cs` 实测 marker 命中 **36 处/35 行**，且它同时被
    `dialect-profile-selection.json` 以**整文件 SHA-256** 钉扎 → 拆它必须同 PR 重算该 sha256。
    本仓已在 2026-09-13 拆分中处理过一次（只重钉了 `FirstWindow.cs` 自己那条，另两条既有漂移未动）。
- **`FunctionIdentifier.cs` 与 `Creator.cs` 禁止把注册行/注册方法移入 partial**：生成器 `Get-CSharpBlock` 有单声明约束，运行时隔离校验靠文本正则钉在这两个文件上，注册表行一旦移动，生成直接 `exit 1`。这两个文件只允许原地修改。
  - 补充：注册行在同目录**其它文件**里也不存在——实测全仓 241 处 `addFunction(FunctionCode.` **全部**在 `FunctionIdentifier.cs`；表达式注册（`["KEY"] =`）全在 `Creator.cs`。所以拆 `Instraction.Child.cs`（同属 `partial class FunctionIdentifier`）是安全的，只要不搬注册块。
- **新分片不得包含方言 branch marker 代码**：新路径文件**真的命中** marker 而无 `fileRegex` 规则会抛 `Unmapped dialect branch hit`。
  但**先确认那个功能域里到底有没有 marker 标识符**（见本节第一条纠正：命中判定是逐行标识符正则，不是文件分类）。
  实测 `EmueraContent.cs` / `EmueraConsole.cs` / `Creator.Method.cs` 迁出的分片 marker 命中均为 0，**无需新增 `fileRegex`**。
  确实含 marker 代码时，才需要更新 `dialect-classification.json` 的 `fileRegex` 并重跑门禁。
- 特例：`Creator.Method*.cs` 当前不在注册快照哈希集内（仅影响停摆未运行的 DIA-03~06 链），拆它不需要再钉扎注册快照。
- **再钉扎流程**（拆了钉扎文件时）：

```powershell
# 0) PS 5.1 + GBK 代码页下无 BOM 的 .psm1 无法 Import-Module，需先换 UTF-8 环境或 PS7
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Invoke-DialectInventory.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Invoke-DialectRegistrySnapshot.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectInventory.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectRegistrySnapshot.ps1
```

新快照 JSON 与源码变更**同一 PR** 提交。先例：`governance/evolution-log/2026-08-22-erafl-dialect-manifest.md`。
**存量红纪律**：这些门禁当前存在与拆分无关的既有失败。正确做法是拆分前先在干净基线跑一次门禁并记录既有失败清单，PR 中只需证明**失败集合相对基线无新增**。
> **2026-09-13 实测基线**（原文此处举例的"`Test-DialectRegistrySnapshot` 347/83 过期计数断言"已过时——该门禁实测**通过**）：
> - **通过**：`Test-DialectInventory.ps1`、`Test-DialectRegistrySnapshot.ps1`、`Test-DialectSignatureInventory.ps1`。
>   注意前两者都是**跑合成临时工程**（`$syntheticRoot`）或只断言投影性质，**不比对冻结哈希**，所以拆分真实文件不会打破它们。
> - **失败（13 个）**：其余 `Test-Dialect*.ps1`，主因是 `docs/NewFrameworkDesign/generated/` 下**生成物缺失**
>   （`dialect-inventory.json`、`dialect-plan-preflight.json`、`dialect-policy-surface.json` 等，该目录实测只有 `dialect-registry-snapshots.json`），
>   另有 DIA-03 的 `sourceDescriptorSetHash` 漂移。
> - **无法直接运行（2 个）**：`Test-DialectOwnershipEvidence.ps1` 需 `-UpstreamProjectRoot`、`Test-LegacyDialectUpstreamDiff.ps1` 需 `-V24ProjectRoot -SnakeProjectRoot`。
> - 完整清单见 `reports/baseline-gates.md`（该目录被 gitignore）与 `governance/evolution-log/2026-09-13-*.md`。

### 6.2 snake-alignment 表面钉扎

`tools/snake-alignment/Test-SnakeReferenceSurface.ps1` 按精确路径 + 内容正则钉扎约 20 个源文件（含 `Creator.Method.cs`、`EmueraConsole.cs`、`ArgumentBuilder.cs`、`Instraction.Child.cs`、`EmueraContent.cs`、`HtmlManager.cs` 等 §5 大部分目标）。成员迁出会使正则在原路径失配。拆这些文件时：同 PR 更新该脚本的 Path 指向或断言位置，并重跑该门禁。

### 6.3 save-baseline 哈希与字面量钉扎

`tools/save-baseline/legacy-save-baseline.json` 对 5 个源文件（含 `VariableEvaluator.cs`、`VariableData.cs`、`EraBinaryDataReader/Writer.cs`、`CharacterData.cs`）做 SHA-256 + 字节长度钉扎，并有 `requiredLiterals` 要求具体代码文本留在指定路径内。拆这些文件须同 PR 重算哈希并核对字面量归属。

### 6.4 legacy-runner 契约测试

`tools/legacy-runner/` 下 **7 个** `Test-*.ps1` 按精确路径钉扎源文件，`Test-InProcessSessionCycle.ps1` 把会话重置语义文本钉在 `GenericUtils.cs` 内。拆 `GenericUtils.cs`（日志桥/UI 队列/诊断导出域）时须同 PR 更新断言位置并重跑。

> 这 7 个脚本**没有任何聚合入口**，且**不是同一类**：其中 6 个是纯文本契约测试
> （`-ProjectRoot` 可选，无参可跑），第 7 个 `Test-LegacyRunner.ps1` 是**端到端 Godot 运行器**，
> 其 `-GodotPath` 与 `-GameRoot` 是 `Mandatory = $true` ⇒ 不带参数跑它必然抛
> `Cannot process command because of one or more missing mandatory parameters`。
> 别把它那条 exit=1 当成契约失败（本轮本人踩过一次）。
>
> 即"拆了文件要重跑"目前靠人记得。改这些脚本后请手工逐个跑
> `powershell -NoProfile -ExecutionPolicy Bypass -File tools\legacy-runner\Test-<X>.ps1 -ProjectRoot <项目根>`，
> 或补一个聚合脚本（尚未有）。

**存量损坏与修复结果（2026-09-13 实测量化 → 2026-09-15 全部修完）**：M0→LegacyRunner 改名时失联，**其中 6 个**（第 7 个 `Test-LegacyRunner.ps1` 本就无 M0 引用）在任何内容断言之前就 throw，
（`In-process session-cycle contract file is missing: D:\gemuera\Scripts\M0\LegacyRunnerConfig.cs`，exit=1）。
实测：**before = 6/6 红、断言执行数 0**；2026-09-13 机械修复后 **5 个转绿**，
只剩 `Test-InProcessSessionCycle.ps1` 红——但它现在**执行了 221 次断言求值**，4 条失败各指名契约与符号，不再是"文件缺失"。
**2026-09-15 补：第 6 个也已转绿（exit 0）**，那 4 条经查全部是 rename-only，见下节更正块。

已修的三类机械问题：
- **15 处陈旧路径** = 14 处 `Scripts\M0\X.cs` → `Scripts\LegacyRunner\X.cs`（8 个目标文件都真实存在：LegacyRunnerConfig / LegacyRunnerHost / LegacyRunnerReportWriter / LegacyDisplayObservation / LegacyInputReplayDriver / LegacySettlementTracker / LegacyTraceEvent / LegacyTraceRecorder）
  ＋ 1 处 `Test-LegacySettlement.ps1:17` 的 `Scripts\EmueraContent.M0.cs` → `Scripts\EmueraContent.LegacyRunner.cs`（前者已不存在，仓库里还留着一个孤儿 `EmueraContent.M0.cs.uid`）。
- **命名空间**：测试内嵌的 C# 探针写的是 `namespace gEmuera.M0` / `[gEmuera.M0.LegacyTrace]` 等；生产端已全部是 `gEmuera.LegacyRunner`。**C# 类型名本身没改**（`LegacyTraceRecorder`/`LegacySettlementTracker`/… 都还在），只改了命名空间。
- **断言符号的纯改名**：`CreateM0RunnerSessionLaunchRegistry`→`CreateLegacyRunnerSessionLaunchRegistry`、`SwitchLegacySessionForM0RunnerAsync`→`…ForLegacyRunnerAsync`、`RestartLegacySessionForM0RunnerAsync`→…、`ConfigureM0Runner*`→`ConfigureLegacyRunner*`、`TryResolveM0RunnerDefaultOutputLogPath`→`TryResolveLegacyRunnerDefaultOutputLogPath`、`ConfigureM0RunnerDisplayBackend`→`ConfigureLegacyRunnerDisplayBackend`、`CaptureM0Screenshot`→`CaptureScreenshot`、`CaptureM0LegacyDisplayObservation`→`CaptureLegacyDisplayObservation`、`EnsureM0ViewportSize`→`EnsureLegacyViewportSize`、`CaptureM0SettlementFingerprint`→**`CaptureLegacySettlementFingerprint`**（不是 `CaptureSettlementFingerprint`——后者是另一个已存在的符号，映射错会让断言必红）。

~~**仍然红的 4 条 = 真正的契约漂移（未修，也不该盲修）**~~

> ✅ **2026-09-15 更正：上句定性被推翻，4 条已全部修好（该脚本现 exit 0）。**
> 结论改为 **rename-only（含跨文件搬迁），业务契约本身没有变**。证据：旧实现
> （`git show 'd24f656^:Scripts/EmueraMain.cs'` L69-83、`739b428^:Scripts/GenericUtils.cs` L438）
> 与现行实现逐句同构，新代码只多不少（GPU 侧多了 `ResetColorMatrixMemoize()`，文本侧在途项由
> 单槽加强为 4 槽，环形排空多了清槽/递减/归还信封池）。**"需要领域决策、不要盲改"这个判断是错的**——
> 需要的是先导出新旧原文逐句对语义，确认"搬了"而不是"变了"，然后改指向 + 换语义锚点。
> 另：`L97–99 已被 L102/L103/L106/L107 覆盖` 也不成立——`workQueue.TryDequeue` 子串在组件里
> 还命中 CPU 兜底排空与 `_ExitTree` 排空，删掉真正的排空循环照样通过，是假覆盖。
> 完整复盘见 `governance/evolution-log/2026-09-15-contract-assertion-repointing.md`。
>
> 修法（4 条）：`L97`→`$gpuComponentSource` 的 `while (workQueue.TryDequeue(out var item))`；
> `L98`→`$textComponentSource` 的 `while (renderQueue.TryDequeue(out var item))`；
> `L99`→`$textComponentSource` 的 `slot.Item.Completed.Set();`（全脚本首次覆盖"被阻塞的等待者会被唤醒"）；
> `L208`→`while (uiQueueCount > 0)`（环形缓冲自己的非空谓词）。
> 注意这三条改指**必须先上移读文件语句**——`$gpuComponentSource` 原在 L100 赋值，在被断言的 L97 之后。

> ⚠️ **三处会误导后来者的陷阱（本轮真实踩到）**：
> 1. **符号存在性搜索必须覆盖 `.ps1`/`.psm1`，不能只搜 `.cs`。** 本轮盘点时只搜了 `Scripts/`+`src/` 的 `.cs`，于是把 4 个**其实存在**的符号
>    （`effective_backend_mismatch`、`summary.isolatedGameCopy`、`runtimeFixtureMutationChangeCounts`、`cross_aba_cycle_evidence_sample_invalid`——它们定义在 `tools/legacy-runner/Invoke-*.ps1` 里）误判为"已消失"。
> 2. **不要"顺手修正"M0 字样。** `Program.cs` 的私有字段至今仍叫 `m0RunnerStartupErrorLogPath` / `m0RunnerDefaultOutputLogPath`，
>    比较脚本里 `PreviousGate:M0` 也是**当前真实值**——把它们改成 LegacyRunner 反而会弄坏断言。
> 3. 潜在缺陷（**2026-09-15 已修**）：`Test-InProcessSessionCycle.ps1` 的 `Substring` 切片定界会算成 -1
>    （`Program.cs` 的 `ResetSessionState()` 之后没有 `/// <summary>`——全文 5 处 `/// <summary>`
>    只在 L60/254/277/310/351，全在 L415 之前），当时被 L97 掩盖。
>    ✅ **更正上文曾写的"并使 L185/L186 在空切片上真空通过"：不成立。** 实测 `IndexOf` 返回 -1 时
>    长度为负 ⇒ `Substring` **必抛**（`Length cannot be less than zero`），L185/L186 根本不会执行。
>    真实危害是**不可诊断的崩溃 + 吞掉它之后的全部诊断**（该行位置早于 GenericUtils 段，
>    L209 的修复结果永远观测不到），不是真空通过。
>    修法：先算两个 `IndexOf` → 先断言 `$programResetEnd -gt $programResetStart` → 最后才 `Substring`，
>    且第二锚点起点写 `$programResetStart + 1`（锚点缺失时 startIndex=0 合法、不再抛）。
>    同类"真空通过"还有 `Test-LegacyDisplay.ps1:66`（`CanonicalizeDisplayBackend`）与 `Test-LegacySettlement.ps1:54`（`_settleFramesRemaining`）——**这两处仍未复核**。

### 6.5 tools 工程显式源链接

三个工程不用默认 glob，显式 `<Compile Include>` 链接宿主源文件：
- `tools/diagnostics-config-smoke/DiagnosticsConfigSmoke.csproj`：`RuntimeDiagnosticsConfig.cs`、`RuntimeTomlParser.cs`、`RuntimeDiagnosticsConfigLoader.cs`、`RuntimeDiagnosticsConfigWriter.cs`
- `tools/core-contracts/CoreContractSmoke.csproj`：`Scripts/GodotHost/LegacySessionLaunchRegistry.cs`、`GameContentProbe.cs`、`LegacyThreadQuiescence.cs`、`Scripts/Emuera/Compatibility/LegacyCompatibilityProfile.cs`、`LegacyCompatibilityModules.cs`
- `tools/dialect-inventory/LegacyDialectSurfaceSmoke/LegacyDialectSurfaceSmoke.csproj`：后两个 Compatibility 文件

拆这些被链接文件时：同 PR 把新分片补进对应 csproj 的 `<Compile Include>`，并**单独构建该工具工程验证**（它们不在 `gemuera-c#.sln` 内，`--build-solutions` 根本编译不到，坏了是静默的）。反向约束：不要向被链接的"无依赖闭包"文件挪入带 Godot 依赖的成员。

### 6.6 场景与测试的脚本路径引用

部分 `.tscn` 以 `res://Scripts/...` 硬路径引用脚本且**无 uid**（路径是唯一引用）：`Scalepad.tscn`、`RuntimeDiagnosticsPanel.tscn`、`QuickButtons.tscn`、`OptionWindow.tscn`、`Inputpad.tscn`、`tools/legacy-runner/legacy_runner.tscn`；`test/GodotHost/PrototypeHostRegressionTest.gd` preload 6 个路径。**规则**：凡被 `.tscn`/`.gd` 引用的脚本主文件禁止改名/移动/把类声明迁出原文件——只允许模式 A partial 拆分（主文件保留类型声明），且拆分 PR 列出引用方清单。面板类场景（Scalepad/QuickButtons 等）可能躲过桌面启动冒烟，不能以"游戏能进"代替检查。

另：`tools/session-state-inventory` 钉扎 `GlobalStatic.cs`（`Reset` 方法必须留在原路径）与 `Program.cs`；`tools/core-contracts/Test-CoreArchitecture.ps1` 钉扎 `ErbLoader.cs`、`Process.State.cs`、`Creator.cs`、`FunctionIdentifier.cs`。

### 6.7 CodeGraph 索引

`.codegraph/` 是本机可再生缓存（非钉扎），但大拆分后旧索引会系统性误导检索。多文件移动/拆分的 PR 合入后重建索引（或注明索引需按需重建）。

## 7. 验证与合规清单（按范围分流）

拆分 PR 完成前逐项确认（通用项 + 按文件所属范围的选择项）：

通用：
- [ ] §6 四条 grep 已跑，命中的钉扎/链接机制已同 PR 更新（快照/catalog/csproj/契约脚本/场景）。
- [ ] 纯移动边界：diff 只含 §4.5 允许的机械变化；功能改动在后续 PR。
- [ ] 按 `governance/README.md` 写 evolution-log；PR 描述引用。
- [ ] 若拆分改变了 `AGENTS.md` 架构速览中的职责描述，同步更新 `AGENTS.md`。

`Scripts/**`（Godot 层）：
- [ ] `Godot_v4.7-stable_mono_win64_console.exe --headless --path <项目根> --build-solutions --quit` 后，`.godot/mono/temp/bin/Debug/gemuera-c#.dll` 时间戳已更新（构建判定口径见 `AGENTS.md`）；
- [ ] 桌面启动冒烟（游戏能进、无新报错）；涉及 UI/渲染/输入路径时说明 APK 验证状态，未验证必须写明；被 `.tscn` 引用的脚本按 §6.6 核对引用方。

`Scripts/Emuera/**`（解释器路径）追加：
- [ ] 按 `ERBAPI.md` §1/§3 核对执行路径边界（未选择侧不变）；
- [ ] 相应家族有 xUnit/core-contracts 测试时运行之；受影响的契约门禁（§6.1–6.4）按"存量红纪律"重跑。

`src/Core/**`：
- [ ] Godot headless 构建 + `dotnet test tests/GEmuera.Core.Tests`（xUnit）通过；
- [ ] `tools/core-contracts` 自检脚本可运行（拆分被其链接的文件时必跑）。

`tests/**`：
- [ ] 构建通过 + 拆分后对应测试套件全部运行（不允许"拆了测试但没跑测试"）。

`tools/**`：
- [ ] 该工具自带的 `Test-*.ps1` 重跑通过。

## 8. 反模式（禁止）

- 按行数机械切片（"每 1000 行切一段"式的无语义分片）；
- 拆分与逻辑修改混在同一提交；
- 一次性大爆炸重构全部存量文件（违背"不为重构而重构"）；
- 新建 `Misc`/`Utils`/`Other` 式垃圾抽屉分片；
- 拆了文件但跨域调用反而增多（分片应沿依赖方向切，不是把纠缠切成两半）；
- 跳过 §6 检查直接拆分（六类钉扎机制任何一类都可能被命中）。

## 附录 A：>2000 行文件快照（2026-09-15，字节 LF 口径）

> 本清单是**行数 >2000 的"在册监控项"**，不是"尚待清理的欠债"——判定归属见 §2（既有 >2000 行文件"只减不增"，
> 触碰时按功能域顺手拆分，不单独开重构 PR）。其中 `VariableEvaluator.cs` 已确认**有结构地板、降不到 2000 以下**（见下）。

| 文件 | 行数 |
| --- | --- |
| `Scripts/EmueraContent.cs` | 8105 |
| `Scripts/Emuera/GameData/Function/Creator.Method.cs` | 6282 |
| `Scripts/Emuera/GameData/Variable/VariableEvaluator.cs` | 3796 |
| `Scripts/Emuera/GameData/Variable/VariableToken.cs` | 3524 |

> `VariableEvaluator.cs` 属 §6 钉扎最重的一类：`tools/save-baseline/legacy-save-baseline.json`
> 对它有 SHA-256 + 字节长度钉扎，且 8 个受钉扎的方法符号全部落在 1129 行的 `#region File操作` 内
> （该区域被钉死不可移动），结构地板约 2057 行 ⇒ **它不可能降到 2000 以下，不要把它当"待清理项"**。

分布：Scripts+src 共 276 个 .cs；>1000 行 33 个；>2000 行 4 个；>4000 行 2 个。

> 上一版快照（2026-08-22）为：253 个 .cs、>1000 行 31 个、>2000 行 **12** 个、>4000 行 4 个。
> 2026-09-13 三批纯移动拆分（提交 `8eef88a`、`e8ad27c` 及第三批）后，
> `Instraction.Child.cs`、`EmueraConsole.cs`、`ArgumentBuilder.cs`、`ConstantData.cs`、
> `RuntimeDiagnosticsPanel.cs`、`HtmlManager.cs`、`FirstWindow.cs`、`GenericUtils.cs`
> **共 8 个文件已脱离本清单**（`EmueraContent.cs` 与 `Creator.Method.cs` 已大幅减量但仍在册）。
> 再生成命令见 §2.1；**切勿用 `Get-Content` 复核行数或行号**（本仓实测会少算并整体错位）。

## 附录 B：现有分片家族（先例参考）

| 家族 | 分片 |
| --- | --- |
| `EmueraContent` | 主文件 + `Canvas` + `AndroidSpriteAnime` + `LegacyRunner` |
| `Creator.Method` | 主文件 + `DT` + `Map` + `Sql` + `Xml` |
| `Process` | 主文件 + `LazyLoading`/`ScriptProc`/`SystemProc` 等 3 分片（`Process.CalledFunction.cs`/`Process.State.cs` 是独立类型，非分片） |
| `EmueraConsole` | 主文件 + `Print` + `uEmuera/partial/` 下 1 片 |
| 其他 | `VariableData`、`FunctionIdentifier`、`ArgumentParser` 各 2 文件 |
