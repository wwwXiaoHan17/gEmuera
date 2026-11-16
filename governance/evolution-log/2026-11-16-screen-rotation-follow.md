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
