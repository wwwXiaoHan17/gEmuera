using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 企业级说明：日志等级/类别/模块开关判断，延迟构造 message，分发到 sinks。
    /// 不做配置文件 I/O，不知道 UI 细节。
    /// Android 高频路径必须先判断开关，再构造字符串，避免关闭调试时仍产生分配。
    /// </summary>
    public static class DiagnosticLogRouter
    {
        static RuntimeDiagnosticsConfig _config = RuntimeDiagnosticsConfig.CreateDefault();
        static string _sessionId;
        static long _sequence;
        static int _initialized;
        static int _cachedLoggingEnabled;
        static int _cachedRuntimeLogLevel = (int)EmueraLogLevel.Error;
        static int _cachedCategoryMask = (int)EmueraLogCategory.None;
        static readonly double StopwatchTickToMilliseconds = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        // 限流状态（轻量，非线程安全但可接受偶尔竞争）
        static readonly Dictionary<string, RateLimitBucket> _rateLimitBuckets = new Dictionary<string, RateLimitBucket>();
        static long _droppedTotal;
        static readonly Dictionary<string, long> _droppedByEventId = new Dictionary<string, long>();
        static readonly Dictionary<string, long> _droppedByCategory = new Dictionary<string, long>();
        static readonly object _rateLimitLock = new object();
        static int _currentFrameCount;
        static long _lastFrameTick;
        static int _currentSecondCount;
        static long _lastSecondTick;

        struct RateLimitBucket
        {
            public long LastTickMs;
            public int CountThisSecond;
        }

        public static void Initialize(RuntimeDiagnosticsConfig config, string sessionId)
        {
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
                return;
            ApplyConfig(config);
            _sessionId = sessionId ?? DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }

        /// <summary>
        /// 企业级说明：运行时重载配置，不修改 session_id，不清空限流状态。
        /// 供 runtime panel 切换开关后立即生效，无需重启。
        /// </summary>
        public static void Reload(RuntimeDiagnosticsConfig config)
        {
            ApplyConfig(config);
        }

        public static string SessionId => _sessionId ?? "unknown";
        public static RuntimeDiagnosticsConfig Config => _config;

        static void ApplyConfig(RuntimeDiagnosticsConfig config)
        {
            // 企业级说明：日志路由是触摸、图片和 UI 诊断的热路径。
            // 启动/热重载时缓存已展开的等级和类别掩码，避免每条日志重复解析字符串或扫描类别布尔字段。
            var nextConfig = config ?? RuntimeDiagnosticsConfig.CreateDefault();
            _config = nextConfig;
            Volatile.Write(ref _cachedLoggingEnabled, nextConfig.LoggingEnabled ? 1 : 0);
            Volatile.Write(ref _cachedRuntimeLogLevel, (int)nextConfig.GetRuntimeLogLevel());
            Volatile.Write(ref _cachedCategoryMask, (int)nextConfig.GetActiveDebugModelCategoryMask());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLoggingEnabled()
        {
            return Volatile.Read(ref _cachedLoggingEnabled) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEnabled(EmueraLogLevel level, EmueraLogCategory category, string eventId)
        {
            if (Volatile.Read(ref _cachedLoggingEnabled) == 0)
                return false;
            if (level == EmueraLogLevel.None)
                return false;
            var globalLevel = (EmueraLogLevel)Volatile.Read(ref _cachedRuntimeLogLevel);
            if ((int)level < (int)globalLevel)
                return false;
            if (level < EmueraLogLevel.Error && category != EmueraLogCategory.None && !IsCategoryEnabled(category))
                return false;
            if (!CheckRateLimit(eventId, category))
                return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCategoryEnabled(EmueraLogCategory category)
        {
            if (Volatile.Read(ref _cachedLoggingEnabled) == 0)
                return false;
            var mask = (EmueraLogCategory)Volatile.Read(ref _cachedCategoryMask);
            return (mask & category) != 0;
        }

        // 模块级细分开关（触摸/输入/语句识别/图片/UI 几何）
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTouchEnabled(string subSwitch)
        {
            var cfg = Config;
            if (cfg == null || !cfg.LoggingEnabled || !cfg.TouchEnabled)
                return false;
            return subSwitch switch
            {
                "pointer" => cfg.TouchPointer,
                "drag" => cfg.TouchDrag,
                "pinch" => cfg.TouchPinch,
                "inertia" => cfg.TouchInertia,
                "scroll" => cfg.TouchScroll,
                _ => false,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInputDebugEnabled(string subSwitch)
        {
            var cfg = Config;
            if (cfg == null || !cfg.LoggingEnabled || !cfg.InputDebugEnabled)
                return false;
            return subSwitch switch
            {
                "submit" => cfg.InputDebugSubmit,
                "consume" => cfg.InputDebugConsume,
                "button" => cfg.InputDebugButton,
                _ => false,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsStatementRecognitionEnabled(string subSwitch)
        {
            var cfg = Config;
            if (cfg == null || !cfg.LoggingEnabled || !cfg.StatementRecognitionEnabled)
                return false;
            return subSwitch switch
            {
                "erb_load" => cfg.StatementRecognitionErbLoad,
                "logical_line" => cfg.StatementRecognitionLogicalLine,
                "expression" => cfg.StatementRecognitionExpression,
                "variable" => cfg.StatementRecognitionVariable,
                "function_call" => cfg.StatementRecognitionFunctionCall,
                _ => false,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsImageDebugEnabled(string subSwitch)
        {
            var cfg = Config;
            if (cfg == null || !cfg.LoggingEnabled || !cfg.ImageDebugEnabled)
                return false;
            return subSwitch switch
            {
                "resolve" => cfg.ImageDebugResolve,
                "texture" => cfg.ImageDebugTexture,
                "render_rect" => cfg.ImageDebugRenderRect,
                "cache" => cfg.ImageDebugCache,
                "color_matrix" => cfg.ImageDebugColorMatrix,
                "log_success" => cfg.ImageDebugLogSuccess,
                _ => false,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsUiLayoutEnabled(string subSwitch)
        {
            var cfg = Config;
            if (cfg == null || !cfg.LoggingEnabled || !cfg.UiLayoutEnabled)
                return false;
            return subSwitch switch
            {
                "target_rect" => cfg.UiLayoutTargetRect,
                "actual_rect" => cfg.UiLayoutActualRect,
                "mismatch_only" => cfg.UiLayoutMismatchOnly,
                "text" => cfg.UiLayoutText,
                "button" => cfg.UiLayoutButton,
                "image" => cfg.UiLayoutImage,
                "shape" => cfg.UiLayoutShape,
                "html_div" => cfg.UiLayoutHtmlDiv,
                "html_img" => cfg.UiLayoutHtmlImg,
                "cbg" => cfg.UiLayoutCbg,
                _ => false,
            };
        }

        public static bool CheckRateLimit(string eventId, EmueraLogCategory category = EmueraLogCategory.None)
        {
            var cfg = _config;
            if (cfg == null || !cfg.LoggingEnabled)
                return false;
            if (!cfg.RateLimitEnabled)
                return true;
            if (string.IsNullOrEmpty(eventId))
                eventId = "<unknown>";

            long nowMs = GetMonotonicMilliseconds();
            lock (_rateLimitLock)
            {
                // 帧计数刷新
                if (nowMs - _lastFrameTick > 50)
                {
                    _currentFrameCount = 0;
                    _lastFrameTick = nowMs;
                }
                _currentFrameCount++;
                if (_currentFrameCount > cfg.RateLimitDefaultPerFrame)
                {
                    RecordDropped(cfg, eventId, category);
                    return false;
                }

                if (nowMs - _lastSecondTick >= 1000)
                {
                    _currentSecondCount = 0;
                    _lastSecondTick = nowMs;
                }
                _currentSecondCount++;
                if (_currentSecondCount > cfg.RateLimitDefaultPerSecond)
                {
                    RecordDropped(cfg, eventId, category);
                    return false;
                }

                if (!_rateLimitBuckets.TryGetValue(eventId, out var bucket))
                {
                    bucket = new RateLimitBucket { LastTickMs = nowMs };
                    _rateLimitBuckets[eventId] = bucket;
                }

                if (nowMs - bucket.LastTickMs >= 1000)
                {
                    bucket.LastTickMs = nowMs;
                    bucket.CountThisSecond = 0;
                }

                bucket.CountThisSecond++;
                if (bucket.CountThisSecond > cfg.RateLimitPerEventPerSecond)
                {
                    RecordDropped(cfg, eventId, category);
                    return false;
                }

                _rateLimitBuckets[eventId] = bucket;
                return true;
            }
        }

        static void RecordDropped(RuntimeDiagnosticsConfig cfg, string eventId, EmueraLogCategory category)
        {
            if (cfg != null && !cfg.RateLimitRecordDroppedCount)
                return;
            Interlocked.Increment(ref _droppedTotal);
            lock (_droppedByEventId)
            {
                _droppedByEventId.TryGetValue(eventId, out long current);
                _droppedByEventId[eventId] = current + 1;
            }
            if (category != EmueraLogCategory.None)
            {
                string categoryName = category.ToString();
                lock (_droppedByCategory)
                {
                    _droppedByCategory.TryGetValue(categoryName, out long current);
                    _droppedByCategory[categoryName] = current + 1;
                }
            }
        }

        public static long GetDroppedTotal() => Interlocked.Read(ref _droppedTotal);

        /// <summary>
        /// 诊断系统可能被后台 Emuera 线程在 Godot 退出阶段调用，不能依赖 Godot.Time 等引擎对象。
        /// Stopwatch 是 CLR 单调时钟，适合日志限流和相对时间戳。
        /// </summary>
        public static long GetMonotonicMilliseconds()
        {
            return (long)(System.Diagnostics.Stopwatch.GetTimestamp() * StopwatchTickToMilliseconds);
        }

        public static Dictionary<string, long> GetDroppedSummary()
        {
            lock (_droppedByEventId)
            {
                return new Dictionary<string, long>(_droppedByEventId);
            }
        }

        public static Dictionary<string, long> GetDroppedCategorySummary()
        {
            lock (_droppedByCategory)
            {
                return new Dictionary<string, long>(_droppedByCategory);
            }
        }

        public static DiagnosticLogRecord BuildRecord(EmueraLogLevel level, EmueraLogCategory category,
            string eventId, string message, string data,
            string member, string source, int line)
        {
            long seq = Interlocked.Increment(ref _sequence);
            return new DiagnosticLogRecord(
                seq,
                DateTimeOffset.UtcNow,
                GetMonotonicMilliseconds(),
                level,
                category,
                eventId ?? "",
                _sessionId ?? "unknown",
                System.Environment.CurrentManagedThreadId,
                source ?? "",
                member ?? "",
                line,
                message ?? "",
                data ?? "");
        }

        public static string RedactText(string text, int maxChars)
        {
            var cfg = Config;
            if (cfg == null || !cfg.LoggingEnabled || !cfg.RedactionEnabled)
                return text;
            if (string.IsNullOrEmpty(text))
                return "";
            maxChars = Math.Max(0, maxChars);
            if (cfg.RedactionReplaceNewlines)
                text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            if (text.Length <= maxChars)
                return text;
            return text.Substring(0, maxChars) + "...";
        }

        public static string RedactPath(string path)
        {
            var cfg = Config;
            if (cfg == null || !cfg.LoggingEnabled || !cfg.RedactionEnabled)
                return path;
            if (string.IsNullOrEmpty(path))
                return "";
            if (cfg.RedactionNormalizePaths)
                path = path.Replace('\\', '/');
            int maxPathChars = Math.Max(0, cfg.RedactionMaxPathChars);
            if (path.Length <= maxPathChars)
                return path;
            return "..." + path.Substring(path.Length - maxPathChars);
        }
    }
}
