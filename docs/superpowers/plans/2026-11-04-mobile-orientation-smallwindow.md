# 移动端横竖屏与系统浮动小窗适配 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复「横竖屏切换后游戏内控制台滑不到底」与「系统浮动小窗默认竖屏」两个问题，交付带内置诊断的 debug APK（1.10.0-dev-20261104）供小米真机验收。

**Architecture:** 问题1：把视口/安全区变化收敛为单一 dirty+deferred 布局入口，滚动范围以布局稳定后 ScrollContainer 的真实 vbar Max/Page 为权威做一次对账（纯数学抽到 GEmuera.Core 由 xUnit TDD 覆盖，Godot 行为由 GDUnit4 场景测试取证）。问题2：Autoload 级方向控制器轮询 Android Runtime 的 isInMultiWindowMode，进入小窗请求 SensorLandscape、退出恢复 Sensor；全程诊断日志 + 状态面板 + 设置页开关。

**Tech Stack:** Godot 4.7 mono / C# (net8.0 desktop, net9.0 android) / GDUnit4 6.1.3 / xUnit / PowerShell 门禁 / Android SDK aapt2 / adb 侧真机（用户执行）。

**Spec:** docs/superpowers/specs/2026-11-04-mobile-orientation-smallwindow-design.md

## Global Constraints

- 分支：`fix/mobile-orientation-smallwindow`；完成后 PR 到 `dev`；禁止直接提交 dev。
- 版本：APK `versionName=1.10.0-dev-20261104`、`versionCode=11`、导出路径 `export/releases/gemuera-1.10.0-dev-20261104-debug.apk`。
- Godot 可执行文件：`C:\Users\30873\Desktop\Godot\Mono\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe`（下文记作 `$GODOT`）。
- 构建判定：`& $GODOT --headless --path . --build-solutions --quit` 后 `.godot/mono/temp/bin/Debug/gemuera-c#.dll` 时间戳更新且 `error CS/MSB` 为 0；Godot 收尾可能挂起，用 Start-Process+WaitForExit(180s)+必要时 Stop-Process，只以 DLL 时间戳判定（AGENTS.md）。
- 门禁基线（2026-11-05 实测）：三冒烟全 pass；xUnit：GEmuera.Core.Tests 280 总数（279 通过 1 跳过）、EmueraFacade.Tests 78 通过、EmueraPluginAbi.Tests 4 通过；`Test-Governance.ps1 -ProjectRoot .` 4/4（含发布门禁 7/7）；`tools/legacy-runner/Test-*.ps1` 6/6 exit 0（`Test-LegacyRunner.ps1` 需必填参数、不在此列）；legacy-runner v24pure 首等待 `Passed` 且 semanticSha = `4859125d3f984f087eb143dd83fa54f3b754055187cb87a9e6b166d4505754a1`。基线证据：`E:\tmp\gemuera-mobile-orientation\baseline\`。
- 文件规模：新文件 ≤1500 行；`scripts/EmueraContent.cs`（7403 行，>2000）只减不增；`Scripts/Emuera/**`（v24 对齐区）、方言/契约层禁止改动；`assets/scenes/main.tscn` 不改。
- 日志规范：新文案走 `GenericUtils.*` 路由、中文、格式「组件: 对象 → 结果（k=v）」；禁止裸 `GD.Print*`；EventId 用派生前缀 `UI.*`（`docs/logging-convention.md`）。
- 编辑纪律：文本文件只用 read/edit/write（禁止 PowerShell Get-Content/Set-Content 往返）；UTF-8 无 BOM。
- 用户数据：测试不得写/覆盖用户 `user://` 真实文件；需要时先备份、测后还原；探针临时文件只放 `E:\tmp\gemuera-mobile-orientation\`（项目外），收尾删除。
- 诊断面板/日志默认关闭；APK 中由设置页开关控制，不依赖只读 config.toml。

## Review Focus

1. **过渡态 vbar range 被当权威**：旋转/小窗尺寸变化当帧，`scrollContainer` 尺寸与 vbar range 可能还是旧值；若此时用 limit 钳制并写回 desired，会永久吃掉一截滚动范围。对应测试：Task 1（GDUnit 引擎时机取证）+ Task 2（纯数学用例：旧 limit 过渡态不污染）。
2. **触摸/惯性状态在旋转中断时残留**：系统可能不派发 Canceled，`contentDragActive/contentPinchActive` 卡 true 会让拖动被吞。对应测试：Task 3（尺寸变化路径强制重置状态；headless 启动不崩 + 桌面窗口化手动按清单验证）。
3. **非 Android / 老 Android 缺失 AndroidRuntime**：Autoload 在桌面、headless、legacy-runner 下必须 no-op 不崩。对应测试：Task 4（纯策略单测 + headless 启动 120 帧冒烟）。
4. **mono APK 内 JavaObject 动态调用异常**：`getActivity`/`isInMultiWindowMode` 可能 null/抛 Java 异常；必须限流告警并降级，不能崩溃或卡方向。对应测试：Task 4（失败路径单测 + 首版 3 条探针日志，真机判定）。
5. **设置页开关不生效/多语言键缺失**：开关写 `user://settings.cfg` 后需热生效，四语 key 必须齐全。对应验证：Task 5 Step 3 手动窗口化勾选/重启检查 + Step 4 四语 key 计数；**不做自动化**（理由：项目 csproj `Compile Remove="tests\**"`，C# GDUnit 测试无法编入项目程序集，写自动化测试是幻觉）。

---

### Task 0: 基线取证与文档提交

**Files:**
- 输出（项目外）：`E:\tmp\gemuera-mobile-orientation\baseline\`
- 提交：`docs/superpowers/specs/2026-11-04-mobile-orientation-smallwindow-design.md`、`docs/superpowers/plans/2026-11-04-mobile-orientation-smallwindow.md`

**Interfaces:**
- Consumes: 无
- Produces: 基线证据文件（供 Task 7 对比失败集合无新增）

- [ ] **Step 1: 记录工作区基线**
Run: `git status --short; git log -1 --format='%h %ad' --date=iso`
Expected: 只有 `export_presets.cfg` 与未知跟踪外的 `.dsh/`、`.zcodeignore`；HEAD=aa3f408。把输出写入 `baseline\git.txt`。

- [ ] **Step 2: 构建基线**
Run: `$GODOT --headless --path . --build-solutions --quit`（按 Global Constraints 的 Start-Process 方式）
Expected: `.godot/mono/temp/bin/Debug/gemuera-c#.dll` 时间戳更新；输出 `baseline\build.txt`。

- [ ] **Step 3: 跑三冒烟并记录**
Run: `dotnet run --project tools/dialect-inventory/LegacyDialectSurfaceSmoke`、`dotnet run --project tools/dialect-inventory/LegacyDialectRuntimeSmoke`、`dotnet run --project tools/core-contracts/CoreContractSmoke.csproj`
Expected: 三条 exit 0；输出合并写入 `baseline\smoke.txt`。

- [ ] **Step 4: 跑 xUnit 基线**
Run: `dotnet test tests/xUnitTest/GEmuera.Core.Tests -c Release`、`dotnet test tests/xUnitTest/EmueraFacade.Tests -c Release`、`dotnet test tests/xUnitTest/EmueraPluginAbi.Tests -c Release`
Expected: 131 / 35 / 3 通过；输出 `baseline\xunit.txt`。

- [ ] **Step 5: 跑 governance 与 legacy 契约测试基线**
Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/governance/Test-Governance.ps1 -ProjectRoot .`；以及 `Get-ChildItem tools/legacy-runner/Test-*.ps1 | Where-Object Name -ne 'Test-LegacyRunner.ps1' | ForEach-Object { powershell -NoProfile -ExecutionPolicy Bypass -File $_.FullName -ProjectRoot . }`
Expected: governance 4/4；契约测试全部 exit 0；输出 `baseline\gates.txt`。

- [ ] **Step 6: legacy-runner v24pure 基线**
Run（按 `tools/legacy-runner/README.md` 的 Invoke-LegacyRunner 示例，游戏根用与 2026-10-20 基线相同的 v24pure 游戏）：
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/legacy-runner/Invoke-LegacyRunner.ps1 `
  -ProjectRoot . -GodotPath $GODOT -GameRoot <v24pure游戏根> -Profile v24pure `
  -OutputDirectory E:\tmp\gemuera-mobile-orientation\baseline\runner
```
Expected: 报告 `PassedRunnerConsistency`；记录 semanticSha，必须等于 `4859125d3f984f087eb143dd83fa54f3b754055187cb87a9e6b166d4505754a1`。写入 `baseline\runner.txt`。

- [ ] **Step 7: 提交设计文档与计划**
Run: `git add docs/superpowers/specs/2026-11-04-mobile-orientation-smallwindow-design.md docs/superpowers/plans/2026-11-04-mobile-orientation-smallwindow.md && git commit -m "docs(spec): 移动端横竖屏与小窗适配设计与实现计划"`
Expected: 提交成功，`git status` 无 docs 未跟踪项。

---

### Task 1: Godot 引擎行为取证（systematic-debugging Phase 1）

**Files:**
- Create: `tests/GDUnit4Test/Layout/GodotScrollRangeBehaviorTest.gd`

**Interfaces:**
- Consumes: 无
- Produces: `[实测]` 结论——改尺寸后 ScrollContainer 子尺寸/vbar range 的更新时机、ScrollVertical 越界赋值是否被钳制；写入测试报告 `reports/` 供设计文档 §4.1 复核。

> **Phase 1 已执行结论（2026-11-05，8/8 passed）**：父尺寸变化会刷新 range；**子节点 custom_minimum_size 变化不会刷新 `largest_child_min_size` 缓存**，child 尺寸已变但 vbar range 6 帧后仍为旧值，写 scroll 被钳到旧上限；必须显式 `get_combined_minimum_size()`/`update_minimum_size()` + `queue_sort()` 才刷新（range 400→3400）。证据：`E:\tmp\gemuera-mobile-orientation\phase1-engine.md`。Task 3 已据此加入强制刷新步骤。

- [ ] **Step 1: 写取证测试（GDScript/GdUnit4）**
```gdscript
extends GdUnitTestSuite

func _make_scroll(custom_height: float) -> Array:
	var root := Control.new()
	root.size = Vector2(800, 600)
	add_child(root)
	auto_free(root)
	var scroll := ScrollContainer.new()
	scroll.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.add_child(scroll)
	var child := Control.new()
	child.custom_minimum_size = Vector2(0, custom_height)
	scroll.add_child(child)
	return [root, scroll, child]

func test_resize_updates_vbar_after_one_frame() -> void:
	var ctx := _make_scroll(2000.0)
	var root: Control = ctx[0]
	var scroll: ScrollContainer = ctx[1]
	var child: Control = ctx[2]
	await get_tree().process_frame
	var vbar: VScrollBar = scroll.get_v_scroll_bar()
	assert_bool(vbar.max_value - vbar.page > 0).is_true()
	root.size = Vector2(600, 300)
	await get_tree().process_frame
	assert_float(child.size.y).is_greater_equal(2000.0)
	assert_float(vbar.max_value - vbar.page).is_equal_approx(maxf(0.0, child.size.y - scroll.size.y), 1.0)

func test_scroll_vertical_is_clamped_to_range() -> void:
	var ctx := _make_scroll(2000.0)
	var scroll: ScrollContainer = ctx[1]
	await get_tree().process_frame
	scroll.scroll_vertical = 999999
	var vbar: VScrollBar = scroll.get_v_scroll_bar()
	assert_int(scroll.scroll_vertical).is_less_equal(int(vbar.max_value - vbar.page) + 1)
```

- [ ] **Step 2: 跑取证测试**
Run: `& $GODOT --headless --path . -s -d --remote-debug tcp://127.0.0.1:0 res://addons/gdUnit4/bin/GdUnitCmdTool.gd tests/GDUnit4Test/Layout`
Expected: exit 0，`2/2`（或报告目录 reports/ 中本次报告）通过。若失败，把实际数值记录到 `E:\tmp\gemuera-mobile-orientation\phase1-engine.json`，**暂停后续修复**，回 Phase 1 修正 §4.1 假设（systematic-debugging 规则）。

- [ ] **Step 3: 提交**
Run: `git add tests/GDUnit4Test/Layout/GodotScrollRangeBehaviorTest.gd tests/GDUnit4Test/Layout/GodotScrollRangeBehaviorTest.gd.uid && git commit -m "test(ui): 取证 Godot ScrollContainer 尺寸传播与越界钳制行为"`
Expected: 提交成功（.uid 若 Godot 自动生成则一并提交）。

---

### Task 2: 纯滚动对账数学（xUnit TDD，Phase 3/4）

**Files:**
- Create: `tests/xUnitTest/GEmuera.Core.Tests/ContentScrollReconciliationTests.cs`
- Create: `src/Core/Ui/ContentScrollReconciliation.cs`

**Interfaces:**
- Produces（供 Task 3 调用，命名空间 `GEmuera.Core.Ui`）：
  - `public static int ComputeScaledExtentY(int layoutHeight, float scale, int viewportHeight)` → `ceil(max(layoutHeight*scale, viewportHeight))`，全部 ≤0 的输入返回 0。
  - `public static int ComputeScrollMax(int scaledExtentY, int viewportHeight)` → `max(0, scaledExtentY - viewportHeight)`。
  - `public static bool IsAtBottom(int scrollY, int scrollMax, int tolerancePx = 2)`。
  - `public static int ComputeTargetScrollY(int previousScrollY, int previousScrollMax, int nextScrollMax, bool wasAtBottom)`。

- [ ] **Step 1: 先写失败测试**
```csharp
using GEmuera.Core.Ui;
using Xunit;

public class ContentScrollReconciliationTests
{
    [Theory]
    [InlineData(940, 0.69f, 648, 649)]
    [InlineData(940, 1.0f, 648, 940)]
    [InlineData(100, 0.5f, 648, 648)]
    [InlineData(0, 0.5f, 0, 0)]
    public void ComputeScaledExtentY_clamps_to_viewport(int layout, float scale, int viewport, int expected)
        => Assert.Equal(expected, ContentScrollReconciliation.ComputeScaledExtentY(layout, scale, viewport));

    [Theory]
    [InlineData(649, 648, 1)]
    [InlineData(648, 648, 0)]
    [InlineData(0, 648, 0)]
    public void ComputeScrollMax_never_negative(int extent, int viewport, int expected)
        => Assert.Equal(expected, ContentScrollReconciliation.ComputeScrollMax(extent, viewport));

    [Theory]
    [InlineData(100, 120, true)]
    [InlineData(0, 120, false)]
    [InlineData(0, 0, true)]
    public void IsAtBottom_uses_tolerance(int scroll, int max, bool expected)
        => Assert.Equal(expected, ContentScrollReconciliation.IsAtBottom(scroll, max));

    [Fact]
    public void TargetScroll_keeps_ratio_when_not_at_bottom()
        => Assert.Equal(500, ContentScrollReconciliation.ComputeTargetScrollY(100, 120, 600, false));

    [Fact]
    public void TargetScroll_sticks_to_bottom_when_was_at_bottom()
        => Assert.Equal(600, ContentScrollReconciliation.ComputeTargetScrollY(100, 120, 600, true));

    [Fact]
    public void TargetScroll_never_exceeds_next_max_for_stale_previous()
        => Assert.Equal(60, ContentScrollReconciliation.ComputeTargetScrollY(590, 120, 600, false));
}
```

- [ ] **Step 2: 跑测试确认失败**
Run: `dotnet test tests/xUnitTest/GEmuera.Core.Tests -c Release --filter FullyQualifiedName~ContentScrollReconciliation`
Expected: 编译失败（类型 `ContentScrollReconciliation` 不存在）。

- [ ] **Step 3: 最小实现**
在 `src/Core/Ui/ContentScrollReconciliation.cs` 实现四个方法（纯静态、无 Godot 依赖、double/float 都用 `Math.Ceiling/Math.Max`；`ComputeTargetScrollY` 在 wasAtBottom 时返回 nextMax，否则 `ratio = previousScrollMax<=0?0:clamp(previousScrollY,0,previousScrollMax)/previousScrollMax` 并 `round(ratio*nextScrollMax)`，最后 clamp 到 `[0,nextScrollMax]`）。

- [ ] **Step 4: 跑测试确认通过 + 全量 Core 回归**
Run: 上次命令；然后 `dotnet test tests/xUnitTest/GEmuera.Core.Tests -c Release`
Expected: 新用例全通过；总数 = 131 + 新增用例数。

- [ ] **Step 5: 提交**
Run: `git add src/Core/Ui/ContentScrollReconciliation.cs tests/xUnitTest/GEmuera.Core.Tests/ContentScrollReconciliationTests.cs && git commit -m "feat(core): 抽出内容滚动对账纯数学并加 xUnit 覆盖"`

---

### Task 3: 视口布局状态机接入（Phase 4 主修复）

**Files:**
- Create: `scripts/EmueraContent.Viewport.cs`（partial，视口/安全区/滚动对账域）
- Modify: `scripts/EmueraContent.cs`（迁出方法、改 OnViewportSizeChanged / ApplyMobileViewportPolicy / ProcessContentScrollCorrection / UpdateScaleBounds 滚动写入）

**Interfaces:**
- Consumes: `ContentScrollReconciliation.*`（Task 2）
- Produces: 私有方法 `MarkViewportLayoutDirty()`、`ApplyViewportLayoutDeferred()`、`ApplyOrientationAwareScale()`、`ReconcileContentScrollAfterLayout()`；调试开关 `static readonly bool ForceMobileViewportForDiagnostics`（读环境变量 `GEMUERA_FORCE_MOBILE_VIEWPORT=1`）。

- [ ] **Step 1: 纯移动提交（不改逻辑）**
把以下成员从 `scripts/EmueraContent.cs` 原样搬进新 partial `scripts/EmueraContent.Viewport.cs`：`GetSafeViewportRect`、`ApplyGameViewHorizontalPadding`、`ClampRectToViewport`、`RectAlmostEqual`、`RefreshViewportMetrics`、`ApplySafeRect`、`ApplySafeAreaLayout`、`ApplyAndroidDynamicWindowWidth`、`ApplyMobileViewportPolicy`、`OnViewportSizeChanged`、`QueueScaleBoundsUpdate`、`DeferredUpdateScaleBounds`。
Run: 构建（Global Constraints 命令）
Expected: 0 error；`EmueraContent.cs` 行数净减；`git diff --stat` 只显示移动。
Run: `git add scripts/EmueraContent.cs scripts/EmueraContent.Viewport.cs && git commit -m "refactor(ui): 视口布局域迁出 EmueraContent 主文件（纯移动）"`

- [ ] **Step 2: 写状态机骨架（先替换入口，保持旧行为可编译）**
在 `EmueraContent.Viewport.cs` 实现：
```csharp
bool viewportLayoutDirty;
ulong viewportLayoutSerial;
static readonly bool ForceMobileViewportForDiagnostics =
    OS.GetEnvironment("GEMUERA_FORCE_MOBILE_VIEWPORT") == "1";

void OnViewportSizeChanged()
{
    viewportLayoutDirty = true;
    viewportLayoutSerial++;
    ResetContentTouchGestureState();
    StopContentInertia();
    CallDeferred(nameof(ApplyViewportLayoutDeferred));
}

async void ApplyViewportLayoutDeferred()
{
    ulong serial = viewportLayoutSerial;
    RefreshViewportMetrics();
    ApplySafeAreaLayout();
    ApplyOrientationAwareScale();
    ApplyContentScaleTransform();
    for (int attempt = 0; attempt < 3; attempt++)
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    if (!GodotObject.IsInstanceValid(this) || serial != viewportLayoutSerial)
        return;
    ReconcileContentScrollAfterLayout();
    VirtualMouse.Active?.RefreshViewportBounds();
    consoleRenderSurface?.MarkDirty();
    viewportLayoutDirty = false;
}
```
并把 `ApplyMobileViewportPolicy` 改为只做：`if (!IsMobilePlatform && !ForceMobileViewportForDiagnostics) return;` → 计算 fit → `ApplyContentScaleValue(fit)`（删除原 :706-708 的 `UpdateScaleBounds`/`ApplyContentScaleTransform` 与 :711 的 `QueueScaleBoundsUpdate`）。`ProcessContentScrollCorrection` 首行加 `if (viewportLayoutDirty) return;`。`UpdateScaleBounds` 中 L3779-3792 的滚动写入块删除，改为只更新 `scaledContentRoot` 尺寸后调用 `ReconcileContentScrollAfterLayout()` 之外不写 ScrollContainer（由调用方在需要时显式 `RestoreContentScaleFocus`/对账）。
Run: 构建
Expected: 0 error。

- [ ] **Step 3: 实现对账与诊断快照**
`ReconcileContentScrollAfterLayout()`：先**强制刷新 ScrollContainer 最小尺寸缓存**——`scrollContainer.UpdateMinimumSize(); scrollContainer.QueueSort();` 并 `await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame)` 一次（Phase 1 实测：不刷新则 vbar range 永远停留在旧值，scroll 被钳到旧上限；刷新后 range 从 400 变 3400）。然后读 `vbar = scrollContainer.GetVScrollBar()`；`scaledExtentY = Mathf.RoundToInt(Mathf.Max(scaledContentRoot.Size.Y, lineContainer.Size.Y * contentScale))`；`nextMax = ContentScrollReconciliation.ComputeScrollMax(scaledExtentY, Mathf.RoundToInt(scrollContainer.Size.Y))`；`previousMax` 用新增字段缓存（每次对账后更新）；`wasAtBottom = pendingScroll || ContentScrollReconciliation.IsAtBottom(previousScrollY, previousMax)`；`target = ComputeTargetScrollY(previousScrollY, previousMax, nextMax, wasAtBottom)`；`scrollContainer.ScrollVertical = target`；`RememberDesiredContentScroll(...)`；输出诊断 `UI.VIEWPORT`/`UI.SCROLL`（`MobileLayoutDiagnostics` 在 Task 5 提供，本步骤先用 `GenericUtils.Info(EmueraLogCategory.UI, ...)` 占位并 TODO 注释指向 Task 5）。
Run: 构建 + `$env:GEMUERA_FORCE_MOBILE_VIEWPORT='1'; & $GODOT --headless --path . --quit-after 120`
Expected: 构建 0 error；headless 启动 120 帧 exit 0（无空引用/异常）；日志中每条 `[VIEWPORT]` 后跟一条 `[SCROLL]` 且含 `ok|short` 判定。

- [ ] **Step 4: 桌面窗口化手动冒烟**
Run: `$env:GEMUERA_FORCE_MOBILE_VIEWPORT='1'; & $GODOT --path .`（窗口模式，手动把窗口从 1280x720 拖成 720x1280 再拖回，进游戏出几屏文本后旋转拖拽验证最后一行可达）
Expected: 无报错；`[SCROLL]` 对账 `ok`；控制台可拖到最底。把日志片段存入 `E:\tmp\gemuera-mobile-orientation\phase4-desktop.log`（若缺少游戏可跳过并在交付说明标注 [未验证]）。

- [ ] **Step 5: 提交**
Run: `git add scripts/EmueraContent.cs scripts/EmueraContent.Viewport.cs && git commit -m "fix(ui): 视口变化收敛为单一布局入口并以真实滚动范围对账"`

---

### Task 4: 窗口形态策略 + 方向控制器（问题2 阶段一）

**Files:**
- Create: `tests/xUnitTest/GEmuera.Core.Tests/WindowOrientationPolicyTests.cs`
- Create: `src/Core/Ui/WindowOrientationPolicy.cs`
- Create: `scripts/GodotHost/AndroidWindowOrientationController.cs`
- Create: `assets/scenes/godot_host/android_window_orientation_host.tscn`
- Modify: `project.godot`（[autoload] 加一行）

**Interfaces:**
- Produces（`GEmuera.Core.Ui`）：`public enum WindowLayoutKind { Unknown, Fullscreen, MultiWindow }`；`public enum OrientationRequest { None, SensorLandscape, Sensor }`；`public static OrientationRequest Decide(WindowLayoutKind previous, WindowLayoutKind current)`（Unknown→MultiWindow=SensorLandscape；Unknown→Fullscreen=None；Fullscreen→MultiWindow=SensorLandscape；MultiWindow→Fullscreen=Sensor；相同=None）。
- 控制器公开：`public override void _Ready()`、`public override void _ExitTree()`；私有 `TryQueryMultiWindow(out bool)`、`RequestOrientation(OrientationRequest, string reason)`。

- [ ] **Step 1: 写策略失败测试**
```csharp
public class WindowOrientationPolicyTests
{
    [Fact] public void Initial_fullscreen_requests_nothing()
        => Assert.Equal(OrientationRequest.None, WindowOrientationPolicy.Decide(WindowLayoutKind.Unknown, WindowLayoutKind.Fullscreen));
    [Fact] public void Entering_multi_window_requests_sensor_landscape()
        => Assert.Equal(OrientationRequest.SensorLandscape, WindowOrientationPolicy.Decide(WindowLayoutKind.Fullscreen, WindowLayoutKind.MultiWindow));
    [Fact] public void Leaving_multi_window_restores_sensor()
        => Assert.Equal(OrientationRequest.Sensor, WindowOrientationPolicy.Decide(WindowLayoutKind.MultiWindow, WindowLayoutKind.Fullscreen));
    [Fact] public void Same_state_requests_nothing()
        => Assert.Equal(OrientationRequest.None, WindowOrientationPolicy.Decide(WindowLayoutKind.MultiWindow, WindowLayoutKind.MultiWindow));
}
```
Run: `dotnet test tests/xUnitTest/GEmuera.Core.Tests -c Release --filter FullyQualifiedName~WindowOrientationPolicy`
Expected: 编译失败（类型不存在）。

- [ ] **Step 2: 实现策略类并跑绿**
实现 `src/Core/Ui/WindowOrientationPolicy.cs` 后重跑上条命令。Expected: 4/4 通过；`dotnet test tests/xUnitTest/GEmuera.Core.Tests -c Release` 总数 = 基线 + 新增。

- [ ] **Step 3: 实现控制器**
`scripts/GodotHost/AndroidWindowOrientationController.cs`：
- `_Ready`：非 Android（`OS.GetName() != "Android"`）→ `SetProcess(false)` 并 return；否则延迟 2 帧首次 `Evaluate()`，连接 `GetWindow().SizeChanged` 与 `GetViewport().SizeChanged`，用 150ms `Timer` 去抖。
- `TryQueryMultiWindow`：`Engine.GetSingleton("AndroidRuntime")` → `Call("getActivity")` → `AsGodotObject()` → `Call("isInMultiWindowMode")` → `AsBool()`；每步判空；`JavaClassWrapper.GetException()` 非空时记录并返回 false；异常全 catch。
- `Evaluate`：查询失败 → 限流 Warn 一次后 return（保留后续重试）；成功则 `kind = isMulti ? MultiWindow : Fullscreen`，`request = WindowOrientationPolicy.Decide(previous, kind)`；`request != None` 时 `DisplayServer.ScreenSetOrientation(...)` 并记录 before/after `ScreenGetOrientation()`、`WindowGetSize`；2s 后复查是否 changed/ignored 写日志。
- `_ExitTree`：若最后一次请求是 SensorLandscape，则 `DisplayServer.ScreenSetOrientation(ScreenOrientation.Sensor)` 恢复可旋转。
Run: 构建。Expected: 0 error。

- [ ] **Step 4: 注册 Autoload 场景**
创建 `assets/scenes/godot_host/android_window_orientation_host.tscn`（根 Node + `script = ExtResource(AndroidWindowOrientationController.cs)`）；在 `project.godot` 增加：
```ini
[autoload]

AndroidWindowOrientationController="*res://assets/scenes/godot_host/android_window_orientation_host.tscn"
```
Run: `& $GODOT --headless --path . --quit-after 120`
Expected: exit 0；桌面日志无 AndroidRuntime 相关错误（应为静默 no-op）。

- [ ] **Step 5: 提交**
Run: `git add src/Core/Ui/WindowOrientationPolicy.cs tests/xUnitTest/GEmuera.Core.Tests/WindowOrientationPolicyTests.cs scripts/GodotHost/AndroidWindowOrientationController.cs assets/scenes/godot_host/android_window_orientation_host.tscn project.godot && git commit -m "feat(android): 小窗形态检测与运行时横屏请求（阶段一）+ 纯策略单测"`

---

### Task 5: 诊断设置页/日志/状态面板

**Files:**
- Create: `scripts/Diagnostics/MobileLayoutDiagnosticsSettings.cs`
- Create: `scripts/Diagnostics/MobileLayoutDiagnostics.cs`
- Create: `scripts/Diagnostics/MobileLayoutStatePanel.cs`
- Modify: `scripts/Panels/OptionWindow.cs`、`scripts/FirstWindow.DiagnosticsSettings.cs`、`assets/lang/default.txt`、`assets/lang/zh_cn.txt`、`assets/lang/jp.txt`、`assets/lang/en_us.txt`、`scripts/EmueraContent.Viewport.cs`（接诊断入口）

**Interfaces:**
- `MobileLayoutDiagnosticsSettings`（Scripts 层，Godot ConfigFile I/O；无自动化测试，理由见 Review Focus 5）：`public const string SettingsPath="user://settings.cfg"; public const string SettingsSection="Display"; public const string LoggingKey="MobileLayoutLogging"; public const string PanelKey="MobileLayoutPanel"; public static bool LoggingEnabled {get;set;}; public static bool PanelEnabled {get;set;};`（读写 ConfigFile，保存即时生效，带内存缓存；默认 false）。
- `MobileLayoutDiagnostics`：`public static bool Enabled {get;}`；`public static void PublishLayout(in LayoutSnapshot s)`；`public static void PublishOrientation(in OrientationSnapshot o)`；`public static LayoutSnapshot LatestLayout {get;}`；`public static OrientationSnapshot LatestOrientation {get;}`。
- 文案键：`OptionWindow.MobileLayoutGroup`、`OptionWindow.MobileLayoutLogging`、`OptionWindow.MobileLayoutLoggingHint`、`OptionWindow.MobileLayoutPanel`、`OptionWindow.MobileLayoutPanelHint`；`FirstWindow.MobileLayoutLogging`、`FirstWindow.MobileLayoutLoggingHint`、`FirstWindow.MobileLayoutPanel`、`FirstWindow.MobileLayoutPanelHint`。

- [ ] **Step 1: 实现设置类 + 诊断类 + 面板**
按 Interfaces 实现三个类（设置类读写 `user://settings.cfg`，验证过程中若改动真实配置须先备份并在结束时还原 false；诊断类在 `Enabled == false` 时不构造日志字符串；面板 `MobileLayoutStatePanel : Control` 显示 10 行快照、`MouseFilter=Ignore`、0.5s 定时刷新，由 `EmueraContent.Viewport` 按 `PanelEnabled` 创建/显隐）。
Run: 构建。Expected: 0 error。

- [ ] **Step 2: 接日志埋点**
在 `EmueraContent.Viewport.cs` 的布局对账/方向控制器中调用 `MobileLayoutDiagnostics.PublishLayout(...)` / `PublishOrientation(...)`；日志用 `GenericUtils.Info(EmueraLogCategory.UI, () => "[VIEWPORT] ...")` / `[SCROLL]` / `[ORIENTATION]`，`Enabled == false` 时直接 return。
Run: 构建。Expected: 0 error。

- [ ] **Step 3: 接设置页开关（两处）**
游戏内 `OptionWindow._Ready`：在 display 组之后新增「移动端布局诊断」分组（复用 `CreateGroup`/`CreateRow` 模式；两个 `CheckButton`，`ButtonPressed=MobileLayoutDiagnosticsSettings.LoggingEnabled/PanelEnabled`，`Toggled` 回调里写设置并 `MobileLayoutDiagnostics.Enabled` 即时生效）。启动器 `FirstWindow.DiagnosticsSettings.cs`：在 `CreateLoggingSection` 的按钮行前追加同两个 `CheckButton`（`CreateSettingsToggle` 模式）。
Run: `$GODOT --path .`（窗口模式手动打开 选项/启动器诊断页，勾选/取消，重启后状态保留）
Expected: 开关可用且持久化；面板在游戏中开关后立即显隐。

- [ ] **Step 4: 四语文案**
在 4 个 lang 文件各加入上述 9 个键（中文/日文/英文/默认英文），保持现有格式与行尾。
Run: `Select-String -Path assets/lang/*.txt -Pattern 'MobileLayout(Group|Logging|Panel)' | Group-Object Path | Select Count,Name`
Expected: 4 个文件各 9 条命中。

- [ ] **Step 5: 提交**
Run: `git add scripts/Diagnostics/MobileLayoutDiagnosticsSettings.cs scripts/Diagnostics/MobileLayoutDiagnostics.cs scripts/Diagnostics/MobileLayoutStatePanel.cs scripts/Panels/OptionWindow.cs scripts/FirstWindow.DiagnosticsSettings.cs assets/lang/default.txt assets/lang/zh_cn.txt assets/lang/jp.txt assets/lang/en_us.txt scripts/EmueraContent.Viewport.cs && git commit -m "feat(diagnostics): 移动端布局诊断日志/状态面板与设置页开关（四语）"`

---

### Task 6: 导出 dev-20261104 APK 并自检

**Files:**
- Modify: `export_presets.cfg`（`export_path`、`version/code`、`version/name`）

**Interfaces:**
- Consumes: Task 2-5 全部改动
- Produces: `export/releases/gemuera-1.10.0-dev-20261104-debug.apk`

- [ ] **Step 1: 更新导出配置**
`export_presets.cfg`：`export_path="export/releases/gemuera-1.10.0-dev-20261104-debug.apk"`、`version/code=11`、`version/name="1.10.0-dev-20261104"`。

- [ ] **Step 2: 构建 + 导出**
Run: `& $GODOT --headless --path . --build-solutions --quit`；再 `& $GODOT --headless --path . --export-debug "Android" "export/releases/gemuera-1.10.0-dev-20261104-debug.apk"`（可能耗时数分钟，用 Start-Process+WaitForExit 并轮询文件大小）。
Expected: APK 文件存在且 >50MB；DLL 时间戳为本次构建。

- [ ] **Step 3: aapt2 自检**
Run: `& 'C:\Users\30873\AppData\Local\Android\Sdk\build-tools\37.0.0\aapt2.exe' dump xmltree 'export/releases/gemuera-1.10.0-dev-20261104-debug.apk' --file AndroidManifest.xml | Select-String 'versionName|versionCode|screenOrientation'`
Expected: `versionName=1.10.0-dev-20261104`、`versionCode=11`、`screenOrientation=13`（阶段一保持 fullUser）。

- [ ] **Step 4: 提交配置**
Run: `git add export_presets.cfg && git commit -m "build(export): 版本 1.10.0-dev-20261104"`

---

### Task 7: 门禁回归、真机交接与收尾

**Files:**
- Create: `governance/evolution-log/2026-11-04-mobile-orientation-smallwindow.md`

**Interfaces:**
- Consumes: Task 0 基线、Task 6 APK
- Produces: 门禁全绿证据 + 真机验收结论 + PR

- [ ] **Step 1: 重跑全部门禁（对比 Task 0 基线，失败集合零新增）**
Run: 构建 → 三冒烟 → 三个 xUnit 工程 → `Test-Governance.ps1` → legacy 契约测试 → GDUnit 全量 → legacy-runner v24pure。
Expected: 全部通过；xUnit 总数 = 基线 + 新增；semanticSha 不变；输出存 `E:\tmp\gemuera-mobile-orientation\final\`。

- [ ] **Step 2: 交付 APK 给用户真机验收**
把 APK 路径与 §6.4 真机清单（6 条）发给用户；等待结果。若问题2 日志为 `ignored`，在 PR/汇报中标注阶段二触发条件，**不要**自行改 manifest。

- [ ] **Step 3: 写 evolution-log（AGENTS.md 六要素）**
包含：任务目标与成果 / 现象与根因 / 关键决策与 Why / 验证记录（逐条 [实测]/[静态]/[未验证]）/ AI 表现复盘 / 教训→具体优化动作 / 遗留登记。附真机结论与 semanticSha、APK 哈希。

- [ ] **Step 4: 收尾检查**
Run: `git status --short`（无临时/产物/游戏内容）；删除 `E:\tmp\gemuera-mobile-orientation\`；确认 `.dsh/`、`.zcodeignore` 未入库；确认无 `res://` 下游戏内容。

- [ ] **Step 5: 提交 + PR**
Run: `git add governance/evolution-log/2026-11-04-mobile-orientation-smallwindow.md && git commit -m "docs(governance): 移动端横竖屏与小窗适配任务记录"`；`git push -u origin fix/mobile-orientation-smallwindow`；`gh pr create --base dev --title "修复移动端横竖屏切换滚动与小窗默认方向" --body ...`（无 gh 时按仓库既有 REST/GraphQL 方式；PR 说明含 进化记录 行、验证证据、真机结论）。
Expected: PR 创建成功，指向 dev。

---

## 依赖与执行顺序

`Task 0 → Task 1 → Task 2 → Task 3 → Task 4 → Task 5 → Task 6 → 用户真机验收 → Task 7`

- Task 1 失败（引擎行为与假设不符）→ 暂停修复，回 systematic-debugging Phase 1 修订 §4.1，再继续。
- Task 2 是 Task 3 的接口前提；Task 5 依赖 Task 3/4 的埋点位置。
- Task 6 必须在 Task 2-5 全绿后执行；Task 7 的门禁在 Task 6 之后。
