using System;
using System.Collections.Generic;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 企业级说明：运行时诊断配置只在启动阶段解析一次。
    /// Android 触摸和脚本执行属于高频路径，后续只读取已展开的布尔开关，避免每帧字典查询和字符串分配。
    /// 本类只表达配置，不做 I/O，不调用 Godot UI，不写日志。
    /// </summary>
    public sealed class RuntimeDiagnosticsConfig
    {
        // ---------- migration ----------
        // Structural rollout switches are read at session startup only. They
        // are deliberately not hot-applied by the diagnostics panel.
        public bool MigrationSessionIsolationEnabled { get; set; } = false;

        // ---------- minimal logging switch ----------
        public bool LoggingEnabled { get; set; } = true;

        // ---------- quick_debug ----------
        public bool QuickDebugEnabled { get; set; } = false;
        public string QuickDebugPreset { get; set; } = "normal";
        public string QuickDebugEffectivePreset { get; private set; } = "normal";
        public bool QuickDebugPresetInvalid { get; private set; }
        public string QuickDebugLanguage { get; set; } = "zh_cn";
        public string QuickDebugEffectiveLanguage { get; private set; } = "zh_cn";
        public bool QuickDebugLanguageInvalid { get; private set; }
        public bool QuickDebugApkSafe { get; set; } = true;
        public bool QuickDebugMirrorNonErrorToGodot { get; set; } = false;
        public bool QuickDebugDiagnosticPackage { get; set; } = true;

        public sealed class QuickDebugModuleSwitches
        {
            public bool Touch { get; set; } = false;
            public bool Input { get; set; } = false;
            public bool Image { get; set; } = false;
            public bool UiLayout { get; set; } = false;
            public bool DynamicMap { get; set; } = false;
            public bool Resource { get; set; } = false;
            public bool LoadSave { get; set; } = false;
            public bool AndroidStorage { get; set; } = true;
            public bool PerformanceSampling { get; set; } = false;
            public bool Snapshot { get; set; } = false;
            public bool InputReplay { get; set; } = false;
        }
        public QuickDebugModuleSwitches QuickModules { get; set; } = new QuickDebugModuleSwitches();

        // ---------- active debug model ----------
        public string ActiveDebugModel { get; set; } = "debug_model_zh_cn";

        public sealed class DebugModelProfile
        {
            public bool Enabled { get; set; }
            public string Language { get; set; } = "zh_cn";
            public string LogLevel { get; set; } = "debug";
            public bool MirrorToGodot { get; set; }
            public bool ScrollTrace { get; set; }
        }
        public DebugModelProfile DebugModelZhCn { get; set; } = new DebugModelProfile { Enabled = false, Language = "zh_cn", LogLevel = "error", MirrorToGodot = false, ScrollTrace = false };
        public DebugModelProfile DebugModelJp { get; set; } = new DebugModelProfile { Enabled = false, Language = "jp", LogLevel = "debug", MirrorToGodot = true, ScrollTrace = false };
        public DebugModelProfile DebugModelEn { get; set; } = new DebugModelProfile { Enabled = false, Language = "en", LogLevel = "debug", MirrorToGodot = true, ScrollTrace = false };

        // ---------- logging ----------
        // 2026-09-19 日志默认开启：代码默认等级与 res://config.toml 模板注释（"默认 warn"）对齐；
        // 无配置文件/未写 level 键时按 warn 捕获（Release 构建仍由编译期剥离兜底只留 Error）。
        public string LoggingLevel { get; set; } = "warn";
        public bool LoggingMirrorNonErrorToGodot { get; set; } = false;
        // 持续文件 sink：LoggingEnabled && FileSinkEnabled 双重门控；等级独立于全局 level（FileSinkLevel）。
        // Debug/诊断构建默认开启，Release APK 默认关闭（避免磁盘增长）；config.toml 显式 file_sink 值优先。
#if DEBUG || GEMUERA_DIAGNOSTIC_LOGS
        public bool FileSinkEnabled { get; set; } = true;
#else
        public bool FileSinkEnabled { get; set; } = false;
#endif
        public string FileSinkLevel { get; set; } = "info";
        // 2026-09-19 审计 P0 修复：user:// 轮转文件总量上限（含当前文件）。0 = 显式不限量。
        // 每次新开轮转文件后按时间保留最新 N 份，防止 Android 私有存储静默累积。
        public int LoggingFileSinkMaxFiles { get; set; } = 8;
        public int LoggingDiagnosticRingCapacity { get; set; } = 1000;
        public int LoggingMaxMessageChars { get; set; } = 8192;
        public string LoggingExportDirectory { get; set; } = "game://";
        public int LoggingSchemaVersion { get; set; } = 1;
        public string LoggingSessionIdFormat { get; set; } = "yyyyMMdd-HHmmss";

        // ---------- logging.categories ----------
        public sealed class CategorySwitches
        {
            public bool General { get; set; } = true;
            public bool Sprite { get; set; } = true;
            public bool Audio { get; set; } = true;
            public bool Input { get; set; } = true;
            public bool Script { get; set; } = true;
            public bool UI { get; set; } = true;
            public bool FileSystem { get; set; } = true;
            public bool Load { get; set; } = true;
            public bool Save { get; set; } = true;
            public bool Config { get; set; } = true;
            public bool Performance { get; set; } = true;
            public bool Touch { get; set; } = false;
            public bool StatementRecognition { get; set; } = false;
        }
        public CategorySwitches Categories { get; set; } = new CategorySwitches();

        // ---------- correlation ----------
        public bool CorrelationEnabled { get; set; } = true;
        public bool CorrelationSessionId { get; set; } = true;
        public bool CorrelationInputId { get; set; } = true;
        public bool CorrelationRenderBatchId { get; set; } = true;
        public bool CorrelationLinePartId { get; set; } = true;
        public bool CorrelationImageId { get; set; } = true;

        // ---------- rate_limit ----------
        public bool RateLimitEnabled { get; set; } = true;
        public int RateLimitDefaultPerSecond { get; set; } = 240;
        public int RateLimitDefaultPerFrame { get; set; } = 64;
        public int RateLimitPerEventPerSecond { get; set; } = 30;
        public bool RateLimitRecordDroppedCount { get; set; } = true;

        // ---------- redaction ----------
        // 2026-09-19 审计修复：删除从未被消费的 path_mode / hash_sensitive_text 死旋钮
        //（RedactPath/RedactText 实际只消费 enabled/normalize_paths/max_path_chars/replace_newlines）。
        public bool RedactionEnabled { get; set; } = true;
        public bool RedactionNormalizePaths { get; set; } = true;
        public int RedactionMaxPathChars { get; set; } = 160;
        public int RedactionMaxScriptTextChars { get; set; } = 120;
        public int RedactionMaxUserTextChars { get; set; } = 64;
        public bool RedactionReplaceNewlines { get; set; } = true;

        // ---------- debug.touch ----------
        public bool TouchEnabled { get; set; } = false;
        public bool TouchPointer { get; set; } = false;
        public bool TouchDrag { get; set; } = false;
        public bool TouchPinch { get; set; } = false;
        public bool TouchInertia { get; set; } = false;
        public bool TouchScroll { get; set; } = false;
        public int TouchDragIntervalMs { get; set; } = 120;
        public int TouchCoreBurstLineCount { get; set; } = 120;

        // ---------- debug.statement_recognition ----------
        public bool StatementRecognitionEnabled { get; set; } = false;
        public bool StatementRecognitionErbLoad { get; set; } = false;
        public bool StatementRecognitionLogicalLine { get; set; } = false;
        public bool StatementRecognitionExpression { get; set; } = false;
        public bool StatementRecognitionVariable { get; set; } = false;
        public bool StatementRecognitionFunctionCall { get; set; } = false;
        public int StatementRecognitionMaxLinesPerBurst { get; set; } = 80;

        // ---------- debug.input ----------
        public bool InputDebugEnabled { get; set; } = false;
        public bool InputDebugSubmit { get; set; } = false;
        public bool InputDebugConsume { get; set; } = false;
        public bool InputDebugButton { get; set; } = false;

        // ---------- debug.performance ----------
        public bool PerformanceEnabled { get; set; } = false;
        public bool PerformanceStartupTiming { get; set; } = true;
        public bool PerformanceUiQueue { get; set; } = false;
        public bool PerformanceResourceLoading { get; set; } = false;

        // ---------- debug.load ----------
        public bool LoadDebugEnabled { get; set; } = false;
        public bool LoadDebugErb { get; set; } = false;
        public bool LoadDebugCsv { get; set; } = false;
        public bool LoadDebugResources { get; set; } = false;

        // ---------- debug.save ----------
        public bool SaveDebugEnabled { get; set; } = false;
        public bool SaveDebugRead { get; set; } = false;
        public bool SaveDebugWrite { get; set; } = false;
        public bool SaveDebugCompatibilityFallback { get; set; } = false;

        // ---------- debug.resource ----------
        public bool ResourceDebugEnabled { get; set; } = false;
        public bool ResourceDebugSprite { get; set; } = false;
        public bool ResourceDebugAudio { get; set; } = false;
        public bool ResourceDebugSqlite { get; set; } = false;

        // ---------- debug.image ----------
        public bool ImageDebugEnabled { get; set; } = false;
        public bool ImageDebugResolve { get; set; } = false;
        public bool ImageDebugTexture { get; set; } = false;
        public bool ImageDebugRenderRect { get; set; } = false;
        public bool ImageDebugCache { get; set; } = false;
        public bool ImageDebugColorMatrix { get; set; } = false;
        public bool ImageDebugLogSuccess { get; set; } = false;
        public int ImageDebugMaxRecordsPerFrame { get; set; } = 16;

        // ---------- debug.ui_layout ----------
        public bool UiLayoutEnabled { get; set; } = false;
        public bool UiLayoutTargetRect { get; set; } = false;
        public bool UiLayoutActualRect { get; set; } = false;
        public bool UiLayoutMismatchOnly { get; set; } = true;
        public int UiLayoutMismatchThresholdPx { get; set; } = 2;
        public bool UiLayoutText { get; set; } = false;
        public bool UiLayoutButton { get; set; } = true;
        public bool UiLayoutImage { get; set; } = true;
        public bool UiLayoutShape { get; set; } = true;
        public bool UiLayoutHtmlDiv { get; set; } = true;
        public bool UiLayoutHtmlImg { get; set; } = true;
        public bool UiLayoutCbg { get; set; } = true;
        public bool UiLayoutIncludeText { get; set; } = false;
        public int UiLayoutMaxTextChars { get; set; } = 32;
        public int UiLayoutMaxRecordsPerFrame { get; set; } = 32;

        // ---------- debug.dynamic_map ----------
        public bool DynamicMapDebugEnabled { get; set; } = false;
        public bool DynamicMapLogLineSnapshot { get; set; } = true;
        public bool DynamicMapLogScroll { get; set; } = true;
        public bool DynamicMapLogButtons { get; set; } = true;
        public bool DynamicMapOnlyBitmapContext { get; set; } = true;
        public int DynamicMapMaxLines { get; set; } = 12;
        public int DynamicMapMaxTextChars { get; set; } = 48;
        public int DynamicMapContextWindowMs { get; set; } = 5000;

        // ---------- debug.lifecycle ----------
        public bool LifecycleEnabled { get; set; } = false;
        public bool LifecycleAndroidPauseResume { get; set; } = false;

        // ---------- debug.runtime_panel ----------
        // 2026-09-19 移除：日志悬浮窗（悬浮球/悬浮弹窗）功能整体下线，配置键一并删除。

        // ---------- diagnostic_package ----------
        public bool DiagnosticPackageEnabled { get; set; } = false;
        public bool DiagnosticPackageIncludeLog { get; set; } = true;
        public bool DiagnosticPackageIncludeConfigSnapshot { get; set; } = true;
        public bool DiagnosticPackageIncludeDeviceInfo { get; set; } = true;
        public bool DiagnosticPackageIncludeScreenInfo { get; set; } = true;
        public bool DiagnosticPackageIncludeGodotInfo { get; set; } = true;
        public bool DiagnosticPackageIncludeApkInfo { get; set; } = true;
        public bool DiagnosticPackageIncludeGamePath { get; set; } = true;
        public bool DiagnosticPackageIncludeErrorSummary { get; set; } = true;

        // ---------- diagnostic_summary ----------
        public bool DiagnosticSummaryEnabled { get; set; } = true;
        public int DiagnosticSummaryTopEventIds { get; set; } = 20;
        public int DiagnosticSummaryTopUiLayoutMismatches { get; set; } = 20;
        public int DiagnosticSummaryTopImageFailures { get; set; } = 20;

        // ---------- debug.ui_overlay ----------
        public bool UiOverlayEnabled { get; set; } = false;
        public bool UiOverlayTargetRect { get; set; } = false;
        public bool UiOverlayActualRect { get; set; } = false;
        public bool UiOverlayMismatch { get; set; } = true;
        public bool UiOverlayImageRect { get; set; } = true;
        public bool UiOverlayButtonRect { get; set; } = true;
        public int UiOverlayMaxDrawnRects { get; set; } = 128;

        // ---------- debug.reference ----------
        public bool ReferenceEnabled { get; set; } = false;
        public string ReferenceName { get; set; } = "";
        public bool ReferenceExpectedFromReference { get; set; } = false;
        public bool ReferenceNote { get; set; } = false;

        // ---------- diagnostic_breadcrumb ----------
        public bool BreadcrumbEnabled { get; set; } = false;
        public string BreadcrumbPath { get; set; } = "game://gemuera-last-session-breadcrumb.log";
        public int BreadcrumbMaxBytes { get; set; } = 32768;
        public bool BreadcrumbWriteOnStartup { get; set; } = true;
        public bool BreadcrumbWriteOnConfigLoaded { get; set; } = true;
        public bool BreadcrumbWriteOnGamePathSelected { get; set; } = true;
        public bool BreadcrumbWriteOnSevereError { get; set; } = true;
        public bool BreadcrumbWriteOnExport { get; set; } = true;
        public bool BreadcrumbWriteOnShutdown { get; set; } = true;

        // ---------- debug.input_replay ----------
        public bool InputReplayEnabled { get; set; } = false;
        public int InputReplayMaxEvents { get; set; } = 240;
        public bool InputReplayCaptureTouch { get; set; } = true;
        public bool InputReplayCaptureButton { get; set; } = true;
        public bool InputReplayCaptureKeyboard { get; set; } = true;
        public bool InputReplayCaptureWaitState { get; set; } = true;
        public bool InputReplayCaptureTiming { get; set; } = true;
        public int InputReplayMaxTextChars { get; set; } = 32;

        // ---------- diagnostic_retention ----------
        // 2026-09-19 审计修复：默认开启。游戏目录选择时（NotifyGamePathSelected，game:// 已解析）
        // 与诊断包导出前按 max_packages/max_total_mb 清理 gemuera_* 管理命名文件，
        // 防止 auto/manual 导出物跨会话无限累积污染游戏目录（eraMegaten 17 份出厂日志实证）。
        public bool RetentionEnabled { get; set; } = true;
        public string RetentionDirectory { get; set; } = "game://";
        public int RetentionMaxPackages { get; set; } = 20;
        public int RetentionMaxTotalMb { get; set; } = 128;
        public bool RetentionCleanupOnStartup { get; set; } = true;
        public bool RetentionCleanupBeforeExport { get; set; } = true;
        public bool RetentionProtectCurrentSession { get; set; } = true;

        // ---------- debug.android_storage ----------
        public bool AndroidStorageEnabled { get; set; } = false;
        public bool AndroidStorageLogPermissions { get; set; } = true;
        public bool AndroidStorageLogGameScan { get; set; } = true;
        public bool AndroidStorageLogPathSelection { get; set; } = true;
        public bool AndroidStorageLogReadWriteFailures { get; set; } = true;
        public bool AndroidStorageLogScopedStorage { get; set; } = true;
        public int AndroidStorageMaxPathRecords { get; set; } = 64;

        // ---------- [logging].performance 映射的运行时采样 ----------
        public bool PerformanceSamplingEnabled { get; set; } = false;
        public int PerformanceSamplingIntervalMs { get; set; } = 1000;
        public bool PerformanceSamplingIncludeFps { get; set; } = true;
        public bool PerformanceSamplingIncludeFrameMs { get; set; } = true;
        public bool PerformanceSamplingIncludeUiQueue { get; set; } = true;
        public bool PerformanceSamplingIncludeTextureQueue { get; set; } = true;
        public bool PerformanceSamplingIncludeRingBuffer { get; set; } = true;
        public bool PerformanceSamplingIncludeDroppedCount { get; set; } = true;
        public bool PerformanceSamplingIncludeMemory { get; set; } = true;

        // ---------- diagnostic_snapshot ----------
        public bool SnapshotEnabled { get; set; } = false;
        public bool SnapshotIncludeScreenshot { get; set; } = false;
        public bool SnapshotIncludeLayoutSnapshot { get; set; } = false;
        public bool SnapshotIncludeVisibleUiRects { get; set; } = true;
        public bool SnapshotIncludeImageRects { get; set; } = true;
        public int SnapshotMaxNodes { get; set; } = 512;
        public int SnapshotMaxFileKb { get; set; } = 1024;

        // ---------- helpers ----------
        /// <summary>
        /// 日志总开关关闭时必须把所有诊断副作用一起关掉：
        /// 不分配 ring buffer、不写 breadcrumb、不做 retention 扫描，也不保留输入回放。
        /// </summary>
        public void DisableAllDiagnostics()
        {
            LoggingEnabled = false;
            QuickDebugEnabled = false;
            LoggingLevel = "none";
            LoggingMirrorNonErrorToGodot = false;
            QuickDebugMirrorNonErrorToGodot = false;
            QuickDebugDiagnosticPackage = false;

            DebugModelZhCn.Enabled = false;
            DebugModelJp.Enabled = false;
            DebugModelEn.Enabled = false;
            DebugModelZhCn.MirrorToGodot = false;
            DebugModelJp.MirrorToGodot = false;
            DebugModelEn.MirrorToGodot = false;
            DebugModelZhCn.ScrollTrace = false;
            DebugModelJp.ScrollTrace = false;
            DebugModelEn.ScrollTrace = false;

            Categories.General = false;
            Categories.Sprite = false;
            Categories.Audio = false;
            Categories.Input = false;
            Categories.Script = false;
            Categories.UI = false;
            Categories.FileSystem = false;
            Categories.Load = false;
            Categories.Save = false;
            Categories.Config = false;
            Categories.Performance = false;
            Categories.Touch = false;
            Categories.StatementRecognition = false;

            CorrelationEnabled = false;
            RateLimitEnabled = false;

            TouchEnabled = false;
            TouchPointer = false;
            TouchDrag = false;
            TouchPinch = false;
            TouchInertia = false;
            TouchScroll = false;
            StatementRecognitionEnabled = false;
            StatementRecognitionErbLoad = false;
            StatementRecognitionLogicalLine = false;
            StatementRecognitionExpression = false;
            StatementRecognitionVariable = false;
            StatementRecognitionFunctionCall = false;
            InputDebugEnabled = false;
            InputDebugSubmit = false;
            InputDebugConsume = false;
            InputDebugButton = false;
            PerformanceEnabled = false;
            PerformanceUiQueue = false;
            PerformanceResourceLoading = false;
            LoadDebugEnabled = false;
            LoadDebugErb = false;
            LoadDebugCsv = false;
            LoadDebugResources = false;
            SaveDebugEnabled = false;
            SaveDebugRead = false;
            SaveDebugWrite = false;
            SaveDebugCompatibilityFallback = false;
            ResourceDebugEnabled = false;
            ResourceDebugSprite = false;
            ResourceDebugAudio = false;
            ResourceDebugSqlite = false;
            ImageDebugEnabled = false;
            ImageDebugResolve = false;
            ImageDebugTexture = false;
            ImageDebugRenderRect = false;
            ImageDebugCache = false;
            ImageDebugColorMatrix = false;
            ImageDebugLogSuccess = false;
            UiLayoutEnabled = false;
            UiLayoutTargetRect = false;
            UiLayoutActualRect = false;
            UiLayoutText = false;
            DynamicMapDebugEnabled = false;
            DynamicMapLogLineSnapshot = false;
            DynamicMapLogScroll = false;
            DynamicMapLogButtons = false;
            LifecycleEnabled = false;
            LifecycleAndroidPauseResume = false;
            DiagnosticPackageEnabled = false;
            DiagnosticSummaryEnabled = false;
            UiOverlayEnabled = false;
            ReferenceEnabled = false;
            BreadcrumbEnabled = false;
            InputReplayEnabled = false;
            RetentionEnabled = false;
            AndroidStorageEnabled = false;
            PerformanceSamplingEnabled = false;
            SnapshotEnabled = false;
        }

        /// <summary>
        /// 将精简 TOML 的模块开关一次性展开到既有强类型字段。
        /// 该方法只在配置加载阶段调用，热路径仍只读布尔值。
        /// </summary>
        public void ApplyMinimalLoggingConfig(
            bool enabled,
            bool touch,
            bool input,
            bool image,
            bool uiLayout,
            bool dynamicMap,
            bool resource,
            bool loadSave,
            bool androidStorage,
            bool performance,
            bool statementRecognition,
            bool inputReplay,
            bool diagnosticPackage,
            bool mirrorToGodot,
            string levelOverride = null)
        {
            DisableAllDiagnostics();
            if (!enabled)
                return;

            LoggingEnabled = true;
            // 企业级说明：配置文件显式给出 [logging] level 时，minimal 展开以它为基准，
            // 避免旧“enabled=true 强制 debug”把用户设置的文件等级吞掉；
            // 缺省回退 warn（2026-09-19 与代码默认等级对齐，此前为 debug）。
            LoggingLevel = string.IsNullOrWhiteSpace(levelOverride) ? "warn" : levelOverride;
            LoggingMirrorNonErrorToGodot = mirrorToGodot;
            ActiveDebugModel = "debug_model_zh_cn";
            DebugModelZhCn.Enabled = true;
            DebugModelZhCn.Language = "zh_cn";
            DebugModelZhCn.LogLevel = LoggingLevel;
            DebugModelZhCn.MirrorToGodot = mirrorToGodot;
            DebugModelZhCn.ScrollTrace = false;

            Categories.General = true;
            Categories.Config = true;
            CorrelationEnabled = true;
            RateLimitEnabled = true;
            RedactionEnabled = true;
            DiagnosticPackageEnabled = diagnosticPackage;
            InputReplayEnabled = inputReplay;

            if (touch) EnableQuickTouch();
            if (input) EnableQuickInput();
            if (image) EnableQuickImage();
            if (uiLayout) EnableQuickUiLayout();
            if (dynamicMap) EnableQuickDynamicMap();
            if (resource) EnableQuickResource();
            if (loadSave) EnableQuickLoadSave();
            if (androidStorage)
            {
                Categories.FileSystem = true;
                EnableQuickAndroidStorage();
            }
            if (performance)
            {
                Categories.Performance = true;
                PerformanceEnabled = true;
                PerformanceStartupTiming = true;
                PerformanceUiQueue = true;
                PerformanceResourceLoading = true;
                EnableQuickPerformanceSampling();
            }
            if (statementRecognition)
            {
                Categories.StatementRecognition = true;
                StatementRecognitionEnabled = true;
                StatementRecognitionErbLoad = true;
                StatementRecognitionLogicalLine = true;
                StatementRecognitionExpression = true;
                StatementRecognitionVariable = true;
                StatementRecognitionFunctionCall = true;
            }
        }

        public static RuntimeDiagnosticsConfig CreateDefault()
        {
            return new RuntimeDiagnosticsConfig();
        }

        /// <summary>
        /// 企业级说明：旧版本把诊断日志写到 user://diagnostics，手机用户很难定位。
        /// 本方法只迁移诊断日志/面包屑/保留策略路径到 game://，不迁移 user://config.toml 等运行时配置覆盖文件。
        /// game:// 会在导出阶段解析为本次启动的游戏目录，避免配置文件写死 Windows 或 Android 绝对路径。
        /// </summary>
        public void MigrateLegacyUserDiagnosticsPaths()
        {
            LoggingExportDirectory = MigrateLegacyUserDiagnosticsPath(LoggingExportDirectory, "game://");
            BreadcrumbPath = MigrateLegacyUserDiagnosticsPath(BreadcrumbPath, "game://gemuera-last-session-breadcrumb.log");
            RetentionDirectory = MigrateLegacyUserDiagnosticsPath(RetentionDirectory, "game://");
        }

        static string MigrateLegacyUserDiagnosticsPath(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            string normalized = value.Trim().Replace('\\', '/');
            const string legacyRoot = "user://diagnostics";
            if (!normalized.StartsWith(legacyRoot, StringComparison.OrdinalIgnoreCase))
                return value;

            string suffix = normalized.Substring(legacyRoot.Length).TrimStart('/');
            if (string.IsNullOrEmpty(suffix))
                return fallback;
            return "game://" + suffix;
        }

        /// <summary>
        /// 企业级说明：quick_debug 是面向人工 APK 排障的简化入口，只允许在配置加载阶段展开。
        /// 展开后热路径只读取 TouchEnabled、ImageDebugEnabled 等布尔字段，不能在触摸、绘制或脚本循环中判断 preset 字符串。
        /// preset 之外的 quick_debug.modules 只做“额外开启”，false 不会反向关闭 preset 已开启的模块。
        /// </summary>
        public void ApplyQuickDebugPreset()
        {
            QuickDebugPresetInvalid = false;
            QuickDebugLanguageInvalid = false;
            QuickDebugEffectivePreset = NormalizeQuickPreset(QuickDebugPreset);
            QuickDebugEffectiveLanguage = NormalizeQuickLanguage(QuickDebugLanguage);

            if (!QuickDebugEnabled || string.Equals(QuickDebugEffectivePreset, "custom", StringComparison.OrdinalIgnoreCase))
                return;

            ResetQuickDebugSurface();

            switch (QuickDebugEffectivePreset)
            {
                case "touch_input":
                    EnableQuickTouch();
                    EnableQuickInput();
                    break;
                case "image_ui":
                    EnableQuickImage();
                    EnableQuickUiLayout();
                    break;
                case "resource_storage":
                    EnableQuickResource();
                    EnableQuickLoadSave();
                    EnableQuickAndroidStorage();
                    break;
                case "performance":
                    EnableQuickPerformanceSampling();
                    break;
                case "apk_issue":
                    EnableQuickAndroidStorage();
                    EnableQuickLifecycle();
                    BreadcrumbEnabled = true;
                    DiagnosticPackageEnabled = true;
                    break;
                case "normal":
                default:
                    break;
            }

            ApplyQuickModuleExtras();
        }

        string NormalizeQuickPreset(string value)
        {
            string preset = string.IsNullOrWhiteSpace(value) ? "normal" : value.Trim().ToLowerInvariant();
            switch (preset)
            {
                case "normal":
                case "touch_input":
                case "image_ui":
                case "resource_storage":
                case "performance":
                case "apk_issue":
                case "custom":
                    return preset;
                default:
                    QuickDebugPresetInvalid = true;
                    return "normal";
            }
        }

        string NormalizeQuickLanguage(string value)
        {
            string language = string.IsNullOrWhiteSpace(value) ? "zh_cn" : value.Trim().ToLowerInvariant();
            switch (language)
            {
                case "zh_cn":
                case "jp":
                case "en":
                    return language;
                default:
                    QuickDebugLanguageInvalid = true;
                    return "zh_cn";
            }
        }

        void ResetQuickDebugSurface()
        {
            ActiveDebugModel = QuickDebugEffectiveLanguage switch
            {
                "jp" => "debug_model_jp",
                "en" => "debug_model_en",
                _ => "debug_model_zh_cn",
            };

            DebugModelZhCn.Enabled = false;
            DebugModelJp.Enabled = false;
            DebugModelEn.Enabled = false;
            var activeModel = GetActiveDebugModel();
            activeModel.Language = QuickDebugEffectiveLanguage;
            activeModel.LogLevel = "debug";
            activeModel.MirrorToGodot = !QuickDebugApkSafe && QuickDebugMirrorNonErrorToGodot;
            activeModel.ScrollTrace = false;

            LoggingLevel = "error";
            LoggingMirrorNonErrorToGodot = !QuickDebugApkSafe && QuickDebugMirrorNonErrorToGodot;
            DiagnosticPackageEnabled = QuickDebugDiagnosticPackage;

            Categories.General = false;
            Categories.Sprite = false;
            Categories.Audio = false;
            Categories.Input = false;
            Categories.Script = false;
            Categories.UI = false;
            Categories.FileSystem = false;
            Categories.Load = false;
            Categories.Save = false;
            Categories.Config = false;
            Categories.Performance = false;
            Categories.Touch = false;
            Categories.StatementRecognition = false;

            TouchEnabled = false;
            TouchPointer = false;
            TouchDrag = false;
            TouchPinch = false;
            TouchInertia = false;
            TouchScroll = false;
            StatementRecognitionEnabled = false;
            InputDebugEnabled = false;
            ImageDebugEnabled = false;
            UiLayoutEnabled = false;
            DynamicMapDebugEnabled = false;
            ResourceDebugEnabled = false;
            LoadDebugEnabled = false;
            SaveDebugEnabled = false;
            LifecycleEnabled = false;
            PerformanceSamplingEnabled = false;
            SnapshotEnabled = false;
            InputReplayEnabled = false;
            UiOverlayEnabled = false;

            BreadcrumbEnabled = true;
            RetentionEnabled = true;
            AndroidStorageEnabled = false;

            if (QuickDebugApkSafe)
            {
                RateLimitEnabled = true;
                RateLimitDefaultPerSecond = Math.Min(RateLimitDefaultPerSecond, 240);
                RateLimitDefaultPerFrame = Math.Min(RateLimitDefaultPerFrame, 64);
                RateLimitPerEventPerSecond = Math.Min(RateLimitPerEventPerSecond, 30);
            }
        }

        void ApplyQuickModuleExtras()
        {
            var modules = QuickModules ?? new QuickDebugModuleSwitches();
            if (modules.Touch) EnableQuickTouch();
            if (modules.Input) EnableQuickInput();
            if (modules.Image) EnableQuickImage();
            if (modules.UiLayout) EnableQuickUiLayout();
            if (modules.DynamicMap) EnableQuickDynamicMap();
            if (modules.Resource) EnableQuickResource();
            if (modules.LoadSave) EnableQuickLoadSave();
            if (modules.AndroidStorage) EnableQuickAndroidStorage();
            if (modules.PerformanceSampling) EnableQuickPerformanceSampling();
            if (modules.Snapshot) EnableQuickSnapshot();
            if (modules.InputReplay) EnableQuickInputReplay();
        }

        void EnsureQuickDebugModel()
        {
            var model = GetActiveDebugModel();
            if (model == null)
                return;
            model.Enabled = true;
            model.LogLevel = "debug";
            model.MirrorToGodot = !QuickDebugApkSafe && QuickDebugMirrorNonErrorToGodot;
        }

        void EnableQuickTouch()
        {
            EnsureQuickDebugModel();
            Categories.Touch = true;
            TouchEnabled = true;
            TouchPointer = true;
            TouchDrag = true;
            TouchDragIntervalMs = Math.Max(80, TouchDragIntervalMs);
        }

        void EnableQuickInput()
        {
            EnsureQuickDebugModel();
            Categories.Input = true;
            InputDebugEnabled = true;
            InputDebugSubmit = true;
            InputDebugConsume = true;
            InputDebugButton = true;
        }

        void EnableQuickImage()
        {
            EnsureQuickDebugModel();
            Categories.Sprite = true;
            ImageDebugEnabled = true;
            ImageDebugResolve = true;
            ImageDebugTexture = true;
            ImageDebugRenderRect = true;
            ImageDebugCache = true;
            ImageDebugLogSuccess = false;
        }

        void EnableQuickUiLayout()
        {
            EnsureQuickDebugModel();
            Categories.UI = true;
            UiLayoutEnabled = true;
            UiLayoutMismatchOnly = true;
            UiLayoutButton = true;
            UiLayoutImage = true;
            UiLayoutShape = true;
            UiLayoutHtmlDiv = true;
            UiLayoutHtmlImg = true;
            UiLayoutCbg = true;
            UiLayoutIncludeText = false;
        }

        void EnableQuickDynamicMap()
        {
            EnsureQuickDebugModel();
            Categories.UI = true;
            Categories.Script = true;
            DynamicMapDebugEnabled = true;
            DynamicMapLogLineSnapshot = true;
            DynamicMapLogScroll = true;
            DynamicMapLogButtons = true;
            DynamicMapOnlyBitmapContext = true;
            DynamicMapMaxLines = Math.Max(4, DynamicMapMaxLines);
            DynamicMapMaxTextChars = Math.Max(24, DynamicMapMaxTextChars);
            DynamicMapContextWindowMs = Math.Max(1000, DynamicMapContextWindowMs);
        }

        void EnableQuickResource()
        {
            EnsureQuickDebugModel();
            Categories.Sprite = true;
            Categories.Audio = true;
            Categories.FileSystem = true;
            ResourceDebugEnabled = true;
            ResourceDebugSprite = true;
            ResourceDebugAudio = true;
            ResourceDebugSqlite = true;
            ImageDebugEnabled = true;
            ImageDebugResolve = true;
            ImageDebugTexture = true;
        }

        void EnableQuickLoadSave()
        {
            EnsureQuickDebugModel();
            Categories.Load = true;
            Categories.Save = true;
            Categories.FileSystem = true;
            LoadDebugEnabled = true;
            LoadDebugErb = true;
            LoadDebugCsv = true;
            LoadDebugResources = true;
            SaveDebugEnabled = true;
            SaveDebugRead = true;
            SaveDebugWrite = true;
            SaveDebugCompatibilityFallback = true;
        }

        void EnableQuickAndroidStorage()
        {
            AndroidStorageEnabled = true;
            AndroidStorageLogPermissions = true;
            AndroidStorageLogGameScan = true;
            AndroidStorageLogPathSelection = true;
            AndroidStorageLogReadWriteFailures = true;
            AndroidStorageLogScopedStorage = true;
        }

        void EnableQuickPerformanceSampling()
        {
            Categories.Performance = true;
            PerformanceSamplingEnabled = true;
            PerformanceSamplingIntervalMs = Math.Max(500, PerformanceSamplingIntervalMs);
        }

        void EnableQuickLifecycle()
        {
            LifecycleEnabled = true;
            LifecycleAndroidPauseResume = true;
        }

        void EnableQuickSnapshot()
        {
            SnapshotEnabled = true;
            DiagnosticPackageEnabled = true;
        }

        void EnableQuickInputReplay()
        {
            InputReplayEnabled = true;
            InputReplayMaxEvents = Math.Min(Math.Max(InputReplayMaxEvents, 1), 240);
        }

        // 企业级说明：运行时有效日志等级必须优先采用当前启用的 debug model。
        // Android APK 排障时通常只切换 debug_model.enabled 和模块开关；如果仍被 [logging].level 拦截，
        // 触摸、输入、图片和 UI 几何诊断会出现“开关已开但无日志”的误判。
        public EmueraLogLevel GetRuntimeLogLevel()
        {
            if (!LoggingEnabled)
                return EmueraLogLevel.None;
            var model = GetActiveDebugModel();
            if (model != null && model.Enabled)
                return ParseLogLevel(model.LogLevel);
            return ParseLogLevel(LoggingLevel);
        }

        public static EmueraLogLevel ParseLogLevel(string level)
        {
            // 值域：error|warn|info|debug|trace|none（设计契约 §3.2-B 含 trace，语义为全量）。
            if (string.Equals(level, "trace", StringComparison.OrdinalIgnoreCase)) return EmueraLogLevel.Debug;
            if (string.Equals(level, "debug", StringComparison.OrdinalIgnoreCase)) return EmueraLogLevel.Debug;
            if (string.Equals(level, "info", StringComparison.OrdinalIgnoreCase)) return EmueraLogLevel.Info;
            if (string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase)) return EmueraLogLevel.Warn;
            if (string.Equals(level, "error", StringComparison.OrdinalIgnoreCase)) return EmueraLogLevel.Error;
            if (string.Equals(level, "none", StringComparison.OrdinalIgnoreCase)) return EmueraLogLevel.None;
            // 未知值静默回退 Error 会掩盖配置笔误（如 UI 写入 trace 而解析器不认），
            // 显式告警一次便于排查；仅配置加载路径调用，不落热路径。
            Godot.GD.PushWarning("[RuntimeDiagnosticsConfig] 未知日志等级 '" + level + "'，回退为 error（合法值域 error|warn|info|debug|trace|none）");
            return EmueraLogLevel.Error;
        }

        public EmueraLogCategory GetActiveDebugModelCategoryMask()
        {
            if (!LoggingEnabled)
                return EmueraLogCategory.None;
            var model = GetActiveDebugModel();
            if (model == null || !model.Enabled)
                return EmueraLogCategory.None;

            EmueraLogCategory mask = EmueraLogCategory.None;
            if (Categories.General) mask |= EmueraLogCategory.General;
            if (Categories.Sprite) mask |= EmueraLogCategory.Sprite;
            if (Categories.Audio) mask |= EmueraLogCategory.Audio;
            if (Categories.Input) mask |= EmueraLogCategory.Input;
            if (Categories.Script) mask |= EmueraLogCategory.Script;
            if (Categories.UI) mask |= EmueraLogCategory.UI;
            if (Categories.FileSystem) mask |= EmueraLogCategory.FileSystem;
            if (Categories.Load) mask |= EmueraLogCategory.Load;
            if (Categories.Save) mask |= EmueraLogCategory.Save;
            if (Categories.Config) mask |= EmueraLogCategory.Config;
            if (Categories.Performance) mask |= EmueraLogCategory.Performance;
            if (Categories.Touch) mask |= EmueraLogCategory.Touch;
            if (Categories.StatementRecognition) mask |= EmueraLogCategory.StatementRecognition;
            return mask;
        }

        public DebugModelProfile GetActiveDebugModel()
        {
            if (string.Equals(ActiveDebugModel, "debug_model_jp", StringComparison.OrdinalIgnoreCase))
                return DebugModelJp;
            if (string.Equals(ActiveDebugModel, "debug_model_en", StringComparison.OrdinalIgnoreCase))
                return DebugModelEn;
            return DebugModelZhCn;
        }
    }
}
