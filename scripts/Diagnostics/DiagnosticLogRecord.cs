using System;
using System.Text;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 企业级说明：单条结构化日志记录，字段顺序稳定，便于人和 AI 都能快速 grep/解析。
    /// 换行、制表符、反斜杠、双引号已转义，确保单行格式不破坏。
    /// </summary>
    public readonly struct DiagnosticLogRecord
    {
        public readonly long Seq;
        public readonly DateTimeOffset Utc;
        public readonly long MonoMs;
        public readonly EmueraLogLevel Level;
        public readonly EmueraLogCategory Category;
        public readonly string EventId;
        public readonly string Session;
        public readonly int ThreadId;
        public readonly string Source;
        public readonly string Member;
        public readonly int Line;
        public readonly string Message;
        public readonly string Data;

        public DiagnosticLogRecord(long seq, DateTimeOffset utc, long monoMs, EmueraLogLevel level,
            EmueraLogCategory category, string eventId, string session, int threadId,
            string source, string member, int line, string message, string data)
        {
            Seq = seq;
            Utc = utc;
            MonoMs = monoMs;
            Level = level;
            Category = category;
            EventId = eventId ?? "";
            Session = session ?? "";
            ThreadId = threadId;
            Source = source ?? "";
            Member = member ?? "";
            Line = line;
            Message = message ?? "";
            Data = data ?? "";
        }

        public string FormatForGodot()
        {
            // 2026-09-19 审计修复：Message 此前未转义换行，多行消息会污染 Godot 控制台镜像
            //（仅控制台；导出格式一直有转义）。与 data 同用单行转义。
            string text = $"[{Level.ToString().ToUpperInvariant()}][{Category}] {EventId} {Source}:{Line} {Member} | {EscapeForSingleLine(Message)}";
            if (!string.IsNullOrEmpty(Data) && EventId.StartsWith("PERF.", StringComparison.OrdinalIgnoreCase))
                text += " | data=" + EscapeForSingleLine(Data);
            return text;
        }

        public string FormatForExport()
        {
            var sb = new StringBuilder(256);
            sb.Append("seq=").Append(Seq.ToString("D6"));
            sb.Append(" utc=").Append(Utc.ToString("O"));
            sb.Append(" mono_ms=").Append(MonoMs);
            sb.Append(" level=").Append(Level.ToString().ToUpperInvariant());
            sb.Append(" category=").Append(Category);
            sb.Append(" event_id=").Append(EventId);
            sb.Append(" session=").Append(Session);
            sb.Append(" thread=").Append(ThreadId);
            sb.Append(" source=").Append(Source);
            if (Line > 0)
                sb.Append(":").Append(Line);
            sb.Append(" member=").Append(Member);
            sb.Append(" message=\"").Append(EscapeForSingleLine(Message)).Append('"');
            if (!string.IsNullOrEmpty(Data))
                sb.Append(" data=\"").Append(EscapeForSingleLine(Data)).Append('"');
            return sb.ToString();
        }

        static string EscapeForSingleLine(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        }
    }
}
