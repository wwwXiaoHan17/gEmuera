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
            // 值/按钮选择型等待用 IsWaitingValueSelection（含 IntValue/StrValue/AnyValue/
            // IntButton/StrButton）——erablue 标题菜单是 IntButton 等待，旧的
            // IsWaitingInputSomething 只认 IntValue/StrValue，导致 first_wait 永远检测不到
            //（与 ef3ba1d 面板侧修复同类缺口）。EnterKey/AnyKey 纯推进等待保持原判定。
            return console.IsWaitingValueSelection || console.IsWaitingEnterKey || console.IsWaitAnyKey;
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
