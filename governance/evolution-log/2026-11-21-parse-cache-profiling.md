# 2026-11-21 P0-C 解析缓存立项第一步：erb-parse/check 构成剖析插桩

承接 markload 日志补遗的数据（erb 13.8s 占启动 75%，parse 6.1s + check 6.5s = 91%）与用户立项批准。分支 `ai/parse-cache`（主仓本地，完成即合回 `ai/ui-scale-setting`——按单分支工作流偏好收敛，见 memory `single-branch-workflow-preference`）。

## 任务目标与成果

在投入大工程（解析产物对象图序列化，预估 1500-2500 行且漏字段=运行期静默错）之前，先回答方案选型问题：**parse 6.1s 里文件读取/解码占多少、行解析占多少？check 6.5s 里参数建树（setArgument）/嵌套校验/跳转解析各占多少？** 构成数据直接决定缓存哪一层：

- 读/解码主导 → 方案 E：解码文本缓存（便宜，数百行）
- 行解析主导 → 方案 B：词法/行切分缓存
- 参数建树主导 → 方案 A：IOperandTerm 对象图序列化（大工程，含 15-20 个 Term 类 visitor）

成果（`ErbLoader.cs`，构建 0 错误、Core.Tests 429 全绿）：

- 实例级 ticks 累计器（read/line 用 mark-and-measure 差分，循环体零重排）；`checkFunctionWithCatch` 的 setArgument/nestCheck/setJumpTo 三步各自计时。
- `load_times.log` 子段名携带细分：`erb-parse(read=Xms lines=Yms)`、`erb-check(setarg=Xms nest=Yms jump=Zms)`。开销 ~10ms 级（10 万行 × 2 次 GetTimestamp）。

## 关键决策与 Why

- **先剖析后选型**（否决直接上方案 A）：LogicalLine 对象图勘察显示序列化面涉及 6 种行类型 + WordCollection + FunctionIdentifier + SelectCaseJumpTable + 行间引用（JumpTo/IfCaseList）+ 运行期状态（argprimitive 的 Pop 语义、privateVar 字典经 GlobalStatic 创建）——盲做风险/工期都不成比例；IO 若占 60% 则方案 E 十分之一成本拿到六成收益。
- **剖析常开**（非 debug 开关）：每次冷启动都产出构成数据，累积观察；开销可忽略。
- **排除**：不缓存 warning（跳过 check 后不重放，登记为行为差异——二次启动警告面板更干净）；不序列化 UserDefinedVariableToken 本体（运行期实体，经 AddPrivateVariable 重放声明）。

## 验证记录

- 构建 0 错误 [实测]；Core.Tests 429 全绿 [实测]。
- 构成数据 [未验证-需真机冷启动]：重导 APK 后 `load_times.log` 子段名即含 read/lines/setarg/nest/jump 六个数字。
- 插桩正确性 [静态]：纯计时累计，无行为分支；mark-and-measure 差分公式 `loop 总耗 - read 耗 = 行处理耗`（循环末追加 loopTicks 采样点）。

## AI 表现复盘

- **有效做法**：立项即勘察对象图（LogicalLine.cs 全读）而非直接开写序列化器——第一次把工程风险量化（1500-2500 行），才有了"先剖析"的降级决策；此前宽度/存档任务的经验模式（先门禁后实现）延续为"先数据后方案"。
- **低效/返工**：零返工；插桩用差分法避免了 130 行循环体的重排（首版方案要求重排，自评审后改为 mark-and-measure）。

## 教训 → 具体优化动作

1. (a) 本轮已执行：大工程前先做构成剖析插桩，数据驱动方案选择（ErbLoader.cs 剖析器）。
2. (b) 决策树已写入本日志"任务目标"——数据到达后按分支执行，不重复论证。

## 遗留登记

- **L1（P1）**：真机重导 APK 冷启动一次，回读 `erb-parse(read=…)` / `erb-check(setarg=…)` 六数字 → 按上方决策树定方案 A/B/E 并立项实施。
- **L2（P1，方案 A 时的前置）**：IOperandTerm/Argument 全类型清单与 visitor 骨架设计（勘察笔记：6 行类型 + WordCollection + FunctionIdentifier + SelectCaseJumpTable + UserDefinedVariableData 重放）。
- **L3（P3）**：warning 不重放的行为差异在文档登记（若用户反馈需要，缓存 warning 列表重放）。
- 无 P0。
