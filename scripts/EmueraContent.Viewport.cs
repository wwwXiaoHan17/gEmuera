// EmueraContent.Viewport.cs —— 视口/安全区变化的状态机收敛域。
// 背景（systematic-debugging Phase 1 取证）：Godot 4.7 ScrollContainer 的
// largest_child_min_size 是 _get_minimum_size() 内的缓存，子节点 custom_minimum_size
// 变化不会刷新它；旋转/小窗改变 contentScale 与 scaledContentRoot 尺寸后，vbar
// Max/Page 可能停留在旧值，写 ScrollVertical 会被引擎钳到旧上限 => 底部不可达。
// 因此视口变化统一走「dirty -> deferred 单入口 -> 强制刷新最小尺寸缓存 -> 用真实
// vbar range + 纯数学对账」，不再在旋转帧内用过渡态 limit 写死滚动范围。
using Godot;
using System;
using GEmuera.Core.Ui;
using MinorShift.Emuera;

public partial class EmueraContent : Control
{
	// 调试用：置位后桌面也走移动端 fit 布局，用于复现/验证旋转路径（APK 不设置）。
	static readonly bool ForceMobileViewportForDiagnostics =
		OS.GetEnvironment("GEMUERA_FORCE_MOBILE_VIEWPORT") == "1";

	bool viewportLayoutDirty;
	bool viewportLayoutApplyScheduled;
	ulong viewportLayoutSerial;
	int lastViewportScrollMax = -1;

	// 视口/安全区/方向变化时立即调用：重置进行中的触摸/惯性状态，标记 dirty。
	void MarkViewportLayoutDirty()
	{
		viewportLayoutDirty = true;
		viewportLayoutSerial++;
		ResetContentTouchGestureState();
		StopContentInertia();
	}

	void ScheduleViewportLayoutApply()
	{
		if (viewportLayoutApplyScheduled)
			return;
		viewportLayoutApplyScheduled = true;
		CallDeferred(nameof(ApplyViewportLayoutDeferred));
	}

	// 单一布局入口：安全区 -> 等比缩放 -> 强制刷新 ScrollContainer 最小尺寸缓存
	// ->（等引擎 sort）用真实 vbar 范围对账滚动位置。
	async void ApplyViewportLayoutDeferred()
	{
		viewportLayoutApplyScheduled = false;
		if (!viewportLayoutDirty || !IsInsideTree() || scrollContainer == null)
			return;
		ulong serial = viewportLayoutSerial;
		RefreshViewportMetrics();
		ApplySafeAreaLayout();
		ApplyOrientationAwareScale();
		ApplyContentScaleTransform();
		RefreshScrollRangeCache();
		for (int attempt = 0; attempt < 2; attempt++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (!GodotObject.IsInstanceValid(this) || serial != viewportLayoutSerial)
			return;
		RefreshScrollRangeCache();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (!GodotObject.IsInstanceValid(this) || serial != viewportLayoutSerial)
			return;
		ReconcileContentScrollAfterLayout();
		VirtualMouse.Active?.RefreshViewportBounds();
		consoleRenderSurface?.MarkDirty();
		viewportLayoutDirty = false;
	}

	// 只计算 fit 并写入 contentScale；尺寸/滚动范围由 deferred 入口统一落地。
	void ApplyOrientationAwareScale()
	{
		if (!IsMobilePlatform && !ForceMobileViewportForDiagnostics)
			return;
		if (Config.WindowX <= 0 || Config.WindowY <= 0)
			return;
		if (contentScaleUserAdjusted
			|| ContentSafeWidth <= 0
			|| ContentSafeHeight <= 0
			|| Config.DrawableWidth <= 0)
			return;
		float fitWidth = ContentSafeWidth / (float)Config.DrawableWidth;
		float fitHeight = ContentSafeHeight / (float)Config.WindowY;
		float fit = Mathf.Clamp(Mathf.Min(fitWidth, fitHeight), 0.35f, 1.0f);
		if (Mathf.Abs(fit - contentScale) > 0.001f)
			ApplyContentScaleValue(fit);
	}

	// Phase 1 取证结论：必须显式刷新 ScrollContainer 的 largest_child_min_size 缓存，
	// 否则改完子节点 min size 后 vbar 范围不会更新（实测 6 帧仍为旧值）。
	void RefreshScrollRangeCache()
	{
		if (scrollContainer == null)
			return;
		scrollContainer.UpdateMinimumSize();
		scrollContainer.QueueSort();
	}

	void ReconcileContentScrollAfterLayout()
	{
		if (scrollContainer == null || scaledContentRoot == null || lineContainer == null)
			return;
		var vbar = scrollContainer.GetVScrollBar();
		int viewportY = Mathf.RoundToInt(scrollContainer.Size.Y);
		int layoutY = Mathf.RoundToInt(lineContainer.Size.Y);
		int scaledExtentY = Mathf.RoundToInt(Mathf.Max(scaledContentRoot.Size.Y, layoutY * contentScale));
		int nextMax = ContentScrollReconciliation.ComputeScrollMax(scaledExtentY, viewportY);
		int engineMax = Mathf.RoundToInt(Mathf.Max(0.0f, vbar.MaxValue - vbar.Page));
		int previousScrollY = desiredContentScrollValid ? desiredContentScrollVertical : scrollContainer.ScrollVertical;
		int previousMax = lastViewportScrollMax >= 0 ? lastViewportScrollMax : engineMax;
		bool wasAtBottom = pendingScroll
			|| ContentScrollReconciliation.IsAtBottom(previousScrollY, previousMax);
		int target = ContentScrollReconciliation.ComputeTargetScrollY(previousScrollY, previousMax, nextMax, wasAtBottom);
		if (target > engineMax)
			target = engineMax;
		int targetHorizontal = NormalizeContentHorizontalScroll(scrollContainer.ScrollHorizontal);
		scrollContainer.ScrollHorizontal = targetHorizontal;
		scrollContainer.ScrollVertical = target;
		RememberDesiredContentScroll(targetHorizontal, target);
		lastViewportScrollMax = nextMax;
		GenericUtils.Info(EmueraLogCategory.UI, () =>
			"[VIEWPORT] apply -> window=" + DisplayServer.WindowGetSize()
			+ " viewport=" + scrollContainer.Size + " safe=" + ContentSafeRect
			+ " scale=" + contentScale.ToString("0.###"));
		GenericUtils.Info(EmueraLogCategory.UI, () =>
			"[SCROLL] reconcile -> layoutY=" + layoutY
			+ " scaledExtentY=" + scaledExtentY
			+ " viewportY=" + viewportY
			+ " engineMax=" + engineMax
			+ " nextMax=" + nextMax
			+ " target=" + target
			+ " atBottom=" + wasAtBottom
			+ " status=" + (target == nextMax || !wasAtBottom ? "ok" : "short"));
	}
}
