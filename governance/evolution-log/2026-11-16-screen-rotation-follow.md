# 系统方向锁定下跟随手机旋转（2026-11-16）

> 同分支前序记录：[系统级 UI 缩放](2026-11-16-ui-scale-setting.md)、[Android 完全全屏](2026-11-16-android-fullscreen.md)。

## 1. 任务目标与成果

用户要求：手机开启「方向锁定」时，转动手机仍要旋转整个应用；并提供开关与手动旋转按钮。
交付 = `AndroidWindowOrientationController` 方向模式管理（fullSensor 优先 + 重力轮询兜底）、启动器「旋转」按钮、游戏内设置窗的「跟随手机朝向」开关 + 「旋转屏幕」按钮、四语言文案。
判定 = 模拟器在系统锁定状态下双向旋转稳定无震荡（[实测]，见 §3）。

## 2. 关键决策与 Why

**现象与根因链（逐层取证得到，非猜测）**：
1. **清单是 `FULL_USER`**：`dumpsys activity` 显示 `requestedOrientation=SCREEN_ORIENTATION_FULL_USER`。
   Godot 把 `display/window/handheld/orientation=6` 导出成 fullUser（尊重用户锁定），**这是「锁定后不跟随」的根本原因**。
2. **`ScreenOrientation.Sensor` 请求无效**：启动时主动请求 Sensor 后，窗口仍不跟随传感器。
3. **`ReverseLandscape` 请求被接受但窗口不旋转**：注入 x=+9.81 后 dumpsys 变 `REVERSE_LANDSCAPE`，而截图仍是竖屏；
   同一路径改请求 `LANDSCAPE` 则窗口真的变成 2424x1080（截图实证）。→ 反向方向在部分设备不可用。
4. **低频震荡**：manifest 为 fullUser 时，系统在 resume 等事件后会按用户锁定纠正窗口，
   应用侧轮询又把它转回去，形成低频循环。

**最终方案**：绕过 Godot 枚举，用 `AndroidRuntime` 调 Java
`Activity.setRequestedOrientation(SCREEN_ORIENTATION_FULL_SENSOR = 10)`。
fullSensor 语义 = 4 方向跟随传感器**且忽略用户方向锁定**，旋转完全交给系统——一次设置即可，
既不震荡也不需要自己轮询（实测 `requestedOrientation` 稳定保持 FULL_SENSOR，双向切换均稳定）。

**保留的重力轮询兜底**（`fullSensorApplied == false` 时才启用）：
- 触发条件：`AndroidRuntime`/`getActivity` 不可用、或 Java 调用抛异常（部分 OEM/加固 ROM）。
- **无状态判定**：当前朝向一律取 `DisplayServer.WindowGetSize()` 的宽高比，不维护 `lastApplied` 之类静态状态。
  为什么：早期版本用静态状态记录"已应用方向"，被 `_Notification`/窗口 resized 事件重置后与真实窗口不一致，
  正是「横→竖→横」反复切换的直接原因（实测日志 `applied` 与窗口实际朝向相反）。
- 只判主分量（|x| vs |y|），不判符号 → 不依赖各 ROM 坐标系符号差异，也不会请求不可用的反向方向。
- 三重防护：主分量差 < 4.0 为死区；旋转后冷却 2 秒（过渡帧窗口尺寸与传感器读数都在抖）；与当前窗口朝向相同则不发请求。

**开关语义**：`FollowDeviceOrientation`（`settings.cfg [Display]`）
- 开 = fullSensor（跟随手机，忽略系统锁定）
- 关 = 固定当前宽高比对应方向（Portrait/Landscape）
- 手动「旋转」按钮会隐含关闭跟随并写盘——否则下一帧就被传感器转回去。

**排除项（查过非缺陷）**：
- 模拟器 `adb emu sensor set` 注入后窗口不自动旋转：这是 emulator 虚拟传感器不触发 framework orientation listener，
  与 Godot 无关；改用应用侧读取（`Input.GetGravity/GetAccelerometer`）后读值正确（日志实测）。
- `Input.GetGravity()` 返回 0：缺 `input_devices/sensors/enable_gravity` 项目设置（Godot 日志明确提示），
  已补 `enable_gravity` 与 `enable_accelerometer`；仍保留 accelerometer 回退。

## 3. 验证记录

- [实测] `requestedOrientation=SCREEN_ORIENTATION_FULL_SENSOR` 在启动后稳定保持（多次 dumpsys 采样一致）。
- [实测] **系统锁定下**（`accelerometer_rotation=0`, `user_rotation=0`）：注入横屏重力后截图 2424x1080，
  t+3/6/9/12/15 秒 5 次采样**全部横屏无震荡**；注回竖屏重力后 t+3/6/9 秒 3 次采样全部 1080x2424。
- [实测] 传感器读数正确：竖屏 `g≈(0,-9.81,0)`、横屏 `g≈(-9.81,0,0)`，与 `acc` 一致。
- [实测] 构建：`dotnet build gemuera-c#.csproj -nodeReuse:false -m:1` → 已成功生成、0 error。
- [实测] APK 导出：130MB（含 .NET 程序集），`Failed to build` 计数 0。
- [未验证] 真机（arm64）行为、OEM 对 FULL_SENSOR 的接受度。
- 环境障碍与打通：见 §4「APK 崩溃」一条。

## 4. AI 表现复盘

- **有效**：
  - 用 `dumpsys activity | grep requestedOrientation` 把"请求被接受"与"窗口真的旋转"分开取证，
    从而发现 `REVERSE_LANDSCAPE` 是假成功、`LANDSCAPE` 才是真成功（截图为最终事实，`dumpsys cur=` 不可信）。
  - 临时 `GD.PrintErr` 打印 `GetGravity/GetAccelerometer/applied` 一次运行就确认了传感器可读性与状态错位，避免继续盲猜。
- **低效 / 事故**：
  1. **`--build-solutions` 静默通过但代码有语法错误**（我编辑时留下重复代码块），编译实际失败 →
     导出时 `dotnet publish` 失败 → APK 缺 .NET 程序集 → 应用启动即 SIGSEGV。
     连续两次导出后才发现（APK 从 130MB 掉到 90MB 才引起怀疑）。
     **可核对后果**：多跑 2 轮「导出+安装+崩溃排查」（约 15 分钟）；APK 体积是快速判据。
     可复用动作：**改完 C# 一定先用 `dotnet build <csproj> -nodeReuse:false -m:1` 验证**，
     `--build-solutions` 的输出不能作为编译通过的证据；导出后检查 APK 体积（本项目约 130MB）。
  2. 震荡问题迭代了三版（4 方向带符号 → 2 方向带静态去抖 → 无状态 + fullSensor），
     根因是缺一份「系统在 resume 后按 manifest 纠正方向」的假设，导致前两版在治标。
- **险些踩的坑**：中途误判「模拟器传感器注入不生效」而准备自实现整屏旋转（SubViewport + canvas_transform），
  成本极高；先做 `dumpsys`/日志取证避免了这条路线。

## 5. 教训 → 具体优化动作

- (a) 本轮已执行：`AndroidWindowOrientationController` 用无状态窗口宽高比判定朝向，消除静态状态污染
  （`scripts/GodotHost/AndroidWindowOrientationController.cs` `_Process`）。
- (a) 本轮已执行：构建验证改用 `dotnet build ... -nodeReuse:false -m:1`（本任务全程实测）。
- (b) 本轮不做：把「Godot C# 改动必须用 dotnet build 验证 + 导出后核对 APK 体积」写入 AGENTS.md。
  升级去向：暂不升级（本轮以日志记录；若再现一次即升级到 AGENTS.md「构建与验证」节）。

## 6. 遗留登记

- P1：真机 arm64 验证 FULL_SENSOR 是否被各 OEM 接受（模拟器行为不代表真机）。不在本轮做：无设备。
  触发条件：下次 APK 发布前在真机开启方向锁定旋转手机核对。
- P2：倒持（Reverse 方向）不支持。为什么不在本轮做：`REVERSE_LANDSCAPE` 请求被接受但窗口不转（实测），
  若 fullSensor 在真机生效则系统会自动处理 4 方向，届时该限制自动消失；兜底轮询路径仍只支持 2 方向。
- P2：兜底轮询路径（fullSensor 不可用时）在真机上的实际表现未验证。为什么不在本轮做：
  无法在模拟器上构造 `AndroidRuntime` 不可用的环境。

> 补遗（2026-11-16，用户提出手动旋转的持久化语义问题）
>
> 问题：`RotateScreen()` 原实现 `SetFollowCache(false)` 无条件写盘，点一次「旋转」就把
> `FollowDeviceOrientation=false` 永久落盘，重启后仍不跟随——用户点旋转通常只是"这次想横着玩"，
> 不该被记成永久偏好。
>
> 用户裁定方案：点旋转时弹窗让用户选择「仅本次 / 永久固定」。
>
> 实现（`scripts/Panels/RotateChoiceDialog.cs` 新增 + 两处调用点）：
> - `AcceptDialog`：OK = 「永久固定」（写盘），右侧自定义按钮 = 「仅本次」（只改内存缓存，
>   不写 settings.cfg，重启恢复配置值），X 关闭 = 取消（`CloseRequested` 释放节点，避免泄漏）。
> - `RotateScreen(bool persist = true)` 重载；`SetFollowCache(bool value, bool persist = true)`。
> - 跟随已关闭时（已是固定模式）不弹窗，直接旋转——此时没有"永久/本次"可选。
> - 选择后回调 `SyncFollowOrientationToggle` 同步设置窗开关显示。
> - 四语言 `RotateChoice.*` 4 个键（Title/Text/Permanent/Session）。
>
> 验证 [实测]：桌面窗口模式截图确认对话框渲染正常（标题「旋转屏幕」/说明/「永久固定」+
> 「仅本次」双按钮/关闭按钮，中文文案生效）；`dotnet build` 0 error。
> [未验证] Android 真机上两个选项的写盘/不写盘行为（需真机安装后检查 settings.cfg）。
>
> 顺带修复：本轮预览时发现桌面 `user://settings.cfg` 被早前手工恢复时写坏
> （`Language=zh_cn` 缺引号 → ConfigFile 解析失败 → 语言回退英文），已修正为 `Language="zh_cn"`。
> 教训：手工改 Godot cfg 的字符串值必须带双引号。

> 补遗 2（2026-11-16，用户反馈「构建后不弹窗」+ 由此发现的升级 bug）
>
> 现象：用户构建后点旋转没有弹出选择框。
> 解释：跟随已关闭时不弹窗是**有意设计**（已是固定模式，"永久/本次"无区别，直接转）。
> 用户设备上此前点过旧版旋转，`settings.cfg` 里 `FollowDeviceOrientation` 已是 false。
>
> **但由此发现一个真实 bug**：`RotateChoiceDialog.Show` 原用 `FollowDeviceOrientation`
> （含「仅本次」的会话覆盖）判断是否弹窗。用户点「仅本次」后内存态为 false 而配置仍为 true，
> 再点旋转会走"已关闭跟随"分支并 `RotateScreen(persist: true)`——**把"仅本次"悄悄升级成永久**。
>
> 修复：新增 `AndroidWindowOrientationController.PersistedFollowDeviceOrientation`
> （只读 settings.cfg，不碰内存缓存），弹窗判断改用它。语义变为：
> 「用户偏好=跟随 → 弹窗询问；用户偏好=固定 → 直接转」。
>
> 验证 [实测]：`dotnet build` 0 error。[未验证] 真机两选项写盘行为（需用户安装后确认）。
> 让弹窗出现的条件：设置窗打开「跟随手机朝向」（或清掉 settings.cfg 的该键）。

> 补遗 3（2026-11-16，用户反馈「旧版本关闭的跟随，更新后仍然关闭」）
>
> 现象正常但不应让用户承担：旧版 `RotateScreen` 无条件写盘，用户点过一次旋转就把
> `FollowDeviceOrientation=false` 永久落盘；该值不代表真实偏好，而新版本迁移不会纠正它。
>
> 修复：一次性配置迁移 `MigrateLegacyRotationSettingsIfNeeded()`。
> - 判据：`settings.cfg` 缺少新键 `RotationFollowSemanticsV2` = 旧配置
> - 动作：把 `FollowDeviceOrientation` 重置为 true，写入标记，后续不再执行（幂等）
> - 触发点：`FollowDeviceOrientation` 与 `PersistedFollowDeviceOrientation` 两个读取入口
>   首次访问时（打开设置窗 / 点旋转 / 启动时应用方向模式）
> - 失败降级：异常只记告警，退回读取原始配置，不阻断启动
>
> 验证 [实测]：`dotnet build` 0 error。[未验证] 真机上迁移后的实际表现（需安装后确认
> settings.cfg 出现 `RotationFollowSemanticsV2=true` 且 `FollowDeviceOrientation=true`）。

> 补遗 4（2026-11-16，用户反馈「rotate 的中文没有」）
>
> 排查：中文键 `FirstWindow.Rotate=旋转` 确实在 `assets/lang/zh_cn.txt`；真机显示英文是因为
> `settings.cfg` 没有 Language 键 → `MultiLanguage` 回退 `default`（英文，`FirstWindow.Rotate=Rotate`）。
>
> 修复（`scripts/MultiLanguage.cs`）：未保存语言偏好时按系统区域选择语言文件。
> - `LoadSavedLanguage` 改为无配置时返回 null（原先直接返回 "default"，无法区分"未设置"与"显式选 default"）
> - 新增 `DetectSystemLanguage`：`zh*`→zh_cn、`ja*`→jp、`en*`→en_us、其它→default
> - `CurrentLanguage` 同步改为按同一顺序推导（未加载时也不返回 null），避免设置窗下拉框与界面文字不一致
> - 用户显式选过语言（含显式选 "default"）不受影响
>
> 验证 [实测]：桌面删除 settings.cfg 的 Language 键后启动（系统 locale=zh_CN），
> 启动器完整显示中文（游戏/调试·日志/操作手册/界面 100%/开始游戏）；构建 0 error。

> 补遗 5（2026-11-16，用户反馈「rotate 按钮没看到」）
>
> 根因：启动器 header 是 HBox，标题 `SizeFlagsHorizontal=ExpandFill` 且原先未允许裁剪，
> 其最小宽度恒为全文宽；窄屏（手机竖屏）或大 UI 缩放下 header 总最小宽度超过容器宽度，
> 右侧「界面缩放」「旋转」被挤出屏幕右侧（真机实测看不到按钮）。
>
> 修复：`title.ClipText = true`（`scripts/FirstWindow.cs`）。标题变成可压缩项后，
> HBox 优先压缩标题，右侧控件始终可见（文本过长时以省略号截断）。
>
> 验证 [实测]：桌面把逻辑宽度压到 540（约 1080 物理 @200% UI 缩放）并临时让旋转按钮
> 在桌面显示，截图确认「界面 100%」与「旋转」两个控件均可见；构建 0 error。

> 补遗 6（2026-11-16，用户要求「竖屏时游戏内那串功能按钮改为竖的」）
>
> 对象：展开的系统菜单栏 `menuExpandedBar`（11 个图标：返回/重启/设置/输入/快捷/
> 自动跳过/保存日志/标题/退出/缩放/鼠标）。横排 11 个图标在竖屏窄宽度下会横向溢出，
> 右侧按钮被裁切。
>
> 实现（`scripts/EmueraContent.cs`）：
> - 字段类型 `HBoxContainer` → `BoxContainer`（HBoxContainer 的 vertical 不可改，引擎报错）
> - 新增 `UpdateMenuBarOrientation()`：`Size.X < Size.Y` 时 `Vertical = true`，在
>   `ApplySafeAreaLayout` 里调用（旋转/尺寸变化走既有 deferred 管线，无需新监听）
> - menuRoot 是顶部锚定的 HBox，面板变高后自然向下展开，位置无需另调
>
> 验证 [实测]：桌面窗口模式加载 eraTW，真实窗口尺寸设为 560x1200（竖屏）与 1200x560
> （横屏）：
> - 竖屏 menubar `vertical=true`，截图确认 11 个图标竖向排列于右上角、全部可见
> - 横屏 menubar `vertical=false`，截图确认保持横排
> - 构建 0 error。

> 补遗 7（2026-11-16，用户反馈三处 UI 问题）
>
> 1. **系统菜单栏缺手动旋转入口**：用户只能去设置页找。修复：菜单栏新增旋转图标按钮
>    （`assets/icons/rotate.svg` 新建，用「手机+旋转箭头」图形，避免与「重启」的环形箭头混淆；
>    桌面验证时临时放开 `OS.GetName()=="Android"` 判断截图确认渲染正确，13 个/12 个图标
>    与重启图标可区分）。
> 2. **快捷面板默认出现在右下角**：用户从右上角菜单的「快捷」按钮唤出，面板却在屏幕另一端。
>    修复（`scripts/Panels/QuickButtons.cs` `ApplyFloatingOverlayPanelSize`）：默认位置改为
>    右上角系统菜单栏下方（`safeRect.Y + 48 + 16`）；用户拖动过的位置仍按持久化坐标优先。
> 3. **设置里找不到「自动旋转」**：原标签是「跟随手机朝向」，与用户预期用词不符。
>    修复：四语言标签改为「自动旋转（跟随手机）」等，代码 fallback 同步。
>
> 验证 [实测]：桌面窗口模式（560x1200 竖屏）截图确认菜单栏新增旋转图标且可区分；
> `dotnet build` 0 error。[未验证] 真机上快捷面板新默认位置与设置页标签显示。

> 补遗 8（2026-11-16，用户反馈「图标都没加载出来」的排查结论）
>
> 排查路径与结论：
> 1. **假设「导出过滤漏了 icons」→ 已证伪**。做对照导出：带 `assets/icons/*` 与不带，
>    APK 内 `assets/.godot/imported/*.svg-*.ctex` 均为 15 个——`export_filter="all_resources"`
>    本来就会打包运行时动态加载的图标。故未保留 include_filter 改动（避免与基线既存改动混提交）。
> 2. **模拟器端实测正常**。x86_64 APK 装入 Pixel_10 后，用最小测试游戏
>    （`/storage/emulated/0/emuera/testgame/{erb,csv}`）进入游戏并展开菜单，
>    截图确认 12 个图标全部渲染（返回/重启/设置/输入/快捷/跳过/日志…），竖屏竖排亦生效。
> 3. 因此「图标没加载」更可能来自用户侧构建环境：`.godot/imported` 缓存缺失/陈旧，
>    或构建产物不完整（本项目正常 APK 约 130MB，含 .NET 程序集；仅 ~90MB 说明程序集未打进去）。
>
> 交付侧未做代码改动；仅记录排查结论与建议（清理 `.godot/imported/` 后重建、
> 核对 APK 体积、必要时提供真机 logcat）。
> 验证 [实测]：模拟器截图（12 图标齐全）；对照导出两版 APK 的 ctex 计数均为 15。

> 补遗 9（2026-11-16，用户指出「仅本次」后会话内不应再弹窗）
>
> 上一版修 bug 时把弹窗判据改成了持久化配置（`PersistedFollowDeviceOrientation`），
> 副作用是：选了「仅本次」后配置仍是 true，会话内每次点旋转都会再次弹窗。
>
> 正确语义（用户确认）：
> - 会话内已固定（无论「仅本次」还是「永久」）→ 再点旋转直接切方向，不再询问
> - 且必须 `persist: false`——否则会把「仅本次」悄悄写成永久
>
> 实现：`RotateChoiceDialog.Show` 判据改回内存态 `FollowDeviceOrientation`，直接转分支
> 从 `persist: true` 改为 `persist: false`；删除已无调用方的 `PersistedFollowDeviceOrientation`。
>
> 行为矩阵（实测推演，构建 0 error）：
> | 状态 | 内存 | 配置 | 点旋转 |
> |---|---|---|---|
> | 跟随中 | true | true | 弹窗询问 |
> | 选「仅本次」后 | false | true | 直接转、不写盘、不弹窗 |
> | 重启（仅本次） | true | true | 恢复跟随，点旋转弹窗 |
> | 选「永久」后 / 重启 | false | false | 直接转、不弹窗 |

> 补遗 10（2026-11-16，「仅本次」会话内不重复弹窗的真机级实测）
>
> 在 Pixel_10 模拟器（系统英文、方向锁定开启）完整跑了一遍启动器入口的旋转链路：
> 1. 点 Rotate → 弹出「Rotate screen / Always / This session only」✓
> 2. 点 This session only → 窗口 1080x2424 → **2424x1080**（真的横屏）✓
> 3. 再点 Rotate → 窗口直接回到 1080x2424，**没有再次弹窗** ✓
> 4. 此刻 `settings.cfg` 为 ABSENT（选「仅本次」确实未写盘）✓
> 5. 强制停止并重启应用 → 点 Rotate → **弹窗恢复**（跟随偏好回到默认）✓
>
> 界面显示英文，因模拟器系统 locale 为英文——正是补遗 4「未设置语言时跟随系统区域」
> 的预期行为，非缺陷。

> 补遗 11（2026-11-16，用户确认「快捷按钮面板位置不符合逻辑」）
>
> 关键：**默认走的是非浮动贴边布局**（`floatingOverlayMode == false`），上一版改的
> `ApplyFloatingOverlayPanelSize` 只作用于浮动模式，对用户的默认体验没有影响。
>
> 非浮动模式原实现（`ApplyPanelSize`）：
> - 水平：`AnchoredLeft`（= `configuredFlip || floatingHosted`）？贴左 : **贴右**
> - 垂直：`AnchoredTop`（= `floating && floatingHosted`）？贴顶 : **贴底**
> → 默认落在**右下角**，而唤出它的「快捷」按钮在右上角菜单里，竖屏下相距整屏。
>
> 修复（`scripts/Panels/QuickButtons.cs`）：
> 1. 垂直锚点一律改为**贴顶**，非浮动模式顶部边距 48+16 让开顶部横排菜单栏；
>    悬浮承载时沿用原 20px 边距。
> 2. 竖屏时**额外让开右侧竖排菜单栏**（右边距 += 48+16）——否则贴右的面板会盖住
>    12 个竖排图标。
> 3. 位置语义版本迁移 `QuickFloatingPositionVersion=2`：旧配置里存的坐标（旧默认
>    右下角产物）一次性清空，否则用户看不到新默认。浮动模式的默认位置同步改为
>    贴合菜单栏（横屏在下方、竖屏在其左侧）。
>
> 验证 [实测]：`dotnet build` 0 error。[未验证] 视觉位置——eraTW 的 ERB 未定义
> `QUICKBUTTON`，桌面 `ShowPad()` 调用成功但面板为空（无按钮即无尺寸），
> 无法截图确认。需要用户真机确认。

> 补遗 12（2026-11-16，用户裁定菜单栏保持单列竖排）
>
> 我曾把竖屏菜单栏改成 GridContainer 两列（想缩短 12 个图标约 576px 的高度），
> 用户明确要求「应该竖着一列」，已回退为 `BoxContainer { Vertical = true }` 单列。
>
> 遗留观察（本轮未修，待用户确认是否处理）：
> 1. 用户截图中菜单栏几乎贴屏幕最上沿（约 10px），沉浸全屏 + 刘海设备下会被切槽压住。
>    `menuSafeArea` 配置为 `OffsetOverrides` + `BaseTop=4` + `ApplyTop=true`，理论上
>    `OffsetTop = 4 + 安全区inset`；启动器同一套 Applicator 能正确避让，游戏内却未生效，
>    怀疑 `DisplayServer.GetDisplaySafeArea()` 在 ExclusiveFullscreen 下返回 0 或调用时机过早。
> 2. 截图中菜单 toggle（四宫格图标）悬在菜单栏外上方，未与栏体对齐——
>    menuRoot 是 HBox `[panel][toggle]`，竖排后 panel 很高，toggle 的垂直对齐不合适。
>
> 验证 [实测]：回退后 `dotnet build` 0 error；单列竖排实现与 520ebdf 完全一致。

> 补遗 13（2026-11-16，用户要求方向控件全平台可见）
>
> 用户反馈 PC 设置里看不到「自动旋转」，认为应该保持一致以便确认功能存在。
>
> 改动：三处方向入口从「仅 Android 创建」改为「全平台创建 + 非 Android 禁用」：
> - `OptionWindow`：「自动旋转（跟随手机）」开关、 「旋转屏幕」按钮 → `Disabled = !isAndroid`
> - `FirstWindow` header：「旋转」按钮 → 同上
> - `EmueraContent` 系统菜单栏：旋转图标（`rotateMenuButton`）→ 同上
> - 非 Android 的 tooltip 改为「仅在 Android 设备上可用。」（四语言 `*RotationAndroidOnly` 键）
>
> 验证 [实测]：桌面打开设置窗截图——「自动旋转（跟随手机）」开关与「旋转屏幕」按钮
> 均以灰色禁用态正常显示在「界面缩放」下方；构建 0 error。

> 补遗 14（2026-11-16，用户反馈「快捷按钮会乱跑」）
>
> 现象：竖屏单列菜单栏下，展开/收起时 toggle（四宫格）位置上下跳动。
> 根因：`menuRoot` 是 HBox `[panel][toggle]`，panel 竖排后高约 576px；toggle 的
> `SizeFlagsVertical` 默认 Fill，被拉伸到面板高度、图形 `KeepAspectCentered` 垂直居中，
> 于是展开时在栏体中部、收起时回顶部。
>
> 修复：`menuToggleBtn.SizeFlagsVertical = ShrinkBegin`（顶部对齐、不拉伸），
> 横竖屏都固定在与栏体顶部同一水平线。
>
> 验证 [实测]：PC 竖屏（560x1200）截图——12 个图标单列、toggle 固定在栏顶右侧；
> 构建 0 error。

> 补遗 15（2026-11-16，用户要求 toggle 与图标纵排同一列）
>
> 上一版把 toggle 固定在栏体顶部右侧（同一水平线），用户指出「竖着这样很奇怪」——
> 正确形态是 toggle 与 12 个图标纵向排成一列、toggle 在最上。
>
> 实现（`scripts/EmueraContent.cs`）：
> - `menuRoot` 字段与实例全部改为 `BoxContainer`（此前字段已是 BoxContainer 但
>   `new HBoxContainer()` 未同步，导致 `Vertical` 设置被引擎拒绝、竖排不生效——
>   这是本轮两次踩到的同一个坑）。
> - 竖屏：`menuRoot.Vertical = true` + `GrowVertical = End`，`menuPanelContainer`
>   设 `ShrinkEnd/ShrinkBegin` 防止被拉伸到容器外（此前实测出现 1362px 溢出）。
> - `AddIconButton` 的图标设 `ShrinkCenter`，竖排时保持 48px 不被拉长。
> - 新增 `EnsureMenuToggleFirst`：竖屏把 toggle 移到子节点首位（最上），横屏移到末位（最右）。
>
> 验证 [实测]：PC 竖屏 560x1200 截图——toggle 在最上、12 个图标顺次向下（含旋转图标），
> `ROOT vertical=true`、`PANEL size=(72,822)` 不再溢出；构建 0 error。

## 7. 自查（2026-11-18，应用户「自行审查一遍」要求，全分支 21 提交逐文件复核）

### 发现并当场修复

- **P0｜跟随模式与小窗方向策略互相覆盖**（`AndroidWindowOrientationController.Evaluate`）：
  原实现不看 `FollowDeviceOrientation`，进入分屏/小窗就 `RequestOrientation(SensorLandscape)`，
  把已应用的 FULL_SENSOR 覆盖掉；1 秒后 `GuardFullSensor` 又重设 FULL_SENSOR 覆盖小窗请求，
  两机制无限互殴。修复：跟随模式下 Evaluate 不再走 RequestOrientation；进入小窗暂停跟随
  （fullSensorApplied=false + SensorLandscape 交给小窗策略），退出小窗恢复跟随（重设 FULL_SENSOR）；
  多窗口查询失败时跟随机制维持现状（不暂停也不恢复，避免误切换）。
- **P1｜`RotateChoiceDialog.pendingCallback`（static）跨场景残留**：对话框被场景切换整体释放时
  不触发 CloseRequested，static 字段继续持有调用方实例引用。修复：`_ExitTree` 兜底清空。

### 复核后确认无需改动（排除项）

- `RotateScreen` 固定模式下 `SetFollowCache(false, persist)`：跟随已关时重复写 false，幂等无害。
- 迁移函数中迁移后重复 `cfg.Load` 一次：无害（结果一致）。
- `_ExitTree` 仅在 `lastRequestedLandscape` 时恢复 Sensor：跟随（FULL_SENSOR）路径不恢复，
  但 app 退出即 Activity 销毁、方向请求随之消失，无实际影响。
- `QuickButtons` 竖屏 `rightMargin += 64` 仅用于贴右分支：flip（贴左）时不用，无浪费；
  flip+竖屏时顶部仍让出 64px 属轻微冗余（无害，不改）。
- `ApplyOrientationMode("resume")` 在多窗口+跟随开启时会短暂应用 FULL_SENSOR，
  随后 deferred `Evaluate` 立即纠正为小窗暂停态——窗口期一个帧内，可接受。
- 三处 `Disabled` 旋转入口在 PC 上不可点，`RotateScreen` 自身再有 OS 检查兜底，防御完整。

### 遗留（此前已登记，本轮复核维持）

- P1 真机验证：FULL_SENSOR 各 OEM 接受度、守护在真机的实际效果、快捷面板新默认位置。
- P2 菜单栏贴顶未避让刘海（补遗 12）；倒持方向不支持（补遗 12）。

验证 [实测]：`dotnet build` 0 error（仅既存无关 warning）。

> 补遗 16（2026-11-18，用户反馈「横屏后看不见快捷键按钮/菜单」）
>
> 根因：补遗 15 改竖排时，横屏分支的 `menuRoot.GrowVertical` 误写成 `Begin`（向上伸展，
> 从竖屏分支复制后未改方向语义）。菜单栏锚定在顶部（AnchorTop=0 + OffsetTop=4），
> 向上伸展 = 整条栏被顶出屏幕上方。实测竖→横切换后 `root.pos=(-30.5, -68)`——栏体
> 完全在可视区外，用户看到的就是「菜单/快捷入口消失」。
>
> 修复：横屏分支 `GrowVertical = End`（与竖屏同向，都从顶部锚点向下伸展）。
> 实测切换后 `root.pos=(-30.5, 4.0)`，横屏截图确认 12 图标横排 + toggle 右端全部可见。
> x 方向 -30.5 为测试窗口逻辑宽 867 < 栏宽 894 的左缘裁切，真机横屏逻辑宽 ~1212
> 不会触发（13×48+间距 ≈ 708）。
>
> 教训：方向切换类代码，两个分支的 Grow 方向必须各自对着锚点语义推一遍，
> 不能从另一分支复制后只改 Vertical——本轮正是「复制后忘改方向」引入回归。
>
> 验证 [实测]：桌面竖屏 560x1200 展开 → 切 1200x560 → dump pos/size + 截图；
> `dotnet build` 0 error。

> 补遗 13（2026-11-22，推翻补遗 11 的「贴顶」决策）
>
> 用户裁定：快捷面板垂直恒贴底、默认右下角——补遗 11 的垂直贴顶（含悬浮浮层
> 默认贴菜单栏）已回退；「Quick面板翻转」选项保留，语义收敛为纯位置换边：
> 翻转后=左下角（内容不镜像）。竖屏右侧菜单栏避让（本文件补遗 11 第 2 条）
> 仍然保留。详情与验证记录见
> [2026-11-22-quick-panel-pin-bottom-right.md](2026-11-22-quick-panel-pin-bottom-right.md)。

> 补遗 17（2026-11-22，用户报「调整字体大小后图片比字体小、被压下去、立绘只加载一部分」）
>
> 根因（代码取证）：`ConsoleImagePart` 的几何在打印时刻一次性算死——百分比尺寸按当时
> `Config.FontSize` 换算（随字号缩放 ✓），但 **px 尺寸是绝对值**（HTML_PRINT 的
> `height='40px'`）。运行时字号改大后：文字按新字号渲染变大，px 图像保持原尺寸
> →「图片比字体小」；后续行按新行高排版覆盖图像下缘 →「立绘只加载一部分」。
> 这与 XEmuera `FontScale` 的「布局整体缩放」语义相悖（文档：窗口宽度相应调整）。
>
> 修复（布局整体缩放语义）：
> - `Config.SetRuntimeFontSize`：首次调用时记录 `RuntimeFontSizeBase`（游戏配置基准字号），
>   新增 `RuntimeFontScale = FontSize / RuntimeFontSizeBase`
> - `ConsoleImagePart`：ctor 内 px 尺寸/偏移（height/width/ypos/xpos 的 px 分支）乘以
>   `ScalePxValue = round(v × RuntimeFontScale)`；百分号分支本就随 FontSize 缩放不变
> - 门控：比例仅在使用运行时字号后 ≠1；默认状态逐字节不变（v24pure 语义无漂移）
>
> 验证 [实测]：桌面 + 测试游戏（HTML_PRINT `<img height='40px'>` 前后两行 + WAIT 推进），
> 字号 18→32 后下次文本更新：新行 px 图像 40→71px（=40×32/18）与 32 号文字比例一致，
> 旧行文字/图像保持冻结；构建 0 error。
> 语义确认（用户澄清）：字号改动不立即生效，在下次文本更新时生效——与 dev#2 空闲帧
> 应用设计一致，测试流程据此推进。
> [未验证] 真机表现；PRINTC 密集列布局在 px 图像缩放后的重排。

> 补遗 18（2026-11-22，用户指认 EraTW 所在地察觉网格：名字折两行 + 立绘被裁）
>
> 用户指认具体 ERB：EraTW_Snake `ERB/fromEN/IMG/IMAGE_HELPERS.ERB` 的
> `QOL_WHEREABOUTS_OUTSIDE_PRINT_CHARA` → `SET_IMAGE_WITH_HEADER`
> （`ERB/魔改内容/GRAPH系.ERB` 调用，高度=100+修正值百分比）。
> 该函数用 ERB 在打印时把每个「名字+立绘」单元算成 **px div**：
> `<div width='{ELEMENT_WIDTH}px' height='{ELEMENT_HEIGHT}px' xpos='…px'><p>名字</p><img …></div>`，
> 其中 ELEMENT_WIDTH 依赖 `デフォルトキャラ画像横幅`（常量）与 `CLIENTWIDTH()/DRAWLINESTR` 估宽——
> 均不随运行时字号缩放。字号调大后：div 内 `<p>` 名字与 `<img>` 按新字号渲染变宽，
> div 的 px 边界不变 → 三个字的名字折两行、立绘被 div 裁掉下半（截图与用户描述逐项吻合）。
>
> 修复（与 ConsoleImagePart px 缩放同语义，补齐 div 侧）：
> - `ConsoleDivPart.ToPixel`：px 分支乘 `Config.RuntimeFontScale`（HtmlManager 对 div
>   width/height/xpos 与 margin/padding/border/radius 的全部 px 换算都经过该方法）
> - `ConsoleShapePart`：px 参数分支同样缩放（所在地面板的分隔竖条）
>
> 验证 [实测]：桌面 + px-div 测试游戏（`<div width='120px' height='110px'>` 内含
> `<p>三字名</p>` + `<img height='40px'>`）：字号 18→32 后下次文本更新，新 div 整体
> 放大 ~1.78×（约 213px）——名字一行放下、立绘完整显示；旧行冻结；构建 0 error。
> EraTW 本体（snake 包）真机复测待用户。

> 补遗 19（2026-11-23，用户实测：18 完整 / 36 立绘被裁——GETCONFIG 基准漂移根因）
>
> 现象与测量：用户两张真机截图（EraTW 所在地察觉）像素测量——字号 18/36 下立绘
> 像素宽度完全相同（111-120px），36 时立绘被推到视口外（colored y 至 1279 屏底截断）。
> 即：**图片尺寸没变，布局溢出了**。
>
> 根因（数学推导 + 引擎复现）：EraTW グラフィック表示ライブラリ的
> `画像表示単独HTML` 计算 `<img height='{常量×比率÷GETCONFIG("フォントサイズ")}'>`
> ——设计意图：除以基准字号再乘回（渲染=运行时字号×百分比）→ 图片 px 随字号**等比放大**。
> 但 dev#2 的 `SetRuntimeFontSize` 把 ConfigData 同步成运行时字号 → GETCONFIG 基准
> 随字号漂移 → 除法与乘法相消 → 图片 px 恒定不变。配合补遗 18 的 div px 缩放（×2），
> 结果：字 2×、div 2×、图片 1× → 「图片比字体小」+ 网格总高 2× 溢出视口 → 「立绘
> 只显示一部分」。
>
> 修复：`Config.SetRuntimeFontSize` 不再同步 ConfigData 的 FontSize/LineHeight——
> GETCONFIG 恒报游戏配置基准（XEmuera 语义：配置=基准、FontScale=运行时乘数）。
> 自洽性验证（全部成立）：图片=常量×(运行时/基准)✓、div/行高推进 ✓、
> ypos 行补偿=行数×lh_base×scale=行数×lh_runtime ✓。
>
> 排查弯路（记录防重蹈）：
> 1. 自建复现中把两张重叠圆盘的并集误读为「被平切」，据此跑了两轮二分实验
> （放大行/改 ZIndex），后经像素 diff（bbox=None）发现两轮实验**全无效果**——
> 此前"解锁整圆"纯属看串对比格。教训：**UI 对比结论必须先做像素 diff/精确色块
> 测量，肉眼对比格在元素重叠时会系统性误导**。
> 2. 场景树黑盒转储三轮（clip 标志/祖先链/DrawSize）均无裁剪者——方向本身就错了
> （复现没有 bug）。有效诊断是对**用户原始截图**做精确色块测量（立绘同宽+溢出）。
>
> 验证 [实测]：桌面 + 真实公式测试游戏（两行完全相同 html——真实 ERB 在基准
> GETCONFIG 下任何字号输出同样的标签）：字号 18→32 后第二行立绘 y40-175
> （修复前 40-104）≈2× 完整渲染、蓝条完整、与第一行比例逐项一致；构建 0 error。
> [未验证] EraTW 真机复测（待用户）。

> 补遗 20（2026-11-23，用户实测「36 明显布局紧凑、18 宽松」——WindowX 未随字号缩放）
>
> 根因：排版级缩放的 px 覆盖清单此前有 img/div/shape，唯独漏了**窗口布局宽度**
> `Config.WindowX`（→DrawableWidth）。XEmuera FontScale 文档明确「窗口宽度相应调整」；
> 不缩放时：每行可容纳字符数随字号减半（文本折行位置漂移）、ERB 按 `CLIENTWIDTH()`
> 的字体无关估宽全部偏小（SET_IMAGE_WITH_HEADER 的 ELEMENT_WIDTH 等）——两层叠加
> 造成字号越大布局越挤。
>
> 修复：
> - `Config.SetRuntimeFontSize`：首次调用同时记录 `RuntimeWindowXBase`，此后
>   `WindowX = RuntimeWindowXBase × (字号/基准)`（GETCONFIG 窗口幅仍报基准，ConfigData
>   不同步——与字号基准语义一致）
> - `ApplyFontSize` 追加 `ApplyMobileViewportPolicy()`：移动端按新 DrawableWidth 重算
>   fit（走既有补偿管线，防横向溢出滚动）；桌面端该调用直接返回
>
> 至此 px 覆盖清单完整：img px + div/shape px + WindowX/DrawableWidth，百分比属性走
> 运行时字号——字号改动在下次文本更新后等价于「整个控制台按倍率重排版的清晰放大」。
>
> 验证 [实测]：桌面 + 测试游戏（70 字符长行 + 短行 ×4 + WAIT 推进）：字号 18→32 后
> 长行折行列位置与 18 完全相同（右缘同为 x≈418/424），行列结构一致、字形 2×——
> 布局等价于整体放大，紧凑感消除；构建 0 error。[未验证] 真机移动端 refit 后的
> 物理呈现。

> 补遗 21（2026-11-23，用户报「排版问题，间距太大，图片渲染位置出错」）
>
> 排查历程（多轮测量陷阱，供复盘）：
> 1. **僵尸进程污染**：一个 11-21 启动的 Godot 进程一直在后台跑旧 ERB 并覆盖截图文件，
>    导致多轮"改了没生效/复现了"实为读取陈旧截图。清理后重建原子化流程（运行→测量单命令完成）。
> 2. **launcher.cfg 回退丢失测试路径** → 游戏回落 AkumaMaid（Now Loading 卡住，控制台全黑）。
> 3. **复现 ERB 两处建模错误**：双 `<p>` 触发「行頭以外」解析错误（需每格 div 包裹）；
>    ypos 漏了 描画開始行数 每批重置（第 2 行起 -700% 把图甩出屏）。
> 4. 中间一次"21px 新带"被判为重渲染 bug，实为脚本末尾错误信息按新字号打印（正常行为）。
>
> 真实发现与修复：
> - **`RefreshFontSize` 违反"旧行冻结"契约**：对 lineContainer 既有子控件重设 font_size 并
>   MarkDirty 重绘画布 → 字号变更后旧文本按新字号重绘、div 内图片停在原位（内容重复/错位）。
>   修复：移除该段，仅刷新辅助 UI（pads/msgbox）。
> - **决定性验证（用户场景：改字号→界面重新打印）**：按 EraTW 精确公式（D=180, ZOOM=60,
>   GETCONFIG=基准, 每格 div 127px + img height 600% + ypos -105 + 6 行预留）构建测试游戏，
>   字号 18→32 后新打印行：脸 49→89px、行距 86→154px、名字 12→22px——全部 1.8× 成比例，
>   名字-脸相对位置一致。**桌面复现下引擎行为正确**。
>
> 结论：用户截图所示症状大概率产自 GETCONFIG 基准修复（750f60a）/WindowX 修复（64989cc）
> 之前的构建；HEAD（含本轮 RefreshFontSize 冻结）在精确公式复现下已全部成比例。
> 待用户以 HEAD 构建真机复测 EraTW 所在地界面确认。
> 验证 [实测]：上述比例测量（3 次运行稳定）；构建 0 error。

> 补遗 22（2026-11-23，用户真机复测通过「没问题了」——字号排版缩放链路验收闭环与任务收尾）
>
> 真机结论 [实测]：用户以自建 APK（项目根 `gemuera-1.10.0-dev-20261122-debug.apk`，文件时间
> 2026-11-23 10:38，晚于 HEAD `bea08b7` 的提交时间 09:43）在真机复测，原话「没问题了」（场景=此前各轮
> 反馈的 EraTW 所在地界面调整字号）。据此关闭三条 [未验证]：补遗 19「EraTW 真机复测」、补遗 20「真机移动端 refit 后的
> 物理呈现」、补遗 21「HEAD 构建真机复测」。本分支字号链路（排版级缩放 `RequestFontSize` →
> `Config.SetRuntimeFontSize`；px 图像/div/shape 与 `WindowX` 乘 `Config.RuntimeFontScale`；GETCONFIG
> 恒报基准；已显示行冻结）验收通过。
> 仍为 [未验证]：PRINTC 密集列布局（EraTW 所在地是 div 网格，不经过该路径），已入台账。
>
> 收尾核对发现的事故——桌面 `launcher.cfg` 被还原成了错误快照：
> - 现象：`%APPDATA%\Godot\app_userdata\gemuera-c#\launcher.cfg`（我 09:43:14 写入，与提交 `bea08b7`
>   同一分钟）和 11-20 的旧快照 `lc10.bak` 逐字节相同 [实测：`cmp`]；而最后一次测试开始前的真实快照是
>   `lc13.bak`（2026-11-23 00:39）。即收尾还原时用错了备份，用户配置被回退 3 天。四处差异：
>   `last_game_path`（EraTW-Magic_DLC-update_base → EraTW_Snake_Beta-open_source）、多出一行 `manual_games`、
>   `emuera_debug_mode` / `debug_show_window` 两个调试开关被打开、Magic DLC 游戏的兼容包选择
>   （`[compat_packs]` 第二行）丢失。
> - 根因：还原命令里的快照编号是凭记忆手写的；另外两次测试中间态快照（`lc14`/`lc15`）显示，测试期间
>   该文件曾只剩指向测试游戏的 131 字节残片——测试直接改写了用户配置，没有隔离。
> - 处置 [实测]：现文件先备份到仓库外 `D:\gemuera-reports\launcher-cfg-backup\launcher.cfg.before-restore-20261123-094314`，
>   再以 `lc13.bak` 覆盖，`cmp` 逐字节一致；`lc13` 引用的两个兼容包 DLL 均在磁盘上。18 份历史配置快照
>   （`launcher.cfg.orig`、`settings.cfg.bak`、`cfg.bak`、`lc`–`lc15`）移至同目录 `history\` 保留，
>   `/tmp` 不再留本任务文件。`settings.cfg` 未被 11-23 的测试运行改动（mtime 2026-11-22 19:47，早于当天全部测试运行）。
>
> 其他收尾 [实测]：
> - 已删除：测试游戏 `D:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\testgame-img\`、
>   测试 APK `D:\gemuera\export\releases\gemuera-mobtest.apk`（131 MB）、`/tmp` 下我的源码备份与构建/导出日志。
> - 保留（均在仓库外）：`D:\gemuera-reports\ui-scale-verify\`（各补遗引用的截图证据）、
>   `D:\gemuera-reports\uEmuera-ref\`（uEmuera 参考源码）。
> - 未能清理：模拟器内 `/storage/emulated/0/emuera/testgame-img`（`adb devices` 为空，设备未连接）→ 台账 P2。
> - 基线对照（`git status --short`，不属于本任务、未提交）：`export_presets.cfg`（版本号/导出路径变更，
>   非本任务所改）、`governance/backlog.md` 的「Emuera 36A 方言评估」段（另一会话未提交）、两份未跟踪
>   evolution-log（`2026-11-22-console-ui-analysis.md`、`2026-11-23-emuera36a-changelog-adaptation-assessment.md`）、
>   7 个 `.uid`（Godot 编辑器为脚本生成的侧车文件，非本任务创建）。本提交对 `backlog.md` 只暂存本任务新增段。
>
> AI 表现复盘：
> - 有效：收尾时没有默认"还原过了就没问题"，而是把现文件与每份快照逐个 `cmp`，才发现回退。可复用动作：
>   **测后还原必须 `cmp <live> <快照>`，并核对所用快照的时间戳确实是"最后一次测试开始前"**。
> - 低效/返工：`launcher.cfg` 还原用错快照。触发点：提交 `bea08b7` 的收尾命令手写 `lc10`；可核对后果：
>   用户配置在 09:43–17:41 处于 11-20 的旧状态（约 8 小时），返工 1 次。这是本任务第三次用户配置事故
>   （11-16 删 `settings.cfg`、11-16 手写无引号 `Language`、本次错快照还原）。
> - 低效：模拟器自动化跑字号路径多轮未跑通（被启动即现的旋转选择弹窗与点击坐标卡住），最终由用户真机复测闭环。
>   可复用动作：涉及启动期弹窗/方向切换的移动端自动化，先请用户真机复测，或先把跟随开关置为固定方向再自动化。
>
> 教训 → 优化动作：
> - (a) 已执行：`AGENTS.md`「构建与验证」新增「桌面窗口模式测试不得破坏用户真实配置」一条（同类教训已 3 次，
>   按沉淀阶梯升级为首读铁律）；`governance/prompt-patterns/README.md` 新增模式 10「参考实现裁决型」
>   （用户问「X 和 Y 的 API 是不是一样 / 有什么区别 / 参考实现怎么做」时，先给参考实现的语义对照再动手）。
> - (b) 本轮不做：不把配置备份/还原做成脚本或门禁。升级去向：暂不自动化（手工两条 `cp` + 一条 `cmp`，
>   脚本化收益低于成本；若再犯，则在 `tools/governance/Test-Governance.ps1` 加检查）。
>
> 遗留登记：本 PR 三份日志 §6 的 open 项与本次新增项已整体迁入 `governance/backlog.md`
> 「界面缩放 / 沉浸全屏 / 方向锁定旋转 / 字号排版缩放」段。
> 关闭：
> - 真机验证方向锁定下旋转（仅用户机型）——证据：用户自建 APK 在真机经历多轮旋转反馈闭环（曾报
>   「对于真机还是有强制竖屏的问题」，修复后未再出现；补遗 7 的真机截图；补遗 16「横屏后看不见菜单，
>   自动手动都是」→ 修复后原话「好了」；解释 FULL_SENSOR 后原话「没问题」）。「方向锁定开启」这一前提
>   按用户最初诉求的使用场景推定，未经我方复核；其他机型另立台账条目。
> - 字号排版缩放真机验收（本补遗）。
> - 菜单 toggle 与栏体未对齐（补遗 15 已修，用户原话「没问题」）。
> 维持 open：各条原文见 §6 与 §7，现状见台账。

> 补遗 23（2026-11-23，收尾核对分支交付状态——PR #4 / PR #10 均已合并，后续提交待开 PR）
>
> 核对 [实测]（GitHub API `repos/524122/gEmuera/pulls?state=all` + `git fetch origin dev` 后
> `git log origin/dev..HEAD`，2026-11-23 17:57）：
> - PR #4（`ai/ui-scale-setting` → `dev`，head `576d62c`，标题「系统级 UI 缩放 / 安卓沉浸全屏 / 方向锁定下
>   跟随手机旋转」）已于 2026-11-21 02:44 合并。
> - PR #10（同一分支，head `948395c`，批次标题「UI 快捷面板/方向守护/兼容页签 + 原生化五模块 + 解析缓存
>   全生命周期退役闭环」）已于 2026-11-22 17:38 合并；`dev` tip = 合并提交 `203ff72`，其树与 `948395c`
>   相同（从本分支再开 PR 无冲突）。
> - 此后分支新增、尚未进入 `dev` 且没有任何开启的 PR 的提交：`2774415`（px 图像随字号缩放）、
>   `b3d4df9`（悬浮面板位置持久化退役 + 系统菜单「面板回位」；**另一会话的提交**，记录在
>   `evolution-log/2026-11-22-quick-panel-pin-bottom-right.md`）、`9233e97`（px div/shape）、
>   `750f60a`（GETCONFIG 恒报基准）、`64989cc`（WindowX 随字号缩放）、`bea08b7`（已显示行冻结）、
>   `dee6b36`（补遗 22 的收尾文档）。
>
> 澄清：补遗 17–22 里的「本 PR」指分支 `ai/ui-scale-setting` 上的这条工作线，不代表这些改动都已随 PR #4
> 合入；字号排版缩放的 5 个修复提交真机验收已通过（补遗 22）但**仍待开 PR**。
>
> AI 表现复盘：会话从摘要恢复时沿用了「PR #4 → dev 仍开启」的旧认知，还准备去改它的描述；直到收尾读取
> PR 列表才发现它两天前已合并（原计划中的「PR #4 描述过期、是否更新」一事作废）。可复用动作：恢复会话与
> 收尾时先跑 `git fetch origin dev && git log --oneline origin/dev..HEAD` 并查 PR 列表，再谈 PR 相关动作。
>
> 遗留登记：为上述提交开 PR → 待用户决定，优先级 P1，已入 `governance/backlog.md` 本任务段首条。

> 补遗 24（2026-11-23，用户指示「pr」——PR #11 已创建）
>
> 用户看过收尾汇报（含「未合并的 8 个提交」清单）后回复「pr」，据此创建 PR #11：
> https://github.com/524122/gEmuera/pull/11 （`ai/ui-scale-setting` → `dev`，非草稿，8 个提交、17 个文件，
> head `5b57645`）。GitHub 判定 `mergeable=true` / `mergeable_state=clean` [实测：GitHub API，2026-11-23 18:09]。
>
> 标题：「fix(display): 字号排版缩放补全（px 图像/div/shape/WindowX、GETCONFIG 恒报基准、旧行冻结） /
> 悬浮面板位置持久化退役」。描述按归属分三段——字号排版 5 个提交 / 另一会话的 `b3d4df9`（含其
> `tools/launcher-compat-tab/` 验收脚本）/ 治理文档 2 个提交；验证记录沿用补遗 17–22 的 [实测]，保留两条
> [未验证]（PRINTC 密集列布局、悬浮面板提交的真机验收）；含「进化记录」行。
>
> 创建前核对 [实测]：`pulls?state=open` 返回 0 条（无重复 PR）；请求体回读与拟稿逐字一致后才提交；
> 提交后读取 PR 确认 8 提交 / 17 文件与 `git diff --stat origin/dev...HEAD` 一致。
>
> 遗留登记：PR #11 待用户合并；合并后把 `governance/backlog.md` 本任务段首条改为 closed。

> 补遗 25（2026-11-23，PR #11 已合并——台账闭环）
>
> [实测]（GitHub API + `git fetch origin dev`，2026-11-23 18:12）：PR #11 于 2026-11-23 18:10:33 合并（合并提交
> `a6d9b24`，合并时 head=`5b57645`，8 个提交 / 17 个文件）。字号排版缩放的 5 个修复与另一会话的 `b3d4df9`
> 至此全部进入 `dev`；`git log origin/dev..5b57645` 为空。
>
> PR 后变更：补遗 24 所在的 `c358bb8`（18:10 推送）比合并晚数秒，落在合并之后；它与本补遗所在的提交都是
> 纯文档的合并后提交，尚未进入 `dev`。台账本任务段首条已据此改为 closed。
>
> 复盘：PR 创建后的记账提交必然晚于 PR 本身，用户在分钟内合并时它就会落到合并后——属正常现象，
> 下一个 PR 带上即可，无需返工。悬浮面板提交（`b3d4df9`）的真机验收仍按台账「快捷面板 / 屏幕方向」段 P1 跟踪。

> 补遗 26（2026-11-23，用户对两条遗留的裁定）
>
> 用户在收尾汇报上逐条批注（原话）：
> - 模拟器内残留测试游戏 `/storage/emulated/0/emuera/testgame-img`：「不管」→ 不处理。台账本任务段该条
>   改为 closed（保留清理命令备查，推翻补遗 22 的「未能清理 → 台账 P2」安排）。
> - 悬浮面板提交 `b3d4df9` 的真机验收（台账「快捷面板 / 屏幕方向」段 P1「真机 APK 验收」）：「测试过了，
>   没问题有问题后续在说」→ 用户自测确认通过，该条改为 closed。①–⑦ 逐项清单未由我方复核（[实测-用户口述]，
>   无截图/日志佐证）；后续如有问题，仍按 `evolution-log/2026-11-22-quick-panel-pin-bottom-right.md` 对应段回修。
>   此条推翻补遗 25 末句「仍按台账 … P1 跟踪」。
>
> 落点：`governance/backlog.md` 两条状态改动（快捷面板段 P1、本任务段末条 P2）+ 本任务段首条的交叉引用措辞。
