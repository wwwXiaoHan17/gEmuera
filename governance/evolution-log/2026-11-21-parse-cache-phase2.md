# 2026-11-21 P0-C Phase 2：Argument/表达式树序列化（entry v4）

承接 [2026-11-21-parse-cache-phase1.md](2026-11-21-parse-cache-phase1.md)（Phase 1/1.5）与设计文档 [docs/plans/2026-11-21-parse-cache-design.md](docs/plans/2026-11-21-parse-cache-design.md)。工作分支：主仓 `ai/ui-scale-setting`（单分支工作流），提交 `21d2d8a`。

## 任务目标与成果

消除 check 段 setarg（~4.2s/轮：FORCE_SETARG 行每次启动重建 Argument）。成果（桌面 eraTW 副本 legacy-runner 闭环实测）：

- **命中轮 setarg 4292→3078ms**（calls=529156 中 **skipped=248880 短路**，47%）；erb-parse 3190→2182ms；**总启动 12.5→10.6s**。
- 新增 `ErbParseTermCodec.cs`（~500 行）：term 树 codec（SingleTerm 三态/NullTerm/VariableTerm 按名重绑+名校验/内建 FunctionMethodTerm 按类型全名经会话键控注册表视图重绑/运算节点 (kind,op) 反查+Reduce 系重建/VariadicArgTerm/StrFormTerm 含 Format 三槽与五静态槽特例）；Argument codec（Expression/Expressions/Void/ExpressionArray/SpTimes/Case/PrintData/StrData）；**不支持的形状按参数降级**（ARebuild，内存试写防流错位），恢复后由 `SetArgumentTo` 的 `Argument!=null` 短路天然跳过。
- 关键配套：**锚点移至 checkScript 之后**（FORCE_SETARG 行的 Argument 与 label.Depth——setJumpTo 的 CALL 传播——都在 check 期才产生；parse 后锚点只能缓存到 null，实测 calls=529156 skipped=0 的证据链）；**行化期参数原语快照**（setArgument 会不可逆 Pop）；OperatorMethodManager 增反查索引与 ReduceByKind；临时插桩（dump 词展开/arg_types 直方图/calls/skipped/restored 计数）用后清理，计数器保留（归零点修至 LoadErbFilesAsync 开头）。

## 关键决策与 Why

- **按参数渐进降级**（否决全有全无）：任一 term/Argument 形状不支持只损失该行缓存，整文件其余行照常受益——覆盖面可以逐类迭代（P2b 候选：SpCallArgment/CalledFunction、UserDefinedRefMethod、VariableStrArgTerm、三元）。
- **运算节点 (kind,op)+Reduce 重建**（否决直接序列化 OperatorMethod 实例）：实例无名字典且子类多态按操作数类型选择；Reduce 系是既有纯函数，重建即重选实现，类型校验（GetEraType 相等）兜底漂移。
- **内建 method 按类型全名**（Name 是 protected 读不到）：单例注册 ⇒ 类型↔实例一一对应；映射表以 methodDic 引用为会话键（兼容面变化自动重建）。
- **锚点后移的发现过程**（本轮最大教训）：Phase 2 首版锚点在 parse 后，三轮性能数据（setarg 恒 4.3s）+ dump/直方图/计数器三层插桩才定位"序列化时机早于 Argument 生成"。**先插桩量测再改架构**再次成立。
- **排除项**：`SpCallArgment`（CALL 2.3 万行，CalledFunction 含运行期 RetAddress/UserDefinedFunctionArgument）本轮不做（P2b）；label.Arg（setLabels 108-160ms 不值得）；`VariableStrArgTerm`/三元/`UserDefinedRefMethod`（低频）。

## 验证记录

- 构建 0 错误 [实测]；Core.Tests 429 全绿 [实测]。
- 桌面闭环多轮 [实测]：p2final-1(全量) vs p2final-4(命中)：state/effects/display **EQUAL**、errors 仅 trace sequence 序号差（消息逐条相等）；p2done 双跑 display 差异=停靠帧（retainedLines 13 vs 18，命中轮进度更快；此前四轮同对比全等）。写入轮 bundle v4 全条目成功；命中轮 `1803h/17m`（17 个运行期懒加载文件首次写）。
- **真机 [未验证]**：Phase 1+1.5+2 合并验收（新 APK 两连冷启动+进游戏）待用户执行。
- 环境障碍与打通：①`grep -c` 输出 0 时 exit 1 短路了 `&&` 链上的清缓存命令（一轮数据作废重跑）；②python heredoc 多次编辑同文件后 Edit 工具状态失配——改用 python 锚点替换。

## AI 表现复盘

- **有效做法**：性能不降时不猜架构，三层插桩（dump 恢复态/写侧直方图/调用计数器）逐层逼近——最后 calls/skipped 一组数字直接指出"锚点早于生成"的时序错误。
- **低效/返工**：①锚点位置错误历经三轮闭环才定位（~30 分钟），若首轮就画"数据流时序"（Argument 何时产生 vs 何时序列化）可省两轮；②临时插桩清理时 try 骨架残留炸构建一次；③restored 计数归零点 bug（每文件归零）先误读为"恢复未发生"。
- **险些踩的坑**：WriteArgument 首版用 Seek 回退法降级（会流错位），自审改内存试写。

## 教训 → 具体优化动作

1. (a) 本轮已执行：延迟类缓存必须先列"数据何时产生"时序表再定序列化锚点（本日志留档，Phase 3 JumpTo 同样适用——好在 JumpTo 也在 check 期生成，锚点已就位）。
2. (a) 本轮已执行：临时插桩（arg_types/dump）用后即删；常驻计数器（calls/skipped/restored）保留进 Mark 输出。
3. (b) P2b 遗留见下。

## 遗留登记

- **L1（P1）**：真机合并验收（Phase 1+1.5+2）。
- **L2（P2）**：P2b 覆盖 SpCallArgment（CalledFunction 序列化：label 引用重绑+UserDefinedFunctionArgument）→ 预计再省 setarg ~1s（28 万未短路 calls 的最大剩余块）。
- **L3（P2）**：Phase 3 JumpTo/SelectCaseJumpTable 行索引缓存（jump 1.5-4.8s 波动段）；锚点已在 check 后，天然适配。
- **L4（P3）**：bundle 偏移索引按需读（104→111MB 常驻缓解）；17 个懒加载首启 miss 的写侧提示。
- 无 P0。
