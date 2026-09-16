using Godot;
using System;

// 快捷按钮面板的悬浮宿主（Godot Window 组件）。
// Why（2026-08-21 样式返工）：上一版无边框透明窗 + 自绘标题条/卡片在 Windows 上
// 拖动、命中、Z 序全靠手写模拟，体验差；本版回归原生窗口组件——
// 标准标题栏由 OS 负责拖动/关闭，窗口保持 Transient（总在主窗口之上），
// Resizable=false（尺寸完全由面板内容驱动，SyncWindowSize 同步）。
// 内容 1:1（按钮大小=配置值），面板内拖动语义沿用 QuickButtons：
// 内容无滚动余量时拖动 = 移动窗口，有滚动余量时拖动 = 滚动。
// 显隐通过 PadShown/PadHidden 信号同步窗口显隐。
// 桌面端由 EmueraContent 按配置挂载；Android 不挂载本类（嵌入 Window 在
// gl_compatibility 下内容不渲染，见 EmueraContent 门控），保持画布内嵌。
//
// 职责（组合优于继承）：只负责窗口几何与生命周期，不触碰按钮/滚动/拖拽等
// 业务逻辑——那些仍由 QuickButtons 自行处理。
public partial class QuickFloatingWindow : Window
{
	// 面板四周留白（窗口内容区内）。
	const int PanelMargin = 20;
	const int ScreenMargin = 32;
	const int MinWindowWidth = 180;
	const int MinWindowHeight = 96;

	QuickButtons quick;
	bool positionInitialized;

	/// <summary>把 QuickButtons 内容挂进本窗口；只允许在 _Ready 前调用一次。</summary>
	public void AttachQuick(QuickButtons content)
	{
		quick = content;
		// 悬浮宿主注入：锚点贴左贴顶（窗口内容区内）、面板拖动 → 移动窗口。
		content.FloatingHosted = true;
		content.WindowDragEnabled = true;
		content.WindowDragRequested += OnPanelWindowDrag;
		AddChild(content);
	}

	/// <summary>
	/// 迁移/销毁前显式解除与面板的关联：退订全部事件并复位宿主注入状态。
	/// 不依赖 QueueFree 的 _ExitTree 时序（同帧迁移时旧窗口尚未退订，避免双订阅）。
	/// </summary>
	public void DetachQuick()
	{
		if (quick == null)
			return;
		quick.PadShown -= OnPadShown;
		quick.PadHidden -= OnPadHidden;
		quick.WindowDragRequested -= OnPanelWindowDrag;
		quick.FloatingHosted = false;
		quick.WindowDragEnabled = false;
		if (quick.GetParent() == this)
			RemoveChild(quick);
		quick = null;
	}

	public override void _Ready()
	{
		// 原生窗口组件：OS 标题栏（拖动/关闭），Transient 保证盖在主窗口之上。
		Title = MultiLanguage.Get("QuickFloatingWindow.Title", "Quick");
		Borderless = false;
		// 尺寸完全由面板内容驱动（SyncWindowSize），禁止用户手动缩放。
		Unresizable = true;
		Transient = true;
		Exclusive = false;
		Visible = false;
		// _Process 只服务窗口尺寸同步；窗口隐藏时关闭，避免每帧一次
		// native→managed 调用（OnPadShown 会按需重开）。
		SetProcess(false);
		MinSize = new Vector2I(MinWindowWidth, MinWindowHeight);
		// 标题栏 ✕ 走 Godot CloseRequested：仅隐藏悬浮窗（保留系统菜单入口），
		// 与 quick 面板显隐状态保持一致。
		CloseRequested += OnCloseRequested;
		if (quick != null)
		{
			quick.PadShown += OnPadShown;
			quick.PadHidden += OnPadHidden;
		}
	}

	public override void _ExitTree()
	{
		// 兜底退订（DetachQuick 之外的销毁路径，如场景卸载）。
		CloseRequested -= OnCloseRequested;
		if (quick != null)
		{
			quick.PadShown -= OnPadShown;
			quick.PadHidden -= OnPadHidden;
			quick.WindowDragRequested -= OnPanelWindowDrag;
		}
	}

	void OnCloseRequested()
	{
		quick?.HidePad();
	}

	void OnPadShown()
	{
		SyncWindowSize();
		// 首次显示定位到主窗口右下角；之后保持用户拖动的位置。
		if (!positionInitialized)
		{
			var root = GetTree()?.Root;
			if (root != null && GodotObject.IsInstanceValid(root))
			{
				Position = new Vector2I(
					Mathf.Max(root.Position.X, root.Position.X + root.Size.X - Size.X - ScreenMargin),
					Mathf.Max(root.Position.Y, root.Position.Y + root.Size.Y - Size.Y - ScreenMargin));
			}
			positionInitialized = true;
		}
		SetProcess(true);
		Show();
	}

	void OnPadHidden()
	{
		SetProcess(false);
		Hide();
	}

	public override void _Process(double delta)
	{
		if (!Visible || quick == null)
			return;
		SyncWindowSize();
	}

	// 面板拖动 → 窗口移动（位移直接相加；1:1 内容下拖动量即物理位移）。
	void OnPanelWindowDrag(Vector2 delta)
	{
		Position += new Vector2I(Mathf.RoundToInt(delta.X), Mathf.RoundToInt(delta.Y));
		ClampToRoot();
	}

	// 悬浮窗保持在主窗口工作区内（独立 OS 窗口 Position 为屏幕坐标）。
	void ClampToRoot()
	{
		var root = GetTree()?.Root;
		if (root == null || !GodotObject.IsInstanceValid(root))
			return;
		int minX = root.Position.X;
		int minY = root.Position.Y;
		var pos = Position;
		pos.X = Mathf.Clamp(pos.X, minX, Mathf.Max(minX, minX + root.Size.X - Size.X));
		pos.Y = Mathf.Clamp(pos.Y, minY, Mathf.Max(minY, minY + root.Size.Y - Size.Y));
		Position = pos;
	}

	// 窗口内容区 = 面板尺寸 + 四周留白（1:1 无缩放；标题栏高度由 OS 管理，
	// 不计入内容区——Godot Window.Size 即客户区尺寸）。
	void SyncWindowSize()
	{
		if (quick == null)
			return;
		var panelSize = quick.GetPanelSize();
		if (panelSize.X <= 0 || panelSize.Y <= 0)
			return;
		var target = new Vector2I(
			Mathf.RoundToInt(panelSize.X + PanelMargin * 2),
			Mathf.RoundToInt(panelSize.Y + PanelMargin * 2));
		if (Size != target)
			Size = target;
	}
}
