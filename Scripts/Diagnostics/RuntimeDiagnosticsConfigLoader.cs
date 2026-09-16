using System;
using System.IO;
using System.Text;
using Godot;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 企业级说明：配置文件发现、读取和默认值回退。
    /// 按优先级读取 config.toml，解析失败时回退到默认配置并记录错误。
    /// 不处理触摸/输入业务，不直接调用 Godot UI（除文件存在检查外）。
    /// </summary>
    public static class RuntimeDiagnosticsConfigLoader
    {
        const string ConfigTomlFileName = "config.toml";

        public sealed class LoadResult
        {
            public readonly RuntimeDiagnosticsConfig Config;
            public readonly bool FileFound;
            public readonly string LoadedFrom;
            public readonly string ErrorMessage;

            public LoadResult(RuntimeDiagnosticsConfig config, bool fileFound, string loadedFrom, string errorMessage)
            {
                Config = config;
                FileFound = fileFound;
                LoadedFrom = loadedFrom ?? "";
                ErrorMessage = errorMessage ?? "";
            }
        }

        /// <summary>
        /// 按优先级加载配置：user:// -> res:// -> AppContext.BaseDirectory -> Directory.GetCurrentDirectory()
        /// 解析失败不抛异常，返回默认配置并附带错误信息。
        /// </summary>
        public static LoadResult Load()
        {
            string[] candidatePaths = new[]
            {
                $"user://{ConfigTomlFileName}",
                $"res://{ConfigTomlFileName}",
                Path.Combine(AppContext.BaseDirectory, ConfigTomlFileName),
                Path.Combine(Directory.GetCurrentDirectory(), ConfigTomlFileName)
            };

            foreach (string path in candidatePaths)
            {
                string text = TryReadText(path);
                if (text == null)
                    continue;

                var parseResult = RuntimeTomlParser.Parse(text);
                if (parseResult.HasErrors)
                {
                    string errorSummary = string.Join("; ", parseResult.Errors);
                    var fallback = RuntimeDiagnosticsConfig.CreateDefault();
                    fallback.MigrateLegacyUserDiagnosticsPaths();
                    fallback.ApplyQuickDebugPreset();
                    return new LoadResult(fallback, true, path, $"TOML_PARSE_FAIL: {errorSummary}");
                }

                var config = BuildConfig(parseResult.Sections);
                return new LoadResult(config, true, path, "");
            }

            var defaultConfig = RuntimeDiagnosticsConfig.CreateDefault();
            defaultConfig.MigrateLegacyUserDiagnosticsPaths();
            defaultConfig.ApplyQuickDebugPreset();
            return new LoadResult(defaultConfig, false, "", "CONFIG_NOT_FOUND: config.toml not found in any search path");
        }

        static string TryReadText(string path)
        {
            try
            {
                if (path.Contains("://", StringComparison.Ordinal))
                {
                    if (Godot.FileAccess.FileExists(path))
                    {
                        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
                        if (file != null)
                            return file.GetAsText();
                    }
                    return null;
                }

                if (File.Exists(path))
                    return File.ReadAllText(path, Encoding.UTF8);
            }
            catch { }
            return null;
        }

        static RuntimeDiagnosticsConfig BuildConfig(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> sections)
        {
            var cfg = RuntimeDiagnosticsConfig.CreateDefault();

            // root
            if (TryGetString(sections, "", "active_debug_model", out string v)) cfg.ActiveDebugModel = v;

            // migration
            // This is a startup-only canary. ReloadRuntimeDiagnosticsConfig may
            // refresh its stored value, but an active session never changes
            // startup mode until the next scene/application start.
            if (TryGetBool(sections, "migration", "session_isolation", out bool b)) cfg.MigrationSessionIsolationEnabled = b;

            // quick_debug
            if (TryGetBool(sections, "quick_debug", "enabled", out b)) cfg.QuickDebugEnabled = b;
            if (TryGetString(sections, "quick_debug", "preset", out v)) cfg.QuickDebugPreset = v;
            if (TryGetString(sections, "quick_debug", "language", out v)) cfg.QuickDebugLanguage = v;
            if (TryGetBool(sections, "quick_debug", "apk_safe", out b)) cfg.QuickDebugApkSafe = b;
            if (TryGetBool(sections, "quick_debug", "mirror_non_error_to_godot", out b)) cfg.QuickDebugMirrorNonErrorToGodot = b;
            if (TryGetBool(sections, "quick_debug", "runtime_panel", out b)) cfg.QuickDebugRuntimePanel = b;
            if (TryGetBool(sections, "quick_debug", "diagnostic_package", out b)) cfg.QuickDebugDiagnosticPackage = b;

            // quick_debug.modules
            if (TryGetBool(sections, "quick_debug.modules", "touch", out b)) cfg.QuickModules.Touch = b;
            if (TryGetBool(sections, "quick_debug.modules", "input", out b)) cfg.QuickModules.Input = b;
            if (TryGetBool(sections, "quick_debug.modules", "image", out b)) cfg.QuickModules.Image = b;
            if (TryGetBool(sections, "quick_debug.modules", "ui_layout", out b)) cfg.QuickModules.UiLayout = b;
            if (TryGetBool(sections, "quick_debug.modules", "dynamic_map", out b)) cfg.QuickModules.DynamicMap = b;
            if (TryGetBool(sections, "quick_debug.modules", "resource", out b)) cfg.QuickModules.Resource = b;
            if (TryGetBool(sections, "quick_debug.modules", "load_save", out b)) cfg.QuickModules.LoadSave = b;
            if (TryGetBool(sections, "quick_debug.modules", "android_storage", out b)) cfg.QuickModules.AndroidStorage = b;
            if (TryGetBool(sections, "quick_debug.modules", "performance_sampling", out b)) cfg.QuickModules.PerformanceSampling = b;
            if (TryGetBool(sections, "quick_debug.modules", "snapshot", out b)) cfg.QuickModules.Snapshot = b;
            if (TryGetBool(sections, "quick_debug.modules", "input_replay", out b)) cfg.QuickModules.InputReplay = b;

            // debug models
            ReadDebugModel(sections, "debug_model_zh_cn", cfg.DebugModelZhCn);
            ReadDebugModel(sections, "debug_model_jp", cfg.DebugModelJp);
            ReadDebugModel(sections, "debug_model_en", cfg.DebugModelEn);

            // logging
            if (TryGetBool(sections, "logging", "enabled", out b)) cfg.LoggingEnabled = b;
            if (TryGetString(sections, "logging", "level", out v)) cfg.LoggingLevel = v;
            // WS2 持续文件 sink：LoggingEnabled && FileSinkEnabled 双重门控；等级独立于全局 level。
            if (TryGetBool(sections, "logging", "file_sink", out b)) cfg.FileSinkEnabled = b;
            if (TryGetString(sections, "logging", "file_sink_level", out v)) cfg.FileSinkLevel = v;
            // 诊断面板显示：等价键复用 RuntimePanelEnabled（[logging] panel_visible 由 ApplyMinimalLoggingSwitches 在 minimal 展开后应用）。
            if (TryGetBool(sections, "logging", "mirror_non_error_to_godot", out b)) cfg.LoggingMirrorNonErrorToGodot = b;
            if (TryGetInt(sections, "logging", "diagnostic_ring_capacity", out int i)) cfg.LoggingDiagnosticRingCapacity = i;
            if (TryGetInt(sections, "logging", "max_message_chars", out i)) cfg.LoggingMaxMessageChars = i;
            if (TryGetString(sections, "logging", "export_directory", out v)) cfg.LoggingExportDirectory = v;
            if (TryGetInt(sections, "logging", "schema_version", out i)) cfg.LoggingSchemaVersion = i;
            if (TryGetString(sections, "logging", "session_id_format", out v)) cfg.LoggingSessionIdFormat = v;

            // logging.categories
            if (TryGetBool(sections, "logging.categories", "general", out b)) cfg.Categories.General = b;
            if (TryGetBool(sections, "logging.categories", "sprite", out b)) cfg.Categories.Sprite = b;
            if (TryGetBool(sections, "logging.categories", "audio", out b)) cfg.Categories.Audio = b;
            if (TryGetBool(sections, "logging.categories", "input", out b)) cfg.Categories.Input = b;
            if (TryGetBool(sections, "logging.categories", "script", out b)) cfg.Categories.Script = b;
            if (TryGetBool(sections, "logging.categories", "ui", out b)) cfg.Categories.UI = b;
            if (TryGetBool(sections, "logging.categories", "file_system", out b)) cfg.Categories.FileSystem = b;
            if (TryGetBool(sections, "logging.categories", "load", out b)) cfg.Categories.Load = b;
            if (TryGetBool(sections, "logging.categories", "save", out b)) cfg.Categories.Save = b;
            if (TryGetBool(sections, "logging.categories", "config", out b)) cfg.Categories.Config = b;
            if (TryGetBool(sections, "logging.categories", "performance", out b)) cfg.Categories.Performance = b;
            if (TryGetBool(sections, "logging.categories", "touch", out b)) cfg.Categories.Touch = b;
            if (TryGetBool(sections, "logging.categories", "statement_recognition", out b)) cfg.Categories.StatementRecognition = b;

            // correlation
            if (TryGetBool(sections, "logging.correlation", "enabled", out b)) cfg.CorrelationEnabled = b;
            if (TryGetBool(sections, "logging.correlation", "session_id", out b)) cfg.CorrelationSessionId = b;
            if (TryGetBool(sections, "logging.correlation", "input_id", out b)) cfg.CorrelationInputId = b;
            if (TryGetBool(sections, "logging.correlation", "render_batch_id", out b)) cfg.CorrelationRenderBatchId = b;
            if (TryGetBool(sections, "logging.correlation", "line_part_id", out b)) cfg.CorrelationLinePartId = b;
            if (TryGetBool(sections, "logging.correlation", "image_id", out b)) cfg.CorrelationImageId = b;

            // rate_limit
            if (TryGetBool(sections, "logging.rate_limit", "enabled", out b)) cfg.RateLimitEnabled = b;
            if (TryGetInt(sections, "logging.rate_limit", "default_per_second", out i)) cfg.RateLimitDefaultPerSecond = i;
            if (TryGetInt(sections, "logging.rate_limit", "default_per_frame", out i)) cfg.RateLimitDefaultPerFrame = i;
            if (TryGetInt(sections, "logging.rate_limit", "per_event_per_second", out i)) cfg.RateLimitPerEventPerSecond = i;
            if (TryGetBool(sections, "logging.rate_limit", "record_dropped_count", out b)) cfg.RateLimitRecordDroppedCount = b;

            // redaction
            if (TryGetBool(sections, "logging.redaction", "enabled", out b)) cfg.RedactionEnabled = b;
            if (TryGetBool(sections, "logging.redaction", "normalize_paths", out b)) cfg.RedactionNormalizePaths = b;
            if (TryGetString(sections, "logging.redaction", "path_mode", out v)) cfg.RedactionPathMode = v;
            if (TryGetInt(sections, "logging.redaction", "max_path_chars", out i)) cfg.RedactionMaxPathChars = i;
            if (TryGetInt(sections, "logging.redaction", "max_script_text_chars", out i)) cfg.RedactionMaxScriptTextChars = i;
            if (TryGetInt(sections, "logging.redaction", "max_user_text_chars", out i)) cfg.RedactionMaxUserTextChars = i;
            if (TryGetBool(sections, "logging.redaction", "replace_newlines", out b)) cfg.RedactionReplaceNewlines = b;
            if (TryGetBool(sections, "logging.redaction", "hash_sensitive_text", out b)) cfg.RedactionHashSensitiveText = b;

            // debug.touch
            if (TryGetBool(sections, "debug.touch", "enabled", out b)) cfg.TouchEnabled = b;
            if (TryGetBool(sections, "debug.touch", "pointer", out b)) cfg.TouchPointer = b;
            if (TryGetBool(sections, "debug.touch", "drag", out b)) cfg.TouchDrag = b;
            if (TryGetBool(sections, "debug.touch", "pinch", out b)) cfg.TouchPinch = b;
            if (TryGetBool(sections, "debug.touch", "inertia", out b)) cfg.TouchInertia = b;
            if (TryGetBool(sections, "debug.touch", "scroll", out b)) cfg.TouchScroll = b;
            if (TryGetInt(sections, "debug.touch", "drag_interval_ms", out i)) cfg.TouchDragIntervalMs = i;
            if (TryGetInt(sections, "debug.touch", "core_burst_line_count", out i)) cfg.TouchCoreBurstLineCount = i;

            // debug.statement_recognition
            if (TryGetBool(sections, "debug.statement_recognition", "enabled", out b)) cfg.StatementRecognitionEnabled = b;
            if (TryGetBool(sections, "debug.statement_recognition", "erb_load", out b)) cfg.StatementRecognitionErbLoad = b;
            if (TryGetBool(sections, "debug.statement_recognition", "logical_line", out b)) cfg.StatementRecognitionLogicalLine = b;
            if (TryGetBool(sections, "debug.statement_recognition", "expression", out b)) cfg.StatementRecognitionExpression = b;
            if (TryGetBool(sections, "debug.statement_recognition", "variable", out b)) cfg.StatementRecognitionVariable = b;
            if (TryGetBool(sections, "debug.statement_recognition", "function_call", out b)) cfg.StatementRecognitionFunctionCall = b;
            if (TryGetInt(sections, "debug.statement_recognition", "max_lines_per_burst", out i)) cfg.StatementRecognitionMaxLinesPerBurst = i;

            // debug.input
            if (TryGetBool(sections, "debug.input", "enabled", out b)) cfg.InputDebugEnabled = b;
            if (TryGetBool(sections, "debug.input", "submit", out b)) cfg.InputDebugSubmit = b;
            if (TryGetBool(sections, "debug.input", "consume", out b)) cfg.InputDebugConsume = b;
            if (TryGetBool(sections, "debug.input", "button", out b)) cfg.InputDebugButton = b;

            // debug.performance
            if (TryGetBool(sections, "debug.performance", "enabled", out b)) cfg.PerformanceEnabled = b;
            if (TryGetBool(sections, "debug.performance", "startup_timing", out b)) cfg.PerformanceStartupTiming = b;
            if (TryGetBool(sections, "debug.performance", "ui_queue", out b)) cfg.PerformanceUiQueue = b;
            if (TryGetBool(sections, "debug.performance", "resource_loading", out b)) cfg.PerformanceResourceLoading = b;

            // debug.load
            if (TryGetBool(sections, "debug.load", "enabled", out b)) cfg.LoadDebugEnabled = b;
            if (TryGetBool(sections, "debug.load", "erb", out b)) cfg.LoadDebugErb = b;
            if (TryGetBool(sections, "debug.load", "csv", out b)) cfg.LoadDebugCsv = b;
            if (TryGetBool(sections, "debug.load", "resources", out b)) cfg.LoadDebugResources = b;

            // debug.save
            if (TryGetBool(sections, "debug.save", "enabled", out b)) cfg.SaveDebugEnabled = b;
            if (TryGetBool(sections, "debug.save", "read", out b)) cfg.SaveDebugRead = b;
            if (TryGetBool(sections, "debug.save", "write", out b)) cfg.SaveDebugWrite = b;
            if (TryGetBool(sections, "debug.save", "compatibility_fallback", out b)) cfg.SaveDebugCompatibilityFallback = b;

            // debug.resource
            if (TryGetBool(sections, "debug.resource", "enabled", out b)) cfg.ResourceDebugEnabled = b;
            if (TryGetBool(sections, "debug.resource", "sprite", out b)) cfg.ResourceDebugSprite = b;
            if (TryGetBool(sections, "debug.resource", "audio", out b)) cfg.ResourceDebugAudio = b;
            if (TryGetBool(sections, "debug.resource", "sqlite", out b)) cfg.ResourceDebugSqlite = b;

            // debug.image
            if (TryGetBool(sections, "debug.image", "enabled", out b)) cfg.ImageDebugEnabled = b;
            if (TryGetBool(sections, "debug.image", "resolve", out b)) cfg.ImageDebugResolve = b;
            if (TryGetBool(sections, "debug.image", "texture", out b)) cfg.ImageDebugTexture = b;
            if (TryGetBool(sections, "debug.image", "render_rect", out b)) cfg.ImageDebugRenderRect = b;
            if (TryGetBool(sections, "debug.image", "cache", out b)) cfg.ImageDebugCache = b;
            if (TryGetBool(sections, "debug.image", "color_matrix", out b)) cfg.ImageDebugColorMatrix = b;
            if (TryGetBool(sections, "debug.image", "log_success", out b)) cfg.ImageDebugLogSuccess = b;
            if (TryGetInt(sections, "debug.image", "max_records_per_frame", out i)) cfg.ImageDebugMaxRecordsPerFrame = i;

            // debug.ui_layout
            if (TryGetBool(sections, "debug.ui_layout", "enabled", out b)) cfg.UiLayoutEnabled = b;
            if (TryGetBool(sections, "debug.ui_layout", "target_rect", out b)) cfg.UiLayoutTargetRect = b;
            if (TryGetBool(sections, "debug.ui_layout", "actual_rect", out b)) cfg.UiLayoutActualRect = b;
            if (TryGetBool(sections, "debug.ui_layout", "mismatch_only", out b)) cfg.UiLayoutMismatchOnly = b;
            if (TryGetInt(sections, "debug.ui_layout", "mismatch_threshold_px", out i)) cfg.UiLayoutMismatchThresholdPx = i;
            if (TryGetBool(sections, "debug.ui_layout", "text", out b)) cfg.UiLayoutText = b;
            if (TryGetBool(sections, "debug.ui_layout", "button", out b)) cfg.UiLayoutButton = b;
            if (TryGetBool(sections, "debug.ui_layout", "image", out b)) cfg.UiLayoutImage = b;
            if (TryGetBool(sections, "debug.ui_layout", "shape", out b)) cfg.UiLayoutShape = b;
            if (TryGetBool(sections, "debug.ui_layout", "html_div", out b)) cfg.UiLayoutHtmlDiv = b;
            if (TryGetBool(sections, "debug.ui_layout", "html_img", out b)) cfg.UiLayoutHtmlImg = b;
            if (TryGetBool(sections, "debug.ui_layout", "cbg", out b)) cfg.UiLayoutCbg = b;
            if (TryGetBool(sections, "debug.ui_layout", "include_text", out b)) cfg.UiLayoutIncludeText = b;
            if (TryGetInt(sections, "debug.ui_layout", "max_text_chars", out i)) cfg.UiLayoutMaxTextChars = i;
            if (TryGetInt(sections, "debug.ui_layout", "max_records_per_frame", out i)) cfg.UiLayoutMaxRecordsPerFrame = i;

            // debug.dynamic_map
            if (TryGetBool(sections, "debug.dynamic_map", "enabled", out b)) cfg.DynamicMapDebugEnabled = b;
            if (TryGetBool(sections, "debug.dynamic_map", "line_snapshot", out b)) cfg.DynamicMapLogLineSnapshot = b;
            if (TryGetBool(sections, "debug.dynamic_map", "scroll", out b)) cfg.DynamicMapLogScroll = b;
            if (TryGetBool(sections, "debug.dynamic_map", "buttons", out b)) cfg.DynamicMapLogButtons = b;
            if (TryGetBool(sections, "debug.dynamic_map", "only_bitmap_context", out b)) cfg.DynamicMapOnlyBitmapContext = b;
            if (TryGetInt(sections, "debug.dynamic_map", "max_lines", out i)) cfg.DynamicMapMaxLines = i;
            if (TryGetInt(sections, "debug.dynamic_map", "max_text_chars", out i)) cfg.DynamicMapMaxTextChars = i;
            if (TryGetInt(sections, "debug.dynamic_map", "context_window_ms", out i)) cfg.DynamicMapContextWindowMs = i;

            // debug.lifecycle
            if (TryGetBool(sections, "debug.lifecycle", "enabled", out b)) cfg.LifecycleEnabled = b;
            if (TryGetBool(sections, "debug.lifecycle", "android_pause_resume", out b)) cfg.LifecycleAndroidPauseResume = b;

            // debug.runtime_panel
            if (TryGetBool(sections, "debug.runtime_panel", "enabled", out b)) cfg.RuntimePanelEnabled = b;
            if (TryGetBool(sections, "debug.runtime_panel", "allow_runtime_toggle", out b)) cfg.RuntimePanelAllowRuntimeToggle = b;
            if (TryGetBool(sections, "debug.runtime_panel", "persist_changes", out b)) cfg.RuntimePanelPersistChanges = b;
            if (TryGetBool(sections, "debug.runtime_panel", "show_active_modules", out b)) cfg.RuntimePanelShowActiveModules = b;
            if (TryGetBool(sections, "debug.runtime_panel", "show_ring_buffer_stats", out b)) cfg.RuntimePanelShowRingBufferStats = b;

            // diagnostic_package
            if (TryGetBool(sections, "diagnostic_package", "enabled", out b)) cfg.DiagnosticPackageEnabled = b;
            if (TryGetBool(sections, "diagnostic_package", "include_log", out b)) cfg.DiagnosticPackageIncludeLog = b;
            if (TryGetBool(sections, "diagnostic_package", "include_config_snapshot", out b)) cfg.DiagnosticPackageIncludeConfigSnapshot = b;
            if (TryGetBool(sections, "diagnostic_package", "include_device_info", out b)) cfg.DiagnosticPackageIncludeDeviceInfo = b;
            if (TryGetBool(sections, "diagnostic_package", "include_screen_info", out b)) cfg.DiagnosticPackageIncludeScreenInfo = b;
            if (TryGetBool(sections, "diagnostic_package", "include_godot_info", out b)) cfg.DiagnosticPackageIncludeGodotInfo = b;
            if (TryGetBool(sections, "diagnostic_package", "include_apk_info", out b)) cfg.DiagnosticPackageIncludeApkInfo = b;
            if (TryGetBool(sections, "diagnostic_package", "include_game_path", out b)) cfg.DiagnosticPackageIncludeGamePath = b;
            if (TryGetBool(sections, "diagnostic_package", "include_error_summary", out b)) cfg.DiagnosticPackageIncludeErrorSummary = b;

            // diagnostic_summary
            if (TryGetBool(sections, "diagnostic_summary", "enabled", out b)) cfg.DiagnosticSummaryEnabled = b;
            if (TryGetInt(sections, "diagnostic_summary", "top_event_ids", out i)) cfg.DiagnosticSummaryTopEventIds = i;
            if (TryGetInt(sections, "diagnostic_summary", "top_ui_layout_mismatches", out i)) cfg.DiagnosticSummaryTopUiLayoutMismatches = i;
            if (TryGetInt(sections, "diagnostic_summary", "top_image_failures", out i)) cfg.DiagnosticSummaryTopImageFailures = i;

            // debug.ui_overlay
            if (TryGetBool(sections, "debug.ui_overlay", "enabled", out b)) cfg.UiOverlayEnabled = b;
            if (TryGetBool(sections, "debug.ui_overlay", "target_rect", out b)) cfg.UiOverlayTargetRect = b;
            if (TryGetBool(sections, "debug.ui_overlay", "actual_rect", out b)) cfg.UiOverlayActualRect = b;
            if (TryGetBool(sections, "debug.ui_overlay", "mismatch", out b)) cfg.UiOverlayMismatch = b;
            if (TryGetBool(sections, "debug.ui_overlay", "image_rect", out b)) cfg.UiOverlayImageRect = b;
            if (TryGetBool(sections, "debug.ui_overlay", "button_rect", out b)) cfg.UiOverlayButtonRect = b;
            if (TryGetInt(sections, "debug.ui_overlay", "max_drawn_rects", out i)) cfg.UiOverlayMaxDrawnRects = i;

            // debug.reference
            if (TryGetBool(sections, "debug.reference", "enabled", out b)) cfg.ReferenceEnabled = b;
            if (TryGetString(sections, "debug.reference", "reference_name", out v)) cfg.ReferenceName = v;
            if (TryGetBool(sections, "debug.reference", "expected_from_reference", out b)) cfg.ReferenceExpectedFromReference = b;
            if (TryGetBool(sections, "debug.reference", "reference_note", out b)) cfg.ReferenceNote = b;

            // diagnostic_breadcrumb
            if (TryGetBool(sections, "diagnostic_breadcrumb", "enabled", out b)) cfg.BreadcrumbEnabled = b;
            if (TryGetString(sections, "diagnostic_breadcrumb", "path", out v)) cfg.BreadcrumbPath = v;
            if (TryGetInt(sections, "diagnostic_breadcrumb", "max_bytes", out i)) cfg.BreadcrumbMaxBytes = i;
            if (TryGetBool(sections, "diagnostic_breadcrumb", "write_on_startup", out b)) cfg.BreadcrumbWriteOnStartup = b;
            if (TryGetBool(sections, "diagnostic_breadcrumb", "write_on_config_loaded", out b)) cfg.BreadcrumbWriteOnConfigLoaded = b;
            if (TryGetBool(sections, "diagnostic_breadcrumb", "write_on_game_path_selected", out b)) cfg.BreadcrumbWriteOnGamePathSelected = b;
            if (TryGetBool(sections, "diagnostic_breadcrumb", "write_on_severe_error", out b)) cfg.BreadcrumbWriteOnSevereError = b;
            if (TryGetBool(sections, "diagnostic_breadcrumb", "write_on_export", out b)) cfg.BreadcrumbWriteOnExport = b;
            if (TryGetBool(sections, "diagnostic_breadcrumb", "write_on_shutdown", out b)) cfg.BreadcrumbWriteOnShutdown = b;

            // debug.input_replay
            if (TryGetBool(sections, "debug.input_replay", "enabled", out b)) cfg.InputReplayEnabled = b;
            if (TryGetInt(sections, "debug.input_replay", "max_events", out i)) cfg.InputReplayMaxEvents = i;
            if (TryGetBool(sections, "debug.input_replay", "capture_touch", out b)) cfg.InputReplayCaptureTouch = b;
            if (TryGetBool(sections, "debug.input_replay", "capture_button", out b)) cfg.InputReplayCaptureButton = b;
            if (TryGetBool(sections, "debug.input_replay", "capture_keyboard", out b)) cfg.InputReplayCaptureKeyboard = b;
            if (TryGetBool(sections, "debug.input_replay", "capture_wait_state", out b)) cfg.InputReplayCaptureWaitState = b;
            if (TryGetBool(sections, "debug.input_replay", "capture_timing", out b)) cfg.InputReplayCaptureTiming = b;
            if (TryGetInt(sections, "debug.input_replay", "max_text_chars", out i)) cfg.InputReplayMaxTextChars = i;

            // diagnostic_retention
            if (TryGetBool(sections, "diagnostic_retention", "enabled", out b)) cfg.RetentionEnabled = b;
            if (TryGetString(sections, "diagnostic_retention", "directory", out v)) cfg.RetentionDirectory = v;
            if (TryGetInt(sections, "diagnostic_retention", "max_packages", out i)) cfg.RetentionMaxPackages = i;
            if (TryGetInt(sections, "diagnostic_retention", "max_total_mb", out i)) cfg.RetentionMaxTotalMb = i;
            if (TryGetBool(sections, "diagnostic_retention", "cleanup_on_startup", out b)) cfg.RetentionCleanupOnStartup = b;
            if (TryGetBool(sections, "diagnostic_retention", "cleanup_before_export", out b)) cfg.RetentionCleanupBeforeExport = b;
            if (TryGetBool(sections, "diagnostic_retention", "protect_current_session", out b)) cfg.RetentionProtectCurrentSession = b;

            // debug.android_storage
            if (TryGetBool(sections, "debug.android_storage", "enabled", out b)) cfg.AndroidStorageEnabled = b;
            if (TryGetBool(sections, "debug.android_storage", "log_permissions", out b)) cfg.AndroidStorageLogPermissions = b;
            if (TryGetBool(sections, "debug.android_storage", "log_game_scan", out b)) cfg.AndroidStorageLogGameScan = b;
            if (TryGetBool(sections, "debug.android_storage", "log_path_selection", out b)) cfg.AndroidStorageLogPathSelection = b;
            if (TryGetBool(sections, "debug.android_storage", "log_read_write_failures", out b)) cfg.AndroidStorageLogReadWriteFailures = b;
            if (TryGetBool(sections, "debug.android_storage", "log_scoped_storage", out b)) cfg.AndroidStorageLogScopedStorage = b;
            if (TryGetInt(sections, "debug.android_storage", "max_path_records", out i)) cfg.AndroidStorageMaxPathRecords = i;

            // debug.performance_sampling
            if (TryGetBool(sections, "debug.performance_sampling", "enabled", out b)) cfg.PerformanceSamplingEnabled = b;
            if (TryGetInt(sections, "debug.performance_sampling", "sample_interval_ms", out i)) cfg.PerformanceSamplingIntervalMs = i;
            if (TryGetBool(sections, "debug.performance_sampling", "include_fps", out b)) cfg.PerformanceSamplingIncludeFps = b;
            if (TryGetBool(sections, "debug.performance_sampling", "include_frame_ms", out b)) cfg.PerformanceSamplingIncludeFrameMs = b;
            if (TryGetBool(sections, "debug.performance_sampling", "include_ui_queue", out b)) cfg.PerformanceSamplingIncludeUiQueue = b;
            if (TryGetBool(sections, "debug.performance_sampling", "include_texture_queue", out b)) cfg.PerformanceSamplingIncludeTextureQueue = b;
            if (TryGetBool(sections, "debug.performance_sampling", "include_ring_buffer", out b)) cfg.PerformanceSamplingIncludeRingBuffer = b;
            if (TryGetBool(sections, "debug.performance_sampling", "include_dropped_count", out b)) cfg.PerformanceSamplingIncludeDroppedCount = b;
            if (TryGetBool(sections, "debug.performance_sampling", "include_memory", out b)) cfg.PerformanceSamplingIncludeMemory = b;

            // diagnostic_snapshot
            if (TryGetBool(sections, "diagnostic_snapshot", "enabled", out b)) cfg.SnapshotEnabled = b;
            if (TryGetBool(sections, "diagnostic_snapshot", "include_screenshot", out b)) cfg.SnapshotIncludeScreenshot = b;
            if (TryGetBool(sections, "diagnostic_snapshot", "include_layout_snapshot", out b)) cfg.SnapshotIncludeLayoutSnapshot = b;
            if (TryGetBool(sections, "diagnostic_snapshot", "include_visible_ui_rects", out b)) cfg.SnapshotIncludeVisibleUiRects = b;
            if (TryGetBool(sections, "diagnostic_snapshot", "include_image_rects", out b)) cfg.SnapshotIncludeImageRects = b;
            if (TryGetInt(sections, "diagnostic_snapshot", "max_nodes", out i)) cfg.SnapshotMaxNodes = i;
            if (TryGetInt(sections, "diagnostic_snapshot", "max_file_kb", out i)) cfg.SnapshotMaxFileKb = i;

            // 企业级说明：旧 user://diagnostics 路径在配置加载阶段迁移到 game://，业务导出阶段再解析为本次启动游戏目录。
            // 这样可兼容旧 user://config.toml，同时避免 Android 热路径或导出路径中继续依赖难找的 user 目录。
            cfg.MigrateLegacyUserDiagnosticsPaths();

            // 企业级说明：quick_debug 只在配置加载阶段展开，之后业务热路径不再感知 preset 字符串。
            cfg.ApplyQuickDebugPreset();
            ApplyMinimalLoggingSwitches(sections, cfg);
            return cfg;
        }

        static void ApplyMinimalLoggingSwitches(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> sections, RuntimeDiagnosticsConfig cfg)
        {
            // Why（错误 3 修复）：minimal 展开曾无条件 DisableAllDiagnostics 后按固定组合重建，
            // 会把完整格式配置（诊断面板/启动器保存，含专家 section）里已读取的专家细项全部吞掉。
            // 完整格式文件中 [logging] 的精简键只是同一状态的冗余投影（写入器同时写出两份），
            // 专家键已在 BuildConfig 全量读取，这里直接跳过破坏性展开。
            // 旧版精简格式（只有 [logging]，如仓库 res://config.toml）保持原语义：
            // DisableAllDiagnostics 后按精简键重建，缺省模块关闭，避免误开 APK 热路径诊断。
            if (HasExpertDiagnosticsSections(sections))
            {
                // 完整格式：专家键已全量读取，但 [logging] panel_visible / runtime_panel
                // 等价键只在本方法应用（BuildConfig 不读它们），跳过展开前必须补上，
                // 否则面板保存的 panel_visible 重启后丢失。
                if (TryGetBool(sections, "logging", "panel_visible", out bool panelVisibleFull))
                    cfg.RuntimePanelEnabled = panelVisibleFull;
                else if (TryGetBool(sections, "logging", "runtime_panel", out bool runtimePanelFull))
                    cfg.RuntimePanelEnabled = runtimePanelFull;
                return;
            }

            // 精简配置只认一个总开关和少量模块开关；缺省即关闭，避免旧 user://config.toml 误把 APK 热路径诊断打开。
            bool enabled = GetMinimalBool(sections, "enabled", false);
            bool mirrorToGodot = GetMinimalBool(sections, "mirror_to_godot",
                GetMinimalBool(sections, "mirror_non_error_to_godot", false));
            // WS2：显式 [logging] level 参与 minimal 展开，避免 enabled=true 强制回退 debug。
            string levelOverride = null;
            if (TryGetString(sections, "logging", "level", out string explicitLevel))
                levelOverride = explicitLevel;
            // WS2：面板显示等价键 [logging] panel_visible（默认 true）优先于旧 minimal runtime_panel（默认 false），
            // 两者都映射到 RuntimePanelEnabled，避免 minimal 展开把用户设置吞掉。
            bool panelVisible = GetMinimalBool(sections, "panel_visible",
                GetMinimalBool(sections, "runtime_panel", false));
            cfg.ApplyMinimalLoggingConfig(
                enabled,
                GetMinimalBool(sections, "touch", false),
                GetMinimalBool(sections, "input", false),
                GetMinimalBool(sections, "image", false),
                GetMinimalBool(sections, "ui_layout", false),
                GetMinimalBool(sections, "dynamic_map", false),
                GetMinimalBool(sections, "resource", false),
                GetMinimalBool(sections, "load_save", false),
                GetMinimalBool(sections, "android_storage", false),
                GetMinimalBool(sections, "performance", GetMinimalBool(sections, "performance_sampling", false)),
                GetMinimalBool(sections, "statement_recognition", false),
                panelVisible,
                GetMinimalBool(sections, "input_replay", false),
                GetMinimalBool(sections, "diagnostic_package", false),
                mirrorToGodot,
                levelOverride);
            // 修复：minimal 展开内部 DisableAllDiagnostics 会清空 Categories 掩码，
            // 若配置显式给出 [logging.categories] 键（设置页/面板保存），展开后按文件值恢复；
            // 未给出的键保持 minimal 默认，避免分类过滤静默失效（文件 sink 只剩 error 记录）。
            RestoreExplicitCategories(sections, cfg);
        }

        /// <summary>
        /// 是否存在专家诊断 section（完整格式配置的标志）。Why：诊断面板/启动器保存的是
        /// 全量键值（RuntimeDiagnosticsConfigWriter），这类文件必须按字面读取，不能被
        /// minimal 展开重置；旧精简格式（仓库 res://config.toml）没有这些 section。
        /// </summary>
        static bool HasExpertDiagnosticsSections(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> sections)
        {
            return sections.ContainsKey("quick_debug")
                || sections.ContainsKey("debug_model_zh_cn")
                || sections.ContainsKey("debug_model_jp")
                || sections.ContainsKey("debug_model_en")
                || sections.ContainsKey("logging.rate_limit")
                || sections.ContainsKey("debug.touch")
                || sections.ContainsKey("debug.image");
        }

        static void RestoreExplicitCategories(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> sections, RuntimeDiagnosticsConfig cfg)
        {
            if (!sections.TryGetValue("logging.categories", out _))
                return;
            if (TryGetBool(sections, "logging.categories", "general", out bool b)) cfg.Categories.General = b;
            if (TryGetBool(sections, "logging.categories", "sprite", out b)) cfg.Categories.Sprite = b;
            if (TryGetBool(sections, "logging.categories", "audio", out b)) cfg.Categories.Audio = b;
            if (TryGetBool(sections, "logging.categories", "input", out b)) cfg.Categories.Input = b;
            if (TryGetBool(sections, "logging.categories", "script", out b)) cfg.Categories.Script = b;
            if (TryGetBool(sections, "logging.categories", "ui", out b)) cfg.Categories.UI = b;
            if (TryGetBool(sections, "logging.categories", "file_system", out b)) cfg.Categories.FileSystem = b;
            if (TryGetBool(sections, "logging.categories", "load", out b)) cfg.Categories.Load = b;
            if (TryGetBool(sections, "logging.categories", "save", out b)) cfg.Categories.Save = b;
            if (TryGetBool(sections, "logging.categories", "config", out b)) cfg.Categories.Config = b;
            // Why：minimal 展开曾漏恢复这两类，导致文件里显式开启的 touch/statement_recognition
            // 分类在热重载/重启后被静默清空（文件 sink 只剩 error 记录的误判来源之一）。
            if (TryGetBool(sections, "logging.categories", "performance", out b)) cfg.Categories.Performance = b;
            if (TryGetBool(sections, "logging.categories", "touch", out b)) cfg.Categories.Touch = b;
            if (TryGetBool(sections, "logging.categories", "statement_recognition", out b)) cfg.Categories.StatementRecognition = b;
        }

        static bool GetMinimalBool(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> sections, string key, bool fallback)
        {
            if (TryGetBool(sections, "logging", key, out bool value))
                return value;
            if (TryGetBool(sections, "", key, out value))
                return value;
            return fallback;
        }

        static void ReadDebugModel(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> sections, string sectionName, RuntimeDiagnosticsConfig.DebugModelProfile model)
        {
            if (TryGetBool(sections, sectionName, "enabled", out bool b)) model.Enabled = b;
            if (TryGetString(sections, sectionName, "language", out string v)) model.Language = v;
            if (TryGetString(sections, sectionName, "log_level", out v)) model.LogLevel = v;
            if (TryGetBool(sections, sectionName, "mirror_to_godot", out b)) model.MirrorToGodot = b;
            if (TryGetBool(sections, sectionName, "scroll_trace", out b)) model.ScrollTrace = b;
        }

        static bool TryGetString(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> sections, string section, string key, out string value)
        {
            value = null;
            if (!sections.TryGetValue(section, out var dict))
                return false;
            return dict.TryGetValue(key, out value);
        }

        static bool TryGetBool(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> sections, string section, string key, out bool value)
        {
            value = false;
            if (!TryGetString(sections, section, key, out string s))
                return false;
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }
            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }
            return false;
        }

        static bool TryGetInt(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> sections, string section, string key, out int value)
        {
            value = 0;
            if (!TryGetString(sections, section, key, out string s))
                return false;
            return int.TryParse(s, out value);
        }
    }
}
