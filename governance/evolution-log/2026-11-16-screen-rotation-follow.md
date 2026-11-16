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
