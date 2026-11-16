# Android 完全全屏（沉浸模式）（2026-11-16）

> 前一份同分支记录：[系统级 UI 缩放设置](2026-11-16-ui-scale-setting.md)（本任务是用户在其之后提出的独立需求）。

## 1. 任务目标与成果

用户要求「安卓上实现完全全屏」——隐藏状态栏与导航栏，画面铺满整屏。
交付 = `AndroidWindowOrientationController` 增加窗口模式管理：非小窗形态一律请求 `WindowMode.ExclusiveFullscreen`，进入分屏/浮动小窗时回 `Windowed`，回前台重新评估。
判定 = 模拟器真机级验证通过（真机 e2e 仍是最终验收，见遗留）。

## 2. 关键决策与 Why

1. **运行时 `DisplayServer.WindowSetMode(ExclusiveFullscreen)` 而非只改导出预设**：`export_presets.cfg` 早已有 `screen/immersive_mode=true` 与 `screen/edge_to_edge=true`（已提交基线），用户反馈仍未完全全屏，说明清单级声明不足以覆盖所有场景（沉浸策略可被系统栏滑动唤出或 OEM 策略覆盖）。运行时请求是 Godot 4 在 Android 上隐藏两条系统栏的官方手段（[godot-proposals#8826](https://github.com/godotengine/godot-proposals/issues/8826) 亦确认 `WINDOW_MODE_EXCLUSIVE_FULLSCREEN` 同时隐藏两者，无更细粒度 API）。
2. **挂进既有 `AndroidWindowOrientationController`（autoload）而非新建节点**：该类已拥有 `isInMultiWindowMode` 检测与窗口尺寸边沿去抖，窗口形态是它既有的状态机输入；新建节点会重复查询与生命周期管理。
3. **小窗形态必须回 `Windowed`**：分屏/浮动小窗下系统不接受全屏请求，且强行请求会让小窗布局错乱。窗口模式以 `WindowGetMode()` 读回去重，避免每次边沿都发请求。
4. **`_Notification(ApplicationResumed/FocusIn)` 重新评估**：切前后台可能被系统恢复系统栏，重评估幂等（方向按边沿、模式按读回去重）。
5. **排除项（查过非缺陷）**：顶部黑色区域经 `dumpsys window` 确认为模拟器自带刘海切槽（`cutout insets=Rect(0,142)`），启动器安全区正确避让，不是残留内边距。

## 3. 验证记录

- [实测] 构建：Godot headless `--build-solutions` 两次，error CS 计 0，DLL 时间戳更新。
- [实测] APK 导出（临时启用 `architectures/x86_64=true` 以匹配 x86_64 模拟器，导后已还原为 false）+ `adb install` 成功。
- [实测] 沉浸全屏：模拟器启动后截图**顶部无状态栏（无时钟/电量）、底部无导航栏**，内容从刘海切槽下方开始；175% UI 缩放下同样保持全屏。
- [实测] 窗口模式请求无异常：logcat 无 `[WINDOWMODE] request failed` 告警（该告警路径会写 logcat）。
- [实测] 运行时行为对照：清数据 → 启动 → `settings.cfg` 不存在（启动不写配置）；点选择器选 175% → 写入 `UiScale=1.75` 且出现 `UI-SCALE-PERSIST` 调用栈（插桩版）；重启 → 读回 1.75。
- [实测] 顺手修复的既有缺陷：`GetWindow().SizeChanged` 与 `GetViewport().SizeChanged` 在 Android 上是同一根窗口对象，重复 Connect 报 `Signal 'size_changed' is already connected`（logcat 实证）；改为同一对象时只连一次，修复后新会话无该报错。
- [实测] 配置写入健壮性加固（代码随 UI 缩放 PR `ai/ui-scale-setting` 提交，不属本 PR 改动范围）：`UiScaleHelper.SetScale` 原先 `ConfigFile.Load` 忽略返回值，文件解析失败时 Save 会把 Language/分辨率等其他键覆盖丢失；改为仅 `Ok`/`FileNotFound` 才写盘。
- [未验证] 真机（arm64）验证：模拟器为 x86_64 + 16KB 页镜像，系统弹「16KB 兼容性」提示（Godot 官方模板库非 16KB 对齐，兼容模式运行），与本次改动无关但影响证据强度。
- 环境障碍与打通：AVD `Pixel_10` 设备定义损坏（`avdmanager` 报 "Google pixel_10 no longer exists"）但已存在实例可直接启动；Gradle 未全局安装，导出走 Godot 内置 gradle wrapper 成功。

## 4. AI 表现复盘

- **有效**：先用 `dumpsys window` 取到切槽几何（`insets=Rect(0,142)`）再判断黑边性质，避免把正确的刘海避让误判成布局 bug；插桩 `SetScale(persist:true)` 打印调用栈，一次运行即证明「启动路径 0 次写盘」，把排查范围从"应用 bug"收敛到"测试误触"。
- **低效**：模拟器上排查 `UiScale=1.75` 的异常值耗费多轮（约 4 次导出/安装循环）。事后用 PIL 测出下拉菜单项纵坐标（75%→200% 依次 y≈213/238/262/288/314/339/364），确认我先前 tap 的 `(1005,215)` 正落在菜单项带内——**是我自己的连续点击选中了 175%，不是应用缺陷**。可复用动作：Android 上点 Godot 下拉前，先用 PIL 量出目标控件/菜单项的精确 bbox，不要凭截图目测换算。
- **险些踩的坑**：预设里 `arquitectures/x86_64=false`（只构建 arm64）会让 x86_64 模拟器装不上；临时改为 true 后**必须记得还原**（本轮两次导出后均还原并 grep 复核）。

## 5. 教训 → 具体优化动作

- (a) 本轮已执行：修复 `AndroidWindowOrientationController._Ready` 重复 Connect（`scripts/GodotHost/AndroidWindowOrientationController.cs`，同对象时只连一次）。
- (a) 已执行但归属另一 PR：`UiScaleHelper.SetScale` 增加 Load 失败保护（`scripts/UiScaleHelper.cs`，随 `ai/ui-scale-setting` 分支提交；本分支不含该文件改动）。
- (b) 本轮不做：「Android 自动化验证需先量 bbox」写入 AGENTS.md。升级去向：暂不升级（单次成本、非复发；若第二次踩再升级到规范）。

## 6. 遗留登记

- P1：真机（arm64，非 16KB 页镜像）验证沉浸全屏。为什么不在本轮做：无实体设备接入，模拟器镜像为 16KB 页并弹兼容提示。触发条件：下次 APK 发布前在真机核对状态栏/导航栏隐藏与刘海避让。
- P2：Godot 4.7 Android 模板库非 16KB 页对齐（模拟器提示 `libgodot_android.so: Unknown error` 等）。为什么不在本轮做：属引擎/模板侧问题，需升级 Godot 版本或等官方修复，超出本项目代码范围。
- P2：系统栏可被用户下滑唤出（Android 沉浸模式固有行为，需 `BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE` 或粘性沉浸才能完全锁定）。为什么不在本轮做：Godot 未暴露该策略开关，需改 Android 清单/原生代码，且该行为对游戏可访问性（返回手势）有益。
