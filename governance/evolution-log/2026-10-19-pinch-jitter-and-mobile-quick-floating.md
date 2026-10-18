# 2026-10-19 双指缩放抖动修复 + Quick 悬浮窗移动端支持与优化

## 任务

1. 修复双指缩放导致屏幕抽搐。
2. 修复「误认为 quick 悬浮窗必须使用 Windows 原生窗口组件、因此在移动端不可用」的问题。
3. 修复并优化 quick 悬浮窗功能。

## 一、双指缩放抽搐

### 根因

旧实现 `EmueraContent.UpdateContentPinchZoom` 每帧调用 `SetContentScaleKeepingFocus(targetScale, center)`，
其中焦点内容点由**上一帧的 scroll + 当前捏合中心**反推：

```
contentFocus = (previousScroll + localFocus) / previousScale
```

`RestoreContentScaleFocus` 又把目标 scroll 取整写回 `ScrollContainer`，下一帧再用取整后的 scroll
反推内容点——形成整数取整反馈环；加上原始触摸 spread 的采样噪声直接映射成 scale 抖动，
表现为双指缩放时画面持续抽搐（手指不动也抖）。

### 修复（`Scripts/EmueraContent.cs`）

- 手势开始时捕获**固定内容锚点** `contentPinchFocusContent`（捏合中心下的内容坐标），整个手势期间
  只做 `scroll = anchor * scale - currentCenter`，不再从上一帧 scroll 反推焦点，消除反馈/取整抖动。
- 触摸原始 spread/center 加低通滤波（每帧向原始值收敛 0.35），抹平触摸采样噪声。
- 每次 pinch dirty 帧同时应用缩放与双指平移（旧实现只在 scale 变化时才滚），并按 `ClampContentScale`
  限制；到达缩放上下限时重算基线锚点。
- 缩放范围、死区、双指最小间距等既有策略不变。

## 二、Quick 悬浮窗移动端支持（去掉“必须 Windows 原生窗口”的误解）

### 旧实现的问题

`QuickFloatingWindow : Window` 使用原生标题栏/Transient OS 窗口；`EmueraContent` 以
`!OS.HasFeature("mobile")` 门控，`OptionWindow` 在移动端直接禁用开关，注释也写着
“Android 嵌入 Window 在 gl_compatibility 下不渲染”。这等于把“悬浮面板”与“Windows 原生窗口”
绑定，移动端无法使用。

### 修复与优化

- `QuickButtons` 新增 `FloatingOverlayMode`：面板不放进任何 `Window`，留在主画布内，由
  `floatingPanelPosition` 决定位置（安全区内钳制）；顶部新增专用拖动条（`⋮⋮`）负责移动，
  不依赖 OS 标题栏；位置写入 `user://settings.cfg [Display] QuickFloatingX/Y`，重启保留。
- `EmueraContent.MountQuickButtonsHost` / `RefreshQuickHost` 改为按平台选宿主：
  - 桌面 → 继续使用 `QuickFloatingWindow` 原生窗口（保留原体验）；
  - 移动/无原生窗口 → `FloatingOverlayMode` 画布浮层（纯 Godot Control，Compat 渲染器可用）。
- `OptionWindow` 的「Quick 悬浮窗」开关移动端不再禁用；tooltip/文案更新为“桌面原生窗口 /
  移动端画布浮层（顶部拖动条，位置自动保存）”，四语同步。
- `QuickFloatingWindow` 增加桌面窗口位置持久化（`QuickFloatingWindowX/Y`，显隐/销毁时保存），
  首次显示恢复上次位置并钳制到主窗口工作区。

## 验证

- `dotnet build gemuera-c#.csproj`：0 error。
- 临时 C# headless 探针（已删除）实例化 `quick_buttons.tscn` 并开启 `FloatingOverlayMode`：
  1152×648 下默认位置 `(1042,556)`；设置按钮后拖动条存在（`⋮⋮`）；`MoveFloatingPanelBy(40,30)`
  → `(1062,576)`，在安全区右/下边界被正确钳制，位置变更生效。
- legacy-runner v24pure × eratohoYMAEM：`PassedRunnerConsistency`（semanticSha `4859125d…`），
  缩放/浮窗改动未破坏游戏启动链路。
- 三冒烟 + `CoreContractSmoke` + xUnit 131+35+3 + `Test-Governance.ps1` 4/4 全绿。
- **未验证**：真机双指缩放手感（需要 Android 触屏实测）、Android 浮层拖动条触控边界、
  桌面原生悬浮窗位置持久化的多显示器场景。桌面端悬停/拖动浮层手感建议复测。

## 关键决策

- 不在移动端强行启用 `Window` 宿主：`Window` 是 OS/嵌入窗口抽象，在 Compat 渲染器上历史证据
  不可靠；画布内浮层是纯 Control 布局，与渲染后端无关，最稳妥且不引入平台组件依赖。
- 桌面保留原生窗口：已有原生标题栏拖动/关闭的用户预期不变，只补位置持久化。
- 缩放锚点固定 + 触摸低通滤波同时处理“数学反馈抖动”和“输入采样噪声”两类来源，避免只治其一。
