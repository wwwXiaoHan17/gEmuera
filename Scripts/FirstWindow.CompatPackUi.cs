using Godot;
using GEmuera.Core.Compatibility.Packs;
using gEmuera.GodotHost;
using System.Collections.Generic;

// FirstWindow 的兼容包选择 UI（partial）：
//   手输路径 LineEdit 升级为“扫描候选 + 勾选 + 手动路径”合并模型（移动端优先——
//   Android 上手输绝对路径近乎不可用）。存储层与语义完全不变：仍是
//   launcher.cfg [compat_packs] 的分号分隔路径串（LauncherSettingsStore raw 读写、
//   CompatPackLauncherConfig 解析/归一），本文件只做 UI 层映射：
//   - 勾选列表：扫描候选根（桌面=exe 同级 compat_packs/，Android=
//     /storage/emulated/0/emuera/packs/）下的 *.dll，多选，勾选变化即提交；
//   - 手动路径 LineEdit：高级用途；已存但不在扫描列表中的路径回填到这里，不丢数据；
//   - 提交时合并两区（勾选在前、手动在后），相对路径按启动器根绝对化——只在
//     启动器侧实现，不改 CompatPackHost 的加载解析。
//   编辑态带“当前为哪个游戏加载”的键（compatPackEditGameKey）：未加载或键不匹配时
//   CommitCompatPackEdit 不提交——P1 缺陷兜底，程序化恢复选中不触发 item_selected，
//   旧实现会在用户直接点 Start 时把空文本写回、抹掉该游戏已存的包选择。
public partial class FirstWindow
{
	const string DesktopCompatPackDirectoryName = "compat_packs";
	const string AndroidCompatPackRoot = "/storage/emulated/0/emuera/packs";
	// 候选上限：兼容包根目录一般只放个位数 dll；上限防异常目录拖慢扫描与列表。
	const int MaxCompatPackCandidates = 64;
	// 列表可视高度按行数自适应：1~4 行直接展示，更多则内部滚动（48px 触控行高）。
	const int CompatPackRowHeight = 48;
	const int CompatPackListMaxVisibleHeight = 196;

	// —— 兼容包 UI 状态（仅本 partial 使用）——
	LineEdit compatPackPathsEdit;                  // 手动路径（分号分隔，高级用途）
	Button compatPackBrowseButton;                 // “浏览…”（桌面；Android 靠扫描列表）
	ScrollContainer compatPackListScroll;
	VBoxContainer compatPackListContainer;
	Label compatPackHeaderLabel;
	Label compatPackEmptyHint;
	FileDialog compatPackBrowseDialog;
	// 扫描结果（绝对路径，'/' 分隔，按文件名排序）与勾选行（重建列表时生成）。
	readonly List<string> compatPackCandidatePaths = new();
	readonly List<CompatPackCandidateRow> compatPackCandidateRows = new();
	// 编辑态对应的游戏键（CompatPackLauncherConfig.NormalizeGameKey）。
	// null = 本会话尚未为任何游戏加载过（提交防御的判据）。
	string compatPackEditGameKey;
	bool compatPackUiEnabled;

	sealed record CompatPackCandidateRow(CheckButton Toggle, string PackPath);

	// —— UI 构建 ————————————————————————————————————

	Control CreateCompatPackPicker()
	{
		var section = new VBoxContainer();
		section.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		section.AddThemeConstantOverride("separation", 6);

		compatPackHeaderLabel = new Label();
		compatPackHeaderLabel.Text = MultiLanguage.Get(
			"FirstWindow.CompatPackSection",
			"兼容包（勾选启用，按所选游戏保存）");
		compatPackHeaderLabel.TooltipText = MultiLanguage.Get(
			"FirstWindow.CompatPackPathsTooltip",
			"为当前选中的游戏启用兼容包（.dll，需内嵌清单）。留空 = 纯 v24；加载失败自动回退并记录日志。");
		compatPackHeaderLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		compatPackHeaderLabel.AddThemeFontSizeOverride("font_size", 13);
		compatPackHeaderLabel.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
		section.AddChild(compatPackHeaderLabel);

		compatPackListScroll = new ScrollContainer();
		compatPackListScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		compatPackListScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		ApplyWideVerticalScrollbar(compatPackListScroll.GetVScrollBar());
		section.AddChild(compatPackListScroll);

		compatPackListContainer = new VBoxContainer();
		compatPackListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		compatPackListContainer.AddThemeConstantOverride("separation", 2);
		compatPackListScroll.AddChild(compatPackListContainer);

		// 手动路径行：高级用途的补充入口，与勾选列表合并成最终路径集合。
		var manualRow = new HBoxContainer();
		manualRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		manualRow.AddThemeConstantOverride("separation", 8);
		section.AddChild(manualRow);

		compatPackPathsEdit = new LineEdit();
		compatPackPathsEdit.PlaceholderText = MultiLanguage.Get(
			"FirstWindow.CompatPackManualPaths",
			"手动路径（分号分隔，高级）");
		compatPackPathsEdit.TooltipText = MultiLanguage.Get(
			"FirstWindow.CompatPackManualPathsTooltip",
			"补充手输的兼容包路径（.dll），与上方勾选合并生效；相对路径按启动器根目录解析。");
		compatPackPathsEdit.CustomMinimumSize = new Vector2(0, 40);
		compatPackPathsEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		compatPackPathsEdit.AddThemeFontSizeOverride("font_size", 14);
		compatPackPathsEdit.TextSubmitted += _ => CommitCompatPackEdit();
		compatPackPathsEdit.FocusExited += CommitCompatPackEdit;
		manualRow.AddChild(compatPackPathsEdit);

		// “浏览…”只上桌面：Android 无可靠的原生文件选择链路，靠扫描列表即可。
		if (OS.GetName() != "Android")
		{
			compatPackBrowseButton = new Button();
			compatPackBrowseButton.Text = MultiLanguage.Get("FirstWindow.CompatPackBrowse", "浏览…");
			compatPackBrowseButton.CustomMinimumSize = new Vector2(84, 40);
			compatPackBrowseButton.AddThemeFontSizeOverride("font_size", 14);
			GEmueraTheme.ApplyButton(compatPackBrowseButton, GEmueraTheme.Surface, GEmueraTheme.Border);
			compatPackBrowseButton.Pressed += OnCompatPackBrowsePressed;
			manualRow.AddChild(compatPackBrowseButton);
		}

		RebuildCompatPackListUi();
		return section;
	}

	// —— 候选扫描（与 ScanGames 同生命周期：进页/切 tab/授权后刷新）—————

	void ScanCompatPackCandidates()
	{
		// 编辑态重置：扫描会重建勾选列表，在 PopulateGameList 恢复选中并回填之前，
		// 编辑态不属于任何游戏（提交防御判据回到“未加载”）。
		compatPackEditGameKey = null;
		compatPackCandidatePaths.Clear();
		foreach (string root in GetCompatPackScanRoots())
		{
			using var dir = DirAccess.Open(root);
			if (dir == null)
				continue;
			dir.IncludeHidden = true;
			foreach (string file in dir.GetFiles())
			{
				if (string.IsNullOrEmpty(file) || !file.EndsWith(".dll", System.StringComparison.OrdinalIgnoreCase))
					continue;
				if (compatPackCandidatePaths.Count >= MaxCompatPackCandidates)
					break;
				compatPackCandidatePaths.Add(root.TrimEnd('/', '\\') + "/" + file);
			}
		}
		compatPackCandidatePaths.Sort(System.StringComparer.OrdinalIgnoreCase);
		RebuildCompatPackListUi();
	}

	// 桌面=exe 同级 compat_packs/（与游戏扫描根的 exe 目录惯例一致）；编辑器模式
	// 额外扫 res://compat_packs，便于不往编辑器目录拷 dll 调试；Android=主存储
	// emuera 容器下的 packs/。目录不存在时安静跳过（空态提示在 UI 层展示）。
	List<string> GetCompatPackScanRoots()
	{
		var roots = new List<string>();
		if (OS.GetName() == "Android")
		{
			AddUniqueRoot(roots, AndroidCompatPackRoot);
			return roots;
		}

		string exeDir = OS.GetExecutablePath().GetBaseDir();
		if (!string.IsNullOrEmpty(exeDir))
			AddUniqueRoot(roots, exeDir.TrimEnd('/', '\\') + "/" + DesktopCompatPackDirectoryName);
		if (OS.HasFeature("editor"))
		{
			string resDir = ProjectSettings.GlobalizePath("res://");
			if (!string.IsNullOrEmpty(resDir))
				AddUniqueRoot(roots, resDir.TrimEnd('/', '\\') + "/" + DesktopCompatPackDirectoryName);
		}
		return roots;
	}

	string GetPrimaryCompatPackRootHint()
	{
		var roots = GetCompatPackScanRoots();
		return roots.Count > 0 ? roots[0] : DesktopCompatPackDirectoryName;
	}

	void RebuildCompatPackListUi()
	{
		if (compatPackListContainer == null)
			return;
		compatPackCandidateRows.Clear();
		foreach (Node child in compatPackListContainer.GetChildren())
			child.QueueFree();

		if (compatPackCandidatePaths.Count == 0)
		{
			compatPackEmptyHint = new Label();
			compatPackEmptyHint.Text = MultiLanguage.Get(
				"FirstWindow.CompatPackEmpty",
				$"未找到可勾选的兼容包。将 .dll 放入 {GetPrimaryCompatPackRootHint()} 后重新进入本页即可刷新。");
			compatPackEmptyHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			compatPackEmptyHint.AddThemeFontSizeOverride("font_size", 13);
			compatPackEmptyHint.AddThemeColorOverride("font_color", GEmueraTheme.TextDim);
			compatPackListContainer.AddChild(compatPackEmptyHint);
			compatPackListScroll.CustomMinimumSize = new Vector2(0, 40);
			ApplyCompatPackControlsEnabledState();
			return;
		}

		compatPackEmptyHint = null;
		// 展示名默认用文件名；重名时前缀父目录消歧（tooltip 始终是完整路径）。
		var duplicateFileNames = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
		foreach (string path in compatPackCandidatePaths)
		{
			string fileName = path.GetFile();
			duplicateFileNames.TryGetValue(fileName, out int count);
			duplicateFileNames[fileName] = count + 1;
		}
		foreach (string path in compatPackCandidatePaths)
		{
			string fileName = path.GetFile();
			string display = duplicateFileNames[fileName] > 1
				? path.GetBaseDir().GetFile() + "/" + fileName
				: fileName;
			var toggle = new CheckButton();
			toggle.Text = display;
			toggle.TooltipText = path;
			toggle.CustomMinimumSize = new Vector2(0, CompatPackRowHeight);
			toggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			toggle.AddThemeFontSizeOverride("font_size", 14);
			toggle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			toggle.Toggled += _ => CommitCompatPackEdit();
			compatPackListContainer.AddChild(toggle);
			compatPackCandidateRows.Add(new CompatPackCandidateRow(toggle, path));
		}
		compatPackListScroll.CustomMinimumSize = new Vector2(
			0,
			System.Math.Min(CompatPackRowHeight * compatPackCandidateRows.Count + 4, CompatPackListMaxVisibleHeight));
		ApplyCompatPackControlsEnabledState();
	}

	void SetCompatPackControlsEnabled(bool enabled)
	{
		compatPackUiEnabled = enabled;
		ApplyCompatPackControlsEnabledState();
	}

	void ApplyCompatPackControlsEnabledState()
	{
		if (compatPackPathsEdit != null)
			compatPackPathsEdit.Editable = compatPackUiEnabled;
		if (compatPackBrowseButton != null)
			compatPackBrowseButton.Disabled = !compatPackUiEnabled;
		foreach (CompatPackCandidateRow row in compatPackCandidateRows)
			row.Toggle.Disabled = !compatPackUiEnabled;
	}

	void RefreshCompatPackHeader()
	{
		if (compatPackHeaderLabel == null)
			return;
		int enabledCount = 0;
		foreach (CompatPackCandidateRow row in compatPackCandidateRows)
		{
			if (row.Toggle.ButtonPressed)
				enabledCount++;
		}
		if (compatPackPathsEdit != null)
			enabledCount += CompatPackLauncherConfig.ParseSelection(compatPackPathsEdit.Text).Count;
		compatPackHeaderLabel.Text = enabledCount > 0
			? string.Format(
				MultiLanguage.Get("FirstWindow.CompatPackSectionActive", "兼容包（已启用 {0} 项，按所选游戏保存）"),
				enabledCount)
			: MultiLanguage.Get(
				"FirstWindow.CompatPackSection",
				"兼容包（勾选启用，按所选游戏保存）");
	}

	// —— 按游戏加载/提交（launcher.cfg [compat_packs]；存储格式与语义不变）———

	void LoadCompatPackEditForGame(LauncherGameEntry entry)
	{
		if (entry == null)
			return;
		// 切换目标游戏时，先把当前编辑态按其加载时的旧 key 落盘一次：为 A 输入未提交就
		// 点选 B 的场景，A 的手动路径编辑若不在此保存会被 B 的加载直接覆盖丢弃
		//（提交防御只保证不写脏，不保证不丢编辑）。
		string newGameKey = CompatPackLauncherConfig.NormalizeGameKey(entry.GameRoot);
		if (compatPackEditGameKey != null && !string.Equals(compatPackEditGameKey, newGameKey, System.StringComparison.Ordinal))
			SaveCurrentEditForLoadedGame();
		CompatPackLauncherConfig.TryGetSelectionForGame(
			LauncherSettingsStore.LoadCompatPackSelections(),
			entry.GameRoot,
			out IReadOnlyList<string> storedPaths);

		// 已存选择分流：命中扫描候选 → 勾选对应项；未命中 → 保留进手动区显示
		// （UI 升级不丢既有数据）。
		var manualPaths = new List<string>();
		var checkedKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
		foreach (string path in storedPaths)
		{
			string key = GetCompatPackPathKey(path);
			if (IsCompatPackCandidate(key))
				checkedKeys.Add(key);
			else
				manualPaths.Add(path);
		}
		foreach (CompatPackCandidateRow row in compatPackCandidateRows)
			row.Toggle.SetPressedNoSignal(checkedKeys.Contains(GetCompatPackPathKey(row.PackPath)));
		if (compatPackPathsEdit != null)
			compatPackPathsEdit.Text = CompatPackLauncherConfig.SerializeSelection(manualPaths);

		compatPackEditGameKey = CompatPackLauncherConfig.NormalizeGameKey(entry.GameRoot);
		SetCompatPackControlsEnabled(true);
		RefreshCompatPackHeader();
	}

	void CommitCompatPackEdit()
	{
		if (!TryGetSelectedGameEntry(out LauncherGameEntry entry))
			return;
		// P1 防御：编辑态并非为当前选中游戏加载（未被用户会话触碰且未从存储加载）时
		// 不提交——此场景下 UI 是空/陈旧态，写回会按“空串清键”抹掉已存选择。
		string gameKey = CompatPackLauncherConfig.NormalizeGameKey(entry.GameRoot);
		if (compatPackEditGameKey != gameKey)
			return;
		SaveCurrentEditForLoadedGame();
		RefreshCompatPackHeader();
	}

	/// <summary>
	/// 把当前编辑态（勾选区 + 手动区）按 <see cref="compatPackEditGameKey"/>（编辑态的
	/// 来源游戏）落盘。与 CommitCompatPackEdit 同一合并/归一/绝对化管线，但不依赖
	/// 当前选中项——供"提交当前选中"与"切换游戏前保存旧编辑"两个入口共用。
	/// </summary>
	void SaveCurrentEditForLoadedGame()
	{
		if (compatPackEditGameKey == null)
			return;

		// 合并勾选区与手动区（勾选在前、手动在后），大小写不敏感去重；
		// 相对路径按启动器根绝对化后再写存储。
		var paths = new List<string>();
		var seenKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
		foreach (CompatPackCandidateRow row in compatPackCandidateRows)
		{
			if (row.Toggle.ButtonPressed && seenKeys.Add(GetCompatPackPathKey(row.PackPath)))
				paths.Add(row.PackPath);
		}
		if (compatPackPathsEdit != null)
		{
			foreach (string manual in CompatPackLauncherConfig.ParseSelection(compatPackPathsEdit.Text))
			{
				string absolute = AbsoluteizeCompatPackPath(manual);
				if (seenKeys.Add(GetCompatPackPathKey(absolute)))
					paths.Add(absolute);
			}
		}

		// 经 parse→serialize 归一（去空/去重），空串清除该游戏的选择（语义不变）。
		LauncherSettingsStore.SaveCompatPackSelection(
			compatPackEditGameKey,
			CompatPackLauncherConfig.SerializeSelection(
				CompatPackLauncherConfig.ParseSelection(string.Join(";", paths))));
	}

	// —— 路径辅助 ————————————————————————————————————

	bool IsCompatPackCandidate(string pathKey)
	{
		foreach (string candidate in compatPackCandidatePaths)
		{
			if (GetCompatPackPathKey(candidate) == pathKey)
				return true;
		}
		return false;
	}

	// 路径比对键：绝对化 + 统一分隔符 + 去尾斜杠 + 小写（沿用存储键的大小写不敏感惯例）。
	static string GetCompatPackPathKey(string path)
	{
		return AbsoluteizeCompatPackPath(path).TrimEnd('/').ToLowerInvariant();
	}

	/// <summary>
	/// 相对路径按启动器根（桌面=exe 目录，Android=/storage/emulated/0/emuera）绝对化。
	/// 只在启动器侧实现；CompatPackHost 的加载解析不动（那是引擎侧的域）。
	/// </summary>
	static string AbsoluteizeCompatPackPath(string path)
	{
		string trimmed = (path ?? "").Trim();
		if (trimmed.Length == 0)
			return trimmed;
		string normalized = trimmed.Replace('\\', '/');
		if (System.IO.Path.IsPathRooted(trimmed))
			return normalized;
		string launcherRoot = GetCompatPackLauncherRoot();
		if (string.IsNullOrEmpty(launcherRoot))
			return normalized;
		return launcherRoot.TrimEnd('/', '\\') + "/" + normalized.TrimStart('/');
	}

	static string GetCompatPackLauncherRoot()
	{
		if (OS.GetName() == "Android")
			return "/storage/emulated/0/emuera";
		string exeDir = OS.GetExecutablePath().GetBaseDir();
		return string.IsNullOrEmpty(exeDir) ? null : exeDir.TrimEnd('/', '\\');
	}

	// —— “浏览…”（桌面加分项；Android 用扫描列表）—————————————————

	void OnCompatPackBrowsePressed()
	{
		if (compatPackBrowseDialog != null && GodotObject.IsInstanceValid(compatPackBrowseDialog)
			&& compatPackBrowseDialog.Visible)
			return;
		compatPackBrowseDialog = new FileDialog();
		compatPackBrowseDialog.Title = MultiLanguage.Get("FirstWindow.CompatPackBrowseTitle", "选择兼容包（可多选）");
		compatPackBrowseDialog.FileMode = FileDialog.FileModeEnum.OpenFiles;
		compatPackBrowseDialog.Access = FileDialog.AccessEnum.Filesystem;
		compatPackBrowseDialog.Filters = new[] { "*.dll" };
		string primaryRoot = GetPrimaryCompatPackRootHint();
		if (!string.IsNullOrEmpty(primaryRoot) && DirAccess.DirExistsAbsolute(primaryRoot))
			compatPackBrowseDialog.CurrentDir = primaryRoot;
		compatPackBrowseDialog.FilesSelected += OnCompatPackBrowseFilesSelected;
		compatPackBrowseDialog.Canceled += () =>
		{
			if (GodotObject.IsInstanceValid(compatPackBrowseDialog))
				compatPackBrowseDialog.QueueFree();
		};
		AddChild(compatPackBrowseDialog);
		compatPackBrowseDialog.PopupCenteredClamped(new Vector2I(760, 520));
	}

	void OnCompatPackBrowseFilesSelected(string[] files)
	{
		if (compatPackBrowseDialog != null && GodotObject.IsInstanceValid(compatPackBrowseDialog))
			compatPackBrowseDialog.QueueFree();
		if (compatPackPathsEdit == null || files == null || files.Length == 0)
			return;
		// 选中的文件追加进手动区（去重），随后与勾选区一起走统一提交。
		var merged = new List<string>(CompatPackLauncherConfig.ParseSelection(compatPackPathsEdit.Text));
		var seenKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
		foreach (string existing in merged)
			seenKeys.Add(GetCompatPackPathKey(existing));
		foreach (string file in files)
		{
			if (seenKeys.Add(GetCompatPackPathKey(file)))
				merged.Add(file);
		}
		compatPackPathsEdit.Text = CompatPackLauncherConfig.SerializeSelection(merged);
		CommitCompatPackEdit();
	}
}
