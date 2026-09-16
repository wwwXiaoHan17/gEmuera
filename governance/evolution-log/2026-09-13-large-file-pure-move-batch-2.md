# 大文件纯移动拆分 · 第二批（large-file-pure-move-batch-2）

> 承接 `2026-09-13-large-file-pure-move-batch.md`（第一批 5 文件，提交 `8eef88a`）。
> 本批基线 = `8eef88a`，分支 `ai/large-file-subdivision-batch`。

## 任务目标与成果

继续对 >2000 行的 C# 文件做纯移动拆分。本批落 **3 个文件、7 个新分片**，全部通过"行多重集零丢失 + 编译 0 错误 + snake 门禁 32/32"三重验证。

| 主文件 | 前 → 后 | 新分片 | 迁出行数 |
| --- | --- | --- | --- |
| `Scripts/Emuera/GameProc/Function/Instraction.Child.cs` | 4051 → **1856** | `FunctionIdentifier.FlowControlInstruction.cs`(924)、`.InputInstruction.cs`(435)、`.PrintInstruction.cs`(483)、`.DataInstruction.cs`(353) | 2195 |
| `Scripts/Emuera/GameView/EmueraConsole.cs` | 2893 → **1947** | `EmueraConsole.Cbg.cs`(511)、`EmueraConsole.Input.cs`(435) | 946 |
| `Scripts/Emuera/GameProc/Function/ArgumentBuilder.cs` | 2371 → **1192** | `ArgumentParser.TypeChecked.cs`(1179，32 个类) | 1179 |

**>2000 行在册清单：12 → 5**（两批累计 **7** 个文件脱离：第一批 4 个 + 本批 3 个；
`VariableToken.cs` 由 4218 降到 3524 属"减量"但**仍在册**，不计入脱离）。
剩余 5 个：`EmueraContent.cs` 9116、`Creator.Method.cs` 8307、`VariableEvaluator.cs` 3796、`VariableToken.cs` 3524、`GenericUtils.cs` 2926。

## ⚠️ 必须知道：`git diff --numstat` 会误报"有插入"

本批 `Instraction.Child.cs` 的真实改动是**纯删除 2195 行、零插入**，但：

```
git diff --numstat                    →  45   2240   ← 假象
git diff --minimal --numstat          →   0   2195   ← 真实
git diff --diff-algorithm=histogram   →   0   2195
```

这是 git 默认 **indent-heuristic** 在"大段花括号/高度相似的指令类"上的对齐伪影（三个文件里只有最像的那一个触发）。
**审查任何纯移动 PR 一律用 `git diff --minimal --numstat`（或 `--histogram`）**，否则会把纯删除误判成"改了 45 行"。
更硬的证据形式：把 HEAD blob 按 LF 切行、精确删除目标区间、重新拼接，比对 SHA-256 与工作区文件完全相同——
本批三个文件都做了这一验证（`Instraction.Child.cs` 得 `c1ba2e6e…`、`EmueraConsole.cs` 整文件重建逐字节相等、`ArgumentBuilder.cs` 逐块 mismatches=0）。

## 关键决策与 Why

1. **只挑 `refsOutOfDomain` 极小的连续区间，且优先"零钉扎成本"的切片。**
   - `Instraction.Child.cs` 的 D1（3126–4049，924 行）与文件自带 `#region flowControlFunction` **完全重合**，反向扫描 101 个类名入边 = 0、共享 helper 命中 = 0 → 成本为 0，是全仓最大的单块零成本切片。
   - `ArgumentBuilder.cs` 搬 `#region 正規型`（1171–2349，1179 行 / 32 类）只付 1 条钉扎；另一条断言 `SP_SETIMAGELAYERL` 因为注册块 L236 留在主文件而**持续命中、不失配**——即"钉扎点仍在主文件"可以让断言免于改动。
   - `EmueraConsole.cs` 的 D1 CBG 出向依赖只有 **1 个符号**（`window?.Refresh()`）。
2. **当"零钉扎成本"与"脱离在册清单"冲突时，选择改门禁脚本 Path（§6.2 明确允许），而不是放弃达标。**
   `EmueraConsole` 的算术是硬事实：D1+D3 = 870 行 → 2023，**恰差 29 行**；其余 ≥200 行的合格域只剩 D2（含 2 条断言）→ 想脱离清单就必须动 D2。
   本批共改 `tools/snake-alignment/Test-SnakeReferenceSurface.ps1` **3 行**（L84/L85/L90），**只改 `Path`，Pattern 与 Label 逐字节未动**，改后门禁 32/32 仍全绿。这是 §6.2 允许且代价最小的做法。
3. **分片名跟随"真实承载的类"，而不是宿主文件名。**
   `Instraction.Child.cs` 装的是 `partial class FunctionIdentifier`，故分片命名 `FunctionIdentifier.*.cs`；`ArgumentBuilder.cs` 94% 内容其实是 `partial class ArgumentParser`，故分片命名 `ArgumentParser.TypeChecked.cs`。这与第一批 `VariableToken.cs` 实为 `VariableData` 实现的情况同源——**本仓存在多个"文件名与内容不符"的巨型文件，拆分时以内容为准**。
4. **不接受"为达标而硬剪"。** 报告给出的所有候选都要求是**连续区间**；`Instraction.Child.cs` 的 D8 若强行把 helper 留主文件会退化为两段非连续切、只能搬 342 行（目标 1856 变为 1867），故按"整段剪切 + helper 随其唯一调用方同走"执行。同理，本次拒绝把 `EmueraContent.cs` 拉进来（见"遗留"）。
5. **`toUInt32inArg` 的归属**：侦察报告内部自相矛盾（D3 行说它跨域共享要留主文件，调用点表却标其归属为 D8）。实测它**只有 2 个调用点、且都是 D8 成员**，故随 D8 整段搬走；主文件对它的引用已为 0。这是"以实测引用计数裁定文档矛盾"的一例。

## 验证方法（与第一批相同，另加两条）

沿用 `reports/Test-PureMove.ps1`（行多重集比对）与 `reports/Test-SnakeBehaviorBaseline.ps1`（从真门禁脚本解析权威 `$behaviorChecks` 后逐条求值）。
本批新增两条经验：

1. **家族发现有名字陷阱，已加 `-Include`**。`Test-PureMove.ps1` 按主文件名通配聚合同家族文件，而本批两个分片名（`ArgumentParser.TypeChecked.cs`、`FunctionIdentifier.*.cs`）都**不匹配**宿主文件名（`ArgumentBuilder.cs`、`Instraction.Child.cs`）→ 首轮会误报"丢失数百行"。必须显式 `-Include` 追加分片。
2. **基线防呆已写入 `reports/Verify-SplitBatch.ps1`**：它默认从 `reports/.base-commit` 读基线，并且**在 `BaseRef` 解析为 HEAD 时直接 `exit 2` 拒绝运行**——因为在提交之后再跑，比对会退化成"拿拆分后的自己比自己"，把所有家族都误报成 PURE MOVE。第一批末尾真实踩过这个坑（提交后复核，5 个家族全报 PURE MOVE）。

## AI 表现复盘

- **有效**：
  - **"钉扎点是否仍在主文件"作为免改门禁的判据**。`ArgumentBuilder.cs` 的 `SP_SETIMAGELAYERL` 断言命中 3 处，其中 1 处在**注册块**（必须留原文件）→ 该断言天然持续命中，于是搬走另外 2 处所在类也无需改脚本。这个推理把"要不要改门禁"从感觉变成了可算的。
  - **`git diff --minimal` 交叉验证**。默认 `numstat` 报 `45 2240`，若无这一步，PR 会带着"改了 45 行"的假象被审查；agent 主动上报了该伪影并给出字节级替代证据。
- **低效（本次最严重）**：
  - **多个子代理试图自行派发 `workflow` 后陷入空转**。一个执行 agent 空转约 25 分钟零产出（其收尾消息显示它在"重试窄波次、回放缓存"），两个侦察 agent 也因"发起对抗验证工作流"而中止并**从未写出报告**（`08-GenericUtils.md`、`12-Creator.Method.md` 始终不存在）。**本环境的 workflow/子代理通道对子代理不可用（实测 `agent()` 调用 6/6 返回 null）**，而子代理的路由表又鼓励它们"对抗性验证"——这是直接冲突。
    修正动作：重新派发时在 prompt **开头显式禁止调用 `workflow`/`subagent`**，并要求"自己用 read/grep/PowerShell 自查"；同时把任务拆成"**一次只做一个区间、做完立刻落盘**"，即使中途被切断也有产物。改后一次成功。
  - 第一批的 `EmueraContent.cs` 侦察虽然质量很高，但给出了"本轮可搬 8 域 ≈3352 行"的方案，而该文件**本轮及后续两轮都不可能脱离 2000**——这类"看起来很值钱但目标不可达"的候选应当更早被排除，避免占用预算。

## 教训 → 具体优化动作

1. **派发子代理时必须写明环境禁令**：子代理不得调用 `workflow`/`subagent`（本环境不可用且会使其空转），需要验证就自查。
2. **大任务拆成"可中断的增量"**：同一文件的多区间拆分，要求"做一个、写一个、报一个"，而不是"全部分析完再一起写"。
3. **纯移动 PR 的审查口径固定为 `git diff --minimal --numstat`**（见上文 ⚠️ 节），并把"HEAD blob 删区间后重拼 SHA-256 相等"作为最强举证。
4. **`Test-PureMove.ps1` 的 `-Include` 与 `Verify-SplitBatch.ps1` 的基线防呆都应长期保留**；建议随"把校验器提升到 `tools/`"一并落地。
5. **建议后续（超出本批范围）**：
   - `FILE_STANDARD.md` §5 表与附录 A 的行数快照已明显过时（本批把 `Instraction.Child.cs` 从 4046/4051 降到 1856 等），应按 §2.1 命令再生成后任务性更新；
   - `FILE_STANDARD.md` §5 表里 `Instraction.Child.cs` 记 4046 而实测 4051，**该文件在拆分前已违反 §2"只减不增"**——快照纪律需要一次性对账。

## 遗留（未纳入两批）

| 文件 | 行数 | 状态 |
| --- | --- | --- |
| `Scripts/EmueraContent.cs` | 9116 | 侦察报告 `reports/domain-map/11-EmueraContent.md` 完整。**明确结论：本轮及后续两轮都不可能脱离 2000**（不可迁核心 ≈1616 行；即便再搬 3 轮主文件仍 ≈3500）。正确目标是"只减不增 + 每轮 -30%"，建议分 3 个纯移动 PR（A: UiDiagnostic+HtmlGeometry+Audio；B: Cbg+VirtualPointer+QuickButtons；C: SpriteTextures+Scroll）。 |
| `Scripts/Emuera/GameData/Function/Creator.Method.cs` | 8307 | **侦察未完成**（子代理空转被中止，报告 `12-CreatorMethod.md` 不存在），需重新测绘。注：`Creator.Method*.cs` 不在方言注册快照哈希集内（§6.1 明示特例），已有 DT/Map/Sql/Xml 4 片先例可沿用。 |
| `Scripts/GenericUtils.cs` | 2926 | **侦察未完成**（报告 `08-GenericUtils.md` 不存在），需重新测绘。其 legacy-runner 路径钉扎为**名义钉扎、实际不可执行**（`tools/legacy-runner/` 全部测试因引用不存在的 `Scripts\M0\*` 而立即失败——本会话实测复现）。 |
| `Scripts/Emuera/GameData/Variable/VariableEvaluator.cs` | 3796 | 主动排除：`#region File操作`(2482–3610) 被 save-baseline 用 8 个方法符号硬钉，且**结构下限 ≈2057 行**，不可能降到 2000 以下；其门禁在干净基线就已红。 |
| `Scripts/Emuera/GameData/Variable/VariableToken.cs` | 3524 | 第一批已搬 VD.ref（694 行）；报告 `03-VariableFamily.md` 实测还有 6 个 ≥200 行的 nested 类族可继续切，**全搬完主文件可降到 ≈1102 行**。 |
| 独立"重钉扎 PR" | — | `save-baseline` 4/5 哈希漂移、`dialect-profile-selection` 2/3 漂移（均为既有红，未触碰）。 |
