# 2026-11-21 P0-C Phase 1：ERB 解析缓存（行骨架序列化）落地

承接 [2026-11-21-parse-cache-profiling.md](2026-11-21-parse-cache-profiling.md)（构成剖析定案方案 A）与设计文档 [docs/plans/2026-11-21-parse-cache-design.md](docs/plans/2026-11-21-parse-cache-design.md)。工作分支：主仓 `ai/ui-scale-setting`（单分支工作流）。

## 任务目标与成果

Phase 1 落地：**文件未变更的二次启动跳过 erb-parse 全阶段**（预期省 ~5.2s，erb 13.8→8.6s）。范围 = 行骨架序列化（6 种行类型 + WordCollection 8 子类 + UserDefinedVariableData 重放），Argument/check 产物由既有 checkScript 重建（Phase 2/3 消除）。

- `scripts/Emuera/GameProc/ErbParseCache.cs`（新，~430 行）：行序列 Write/Read（类型分派 tag + kind byte 双层消除 Symbol 歧义）、Word 集合（StrFormWord/TermWord 不支持 → 抛 CacheFormatException → 文件弃缓存）、UserDefinedVariableData 全字段重放、缓存键 SHA-256（解析器版本+相对路径+mtime+长度+方言 CanonicalHash+ERH 宏表快照）、magic+版本头防半写/漂移。
- `LogicalLine.cs`（+11 行）：`AssignmentDest`/`PeekRowArgs` 只读访问器（不消费）、`RestoreRowArgs` 回填、`PrivateVarDataCache` 记录（AddPrivateVariable 成功路径追加 data——token 不保留 data，重放必需）。
- `ErbLoader.cs`：`TryBuildCachePath`（AnalysisMode/键缺失 → null 跳过）、`TryRestoreFromCache`（重建行链 + AddLabel/AddInvalidLabel/AddLabelDollar/AddPrivateVariable 重放，ParentLabelLine 串链）、写入点（tmp 隔离 + 全吞异常——**缓存失败绝不影响解析结果**）、hit/miss 计数进 `erb-parse(cache=Nh/Nm)` 子段名。

## 关键决策与 Why

- **Word 双层判别**（kind byte 由 C# 类型 switch 决定，不复用 Word.Type 字符）：SymbolWord 的 Type 就是其字符本身，与其它子类字符存在碰撞空间；类型分派彻底消除歧义。
- **privateVar 重放**：token 不保留 data（构造即拆字段），故在 `AddPrivateVariable` 成功路径记录 data 列表；重放时逐条 AddPrivateVariable——顺序一致则重名/hasPrivDynamicVar 行为一致。重复声明 warning 不重放（日志面差异，登记）。
- **写入全吞异常**：解析途中 WriteLines 抛（遇 StrFormWord 等）若不吞会被 loadErb 外层 catch 当解析失败——tmp 隔离 + catch-all 保证"缓存永远只是加速器"。
- **Phase 1 不做**：Argument visitor（Phase 2，setarg 3.6s 重付）、JumpTo/nest/Depth 行索引（Phase 3，2.9s 重付）、缓存目录容量清理（键含内容哈希，残留无害）。

## 验证记录

- 构建 0 错误 [实测]；Core.Tests 429 + Facade 79 全绿 [实测]。
- **真机两连冷启动验收 [未验证-需用户执行]**：第一次启动 `erb-parse(... cache=0h/Nm)`（全量+写缓存），第二次 `cache=Nh/0m`（全命中，erb-parse 段应降至 ~0.2s 级）且游戏进入标题/读档/剧情正常。命中即证明往返等价可用；若第二次启动异常或 label 缺失，删除 `<游戏目录>/cache/erbparse/` 即回退全量（不损失功能）。
- 对拍工具（全量 vs 缓存进程内逐行对比）未建 [登记 Phase 2 前置]：进程内对比需完整游戏环境，暂以真机两连跑 + 用户功能验证替代。
- 环境障碍与打通：无（git add 大小写教训已应用——本轮全部小写路径）。

## AI 表现复盘

- **有效做法**：写序列化器前逐类核实成员名（首轮编译暴露 5 处 `Code`/`Int`/`Float`/`Str` 假设错误——Word 子类属性名不统一），一次修正全绿；比"写完再对拍"返工成本低。
- **险些踩坑（未踩）**：WriteLines 途中抛异常若被外层 catch 吞会污染解析结果——写入点用独立 catch-all 隔离，缓存失败与解析成功解耦。
- **低效**：无显著返工。

## 教训 → 具体优化动作

1. (a) 本轮已执行：缓存类"失败即回退全量"的兜底结构（tmp + catch-all + magic/version 头）。
2. (a) 本轮已执行：git add 小写路径纪律贯彻（零漏文件）。

### 补遗（2026-11-21）：lazy loading × 解析缓存交互补课——发现并修复快照实例化缺口

用户质询"lazy loading 看过了吗"后的补课结论：

- **看过的**：LoadErbFilesAsync 的 lazy 分支（LoadLazyLoadingTable/isLazyLoading 跳过/tempFunctionLabels 立即 setLabelsArg+check/SaveLazyLoadingList·SavePartialLazyLoadingList）、loadErb 的 isLazyLoading 参数、运行期入口 TryLazyLoadErb → LoadErbsAsync（经勘察代理报告）。
- **补课后发现真实缺口 [实测]**：运行期懒加载经 `new ErbLoader(...)` 新实例进入 loadErb，而 `cacheErhSnapshotHash` 是实例字段且只在 LoadErbFilesAsync 赋值 → **运行期懒加载文件永远不走缓存**（TryBuildCachePath 恒 null）。无回归（回退全量=原行为），但零收益。已修：快照改进程级静态惰性计算（`EnsureCacheErhSnapshotHash`，枚举失败置 null 禁用缓存）。
- **交互审查通过项 [静态]**：①运行期懒加载 labelDic.Initialized=true 与原路径一致，AddLabel 行为等价；②LazyStatus（Loaded/UpdateTable/BuildTable/NoLazy）只决定启动期文件集合，缓存键与状态无耦合；③UpdateTable 的变更文件启动期强制加载 → mtime 变 → 缓存键变 → 回退全量+写新缓存，自洽；④RemoveLazyLoadingEntriesForFiles 防重复加载不与缓存冲突；⑤重复 $label 的 warning 在缓存重放路径不重发（日志面差异，已登记）。
- 用户两连冷启动数据显示 `cache=0h/1789m` 双零：除目录缺失 bug 外，此缺口使运行期懒加载文件（启动期跳过的部分）即便目录修好也不落缓存——本次修复后需要再测一轮。

### 补遗（2026-11-21）：真机首测抓到阻断性 bug——SET 静态单例 + 读取路径未隔离

用户真机首测（eraTW Snake，2336 files indexed）加载失败：`CacheFormatException: function identifier drifted: SET` 直接炸到 loadErb 外层 catch（"予期しないエラー"→整个加载失败）。两个根因一起修：

1. **读取路径未隔离（实现缺陷，违反设计哲学）**：设计要求"任何缓存异常回退全量"，实现里 TryRestoreFromCache 的 CacheFormatException 未捕获、直接传播 → 整个加载失败；且中途抛出时 labelDic 已部分污染（回退会重复注册）。修复：**反序列化（纯新对象、零副作用）与注册重放分离**——ReadLines 失败置 null 回退全量；注册重放只做字典/链表操作（不抛格式异常）。
2. **SET 标识符重建错误（格式契约盲点）**：赋值行（`A=1`）的 SET FunctionIdentifier 是**静态单例直引**（`FunctionIdentifier.cs:607 setFunc`，公共访问器 `SETFunction`），**不在字典查找链里**——解析端直引单例、缓存重建却按名查 `GetFunctionIdentifier("SET")` → snake 兼容字典链查不到 → "drifted"。修复：`funcCode==FunctionCode.SET` 特判走 `SETFunction`。通用性：`CreateMethodStatement` 产物经 routedInstructions 进字典查找链（按名可重建）✓。
3. **附带确认**：用户设备上缓存条目从未成功写入（0 命中根因=目录缺失），故无旧格式污染；SET 行的序列化字节本身无错，读取特判后旧/新条目均兼容。

教训：**外部类型的重建途径必须以"解析端实际构造方式"为准**（字典查找只是构造途径之一，静态单例/动态构造不走字典）；**隔离边界必须在实现层兑现**（设计写了"回退"不等于代码包了 try）。升级去向：暂不升级（缓存类实现经验，同类再犯再进 AGENTS.md）。

### 补遗 2（2026-11-21）：真机第二轮——SET 行 AssignOperator (byte) 截断（3 个修复后命中路径已工作）

用户第二轮真机：加载成功（隔离修复生效 ✓、懒加载文件命中路径已工作——TITLE.ERB 经运行期懒加载恢复执行），但 `TITLE.ERB 13 行 RESULT = GLOBAL` 报"数値型と数値型の演算に二項演算子''は適用できません"。

- **根因 [实测+代码实锤]**：`ErbParseCache` 用 `(byte)` 序列化 AssignOperator，而 `OperatorCode.Assignment = 0x0100+0xFE = 510`（枚举值含 0x10000 级标志位，远超 byte）→ 截断为 254 无效值 → SET 执行"空二项演算子"。**所有赋值行必中**。
- 修复 [实测构建+测试]：AssignOperator 改 int 序列化；`ParserCacheVersion` 1→2、条目 entryVersion 1→2（设备上首轮写入的坏条目自动失效回退全量）；构建 0 错误、Core.Tests 429 全绿。
- 附带确认 [实测]：命中路径本身已工作（TITLE.ERB 是运行期懒加载文件，经静态快照修复后命中恢复执行——静态哈希修复生效的实证）。
- **教训 [升级去向：暂不升级]**：枚举序列化禁止窄化 cast（值域先查定义）；本轮靠用户真机报错文本（"二項演算子''"）+ 枚举定义比对定位，未走弯路。

## 遗留登记

- **L1（P1）**：真机两连冷启动验收（hit 计数 + 功能验证）——用户执行。
- **L2（P2）**：Phase 2 前建 `tools/parse-cache-check --parity` 进程内对拍工具。
- **L3（P3）**：缓存目录容量清理（LRU 或全清重建）；重复 $label warning 重放策略。
- 无 P0。
