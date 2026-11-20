# 2026-11-21 MarkLoad 分段细化：启动计时从 7 个累计段拆为分段+erb 子段

承接 [2026-11-20-native-rust-cpp-opportunity-scan.md](2026-11-20-native-rust-cpp-opportunity-scan.md) 遗留 L1（"原生化排序前先量化"）与 2026-11-21 扫描修复日志遗留 L2。分支 `ai/markload-staging`（基于合并后 dev 9a7a97e）。

## 任务目标与成果

`debug/load_times.log` 从 7 个累计段（preload/resources/gamebase/csv/erh/erb/total-before-title）升级为**分段值+累计值双列**，且 erb 阶段（启动大头）拆出 5 个子段：

```
[LOADTIME] preload: Xms (total Yms)      ← 旧格式 [LOADTIME] stage: Nms 的分段化
[LOADTIME] erb-enum: Xms (...)           # ERB 树枚举 + # 目录收集
[LOADTIME] erb-lazy-index: Xms (...)     # lazy 索引装载/校验
[LOADTIME] erb-parse: Xms (...)          # 全部文件读取+解析（Task.Run 循环）
[LOADTIME] erb-setlabels: Xms (...)
[LOADTIME] erb-check: Xms (...)          # 三轮语法校验
[LOADTIME] erb-lazy-save: Xms (...)
```

改动：`Process.MarkLoad` 闭包加 `lastMarkMs` 差分（旧 total 保留在行内，日志可比性不丢）；`ErbLoader.LoadErbFilesAsync` 新增可选 `Action<string> markStage` 参数（null 时零开销），5 个埋点。AnalysisMode 分支保持旧单段。构建 0 错误；xUnit 429+79 全绿。

## 关键决策与 Why

- **格式 `seg (total)` 双列**而非替换：旧日志（M0 起累积）与新格式可比；无解析器消费该文件，格式变更零破坏面。
- **子段名 `erb-` 前缀**而非顶级段名：保持 7 大段心智模型，erb 内部细化不破坏阶段序。
- **markStage 可选参数注入**而非静态回调/事件：ErbLoader 与 Process 的既有构造关系（new ErbLoader(console, exm, this)）最短路径；local function `Mark` 对 null 的 `?.Invoke` 零分支成本。
- **不做**：`startup_timing` 死配置激活（2026-11-20 扫描遗留）——load_times.log 始终落盘已覆盖其意图，双机制徒增维护面，明确放弃并在此登记。
- **排除**：分段的线程安全不需要锁——各段顺序 await，无并发写入。

## 验证记录

- 主工程构建 0 错误 [实测]；Core.Tests 429 通过/Facade 79 全绿 [实测]（新 dev 带入的 FontSizeScalingTests 16 例亦绿）。
- **运行期日志输出 [未验证-需游戏内容]**：`load_times.log` 仅在 Initialize 时产生，无头环境无游戏内容；真机验证方式=任意游戏冷启动后读 `<游戏目录>/debug/load_times.log`，预期行序如上。改动为纯插桩（差分+字符串拼接+参数透传），无行为分支。
- 既有 ErbLoader 逻辑零改动核对 [静态]：`Mark` 仅新增调用点，`Task.Run` 结构/warning 刷新顺序/异常路径未触碰。

## AI 表现复盘

- **有效做法**：埋点前实读 ErbLoader 全函数（130-175 行段落结构），把 `Mark` 插在 `ParserMediator.FlushWarningList()` 之后而非之前——警告刷盘（可能触发 warning 重读文件）计入其所属阶段而非下一阶段，计时归因准确。
- **低效/返工**：无返工；AnalysisMode 分支的首版方案漏了它（构建时发现 LoadErbsAsync 无 markStage 参数会导致调用编译错），第二版把 MarkLoad("erb") 移入 Analysis 分支保持兜底。

## 教训 → 具体优化动作

1. (a) 本轮已执行：插桩段落按"归因"放置（FlushWarningList 后），非机械行号。
2. (b) 遗留见下 L1-L3。

## 遗留登记

- **L1（P1）**：真机/有游戏桌面跑一次冷启动，回读 load_times.log 分段分布——数据出来后重排原生化优先级（读侧解码 vs 2D/3D 段 vs HTML）。**这是本轮的产出消费动作**。
- **L2（P2）**：`erb-parse` 可再加"文件数/总行数"字段（ErbLoader 内有 files 计数，行数无现成累计）。
- **L3（P2）**：`startup_timing` 死配置正式移除（本任务已决定放弃激活，配置残留可在诊断配置清理任务一并删）。
- 无 P0。
