using System;
using System.Collections.Generic;

namespace gEmuera.LegacyRunner
{
    public static class LegacyTraceCategory
    {
        public const string Clock = "clock";
        public const string Rng = "rng";
        public const string Wait = "wait";
        public const string Input = "input";
        public const string Display = "display";
        public const string Effect = "effect";
        public const string Error = "error";
        public const string Runner = "runner";
    }

    public static class LegacyTraceThreadOwner
    {
        public const string GodotMain = "godot_main";
        public const string LegacyVm = "legacy_vm";
        public const string ExternalHarness = "external_harness";
        public const string Unknown = "unknown";
    }

    public static class LegacyTraceOrderingPoint
    {
        public const string RunnerLifecycle = "runner_lifecycle";
        public const string ClockObservation = "clock_observation";
        public const string RngCall = "rng_call";
        public const string WaitRequest = "wait_request";
        public const string InputSubmission = "input_submission";
        public const string VmConsumption = "vm_consumption";
        public const string DisplayCommit = "display_commit";
        public const string UiProjection = "ui_projection";
        public const string EffectEnqueue = "effect_enqueue";
        public const string ErrorSink = "error_sink";
    }

    public static class LegacyTraceCompletionMode
    {
        public const string ObserveOnly = "observe_only";
        public const string CoreImmediateLegacy = "core_immediate_legacy";
        public const string WaitPortLegacy = "wait_port_legacy";
        public const string CommitThenProjectLegacy = "commit_then_project_legacy";
        public const string FireAndContinueLegacy = "fire_and_continue_legacy";
        public const string Fault = "fault";
    }

    public abstract class LegacyTracePayload
    {
        internal abstract LegacyTracePayload Clone(Func<string, string> textNormalizer);

        protected static string Normalize(string value, Func<string, string> textNormalizer)
        {
            string safe = value ?? "";
            return textNormalizer == null ? safe : textNormalizer(safe) ?? "";
        }
    }

    public sealed class LegacyTraceRunnerPayload : LegacyTracePayload
    {
        public string Data { get; set; }

        internal override LegacyTracePayload Clone(Func<string, string> textNormalizer)
        {
            return new LegacyTraceRunnerPayload { Data = Normalize(Data, textNormalizer) };
        }
    }

    public sealed class LegacyTraceClockPayload : LegacyTracePayload
    {
        public string Source { get; set; }
        public string SampleKind { get; set; }
        public string Value { get; set; }

        internal override LegacyTracePayload Clone(Func<string, string> textNormalizer)
        {
            return new LegacyTraceClockPayload
            {
                Source = Normalize(Source, textNormalizer),
                SampleKind = SampleKind ?? "",
                Value = Value ?? ""
            };
        }
    }

    public sealed class LegacyTraceRngPayload : LegacyTracePayload
    {
        public string Provider { get; set; }
        public long StreamId { get; set; }
        public long CallIndex { get; set; }
        public string Operation { get; set; }
        public string SeedOrigin { get; set; }
        public string Value { get; set; }

        internal override LegacyTracePayload Clone(Func<string, string> textNormalizer)
        {
            return new LegacyTraceRngPayload
            {
                Provider = Provider ?? "",
                StreamId = StreamId,
                CallIndex = CallIndex,
                Operation = Operation ?? "",
                SeedOrigin = SeedOrigin ?? "",
                Value = Value ?? ""
            };
        }
    }

    public sealed class LegacyTraceWaitPayload : LegacyTracePayload
    {
        public long RequestId { get; set; }
        public string InputType { get; set; }
        public bool NeedValue { get; set; }
        public bool OneInput { get; set; }
        public bool NoFocus { get; set; }
        public long TimelimitMs { get; set; }
        public long ButtonGeneration { get; set; }

        internal override LegacyTracePayload Clone(Func<string, string> textNormalizer)
        {
            return new LegacyTraceWaitPayload
            {
                RequestId = RequestId,
                InputType = InputType ?? "",
                NeedValue = NeedValue,
                OneInput = OneInput,
                NoFocus = NoFocus,
                TimelimitMs = TimelimitMs,
                ButtonGeneration = ButtonGeneration
            };
        }
    }

    public sealed class LegacyTraceInputPayload : LegacyTracePayload
    {
        public string Value { get; set; }
        public bool FromButton { get; set; }
        public bool Skip { get; set; }
        public int MouseButton { get; set; }
        public bool Accepted { get; set; }
        public string InputType { get; set; }
        public long ButtonGeneration { get; set; }

        internal override LegacyTracePayload Clone(Func<string, string> textNormalizer)
        {
            return new LegacyTraceInputPayload
            {
                Value = Value ?? "",
                FromButton = FromButton,
                Skip = Skip,
                MouseButton = MouseButton,
                Accepted = Accepted,
                InputType = InputType ?? "",
                ButtonGeneration = ButtonGeneration
            };
        }
    }

    public sealed class LegacyTraceDisplayPayload : LegacyTracePayload
    {
        public long TransactionId { get; set; }
        public int RemoveBottomCount { get; set; }
        public int LineCount { get; set; }
        public bool Update { get; set; }
        public long ButtonGeneration { get; set; }
        public string ScrollIntent { get; set; }
        public int DataOnlyLineCount { get; set; }

        internal override LegacyTracePayload Clone(Func<string, string> textNormalizer)
        {
            return new LegacyTraceDisplayPayload
            {
                TransactionId = TransactionId,
                RemoveBottomCount = RemoveBottomCount,
                LineCount = LineCount,
                Update = Update,
                ButtonGeneration = ButtonGeneration,
                ScrollIntent = ScrollIntent ?? "",
                DataOnlyLineCount = DataOnlyLineCount
            };
        }
    }

    public sealed class LegacyTraceEffectPayload : LegacyTracePayload
    {
        public string EffectType { get; set; }
        public string Target { get; set; }
        public string Operation { get; set; }
        public int Channel { get; set; }
        public int Value { get; set; }

        public LegacyTraceEffectPayload()
        {
            Channel = -1;
        }

        internal override LegacyTracePayload Clone(Func<string, string> textNormalizer)
        {
            return new LegacyTraceEffectPayload
            {
                EffectType = EffectType ?? "",
                Target = Normalize(Target, textNormalizer),
                Operation = Operation ?? "",
                Channel = Channel,
                Value = Value
            };
        }
    }

    public sealed class LegacyTraceErrorPayload : LegacyTracePayload
    {
        public string ErrorType { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string SourceSymbol { get; set; }

        internal override LegacyTracePayload Clone(Func<string, string> textNormalizer)
        {
            return new LegacyTraceErrorPayload
            {
                ErrorType = ErrorType ?? "",
                Code = Code ?? "",
                Message = Normalize(Message, textNormalizer),
                SourceSymbol = Normalize(SourceSymbol, textNormalizer)
            };
        }
    }

    public sealed class LegacyTraceEvent
    {
        public long Sequence { get; set; }
        public string Category { get; set; }
        public string Kind { get; set; }
        public string ThreadOwner { get; set; }
        public string OrderingPoint { get; set; }
        public string CompletionMode { get; set; }
        public long ObserverMonotonicTicks { get; set; }
        public string ObserverUtc { get; set; }
        public object Payload { get; set; }

        internal LegacyTraceEvent Clone(bool canonical, Func<string, string> textNormalizer)
        {
            return new LegacyTraceEvent
            {
                Sequence = Sequence,
                Category = Category ?? "",
                Kind = Kind ?? "",
                ThreadOwner = ThreadOwner ?? "",
                OrderingPoint = OrderingPoint ?? "",
                CompletionMode = CompletionMode ?? "",
                ObserverMonotonicTicks = canonical ? 0 : ObserverMonotonicTicks,
                ObserverUtc = canonical ? "<NORMALIZED_UTC>" : ObserverUtc ?? "",
                Payload = Payload is LegacyTracePayload
                    ? ((LegacyTracePayload)Payload).Clone(textNormalizer)
                    : new LegacyTraceRunnerPayload()
            };
        }
    }

    public sealed class LegacyTraceSnapshot
    {
        public string SchemaVersion { get; set; }
        public string CoverageStatus { get; set; }
        public int Capacity { get; set; }
        public int RecordedCount { get; set; }
        public bool Overflowed { get; set; }
        public long DroppedEventCount { get; set; }
        public string[] NormalizationRules { get; set; }
        public LegacyTraceEvent[] Events { get; set; }

        public LegacyTraceSnapshot()
        {
            SchemaVersion = "1.0.0";
            CoverageStatus = "Partial";
            NormalizationRules = new string[0];
            Events = new LegacyTraceEvent[0];
        }

        public LegacyTraceSnapshot ToCanonical()
        {
            return ToCanonical(null);
        }

        public LegacyTraceSnapshot ToCanonical(Func<string, string> textNormalizer)
        {
            var normalized = new LegacyTraceEvent[Events.Length];
            for (int index = 0; index < Events.Length; index++)
                normalized[index] = Events[index].Clone(true, textNormalizer);
            return new LegacyTraceSnapshot
            {
                SchemaVersion = SchemaVersion,
                CoverageStatus = CoverageStatus,
                Capacity = Capacity,
                RecordedCount = RecordedCount,
                Overflowed = Overflowed,
                DroppedEventCount = DroppedEventCount,
                NormalizationRules = new[] { "observer_timestamps", "absolute_paths", "session_identifiers" },
                Events = normalized
            };
        }

        public LegacySemanticTraceSnapshot ToSemanticComparison()
        {
            return ToSemanticComparison(null);
        }

        public LegacySemanticTraceSnapshot ToSemanticComparison(Func<string, string> textNormalizer)
        {
            var semanticEvents = new List<LegacyTraceEvent>();
            LegacyTraceEvent[] sourceEvents = Events ?? new LegacyTraceEvent[0];
            for (int index = 0; index < sourceEvents.Length; index++)
            {
                LegacyTraceEvent source = sourceEvents[index];
                if (source == null || string.Equals(source.OrderingPoint, LegacyTraceOrderingPoint.UiProjection, StringComparison.Ordinal))
                    continue;
                LegacyTraceEvent semantic = source.Clone(true, textNormalizer);
                semantic.Sequence = semanticEvents.Count + 1;
                semanticEvents.Add(semantic);
            }
            return new LegacySemanticTraceSnapshot
            {
                SchemaVersion = SchemaVersion,
                CoverageStatus = CoverageStatus,
                Projection = "script_observable_trace_v1",
                ExcludedTransportOrderingPoints = new[] { LegacyTraceOrderingPoint.UiProjection },
                ComparisonRules = new[] { "observer_timestamps", "absolute_paths", "session_identifiers", "semantic_sequence" },
                Events = semanticEvents.ToArray()
            };
        }
    }

    public sealed class LegacySemanticTraceSnapshot
    {
        public string SchemaVersion { get; set; }
        public string CoverageStatus { get; set; }
        public string Projection { get; set; }
        public string[] ExcludedTransportOrderingPoints { get; set; }
        public string[] ComparisonRules { get; set; }
        public LegacyTraceEvent[] Events { get; set; }

        public LegacySemanticTraceSnapshot()
        {
            SchemaVersion = "1.0.0";
            CoverageStatus = "Partial";
            Projection = "script_observable_trace_v1";
            ExcludedTransportOrderingPoints = new string[0];
            ComparisonRules = new string[0];
            Events = new LegacyTraceEvent[0];
        }
    }
}
