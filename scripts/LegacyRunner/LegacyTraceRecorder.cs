using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace gEmuera.LegacyRunner
{
    public sealed class LegacyTraceRecorder
    {
        readonly object _gate = new object();
        readonly List<LegacyTraceEvent> _events;
        readonly int _capacity;
        long _sequence;
        long _droppedEventCount;
        long _rngStreamId;

        public LegacyTraceRecorder(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException("capacity");
            _capacity = capacity;
            _events = new List<LegacyTraceEvent>(Math.Min(capacity, 4096));
        }

        public bool Overflowed { get { return Interlocked.Read(ref _droppedEventCount) > 0; } }

        public long RecordedCount
        {
            get
            {
                lock (_gate)
                    return _events.Count;
            }
        }

        public long DroppedEventCount { get { return Interlocked.Read(ref _droppedEventCount); } }

        public bool TryRecord(string category, string kind, string threadOwner, string orderingPoint,
            string completionMode, LegacyTracePayload payload)
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("Trace category must not be empty.", "category");
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("Trace kind must not be empty.", "kind");
            if (payload == null)
                throw new ArgumentNullException("payload");

            lock (_gate)
            {
                if (_events.Count >= _capacity)
                {
                    Interlocked.Increment(ref _droppedEventCount);
                    return false;
                }
                long sequence = Interlocked.Increment(ref _sequence);
                _events.Add(new LegacyTraceEvent
                {
                    Sequence = sequence,
                    Category = category,
                    Kind = kind,
                    ThreadOwner = threadOwner ?? LegacyTraceThreadOwner.Unknown,
                    OrderingPoint = orderingPoint ?? "",
                    CompletionMode = completionMode ?? LegacyTraceCompletionMode.ObserveOnly,
                    ObserverMonotonicTicks = Stopwatch.GetTimestamp(),
                    ObserverUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    Payload = payload.Clone(null)
                });
                return true;
            }
        }

        public long NextRngStreamId()
        {
            return Interlocked.Increment(ref _rngStreamId);
        }

        public LegacyTraceSnapshot Snapshot()
        {
            lock (_gate)
            {
                var events = new LegacyTraceEvent[_events.Count];
                for (int index = 0; index < _events.Count; index++)
                    events[index] = _events[index].Clone(false, null);
                return new LegacyTraceSnapshot
                {
                    Capacity = _capacity,
                    RecordedCount = events.Length,
                    Overflowed = _droppedEventCount > 0,
                    DroppedEventCount = _droppedEventCount,
                    Events = events
                };
            }
        }
    }

    public static class LegacyTrace
    {
        static LegacyTraceRecorder _current;
        static long _displayTransactionId;

        public static bool IsEnabled { get { return Volatile.Read(ref _current) != null; } }

        public static long RecordedCount
        {
            get
            {
                LegacyTraceRecorder recorder = Volatile.Read(ref _current);
                return recorder == null ? -1 : recorder.RecordedCount;
            }
        }

        public static long DroppedEventCount
        {
            get
            {
                LegacyTraceRecorder recorder = Volatile.Read(ref _current);
                return recorder == null ? -1 : recorder.DroppedEventCount;
            }
        }

        public static LegacyTraceRecorder Enable(int capacity)
        {
            var recorder = new LegacyTraceRecorder(capacity);
            Interlocked.Exchange(ref _displayTransactionId, 0);
            Interlocked.Exchange(ref _current, recorder);
            return recorder;
        }

        public static LegacyTraceRecorder Disable()
        {
            return Interlocked.Exchange(ref _current, null);
        }

        public static LegacyTraceSnapshot Snapshot()
        {
            LegacyTraceRecorder recorder = Volatile.Read(ref _current);
            return recorder == null ? null : recorder.Snapshot();
        }

        public static bool TryRecordRunner(string kind, string data)
        {
            LegacyTraceRecorder recorder = Volatile.Read(ref _current);
            return recorder != null && recorder.TryRecord(LegacyTraceCategory.Runner, kind,
                LegacyTraceThreadOwner.GodotMain, LegacyTraceOrderingPoint.RunnerLifecycle,
                LegacyTraceCompletionMode.ObserveOnly, new LegacyTraceRunnerPayload { Data = data ?? "" });
        }

        public static bool TryRecordClock(string kind, string source, string sampleKind, string value, string threadOwner)
        {
            LegacyTraceRecorder recorder = Volatile.Read(ref _current);
            return recorder != null && recorder.TryRecord(LegacyTraceCategory.Clock, kind, threadOwner,
                LegacyTraceOrderingPoint.ClockObservation, LegacyTraceCompletionMode.CoreImmediateLegacy,
                new LegacyTraceClockPayload { Source = source ?? "", SampleKind = sampleKind ?? "", Value = value ?? "" });
        }

        public static long RegisterRng(string provider, string seedOrigin, string seedValue)
        {
            LegacyTraceRecorder recorder = Volatile.Read(ref _current);
            if (recorder == null)
                return 0;
            long streamId = recorder.NextRngStreamId();
            bool recorded = recorder.TryRecord(LegacyTraceCategory.Rng, "seeded", LegacyTraceThreadOwner.LegacyVm,
                LegacyTraceOrderingPoint.RngCall, LegacyTraceCompletionMode.CoreImmediateLegacy,
                new LegacyTraceRngPayload
                {
                    Provider = provider ?? "",
                    StreamId = streamId,
                    CallIndex = 0,
                    Operation = "seed",
                    SeedOrigin = seedOrigin ?? "",
                    Value = seedValue ?? ""
                });
            return recorded ? streamId : 0;
        }

        public static bool TryRecordRngCall(string provider, long streamId, long callIndex, string operation, string value)
        {
            LegacyTraceRecorder recorder = Volatile.Read(ref _current);
            return recorder != null && recorder.TryRecord(LegacyTraceCategory.Rng, "value_produced",
                LegacyTraceThreadOwner.LegacyVm, LegacyTraceOrderingPoint.RngCall,
                LegacyTraceCompletionMode.CoreImmediateLegacy,
                new LegacyTraceRngPayload
                {
                    Provider = provider ?? "",
                    StreamId = streamId,
                    CallIndex = callIndex,
                    Operation = operation ?? "",
                    SeedOrigin = "",
                    Value = value ?? ""
                });
        }

        public static bool TryRecordWait(string kind, long requestId, string inputType, bool needValue,
            bool oneInput, bool noFocus, long timelimitMs, long buttonGeneration)
        {
            LegacyTraceRecorder recorder = Volatile.Read(ref _current);
            return recorder != null && recorder.TryRecord(LegacyTraceCategory.Wait, kind,
                LegacyTraceThreadOwner.LegacyVm, LegacyTraceOrderingPoint.WaitRequest,
                LegacyTraceCompletionMode.WaitPortLegacy,
                new LegacyTraceWaitPayload
                {
                    RequestId = requestId,
                    InputType = inputType ?? "",
                    NeedValue = needValue,
                    OneInput = oneInput,
                    NoFocus = noFocus,
                    TimelimitMs = timelimitMs,
                    ButtonGeneration = buttonGeneration
                });
        }

        public static bool TryRecordInput(string kind, string threadOwner, string orderingPoint, string value,
            bool fromButton, bool skip, int mouseButton, bool accepted, string inputType, long buttonGeneration)
        {
            LegacyTraceRecorder recorder = Volatile.Read(ref _current);
            return recorder != null && recorder.TryRecord(LegacyTraceCategory.Input, kind, threadOwner, orderingPoint,
                LegacyTraceCompletionMode.WaitPortLegacy,
                new LegacyTraceInputPayload
                {
                    Value = value ?? "",
                    FromButton = fromButton,
                    Skip = skip,
                    MouseButton = mouseButton,
                    Accepted = accepted,
                    InputType = inputType ?? "",
                    ButtonGeneration = buttonGeneration
                });
        }

        public static long TryRecordDisplayCommit(string kind, int removeBottomCount, int lineCount, bool update,
            long buttonGeneration, string scrollIntent, int dataOnlyLineCount)
        {
            LegacyTraceRecorder recorder = Volatile.Read(ref _current);
            if (recorder == null)
                return 0;
            long transactionId = Interlocked.Increment(ref _displayTransactionId);
            bool recorded = recorder.TryRecord(LegacyTraceCategory.Display, kind,
                LegacyTraceThreadOwner.LegacyVm, LegacyTraceOrderingPoint.DisplayCommit,
                LegacyTraceCompletionMode.CommitThenProjectLegacy,
                new LegacyTraceDisplayPayload
                {
                    TransactionId = transactionId,
                    RemoveBottomCount = removeBottomCount,
                    LineCount = lineCount,
                    Update = update,
                    ButtonGeneration = buttonGeneration,
                    ScrollIntent = scrollIntent ?? "",
                    DataOnlyLineCount = dataOnlyLineCount
                });
            return recorded ? transactionId : 0;
        }

        public static bool TryRecordDisplayProjection(string kind, int projectedActionCount)
        {
            return TryRecordDisplayProjection(kind, 0, projectedActionCount, false, -1, "projection_batch", 0) != 0;
        }

        public static long TryRecordDisplayProjection(string kind, int removeBottomCount, int lineCount, bool update,
            long buttonGeneration, string scrollIntent, int dataOnlyLineCount)
        {
            LegacyTraceRecorder recorder = Volatile.Read(ref _current);
            if (recorder == null)
                return 0;
            long transactionId = Interlocked.Increment(ref _displayTransactionId);
            bool recorded = recorder.TryRecord(LegacyTraceCategory.Display, kind,
                LegacyTraceThreadOwner.GodotMain, LegacyTraceOrderingPoint.UiProjection,
                LegacyTraceCompletionMode.CommitThenProjectLegacy,
                new LegacyTraceDisplayPayload
                {
                    TransactionId = transactionId,
                    RemoveBottomCount = removeBottomCount,
                    LineCount = lineCount,
                    Update = update,
                    ButtonGeneration = buttonGeneration,
                    ScrollIntent = scrollIntent ?? "",
                    DataOnlyLineCount = dataOnlyLineCount
                });
            return recorded ? transactionId : 0;
        }

        public static bool TryRecordEffect(string kind, string effectType, string target, string operation, int channel, int value)
        {
            LegacyTraceRecorder recorder = Volatile.Read(ref _current);
            return recorder != null && recorder.TryRecord(LegacyTraceCategory.Effect, kind,
                LegacyTraceThreadOwner.LegacyVm, LegacyTraceOrderingPoint.EffectEnqueue,
                LegacyTraceCompletionMode.FireAndContinueLegacy,
                new LegacyTraceEffectPayload
                {
                    EffectType = effectType ?? "",
                    Target = target ?? "",
                    Operation = operation ?? "",
                    Channel = channel,
                    Value = value
                });
        }

        public static bool TryRecordError(string kind, string errorType, string code, string message, string sourceSymbol)
        {
            LegacyTraceRecorder recorder = Volatile.Read(ref _current);
            return recorder != null && recorder.TryRecord(LegacyTraceCategory.Error, kind,
                LegacyTraceThreadOwner.Unknown, LegacyTraceOrderingPoint.ErrorSink,
                LegacyTraceCompletionMode.Fault,
                new LegacyTraceErrorPayload
                {
                    ErrorType = errorType ?? "",
                    Code = code ?? "",
                    Message = message ?? "",
                    SourceSymbol = sourceSymbol ?? ""
                });
        }
    }
}
