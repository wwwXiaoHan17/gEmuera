# ERB 解析缓存退役（用户裁定整体移除）

前置记录：`governance/evolution-log/2026-11-21-parse-cache-phase2.md`（含 5 个补遗）、`2026-11-21-parse-cache-phase1.md`、`2026-11-21-parse-cache-profiling.md`。本日志为该功能线的最终收尾。

## 任务目标与成果

用户裁定原话："这个缓存功能给不到理想的效果，并且会导致游玩出现问题"——据此把解析缓存（ErbParseCache 行骨架序列化 + ErbParseBundle 单 bundle 存储 + ErbParseTermCodec 表达式树编解码）从代码库**整体移除**并验证行为中性。

成果（全部可复核）：
- 删除 3 个缓存类：`scripts/Emuera/GameProc/ErbParseCache.cs`、`ErbParseBundle.cs`、`ErbParseTermCodec.cs`（git rm，含未跟踪 .uid 一并清理）。
- `ErbLoader.cs` 剥离全部缓存接线（-247 行）：命中路径块、pending 快照块、两个 Flush 锚点、`TryBuildCacheKey`/`EnsureCacheBundlePath`/`EnsureCacheErhSnapshotHash`/`CountRestoredArguments`/`SerializePendingCacheEntries`/`TryRestoreFromCache` 六个辅助方法、Mark 字符串缓存计数段；保留 prof* 剖析计时与 MarkLoad 分段。
- 8 个共享文件移除编解码专用访问器/字段：`LogicalLine.cs`（AssignmentDest/HasArgumentPrimitive/PeekRowArgs/SetRestoredArgs/RestoreRowArgs/PrivateVarDataCache）、`Term.cs`（CodecArgs）、`VariableTerm.cs`（CodecArguments）、`FunctionMethodTerm.cs`（CodecMethod/CodecArguments）、`StrForm.cs`（Codec 系 11 项）、`OperatorMethod.cs`（operatorIndex/BuildOperatorIndex/TryGetOperatorInfo/ReduceByKind）、`IdentifierDictionary.cs`（CodecMethodDic/CodecLookupVariable）、`VariableToken.cs`（CodecSubId）。
- `Process.ScriptProc.cs` 移除 TEMP-DIAG `stream_line.txt` 块（`Process.cs` 的 `pending_error.txt` 块已在上一轮移除）。
- 设计文档 `docs/plans/2026-11-21-parse-cache-design.md` 顶部加废弃声明。

净改动：**-386 行 / +3 行**（不含用户自己的 `export_presets.cfg` 改动，未触碰）。

## 关键决策与 Why

1. **移除而非再修补**：Phase 1→2b→P2c 五轮真机/桌面 bug 修复（HALFSIZE/FULLSIZE 表排序、SET 静态单例重绑、AssignOperator byte 截断、WordCollection.Pointer 漏序列化、空流崩溃/label NRE 级联）均收敛为"更复杂的守卫"，而审计确认缓存键未覆盖 `*.als` 别名 CSV、`VariableSize.CSV`、`emuera.config` 的 ICVariable/ICFunction——即**永远无法从键上根治**，只能靠失配回退兜底。收益面同时坍缩（条目守卫 + 懒加载快照 bug 使实测命中率 0h/1789m）。收益/风险比为负，退役是唯一理性选项。
2. **保留与缓存正交的资产**：MarkLoad 分段计时（`load_times.log`）、`prof*` 剖析计数（setarg calls/skipped/restored、nest/jump ticks）、原生宽度/保存编解码系统（`native/gemuera-native`、`NativeSaveCodec`——保存编解码 ≠ 解析缓存）、ERH 懒加载机制本身。退役只拆缓存链路。
3. **访问器删除边界靠 grep 证据**：`PeekArgumentPrimitive` 保留——`ErbLoader.cs:293` 的 `TryPreRegisterSnakeDynamicVariable`（SNAKE 动态变量预注册）是原有使用方；`HasArgumentPrimitive`/`AssignmentDest` 等删除前 grep 全仓确认仅缓存路径使用。
4. **验证策略**：桌面 legacy-runner 双轮（用户此任务线明确"电脑测"+ 自动化偏好），不做真机（用户无法碰设备，重导 APK 属遗留）。

## 验证记录

- 构建：`dotnet build gemuera-c#.csproj -nodeReuse:false -m:1` → 0 错误、4 警告（既有 CS8632/CS8600 nullable 告警，与本次无关），DLL 时间戳 2026-11-22 02:57 更新 [实测]。
- xUnit：`dotnet test tests/xUnitTest/GEmuera.Core.Tests` → 429 通过 / 0 失败 / 1 跳过（Symlink 守卫，历史基线同） [实测]。
- 桌面双轮：`tools/legacy-runner/Invoke-LegacyRunner.ps1`（snake profile→v24pure 引擎+pack.gemuera.snake 包，G10 假绿守卫通过）→ `D:\gemuera-reports\cache-retire-1` 与 `-2`；8 个语义文件（state/display/screenshots/hit-test/effects/errors/timeline/semantic-trace）SHA256 **逐字节一致**，errors 两轮均 0 条 [实测]。
- 行为中性（对缓存时代）：退役轮 vs 缓存时代最后验证轮 `p2c2-2` 对比 state/effects/errors/timeline/hit-test **5/5 哈希一致**（errors.json `1ee4f274789b…`） [实测]。
- 残留检查：全仓 grep 14 类符号（ErbParse*、Codec*、pendingCacheEntries、cacheHit 系等）仅剩 `PopAssignmentDestStr`（原有方法，子串误中） [静态]。
- 缓存目录：游戏副本 `cache/erbparse`（127MB）已删，两轮运行后**未再生成** [实测]。
- 临时配置已还原：user://config.toml（debug_model_zh_cn mirror/log_level）diff 与备份逐字节一致后备份才删除；仓库 `config.toml` 的临时 mirror 行已移除 [实测]。
- 真机/APK：**未验证**（遗留 P1）。

环境障碍与打通：G10 假绿守卫要求 stdout 出现 Info 级 `[LOAD] CompatPack enabled` 行；仓库 `config.toml` 加 `mirror_non_error_to_godot` 无效——诊断首条记录 `loaded_from=user://config.toml` 表明运行时读的是 `%APPDATA%/Godot/app_userdata/gemuera-c#/config.toml`，且 `debug_model_zh_cn.enabled=true` 时其 `log_level`/`mirror_to_godot` 优先。打通 = 临时改该文件 `log_level="info"`+`mirror_to_godot=true`，跑完还原。

## AI 表现复盘

有效：
- 退役前先全局 grep 出 14 类待清符号清单再逐文件精读删除，删除后一次构建通过、零编译往返。
- 动用户配置前先 `cp` 备份、还原后 `diff` 逐字节确认再删备份（`never-delete-user-config` 教训的正确执行）。

低效/返工：
- 上一会话 `git rm ... 2>/dev/null` 吞掉了失败（随后 `ls` 仍列出文件），本轮恢复时才发现文件未删成——**删除类操作不吞 stderr**。触发点：`git rm`+`ls | grep`；后果：1 轮重复操作；动作：删除命令不用 `2>/dev/null`。
- 摘要声称"编解码访问器已移除"，磁盘上其实未做（8 个文件本轮才真删）——**跨会话恢复一律以磁盘 grep 为准，不信摘要/记忆**。后果：多一轮全局 grep + 8 文件补删；动作：已执行（本轮开头）。
- G10 守卫排障走了 3 轮：先改仓库 config.toml（无效）才发现应读诊断首条的 `loaded_from` 字段。后果：2 次无效 runner 全跑（各约 2 分钟）；动作：排障先读诊断自检记录的元数据字段。
- 升级去向：`跨会话以磁盘为准` 已写入自动记忆（parse-cache 记忆清理 + 本次教训）；`删除不吞 stderr` 属通用 shell 素养，暂不升级（理由：单次代价低，已有记忆覆盖 PowerShell 5.1 陷阱类）。

## 遗留登记

- P1 真机/APK 验收：当前设备上的 APK 是坏配置版本构建的，退役后需重新导出并真机跑一轮——不在本轮：用户明确"没法碰电脑/手机，先暂停"，重导出待用户发起。
- P2 报告目录清理：`D:\gemuera-reports\cache-retire-1/2`、`eratw-cache-test`（游戏副本约 2GB）保留供后续对照，用户可随时删除——不在本轮：对照价值未过期。
- P2 三个既存未跟踪 `.uid`（`NativeSaveCodec.cs.uid`、`tools/native-width-check/Program.cs.uid`、`tools/save-codec-check/Program.cs.uid`）：属原生编解码任务线，非本任务范围，未入库。
