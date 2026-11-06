# 2026-11-04 移动端横竖屏滚动与小窗默认方向修复

## 任务目标与成果

修复两个 Android 真机严重问题（用户报障）：
1. 游戏内控制台在「横屏→竖屏→横屏」后无法滚动到底部（竖屏正常）；系统浮动小窗进出同样诱发。
2. 部分机型进入系统浮动小窗后游戏默认竖屏（应为默认横屏）。

成果（截至记录时）：
- [实测] Phase 1 GDUnit 取证确认 Godot 4.7 ScrollContainer 的 largest_child_min_size 缓存机制：子节点 custom_minimum_size 变化不刷新缓存 → vbar range 停留旧值 → 写 ScrollVertical 被钳到旧上限（child 4000 而 range 仍 400；显式 get_combined_minimum_size()/update_minimum_size()+queue_sort() 后 range=3400 可滚到底）。
- 实现：视口变化收敛为单一 deferred 布局入口 + 强制刷新滚动范围缓存 + 真实 vbar range 对账（纯数学在 src/Core/Ui/ContentScrollReconciliation.cs，xUnit 15 例）；小窗方向控制器（Autoload，isInMultiWindowMode → 请求 SensorLandscape / 恢复 Sensor，纯策略 WindowOrientationPolicy xUnit 5 例）；布局诊断日志/状态面板 + 游戏内/启动器设置页开关（四语）。
- [实测] 桌面/无头（复审返工后）：dotnet build 0 error；Godot headless 启动 exit 0；GDUnit 12/12（断言已强化为可失败）；xUnit Core 303 通过/1 跳过/304 总（基线 279+1 + 24 新增：ContentScroll 15 + WindowOrientation 9）、Facade 78、PluginAbi 4；governance 4/4 + 发布门禁 7/7；legacy 契约 6/6。
- [实测] APK：`export/releases/gemuera-1.10.0-dev-20261104-debug.apk`（**75,057,090 bytes，复审返工后重新导出**；aapt2：versionCode=11、versionName=1.10.0-dev-20261104、screenOrientation=13/fullUser 阶段一不变；SHA256 `5EC9F6C445DC15D6BF30112FB8AAFA1114B516D336862935CEFEDAE12EF10D00`）。
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
- [实测] xUnit 新增：ContentScrollReconciliation 15/15、WindowOrientationPolicy 9/9；全量 Core 303 通过/1 跳过/304 总。
- [实测] dotnet build 0 error（多次）；Godot headless --quit-after 120 exit 0（含 Autoload 控制器）。
- [实测] GDUnit 全量 12/12（PrototypeHost 4 + Layout 8）。
- [实测] legacy-runner v24pure 终验：status=Passed、semanticSha256=`4859125d3f984f087eb143dd83fa54f3b754055187cb87a9e6b166d4505754a1`（与基线一致，未破坏游戏启动链路）。
- [未验证] 真机（小米/Redmi HyperOS 3）：旋转/小窗/落底/诊断面板；APK aapt2 待本次导出后复核。


## 复审与返工（requesting-code-review + executing-plans 单次 fix pass）

- 复审结论：**With fixes**，无 Critical；5 Important + 3 Minor。已按 executing-plans 做单次 fix pass，每项修复以 xUnit/GDUnit 或编译验证收口：
  1. [Important] 方向结果判定不可靠（ScreenGetOrientation 在 Android 是 requestedOrientation 映射）→ 新增纯函数 `WindowOrientationPolicy.ClassifyLandscapeRequest` 按窗口宽高比判定 changed/already/ignored；TDD RED（CS0246）→ GREEN 4 例；控制器结果日志改用窗口 before/after 宽高。
  2. [Important] 旋转未清普通拖拽状态 → `MarkViewportLayoutDirty` 调用既有 `ResetContentDragState()`（含按钮选中/输入/画布视觉复位）。
  3. [Important] deferred 入口未显式布局 → 入口在 `ApplyContentScaleTransform` 后显式 `UpdateScaleBounds()`（幂等），再刷新滚动范围缓存。
  4. [Important] GDUnit 8 例中 6 例弱断言（assert true）→ 全部改为具体数值断言（range 400 / 3400、scroll 400 / 3400、child 尺寸），引擎行为变化会红；**集成冒烟另见遗留 P1**：legacy-runner 不实例化 EmueraContent，直接运行 main.tscn 到 TITLE 但未捕获 [VIEWPORT]/[SCROLL] 日志（[未验证]，由首版 APK 真机日志覆盖）。
  5. [Important] Java 异常被当 Fullscreen → `TryQueryMultiWindow` 检查 `JavaClassWrapper.GetException()` 与返回 Nil，异常按查询失败计（不计入成功、保留告警限流）。
  6. [Minor] `UpdateScaleBounds` 内刷新缓存按 `layoutChanged` 门控，避免 pendingScroll 每帧循环/pinch 每事件触发布局。
  7. [Minor] 旋转前在 `MarkViewportLayoutDirty` 快照当时 scroll/max（`layoutSnapshotScrollY/Max`），对账不再用 stale 的上一轮值。
  8. [Minor] 面板开关即时生效（设置 setter → `EmueraContent.ApplyMobileLayoutPanelSetting`）、隐藏时不刷新、诊断关闭不拼字符串（`Wanted` 门控）、日志文案改中文符合 logging-convention。
- 返工验证：xUnit Core 304 总 / 303 通过（新增 4 例）；GDUnit 12/12；dotnet build 0 error；APK 重新导出并复核 aapt2/hash（见上）。
- 复审中「无法在桌面自证」的诚实结论：GDUnit 已把引擎缓存行为钉死为可失败断言，但产品级旋转路径仍需真机诊断日志确认（遗留 P1），不在本轮伪造证据。
- P1 补测（2026-11-05 01:45，fix 后、DLL 01:21:40）：临时 `level=info/file_sink=true` + 临时 launcher.cfg 指向 eratoho + `GEMUERA_FORCE_MOBILE_VIEWPORT=1` 窗口化运行 `main.tscn`（1280x720, 1800 帧）：进程 exit 0、游戏日志到 `total-before-title`（54 行），**仍未捕获 `[VIEWPORT]/[SCROLL]`**；随后立即还原 config.toml 与用户 launcher/settings（备份于 `E:\tmp\gemuera-mobile-orientation\userdata-backup-2`）。结论：**P1 保持未闭合**，原因疑似主场景直启时 `StartLegacySessionAsync` 之后的时间窗/日志 flush 未覆盖，真机诊断日志将提供首次产品级端到端证据。

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
---

## 补遗（2026-11-06）：首版真机复测「日志无信息」的两处根因与修复（build 20261106）

### 现象
用户装 20261104 APK 后反馈「还是有问题」，附导出日志 `gemuera_20261106-210504.log`。日志头为
`RuntimeLevel=WARN Categories=General,Config`、`Records=51`，**没有任何 `[VIEWPORT]/[SCROLL]/[ORIENTATION]`**。

### 根因 A：布局诊断事件被日志等级/分类过滤（诊断链路缺陷）
- `MobileLayoutDiagnostics` 走 `GenericUtils.Info(EmueraLogCategory.UI, ...)`；真机 `RuntimeLevel=WARN` 且分类只启用 General/Config（active debug model 决定），INFO+UI 事件被双重丢弃。用户即使打开「布局诊断日志」开关也拿不到任何证据。
- 修复：改用 `DiagnosticLogExporter.WriteInfrastructureRecord`（基础设施通道，不受 RuntimeLevel/CategorySwitches 过滤，仍受 `logging.enabled`、`level=none` 全关契约与限流约束），事件 id 改为 `UI.LAYOUT.BOOT/VIEWPORT/SCROLL/ORIENTATION/TRACE/WARN`；每条附加 `build=mobile-layout-20261106` 构建标记；debug 构建默认开启诊断（release 仍由设置页开关控制）。
- [实测] 本地真实主场景窗口化运行（WARN 等级、file sink）：`UI.LAYOUT.BOOT`、`配置就绪`、`视口应用`、`[SCROLL] 滚动对账 status=ok` 均写入 ring buffer/日志。

### 根因 B：移动端 fit 布局在启动时从未被应用（启动时序缺陷）
- [实测] `EmueraMain` 在 `StartLegacySessionAsync()` 返回后立即调用 `ApplyMobileViewportPolicy()`，但此时 `emuera.config` 仍在后台线程解析（本地日志：BOOT 记录比 `ConfigData.loadConfig` 早 42ms）；旧逻辑 `Config.WindowX<=0` 直接 return → 移动端 fit/安全区布局在启动时从未生效，直到某次视口变化才可能碰巧触发。这解释了「改了但看起来没变化」类反馈。
- 修复：`ApplyMobileViewportPolicy` 配置未就绪时进入 15s 轮询重试（`ProcessPendingMobileViewportPolicy` 每帧检查），配置就绪后自动补做 `MarkViewportLayoutDirty + ScheduleViewportLayoutApply`；超时打印 `[VIEWPORT] 配置等待超时` 告警。
- [实测] FORCE=1 桌面真实场景：`[VIEWPORT] 配置就绪 → 补应用移动端布局 config=1920x1080`、`[VIEWPORT] 视口应用 → scale=0.6`、`[SCROLL] 滚动对账 → engineMax=0 nextMax=0 target=0 atBottom=True status=ok`（布局链路首次以实测证据跑通，部分关闭原 P1 证据缺口）。

### 新增诊断埋点（为下一轮定位剩余问题）
- 视口变化即时记录：window/viewport/scrollSize/safe/scale；
- 拖动结束记录：`[SCROLL] 拖动结束 → scrollY/vbarMax/mathMaxY/atBottom`（直接判断引擎 range 与自算上限是否一致、是否真的到达底部）；
- 方向状态记录：multiWindow/window/screen/requestedRaw；请求结果按窗口宽高比输出 changed|already|ignored。

### 本轮交付
- APK：`export/releases/gemuera-1.10.0-dev-20261106-debug.apk`（75,057,906 bytes；versionName `1.10.0-dev-20261106`、versionCode `12`、screenOrientation=13 阶段一不变；SHA256 `FCC4B6EFF165892107182B28FF4D169C836456ED61444A95758645D2F20451A7`）。
- [实测] 构建 0 error；Core xUnit 303 通过/1 跳过/304 总。

### 用户反馈与下一步
- 用户确认当前**剩余问题只有**「横屏/竖屏切换后滑不到底」；小窗默认竖屏本轮未再报（待下一轮日志确认）。
- 用户已授权：若新日志显示小窗请求 `status=ignored`，可直接进入阶段二（manifest 默认横屏 + 全屏恢复 Sensor；冷启动竖持会一次可见跳变）。
- 下一轮真机需要：装 20261106 包 → 复现旋转/小窗 → 保存日志；判据：含 `UI.LAYOUT.BOOT build=mobile-layout-20261106`、旋转后 `[SCROLL] 滚动对账` 的 engineMax/nextMax/status、拖动结束的 vbarMax/mathMaxY/atBottom。
---

## 补遗 2（2026-11-06 第二轮真机日志）：底部滑不到底的真根因——`ApplySafeRect` 的 Size/min 顺序

### 真机证据（`gemuera_20261106-225033.log`，42 条 UI.LAYOUT 事件）
- 诊断链路已修复：[实测] 日志含 `UI.LAYOUT.BOOT build=mobile-layout-20261106`、`UI.LAYOUT.VIEWPORT/SCROLL/TRACE` 共 42 条，说明 20261106 包与诊断通道均生效。
- 旋转后 `[VIEWPORT] 视口应用` 的关键数值：`window=(2772,1280)`（横屏）、`safe=(76.9,0),(1326,648)`（横屏安全区），但 `scroll=(1783,2357.244)` / `viewportY=2357`——**ScrollContainer 高度仍是竖屏的 2357**；`[SCROLL] 滚动对账 → layoutY=4579 scaledExtentY=3709 viewportY=2357 engineMax=1352 nextMax=1352 target=1352 status=ok`。横屏真实可视高只有 648，正确 `nextMax` 应为 `3709-648=3061`，实际被算成 1352 → 底部约 1709px 内容永远不可达。
- 小窗方向本轮未测（整段 `multiWindow=False`），用户本轮只报「横竖屏切换后滑不到底」。

### 根因（桌面窗口 resize 复现，[实测]）
- `ApplySafeRect(control, rect)` 原实现顺序：`SetAnchorsPreset(TopLeft)` → `Position` → `Size = rect.Size` → `CustomMinimumSize = rect.Size`。
- Godot `Control.Size` 的 setter 会把请求值**钳到当前 `CustomMinimumSize`**。竖屏时 rootContent 的 min 被设为 2357；旋转回横屏时先写 `Size=648` 会被旧 min 2357 钳住、缩不回去，随后写 min=648 也不会把 size 拉回 → `rootContent` 高度停在 2357 → 子节点 `scrollContainer` 高度也是 2357 → `update_scale_bounds`/对账用错误视口高算滚动范围。
- 复现手段：桌面窗口化 + `GEMUERA_FORCE_MOBILE_VIEWPORT=1` + Win32 `MoveWindow` 从 1280x720 → 720x1280 → 1280x720（用 EnumWindows 按 PID 找 HWND；Godot `_console.exe` 的窗口属于子进程，`MainWindowHandle` 为 0）。修复前最终横屏 apply：`self=(1202,648) root=(1202,2030) scroll=(2000,2030) viewportY=2030`；修复后：`self=(1202,648) root=(1202,648) scroll=(2160,648) viewportY=648`。

### 修复
- `ApplySafeRect` 改为：先 `CustomMinimumSize = rect.Size`（先降/升 min）→ `Position` → `Size` → 再写回 `CustomMinimumSize`（保持 min=目标尺寸）。
- 新增 GDUnit 取证回归：`test_control_size_shrink_is_clamped_by_stale_custom_minimum_size`（13/13 通过），把「Size 被旧 min 钳住」这一引擎行为钉成可失败断言，防止此顺序回归。

### 验证
- [实测] GDUnit 13/13；xUnit Core 303 通过/1 跳过/304 总。
- [实测] 桌面 resize 复现：修复前后尺寸链日志如上，`viewportY` 2357/2030 → 648。
- [实测] 诊断 build 标记更新为 `mobile-layout-20261106b`（新旧 APK 可从 `UI.LAYOUT.BOOT` 区分）；最终重导出后 aapt2 复核 versionName/versionCode/screenOrientation 均符合预期。
- [实测] legacy-runner v24pure：status=Passed，semanticSha256=`4859125d3f984f087eb143dd83fa54f3b754055187cb87a9e6b166d4505754a1` 不变。环境注记：Android APK 导出（dotnet publish）会把 `obj/project.assets.json` 重建为只有 net9.0 目标，随后 legacy-runner 的 `dotnet build --no-restore` 会以 NETSDK1005 失败；先 `dotnet restore gemuera-c#.sln` 即恢复（非代码回归）。
- [未验证] 真机：等 20261106b 包安装后复测旋转落底；小窗方向仍待测（若 ignored，用户已授权阶段二）。
- [观察] 修复后仍可见 `scroll=(2160,648)` 宽度大于视口（横向滚动模式 Disabled 时 ScrollContainer 最小宽度=内容宽）；它不影响纵向落底，但横向内容超宽时的表现另记 P2，不在本轮扩大范围。

### 交付
- APK：`export/releases/gemuera-1.10.0-dev-20261106b-debug.apk`（**75,144,308 bytes，最终重导出版**；versionName `1.10.0-dev-20261106b`、versionCode `13`、screenOrientation=13 阶段一不变；SHA256 `6DCA07812DDB0F6466CC46DE344E8862132E0DB4AFA1556383CA4108C0305E32`）。
---

## 收尾（2026-11-07）：用户真机确认修复 + 合并 dev + 发布 20261106

- 用户 2026-11-07 确认 20261106b APK 真机验证「没问题了」（旋转后底部可达）。
- 最终发布版按用户要求去掉 `b` 后缀：versionName `1.10.0-dev-20261106`、versionCode `14`（> 曾用于测试的 20261106b/code13，保证可覆盖升级）、screenOrientation 仍为 fullUser(13)（阶段一不变）。
- 代码流程：`fix/mobile-orientation-smallwindow` → PR → 合并 `dev`；APK 作为 GitHub Release `v1.10.0-dev-20261106` 的资产发布。
- 诊断标记保持 `mobile-layout-20261106b`（用于区分本轮修复代码），不在发布版另改，避免再次引起包指纹变化。
- 小窗默认竖屏问题：用户本轮未再报，但该会话未实际进入小窗（日志 `multiWindow=False`），阶段二（manifest 默认横屏）保持已授权但未实施的待命状态；若后续复现 `status=ignored` 再启用。
- 清理：删除本地测试用 `20261106b`/旧 `20261106(code12)` APK；删除 `E:\tmp\gemuera-mobile-orientation\userdata-backup*` 用户数据副本（已还原原文件）。
