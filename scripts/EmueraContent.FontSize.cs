// EmueraContent.FontSize.cs —— 运行时核心字号 API 收敛域（与内容缩放 SetContentScale 严格分离）。
// 背景：OptionWindow 的「字号」滑杆曾是内容缩放的代理（字号/核心字号 换算成 scale 后
// 调 SetContentScale），字体与缩放共用一个状态。本文件提供真正的字号 API：
// 改 Config.FontSize/LineHeight（镜像 SetConfig 钳制规则）+ 字体缓存与 PRINTC 阈值失效
// + 辅助 UI 刷新。仅影响之后生成的行；已显示行的几何在输出时已固化为像素，保持原字号。
// 生效时机：解释器空闲帧才应用 —— Config.ClearFont 与解释器线程并发读 fontDic 存在竞态
// （同 ConfigData.Inspector.cs 记载的运行时改配置竞态），输入等待点是安全写入窗口。

using Godot;
using System;
using System.Collections.Generic;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Content;

public partial class EmueraContent
{
	// UI 可请求的字号范围（核心下限 8 另有 FontSizeScaling.ClampFontSize 兜底）。
	internal const int FontSizeRequestMin = 8;
	internal const int FontSizeRequestMax = 48;

	// 待应用的字号请求；int.MinValue 表示无挂起请求。拖动滑杆连续触发自然合并为最后一次。
	int pendingFontSize = int.MinValue;

	/// <summary>当前核心字号（Config.FontSize，未配置时回退 18）。UI 展示用。</summary>
	public int ConsoleFontSize => FontSize;

	/// <summary>
	/// 字号 API 入口：请求修改核心字号。不立即生效 —— 在解释器空闲帧应用
	/// （见 ProcessPendingFontSize）。范围钳到 [FontSizeRequestMin, FontSizeRequestMax]。
	/// 持久化：会话内生效，不写回 emuera.config。
	/// </summary>
	public void RequestFontSize(int fontSize)
	{
		pendingFontSize = Mathf.Clamp(fontSize, FontSizeRequestMin, FontSizeRequestMax);
	}

	// 每帧维护：存在挂起请求且解释器不在执行脚本时应用字号。
	void ProcessPendingFontSize()
	{
		if (pendingFontSize == int.MinValue)
			return;
		if (EmueraThread.instance == null || EmueraThread.instance.Running())
			return;
		int fontSize = pendingFontSize;
		pendingFontSize = int.MinValue;
		ApplyFontSize(fontSize);
	}

	void ApplyFontSize(int fontSize)
	{
		// 核心侧：字号/行高/派生排版参数/字体缓存（Config.SetRuntimeFontSize；
		// ConfigData/GETCONFIG 保持基准值不同步，见该方法注释）。
		Config.SetRuntimeFontSize(fontSize);
		// PRINTC 像素分栏阈值按字号懒计算且无过期机制，必须显式失效。
		GlobalStatic.Console?.InvalidatePrintCWidthCache();
		// UI 侧：辅助 pads/msgbox 字号刷新 + 渲染面重绘（RefreshFontSize）；
		// DrawableWidth 已随字号变化，内容最小宽/滚动范围需对账（QueueScaleBoundsUpdate）。
		RefreshFontSize();
		QueueScaleBoundsUpdate();
		// WindowX 已随字号缩放：移动端按新 DrawableWidth 重算 fit（内容物理大小按
		// 补偿后的 fit 呈现，避免横向溢出滚动）；桌面端此调用直接返回。
		ApplyMobileViewportPolicy();
	}
}
