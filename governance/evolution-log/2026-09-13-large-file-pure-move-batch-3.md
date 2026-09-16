# 大文件纯移动拆分 · 第三批（large-file-pure-move-batch-3）

> 承接 `2026-09-13-large-file-pure-move-batch.md`（第一批 5 文件，`8eef88a`）与
> `2026-09-13-large-file-pure-move-batch-2.md`（第二批 3 文件，`e8ad27c`）。
> 本批基线 = `e8ad27c`，分支 `ai/large-file-subdivision-batch`。

## 任务目标与成果

对上一轮**未能处理**的 3 个文件做纯移动拆分（另 3 个已在第二批完成）。本批落 **3 个文件、9 个新分片**，三项验证全绿。

| 主文件 | 前 → 后 | 新分片 | 迁出行数 |
| --- | --- | --- | --- |
| `Scripts/Emuera/GameData/Function/Creator.Method.cs` | 8307 → **6282** | `Creator.Method.{Array,Math,Csv}.cs`（1155/561/309） | 2025 |
| `Scripts/GenericUtils.cs` | 2926 → **1094** | `GenericUtils.{Audio,Trace,DiagnosticsLogging}.cs`（629/471/732） | 1832 |
| `Scripts/EmueraContent.cs` | 9116 → **8105** | `EmueraContent.{Audio,HtmlGeometry,UiDiagnostic}.cs`（569/216/226） | 1011 |

**>2000 行在册清单：5 → 4**（三批累计 **8 个文件脱离**）。剩余 4 个：
`EmueraContent.cs` 8105、`Creator.Method.cs` 6282、`VariableEvaluator.cs` 3796、`VariableToken.cs` 3524。

**本批无任何门禁脚本改动**——9 个分片全部落在 snake-alignment 的 32 条断言之外，`tools/snake-alignment/Test-SnakeReferenceSurface.ps1` 零改动，改后门禁仍 32/32。

## 关键决策与 Why

1. **`GenericUtils.cs` 的 13 个区间高度交错，因此强制"自下而上切"。**
   该文件的可切区间是 94–99 / 105–108 / 112–120 / 125–126 / 310–314 / 316 / 318 / 329–358 / 516–528 / 536–541 / 642–1355 / 1357–1434 / 1453–1835 / 2327–2906 —— 前 4 个挤在 40 行内互相穿插。
   若从上往下切，每切一刀都会让下方所有行号失效。**从最大行号往下切**则未处理部分的较小行号始终有效，不必反复重定位。这条写进了派发 prompt，实测 13/13 区间与报告行号**零漂移**。
2. **`Creator.Method.cs` 只取报告的"保守第一梯队"（R1+R4+R5），不追一轮达标。**
   报告算出：一轮脱离 2000 需搬 ≥6307 行（R8+R5+R6 = 6641 行），代价是 **14 个新文件 + 改 5 条 snake Path**。
   而保守梯队在**零脚本改动、零 pin 冲突、区间 100% 纯净**的前提下搬 2025 行 → 6282。按 §8「禁止一次性大爆炸」，选后者，R6+R8 留给下一轮。
3. **模式选择是被实测推翻前提后重新定的**：派发时我按惯例问了"独立顶层类 → 模式 B"，侦察实测**206 个类型声明全部嵌在 `FunctionMethodCreator` 内（花括号深度恒为 2），顶层独立类型 0 个** → 模式 B 不可用（且提为顶层要放宽 129 个 `private` + 改 `Creator.cs` 注册，属逻辑变更）。最终走模式 A，与既有 DT/Map/Sql/Xml 四片同构。
4. **`EmueraContent.cs` 只搬"极低风险"三域。** 该文件侦察结论明确：**一轮及后续两轮都不可能脱离 2000**（不可迁核心 ≈1616 行），正确目标是"只减不增 + 每轮 -30%"。本批取 D8/D7/D1（refsOut 分别 5/8/1），并刻意避开被两条 snake 断言钉住的 `AddPartToContainer`、`ConsoleTextPart`、`AddBorderRect`。

## 验证（提交前完成，基线 = `e8ad27c`）

| 项 | 结果 |
| --- | --- |
| 行多重集（`Test-PureMove.ps1`） | 3 个家族「仅基线有」= **0 / 1 / 0 行**；`GenericUtils` 那 1 行是声明行加 `partial`（模式 A 固有前提，已登记） |
| 新增行 | 全部是 `namespace`/类型声明/包裹大括号等 §4.5 机械差异（9 / 18 / 10 行） |
| 编译 | `dotnet build -nodeReuse:false -m:1` **0 错误**，DLL 16:55:26 晚于最新源文件 16:54:47 |
| snake behaviorChecks | **32/32**，与拆分前基线一致 |
| uid 侧车 | 9 个全新建，全仓 **231 个 `.uid`、0 重复** |

**最强单项证据（`Creator.Method.cs`）**：把「当前主文件 + 3 个分片载荷」按原顺序重组成 8307 行文件，`git hash-object` = `c8ef28cf6aef67dd1f405373cb9c72a315447b33`，与 `HEAD` blob **完全相同**。这字节级地同时证明了"无丢失、无新增、**无重排**、无空白/缩进改动"——比行多重集更强，因为它对顺序也成立。

## 我纠正的一个误报（值得记）

`GenericUtils` 的执行 agent 主动上报"**D2 片内部有 1 处成员换序**：`ClipTrace` 现在排在 `ScrollTrace` 之后"。
我实测复核后**判定该上报不成立**：HEAD 中顺序为 `ScrollTrace`(L1357, L1368) → `ClipTrace`(L1430) → `ClipFlatText`(L1436)，分片中为 `ScrollTrace`(L30, L41) → `ClipTrace`(L103)，**相对顺序完全一致**。
该 agent 是在描述它自己的分析时把两个区间的拼接顺序讲反了。

不过这个上报指向了一个**真实的方法论缺口**：行多重集比对**对顺序不敏感**，而"纯移动"理论上应保序。
- 对**方法/属性**，C# 内顺序无语义影响；
- 但 **`static readonly` 字段初始化器是按文本顺序执行的**，字段换序是真实的行为风险。

后续加固建议：给 `Test-PureMove.ps1` 增加"保序子序列"检查——对每个分片，验证其成员声明序列是原文件成员声明序列的**子序列**（不要求连续，只要求相对顺序不翻转）。这样能在保持"允许区间交错"的同时捕获字段换序。

## AI 表现复盘

- **有效**：
  - **"禁用 workflow/subagent"写进 prompt 后，三个 agent 全部一次成功**。上一轮同类的三个 agent 全部因试图自行派发验证流程而空转/中止（其中两个从未产出报告）。这条是上一批复盘结论的直接验证，值得固化为派发模板。
  - **"自下而上切 + 每刀前重新定位"** 让 13 个交错区间零漂移完成——把"最容易出错的多区间改行号"变成了机械动作。
  - **`git hash-object` 重建比对**是目前最强、也最省事的纯移动举证：一条命令就能同时覆盖内容、顺序与空白。
- **低效**：
  - **我把"是否模式 B"当成了常规问句，而该文件根本不存在独立顶层类**。侦察多花了一轮才发现前提是假的。教训：派发前应先花一条命令量一下"顶层类型有几个、花括号深度几何"，而不是让 agent 去验证一个可能不成立的前提。
  - **执行 agent 会主动"上报自己也不确定的偏差"**（如上面那处误报的换序）。这本身是好行为（比隐瞒强），但它说明：**agent 的自述结论必须由主会话用可复现命令复核**，不能直接写进交付物。

## 教训 → 具体优化动作

1. **派发子代理的 prompt 固定包含两条**：① 禁止调用 `workflow`/`subagent`（本环境实测不可用，会空转）；② "做一个、写一个、报一个"，避免长链条分析后被切断而零产出。
2. **多区间的行号操作一律自下而上**，并在每刀前重新定位。
3. **纯移动的最强举证用 `git hash-object` 重建比对**（内容 + 顺序 + 空白一次性覆盖），行多重集退居为快速筛查。
4. **`Test-PureMove.ps1` 应补"保序子序列"检查**（见上文缺口分析），否则字段换序类缺陷会漏网。
5. **派发前先量前提**：涉及"模式 A 还是 B"的判断，先跑一条命令数顶层类型与花括号深度，不要让 agent 验证可能有假的前提。

## 遗留（下一轮）

| 文件 | 行数 | 下一步 |
| --- | --- | --- |
| `Scripts/Emuera/GameData/Function/Creator.Method.cs` | 6282 | 报告 `12-CreatorMethod.md` 已给完整方案：**R6 文字列(795) + R8 画像処理(4691，须再切 12 片)** = 6641 行 → 主文件 **1666** 可脱离清单；代价 = 改 5 条 snake Path + 14 个新文件。R8 拆前须补"逐类跨子域引用穷举"（报告 §5 自述未做）。 |
| `Scripts/EmueraContent.cs` | 8105 | 报告 `11-EmueraContent.md` 已给 3 个 PR 的分批方案（B: Cbg+VirtualPointer+QuickButtons；C: SpriteTextures+Scroll）。累计再搬约 2341 行 → 仍有 ≈5764 行，**不可能一轮脱离**。 |
| `Scripts/Emuera/GameData/Variable/VariableToken.cs` | 3524 | 报告 `03-VariableFamily.md` 实测还有 6 个 ≥200 行的类族可切，全搬完 ≈1102。 |
| `Scripts/Emuera/GameData/Variable/VariableEvaluator.cs` | 3796 | 主动排除：8 个方法符号硬钉 + 结构下限 ≈2057，**不可达 2000**。 |
| 独立"重钉扎 PR" | — | `save-baseline` 4/5 漂移、`dialect-profile-selection` 2/3 漂移（既有红，三批均未触碰）。 |

> 未做（三批一贯）：**桌面启动冒烟与 APK 实测**均未执行；push/PR 未做（本机离线，`E:\` 参考检出与 NuGet 均不可达）。
