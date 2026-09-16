using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 运行时诊断配置写入器。
    /// Why（错误 1 修复）：旧版只写出约 15 个精简键，面板暴露的 200+ 专家项保存后
    /// 重启即被默认值吞掉；且 user://config.toml 一旦生成就会屏蔽 res://config.toml，
    /// 不属于诊断系统的段（如 [agent.llm]）也会被清空。
    /// What/How：BuildToml 与 RuntimeDiagnosticsConfigLoader.BuildConfig 的键一一对应地
    /// 全量写出；SaveUserConfig 保存前解析现有文件，把非诊断管理的 section 原文
    /// （含注释）原样拼回，保证第三方/未来新增段不被诊断面板破坏。
    /// </summary>
    public static class RuntimeDiagnosticsConfigWriter
    {
        public const string UserConfigPath = "user://config.toml";

        /// <summary>诊断系统管理的全部 section 名（与 Loader.BuildConfig 读取范围一致）。</summary>
        static readonly HashSet<string> DiagnosticsSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "",
            "migration",
            "quick_debug",
            "quick_debug.modules",
            "debug_model_zh_cn",
            "debug_model_jp",
            "debug_model_en",
            "logging",
            "logging.categories",
            "logging.correlation",
            "logging.rate_limit",
            "logging.redaction",
            "debug.touch",
            "debug.statement_recognition",
            "debug.input",
            "debug.performance",
            "debug.load",
            "debug.save",
            "debug.resource",
            "debug.image",
            "debug.ui_layout",
            "debug.dynamic_map",
            "debug.lifecycle",
            "debug.runtime_panel",
            "diagnostic_package",
            "diagnostic_summary",
            "debug.ui_overlay",
            "debug.reference",
            "diagnostic_breadcrumb",
            "debug.input_replay",
            "diagnostic_retention",
            "debug.android_storage",
            "debug.performance_sampling",
            "diagnostic_snapshot",
        };

        public static bool SaveUserConfig(RuntimeDiagnosticsConfig config, out string savedPath, out string errorMessage)
        {
            savedPath = UserConfigPath;
            errorMessage = "";
            try
            {
                string text = BuildTomlWithForeignSectionsPreserved(config ?? RuntimeDiagnosticsConfig.CreateDefault());
                using var file = Godot.FileAccess.Open(UserConfigPath, Godot.FileAccess.ModeFlags.Write);
                if (file == null)
                {
                    errorMessage = Godot.FileAccess.GetOpenError().ToString();
                    return false;
                }
                file.StoreString(text);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 生成完整诊断配置，并把现有文件中外来 section（诊断不管理，如 [agent.llm]）的
        /// 原始文本块（含注释与空行）拼接到末尾。Why：user://config.toml 存在后优先级高于
        /// res://config.toml，整体重写会把用户手工添加的非诊断配置静默清空。
        /// </summary>
        static string BuildTomlWithForeignSectionsPreserved(RuntimeDiagnosticsConfig config)
        {
            return BuildTomlWithForeignSections(config, TryReadExistingText());
        }

        /// <summary>纯文本组合逻辑（internal 供回归测试直接验证，不依赖 Godot I/O）。</summary>
        internal static string BuildTomlWithForeignSections(RuntimeDiagnosticsConfig config, string existingText)
        {
            string diagnosticsToml = BuildToml(config);
            string foreign = ExtractForeignSections(existingText);
            if (string.IsNullOrEmpty(foreign))
                return diagnosticsToml;
            // Godot.Environment 与 System.Environment 同名歧义：本文件 using Godot，需显式限定。
            return diagnosticsToml + System.Environment.NewLine + foreign.TrimEnd() + System.Environment.NewLine;
        }

        static string TryReadExistingText()
        {
            try
            {
                if (!Godot.FileAccess.FileExists(UserConfigPath))
                    return null;
                using var file = Godot.FileAccess.Open(UserConfigPath, Godot.FileAccess.ModeFlags.Read);
                return file?.GetAsText();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 逐行扫描现有配置，收集不属于 DiagnosticsSections 的 section 原文块。
        /// section 头之前的注释/空行跟随其后的 section 归属；root("") 视为诊断管理
        /// （其唯一合法键 active_debug_model 由本写入器负责）。
        /// internal 供回归测试直接验证（纯文本处理，不依赖 Godot I/O）。
        /// </summary>
        internal static string ExtractForeignSections(string existingText)
        {
            if (string.IsNullOrWhiteSpace(existingText))
                return "";

            var foreignBlocks = new List<string>();
            var currentBlock = new StringBuilder();
            string currentSection = "";
            bool currentSectionIsForeign = false;

            void FlushBlock()
            {
                if (currentSectionIsForeign && currentBlock.Length > 0)
                    foreignBlocks.Add(currentBlock.ToString().TrimEnd());
                currentBlock.Clear();
            }

            string[] lines = existingText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string rawLine in lines)
            {
                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    FlushBlock();
                    currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    currentSectionIsForeign = !DiagnosticsSections.Contains(currentSection);
                    currentBlock.AppendLine(rawLine);
                    continue;
                }
                // 空/注释行先挂到当前块，归属由下一个 section 头决定；
                // 键值行落在 root 时归属诊断管理（丢弃），落在外来 section 时保留。
                if (currentSectionIsForeign || trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                    currentBlock.AppendLine(rawLine);
            }
            FlushBlock();

            return foreignBlocks.Count == 0 ? "" : string.Join(System.Environment.NewLine, foreignBlocks);
        }

        public static string BuildToml(RuntimeDiagnosticsConfig c)
        {
            c ??= RuntimeDiagnosticsConfig.CreateDefault();
            var sb = new StringBuilder(4096);
            sb.AppendLine("# gEmuera 运行时日志/诊断配置（完整格式，由诊断面板/启动器生成）");
            sb.AppendLine("# enabled=false 表示日志/诊断系统完全关闭。");
            sb.AppendLine("# 本文件之外的手工段落（如 [agent.llm]）在保存时会被原样保留。");
            sb.AppendLine("# migration.session_isolation 仅在下次启动时生效；默认 false 保持旧版启动链。");
            sb.AppendLine("[migration]");
            sb.AppendLine("session_isolation = " + Bool(c.MigrationSessionIsolationEnabled));
            sb.AppendLine();
            sb.AppendLine("[quick_debug]");
            sb.AppendLine("enabled = " + Bool(c.QuickDebugEnabled));
            sb.AppendLine("preset = \"" + Escape(c.QuickDebugPreset) + "\"");
            sb.AppendLine("language = \"" + Escape(c.QuickDebugLanguage) + "\"");
            sb.AppendLine("apk_safe = " + Bool(c.QuickDebugApkSafe));
            sb.AppendLine("mirror_non_error_to_godot = " + Bool(c.QuickDebugMirrorNonErrorToGodot));
            sb.AppendLine("runtime_panel = " + Bool(c.QuickDebugRuntimePanel));
            sb.AppendLine("diagnostic_package = " + Bool(c.QuickDebugDiagnosticPackage));
            sb.AppendLine();
            sb.AppendLine("[quick_debug.modules]");
            sb.AppendLine("touch = " + Bool(c.QuickModules.Touch));
            sb.AppendLine("input = " + Bool(c.QuickModules.Input));
            sb.AppendLine("image = " + Bool(c.QuickModules.Image));
            sb.AppendLine("ui_layout = " + Bool(c.QuickModules.UiLayout));
            sb.AppendLine("dynamic_map = " + Bool(c.QuickModules.DynamicMap));
            sb.AppendLine("resource = " + Bool(c.QuickModules.Resource));
            sb.AppendLine("load_save = " + Bool(c.QuickModules.LoadSave));
            sb.AppendLine("android_storage = " + Bool(c.QuickModules.AndroidStorage));
            sb.AppendLine("performance_sampling = " + Bool(c.QuickModules.PerformanceSampling));
            sb.AppendLine("snapshot = " + Bool(c.QuickModules.Snapshot));
            sb.AppendLine("input_replay = " + Bool(c.QuickModules.InputReplay));
            AppendDebugModel(sb, "debug_model_zh_cn", c.DebugModelZhCn);
            AppendDebugModel(sb, "debug_model_jp", c.DebugModelJp);
            AppendDebugModel(sb, "debug_model_en", c.DebugModelEn);
            sb.AppendLine();
            sb.AppendLine("[logging]");
            sb.AppendLine("enabled = " + Bool(c.LoggingEnabled));
            sb.AppendLine("level = \"" + Escape(c.LoggingLevel) + "\"");
            sb.AppendLine("file_sink = " + Bool(c.FileSinkEnabled));
            sb.AppendLine("file_sink_level = \"" + Escape(c.FileSinkLevel) + "\"");
            sb.AppendLine("panel_visible = " + Bool(c.RuntimePanelEnabled));
            sb.AppendLine("mirror_non_error_to_godot = " + Bool(c.LoggingMirrorNonErrorToGodot));
            sb.AppendLine("diagnostic_ring_capacity = " + c.LoggingDiagnosticRingCapacity);
            sb.AppendLine("max_message_chars = " + c.LoggingMaxMessageChars);
            sb.AppendLine("export_directory = \"" + Escape(c.LoggingExportDirectory) + "\"");
            sb.AppendLine("schema_version = " + c.LoggingSchemaVersion);
            sb.AppendLine("session_id_format = \"" + Escape(c.LoggingSessionIdFormat) + "\"");
            // 分类掩码必须持久化，否则设置页的“日志分类”勾选重启/热重载后全部还原为关，
            // minimal 展开（DisableAllDiagnostics）也会把掩码清空（见 loader 的恢复逻辑）。
            sb.AppendLine();
            sb.AppendLine("[logging.categories]");
            sb.AppendLine("general = " + Bool(c.Categories.General));
            sb.AppendLine("sprite = " + Bool(c.Categories.Sprite));
            sb.AppendLine("audio = " + Bool(c.Categories.Audio));
            sb.AppendLine("input = " + Bool(c.Categories.Input));
            sb.AppendLine("script = " + Bool(c.Categories.Script));
            sb.AppendLine("ui = " + Bool(c.Categories.UI));
            sb.AppendLine("file_system = " + Bool(c.Categories.FileSystem));
            sb.AppendLine("load = " + Bool(c.Categories.Load));
            sb.AppendLine("save = " + Bool(c.Categories.Save));
            sb.AppendLine("config = " + Bool(c.Categories.Config));
            sb.AppendLine("performance = " + Bool(c.Categories.Performance));
            sb.AppendLine("touch = " + Bool(c.Categories.Touch));
            sb.AppendLine("statement_recognition = " + Bool(c.Categories.StatementRecognition));
            sb.AppendLine();
            sb.AppendLine("[logging.correlation]");
            sb.AppendLine("enabled = " + Bool(c.CorrelationEnabled));
            sb.AppendLine("session_id = " + Bool(c.CorrelationSessionId));
            sb.AppendLine("input_id = " + Bool(c.CorrelationInputId));
            sb.AppendLine("render_batch_id = " + Bool(c.CorrelationRenderBatchId));
            sb.AppendLine("line_part_id = " + Bool(c.CorrelationLinePartId));
            sb.AppendLine("image_id = " + Bool(c.CorrelationImageId));
            sb.AppendLine();
            sb.AppendLine("[logging.rate_limit]");
            sb.AppendLine("enabled = " + Bool(c.RateLimitEnabled));
            sb.AppendLine("default_per_second = " + c.RateLimitDefaultPerSecond);
            sb.AppendLine("default_per_frame = " + c.RateLimitDefaultPerFrame);
            sb.AppendLine("per_event_per_second = " + c.RateLimitPerEventPerSecond);
            sb.AppendLine("record_dropped_count = " + Bool(c.RateLimitRecordDroppedCount));
            sb.AppendLine();
            sb.AppendLine("[logging.redaction]");
            sb.AppendLine("enabled = " + Bool(c.RedactionEnabled));
            sb.AppendLine("normalize_paths = " + Bool(c.RedactionNormalizePaths));
            sb.AppendLine("path_mode = \"" + Escape(c.RedactionPathMode) + "\"");
            sb.AppendLine("max_path_chars = " + c.RedactionMaxPathChars);
            sb.AppendLine("max_script_text_chars = " + c.RedactionMaxScriptTextChars);
            sb.AppendLine("max_user_text_chars = " + c.RedactionMaxUserTextChars);
            sb.AppendLine("replace_newlines = " + Bool(c.RedactionReplaceNewlines));
            sb.AppendLine("hash_sensitive_text = " + Bool(c.RedactionHashSensitiveText));
            sb.AppendLine();
            sb.AppendLine("[debug.touch]");
            sb.AppendLine("enabled = " + Bool(c.TouchEnabled));
            sb.AppendLine("pointer = " + Bool(c.TouchPointer));
            sb.AppendLine("drag = " + Bool(c.TouchDrag));
            sb.AppendLine("pinch = " + Bool(c.TouchPinch));
            sb.AppendLine("inertia = " + Bool(c.TouchInertia));
            sb.AppendLine("scroll = " + Bool(c.TouchScroll));
            sb.AppendLine("drag_interval_ms = " + c.TouchDragIntervalMs);
            sb.AppendLine("core_burst_line_count = " + c.TouchCoreBurstLineCount);
            sb.AppendLine();
            sb.AppendLine("[debug.statement_recognition]");
            sb.AppendLine("enabled = " + Bool(c.StatementRecognitionEnabled));
            sb.AppendLine("erb_load = " + Bool(c.StatementRecognitionErbLoad));
            sb.AppendLine("logical_line = " + Bool(c.StatementRecognitionLogicalLine));
            sb.AppendLine("expression = " + Bool(c.StatementRecognitionExpression));
            sb.AppendLine("variable = " + Bool(c.StatementRecognitionVariable));
            sb.AppendLine("function_call = " + Bool(c.StatementRecognitionFunctionCall));
            sb.AppendLine("max_lines_per_burst = " + c.StatementRecognitionMaxLinesPerBurst);
            sb.AppendLine();
            sb.AppendLine("[debug.input]");
            sb.AppendLine("enabled = " + Bool(c.InputDebugEnabled));
            sb.AppendLine("submit = " + Bool(c.InputDebugSubmit));
            sb.AppendLine("consume = " + Bool(c.InputDebugConsume));
            sb.AppendLine("button = " + Bool(c.InputDebugButton));
            sb.AppendLine();
            sb.AppendLine("[debug.performance]");
            sb.AppendLine("enabled = " + Bool(c.PerformanceEnabled));
            sb.AppendLine("startup_timing = " + Bool(c.PerformanceStartupTiming));
            sb.AppendLine("ui_queue = " + Bool(c.PerformanceUiQueue));
            sb.AppendLine("resource_loading = " + Bool(c.PerformanceResourceLoading));
            sb.AppendLine();
            sb.AppendLine("[debug.load]");
            sb.AppendLine("enabled = " + Bool(c.LoadDebugEnabled));
            sb.AppendLine("erb = " + Bool(c.LoadDebugErb));
            sb.AppendLine("csv = " + Bool(c.LoadDebugCsv));
            sb.AppendLine("resources = " + Bool(c.LoadDebugResources));
            sb.AppendLine();
            sb.AppendLine("[debug.save]");
            sb.AppendLine("enabled = " + Bool(c.SaveDebugEnabled));
            sb.AppendLine("read = " + Bool(c.SaveDebugRead));
            sb.AppendLine("write = " + Bool(c.SaveDebugWrite));
            sb.AppendLine("compatibility_fallback = " + Bool(c.SaveDebugCompatibilityFallback));
            sb.AppendLine();
            sb.AppendLine("[debug.resource]");
            sb.AppendLine("enabled = " + Bool(c.ResourceDebugEnabled));
            sb.AppendLine("sprite = " + Bool(c.ResourceDebugSprite));
            sb.AppendLine("audio = " + Bool(c.ResourceDebugAudio));
            sb.AppendLine("sqlite = " + Bool(c.ResourceDebugSqlite));
            sb.AppendLine();
            sb.AppendLine("[debug.image]");
            sb.AppendLine("enabled = " + Bool(c.ImageDebugEnabled));
            sb.AppendLine("resolve = " + Bool(c.ImageDebugResolve));
            sb.AppendLine("texture = " + Bool(c.ImageDebugTexture));
            sb.AppendLine("render_rect = " + Bool(c.ImageDebugRenderRect));
            sb.AppendLine("cache = " + Bool(c.ImageDebugCache));
            sb.AppendLine("color_matrix = " + Bool(c.ImageDebugColorMatrix));
            sb.AppendLine("log_success = " + Bool(c.ImageDebugLogSuccess));
            sb.AppendLine("max_records_per_frame = " + c.ImageDebugMaxRecordsPerFrame);
            sb.AppendLine();
            sb.AppendLine("[debug.ui_layout]");
            sb.AppendLine("enabled = " + Bool(c.UiLayoutEnabled));
            sb.AppendLine("target_rect = " + Bool(c.UiLayoutTargetRect));
            sb.AppendLine("actual_rect = " + Bool(c.UiLayoutActualRect));
            sb.AppendLine("mismatch_only = " + Bool(c.UiLayoutMismatchOnly));
            sb.AppendLine("mismatch_threshold_px = " + c.UiLayoutMismatchThresholdPx);
            sb.AppendLine("text = " + Bool(c.UiLayoutText));
            sb.AppendLine("button = " + Bool(c.UiLayoutButton));
            sb.AppendLine("image = " + Bool(c.UiLayoutImage));
            sb.AppendLine("shape = " + Bool(c.UiLayoutShape));
            sb.AppendLine("html_div = " + Bool(c.UiLayoutHtmlDiv));
            sb.AppendLine("html_img = " + Bool(c.UiLayoutHtmlImg));
            sb.AppendLine("cbg = " + Bool(c.UiLayoutCbg));
            sb.AppendLine("include_text = " + Bool(c.UiLayoutIncludeText));
            sb.AppendLine("max_text_chars = " + c.UiLayoutMaxTextChars);
            sb.AppendLine("max_records_per_frame = " + c.UiLayoutMaxRecordsPerFrame);
            sb.AppendLine();
            sb.AppendLine("[debug.dynamic_map]");
            sb.AppendLine("enabled = " + Bool(c.DynamicMapDebugEnabled));
            sb.AppendLine("line_snapshot = " + Bool(c.DynamicMapLogLineSnapshot));
            sb.AppendLine("scroll = " + Bool(c.DynamicMapLogScroll));
            sb.AppendLine("buttons = " + Bool(c.DynamicMapLogButtons));
            sb.AppendLine("only_bitmap_context = " + Bool(c.DynamicMapOnlyBitmapContext));
            sb.AppendLine("max_lines = " + c.DynamicMapMaxLines);
            sb.AppendLine("max_text_chars = " + c.DynamicMapMaxTextChars);
            sb.AppendLine("context_window_ms = " + c.DynamicMapContextWindowMs);
            sb.AppendLine();
            sb.AppendLine("[debug.lifecycle]");
            sb.AppendLine("enabled = " + Bool(c.LifecycleEnabled));
            sb.AppendLine("android_pause_resume = " + Bool(c.LifecycleAndroidPauseResume));
            sb.AppendLine();
            sb.AppendLine("[debug.runtime_panel]");
            sb.AppendLine("enabled = " + Bool(c.RuntimePanelEnabled));
            sb.AppendLine("allow_runtime_toggle = " + Bool(c.RuntimePanelAllowRuntimeToggle));
            sb.AppendLine("persist_changes = " + Bool(c.RuntimePanelPersistChanges));
            sb.AppendLine("show_active_modules = " + Bool(c.RuntimePanelShowActiveModules));
            sb.AppendLine("show_ring_buffer_stats = " + Bool(c.RuntimePanelShowRingBufferStats));
            sb.AppendLine();
            sb.AppendLine("[diagnostic_package]");
            sb.AppendLine("enabled = " + Bool(c.DiagnosticPackageEnabled));
            sb.AppendLine("include_log = " + Bool(c.DiagnosticPackageIncludeLog));
            sb.AppendLine("include_config_snapshot = " + Bool(c.DiagnosticPackageIncludeConfigSnapshot));
            sb.AppendLine("include_device_info = " + Bool(c.DiagnosticPackageIncludeDeviceInfo));
            sb.AppendLine("include_screen_info = " + Bool(c.DiagnosticPackageIncludeScreenInfo));
            sb.AppendLine("include_godot_info = " + Bool(c.DiagnosticPackageIncludeGodotInfo));
            sb.AppendLine("include_apk_info = " + Bool(c.DiagnosticPackageIncludeApkInfo));
            sb.AppendLine("include_game_path = " + Bool(c.DiagnosticPackageIncludeGamePath));
            sb.AppendLine("include_error_summary = " + Bool(c.DiagnosticPackageIncludeErrorSummary));
            sb.AppendLine();
            sb.AppendLine("[diagnostic_summary]");
            sb.AppendLine("enabled = " + Bool(c.DiagnosticSummaryEnabled));
            sb.AppendLine("top_event_ids = " + c.DiagnosticSummaryTopEventIds);
            sb.AppendLine("top_ui_layout_mismatches = " + c.DiagnosticSummaryTopUiLayoutMismatches);
            sb.AppendLine("top_image_failures = " + c.DiagnosticSummaryTopImageFailures);
            sb.AppendLine();
            sb.AppendLine("[debug.ui_overlay]");
            sb.AppendLine("enabled = " + Bool(c.UiOverlayEnabled));
            sb.AppendLine("target_rect = " + Bool(c.UiOverlayTargetRect));
            sb.AppendLine("actual_rect = " + Bool(c.UiOverlayActualRect));
            sb.AppendLine("mismatch = " + Bool(c.UiOverlayMismatch));
            sb.AppendLine("image_rect = " + Bool(c.UiOverlayImageRect));
            sb.AppendLine("button_rect = " + Bool(c.UiOverlayButtonRect));
            sb.AppendLine("max_drawn_rects = " + c.UiOverlayMaxDrawnRects);
            sb.AppendLine();
            sb.AppendLine("[debug.reference]");
            sb.AppendLine("enabled = " + Bool(c.ReferenceEnabled));
            sb.AppendLine("reference_name = \"" + Escape(c.ReferenceName) + "\"");
            sb.AppendLine("expected_from_reference = " + Bool(c.ReferenceExpectedFromReference));
            sb.AppendLine("reference_note = " + Bool(c.ReferenceNote));
            sb.AppendLine();
            sb.AppendLine("[diagnostic_breadcrumb]");
            sb.AppendLine("enabled = " + Bool(c.BreadcrumbEnabled));
            sb.AppendLine("path = \"" + Escape(c.BreadcrumbPath) + "\"");
            sb.AppendLine("max_bytes = " + c.BreadcrumbMaxBytes);
            sb.AppendLine("write_on_startup = " + Bool(c.BreadcrumbWriteOnStartup));
            sb.AppendLine("write_on_config_loaded = " + Bool(c.BreadcrumbWriteOnConfigLoaded));
            sb.AppendLine("write_on_game_path_selected = " + Bool(c.BreadcrumbWriteOnGamePathSelected));
            sb.AppendLine("write_on_severe_error = " + Bool(c.BreadcrumbWriteOnSevereError));
            sb.AppendLine("write_on_export = " + Bool(c.BreadcrumbWriteOnExport));
            sb.AppendLine("write_on_shutdown = " + Bool(c.BreadcrumbWriteOnShutdown));
            sb.AppendLine();
            sb.AppendLine("[debug.input_replay]");
            sb.AppendLine("enabled = " + Bool(c.InputReplayEnabled));
            sb.AppendLine("max_events = " + c.InputReplayMaxEvents);
            sb.AppendLine("capture_touch = " + Bool(c.InputReplayCaptureTouch));
            sb.AppendLine("capture_button = " + Bool(c.InputReplayCaptureButton));
            sb.AppendLine("capture_keyboard = " + Bool(c.InputReplayCaptureKeyboard));
            sb.AppendLine("capture_wait_state = " + Bool(c.InputReplayCaptureWaitState));
            sb.AppendLine("capture_timing = " + Bool(c.InputReplayCaptureTiming));
            sb.AppendLine("max_text_chars = " + c.InputReplayMaxTextChars);
            sb.AppendLine();
            sb.AppendLine("[diagnostic_retention]");
            sb.AppendLine("enabled = " + Bool(c.RetentionEnabled));
            sb.AppendLine("directory = \"" + Escape(c.RetentionDirectory) + "\"");
            sb.AppendLine("max_packages = " + c.RetentionMaxPackages);
            sb.AppendLine("max_total_mb = " + c.RetentionMaxTotalMb);
            sb.AppendLine("cleanup_on_startup = " + Bool(c.RetentionCleanupOnStartup));
            sb.AppendLine("cleanup_before_export = " + Bool(c.RetentionCleanupBeforeExport));
            sb.AppendLine("protect_current_session = " + Bool(c.RetentionProtectCurrentSession));
            sb.AppendLine();
            sb.AppendLine("[debug.android_storage]");
            sb.AppendLine("enabled = " + Bool(c.AndroidStorageEnabled));
            sb.AppendLine("log_permissions = " + Bool(c.AndroidStorageLogPermissions));
            sb.AppendLine("log_game_scan = " + Bool(c.AndroidStorageLogGameScan));
            sb.AppendLine("log_path_selection = " + Bool(c.AndroidStorageLogPathSelection));
            sb.AppendLine("log_read_write_failures = " + Bool(c.AndroidStorageLogReadWriteFailures));
            sb.AppendLine("log_scoped_storage = " + Bool(c.AndroidStorageLogScopedStorage));
            sb.AppendLine("max_path_records = " + c.AndroidStorageMaxPathRecords);
            sb.AppendLine();
            sb.AppendLine("[debug.performance_sampling]");
            sb.AppendLine("enabled = " + Bool(c.PerformanceSamplingEnabled));
            sb.AppendLine("sample_interval_ms = " + c.PerformanceSamplingIntervalMs);
            sb.AppendLine("include_fps = " + Bool(c.PerformanceSamplingIncludeFps));
            sb.AppendLine("include_frame_ms = " + Bool(c.PerformanceSamplingIncludeFrameMs));
            sb.AppendLine("include_ui_queue = " + Bool(c.PerformanceSamplingIncludeUiQueue));
            sb.AppendLine("include_texture_queue = " + Bool(c.PerformanceSamplingIncludeTextureQueue));
            sb.AppendLine("include_ring_buffer = " + Bool(c.PerformanceSamplingIncludeRingBuffer));
            sb.AppendLine("include_dropped_count = " + Bool(c.PerformanceSamplingIncludeDroppedCount));
            sb.AppendLine("include_memory = " + Bool(c.PerformanceSamplingIncludeMemory));
            sb.AppendLine();
            sb.AppendLine("[diagnostic_snapshot]");
            sb.AppendLine("enabled = " + Bool(c.SnapshotEnabled));
            sb.AppendLine("include_screenshot = " + Bool(c.SnapshotIncludeScreenshot));
            sb.AppendLine("include_layout_snapshot = " + Bool(c.SnapshotIncludeLayoutSnapshot));
            sb.AppendLine("include_visible_ui_rects = " + Bool(c.SnapshotIncludeVisibleUiRects));
            sb.AppendLine("include_image_rects = " + Bool(c.SnapshotIncludeImageRects));
            sb.AppendLine("max_nodes = " + c.SnapshotMaxNodes);
            sb.AppendLine("max_file_kb = " + c.SnapshotMaxFileKb);
            return sb.ToString();
        }

        static void AppendDebugModel(StringBuilder sb, string sectionName, RuntimeDiagnosticsConfig.DebugModelProfile model)
        {
            sb.AppendLine();
            sb.AppendLine("[" + sectionName + "]");
            sb.AppendLine("enabled = " + Bool(model.Enabled));
            sb.AppendLine("language = \"" + Escape(model.Language) + "\"");
            sb.AppendLine("log_level = \"" + Escape(model.LogLevel) + "\"");
            sb.AppendLine("mirror_to_godot = " + Bool(model.MirrorToGodot));
            sb.AppendLine("scroll_trace = " + Bool(model.ScrollTrace));
        }

        /// <summary>TOML basic string 最小转义（\、"、换行）；本写入器的值域均为简单 ASCII。</summary>
        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n");
        }

        static string Bool(bool value) => value ? "true" : "false";
    }
}
