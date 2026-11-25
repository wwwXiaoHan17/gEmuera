# 遗留台账（Backlog）

跨任务遗留事项的**唯一汇总视图**。evolution-log 回答「当时为什么没做」，本台账回答「现在还欠什么」——对接、验收、开新任务前先读这里，不要靠翻历史日志考古。

## 使用规则（对齐 AGENTS.md「Agent 自我进化」节）

- 任务收尾与 evolution-log 同一时点，把「遗留登记」中**仍未关闭**的条目同步到这里；evolution-log 保持原样（只增不改），台账是活文档。
- 关闭条目时：状态改 `closed`，行内给验收证据链接（`文件:行` / 测试名 / 日志段），**不删除条目**。
- 条目被推翻 → 状态改 `obsolete` 并链接推翻它的日志段，不删除。
- 每条必须有：优先级（P0 阻塞 / P1 重要 / P2 建议）、状态（open / closed / obsolete）、来源（`evolution-log/<文件>#<段>` + 日期）、触发条件（什么情况下必须做）。
- 细节写来源日志，台账条目只保留「欠什么 + 怎么验收 + 去哪查」，避免双源漂移。

## 当前遗留

### 快捷面板 / 屏幕方向（2026-11-22 会话）

- [P1][closed] **真机 APK 验收**（2026-11-22 全部 UI 改动的最终关口）——清单：①内嵌面板右下角锚定（横屏/竖屏/手动旋转「仅本次」「永久」各态）；②翻转开关即时换边右下↔左下、内容不镜像、宽度调节条在内侧；③悬浮浮层拖动条 1:1 跟手、拨翻转立即镜像、隐藏→重开回目标角、跨方向旋转回目标角；④桌面悬浮窗翻转时窗口水平镜像、默认位换边；⑤启动后立即转手机应即时跟随（原 ~1s 级延迟应消失）。⑥启动器「兼容」独立页签：竖屏手机上侧栏 4 按钮排版正常、兼容包列表触控滚动、无滚动条遮挡（2026-11-22 新增页签）。⑦系统菜单「面板回位」图标（rehome.svg，紧跟「快捷」开关）：渲染正常、悬浮模式下可用/非悬浮置灰、点击回当前方向目标角；转屏后面板应自动落新方向目标角、关闭即回角（2026-11-22 扩展任务 6）。触发条件：下次出 APK 必测；任一红项按 `evolution-log/2026-11-22-quick-panel-pin-bottom-right.md` 对应扩展段回修。来源：同文件 扩展任务 1/2/3 验证记录的 [未验证] 项。关闭于 2026-11-23：用户确认「测试过了，没问题，有问题后续再说」（①–⑦ 逐项清单未由我方复核；记录见 `evolution-log/2026-11-16-screen-rotation-follow.md` 补遗 26）。
- [P2][closed] **`QuickFloatingX/Y` 配置键**——2026-11-22 扩展任务 6 已彻底移除读写（用户澄清语义后位置持久化整体退役）；用户旧配置文件中的遗留键不删除、仅无消费方。来源：扩展任务 3 遗留段 → 关闭于扩展任务 6。
- [P2][open] **设备 180° 倒持（Reverse 方向）不支持**——平台硬事实：Godot fullUser + Java FULL_SENSOR 链路下 Reverse 方向不生效，倒持手机时面板/画面在物理另一角。触发条件：用户明确提出倒持可用性诉求时立项（动 Android Java 方向链路，独立任务）。来源：扩展任务 1 遗留段 + memory `android-orientation-fullscreen`。
- [P2][open] **桌面悬浮窗位置记忆与移动端新语义不一致**——桌面原生窗仍保留跨开合位置记忆（OS 标题栏拖动，调试场景）。触发条件：用户要求两端行为一致时统一；否则维持。来源：扩展任务 3 遗留段。
- [P2][open] **历史日志 open 遗留未迁移**——如 `2026-11-16-screen-rotation-follow.md` 补遗 12 的两条观察（竖屏菜单栏刘海避让存疑、菜单 toggle 与栏体未对齐）等更早日志的 open 项。触发条件：下次触达对应功能域时顺手迁入本台账；不主动批量考古。来源：该文件补遗 12。

### 界面缩放 / 沉浸全屏 / 方向锁定旋转 / 字号排版缩放（分支 `ai/ui-scale-setting`，2026-11-16 → 11-23）

来源日志三份：`evolution-log/2026-11-16-ui-scale-setting.md`、`evolution-log/2026-11-16-android-fullscreen.md`、`evolution-log/2026-11-16-screen-rotation-follow.md`（字号排版缩放记在第三份的补遗 17–26）。本段把三份日志 §6/§7 与补遗里的 open 项一次性迁入；「设备 180° 倒持不支持」已由上方「快捷面板 / 屏幕方向」段覆盖，不重复登记。

- [P1][closed] **字号排版缩放的 5 个修复提交已真机验收、进入 dev**——`2774415` / `9233e97` / `750f60a` / `64989cc` / `bea08b7`（字号排版）与另一会话的 `b3d4df9`（悬浮面板）经 PR #11 合并（https://github.com/524122/gEmuera/pull/11 ，2026-11-23 18:10，合并提交 `a6d9b24`，合并时 head `5b57645`）；验收证据=`git log origin/dev..5b57645` 为空。悬浮面板提交的真机验收见上方「快捷面板 / 屏幕方向」段（2026-11-23 用户确认通过，已关闭）。来源：`evolution-log/2026-11-16-screen-rotation-follow.md` 补遗 23–25（2026-11-23）。
- [P1][open] **真机核对界面缩放 75% / 150% / 200% 三档**——用户真机测试未显式覆盖全部档位。验收=启动器标题栏「界面」选择器、游戏内设置窗/输入板/缩放板在各档位下无溢出无错位，且游戏画面物理大小不随界面缩放变化。触发条件：下次在真机装 APK 时顺带逐档截图核对。来源：`evolution-log/2026-11-16-ui-scale-setting.md#6-遗留登记`（2026-11-16）。
- [P2][open] **极小窗口（高度 <600px）+ 高倍率界面缩放下 OptionWindow 弹窗可用面积紧张**——桌面 648p 基准窗口已验证可滚动兜底。验收=540p 高度 + 200% 缩放下设置窗能滚动到全部行。触发条件：用户反馈小屏设备上设置窗显示不全时。来源：同上 §6。
- [P2][open] **桌面 `settings.cfg` 分辨率档位是自动推导值（540p）**——2026-11-16 测试事故丢失原值、不可考，现值 `ResolutionIndex=4` / `ResolutionHeight=540`。非代码缺陷：用户想要别的档位时在启动器分辨率选择器里重选一次即可。触发条件：用户发现桌面窗口分辨率不对时提示一句，无需改代码。来源：同上 §6。
- [P1][open] **真机核对沉浸全屏的刘海/切槽避让**——状态栏/导航栏隐藏在用户真机截图中只是间接可见（菜单栏贴到屏幕最上沿），刘海避让存疑且无显式确认。验收=刘海机上游戏内竖排菜单栏不被切槽压住（与下方「竖屏菜单栏刘海避让存疑」合并处理）。触发条件：用户反馈菜单栏被刘海遮住，或下次出 APK 做验收时。来源：`evolution-log/2026-11-16-android-fullscreen.md#6-遗留登记` + `evolution-log/2026-11-16-screen-rotation-follow.md` 补遗 12 遗留观察 1（2026-11-16）。
- [P2][open] **Godot 4.7 Android 模板库非 16KB 页对齐**——引擎/模板侧问题，16KB 页镜像的模拟器会弹兼容提示；用户机型可正常安装运行。触发条件：升级 Godot 版本，或需满足 Android 15+ 16KB 页设备/商店要求时复查。来源：`evolution-log/2026-11-16-android-fullscreen.md#6-遗留登记`。
- [P2][open] **沉浸模式下系统栏可被下滑唤出**——Android 沉浸模式固有行为，Godot 未暴露粘性沉浸开关；保留有利于返回手势。触发条件：用户明确要求完全锁定系统栏时（需改 Android 清单/原生代码）。来源：同上 §6。
- [P1][closed] **真机验证方向锁定下旋转（仅用户机型）**——用户自建 APK 在真机多轮反馈闭环：曾报「真机还是有强制竖屏的问题」，修复后未再出现；补遗 16「横屏后看不见菜单，自动手动都是」修复后原话「好了」；解释 FULL_SENSOR 后原话「没问题」。弹窗「仅本次 / 永久固定」语义经用户反馈重设计并确认（补遗 1–3、9）。「方向锁定开启」这一前提按用户诉求场景推定、未经我方复核。来源：`evolution-log/2026-11-16-screen-rotation-follow.md` §6 → 关闭于 补遗 22（2026-11-23）。
- [P2][open] **其他 OEM 机型对 Java `FULL_SENSOR` 的接受度未验证**——仅用户机型通过。验收=换品牌机型在方向锁定下转手机，应用跟随且无来回震荡。触发条件：用户换机，或他人机型报「锁定下不转 / 来回震荡」时。来源：同上 §6、§7。
- [P2][open] **重力轮询兜底路径（`AndroidRuntime` 不可用时）真机表现未验证**——只支持 2 方向，带 4.0 迟滞与 2s 冷却。验收=人为禁用 fullSensor 分支后真机转手机可跟转。触发条件：出现 fullSensor 不可用的机型或对应日志告警时。来源：同上 §6。
- [P2][open] **竖屏菜单栏刘海避让存疑**——补遗 12 遗留观察 1：`menuSafeArea` 理论上 `OffsetTop = 4 + 安全区 inset`，启动器能正确避让而游戏内疑似未生效（怀疑 `DisplayServer.GetDisplaySafeArea()` 在 ExclusiveFullscreen 下返回 0，或调用时机过早）。触发条件：同上方「真机核对沉浸全屏的刘海/切槽避让」。来源：同上 补遗 12。
- [P2][closed] **菜单 toggle 与栏体未对齐**——补遗 12 遗留观察 2；补遗 15 改为 toggle 与图标同列纵排（栏顶），用户原话「没问题」。来源：同上 补遗 12 → 关闭于 补遗 15（2026-11-16）。
- [P2][closed] **字号排版缩放链路真机验收**——补遗 19/20/21 的三条 [未验证]（EraTW 立绘完整性、移动端 refit 后的物理呈现、HEAD 真机复测）；2026-11-23 用户以 HEAD `bea08b7` 自建 APK 真机复测，原话「没问题了」。来源：同上 补遗 19–21 → 关闭于 补遗 22（2026-11-23）。
- [P2][open] **PRINTC 密集列布局在 px 图像缩放后的重排未验证**——EraTW 所在地是 div 网格，没有覆盖 PRINTC 路径。验收=以 PRINTC 密集输出的游戏在改字号后分栏不错位。触发条件：用户反馈某游戏改字号后 PRINTC 列错位时。来源：`evolution-log/2026-11-16-ui-scale-setting.md` 补遗（2026-11-20 恢复字体滑条）+ 同上 补遗 17。
- [P2][closed] **模拟器内残留测试游戏 `/storage/emulated/0/emuera/testgame-img`**——2026-11-23 收尾时设备未连接、未删；用户裁定「不管」，不处理。日后若需清理：`adb shell rm -rf /storage/emulated/0/emuera/testgame-img`。来源：同上 补遗 22 → 关闭于 补遗 26（2026-11-23，用户裁定）。

### 蛇版（snake）对齐 Emuera-SKIA（2026-11-24）

来源日志：`evolution-log/2026-11-24-snake-18.0-alignment.md`（对齐到 Skiav18.0.0；参考实现在本机 `D:\clone\emuera_lazyloading_selfmodified_version`，上游提交 `7154549`，下一轮对齐从它起做增量审计）。

- [P1][open] **`resources.pak` 单文件资源包未对齐**——上游 17.4.0 引入、18.0 列为正式特性；只以该包分发资源的游戏在本引擎找不到图片与图片 CSV。验收=只带 `resources.pak`（无散图）的夹具游戏在桌面与 Android 真机上立绘、背景、图片 CSV 均正常。触发条件：用户决定支持该分发格式，或有用户新版蛇版分发的游戏报缺图。为什么未做：属资源加载子系统，需独立设计（上游 `Runtime/Utils/ResourceStore.cs` 502 行及 `AppContents.cs` 等接入）。来源：该日志 §2 第 10 条、§6 第 1 条。
- [P1][open] **legacy-runner 的 G10 守卫在默认日志配置下必然失败**——`Invoke-LegacyRunner.ps1 -Profile <退役档案>` 报 `Runner did not load expected first-party pack`：守卫在日志里找 Info 级的 `[LOAD] CompatPack enabled`，而用户 `config.toml` 的 `level = "warn"` 丢弃 Info；包其实已启用。验收=默认配置下该脚本对 snake 夹具 exit 0。修法二选一：runner 会话内临时打开 Info 镜像（不写用户配置），或守卫改读会话实际绑定的 `packIds`。触发条件：下次需要用该脚本做蛇版执行级冒烟时。为什么未做：改 runner 行为需用户确认口径；临时绕行（直接启动 Godot runner 场景）见该日志 §3「环境障碍与打通」。来源：该日志 §3、§6 第 2 条。
- [P2][open] **`tools/pure-move/Test-PureMove.ps1` 的 `-Include` 不补基线侧**——分片与主文件不同名的家族（`VariableToken.cs` ↔ `VariableData.*.cs`）里，旧分片永远被算成新增。验收=`-Include` 同时传入新旧分片时报 `MULTISET: PURE MOVE`，并补一条该工具自带的 `Test-PureMove.ps1` 用例。触发条件：下次对该家族做拆分时。来源：该日志 §3、§6 第 3 条。
- [P2][open] **两个生成物的换行符与 `.gitattributes` 不一致**——`src/Core/Compatibility/LegacyDialectInventories.Generated.cs`、`tools/legacy-runner/profiles.generated.json` 以 CRLF 提交而属性要求 LF，被生成器重写后内容零差异却显示为已修改。验收=重跑生成器后 `git status` 不列出这两文件。触发条件：用户决定做一次纯换行符提交时。来源：该日志 §3、§6 第 4 条。
- [P2][open] **AGENTS.md 里其余三个参考实现路径在本机不存在**——`E:\MyCode\Era\emuera.em-master`（v24）、`E:\MyCode\GodotCode\gemuera\uEmuera-0.2.9d`、`XEmuera-0.5.1等`；`tools/dialect-inventory/README.md` 的 v24 示例路径同理（`PLAN.md:47-48` 为历史记录不算）。验收=AGENTS.md 四个参考路径都指向本机存在的目录。触发条件：用户提供 v24 等参考实现的位置时。来源：该日志 §6 第 5 条。
- [P2][open] **`SnakeCompatibilityModule.ModuleVersion` 仍是 `1.1.0`**——snake 包已到 1.3.0，该常量目前无消费者。验收=定一条「模块版本是否随包版本联动」的规则并执行。触发条件：下次给 snake 包升版本时。来源：该日志 §6 第 6 条。
- [P2][closed] **新 `.cs` 文件的 `.uid` 侧车尚未生成**——`scripts/Emuera/GameData/ConstArrayTable.cs`、`ConstArrayTableDiskFiles.cs`、`scripts/Emuera/GameData/Variable/VariableData.FloatTokens.cs`、`tests/xUnitTest/GEmuera.Core.Tests/ConstArrayTableTests.cs`。验收=在 Godot 编辑器里打开一次项目后，对应 `.cs.uid` 生成并入库。触发条件：下次打开编辑器后。来源：该日志 §6 第 7 条。→ 2026-11-24 关闭：编辑器已重扫项目，四个 `.uid` 都已出现在工作区（未跟踪，随各自源文件一起提交即可；`scripts/FirstWindow.CompatPackSelfHeal.cs.uid` 同理）。
- [P2][open] **已失效的旧上游差异工具是否清理**——`tools/dialect-inventory/Invoke-/Test-LegacyDialectUpstreamDiff.ps1`、`LegacyDialectUpstreamDiff.psm1`、`legacy-dialect-upstream-diff.json`、`LegacyDialectReflectionDiff/`（README 已标注失效）。验收=用户裁定删除或修复。触发条件：下次整理 `tools/dialect-inventory` 时。来源：该日志 §6 第 8 条。
- [P2][open] **Android 真机上的常数表未验证**——桌面无头已通过（含走 `uEmuera.Utils` 的磁盘层），但 Android 走 Godot 文件 API 的分支与大小写不敏感路径索引未在真机跑过。验收=APK 上运行同一夹具输出一致。触发条件：下次出 APK 验收时顺带。来源：该日志 §3「未验证」。→ 2026-11-24 进展：x86_64 模拟器（API 37，16KB 页镜像）上用导出的 APK 跑同一夹具，输出与桌面逐项一致（`CTBL:3:4=18 … CONSTTABLE_OK`）；arm64 真机仍未验证，条目保持 open。

### 过期第一方包自愈（2026-11-24）

来源日志：`evolution-log/2026-11-24-stale-first-party-pack-self-heal.md`（清单预检 + 启动器改绑 + 释放器例外；执行设计 §6.2 的 KeptUser 由此收窄为「只保留仍然可用、读不出或并非同名包的用户文件」）。

- [P1][open] **自愈的 Android 真机验收未做**——x86_64 模拟器上三种场景（内置路径被手动覆盖成旧版 → 释放器替换；保存的选择指向别处的旧包 → 启动器改绑并写回；过期社区包 → 只提示）均已通过，但 arm64 真机（用户的 MIUI/HyperOS 手机）未测。验收=真机上把一个旧版蛇版包手动放进 `/storage/emulated/0/emuera/packs/pack.gemuera.snake/`，启动 App 后日志出现 `replaced-stale=1`、包目录内出现 `.pack.gemuera.snake.dll.stale-bak`，eraTW 正常进入标题画面。为什么未做：设备未连接。触发条件：用户下次把手机连上电脑（USB 调试）时。
- [P2][open] **自愈在首次调用时有一次引擎头部计算，冷启动耗时未在真机度量**——`FirstPartyPackBootstrap.TryGetEngineHeader` 进程内只算一次（`CompatPackHost.BuildValidationContext`），x86_64 模拟器 debug 包上从释放摘要到自愈日志约 0.8–1.2 秒。验收=arm64 真机 release 包上启动器首屏可交互时间不因它增加可感知的卡顿；若明显，改为在后台线程预热或仅在存在按游戏保存的选择时才计算。触发条件：做上一条真机验收时一并记录耗时。
- [P2][open] **启动器原有告警在默认日志配置下不落盘**——`scripts/FirstWindow.cs` 的 `GetPackProfileMap` / `ApplyCompatPackEnvironment` 里几处 `GenericUtils.Warn(EmueraLogCategory.UI, …)`（映射文件打不开、解析失败、外部 `GEMUERA_COMPAT_PACKS` 与按游戏选择并存）显式指定了日志类别；默认配置的类别开关只放行未指定类别的非 Error 日志，这些行不会出现在 `gemuera_runtime_*.log`。验收=默认配置下人为弄坏 `assets/text/pack-profile-map.json`，运行日志里能搜到 `LOAD.WARN`。修法=改成不指定类别 + `[LOAD]` 前缀（同 `FirstPartyPackBootstrap` 与自愈日志的写法）。为什么未做：不在本次范围，且状态栏已有对应提示。触发条件：下次排查启动器映射问题时，或整理日志 convention 时。
- [P2][open] **调试浮层在退役档案下弹 `Core prototype unavailable: Compatibility profile 'snake' was …`**——`scripts/GodotHost/PrototypeRuntimeNode.cs` 启动时按 launcher 档案名切换 Core 档案，退役档案解析失败；包加载成功时也出现，文字还在屏幕右侧被截断，容易被当成故障原因。验收=snake 等（由包供给的）档案启动游戏不再出现该提示。为什么未做：与包加载无关的独立界面瑕疵。触发条件：下次动调试浮层时。
- [P2][open] **x86_64 模拟器上 `[LOAD] SQLite runtime initialization deferred: DllNotFoundException: e_sqlite3`**——eraTW 的 `SQL_*` 指令依赖 SQLite；未查它在 arm64 真机上是否同样延迟/失败、以及是否影响运行。验收=eraTW 在真机上执行到 `SQL_EXECUTE_*` 不报错。为什么未做：本轮没有在模拟器上跑 eraTW，也没有真机。触发条件：用户反馈 eraTW 在手机上报 SQL 相关错误时。
- [P2][open] **`FirstWindow.cs` 本次净增 11 行（1840 → 1851）**——只加了接线钩子与一段注释，逻辑都在新的 partial `FirstWindow.CompatPackSelfHeal.cs`；该文件仍在 2000 行红线之下。验收=下次触碰该文件时把 `ApplyCompatPackEnvironment` 及其映射辅助（约 60 行）纯移动进 `FirstWindow.CompatPackSelfHeal.cs`。触发条件：下次改启动器兼容包逻辑时。

### 其他功能域

（暂无——各历史日志的 open 项按上一条规则增量迁移。）

## 已关闭 / 已废弃（保留备查）

（暂无。）
