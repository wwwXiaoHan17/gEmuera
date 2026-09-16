# 契约断言改指真实归属（contract-assertion-repointing）

> 承接 `governance/evolution-log/2026-09-15-guard-and-tooling-consolidation.md`。那份记录把
> `Test-InProcessSessionCycle.ps1` 的 4 条失败定性为「接口真实演进、需领域决策、不要盲改」
> （`FILE_STANDARD.md` §6.4 L188-192 同此口径）。本轮把它查清并改完：**4 条全部是生产代码
> 搬迁 / 改名导致断言指错文件，业务契约本身没有变**，另加一条会把后续诊断一起吞掉的切片崩溃点。

## 任务目标与成果

目标：让 `tools/legacy-runner/Test-InProcessSessionCycle.ps1` 里那 4 条因生产代码重构而失效的
契约断言重新指向真实归属，并判明「是契约变了，还是代码搬了」——这决定了该改断言还是该报案。

| # | 事项 | 结果 |
| --- | --- | --- |
| 1 | L97/L98/L99 三条对 `$mainSource` 的断言 | 改指 `$gpuComponentSource` / `$textComponentSource` 并换成语义锚点 |
| 2 | L208 `while (uiQueue.TryDequeue(out _))` | 改指环形缓冲自身的非空谓词 `while (uiQueueCount > 0)` |
| 3 | L182-184 Program.cs 切片定界（会被同一次修复暴露的崩溃点） | 同 PR 修掉：先算两个 `IndexOf` → 守卫断言 → 才 `Substring` |
| 4 | 脚本全量 | 实测 `M1 in-process session-cycle contract tests passed.`，`EXITCODE=0` |

规模（我亲自数）：`Test-InProcessSessionCycle.ps1` 309 行 / 37402 字节；`git diff --stat` 为
`1 file changed, 6 insertions(+), 5 deletions(-)`。

## 现象：4 条失效的原因是搬迁与改名，不是契约变更

新旧原文均为本轮亲自导出（历史版本用 `git show <rev>:<path>` 落盘后按行号打印，未用 `Get-Content`）。

### 旧代码：`git show 'd24f656^:Scripts/EmueraMain.cs'`（L69-83，d24f656 = 2026-07-25）

```csharp
 69: 	internal static void ResetCanarySessionState()
 70: 	{
 71: 		while (gpuQueue.TryDequeue(out var gpuItem))
 72: 		{
 73: 			gpuItem.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
 74: 			gpuItem.Completed.Set();
 75: 		}
 76: 		while (textRenderQueue.TryDequeue(out var textItem))
 77: 		{
 78: 			textItem.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
 79: 			textItem.Completed.Set();
 80: 		}
 81: 		currentInstance?.ResetPendingRenderState();
 82: 		GpuReady = false;
 83: 	}
```

### 新代码：三处（`EmueraMain` 只剩门面）

```csharp
// Scripts/GodotHost/EmueraGpuRenderComponent.cs:32-43
32:     internal static void ResetCanarySessionState()
34:         while (workQueue.TryDequeue(out var item))
36:             item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
37:             item.Completed.Set();
39:         currentInstance?.ResetPendingRenderState();
41:         GraphicsImage.ResetColorMatrixMemoize();   // ← 旧代码没有，属新增
42:         GpuReady = false;

// Scripts/GodotHost/EmueraTextRenderComponent.cs:110-118（+ 实例方法 L120-135，多槽位）
112: 		while (renderQueue.TryDequeue(out var item))
114: 			item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
115: 			item.Completed.Set();
117: 		currentInstance?.ResetPendingRenderState();   // → L122 foreach (var slot in renderSlots) / L127 slot.Item.Completed.Set();

// Scripts/GenericUtils.cs:411-426（环形缓冲版）
413:             while (uiQueueCount > 0)
415:                 UiEnvelope envelope = uiQueueRing[uiQueueHead];
416:                 uiQueueRing[uiQueueHead] = null;
417:                 uiQueueHead = (uiQueueHead + 1) % uiQueueRing.Length;
418:                 uiQueueCount--;
419:                 envelope.Action = null;
420:                 envelope.DisplayWork = false;
421:                 uiEnvelopePool.Push(envelope);
424:         Interlocked.Exchange(ref pendingUiActions, 0);      // ← 与旧 L439 逐字相同
425:         Interlocked.Exchange(ref pendingDisplayActions, 0);  // ← 与旧 L440 逐字相同
426:         Interlocked.Increment(ref uiFrameGeneration);        // ← 与旧 L441 逐字相同

// Scripts/EmueraMain.cs:63-67——已无队列、无排空、无 Completed，只剩转调
63: 	internal static void ResetCanarySessionState()
65: 		EmueraGpuRenderComponent.ResetCanarySessionState();
66: 		EmueraTextRenderComponent.ResetCanarySessionState();
```

**逐句等价**：旧 L71→新 :34、旧 L73→新 :36、旧 L74→新 :37、旧 L76→新 :112、旧 L78→新 :114、
旧 L79→新 :115、旧 L81→新 :39/:117、旧 L82→新 :42 全部同构，差别只有变量名
（`gpuItem`→`item`、`gpuQueue`→`workQueue`）与承载类（`EmueraMain` 静态类 → 组件自身实例）。
旧 GenericUtils L438 `while (uiQueue.TryDequeue(out _)) { }`（739b428^ 我实测 L55 仍是
`static readonly ConcurrentQueue<Action> uiQueue`）→ 新 L413 是同一件事在环形缓冲下的写法：
`uiQueueCount > 0` 就是环形缓冲自己的非空谓词，即「`TryDequeue` 返回 true」的文本后继。
新代码在每一处都**只多不少**：GPU 多了 `ResetColorMatrixMemoize()`；文本把在途项从单槽
（旧 `pendingTextRenderItem`）加强为 `renderSlots` 多槽；环形排空多做了清槽、递减、归还信封池。
⇒ 判定 **rename-only（含跨文件搬迁）**，不是 contract-changed，也不是 capability-removed。

### 为什么这 4 条会「静默」失效（当前命中数实测）

| 旧断言串 | 断言目标变量 | 在目标文件里的当前命中数 |
| --- | --- | --- |
| `gpuQueue.TryDequeue` | `$mainSource` | **0** |
| `textRenderQueue.TryDequeue` | `$mainSource` | **0** |
| `ResetPendingRenderState` | `$mainSource` | **0**（`EmueraMain.cs` 内该串 0 次；同文件 `ResetCanarySessionState` 仍 3 次） |
| `while (uiQueue.TryDequeue(out _))` | `$genericUtilsSource` | **0**（当前 `GenericUtils.cs` 内 `TryDequeue` 0 次） |

失效是**静默**的：这些断言在重构当天就变成恒假，却没有任何机制提示「你的锚点已经不指向任何代码了」——
只有把脚本跑起来才会红，而这份脚本是手工调用的（`tools/legacy-runner/` 下 7 个 `Test-*.ps1`
无一引用它，只有 `FILE_STANDARD.md` 与治理日志提到它）。

## 关键决策与 Why

1. **改指向 + 换锚点，而不是删除断言。**
   `FILE_STANDARD.md` L192 当时悬置了一个「删除 vs 重写」的判断，理由是「L97–99 似已被
   L102/L103/L106/L107 覆盖」。探明推翻了这条覆盖假设：L102 断言的是 `workQueue.TryDequeue`
   这个子串，它同样出现在组件的 CPU 兜底排空与 `_ExitTree` 排空里，**删掉真正的排空循环它照样通过**。
   删掉 L97 只会留下「没有任何断言覆盖重置语义」的空档，所以选择重写。
2. **锚点钉语义关键点，而不是任意标识符。**
   L97-99 原来钉的是队列字段名 / 方法名这类标识符——它们是重构最先改的东西，与契约无关。
   新锚点分别是「排空循环的完整头部」「被排出的项会被 `Completed.Set()`」「等待者会被放行的槽位语句」，
   即契约本身的落点（详见下节教训 1）。
3. **L182-185 必须同 PR 修，不能留到「下一个诊断性 PR」。**
   旧写法在 Program.cs 上必抛（我实测：全文 `/// <summary>` 只出现在 L60/254/277/310/351，
   **全在 L415 `internal static void ResetSessionState()` 之前** ⇒ 旧 L183 的
   `IndexOf('/// <summary>', start)` 返回 -1 ⇒ 旧 L184 的 `Substring(start, -1-start)` 长度必为负）。
   实测异常消息只有一句 `Length cannot be less than zero. Parameter name: length`，
   既无文件名、无行号，也不提 `ResetSessionState`。而它在脚本里的位置早于 GenericUtils 那段，
   **L184 一抛，L209 的修复结果就永远观测不到**。修法刻意不用 L265-267 那种写法：先算两个
   `IndexOf` → 先断言 `$programResetEnd -gt $programResetStart` → 最后才 `Substring`，
   且第二锚点起点写 `$programResetStart + 1`（锚点缺失时 startIndex=0 合法、不再抛，沿用 L152 既有技巧），
   定界锚点换成结构上必然存在的下一个成员声明（`Program.cs:439`），不再依赖「下文有没有写文档注释」。
4. **保留全部旧有的宽松锚点，是叠加而不是替换。**
   L100/L101/L106/L107 那四条原样留着重钉（`workQueue.TryDequeue`、`ResetPendingRenderState` 等），
   新断言插在它们前面。多一条更长的锚点等于多一道绊线，代价零。

## AI 表现复盘

- **有效**：
  - **先判性质、再决定改法**。4 条断言摆在面前时，最省事的做法是「找到同名符号就改上去」；
    本轮坚持先把新旧的**逐句语义**对上（导出 d24f656^ 原文打印行号），确认是搬迁而非契约变更，
    才动手改指向。判定若反过来（其实是契约变更却按改名处理），就会把守卫改成永远成立。
  - **要求给出「能通过新断言但实际违约」的反例**。三个单元都实测列出了具体反例，
    其中最关键的一条是：`item.Completed.Set();` 这类单行子串在文件里多次命中，
    删掉排空循环里那一行它照样通过——这个自曝促成了「锚点必须钉在排空循环内」的取舍，
    也明确了「文本断言只能当绊线，真正的证明要跑代码」。
  - **校验方独立复现，并把「非空洞性」做成可执行实验**：把每条新锚点从源码文本里移除后
    重算 `Contains` 必须由 True 变 False，四条全部变红。这条实验把「断言不是恒真摆设」从
    口头承诺变成可复跑的证据。
  - **校验方对夹带做了加强判据**（`git diff -w` / `--ignore-all-space --numstat -- Scripts/`），
    把「3 个 Scripts 文件被改」直接判成纯空白重排，避免了「本次修复夹带了生产代码」的误判。
- **低效**：
  - **被「同一子串在别处也命中」咬了两次**。探明阶段一度想用 `item.Completed.Set();` 单行
    作为最终锚点，实测发现它在 GPU/文本组件里都有 3-4 处命中（正常渲染路径），
    整文件 `Contains` 会被那些路径满足；绕了一圈才回到「限定在 reset 的排空循环内」。
    教训见下节。
  - **第一位检查者对脚本清单的怀疑是错的**：怀疑 `$backendPath` 未列入 L15-48/L49 清单，
    实测它在 `L20` 定义、在 `L49` 清单里是第 6 项，清单也没有「声明未列出」或「列出未使用」的项。
    这类「凭印象怀疑清单」的动作应改成直接核对（一条命令）。
  - **校验过程本身出过一次破坏性事故（校验方自报，我未复现其过程）**：
    在临时校验脚本里把函数命名为 `Rd`（本意包装 `[IO.File]::ReadAllText`），
    而 PowerShell 的命令解析顺序是 **Alias 优先于 Function**，`rd` 是 `Remove-Item` 的内置别名，
    于是 4 次调用删掉了 4 个仓库文件及其 `.cs.uid` 侧车；随后对这 8 条精确路径执行
    `git restore --source=HEAD` 还原。我独立核对的一致性：当前 `git status --porcelain` 无任何
    `D` 条目、涉事文件不在修改列表里，与「已完整还原」一致。

## 教训 → 具体优化动作

1. **契约测试把生产源码当文本断言时，重构会「静默地」让断言失效；断言锚点必须钉在语义关键点上，
   而不是任意标识符。**
   本轮 4 条断言钉的全是**重构第一时间就会改掉**的东西：私有队列字段名（`gpuQueue`）、
   承载类名（`EmueraMain` 内的实例方法）。代码一搬家，断言就变成恒假，而**没有任何编译期或
   结构性的提示**——只有跑脚本才红。
   具体动作（已落进脚本）：**改用「契约落点」作锚点**——
   「排空陈旧工作 + 唤醒被阻塞的等待者」这条契约的落点是 `item.Completed.Set();` 与
   `slot.Item.Completed.Set();`（释放 `ManualResetEventSlim`），不是队列字段名；
   并且**把锚点限定在承载该方法的那段代码里**，因为同名字符串在正常渲染路径上也有命中
   （GPU/文本组件里各有 3-4 处），整文件级 `Contains` 会被它们满足，形成假覆盖。
   ⇒ 下次写这类断言时，先问一句：「如果这段代码被搬到别处、只改名字，我的断言还会指向同一件事吗？」
2. **文本断言只能当绊线，不能当证明。**
   本轮已明确：`Contains` 类断言无法排除「排空 + Set 整块被抽成从不被调用的私有方法」这种改法
   （原理性上限，不是实现瑕疵）。真正的语义证明需要运行时用例
   （enqueue → `ResetCanarySessionState()` → 断言 `Completed.Wait(0) == true` 且 `ResultImage` 为 1x1）。
   注意 reset 内部会调 `Godot.Image.CreateEmpty`，所以这条要走 **GDUnit4**，xUnit 跑不了。
   已列进遗留，本轮不假装覆盖。
3. **在 PowerShell 里给函数起名，先避开内置别名（`rd`/`ls`/`cd`/`cat`/`sc` 等）。**
   别名优先于函数，`function Rd` 会静默变成 `Remove-Item`。校验脚本改为不定义函数、直接内联
   `[IO.File]::ReadAllText(...)`；涉及删除/覆盖的高危命令名一律先查 `Get-Alias`。
4. **修一个「会崩溃的诊断点」时，先算全部索引、先断言、再切片。**
   一次性修掉的不只是本次 4 条断言：同类的 `L266`
   （`$forceClearEnd = IndexOf('static bool CanEvict', $forceClearStart)` 没做 `+ 1`，
   锚点缺失时第二个 `IndexOf` 自己先抛，定制消息永远轮不到）仍在，见遗留。

## 落地清单

- **修改（唯一）**：`tools/legacy-runner/Test-InProcessSessionCycle.ps1`（3 个 hunk，净 +1 行）：
  L97-107 组件队列断言块、L182-185 切片定界守卫、L209 GenericUtils 排空断言。
- **未改**：`Scripts/`、`src/` 下任何生产代码（4 条断言的目标文件
  `EmueraGpuRenderComponent.cs` / `EmueraTextRenderComponent.cs` / `GenericUtils.cs` /
  `Program.cs` / `EmueraMain.cs` 均不在 `git status` 修改列表里）。
- **未改**：文档（`FILE_STANDARD.md`、`tools/legacy-runner/README.md`）——本轮范围外，见遗留。
- **新增**：仅本进化记录一个文件。

### 仓库状态口径（一处必须点明的偏差）

修复报告写的是「唯一被修改的文件」，但 `git status --short` 实际列出 **4 个** M 条目：

```text
 M Scripts/GenericUtils.DiagnosticsLogging.cs
 M Scripts/SpriteDebugNotifier.cs
 M Scripts/SpriteDebugViewer.cs
 M tools/legacy-runner/Test-InProcessSessionCycle.ps1
?? android/
```

我亲自核实的三条证据：（a）`git diff -w --stat` 只剩 `Test-InProcessSessionCycle.ps1`
（`1 file changed, 6 insertions(+), 5 deletions(-)`）；等价地
`git diff --ignore-all-space --numstat -- Scripts/` **输出为空**。（b）
`git diff -- Scripts/SpriteDebugNotifier.cs` 的实际内容是缩进由空格改成 tab（`-    public` / `+	public`），
无语句增删。（c）这 3 个文件的 `LastWriteTime` 同为 `2026-09-15 17:27:02`，早于测试脚本的
`2026-09-15 17:47:02`。
⇒ 结论：「唯一被修改」应读作「本次会话唯一被修改」，那 3 个 Scripts 文件是**本次修复开始前就已存在**
的纯空白重排（非本次产生）；`?? android/` 是未跟踪目录。提 PR 时只应挑
`tools/legacy-runner/Test-InProcessSessionCycle.ps1`（与本文档）。

## 被推翻的中间结论

1. **「这 4 条是真正的契约漂移，需领域决策、不该盲修」——被推翻。**
   `FILE_STANDARD.md` L188-192 与上一份进化记录都这么写。实测：4 条旧串的当前命中数全为 0（指错文件），
   而旧实现在 `d24f656^` 与组件现行实现**逐句同构**，新代码只多不少 ⇒ 属 rename-only。
   所以正确的动作是「改指向 + 换语义锚点」，不是「重新做契约决策」。
2. **「不是能力搬迁，是删掉了一份重复副本」——推翻了更早的「搬迁」假设。**
   我实测 `git show 'd24f656^:Scripts/GodotHost/EmueraTextRenderComponent.cs'`（187 行）的
   L28-36 与当前 L110-118 逐句相同：组件侧在 `d24f656` 之前**就已经具备**同构实现。
   即 `d24f656` 删掉的是 `EmueraMain` 里那份与组件重复的陈旧副本，而非把能力搬过去。
3. **「L184 会让 L185/L186 在空切片上真空通过」——被推翻。**
   `FILE_STANDARD.md` L199-200 与上一份进化记录 L64 都这么写。实测：Program.cs 全文
   `/// <summary>` 只在 L60/254/277/310/351（全在 L415 之前）⇒ 第二个 `IndexOf` 返回 -1
   ⇒ 旧 L184 的 `Substring` 长度必为负 ⇒ **必抛**，L185/L186 根本不会执行。
   真正的危害是「不可诊断的崩溃 + 吞掉后续诊断」，不是「真空通过」。（这两处文档本轮未改。）
4. **两个凭印象的推断被推翻（探明阶段）**：① 「`$backendPath` 未列入清单」——我实测它在 `L20`
   定义、在 `L49` 清单里是第 6 项，且清单无声明遗漏。② 「`ResetSessionState()` 之后最近的
   `/// <summary>` 在 L439」——我实测 `Program.cs:439` 是
   `internal static bool TryResolveLegacyRunnerDefaultOutputLogPath(string requestedPath, out string outputPath)`
   的方法声明，不是文档注释。
5. **「L97–99 已被 L102/L103/L106/L107 覆盖，可以考虑删掉」——被推翻。**
   探明报告实测：L102 的 `workQueue.TryDequeue` 子串在组件里还命中 CPU 兜底排空与 `_ExitTree` 排空，
   删掉真正的排空循环它照样通过 ⇒ 不是真覆盖，删掉会留下空档。
   （我独立复核：该子串在 `EmueraGpuRenderComponent.cs` 内共 **4 处**命中，确非唯一锚点。）

## 遗留（本轮未处理项与「为什么不动」）

### A. 脚本内残留的陈旧 / 弱断言（行号按当前 309 行版；L184 之后比修复前整体 +1 行）

| 位置 | 隐患 | 为什么这次不动 |
| --- | --- | --- |
| `L95` | 锚点 `Console.IsInProcess, which is false while` 只命中 `Scripts/EmueraMain.cs:338` 的**行注释**（我实测该文件内 `IsInProcess` 仅此 1 处，且该行以 `//` 开头）⇒ 把语义改回注释所警告的形态、注释原样保留，断言照常通过 | 与本次 4 条改指无关；它是一条「注释即契约」的既有绊线，删掉等于白丢一条提示 |
| `L143` | 排序断言只在 L141 保证左操作数存在，右操作数 `inputEvent?.Dispose()` **没有存在性断言**（我实测 `Scripts/EmueraThread.cs` 内 1 处）；删掉该行则 `-1 < 正数` ⇒ 静默通过 | 修它要新增断言，超出「改指 4 条」范围；这是本轮校验阶段**新发现**的空档（修复报告的披露清单里未含此条） |
| `L155`/`L156`/`L157` | 切片内左操作数被移出 `StartAsync` 后 `IndexOf` 返回 -1 ⇒ `-1 < 正数` ⇒ 伪顺序通过 | 既有问题，涉及 `StartAsync` 切片边界与契约口径，属独立诊断性改动 |
| `L171` | `GlobalStatic.Reset()` 在 `LegacySessionBackend.cs` 内有 3 处同名调用（切片内 2 处：真目标与基线路径），删掉真目标仍通过；L172 断言的是另一个方法，接不住 | 同上；需要「限定在 canary 路径内」的边界，属契约决策 |
| `L192` | `#if UEMUERA_DEBUG` 与 `StackList.Clear()` 两条独立 `Contains` 的 `-and`，不保证 Clear 在守卫内 | 既有；证明力问题而非失效问题 |
| `L233` | 用 `LastIndexOf` 当排序证明，文件任意处再插一处同名符号即可翻转结论 | 既有；同上 |
| `L237` | 负断言含 `readonly`（`-not Contains('readonly static string macroPath')`），去掉 readonly 重新引入 bug 仍通过 | 既有；负断言的固有形态 |
| `L266` | `$forceClearEnd = IndexOf('static bool CanEvict', $forceClearStart)` 未做 `+ 1`：锚点缺失时**第二个 `IndexOf` 自己先抛**裸异常（`Index was out of range... startIndex`），L267 的定制消息永远轮不到 —— **与本次修掉的 L182-184 同类** | 校验方判定「非本次必需，可留到下一个诊断性 PR」；且它 fail-closed（仍 `exit 1`，不是假绿），故严格守范围未动 |
| `L276` | `Contains('LegacySessionLaunchRegistry')` 命中的是类声明本身（自指套话），与 L50 的存在性检查重复，「Host launch registry is missing.」这条契约实际无人守 | 既有 |
| `L241` | `$configSource` 被复用（L65-76 指向 `LegacyRunnerConfig.cs`，L241 改指 `Config.cs`）；L241 之后任何新增的 `$configSource.Contains(...)` 会静默检查错文件 | 当前逻辑正确；改名属重构，不在本任务 |
| `L293`/`L299` | `PreviousGate:M0` 不是漂移，是**当前真实值**（Aba / Cross 比较脚本同值） | **绝对不能动**，改成 LegacyRunner 会让断言必红 |
| `L303` | 通过消息仍写阶段代号 `M1` | 属文案；`AGENTS.md` 禁止阶段代号命名，但改文案宜另开 PR 避免混入 |

### B. 明确的覆盖空档（本轮**不编造能过的串**来填空）

- **「被阻塞的等待者会被唤醒」在 GPU 侧仍无断言覆盖**：`pendingGpuItem.Completed.Set();` 在
  `EmueraGpuRenderComponent.cs` 内有 2 处命中（排空路径与正常完成路径），非唯一锚点，
  单行 `Contains` 会被正常路径满足；本轮只覆盖了文本侧的 `slot.Item.Completed.Set();`（当前唯一命中）。
- **`EmueraMain` 门面到两个组件的委派仍无断言**：L96 只断言名字 `ResetCanarySessionState`，
  而 `EmueraMain.cs:63` 的定义签名单独就能满足它。若门面委派被删，`LegacySessionBackend` 里对两个
  组件的直接调用仍在，运行时毫无症状、也没有任何断言会报警。
- **GenericUtils 的 pending 计数子句无断言**：`Interlocked.Exchange(ref pendingUiActions, 0);`
  （`GenericUtils.cs:424`，全文件唯一、非注释）没有被任何断言覆盖；建议追加为独立单行断言。
  **注意别用** `uiQueueRing[uiQueueHead] = null;`（:416 与 :479）、
  `envelope.Action = null;`（:419 与 :559）、`uiEnvelopePool.Push(envelope);`（:421 与 :562）——
  正常 `FlushUI` 路径也命中，是假阳性锚点。
- **真正的语义证明仍需运行时用例**：GDUnit4（enqueue → `ResetCanarySessionState()` →
  `Completed.Wait(0) == true` 且 `ResultImage` 为 1x1）。xUnit 跑不了（reset 内调 `Godot.Image.CreateEmpty`）。
  本轮已实测三条文本侧 `Contains` 断言在数种违约实现下都能通过，故不把文本断言当证明。

### C. 文档漂移（本轮禁止改文档，仅记录）

- `FILE_STANDARD.md` §6.4 **L188-192** 对 4 条断言的定性（「真正的契约漂移」）已被本轮推翻，应更新为 rename-only。
- `FILE_STANDARD.md` **L199-201** 关于「L185/L186 在空切片上真空通过」的论断应更正为「旧 L184 先抛、
  L185/L186 不执行」；同类真空通过还有 `Test-LegacyDisplay.ps1:66`、`Test-LegacySettlement.ps1:54`（未复核）。
- `tools/legacy-runner/README.md:88` 仍写 `EmueraMain.RestartLegacySessionForM0RunnerAsync()`：
  我实测该串在 `tools/legacy-runner/README.md` 内出现 1 次，而真实符号是
  `Scripts/EmueraMain.cs:312 RestartLegacySessionForLegacyRunnerAsync`
  （正是脚本 L90 断言的那个）。**脚本是对的，README 是旧的**——要改文档，不要改断言。

## 附记：本记录落盘之后主会话又做的三处收尾（2026-09-15，追加不改上文）

上面「遗留 A」表里 `L266` 那条我标了「可留到下一个诊断性 PR」。主会话复核后判定它**就是本任务同类的
诊断性缺陷、且是五行改动**，当场修掉，而不是留档：

1. **修 `L266` 同类缺陷（已修）**：`$forceClearEnd = ...IndexOf('static bool CanEvict', $forceClearStart)`
   改为 `...IndexOf('static bool CanEvict', [System.Math]::Max($forceClearStart, 0))`。
   即锚点缺失时 startIndex 用 0（合法）而不是 -1（跳过抛异常），让紧跟的 L267 守卫能给出定制消息。
   实测三态：原文件 `exit 0`；把 `ForceClear()` 改名 → `exit 1 / 'SpriteManager full lifecycle cleanup boundary is missing.'`；
   把 `CanEvict` 改名 → 同一条定制消息。**修复前这两种改名都会抛裸的 `Index was out of range... startIndex`。**
   ⚠️ 因此**上文与遗留 A 里的行号在本次收尾后仍然有效**（改动是行内替换，未增删行；文件仍是 309 行）。
2. **修 README 旧符号（已修）**：`tools/legacy-runner/README.md:88` 的
   `RestartLegacySessionForM0RunnerAsync()` → `RestartLegacySessionForLegacyRunnerAsync()`。
   实测前者全仓只出现在文档里（本记录与 `FILE_STANDARD.md` 提到它是在引用这个错误名），代码里零命中。
3. **更正 `FILE_STANDARD.md` §6.4（已改）**：把「仍然红的 4 条 = 真正的契约漂移（未修，也不该盲修）」
   划改为已推翻，并更正「L185/L186 在空切片上真空通过」的错误论断（实为 L184 先抛、L185/L186 不执行）。
   同时把附录 A 的 `VariableEvaluator.cs` 标注为「有结构地板、降不到 2000 以下」。

> 这三处是本记录写成之后的改动，故以「附记」追加，**不改动上文任何已发布内容**（遵守
> `governance/README.md`「不删历史：进化记录只增不改」）。

