using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using gEmuera.GodotHost;

namespace gEmuera.LegacyRunner
{
	public sealed class LegacyRunnerInput
	{
		public string Value { get; set; } = "";
		public bool FromButton { get; set; } = true;
		public bool Skip { get; set; }
		public int MouseButton { get; set; }
		public int WaitTimeoutMs { get; set; } = 15000;
		public string ExpectedInputType { get; set; } = "";
		public int? ExpectedButtonGeneration { get; set; }
	}

	/// <summary>
	/// Runner-only alternate binding for the narrow cross-configuration A→B→A
	/// probe. It is never consumed by the normal launcher.
	/// </summary>
	public sealed class LegacyRunnerSessionTarget
	{
		public string GameRoot { get; set; } = "";
		public string Profile { get; set; } = global::FirstWindow.CoreProfileV24Pure;
	}

	public sealed class LegacyRunnerConfig
	{
		public const string CurrentSchemaVersion = "1.0.0";
		public const string SessionIsolationModeBaseline = "baseline";
		public const string SessionIsolationModeCanary = "canary";
		public const string InProcessSessionCycleDisabled = "disabled";
		public const string InProcessSessionCycleAba = "aba";
		public const string InProcessSessionCycleCrossAba = "cross-aba";

		public string SchemaVersion { get; set; } = CurrentSchemaVersion;
		public string GameRoot { get; set; } = "";
		public string Profile { get; set; } = global::FirstWindow.CoreProfileV24Pure;
		public string SessionIsolationMode { get; set; } = SessionIsolationModeBaseline;
		public string InProcessSessionCycle { get; set; } = InProcessSessionCycleDisabled;
		public int InProcessSessionSwitchCount { get; set; } = 2;
		public LegacyRunnerSessionTarget InProcessAlternateSession { get; set; }
		public string OutputDirectory { get; set; } = "";
		public int MaxRuntimeMs { get; set; } = 60000;
		public int InitialWaitTimeoutMs { get; set; } = 45000;
		public int SettleFrames { get; set; } = 3;
		public int DisplaySettleFrames { get; set; } = 2;
		public string DisplayBackend { get; set; } = "canvas";
		public bool CaptureScreenshot { get; set; }
		public int ViewportWidth { get; set; } = 1280;
		public int ViewportHeight { get; set; } = 720;
		public int MaxDisplayBytes { get; set; } = 16 * 1024 * 1024;
		public bool TraceEnabled { get; set; } = true;
		public int MaxTraceEvents { get; set; } = 250000;
		public int RandomSeed { get; set; } = 20260712;
		public List<LegacyRunnerInput> Inputs { get; set; } = new List<LegacyRunnerInput>();

		/// <summary>
		/// Runner-only, startup-before-scene choice for comparing the default
		/// Legacy bridge with the default-off M1 canary. It is never read by the
		/// normal launcher and cannot hot-swap an already running session.
		/// </summary>
		public bool SessionIsolationCanary => string.Equals(
			SessionIsolationMode,
			SessionIsolationModeCanary,
			StringComparison.Ordinal);

		/// <summary>
		/// Explicit runner-only same-process restart probe. "aba" performs two
		/// facade switches against the same configured fixture; it is not a
		/// general interactive hot-switch feature.
		/// </summary>
		public bool IsInProcessAbaCycle => string.Equals(
			InProcessSessionCycle,
			InProcessSessionCycleAba,
			StringComparison.Ordinal);

		/// <summary>
		/// Explicit runner-only A→B→A probe. B must be pre-registered before
		/// main.tscn is attached; it is not an interactive session switch.
		/// </summary>
		public bool IsInProcessCrossAbaCycle => string.Equals(
			InProcessSessionCycle,
			InProcessSessionCycleCrossAba,
			StringComparison.Ordinal);

		public bool IsInProcessSessionCycle => IsInProcessAbaCycle || IsInProcessCrossAbaCycle;

		public LegacySessionLaunchRegistry CreateLegacyRunnerSessionLaunchRegistry()
		{
			var primary = new LegacySessionLaunchConfiguration(GameRoot, Profile);
			if (!IsInProcessCrossAbaCycle)
				return new LegacySessionLaunchRegistry(new[] { primary });

			var alternate = InProcessAlternateSession
				?? throw new InvalidOperationException("Cross-ABA requires an alternate session binding.");
			return new LegacySessionLaunchRegistry(new[]
			{
				primary,
				new LegacySessionLaunchConfiguration(alternate.GameRoot, alternate.Profile),
			});
		}

		public IReadOnlyList<LegacySessionLaunchConfiguration> CreateInProcessSessionCycleTargets()
		{
			var primary = new LegacySessionLaunchConfiguration(GameRoot, Profile);
			var targets = new List<LegacySessionLaunchConfiguration>(InProcessSessionSwitchCount + 1);
			if (IsInProcessAbaCycle)
			{
				for (int index = 0; index <= InProcessSessionSwitchCount; index++)
					targets.Add(primary);
				return targets;
			}
			if (IsInProcessCrossAbaCycle)
			{
				var alternate = InProcessAlternateSession
					?? throw new InvalidOperationException("Cross-ABA requires an alternate session binding.");
				var alternateLaunch = new LegacySessionLaunchConfiguration(alternate.GameRoot, alternate.Profile);
				for (int index = 0; index <= InProcessSessionSwitchCount; index++)
					targets.Add((index & 1) == 0 ? primary : alternateLaunch);
				return targets;
			}

			throw new InvalidOperationException("No in-process session cycle was requested.");
		}

		public static LegacyRunnerConfig Load(string path)
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
				throw new InvalidDataException("runner_config_not_found");

			var options = new JsonSerializerOptions
			{
				AllowTrailingCommas = true,
				ReadCommentHandling = JsonCommentHandling.Skip,
				PropertyNameCaseInsensitive = true
			};
			var config = JsonSerializer.Deserialize<LegacyRunnerConfig>(File.ReadAllText(path), options);
			if (config == null)
				throw new InvalidDataException("runner_config_empty");
			config.Validate();
			return config;
		}

		public void Validate()
		{
			if (!string.Equals(SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
				throw new InvalidDataException("unsupported_schema_version");
			if (string.IsNullOrWhiteSpace(GameRoot) || !Path.IsPathRooted(GameRoot) || !Directory.Exists(GameRoot))
				throw new InvalidDataException("game_root_must_be_existing_absolute_directory");
			if (string.IsNullOrWhiteSpace(OutputDirectory) || !Path.IsPathRooted(OutputDirectory))
				throw new InvalidDataException("output_directory_must_be_absolute");
			// megaten：runner 亦接受 megaten profile（错误码文案同步扩展）。
		ValidateSupportedProfile(Profile, "profile_must_be_v24pure_or_snake_or_erafl_or_megaten");
			if (!string.Equals(SessionIsolationMode, SessionIsolationModeBaseline, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(SessionIsolationMode, SessionIsolationModeCanary, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("session_isolation_mode_must_be_baseline_or_canary");
			InProcessSessionCycle ??= InProcessSessionCycleDisabled;
			if (!string.Equals(InProcessSessionCycle, InProcessSessionCycleDisabled, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(InProcessSessionCycle, InProcessSessionCycleAba, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(InProcessSessionCycle, InProcessSessionCycleCrossAba, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("in_process_session_cycle_must_be_disabled_aba_or_cross_aba");
			bool inProcessSessionCycleRequested = !string.Equals(
				InProcessSessionCycle,
				InProcessSessionCycleDisabled,
				StringComparison.OrdinalIgnoreCase);
			bool inProcessCrossAbaRequested = string.Equals(
				InProcessSessionCycle,
				InProcessSessionCycleCrossAba,
				StringComparison.OrdinalIgnoreCase);
			if (InProcessSessionSwitchCount < 2 || InProcessSessionSwitchCount > 100)
				throw new InvalidDataException("in_process_session_switch_count_out_of_range");
			if (!inProcessSessionCycleRequested && InProcessSessionSwitchCount != 2)
				throw new InvalidDataException("in_process_session_switch_count_requires_cycle");
			if (inProcessSessionCycleRequested && !string.Equals(
					SessionIsolationMode,
					SessionIsolationModeCanary,
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("in_process_session_cycle_requires_canary");
			if (inProcessCrossAbaRequested && (InProcessSessionSwitchCount & 1) != 0)
				throw new InvalidDataException("in_process_cross_aba_switch_count_must_be_even");
			if (MaxRuntimeMs < 1000 || MaxRuntimeMs > 600000)
				throw new InvalidDataException("max_runtime_ms_out_of_range");
			if (InitialWaitTimeoutMs < 1000 || InitialWaitTimeoutMs > MaxRuntimeMs)
				throw new InvalidDataException("initial_wait_timeout_ms_out_of_range");
			if (SettleFrames < 1 || SettleFrames > 120)
				throw new InvalidDataException("settle_frames_out_of_range");
			if (DisplaySettleFrames < 1 || DisplaySettleFrames > 120)
				throw new InvalidDataException("display_settle_frames_out_of_range");
			if (!string.Equals(DisplayBackend, "controls", StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(DisplayBackend, "canvas", StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("display_backend_must_be_controls_or_canvas");
			if (ViewportWidth < 320 || ViewportWidth > 7680 || ViewportHeight < 240 || ViewportHeight > 4320)
				throw new InvalidDataException("viewport_dimensions_out_of_range");
			if (MaxDisplayBytes < 1024 || MaxDisplayBytes > 64 * 1024 * 1024)
				throw new InvalidDataException("max_display_bytes_out_of_range");
			if (MaxTraceEvents < 100 || MaxTraceEvents > 5000000)
				throw new InvalidDataException("max_trace_events_out_of_range");

			string game = Path.GetFullPath(GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			string output = Path.GetFullPath(OutputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			if (output.StartsWith(game, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("output_directory_must_not_be_inside_game_root");

			if (inProcessCrossAbaRequested)
			{
				if (InProcessAlternateSession is null)
					throw new InvalidDataException("in_process_cross_aba_requires_alternate_session");
				if (string.IsNullOrWhiteSpace(InProcessAlternateSession.GameRoot)
					|| !Path.IsPathRooted(InProcessAlternateSession.GameRoot)
					|| !Directory.Exists(InProcessAlternateSession.GameRoot))
					throw new InvalidDataException("in_process_alternate_game_root_must_be_existing_absolute_directory");
				ValidateSupportedProfile(
					InProcessAlternateSession.Profile,
					"in_process_alternate_profile_must_be_v24pure_or_snake_or_erafl_or_megaten");

				string alternateGame = Path.GetFullPath(InProcessAlternateSession.GameRoot)
					.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
					+ Path.DirectorySeparatorChar;
				if (output.StartsWith(alternateGame, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException("output_directory_must_not_be_inside_alternate_game_root");

				var primaryLaunch = new LegacySessionLaunchConfiguration(
					game.TrimEnd(Path.DirectorySeparatorChar),
					NormalizeProfile(Profile));
				var alternateLaunch = new LegacySessionLaunchConfiguration(
					alternateGame.TrimEnd(Path.DirectorySeparatorChar),
					NormalizeProfile(InProcessAlternateSession.Profile));
				if (string.Equals(primaryLaunch.GameId, alternateLaunch.GameId, StringComparison.Ordinal)
					&& string.Equals(primaryLaunch.ProfileId, alternateLaunch.ProfileId, StringComparison.Ordinal))
					throw new InvalidDataException("in_process_cross_aba_requires_distinct_session_binding");

				InProcessAlternateSession.GameRoot = alternateGame.TrimEnd(Path.DirectorySeparatorChar);
				InProcessAlternateSession.Profile = NormalizeProfile(InProcessAlternateSession.Profile);
			}
			else if (InProcessAlternateSession is not null)
			{
				throw new InvalidDataException("in_process_alternate_session_requires_cross_aba");
			}

			Inputs ??= new List<LegacyRunnerInput>();
			if (inProcessSessionCycleRequested && Inputs.Count != 0)
				throw new InvalidDataException("in_process_session_cycle_does_not_support_inputs");
			foreach (var input in Inputs)
			{
				if (input == null)
					throw new InvalidDataException("input_entry_must_not_be_null");
				if (input.WaitTimeoutMs < 100 || input.WaitTimeoutMs > MaxRuntimeMs)
					throw new InvalidDataException("input_wait_timeout_ms_out_of_range");
				if (input.MouseButton != 0 && input.MouseButton != 1 && input.MouseButton != 2 && input.MouseButton != 4)
					throw new InvalidDataException("mouse_button_must_be_0_1_2_or_4");
				input.ExpectedInputType ??= "";
				input.ExpectedInputType = NormalizeExpectedInputType(input.ExpectedInputType);
				if (input.ExpectedInputType.Length > 0
					&& input.ExpectedInputType != "EnterKey"
					&& input.ExpectedInputType != "AnyKey"
					&& input.ExpectedInputType != "IntValue"
					&& input.ExpectedInputType != "StrValue")
					throw new InvalidDataException("expected_input_type_not_supported");
				if (input.ExpectedButtonGeneration < 0)
					throw new InvalidDataException("expected_button_generation_out_of_range");
				input.Value ??= "";
			}

			GameRoot = game.TrimEnd(Path.DirectorySeparatorChar);
			OutputDirectory = output.TrimEnd(Path.DirectorySeparatorChar);
			Profile = NormalizeProfile(Profile);
			SessionIsolationMode = string.Equals(
				SessionIsolationMode,
				SessionIsolationModeCanary,
				StringComparison.OrdinalIgnoreCase)
				? SessionIsolationModeCanary
				: SessionIsolationModeBaseline;
			InProcessSessionCycle = string.Equals(
				InProcessSessionCycle,
				InProcessSessionCycleAba,
				StringComparison.OrdinalIgnoreCase)
				? InProcessSessionCycleAba
				: string.Equals(
					InProcessSessionCycle,
					InProcessSessionCycleCrossAba,
					StringComparison.OrdinalIgnoreCase)
					? InProcessSessionCycleCrossAba
					: InProcessSessionCycleDisabled;
			DisplayBackend = string.Equals(DisplayBackend, "controls", StringComparison.OrdinalIgnoreCase)
				? "controls"
				: "canvas";
		}

		static string NormalizeExpectedInputType(string value)
		{
			string trimmed = (value ?? "").Trim();
			if (trimmed.Length == 0)
				return "";
			foreach (string supported in new[] { "EnterKey", "AnyKey", "IntValue", "StrValue" })
			{
				if (string.Equals(trimmed, supported, StringComparison.OrdinalIgnoreCase))
					return supported;
			}
			return trimmed;
		}

		static void ValidateSupportedProfile(string value, string error)
		{
			if (!string.Equals(value, global::FirstWindow.CoreProfileV24Pure, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(value, global::FirstWindow.CoreProfileSnake, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(value, global::FirstWindow.CoreProfileEraFl, StringComparison.OrdinalIgnoreCase)
				// megaten：新增合法 profile 值（不改变既有三项的判定）。
				&& !string.Equals(value, global::FirstWindow.CoreProfileMegaten, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(error);
		}

		static string NormalizeProfile(string value)
		{
			if (string.Equals(value, global::FirstWindow.CoreProfileSnake, StringComparison.OrdinalIgnoreCase))
				return global::FirstWindow.CoreProfileSnake;
			if (string.Equals(value, global::FirstWindow.CoreProfileEraFl, StringComparison.OrdinalIgnoreCase))
				return global::FirstWindow.CoreProfileEraFl;
			// megaten：归一化为规范小写 id，其余未知值仍回落 v24 基线。
			if (string.Equals(value, global::FirstWindow.CoreProfileMegaten, StringComparison.OrdinalIgnoreCase))
				return global::FirstWindow.CoreProfileMegaten;
			return global::FirstWindow.CoreProfileV24Pure;
		}
	}
}
