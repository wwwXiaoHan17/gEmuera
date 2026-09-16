// GenericUtils.Trace.cs —— 承载滚动/触摸/输入/图像/UI 布局/动态地图诊断追踪与调试开关功能域，自 GenericUtils.cs 拆出（原因：主文件超 2000 行只减不增约束）。
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
    static int scrollTraceEnabled = 0;
    static int scrollTraceSequence = 0;
    static int scrollTraceCoreLinesRemaining = 0;
    static long dynamicMapLastContextTickMs = long.MinValue;

    public static bool ScrollTraceEnabled
    {
        get => Volatile.Read(ref scrollTraceEnabled) != 0;
        set => Volatile.Write(ref scrollTraceEnabled, value ? 1 : 0);
    }

    public static bool IsScrollTraceActive => ScrollTraceEnabled && IsLogEnabled(EmueraLogLevel.Debug, EmueraLogCategory.Script);

    public static void ScrollTrace(string category, string message)
    {
        if (!IsScrollTraceActive)
            return;
        WriteScrollTrace(category, message);
    }

    /// <summary>
    /// 企业级说明：滚动/输入追踪属于 Android 高频路径，必须在 ScrollTrace 开关和日志等级通过后再构造文本。
    /// 该重载用于替换调用点的插值字符串，避免调试关闭时仍产生 GC 分配。
    /// </summary>
    public static void ScrollTrace(string category, Func<string> messageFactory)
    {
        if (!IsScrollTraceActive)
            return;
        string message;
        try
        {
            message = messageFactory != null ? messageFactory() : "";
        }
        catch (Exception ex)
        {
            message = "[LOGGER] scroll trace factory failed: " + ex.GetType().Name + ": " + ex.Message;
        }
        WriteScrollTrace(category, message);
    }

    static void WriteScrollTrace(string category, string message)
    {
        int seq = Interlocked.Increment(ref scrollTraceSequence);
        string formatted = $"{ScrollTracePrefix} #{seq} t={GetTickMs()} {category}: {message}";
        LogInternal(EmueraLogLevel.Debug, EmueraLogCategory.Script, formatted, null, nameof(ScrollTrace), "Scripts/GenericUtils.cs", 0);
    }

    public static void StartScrollTraceCoreWindow(string reason)
    {
        if (!IsScrollTraceActive)
            return;
        Interlocked.Exchange(ref scrollTraceCoreLinesRemaining, ScrollTraceCoreBurstLineCount);
        ScrollTrace("core", $"window_start lines={ScrollTraceCoreBurstLineCount} reason={ClipTrace(reason)}");
    }

    public static void StartScrollTraceCoreWindow(Func<string> reasonFactory)
    {
        if (!IsScrollTraceActive)
            return;
        string reason;
        try
        {
            reason = reasonFactory != null ? reasonFactory() : "";
        }
        catch (Exception ex)
        {
            reason = "[LOGGER] core window reason failed: " + ex.GetType().Name + ": " + ex.Message;
        }
        Interlocked.Exchange(ref scrollTraceCoreLinesRemaining, ScrollTraceCoreBurstLineCount);
        ScrollTrace("core", () => $"window_start lines={ScrollTraceCoreBurstLineCount} reason={ClipTrace(reason)}");
    }

    public static bool TryConsumeScrollTraceCoreLine()
    {
        if (!IsScrollTraceActive)
            return false;
        while (true)
        {
            int current = Volatile.Read(ref scrollTraceCoreLinesRemaining);
            if (current <= 0)
                return false;
            if (Interlocked.CompareExchange(ref scrollTraceCoreLinesRemaining, current - 1, current) == current)
                return true;
        }
    }

    public static string ClipTrace(string value, int maxLength = 96)
    {
        return ClipFlatText(value, maxLength, "...");
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTouchTraceEnabled(string subSwitch = "")
    {
        if (string.IsNullOrEmpty(subSwitch))
            return (_runtimeConfig?.TouchEnabled ?? false) && DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.Touch);
        return DiagnosticLogRouter.IsTouchEnabled(subSwitch);
    }

    /// <summary>
    /// 企业级说明：语句识别调试开关默认关闭，未获用户确认前不开启。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsStatementTraceEnabled(string subSwitch = "")
    {
        if (string.IsNullOrEmpty(subSwitch))
            return (_runtimeConfig?.StatementRecognitionEnabled ?? false) && DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.StatementRecognition);
        return DiagnosticLogRouter.IsStatementRecognitionEnabled(subSwitch);
    }

    /// <summary>
    /// 企业级说明：输入调试开关默认关闭，只在输入链路记录 SUBMIT/CONSUME 等关键事件。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInputTraceEnabled(string subSwitch = "")
    {
        if (string.IsNullOrEmpty(subSwitch))
            return (_runtimeConfig?.InputDebugEnabled ?? false) && DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.Input);
        return DiagnosticLogRouter.IsInputDebugEnabled(subSwitch);
    }

    /// <summary>
    /// 企业级说明：图片调试开关默认关闭，成功日志默认不记录，避免图片热路径分配。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsImageDebugEnabled(string subSwitch = "")
    {
        if (string.IsNullOrEmpty(subSwitch))
            return (_runtimeConfig?.ImageDebugEnabled ?? false) && DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.Sprite);
        return DiagnosticLogRouter.IsImageDebugEnabled(subSwitch);
    }

    /// <summary>
    /// 企业级说明：UI 几何调试开关默认关闭，且默认只记录 mismatch，不记录所有 part。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsUiLayoutTraceEnabled(string subSwitch = "")
    {
        if (string.IsNullOrEmpty(subSwitch))
            return (_runtimeConfig?.UiLayoutEnabled ?? false) && DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.UI);
        return DiagnosticLogRouter.IsUiLayoutEnabled(subSwitch);
    }

    public static bool IsUiLayoutTargetRectTraceEnabled()
    {
        return IsUiLayoutTraceEnabled() && !(_runtimeConfig?.UiLayoutMismatchOnly ?? true) && (_runtimeConfig?.UiLayoutTargetRect ?? false);
    }

    public static bool IsUiLayoutActualRectTraceEnabled()
    {
        return IsUiLayoutTraceEnabled() && !(_runtimeConfig?.UiLayoutMismatchOnly ?? true) && (_runtimeConfig?.UiLayoutActualRect ?? false);
    }

    public static bool IsUiLayoutMismatchTraceEnabled()
    {
        return IsUiLayoutTraceEnabled();
    }

    public static int UiLayoutMismatchThresholdPx => _runtimeConfig?.UiLayoutMismatchThresholdPx ?? 2;

    public static bool IsUiOverlayEnabled() => _runtimeConfig?.UiOverlayEnabled ?? false;

    public static bool UiOverlayTargetRectEnabled => _runtimeConfig?.UiOverlayTargetRect ?? false;
    public static bool UiOverlayActualRectEnabled => _runtimeConfig?.UiOverlayActualRect ?? false;
    public static bool UiOverlayMismatchEnabled => _runtimeConfig?.UiOverlayMismatch ?? true;
    public static bool UiOverlayImageRectEnabled => _runtimeConfig?.UiOverlayImageRect ?? true;
    public static bool UiOverlayButtonRectEnabled => _runtimeConfig?.UiOverlayButtonRect ?? true;
    public static int UiOverlayMaxDrawnRects => _runtimeConfig?.UiOverlayMaxDrawnRects ?? 128;

    /// <summary>
    /// 动态地图诊断只记录证据，不改变滚动、按钮或渲染行为。
    /// 调用方需要先判断此开关，避免在 Android 刷新路径中无意义地构造行快照字符串。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDynamicMapTraceEnabled(string subSwitch = "")
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.LoggingEnabled || !cfg.DynamicMapDebugEnabled)
            return false;
        if (!DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.UI))
            return false;

        if (string.IsNullOrEmpty(subSwitch))
            return true;
        return subSwitch switch
        {
            "line_snapshot" => cfg.DynamicMapLogLineSnapshot,
            "scroll" => cfg.DynamicMapLogScroll,
            "buttons" => cfg.DynamicMapLogButtons,
            _ => false,
        };
    }

    public static bool IsDynamicMapLineSnapshotTraceEnabled => IsDynamicMapTraceEnabled("line_snapshot");
    public static bool IsDynamicMapScrollTraceEnabled => IsDynamicMapTraceEnabled("scroll");
    public static bool IsDynamicMapButtonTraceEnabled => IsDynamicMapTraceEnabled("buttons");

    public static bool ShouldTraceDynamicMap(bool hasBitmapContext)
    {
        var cfg = _runtimeConfig;
        if (!IsDynamicMapTraceEnabled())
            return false;

        long now = DiagnosticLogRouter.GetMonotonicMilliseconds();
        if (hasBitmapContext)
            Volatile.Write(ref dynamicMapLastContextTickMs, now);

        if (!cfg.DynamicMapOnlyBitmapContext)
            return true;
        if (hasBitmapContext)
            return true;

        long last = Volatile.Read(ref dynamicMapLastContextTickMs);
        int windowMs = Math.Max(0, cfg.DynamicMapContextWindowMs);
        return last != long.MinValue && now - last <= windowMs;
    }

    public static void DynamicMapTrace(string eventId, Func<string> messageFactory, Func<string> dataFactory = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsDynamicMapTraceEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.UI, eventId, dataFactory,
            messageFactory, member, file, line);
    }

    public static string BuildDynamicMapLineTailSummary(IReadOnlyList<ConsoleDisplayLine> lines)
    {
        if (lines == null || lines.Count == 0)
            return "none";
        int maxLines = GetDynamicMapMaxLines();
        int start = Math.Max(0, lines.Count - maxLines);
        var sb = new StringBuilder(maxLines * 96);
        for (int i = start; i < lines.Count; i++)
        {
            if (sb.Length > 0)
                sb.Append('|');
            sb.Append(i).Append(':').Append(BuildDynamicMapLineSummary(lines[i], false, false));
        }
        return sb.ToString();
    }

    public static string BuildDynamicMapDeltaLineSummary(IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines)
    {
        if (lines == null || lines.Count == 0)
            return "none";
        int maxLines = GetDynamicMapMaxLines();
        int start = Math.Max(0, lines.Count - maxLines);
        var sb = new StringBuilder(maxLines * 96);
        for (int i = start; i < lines.Count; i++)
        {
            if (sb.Length > 0)
                sb.Append('|');
            sb.Append(i).Append(':').Append(BuildDynamicMapLineSummary(lines[i].Line, true, lines[i].Update));
        }
        return sb.ToString();
    }

    static string BuildDynamicMapLineSummary(ConsoleDisplayLine line, bool includeUpdate, bool update)
    {
        if (line == null)
            return "{null}";

        int commandCount = 0;
        int commandWritten = 0;
        var commands = new StringBuilder(64);
        AccumulateDynamicMapCommandSummary(line, commands, ref commandCount, ref commandWritten, 0);

        int maxTextChars = GetDynamicMapMaxTextChars();
		var sb = new StringBuilder(160);
		sb.Append("{no=").Append(line.LineNo)
			.Append(",logic=").Append(line.IsLogicalLine ? 1 : 0)
            .Append(",tmp=").Append(line.IsTemporary ? 1 : 0)
            .Append(",end=").Append(line.IsLineEnd ? 1 : 0)
            .Append(",seg=").Append(line.Buttons?.Length ?? 0)
            .Append(",cmd=").Append(commandCount);
        if (includeUpdate)
            sb.Append(",upd=").Append(update ? 1 : 0);
        if (commands.Length > 0)
            sb.Append(",cmds=").Append(commands);
        sb.Append(",text=").Append(CompactTraceValue(line.ToString(), maxTextChars)).Append('}');
        return sb.ToString();
    }

    static void AccumulateDynamicMapCommandSummary(ConsoleDisplayLine line, StringBuilder commands, ref int commandCount, ref int commandWritten, int depth)
    {
        if (line?.Buttons == null || depth > 4)
            return;
        for (int i = 0; i < line.Buttons.Length; i++)
        {
            var button = line.Buttons[i];
            if (button == null)
                continue;
            if (button.IsButton)
            {
                commandCount++;
                if (commandWritten < 4)
                {
                    if (commands.Length > 0)
                        commands.Append(',');
                    commands.Append(CompactTraceValue(button.Inputs, 24))
                        .Append(':')
                        .Append(CompactTraceValue(button.ToString(), 24))
                        .Append("@g")
                        .Append(button.Generation);
                    commandWritten++;
                }
            }

            var parts = button.StrArray;
            if (parts == null)
                continue;
            for (int j = 0; j < parts.Length; j++)
            {
                if (parts[j] is ConsoleDivPart div && div.Children != null)
                {
                    for (int k = 0; k < div.Children.Length; k++)
                        AccumulateDynamicMapCommandSummary(div.Children[k], commands, ref commandCount, ref commandWritten, depth + 1);
                }
            }
        }
    }

    static int GetDynamicMapMaxLines()
    {
        return Math.Min(64, Math.Max(1, _runtimeConfig?.DynamicMapMaxLines ?? 12));
    }

    static int GetDynamicMapMaxTextChars()
    {
        return Math.Min(240, Math.Max(8, _runtimeConfig?.DynamicMapMaxTextChars ?? 48));
    }

    static string CompactTraceValue(string value, int maxLength)
    {
        return ClipTrace(value, maxLength)
            .Replace(' ', '_')
            .Replace('|', '/')
            .Replace(';', ',');
    }

    public static string RedactTracePath(string path) => DiagnosticLogRouter.RedactPath(path);


    /// <summary>
    /// 企业级说明：输出结构化触摸诊断日志，event_id 与 data 原样写入结构化记录。
    /// message 延迟构造，仅在开关和限流通过后执行。
    /// 调用前建议先通过 IsTouchTraceEnabled 判断，避免热路径字符串分配。
    /// </summary>
    public static void TouchTrace(string eventId, string message, string data = "",
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsTouchTraceEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Touch, eventId, data,
            () => DiagnosticLogRouter.RedactText(message, _runtimeConfig?.LoggingMaxMessageChars ?? MaxLogMessageChars),
            member, file, line);
    }

    public static void TouchTrace(string eventId, Func<string> messageFactory, Func<string> dataFactory = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsTouchTraceEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Touch, eventId, dataFactory,
            () => DiagnosticLogRouter.RedactText(messageFactory?.Invoke(), _runtimeConfig?.LoggingMaxMessageChars ?? MaxLogMessageChars),
            member, file, line);
    }

    /// <summary>
    /// 企业级说明：输出结构化语句识别诊断日志，event_id 与 data 原样写入结构化记录。
    /// 脚本文本默认截断到 120 字符，防止长行爆炸。
    /// </summary>
    public static void StatementTrace(string eventId, string message, string data = "",
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsStatementTraceEnabled())
            return;
        int maxChars = _runtimeConfig?.RedactionMaxScriptTextChars ?? 120;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.StatementRecognition, eventId, data,
            () => DiagnosticLogRouter.RedactText(message, maxChars), member, file, line);
    }

    /// <summary>
    /// 企业级说明：输出结构化输入诊断日志，event_id 与 data 原样写入结构化记录。
    /// 输入文本截断到 64 字符，不记录完整用户输入。
    /// </summary>
    public static void InputTrace(string eventId, string message, string data = "",
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsInputTraceEnabled())
            return;
        int maxChars = _runtimeConfig?.RedactionMaxUserTextChars ?? 64;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Input, eventId, data,
            () => DiagnosticLogRouter.RedactText(message, maxChars), member, file, line);
    }

    public static void InputTrace(string eventId, Func<string> messageFactory, Func<string> dataFactory = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsInputTraceEnabled())
            return;
        int maxChars = _runtimeConfig?.RedactionMaxUserTextChars ?? 64;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Input, eventId, dataFactory,
            () => DiagnosticLogRouter.RedactText(messageFactory?.Invoke(), maxChars), member, file, line);
    }

    /// <summary>
    /// 企业级说明：输出结构化性能报告日志，event_id 与 data 原样写入结构化记录（docs/logging-convention.md §2）。
    /// 与 InputTrace 家族对称，但不做文本截断——性能报告不含用户输入。message/data 延迟构造，
    /// 仅在等级/类别/限流全部通过后执行。
    /// </summary>
    public static void PerfTrace(string eventId, Func<string> messageFactory, Func<string> dataFactory = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsLogEnabled(EmueraLogLevel.Info, EmueraLogCategory.Performance))
            return;
        LogStructured(EmueraLogLevel.Info, EmueraLogCategory.Performance, eventId, dataFactory,
            messageFactory, member, file, line);
    }

    /// <summary>
    /// 企业级说明：输出结构化图片诊断日志，event_id 与 data 原样写入结构化记录。
    /// 路径脱敏，长路径截断，不输出像素或二进制。
    /// </summary>
    public static void ImageTrace(string eventId, string message, string data = "",
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsImageDebugEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Sprite, eventId, data,
            () => message, member, file, line);
    }

    public static void ImageTrace(string eventId, Func<string> messageFactory, Func<string> dataFactory = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsImageDebugEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Sprite, eventId, dataFactory,
            messageFactory, member, file, line);
    }

    /// <summary>
    /// 企业级说明：输出结构化 UI 几何诊断日志，event_id 与 data 原样写入结构化记录。
    /// 默认只记录 mismatch，不记录所有 part 矩形。
    /// </summary>
    public static void UiLayoutTrace(string eventId, string message, string data = "",
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsUiLayoutTraceEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.UI, eventId, data,
            () => message, member, file, line);
    }

    public static void UiLayoutTrace(string eventId, Func<string> messageFactory, Func<string> dataFactory = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsUiLayoutTraceEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.UI, eventId, dataFactory,
            messageFactory, member, file, line);
    }

    /// <summary>
    /// 企业级说明：输入复现轨迹捕获，默认关闭，只在开启后保留最近 N 条。
    /// 不记录完整用户输入文本，按 max_text_chars 截断。
}
