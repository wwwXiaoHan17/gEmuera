using Godot;
using GEmuera.Core.Application;
using GEmuera.Core.Parsing;
using GEmuera.Core.Session;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#nullable enable annotations

/// <summary>
/// Candidate Core projection used by the first prototype. It observes the
/// selected legacy game without changing the legacy parser or VM path.
/// </summary>
public partial class PrototypeRuntimeNode : Node
{
	[Signal]
	public delegate void PrototypeStatusChangedEventHandler(string status);

	[Export]
	public bool Enabled = true;

	public SessionGeneration Generation { get; private set; } = SessionGeneration.Initial;
	public string ProfileId { get; private set; } = string.Empty;
	public string PlanHash { get; private set; } = string.Empty;
	public int ParsedFileCount { get; private set; }
	public int ParsedInstructionCount { get; private set; }

	private readonly object commandGate = new();
	private readonly CancellationTokenSource lifetimeCancellation = new();
	private CoreApplicationRuntime runtime;
	private GameSession session;
	private PrototypeCommandPanel commandPanel;
	private Task activeCommand = Task.CompletedTask;
	private Task shutdownTask = Task.CompletedTask;
	private bool exiting;

	public override void _Ready()
	{
		if (!Enabled || !ReadBoolSetting("application/prototype_runtime", true))
			return;

		SubscribeCommandPanel();
		CallDeferred(nameof(InitializeDeferred));
	}

	private void InitializeDeferred()
	{
		QueueCommand(token => SwitchProfileAsync(NormalizeProfile(FirstWindow.SelectedCoreProfileName), token));
	}

	private void SubscribeCommandPanel()
	{
		commandPanel = GetParent()?.GetNodeOrNull<PrototypeCommandPanel>("PrototypeCommandPanel");
		if (commandPanel is null)
			return;

		commandPanel.RequestReload += OnRequestReload;
		commandPanel.RequestToggle += OnRequestToggle;
		commandPanel.RequestDetach += OnRequestDetach;
	}

	private void UnsubscribeCommandPanel()
	{
		if (commandPanel is null)
			return;

		commandPanel.RequestReload -= OnRequestReload;
		commandPanel.RequestToggle -= OnRequestToggle;
		commandPanel.RequestDetach -= OnRequestDetach;
		commandPanel = null;
	}

	private void OnRequestReload()
	{
		QueueCommand(token => SwitchProfileAsync(NormalizeProfile(ProfileId), token));
	}

	private void OnRequestToggle()
	{
		// 调试浮层的 profile 轮换：v24pure → snake → erafl → megaten → v24pure。
		// megaten 是 2026-09-07 新增的方言 profile（eraMegaten 适配），此前轮换缺环。
		string nextProfile = string.Equals(ProfileId, FirstWindow.CoreProfileV24Pure, StringComparison.OrdinalIgnoreCase)
			? FirstWindow.CoreProfileSnake
			: string.Equals(ProfileId, FirstWindow.CoreProfileSnake, StringComparison.OrdinalIgnoreCase)
				? FirstWindow.CoreProfileEraFl
				: string.Equals(ProfileId, FirstWindow.CoreProfileEraFl, StringComparison.OrdinalIgnoreCase)
					? FirstWindow.CoreProfileMegaten
					: FirstWindow.CoreProfileV24Pure;
		QueueCommand(token => SwitchProfileAsync(nextProfile, token));
	}

	private void OnRequestDetach()
	{
		QueueCommand(DetachSessionAsync);
	}

	private void QueueCommand(Func<CancellationToken, Task> command)
	{
		ArgumentNullException.ThrowIfNull(command);

		lock (commandGate)
		{
			if (exiting || lifetimeCancellation.IsCancellationRequested)
				return;

			activeCommand = RunQueuedCommandAsync(activeCommand, command, lifetimeCancellation.Token);
		}
	}

	private async Task RunQueuedCommandAsync(
		Task previous,
		Func<CancellationToken, Task> command,
		CancellationToken cancellationToken)
	{
		try
		{
			await previous;
			cancellationToken.ThrowIfCancellationRequested();
			await command(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Host shutdown owns cancellation; no UI projection is valid afterwards.
		}
		catch (Exception error)
		{
			if (CanProject(cancellationToken))
				EmitStatus("Core prototype unavailable: " + error.Message);
		}
	}

	private async Task SwitchProfileAsync(string profile, CancellationToken cancellationToken)
	{
		if (!CanProject(cancellationToken))
			return;

		var previousGeneration = Generation;
		var hadSession = session is not null;
		if (hadSession)
			DetachPrototypeBridges(previousGeneration.Value);

		runtime ??= new CoreApplicationRuntime();
		var selection = CreateSelection(profile);
		RuntimeSwitchResult result;
		try
		{
			result = await runtime.SwitchAsync(
				selection,
				new ContentToken("game:" + profile),
				cancellationToken);
		}
		catch
		{
			if (CanProject(cancellationToken))
				RestoreCurrentSessionProjection();
			throw;
		}

		if (!CanProject(cancellationToken))
			return;
		if (!result.IsCommitted || result.Session is null)
		{
			RestoreCurrentSessionProjection();
			throw result.Error ?? new InvalidOperationException("Core session was not committed.");
		}

		session = result.Session;
		Generation = session.Generation;
		ProfileId = session.Selection.ProfileId;
		PlanHash = session.Compatibility.CanonicalHash;
		ParsedFileCount = 0;
		ParsedInstructionCount = 0;
		AttachPrototypeBridges();
		EmitStatus($"Core prototype ready | {ProfileId} | {ShortPlanHash()}");

		var gamePath = FirstWindow.ResolveStartupGamePath();
		if (!string.IsNullOrWhiteSpace(gamePath) && Directory.Exists(gamePath))
			ParseRepresentativeScript(gamePath);
	}

	private async Task DetachSessionAsync(CancellationToken cancellationToken)
	{
		if (!CanProject(cancellationToken) || runtime is null)
			return;

		DetachPrototypeBridges(Generation.Value);
		await runtime.StopAsync();
		if (!CanProject(cancellationToken))
			return;

		session = null;
		ParsedFileCount = 0;
		ParsedInstructionCount = 0;
		commandPanel?.SetSessionInfo("detached", Generation.Value, ShortPlanHash());
		EmitStatus($"Core prototype detached | gen {Generation.Value}");
	}

	private static SessionSelection CreateSelection(string profile)
	{
		string? saveProfile = profile switch
		{
			FirstWindow.CoreProfileSnake => "gemuera.snake",
			FirstWindow.CoreProfileEraFl => GEmuera.Core.Compatibility.EraFlCompatibilityModule.SaveProfileId,
			// megaten 此前落到默认分支 gemuera.v24，调试会话的存档 profile 指向错误（2026-09-07）。
			FirstWindow.CoreProfileMegaten => GEmuera.Core.Compatibility.MegatenCompatibilityModule.SaveProfileId,
			_ => "gemuera.v24",
		};
		return new SessionSelection(
			"prototype." + profile,
			profile,
			saveProfileId: saveProfile);
	}

	private void RestoreCurrentSessionProjection()
	{
		var current = runtime?.Current;
		if (current is null)
		{
			session = null;
			return;
		}

		session = current;
		Generation = current.Generation;
		ProfileId = current.Selection.ProfileId;
		PlanHash = current.Compatibility.CanonicalHash;
		AttachPrototypeBridges();
	}

	private void ParseRepresentativeScript(string gamePath)
	{
		var erbDirectory = Directory.EnumerateDirectories(gamePath)
			.FirstOrDefault(path => string.Equals(Path.GetFileName(path), "erb", StringComparison.OrdinalIgnoreCase));
		if (erbDirectory is null)
			return;

		var scriptPath = Directory.EnumerateFiles(erbDirectory, "*", SearchOption.AllDirectories)
			.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".erb", StringComparison.OrdinalIgnoreCase));
		if (scriptPath is null)
			return;

		var source = File.ReadAllText(scriptPath);
		var result = session.Parse(source, "erb:" + Path.GetFileName(scriptPath));
		ParsedFileCount = 1;
		ParsedInstructionCount = result.Lines.Count(line => line.Kind == ErbLineKind.Instruction);
		EmitStatus($"Core prototype ready | {ProfileId} | parsed {ParsedInstructionCount} instructions");
	}

	private static string NormalizeProfile(string profile)
	{
		if (string.Equals(profile, FirstWindow.CoreProfileSnake, StringComparison.OrdinalIgnoreCase))
			return FirstWindow.CoreProfileSnake;
		if (string.Equals(profile, FirstWindow.CoreProfileEraFl, StringComparison.OrdinalIgnoreCase))
			return FirstWindow.CoreProfileEraFl;
		// megaten 会话此前落到默认分支，右上角调试浮层误显示 v24pure（2026-09-07 eraMegaten 实证）。
		if (string.Equals(profile, FirstWindow.CoreProfileMegaten, StringComparison.OrdinalIgnoreCase))
			return FirstWindow.CoreProfileMegaten;
		return FirstWindow.CoreProfileV24Pure;
	}

	private void EmitStatus(string status)
	{
		if (!exiting && IsInsideTree())
			EmitSignal(SignalName.PrototypeStatusChanged, status);
	}

	private void AttachPrototypeBridges()
	{
		if (exiting || !IsInsideTree())
			return;

		var parent = GetParent();
		parent.GetNodeOrNull<PrototypeResourceBridge>("PrototypeResourceBridge")?.AttachGeneration(Generation.Value);
		parent.GetNodeOrNull<PrototypeAudioBridge>("PrototypeAudioBridge")?.AttachGeneration(Generation.Value);
		parent.GetNodeOrNull<PrototypeInputBridge>("PrototypeInputBridge")?.AttachGeneration(Generation.Value);
		commandPanel?.SetSessionInfo(ProfileId, Generation.Value, ShortPlanHash());
	}

	private void DetachPrototypeBridges(long generation)
	{
		if (!IsInsideTree())
			return;

		var parent = GetParent();
		parent.GetNodeOrNull<PrototypeInputBridge>("PrototypeInputBridge")?.DetachGeneration(generation);
		parent.GetNodeOrNull<PrototypeAudioBridge>("PrototypeAudioBridge")?.DetachGeneration(generation);
		parent.GetNodeOrNull<PrototypeResourceBridge>("PrototypeResourceBridge")?.DetachGeneration(generation);
	}

	private string ShortPlanHash()
	{
		return string.IsNullOrEmpty(PlanHash)
			? string.Empty
			: PlanHash[..Math.Min(12, PlanHash.Length)];
	}

	private bool CanProject(CancellationToken cancellationToken)
	{
		return !exiting && !cancellationToken.IsCancellationRequested && IsInsideTree();
	}

	private static bool ReadBoolSetting(string path, bool fallback)
	{
		return !ProjectSettings.HasSetting(path) ? fallback : ProjectSettings.GetSetting(path, fallback).AsBool();
	}

	public override void _ExitTree()
	{
		if (exiting)
			return;

		exiting = true;
		UnsubscribeCommandPanel();
		lifetimeCancellation.Cancel();
		DetachPrototypeBridges(Generation.Value);

		Task command;
		lock (commandGate)
			command = activeCommand;
		shutdownTask = ShutdownAsync(command, runtime);
		session = null;
	}

	private async Task ShutdownAsync(Task command, CoreApplicationRuntime runtimeToDispose)
	{
		try
		{
			await command.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception error)
		{
			GD.PushWarning("Core prototype command shutdown failed: " + error.Message);
		}

		try
		{
			if (runtimeToDispose is not null)
				await runtimeToDispose.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception error)
		{
			Console.Error.WriteLine("Core prototype runtime shutdown failed: " + error.Message);
		}
		finally
		{
			lifetimeCancellation.Dispose();
		}
	}
}
