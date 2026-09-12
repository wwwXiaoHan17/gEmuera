using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GEmuera.Core.Session;
using Godot;
using MinorShift.Emuera;
using gEmuera.GodotHost;

namespace gEmuera.LegacyRunner
{
    internal sealed class InProcessSessionCycleSample
    {
        public InProcessSessionCycleSample(
            int ordinal,
            string gameId,
            string profileId,
            string semanticFingerprint,
            long managedBytes,
            long privateBytes,
            long workingSetBytes,
            int handleCount,
            int threadCount)
        {
            Ordinal = ordinal;
            GameId = gameId ?? "";
            ProfileId = profileId ?? "";
            SemanticFingerprint = semanticFingerprint ?? "";
            ManagedBytes = managedBytes;
            PrivateBytes = privateBytes;
            WorkingSetBytes = workingSetBytes;
            HandleCount = handleCount;
            ThreadCount = threadCount;
        }

        public int Ordinal { get; }
        public string GameId { get; }
        public string ProfileId { get; }
        public string SemanticFingerprint { get; }
        public long ManagedBytes { get; }
        public long PrivateBytes { get; }
        public long WorkingSetBytes { get; }
        public int HandleCount { get; }
        public int ThreadCount { get; }
    }

    public partial class LegacyRunnerHost : Node
    {
        const string ConfigArgumentPrefix = "--m0-runner-config=";

        LegacyRunnerConfig _config;
        LegacyRunnerReportWriter _report;
        LegacyInputReplayDriver _replay;
        readonly Stopwatch _stopwatch = new Stopwatch();
        readonly LegacySettlementTracker _settlementTracker = new LegacySettlementTracker();
        long _frameCount;
        bool _settlementActive;
        bool _mainAttached;
        bool _finishing;
        global::EmueraMain _main;
        Task<LegacySessionSwitchResult> _pendingInProcessSessionSwitch;
        LegacySessionLaunchConfiguration _pendingInProcessSessionTarget;
        IReadOnlyList<LegacySessionLaunchConfiguration> _inProcessSessionCycleTargets =
            Array.Empty<LegacySessionLaunchConfiguration>();
        long _inProcessSessionDeadlineMs;
        readonly List<string> _inProcessWaitFingerprints = new List<string>();
        readonly List<InProcessSessionCycleSample> _inProcessSessionCycleSamples = new List<InProcessSessionCycleSample>();

        public override void _Ready()
        {
            try
            {
                string configPath = ResolveConfigPath();
                _config = LegacyRunnerConfig.Load(configPath);
                LegacyRunnerDeterminism.Configure(_config.RandomSeed);
                if (_config.TraceEnabled)
                {
                    LegacyTrace.Enable(_config.MaxTraceEvents);
                    LegacyTrace.TryRecordClock("trace_origin", "LegacyRunnerHost.Stopwatch", "observer_origin", "0",
                        LegacyTraceThreadOwner.GodotMain);
                }
                _report = new LegacyRunnerReportWriter(_config);
                _replay = new LegacyInputReplayDriver(_config);
                _report.AddTimeline("runner_started", "profile=" + _config.Profile);

                var launchRegistry = _config.CreateLegacyRunnerSessionLaunchRegistry();
                if (_config.IsInProcessSessionCycle)
                {
                    _inProcessSessionCycleTargets = _config.CreateInProcessSessionCycleTargets();
                    if (_inProcessSessionCycleTargets.Count != _config.InProcessSessionSwitchCount + 1)
                        throw new InvalidDataException("in_process_session_cycle_targets_must_match_switch_count");
                }

                if (!global::FirstWindow.ConfigureLegacyRunnerSession(_config.GameRoot, _config.Profile, out string error))
                    throw new InvalidDataException(error);
                global::MinorShift.Emuera.Program.ConfigureLegacyRunnerStartupErrorLogPath(
                    Path.Combine(_config.OutputDirectory, "emuera_startup_errors.log"));
                global::MinorShift.Emuera.Program.ConfigureLegacyRunnerDefaultOutputLogPath(
                    Path.Combine(_config.OutputDirectory, "emuera.log"));
                if (!global::EmueraContent.ConfigureLegacyRunnerDisplayBackend(_config.DisplayBackend, out error))
                    throw new InvalidDataException(error);

                EnsureLegacyViewportSize();

                global::GenericUtils.SetMainThread();
                global::GenericUtils.InitializeLogging();
                var diagnosticsConfig = global::GenericUtils.GetRuntimeDiagnosticsConfig()
                    ?? throw new InvalidOperationException("runtime_diagnostics_config_not_initialized");
                // This test-only override is made before main.tscn exists and
                // therefore before EmueraMain snapshots the structural flag.
                // Normal project startup never calls this runner-only path.
                diagnosticsConfig.MigrationSessionIsolationEnabled = _config.SessionIsolationCanary;

                var mainScene = ResourceLoader.Load<PackedScene>("res://main.tscn");
                if (mainScene == null)
                    throw new InvalidDataException("main_scene_not_found");
                var mainInstance = mainScene.Instantiate();
                _main = mainInstance as global::EmueraMain
                    ?? throw new InvalidDataException("main_scene_root_is_not_emuera_main");
                _main.ConfigureLegacyRunnerSessionLaunchRegistry(launchRegistry);
                AddChild(mainInstance);
                _mainAttached = true;
                _report.AddTimeline("main_attached");
                _stopwatch.Start();
                _inProcessSessionDeadlineMs = _config.InitialWaitTimeoutMs;
            }
            catch (Exception ex)
            {
                Fail("runner_start_failed", ex.GetType().Name + ": " + ex.Message, 70);
            }
        }

        public override void _Process(double delta)
        {
            if (_finishing || !_mainAttached || _config == null)
                return;

            EnsureLegacyViewportSize();
            _frameCount++;
            long elapsedMs = _stopwatch.ElapsedMilliseconds;
            if (elapsedMs >= _config.MaxRuntimeMs)
            {
                Fail("runner_internal_timeout", "max_runtime_ms=" + _config.MaxRuntimeMs, 124);
                return;
            }

            if (_config.IsInProcessSessionCycle)
            {
                TickInProcessSessionCycle(elapsedMs);
                return;
            }

            var console = GlobalStatic.Console;
            if (console == null)
            {
                if (elapsedMs >= _config.InitialWaitTimeoutMs)
                    Fail("console_initialization_timeout", "initial_wait_timeout_ms=" + _config.InitialWaitTimeoutMs, 71);
                return;
            }

            if (!_replay.HasInputs)
            {
                if (!LegacyInputReplayDriver.IsReplayWait(console))
                {
                    ResetSettlement();
                    if (elapsedMs >= _config.InitialWaitTimeoutMs)
                        Fail("initial_wait_timeout", "initial_wait_timeout_ms=" + _config.InitialWaitTimeoutMs, 72);
                    return;
                }
                SettleOrFinish(console, "first_wait_reached");
                return;
            }

            LegacyReplayTick tick = _replay.Tick(console, elapsedMs);
            switch (tick.Kind)
            {
                case LegacyReplayTickKind.Submit:
                    ResetSettlement();
                    _report.AddTimeline("input_submitted",
                        "index=" + tick.InputIndex + " mouse=" + tick.Input.MouseButton + " value=" + EscapeTimelineValue(tick.Input.Value));
                    global::EmueraThread.instance.Input(tick.Input.Value, tick.Input.FromButton, tick.Input.Skip, tick.Input.MouseButton);
                    break;
                case LegacyReplayTickKind.Advanced:
                    ResetSettlement();
                    _report.AddTimeline("input_advanced", "index=" + tick.InputIndex);
                    break;
                case LegacyReplayTickKind.Failed:
                    Fail("input_replay_failed", "index=" + tick.InputIndex + " reason=" + tick.Error, 73);
                    return;
            }

            if (_replay.AllInputsAdvanced && (LegacyInputReplayDriver.IsReplayWait(console) || !console.IsInProcess))
                SettleOrFinish(console, "inputs_consumed");
            else
                ResetSettlement();
        }

        void TickInProcessSessionCycle(long elapsedMs)
        {
            if (_pendingInProcessSessionSwitch != null)
            {
                if (!_pendingInProcessSessionSwitch.IsCompleted)
                {
                    if (elapsedMs >= _inProcessSessionDeadlineMs)
                        Fail("in_process_session_switch_timeout", "initial_wait_timeout_ms=" + _config.InitialWaitTimeoutMs, 76);
                    return;
                }

                LegacySessionSwitchResult result;
                try
                {
                    result = _pendingInProcessSessionSwitch.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Fail("in_process_session_switch_failed", ex.GetType().Name + ": " + ex.Message, 76);
                    return;
                }
                finally
                {
                    _pendingInProcessSessionSwitch = null;
                }

                if (!result.IsCommitted)
                {
                    string error = result.BackendError?.Message ?? result.Session.Error?.Message ?? "switch_not_committed";
                    Fail("in_process_session_switch_not_committed", error, 76);
                    return;
                }

                _report.AddTimeline(
                    "in_process_session_switch_committed",
                    "ordinal=" + _inProcessWaitFingerprints.Count +
                    " generation=" + result.Session.Stamp.Generation.Value +
                    " target=" + (_pendingInProcessSessionTarget?.GameId ?? "unknown") +
                    " profile=" + (_pendingInProcessSessionTarget?.ProfileId ?? "unknown") +
                    " plan=" + (result.Session.Session?.Compatibility.CanonicalHash ?? "unknown"));
                _pendingInProcessSessionTarget = null;
                _inProcessSessionDeadlineMs = elapsedMs + _config.InitialWaitTimeoutMs;
                ResetSettlement();
                return;
            }

            var console = GlobalStatic.Console;
            if (console == null)
            {
                if (elapsedMs >= _inProcessSessionDeadlineMs)
                    Fail("in_process_console_initialization_timeout", "initial_wait_timeout_ms=" + _config.InitialWaitTimeoutMs, 71);
                return;
            }
            if (!LegacyInputReplayDriver.IsReplayWait(console))
            {
                ResetSettlement();
                if (elapsedMs >= _inProcessSessionDeadlineMs)
                    Fail("in_process_initial_wait_timeout", "initial_wait_timeout_ms=" + _config.InitialWaitTimeoutMs, 72);
                return;
            }

            SettleInProcessSessionCycle(console, elapsedMs);
        }

        void SettleInProcessSessionCycle(MinorShift.Emuera.GameView.EmueraConsole console, long elapsedMs)
        {
            if (!_settlementActive)
            {
                _settlementActive = true;
                _settlementTracker.Reset();
                _report.AddTimeline("in_process_session_settle_started", "ordinal=" + (_inProcessWaitFingerprints.Count + 1));
            }

            try
            {
                if (!_settlementTracker.Observe(CaptureSettlementFingerprint(console), _config.SettleFrames))
                    return;
            }
            catch (Exception ex)
            {
                Fail("in_process_settlement_snapshot_failed", ex.GetType().Name + ": " + ex.Message, 74);
                return;
            }

            string semanticFingerprint;
            try
            {
                semanticFingerprint = CaptureInProcessSemanticFingerprint(console);
            }
            catch (Exception ex)
            {
                Fail("in_process_semantic_snapshot_failed", ex.GetType().Name + ": " + ex.Message, 74);
                return;
            }

            _inProcessWaitFingerprints.Add(semanticFingerprint);
            try
            {
                WriteInProcessDisplaySnapshot(_inProcessWaitFingerprints.Count, console);
            }
            catch (Exception ex)
            {
                Fail("in_process_display_snapshot_write_failed", ex.GetType().Name + ": " + ex.Message, 74);
                return;
            }
            var reachedTarget = GetInProcessSessionCycleTarget(_inProcessWaitFingerprints.Count - 1);
            _inProcessSessionCycleSamples.Add(CaptureInProcessSessionCycleSample(
                _inProcessWaitFingerprints.Count,
                reachedTarget,
                semanticFingerprint));
            _report.AddTimeline(
                "in_process_session_wait_fingerprint",
                "ordinal=" + _inProcessWaitFingerprints.Count +
                " target=" + reachedTarget.GameId +
                " profile=" + reachedTarget.ProfileId +
                " sha256=" + semanticFingerprint);
            ResetSettlement();

            if (_inProcessWaitFingerprints.Count < _inProcessSessionCycleTargets.Count)
            {
                if (_main == null)
                {
                    Fail("in_process_main_unavailable", "EmueraMain instance was not retained by runner.", 76);
                    return;
                }

                var nextTarget = GetInProcessSessionCycleTarget(_inProcessWaitFingerprints.Count);
                _report.AddTimeline(
                    "in_process_session_switch_requested",
                    "ordinal=" + _inProcessWaitFingerprints.Count +
                    " target=" + nextTarget.GameId +
                    " profile=" + nextTarget.ProfileId);
                _inProcessSessionDeadlineMs = elapsedMs + _config.InitialWaitTimeoutMs;
                _pendingInProcessSessionTarget = nextTarget;
                _pendingInProcessSessionSwitch = _main.SwitchLegacySessionForLegacyRunnerAsync(nextTarget);
                return;
            }

            if (!AreInProcessCycleFingerprintsStable(out string fingerprintError))
            {
                Fail(
                    "in_process_session_semantic_mismatch",
                    fingerprintError,
                    76);
                return;
            }

            try
            {
                _report.WriteInProcessSessionCycleEvidence(_inProcessSessionCycleSamples);
            }
            catch (Exception ex)
            {
                Fail("in_process_session_lifecycle_evidence_write_failed", ex.GetType().Name + ": " + ex.Message, 74);
                return;
            }

            Finish(
                true,
                _config.IsInProcessCrossAbaCycle
                    ? "in_process_cross_aba_cycle_completed"
                    : "in_process_aba_cycle_completed",
                0,
                console);
        }

        LegacySessionLaunchConfiguration GetInProcessSessionCycleTarget(int index)
        {
            if (index < 0 || index >= _inProcessSessionCycleTargets.Count)
                throw new InvalidOperationException("in_process_session_cycle_target_out_of_range");
            return _inProcessSessionCycleTargets[index];
        }

        bool AreInProcessCycleFingerprintsStable(out string error)
        {
            if (_inProcessWaitFingerprints.Count != _inProcessSessionCycleTargets.Count)
            {
                error = "fingerprint_count=" + _inProcessWaitFingerprints.Count +
                    " target_count=" + _inProcessSessionCycleTargets.Count;
                return false;
            }

            var fingerprintsByTarget = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < _inProcessWaitFingerprints.Count; index++)
            {
                var target = GetInProcessSessionCycleTarget(index);
                string key = target.GameId + "\u001f" + target.ProfileId;
                string fingerprint = _inProcessWaitFingerprints[index];
                if (fingerprintsByTarget.TryGetValue(key, out string previous))
                {
                    if (!string.Equals(previous, fingerprint, StringComparison.Ordinal))
                    {
                        error = "target=" + target.GameId + " profile=" + target.ProfileId +
                            " expected=" + previous + " actual=" + fingerprint;
                        return false;
                    }
                }
                else
                {
                    fingerprintsByTarget.Add(key, fingerprint);
                }
            }

            error = "";
            return true;
        }

        static InProcessSessionCycleSample CaptureInProcessSessionCycleSample(
            int ordinal,
            LegacySessionLaunchConfiguration target,
            string semanticFingerprint)
        {
            long privateBytes = -1;
            long workingSetBytes = -1;
            int handleCount = -1;
            int threadCount = -1;
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                privateBytes = process.PrivateMemorySize64;
                workingSetBytes = process.WorkingSet64;
                try
                {
                    handleCount = process.HandleCount;
                }
                catch (Exception)
                {
                    // The lifecycle report keeps an explicit unavailable value.
                }
                try
                {
                    threadCount = process.Threads.Count;
                }
                catch (Exception)
                {
                    // Some sandboxed environments cannot enumerate all threads.
                }
            }
            catch (Exception)
            {
                // Metrics are observation-only; session semantics remain authoritative.
            }

            return new InProcessSessionCycleSample(
                ordinal,
                target.GameId,
                target.ProfileId,
                semanticFingerprint,
                GC.GetTotalMemory(false),
                privateBytes,
                workingSetBytes,
                handleCount,
                threadCount);
        }

        void WriteInProcessDisplaySnapshot(int ordinal, MinorShift.Emuera.GameView.EmueraConsole console)
        {
            var display = new StringBuilder();
            console.GetDisplayStrings(display);
            string fileName = "in-process-session-" + ordinal.ToString("D2", CultureInfo.InvariantCulture) + ".display.txt";
            File.WriteAllText(
                Path.Combine(_config.OutputDirectory, fileName),
                display.ToString(),
                new UTF8Encoding(false));
        }

        void SettleOrFinish(MinorShift.Emuera.GameView.EmueraConsole console, string reason)
        {
            if (!_settlementActive)
            {
                _settlementActive = true;
                _settlementTracker.Reset();
                _report.AddTimeline("settle_started", reason);
            }

            try
            {
                string fingerprint = CaptureSettlementFingerprint(console);
                if (!_settlementTracker.Observe(fingerprint, _config.SettleFrames))
                    return;
            }
            catch (Exception ex)
            {
                Fail("settlement_snapshot_failed", ex.GetType().Name + ": " + ex.Message, 74);
                return;
            }

            if (_settlementTracker.StableFrameCount >= _config.SettleFrames)
                Finish(true, reason, 0, console);
        }

        void ResetSettlement()
        {
            if (!_settlementActive)
                return;
            _settlementActive = false;
            _settlementTracker.Reset();
        }

        static string CaptureSettlementFingerprint(MinorShift.Emuera.GameView.EmueraConsole console)
        {
            if (console == null)
                throw new ArgumentNullException(nameof(console));

            var display = new StringBuilder();
            console.GetDisplayStrings(display);
            string displayHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(display.ToString())));
            string surface = global::EmueraContent.instance == null
                ? "surface_unavailable"
                : global::EmueraContent.instance.CaptureLegacySettlementFingerprint();
            return string.Join("\u001f", new[]
            {
                displayHash,
                LegacyTrace.RecordedCount.ToString(CultureInfo.InvariantCulture),
                LegacyTrace.DroppedEventCount.ToString(CultureInfo.InvariantCulture),
                console.LineCount.ToString(CultureInfo.InvariantCulture),
                console.IsInProcess ? "1" : "0",
                console.IsWaitingInput ? "1" : "0",
                console.IsWaitingInputSomething ? "1" : "0",
                console.IsWaitingEnterKey ? "1" : "0",
                console.IsWaitAnyKey ? "1" : "0",
                console.InputType.ToString(),
                console.NewButtonGeneration.ToString(CultureInfo.InvariantCulture),
                surface
            });
        }

        static string CaptureInProcessSemanticFingerprint(MinorShift.Emuera.GameView.EmueraConsole console)
        {
            if (console == null)
                throw new ArgumentNullException(nameof(console));

            var display = new StringBuilder();
            console.GetDisplayStrings(display);
            string canonicalDisplay = Regex.Replace(
                display.ToString().Replace("\r\n", "\n").Replace('\r', '\n'),
                @"(?m)^字典加载完毕 用时\d+毫秒$",
                "字典加载完毕 用时<ELAPSED_MS>毫秒",
                RegexOptions.CultureInvariant);
            string payload = string.Join("\u001f", new[]
            {
                canonicalDisplay,
                console.LineCount.ToString(CultureInfo.InvariantCulture),
                console.IsInProcess ? "1" : "0",
                console.IsWaitingInput ? "1" : "0",
                console.IsWaitingInputSomething ? "1" : "0",
                console.IsWaitingEnterKey ? "1" : "0",
                console.IsWaitAnyKey ? "1" : "0",
                console.InputType.ToString(),
                console.NewButtonGeneration.ToString(CultureInfo.InvariantCulture)
            });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        }

        void Fail(string code, string message, int exitCode)
        {
            if (_finishing)
                return;
            if (_report != null)
            {
                _report.AddError(code, message);
                _report.AddTimeline("runner_failed", "code=" + code);
            }
            Finish(false, code, exitCode, GlobalStatic.Console);
        }

        void Finish(bool success, string reason, int exitCode, MinorShift.Emuera.GameView.EmueraConsole console)
        {
            if (_finishing)
                return;
            _finishing = true;
            _stopwatch.Stop();
			_ = FinishAfterRenderingAsync(success, reason, exitCode, console);
		}

        async Task FinishAfterRenderingAsync(bool success, string reason, int exitCode,
            MinorShift.Emuera.GameView.EmueraConsole console)
        {
            try
            {
                // 配置加载失败（_Ready 早期 Fail）时 _config/_report 均为 null：
                // 此时不可能产出任何报告，必须带着原始失败原因直接退出，
                // 否则下方 _config.CaptureScreenshot 解引用会 NRE，把真实错误
                // （如 max_runtime_ms_out_of_range）掩盖成空报告 + exit 74。
                if (_config == null)
                {
                    GD.PushError("Legacy runner aborted before config load: reason=" + reason + " exitCode=" + exitCode);
                    GetTree().Quit(exitCode);
                    return;
                }
                var screenshot = new LegacyScreenshotEvidence();
                if (_config.CaptureScreenshot)
                {
                    if (string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase))
                    {
                        screenshot.Failure = "headless_display_driver";
                    }
                    else
                    {
                        for (int index = 0; index < _config.DisplaySettleFrames; index++)
                        {
                            EnsureLegacyViewportSize();
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        }
                        EnsureLegacyViewportSize();
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        screenshot = CaptureLegacyScreenshot();
                    }
                }

                LegacyDisplayObservation displayObservation = global::EmueraContent.instance == null
                    ? LegacyDisplayObservation.CreateUncovered(_config.DisplayBackend, "legacy_display_surface_unavailable")
                    : global::EmueraContent.instance.CaptureLegacyDisplayObservation(_config.DisplayBackend);
                displayObservation.Screenshot = screenshot;
                displayObservation.RefreshCoverageUncovered();
                _report?.AddTimeline("runner_finished", "success=" + success + " reason=" + reason);
                LegacyTraceRecorder traceRecorder = LegacyTrace.Disable();
                LegacyTraceSnapshot trace = traceRecorder == null ? new LegacyTraceSnapshot() : traceRecorder.Snapshot();
                if (trace.Overflowed)
                {
                    success = false;
                    reason = "trace_capacity_exceeded";
                    if (exitCode == 0)
                        exitCode = 75;
                    _report?.AddError(reason, "dropped_event_count=" + trace.DroppedEventCount);
                }
                _report?.WriteAll(console, success, reason, _stopwatch.ElapsedMilliseconds, _frameCount,
                    _replay?.SubmittedCount ?? 0, trace, displayObservation);
            }
            catch (Exception ex)
            {
                // 带上原始 reason，避免上报路径自身的异常掩盖真正的失败原因
                GD.PushError("Legacy legacy runner report failure (reason=" + reason + ", exitCode=" + exitCode + "): " + ex);
                exitCode = 74;
            }
            GetTree().Quit(exitCode);
        }

        LegacyScreenshotEvidence CaptureLegacyScreenshot()
        {
            var evidence = new LegacyScreenshotEvidence { Failure = "capture_failed" };
            try
            {
                Image image = GetViewport()?.GetTexture()?.GetImage();
                if (image == null || image.IsEmpty() || image.GetWidth() <= 0 || image.GetHeight() <= 0)
                {
                    evidence.Failure = "viewport_image_empty";
                    return evidence;
                }
                const string fileName = "viewport.png";
                string path = Path.Combine(_config.OutputDirectory, fileName);
                Error error = image.SavePng(path);
                if (error != Error.Ok || !File.Exists(path))
                {
                    evidence.Failure = "save_png_" + error;
                    return evidence;
                }
                using var stream = File.OpenRead(path);
                using var sha = SHA256.Create();
                evidence.Status = "Captured";
                evidence.FileName = fileName;
                evidence.Sha256 = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
                evidence.Width = image.GetWidth();
                evidence.Height = image.GetHeight();
                evidence.Failure = "";
                return evidence;
            }
            catch (Exception ex)
            {
                evidence.Failure = ex.GetType().Name + ":" + ex.Message;
                return evidence;
            }
        }

        void EnsureLegacyViewportSize()
        {
            if (_config == null || GetWindow() == null)
                return;
            var target = new Vector2I(_config.ViewportWidth, _config.ViewportHeight);
            if (GetWindow().Size != target)
                GetWindow().Size = target;
        }

        static string ResolveConfigPath()
        {
            foreach (string argument in OS.GetCmdlineUserArgs())
            {
                if (argument.StartsWith(ConfigArgumentPrefix, StringComparison.Ordinal))
                    return argument.Substring(ConfigArgumentPrefix.Length).Trim('"');
            }
            throw new InvalidDataException("missing_m0_runner_config_argument");
        }

        static string EscapeTimelineValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            string normalized = value.Replace("\\", "\\\\").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            return normalized.Length <= 128 ? normalized : normalized.Substring(0, 128) + "...";
        }
    }
}
