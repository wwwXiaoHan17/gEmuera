using Godot;
using System.Collections.Generic;

// OptionWindow 的「高级：emuera.config」调试区（partial）。
// 位置：System 组之后、Close 之前；默认折叠，第一次展开时才构建内容（约 70 个配置项的
// 行不会拖慢普通用户打开设置面板）。
// 语义：只做“查看 + 手动改 + 显式保存”，不改变任何配置读取逻辑；没有用户操作（开关切换 /
// 回车或失焦提交 / 重新加载按钮）时绝不写 emuera.config。全部在 Godot 主线程执行，
// ConfigData 检查器 API 同样只允许主线程调用。
public partial class OptionWindow
{
	const int EmueraConfigListHeight = 320;   // 列表固定高度，内部滚动
	const int EmueraConfigRowMinHeight = 44;  // 触屏最小可点高度
	const int EmueraConfigDescWidth = 200;    // 非布尔项描述列宽（超出换行）
	const int EmueraConfigRowSeparation = 4;  // 配置项行间距（紧凑）

	VBoxContainer emueraConfigSection;   // 外层：折叠标题 + 内容
	Button emueraConfigFoldButton;       // 折叠开关（ToggleMode）
	VBoxContainer emueraConfigBody;      // 展开内容（懒构建，折叠时隐藏）
	Label emueraConfigPathLabel;         // 配置路径 + 是否存在
	Label emueraConfigHintLabel;         // 说明行
	Button emueraConfigReloadButton;
	Button emueraConfigOpenDirButton;
	VBoxContainer emueraConfigList;      // 配置项行容器
	Label emueraConfigStatusLabel;       // 状态行（已保存 / 解析失败 / 只读）
	bool emueraConfigBodyBuilt;

	/// <summary>在 System 组之后构建折叠区骨架；内容首次展开时才创建。由 _Ready 调用一次。</summary>
	void BuildEmueraConfigSection(VBoxContainer content)
	{
		emueraConfigSection = new VBoxContainer();
		emueraConfigSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		emueraConfigSection.AddThemeConstantOverride("separation", 8);
		content.AddChild(emueraConfigSection);

		emueraConfigFoldButton = new Button();
		emueraConfigFoldButton.ToggleMode = true;
		emueraConfigFoldButton.ButtonPressed = false;
		emueraConfigFoldButton.CustomMinimumSize = new Vector2(0, EmueraConfigRowMinHeight);
		emueraConfigFoldButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		emueraConfigFoldButton.Alignment = HorizontalAlignment.Left;
		emueraConfigFoldButton.AddThemeFontSizeOverride("font_size", 15);
		GEmueraTheme.ApplyButton(emueraConfigFoldButton, GEmueraTheme.Surface, GEmueraTheme.Border);
		RefreshEmueraConfigFoldButtonText(false);
		emueraConfigFoldButton.Toggled += OnEmueraConfigFoldToggled;
		emueraConfigSection.AddChild(emueraConfigFoldButton);
	}

	void OnEmueraConfigFoldToggled(bool expanded)
	{
		RefreshEmueraConfigFoldButtonText(expanded);
		if (!expanded)
		{
			if (emueraConfigBody != null) emueraConfigBody.Visible = false;
			return;
		}
		EnsureEmueraConfigBodyBuilt();
		emueraConfigBody.Visible = true;
		// 每次展开按当前值重建：折叠期间配置可能被其它途径改过。
		RebuildEmueraConfigRows();
	}

	/// <summary>首次展开时构建内容：路径 / 两个按钮 / 说明 / 固定高度列表 / 状态行。</summary>
	void EnsureEmueraConfigBodyBuilt()
	{
		if (emueraConfigBodyBuilt || emueraConfigSection == null) return;
		emueraConfigBodyBuilt = true;

		emueraConfigBody = new VBoxContainer();
		emueraConfigBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		emueraConfigBody.AddThemeConstantOverride("separation", 8);
		emueraConfigSection.AddChild(emueraConfigBody);

		// 配置路径 + 是否存在（只读展示，不做任何创建）。
		emueraConfigPathLabel = new Label();
		emueraConfigPathLabel.AddThemeFontSizeOverride("font_size", 12);
		emueraConfigPathLabel.AddThemeColorOverride("font_color", GEmueraTheme.TextDim);
		emueraConfigPathLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		emueraConfigBody.AddChild(emueraConfigPathLabel);
		RefreshEmueraConfigPathLabel();

		var actionRow = new HBoxContainer();
		actionRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		actionRow.AddThemeConstantOverride("separation", 8);
		emueraConfigBody.AddChild(actionRow);

		emueraConfigReloadButton = CreateEmueraConfigActionButton(
			"OptionWindow.EmueraConfigReload", "重新加载配置");
		emueraConfigReloadButton.Pressed += OnEmueraConfigReloadPressed;
		actionRow.AddChild(emueraConfigReloadButton);

		emueraConfigOpenDirButton = CreateEmueraConfigActionButton(
			"OptionWindow.EmueraConfigOpenDir", "打开配置目录");
		emueraConfigOpenDirButton.Pressed += OnEmueraConfigOpenDirPressed;
		actionRow.AddChild(emueraConfigOpenDirButton);

		// 说明行：不承诺“立即全部生效”。
		emueraConfigHintLabel = new Label();
		emueraConfigHintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		emueraConfigHintLabel.AddThemeFontSizeOverride("font_size", 12);
		emueraConfigHintLabel.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
		emueraConfigHintLabel.Text = MultiLanguage.Get("OptionWindow.EmueraConfigHint",
			"修改会写入游戏目录 emuera.config；部分项需重新开始游戏才生效");
		emueraConfigBody.AddChild(emueraConfigHintLabel);

		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.CustomMinimumSize = new Vector2(0, EmueraConfigListHeight);
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		GEmueraTheme.ApplyWideScrollbar(scroll.GetVScrollBar());
		emueraConfigBody.AddChild(scroll);

		emueraConfigList = new VBoxContainer();
		emueraConfigList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		emueraConfigList.AddThemeConstantOverride("separation", EmueraConfigRowSeparation);
		scroll.AddChild(emueraConfigList);

		emueraConfigStatusLabel = new Label();
		emueraConfigStatusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		emueraConfigStatusLabel.AddThemeFontSizeOverride("font_size", 12);
		emueraConfigStatusLabel.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
		emueraConfigBody.AddChild(emueraConfigStatusLabel);
		SetEmueraConfigStatus(string.Empty, GEmueraTheme.TextSecondary);

		emueraConfigBody.Visible = false;
	}

	Button CreateEmueraConfigActionButton(string key, string fallback)
	{
		var button = new Button();
		button.Text = MultiLanguage.Get(key, fallback);
		button.CustomMinimumSize = new Vector2(0, EmueraConfigRowMinHeight);
		button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		button.AddThemeFontSizeOverride("font_size", 14);
		GEmueraTheme.ApplyButton(button, GEmueraTheme.Surface, GEmueraTheme.Border);
		return button;
	}

	/// <summary>按 configArray 顺序重建全部行：Bool → CheckButton，其余 → 文本输入。</summary>
	void RebuildEmueraConfigRows()
	{
		if (emueraConfigList == null) return;
		foreach (Node child in emueraConfigList.GetChildren())
		{
			emueraConfigList.RemoveChild(child);
			child.QueueFree();
		}

		MinorShift.Emuera.ConfigData config;
		IReadOnlyList<MinorShift.Emuera.AConfigItem> items;
		try
		{
			config = MinorShift.Emuera.ConfigData.Instance;
			items = config.GetInspectorItems();
		}
		catch (System.Exception)
		{
			SetEmueraConfigStatus(MultiLanguage.Get(
				"OptionWindow.EmueraConfigListFailed", "Failed to read config data."),
				GEmueraTheme.Danger);
			return;
		}

		foreach (MinorShift.Emuera.AConfigItem item in items)
		{
			if (config.IsBooleanItem(item))
				AddEmueraConfigBoolRow(item);
			else
				AddEmueraConfigTextRow(item);
		}

		SetEmueraConfigStatus(string.Format(MultiLanguage.Get(
			"OptionWindow.EmueraConfigItemCount", "{0} config items"), items.Count),
			GEmueraTheme.TextSecondary);
	}

	void AddEmueraConfigBoolRow(MinorShift.Emuera.AConfigItem item)
	{
		var row = new HBoxContainer();
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddThemeConstantOverride("separation", 8);
		emueraConfigList.AddChild(row);

		var toggle = new CheckButton();
		// 先写初值再连信号：程序化赋值不应触发一次“假保存”。
		toggle.SetPressedNoSignal(ReadEmueraConfigBool(item));
		toggle.Disabled = item.Fixed; // fixed.config 固定项只读
		toggle.Text = GetEmueraConfigDescription(item);
		toggle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		toggle.CustomMinimumSize = new Vector2(0, EmueraConfigRowMinHeight);
		toggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		toggle.AddThemeFontSizeOverride("font_size", 14);
		toggle.Toggled += enabled => OnEmueraConfigBoolToggled(item, toggle, enabled);
		row.AddChild(toggle);
	}

	void AddEmueraConfigTextRow(MinorShift.Emuera.AConfigItem item)
	{
		var row = new HBoxContainer();
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddThemeConstantOverride("separation", 8);
		emueraConfigList.AddChild(row);

		var description = new Label();
		description.Text = GetEmueraConfigDescription(item);
		description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		description.CustomMinimumSize = new Vector2(EmueraConfigDescWidth, 0);
		description.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		description.AddThemeFontSizeOverride("font_size", 12);
		description.AddThemeColorOverride(
			"font_color", item.Fixed ? GEmueraTheme.TextDim : GEmueraTheme.TextSecondary);
		row.AddChild(description);

		var edit = new LineEdit();
		edit.Text = ReadEmueraConfigText(item);
		edit.Editable = !item.Fixed; // fixed.config 固定项只读
		edit.CustomMinimumSize = new Vector2(0, EmueraConfigRowMinHeight);
		edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		edit.AddThemeFontSizeOverride("font_size", 14);
		edit.TextSubmitted += text => CommitEmueraConfigText(item, edit, text);
		edit.FocusExited += () => CommitEmueraConfigText(item, edit, edit.Text);
		row.AddChild(edit);
	}

	void OnEmueraConfigBoolToggled(MinorShift.Emuera.AConfigItem item, CheckButton toggle,
		bool enabled)
	{
		if (item == null || toggle == null || !GodotObject.IsInstanceValid(toggle)) return;
		if (item.Fixed)
		{
			toggle.SetPressedNoSignal(ReadEmueraConfigBool(item));
			SetEmueraConfigItemStatus("OptionWindow.EmueraConfigReadOnly",
				"Read-only (fixed in fixed.config): {0}", item, GEmueraTheme.TextSecondary);
			return;
		}

		bool parsed;
		try
		{
			parsed = MinorShift.Emuera.ConfigData.Instance.TrySetItemFromText(
				item, enabled ? "YES" : "NO");
		}
		catch (System.Exception) { parsed = false; }
		if (!parsed)
		{
			// 数据没变，回滚界面开关，避免界面与配置不一致。
			toggle.SetPressedNoSignal(ReadEmueraConfigBool(item));
			SetEmueraConfigItemStatus("OptionWindow.EmueraConfigParseFailed",
				"Invalid value: {0}", item, GEmueraTheme.Danger);
			return;
		}
		ApplyEmueraConfigAfterEdit(item);
	}

	void CommitEmueraConfigText(MinorShift.Emuera.AConfigItem item, LineEdit edit, string text)
	{
		if (item == null || edit == null || !GodotObject.IsInstanceValid(edit)) return;
		if (item.Fixed)
		{
			SetEmueraConfigItemStatus("OptionWindow.EmueraConfigReadOnly",
				"Read-only (fixed in fixed.config): {0}", item, GEmueraTheme.TextSecondary);
			return;
		}

		string proposed = text ?? string.Empty;
		// 未改动（展开/折叠、切换焦点时重复触发的提交）不写盘。
		if (string.Equals(proposed, ReadEmueraConfigText(item), System.StringComparison.Ordinal))
			return;

		bool parsed;
		try
		{
			parsed = MinorShift.Emuera.ConfigData.Instance.TrySetItemFromText(item, proposed);
		}
		catch (System.Exception) { parsed = false; }
		if (!parsed)
		{
			SetEmueraConfigItemStatus("OptionWindow.EmueraConfigParseFailed",
				"Invalid value: {0}", item, GEmueraTheme.Danger);
			return;
		}
		ApplyEmueraConfigAfterEdit(item);

		// 回显归一后的值（字符串去首尾空白、数字/枚举按规范写法），避免显示与内存不一致。
		string normalized = ReadEmueraConfigText(item);
		if (!string.Equals(edit.Text, normalized, System.StringComparison.Ordinal))
			edit.Text = normalized;
	}

	/// <summary>编辑成功后的统一收尾：写盘 + 刷新静态 Config + 状态行提示。</summary>
	void ApplyEmueraConfigAfterEdit(MinorShift.Emuera.AConfigItem item)
	{
		bool saved = SaveEmueraConfigFromUi();
		RefreshEmueraConfigPathLabel();
		if (saved)
			SetEmueraConfigItemStatus("OptionWindow.EmueraConfigSaved",
				"Saved: {0}", item, GEmueraTheme.Success);
		else
			SetEmueraConfigStatus(MultiLanguage.Get("OptionWindow.EmueraConfigSaveFailed",
				"Value applied in memory, but writing emuera.config failed."),
				GEmueraTheme.Danger);
	}

	// 用户显式操作后的落盘：SaveConfig 只写文件、不更新解释器使用的静态 Config，
	// 因此成功后补一次 Config.SetConfig（该刷新失败不回滚已写文件）。
	bool SaveEmueraConfigFromUi()
	{
		MinorShift.Emuera.ConfigData config = MinorShift.Emuera.ConfigData.Instance;
		bool saved;
		try { saved = config.Save(); }
		catch (System.Exception) { saved = false; }
		if (saved)
		{
			try { MinorShift.Emuera.Config.SetConfig(config); }
			catch (System.Exception) { /* 已写盘；静态表刷新失败不影响“已保存”提示 */ }
		}
		return saved;
	}

	void OnEmueraConfigReloadPressed()
	{
		bool reloaded;
		try { reloaded = MinorShift.Emuera.ConfigData.Instance.Reload(); }
		catch (System.Exception) { reloaded = false; }
		RefreshEmueraConfigPathLabel();
		RebuildEmueraConfigRows();
		if (reloaded)
			SetEmueraConfigStatus(MultiLanguage.Get("OptionWindow.EmueraConfigReloaded",
				"Reloaded. Some options take effect only after restarting the game."),
				GEmueraTheme.Success);
		else
			SetEmueraConfigStatus(MultiLanguage.Get(
				"OptionWindow.EmueraConfigReloadFailed", "Reload failed."), GEmueraTheme.Danger);
	}

	// 用系统文件管理器打开游戏目录（ExeDir）；失败/无文件管理器时静默。
	void OnEmueraConfigOpenDirPressed()
	{
		string gameDir = GetEmueraConfigGameDir();
		if (gameDir.Length == 0) return;
		try { OS.ShellOpen(gameDir); }
		catch (System.Exception) { /* 打开失败不打断设置面板：这是可选的辅助入口 */ }
	}

	// 语言切换时刷新本区 i18n 文案；折叠内容尚未构建（用户没展开过）时只刷新标题。
	// 由 OptionWindow.ApplyLanguageTexts 调用，幂等。
	void ApplyEmueraConfigLanguageTexts()
	{
		if (emueraConfigFoldButton != null)
			RefreshEmueraConfigFoldButtonText(emueraConfigFoldButton.ButtonPressed);
		if (emueraConfigReloadButton != null)
			emueraConfigReloadButton.Text = MultiLanguage.Get(
				"OptionWindow.EmueraConfigReload", "重新加载配置");
		if (emueraConfigOpenDirButton != null)
			emueraConfigOpenDirButton.Text = MultiLanguage.Get(
				"OptionWindow.EmueraConfigOpenDir", "打开配置目录");
		if (emueraConfigHintLabel != null)
			emueraConfigHintLabel.Text = MultiLanguage.Get("OptionWindow.EmueraConfigHint",
				"修改会写入游戏目录 emuera.config；部分项需重新开始游戏才生效");
	}

	void RefreshEmueraConfigFoldButtonText(bool expanded)
	{
		if (emueraConfigFoldButton == null) return;
		string title = MultiLanguage.Get("OptionWindow.EmueraConfigGroup", "高级：emuera.config");
		emueraConfigFoldButton.Text = (expanded ? "▼ " : "▶ ") + title;
	}

	void RefreshEmueraConfigPathLabel()
	{
		if (emueraConfigPathLabel == null) return;
		string path = GetEmueraConfigPath();
		bool exists;
		try { exists = !string.IsNullOrEmpty(path) && uEmuera.Utils.FileExists(path); }
		catch (System.Exception) { exists = false; }
		// 不存在时标红：通常意味着用户从未保存过配置（LoadConfig 首次运行时也会自动生成）。
		emueraConfigPathLabel.Text = path + (exists ? "  [exists]" : "  [missing]");
		emueraConfigPathLabel.AddThemeColorOverride(
			"font_color", exists ? GEmueraTheme.TextDim : GEmueraTheme.Danger);
	}

	void SetEmueraConfigStatus(string text, Color color)
	{
		if (emueraConfigStatusLabel == null) return;
		emueraConfigStatusLabel.Text = text ?? string.Empty;
		emueraConfigStatusLabel.AddThemeColorOverride("font_color", color);
	}

	// 状态行统一格式：{0} = 配置项原名（Text）。
	void SetEmueraConfigItemStatus(
		string key, string fallback, MinorShift.Emuera.AConfigItem item, Color color)
	{
		SetEmueraConfigStatus(
			string.Format(MultiLanguage.Get(key, fallback), item != null ? item.Text : string.Empty),
			color);
	}

	static string GetEmueraConfigDescription(MinorShift.Emuera.AConfigItem item)
	{
		if (item == null) return string.Empty;
		return item.Fixed ? item.Text + " [fixed]" : item.Text;
	}

	static string GetEmueraConfigPath() =>
		(MinorShift.Emuera.Program.ExeDir ?? string.Empty) + "emuera.config";

	static string GetEmueraConfigGameDir() =>
		(MinorShift.Emuera.Program.ExeDir ?? string.Empty).TrimEnd('/', '\\');

	static bool ReadEmueraConfigBool(MinorShift.Emuera.AConfigItem item)
	{
		try
		{
			return MinorShift.Emuera.ConfigData.Instance.TryGetBoolean(item, out bool value)
				&& value;
		}
		catch (System.Exception) { return false; }
	}

	static string ReadEmueraConfigText(MinorShift.Emuera.AConfigItem item)
	{
		try
		{
			return MinorShift.Emuera.ConfigData.Instance.DescribeItemValue(item) ?? string.Empty;
		}
		catch (System.Exception) { return string.Empty; }
	}
}
