using Godot;
using System;
using MinorShift._Library;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using System.Threading;
using MinorShift.Emuera.Content;
using gEmuera.Diagnostics;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Session;
using gEmuera.GodotHost;
using System.Threading.Tasks;

public partial class EmueraMain : Node
{
	[Export] public bool debug = false;
	[Export] public bool use_coroutine = false;
	[Export] public bool enable_sprite_debug_viewer = true;

	// GPU work queue for cross-thread ColorMatrix rendering from background thread
	public class GpuWorkItem
	{
		public int Id;
		public Godot.Image SrcImage;
		public Godot.Rect2I SrcRegion;
		public float[][] ColorMatrix;
		public Godot.Image ResultImage;
		public ManualResetEventSlim Completed = new ManualResetEventSlim(false);
	}

	public class TextRenderItem
	{
		public int Id;
		public string Text;
		public string FontName;
		public int FontSize;
		public int FontStyle;
		public uEmuera.Drawing.Color Color;
		public int Width;
		public int Height;
		public Godot.Image ResultImage;
		public ManualResetEventSlim Completed = new ManualResetEventSlim(false);
	}

	static readonly object configMapCacheLock = new object();
	static System.Collections.Generic.Dictionary<string, string> cachedShiftJisToUtf8Map;
	static System.Collections.Generic.Dictionary<string, string> cachedUtf8ZhCnToUtf8Map;
	LegacySessionFacade legacySessionFacade;
	LegacySessionBackend legacySessionBackend;
	LegacySessionLaunchRegistry m0RunnerSessionLaunchRegistry;

	/// <summary>
	/// Legacy facade for the host-owned ColorMatrix queue readiness.
	/// </summary>
	public static bool GpuReady => EmueraGpuRenderComponent.GpuReady;

	/// <summary>
	/// Completes and drops render work submitted by the stopped legacy session.
	/// Work items block a worker for a bounded wait while the Godot main loop
	/// renders them; leaving them in these process-static queues would let a
	/// stale image or text result be consumed by the next candidate.
	/// </summary>
	internal static void ResetCanarySessionState()
	{
		EmueraGpuRenderComponent.ResetCanarySessionState();
		EmueraTextRenderComponent.ResetCanarySessionState();
	}

	/// Submit ColorMatrix work from any thread. Returns the GpuWorkItem for direct wait.
	public static GpuWorkItem GpuSubmitColorMatrix(Godot.Image src, Godot.Rect2I region, float[][] cm)
	{
		return EmueraGpuRenderComponent.Submit(src, region, cm);
	}

	public static TextRenderItem SubmitTextRender(string text, string fontName, int fontSize, int fontStyle, uEmuera.Drawing.Color color, int width, int height)
	{
		return EmueraTextRenderComponent.Submit(text, fontName, fontSize, fontStyle, color, width, height);
	}

	bool startupStarted = false;
	Control startupOverlay;
	Label startupStatusLabel;

	public override void _Ready()
	{
		FrameRateHelper.Apply();
		ResolutionHelper.Apply();
		GenericUtils.SetMainThread();
		GenericUtils.InitializeLogging();
		RuntimeDiagnosticsPanel.AttachFloatingTo(this);
		uEmuera.Logger.isEnabled = GenericUtils.IsLogEnabled;
		uEmuera.Logger.sink = GenericUtils.LogFromBridge;
		uEmuera.Logger.info = content => GenericUtils.Info(content);
		uEmuera.Logger.warn = content => GenericUtils.Warn(content);
		uEmuera.Logger.error = content => GenericUtils.Error(content);

		ApplyLauncherDebugSettings();
		EmueraDebugDialogPanel.AttachTo(this);

		CreateStartupOverlay();
		CallDeferred(nameof(StartGameDeferred));
	}

	const string LauncherSettingsPath = "user://launcher.cfg";
	const string LauncherSettingsSection = "launcher";
	const string LauncherEmueraDebugModeKey = "emuera_debug_mode";
	const string LauncherDebugShowWindowKey = "debug_show_window";

	/// <summary>
	/// 从 user://launcher.cfg [launcher] 读取 Emuera DEBUG 开关并应用到会话启动链：
	/// emuera_debug_mode → debug 标志（与 Export 位或）；debug_show_window →
	/// EmueraThread.DebugShowWindowOverride → Program.DebugShowWindowOverride（launcher
	/// 覆盖 debug.config，默认 true，仅在 DEBUG 模式下生效）。
	/// </summary>
	void ApplyLauncherDebugSettings()
	{
		bool emueraDebugMode = false;
		bool showWindow = true;
		var config = new ConfigFile();
		if (config.Load(LauncherSettingsPath) == Error.Ok)
		{
			emueraDebugMode = config.GetValue(
				LauncherSettingsSection, LauncherEmueraDebugModeKey, false).AsBool();
			showWindow = config.GetValue(
				LauncherSettingsSection, LauncherDebugShowWindowKey, true).AsBool();
		}
		if (emueraDebugMode)
			debug = true;
		debugShowWindowOverride = showWindow;
		EmueraThread.DebugShowWindowOverride = debugShowWindowOverride;
	}

	bool debugShowWindowOverride = true;

	async void StartGameDeferred()
	{
		if (startupStarted)
			return;
		startupStarted = true;

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (!IsInsideTree())
			return;

		UpdateStartupStatus("正在准备游戏目录...");

		// Setup path resolution
		string eraPath = FirstWindow.ResolveStartupGamePath();
		if (string.IsNullOrEmpty(eraPath) || !uEmuera.Utils.DirectoryExists(eraPath))
		{
			eraPath = ProjectSettings.GlobalizePath("res://eraAkumaMaid0.305-CH-正式版");
		}
		if (!string.IsNullOrEmpty(eraPath) && uEmuera.Utils.DirectoryExists(eraPath))
		{
			Sys.ExeDir = uEmuera.Utils.NormalizePath(eraPath + "/");
		}
		else
		{
			Sys.ExeDir = uEmuera.Utils.NormalizePath(OS.GetExecutablePath().GetBaseDir() + "/");
		}
		GenericUtils.NotifyGamePathSelected(Sys.ExeDir, FirstWindow.SelectedCoreProfileName);

		// EmueraContent is created before the legacy worker. Bind the same
		// immutable plan now so its profile-scoped display defaults cannot
		// read the mutable launcher selection directly.
		try
		{
			MinorShift.Emuera.Program.ConfigureCompatibilityPlan(
				BuiltInDialectCatalog.CreateLegacySessionPlan(
					FirstWindow.SelectedCoreProfileName));
		}
		catch (Exception error)
		{
			GenericUtils.Error($"LEGACY_COMPATIBILITY_PLAN_STARTUP_FAILED {error}");
			UpdateStartupStatus("Unable to resolve compatibility profile.");
			return;
		}

		// Load SHIFT-JIS / UTF-8 config maps
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (!IsInsideTree())
			return;

		UpdateStartupStatus("Loading config...");
		LoadConfigMaps();

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (!IsInsideTree())
			return;

		UpdateStartupStatus("Creating interface...");

		// Sprite debug viewer — press F3 to toggle
		if (enable_sprite_debug_viewer && OS.GetName() != "Android")
		{
			var debugViewer = new SpriteDebugViewer();
			debugViewer.Name = "SpriteDebugViewer";
			AddChild(debugViewer);
		}

		// Create content renderer
		var content = new EmueraContent();
		content.Name = "EmueraContent";
		AddChild(content);

		// 虚拟鼠标独立场景：在内容就绪后实例化，作为 Main 的最后一个子节点。
		// Why（场景化而非内联）：main.tscn 保持干净、鼠标可单独 F6 测试；层级不靠树序——
		// VirtualMouse._Ready 的 MoveIntoOwnLayer 会把它移入专用 CanvasLayer(Layer=90)，
		// 恒盖在内容(HTML div z 基准 1024)之上、光标/菜单/弹窗之下。
		// 移动端自动启用；桌面端用系统菜单的鼠标按钮手动开启。
		var mouseScene = GD.Load<PackedScene>("res://鼠标.tscn");
		if (mouseScene != null)
		{
			var mouse = mouseScene.Instantiate();
			AddChild(mouse);
		}

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (!IsInsideTree())
			return;

		UpdateStartupStatus("Starting game...");
		if (!await StartLegacySessionAsync())
		{
			UpdateStartupStatus("Unable to start game.");
			return;
		}
		working = true;

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		HideStartupOverlay();
	}

	public override void _Process(double delta)
	{
		GenericUtils.FlushLogs();
		GenericUtils.FlushUI();
		if (GenericUtils.IsPerformanceSamplingEnabled)
			GenericUtils.SamplePerformanceFrame(delta,
				EmueraGpuRenderComponent.QueuedWorkCount,
				EmueraTextRenderComponent.QueuedWorkCount,
				RendererRuntimeIdentity.GetPerformanceData());

		if (!working)
			return;

		if (clearRequested)
		{
			clearRequested = false;
			GenericUtils.ClearText();
		}

		if (restartRequested)
		{
			restartRequested = false;
			GetTree().ReloadCurrentScene();
			return;
		}

		if (GlobalStatic.MainWindow != null)
			GlobalStatic.MainWindow.Update();

		SpriteManager.UpdateCleanup();
		SpriteManager.UpdateOtherThreads();
		// 按需轮询：仅在 VM 线程请求（GETKEY/GETKEYTRIGGERED）的帧才执行
		// 42 键轮询，INPUT 等待期间不再每帧空转。
		if (MinorShift._Library.WinInput.ConsumeKeyRefreshRequest())
			MinorShift._Library.WinInput.UpdateKeyState();

		var console = GlobalStatic.Console;
		var content = EmueraContent.instance;
		if (console != null && content != null)
		{
			bool needsInput = console.IsWaitingInputSomething;
			if (!needsInput && content.IsInputVisible())
				content.ShowInput(false);
		}
	}

	public override void _ExitTree()
	{
		GenericUtils.NotifyApplicationShutdown();
		StopLegacySession();
		working = false;
		uEmuera.Utils.ResourceClear();
	}

	public async void Run()
	{
		if (await StartLegacySessionAsync())
			working = true;
	}

	/// <summary>
	/// Legacy runner-only same-process restart seam. This remains internal to the
	/// Godot host assembly: normal UI startup never calls it, and legacy Parser/
	/// VM semantics still own the actual session behavior.
	/// </summary>
	/// <summary>
	/// Configures the immutable, runner-only host route allowlist before this
	/// node enters the tree. Normal launcher startup never supplies this map;
	/// it therefore cannot expose a live game/profile switch UI.
	/// </summary>
	internal void ConfigureLegacyRunnerSessionLaunchRegistry(LegacySessionLaunchRegistry launchRegistry)
	{
		ArgumentNullException.ThrowIfNull(launchRegistry);
		if (startupStarted || legacySessionFacade is not null || legacySessionBackend is not null)
			throw new InvalidOperationException("Runner session launch routes must be configured before startup.");
		m0RunnerSessionLaunchRegistry = launchRegistry;
	}

	internal Task<LegacySessionSwitchResult> RestartLegacySessionForLegacyRunnerAsync()
	{
		var selection = legacySessionFacade?.Current?.Selection
			?? new SessionSelection(
				BuildLegacyGameId(Sys.ExeDir),
				FirstWindow.SelectedCoreProfileName);
		return SwitchLegacySessionForLegacyRunnerAsync(selection);
	}

	/// <summary>
	/// Legacy runner-only cross-configuration seam. The caller can select only a
	/// pre-registered host binding; Core receives the resulting opaque game id
	/// and profile, never a filesystem path.
	/// </summary>
	internal Task<LegacySessionSwitchResult> SwitchLegacySessionForLegacyRunnerAsync(
		LegacySessionLaunchConfiguration launch)
	{
		ArgumentNullException.ThrowIfNull(launch);
		return SwitchLegacySessionForLegacyRunnerAsync(launch.CreateSelection());
	}

	async Task<LegacySessionSwitchResult> SwitchLegacySessionForLegacyRunnerAsync(
		SessionSelection selection)
	{
		var facade = legacySessionFacade
			?? throw new InvalidOperationException("M1 session-isolation facade is not active.");
		// EmueraThread.Running() mirrors Console.IsInProcess, which is false while
		// the legacy VM is deliberately blocked at INPUT/WAIT. The facade owns the
		// lifecycle state needed for a transactional restart.
		if (!facade.IsBackendRunning)
			throw new InvalidOperationException("Legacy session is not running and cannot be restarted.");

		GetLegacySessionLaunchRegistry().Resolve(selection);
		bool wasWorking = working;
		working = false;
		try
		{
			var result = await facade.SwitchAsync(selection, null);
			if (result.IsCommitted)
			{
				GenericUtils.Info(
					$"M1_SESSION_ISOLATION_INPROCESS_SWITCH generation={result.Session.Stamp.Generation.Value} " +
					$"profile={selection.ProfileId} plan={result.Session.Session?.Compatibility.CanonicalHash ?? "unknown"}");
			}
			return result;
		}
		finally
		{
			working = wasWorking && facade.IsBackendRunning;
		}
	}

	async Task<bool> StartLegacySessionAsync()
	{
		// This is deliberately evaluated only when starting a session. A
		// diagnostic-config hot reload must never swap an already running VM
		// between the Legacy baseline path and the M1 canary path.
		if (legacySessionBackend?.IsRunning == true)
			return true;

		bool useSessionIsolation = GenericUtils.GetRuntimeDiagnosticsConfig()?.MigrationSessionIsolationEnabled == true;
		if (!useSessionIsolation)
		{
			var backend = legacySessionBackend ??= new LegacySessionBackend(debug, use_coroutine);
			try
			{
				await backend.StartLegacyBaselineAsync();
				return true;
			}
			catch (Exception error)
			{
				GenericUtils.Error($"LEGACY_BASELINE_START_EXCEPTION {error}");
				return false;
			}
		}

		try
		{
			var backend = legacySessionBackend ??= new LegacySessionBackend(
				debug,
				use_coroutine,
				GetLegacySessionLaunchRegistry());
			legacySessionFacade ??= LegacySessionFacade.CreateLegacyBaseline(
				backend);
			var selection = new SessionSelection(
				BuildLegacyGameId(Sys.ExeDir),
				FirstWindow.SelectedCoreProfileName);
			GetLegacySessionLaunchRegistry().Resolve(selection);
			var result = await legacySessionFacade.SwitchAsync(selection, null);
			if (result.IsCommitted)
			{
				GenericUtils.Info(
					$"M1_SESSION_ISOLATION_CANARY profile={selection.ProfileId} plan={result.Session.Session?.Compatibility.CanonicalHash ?? "unknown"}");
				return true;
			}

			var error = result.BackendError ?? result.Session.Error;
			GenericUtils.Error(
				$"LEGACY_SESSION_START_FAILED status={result.Session.Status} generation={result.Session.Stamp.Generation.Value} " +
				$"error={error?.Message ?? "none"}");
			return false;
		}
		catch (Exception error)
		{
			GenericUtils.Error($"LEGACY_SESSION_START_EXCEPTION {error}");
			return false;
		}
	}

	void StopLegacySession()
	{
		var facade = legacySessionFacade;
		legacySessionFacade = null;
		if (facade is not null)
		{
			try
			{
				// The current legacy bridge completes synchronously after stopping
				// EmueraThread. Keeping the wait here preserves the former
				// _ExitTree ordering: thread end -> legacy state reset -> resources.
				facade.DisposeAsync().AsTask().GetAwaiter().GetResult();
			}
			catch (Exception error)
			{
				GenericUtils.Error($"LEGACY_SESSION_STOP_FAILED {error}");
			}
			return;
		}

		var backend = legacySessionBackend;
		if (backend is null || !backend.IsRunning)
			return;

		try
		{
			backend.StopLegacyBaselineAsync().AsTask().GetAwaiter().GetResult();
		}
		catch (Exception error)
		{
			GenericUtils.Error($"LEGACY_BASELINE_STOP_FAILED {error}");
		}
	}

	static string BuildLegacyGameId(string gameDirectory)
	{
		return LegacySessionLaunchConfiguration.CreateGameId(gameDirectory);
	}

	LegacySessionLaunchRegistry GetLegacySessionLaunchRegistry()
	{
		return m0RunnerSessionLaunchRegistry ?? new LegacySessionLaunchRegistry(
			new[]
			{
				new LegacySessionLaunchConfiguration(
					Sys.ExeDir,
					FirstWindow.SelectedCoreProfileName),
			});
	}

	public void Clear()
	{
		if (working)
		{
			// Request clear on next process
			clearRequested = true;
		}
	}

	public void Restart()
	{
		if (working)
		{
			restartRequested = true;
		}
	}

	bool working = false;
	bool clearRequested = false;
	bool restartRequested = false;

	void CreateStartupOverlay()
	{
		startupOverlay = new Control();
		startupOverlay.Name = "StartupOverlay";
		startupOverlay.AnchorLeft = 0;
		startupOverlay.AnchorTop = 0;
		startupOverlay.AnchorRight = 1;
		startupOverlay.AnchorBottom = 1;
		startupOverlay.MouseFilter = Control.MouseFilterEnum.Stop;

		var bg = new ColorRect();
		bg.AnchorLeft = 0;
		bg.AnchorTop = 0;
		bg.AnchorRight = 1;
		bg.AnchorBottom = 1;
		bg.Color = Colors.Black;
		startupOverlay.AddChild(bg);

		startupStatusLabel = new Label();
		startupStatusLabel.AnchorLeft = 0;
		startupStatusLabel.AnchorTop = 0;
		startupStatusLabel.AnchorRight = 1;
		startupStatusLabel.AnchorBottom = 1;
		startupStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		startupStatusLabel.VerticalAlignment = VerticalAlignment.Center;
		startupStatusLabel.Text = "Loading game...";
		startupStatusLabel.AddThemeFontSizeOverride("font_size", 20);
		startupStatusLabel.AddThemeColorOverride("font_color", Colors.White);
		startupOverlay.AddChild(startupStatusLabel);

		AddChild(startupOverlay);
	}

	void UpdateStartupStatus(string status)
	{
		if (startupStatusLabel != null)
			startupStatusLabel.Text = status;
	}

	void HideStartupOverlay()
	{
		if (startupOverlay == null)
			return;

		startupOverlay.QueueFree();
		startupOverlay = null;
		startupStatusLabel = null;
	}

	void LoadConfigMaps()
	{
		lock (configMapCacheLock)
		{
			if (cachedShiftJisToUtf8Map != null && cachedUtf8ZhCnToUtf8Map != null)
			{
				uEmuera.Utils.SetSHIFTJIS_to_UTF8Dict(cachedShiftJisToUtf8Map);
				uEmuera.Utils.SetUTF8ZHCN_to_UTF8Dict(cachedUtf8ZhCnToUtf8Map);
				return;
			}
		}

		char[] split = new char[] { '\r', '\n' };
		var shiftjisPath = "res://assets/text/emuera_config_shiftjis.bytes";
		var utf8Path = "res://assets/text/emuera_config_utf8.txt";
		var utf8CnPath = "res://assets/text/emuera_config_utf8_zhcn.txt";

		if (!Godot.FileAccess.FileExists(shiftjisPath) ||
			!Godot.FileAccess.FileExists(utf8Path) ||
			!Godot.FileAccess.FileExists(utf8CnPath))
			return;

		var shiftjisBytes = Godot.FileAccess.GetFileAsBytes(shiftjisPath);
		var utf8Text = Godot.FileAccess.GetFileAsString(utf8Path);
		var utf8CnText = Godot.FileAccess.GetFileAsString(utf8CnPath);

		var jis_md5_strs = GenericUtils.CalcMd5List(shiftjisBytes);

		var utf8_strs = utf8Text.Split(split, System.StringSplitOptions.RemoveEmptyEntries);
		var utf8_str_list = new System.Collections.Generic.List<string>();
		foreach (var str in utf8_strs)
		{
			if (string.IsNullOrWhiteSpace(str))
				continue;
			utf8_str_list.Add(str);
		}

		var utf8cn_strs = utf8CnText.Split(split, System.StringSplitOptions.RemoveEmptyEntries);
		var utf8cn_str_list = new System.Collections.Generic.List<string>();
		foreach (var str in utf8cn_strs)
		{
			if (string.IsNullOrWhiteSpace(str))
				continue;
			utf8cn_str_list.Add(str);
		}

		if (jis_md5_strs.Count == 0 || utf8_str_list.Count == 0)
			return;

		var jis_map = new System.Collections.Generic.Dictionary<string, string>();
		int jisCount = System.Math.Min(jis_md5_strs.Count, utf8_str_list.Count);
		for (int i = 0; i < jisCount; ++i)
		{
			jis_map[jis_md5_strs[i]] = utf8_str_list[i];
		}
		var utf8cn_map = new System.Collections.Generic.Dictionary<string, string>();
		int utf8CnCount = System.Math.Min(utf8cn_str_list.Count, utf8_str_list.Count);
		for (int i = 0; i < utf8CnCount; ++i)
		{
			utf8cn_map[utf8cn_str_list[i]] = utf8_str_list[i];
		}
		lock (configMapCacheLock)
		{
			// res://Text 配置映射在进程内不变化，缓存后重启游戏不再重复读盘和构建字典。
			cachedShiftJisToUtf8Map ??= jis_map;
			cachedUtf8ZhCnToUtf8Map ??= utf8cn_map;
			uEmuera.Utils.SetSHIFTJIS_to_UTF8Dict(cachedShiftJisToUtf8Map);
			uEmuera.Utils.SetUTF8ZHCN_to_UTF8Dict(cachedUtf8ZhCnToUtf8Map);
		}
	}
}
