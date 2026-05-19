using Godot;
using System.Collections.Generic;
using System.IO;

public partial class FirstWindow : Control
{
	const string ProjectGitHubUrl = "https://github.com/wwwXiaoHan17/gEmuera";
	const string FeedbackQqGroup = "413675556";
	const string LauncherSettingsPath = "user://launcher.cfg";
	const string LauncherSettingsSection = "launcher";
	const string LauncherLastGamePathKey = "last_game_path";
	const string LauncherLastCoreProfileKey = "last_core_profile";
	public const string CoreProfileV24Pure = "v24pure";
	public const string CoreProfileSnake = "snake";

	enum LauncherGameCategory
	{
		V24Pure,
		Snake,
		All
	}

	public static string SelectedGamePath { get; private set; }
	public static string SelectedCoreProfileName { get; private set; } = CoreProfileV24Pure;

	ItemList gameList;
	Button startButton;
	Label statusLabel;
	OptionButton categoryButton;
	Label categoryHintLabel;
	Label selectedGameLabel;
	Control announcementOverlay;
	Label announcementStatusLabel;
	LauncherGameCategory currentCategory = LauncherGameCategory.V24Pure;
	bool androidPermissionCheckPending = false;
	bool androidPermissionResultReceived = false;
	bool gameListDragActive = false;
	bool gameListDragMoved = false;
	Vector2 gameListDragStartPosition = Vector2.Zero;
	Vector2 gameListDragLastPosition = Vector2.Zero;
	ulong gameListDragLastTick = 0;
	float gameListScrollVelocity = 0.0f;
	const float GameListDragThreshold = 8.0f;
	const float GameListInertiaDeceleration = 2600.0f;

	public override void _Ready()
	{
		FrameRateHelper.Apply();
		ResolutionHelper.Apply();

		BuildLauncherUi();

		if (OS.GetName() == "Android")
		{
			GetTree().OnRequestPermissionsResult += OnPermissionsResult;
			androidPermissionCheckPending = true;
			statusLabel.Text = "正在请求文件权限...\nRequesting file permissions...";
			OS.RequestPermissions();
			GetTree().CreateTimer(2.5).Timeout += OnPermissionRequestFallbackTimeout;
		}
		else
		{
			ScanGames();
		}
	}

	void BuildLauncherUi()
	{
		var background = new ColorRect();
		background.Color = new Color(0.07f, 0.085f, 0.105f);
		background.MouseFilter = MouseFilterEnum.Ignore;
		background.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(background);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_top", 22);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		AddChild(margin);

		var root = new VBoxContainer();
		root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		root.SizeFlagsVertical = SizeFlags.ExpandFill;
		root.AddThemeConstantOverride("separation", 10);
		margin.AddChild(root);

		root.AddChild(CreateHeader());

		var body = new HBoxContainer();
		body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.SizeFlagsVertical = SizeFlags.ExpandFill;
		body.AddThemeConstantOverride("separation", 14);
		root.AddChild(body);

		body.AddChild(CreateOptionsPanel());
		body.AddChild(CreateGamePanel());

		statusLabel = new Label();
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		statusLabel.AddThemeColorOverride("font_color", new Color(0.78f, 0.84f, 0.9f));
		statusLabel.AddThemeFontSizeOverride("font_size", 14);
		root.AddChild(statusLabel);
	}

	Control CreateHeader()
	{
		var header = new HBoxContainer();
		header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddThemeConstantOverride("separation", 12);

		var titleBlock = new VBoxContainer();
		titleBlock.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddChild(titleBlock);

		var title = new Label();
		title.Text = MultiLanguage.Get("FirstWindow.Title", "gEmuera");
		title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		title.AddThemeFontSizeOverride("font_size", 28);
		title.AddThemeColorOverride("font_color", new Color(0.96f, 0.98f, 1.0f));
		titleBlock.AddChild(title);

		var noticeButton = new Button();
		noticeButton.Text = MultiLanguage.Get("FirstWindow.NoticeButton", "公告");
		noticeButton.CustomMinimumSize = new Vector2(96, 44);
		noticeButton.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		noticeButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		noticeButton.AddThemeFontSizeOverride("font_size", 17);
		noticeButton.Pressed += ShowAnnouncementDialog;
		header.AddChild(noticeButton);

		return header;
	}

	void ShowAnnouncementDialog()
	{
		if (announcementOverlay == null)
			announcementOverlay = CreateAnnouncementOverlay();

		announcementOverlay.Visible = true;
		announcementOverlay.MoveToFront();
	}

	Control CreateAnnouncementOverlay()
	{
		var overlay = new Control();
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = MouseFilterEnum.Stop;
		overlay.Visible = false;
		AddChild(overlay);

		var dim = new ColorRect();
		dim.Color = new Color(0, 0, 0, 0.52f);
		dim.MouseFilter = MouseFilterEnum.Stop;
		dim.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(dim);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 18);
		margin.AddThemeConstantOverride("margin_top", 28);
		margin.AddThemeConstantOverride("margin_right", 18);
		margin.AddThemeConstantOverride("margin_bottom", 28);
		overlay.AddChild(margin);

		var panel = CreatePanel(new Color(0.105f, 0.125f, 0.15f), new Color(0.26f, 0.33f, 0.39f));
		panel.SizeFlagsVertical = SizeFlags.ExpandFill;
		margin.AddChild(panel);

		var content = CreatePanelContent(panel, 14);
		content.AddThemeConstantOverride("separation", 10);

		var header = new HBoxContainer();
		header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddThemeConstantOverride("separation", 12);
		content.AddChild(header);

		var title = CreateSectionTitle(MultiLanguage.Get("FirstWindow.NoticeTitle", "公告"));
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddChild(title);

		var closeButton = new Button();
		closeButton.Text = "X";
		closeButton.CustomMinimumSize = new Vector2(44, 40);
		closeButton.Pressed += HideAnnouncementOverlay;
		header.AddChild(closeButton);

		var tabs = new TabContainer();
		tabs.CustomMinimumSize = new Vector2(0, 120);
		tabs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tabs.SizeFlagsVertical = SizeFlags.ExpandFill;
		content.AddChild(tabs);

		tabs.AddChild(CreateNoticeTab());
		tabs.AddChild(CreateFeedbackTab());
		tabs.AddChild(CreateProjectTab());

		var okButton = new Button();
		okButton.Text = "OK";
		okButton.CustomMinimumSize = new Vector2(0, 44);
		okButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		okButton.Pressed += HideAnnouncementOverlay;
		content.AddChild(okButton);

		return overlay;
	}

	void HideAnnouncementOverlay()
	{
		if (announcementOverlay != null)
			announcementOverlay.Visible = false;
	}

	Control CreateNoticeTab()
	{
		var content = CreateDialogTab(MultiLanguage.Get("FirstWindow.NoticeTitle", "公告"));

		var body = CreateDialogText(MultiLanguage.Get("FirstWindow.NoticeBody",
			"如果遇到 bug、兼容性问题或游戏无法正常运行，可以加入 QQ 群反馈。\n\n选择游戏时请先确认分类：v24 用普通 emuera 游戏（兼容 v18）；snake 用蛇版 TW 等 snake 核心游戏；All 会显示所有可识别游戏。"));
		content.AddChild(body);

		return content;
	}

	Control CreateFeedbackTab()
	{
		var content = CreateDialogTab(MultiLanguage.Get("FirstWindow.FeedbackTab", "反馈"));

		content.AddChild(CreateDialogText(MultiLanguage.Get("FirstWindow.FeedbackBody", "遇到 bug 可以进群反馈，反馈时尽量带上游戏名、操作步骤和报错截图。")));

		var qqField = new LineEdit();
		qqField.Text = FeedbackQqGroup;
		qqField.Editable = false;
		qqField.SelectAllOnFocus = true;
		qqField.CustomMinimumSize = new Vector2(120, 36);
		qqField.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		qqField.AddThemeFontSizeOverride("font_size", 16);
		qqField.TooltipText = MultiLanguage.Get("FirstWindow.QQTooltip", "QQ群号，可选中复制");
		content.AddChild(qqField);

		var copyButton = new Button();
		copyButton.Text = MultiLanguage.Get("FirstWindow.CopyQQ", "复制群号");
		copyButton.CustomMinimumSize = new Vector2(0, 40);
		copyButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		copyButton.Pressed += CopyFeedbackGroup;
		content.AddChild(copyButton);

		announcementStatusLabel = new Label();
		announcementStatusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		announcementStatusLabel.AddThemeFontSizeOverride("font_size", 13);
		announcementStatusLabel.AddThemeColorOverride("font_color", new Color(0.67f, 0.76f, 0.84f));
		content.AddChild(announcementStatusLabel);

		return content;
	}

	Control CreateProjectTab()
	{
		var content = CreateDialogTab(MultiLanguage.Get("FirstWindow.ProjectTab", "项目"));

		content.AddChild(CreateDialogText(MultiLanguage.Get("FirstWindow.Author", "Author: 恋雨朦胧/xiao_han17")));

		var githubButton = new LinkButton();
		githubButton.Text = MultiLanguage.Get("FirstWindow.GitHub", $"GitHub: {ProjectGitHubUrl}");
		githubButton.TooltipText = ProjectGitHubUrl;
		githubButton.AddThemeFontSizeOverride("font_size", 16);
		githubButton.Pressed += () => OS.ShellOpen(ProjectGitHubUrl);
		content.AddChild(githubButton);

		return content;
	}

	VBoxContainer CreateDialogTab(string name)
	{
		var content = new VBoxContainer();
		content.Name = name;
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		content.SizeFlagsVertical = SizeFlags.ExpandFill;
		content.AddThemeConstantOverride("separation", 12);
		return content;
	}

	Label CreateDialogText(string text)
	{
		var label = new Label();
		label.Text = text;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.AddThemeFontSizeOverride("font_size", 15);
		label.AddThemeColorOverride("font_color", new Color(0.88f, 0.92f, 0.96f));
		return label;
	}

	Control CreateOptionsPanel()
	{
		var panel = CreatePanel(new Color(0.095f, 0.115f, 0.14f), new Color(0.2f, 0.25f, 0.31f));
		panel.SizeFlagsVertical = SizeFlags.ExpandFill;
		panel.CustomMinimumSize = new Vector2(280, 0);

		var content = CreatePanelContent(panel, 16);
		content.AddThemeConstantOverride("separation", 12);

		content.AddChild(CreateSectionTitle(MultiLanguage.Get("FirstWindow.Options", "选项")));
		content.AddChild(CreateCategorySelector());

		categoryHintLabel = new Label();
		categoryHintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		categoryHintLabel.AddThemeFontSizeOverride("font_size", 13);
		categoryHintLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.86f));
		content.AddChild(categoryHintLabel);
		UpdateCategoryHint();

		var refreshButton = new Button();
		refreshButton.Text = MultiLanguage.Get("FirstWindow.Refresh", "刷新列表");
		refreshButton.CustomMinimumSize = new Vector2(0, 42);
		refreshButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		refreshButton.Pressed += ScanGames;
		content.AddChild(refreshButton);

		content.AddChild(new HSeparator());
		content.AddChild(CreateSectionTitle(MultiLanguage.Get("FirstWindow.NoticeTitle", "公告")));
		content.AddChild(CreateInlineAnnouncement());

		return panel;
	}

	Control CreateInlineAnnouncement()
	{
		var box = new VBoxContainer();
		box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		box.SizeFlagsVertical = SizeFlags.ExpandFill;
		box.AddThemeConstantOverride("separation", 10);

		var notice = new RichTextLabel();
		notice.BbcodeEnabled = true;
		notice.FitContent = false;
		notice.ScrollActive = true;
		notice.CustomMinimumSize = new Vector2(0, 130);
		notice.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		notice.SizeFlagsVertical = SizeFlags.ExpandFill;
		notice.AddThemeFontSizeOverride("normal_font_size", 14);
		notice.Text = MultiLanguage.Get("FirstWindow.NoticeBody",
			"[b]启动提示[/b]\n"
			+ "选择 v24 可运行普通 Emuera 游戏；snake 用于 TW、蛇系核心游戏；All 会显示全部识别到的游戏。\n\n"
			+ "如果游戏使用 [[名称]] 语法，请保留 csv/_Rename.csv。");
		box.AddChild(notice);

		var feedback = new Label();
		feedback.Text = MultiLanguage.Get("FirstWindow.FeedbackBody", $"反馈 QQ 群：{FeedbackQqGroup}");
		feedback.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		feedback.AddThemeFontSizeOverride("font_size", 13);
		feedback.AddThemeColorOverride("font_color", new Color(0.74f, 0.82f, 0.9f));
		box.AddChild(feedback);

		var copyButton = new Button();
		copyButton.Text = MultiLanguage.Get("FirstWindow.CopyQQ", "复制群号");
		copyButton.CustomMinimumSize = new Vector2(0, 40);
		copyButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		copyButton.Pressed += CopyFeedbackGroup;
		box.AddChild(copyButton);

		var githubButton = new LinkButton();
		githubButton.Text = MultiLanguage.Get("FirstWindow.GitHubShort", "GitHub 项目页");
		githubButton.TooltipText = ProjectGitHubUrl;
		githubButton.AddThemeFontSizeOverride("font_size", 14);
		githubButton.Pressed += () => OS.ShellOpen(ProjectGitHubUrl);
		box.AddChild(githubButton);

		return box;
	}

	Control CreateGamePanel()
	{
		var panel = CreatePanel(new Color(0.105f, 0.125f, 0.15f), new Color(0.2f, 0.25f, 0.31f));
		panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		panel.SizeFlagsVertical = SizeFlags.ExpandFill;
		panel.CustomMinimumSize = new Vector2(360, 0);

		var content = CreatePanelContent(panel, 16);
		content.AddThemeConstantOverride("separation", 12);

		content.AddChild(CreateSectionTitle(MultiLanguage.Get("FirstWindow.SelectGame", "选择游戏")));
		gameList = new ItemList();
		gameList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		gameList.SizeFlagsVertical = SizeFlags.ExpandFill;
		gameList.CustomMinimumSize = new Vector2(0, 260);
		gameList.AddThemeFontSizeOverride("font_size", 20);
		gameList.AddThemeConstantOverride("v_separation", 12);
		gameList.AddThemeConstantOverride("line_separation", 8);
		gameList.ItemSelected += OnGameSelected;
		gameList.ItemActivated += OnGameActivated;
		gameList.GuiInput += OnGameListGuiInput;
		content.AddChild(gameList);

		selectedGameLabel = new Label();
		selectedGameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		selectedGameLabel.AddThemeFontSizeOverride("font_size", 13);
		selectedGameLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.86f));
		selectedGameLabel.Text = MultiLanguage.Get("FirstWindow.SelectHint", "选择一个游戏后启动。");
		content.AddChild(selectedGameLabel);

		startButton = new Button();
		startButton.Text = MultiLanguage.Get("FirstWindow.Start", "Start");
		startButton.Disabled = true;
		startButton.CustomMinimumSize = new Vector2(0, 52);
		startButton.AddThemeFontSizeOverride("font_size", 18);
		startButton.Pressed += OnStartPressed;
		content.AddChild(startButton);

		return panel;
	}

	Control CreateCategorySelector()
	{
		var box = new VBoxContainer();
		box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		box.AddThemeConstantOverride("separation", 6);

		var label = new Label();
		label.Text = MultiLanguage.Get("FirstWindow.Category", "分类");
		label.AddThemeFontSizeOverride("font_size", 14);
		label.AddThemeColorOverride("font_color", new Color(0.84f, 0.89f, 0.94f));
		box.AddChild(label);

		categoryButton = new OptionButton();
		categoryButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		categoryButton.CustomMinimumSize = new Vector2(0, 44);
		categoryButton.AddThemeFontSizeOverride("font_size", 17);
		categoryButton.AddItem(MultiLanguage.Get("FirstWindow.CategoryV24Pure", "v24"), (int)LauncherGameCategory.V24Pure);
		categoryButton.AddItem(MultiLanguage.Get("FirstWindow.CategorySnake", "snake"), (int)LauncherGameCategory.Snake);
		categoryButton.AddItem(MultiLanguage.Get("FirstWindow.CategoryAll", "All"), (int)LauncherGameCategory.All);
		categoryButton.Select((int)currentCategory);
		categoryButton.ItemSelected += OnCategorySelected;
		box.AddChild(categoryButton);

		return box;
	}

	PanelContainer CreatePanel(Color backgroundColor, Color borderColor)
	{
		var panel = new PanelContainer();
		panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		panel.AddThemeStyleboxOverride("panel", CreatePanelStyle(backgroundColor, borderColor));
		return panel;
	}

	VBoxContainer CreatePanelContent(PanelContainer panel, int marginSize)
	{
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", marginSize);
		margin.AddThemeConstantOverride("margin_top", marginSize);
		margin.AddThemeConstantOverride("margin_right", marginSize);
		margin.AddThemeConstantOverride("margin_bottom", marginSize);
		panel.AddChild(margin);

		var content = new VBoxContainer();
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		content.SizeFlagsVertical = SizeFlags.ExpandFill;
		margin.AddChild(content);
		return content;
	}

	Label CreateSectionTitle(string text)
	{
		var label = new Label();
		label.Text = text;
		label.AddThemeFontSizeOverride("font_size", 20);
		label.AddThemeColorOverride("font_color", new Color(0.97f, 0.98f, 1.0f));
		return label;
	}

	StyleBoxFlat CreatePanelStyle(Color backgroundColor, Color borderColor)
	{
		var style = new StyleBoxFlat();
		style.BgColor = backgroundColor;
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(8);
		return style;
	}

	void CopyFeedbackGroup()
	{
		DisplayServer.ClipboardSet(FeedbackQqGroup);
		if (announcementStatusLabel != null)
			announcementStatusLabel.Text = MultiLanguage.Get("FirstWindow.QQCopied", "QQ群号已复制，可以粘贴分享给需要反馈的人。");
		if (statusLabel != null)
			statusLabel.Text = "";
	}

	void OnCategorySelected(long index)
	{
		if (categoryButton == null)
			return;

		currentCategory = (LauncherGameCategory)categoryButton.GetItemId((int)index);
		UpdateCategoryHint();
		ScanGames();
	}

	void UpdateCategoryHint()
	{
		if (categoryHintLabel == null)
			return;

		categoryHintLabel.Text = BuildCategoryHint();
	}

	string BuildCategoryHint()
	{
		return currentCategory switch
		{
			LauncherGameCategory.Snake => $"snake：扫描 {GetSnakeRootHint()}，使用 snake 核心。",
			LauncherGameCategory.All => $"All：显示 {GetNormalRootHint()} 下所有可识别游戏，包括 snake。",
			_ => $"v24：扫描 {GetNormalRootHint()} 下的普通游戏，兼容 v18/v24。"
		};
	}

	public override void _Process(double delta)
	{
		if (gameListDragActive || Mathf.Abs(gameListScrollVelocity) < 1.0f)
			return;

		ScrollGameListBy(gameListScrollVelocity * (float)delta);
		gameListScrollVelocity = Mathf.MoveToward(gameListScrollVelocity, 0.0f, GameListInertiaDeceleration * (float)delta);
	}

	public override void _ExitTree()
	{
		if (OS.GetName() == "Android")
			GetTree().OnRequestPermissionsResult -= OnPermissionsResult;
	}

	void OnPermissionsResult(string permission, bool granted)
	{
		androidPermissionResultReceived = true;
		ContinueAfterPermissionRequest();
	}

	void OnPermissionRequestFallbackTimeout()
	{
		if (OS.GetName() == "Android" && androidPermissionCheckPending && !androidPermissionResultReceived)
			ContinueAfterPermissionRequest();
	}

	public override void _Notification(int what)
	{
		if (statusLabel == null || OS.GetName() != "Android" || !androidPermissionCheckPending)
			return;

		if (what == NotificationApplicationResumed || what == NotificationApplicationFocusIn)
			ContinueAfterPermissionRequest();
	}

	void ContinueAfterPermissionRequest()
	{
		statusLabel.Text = "";

		// Check if we can actually access the target directory
		bool canAccess = false;
		using (var dir = DirAccess.Open("/storage/emulated/0"))
		{
			canAccess = dir != null;
		}

		if (!canAccess)
		{
			// MANAGE_EXTERNAL_STORAGE not granted — guide user to settings
			statusLabel.Text = "需要\"所有文件访问\"权限。请在系统设置中开启后返回此应用。\n"
				+ "\"All files access\" permission required. Please enable in system settings.";
			// Still try to scan — user:// and app-specific dirs don't need this permission
		}
		androidPermissionCheckPending = !canAccess;

		// Try to create the emuera directory
		using (var dir = DirAccess.Open("/storage/emulated/0"))
		{
			if (dir != null && !dir.DirExists("emuera"))
				dir.MakeDir("emuera");
		}

		ScanGames();
	}

	void ScanGames()
	{
		string selectedPath = null;
		var selectedItems = gameList.GetSelectedItems();
		if (selectedItems.Length > 0 && !gameList.IsItemDisabled(selectedItems[0]))
			selectedPath = gameList.GetItemMetadata(selectedItems[0]).As<string>();
		if (string.IsNullOrEmpty(selectedPath))
			selectedPath = ResolveStartupGamePath();

		gameList.Clear();
		startButton.Disabled = true;
		if (selectedGameLabel != null)
			selectedGameLabel.Text = MultiLanguage.Get("FirstWindow.SelectHint", "Select a game to start.");

		var roots = GetScanRoots(currentCategory);
		bool includeSnake = currentCategory != LauncherGameCategory.V24Pure;

		var addedPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
		foreach (var root in roots)
			ScanGameRoot(root, selectedPath, addedPaths, includeSnake);

		if (gameList.ItemCount == 0)
		{
			gameList.AddItem(MultiLanguage.Get("FirstWindow.NoGames", "No era games found"));
			gameList.SetItemDisabled(0, true);
			if (string.IsNullOrEmpty(statusLabel.Text))
				statusLabel.Text = "请将 era 游戏文件夹放入以下路径:\n" + string.Join("\n", roots);
		}
	}

	List<string> GetScanRoots(LauncherGameCategory category)
	{
		var roots = new List<string>();
		var baseRoots = GetBaseScanRoots();

		if (category == LauncherGameCategory.Snake)
		{
			foreach (var root in baseRoots)
				AddUniqueRoot(roots, root.TrimEnd('/', '\\') + "/snake");
			return roots;
		}

		foreach (var root in baseRoots)
			AddUniqueRoot(roots, root);
		return roots;
	}

	List<string> GetBaseScanRoots()
	{
		var roots = new List<string>();

		if (OS.GetName() == "Android")
		{
			AddUniqueRoot(roots, "/storage/emulated/0/emuera");
			string appDir = OS.GetUserDataDir();
			if (!string.IsNullOrEmpty(appDir))
				AddUniqueRoot(roots, appDir);
		}
		else
		{
			string exeDir = OS.GetExecutablePath().GetBaseDir();
			if (!string.IsNullOrEmpty(exeDir))
				AddUniqueRoot(roots, exeDir);

			if (OS.HasFeature("editor"))
			{
				string resDir = ProjectSettings.GlobalizePath("res://");
				if (!string.IsNullOrEmpty(resDir))
					AddUniqueRoot(roots, resDir);
			}
		}

		string userDir = ProjectSettings.GlobalizePath("user://");
		if (!string.IsNullOrEmpty(userDir))
			AddUniqueRoot(roots, userDir);

		return roots;
	}

	void AddUniqueRoot(List<string> roots, string root)
	{
		if (string.IsNullOrEmpty(root))
			return;

		string normalized = root.TrimEnd('/', '\\');
		foreach (var existing in roots)
		{
			if (PathsEqual(existing, normalized))
				return;
		}
		roots.Add(normalized);
	}

	string GetNormalRootHint()
	{
		if (OS.GetName() == "Android")
			return "/storage/emulated/0/emuera";

		string exeDir = OS.GetExecutablePath().GetBaseDir();
		return string.IsNullOrEmpty(exeDir) ? "emuera" : exeDir.TrimEnd('/', '\\');
	}

	string GetSnakeRootHint()
	{
		if (OS.GetName() == "Android")
			return "/storage/emulated/0/emuera/snake";

		return GetNormalRootHint().TrimEnd('/', '\\') + "/snake";
	}

	void ScanGameRoot(string root, string selectedPath, HashSet<string> addedPaths, bool includeSnake)
	{
		if (string.IsNullOrEmpty(root))
			return;
		root = root.TrimEnd('/', '\\');
		ScanGameDirectory(root, root, 0, OS.GetName() == "Android" ? 8 : 3, selectedPath, addedPaths, includeSnake);
	}

	void ScanGameDirectory(string root, string path, int depth, int maxDepth, string selectedPath, HashSet<string> addedPaths, bool includeSnake)
	{
		if (string.IsNullOrEmpty(path) || depth > maxDepth)
			return;

		if (IsEraGameDirectory(path))
		{
			AddGamePath(root, path.TrimEnd('/'), selectedPath, addedPaths);
			return;
		}

		using var dir = DirAccess.Open(path);
		if (dir == null)
			return;

		dir.IncludeHidden = true;
		foreach (string entry in dir.GetDirectories())
		{
			if (string.IsNullOrEmpty(entry) || entry == "." || entry == "..")
				continue;
			if (!includeSnake && string.Equals(entry, "snake", System.StringComparison.OrdinalIgnoreCase))
				continue;
			string child = path.TrimEnd('/') + "/" + entry;
			ScanGameDirectory(root, child, depth + 1, maxDepth, selectedPath, addedPaths, includeSnake);
		}
	}

	bool IsEraGameDirectory(string path)
	{
		if (string.IsNullOrEmpty(path))
			return false;
		return HasSubDirectory(path, "erb")
			&& (HasSubDirectory(path, "csv") || HasSubDirectory(path, "dat") || HasSubDirectory(path, "resources"));
	}

	bool HasSubDirectory(string path, string name)
	{
		return uEmuera.Utils.DirectoryExists(path.TrimEnd('/') + "/" + name)
			|| uEmuera.Utils.DirectoryExists(path.TrimEnd('/') + "/" + name.ToUpperInvariant());
	}

	void AddGamePath(string root, string fullPath, string selectedPath, HashSet<string> addedPaths)
	{
		if (!addedPaths.Add(fullPath))
			return;

		string label = GetGameDisplayName(root, fullPath);
		gameList.AddItem(label);
		gameList.SetItemMetadata(gameList.ItemCount - 1, fullPath);
		gameList.SetItemTooltip(gameList.ItemCount - 1, fullPath);
		if (PathsEqual(fullPath, selectedPath))
		{
			gameList.Select(gameList.ItemCount - 1);
			startButton.Disabled = false;
			UpdateSelectedGameLabel(gameList.ItemCount - 1);
		}
	}

	string GetGameDisplayName(string root, string fullPath)
	{
		string normalizedRoot = root.TrimEnd('/');
		if (fullPath.StartsWith(normalizedRoot + "/", System.StringComparison.OrdinalIgnoreCase))
			return fullPath.Substring(normalizedRoot.Length + 1);
		return Path.GetFileName(fullPath.TrimEnd('/'));
	}

	void OnGameSelected(long index)
	{
		startButton.Disabled = false;
		UpdateSelectedGameLabel((int)index);
	}

	void UpdateSelectedGameLabel(int index)
	{
		if (selectedGameLabel == null || gameList == null || index < 0 || index >= gameList.ItemCount || gameList.IsItemDisabled(index))
			return;

		string path = gameList.GetItemMetadata(index).As<string>();
		selectedGameLabel.Text = $"{gameList.GetItemText(index)}\n{path}";
	}

	void OnGameListGuiInput(InputEvent inputEvent)
	{
		if (gameList == null)
			return;

		if (inputEvent is InputEventScreenDrag screenDrag)
		{
			if (!gameListDragActive)
				BeginGameListDrag(screenDrag.Position);
			HandleGameListDrag(screenDrag.Position, screenDrag.Relative);
			AcceptEvent();
			return;
		}

		if (inputEvent is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			if (mouseButton.Pressed)
				BeginGameListDrag(mouseButton.Position);
			else
				EndGameListDrag();
			return;
		}

		if (inputEvent is InputEventMouseMotion mouseMotion && gameListDragActive && (mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
		{
			HandleGameListDrag(mouseMotion.Position, mouseMotion.Relative);
			if (gameListDragMoved)
				AcceptEvent();
		}
	}

	void BeginGameListDrag(Vector2 position)
	{
		gameListDragActive = true;
		gameListDragMoved = false;
		gameListDragStartPosition = position;
		gameListDragLastPosition = position;
		gameListDragLastTick = Time.GetTicksMsec();
		gameListScrollVelocity = 0.0f;
	}

	void HandleGameListDrag(Vector2 position, Vector2 relative)
	{
		if (!gameListDragActive)
			BeginGameListDrag(position);

		if (!gameListDragMoved && (position - gameListDragStartPosition).Length() >= GameListDragThreshold)
			gameListDragMoved = true;

		if (gameListDragMoved)
			ScrollGameListBy(relative.Y);

		ulong now = Time.GetTicksMsec();
		float elapsed = Mathf.Max((now - gameListDragLastTick) / 1000.0f, 0.001f);
		gameListScrollVelocity = relative.Y / elapsed;
		gameListDragLastPosition = position;
		gameListDragLastTick = now;
	}

	void EndGameListDrag()
	{
		gameListDragActive = false;
		if (!gameListDragMoved)
			gameListScrollVelocity = 0.0f;
	}

	void ScrollGameListBy(float verticalDelta)
	{
		var bar = gameList?.GetVScrollBar();
		if (bar == null)
			return;

		float nextValue = Mathf.Clamp((float)bar.Value - verticalDelta, (float)bar.MinValue, (float)bar.MaxValue);
		bar.Value = nextValue;
	}

	void OnGameActivated(long index)
	{
		if (gameList.IsItemDisabled((int)index))
			return;
		string selectedPath = gameList.GetItemMetadata((int)index).As<string>();
		SetSelectedGamePath(selectedPath, GetSelectedCoreProfileName(selectedPath));
		GetTree().ChangeSceneToFile("res://main.tscn");
	}

	void OnStartPressed()
	{
		var selected = gameList.GetSelectedItems();
		if (selected.Length == 0)
			return;
		string selectedPath = gameList.GetItemMetadata(selected[0]).As<string>();
		SetSelectedGamePath(selectedPath, GetSelectedCoreProfileName(selectedPath));
		GetTree().ChangeSceneToFile("res://main.tscn");
	}

	public static string ResolveStartupGamePath()
	{
		if (IsUsableEraGameDirectory(SelectedGamePath))
			return SelectedGamePath;

		string saved = LoadLastGamePath();
		if (IsUsableEraGameDirectory(saved))
		{
			SelectedGamePath = saved;
			SelectedCoreProfileName = LoadLastCoreProfileName();
			return saved;
		}

		return null;
	}

	string GetSelectedCoreProfileName(string selectedPath)
	{
		if (currentCategory == LauncherGameCategory.Snake)
			return CoreProfileSnake;
		if (currentCategory == LauncherGameCategory.All && IsUnderSnakeRoot(selectedPath))
			return CoreProfileSnake;
		return CoreProfileV24Pure;
	}

	bool IsUnderSnakeRoot(string path)
	{
		if (string.IsNullOrEmpty(path))
			return false;

		foreach (string root in GetBaseScanRoots())
		{
			string snakeRoot = root.TrimEnd('/', '\\') + "/snake";
			if (PathsEqual(path, snakeRoot)
				|| path.TrimEnd('/', '\\').StartsWith(snakeRoot.TrimEnd('/', '\\') + "/", System.StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	static void SetSelectedGamePath(string path, string coreProfileName = CoreProfileV24Pure)
	{
		if (string.IsNullOrEmpty(path))
			return;

		SelectedGamePath = path.TrimEnd('/', '\\');
		SelectedCoreProfileName = NormalizeCoreProfileName(coreProfileName);
		SaveLastGamePath(SelectedGamePath, SelectedCoreProfileName);
	}

	static string NormalizeCoreProfileName(string coreProfileName)
	{
		if (string.Equals(coreProfileName, CoreProfileSnake, System.StringComparison.OrdinalIgnoreCase))
			return CoreProfileSnake;
		return CoreProfileV24Pure;
	}

	static string LoadLastGamePath()
	{
		var config = new ConfigFile();
		if (config.Load(LauncherSettingsPath) != Error.Ok)
			return null;

		return config.GetValue(LauncherSettingsSection, LauncherLastGamePathKey, "").As<string>();
	}

	static string LoadLastCoreProfileName()
	{
		var config = new ConfigFile();
		if (config.Load(LauncherSettingsPath) != Error.Ok)
			return CoreProfileV24Pure;

		return NormalizeCoreProfileName(config.GetValue(LauncherSettingsSection, LauncherLastCoreProfileKey, CoreProfileV24Pure).As<string>());
	}

	static void SaveLastGamePath(string path, string coreProfileName)
	{
		if (string.IsNullOrEmpty(path))
			return;

		var config = new ConfigFile();
		config.Load(LauncherSettingsPath);
		config.SetValue(LauncherSettingsSection, LauncherLastGamePathKey, path);
		config.SetValue(LauncherSettingsSection, LauncherLastCoreProfileKey, NormalizeCoreProfileName(coreProfileName));
		config.Save(LauncherSettingsPath);
	}

	static string FindFirstEraGameDirectory(string root, int maxDepth)
	{
		if (string.IsNullOrEmpty(root))
			return null;

		root = root.TrimEnd('/', '\\');
		return FindFirstEraGameDirectoryRecursive(root, 0, maxDepth);
	}

	static string FindFirstEraGameDirectoryRecursive(string path, int depth, int maxDepth)
	{
		if (string.IsNullOrEmpty(path) || depth > maxDepth)
			return null;

		if (IsUsableEraGameDirectory(path))
			return path.TrimEnd('/', '\\');

		using var dir = DirAccess.Open(path);
		if (dir == null)
			return null;

		dir.IncludeHidden = true;
		foreach (string entry in dir.GetDirectories())
		{
			if (string.IsNullOrEmpty(entry) || entry == "." || entry == "..")
				continue;

			string found = FindFirstEraGameDirectoryRecursive(path.TrimEnd('/', '\\') + "/" + entry, depth + 1, maxDepth);
			if (!string.IsNullOrEmpty(found))
				return found;
		}

		return null;
	}

	static bool IsUsableEraGameDirectory(string path)
	{
		if (string.IsNullOrEmpty(path))
			return false;

		return HasEraSubDirectory(path, "erb")
			&& (HasEraSubDirectory(path, "csv") || HasEraSubDirectory(path, "dat") || HasEraSubDirectory(path, "resources"));
	}

	static bool HasEraSubDirectory(string path, string name)
	{
		return uEmuera.Utils.DirectoryExists(path.TrimEnd('/', '\\') + "/" + name)
			|| uEmuera.Utils.DirectoryExists(path.TrimEnd('/', '\\') + "/" + name.ToUpperInvariant());
	}

	static bool PathsEqual(string left, string right)
	{
		if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
			return false;

		return string.Equals(left.TrimEnd('/', '\\'), right.TrimEnd('/', '\\'), System.StringComparison.OrdinalIgnoreCase);
	}
}
