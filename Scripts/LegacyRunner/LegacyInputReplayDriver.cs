using System;
using MinorShift.Emuera.GameView;

namespace gEmuera.LegacyRunner
{
    internal enum LegacyReplayTickKind
    {
        None,
        Submit,
        Advanced,
        Failed
    }

    internal readonly struct LegacyReplayTick
    {
        public LegacyReplayTickKind Kind { get; }
        public int InputIndex { get; }
        public LegacyRunnerInput Input { get; }
        public string Error { get; }

        public LegacyReplayTick(LegacyReplayTickKind kind, int inputIndex, LegacyRunnerInput input, string error)
        {
            Kind = kind;
            InputIndex = inputIndex;
            Input = input;
            Error = error ?? "";
        }
    }

    internal sealed class LegacyInputReplayDriver
    {
        readonly LegacyRunnerConfig _config;
        int _nextInputIndex;
        bool _awaitingAdvance;
        bool _observedNotWaiting;
        int _generationAtSubmit;
        long _deadlineMs;

        public LegacyInputReplayDriver(LegacyRunnerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public int SubmittedCount => _nextInputIndex;
        public bool HasInputs => _config.Inputs.Count > 0;
        public bool AllInputsAdvanced => _nextInputIndex >= _config.Inputs.Count && !_awaitingAdvance;

        internal static bool IsReplayWait(EmueraConsole console)
        {
            if (console == null || !console.IsWaitingInput)
                return false;
            // 按钮选择等待（IntButton/StrButton/AnyValue，如标题菜单 PRINTBUTTON）同样是需要外部输入的
            // 等待点：erablue 标题即 IntButton，漏判会导致 first_wait 永远检测不到（2026-09-04 实证）。
            return console.IsWaitingInputSomething || console.IsWaitingEnterKey || console.IsWaitAnyKey
                || console.IsWaitingValueSelection;
        }

        public LegacyReplayTick Tick(EmueraConsole console, long elapsedMs)
        {
            if (console == null)
                return default;

            if (_awaitingAdvance)
            {
                bool waiting = IsReplayWait(console);
                if (!waiting)
                    _observedNotWaiting = true;
                if (console.NewButtonGeneration != _generationAtSubmit || (_observedNotWaiting && waiting) || !console.IsInProcess)
                {
                    _awaitingAdvance = false;
                    return new LegacyReplayTick(LegacyReplayTickKind.Advanced, _nextInputIndex - 1, null, "");
                }
                if (elapsedMs >= _deadlineMs)
                {
                    _awaitingAdvance = false;
                    return new LegacyReplayTick(LegacyReplayTickKind.Failed, _nextInputIndex - 1, null, "input_not_consumed_before_timeout");
                }
                return default;
            }

            if (_nextInputIndex >= _config.Inputs.Count || !console.IsWaitingInput)
                return default;

            int index = _nextInputIndex;
            var input = _config.Inputs[index];
            if (!IsReplayWait(console))
            {
                return new LegacyReplayTick(LegacyReplayTickKind.Failed, index, input,
                    "unsupported_actual_input_type:actual=" + console.InputType);
            }

            string actualInputType = console.InputType.ToString();
            if (!string.IsNullOrEmpty(input.ExpectedInputType)
                && !string.Equals(input.ExpectedInputType, actualInputType, StringComparison.Ordinal))
            {
                return new LegacyReplayTick(LegacyReplayTickKind.Failed, index, input,
                    "unexpected_input_type:expected=" + input.ExpectedInputType + ":actual=" + actualInputType);
            }
            if (input.ExpectedButtonGeneration.HasValue
                && input.ExpectedButtonGeneration.Value != console.NewButtonGeneration)
            {
                return new LegacyReplayTick(LegacyReplayTickKind.Failed, index, input,
                    "unexpected_button_generation:expected=" + input.ExpectedButtonGeneration.Value
                    + ":actual=" + console.NewButtonGeneration);
            }

            _nextInputIndex++;
            _awaitingAdvance = true;
            _observedNotWaiting = false;
            _generationAtSubmit = console.NewButtonGeneration;
            _deadlineMs = elapsedMs + input.WaitTimeoutMs;
            return new LegacyReplayTick(LegacyReplayTickKind.Submit, index, input, "");
        }
    }
}
