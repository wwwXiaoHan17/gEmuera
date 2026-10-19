using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// The typed effects understood by the prototype audio bridge. The bridge
/// intentionally does not know ERB instruction names or legacy script types.
/// </summary>
public enum PrototypeAudioEffectKind
{
    PlayBgm,
    StopBgm,
    PlaySound,
    StopSound,
    StopAllSounds,
    SetBgmVolume,
    SetSoundVolume,
    Pause,
    Resume
}

public enum PrototypeAudioBridgeState
{
    Detached,
    Ready,
    Paused
}

/// <summary>
/// Immutable, Godot-bound command DTO. Core-side code can map its own
/// AudioEffect DTO into this object without receiving a Node reference.
/// AudioStream is the resource handle owned by the bridge boundary.
/// </summary>
public sealed class PrototypeAudioEffectCommand
{
    public PrototypeAudioEffectCommand(
        long requestId,
        long generation,
        PrototypeAudioEffectKind effect,
        AudioStream stream = null,
        int channel = -1,
        int repeatCount = 1,
        float volumeLinear = 1.0f,
        float pitchScale = 1.0f)
    {
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (!float.IsFinite(volumeLinear))
            throw new ArgumentOutOfRangeException(nameof(volumeLinear));
        if (!float.IsFinite(pitchScale))
            throw new ArgumentOutOfRangeException(nameof(pitchScale));
        if (repeatCount == 0 && (effect == PrototypeAudioEffectKind.PlayBgm || effect == PrototypeAudioEffectKind.PlaySound))
            throw new ArgumentOutOfRangeException(nameof(repeatCount), "Repeat count must be -1 or at least 1.");
        if ((effect == PrototypeAudioEffectKind.PlayBgm || effect == PrototypeAudioEffectKind.PlaySound) && stream == null)
            throw new ArgumentNullException(nameof(stream));

        RequestId = requestId;
        Generation = generation;
        Effect = effect;
        Stream = stream;
        Channel = channel;
        RepeatCount = repeatCount;
        VolumeLinear = ClampVolume(volumeLinear);
        PitchScale = MathF.Max(0.01f, pitchScale);
    }

    public long RequestId { get; }
    public long Generation { get; }
    public PrototypeAudioEffectKind Effect { get; }
    public AudioStream Stream { get; }
    public int Channel { get; }
    public int RepeatCount { get; }
    public float VolumeLinear { get; }
    public float PitchScale { get; }

    public bool Loops => RepeatCount < 0;

    public static PrototypeAudioEffectCommand PlayBgm(
        long requestId,
        long generation,
        AudioStream stream,
        bool loop = true,
        float volumeLinear = 1.0f,
        float pitchScale = 1.0f)
    {
        return new PrototypeAudioEffectCommand(
            requestId,
            generation,
            PrototypeAudioEffectKind.PlayBgm,
            stream,
            repeatCount: loop ? -1 : 1,
            volumeLinear: volumeLinear,
            pitchScale: pitchScale);
    }

    public static PrototypeAudioEffectCommand StopBgm(long requestId, long generation)
    {
        return new PrototypeAudioEffectCommand(requestId, generation, PrototypeAudioEffectKind.StopBgm);
    }

    public static PrototypeAudioEffectCommand PlaySound(
        long requestId,
        long generation,
        AudioStream stream,
        int channel = -1,
        int repeatCount = 1,
        float volumeLinear = 1.0f,
        float pitchScale = 1.0f)
    {
        return new PrototypeAudioEffectCommand(
            requestId,
            generation,
            PrototypeAudioEffectKind.PlaySound,
            stream,
            channel,
            repeatCount,
            volumeLinear,
            pitchScale);
    }

    public static PrototypeAudioEffectCommand StopSound(long requestId, long generation, int channel)
    {
        return new PrototypeAudioEffectCommand(
            requestId,
            generation,
            PrototypeAudioEffectKind.StopSound,
            channel: channel);
    }

    public static PrototypeAudioEffectCommand StopAllSounds(long requestId, long generation)
    {
        return new PrototypeAudioEffectCommand(requestId, generation, PrototypeAudioEffectKind.StopAllSounds);
    }

    public static PrototypeAudioEffectCommand SetBgmVolume(long requestId, long generation, float volumeLinear)
    {
        return new PrototypeAudioEffectCommand(
            requestId,
            generation,
            PrototypeAudioEffectKind.SetBgmVolume,
            volumeLinear: volumeLinear);
    }

    public static PrototypeAudioEffectCommand SetSoundVolume(long requestId, long generation, float volumeLinear)
    {
        return new PrototypeAudioEffectCommand(
            requestId,
            generation,
            PrototypeAudioEffectKind.SetSoundVolume,
            volumeLinear: volumeLinear);
    }

    public static PrototypeAudioEffectCommand Pause(long requestId, long generation)
    {
        return new PrototypeAudioEffectCommand(requestId, generation, PrototypeAudioEffectKind.Pause);
    }

    public static PrototypeAudioEffectCommand Resume(long requestId, long generation)
    {
        return new PrototypeAudioEffectCommand(requestId, generation, PrototypeAudioEffectKind.Resume);
    }

    private static float ClampVolume(float value)
    {
        return MathF.Min(1.0f, MathF.Max(0.0f, value));
    }
}

/// <summary>
/// First-round Godot audio/lifecycle bridge.
///
/// The node owns a pre-created BGM player and bounded SFX voice pool. All
/// commands are applied on the node's main-thread process tick, are accepted
/// only for the attached generation, and are rejected after detach or pause.
/// CompatibilityMode is deliberately enabled by default: BGM replacement is
/// immediate and pool pressure never steals an explicitly playing voice.
/// </summary>
public partial class PrototypeAudioBridge : Node
{
    private const int HardMaximumPoolSize = 64;
    private const int HardMaximumPendingCommands = 256;
    private const string BgmRole = "bgm";
    private const string SoundRole = "sound";

    [Signal]
    public delegate void BridgeStateChangedEventHandler(string state, long generation, bool attached, bool paused);

    [Signal]
    public delegate void GenerationChangedEventHandler(long generation, bool attached);

    [Signal]
    public delegate void CommandQueuedEventHandler(long requestId, long generation, string effect, int pendingCommands);

    [Signal]
    public delegate void CommandAppliedEventHandler(long requestId, long generation, string effect, int channel);

    [Signal]
    public delegate void CommandRejectedEventHandler(long requestId, long generation, string effect, string reason);

    [Signal]
    public delegate void PlaybackStateChangedEventHandler(
        long generation,
        long trackGeneration,
        string role,
        int channel,
        long requestId,
        string state);

    [Signal]
    public delegate void PlaybackFinishedEventHandler(
        long generation,
        long trackGeneration,
        string role,
        int channel,
        long requestId);

    [Signal]
    public delegate void PoolStateChangedEventHandler(
        long generation,
        int activeVoices,
        int poolCapacity,
        int pendingCommands);

    [Signal]
    public delegate void VoiceStolenEventHandler(
        long generation,
        int channel,
        long oldRequestId,
        long newRequestId);

    [Signal]
    public delegate void AudioFaultEventHandler(long requestId, long generation, string code, string message);

    [Signal]
    public delegate void ApplicationPausedEventHandler(long generation);

    [Signal]
    public delegate void ApplicationResumedEventHandler(long generation);

    [Export]
    public int VoicePoolSize = 8;

    [Export]
    public int MaxConcurrentVoices = 8;

    [Export]
    public int MaxPendingCommands = 64;

    [Export]
    public int MaxCommandsPerProcess = 16;

    [Export]
    public bool CompatibilityMode = true;

    [Export]
    public bool AllowVoiceStealing = false;

    [Export]
    public bool PauseOnApplicationNotification = true;

    [Export]
    public string BgmBus = "Master";

    [Export]
    public string SoundBus = "Master";

    private readonly Queue<PrototypeAudioEffectCommand> pendingCommands = new();
    private readonly List<AudioVoiceSlot> soundPool = new();
    private AudioVoiceSlot bgmSlot;
    private bool isReady;
    private bool isExiting;
    private bool generationAttached;
    private bool applicationPaused;
    private long currentGeneration;
    private long requestSequence;
    private long trackSequence;
    private float bgmScriptGain = 1.0f;
    private float soundScriptGain = 1.0f;
    private float bgmUserGain = 1.0f;
    private float soundUserGain = 1.0f;

    public PrototypeAudioBridgeState State { get; private set; } = PrototypeAudioBridgeState.Detached;

    public long CurrentGeneration => currentGeneration;
    public bool IsGenerationAttached => generationAttached;
    public bool IsApplicationPaused => applicationPaused;
    public int PendingCommandCount => pendingCommands.Count;
    public int PoolCapacity => soundPool.Count > 0 ? soundPool.Count : ClampPoolSize(VoicePoolSize);
    public int ActiveVoiceCount => CountActiveSoundVoices();
    public bool IsBgmPlaying => bgmSlot is not null && bgmSlot.Active;
    public long BgmTrackGeneration => bgmSlot?.TrackGeneration ?? 0;

    public override void _Ready()
    {
        isReady = true;
        ProcessMode = Node.ProcessModeEnum.Always;
        SetProcess(true);
        EnsurePool();
        SetBridgeState(generationAttached
            ? (applicationPaused ? PrototypeAudioBridgeState.Paused : PrototypeAudioBridgeState.Ready)
            : PrototypeAudioBridgeState.Detached);
        EmitPoolState();
    }

    public override void _Process(double delta)
    {
        if (!isReady || isExiting || pendingCommands.Count == 0)
            return;

        DrainCommandQueue();
    }

    public override void _Notification(int what)
    {
        if (!PauseOnApplicationNotification || !isReady || isExiting)
            return;

        if (what == NotificationApplicationPaused)
            Pause();
        else if (what == NotificationApplicationResumed)
            Resume();
    }

    public override void _ExitTree()
    {
        isExiting = true;
        generationAttached = false;
        applicationPaused = false;
        pendingCommands.Clear();

        DetachSlot(bgmSlot);
        for (int i = 0; i < soundPool.Count; i++)
            DetachSlot(soundPool[i]);

        bgmSlot = null;
        soundPool.Clear();
        State = PrototypeAudioBridgeState.Detached;
    }

    /// <summary>
    /// Commits a session generation to this bridge. A newer generation clears
    /// every old stream, playback continuation and queued command first.
    /// </summary>
    public bool CommitGeneration(long generation)
    {
        if (generation < 0 || generation < currentGeneration)
            return false;
        if (generationAttached && generation == currentGeneration)
            return true;

        ClearRuntimeState(emitPlaybackSignals: isReady);
        currentGeneration = generation;
        generationAttached = true;
        applicationPaused = false;
        SetBridgeState(PrototypeAudioBridgeState.Ready);
        EnsurePool();
        EmitSignal(SignalName.GenerationChanged, currentGeneration, true);
        EmitPoolState();
        return true;
    }

    /// <summary>
    /// Alias used by session hosts that attach an already committed session.
    /// </summary>
    public bool AttachGeneration(long generation)
    {
        return CommitGeneration(generation);
    }

    /// <summary>
    /// Detaches the current session and leaves the node reusable for a later
    /// generation. A detached node rejects all old and future commands until a
    /// new generation is committed.
    /// </summary>
    public bool DetachGeneration(long generation = -1)
    {
        long expectedGeneration = generation < 0 ? currentGeneration : generation;
        if (!generationAttached)
            return expectedGeneration == currentGeneration;
        if (expectedGeneration != currentGeneration)
            return false;

        ClearRuntimeState(emitPlaybackSignals: isReady);
        generationAttached = false;
        applicationPaused = false;
        SetBridgeState(PrototypeAudioBridgeState.Detached);
        EmitSignal(SignalName.GenerationChanged, currentGeneration, false);
        EmitPoolState();
        return true;
    }

    /// <summary>
    /// Resets playback and pending effects without changing the attached
    /// generation. User bus gains survive; script-controlled gains reset.
    /// </summary>
    public void Reset()
    {
        ClearRuntimeState(emitPlaybackSignals: isReady);
        applicationPaused = false;
        SetBridgeState(generationAttached
            ? PrototypeAudioBridgeState.Ready
            : PrototypeAudioBridgeState.Detached);
        EmitPoolState();
    }

    /// <summary>
    /// Enqueues one bounded, generation-scoped effect. Callers should invoke
    /// this on the Godot main thread; the node applies it from _Process.
    /// </summary>
    public bool Enqueue(PrototypeAudioEffectCommand command)
    {
        if (command is null)
            return false;
        if (!isReady || isExiting)
            return RejectCommand(command, "not_ready");
        if (!generationAttached)
            return RejectCommand(command, "detached");
        if (command.Generation != currentGeneration)
            return RejectCommand(command, "stale_generation");
        if (applicationPaused && command.Effect != PrototypeAudioEffectKind.Resume)
            return RejectCommand(command, "application_paused");
        if (pendingCommands.Count >= EffectiveMaxPendingCommands())
            return RejectCommand(command, "command_queue_full");

        pendingCommands.Enqueue(command);
        EmitSignal(
            SignalName.CommandQueued,
            command.RequestId,
            command.Generation,
            command.Effect.ToString(),
            pendingCommands.Count);
        EmitPoolState();
        return true;
    }

    public bool EnqueueCommand(PrototypeAudioEffectCommand command)
    {
        return Enqueue(command);
    }

    public long AllocateRequestId()
    {
        if (requestSequence == long.MaxValue)
            requestSequence = 0;
        return ++requestSequence;
    }

    public bool QueuePlayBgm(
        long requestId,
        long generation,
        AudioStream stream,
        bool loop = true,
        float volumeLinear = 1.0f,
        float pitchScale = 1.0f)
    {
        return Enqueue(PrototypeAudioEffectCommand.PlayBgm(
            NormalizeRequestId(requestId),
            generation,
            stream,
            loop,
            volumeLinear,
            pitchScale));
    }

    public bool QueueStopBgm(long requestId, long generation)
    {
        return Enqueue(PrototypeAudioEffectCommand.StopBgm(NormalizeRequestId(requestId), generation));
    }

    public bool QueuePlaySound(
        long requestId,
        long generation,
        AudioStream stream,
        int channel = -1,
        int repeatCount = 1,
        float volumeLinear = 1.0f,
        float pitchScale = 1.0f)
    {
        return Enqueue(PrototypeAudioEffectCommand.PlaySound(
            NormalizeRequestId(requestId),
            generation,
            stream,
            channel,
            repeatCount,
            volumeLinear,
            pitchScale));
    }

    public bool QueueStopSound(long requestId, long generation, int channel)
    {
        return Enqueue(PrototypeAudioEffectCommand.StopSound(
            NormalizeRequestId(requestId),
            generation,
            channel));
    }

    public bool QueueStopAllSounds(long requestId, long generation)
    {
        return Enqueue(PrototypeAudioEffectCommand.StopAllSounds(NormalizeRequestId(requestId), generation));
    }

    public bool QueueSetBgmVolume(long requestId, long generation, float volumeLinear)
    {
        return Enqueue(PrototypeAudioEffectCommand.SetBgmVolume(
            NormalizeRequestId(requestId),
            generation,
            volumeLinear));
    }

    public bool QueueSetSoundVolume(long requestId, long generation, float volumeLinear)
    {
        return Enqueue(PrototypeAudioEffectCommand.SetSoundVolume(
            NormalizeRequestId(requestId),
            generation,
            volumeLinear));
    }

    public bool QueuePause(long requestId, long generation)
    {
        return Enqueue(PrototypeAudioEffectCommand.Pause(NormalizeRequestId(requestId), generation));
    }

    public bool QueueResume(long requestId, long generation)
    {
        return Enqueue(PrototypeAudioEffectCommand.Resume(NormalizeRequestId(requestId), generation));
    }

    /// <summary>
    /// Pauses all attached players while preserving script-level paused state.
    /// </summary>
    public bool Pause()
    {
        if (!generationAttached || applicationPaused)
            return generationAttached;

        applicationPaused = true;
        PauseSlot(bgmSlot);
        for (int i = 0; i < soundPool.Count; i++)
            PauseSlot(soundPool[i]);
        SetBridgeState(PrototypeAudioBridgeState.Paused);
        EmitSignal(SignalName.ApplicationPaused, currentGeneration);
        EmitPoolState();
        return true;
    }

    /// <summary>
    /// Restores all players to their script-controlled pause state.
    /// </summary>
    public bool Resume()
    {
        if (!generationAttached || !applicationPaused)
            return generationAttached;

        applicationPaused = false;
        ResumeSlot(bgmSlot);
        for (int i = 0; i < soundPool.Count; i++)
            ResumeSlot(soundPool[i]);
        SetBridgeState(PrototypeAudioBridgeState.Ready);
        EmitSignal(SignalName.ApplicationResumed, currentGeneration);
        EmitPoolState();
        return true;
    }

    public void SetBgmUserGain(float linearGain)
    {
        bgmUserGain = ClampVolume(linearGain);
        ApplyBgmVolume();
    }

    public void SetSoundUserGain(float linearGain)
    {
        soundUserGain = ClampVolume(linearGain);
        ApplySoundVolumes();
    }

    public void SetBusRouting(string bgmBus, string soundBus)
    {
        BgmBus = NormalizeBusName(bgmBus);
        SoundBus = NormalizeBusName(soundBus);
        if (bgmSlot is not null)
            bgmSlot.Player.Bus = ResolveBusName(BgmBus);
        for (int i = 0; i < soundPool.Count; i++)
            soundPool[i].Player.Bus = ResolveBusName(SoundBus);
    }

    /// <summary>
    /// Returns only Variant-safe state for diagnostics or a status projection.
    /// It never exposes a Node, stream, or callback reference.
    /// </summary>
    public Godot.Collections.Dictionary GetStateSnapshot()
    {
        var snapshot = new Godot.Collections.Dictionary();
        snapshot["state"] = State.ToString();
        snapshot["generation"] = currentGeneration;
        snapshot["attached"] = generationAttached;
        snapshot["paused"] = applicationPaused;
        snapshot["pending_commands"] = pendingCommands.Count;
        snapshot["active_voices"] = ActiveVoiceCount;
        snapshot["pool_capacity"] = PoolCapacity;
        snapshot["compatibility_mode"] = CompatibilityMode;
        snapshot["voice_stealing"] = !CompatibilityMode && AllowVoiceStealing;
        return snapshot;
    }

    private void DrainCommandQueue()
    {
        int budget = EffectiveMaxCommandsPerProcess();
        int processed = 0;
        while (processed < budget && pendingCommands.Count > 0)
        {
            var command = pendingCommands.Dequeue();
            processed++;

            if (!generationAttached)
            {
                RejectCommand(command, "detached");
                continue;
            }
            if (command.Generation != currentGeneration)
            {
                RejectCommand(command, "stale_generation");
                continue;
            }
            if (applicationPaused && command.Effect != PrototypeAudioEffectKind.Resume)
            {
                RejectCommand(command, "application_paused");
                continue;
            }

            int channel;
            string rejection;
            try
            {
                if (TryApplyCommand(command, out channel, out rejection))
                {
                    EmitSignal(
                        SignalName.CommandApplied,
                        command.RequestId,
                        command.Generation,
                        command.Effect.ToString(),
                        channel);
                }
                else
                {
                    RejectCommand(command, rejection);
                }
            }
            catch (Exception error)
            {
                EmitSignal(
                    SignalName.AudioFault,
                    command.RequestId,
                    command.Generation,
                    "command_exception",
                    error.Message);
                RejectCommand(command, "command_exception");
            }
        }

        EmitPoolState();
    }

    private bool TryApplyCommand(
        PrototypeAudioEffectCommand command,
        out int channel,
        out string rejection)
    {
        channel = -1;
        rejection = string.Empty;

        switch (command.Effect)
        {
            case PrototypeAudioEffectKind.PlayBgm:
                return TryPlayBgm(command, out rejection);
            case PrototypeAudioEffectKind.StopBgm:
                StopSlot(bgmSlot, "command_stop", emitSignal: true);
                return true;
            case PrototypeAudioEffectKind.PlaySound:
                return TryPlaySound(command, out channel, out rejection);
            case PrototypeAudioEffectKind.StopSound:
                if (!TryGetSoundSlot(command.Channel, out var soundSlot))
                {
                    rejection = "channel_unavailable";
                    return false;
                }
                StopSlot(soundSlot, "command_stop", emitSignal: true);
                channel = soundSlot.Channel;
                return true;
            case PrototypeAudioEffectKind.StopAllSounds:
                StopAllSoundSlots("command_stop_all");
                return true;
            case PrototypeAudioEffectKind.SetBgmVolume:
                bgmScriptGain = ClampVolume(command.VolumeLinear);
                ApplyBgmVolume();
                return true;
            case PrototypeAudioEffectKind.SetSoundVolume:
                soundScriptGain = ClampVolume(command.VolumeLinear);
                ApplySoundVolumes();
                return true;
            case PrototypeAudioEffectKind.Pause:
                Pause();
                return true;
            case PrototypeAudioEffectKind.Resume:
                Resume();
                return true;
            default:
                rejection = "unknown_effect";
                return false;
        }
    }

    private bool TryPlayBgm(PrototypeAudioEffectCommand command, out string rejection)
    {
        rejection = string.Empty;
        if (command.Stream is null)
        {
            rejection = "missing_stream";
            return false;
        }

        if (bgmSlot is null)
        {
            rejection = "bgm_player_unavailable";
            return false;
        }

        StopSlot(bgmSlot, "command_replace", emitSignal: true);
        StartSlot(bgmSlot, command, BgmRole);
        return true;
    }

    private bool TryPlaySound(
        PrototypeAudioEffectCommand command,
        out int channel,
        out string rejection)
    {
        channel = -1;
        rejection = string.Empty;
        AudioVoiceSlot slot;

        if (command.Channel >= 0)
        {
            if (!TryGetSoundSlot(command.Channel, out slot))
            {
                rejection = "channel_unavailable";
                return false;
            }

            if (!slot.Active && CountActiveSoundVoices() >= EffectiveMaxConcurrentVoices())
            {
                rejection = "concurrency_limit";
                return false;
            }
        }
        else
        {
            slot = FindAvailableSoundSlot();
            if (slot is null)
            {
                if (!CanStealVoice(out slot))
                {
                    rejection = "pool_exhausted";
                    return false;
                }

                long oldRequestId = slot.RequestId;
                StopSlot(slot, "voice_stolen", emitSignal: true);
                EmitSignal(SignalName.VoiceStolen, currentGeneration, slot.Channel, oldRequestId, command.RequestId);
            }
            else if (CountActiveSoundVoices() >= EffectiveMaxConcurrentVoices())
            {
                rejection = "concurrency_limit";
                return false;
            }
        }

        if (slot.Active)
            StopSlot(slot, "command_replace", emitSignal: true);

        channel = slot.Channel;
        StartSlot(slot, command, SoundRole);
        return true;
    }

    private bool CanStealVoice(out AudioVoiceSlot slot)
    {
        slot = null;
        if (CompatibilityMode || !AllowVoiceStealing)
            return false;

        long oldestTrack = long.MaxValue;
        for (int i = 0; i < soundPool.Count; i++)
        {
            var candidate = soundPool[i];
            if (!candidate.Active || candidate.TrackGeneration >= oldestTrack)
                continue;
            oldestTrack = candidate.TrackGeneration;
            slot = candidate;
        }

        return slot is not null;
    }

    private AudioVoiceSlot FindAvailableSoundSlot()
    {
        for (int i = 0; i < soundPool.Count; i++)
        {
            if (!soundPool[i].Active)
                return soundPool[i];
        }

        return null;
    }

    private bool TryGetSoundSlot(int channel, out AudioVoiceSlot slot)
    {
        slot = null;
        if (channel < 0 || channel >= soundPool.Count)
            return false;
        slot = soundPool[channel];
        return true;
    }

    private void StartSlot(AudioVoiceSlot slot, PrototypeAudioEffectCommand command, string role)
    {
        slot.Generation = currentGeneration;
        slot.TrackGeneration = ++trackSequence;
        slot.RequestId = command.RequestId;
        slot.RepeatCount = command.RepeatCount;
        slot.CommandVolumeLinear = command.VolumeLinear;
        slot.ApplicationPausedBeforePause = false;
        slot.Active = true;
        slot.Player.Stream = command.Stream;
        slot.Player.PitchScale = command.PitchScale;
        slot.Player.StreamPaused = false;
        ApplySlotVolume(slot, role == BgmRole);
        slot.Player.Play();
        if (applicationPaused)
            slot.Player.StreamPaused = true;
        EmitPlaybackState(slot, role, "playing");
    }

    private void OnPlayerFinished(AudioVoiceSlot slot)
    {
        if (isExiting || !isReady || !generationAttached || !slot.Active)
            return;
        if (slot.Generation != currentGeneration)
            return;
        if (applicationPaused)
        {
            slot.Player.StreamPaused = true;
            return;
        }

        string role = slot.IsBgm ? BgmRole : SoundRole;
        if (slot.RepeatCount < 0 || slot.RepeatCount > 1)
        {
            if (slot.RepeatCount > 1)
                slot.RepeatCount--;
            slot.Player.StreamPaused = false;
            slot.Player.Play();
            EmitPlaybackState(slot, role, "replayed");
            return;
        }

        long generation = slot.Generation;
        long trackGeneration = slot.TrackGeneration;
        long requestId = slot.RequestId;
        int channel = slot.Channel;
        slot.Active = false;
        slot.RepeatCount = 0;
        slot.Player.StreamPaused = false;
        slot.Player.Stream = null;
        slot.Generation = 0;
        slot.RequestId = 0;
        slot.CommandVolumeLinear = 1.0f;
        EmitSignal(SignalName.PlaybackFinished, generation, trackGeneration, role, channel, requestId);
        EmitPlaybackState(slot, role, "finished", generation, trackGeneration, requestId, channel);
        EmitPoolState();
    }

    private void StopAllSoundSlots(string state)
    {
        for (int i = 0; i < soundPool.Count; i++)
            StopSlot(soundPool[i], state, emitSignal: true);
    }

    private bool StopSlot(AudioVoiceSlot slot, string state, bool emitSignal)
    {
        if (slot is null)
            return false;

        bool wasActive = slot.Active;
        long generation = slot.Generation;
        long trackGeneration = slot.TrackGeneration;
        long requestId = slot.RequestId;
        string role = slot.IsBgm ? BgmRole : SoundRole;
        slot.Active = false;
        slot.RepeatCount = 0;
        slot.Generation = 0;
        slot.RequestId = 0;
        slot.TrackGeneration = ++trackSequence;
        slot.CommandVolumeLinear = 1.0f;
        slot.ApplicationPausedBeforePause = false;
        if (slot.Player is not null)
        {
            slot.Player.Stop();
            slot.Player.StreamPaused = false;
            slot.Player.Stream = null;
            slot.Player.PitchScale = 1.0f;
        }

        if (emitSignal && wasActive)
            EmitPlaybackState(slot, role, state, generation, trackGeneration, requestId, slot.Channel);
        return wasActive;
    }

    private void PauseSlot(AudioVoiceSlot slot)
    {
        if (slot is null || !slot.Active || slot.Player is null)
            return;

        slot.ApplicationPausedBeforePause = slot.Player.StreamPaused;
        slot.Player.StreamPaused = true;
    }

    private void ResumeSlot(AudioVoiceSlot slot)
    {
        if (slot is null || !slot.Active || slot.Player is null)
            return;

        slot.Player.StreamPaused = slot.ApplicationPausedBeforePause;
        slot.ApplicationPausedBeforePause = false;
    }

    private void ClearRuntimeState(bool emitPlaybackSignals)
    {
        pendingCommands.Clear();
        StopSlot(bgmSlot, "reset", emitPlaybackSignals);
        for (int i = 0; i < soundPool.Count; i++)
            StopSlot(soundPool[i], "reset", emitPlaybackSignals);
        bgmScriptGain = 1.0f;
        soundScriptGain = 1.0f;
        applicationPaused = false;
        ApplyBgmVolume();
        ApplySoundVolumes();
    }

    private void DetachSlot(AudioVoiceSlot slot)
    {
        if (slot is null || slot.Player is null)
            return;

        slot.Active = false;
        slot.RepeatCount = 0;
        slot.Generation = 0;
        slot.RequestId = 0;
        slot.Player.Stop();
        slot.Player.StreamPaused = false;
        slot.Player.Stream = null;
        if (slot.FinishedHandler is not null)
        {
            slot.Player.Finished -= slot.FinishedHandler;
            slot.FinishedHandler = null;
        }
    }

    private void EnsurePool()
    {
        if (!isReady || bgmSlot is not null)
            return;

        bgmSlot = CreateSlot(-1, true);
        int poolSize = ClampPoolSize(VoicePoolSize);
        for (int i = 0; i < poolSize; i++)
            soundPool.Add(CreateSlot(i, false));
        ApplyBgmVolume();
        ApplySoundVolumes();
    }

    private AudioVoiceSlot CreateSlot(int channel, bool isBgm)
    {
        var player = new AudioStreamPlayer
        {
            Name = isBgm ? "PrototypeBgmPlayer" : "PrototypeSoundVoice" + channel,
            Bus = ResolveBusName(isBgm ? BgmBus : SoundBus),
            VolumeDb = 0.0f,
            PitchScale = 1.0f,
            StreamPaused = false
        };
        AddChild(player);

        var slot = new AudioVoiceSlot(channel, isBgm, player);
        slot.FinishedHandler = () => OnPlayerFinished(slot);
        player.Finished += slot.FinishedHandler;
        return slot;
    }

    private void ApplyBgmVolume()
    {
        if (bgmSlot is null || bgmSlot.Player is null)
            return;
        ApplySlotVolume(bgmSlot, true);
    }

    private void ApplySoundVolumes()
    {
        for (int i = 0; i < soundPool.Count; i++)
            ApplySlotVolume(soundPool[i], false);
    }

    private void ApplySlotVolume(AudioVoiceSlot slot, bool isBgm)
    {
        if (slot is null || slot.Player is null)
            return;

        float scriptGain = isBgm ? bgmScriptGain : soundScriptGain;
        float userGain = isBgm ? bgmUserGain : soundUserGain;
        slot.Player.VolumeDb = LinearToDb(scriptGain * userGain * slot.CommandVolumeLinear);
    }

    private bool RejectCommand(PrototypeAudioEffectCommand command, string reason)
    {
        if (isReady && !isExiting)
        {
            EmitSignal(
                SignalName.CommandRejected,
                command.RequestId,
                command.Generation,
                command.Effect.ToString(),
                reason);
        }
        return false;
    }

    private void EmitPlaybackState(AudioVoiceSlot slot, string role, string state)
    {
        if (slot is null)
            return;
        EmitPlaybackState(
            slot,
            role,
            state,
            slot.Generation,
            slot.TrackGeneration,
            slot.RequestId,
            slot.Channel);
    }

    private void EmitPlaybackState(
        AudioVoiceSlot slot,
        string role,
        string state,
        long generation,
        long trackGeneration,
        long requestId,
        int channel)
    {
        if (!isReady || isExiting)
            return;
        EmitSignal(
            SignalName.PlaybackStateChanged,
            generation,
            trackGeneration,
            role,
            channel,
            requestId,
            state);
    }

    private void SetBridgeState(PrototypeAudioBridgeState state)
    {
        State = state;
        if (isReady && !isExiting)
        {
            EmitSignal(
                SignalName.BridgeStateChanged,
                State.ToString(),
                currentGeneration,
                generationAttached,
                applicationPaused);
        }
    }

    private void EmitPoolState()
    {
        if (!isReady || isExiting)
            return;
        EmitSignal(
            SignalName.PoolStateChanged,
            currentGeneration,
            ActiveVoiceCount,
            PoolCapacity,
            pendingCommands.Count);
    }

    private int CountActiveSoundVoices()
    {
        int count = 0;
        for (int i = 0; i < soundPool.Count; i++)
        {
            if (soundPool[i].Active)
                count++;
        }
        return count;
    }

    private int EffectiveMaxConcurrentVoices()
    {
        return ClampInt(MaxConcurrentVoices, 1, Math.Max(1, PoolCapacity));
    }

    private int EffectiveMaxPendingCommands()
    {
        return ClampInt(MaxPendingCommands, 1, HardMaximumPendingCommands);
    }

    private int EffectiveMaxCommandsPerProcess()
    {
        return ClampInt(MaxCommandsPerProcess, 1, HardMaximumPendingCommands);
    }

    private static int ClampPoolSize(int value)
    {
        return ClampInt(value, 1, HardMaximumPoolSize);
    }

    private static int ClampInt(int value, int minimum, int maximum)
    {
        return Math.Min(maximum, Math.Max(minimum, value));
    }

    private long NormalizeRequestId(long requestId)
    {
        return requestId > 0 ? requestId : AllocateRequestId();
    }

    private static float ClampVolume(float linearGain)
    {
        if (!float.IsFinite(linearGain))
            return 0.0f;
        return MathF.Min(1.0f, MathF.Max(0.0f, linearGain));
    }

    private static float LinearToDb(float linearGain)
    {
        return linearGain <= 0.0001f ? -80.0f : Mathf.LinearToDb(linearGain);
    }

    private static string NormalizeBusName(string bus)
    {
        return string.IsNullOrWhiteSpace(bus) ? "Master" : bus.Trim();
    }

    private static string ResolveBusName(string bus)
    {
        string candidate = NormalizeBusName(bus);
        return AudioServer.GetBusIndex(candidate) >= 0 ? candidate : "Master";
    }

    private sealed class AudioVoiceSlot
    {
        public AudioVoiceSlot(int channel, bool isBgm, AudioStreamPlayer player)
        {
            Channel = channel;
            IsBgm = isBgm;
            Player = player;
        }

        public readonly int Channel;
        public readonly bool IsBgm;
        public readonly AudioStreamPlayer Player;
        public Action FinishedHandler;
        public bool Active;
        public bool ApplicationPausedBeforePause;
        public long Generation;
        public long TrackGeneration;
        public long RequestId;
        public int RepeatCount;
        public float CommandVolumeLinear = 1.0f;
    }
}
