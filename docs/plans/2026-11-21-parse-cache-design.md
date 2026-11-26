# ERB 解析缓存（P0-C）设计方案

> **已废弃（2026-11-22）**：本设计的全部实现已从代码库移除（用户裁定"这个缓存功能给不到理想的效果，并且会导致游玩出现问题"）。裁決依据、正确性漏洞清单（缓存键未覆盖 `*.als`/`VariableSize.CSV`/`emuera.config` 输入）与验证证据见 `governance/evolution-log/2026-11-22-parse-cache-retirement.md`。本文仅作设计历史保留，勿按此实现。

状态：设计定稿，分期实施。前置数据：真机实测 erb 阶段 13.8s 占启动 75%，内部构成 parse 5237ms（行解析≈100%，IO≈0 因 Preload 内存命中）+ check 6579ms（setarg 3591 / jump 2830 / nest 126）。

## 目标与非目标

- 目标：文件未变更的二次启动，erb 阶段 13.8s → ≤2s（最终相），总启动 ≤8s。
- 非目标：首次启动加速（缓存构建成本另计，目标 <5% 额外）；不改变任何解析语义（缓存失配 = 全量重解析，永远安全）。

## 缓存键（任一失配 → 整文件回退全量解析）

`parserCacheVersion(递增常量) + relativePath + mtime + byteLength + CompatibilityPlan.CanonicalHash + ERH宏表哈希 + UseMacro/AllowFullSpace 等词法开关快照`

存储：`<游戏目录>/cache/erbparse/<sha256(键).bin>`，二进制，带 magic+version 头。

## 序列化面（勘察定案，40+ 类型）

| 组 | 类型 | 备注 |
|---|---|---|
| 行 | NullLine/InstructionLine/GotoLabelLine/FunctionLabelLine/InvalidLine/InvalidLabelLine | Invalid* 含 errMes |
| InstructionLine 字段 | FunctionIdentifier(→FunctionCode int + 名称，重建查 IdentifierDictionary)、argprimitive 文本、AssignOperator、assigndest(WordCollection)、LoopEnd/Step/Counter(VariableTerm→visitor)、JumpTo/JumpToEndCatch(→**行索引引用**)、IfCaseList/SelectCaseJumpTable/dataList/callList | argprimitive 存未 Pop 态 |
| FunctionLabelLine | 全部标志/长度字段、Arg(VariableTerm[])、Def(SingleTerm[])、privateVar(→UserDefinedVariableData 重放 AddPrivateVariable)、Index/FileIndex/Depth | wc 词集不缓存（PopRowArgs 一次性，重建自参数原语？**待验证**，见开放问题） |
| Argument 系 20+ 类 | Expressions/Void/Error/Expression/SpInputs/ExpressionArray/SpPrintShape/SpPrintImg/SpPrintV/SpTimes/SpBar/SpSwapChara/SpSwapVar/SpVarsize/SpSaveData/SpTInputs/... | 每类字段 visitor 显式编写 |
| IOperandTerm 系 15+ 类 | Null/Single(Int64/str/double 三态)/StrFormTerm(→StrForm 槽位图)/VariadicArg/VariableStrArg/FunctionMethodTerm/Super&UserDefinedMethodTerm(→label 引用)/OperatorMethodTerm/ConditionalTerm/IntValue/Double/VariableTerm(→VariableToken 引用)/FixedVariableTerm | VariableToken 不序列化，存变量名+索引参数，加载期经 IdentifierDictionary.GetVariableToken 重绑 |
| 词法 | Word/WordCollection | Word=字符串+类型枚举 |
| 引用 | 行间引用统一 **文件内行索引**（JumpTo 只在同函数内）；UserDefinedMethodTerm→label 名（跨文件，经 LabelDictionary 重绑） | |

## 分期（每期独立 PR、独立收益、可回退）

- **Phase 1**：行序列化（不含 Argument——置 null 由加载期 setArgument 重付）+ 键/存储/失配回退。收益：erb-parse 5.2s；erb 13.8→8.6s。
- **Phase 2**：IOperandTerm visitor + 20+ Argument 类 → 省 setarg 3.6s；erb → ~5s。
- **Phase 3**：JumpTo/nest/Depth/IfCaseList/SelectCaseJumpTable 行索引缓存 → 省 2.9s；erb → ~2.1s。
- Phase 2/3 期间缓存版本递增，旧缓存自动失效。

## 验证策略（每期相同）

1. 对拍工具 `tools/parse-cache-check`：`--parity <游戏dll> <ERB目录>`——全量解析 vs 缓存加载，逐行对比（行类型/文本/label 字段/Argument 结构哈希/JumpTo 索引）。
2. `FirstPartyPackEquivalenceTests` 模式：v24pure 基线加载行为不变。
3. 缓存损坏注入：坏 magic/截断文件 → 必须回退全量（不 crash）。

## 开放问题（实施 Phase 1 时定）

- FunctionLabelLine.wc（PopRowArgs 一次性词集）是否必须缓存：setLabelsArg 消费它，若重建缺失会导致 label 参数缺失 → 需缓存 WordCollection（已列入面）。
- 多文件 label 引用顺序（FileIndex）在缓存下的重放一致性。

## 性能预期汇总

| 阶段 | erb | 总启动（IO 冷态 18.4s 口径） |
|---|---|---|
| 现状 | 13.8s | 18.4s |
| Phase 1 | 8.6s | ~13s |
| Phase 2 | ~5s | ~9.5s |
| Phase 3 | ~2.1s | ~6.5s |
