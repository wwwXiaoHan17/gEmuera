# 2026-11-21 修复扫描发现的确定性浪费 + SETCOLORBYNAME NRE

承接 [2026-11-20-native-rust-cpp-opportunity-scan.md](2026-11-20-native-rust-cpp-opportunity-scan.md) 遗留 L2（用户追问"修了没"后立项）。分支 `fix/scan-deterministic-waste`（worktree 内基于 dev 8c18958，与 spike 分支分开），**未推送**。

## 任务目标与成果

五项扫描发现全部落地（5 文件改动 + AGENTS.md 一句话补丁 + 本日志），主工程构建 0 错误，xUnit 三套件全绿（413+79+4，含 `FirstPartyPackEquivalenceTests`）。逐项：

1. **warning 重读全文件**（`Process.cs` `getRawTextFormFilewithLine`）：`Preload.TryGetFileLines` 优先命中，未命中回退原磁盘读取路径；顺带消除越界行号 `Skip().First()` 抛 `InvalidOperationException` 的崩溃风险（改为边界检查返回空串）。缓存命中窗口=解析期警告刷盘（`ErbLoader.cs:130` FlushWarningList 在 `:143` RemoveByExtension 释放 erb 缓存之前）；运行期 CHECK 类警告走回退路径（原行为）。
2. **`loadedFirstFiles` O(n²) 判重**（`ErbLoader.cs:68`）：`List<string>`→`HashSet<string>`，默认序数比较与 `List.Contains` 字符串相等语义一致（刻意不用 IgnoreCase：Android 文件系统大小写敏感，保持精确等价）。
3. **STRCOUNT/REPLACE 每次 `new Regex`**（`Creator.Method.cs:841/1016`）：新增 namespace 级 `EraRegexCache`（ConcurrentDictionary，256 条上限防御性 Clear），无效模式不缓存、照旧抛 `ArgumentException` 由调用方转 CodeEE——异常语义逐字保留。
4. **lazy 开启时 ERB 树枚举**（`Preload.cs:61-64`）：`.Where(IsPreloadTarget)` 前移到 `AsParallel()` 之前，无关文件不再进入并行调度管线。
5. **SETCOLORBYNAME/SETBGCOLORBYNAME NRE**（`Process.ScriptProc.cs:492/539`）：`str.Equals(...)`→`string.Equals(colorName, ...)`。`str` 是 `doNormalFunction` 局部且这两个 case 从未赋值——空/无效色名必 NRE，连 "transparent" 专属报错分支都不可达；现空色名得到预期 CodeEE。同 switch 的 `:573-577` 的 `str.Equals` 有赋值前置路径，未动。

## 关键决策与 Why

- **修 4 的定性修正（部分推翻 2026-11-20 扫描结论）**：实读 `Preload.LoadFile` 后确认它只读目标扩展名文件——"白枚举"的真实浪费=目录遍历本身 + 过滤位于并行化之后。目录遍历不可省（ERH/CSV/ERD 可能与 ERB 共存于 ERB 树，`HeaderFileLoader` 依赖该树），故本轮只做零语义改动的过滤前移；遍历本身的归并优化归入启动量化任务（见遗留）。
- **不动解释器语义面**：修 3/修 5 触及内建函数与指令 switch，但未触碰 Compatibility/注册表/方言名单，三冒烟门禁未触发；以 Core.Tests 内 `FirstPartyPackEquivalenceTests` 全绿作为"未选择侧不变"硬证据。
- **AGENTS.md 升级（沉淀阶梯：同类教训第 3 次出现）**：本次 CS0104 与既有 CS0579 条目同根（杂散 obj .cs 被 glob 收编），在「构建与验证」节该条下补 GlobalUsings.g.cs 变体一句。
- **排除项**：不修 `:573-577`（非缺陷）；不给 Regex 缓存做 LRU 淘汰（256 上限全清已足够，脚本模式集合小）。

## 验证记录

- 主工程构建 0 错误 [实测]（`dotnet build -nodeReuse:false -m:1`，判定口径含 DLL 产出；**前置条件**：删除 worktree 内 `tools/native-width-check/bin|obj` 残留，见下）。
- xUnit：Core.Tests 413 通过/1 跳过、Facade.Tests 79、PluginAbi.Tests 4，全绿 [实测]。
- 运行期行为（真机/legacy-runner 跑游戏观察 warning 与 STRCOUNT 频繁调用）[未验证——需游戏内容，超出本地构建验证口径]；五处改动均为可静态论证的局部等价变换，语义等价由 FirstPartyPackEquivalenceTests 全绿佐证。
- 环境障碍与打通（**本轮最有价值的意外发现**）：切到 fix 分支后构建报 `CS0104 Timer 歧义`（`scripts/GodotHost/AndroidWindowOrientationController.cs:17`），同内容 spike 分支却绿。判别路径：确认文件内容/SDK/工作树三同 → spike 分支重建仍绿（改动被 stash 携带）→ csproj diff 唯一差异是 spike 的 `Compile Remove="tools\native-width-check\**"` → **根因：昨天 `dotnet run` 在 tools/native-width-check 留下的 `obj/**/GlobalUsings.g.cs`（gitignore 不删文件、跨分支残留）被 dev csproj 的默认 glob 收进宿主编译，其 `global using System.Threading;` 造成 Godot.Timer/System.Threading.Timer 歧义**。删除两目录残留后 fix 分支干净重建通过。这与 AGENTS.md 既有"杂散 obj .cs → CS0579"条目同根，已在该条补新变体（本 PR 内完成升级）。

## AI 表现复盘

- **有效做法**：同输入不同结果的矛盾（同文件、同 SDK、一绿一红）没有走"顺手给文件加 using 别名糊过去"的捷径，而是先做最小判别实验（两分支同内容重建对比），把变量压缩到 csproj 后再落到 obj 残留——修的是环境病灶而非症状。
- **有效做法**：修 4 前先实读 `Preload.LoadFile` 全函数，避免了按扫描结论（"白枚举"）直接跳目录造成行为破坏。
- **低效/返工**：CS0104 出现后先入为主猜了三轮环境变量（隐式 using 配置/SDK 版本/分支文件差异）才做判别实验，返工 2-3 轮假设。可复用动作：**同输入不同输出时，第一动作就是两状态最小差异实验，不做假设链**。
- **扫描结论被部分推翻并如实降级**：修 4 按上文"定性修正"处理，未按原话"白枚举 ERB 树"过度优化。

## 教训 → 具体优化动作

1. (a) 本轮已执行：AGENTS.md「构建与验证」节 CS0579 条目补 GlobalUsings.g.cs/CS0104 变体（`AGENTS.md:91-96`）。
2. (a) 本轮已执行：切分支后清理跨分支残留 obj/bin 才构建（本轮实际操作顺序，`rm -rf tools/native-width-check/bin obj`）。
3. 升级去向：暂不自动化（"构建前查残留 obj"可脚本化，但复发成本低、脚本维护成本高；待同类再犯再进 tools/governance）。

## 遗留登记

- **L1（P1，不在本轮原因：属用户决策）**：本分支（5 修复）与 `ai/native-width-spike`（spike 两提交）均未推送/未合并——等待用户指示推送与 PR 策略（推送依赖本地代理，PR 走 GitHub API）。
- **L2（P2，不在本轮原因：归并进启动量化任务更划算）**：Preload 目录遍历在 lazy 模式下的真实占比需先有 `MarkLoad` 分段数据（见 2026-11-20 扫描日志遗留 L1）。
- **L3（P2，不在本轮原因：占比小）**：运行期 CHECK 警告在缓存释放后仍走全文件读回退——若量化显示占比可观，可把 Preload 释放改为"保留最近告警文件"或延迟释放。
- 无 P0（本轮范围内五项已全部落地）。
