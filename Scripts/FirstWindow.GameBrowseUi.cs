using Godot;
using gEmuera.GodotHost;
using System;
using System.Collections.Generic;
using System.IO;

// FirstWindow 的游戏选择扩展（partial）：在扫描列表之外用 FileDialog 浏览任意目录添加游戏。
//   移动端优先：扫描列表仍是主选择面（一手可见、免输入）；本文件补"游戏不在扫描根"
//   的扩展通道——浏览 → 校验（IsEraGameDirectory：erb + csv/dat/resources 子目录）→
//   以 ManualBrowse 来源入列并持久化（launcher.cfg [launcher] manual_games，
//   "profile|path" 分号清单，LauncherSettingsStore 原始存取），重启启动器后仍在列表。
//   手动条目按添加时所在标签的兼容 profile 记录（v24pure/snake）；高级兼容模式
//   仍按既有语义在每次启动时覆盖生效 profile（GetSelectedCoreProfileName）。
//   可选中手动条目后移除（只清记录，不动游戏文件）；路径失效的条目在刷新时自动剔除。
//   本文件不得含方言 branch marker 标识（profile 值只用常量与参数，见
//   FILE_STANDARD §6 与 dialect-classification 约束）。
public partial class FirstWindow
{
	FileDialog gameBrowseDialog;
	Button removeManualGameButton;

	const string ManualGamePairSeparator = "|";

	// —— UI 构建 ————————————————————————————————————

	Control CreateGameBrowseControls()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);

		var browseButton = new Button();
		browseButton.Text = MultiLanguage.Get("FirstWindow.BrowseGame", "浏览游戏目录…");
		browseButton.TooltipText = MultiLanguage.Get(
			"FirstWindow.BrowseGameTooltip",
			"选择任意位置的游戏文件夹加入列表（按当前标签的兼容 profile 记录，可用高级兼容模式覆盖启动 profile）");
		browseButton.CustomMinimumSize = new Vector2(0, 44);
		browseButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		browseButton.AddThemeFontSizeOverride("font_size", 15);
		GEmueraTheme.ApplyButton(browseButton, GEmueraTheme.Surface, GEmueraTheme.Border);
		browseButton.Pressed += OnBrowseGamesPressed;
		row.AddChild(browseButton);

		removeManualGameButton = new Button();
		removeManualGameButton.Text = MultiLanguage.Get("FirstWindow.RemoveManualGame", "移除");
		removeManualGameButton.TooltipText = MultiLanguage.Get(
			"FirstWindow.RemoveManualGameTooltip",
			"把选中的手动添加条目从列表移除（只清启动器记录，不删除游戏文件）");
		removeManualGameButton.CustomMinimumSize = new Vector2(96, 44);
		removeManualGameButton.AddThemeFontSizeOverride("font_size", 15);
		GEmueraTheme.ApplyButton(removeManualGameButton, GEmueraTheme.Surface, GEmueraTheme.Border);
		removeManualGameButton.Disabled = true;
		removeManualGameButton.Pressed += OnRemoveManualGamePressed;
		row.AddChild(removeManualGameButton);

		return row;
	}

	// —— 浏览（FileDialog，桌面与 Android 共用；目录选择模式） ————————

	void OnBrowseGamesPressed()
	{
		if (gameBrowseDialog != null && GodotObject.IsInstanceValid(gameBrowseDialog)
			&& gameBrowseDialog.Visible)
			return;
		gameBrowseDialog = new FileDialog();
		gameBrowseDialog.Title = MultiLanguage.Get("FirstWindow.BrowseGameTitle", "选择游戏文件夹");
		gameBrowseDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
		gameBrowseDialog.Access = FileDialog.AccessEnum.Filesystem;
		string root = GetGameBrowseRootPath();
		if (!string.IsNullOrEmpty(root) && DirAccess.DirExistsAbsolute(root))
			gameBrowseDialog.CurrentDir = root;
		gameBrowseDialog.DirSelected += OnBrowseGameDirSelected;
		gameBrowseDialog.Canceled += () =>
		{
			if (GodotObject.IsInstanceValid(gameBrowseDialog))
				gameBrowseDialog.QueueFree();
		};
		AddChild(gameBrowseDialog);
		// 移动端优先：按屏幕比例开大窗（对话框内列表/按钮触控目标继承引擎主题）。
		gameBrowseDialog.PopupCenteredRatio(0.9f);
	}

	/// <summary>浏览起始目录：Android=主存储 emuera 容器（应用已获全文件访问）；
	/// 桌面=上次启动游戏的目录，其次 exe 目录。</summary>
	string GetGameBrowseRootPath()
	{
		if (OS.GetName() == "Android")
		{
			return uEmuera.Utils.DirectoryExists("/storage/emulated/0/emuera")
				? "/storage/emulated/0/emuera"
				: "/storage/emulated/0";
		}
		string lastGame = LauncherSettingsStore.LoadLastGamePath();
		if (!string.IsNullOrEmpty(lastGame))
		{
			string lastParent = Path.GetDirectoryName(lastGame.TrimEnd('/', '\\'));
			if (!string.IsNullOrEmpty(lastParent) && uEmuera.Utils.DirectoryExists(lastParent))
				return lastParent;
		}
		string exeDir = OS.GetExecutablePath().GetBaseDir();
		return uEmuera.Utils.DirectoryExists(exeDir) ? exeDir : "";
	}

	void OnBrowseGameDirSelected(string dir)
	{
		if (gameBrowseDialog != null && GodotObject.IsInstanceValid(gameBrowseDialog))
			gameBrowseDialog.QueueFree();
		if (string.IsNullOrEmpty(dir))
			return;

		string gameRoot = dir.TrimEnd('/', '\\');
		if (!IsEraGameDirectory(gameRoot))
		{
			statusLabel.Text = MultiLanguage.Get(
				"FirstWindow.NotEraGame",
				"所选目录不是 era 游戏（需含 erb 与 csv/dat/resources 子目录）：") + "\n" + gameRoot;
			return;
		}

		// 同路径已存在则更新其 profile 记录（重选目录 = 改 profile 的便捷路径），否则追加。
		var kept = new List<string>();
		foreach (string raw in LoadManualGamePairsForEdit())
		{
			if (TrySplitManualGamePair(raw, out _, out string existingPath)
				&& string.Equals(existingPath, gameRoot, StringComparison.OrdinalIgnoreCase))
				continue;
			kept.Add(raw);
		}
		kept.Add(GetCurrentCategoryDefaultProfileId() + ManualGamePairSeparator + gameRoot);
		LauncherSettingsStore.SaveManualGameEntries(kept);

		ScanGames();

		// 程序化选中新增条目并复用手动选择的副作用链（程序化 Select 不触发信号）。
		for (int index = 0; index < gameEntries.Count; index++)
		{
			if (string.Equals(gameEntries[index].GameRoot, gameRoot, StringComparison.OrdinalIgnoreCase))
			{
				gameList.Select(index);
				OnGameSelected(index);
				break;
			}
		}
		statusLabel.Text = MultiLanguage.Get("FirstWindow.ManualGameAdded", "已添加：") + Path.GetFileName(gameRoot);
	}

	// —— 手动条目装配（随 ScanGames 重建） ——————————————————

	/// <summary>把持久化的手动游戏追加进扫描结果：路径已失效/记录损坏的自动剔除并回写；
	/// 与扫描条目同路径时扫描优先（AddGameEntry 的 addedPaths 去重），记录保留不删——
	/// 目录以后移出扫描根时手动条目自动恢复可见。profile 记录顺手归一（手改 cfg 的
	/// 大小写变体），上限溢出提示并入 scanMessages（AddGameEntry 同纪律）。</summary>
	void AppendManualGameEntries(
		List<LauncherGameEntry> entries,
		HashSet<string> addedPaths,
		List<string> scanMessages)
	{
		var kept = new List<string>();
		bool changed = false;
		foreach (string raw in LoadManualGamePairsForEdit())
		{
			if (!TrySplitManualGamePair(raw, out string profileId, out string gameRoot)
				|| !TryNormalizeCoreProfileName(profileId, out string normalizedProfileId)
				|| !IsEraGameDirectory(gameRoot))
			{
				changed = true;
				continue;
			}
			if (!string.Equals(normalizedProfileId, profileId, StringComparison.Ordinal))
			{
				changed = true;
				profileId = normalizedProfileId;
			}
			kept.Add(profileId + ManualGamePairSeparator + gameRoot);
			AddGameEntry(
				entries,
				addedPaths,
				Path.GetFileName(gameRoot),
				gameRoot,
				profileId,
				LauncherGameSource.ManualBrowse,
				scanMessages);
		}
		if (changed)
			LauncherSettingsStore.SaveManualGameEntries(kept);
	}

	// —— 移除手动条目 ————————————————————————————————————

	void OnRemoveManualGamePressed()
	{
		if (!TryGetSelectedGameEntry(out LauncherGameEntry entry)
			|| entry.Source != LauncherGameSource.ManualBrowse)
			return;
		var kept = new List<string>();
		foreach (string raw in LoadManualGamePairsForEdit())
		{
			if (TrySplitManualGamePair(raw, out _, out string existingPath)
				&& string.Equals(existingPath, entry.GameRoot, StringComparison.OrdinalIgnoreCase))
				continue;
			kept.Add(raw);
		}
		LauncherSettingsStore.SaveManualGameEntries(kept);
		ScanGames();
	}

	/// <summary>移除按钮只在选中手动条目时可用（选择变化与扫描重建后都会刷新）。</summary>
	void UpdateRemoveManualGameButtonState()
	{
		if (removeManualGameButton == null)
			return;
		removeManualGameButton.Disabled = !(TryGetSelectedGameEntry(out LauncherGameEntry entry)
			&& entry.Source == LauncherGameSource.ManualBrowse);
	}

	// —— 存取辅助 ————————————————————————————————————

	static IReadOnlyList<string> LoadManualGamePairsForEdit()
		=> LauncherSettingsStore.LoadManualGameEntries();

	/// <summary>拆 "profile|path"；profile 或路径为空、路径含分隔符歧义的记录判为损坏。</summary>
	static bool TrySplitManualGamePair(string raw, out string profileId, out string gameRoot)
	{
		profileId = "";
		gameRoot = "";
		if (string.IsNullOrEmpty(raw))
			return false;
		int separator = raw.IndexOf(ManualGamePairSeparator, StringComparison.Ordinal);
		if (separator <= 0 || separator == raw.Length - 1)
			return false;
		profileId = raw[..separator];
		gameRoot = raw[(separator + 1)..].TrimEnd('/', '\\');
		return profileId.Length > 0 && gameRoot.Length > 0;
	}

	/// <summary>手动条目的默认 profile = 添加时所在标签的目录路由 profile
	///（与扫描条目同源：v24 标签→v24pure、snake 标签→snake）。</summary>
	string GetCurrentCategoryDefaultProfileId()
		=> currentCategory == LauncherGameCategory.Snake ? CoreProfileSnake : CoreProfileV24Pure;
}
