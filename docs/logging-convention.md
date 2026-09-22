# 日志内容层规范（Logging Content Convention）

> 状态：施行中（2026-09-04 立）。管道层（DiagnosticLogRouter → 内存 ring buffer + user:// 文件 sink
> + 导出打包）已稳定，本规范只约束**内容层**：消息文案、EventId、语言、文件落点、旁路禁令。
>
> **适用范围**：Godot 侧自有代码（`Scripts/` 除 `Scripts/Emuera/` 外、`Scripts/Diagnostics/`、
> `Scripts/GodotHost/`、`Scripts/Panels/`、`Scripts/LegacyRunner/` 等）。
> `Scripts/Emuera/`（解释器对齐区）内的日文系统行、`emuera.log` 命名与写入语义是
> snake/v24 语义对齐产物，**不适用本规范，原样保留**。

## 0. 管道纪律（既有，重申）

- 所有日志统一经 `GenericUtils.*`（或下述专用 Trace 入口）→ `DiagnosticLogRouter`
  开关/限流判断 → `DiagnosticLogSinks` 写入。关闭日志时不构造 message，热路径零分配。
- `Info/Warn/Debug` 带 `[Conditional("DEBUG")]`/`[Conditional("GEMUERA_DIAGNOSTIC_LOGS")]`，
  非诊断构建编译期剥离；`Error` 无条件编译但仍受运行时总闸门控制。
- `VerboseLogBuild=false`（Release/APK）时仅 Error 级且 `logging.enabled` 才输出。
- 例外：`DiagnosticLogExporter.WriteInfrastructureRecord`（CONFIG.SELF_CHECK / RETENTION.* /
  DIAGNOSTIC_PACKAGE.* 等低频基础设施事件）不带 `[Conditional]` 且不受等级门控，只受
  总开关+限流约束（启动自检在 Release 也需可见，量固定有界）；`level=none`（全关契约）
  时与普通记录一并拦截（2026-09-19 修补）。

## 1. 消息句式

**「组件: 对象 → 结果（关键值）」**，单行、可 grep。

好例：

```
Launcher: 游戏扫描完成 → 12 个条目（root=/storage/emulated/0/ERA）
Session: dialect profile applied → snake (game=eraAkumaMaid)
Audio: BGM 加载失败 → file=bgm01.ogg error=FileNotFoundException
```

坏例（禁止）：

```
"via subdirectory search: " + found          // 无组件、无结果语义、裸拼接
"..." + ex                                   // 裸异常串接，丢失异常类型
"==== 汇总 ===="                             // 多行横幅/装饰行不进日志
```

规则：

1. 关键值写 `k=v` 或括号内并列，路径经 `DiagnosticLogRouter.RedactPath`。
2. 异常必须带类型：`ex.GetType().Name + ": " + ex.Message`。
3. 不输出多行报告、横幅、装饰行；报告类工具输出按节拆成单行记录（见 §2 PerfTrace）。

## 2. EventId 命名空间

**派生前缀**（走 `GenericUtils.Info/Warn/Error/Debug` 自动生成，`<前缀>.<LEVEL>`）：

| 前缀 | EmueraLogCategory | 前缀 | EmueraLogCategory |
| --- | --- | --- | --- |
| `LOG.` | General | `LOAD.` | Load |
| `SPRITE.` | Sprite | `SAVE.` | Save |
| `AUDIO.` | Audio | `CONFIG.` | Config |
| `INPUT.` | Input | `PERF.` | Performance |
| `SCRIPT.` | Script | `TOUCH.` | Touch |
| `UI.` | UI | `PARSER.` | StatementRecognition |
| `FS.` | FileSystem | | |

**稳定前缀**（显式 eventId，专用入口；同一 eventId 拥有独立限流桶）：

| 入口 | 前缀 | 示例 |
| --- | --- | --- |
| `GenericUtils.TouchTrace` | `TOUCH.*` | `TOUCH.PAD_TAP` |
| `GenericUtils.InputTrace` | `INPUT.*` | `INPUT.VM.CLICK` |
| `GenericUtils.ImageTrace` | `IMAGE.*` | `IMAGE.ATLAS_HIT` |
| `GenericUtils.UiLayoutTrace` | `UI_LAYOUT.*` | `UI_LAYOUT.LINE_BUILD` |
| `GenericUtils.StatementTrace` | `PARSER.*` | `PARSER.LINE_CLASSIFY` |
| `GenericUtils.PerfTrace` | `PERF.*` | `PERF.BENCH.SUMMARY` |
| 字面量（历史存量） | `PERF.SAMPLE` / `PERF.DISPLAY_BRIDGE` / `PERF.CONSOLE_BRIDGE` / `BREADCRUMB.*` | |

规则：高频事件、需独立限流或需在面板按事件过滤的，必须用稳定入口；
新增稳定前缀先登记到本表再使用。

## 3. 语言政策

| 区域 | 政策 |
| --- | --- |
| 自有代码**新**文案 | 中文（VirtualMouse 手势日志为先例；读者是真机排障的中文用户） |
| 自有代码存量英文 | 保留过渡，随触碰顺手迁移；**禁止**为迁移单独开 PR |
| `Scripts/Emuera/` 对齐区 | 日文系统行原样保留，禁止翻译（上游契约） |
| 面向用户的操作说明 | 走 UI/诊断面板中文文案，不进日志 |

## 4. 文件落点清单

| 文件 | 写入者 | 触发 | 位置 | 状态 |
| --- | --- | --- | --- | --- |
| `emuera.log` | 对齐区 `OutputLog`（Process.SystemProc / EmueraConsole*）+ Program.cs 兼容默认 | ERB `OutputLog` 指令 / 兼容行为 | 游戏目录 | 保留（上游契约） |
| `emuera_startup_errors.log` | Program.cs | 启动期错误 | 游戏目录 | 保留（上游契约） |
| `gemuera_runtime_*.log` | DiagnosticLogSinks 文件 sink | `[logging] file_sink` 开启时持续写入，1 MiB 轮转 | `user://` | 保留（主结构化日志）；总量上限 `file_sink_max_files`（默认 8，新开文件时删最旧，2026-09-19） |
| `gemuera_{stamp}.log` | `GenericUtils.ExportDiagnosticLog` 默认路径 | 用户主动导出 | 默认 `game://`（`logging_export_directory` 可改） | **已收敛**（2026-09-19）：`diagnostic_retention` 默认开启，游戏目录选择时与诊断包导出前按 20 份/128 MiB 清理 `gemuera_*` 管理命名文件（含 auto 导出） |
| `launcher.cfg` | LauncherSettingsStore | 启动器设置 | `user://` | 非日志，顺带登记 |

导出包内格式（三种产物各守其格式，**不跨产物统一**）：

- 诊断日志报告头：`# Key=Value` 哈希风格（DiagnosticLogExporter.BuildDiagnosticLogText）。
- config 快照段：TOML 小写 `key = value`（会被 RuntimeTomlParser 读回，有意为之）。
- `emuera.log` 头：`Environment Information` 标题式（上游 OutputLog 样式，对齐区）。

## 5. 旁路禁令与例外

- 新代码**禁止**裸 `GD.Print` / `GD.PrintErr` / `GD.PushWarning` 做诊断输出；
  一律走 `GenericUtils.*` 或专用 Trace 入口。Error 级经路由后由 sink 自动
  `GD.PushError` 镜像到 Godot 控制台，无需直推。
- 允许直用 `GD.*` 的例外：
  1. `DiagnosticLogSinks.WriteToGodotConsole` —— 路由自身的 Godot 控制台渲染通道
     （transport，非旁路）。
  2. Godot 生命周期级致命输出（崩溃前最后手段），须注释说明为何不能走路由。

**存量直推 `GD.Push*` 台账**（2026-09-19 重新盘点，各有基础设施生命周期理由，暂不收口；触碰时再评估）：

| 位置 | 理由 |
| --- | --- |
| DiagnosticLogSinks.cs:395-401（`WriteToGodotConsole`） | 路由自身的 Godot 控制台渲染通道（transport，非旁路） |
| DiagnosticLogSinks.cs:223/240（文件 sink 打开失败/拒绝） | 日志基础设施自身的失败通道，走路由有递归/依赖倒置风险 |
| DiagnosticLogExporter.cs:158/169（BREADCRUMB 写入失败/拒绝） | 同上（基础设施失败通道） |
| RuntimeDiagnosticsConfig.cs:859（未知日志等级回退） | 配置解析期，路由尚未初始化 |
| GodotHost/PrototypeRuntimeNode.cs:338（原型命令关闭失败） | 关闭期，sink 可能已释放 |
| LegacyRunner/LegacyRunnerHost.cs:138/594/637/642（runner 启动失败/中止/UIQUEUE 统计/报告失败） | 无头诊断宿主自身的输出；:138/:642 为裸 `+ ex` 拼接（已知句式债务，触碰时顺手修） |

治理基线（2026-09-04 首次收口，2026-09-19 复盘）：`Scripts/` 解释器对齐区之外的裸
`GD.Print` 仅剩渲染通道 1 处 + LegacyRunnerHost UIQUEUE 统计 1 处（台账内）；
PerformanceBenchmark 17 处、VirtualMouse 9 处、EmueraThread 1 处已收口进路由。
