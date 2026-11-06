# 移动端横竖屏与系统浮动小窗适配设计

- 日期：2026-11-04
- 状态：待用户批准（brainstorming 设计文档；批准后才进入 writing-plans / 实现）
- 分支：fix/mobile-orientation-smallwindow（从本地 dev = origin/dev aa3f408 拉出）
- 决策来源：用户访谈（grill/brainstorming）+ 两个只读调查代理（rotation-scroll-investigator、android-orientation-researcher）

## 1. 现象

### 问题1：横竖屏切换后，游戏内控制台滑不到底
- 用户操作：横屏 → 竖屏 → 再切回横屏。结果：游戏内正文控制台「下方的部分界面无法滑动到底部」。
- 用户补充关键事实：**竖屏下正常，只有横屏出问题**；系统浮动小窗进出也会诱发同样现象。
- 界面已确认：**游戏内正文控制台**（不是启动器、不是设置/输入/Quick 浮层）。

### 问题2：系统浮动小窗默认竖屏
- 具体形态：**系统浮动小窗**（最近任务/手势触发、悬浮在其他 App 之上的那种，如小米小窗），不是分屏、不是画中画。
- 时序：**本来是全屏横屏，一进小窗就变竖屏**；退回全屏后方向/界面也不对，接着出现「滑不到底」。
- 影响机型（用户确认）：小米/Redmi（HyperOS/MIUI）。Android 版本待补（影响 API36 方向限制的适用性判断，见 §4.2）。
- 用户期望：小窗形态**默认横屏**。

## 2. 用户已拍板的决策（硬约束）

1. **竖屏是正式支持**：全屏时用户可自由旋转，横竖屏布局都必须正确；小窗只要求默认横屏。
2. **不接受冷启动可见的「先横后转」跳变**：问题2分阶段——**阶段一（本轮）只做运行时方向请求**（manifest 保持 fullUser）；若真机证明 OEM 忽略请求，再来商量是否接受 manifest 默认横屏（阶段二）。
3. **验收形态**：Agent 产出 **debug APK（版本名 1.10.0-dev-20261104）** + **内置诊断日志/状态面板**；诊断开关走**设置页**（游戏内 OptionWindow + 启动器「调试/日志」页各两个开关，四语文案，存 user://settings.cfg），不依赖 APK 内只读的 config.toml；用户装小米真机按清单实测；桌面/无头回归由 Agent 全包。
4. **git 流程**：fix/mobile-orientation-smallwindow 分支 → 完成后 PR 到 dev；工作区遗留改动已按用户确认清理（manual.md/governance 缩进还原；.dsh/、.zcodeignore 留本地不入库；0 字节文件已删；export_presets 由本轮改版本号）。

## 3. 目标 / 非目标 / 反目标

### 目标
- **G1** 横竖屏切换（横→竖→横，任意次数）后，控制台滚动范围与内容一致，最后一行可达；竖屏原有正常行为不退化。
- **G2** 视口/安全区尺寸变化（旋转、系统浮动小窗进出/改尺寸）后，布局在一次收敛流程内稳定，不残留触摸/惯性/滚动修正中间态。
- **G3** 进入系统浮动小窗时向系统请求横屏；退出小窗回全屏时恢复「可自由旋转」；请求失败可观测、可降级、不崩溃、不卡死方向。
- **G4** 诊断可交付：日志（可导出）+ 状态面板（可截图）能回答「当时 window/viewport/safe/scale/滚动范围/multiwindow/requestedOrientation 各是多少」。
- **G5** 回归门禁全绿：构建、三冒烟、xUnit、governance、legacy-runner v24pure（semanticSha 不变）；APK 可导出。

### 非目标
- 不做「滚动范围大重构」（方案 C 不选，回归面过大）。
- 不改 Scripts/Emuera/**（v24/snake/erafl 解释器对齐区）任何语义；不碰方言门控。
- 不在本轮引入自定义 Android 插件（.gdap/AAR）；先用 Godot 内置 AndroidRuntime，失败再评估。
- 不处理分屏、画中画、桌面端窗口化的方向策略（仅系统浮动小窗）。
- 不为小窗内的竖屏布局做专门适配（小窗目标就是横屏）。

### 反目标
- 修复后全屏竖屏出现新的布局回归。
- 冷启动出现可见方向跳变。
- 方向请求把用户锁死在某一个方向（无法回退/无法旋转）。
- 诊断日志在 release/APK 默认高频输出，影响性能或淹没日志。
- 在 EmueraContent.cs（7403 行，>2000 行「只减不增」）里继续堆代码。

## 4. 现状与证据

> 标注约定：**[实测]**=本机实际运行/工具输出；**[静态]**=读码或读官方源码推断；**[未验证]**=需真机/后续验证。

### 4.1 问题1 根因（[静态]，按可能性排序）

**唯一暴露条件**：项目 stretch 为 canvas_items+expand，基准视口 1152x648（project.godot 未设 viewport 尺寸，用默认值）。横屏可见视口高约 648 视口单位，ApplyMobileViewportPolicy 的等比 fit=min(safeW/DrawableWidth, safeH/Config.WindowY) 在横屏 <1，缩放后内容必然超过一屏、必须滚动；竖屏可见高度约 2560 视口单位，内容一屏装得下、**不需要滚动**，所以「竖屏正常」只是没有暴露滚动问题。

**主假设（竞态链）**：
1. OnViewportSizeChanged（scripts/EmueraContent.cs:6519）在同一个回调里调 ApplyMobileViewportPolicy（:683）。
2. ApplyMobileViewportPolicy 先 ApplySafeAreaLayout（:655）改 rootContent 尺寸，然后（:704-708）在 rootContent 刚改尺寸、FullRect 子 scrollContainer 的新尺寸尚未可靠传播的时机，**同步**调用 UpdateScaleBounds（:3736）。
3. UpdateScaleBounds 在 :3747 读 scrollContainer.Size、:3748-3751 算 scaledSize、:3775-3776 写死 scaledContentRoot 的 min/size；若此时仍是旧（竖屏）尺寸，写出的内容范围是错的。修正只落在 DeferredUpdateScaleBounds（:3686-3700）的下一帧。
4. 同帧 _Process 的 ProcessContentScrollCorrection（:6635→:7722-7742）已先用过渡态 GetContentScrollLimit（:7884-7902）把 desiredContentScrollVertical 钳小并写回（:7739）；随后 UpdateScaleBounds 又把该 desired 当作 previousVertical（:3783）。
5. Godot ScrollBar 在排序（sort）前仍是旧 range，写 ScrollVertical 会被引擎钳制 → 最终「滚到底差一截」。横屏需要滚动才可见，与用户现象吻合。

**引擎级补充证据（Phase 1 GDUnit，2026-11-05，[实测]）**：Godot 4.7 `ScrollContainer` 的 `largest_child_min_size` 是 `_get_minimum_size()` 里的缓存（scene/gui/scroll_container.cpp:43-56），`_update_scrollbars()`（:639-655）只读缓存；**子节点 `custom_minimum_size` 变化本身不会刷新该缓存**（实测：child 尺寸 frame+1 变 4000，vbar range 6 帧后仍 400，写 scroll=999999 被钳到 400；显式 `get_combined_minimum_size()`/`update_minimum_size()`+`queue_sort()` 后 range=3400、可滚到底）。因此修复除了「单一布局入口 + 真实 range 对账」，还必须**在改完 `scaledContentRoot` 尺寸后强制刷新 ScrollContainer 最小尺寸缓存**（`UpdateMinimumSize()`+`QueueSort()`），否则自算 limit 再正确也会被引擎按旧 range 钳制。证据文件：`E:\tmp\gemuera-mobile-orientation\phase1-engine.md`。

**次假设**：旋转时系统若不派发触摸 Canceled/up，contentDragActive（:6861）/contentPinchActive（:7388）会残留；残留时 ProcessContentScrollCorrection 直接 return（:7724），单指拖动被 pinch 分支吞（:7305-7341）→ 表现为拖不到底；一次完整的 tap/release 可自愈，与「持续坏」不完全吻合，故列为次因。

**已排除（附依据）**：
- menuSafeArea（EmueraContent.cs:986-998）AutoRefresh=false，SafeAreaApplicator 回调顺序不影响控制台。
- 启动器 FirstWindow 布局/滚动（FirstWindow.cs:180-192/515-569）不是用户报告的界面。
- Program.ApplyAndroidWindowWidthPolicy（Program.cs:535-548）是空实现；Config.UpdateWindowSize（Config.cs:569-574）无调用点 → 逻辑画布不随旋转改变，不是根因。
- GetSafeViewportRect（:547-584）安全区换算在非全屏/小窗时可能错位，但 :613-622 会把矩形钳回视口，通常只造成内容区变小/偏移；且横屏 648 视口高来自 stretch 基准而非安全区，与「仅横屏坏」不吻合 → 非首因（小窗场景仍需观察，见风险）。

### 4.2 问题2 事实（[实测]+[静态]）

- project.godot:25：window/handheld/orientation=6（Godot 语义 Sensor）。
- 最新 APK（gemuera-1.10.0-dev-20261020.apk）用 aapt2 实测 manifest：android:screenOrientation=13（**fullUser**，全四向可转）、resizeableActivity=true、supportsPictureInPicture=true、configChanges 含 orientation|screenSize|density（旋转不重建 Activity）[实测]。
- Godot 4.7 映射（4.7-stable platform/android/export/gradle_export_util.cpp:42-80）：0→landscape(0)、1→portrait(1)、2→reverseLandscape(8)、3→reversePortrait(9)、4→**userLandscape(11)**、5→userPortrait(12)、6→**fullUser(13)** [静态]。
- DisplayServer.ScreenSetOrientation 在 Android = DisplayServerAndroid::screen_set_orientation → GodotIO.setScreenOrientation → Activity.setRequestedOrientation，无权限校验；APK 未申请 SET_ORIENTATION 不影响 [静态]。
- C# 检测多窗口：Engine.GetSingleton("AndroidRuntime")（Godot 4.4+ 内置插件，mono 同用）→ Call("getActivity") 返回 JavaObject → Call("isInMultiWindowMode")；GodotSharp 4.7.0 含 Godot.JavaClassWrapper/JavaClass/JavaObject；AndroidRuntime 在 setup 期注册，主循环 _Ready 可用 [静态]；真机行为 **[未验证]**（本轮先打探针日志）。
- Android 16(API36) 在 sw>=600dp 忽略 manifest 方向与 setRequestedOrientation（例外：sw<600dp 手机、appCategory=game）；本 APK targetSdk=36、isGame=true、appCategory=game、手机小窗 sw<600dp → 不受该限制 [静态+实测]。
- 目标机已确认：**小米/Redmi HyperOS 3（Android 16 / API36）**。该版本的方向忽略规则对本 APK 有 appCategory=game 例外（且手机窗口 sw<600dp），阶段一的 setRequestedOrientation 预期仍有效；需用首版探针日志在真机复核（若日志显示请求被忽略，即阶段二触发条件之一）[待真机验证]。
- supportsPictureInPicture=true 是 Godot 模板硬编码（GodotApp.java isPiPEnabled()），PiP≠悬浮小窗，自动入 PiP 默认关 [静态]。
- 结论：小窗竖屏是因为 manifest 允许全四向、窗口形状由 OEM WindowManager 决定；要「小窗默认横屏」，最可靠的杠杆是 manifest 固定方向（阶段二），阶段一先验证「进入小窗后运行时请求横屏」是否被小米小窗接受。

## 5. 方案（Approaches）

### 5.1 问题1：三条候选路
- **A. 视口布局状态机收敛（选中）**：把「视口/安全区变化」收敛成单一 dirty+deferred 流程；旋转时重置触摸/惯性并抑制过渡态滚动修正；以布局完成后的 ScrollBar 真实 Max/Page 为权威做一次滚动对账；加诊断。优点：治竞态类问题本身，覆盖旋转/小窗/系统栏变化全部入口；不推翻现有缩放/lazy 布局。代价：一帧延迟布局，需处理滚动锚点策略与测试。
- **B. 最小补丁**：只删旋转帧的同步 UpdateScaleBounds、reset 触摸状态、修 desired 钳制。优点：改动 20 行级、风险低。否决理由：同类竞态还会从 ApplySafeAreaLayout 的其他调用点（GameViewLeftPadding setter :804/:824、缩放 :707、其他入口）复发；用户已两次报同类问题，治标不划算。
- **C. 滚动范围大重构**：放弃自算 limit，完全以 ScrollContainer Max/Page 为唯一真相，删除手动 Size/CustomMinimumSize 与 GetContentScrollLimit。否决理由：会波及 pinch 缩放、desiredContentScroll 恢复、CBG 跟随、legacy-runner 显示观测；回归面过大，且与「只减不增」文件约束冲突。

### 5.2 问题2：三条候选路
- **A. 运行时检测+方向请求（选中，分阶段第一刀）**：manifest 保持 fullUser；检测到多窗口 → ScreenSetOrientation(SensorLandscape)；退出多窗口 → ScreenSetOrientation(Sensor) 恢复可旋转；全链路探针日志+降级。优点：无冷启动跳变（用户硬约束）；失败可观测、可无损回退；全屏行为不变。风险：小米小窗可能忽略请求（真机验证项）；C#→JavaObject 调用在本工程 mono 导出未验证（先探针）。
- **B. manifest 默认横屏（orientation=4→userLandscape）+ 全屏恢复 Sensor**：小窗最可靠。否决（本轮）：用户明确不接受冷启动跳变；作为**阶段二后备**保留，触发条件=阶段一真机失败且用户重新同意接受跳变。
- **C. 只加诊断、不动方向**：最保守，但问题2本轮不修。否决：用户要求本轮修。

### 5.3 选中组合
**问题1=A + 问题2=A**，一次交付 APK；问题2 若真机失败再升级到 B。

## 6. 详细设计

### 6.1 问题1：视口布局状态机

新增 partial：scripts/EmueraContent.Viewport.cs（新文件，目标 <700 行；从 EmueraContent.cs 迁出视口/安全区/滚动范围域代码，保证主文件「只减不增」）。视口域主文件迁出候选（实现时按纯移动先提交、再改逻辑）：GetSafeViewportRect、ApplyGameViewHorizontalPadding、ClampRectToViewport、RectAlmostEqual、RefreshViewportMetrics、ApplySafeRect、ApplySafeAreaLayout、ApplyAndroidDynamicWindowWidth、ApplyMobileViewportPolicy、OnViewportSizeChanged、QueueScaleBoundsUpdate/DeferredUpdateScaleBounds、UpdateScaleBounds 的滚动对账部分。
注意：tools/snake-alignment/Test-SnakeReferenceSurface.ps1 对 EmueraContent.cs 只钉扎 CreateTextPart 字体对齐与 div 边框 ARGB 两段，与本域无关；tools/legacy-runner/*Test-*.ps1 仍有路径钉扎，迁移后需全跑（见 §8）。

状态机：

    视口变化(Window/Viewport SizeChanged)
      → MarkViewportLayoutDirty()            // 立即：reset 触摸/惯性状态；viewportLayoutDirty=true
      → CallDeferred(ApplyViewportLayoutDeferred)
    ApplyViewportLayoutDeferred()            // 唯一布局入口，串行化（带 serial 防重入）
      1 RefreshViewportMetrics()             // Size/ContentSafeRect 等
      2 ApplySafeAreaLayout()                // rootContent/cbg/menu/panels
      3 ApplyOrientationAwareScale()         // 只算 fit 并 ApplyContentScaleValue；不再同步 UpdateScaleBounds
      4 ApplyContentScaleTransform()
      5 await ProcessFrame（最多重试 N=3 帧，直到 vbar range 稳定）
      6 ReconcileContentScroll()             // 以 vbar.Max-Page 为权威 + 锚点策略
      7 诊断快照 + 清 dirty

关键改动点：
1. ApplyMobileViewportPolicy() 删除 :706-708 的同步 UpdateScaleBounds()/ApplyContentScaleTransform() 与 :711 的重复 QueueScaleBoundsUpdate()（统一由上面流程驱动）；保留「非移动端直接 return、读 Config 逻辑尺寸、计算 fit 并 clamp 0.35..1.0」语义。
2. UpdateScaleBounds() 保留（其他调用者：pinch :7536、EndContentTouchGesture :7570、输出/布局 dirty 等），但**不再写入 ScrollVertical 目标**；滚动位置只在 ReconcileContentScroll() 或既有 pinch/output 流程里写。若评估发现输出流程依赖其写滚动，则改为「写 desired 但不写 ScrollContainer」，由同一对账入口落地。
3. ProcessContentScrollCorrection() 增加守卫：viewportLayoutDirty 或对账未完成时直接 return（防止过渡态 limit 污染 desired）。
4. 旋转/尺寸变化时：ResetContentTouchGestureState()（已有 :7582）+ 新增 ResetContentScrollInteractionState()（停止 inertia、清 drag 起点/速度/remainder、清 pending correction，但**保留** pendingScroll 贴底意图）。
5. 滚动锚点策略（避免跳顶/错位）：若变化前处于贴底（pendingScroll 或 scrollY >= maxY-2）→ 对账后贴底；否则按比例锚点 anchor = scrollY / max(1, maxY)，对账后 target = round(anchor * newMaxY)（内容坐标比例对用户已读位置最直观）。
6. SafeAreaApplicator 对 menuSafeArea 已是 AutoRefresh=false，不新增订阅；仅保证 ApplySafeAreaLayout 在流程内调用一次。
7. 诊断快照字段（每次对账输出一条）：viewport=(w,h) window=(w,h) safe=(x,y,w,h) scale=old->new layoutY=.. scaledRootY=.. vbar=(max,page) scrollY=(before->after) pendingScroll=..；一致性判定 ok/short（公式见 §8）。
8. 桌面可验证性（builder-check 记录，我方拍板）：增加**仅调试用**环境变量 GEMUERA_FORCE_MOBILE_VIEWPORT=1——置位时 ApplyMobileViewportPolicy 在桌面也走移动端分支（只影响布局计算与诊断，其余行为不变），用于桌面窗口改尺寸复现 §4.1 竞态链并截图/GDUnit 断言；APK 不设置该环境变量，行为不变。不写 config.toml（APK 内只读，避免混淆）。

### 6.2 问题2：系统浮动小窗方向控制器

新增 scripts/GodotHost/AndroidWindowOrientationController.cs（Node，约 250-350 行；非 Android 立即禁用）。
挂载（builder-check 后调整）：改为 **Autoload 级单例**——在 project.godot [autoload] 注册 scripts/GodotHost/AndroidWindowOrientationController.cs（名 AndroidWindowOrientationController），覆盖启动器与游戏场景；OnExitTree/进程退出前把方向恢复为 Sensor（避免销毁控制器时停在 SensorLandscape 导致「锁死方向」的反目标）。非 Android 平台 Autoload 仍实例化但全链路 no-op（不影响 headless/legacy-runner）。

检测：

    static bool TryQueryMultiWindow(out bool isInMultiWindow)
    {
        isInMultiWindow = false;
        GodotObject runtime = Engine.GetSingleton("AndroidRuntime");
        if (runtime == null) return false;
        GodotObject activity = runtime.Call("getActivity").AsGodotObject();
        if (activity == null) return false;
        Variant v = activity.Call("isInMultiWindowMode");
        if (JavaClassWrapper.GetException() != null) return false; // 记录异常，限流
        isInMultiWindow = v.AsBool();
        return true;
    }

- 只在 IsMobilePlatform（OS.HasFeature("mobile") || OS.GetName()=="Android"）执行；桌面/hosted 一律 no-op。
- 触发时机：_Ready 后延迟 2 帧首次检测；之后监听 GetWindow().SizeChanged（小窗进出/改尺寸都会触发），并在 GetViewport().SizeChanged 后经 150ms 去抖 Timer 再检测。
- 状态机（边沿触发，防抖）：Unknown → Fullscreen / MultiWindow；状态不变不发请求。进入 MultiWindow：DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.SensorLandscape)。退出回 Fullscreen：DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Sensor)（恢复用户可旋转）。30s 内同一状态重复进入只请求一次；用户在小窗内主动旋转（尺寸变化）不回写方向。
- 判据边界（builder-check 记录，我方拍板）：Android 的 isInMultiWindowMode() 在分屏/自由窗口/PiP 下都为 true；阶段一接受粗判据（分屏也会请求横屏，日志同时记录 window 尺寸与判定原始值）；「不处理分屏」= 不做分屏专门布局适配，不再细分为 freeform-only。
- 失败处理：单例缺失/activity null/Java 异常/API 抛错 → GenericUtils.Warn(EmueraLogCategory.UI, ...)（限流 1 次/会话/原因），控制器继续工作但不再重试方向请求（只保留查询），**绝不崩溃或阻塞**。
- 结果观测：请求后监听 Window.SizeChanged 与 ScreenGetOrientation() 2s 窗口，记录「请求→实际是否改变」的判定（changed|ignored|unknown），写诊断；这就是小米机型是否接受请求的证据。
- 降级/阶段二钩子：控制器内用一个常量 OrientationStrategy.EnableManifestFallback（本轮 false）与注释写明阶段二步骤（project.godot orientation=4 + 全屏恢复 Sensor），不改架构。

### 6.3 诊断交付（日志 + 状态面板 + 设置页开关）

- 开关存储（用户已选定设置页，四语言）：user://settings.cfg 的 [Display] 段（与 EmueraContent 现有 SettingsPath 一致，APK 可写）：
  - MobileLayoutDiagnosticsEnabled（默认 false）：开启后按事件写诊断日志。
  - MobileLayoutStatePanelEnabled（默认 false）：游戏内显示状态面板。
- 设置页入口：**两处各加两个开关**，共用同一存储键：
  - 游戏内 OptionWindow：新增「移动端布局诊断」分组（日志开关 + 状态面板开关 + 简短说明）。
  - 启动器「调试 / 日志」页（FirstWindow.DiagnosticsSettings.cs）：同两个开关。
  - 文案四语：assets/lang/{default,zh_cn,jp,en_us}.txt（OptionWindow.* 与 FirstWindow.* 键）。
- 新增 scripts/Diagnostics/MobileLayoutDiagnosticsSettings.cs：统一读写上述两个键（无 UI 依赖，供 OptionWindow/FirstWindow/诊断类复用），带内存缓存、保存即生效。
- 新增 scripts/Diagnostics/MobileLayoutDiagnostics.cs：Enabled 取设置；经 GenericUtils.Info(EmueraLogCategory.UI, ...) 输出事件：[ORIENTATION] ...、[VIEWPORT] ...、[SCROLL] ...（沿用 UI.INFO 派生前缀；文案遵守 docs/logging-convention.md「组件: 对象 → 结果（k=v）」）；PublishLayoutSnapshot(...) / PublishOrientationSnapshot(...) 更新静态最新快照（供面板读取）。
- 新增 scripts/Diagnostics/MobileLayoutStatePanel.cs（Control，挂 CanvasLayer 最高层，MouseFilter=Ignore，默认不创建）：左上半透明面板，8-10 行：window/viewport/safe/scale/layoutY/scaledRootY/vbar(max,page)/scrollY/multiWindow/requestedOrientation/lastEvent；每 0.5s 刷新静态快照；由 EmueraContent 根节点在开关为 true 时创建。用于用户真机截图回报。
- 日志同时受 RuntimeDiagnosticsConfig 的 logging.enabled 总闸门与等级门控（沿用既有路由，不新开旁路）；不改 RuntimeDiagnosticsConfig 既有等级语义。

### 6.4 测试与验证设计
- **xUnit（TDD 主载体，systematic-debugging Phase 4 的失败测试）**：把滚动对账数学抽成纯 C# 放到 src/Core/Ui/ContentScrollReconciliation.cs（Godot-free，只吃 float/int），在 tests/xUnitTest/GEmuera.Core.Tests/ContentScrollReconciliationTests.cs 先写失败测试再实现（贴底保持、比例锚点、旧 limit 过渡态不污染、scaledRoot=ceil(max(layout*scale, viewport)) 不变式）；窗口形态决策抽成 src/Core/Ui/WindowOrientationPolicy.cs + WindowOrientationPolicyTests.cs。三个既有 xUnit 工程同时跑零回归（2026-11-05 实测基线：GEmuera.Core.Tests 280 总数/279 通过/1 跳过、EmueraFacade.Tests 78 通过、EmueraPluginAbi.Tests 4 通过）。
- **GDUnit4（Godot 4.7 引擎行为取证，systematic-debugging Phase 1/3）**：新增 tests/GDUnit4Test/Layout/GodotScrollRangeBehaviorTest.gd，用真实 ScrollContainer 复现并钉住三件事：改父尺寸后子尺寸传播时机、vbar Max/Page 更新时机、给 ScrollVertical 赋超出范围值时引擎的钳制行为；运行入口 addons/gdUnit4/bin/GdUnitCmdTool.gd（headless 可跑），结果为 [实测] 证据。
- **桌面/无头回归（Agent 全包）**：构建（Godot mono --build-solutions，判定 DLL 时间戳）→ 三冒烟 → xUnit → governance 4/4 → legacy-runner v24pure（semanticSha 4859125d3f98… 不变）。
- **APK**：export_presets.cfg 版本改 1.10.0-dev-20261104、导出路径 export/releases/gemuera-1.10.0-dev-20261104-debug.apk；aapt2 复核 manifest screenOrientation=13（阶段一保持 fullUser）、versionName。
- **真机清单（用户执行，小米/Redmi HyperOS）**：1) 冷启动全屏（竖持）→ 界面正常竖屏、无跳变；2) 横→竖→横 x3：每次在横屏把控制台拖到最底，确认最后一行可见；竖屏同样确认；3) 横屏状态进系统浮动小窗：小窗默认横屏（诊断面板/日志记录 multiWindow=true requested=SensorLandscape ignored|changed）；退出回全屏后可继续旋转；4) 竖屏状态进小窗：小窗应转为/呈现横屏；5) 小窗内多次改尺寸/拖动小窗：控制台仍可落底；6) 导出诊断日志（游戏内「保存日志」）或截图状态面板回报。

## 7. 风险与缓解
| 风险 | 概率/影响 | 缓解 |
|---|---|---|
| C#→JavaObject 动态调用在本工程 mono APK 不可用 | 中/高 | 首版内置 3 条探针日志（singleton/activity/返回值），失败则本轮降级为「仅诊断」，阶段二改用 .gdap 插件或 manifest |
| 小米小窗忽略 setRequestedOrientation | 中高/中 | 日志证据 + 阶段二 manifest（用户已预留决策）；不阻塞问题1 修复 |
| 一帧 deferred 布局造成可见闪烁/滞后 | 中/低 | 对账最多重试 3 帧；面板/日志可观测；必要时把 apply 提前到 size_changed 当帧尾部 |
| 旋转重置触摸状态误伤正常手势 | 低/中 | 只在视口变化路径重置；gdUnit 触摸模拟回归 + 真机清单 |
| EmueraContent.cs 拆分破坏钉扎/契约 | 中/中 | 纯移动提交与逻辑改动分离；跑 snake-alignment 与 legacy-runner 全部 Test-*.ps1；新文件 <700 行 |
| 诊断日志默认开销 | 低/低 | 默认关 + [Conditional] + 事件级低频 |
| 逻辑画布/安全区在小窗非全屏时仍偏差 | 中/中 | 诊断记录 safeRect；若定位到 GetDisplaySafeArea 在非全屏错位，作为后续 P1 项修（本轮先不扩大范围） |

## 8. 验收标准
桌面/无头（Agent 出证据）：
- [ ] 构建：Godot mono --headless --path . --build-solutions --quit，.godot/mono/temp/bin/Debug/gemuera-c#.dll 时间戳更新；0 error。
- [ ] 三冒烟 + xUnit（三个测试工程）+ Test-Governance.ps1 4/4 全绿。
- [ ] tools/legacy-runner 契约测试（路径钉扎相关全部）不因拆分变红。
- [ ] legacy-runner v24pure 无头跑到 first_wait：Passed，semanticSha = 4859125d3f984f087eb143dd83fa54f3b754055187cb87a9e6b166d4505754a1 不变。
- [ ] 新增 GDUnit4 用例通过；新增诊断事件在 headless 或窗口模式探针中各出现一次。
- [ ] APK 导出成功；aapt2：versionName=1.10.0-dev-20261104，screenOrientation=13（阶段一不变）。

真机（用户出结论）：
- [ ] §6.4 清单 1-6 全部通过。
- [ ] 诊断面板/日志能证明：落底时 scrollY == vbar.Max-Page（±1）；对账快照满足 scaledRootY == ceil(max(layoutY*scale, viewportY))。
- [ ] 问题2 判定证据：multiWindow=true 时请求 SensorLandscape，结果为 changed（成功）或 ignored（失败→进入阶段二决策）。

## 9. 文件清单（预计）
新增：
- docs/superpowers/specs/2026-11-04-mobile-orientation-smallwindow-design.md（本文件）
- src/Core/Ui/ContentScrollReconciliation.cs（纯 C# 滚动对账数学）
- src/Core/Ui/WindowOrientationPolicy.cs（纯 C# 窗口形态→方向动作决策）
- tests/xUnitTest/GEmuera.Core.Tests/ContentScrollReconciliationTests.cs
- tests/xUnitTest/GEmuera.Core.Tests/WindowOrientationPolicyTests.cs
- tests/GDUnit4Test/Layout/GodotScrollRangeBehaviorTest.gd（引擎行为取证）
- docs/superpowers/plans/2026-11-04-mobile-orientation-smallwindow.md（实现计划）
- scripts/EmueraContent.Viewport.cs（从主文件迁出视口域 + 新状态机）
- scripts/GodotHost/AndroidWindowOrientationController.cs
- scripts/Diagnostics/MobileLayoutDiagnosticsSettings.cs
- scripts/Diagnostics/MobileLayoutDiagnostics.cs
- scripts/Diagnostics/MobileLayoutStatePanel.cs
- tests/GDUnit4Test/...（新增用例文件，具体路径实现时按现有套件目录）
- governance/evolution-log/2026-11-04-mobile-orientation-smallwindow.md（收尾必写）
修改：
- scripts/EmueraContent.cs（净减：迁出视口域，少量调用点替换）
- project.godot（新增 [autoload] 注册方向控制器；不改 main.tscn）
- scripts/Panels/OptionWindow.cs（新增「移动端布局诊断」分组：两开关 + 说明）
- scripts/FirstWindow.DiagnosticsSettings.cs（启动器「调试/日志」页：同两开关）
- assets/lang/{default,zh_cn,jp,en_us}.txt（新增设置页文案键）
- export_presets.cfg（版本号 1.10.0-dev-20261104 + 导出路径回 export/releases/）
- 可能：docs/logging-convention.md（若新增稳定前缀；当前方案用 UI. 派生前缀则不动）
不修改：Scripts/Emuera/**（对齐区）、任何方言/契约层、assets/scenes/main.tscn。

## 10. 开放问题（builder check / 实现前闭环）
1. ~~用户目标机 Android/HyperOS 版本号~~ 已确认：HyperOS 3（Android 16）。
2. ~~小窗状态面板多语文案~~ 已确认：设置页入口 + 四语文案。
3. APK 导出产物默认只给 export/releases/ 路径；是否额外复制一份到项目根方便取用（默认不复制，验收时按需告知）。
4. 阶段二触发判据写在 §5.2，不另开文档（默认）。

## 12. systematic-debugging 执行顺序（superpowers 流程）

1. **Phase 1 取证**：静态链路（§4.1 已完成）+ Godot 引擎行为测试（GDUnit4：尺寸传播/vbar 更新/ScrollVertical 钳制）+ 诊断插桩（日志/面板/强制移动端视口开关）。产出：失败复现证据或引擎机制实测结论；**不写修复代码**。
2. **Phase 2 模式分析**：对照正常路径（竖屏不滚动）与异常路径（横屏需滚动）差异，确认「过渡态 range 被当权威」这一差异假设。
3. **Phase 3 假设检验**：先写失败测试（xUnit 纯数学 + 可能的 GDUnit 场景测试），最小改动验证单一假设；不通过则回 Phase 1 换假设，不在同一处叠加多个修复。
4. **Phase 4 实现**：TDD 先失败测试→最小修复→验证；一次只改一个根因。禁止把「顺手优化」混入。
5. **问题2 单列实验**：manifest fullUser→小窗竖屏是已知根因；「运行时请求横屏是否被 OEM 接受」是待真机检验的假设，用首版 APK 的探针日志判定 changed/ignored；ignored 时按 §5.2 升级到阶段二（用户重新批准）。
6. **验证门禁**：verification-before-completion——所有结论必须有命令输出/日志/截图证据，再宣布完成。

> 若 Phase 3 连续 3 个假设均失败：停止改代码，回到架构层讨论（可能需要 manifest 阶段二或 .gdap 插件）。

## 13. 访谈决策记录（供追溯）
- 竖屏定位：正式支持（必须修好），不是「锁定横屏」。
- 问题1界面：游戏内正文控制台；竖屏正常、仅横屏坏。
- 问题2形态：系统浮动小窗；本来横屏、一进小窗变竖屏。
- 跳变取舍：不接受冷启动跳变 → 先运行时请求；真机失败再谈 manifest。
- 验收获批：Agent 出 debug APK（dev-20261104）+ 内置诊断；用户小米真机实测；桌面/无头回归 Agent 全包。
- 遗留改动：清理（还原 doc/缩进误改；export_presets 由本轮改版本；.dsh/、.zcodeignore 留本地不入库；0 字节文件删除）。
- 诊断开关真机可达性：设置页（游戏内 OptionWindow + 启动器调试/日志页各两个开关，四语言）。
- 目标机 Android 版本：HyperOS 3（Android 16 / API36）。
- git：fix/mobile-orientation-smallwindow → PR 到 dev。
