// FirstWindow.DiagnosticsSettings.cs —— 承载启动器调试/日志设置页功能域（Emuera DEBUG 段 + gEmuera 日志段 + 等级/分类 + 导出/立即应用），自 FirstWindow.cs 拆出（原因：主文件超 2000 行只减不增约束）。
using Godot;
using GEmuera.Core.Compatibility;
using gEmuera.Diagnostics;
using System.Collections.Generic;
using System.IO;

public partial class FirstWindow : Control
{
	Control CreateDiagnosticsContent()
	{
		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		ApplyWideVerticalScrollbar(scroll.GetVScrollBar());

		var content = new VBoxContainer();
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		content.AddThemeConstantOverride("separation", 14);
		scroll.AddChild(content);

		content.AddChild(CreateSectionTitle(MultiLanguage.Get("FirstWindow.DebugLogTitle", "调试 / 日志")));
		content.AddChild(CreateEmueraDebugSection());
		content.AddChild(CreateLoggingSection());

		return scroll;
	}

	/// <summary>Emuera DEBUG 段：写入 user://launcher.cfg [launcher]，键名与 WS1 引擎侧契约一致。</summary>
	Control CreateEmueraDebugSection()
	{
		LoadLauncherDebugSettings();

		var panel = CreatePanel(GEmueraTheme.Surface, GEmueraTheme.Border, false);
		var body = CreatePanelContent(panel, 14);
		body.AddThemeConstantOverride("separation", 6);

		body.AddChild(CreateGroupTitle(MultiLanguage.Get("FirstWindow.DebugModeGroup", "Emuera DEBUG 模式")));

		emueraDebugModeToggle = CreateSettingsToggle(
			MultiLanguage.Get("FirstWindow.EmueraDebugModeToggle", "Emuera DEBUG 模式"),
			EmueraDebugModeEnabled,
			OnEmueraDebugModeToggled);
		body.AddChild(emueraDebugModeToggle);
		body.AddChild(CreateHintLabel(MultiLanguage.Get("FirstWindow.EmueraDebugModeHint",
			"开启后可执行 DEBUG 系指令（DEBUGPRINT 等），控制台输入 @DEBUG 可打开调试窗口，并支持 [IF_DEBUG] 与 __FILE__ 等调试标记。下次进入游戏生效。")));

		debugShowWindowToggle = CreateSettingsToggle(
			MultiLanguage.Get("FirstWindow.DebugShowWindowToggle", "启动时显示调试窗口"),
			DebugShowWindowEnabled,
			OnDebugShowWindowToggled);
		debugShowWindowToggle.Disabled = !EmueraDebugModeEnabled;
		body.AddChild(debugShowWindowToggle);
		body.AddChild(CreateHintLabel(MultiLanguage.Get("FirstWindow.DebugShowWindowHint",
			"仅在 Emuera DEBUG 模式开启时生效。开启后进入游戏自动打开调试窗口（变量监视 / 调用栈 / 调试控制台）。")));

		return panel;
	}

	/// <summary>
	/// gEmuera 日志段：先 Load 一次 config.toml，勾选后由「立即应用」统一写回并热重载。
	/// config.toml 键名与 RuntimeDiagnosticsConfig 字段名严格一致。
	/// </summary>
	Control CreateLoggingSection()
	{
		diagnosticsLoggingConfig = RuntimeDiagnosticsConfigLoader.Load().Config
			?? RuntimeDiagnosticsConfig.CreateDefault();

		var panel = CreatePanel(GEmueraTheme.Surface, GEmueraTheme.Border, false);
		var body = CreatePanelContent(panel, 14);
		body.AddThemeConstantOverride("separation", 6);

		body.AddChild(CreateGroupTitle(MultiLanguage.Get("FirstWindow.LoggingGroup", "gEmuera 日志")));

		loggingEnabledToggle = CreateSettingsToggle(
			MultiLanguage.Get("FirstWindow.LoggingEnabledToggle", "日志/诊断系统"),
			diagnosticsLoggingConfig.LoggingEnabled,
			OnLoggingEnabledToggled);
		body.AddChild(loggingEnabledToggle);

		fileSinkToggle = CreateSettingsToggle(
			MultiLanguage.Get("FirstWindow.FileSinkToggle", "持续文件日志"),
			diagnosticsLoggingConfig.FileSinkEnabled,
			null);
		body.AddChild(fileSinkToggle);
		body.AddChild(CreateHintLabel(MultiLanguage.Get("FirstWindow.FileSinkHint",
			"持续写入 user://gemuera_runtime_*.log（自动轮转）。仅在日志/诊断系统开启时生效。")));

		panelVisibleToggle = CreateSettingsToggle(
			MultiLanguage.Get("FirstWindow.PanelVisibleToggle", "诊断面板显示"),
			diagnosticsLoggingConfig.RuntimePanelEnabled,
			null);
		body.AddChild(panelVisibleToggle);
		body.AddChild(CreateHintLabel(MultiLanguage.Get("FirstWindow.PanelVisibleHint",
			"控制游戏内诊断面板（悬浮球）显示；进入游戏后即时生效。")));

		mirrorToGodotToggle = CreateSettingsToggle(
			MultiLanguage.Get("FirstWindow.MirrorToGodotToggle", "非错误日志镜像到 Godot 控制台"),
			diagnosticsLoggingConfig.LoggingMirrorNonErrorToGodot,
			null);
		body.AddChild(mirrorToGodotToggle);

		// 初始同步：日志总开关关闭时，持续文件日志开关置灰。
		OnLoggingEnabledToggled(loggingEnabledToggle.ButtonPressed);

		body.AddChild(CreateLogLevelRow());
		body.AddChild(CreateLogCategoryGrid());

		var buttonRow = new HBoxContainer();
		buttonRow.AddThemeConstantOverride("separation", 10);
		buttonRow.AddChild(CreateActionButton(
			MultiLanguage.Get("FirstWindow.ExportLogButton", "导出日志"), ExportDiagnosticLogFromLauncher));
		buttonRow.AddChild(CreateActionButton(
			MultiLanguage.Get("FirstWindow.ExportPackageButton", "导出诊断包"), ExportDiagnosticPackageFromLauncher));
		buttonRow.AddChild(CreateActionButton(
			MultiLanguage.Get("FirstWindow.ApplyButton", "立即应用"), ApplyDiagnosticsSettings));
		body.AddChild(buttonRow);

		body.AddChild(CreateHintLabel(MultiLanguage.Get("FirstWindow.RestartHint",
			"Emuera DEBUG 模式与持续文件日志在下次进入游戏时生效；其余设置保存后立即热重载。")));

		diagnosticsStatusLabel = new Label();
		diagnosticsStatusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		diagnosticsStatusLabel.AddThemeFontSizeOverride("font_size", 13);
		diagnosticsStatusLabel.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
		body.AddChild(diagnosticsStatusLabel);

		return panel;
	}

	Control CreateLogLevelRow()
	{
		var row = new HBoxContainer();
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddThemeConstantOverride("separation", 10);

		var label = new Label();
		label.Text = MultiLanguage.Get("FirstWindow.LogLevelLabel", "日志等级");
		label.AddThemeFontSizeOverride("font_size", 15);
		label.AddThemeColorOverride("font_color", GEmueraTheme.TextPrimary);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(label);

		logLevelOption = new OptionButton();
		logLevelOption.CustomMinimumSize = new Vector2(150, 44);
		logLevelOption.AddThemeFontSizeOverride("font_size", 15);
		// 与 config.toml [logging] level 的值域一致（设计契约 error|warn|info|debug|trace）。
		foreach (string level in LogLevelOptions)
			logLevelOption.AddItem(level);
		logLevelOption.Select(NormalizeLogLevelIndex(diagnosticsLoggingConfig.LoggingLevel));
		row.AddChild(logLevelOption);

		return row;
	}

	Control CreateLogCategoryGrid()
	{
		var label = new Label();
		label.Text = MultiLanguage.Get("FirstWindow.LogCategoryLabel", "日志分类");
		label.AddThemeFontSizeOverride("font_size", 15);
		label.AddThemeColorOverride("font_color", GEmueraTheme.TextPrimary);
		var container = new VBoxContainer();
		container.AddThemeConstantOverride("separation", 4);
		container.AddChild(label);

		// 与 RuntimeDiagnosticsConfig.CategorySwitches 字段及 config.toml [logging.categories] 键一一对应。
		var grid = new GridContainer();
		grid.Columns = 2;
		grid.AddThemeConstantOverride("h_separation", 16);
		grid.AddThemeConstantOverride("v_separation", 4);
		AddCategoryToggle(grid, "FirstWindow.LogCategoryGeneral", "通用", diagnosticsLoggingConfig.Categories.General);
		AddCategoryToggle(grid, "FirstWindow.LogCategorySprite", "精灵", diagnosticsLoggingConfig.Categories.Sprite);
		AddCategoryToggle(grid, "FirstWindow.LogCategoryAudio", "音频", diagnosticsLoggingConfig.Categories.Audio);
		AddCategoryToggle(grid, "FirstWindow.LogCategoryInput", "输入", diagnosticsLoggingConfig.Categories.Input);
		AddCategoryToggle(grid, "FirstWindow.LogCategoryScript", "脚本", diagnosticsLoggingConfig.Categories.Script);
		AddCategoryToggle(grid, "FirstWindow.LogCategoryUI", "界面", diagnosticsLoggingConfig.Categories.UI);
		AddCategoryToggle(grid, "FirstWindow.LogCategoryFileSystem", "文件系统", diagnosticsLoggingConfig.Categories.FileSystem);
		AddCategoryToggle(grid, "FirstWindow.LogCategoryLoad", "加载", diagnosticsLoggingConfig.Categories.Load);
		AddCategoryToggle(grid, "FirstWindow.LogCategorySave", "存档", diagnosticsLoggingConfig.Categories.Save);
		AddCategoryToggle(grid, "FirstWindow.LogCategoryConfig", "配置", diagnosticsLoggingConfig.Categories.Config);
		container.AddChild(grid);

		return container;
	}

	static readonly string[] LogLevelOptions = { "error", "warn", "info", "debug", "trace" };

	static int NormalizeLogLevelIndex(string level)
	{
		if (string.IsNullOrEmpty(level))
			return 0;
		for (int i = 0; i < LogLevelOptions.Length; i++)
		{
			if (string.Equals(level.Trim(), LogLevelOptions[i], System.StringComparison.OrdinalIgnoreCase))
				return i;
		}
		return 0;
	}

	CheckButton CreateSettingsToggle(string text, bool pressed, System.Action<bool> onToggled)
	{
		var toggle = new CheckButton();
		toggle.Text = text;
		toggle.ButtonPressed = pressed;
		toggle.CustomMinimumSize = new Vector2(0, 40);
		toggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		toggle.AddThemeFontSizeOverride("font_size", 15);
		if (onToggled != null)
			toggle.Toggled += pressed => onToggled(pressed);
		return toggle;
	}

	void AddCategoryToggle(GridContainer grid, string key, string fallback, bool pressed)
	{
		var toggle = new CheckButton();
		toggle.Text = MultiLanguage.Get(key, fallback);
		toggle.ButtonPressed = pressed;
		toggle.AddThemeFontSizeOverride("font_size", 14);
		grid.AddChild(toggle);
		logCategoryToggles.Add(toggle);
	}

	Button CreateActionButton(string text, System.Action pressed)
	{
		var button = new Button();
		button.Text = text;
		button.CustomMinimumSize = new Vector2(0, 42);
		button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		button.AddThemeFontSizeOverride("font_size", 15);
		GEmueraTheme.ApplyButton(button, GEmueraTheme.Surface, GEmueraTheme.Border);
		button.Pressed += pressed;
		return button;
	}

	Label CreateGroupTitle(string text)
	{
		var label = new Label();
		label.Text = text;
		label.AddThemeFontSizeOverride("font_size", 16);
		label.AddThemeColorOverride("font_color", GEmueraTheme.TextPrimary);
		return label;
	}

	Label CreateHintLabel(string text)
	{
		var label = new Label();
		label.Text = text;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.AddThemeFontSizeOverride("font_size", 13);
		label.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
		return label;
	}

	void OnEmueraDebugModeToggled(bool enabled)
	{
		EmueraDebugModeEnabled = enabled;
		if (debugShowWindowToggle != null)
			debugShowWindowToggle.Disabled = !enabled;
		SaveLauncherDebugSettings();
		SetDiagnosticsStatus(MultiLanguage.Get("FirstWindow.DebugLauncherSaved", "调试设置已保存到 launcher.cfg，下次进入游戏生效。"));
	}

	void OnDebugShowWindowToggled(bool enabled)
	{
		DebugShowWindowEnabled = enabled;
		SaveLauncherDebugSettings();
	}

	void OnLoggingEnabledToggled(bool enabled)
	{
		// 日志总开关关闭时，其余日志子开关失去意义，直接禁用避免误导。
		if (fileSinkToggle != null)
			fileSinkToggle.Disabled = !enabled;
	}

	static void LoadLauncherDebugSettings()
	{
		var config = new ConfigFile();
		if (config.Load(LauncherSettingsPath) != Error.Ok)
			return;

		EmueraDebugModeEnabled = config.GetValue(
			LauncherSettingsSection,
			LauncherEmueraDebugModeKey,
			false).AsBool();
		DebugShowWindowEnabled = config.GetValue(
			LauncherSettingsSection,
			LauncherDebugShowWindowKey,
			true).AsBool();
	}

	static void SaveLauncherDebugSettings()
	{
		var config = new ConfigFile();
		config.Load(LauncherSettingsPath);
		config.SetValue(LauncherSettingsSection, LauncherEmueraDebugModeKey, EmueraDebugModeEnabled);
		config.SetValue(LauncherSettingsSection, LauncherDebugShowWindowKey, DebugShowWindowEnabled);
		config.Save(LauncherSettingsPath);
	}

	/// <summary>
	/// 立即应用：RuntimeDiagnosticsConfigLoader.Load() → 改字段 → SaveUserConfig → 热重载。
	/// 与运行时诊断面板的保存语义一致：非 custom 的 quick_debug preset 会覆盖专家项，需先切到 custom。
	/// </summary>
	void ApplyDiagnosticsSettings()
	{
		var config = diagnosticsLoggingConfig;
		if (config == null)
		{
			SetDiagnosticsStatus(MultiLanguage.Get("FirstWindow.DiagnosticsNotReady", "诊断系统尚未初始化，无法保存。"));
			return;
		}

		bool switchedToCustom = false;
		if (config.QuickDebugEnabled
			&& !string.Equals(config.QuickDebugPreset, "custom", System.StringComparison.OrdinalIgnoreCase))
		{
			config.QuickDebugPreset = "custom";
			switchedToCustom = true;
		}

		config.LoggingEnabled = loggingEnabledToggle.ButtonPressed;
		config.FileSinkEnabled = fileSinkToggle.ButtonPressed;
		config.RuntimePanelEnabled = panelVisibleToggle.ButtonPressed;
		config.LoggingMirrorNonErrorToGodot = mirrorToGodotToggle.ButtonPressed;
		if (logLevelOption != null)
			config.LoggingLevel = logLevelOption.GetItemText(logLevelOption.Selected);

		var categories = config.Categories;
		categories.General = logCategoryToggles[0].ButtonPressed;
		categories.Sprite = logCategoryToggles[1].ButtonPressed;
		categories.Audio = logCategoryToggles[2].ButtonPressed;
		categories.Input = logCategoryToggles[3].ButtonPressed;
		categories.Script = logCategoryToggles[4].ButtonPressed;
		categories.UI = logCategoryToggles[5].ButtonPressed;
		categories.FileSystem = logCategoryToggles[6].ButtonPressed;
		categories.Load = logCategoryToggles[7].ButtonPressed;
		categories.Save = logCategoryToggles[8].ButtonPressed;
		categories.Config = logCategoryToggles[9].ButtonPressed;

		if (!RuntimeDiagnosticsConfigWriter.SaveUserConfig(config, out string path, out string error))
		{
			SetDiagnosticsStatus(MultiLanguage.Get("FirstWindow.DebugSaveFailed", "保存失败：") + error);
			return;
		}

		string displayPath = GenericUtils.ResolveDiagnosticPathForDisplay(path);
		string reloadError = "";
		bool hotReloaded = GenericUtils.GetRuntimeDiagnosticsConfig() != null
			&& GenericUtils.ReloadRuntimeDiagnosticsConfig(out reloadError);
		string message;
		if (hotReloaded)
		{
			message = MultiLanguage.Get("FirstWindow.DebugSavedApplied", "设置已保存并热重载")
				+ "：" + displayPath;
		}
		else
		{
			message = string.IsNullOrEmpty(reloadError)
				? MultiLanguage.Get("FirstWindow.DebugSavedLater", "设置已保存，将在下次进入游戏时生效")
					+ "：" + displayPath
				: MultiLanguage.Get("FirstWindow.DebugSaveFailed", "保存失败：") + reloadError;
		}
		if (switchedToCustom)
			message += "；quick_debug 已自动切到 custom";
		SetDiagnosticsStatus(message);
	}

	void ExportDiagnosticLogFromLauncher()
	{
		if (GenericUtils.GetRuntimeDiagnosticsConfig() == null)
		{
			SetDiagnosticsStatus(MultiLanguage.Get("FirstWindow.DiagnosticsNotReady", "诊断系统尚未初始化，无法导出。"));
			return;
		}

		string path = GenericUtils.GetDefaultDiagnosticLogPath();
		if (GenericUtils.ExportDiagnosticLog(path, out string error))
			SetDiagnosticsStatus(MultiLanguage.Get("FirstWindow.DebugExportDone", "已导出诊断日志：")
				+ GenericUtils.ResolveDiagnosticPathForDisplay(path));
		else
			SetDiagnosticsStatus(MultiLanguage.Get("FirstWindow.DebugExportFailed", "导出失败：") + error);
	}

	void ExportDiagnosticPackageFromLauncher()
	{
		if (GenericUtils.GetRuntimeDiagnosticsConfig() == null)
		{
			SetDiagnosticsStatus(MultiLanguage.Get("FirstWindow.DiagnosticsNotReady", "诊断系统尚未初始化，无法导出。"));
			return;
		}

		if (GenericUtils.ExportDiagnosticPackage(null, out string error))
			SetDiagnosticsStatus(MultiLanguage.Get("FirstWindow.DebugExportPackageDone", "已导出诊断包：")
				+ GenericUtils.ResolveDiagnosticPathForDisplay("game://"));
		else
			SetDiagnosticsStatus(MultiLanguage.Get("FirstWindow.DebugExportFailed", "导出失败：") + error);
	}

	void SetDiagnosticsStatus(string text)
	{
		if (diagnosticsStatusLabel != null)
			diagnosticsStatusLabel.Text = text ?? "";
	}
}
