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

## 遗留登记

- **L1（P1）**：真机两连冷启动验收（hit 计数 + 功能验证）——用户执行。
- **L2（P2）**：Phase 2 前建 `tools/parse-cache-check --parity` 进程内对拍工具。
- **L3（P3）**：缓存目录容量清理（LRU 或全清重建）；重复 $label warning 重放策略。
- 无 P0。
