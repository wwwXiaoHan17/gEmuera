using Godot;

// 悬浮浮层「返回初始位置」语义验收（窗口模式运行，桌面 QuickButtons 独立实例）。
// Run: Godot --path "<project root>" res://tools/launcher-compat-tab/overlay_check.tscn
// Exit code: 0 = PASS, 1 = FAIL。
//
// 背景（2026-11-22 用户反馈）：竖屏拖到下面的浮层在横屏很难看到（安全区变化后
// 坐标陈旧）、关闭后钉死在最后移动的位置。裁定语义：
// 1) 浮层坐标只对放置时的安全区有效——安全区一变（转屏/折叠/分辨率）即重落角；
// 2) 关闭那一刻立即回角（右下；翻转开=左下），重开同样在角上；
// 3) 位置不持久化（QuickFloatingX/Y 停止读写）。
// 说明：EmueraContent 不在本测试场景内，RefreshSafeAreaLayout 由本驱动显式调用
// （真实调用链：视口尺寸变化 → EmueraContent.ApplySafeAreaLayout → 本方法）。
public partial class OverlayCheck : Node
{
	QuickButtons _qb;
	Vector2 _p0;
	Vector2 _p2;
	int _frames;
	int _stage;
	readonly System.Collections.Generic.List<string> _failures = new();

	public override void _Ready()
	{
		_qb = new QuickButtons();
		_qb.FloatingOverlayMode = true;
		AddChild(_qb);
		_qb.ShowPad();
	}

	public override void _Process(double delta)
	{
		_frames++;
		switch (_stage)
		{
			case 0 when _frames >= 15:
			{
				_p0 = _qb.FloatingPanelPosition;
				ExpectRegion(_p0, "初始位（右下角）", 0.5f, 0.5f);
				_qb.MoveFloatingPanelBy(new Vector2(-300, -400));
				Next();
				break;
			}
			case 1 when _frames >= 10:
			{
				var p1 = _qb.FloatingPanelPosition;
				if (p1.DistanceTo(_p0) < 1.0f)
					_failures.Add($"拖动未生效：p1={p1} 与 p0={_p0} 相同");
				_qb.HidePad();
				_qb.ShowPad();
				Next();
				break;
			}
			case 2 when _frames >= 15:
			{
				_p2 = _qb.FloatingPanelPosition;
				if (_p2.DistanceTo(_p0) > 1.0f)
					_failures.Add($"关闭→重开未回初始位：p2={_p2} 应等于 p0={_p0}");
				// 模拟转屏（横→竖）：真实链路是视口变化 → EmueraContent.
				// ApplySafeAreaLayout → RefreshSafeAreaLayout，此处直调同一方法。
				DisplayServer.WindowSetSize(new Vector2I(800, 1280));
				Next();
				break;
			}
			case 3 when _frames >= 15:
			{
				_qb.RefreshSafeAreaLayout();
				Next();
				break;
			}
			case 4 when _frames >= 15:
			{
				var p3 = _qb.FloatingPanelPosition;
				if (p3.DistanceTo(_p2) < 1.0f)
					_failures.Add($"跨方向旋转未重落角：p3={p3} 不应停留在 p2={_p2}");
				if (p3.Y < 1280f * 0.55f)
					_failures.Add($"转屏后未落到新方向下半屏：p3={p3}");
				_qb.MoveFloatingPanelBy(new Vector2(-600, -900));
				_qb.RehomeOverlayPanel();
				Next();
				break;
			}
			case 5 when _frames >= 15:
			{
				ExpectRegion(_qb.FloatingPanelPosition, "一键回位（RehomeOverlayPanel）", 0.5f, 0.5f);
				Finish();
				break;
			}
		}
	}

	void Next()
	{
		_stage++;
		_frames = 0;
	}

	void ExpectRegion(Vector2 pos, string label, float minXRatio, float minYRatio)
	{
		var size = GetViewport().GetVisibleRect().Size;
		if (pos.X < size.X * minXRatio || pos.Y < size.Y * minYRatio)
			_failures.Add($"{label} 不在预期区域：{pos}（视口 {size}）");
	}

	void Finish()
	{
		System.IO.Directory.CreateDirectory("D:/gemuera-reports/launcher-compat-tab");
		if (_failures.Count == 0)
		{
			GD.Print("OVERLAY_REHOME_CHECK: PASS");
			GetTree().Quit(0);
		}
		else
		{
			foreach (var failure in _failures)
				GD.PushError("CHECK FAIL: " + failure);
			GD.Print($"OVERLAY_REHOME_CHECK: FAIL ({_failures.Count})");
			GetTree().Quit(1);
		}
	}
}
