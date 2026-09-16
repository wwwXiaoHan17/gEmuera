// GenericUtils.DiagnosticsLogging.cs —— 承载 TOML 配置装载/等级类别闸门/结构化日志/诊断导出/会话 ID/Android 存储诊断功能域，自 GenericUtils.cs 拆出（原因：主文件超 2000 行只减不增约束）。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Godot;
using gEmuera.Diagnostics;
using MinorShift.Emuera.GameView;

internal static partial class GenericUtils
{
#if DEBUG || GEMUERA_DIAGNOSTIC_LOGS
    const bool VerboseLogBuild = true;
#else
    const bool VerboseLogBuild = false;
#endif
    static int runtimeLogLevel = (int)EmueraLogLevel.None;
    static int runtimeLogCategories = (int)EmueraLogCategory.None;
    static int mirrorNonErrorLogsToGodot = 0;
    static int loggingInitialized = 0;

    static string _runtimeGamePath = "";
    static string _runtimeCoreProfile = "";

    public static bool IsDiagnosticsLoggingEnabled => _runtimeConfig?.LoggingEnabled ?? false;

    public static void FlushLogs()
    {
        // 新诊断系统已统一在 LogInternal 中按主线程/非主线程策略直接写入，
        // 旧日志刷新入口保留为空实现，避免调用方编译错误。
    }


    public static EmueraLogLevel RuntimeLogLevel
    {
        get => (EmueraLogLevel)Volatile.Read(ref runtimeLogLevel);
        set => Volatile.Write(ref runtimeLogLevel, (int)value);
    }

    public static EmueraLogCategory RuntimeLogCategories
    {
        get => (EmueraLogCategory)Volatile.Read(ref runtimeLogCategories);
        set => Volatile.Write(ref runtimeLogCategories, (int)value);
    }

    public static bool MirrorNonErrorLogsToGodot
    {
        get => Volatile.Read(ref mirrorNonErrorLogsToGodot) != 0;
        set => Volatile.Write(ref mirrorNonErrorLogsToGodot, value ? 1 : 0);
    }

    /// <summary>
    /// 企业级说明：日志初始化只做一次 TOML 配置解析，将开关展开为已解析的布尔值。
    /// 后续热路径只读取展开后的 int/布尔，不再查询字典或解析字符串，避免 Android 帧尖峰。
    /// 默认 Release/APK 只输出 Error，Debug/诊断构建可按 config.toml 开启模块。
    /// </summary>
    public static void InitializeLogging()
    {
        // 企业级说明：Android APK 冷启动可能先进入启动器界面，随后才进入实际模拟器主场景。
        // 日志系统必须允许更早初始化，但不能在场景切换时重置 ring buffer、session 或 breadcrumb。
        if (Interlocked.CompareExchange(ref loggingInitialized, 1, 0) != 0)
            return;

        var loadResult = RuntimeDiagnosticsConfigLoader.Load();
        _runtimeConfig = loadResult.Config ?? RuntimeDiagnosticsConfig.CreateDefault();
        string sessionId = BuildSessionId(_runtimeConfig.LoggingSessionIdFormat);
        DiagnosticLogRouter.Initialize(_runtimeConfig, sessionId);
        DiagnosticLogSinks.Initialize(_runtimeConfig);

        ApplyRuntimeDiagnosticsConfig();
        if (!_runtimeConfig.LoggingEnabled)
            return;

        DiagnosticLogExporter.WriteBreadcrumb(_runtimeConfig, "LOG.INIT", "source=GenericUtils.InitializeLogging");
        WriteConfigSelfCheck(loadResult);
        WriteAndroidStorageDiagnostics();

        if (_runtimeConfig.BreadcrumbEnabled && _runtimeConfig.BreadcrumbWriteOnStartup)
            DiagnosticLogExporter.WriteBreadcrumb(_runtimeConfig, "BREADCRUMB.WRITE", "event=startup");
        if (_runtimeConfig.RetentionEnabled && _runtimeConfig.RetentionCleanupOnStartup)
            DiagnosticLogExporter.RunRetentionCleanup(_runtimeConfig);
    }

    public static RuntimeDiagnosticsConfig GetRuntimeDiagnosticsConfig() => _runtimeConfig;

    /// <summary>
    /// 企业级说明：运行时面板保存 user://config.toml 后调用本方法热重载诊断开关。
    /// 只更新等级、类别、镜像、滚动追踪和输入复现等轻量运行时状态；ring buffer 容量等结构性参数保留下次启动生效。
    /// </summary>
    public static bool ReloadRuntimeDiagnosticsConfig(out string errorMessage)
    {
        errorMessage = "";
        var loadResult = RuntimeDiagnosticsConfigLoader.Load();
        _runtimeConfig = loadResult.Config ?? RuntimeDiagnosticsConfig.CreateDefault();
        ApplyRuntimeDiagnosticsConfig();
        DiagnosticLogRouter.Reload(_runtimeConfig);
        if (!string.IsNullOrEmpty(loadResult.ErrorMessage))
        {
            errorMessage = loadResult.ErrorMessage;
            return false;
        }
        return true;
    }

    static void ApplyRuntimeDiagnosticsConfig()
    {
        if (_runtimeConfig == null || !_runtimeConfig.LoggingEnabled)
        {
            RuntimeLogLevel = EmueraLogLevel.None;
            RuntimeLogCategories = EmueraLogCategory.None;
            MirrorNonErrorLogsToGodot = false;
            ScrollTraceEnabled = false;
            DiagnosticLogSinks.SetMirrorNonErrorToGodot(false);
            DiagnosticLogSinks.Reload(_runtimeConfig);
            RuntimeDiagnosticsPanel.SetDiagnosticsPanelVisible(false);
            _inputReplay = null;
            return;
        }

        var model = _runtimeConfig.GetActiveDebugModel();
        if (model != null && model.Enabled)
        {
            RuntimeLogLevel = RuntimeDiagnosticsConfig.ParseLogLevel(model.LogLevel);
            MirrorNonErrorLogsToGodot = model.MirrorToGodot;
            ScrollTraceEnabled = model.ScrollTrace;
        }
        else
        {
            RuntimeLogLevel = _runtimeConfig.GetRuntimeLogLevel();
            MirrorNonErrorLogsToGodot = _runtimeConfig.LoggingMirrorNonErrorToGodot;
            ScrollTraceEnabled = false;
        }

        RuntimeLogCategories = _runtimeConfig.GetActiveDebugModelCategoryMask();

        DiagnosticLogSinks.SetMirrorNonErrorToGodot(MirrorNonErrorLogsToGodot);
        DiagnosticLogSinks.Reload(_runtimeConfig);
        RuntimeDiagnosticsPanel.SetDiagnosticsPanelVisible(_runtimeConfig.RuntimePanelEnabled);
        _inputReplay = _runtimeConfig.InputReplayEnabled
            ? new InputReplayBuffer(_runtimeConfig.InputReplayMaxEvents)
            : null;
    }

    /// <summary>
    /// 运行时显示/隐藏诊断悬浮球与面板（悬浮球与面板节点均由 RuntimeDiagnosticsPanel 静态管理）。
    /// 悬浮窗未挂载（debug.runtime_panel.enabled=false 或启动时 panel_visible=false）时为空操作，
    /// 此时需重启或开启挂载门后生效。
    /// </summary>
    public static void SetDiagnosticsPanelVisible(bool visible)
    {
        RuntimeDiagnosticsPanel.SetDiagnosticsPanelVisible(visible);
    }

    static void WriteConfigSelfCheck(RuntimeDiagnosticsConfigLoader.LoadResult loadResult)
    {
        var cfg = _runtimeConfig;
        if (cfg == null)
            return;
        string activeModules = BuildActiveDiagnosticModulesSummary(cfg);
        DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Config,
            "CONFIG.SELF_CHECK", "runtime diagnostics config loaded",
            "schema_version=" + cfg.LoggingSchemaVersion
            + " file_found=" + loadResult.FileFound
            + " loaded_from=" + DiagnosticLogRouter.RedactPath(loadResult.LoadedFrom)
            + " quick_enabled=" + cfg.QuickDebugEnabled
            + " quick_preset=" + cfg.QuickDebugPreset
            + " quick_effective=" + cfg.QuickDebugEffectivePreset
            + " quick_language=" + cfg.QuickDebugEffectiveLanguage
            + " active_model=" + cfg.ActiveDebugModel
            + " runtime_level=" + cfg.GetRuntimeLogLevel().ToString().ToLowerInvariant()
            + " modules=" + activeModules);
        DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Config,
            "LOG.MODULES.ACTIVE", "active diagnostic modules",
            "modules=" + activeModules);
        if (!VerboseLogBuild && cfg.GetActiveDebugModel()?.Enabled == true)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.Config,
                "LOG.MODULE.FORCED_OFF", "non-error logs are disabled by build policy",
                "reason=release_build_without_GEMUERA_DIAGNOSTIC_LOGS");
        }
        if (cfg.QuickDebugPresetInvalid)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.Config,
                "CONFIG.QUICK_PRESET.INVALID", "invalid quick_debug preset, fallback to normal",
                "preset=" + DiagnosticLogRouter.RedactText(cfg.QuickDebugPreset, 64)
                + " effective=" + cfg.QuickDebugEffectivePreset);
        }
        if (cfg.QuickDebugLanguageInvalid)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.Config,
                "CONFIG.QUICK_LANGUAGE.INVALID", "invalid quick_debug language, fallback to zh_cn",
                "language=" + DiagnosticLogRouter.RedactText(cfg.QuickDebugLanguage, 32)
                + " effective=" + cfg.QuickDebugEffectiveLanguage);
        }
    }

    static string BuildActiveDiagnosticModulesSummary(RuntimeDiagnosticsConfig cfg)
    {
        var parts = new List<string>(16);
        if (cfg.TouchEnabled) parts.Add("touch");
        if (cfg.InputDebugEnabled) parts.Add("input");
        if (cfg.StatementRecognitionEnabled) parts.Add("statement_recognition");
        if (cfg.ImageDebugEnabled) parts.Add("image");
        if (cfg.UiLayoutEnabled) parts.Add("ui_layout");
        if (cfg.DynamicMapDebugEnabled) parts.Add("dynamic_map");
        if (cfg.RuntimePanelEnabled) parts.Add("runtime_panel");
        if (cfg.InputReplayEnabled) parts.Add("input_replay");
        if (cfg.AndroidStorageEnabled) parts.Add("android_storage");
        if (cfg.PerformanceSamplingEnabled) parts.Add("performance_sampling");
        if (cfg.SnapshotEnabled) parts.Add("snapshot");
        if (cfg.UiOverlayEnabled) parts.Add("ui_overlay");
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }

    static void WriteAndroidStorageDiagnostics()
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.AndroidStorageEnabled || OS.GetName() != "Android")
            return;

        string root = "/storage/emulated/0/emuera";
        bool rootExists = Directory.Exists(root);
        if (cfg.AndroidStorageLogPermissions)
        {
            string permissions = BuildGrantedPermissionSummary();
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.FileSystem,
                "ANDROID_STORAGE.PERMISSION", "android storage permission summary",
                "platform=" + OS.GetName()
                + " mobile=" + OS.HasFeature("mobile")
                + " permissions=" + permissions);
        }
        if (cfg.AndroidStorageLogScopedStorage)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.FileSystem,
                "ANDROID_STORAGE.SCOPED_STORAGE", "android scoped storage summary",
                "root=" + root + " root_exists=" + rootExists + " max_path_records=" + cfg.AndroidStorageMaxPathRecords);
        }
        if (cfg.AndroidStorageLogGameScan)
        {
            int eraCount = CountEraDirectories(root, cfg.AndroidStorageMaxPathRecords, out string sample);
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.FileSystem,
                "ANDROID_STORAGE.GAME_SCAN", "android game directory scan summary",
                "root=" + root + " root_exists=" + rootExists + " era_count=" + eraCount + " sample=" + sample);
        }
        if (cfg.AndroidStorageLogReadWriteFailures)
            TryAndroidStorageWriteProbe(root);
    }

    static string BuildGrantedPermissionSummary()
    {
        try
        {
            var permissions = OS.GetGrantedPermissions();
            if (permissions == null || permissions.Length == 0)
                return "none";
            return string.Join(",", permissions);
        }
        catch (Exception ex)
        {
            return "unavailable:" + ex.GetType().Name;
        }
    }

    static int CountEraDirectories(string root, int maxRecords, out string sample)
    {
        var samples = new List<string>(Math.Max(1, Math.Min(maxRecords, 16)));
        int count = CountEraDirectoriesRecursive(root, 0, 2, Math.Max(1, maxRecords), samples);
        sample = samples.Count == 0 ? "" : string.Join("|", samples);
        return count;
    }

    static int CountEraDirectoriesRecursive(string path, int depth, int maxDepth, int maxRecords, List<string> samples)
    {
        if (string.IsNullOrEmpty(path) || depth > maxDepth || !Directory.Exists(path))
            return 0;
        int count = 0;
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(path))
            {
                if (IsEraDirectory(dir))
                {
                    count++;
                    if (samples.Count < maxRecords)
                        samples.Add(DiagnosticLogRouter.RedactPath(dir));
                }
                if (depth < maxDepth)
                    count += CountEraDirectoriesRecursive(dir, depth + 1, maxDepth, maxRecords, samples);
                if (samples.Count >= maxRecords && count >= maxRecords)
                    break;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.FileSystem,
                "ANDROID_STORAGE.READ_FAIL", "android game directory scan failed",
                "path=" + DiagnosticLogRouter.RedactPath(path) + " error=" + ex.GetType().Name);
        }
        return count;
    }

    static bool IsEraDirectory(string path)
    {
        return Directory.Exists(System.IO.Path.Combine(path, "ERB"))
            || Directory.Exists(System.IO.Path.Combine(path, "erb"));
    }

    static void TryAndroidStorageWriteProbe(string root)
    {
        if (!Directory.Exists(root))
            return;
        string probe = System.IO.Path.Combine(root, ".gemuera_write_probe.tmp");
        try
        {
            File.WriteAllText(probe, "probe", Encoding.UTF8);
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.FileSystem,
                "ANDROID_STORAGE.WRITE_FAIL", "android storage write probe failed",
                "path=" + DiagnosticLogRouter.RedactPath(probe) + " error=" + ex.GetType().Name);
        }
    }

    /// <summary>
    /// 企业级说明：所有诊断日志的运行时总闸门。
    /// Release/APK 默认只允许 Error；诊断构建中也必须同时通过等级、类别和模块开关，防止误开高频日志拖慢手机。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLogEnabled(EmueraLogLevel level, EmueraLogCategory category = EmueraLogCategory.General)
    {
        if (!(_runtimeConfig?.LoggingEnabled ?? false))
            return false;
        if (level == EmueraLogLevel.None)
            return false;
        if (!VerboseLogBuild && level < EmueraLogLevel.Error)
            return false;
        if ((int)level < Volatile.Read(ref runtimeLogLevel))
            return false;
        if (level < EmueraLogLevel.Error
            && category != EmueraLogCategory.None
            && (Volatile.Read(ref runtimeLogCategories) & (int)category) == 0)
        {
            return false;
        }
        return true;
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Debug(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Debug, category, content, null, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Debug(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Debug, category, null, messageFactory, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Info(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Info, category, content, null, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Info(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Info, category, null, messageFactory, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Warn(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Warn, category, content, null, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Warn(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Warn, category, null, messageFactory, member, file, line);
    }

    public static void Error(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Error, category, content, null, member, file, line);
    }

    public static void Error(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Error, category, null, messageFactory, member, file, line);
    }

    public static void LogFromBridge(EmueraLogLevel level, EmueraLogCategory category, object content,
        Func<string> messageFactory, string member, string file, int line)
    {
        LogInternal(level, category, content, messageFactory, member, file, line);
    }

    /// <summary>
    /// 企业级说明：所有日志统一经 DiagnosticLogRouter 做开关/限流判断，再经 DiagnosticLogSinks 写入 ring buffer 和 Godot 控制台。
    /// 关闭日志时不构造 message，不对 category 做额外归一化，避免热路径分配。
    /// 限流检查在 message 构造之前，若被限流则直接返回，不产生字符串。
    /// </summary>
    static void LogInternal(EmueraLogLevel level, EmueraLogCategory category, object content,
        Func<string> messageFactory, string member, string file, int line)
    {
        if (!IsLogEnabled(level, category))
            return;

        // 先用初始 category 推导 event_id 做限流预检，避免构造 message 后被限流浪费分配。
        string preliminaryEventId = DeriveEventId(category, level);
        if (!DiagnosticLogRouter.IsEnabled(level, category, preliminaryEventId))
            return;

        string message = BuildLogMessage(content, messageFactory);
        category = NormalizeLogCategory(category, message);
        string eventId = DeriveEventId(category, level);

        string source = NormalizeSourcePath(file);
        string data = AppendCorrelationData(category, eventId, "", source, line);
        var record = DiagnosticLogRouter.BuildRecord(level, category, eventId, message, data, member, source, line);
        DiagnosticLogSinks.Write(record);

        if (level >= EmueraLogLevel.Error)
            DiagnosticLogExporter.NotifyError(eventId);
        else
            DiagnosticLogExporter.NotifyEvent(eventId);
    }

    /// <summary>
    /// 企业级说明：结构化诊断日志专用入口，接受调用方显式传入的 event_id 和 data。
    /// 不走 DeriveEventId 派生，确保 TOUCH.* / INPUT.* / IMAGE.* / UI_LAYOUT.* 等稳定事件 ID 原样写入记录。
    /// 限流使用显式 eventId，保证同一事件 ID 有独立限流桶。
    /// messageFactory 为延迟构造，仅在开关和限流通过后执行。
    /// </summary>
    static void LogStructured(EmueraLogLevel level, EmueraLogCategory category,
        string eventId, string data,
        Func<string> messageFactory,
        string member, string file, int line)
    {
        LogStructured(level, category, eventId, () => data, messageFactory, member, file, line);
    }

    /// <summary>
    /// 企业级说明：结构化 data 也必须延迟构造。Android 触摸、图片和 UI 几何日志开启后仍可能被限流，
    /// 因此 message/data 都只能在开关、类别和事件限流全部通过后再生成。
    /// </summary>
    static void LogStructured(EmueraLogLevel level, EmueraLogCategory category,
        string eventId, Func<string> dataFactory,
        Func<string> messageFactory,
        string member, string file, int line)
    {
        if (!IsLogEnabled(level, category))
            return;
        if (string.IsNullOrEmpty(eventId))
            eventId = DeriveEventId(category, level);

        // 限流预检：使用显式 event_id，避免构造 message 后被限流浪费分配。
        if (!DiagnosticLogRouter.IsEnabled(level, category, eventId))
            return;

        string source = NormalizeSourcePath(file);
        string message = BuildLogMessage(null, messageFactory);
        string data = AppendCorrelationData(category, eventId, BuildLogData(dataFactory), source, line);
        var record = DiagnosticLogRouter.BuildRecord(level, category, eventId, message, data ?? "", member, source, line);
        DiagnosticLogSinks.Write(record);

        if (level >= EmueraLogLevel.Error)
            DiagnosticLogExporter.NotifyError(eventId);
        else
            DiagnosticLogExporter.NotifyEvent(eventId);
    }

    static string BuildLogMessage(object content, Func<string> messageFactory)
    {
        string message;
        try
        {
            message = messageFactory != null ? messageFactory() : content?.ToString();
        }
        catch (Exception ex)
        {
            message = "[LOGGER] message factory failed: " + ex.GetType().Name + ": " + ex.Message;
        }
        return ClipLogMessage(message);
    }

    static string BuildLogData(Func<string> dataFactory)
    {
        if (dataFactory == null)
            return "";
        try
        {
            return dataFactory() ?? "";
        }
        catch (Exception ex)
        {
            return "[LOGGER] data factory failed: " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    static string AppendCorrelationData(EmueraLogCategory category, string eventId, string data, string source, int line)
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.CorrelationEnabled)
            return data ?? "";

        string result = data ?? "";
        // 企业级说明：关联字段在日志通过开关和限流之后追加，避免关闭调试时产生字符串分配。
        // 字段采用轻量整数或短哈希，不在 Android 热路径生成 GUID。
        if (cfg.CorrelationSessionId && !ContainsDataKey(result, "session_id"))
            result = AppendDataField(result, "session_id=" + DiagnosticLogRouter.SessionId);
        if (cfg.CorrelationInputId && !ContainsDataKey(result, "input_id"))
            result = AppendDataField(result, "input_id=" + CurrentInputId);
        if (cfg.CorrelationRenderBatchId
            && (category == EmueraLogCategory.UI || category == EmueraLogCategory.Sprite)
            && !ContainsDataKey(result, "render_batch_id"))
        {
            result = AppendDataField(result, "render_batch_id=" + UiFrameGeneration);
        }
        if (cfg.CorrelationLinePartId
            && (category == EmueraLogCategory.UI || category == EmueraLogCategory.Sprite || category == EmueraLogCategory.Script)
            && !ContainsDataKey(result, "line_part_id"))
        {
            result = AppendDataField(result, "line_part_id=" + BuildLinePartId(source, line));
        }
        if (cfg.CorrelationImageId && category == EmueraLogCategory.Sprite && !ContainsDataKey(result, "image_id"))
            result = AppendDataField(result, "image_id=" + BuildImageCorrelationId(eventId, result));
        return result;
    }

    static bool ContainsDataKey(string data, string key)
    {
        return !string.IsNullOrEmpty(data) && data.Contains(key + "=", StringComparison.Ordinal);
    }

    static string AppendDataField(string data, string field)
    {
        if (string.IsNullOrEmpty(data))
            return field;
        return data + " " + field;
    }

    static string BuildLinePartId(string source, int line)
    {
        string src = string.IsNullOrEmpty(source) ? "unknown" : System.IO.Path.GetFileNameWithoutExtension(source);
        return src + ":" + line;
    }

    static string BuildImageCorrelationId(string eventId, string data)
    {
        string key = ExtractDataValue(data, "resource");
        if (string.IsNullOrEmpty(key))
            key = ExtractDataValue(data, "name");
        if (string.IsNullOrEmpty(key))
            key = ExtractDataValue(data, "filename");
        if (string.IsNullOrEmpty(key))
            key = eventId ?? "image";
        uint hash = 2166136261u;
        foreach (char c in key)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return "img_" + hash.ToString("x8");
    }

    static string ExtractDataValue(string data, string key)
    {
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(key))
            return "";
        string prefix = key + "=";
        foreach (var part in data.Split(' '))
        {
            if (part.StartsWith(prefix, StringComparison.Ordinal))
                return part.Substring(prefix.Length);
        }
        return "";
    }

    static string DeriveEventId(EmueraLogCategory category, EmueraLogLevel level)
    {
        switch (category)
        {
            case EmueraLogCategory.Sprite: return "SPRITE." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Audio: return "AUDIO." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Input: return "INPUT." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Script: return "SCRIPT." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.UI: return "UI." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.FileSystem: return "FS." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Load: return "LOAD." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Save: return "SAVE." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Config: return "CONFIG." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Performance: return "PERF." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Touch: return "TOUCH." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.StatementRecognition: return "PARSER." + level.ToString().ToUpperInvariant();
            default: return "LOG." + level.ToString().ToUpperInvariant();
        }
    }

    public static string GetDefaultDiagnosticLogPath(string stamp = null)
    {
        if (string.IsNullOrEmpty(stamp))
            stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return $"{DiagnosticLogExporter.GameDirectoryPathPrefix}gemuera_{stamp}.log";
    }

    public static string ResolveDiagnosticPathForDisplay(string path)
    {
        return DiagnosticLogExporter.ResolvePathForDisplay(path);
    }

    static string BuildSessionId(string format)
    {
        // 企业级说明：session_id_format 来自可编辑 TOML，不能让格式错误阻断 APK 启动。
        // 格式异常时回退到稳定默认值，并继续让配置自检日志记录实际运行状态。
        if (string.IsNullOrWhiteSpace(format))
            format = "yyyyMMdd-HHmmss";
        try
        {
            return DateTime.Now.ToString(format);
        }
        catch (FormatException)
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }
    }

    /// <summary>
    /// 企业级说明：导出日志只在用户主动触发时写盘，Android 游玩期间不产生文件 I/O。
    /// 委托 DiagnosticLogExporter 生成带报告头的结构化日志。
    /// </summary>
    public static bool ExportDiagnosticLog(string path, out string errorMessage)
    {
        errorMessage = "";
        if (!(_runtimeConfig?.LoggingEnabled ?? false))
        {
            errorMessage = "diagnostics_disabled";
            return false;
        }
        if (string.IsNullOrEmpty(path))
            path = GetDefaultDiagnosticLogPath();

        if (_runtimeConfig != null && _runtimeConfig.BreadcrumbWriteOnExport)
            DiagnosticLogExporter.WriteBreadcrumb(_runtimeConfig, "BREADCRUMB.WRITE", "event=before_export path=" + path);

        string gamePath = _runtimeGamePath;
        string coreProfile = _runtimeCoreProfile;
        //报告头必须反映真实运行配置：此前硬编码 false，误导排障（20260819 华扇口上日志实证）。
        bool useLazyLoading = MinorShift.Emuera.Config.UseLazyLoading && MinorShift.Emuera.Program.SupportsLazyLoading;
        bool ok = DiagnosticLogExporter.ExportDiagnosticLog(_runtimeConfig, path, gamePath, coreProfile, useLazyLoading, _saveLogOperationTrail, out errorMessage);

        if (_runtimeConfig != null && _runtimeConfig.BreadcrumbWriteOnExport)
            DiagnosticLogExporter.WriteBreadcrumb(_runtimeConfig, "BREADCRUMB.WRITE", "event=after_export ok=" + ok);
        return ok;
    }

    // —— emuera.log 自动触发诊断导出（2026-09-12）——
    // 引擎在游戏报错路径自动写出 emuera.log（启动致命错误/解析警告容错/运行中错误）。
    // 此前 gemuera 侧诊断日志只能靠菜单手工导出；现在首个 emuera.log 落盘时会话内
    // 自动导出一次（含错误上下文，最有排障价值），后续报错不再重复导出文件。
    static int autoDiagnosticExported;

    /// <summary>
    /// 引擎自动写出 emuera.log 时调用：会话内首次触发 gemuera 诊断导出（主线程执行）。
    /// 幂等；线程安全（Interlocked 抢占 + EnqueueUI 切主线程，导出与手工触发同路径）。
    /// </summary>
    public static void AutoExportDiagnosticOnEmueraLog(string emueraLogPath)
    {
        if (System.Threading.Interlocked.Exchange(ref autoDiagnosticExported, 1) != 0)
            return;
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        EnqueueUI(() =>
        {
            string diagnosticPath = GetDefaultDiagnosticLogPath("auto_" + stamp);
            // 触发记录写在导出之前：让 auto 日志自身包含触发链（emuera.log 路径），
            // 排障时单文件自足；导出结果另由后续日志/面板可见。
            Info(EmueraLogCategory.General, () =>
                $"[SaveLog] auto-export triggered by emuera.log at {emueraLogPath}; exporting to {diagnosticPath}");
            ExportDiagnosticLog(diagnosticPath, out _);
        });
    }

    /// <summary>会话重置时清自动导出标志（与 _saveLogOperationTrail 等会话态一致）。</summary>
    static void ResetAutoDiagnosticExport()
    {
        System.Threading.Interlocked.Exchange(ref autoDiagnosticExported, 0);
    }

    public static bool ExportDiagnosticPackage(string outputDirectory, out string errorMessage)
    {
        if (!(_runtimeConfig?.LoggingEnabled ?? false))
        {
            errorMessage = "diagnostics_disabled";
            return false;
        }
        string gamePath = _runtimeGamePath;
        string coreProfile = _runtimeCoreProfile;
        bool useLazyLoading = MinorShift.Emuera.Config.UseLazyLoading && MinorShift.Emuera.Program.SupportsLazyLoading;
        return DiagnosticLogExporter.ExportDiagnosticPackage(_runtimeConfig, outputDirectory, gamePath, coreProfile, useLazyLoading, _inputReplay, out errorMessage);
    }

    static string NormalizeSourcePath(string file)
    {
        if (string.IsNullOrEmpty(file))
            return "<unknown>";
        string normalized = file.Replace('\\', '/');
        int scripts = normalized.LastIndexOf("/Scripts/", StringComparison.OrdinalIgnoreCase);
        if (scripts >= 0)
            return normalized.Substring(scripts + 1);
        return Path.GetFileName(normalized);
    }

    static EmueraLogCategory NormalizeLogCategory(EmueraLogCategory category, string message)
    {
        if (category != EmueraLogCategory.General || string.IsNullOrEmpty(message))
            return category;
        if (message.StartsWith("[IMG]", StringComparison.Ordinal) || message.Contains("[SpriteManager]", StringComparison.Ordinal))
            return EmueraLogCategory.Sprite;
        if (message.StartsWith("[AUDIO]", StringComparison.Ordinal))
            return EmueraLogCategory.Audio;
        if (message.StartsWith("[LOADSAVE]", StringComparison.Ordinal))
            return EmueraLogCategory.Save;
        if (message.StartsWith("[LOAD]", StringComparison.Ordinal) || message.StartsWith("[LOADTIME]", StringComparison.Ordinal))
            return EmueraLogCategory.Load;
        if (message.StartsWith("[PROC]", StringComparison.Ordinal))
            return EmueraLogCategory.Script;
        if (message.StartsWith("[CONFIG]", StringComparison.Ordinal))
            return EmueraLogCategory.Config;
        if (message.StartsWith("[FS]", StringComparison.Ordinal))
            return EmueraLogCategory.FileSystem;
        if (message.StartsWith("[UI Queue]", StringComparison.Ordinal))
            return EmueraLogCategory.UI;
        if (message.StartsWith(ScrollTracePrefix, StringComparison.Ordinal))
            return EmueraLogCategory.Script;
        return category;
    }

    static string ClipLogMessage(string value)
    {
        return ClipFlatText(value, MaxLogMessageChars, "...<truncated>");
    }
}
