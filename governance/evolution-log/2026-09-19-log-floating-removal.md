# 2026-09-19 日志悬浮窗移除 + 三 Agent 企业级审计 + 日志默认开启

## 任务目标与成果

一句话：整体下线游戏内日志悬浮窗（悬浮球/悬浮弹窗/RuntimeDiagnosticsPanel 全链），
三份同提示词审计（73/73/76，未达 85 线）驱动修复全部收敛的 P0/P1，并把日志系统默认开启
（精简配置缺 `enabled` 键回退 true、代码默认等级对齐模板 warn）。

- 删除：`RuntimeDiagnosticsPanel.cs`（Window 宿主 + FloatingDiagnosticsHost 悬浮球 + 共享内容）、
  `RuntimeDiagnosticsPanel.Options.cs`、`assets/scenes/RuntimeDiagnosticsPanel.tscn`（含 .uid）。
- 配置链清理：`RuntimePanelEnabled`/`QuickDebugRuntimePanel`/`[debug.runtime_panel]` 五键/
  `[logging] panel_visible`/`[quick_debug] runtime_panel` 全部停止读写；Writer 的
  `DiagnosticsSections` **保留** `debug.runtime_panel` 条目（保存时清出旧用户文件里的废弃 section）。
- 审计修复（均带 2026-09-19 注释与来源标注）：
  - F1(P0×3源)：user:// 轮转文件总量上限 `file_sink_max_files`（默认 8，新开文件时删最旧）。
  - F2(P0×2源)：`DiagnosticLogSinks.Reload` 刷新 `_config` + 关→开补建 ring（此前热重载开启后全部静默丢弃）。
  - F3(×3源)：`WriteInfrastructureRecord` 尊重 `level=none` 全关契约。
  - F4(×3源)：删除死旋钮 `path_mode`/`hash_sensitive_text`（`replace_newlines`/`normalize_paths` 实有消费，保留）。
  - F5(×3源)：`diagnostic_retention` 默认开启（游戏目录选择时按 20 份/128MiB 清 `gemuera_*`）。
  - F6/F7/F9(×3源)：ring 容量上限 100k 钳制、`max_message_chars` 死键激活（夹 [256,65536]）、
    `FormatForGodot` Message 换行转义。
  - F8：logging-convention.md §0 基础设施记录例外 + §4 落点收敛状态 + §5 旁路台账重盘（行号已核实）。
- 验证：构建 0 警告 0 错误；diagnostics-config-smoke（含新断言）+ Surface/Runtime/CoreContract 三冒烟全绿；
  first_window.tscn / main.tscn 无头探针引导干净（仅既有锚点 WARNING）；FirstWindow.cs sha256 已重钉。

## 关键决策与 Why

1. **悬浮窗内容类随宿主一起删而非保留**：`RuntimeDiagnosticsPanelContent` 仅被悬浮宿主实例化，
   启动器诊断页有自己的独立轻量 UI（FirstWindow.DiagnosticsSettings.cs），保留即死代码。
2. **"日志默认开启"落点 = 精简格式回退 + 等级对齐，不动 file_sink**：代码默认 `LoggingEnabled=true`
   本就存在、APK 内置 res://config.toml 也写 true，真正的缺口是"精简配置缺 enabled 键回退 false"；
   file_sink 保持 opt-in（P0 轮转治理修复前更不能默认开）。等级 `error→warn` 是让代码默认与模板
   注释"默认 warn"的文档契约一致。
3. **三 Agent 审计的收敛甄别法**：3 源共证→直接修；2 源（热重载丢记录）→先读源码核实机制再修；
   单源不修只记录。本次 2 源项核实为真（`_config` 仅 Initialize 赋值），机制与两 Agent 描述一致。
4. **F4 死旋钮删而不实现**：`path_mode=basename_and_root` 若真实现会把所有路径日志削成
   root+basename，损失真机排障价值；删掉假承诺比实现一个伤害诊断力的真语义更符合企业级诚实原则。

## AI 表现复盘

- 有效：审计 Agent 全部在后台跑，主会话并行完成移除编辑，总耗时约等于单线程最长的分支；
  三个 Agent 都主动交叉验证了枚举基数、编译期剥离、导出过滤器等横切事实，报告可直接用于修复定位。
- 低效：第一次构建因 `out i` 在声明前使用而红（Loader 惯用 `out bool b` 先声明、`out int i` 后声明，
  插入键时应逐行核对既有变量声明顺序）；前期误把 `replace_newlines`/`normalize_paths` 当死旋钮，
  grep 消费方后才修正范围——审计结论入修复前必须本地复核（本次避免了过度删除）。

## 教训 → 具体优化动作

- 审计驱动的修复清单要先做"收敛度分级"（3源/2源/1源），1 源项默认不修防止幻觉污染；
  本次 LegacyRunnerHost 裸 `+ ex` 拼接等单源债务只进台账不进代码。
- 删除功能时配置键要分三类处理：停止读取（loader）、停止写出（writer）、
  但清理集合（DiagnosticsSections）要保留旧键以清扫存量用户文件。
- 探针噪声过滤表：Inputpad/Scalepad/RefitLauncherToViewport 的锚点 WARNING 是存量噪声，
  grep error 时先排除，避免误判回归。
