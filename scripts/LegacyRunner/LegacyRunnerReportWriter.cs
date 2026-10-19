using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using gEmuera.Diagnostics;
using Godot;
using MinorShift.Emuera.GameView;

namespace gEmuera.LegacyRunner
{
    internal sealed class LegacyRunnerReportWriter
    {
        readonly LegacyRunnerConfig _config;
        readonly List<object> _runnerErrors = new List<object>();

        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public LegacyRunnerReportWriter(LegacyRunnerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Directory.CreateDirectory(_config.OutputDirectory);
        }

        public void AddTimeline(string kind, string data = "")
        {
            LegacyTrace.TryRecordRunner(kind ?? "", data ?? "");
        }

        public void AddError(string code, string message)
        {
            _runnerErrors.Add(new { code = code ?? "runner_error", message = message ?? "" });
            LegacyTrace.TryRecordError("runner_error", "RunnerError", code, message, nameof(LegacyRunnerReportWriter));
        }

        public void WriteAll(EmueraConsole console, bool success, string exitReason, long elapsedMs, long frameCount,
            int submittedInputs, LegacyTraceSnapshot trace, LegacyDisplayObservation displayObservation)
        {
            string displayText = "";
            if (console != null)
            {
                var builder = new StringBuilder();
                console.GetDisplayStrings(builder);
                displayText = builder.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
            }
            if (Encoding.UTF8.GetByteCount(displayText) > _config.MaxDisplayBytes)
                throw new InvalidDataException("display_snapshot_exceeds_max_display_bytes");
            string canonicalDisplayText = CanonicalizeDisplayText(displayText);

            var diagnosticRecords = DiagnosticLogSinks.Snapshot();
            var diagnosticErrors = diagnosticRecords
                .Where(record => record.Level >= EmueraLogLevel.Error)
                .Select(record => new
                {
                    sequence = record.Seq,
                    level = record.Level.ToString(),
                    category = record.Category.ToString(),
                    eventId = record.EventId,
                    message = CanonicalizeDiagnosticText(record.Message),
                    data = CanonicalizeDiagnosticText(record.Data)
                })
                .ToArray();
            LegacyTraceSnapshot rawTrace = trace ?? new LegacyTraceSnapshot();
            LegacyTraceSnapshot canonicalTrace = rawTrace.ToCanonical(CanonicalizeDiagnosticText);
            LegacySemanticTraceSnapshot semanticTrace = rawTrace.ToSemanticComparison(CanonicalizeDiagnosticText);
            displayObservation ??= LegacyDisplayObservation.CreateUncovered(_config.DisplayBackend,
                "display_observation_not_supplied");
            displayObservation.ApplyTraceCoverage(rawTrace);
            string displayCoverageStatus = displayObservation.Uncovered.Count == 0 ? "Captured" : "Partial";
            var canonicalTimeline = semanticTrace.Events
                .Where(item => item.Category == LegacyTraceCategory.Runner
                    || item.Category == LegacyTraceCategory.Clock
                    || item.Category == LegacyTraceCategory.Wait
                    || item.Category == LegacyTraceCategory.Input)
                .ToArray();
            var canonicalEffects = semanticTrace.Events
                .Where(item => item.Category == LegacyTraceCategory.Effect)
                .ToArray();
            var canonicalTraceErrors = semanticTrace.Events
                .Where(item => item.Category == LegacyTraceCategory.Error)
                .ToArray();

            WriteJson("state.json", new
            {
                schemaVersion = "1.0.0",
                coverageStatus = "Partial",
                profile = _config.Profile,
                inProcessSessionCycle = _config.InProcessSessionCycle,
                inProcessSessionSwitchCount = _config.InProcessSessionSwitchCount,
                runnerSuccess = success,
                exitReason = exitReason ?? "",
                submittedInputs,
                randomSeed = _config.RandomSeed,
                requestedDisplayBackend = _config.DisplayBackend,
                effectiveDisplayBackend = displayObservation.EffectiveBackend,
                console = console == null ? null : new
                {
                    lineCount = console.LineCount,
                    isInProcess = console.IsInProcess,
                    isWaitingInput = console.IsWaitingInput,
                    isWaitingInputSomething = console.IsWaitingInputSomething,
                    isWaitingEnterKey = console.IsWaitingEnterKey,
                    isWaitAnyKey = console.IsWaitAnyKey,
                    inputType = console.InputType.ToString(),
                    buttonGeneration = console.NewButtonGeneration
                }
            });
            WriteJson("display.json", new
            {
                schemaVersion = "1.0.0",
                coverageStatus = displayCoverageStatus,
                projection = "legacy_backend_observation_and_text",
                normalizationRules = new[] { "legacy_dictionary_load_elapsed_ms" },
                requestedBackend = displayObservation.RequestedBackend,
                effectiveBackend = displayObservation.EffectiveBackend,
                backendEvidence = displayObservation.BackendEvidence,
                requestedViewport = new { width = _config.ViewportWidth, height = _config.ViewportHeight },
                viewport = displayObservation.Viewport,
                screenshot = displayObservation.Screenshot,
                featureCoverage = displayObservation.FeatureCoverage,
                uncovered = displayObservation.Uncovered,
                text = canonicalDisplayText
            });
            WriteJson("display.raw.json", new
            {
                schemaVersion = "1.0.0",
                projection = "legacy_backend_observation_and_text_raw",
                observation = displayObservation,
                text = displayText
            });
            WriteJson("screenshots.json", new
            {
                schemaVersion = "1.0.0",
                coverageStatus = displayObservation.Screenshot.Status,
                backend = displayObservation.EffectiveBackend,
                screenshot = displayObservation.Screenshot,
                uncovered = displayObservation.Screenshot.Status == "Captured"
                    ? Array.Empty<string>()
                    : new[] { displayObservation.Screenshot.Failure }
            });
            bool anyHitMismatch = displayObservation.Hits.Any(hit => hit == null || !hit.Matched);
            WriteJson("hit-test.json", new
            {
                schemaVersion = "1.0.0",
                coverageStatus = displayObservation.Hits.Count == 0 ? "Uncovered" : anyHitMismatch ? "Partial" : "Captured",
                backend = displayObservation.EffectiveBackend,
                hits = displayObservation.Hits,
                uncovered = displayObservation.Uncovered.Where(value => value.StartsWith("hit-test:", StringComparison.Ordinal)).ToArray()
            });
            WriteJson("trace.raw.json", rawTrace);
            WriteJson("trace.json", canonicalTrace);
            WriteJson("semantic-trace.json", semanticTrace);
            WriteJson("effects.json", new
            {
                schemaVersion = "1.0.0",
                coverageStatus = "Partial",
                projection = "trace_effect_events",
                uncovered = new[] { "save/file/application effects not yet instrumented" },
                effects = canonicalEffects
            });
            WriteJson("errors.json", new
            {
                schemaVersion = "1.0.0",
                runnerErrors = _runnerErrors,
                diagnosticErrors,
                traceErrors = canonicalTraceErrors
            });
            WriteJson("timeline.json", new
            {
                schemaVersion = "1.0.0",
                coverageStatus = "Partial",
                projection = "trace_clock_wait_input_runner_events",
                events = canonicalTimeline
            });
            WriteJson("metrics.json", new
            {
                schemaVersion = "1.0.0",
                elapsedMs,
                frameCount,
                submittedInputs,
                inProcessSessionSwitchCount = _config.InProcessSessionSwitchCount,
                diagnosticRecordCount = diagnosticRecords.Length,
                diagnosticRingOverwritten = DiagnosticLogSinks.OverwrittenTotal
            });
            WriteJson("diagnostics.json", new
            {
                schemaVersion = "1.0.0",
                records = diagnosticRecords.Select(record => new
                {
                    sequence = record.Seq,
                    level = record.Level.ToString(),
                    category = record.Category.ToString(),
                    eventId = record.EventId,
                    message = record.Message,
                    data = record.Data
                }).ToArray()
            });
            WriteJson("artifacts.json", new
            {
                schemaVersion = "1.0.0",
                status = "PendingExternalCollection",
                owner = "tools/legacy-runner/Invoke-LegacyRunner.ps1"
            });
        }

        public void WriteInProcessSessionCycleEvidence(
            IReadOnlyList<InProcessSessionCycleSample> samples)
        {
            ArgumentNullException.ThrowIfNull(samples);
            int expectedSessionCount = checked(_config.InProcessSessionSwitchCount + 1);
            if (samples.Count != expectedSessionCount)
            {
                throw new InvalidDataException(
                    "in_process_session_cycle_sample_count_invalid:" + samples.Count + "/" + expectedSessionCount);
            }

            WriteJson("in-process-session-cycle.json", new
            {
                schemaVersion = "1.0.0",
                coverageStatus = "Partial",
                cycle = _config.InProcessSessionCycle,
                switchCount = _config.InProcessSessionSwitchCount,
                sessionCount = samples.Count,
                samples = samples.Select(sample => new
                {
                    ordinal = sample.Ordinal,
                    gameId = sample.GameId,
                    profile = sample.ProfileId,
                    semanticFingerprint = sample.SemanticFingerprint,
                    managedBytes = sample.ManagedBytes,
                    privateBytes = sample.PrivateBytes,
                    workingSetBytes = sample.WorkingSetBytes,
                    handleCount = sample.HandleCount,
                    threadCount = sample.ThreadCount,
                }).ToArray(),
                uncovered = new[]
                {
                    "Samples are runner-only checkpoint observations, not a leak-proof or Android/device conclusion.",
                    "Node, Resource, RID, SQLite and native allocation ledgers remain separate M1 evidence work.",
                },
            });
        }

        void WriteJson(string fileName, object value)
        {
            string finalPath = Path.Combine(_config.OutputDirectory, fileName);
            string tempPath = finalPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions) + "\n", new UTF8Encoding(false));
            File.Move(tempPath, finalPath, true);
        }

        string CanonicalizeDiagnosticText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            string normalized = value.Replace('\\', '/');
            string gameRoot = Path.GetFullPath(_config.GameRoot).Replace('\\', '/').TrimEnd('/');
            string projectRoot = ProjectSettings.GlobalizePath("res://").Replace('\\', '/').TrimEnd('/');
            normalized = normalized.Replace(gameRoot, "<GAME_ROOT>", StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace(projectRoot, "<PROJECT_ROOT>", StringComparison.OrdinalIgnoreCase);
            normalized = Regex.Replace(normalized, @"\bsession_id=\S+", "session_id=<SESSION>", RegexOptions.CultureInvariant);
            normalized = Regex.Replace(normalized, @"\bsession=\S+", "session=<SESSION>", RegexOptions.CultureInvariant);
            return normalized;
        }

        static string CanonicalizeDisplayText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return Regex.Replace(value,
                @"(?m)^字典加载完毕 用时\d+毫秒$",
                "字典加载完毕 用时<ELAPSED_MS>毫秒",
                RegexOptions.CultureInvariant);
        }
    }
}
