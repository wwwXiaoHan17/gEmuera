# 2026-11-04 移动端横竖屏滚动与小窗默认方向修复

## 任务目标与成果

修复两个 Android 真机严重问题（用户报障）：
1. 游戏内控制台在「横屏→竖屏→横屏」后无法滚动到底部（竖屏正常）；系统浮动小窗进出同样诱发。
2. 部分机型进入系统浮动小窗后游戏默认竖屏（应为默认横屏）。

成果（截至记录时）：
- [实测] Phase 1 GDUnit 取证确认 Godot 4.7 ScrollContainer 的 largest_child_min_size 缓存机制：子节点 custom_minimum_size 变化不刷新缓存 → vbar range 停留旧值 → 写 ScrollVertical 被钳到旧上限（child 4000 而 range 仍 400；显式 get_combined_minimum_size()/update_minimum_size()+queue_sort() 后 range=3400 可滚到底）。
- 实现：视口变化收敛为单一 deferred 布局入口 + 强制刷新滚动范围缓存 + 真实 vbar range 对账（纯数学在 src/Core/Ui/ContentScrollReconciliation.cs，xUnit 15 例）；小窗方向控制器（Autoload，isInMultiWindowMode → 请求 SensorLandscape / 恢复 Sensor，纯策略 WindowOrientationPolicy xUnit 5 例）；布局诊断日志/状态面板 + 游戏内/启动器设置页开关（四语）。
- [实测] 桌面/无头：dotnet build 0 error；Godot headless 启动 exit 0；GDUnit 12/12；xUnit Core 299 通过/1 跳过/300 总（基线 279+1 + 20 新增：ContentScroll 15 + WindowOrientation 5）、Facade 78、PluginAbi 4；governance 4/4 + 发布门禁 7/7；legacy 契约 6/6。
- [实测] APK：`export/releases/gemuera-1.10.0-dev-20261104-debug.apk`（74,927,890 bytes；aapt2：versionCode=11、versionName=1.10.0-dev-20261104、screenOrientation=13/fullUser 阶段一不变；SHA256 `C3460FE97AA812794DF4F7DF24B48268B5A62C3CA97052B912BF3F5C6CAA8BAD`）。
- [未验证] 真机行为（用户小米/Redmi HyperOS 3 / Android 16 待测）：滚动是否落底、小窗是否默认横屏、[ORIENTATION] result status=changed|ignored。

## 现象与根因

**问题1**：横屏是唯一「必须滚动」的方向（stretch canvas_items/expand 基准 1152x648：横屏内容必超一屏；竖屏视口高约 2560 一屏装得下，所以看起来正常）。旋转/小窗改变 contentScale 与 scaledContentRoot 尺寸后，旧实现只在 ApplyMobileViewportPolicy 里同步 UpdateScaleBounds 并用自算 limit 写滚动位置，没有强制刷新 ScrollContainer 的最小尺寸缓存：vbar range 停留在旧值，ScrollVertical 被引擎钳到旧上限 → 底部内容不可达。Phase 1 的 GDUnit 取证把这条机制从「静态推断」升级为 [实测]。
**问题2**：project.godot:25 为 window/handheld/orientation=6，Godot 4.7 映射到 AndroidManifest screenOrientation=fullUser(13)（[实测] aapt2），全四向允许；系统浮动小窗形状由 OEM WindowManager 决定，于是小窗可竖屏。阶段一按用户决策做运行时请求（不改变冷启动方向，避免跳变）。

## 关键决策与 Why

- 竖屏是正式支持（用户拍板），不锁横屏；小窗「默认横屏」通过运行时请求实现；若真机证明 OEM 忽略请求（日志 ignored），再升级到阶段二（manifest orientation=4 + 全屏恢复 Sensor，需用户重新批准）。
- 问题1 方案选「状态机收敛 + 真实 range 对账」而非最小补丁（同类竞态还会从其他入口复发）或大重构（回归面过大）。
- 小窗方向控制器挂 Autoload 而非游戏场景：避免回启动器销毁控制器后停留在 SensorLandscape 锁死方向；_ExitTree 恢复 Sensor。
- 接受 isInMultiWindowMode 粗判据（分屏同为 true）：阶段一不细分 freeform，日志记录原始状态；「不处理分屏」仅指不做分屏专门布局。
- 诊断开关走设置页（游戏内 OptionWindow + 启动器调试页，存 user://settings.cfg）而非只读 config.toml：APK 内 config.toml 只读，真机不可改。
- 未做 170 行纯移动拆分（计划原任务）：跨文件纯移动风险高；新逻辑独立 partial、主文件净减；拆分目标登记 P2。

## 验证记录

- [实测] 基线（2026-11-05 00:1x）：构建 DLL 0:11:19；三冒烟 pass（CoreContractSmoke 需 DOTNET_ROLL_FORWARD=Major）；xUnit 280/78/4；governance 4/4 + 发布门禁 7/7；legacy 契约 6/6；legacy-runner v24pure Passed、semanticSha 4859125d3f984f087eb143dd83fa54f3b754055187cb87a9e6b166d4505754a1。
- [实测] Phase 1 GDUnit：tests/GDUnit4Test/Layout/GodotScrollRangeBehaviorTest.gd 8/8；关键数值见 E:\tmp\gemuera-mobile-orientation\phase1-engine.md。
- [实测] xUnit 新增：ContentScrollReconciliation 15/15、WindowOrientationPolicy 5/5；全量 Core 299 通过/1 跳过/300 总。
- [实测] dotnet build 0 error（多次）；Godot headless --quit-after 120 exit 0（含 Autoload 控制器）。
- [实测] GDUnit 全量 12/12（PrototypeHost 4 + Layout 8）。
- [未验证] 真机（小米/Redmi HyperOS 3）：旋转/小窗/落底/诊断面板；APK aapt2 待本次导出后复核。

## AI 表现复盘

- 有效做法：先用只读调查代理并行产出「代码链路假设」与「Android 方向机制事实」，再用 GDUnit 引擎行为测试把假设升级为实测——最终找到了比静态推断更硬的根因（ScrollContainer 缓存），直接改变了修复实现（强制刷新缓存）。
- 低效/返工：①Task 2 完成后漏执行提交，直到 Task 5 收尾才发现并补提交 114ef78（触发点：连续执行计划任务时把「测试绿」当作任务完成，忽略计划的提交步骤；后果：提交历史顺序错乱 1 次；可复用动作：每个 Task 结束必须显式核对 git log -1 与 ledger 的 commit 行）。②ask_user_question 首次 120s 超时导致提问丢失（后续加 timeoutMs）。

## 教训 → 具体优化动作

- 教训：Godot ScrollContainer 的 range 来自最小尺寸缓存，不能假设子节点尺寸变化会自动同步。动作：本 PR 新增 tests/GDUnit4Test/Layout/GodotScrollRangeBehaviorTest.gd 钉住该引擎行为（回归时若引擎升级改变行为会红）。
- 教训：计划中的测试期望值可能本身错误。动作：TDD RED 阶段先验证期望语义（本轮修正 2 条用例并同步计划）。
- 升级去向：暂不升级到 AGENTS.md（一次性引擎行为，已用 GDUnit 测试固化）。

## 遗留登记

- P1：真机验收（小米/Redmi HyperOS 3）——依赖用户装 APK 实测；若小窗仍竖屏（日志 ignored）进入阶段二决策（manifest+全屏恢复，需用户批准）。
- P1：真机确认 ScrollContainer.UpdateMinimumSize() 修复在真实旋转路径生效（诊断日志 [SCROLL] status=ok）。
- P2：EmueraContent.cs 视口域纯移动拆分（本轮未做，主文件仍 7403 行）。
- P2：isInMultiWindowMode 粗判据对分屏的影响（分屏也会请求横屏）——如需细分 freeform 另立任务。
