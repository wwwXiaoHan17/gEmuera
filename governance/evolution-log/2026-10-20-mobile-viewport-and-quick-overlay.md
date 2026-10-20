# 2026-10-20 移动端画布尺寸与 Quick 悬浮窗横竖屏

## 问题 1：移动端界面大小与桌面不一致

此前为满足“横竖屏都用竖屏宽高”的建议，Android 启动时把
`emuera.config` 的 `WindowX/Y` 做了长短边归一化（例如 1340x940 → 940x1340），
`ApplyMobileViewportPolicy` 又只按宽度 fit。结果是：

- 桌面端仍是游戏原始的 1340x940 逻辑画布；
- Android 横屏被压成 940 宽的逻辑画布，再以 1x 显示，界面明显变小；
- 竖屏/横屏的界面比例也与桌面不一致。

修复：

- `Program.ApplyAndroidWindowWidthPolicy` 不再归一化 `WindowX/Y`，严格保留游戏
  `emuera.config` 的 1340x940 逻辑画布；
- `EmueraContent.ApplyMobileViewportPolicy` 按 **宽度和高度同时约束** 计算 fit：
  `min(safeWidth/drawableWidth, safeHeight/windowY)`，clamp 到合理范围；
- 旋转只重算 fit，不改逻辑行宽/行高，继续避免横竖屏重排。

## 问题 2：横屏挂载 Quick 悬浮窗后自动变竖屏

`EmueraContent` 原先用 `OS.HasFeature("mobile")` 判断 Android 悬浮宿主：

```csharp
bool wantsWindow = wantsFloating && !OS.HasFeature("mobile");
```

如果 Android 构建/调试环境没有暴露 `mobile` feature tag，就会错误地创建
`QuickFloatingWindow`（原生 Godot `Window` 宿主）。原生子窗口在 Android 上会参与
窗口/Activity 生命周期，可能触发方向重建或让主窗口按竖屏重建。

修复：

- 新增 `EmueraContent.IsMobilePlatform = OS.HasFeature("mobile") || OS.GetName()=="Android"`；
- Quick 悬浮宿主、safety area、移动端 max lines 等统一使用该判断；
- Android 上即使 feature tag 缺失，也强制走**画布内 overlay**（`FloatingOverlayMode`），
  不再创建原生 `QuickFloatingWindow`，因此不会因挂载悬浮窗改变 Activity 方向。

## 验证

- 构建 0 error。
- 桌面 eratoho v24pure 回归 `Passed`，`semanticSha 4859125d3f98…` 不变；桌面画布逻辑
  未改变（IsMobilePlatform 为 false）。
- Android 真机/模拟器当前不可用（adb 无设备），需要用户安装新 APK 验证：
  1. 横屏时游戏界面是否与桌面比例一致；
  2. 横屏挂载 Quick 悬浮窗是否保持横屏。

## 相关

- `scripts/Emuera/Program.cs`
- `scripts/EmueraContent.cs`
- `scripts/Panels/QuickButtons.cs`（overlay 模式逻辑未改，只改宿主选择）
