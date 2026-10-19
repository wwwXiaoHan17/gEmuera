// EmueraContent.UiDiagnostic.cs —— 承载 UI 布局/Overlay 诊断功能域，自 EmueraContent.cs 拆出（原因：主文件超 2000 行只减不增约束）。
using Godot;
using System;
using System.Collections.Generic;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Content;
using EmuFont = uEmuera.Drawing.Font;
using EmuColor = uEmuera.Drawing.Color;

public partial class EmueraContent : Control
{
	UiDiagnosticOverlay uiDiagnosticOverlay;

	void QueueUiLayoutTrace(Control control, string kind, string resourceName, int targetX, int targetY, int targetW, int targetH)
	{
		if (!GenericUtils.IsUiLayoutTraceEnabled(kind))
			return;
		// 企业级说明：本方法在行节点注册完成前调用，而注册成功会推进一次 displayRevision。
		// 记录“预期稳定修订号”可区分当前行自身完成注册和后续 UPDATE 批次替换旧节点。
		int expectedDisplayRevision = displayRevision + 1;
		TraceUiLayoutAfterLayout(control, kind, resourceName ?? "", targetX, targetY, targetW, targetH,
			expectedDisplayRevision, GenericUtils.UiFrameGeneration);
	}

	async void TraceUiLayoutAfterLayout(Control control, string kind, string resourceName,
		int targetX, int targetY, int targetW, int targetH,
		int expectedDisplayRevision, int queuedUiFrameGeneration)
	{
		// 企业级说明：Godot 容器的最终 Control rect 可能在当前帧结束后才稳定。
		// UI 几何诊断只在调试开关开启时排队到下一帧采样，不阻塞主线程，也不修改布局行为。
		var tree = GetTree();
		if (tree != null)
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

		if (control == null || !GodotObject.IsInstanceValid(control) || control.IsQueuedForDeletion())
		{
			// 企业级说明：LOAD/UPDATE 会在 Android 上分帧重建大量控制台行。
			// 如果等待布局帧期间显示修订号已经推进，当前采样对象属于旧批次，
			// 节点失效是正常生命周期结束，不能记录为立绘未加载或实际矩形缺失。
			if (displayRevision != expectedDisplayRevision)
				return;
			if (GenericUtils.IsUiLayoutMismatchTraceEnabled())
				GenericUtils.UiLayoutTrace("UI_LAYOUT.MISSING_ACTUAL",
					() => kind + " actual rect missing",
					() => BuildUiLayoutData(kind, resourceName, targetX, targetY, targetW, targetH, 0, 0, 0, 0)
						+ $" reason=node_missing expected_display_revision={expectedDisplayRevision} queued_render_batch_id={queuedUiFrameGeneration}");
			return;
		}

		int actualX = Mathf.RoundToInt(control.Position.X);
		int actualY = Mathf.RoundToInt(control.Position.Y);
		int actualW = Mathf.RoundToInt(control.Size.X);
		int actualH = Mathf.RoundToInt(control.Size.Y);
		EmitUiLayoutTrace(kind, resourceName, targetX, targetY, targetW, targetH, actualX, actualY, actualW, actualH);
		EmitUiOverlayTrace(control, kind, targetX, targetY, targetW, targetH, actualX, actualY, actualW, actualH);
	}

	void EmitUiLayoutTrace(string kind, string resourceName,
		int targetX, int targetY, int targetW, int targetH,
		int actualX, int actualY, int actualW, int actualH)
	{
		int dx = targetX - actualX;
		int dy = targetY - actualY;
		int dw = targetW - actualW;
		int dh = targetH - actualH;
		string dataFactory() => BuildUiLayoutData(kind, resourceName, targetX, targetY, targetW, targetH, actualX, actualY, actualW, actualH);

		if (GenericUtils.IsUiLayoutTargetRectTraceEnabled())
			GenericUtils.UiLayoutTrace("UI_LAYOUT.TARGET.RECORDED", () => kind + " target rect", dataFactory);
		if (GenericUtils.IsUiLayoutActualRectTraceEnabled())
			GenericUtils.UiLayoutTrace("UI_LAYOUT.ACTUAL.RECORDED", () => kind + " actual rect", dataFactory);

		int threshold = GenericUtils.UiLayoutMismatchThresholdPx;
		if (GenericUtils.IsUiLayoutMismatchTraceEnabled()
			&& (Mathf.Abs(dx) > threshold || Mathf.Abs(dy) > threshold || Mathf.Abs(dw) > threshold || Mathf.Abs(dh) > threshold))
		{
			GenericUtils.UiLayoutTrace("UI_LAYOUT.MISMATCH", () => kind + " mismatch", dataFactory);
		}
	}

	static string BuildUiLayoutData(string kind, string resourceName,
		int targetX, int targetY, int targetW, int targetH,
		int actualX, int actualY, int actualW, int actualH)
	{
		string resource = string.IsNullOrEmpty(resourceName) ? "" : " resource=" + resourceName;
		return $"kind={kind}{resource} target=({targetX},{targetY},{targetW},{targetH}) actual=({actualX},{actualY},{actualW},{actualH}) delta=({targetX - actualX},{targetY - actualY},{targetW - actualW},{targetH - actualH})";
	}

	void EmitUiOverlayTrace(Control control, string kind,
		int targetX, int targetY, int targetW, int targetH,
		int actualX, int actualY, int actualW, int actualH)
	{
		if (control == null || !GenericUtils.IsUiOverlayEnabled())
			return;
		if (kind == "button" && !GenericUtils.UiOverlayButtonRectEnabled)
			return;
		if (kind == "image" && !GenericUtils.UiOverlayImageRectEnabled)
			return;

		var overlay = EnsureUiDiagnosticOverlay();
		if (overlay == null)
			return;

		// 企业级说明：overlay 只复用 UI_LAYOUT 已经采样到的矩形，不主动遍历 UI 树。
		// 默认关闭时没有节点、没有绘制、没有额外 I/O；开启后也不修改布局或触摸命中，只画临时线框。
		var actualRect = ConvertGlobalRectToOverlay(control.GetGlobalRect());
		var targetRect = ConvertTargetRectToOverlay(control, targetX, targetY, targetW, targetH, actualX, actualY, actualW, actualH);
		int threshold = GenericUtils.UiLayoutMismatchThresholdPx;
		bool mismatch = Mathf.Abs(targetX - actualX) > threshold
			|| Mathf.Abs(targetY - actualY) > threshold
			|| Mathf.Abs(targetW - actualW) > threshold
			|| Mathf.Abs(targetH - actualH) > threshold;

		if (GenericUtils.UiOverlayTargetRectEnabled)
			overlay.AddRect(targetRect, new Color(0.2f, 0.55f, 1.0f, 0.95f));
		if (GenericUtils.UiOverlayActualRectEnabled)
			overlay.AddRect(actualRect, new Color(0.35f, 1.0f, 0.45f, 0.95f));
		if (mismatch && GenericUtils.UiOverlayMismatchEnabled)
			overlay.AddRect(actualRect, new Color(1.0f, 0.2f, 0.35f, 1.0f));
	}

	UiDiagnosticOverlay EnsureUiDiagnosticOverlay()
	{
		if (!GenericUtils.IsUiOverlayEnabled())
			return null;
		if (uiDiagnosticOverlay == null || !GodotObject.IsInstanceValid(uiDiagnosticOverlay))
		{
			uiDiagnosticOverlay = new UiDiagnosticOverlay();
			uiDiagnosticOverlay.Name = "UiDiagnosticOverlay";
			uiDiagnosticOverlay.MouseFilter = MouseFilterEnum.Ignore;
			uiDiagnosticOverlay.ZIndex = 4096;
			uiDiagnosticOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
			AddChild(uiDiagnosticOverlay);
		}
		uiDiagnosticOverlay.Visible = true;
		uiDiagnosticOverlay.MaxRects = GenericUtils.UiOverlayMaxDrawnRects;
		return uiDiagnosticOverlay;
	}

	void RefreshUiDiagnosticOverlay()
	{
		if (uiDiagnosticOverlay == null || !GodotObject.IsInstanceValid(uiDiagnosticOverlay))
			return;
		bool enabled = GenericUtils.IsUiOverlayEnabled();
		uiDiagnosticOverlay.Visible = enabled;
		uiDiagnosticOverlay.MaxRects = GenericUtils.UiOverlayMaxDrawnRects;
		if (!enabled)
			uiDiagnosticOverlay.ClearRects();
	}

	Rect2 ConvertTargetRectToOverlay(Control control,
		int targetX, int targetY, int targetW, int targetH,
		int actualX, int actualY, int actualW, int actualH)
	{
		Rect2 actualGlobal = control.GetGlobalRect();
		float scaleX = actualW != 0 ? actualGlobal.Size.X / actualW : 1.0f;
		float scaleY = actualH != 0 ? actualGlobal.Size.Y / actualH : 1.0f;
		var targetGlobal = new Rect2(
			actualGlobal.Position + new Vector2((targetX - actualX) * scaleX, (targetY - actualY) * scaleY),
			new Vector2(targetW * scaleX, targetH * scaleY));
		return ConvertGlobalRectToOverlay(targetGlobal);
	}

	Rect2 ConvertGlobalRectToOverlay(Rect2 globalRect)
	{
		if (uiDiagnosticOverlay == null || !GodotObject.IsInstanceValid(uiDiagnosticOverlay))
			return globalRect;
		return new Rect2(globalRect.Position - uiDiagnosticOverlay.GetGlobalRect().Position, globalRect.Size);
	}
	sealed partial class UiDiagnosticOverlay : Control
	{
		const ulong RectLifetimeMs = 2500;
		readonly List<OverlayRect> rects = new List<OverlayRect>(128);

		public int MaxRects { get; set; } = 128;

		struct OverlayRect
		{
			public Rect2 Rect;
			public Color Color;
			public ulong ExpireTick;
		}

		public override void _Ready()
		{
			MouseFilter = MouseFilterEnum.Ignore;
			SetProcess(true);
		}

		public void AddRect(Rect2 rect, Color color)
		{
			if (rect.Size.X <= 0 || rect.Size.Y <= 0)
				return;
			int maxRects = System.Math.Max(1, MaxRects);
			while (rects.Count >= maxRects)
				rects.RemoveAt(0);
			rects.Add(new OverlayRect
			{
				Rect = rect,
				Color = color,
				ExpireTick = Time.GetTicksMsec() + RectLifetimeMs
			});
			QueueRedraw();
		}

		public void ClearRects()
		{
			if (rects.Count == 0)
				return;
			rects.Clear();
			QueueRedraw();
		}

		public override void _Process(double delta)
		{
			if (rects.Count == 0)
				return;
			ulong now = Time.GetTicksMsec();
			bool changed = false;
			for (int i = rects.Count - 1; i >= 0; i--)
			{
				if (rects[i].ExpireTick <= now)
				{
					rects.RemoveAt(i);
					changed = true;
				}
			}
			if (changed)
				QueueRedraw();
		}

		public override void _Draw()
		{
			for (int i = 0; i < rects.Count; i++)
				DrawRect(rects[i].Rect, rects[i].Color, false, 2.0f);
		}
	}
}
