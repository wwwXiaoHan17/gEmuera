using Godot;

/// <summary>
/// 共享虚拟指针层（CanvasLayer）：只渲染「可见光标 + 按下反馈环」。
///
/// Why（方案 B：一个指针 + 多个设备 → 一个设备 + 共享渲染）：虚拟鼠标（VirtualMouse）是唯一的
/// 虚拟指针输入设备，它驱动同一个指针——MOUSEX/MOUSEY + hover 高亮 + tooltip。指针的语义由
/// EmueraContent 持有（VirtualCursorSynchronizePosition / VirtualCursorUpdateHover /
/// VirtualCursorCommitClick），本组件只负责两件事：
///   1. 把指针位置渲染为可见光标（跟随 EmueraContent 推入的指针位置）；
///   2. 提供按下反馈环（机身按钮点击时确认点击落点）。
/// 中键按钮已下放到 VirtualMouse 机身的可见 M 标注（滚轮区），本层不再持有独立中键按钮——
/// 减少与机身重复的输入入口。
///
/// What/How：位置由 EmueraContent.VirtualCursorSynchronizePosition 推入 SetPointerPosition；
/// 可见性由 EmueraContent.RefreshVirtualPointerVisibility 按"是否有设备启用"控制。
/// 本层不拦截输入（MouseFilter=Ignore），仅渲染。
/// </summary>
public partial class VirtualPointer : CanvasLayer
{
	public const int PointerCanvasLayer = 92;

	TextureRect cursorVisual;
	PressFeedbackRing feedbackRing;
	Vector2 pointerPosition = new Vector2(float.NaN, float.NaN);

	public Vector2 PointerPosition => pointerPosition;

	public override void _Ready()
	{
		Layer = PointerCanvasLayer;
		BuildCursorVisual();
		BuildPressFeedbackRing();
		SetPointerVisible(false);
	}

	/// <summary>指针视觉（光标/反馈环）整体显隐：跟随设备启用状态。</summary>
	public void SetPointerVisible(bool visible)
	{
		if (cursorVisual != null)
			cursorVisual.Visible = visible;
		if (feedbackRing != null)
			feedbackRing.Visible = false;
	}

	/// <summary>指针位置 → 可见光标跟随（由 EmueraContent.VirtualCursorSynchronizePosition 推入）。</summary>
	public void SetPointerPosition(Vector2 globalPosition)
	{
		pointerPosition = globalPosition;
		if (cursorVisual == null)
			return;
		cursorVisual.GlobalPosition = globalPosition;
		cursorVisual.Visible = true;
	}

	void BuildCursorVisual()
	{
		cursorVisual = new TextureRect();
		cursorVisual.Texture = ResourceLoader.Load<Texture2D>("res://assets/icons/cursor.svg");
		cursorVisual.CustomMinimumSize = new Vector2(32, 32);
		cursorVisual.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		cursorVisual.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		cursorVisual.MouseFilter = Control.MouseFilterEnum.Ignore;
		cursorVisual.Visible = false;
		AddChild(cursorVisual);
	}

	void BuildPressFeedbackRing()
	{
		feedbackRing = new PressFeedbackRing();
		feedbackRing.Visible = false;
		AddChild(feedbackRing);
	}

	/// <summary>按下时在点击落点显示高亮环（确认点击落点），松手由 Hide 隐藏。</summary>
	public void ShowPressFeedback(Vector2 globalPosition)
	{
		if (feedbackRing == null)
			return;
		feedbackRing.Position = globalPosition;
		feedbackRing.Visible = true;
	}

	public void HidePressFeedback()
	{
		if (feedbackRing != null)
			feedbackRing.Visible = false;
	}

	/// <summary>按下反馈环：屏幕空间圆环（CanvasLayer 内不受鼠标机身缩放影响）。</summary>
	sealed partial class PressFeedbackRing : Node2D
	{
		public override void _Draw()
		{
			DrawArc(Vector2.Zero, 16f, 0f, Mathf.Tau, 40, new Color(1f, 0.85f, 0.3f, 0.95f), 3f);
		}
	}
}
