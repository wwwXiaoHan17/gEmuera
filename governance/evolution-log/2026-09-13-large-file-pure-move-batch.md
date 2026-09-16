# 大文件纯移动拆分批次（large-file-pure-move-batch）

## 任务目标与成果

对 12 个 >2000 行的 C# 文件做**纯重构瘦身**：按 `FILE_STANDARD.md` §3 模式 A 机械迁出功能域到 partial 分片，**零逻辑改动**，并同 PR 维护 §6 钉扎机制。

本批次实际落 5 个文件、6 个新分片，全部通过"行多重集零丢失 + 编译通过"双重验证：

| 主文件 | 前 | 后 | 新分片 | 迁出行数 |
| --- | --- | --- | --- | --- |
| `Scripts/EmueraContent.cs` | — | — | （未纳入本批，见"遗留"） | — |
| `Scripts/Diagnostics/RuntimeDiagnosticsPanel.cs` | 2138 | **882** | `RuntimeDiagnosticsPanel.Options.cs`（224–1479 连续块） | 1256 |
| `Scripts/Emuera/GameData/ConstantData.cs` | 2633 | **1750** | `ConstantData.CsvFieldParsing.cs` + `ConstantData.KeywordLookup.cs` | 883 |
| `Scripts/Emuera/GameData/Variable/VariableToken.cs` | 4218 | **3524** | `VariableData.ReferenceTokens.cs`（12 个嵌套类） | 694 |
| `Scripts/FirstWindow.cs` | 2050 | **1653** | `FirstWindow.DiagnosticsSettings.cs`（577–973 连续块） | 397 |
| `Scripts/Emuera/GameView/HtmlManager.cs` | 2042 | **1826** | `HtmlManager.DisplayHtml.cs`（HM-B） | 216 |

**4 个文件（ConstantData / RuntimeDiagnosticsPanel / FirstWindow / HtmlManager）脱离 >2000 在册清单。**

## 关键决策与 Why

1. **范围只取"零钉扎冲突"的文件，不碰重钉扎对象。**
   `VariableEvaluator.cs`（3796）与 `EmueraContent.cs`（9116）/`Creator.Method.cs`（8307）本批**主动排除**：
   - `VariableEvaluator.cs` 的 `#region File操作`(2482–3610) 被 `save-baseline` 用 8 个方法符号正则硬钉（`SaveToStreamBinary`/`LoadFromStreamBinary`/`WriteGlobalSaveStream`/`LoadGlobal`/`SaveVariable`/`LoadVariable`/`SaveChara`/`LoadChara`），
     且该文件理论下限 ≈2057 行，**结构上不可能降到 2000 以下**——拆它是"减量"而非"脱册"，性价比低且风险高。
   - 更关键：`save-baseline` 与 `dialect-profile-selection` 的哈希在**干净基线就已是红的**（详见 §3）。在红基线上去重算哈希，等于把与本次无关的历史漂移**洗进基线**，违反"每个 PR 只解决一个明确问题"。
2. **`save-baseline` 的既有漂移不修、`dialect-profile-selection` 只重钉自己动过的文件。**
   实测：`legacy-save-baseline.json` 5 个源文件 **4/5 DRIFT**（仅 `VariableData.cs` 仍 MATCH）；`dialect-profile-selection.json` 3 个源文件 **3/3 DRIFT**。
   本批次只重钉 `FirstWindow.cs` 自己的 sha256（因为我确实改了它，§6.1 要求同 PR 重钉），
   `Program.cs` / `LegacyRunnerConfig.cs` / `EraBinaryDataReader.cs` 等**原地不动**，留给独立的"重钉扎"PR。
3. **"连续块整段剪切"优先于"按成员清单逐条搬"。**
   `RuntimeDiagnosticsPanel` 的 224–1479（1256 行）与 `FirstWindow` 的 577–973（397 行）都是**无缝连续区间**，
   整段剪切把改动面从"数百处零散编辑"压到"一次剪切 + 补 using"，出错面大幅下降。
   报告建议把散落字段一并搬走时，**一律拒绝**：同 partial 类内分片可直接访问共享字段，§4.3 本就要求它们留主文件。
4. **新分片同 PR 提交 `.uid` 侧车。**
   仓库 `Scripts/**` 下有 209 个入库 `.uid`（格式 `uid://` + 12–13 个 `0123456789abcdefghijklmnopqrstuvwxy` 字符），
   含全部既有分片。这是 §6 六类钉扎**之外**的第 7 类仓库约定，拆分时必须遵守（已按此生成 6 个 uid）。

## 环境障碍与打通（**下次会话必读**）

`AGENTS.md` 记载的构建命令**在本机不可用**：

```
Godot_v4.7-stable_mono_win64_console.exe --headless --path <root> --build-solutions --quit
```

失败原因：Godot 安装目录下**没有任何 MSBuild 程序集**（只有 `GodotSharp\Tools\Microsoft.Build.Locator.dll`），
GodotTools 抛 `FileNotFoundException: Microsoft.Build.Framework, Version=15.1.0.0` 并 abort。

**可用替代命令**：

```powershell
dotnet build 'D:\gemuera\gemuera-c#.csproj' -v minimal -nodeReuse:false -m:1
```

- 成功判定：输出含「已成功生成」**且** `.godot/mono/temp/bin/Debug/gemuera-c#.dll` 时间戳更新。
- **`-nodeReuse:false -m:1` 是必需品**：复用陈旧 MSBuild 工作节点时会"静默失败"——报「生成失败」却显示「0 个错误」且不产出 DLL。
  遇到不明失败先 `dotnet build-server shutdown`。
- ⚠️ 不要用 `--no-incremental`：它会先清空输出，一旦后续失败就**连 DLL 一起丢掉**（本会话踩过，已重建）。

另有一个**必须先处理才能构建**的障碍：`src/Core/obj/**` 与 `src/Core/bin/**` 下有 10 个文件属于
**外来沙箱身份 `DESKTOP-MG17PVH\CodexSandboxOffline`**（另一个 Agent harness 的历史会话残留）。
本会话的受限令牌（`Administrators` 为 deny-only）无法覆盖/删除它们，构建报
`MSB3491: ... Access to the path ... is denied`。

解法（未改任何源码）：`obj`/`bin` **目录自身**带能力 ACE 允许改名，故把两目录改名后移到 csproj 显式排除的
`artifacts/codex-stale-artifacts/`。
**注意不要放进 `Build/`**——`gemuera-c#.csproj` 的默认 glob 不排除 `Build/**`，放那里会把残留的
`AssemblyInfo.cs` 全局引入，产生 35 个 CS0579 重复特性错误。

## 验证方法（两个工具，见 `reports/`）

> ⚠️ **路径说明**：`reports/` 已被 `.gitignore` 排除，故下面两个校验器**不在本 PR 的提交内容里**，
> 它们只存在于本机工作副本。本条同时是"应把它们提升为 `tools/` 常驻工具"的建议依据（见"教训 → 具体优化动作"第 4 条）。
> 本 PR 的可见证据是 PR 描述与本文档中记录的**实测数值**（行多重集差异、`git diff --numstat`、门禁 32/32）。

1. **纯移动校验器** `reports/Test-PureMove.ps1`
   取同一「文件家族」（主文件 + 分片）在 git 基线与工作区的全部代码行，做归一化（去空白/空行/`using`/`#region`/注释）后的
   **行多重集**比对。纯移动只是把行在文件间搬运，两侧应逐行相等；剩余差异只允许来自 §4.5 许可的机械变化。
   - 自校验：未改动的 `EmueraContent` 家族两侧均为 9416 行、差异 0。
   - 本批次实测：5 个家族**"仅基线有"全部为 0 行**（= 没有任何行被丢失或改写），
     "仅工作区有"合计只有 35 行，**全部**是 `namespace` 声明、`partial` 类型声明与包裹大括号。
   - **踩坑**：家族发现是按主文件名通配的，而 `VariableToken.cs` 里其实装的是 `VariableData` 的实现、
     分片按事实命名为 `VariableData.ReferenceTokens.cs`，通配不到 → 首轮误报"丢失 635 行"。
     为此加了 `-Include` 参数显式追加家族成员。
2. **snake-alignment 替身 harness** `reports/Test-SnakeBehaviorBaseline.ps1`
   真门禁 `Test-SnakeReferenceSurface.ps1` 需要 `-ReferenceRoot` 指向 `E:\MyCode\Era\emuera_lazyloading_*`，
   **该检出在本机不存在**，脚本会先 throw 再谈断言。但其 `behaviorChecks` 半部分是自包含的（断言某正则仍匹配某**文件路径**）——
   而这正是纯移动最容易破坏的面。故该 harness **从真门禁脚本里解析出权威的 `$behaviorChecks` 字面量**（不复制，避免漂移）后逐条求值。
   - 带两条防呆：解析结果为空 → throw；任一 `Path` 为空 → throw。
   - **防呆不是装饰**：首版 harness 忘了定义 `$currentInstructionPath` 等上游变量，条目 `Path` 为空后几乎"全过"。
   - 实测：拆分前后均 **32/32 通过**。

## AI 表现复盘

- **有效**：
  - **"正则命中位置 → 成员归属"的逐条比对**是本次最有价值的侦察动作。它把"这个文件被钉扎了"推进到
    "这条断言钉在哪个成员的第几行、这个成员会不会被搬走"，从而**证明**了本批 5 个文件的迁出集合与全部钉扎断言**零重叠**——
    这是"不需要改任何门禁脚本"的硬依据，而不是"看起来不冲突"。
  - **字节级重构证明**（`FirstWindow`）：用 HEAD 1–576 + 分片 + HEAD 974–2050 拼回，
    SHA-256 与原文件**完全相同**。比"行多重集比对"更强，值得作为连续块拆分的标准举证方式。
  - **先建工具再动代码**：纯移动校验器与 snake 替身 harness 都先在未改动状态自校验，避免"用没校准的尺子量结果"。
- **低效**：
  - **把构建通路排在动代码之后**。实际是先撞上 `--build-solutions` 不可用 + 外来沙箱产物 MSB3491 + 陈旧 MSBuild 节点三重障碍，
    连"改动前是不是绿的"都无法判定。应当在任何重构任务开头就先用最朴素的改动验证一次构建闭环。
  - **`FILE_STANDARD.md` §6.1 的"拆 EmueraContent.cs/EmueraConsole.cs 几乎必然要同 PR 扩展 fileRegex"是错的**。
    实测 `DialectInventory.psm1:5-16` 的 10 条 marker 是**标识符正则**，只有文件里**真的出现**该标识符才要求 `fileRegex`；
    两个文件**零 marker 命中**，其分类规则是"声明了但当前未命中"的惰性条目。照文档判断会白白劝退一个低风险目标。
  - 两个前台 workflow 连续被 round 边界信号中止（`workflow signal aborted`），期间无任何产出；
    改用后台 subagent 后顺利完成。**长前台编排在本 harness 下不可靠。**

## 教训 → 具体优化动作

1. **构建闭环前置**：任何涉及多文件的改造任务，第一步先跑一次可用的构建命令并记录基线红绿，
   再开始改代码。（对应动作：`AGENTS.md` 的构建章节应补本机可用命令，见第 4 条）
2. **钉扎判断不许转述**：`FILE_STANDARD.md` §6.1 关于 marker 的判断需要按实测修正
   （marker 是**标识符正则**，不是文件分类；规则惰性存在不等于会被触发）。
3. **纯移动必须有机器证据**：`reports/Test-PureMove.ps1` 的行多重集比对应作为拆分 PR 的标配举证；
   连续块拆分优先补一条"整文件字节重构等价"证明。
4. **建议后续（超出本次范围）**：
   - 把"本机可用构建命令 + 外来沙箱产物障碍"写进 `AGENTS.md` 构建章节；
   - 把 §2.1 的行数口径警告扩展为"**行号索引**也要用 `ReadAllLines`"——
     实测 `Get-Content` 在本仓不只是**少算**，还会**整体错位行号**，导致按行号区间统计的脚本静默算错区间
     （某侦察轮首版把 `AddCheck 139` 算成 140、`BeginTab 8` 算成 4）；
   - 把 `reports/` 下两个校验器提升为 `tools/` 下的常驻工具（`FILE_STANDARD` §6 末段本就建议过"把四条检查工具化"）；
   - 独立的"重钉扎 PR"：`save-baseline`（4/5 DRIFT）与 `dialect-profile-selection`（2/3 DRIFT）应予清理，
     否则每次触碰这些文件都要面对"要不要顺手洗白历史漂移"的两难。

## 遗留（未纳入本批）

- `Scripts/EmueraContent.cs`（9116）、`Scripts/Emuera/GameData/Function/Creator.Method.cs`（8307）：
  本批未处理（侦察报告见 `reports/domain-map/`）。两者均有分片先例（`EmueraContent.Canvas/AndroidSpriteAnime/LegacyRunner`、
  `Creator.Method.DT/Map/Sql/Xml`），且经实测**不在方言注册快照哈希集内**（`Creator.Method*.cs` 为 §6.1 明示特例；
  `EmueraContent.cs` 零 marker 命中），但这属于另一个量级的改动，应独立成 PR。
- `Scripts/Emuera/GameData/Variable/VariableEvaluator.cs`（3796）：因 §3.1 所述双重原因（硬钉 + 结构下限 2057）本批排除。
- `Scripts/Emuera/GameData/Variable/VariableToken.cs` 后续分片：本次只搬了 VD.ref 一个域，
  报告实测还有 6 个 ≥200 行的 nested 类族可继续切（全搬完主文件可降到 ≈1102 行）。
- `Scripts/Emuera/GameData/ConstantData.cs` 后续：报告另有 CD-B/CD-D/CD-G 等候选域。
