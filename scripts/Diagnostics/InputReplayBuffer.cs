using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Godot;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 企业级说明：输入复现轨迹只在开启后记录最近 N 条事件，默认关闭，避免热路径分配。
    /// 不记录完整用户文本，坐标和文本均截断，导出时生成 input-replay.txt。
    /// </summary>
    public sealed class InputReplayBuffer
    {
        readonly object _lock = new object();
        readonly ReplayEntry[] _buffer;
        int _count;
        int _start;
        long _sequence;
        readonly long _startTickMs;

        public InputReplayBuffer(int capacity)
        {
            _buffer = new ReplayEntry[Math.Max(16, capacity)];
            _startTickMs = DiagnosticLogRouter.GetMonotonicMilliseconds();
        }

        public int Count
        {
            get { lock (_lock) return _count; }
        }

        public void Capture(string kind, string input, Vector2 globalPos, Vector2 localPos,
            long inputId, string waitState, bool consumed)
        {
            lock (_lock)
            {
                int index = (_start + _count) % _buffer.Length;
                if (_count >= _buffer.Length)
                {
                    _start = (_start + 1) % _buffer.Length;
                }
                else
                {
                    _count++;
                }
                _buffer[index] = new ReplayEntry(
                    Interlocked.Increment(ref _sequence),
                    Math.Max(0, DiagnosticLogRouter.GetMonotonicMilliseconds() - _startTickMs),
                    kind ?? "",
                    input ?? "",
                    globalPos,
                    localPos,
                    inputId,
                    waitState ?? "",
                    consumed);
            }
        }

        public string BuildExportText()
        {
            lock (_lock)
            {
                var sb = new StringBuilder(_count * 120 + 128);
                sb.AppendLine("# input replay");
                sb.AppendLine("# session=" + DiagnosticLogRouter.SessionId);
                sb.AppendLine("# count=" + _count);
                for (int i = 0; i < _count; i++)
                {
                    var e = _buffer[(_start + i) % _buffer.Length];
                    sb.Append("seq=").Append(e.Sequence);
                    sb.Append(" dt_ms=").Append(e.DtMs);
                    sb.Append(" kind=").Append(e.Kind);
                    sb.Append(" input=\"").Append(Escape(e.Input)).Append('"');
                    sb.Append(" global=").Append(e.GlobalPos.ToString());
                    sb.Append(" local=").Append(e.LocalPos.ToString());
                    sb.Append(" input_id=").Append(e.InputId);
                    sb.Append(" wait=").Append(e.WaitState);
                    sb.Append(" consumed=").Append(e.Consumed);
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

        readonly struct ReplayEntry
        {
            public readonly long Sequence;
            public readonly long DtMs;
            public readonly string Kind;
            public readonly string Input;
            public readonly Vector2 GlobalPos;
            public readonly Vector2 LocalPos;
            public readonly long InputId;
            public readonly string WaitState;
            public readonly bool Consumed;

            public ReplayEntry(long sequence, long dtMs, string kind, string input,
                Vector2 globalPos, Vector2 localPos, long inputId, string waitState, bool consumed)
            {
                Sequence = sequence;
                DtMs = dtMs;
                Kind = kind;
                Input = input;
                GlobalPos = globalPos;
                LocalPos = localPos;
                InputId = inputId;
                WaitState = waitState;
                Consumed = consumed;
            }
        }
    }
}
