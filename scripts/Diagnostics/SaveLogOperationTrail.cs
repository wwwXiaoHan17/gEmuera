using System;
using System.Text;
using System.Threading;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 企业级说明：save_log 操作轨迹是手机端唯一默认保留的轻量定位信息。
    /// 它只在核心输入被消费时写入最近 N 条记录，不接入触摸拖动、渲染和脚本循环热路径，
    /// 便于导出的 gemuera 日志在电脑端快速对齐 emuera 输出日志中的位置。
    /// </summary>
    public sealed class SaveLogOperationTrail
    {
        readonly object _lock = new object();
        readonly OperationEntry[] _buffer;
        int _count;
        int _start;
        long _sequence;

        public SaveLogOperationTrail(int capacity)
        {
            _buffer = new OperationEntry[Math.Max(1, capacity)];
        }

        /// <summary>
        /// Drops entries captured by the previous legacy session while keeping
        /// the reusable ring allocation.  Sequence numbers restart with the
        /// next candidate because this trail is a session diagnostic, not a
        /// process-wide event journal.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _count = 0;
                _start = 0;
                _sequence = 0;
            }
        }

        public long Capture(string kind, string input, string codeBefore, string codeAfter,
            string waitState, bool consumed, string effect)
        {
            lock (_lock)
            {
                long sequence = Interlocked.Increment(ref _sequence);
                int index = (_start + _count) % _buffer.Length;
                if (_count >= _buffer.Length)
                {
                    _start = (_start + 1) % _buffer.Length;
                }
                else
                {
                    _count++;
                }

                _buffer[index] = new OperationEntry(
                    sequence,
                    DateTimeOffset.UtcNow,
                    DiagnosticLogRouter.GetMonotonicMilliseconds(),
                    kind ?? "",
                    input ?? "",
                    codeBefore ?? "",
                    codeAfter ?? "",
                    waitState ?? "",
                    consumed,
                    effect ?? "");
                return sequence;
            }
        }

        public string BuildExportText(int maxEntries)
        {
            lock (_lock)
            {
                int take = Math.Min(Math.Max(1, maxEntries), _count);
                int skip = Math.Max(0, _count - take);
                var sb = new StringBuilder(take * 256 + 256);
                sb.AppendLine("# save_log operation trail");
                sb.AppendLine("# 最近 " + take + " / " + _count + " 次核心输入操作，用于和 emuera_*.log 对齐问题位置。");
                sb.AppendLine("# code_before/code_after 分别表示输入消费前后的核心脚本位置；输入文本已按诊断脱敏策略截断。");
                for (int i = skip; i < _count; i++)
                {
                    var e = _buffer[(_start + i) % _buffer.Length];
                    sb.Append("operation_seq=").Append(e.Sequence.ToString("D6"));
                    sb.Append(" utc=").Append(e.Utc.ToString("O"));
                    sb.Append(" mono_ms=").Append(e.MonoMs);
                    sb.Append(" kind=").Append(e.Kind);
                    sb.Append(" input=\"").Append(Escape(e.Input)).Append('"');
                    sb.Append(" consumed=").Append(e.Consumed);
                    sb.Append(" wait=\"").Append(Escape(e.WaitState)).Append('"');
                    sb.Append(" effect=\"").Append(Escape(e.Effect)).Append('"');
                    sb.Append(" code_before=\"").Append(Escape(e.CodeBefore)).Append('"');
                    sb.Append(" code_after=\"").Append(Escape(e.CodeAfter)).Append('"');
                    sb.AppendLine();
                }
                return sb.ToString();
            }
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        }

        readonly struct OperationEntry
        {
            public readonly long Sequence;
            public readonly DateTimeOffset Utc;
            public readonly long MonoMs;
            public readonly string Kind;
            public readonly string Input;
            public readonly string CodeBefore;
            public readonly string CodeAfter;
            public readonly string WaitState;
            public readonly bool Consumed;
            public readonly string Effect;

            public OperationEntry(long sequence, DateTimeOffset utc, long monoMs, string kind, string input,
                string codeBefore, string codeAfter, string waitState, bool consumed, string effect)
            {
                Sequence = sequence;
                Utc = utc;
                MonoMs = monoMs;
                Kind = kind;
                Input = input;
                CodeBefore = codeBefore;
                CodeAfter = codeAfter;
                WaitState = waitState;
                Consumed = consumed;
                Effect = effect;
            }
        }
    }
}
