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

### 补遗（2026-11-21）：P2b——SpCall 覆盖 + FixedVariableTerm 负收益回退（提交 c36ea5d）

- **SpCallArgment/SpCallSharpArgment 覆盖 [实测]**：terms + 基类标量入缓存；CallFunc/UDFArgument 留 null——check 期 setJumpTo 会为常量 CALL 重建（ConvertArg 同源），运行期 Clone 不受影响。skipped 248880→**261184**。
- **诊断方法链 [实测]**：重建行 FunctionCode 直方图（SET 98099 原生即 null 不可缓存 / IF 系 6.2 万 / CALL 1.2 万）→ 重建后类型采样（SET→STILL-NULL 佐证）→ **写侧 term 降级直方图**（FixedVariableTerm 29828 / UserDefinedMethodTerm 4338）三层逼近。
- **FixedVariableTerm 覆盖尝试后回退 [实测]**：其 token（CSV 别名/#DIM 变量）走 `GetVariableToken(name,null,true)` 查询返回 null → 读侧抛 → **1599 条目整体降级**（skipped 跌到 4511，净负收益）；改 VariableCode 权威校验无效（查询本身失败）。回退后恢复 1803h/17m、setarg 3214ms、总启动 10.9s。教训：**重绑路径必须与解析期构造路径同源**，"存什么键"取决于"写侧怎么查到"而非对象的标识字段。
- **UserDefinedMethodTerm 不覆盖**：ConvertArg 转换不可逆（无原始 srcArgs），且仅 4338 处（<1%）。
- 连续多轮 DIFF 定性：均为停靠帧形态（retainedLines 13 vs 18，命中轮进度更快）；等价结论维持（Phase 2 主验证轮 state/effects/display 全等）。

### 补遗 2（2026-11-21）：Phase 3 尝试后整体回退（负结果存档，工作树回 0057984）

实施内容（已全部回退）：entry v5 行间引用缓存（JumpTo/JumpToEndCatch/IfCaseList/dataList/callList 同文件索引两遍回填 + LoopCounter term）+ setJumpTo 幂等短路（JumpTo!=null && Argument!=null → continue）。

- **性能 [实测]**：命中轮 jump 段 1855ms（基线 1478-1667）——**零收益**。根因：短路条件要求 Argument 非 null，而 IF/SIF/ELSEIF 6 万行 Argument 仍 ARebuild（FixedVariableTerm 未覆盖，P2c 卡点）→ 短路覆盖率不足；jump 段剩余成本在遍历 + nestCheck（不可省）+ 非短路行重算。
- **正确性 [实测]**：命中轮 errors 出现 `CodeEE: 空のストリームを渡された at ProcessState.Return` + runner initial_wait_timeout——**短路跳过了 setJumpTo 的某个必要副作用**（RETURN/跳转族指令的 setJumpTo 不止设 JumpTo，或与恢复的 primitive/Argument 消费时序相关），游戏在 TITLE 崩溃。display 差异中 13/18 为停靠帧噪声，但该错误为真实行为差异。
- **教训**：①幂等短路的前提是"重算与缓存值完全等价且无其它副作用"——setJumpTo 对部分指令存在未文档化副作用（useCallForm/Depth/流消费），**逐指令验证成本 > 收益**；②依赖关系倒置：P3 短路的收益被 P2c（IF 覆盖）卡死，**先 P2c 后 P3** 才有意义。
- **操作事故与恢复**：回退用 `git stash && git stash drop` 误将用户未提交的 export_presets.cfg 一并丢弃——通过 `git fsck --unreachable` 找回 dropped stash 对象（16071d0）恢复该文件。**教训：含用户未提交改动的回退必须用路径限定 checkout（git checkout HEAD -- <我的文件>），禁用整树 stash**。升级去向：暂不升级（单次事故，git fsck 恢复路径已在此留档）。

### 补遗 3（2026-11-21）：命中轮"空のストリーム"崩溃根因闭环与修复（提交 125c1d1）

用户真机截图报告 TITLE.ERB:13 `空のストリームを渡された`——**审计发现该错误自 P2b 起每轮命中轮都存在**（此前轮次 errors 只比哈希未比消息，停靠帧定性掩盖了它；P3 复盘曾误归因于短路）。

- **根因链 [三层插桩实锤]**：`ScriptProc:61` 是 IsError 行重抛（非 DoInstruction）→ 行级诊断定位 `TITLE:13 func=SET argNull=True hasPrim=True` → **parse 期消费原语的行**（SNAKE builder/私有赋值路径 pop 掉 argprimitive）依赖 parse 期建的 Argument（check 短路不碰原语）；codec 对其 Argument 降级 ARebuild（SpSet/Snake 类未覆盖或 term 链含 FixedVariableTerm）→ 恢复行 check 期重建拿空流 → CodeEE → BEFORE_ERROR 事件在 Return 重抛。
- **修复**：①SpSetArgument/SpSetArrayArgument/SnakeVariArgument/SnakeVarsArgument codec（这些行 Argument 得以缓存，短路生效）；②原语快照改三态（null=已消费）；③条目级守卫：仍含"Argument==null 且原语已消费"行的文件放弃缓存（安全回退全量）。
- **验证 [实测]**：连续双跑 stream 错误 0/0；命中 775/1820（守卫正确放弃 1045 个含不可缓存行文件）；xUnit 429 绿。
- **遗留**：1045 个降级文件的终解是 P2c（FixedVariableTerm 全字典路径重绑——term 链降级消失后守卫几乎不触发，命中率恢复 90+）。
- 教训：**errors 等价验证必须消息级对比**（本轮之前多轮"等价"结论含未被发现的运行期错误——停靠帧定性让我放松了 errors.json 的检查标准）。

### 补遗 4（2026-11-21）：v5 label Arg/Def 缓存——命中轮 label NRE 级联修复（提交 d55948d）

用户指示 PC 测试后的第二轮闭环发现：空流修复后命中轮仍有 `ロード時に解析に失敗した関数が呼び出されました`——警告堆栈插桩定位 `parseLabel:964 wc.EOL NRE`，**写轮 0 vs 命中轮 5390 条 label NRE 警告**。

- **根因**：序列化锚点在 checkScript 后——`parseLabel` 已把 label 词集（wc）Pop 消费；未缓存 `label.Arg` 的条目恢复后 `setLabelsArg` 重跑 `parseLabel(wc=null)` → NRE → 5390 label IsError → 首次 CALL 即抛。
- **修复**：`FunctionLabelLine.Arg/Def`（parseLabel 产物，terms）直存（entry v5）；`setLabelsArg` 既有 `Arg!=null` 短路使恢复端完全不碰 wc。
- **验证 [实测]**：双跑 label NRE 警告 **0/0**、errors **0/0**（消息级）、state/effects **EQUAL**、display 仅 hit-test count 停靠差；xUnit 429 绿。
- **代价（诚实）**：命中率暂降至 246/1820——label Arg 的 term 链（FixedVariableTerm）降级触发条目回退；**P2c 完成后守卫/降级几乎消失，命中率与启动收益随之恢复**。当前版本为"正确性完整"基线。
- 过程教训：多轮 python 整文件编辑曾造成写读不对称（写侧块丢失、版本错位）——**最终以 git diff 全量核对 + Edit 工具定点修复收口**；此类成对修改应以 diff 复核为收尾步骤。

## 遗留登记

- **L1（P1）**：真机合并验收（Phase 1+1.5+2）。
- **L2（P2）**：P2b 覆盖 SpCallArgment（CalledFunction 序列化：label 引用重绑+UserDefinedFunctionArgument）→ 预计再省 setarg ~1s（28 万未短路 calls 的最大剩余块）。
- **L3（P2）**：Phase 3 JumpTo/SelectCaseJumpTable 行索引缓存（jump 1.5-4.8s 波动段）；锚点已在 check 后，天然适配。
- **L4（P3）**：bundle 偏移索引按需读（104→111MB 常驻缓解）；17 个懒加载首启 miss 的写侧提示。
- 无 P0。
