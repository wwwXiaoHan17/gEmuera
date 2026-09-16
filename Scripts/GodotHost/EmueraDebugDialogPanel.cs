using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Godot;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using uEmuera.Window;
// 基类从 PanelContainer(Control) 改为 Window(Viewport) 后，Control 嵌套枚举不再隐式可见。
using SizeFlags = Godot.Control.SizeFlags;
using MouseFilterEnum = Godot.Control.MouseFilterEnum;
using FocusModeEnum = Godot.Control.FocusModeEnum;

namespace gEmuera.GodotHost
{
	/// <summary>
	/// worker 线程（DebugDialog 定时快照）推送给主线程面板的只读快照。
	/// 面板一律通过本 DTO 展示引擎态，禁止主线程直接读取 EmueraConsole 的
	/// DebugConsoleLog / GetDebugTraceLog / 变量求值。
	/// </summary>
	public sealed class EmueraDebugSnapshot
	{
		public readonly string ConsoleLog;
		public readonly string TraceLog;
		/// <summary>当前 watch 表达式列表（worker 侧权威回显，用于面板首次同步）。</summary>
		public readonly string[] WatchExpressions;
		/// <summary>与 WatchExpressions 平行的求值结果。</summary>
		public readonly string[] WatchValues;

		public EmueraDebugSnapshot(
			string consoleLog,
			string traceLog,
			string[] watchExpressions,
			string[] watchValues)
		{
			ConsoleLog = consoleLog ?? "";
			TraceLog = traceLog ?? "";
			WatchExpressions = watchExpressions ?? Array.Empty<string>();
			WatchValues = watchValues ?? Array.Empty<string>();
		}
	}

	/// <summary>
	/// Emuera DEBUG 模式调试面板内容（纯 Control 构建，代码构建，不依赖 .tscn）。
	/// 引擎运行在 worker 线程，本内容运行在 Godot 主线程：
	/// - 打开/关闭/聚焦由 worker 的 DebugDialog 通过静态入口调度（uiActions 队列，
	///   由 _Process 在主线程消费，不使用 CallDeferred，避免 C# 方法表注册问题）；
	/// - 展示数据（日志/调用栈/watch 求值）由 worker 定时快照推送到 snapshotQueue。
	/// 三个 Tab：变量监视 / 调用栈 / 调试控制台，深色主题走 GEmueraTheme token。
	///
	/// 本类为共享内容（PanelContainer），被两种宿主复用：
	/// 桌面端由 EmueraDebugDialogPanel（Window）包一层原生 Window（标题栏/拖动/缩放），
	/// Android 端由 EmueraDebugDialogPanel.AttachTo 直接作为全屏覆盖层挂载（嵌入 Window
	/// 在 gl_compatibility 下内容不渲染，见 EmueraDebugDialogPanel 类注释）。
	/// </summary>
	public partial class EmueraDebugDialogContent : PanelContainer
	{
		internal static readonly ConcurrentQueue<EmueraDebugSnapshot> snapshotQueue =
			new ConcurrentQueue<EmueraDebugSnapshot>();

		internal readonly ConcurrentQueue<Action> uiActions = new ConcurrentQueue<Action>();

		// 绑定（仅主线程在 DoShow/DoHide 内读写）
		EmueraConsole boundConsole;
		DebugDialog debugLink;

		/// <summary>桌面端宿主 Window（AttachTo 注入）；Android 覆盖层为 null。</summary>
		internal EmueraDebugDialogPanel hostWindow;
		/// <summary>桌面端宿主在 Show 前应用的窗口几何；Android 覆盖层为 null。</summary>
		public Action BeforeShow;
		/// <summary>Android 覆盖层标记：头部追加关闭按钮（覆盖层没有原生标题栏 ✕）。</summary>
		public bool IsOverlay { get; set; }

		// UI 组件
		TabContainer tabs;
		VBoxContainer watchRowsRoot;
		LineEdit addExpressionInput;
		LineEdit commandInput;
		ScrollContainer logScroll;
		RichTextLabel consoleLogText;
		ScrollContainer traceScroll;
		RichTextLabel traceText;

		readonly List<WatchRow> watchRows = new List<WatchRow>();
		string[] localExpressions = Array.Empty<string>();

		sealed class WatchRow
		{
			public LineEdit Expression;
			public Label Value;
		}

		#region Godot 生命周期（主线程）

		public override void _Ready()
		{
			// 标题栏/最小尺寸/原生 ✕ 由桌面端 EmueraDebugDialogPanel（Window）负责；
			// 本类只构建纯 Control 内容（Window 版与 Android 覆盖层共用）。
			BuildPanel();
		}

		public override void _Process(double delta)
		{
			while (uiActions.TryDequeue(out var action))
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					GenericUtils.Error(
						$"DEBUG_DIALOG_UI_ACTION_FAILED {ex.GetType().Name}: {ex.Message}");
				}
			}

			if (!Visible)
				return;
			while (snapshotQueue.TryDequeue(out var snap))
			{
				try
				{
					ApplySnapshot(snap);
				}
				catch (Exception ex)
				{
					GenericUtils.Error(
						$"DEBUG_DIALOG_SNAPSHOT_FAILED {ex.GetType().Name}: {ex.Message}");
				}
			}
		}

		#endregion

		#region 显示/隐藏/聚焦

		internal void DoShow(EmueraConsole console, DebugDialog link)
		{
			boundConsole = console;
			debugLink = link;
			BeforeShow?.Invoke(); // 桌面端宿主在此应用窗口几何；Android 覆盖层为 null
			Visible = true;
			if (hostWindow != null)
				hostWindow.Show();
			while (snapshotQueue.TryDequeue(out _)) { } // 丢弃旧会话残留快照
		}

		internal void DoFocus()
		{
			if (!Visible)
			{
				Visible = true;
				if (hostWindow != null)
					hostWindow.Show();
			}
			// 聚焦输入框而非窗口自身。
			if (addExpressionInput != null)
			{
				addExpressionInput.GrabFocus();
				addExpressionInput.CaretColumn = addExpressionInput.Text.Length;
			}
		}

		internal void DoHide()
		{
			Visible = false;
			if (hostWindow != null)
				hostWindow.Hide();
			boundConsole = null;
			debugLink = null;
			while (snapshotQueue.TryDequeue(out _)) { }
		}

		#endregion

		#region 构建

		void BuildPanel()
		{
			// 本类即根面板（PanelContainer）：桌面端由 Window 外壳包一层原生标题栏，
			// Android 端直接 FullRect 填满视口；两种宿主共用同一份内容构建。
			Theme = GEmueraTheme.LoadTheme();
			AddThemeStyleboxOverride(
				"panel",
				GEmueraTheme.SurfaceStyle(
					GEmueraTheme.SurfaceRaised, GEmueraTheme.Border, GEmueraTheme.CardRadius,
					1, 14, new Vector2(0, 6)));
			SetAnchorsPreset(Control.LayoutPreset.FullRect);
			SizeFlagsHorizontal = SizeFlags.ExpandFill;
			SizeFlagsVertical = SizeFlags.ExpandFill;
			MouseFilter = MouseFilterEnum.Stop;

			var margin = new MarginContainer();
			margin.MouseFilter = MouseFilterEnum.Pass;
			margin.AddThemeConstantOverride("margin_left", 12);
			margin.AddThemeConstantOverride("margin_right", 12);
			margin.AddThemeConstantOverride("margin_top", 10);
			margin.AddThemeConstantOverride("margin_bottom", 10);
			AddChild(margin);

			var root = new VBoxContainer();
			root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			root.SizeFlagsVertical = SizeFlags.ExpandFill;
			root.AddThemeConstantOverride("separation", 8);
			root.MouseFilter = MouseFilterEnum.Pass;
			margin.AddChild(root);

			root.AddChild(CreateHeader());

			tabs = CreateTabs();
			root.AddChild(tabs);

			// Tab 1：变量监视
			var watchTab = new VBoxContainer();
			watchTab.Name = "变量监视";
			watchTab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			watchTab.SizeFlagsVertical = SizeFlags.ExpandFill;
			watchTab.AddThemeConstantOverride("separation", 6);
			watchTab.MouseFilter = MouseFilterEnum.Stop;
			tabs.AddChild(watchTab);

			var watchScroll = CreateScroll();
			watchScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
			watchTab.AddChild(watchScroll);

			watchRowsRoot = new VBoxContainer();
			watchRowsRoot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			watchRowsRoot.AddThemeConstantOverride("separation", 4);
			watchRowsRoot.MouseFilter = MouseFilterEnum.Pass;
			watchScroll.AddChild(watchRowsRoot);

			var addRow = new HBoxContainer();
			addRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			addRow.AddThemeConstantOverride("separation", 6);
			watchTab.AddChild(addRow);

			addExpressionInput = new LineEdit { PlaceholderText = "添加监视表达式（如 DAY、CFLAG:100），回车或点添加" };
			addExpressionInput.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			StyleLineEdit(addExpressionInput);
			addExpressionInput.TextSubmitted += _ => AddWatchExpression();
			addRow.AddChild(addExpressionInput);

			var addButton = new Button { Text = "添加" };
			GEmueraTheme.ApplyButton(addButton, GEmueraTheme.Surface, GEmueraTheme.Accent);
			addButton.Pressed += AddWatchExpression;
			addRow.AddChild(addButton);

			// Tab 2：调用栈
			var traceTab = new VBoxContainer();
			traceTab.Name = "调用栈";
			traceTab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			traceTab.SizeFlagsVertical = SizeFlags.ExpandFill;
			traceTab.MouseFilter = MouseFilterEnum.Stop;
			tabs.AddChild(traceTab);

			traceScroll = CreateScroll();
			traceScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
			traceTab.AddChild(traceScroll);

			traceText = CreateReadOnlyText();
			traceScroll.AddChild(traceText);

			// Tab 3：调试控制台
			var consoleTab = new VBoxContainer();
			consoleTab.Name = "调试控制台";
			consoleTab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			consoleTab.SizeFlagsVertical = SizeFlags.ExpandFill;
			consoleTab.AddThemeConstantOverride("separation", 6);
			consoleTab.MouseFilter = MouseFilterEnum.Stop;
			tabs.AddChild(consoleTab);

			logScroll = CreateScroll();
			logScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
			consoleTab.AddChild(logScroll);

			consoleLogText = CreateReadOnlyText();
			logScroll.AddChild(consoleLogText);

			commandInput = new LineEdit { PlaceholderText = "调试命令（如 SET DAY:0、PRINT 文本），回车执行" };
			StyleLineEdit(commandInput);
			commandInput.TextSubmitted += text =>
			{
				if (string.IsNullOrEmpty(text))
					return;
				var link = debugLink;
				if (link != null)
					link.EnqueueCommand(text);
				commandInput.Text = "";
			};
			consoleTab.AddChild(commandInput);
		}

		Control CreateHeader()
		{
			var header = new HBoxContainer();
			header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			header.AddThemeConstantOverride("separation", 8);

			var title = new Label { Text = "调试窗口 (Debug)" };
			title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			title.MouseFilter = MouseFilterEnum.Ignore;
			title.AddThemeFontSizeOverride("font_size", 16);
			title.AddThemeColorOverride("font_color", GEmueraTheme.TextPrimary);
			header.AddChild(title);

			// 桌面端关闭走 Window 标题栏原生 ✕（CloseRequested 信号）；
			// Android 覆盖层没有原生标题栏，必须补一个 ✕ 按钮。
			if (IsOverlay)
			{
				var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(36, 0) };
				GEmueraTheme.ApplyButton(close, GEmueraTheme.Surface, GEmueraTheme.Danger);
				close.Pressed += RequestClose;
				header.AddChild(close);
			}
			return header;
		}

		static TabContainer CreateTabs()
		{
			var tabs = new TabContainer();
			tabs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			tabs.SizeFlagsVertical = SizeFlags.ExpandFill;
			tabs.MouseFilter = MouseFilterEnum.Stop;
			tabs.AddThemeColorOverride("font_selected_color", GEmueraTheme.TextPrimary);
			tabs.AddThemeColorOverride("font_unselected_color", GEmueraTheme.TextSecondary);
			tabs.AddThemeColorOverride("font_hover_color", GEmueraTheme.TextPrimary);
			tabs.AddThemeStyleboxOverride(
				"tab_selected",
				GEmueraTheme.ButtonBox(GEmueraTheme.SurfaceRaised, GEmueraTheme.Accent, GEmueraTheme.SmallRadius));
			tabs.AddThemeStyleboxOverride(
				"tab_unselected",
				GEmueraTheme.ButtonBox(GEmueraTheme.Surface, GEmueraTheme.Border, GEmueraTheme.SmallRadius));
			tabs.AddThemeStyleboxOverride(
				"tab_hover",
				GEmueraTheme.ButtonBox(GEmueraTheme.SurfaceRaised, GEmueraTheme.BorderStrong, GEmueraTheme.SmallRadius));
			tabs.AddThemeStyleboxOverride(
				"panel",
				GEmueraTheme.SurfaceStyle(GEmueraTheme.Surface, GEmueraTheme.Border, GEmueraTheme.SmallRadius));
			return tabs;
		}

		static ScrollContainer CreateScroll()
		{
			var scroll = new ScrollContainer();
			scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
			scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
			scroll.MouseFilter = MouseFilterEnum.Stop;
			GEmueraTheme.ApplyWideScrollbar(scroll.GetVScrollBar(), 14);
			return scroll;
		}

		static RichTextLabel CreateReadOnlyText()
		{
			var text = new RichTextLabel();
			text.BbcodeEnabled = false;
			text.ScrollActive = false;
			// FitContent=true 让高度随内容（ScrollContainer 垂直滚动）；但宽度也会收缩到
			// 内容宽，导致文本靠左留白。必须 ExpandFill 水平撑满 ScrollContainer 宽度。
			text.FitContent = true;
			text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			text.SelectionEnabled = true;
			text.MouseFilter = MouseFilterEnum.Stop;
			text.AddThemeColorOverride("default_color", GEmueraTheme.TextPrimary);
			text.AddThemeColorOverride("font_selected_color", GEmueraTheme.TextPrimary);
			text.AddThemeColorOverride("selection_color", GEmueraTheme.WithAlpha(GEmueraTheme.Accent, 0.4f));
			return text;
		}

		static void StyleLineEdit(LineEdit lineEdit)
		{
			lineEdit.AddThemeStyleboxOverride(
				"normal",
				GEmueraTheme.SurfaceStyle(
					GEmueraTheme.Background, GEmueraTheme.Border, GEmueraTheme.SmallRadius, 1,
					0, null, 6, 6, 4, 4));
			lineEdit.AddThemeStyleboxOverride(
				"focus",
				GEmueraTheme.ButtonBox(GEmueraTheme.Background, GEmueraTheme.Accent, GEmueraTheme.SmallRadius, 2));
			lineEdit.AddThemeColorOverride("font_color", GEmueraTheme.TextPrimary);
			lineEdit.AddThemeColorOverride("font_placeholder_color", GEmueraTheme.TextDim);
			lineEdit.AddThemeColorOverride("caret_color", GEmueraTheme.Accent);
			lineEdit.AddThemeColorOverride("selection_color", GEmueraTheme.WithAlpha(GEmueraTheme.Accent, 0.4f));
		}

		#endregion

		#region 快照应用（主线程）

		void ApplySnapshot(EmueraDebugSnapshot snap)
		{
			if (consoleLogText.Text != snap.ConsoleLog)
			{
				bool stickToBottom =
					logScroll.GetVScrollBar().MaxValue - logScroll.ScrollVertical < 24;
				consoleLogText.Text = snap.ConsoleLog;
				if (stickToBottom)
					logScroll.ScrollVertical = (int)logScroll.GetVScrollBar().MaxValue;
			}
			if (traceText.Text != snap.TraceLog)
			{
				bool stickToBottom =
					traceScroll.GetVScrollBar().MaxValue - traceScroll.ScrollVertical < 24;
				traceText.Text = snap.TraceLog;
				if (stickToBottom)
					traceScroll.ScrollVertical = (int)traceScroll.GetVScrollBar().MaxValue;
			}
			SyncWatchRows(snap);
		}

		void SyncWatchRows(EmueraDebugSnapshot snap)
		{
			if (!ArraysEqual(localExpressions, snap.WatchExpressions))
			{
				localExpressions = snap.WatchExpressions;
				RebuildWatchRows(localExpressions, snap.WatchValues);
				return;
			}
			int count = Math.Min(watchRows.Count, snap.WatchValues.Length);
			for (int i = 0; i < count; i++)
			{
				string value = snap.WatchValues[i];
				if (watchRows[i].Value.Text != value)
					watchRows[i].Value.Text = value;
			}
		}

		static bool ArraysEqual(string[] a, string[] b)
		{
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;
			if (a.Length != b.Length)
				return false;
			for (int i = 0; i < a.Length; i++)
			{
				if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
					return false;
			}
			return true;
		}

		void RebuildWatchRows(string[] expressions, string[] values)
		{
			foreach (Node child in watchRowsRoot.GetChildren())
				child.QueueFree();
			watchRows.Clear();
			for (int i = 0; i < expressions.Length; i++)
				AddWatchRow(expressions[i], i < values.Length ? values[i] : "");
		}

		void AddWatchRow(string expression, string value)
		{
			var row = new HBoxContainer();
			row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			row.AddThemeConstantOverride("separation", 6);
			row.MouseFilter = MouseFilterEnum.Pass;

			var lineEdit = new LineEdit { Text = expression, PlaceholderText = "表达式" };
			lineEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			StyleLineEdit(lineEdit);
			lineEdit.TextSubmitted += _ => PushWatchList();
			lineEdit.FocusExited += () => PushWatchList();
			row.AddChild(lineEdit);

			var valueLabel = new Label { Text = value, CustomMinimumSize = new Vector2(160, 0) };
			valueLabel.MouseFilter = MouseFilterEnum.Ignore;
			valueLabel.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
			valueLabel.ClipText = true;
			row.AddChild(valueLabel);

			int index = watchRows.Count;
			var removeButton = new Button { Text = "✕", CustomMinimumSize = new Vector2(28, 0) };
			GEmueraTheme.ApplyButton(removeButton, GEmueraTheme.Surface, GEmueraTheme.Danger);
			removeButton.Pressed += () => RemoveWatchRowAt(index);
			row.AddChild(removeButton);

			watchRowsRoot.AddChild(row);
			watchRows.Add(new WatchRow { Expression = lineEdit, Value = valueLabel });
		}

		List<string> CollectExpressions()
		{
			var list = new List<string>();
			foreach (var row in watchRows)
			{
				string text = row.Expression.Text?.Trim();
				if (!string.IsNullOrEmpty(text))
					list.Add(text);
			}
			return list;
		}

		void PushWatchList()
		{
			var link = debugLink;
			if (link == null)
				return;
			var list = CollectExpressions();
			localExpressions = list.ToArray();
			link.EnqueueWatchList(list.ToArray());
		}

		void CommitExpressions(List<string> list)
		{
			localExpressions = list.ToArray();
			RebuildWatchRows(localExpressions, new string[localExpressions.Length]);
			PushWatchList();
		}

		void AddWatchExpression()
		{
			string text = addExpressionInput.Text?.Trim();
			if (string.IsNullOrEmpty(text))
				return;
			addExpressionInput.Text = "";
			var list = CollectExpressions();
			list.Add(text);
			CommitExpressions(list);
		}

		void RemoveWatchRowAt(int index)
		{
			if (index < 0 || index >= watchRows.Count)
				return;
			var list = CollectExpressions();
			if (index < list.Count)
				list.RemoveAt(index);
			CommitExpressions(list);
		}

		internal void RequestClose()
		{
			// 面板关闭等价于 DebugDialog.Dispose（worker 侧保存 console.log + watchlist.csv）。
			// Dispose 只能由 worker 线程执行（它操作 uEmuera.Forms.Timer 与引擎缓冲），
			// 因此主线程只投递关闭请求，由 worker 定时器在空闲边界消费。
			// Why：先立即视觉隐藏——Android 端面板是全屏 Control 覆盖层（MouseFilter=Stop），
			// 若等到 worker 空闲才隐藏，用户点 ✕ 后整个游戏会一直无法触摸。
			// worker 到达空闲边界后的 DoHide 再隐藏一次是幂等的。
			Visible = false;
			hostWindow?.Hide();
			var link = debugLink;
			if (link != null)
				link.EnqueueCloseRequest();
			else
				DoHide();
		}

		#endregion
	}

	/// <summary>
	/// Emuera DEBUG 模式调试窗口（Godot 原生 Window）。桌面端以独立 OS 窗口显示
	/// （标题栏/拖动/缩放），内容由共享的 EmueraDebugDialogContent 提供。
	/// Android 不使用本类：AttachTo 直接挂全屏 Control 覆盖层（嵌入 Window 在
	/// gl_compatibility 下内容不渲染，见 EmueraDebugDialogContent 类注释）。
	///
	/// Why（错误 1）：gEmuera 与 DEBUG 窗口保持为 Window 类，不退回画布内嵌面板——
	/// Window 在桌面端是独立 OS 窗口，不遮挡游戏主画面，且带原生标题栏/关闭。
	/// </summary>
	public partial class EmueraDebugDialogPanel : Window
	{
		const int MinWindowWidth = 320;
		const int MinWindowHeight = 240;

		// 当前挂载的内容实例（桌面端 = 本 Window 的内容；Android = 全屏覆盖层内容）。
		// 跨线程读写统一走 Volatile.Read/Write。
		static EmueraDebugDialogContent currentContent;

		internal EmueraDebugDialogContent _content;

		public override void _Ready()
		{
			Title = "调试窗口 (Debug)";
			MinSize = new Vector2I(MinWindowWidth, MinWindowHeight);
			// 标题栏 ✕ → 与覆盖层 ✕ 同一关闭路径（worker 侧 DebugDialog.Dispose）。
			CloseRequested += OnNativeWindowCloseRequested;

			_content = new EmueraDebugDialogContent { Visible = true };
			_content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			_content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			_content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			_content.hostWindow = this;
			_content.BeforeShow = ApplyWindowGeometry;
			AddChild(_content);
		}

		void OnNativeWindowCloseRequested()
		{
			_content.RequestClose();
		}

		#region 静态入口（worker 线程可调用）

		/// <summary>EmueraMain._Ready 挂载调试面板宿主（主线程）。桌面端为原生 Window，Android 为全屏覆盖层。</summary>
		public static void AttachTo(Node parent)
		{
			if (parent == null)
				return;

			EmueraDebugDialogContent content;
			if (OS.GetName() == "Android")
			{
				// Android：嵌入 Window 在 gl_compatibility 下内容不渲染 → 全屏 Control 覆盖层。
				// 独立高 CanvasLayer（高于菜单 100 / tooltip 150）保证盖在游戏内容之上。
				var layer = new CanvasLayer { Layer = 200, Name = "EmueraDebugDialogLayer" };
				parent.AddChild(layer);
				content = new EmueraDebugDialogContent
				{
					Name = "EmueraDebugDialogPanel",
					Visible = false,
					IsOverlay = true,
				};
				content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
				content.MouseFilter = MouseFilterEnum.Stop;
				layer.AddChild(content);
			}
			else
			{
				// 桌面端：原生 Window 外壳（标题栏/拖动/缩放），内容 FullRect 填充。
				var win = new EmueraDebugDialogPanel { Name = "EmueraDebugDialogPanel", Visible = false };
				parent.AddChild(win);
				content = win._content;
			}
			content.TreeExiting += () =>
			{
				if (System.Threading.Volatile.Read(ref currentContent) == content)
					System.Threading.Volatile.Write(ref currentContent, null);
			};
			System.Threading.Volatile.Write(ref currentContent, content);
		}

		// 参数含 internal 类型（EmueraConsole / DebugDialog），保持 internal 可见性。
		internal static void ShowPanel(EmueraConsole console, DebugDialog link)
		{
			var content = System.Threading.Volatile.Read(ref currentContent);
			if (content == null || !GodotObject.IsInstanceValid(content))
				return;
			content.uiActions.Enqueue(() => content.DoShow(console, link));
		}

		internal static void FocusPanel()
		{
			var content = System.Threading.Volatile.Read(ref currentContent);
			if (content == null || !GodotObject.IsInstanceValid(content))
				return;
			content.uiActions.Enqueue(() => content.DoFocus());
		}

		internal static void HidePanel()
		{
			var content = System.Threading.Volatile.Read(ref currentContent);
			if (content == null || !GodotObject.IsInstanceValid(content))
				return;
			content.uiActions.Enqueue(() => content.DoHide());
		}

		internal static void PushSnapshot(EmueraDebugSnapshot snapshot)
		{
			if (snapshot == null)
				return;
			EmueraDebugDialogContent.snapshotQueue.Enqueue(snapshot);
		}

		#endregion

		/// <summary>Show 前由内容 BeforeShow 回调；嵌入 Window 的 Position/Size 用主窗口物理像素。</summary>
		public void ApplyWindowGeometry()
		{
			int width = Math.Max(MinWindowWidth, Config.DebugWindowWidth);
			int height = Math.Max(MinWindowHeight, Config.DebugWindowHeight);
			MinSize = new Vector2I(MinWindowWidth, MinWindowHeight);
			Size = new Vector2I(width, height);
			// 必须用主窗口实际物理尺寸（GetTree().Root.Size），不能用 ProjectSettings 的
			// 逻辑分辨率——否则在拉伸/高分屏上居中坐标严重偏上、标题栏贴顶无法拖动。
			var parentSize = GetParentWindowPixelSize();
			const int topMargin = 12; // 标题栏离开顶部，保证可抓取拖动
			if (Config.DebugSetWindowPos)
				Position = new Vector2I((int)Config.DebugWindowPosX, (int)Config.DebugWindowPosY);
			else
				Position = new Vector2I((parentSize.X - width) / 2, (parentSize.Y - height) / 2);
			Position = new Vector2I(
				Mathf.Clamp(Position.X, 0, Math.Max(0, parentSize.X - width)),
				Mathf.Clamp(Position.Y, topMargin, Math.Max(topMargin, parentSize.Y - height)));
		}

		/// <summary>父窗口（主窗口）内容区的物理像素尺寸；嵌入 Window 定位/钳制的基准。</summary>
		Vector2I GetParentWindowPixelSize()
		{
			var root = GetTree()?.Root;
			if (root != null && GodotObject.IsInstanceValid(root))
				return root.Size;
			// 兜底：退化为 ProjectSettings 逻辑分辨率（尽量接近）。
			return new Vector2I(
				(int)ProjectSettings.GetSetting("display/window/size/viewport_width", 1280),
				(int)ProjectSettings.GetSetting("display/window/size/viewport_height", 720));
		}
	}
}
