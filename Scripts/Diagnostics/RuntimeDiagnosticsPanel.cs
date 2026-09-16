using System;
using System.Text;
using Godot;
// 基类从 PanelContainer(Control) 改为 Window(Viewport) 后，Control 嵌套枚举不再隐式可见。
using SizeFlags = Godot.Control.SizeFlags;
using MouseFilterEnum = Godot.Control.MouseFilterEnum;
using FocusModeEnum = Godot.Control.FocusModeEnum;
using LayoutPreset = Godot.Control.LayoutPreset;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 企业级说明：运行时诊断面板内容（纯 Control 构建）是配置编辑 UI，不承载日志路由和业务诊断逻辑。
    /// 面板只在 debug.runtime_panel.enabled=true 时实例化，避免普通 APK 游玩期增加节点和布局成本。
    /// 所有改动写入 user://config.toml，保存后可热重载轻量开关；结构性参数建议重启后生效。
    /// 2026-08：从画布内嵌 PanelContainer 重构为原生 Window（标题栏/可拖动/可缩放）。
    /// 2026-08 APK 修复：Android 上嵌入 Window（内部依赖 SubViewport 合成）在
    /// gl_compatibility 渲染器下内容不渲染（与本仓库其它 SubViewport 路径一致地仅桌面可用），
    /// 因此本类为共享内容（PanelContainer），同一份内容代码被两种宿主共用：
    /// 桌面端由 RuntimeDiagnosticsPanel（Window）包一层原生标题栏/拖动/缩放，
    /// Android 端由 FloatingDiagnosticsHost 直接作为全屏 Control 覆盖层挂载。
    /// </summary>
    public partial class RuntimeDiagnosticsPanelContent : PanelContainer
    {
        RuntimeDiagnosticsConfig config;
        VBoxContainer optionRoot;
        TabContainer optionTabs;
        MarginContainer panelMargin;
        Label titleLabel;
        Label statusLabel;
        Label statsLabel;
        Label consoleUserPathLabel;
        TextEdit consoleTextEdit;
        Label logViewStatusLabel;
        TextEdit logViewTextEdit;
        OptionButton logViewFilter;
        Control optionBody;
        bool collapsed;
        bool addingQuickOptions;
        bool quickOptionsDirty;
        bool expertOptionsDirty;

        public bool FloatingMode { get; set; }
        public event Action HideRequested;
        // 关闭事件：与桌面端原生 ✕（CloseRequested）同语义，宿主 QueueFree 整个悬浮窗。
        public event Action RequestCloseEvent;

        public override void _Ready()
        {
            // 标题栏/最小尺寸/原生 ✕ 由桌面端 RuntimeDiagnosticsPanel（Window）负责，
            // 本类只构建纯 Control 内容，两种宿主（Window / Android 覆盖层）共用。
            BuildPanel();
            RefreshResponsiveLayout();
        }

        public override void _Notification(int what)
        {
            if (what == Control.NotificationResized)
                RefreshResponsiveLayout();
        }

        void BuildPanel()
        {
            // 本类即根面板（PanelContainer）：桌面端由 Window 外壳包一层原生标题栏，
            // Android 端直接 FullRect 填满视口；两种宿主共用同一份内容构建。
            AddThemeStyleboxOverride("panel", CreatePanelStyle());
            SetAnchorsPreset(LayoutPreset.FullRect);
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
            MouseFilter = MouseFilterEnum.Stop;

            panelMargin = new MarginContainer();
            panelMargin.MouseFilter = MouseFilterEnum.Pass;
            ApplyMargin(panelMargin, 12, 12);
            AddChild(panelMargin);

            var root = new VBoxContainer();
            root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            root.SizeFlagsVertical = SizeFlags.ExpandFill;
            root.AddThemeConstantOverride("separation", 10);
            root.MouseFilter = MouseFilterEnum.Pass;
            panelMargin.AddChild(root);

            root.AddChild(CreateHeader());

            statusLabel = CreateSmallLabel("");
            root.AddChild(statusLabel);

            statsLabel = CreateSmallLabel("");
            root.AddChild(statsLabel);

            optionBody = new VBoxContainer();
            optionBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            optionBody.SizeFlagsVertical = SizeFlags.ExpandFill;
            root.AddChild(optionBody);

            // 企业级说明：调试配置项数量很多，手机横屏下一条长列表难以扫描。
            // TabContainer 只分组 UI 节点，不改变配置模型和日志路由；每个页签独立滚动，避免切换模块时丢失主面板操作区。
            optionTabs = new TabContainer();
            optionTabs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            optionTabs.SizeFlagsVertical = SizeFlags.ExpandFill;
            optionTabs.CustomMinimumSize = FloatingMode ? new Vector2(0, 160) : new Vector2(0, 180);
            optionTabs.MouseFilter = MouseFilterEnum.Stop;
            optionBody.AddChild(optionTabs);

            LoadConfigIntoPanel();
        }

        Control CreateHeader()
        {
            var header = new VBoxContainer();
            header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            header.AddThemeConstantOverride("separation", 8);

            titleLabel = new Label();
            titleLabel.Text = FloatingMode ? "日志诊断悬浮窗" : "日志系统调试";
            titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            titleLabel.MouseFilter = MouseFilterEnum.Ignore;
            titleLabel.AddThemeFontSizeOverride("font_size", 20);
            titleLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.98f, 1.0f));
            header.AddChild(titleLabel);

            var actions = new HFlowContainer();
            actions.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            actions.AddThemeConstantOverride("h_separation", 8);
            actions.AddThemeConstantOverride("v_separation", 8);
            header.AddChild(actions);

            var reload = CreateActionButton("重读", "重新读取 user://config.toml 或 res://config.toml。");
            reload.Pressed += LoadConfigIntoPanel;
            actions.AddChild(reload);

            var save = CreateActionButton("保存", "写入 user://config.toml，并在主运行场景中热重载轻量开关。");
            save.Pressed += SaveConfigFromPanel;
            actions.AddChild(save);

            var export = CreateActionButton("导出", "导出当前 ring buffer 诊断日志。进入主运行场景前可能没有运行时日志。");
            export.Pressed += ExportDiagnosticLog;
            actions.AddChild(export);

            var exportPackage = CreateActionButton("导出包", "导出完整诊断包，包含日志、配置快照、设备、屏幕、Godot、APK、游戏和摘要信息。");
            exportPackage.Pressed += ExportDiagnosticPackage;
            actions.AddChild(exportPackage);

            if (FloatingMode)
            {
                var collapse = CreateActionButton("隐藏", "隐藏悬浮弹窗，保留悬浮球入口。");
                collapse.Pressed += ToggleCollapsed;
                actions.AddChild(collapse);

                var close = CreateActionButton("关闭", "关闭本次悬浮窗，不修改配置文件。");
                close.Pressed += () => { Hide(); RequestCloseEvent?.Invoke(); };
                actions.AddChild(close);
            }

            return header;
        }

        Button CreateActionButton(string text, string tooltip)
        {
            var button = new Button();
            button.Text = text;
            button.TooltipText = tooltip;
            button.CustomMinimumSize = new Vector2(88, 44);
            button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            button.AddThemeFontSizeOverride("font_size", 15);
            GEmueraTheme.ApplyButton(button, GEmueraTheme.Surface, GEmueraTheme.Border);
            return button;
        }

        void RefreshResponsiveLayout()
        {
            // 企业级说明：诊断面板面向 APK 实机排障，横屏手机、折叠屏和桌面窗口尺寸差异较大。
            // 这里仅调整诊断面板自身的边距、触摸控件高度和滚动区最小高度，不触碰业务 UI 布局。
            Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? Size;
            bool compact = viewportSize.X < 900 || viewportSize.Y < 620;
            int outerMargin = compact ? 8 : 12;
            int rootGap = compact ? 8 : 10;
            int listGap = compact ? 8 : 12;

            if (panelMargin != null)
                ApplyMargin(panelMargin, outerMargin, outerMargin);
            ApplyTabListSpacing(listGap);
            if (titleLabel != null)
                titleLabel.AddThemeFontSizeOverride("font_size", compact ? 18 : 20);
            if (optionTabs != null)
            {
                // 企业级说明：这里使用较小的最小高度，让外层 Container 根据可用屏幕分配剩余空间。
                // 不能把 ScrollContainer/TabContainer 写成固定大高度，否则手机横屏下会把启动器页面撑出屏幕。
                int minHeight = FloatingMode
                    ? (compact ? 120 : 160)
                    : (compact ? 140 : 180);
                optionTabs.CustomMinimumSize = new Vector2(0, minHeight);
                ApplyTabScrollMinimumHeight(minHeight);
            }

            if (FloatingMode && viewportSize.X > 0)
            {
                float maxWidth = Mathf.Max(360.0f, viewportSize.X - outerMargin * 2.0f);
                // 桌面端 Window 的最小尺寸由宿主 Window 负责；这里只约束 Control 自身的最小宽，
                // 保证 Android 覆盖层/窗口内容在窄屏下仍可读。FullRect 锚点下该值基本惰性。
                CustomMinimumSize = new Vector2(Mathf.Min(520.0f, maxWidth), 220);
            }

            var root = panelMargin?.GetChildCount() > 0 ? panelMargin.GetChild(0) as VBoxContainer : null;
            root?.AddThemeConstantOverride("separation", rootGap);
        }

        void LoadConfigIntoPanel()
        {
            var loadResult = RuntimeDiagnosticsConfigLoader.Load();
            config = loadResult.Config ?? RuntimeDiagnosticsConfig.CreateDefault();
            RebuildOptions();
            SetStatus(loadResult.FileFound
                ? "已读取配置：" + BuildPathDisplay(loadResult.LoadedFrom)
                : "未找到配置文件，当前显示默认值。保存后会生成 " + BuildPathDisplay(RuntimeDiagnosticsConfigWriter.UserConfigPath));
            if (!string.IsNullOrEmpty(loadResult.ErrorMessage))
                SetStatus(loadResult.ErrorMessage);
            quickOptionsDirty = false;
            expertOptionsDirty = false;
            RefreshStats();
        }


        void SaveConfigFromPanel()
        {
            if (config == null)
                return;

            // 企业级说明：快捷页和专家页的保存语义不同。
            // 只改快捷页时必须保留 quick_debug.preset；改专家细项时如果仍停留在非 custom preset，
            // 下一次加载会被快捷层重新覆盖，导致用户误判为保存失败，因此仅在专家页发生改动时自动切到 custom。
            bool switchedToCustom = false;
            if (expertOptionsDirty
                && config.QuickDebugEnabled
                && !string.Equals(config.QuickDebugPreset, "custom", StringComparison.OrdinalIgnoreCase))
            {
                config.QuickDebugPreset = "custom";
                switchedToCustom = true;
            }

            if (!RuntimeDiagnosticsConfigWriter.SaveUserConfig(config, out string path, out string error))
            {
                SetStatus("保存失败：" + error);
                return;
            }

            string reloadError = "";
            bool hotReloaded = false;
            if (global::GenericUtils.GetRuntimeDiagnosticsConfig() != null)
                hotReloaded = global::GenericUtils.ReloadRuntimeDiagnosticsConfig(out reloadError);

            SetStatus(hotReloaded
                ? "已保存并热重载：" + BuildPathDisplay(path) + (switchedToCustom ? "；专家项已自动切到 quick_debug.preset=custom" : "")
                : string.IsNullOrEmpty(reloadError)
                    ? "已保存：" + BuildPathDisplay(path) + "。进入游戏或重启后生效。" + (switchedToCustom ? " 专家项已自动切到 quick_debug.preset=custom。" : "")
                    : "已保存，但热重载失败：" + reloadError);
            quickOptionsDirty = false;
            expertOptionsDirty = false;
            RefreshUserDirectoryLabel();
            RefreshConsoleLog();
            RefreshStats();
        }

        void ExportDiagnosticLog()
        {
            if (global::GenericUtils.GetRuntimeDiagnosticsConfig() == null)
            {
                SetStatus("当前还未进入主运行场景，没有可导出的运行时 ring buffer。");
                return;
            }

            string path = global::GenericUtils.GetDefaultDiagnosticLogPath();
            if (global::GenericUtils.ExportDiagnosticLog(path, out string error))
                SetStatus("已导出诊断日志：" + BuildPathDisplay(path));
            else
                SetStatus("导出失败：" + error);
            RefreshUserDirectoryLabel();
            RefreshConsoleLog();
            RefreshStats();
        }

        void ExportDiagnosticPackage()
        {
            if (global::GenericUtils.GetRuntimeDiagnosticsConfig() == null)
            {
                SetStatus("当前还未进入主运行场景，无法生成完整运行时诊断包。");
                return;
            }

            if (global::GenericUtils.ExportDiagnosticPackage(null, out string error))
                SetStatus("已导出诊断包：" + BuildPathDisplay(DiagnosticLogExporter.GameDirectoryPathPrefix));
            else
                SetStatus("导出诊断包失败：" + error);
            RefreshUserDirectoryLabel();
            RefreshConsoleLog();
            RefreshStats();
        }

        void ToggleCollapsed()
        {
            if (FloatingMode)
            {
                HideRequested?.Invoke();
                return;
            }
            collapsed = !collapsed;
            optionBody.Visible = !collapsed;
        }

        void RefreshStats()
        {
            if (statsLabel == null)
                return;
            statsLabel.Text = "ring buffer: "
                + DiagnosticLogSinks.RingCount + "/"
                + DiagnosticLogSinks.RingCapacity
                + " overwritten=" + DiagnosticLogSinks.OverwrittenTotal
                + " dropped=" + DiagnosticLogRouter.GetDroppedTotal()
                + " | 游戏目录=" + DiagnosticLogExporter.GetCurrentGameDirectoryForDisplay();
        }

        void SetStatus(string text)
        {
            if (statusLabel != null)
                statusLabel.Text = text ?? "";
        }

        void RefreshUserDirectoryLabel()
        {
            if (consoleUserPathLabel == null)
                return;

            consoleUserPathLabel.Text =
                "启动游戏目录：" + DiagnosticLogExporter.GetCurrentGameDirectoryForDisplay()
                + "\n日志文件：" + BuildPathDisplay(global::GenericUtils.GetDefaultDiagnosticLogPath())
                + "\n诊断包目录：" + BuildPathDisplay(DiagnosticLogExporter.GameDirectoryPathPrefix);
        }

        void RefreshConsoleLog()
        {
            if (consoleTextEdit == null)
                return;

            var records = DiagnosticLogSinks.Snapshot();
            if (records.Length == 0)
            {
                consoleTextEdit.Text = "暂无运行时日志。进入游戏主场景后，日志会先写入内存 ring buffer；点击“刷新”可重新读取。";
                RefreshStats();
                return;
            }

            const int MaxConsoleRecords = 200;
            int start = Math.Max(0, records.Length - MaxConsoleRecords);
            var sb = new StringBuilder(Math.Min(records.Length - start, MaxConsoleRecords) * 160);
            sb.AppendLine("显示最近 " + (records.Length - start) + " / " + records.Length + " 条 ring buffer 日志；完整内容请点“导出日志”。");
            for (int i = start; i < records.Length; i++)
            {
                var r = records[i];
                sb.Append(r.Seq.ToString("D6"))
                    .Append(" ")
                    .Append(r.Level.ToString().ToUpperInvariant())
                    .Append(" ")
                    .Append(r.Category)
                    .Append(" ")
                    .Append(r.EventId)
                    .Append(" | ")
                    .Append(r.Message);
                if (!string.IsNullOrEmpty(r.Data))
                    sb.Append(" | ").Append(r.Data);
                sb.AppendLine();
            }
            consoleTextEdit.Text = sb.ToString();
            RefreshStats();
        }

        void RefreshLogView()
        {
            if (logViewTextEdit == null)
                return;

            var records = DiagnosticLogSinks.Snapshot();
            if (records.Length == 0)
            {
                logViewTextEdit.Text = "暂无运行时日志。进入游戏主场景后，日志会先写入内存 ring buffer；点击“刷新”可重新读取。";
                RefreshStats();
                return;
            }

            EmueraLogLevel minLevel = SelectedLogViewLevel();
            string levelText = minLevel == EmueraLogLevel.None
                ? "全部"
                : minLevel.ToString().ToLowerInvariant();
            const int MaxLogViewRecords = 200;

            int matched = 0;
            foreach (var r in records)
            {
                if (minLevel == EmueraLogLevel.None || r.Level >= minLevel)
                    matched++;
            }
            int skipped = Math.Max(0, matched - MaxLogViewRecords);

            var sb = new StringBuilder(Math.Min(matched, MaxLogViewRecords) * 160);
            sb.AppendLine("显示最近 " + Math.Min(matched, MaxLogViewRecords) + " / " + matched
                + " 条 ring buffer 日志（level >= " + levelText + "）；完整内容请点“导出日志”。");
            int written = 0;
            foreach (var r in records)
            {
                if (minLevel != EmueraLogLevel.None && r.Level < minLevel)
                    continue;
                if (written < skipped)
                {
                    written++;
                    continue;
                }
                if (written - skipped >= MaxLogViewRecords)
                    break;
                sb.Append(r.Seq.ToString("D6"))
                    .Append(" ")
                    .Append(r.Level.ToString().ToUpperInvariant())
                    .Append(" ")
                    .Append(r.Category)
                    .Append(" ")
                    .Append(r.EventId)
                    .Append(" | ")
                    .Append(r.Message);
                if (!string.IsNullOrEmpty(r.Data))
                    sb.Append(" | ").Append(r.Data);
                sb.AppendLine();
                written++;
            }
            logViewTextEdit.Text = sb.ToString();
            RefreshLogViewStatus();
            RefreshStats();
        }

        EmueraLogLevel SelectedLogViewLevel()
        {
            if (logViewFilter == null)
                return EmueraLogLevel.None;
            switch (logViewFilter.Selected)
            {
                case 1: return EmueraLogLevel.Error;
                case 2: return EmueraLogLevel.Warn;
                case 3: return EmueraLogLevel.Info;
                case 4: return EmueraLogLevel.Debug;
                default: return EmueraLogLevel.None;
            }
        }

        void RefreshLogViewStatus()
        {
            if (logViewStatusLabel == null)
                return;
            if (DiagnosticLogSinks.IsFileSinkEnabled)
                logViewStatusLabel.Text = "持续文件日志：开启 → " + DiagnosticLogSinks.FileSinkCurrentPath
                    + "（单文件 1 MiB 轮转，等级 " + config?.FileSinkLevel + "）";
            else
                logViewStatusLabel.Text = "持续文件日志：关闭（[logging] file_sink=false 或 logging.enabled=false，文件不会生成）。";
        }

        static string BuildPathDisplay(string godotPath)
        {
            if (string.IsNullOrWhiteSpace(godotPath))
                return "";
            string absolute = ToAbsolutePath(godotPath);
            if (string.IsNullOrWhiteSpace(absolute) || string.Equals(absolute, godotPath, StringComparison.OrdinalIgnoreCase))
                return godotPath;
            return godotPath + "（实际：" + absolute + "）";
        }

        static string ToAbsolutePath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return "";
                if (path.StartsWith(DiagnosticLogExporter.GameDirectoryPathPrefix, StringComparison.OrdinalIgnoreCase))
                    return DiagnosticLogExporter.ResolvePathForDisplay(path);
                if (path.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
                    return ProjectSettings.GlobalizePath(path);
                return path;
            }
            catch
            {
                return path ?? "";
            }
        }

        static void ClearChildren(Node node)
        {
            if (node == null)
                return;
            foreach (Node child in node.GetChildren())
            {
                node.RemoveChild(child);
                child.QueueFree();
            }
        }

        static void ApplyMargin(MarginContainer margin, int horizontal, int vertical)
        {
            if (margin == null)
                return;
            margin.AddThemeConstantOverride("margin_left", horizontal);
            margin.AddThemeConstantOverride("margin_top", vertical);
            margin.AddThemeConstantOverride("margin_right", horizontal);
            margin.AddThemeConstantOverride("margin_bottom", vertical);
        }

        StyleBoxFlat CreatePanelStyle()
        {
            // 深色现代卡片：表面 token + 边框 + 8px 圆角。
            return GEmueraTheme.SurfaceStyle(
                GEmueraTheme.WithAlpha(GEmueraTheme.Surface, 0.96f),
                GEmueraTheme.Border,
                GEmueraTheme.CardRadius,
                1,
                8,
                new Vector2(0, 4),
                8, 8, 8, 8);
        }

        StyleBoxFlat CreateRowStyle()
        {
            return GEmueraTheme.SurfaceStyle(
                GEmueraTheme.Surface,
                GEmueraTheme.Border,
                GEmueraTheme.SmallRadius);
        }
    }

    /// <summary>
    /// 运行时诊断悬浮窗口（Godot 原生 Window）。桌面端以独立 OS 窗口显示（标题栏/可拖动/可缩放），
    /// 内容由共享的 RuntimeDiagnosticsPanelContent 提供。Android 不使用本类的 Window 形态——
    /// FloatingDiagnosticsHost 直接挂全屏 Control 覆盖层（嵌入 Window 在 gl_compatibility 的
    /// Android 上内容不渲染，见 RuntimeDiagnosticsPanelContent 类注释）。
    ///
    /// Why（错误 1）：gEmuera 日志诊断面板保持为 Window 类，不退回画布内嵌面板——
    /// Window 在桌面端是独立 OS 窗口，不遮挡游戏主画面，且带原生标题栏/关闭。
    /// </summary>
    public sealed partial class RuntimeDiagnosticsPanel : Window
    {
        const int MinWindowWidth = 360;
        const int MinWindowHeight = 260;

        // 悬浮宿主引用：启动门控 + 热重载补挂载共用（见 AttachFloatingTo）。
        static FloatingDiagnosticsHost _activeFloatingHost;
        static Node _floatingAttachParent;

        RuntimeDiagnosticsPanelContent _content;

        public event Action HideRequested;
        public event Action RequestCloseEvent;

        /// <summary>
        /// WS2：启动挂载门仍为 RuntimePanelEnabled（debug.runtime_panel.enabled / [logging] panel_visible 等价复用，行为保留）。
        /// 热重载通过 SetDiagnosticsPanelVisible 即时显隐悬浮球与面板。
        /// </summary>
        public static void AttachFloatingTo(Node parent)
        {
            // 宿主引用必须在门控之前无条件记录：启动时 panel_visible=false 不挂载，
            // 之后热重载/设置页改回 true 时 SetDiagnosticsPanelVisible 依赖该引用补挂载。
            if (parent != null)
                _floatingAttachParent = parent;
            var currentConfig = global::GenericUtils.GetRuntimeDiagnosticsConfig()
                ?? RuntimeDiagnosticsConfigLoader.Load().Config;
            if (parent == null || currentConfig == null || !currentConfig.RuntimePanelEnabled)
                return;
            if (_activeFloatingHost != null && GodotObject.IsInstanceValid(_activeFloatingHost))
                return;

            // Why（错误 3 修复）：悬浮球/面板层必须高于游戏系统菜单(100)与 tooltip(150)——
            // 原先与菜单同层(100)，菜单或 tooltip 展开时可能盖住悬浮球，造成“悬浮小球不显示”。
            var layer = new CanvasLayer { Layer = 160 };
            parent.AddChild(layer);

            var host = new FloatingDiagnosticsHost();
            host.SetAnchorsPreset(LayoutPreset.FullRect);
            host.MouseFilter = MouseFilterEnum.Ignore;
            layer.AddChild(host);
            _activeFloatingHost = host;
            host.TreeExiting += () =>
            {
                if (_activeFloatingHost == host)
                    _activeFloatingHost = null;
                layer.QueueFree();
            };
        }

        /// <summary>
        /// 运行时显示/隐藏悬浮球与面板。请求显示但尚未挂载时（例如热重载把 panel_visible 从 false 改为 true），
        /// 会按当前配置补挂载；门控不通过时为空操作，可安全调用。
        /// </summary>
        public static void SetDiagnosticsPanelVisible(bool visible)
        {
            var host = _activeFloatingHost;
            if ((host == null || !GodotObject.IsInstanceValid(host)) && visible)
            {
                var cfg = global::GenericUtils.GetRuntimeDiagnosticsConfig();
                if (_floatingAttachParent != null && GodotObject.IsInstanceValid(_floatingAttachParent)
                    && cfg != null && cfg.RuntimePanelEnabled)
                {
                    AttachFloatingTo(_floatingAttachParent);
                }
                host = _activeFloatingHost;
            }
            if (host == null || !GodotObject.IsInstanceValid(host))
                return;
            host.SetBallAndPanelVisible(visible);
        }

        public static bool GetDiagnosticsPanelVisible()
        {
            var host = _activeFloatingHost;
            if (host == null || !GodotObject.IsInstanceValid(host))
                return false;
            return host.IsBallVisible();
        }

        public override void _Ready()
        {
            Title = "日志诊断";
            MinSize = new Vector2I(MinWindowWidth, MinWindowHeight);
            // 标题栏 ✕ → 与面板内 ✕ 同一关闭路径（宿主 QueueFree 整个悬浮窗）。
            CloseRequested += OnNativeWindowCloseRequested;

            _content = new RuntimeDiagnosticsPanelContent { FloatingMode = true };
            _content.SetAnchorsPreset(LayoutPreset.FullRect);
            _content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _content.SizeFlagsVertical = SizeFlags.ExpandFill;
            // 内容内「隐藏」→ 仅隐藏窗口（保留悬浮球）；「关闭」→ 隐藏并通知宿主销毁。
            _content.HideRequested += () => HideRequested?.Invoke();
            _content.RequestCloseEvent += () =>
            {
                Hide();
                RequestCloseEvent?.Invoke();
            };
            AddChild(_content);
        }

        void OnNativeWindowCloseRequested()
        {
            // 先隐藏（Godot 标准），即使后续 QueueFree 延迟/失败，视觉先关闭。
            Hide();
            RequestCloseEvent?.Invoke();
        }

        /// <summary>
        /// 嵌入子窗口首次 Show 合成缺失唤醒（转发共享实现；内容为本窗 _content）。
        /// gl_compatibility 桌面端子 Window 同样被强制嵌入，2026-09-13 实证。
        /// </summary>
        internal void KickEmbeddedComposite()
        {
            global::gEmuera.GodotHost.EmueraDebugDialogPanel.KickEmbeddedComposite(this, _content);
        }
    }

    /// <summary>
    /// 企业级说明：悬浮诊断入口只负责调试 UI 的显示、隐藏和窗口尺寸调整。
    /// 它不读取业务状态、不写配置、不接入输入模拟器链路；全屏根节点保持 Ignore，避免悬浮入口以外区域拦截游戏触摸。
    /// 2026-08 APK 修复：Android 上用全屏 Control 覆盖层承载面板内容（嵌入 Window 在
    /// gl_compatibility 下不渲染），桌面端用原生 Window 外壳；悬浮球两种平台都可用。
    /// </summary>
    public sealed partial class FloatingDiagnosticsHost : Control
    {
        const float BallSize = 64.0f;
        const float ViewportMargin = 12.0f;
        const float MinPanelWidth = 360.0f;
        const float MinPanelHeight = 260.0f;

        /// <summary>
        /// Android 上嵌入 Window（内部依赖 SubViewport 合成）在 gl_compatibility 下内容不渲染，
        /// 改用全屏 Control 覆盖层；桌面端保留原生 Window（标题栏/拖动/缩放）。
        /// </summary>
        internal static bool IsOverlayPanel => OS.GetName() == "Android";

        RuntimeDiagnosticsPanelContent panelOverlay;
        RuntimeDiagnosticsPanel panelWindow;
        Button ballButton;
        bool panelVisible;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            BuildFloatingUi();
            CallDeferred(MethodName.PlaceInitialFloatingControls);
        }

        public override void _Notification(int what)
        {
            if (what != NotificationResized)
                return;

            ClampBallToViewport();
            if (panelVisible && panelWindow != null)
                ClampPanelToViewport();
        }

        void BuildFloatingUi()
        {
            ballButton = new Button();
            ballButton.Text = "调";
            ballButton.TooltipText = "打开/隐藏日志诊断悬浮窗";
            ballButton.FocusMode = FocusModeEnum.None;
            ballButton.CustomMinimumSize = new Vector2(BallSize, BallSize);
            ballButton.Size = new Vector2(BallSize, BallSize);
            ballButton.MouseFilter = MouseFilterEnum.Stop;
            ballButton.AddThemeFontSizeOverride("font_size", 22);
            ApplyFloatingBallStyle(ballButton);
            ballButton.Pressed += TogglePanel;
            AddChild(ballButton);

            if (IsOverlayPanel)
            {
                // Android：嵌入 Window 在 gl_compatibility 下不渲染，直接挂全屏 Control 覆盖层。
                // 覆盖层 FullRect 填满视口，无需窗口几何/钳制；显隐走 Control.Visible。
                panelOverlay = new RuntimeDiagnosticsPanelContent
                {
                    FloatingMode = true,
                    Visible = false,
                };
                panelOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
                panelOverlay.HideRequested += () => SetPanelVisible(false);
                panelOverlay.RequestCloseEvent += () => { SetPanelVisible(false); QueueFree(); };
                AddChild(panelOverlay);
            }
            else
            {
                // 桌面端：原生 Window 外壳（标题栏/可拖动/可缩放），内容为纯 Control。
                panelWindow = new RuntimeDiagnosticsPanel
                {
                    Visible = false,
                };
                panelWindow.HideRequested += () => SetPanelVisible(false);
                panelWindow.RequestCloseEvent += () => { panelWindow.Hide(); QueueFree(); };
                AddChild(panelWindow);
            }
        }

        void PlaceInitialFloatingControls()
        {
            // 悬浮球是主窗口 viewport 里的 Control，用逻辑坐标（GetViewportSize）。
            Vector2 viewportSize = GetViewportSize();
            ballButton.Size = new Vector2(BallSize, BallSize);
            ballButton.Position = new Vector2(
                Mathf.Max(ViewportMargin, viewportSize.X - BallSize - ViewportMargin),
                Mathf.Max(ViewportMargin, viewportSize.Y * 0.5f - BallSize * 0.5f));

            // 桌面端 Window 的 Position/Size 用主窗口物理像素（GetParentWindowPixelSize），
            // 不能用逻辑 viewport 尺寸，否则高分屏/拉伸下位置偏差、标题栏贴边。
            // Android 覆盖层 FullRect 自动填满视口，无需几何设置。
            if (panelWindow != null)
            {
                Vector2I parentSize = GetParentWindowPixelSize();
                Vector2I initialSize = new Vector2I(
                    (int)Mathf.Min(760.0f, Mathf.Max(MinPanelWidth, parentSize.X - ViewportMargin * 2.0f)),
                    (int)Mathf.Min(640.0f, Mathf.Max(MinPanelHeight, parentSize.Y - ViewportMargin * 2.0f)));
                panelWindow.Size = initialSize;
                panelWindow.Position = new Vector2I(
                    (int)Mathf.Max(ViewportMargin, parentSize.X - initialSize.X - ViewportMargin),
                    (int)Mathf.Max(ViewportMargin, 0));
            }
            SetPanelVisible(false);
        }

        void TogglePanel()
        {
            SetPanelVisible(!panelVisible);
        }
        void SetPanelVisible(bool visible)
        {
            panelVisible = visible;
            if (panelWindow != null)
            {
                // 桌面嵌入 Window 用标准 Show/Hide 显隐（比直接设 Visible 更可靠）。
                if (visible)
                {
                    panelWindow.Show();
                    ClampPanelToViewport();
                    // 嵌入子窗口首次 Show 合成缺失唤醒（同 EmueraDebugDialogPanel，
                    // gl_compatibility 桌面亦强制嵌入，2026-09-13 实证）。
                    panelWindow.KickEmbeddedComposite();
                }
                else
                {
                    panelWindow.Hide();
                }
            }
            else if (panelOverlay != null)
            {
                // Android 覆盖层：Control.Visible 即可（FullRect，无窗口几何）。
                panelOverlay.Visible = visible;
            }
            ballButton.Text = visible ? "×" : "调";
        }

        /// <summary>
        /// WS2：运行时显隐 API（GenericUtils.SetDiagnosticsPanelVisible → 静态转发）。
        /// true 时显示悬浮球并保持面板当前开合状态；false 时同时隐藏悬浮球与面板。
        /// </summary>
        public void SetBallAndPanelVisible(bool visible)
        {
            if (ballButton == null)
                return;
            ballButton.Visible = visible;
            if (!visible)
                SetPanelVisible(false);
        }

        public bool IsBallVisible() => ballButton?.Visible ?? false;

        void ClampPanelToViewport()
        {
            // 仅桌面 Window 需要钳制：面板钳制在父窗口内容区内，留 margin，标题栏始终可抓取。
            // Android 覆盖层为 FullRect，始终填满视口，无需钳制。
            if (panelWindow == null)
                return;

            Vector2I parentSize = GetParentWindowPixelSize();
            const int margin = 8;
            int maxX = Math.Max(0, parentSize.X - margin * 2);
            int maxY = Math.Max(0, parentSize.Y - margin * 2);
            Vector2I size = panelWindow.Size;
            size.X = Mathf.Clamp(size.X, (int)Mathf.Min(MinPanelWidth, maxX), maxX);
            size.Y = Mathf.Clamp(size.Y, (int)Mathf.Min(MinPanelHeight, maxY), maxY);

            Vector2I pos = panelWindow.Position;
            pos.X = Mathf.Clamp(pos.X, margin, Math.Max(margin, parentSize.X - size.X - margin));
            pos.Y = Mathf.Clamp(pos.Y, margin, Math.Max(margin, parentSize.Y - size.Y - margin));
            panelWindow.Position = pos;
            panelWindow.Size = size;
        }

        void ClampBallToViewport()
        {
            if (ballButton == null)
                return;

            Vector2 viewportSize = GetViewportSize();
            Vector2 max = new Vector2(
                Mathf.Max(ViewportMargin, viewportSize.X - ballButton.Size.X - ViewportMargin),
                Mathf.Max(ViewportMargin, viewportSize.Y - ballButton.Size.Y - ViewportMargin));
            ballButton.Position = new Vector2(
                Mathf.Clamp(ballButton.Position.X, ViewportMargin, max.X),
                Mathf.Clamp(ballButton.Position.Y, ViewportMargin, max.Y));
        }

        Vector2I GetParentWindowPixelSize()
        {
            // 嵌入 Window 的 Position/Size 用主窗口物理像素（GetTree().Root.Size）。
            var root = GetTree()?.Root;
            if (root != null && GodotObject.IsInstanceValid(root))
                return root.Size;
            return new Vector2I(
                (int)ProjectSettings.GetSetting("display/window/size/viewport_width", 1280),
                (int)ProjectSettings.GetSetting("display/window/size/viewport_height", 720));
        }

        Vector2 GetViewportSize()
        {
            Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? Size;
            if (viewportSize.X <= 0.0f || viewportSize.Y <= 0.0f)
                viewportSize = Size;
            return viewportSize;
        }

        static void ApplyFloatingBallStyle(Button button)
        {
            // 悬浮诊断球：强调蓝紫填充 + 高亮边框（深色主题），hover 提亮。
            var normal = CreateBallStyle(GEmueraTheme.WithAlpha(GEmueraTheme.Accent, 0.94f));
            var hover = CreateBallStyle(GEmueraTheme.WithAlpha(GEmueraTheme.Lighten(GEmueraTheme.Accent), 0.98f));
            var pressed = CreateBallStyle(GEmueraTheme.WithAlpha(GEmueraTheme.Darken(GEmueraTheme.Accent, 0.14f), 1.0f));
            button.AddThemeStyleboxOverride("normal", normal);
            button.AddThemeStyleboxOverride("hover", hover);
            button.AddThemeStyleboxOverride("pressed", pressed);
            button.AddThemeColorOverride("font_color", GEmueraTheme.TextPrimary);
            button.AddThemeColorOverride("font_hover_color", GEmueraTheme.TextPrimary);
            button.AddThemeColorOverride("font_pressed_color", GEmueraTheme.TextPrimary);
        }

        static StyleBoxFlat CreateBallStyle(Color color)
        {
            var style = new StyleBoxFlat();
            style.BgColor = color;
            style.BorderColor = GEmueraTheme.AccentAlt;
            style.SetBorderWidthAll(2);
            style.SetCornerRadiusAll(32);
            return style;
        }
    }
}
