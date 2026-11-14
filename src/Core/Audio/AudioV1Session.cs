using System;
using System.Collections.Generic;

namespace GEmuera.Core.Audio;

/// <summary>固定四条总线（值同参考 spec §8.3 BUS_*）。</summary>
public enum AudioV1Bus
{
    Master = 0,
    Bgm = 1,
    Se = 2,
    Voice = 3,
}

/// <summary>v1 实例状态（值同参考 spec §8.3 STATE_*；0＝保留的 unknown）。</summary>
public enum AudioV1State
{
    Unknown = 0,
    Pending = 1,
    Playing = 2,
    Paused = 3,
    Deferred = 4,
    Ended = 5,
    Failed = 6,
    Stopped = 7,
    Stolen = 8,
}

/// <summary>v1 ctl op（值同参考 §8.3 OP_*）。</summary>
public enum AudioV1Op
{
    Stop = 0,
    Pause = 1,
    Resume = 2,
    Seek = 3,
    Volume = 4,
    Fade = 5,
    Rate = 6,
    Priority = 7,
    Loop = 8,
}

/// <summary>v1 bus op（值同参考 §8.3 BUSOP_*）。</summary>
public enum AudioV1BusOp
{
    Volume = 0,
    Mute = 1,
    StopAll = 2,
}

/// <summary>v1 实例查询字段（§8.3 QF_*）。</summary>
public enum AudioV1QueryField
{
    State = 0,
    Pos = 1,
    Dur = 2,
    Rate = 3,
    Volume = 4,
    EffectiveVolume = 5,
    Priority = 6,
    StolenBy = 7,
    PitchPreserved = 8,
    Latency = 9,
    Bus = 10,
    Loop = 11,
}

/// <summary>v1 总线查询字段（§8.3 BQF_*）。</summary>
public enum AudioV1BusQueryField
{
    Volume = 0,
    EffectiveVolume = 1,
    Mute = 2,
    DuckDb = 3,
    Voices = 4,
}

/// <summary>ERB → 账本的参数种类；Missing 表示该实参被省略。</summary>
public enum AudioV1ArgumentKind
{
    Missing = 0,
    Integer = 1,
    Float = 2,
    String = 3,
}

/// <summary>一个已经求值的 ERB 实参；形状校验只看 Kind，取值看对应属性。</summary>
public readonly struct AudioV1Argument
{
    private AudioV1Argument(AudioV1ArgumentKind kind, long intValue, double floatValue, string? stringValue)
    {
        Kind = kind;
        IntValue = intValue;
        FloatValue = floatValue;
        StringValue = stringValue;
    }

    public AudioV1ArgumentKind Kind { get; }
    public long IntValue { get; }
    public double FloatValue { get; }
    public string? StringValue { get; }

    public bool IsNumeric => Kind == AudioV1ArgumentKind.Integer || Kind == AudioV1ArgumentKind.Float;

    public static AudioV1Argument Missing => default;

    public static AudioV1Argument FromInt(long value)
        => new AudioV1Argument(AudioV1ArgumentKind.Integer, value, value, null);

    public static AudioV1Argument FromFloat(double value)
        => new AudioV1Argument(AudioV1ArgumentKind.Float, 0, value, null);

    public static AudioV1Argument FromString(string? value)
        => new AudioV1Argument(AudioV1ArgumentKind.String, 0, 0, value);
}

/// <summary>形状校验失败类别；Creator.Method 负责把它翻译成 ERB 错误文案。</summary>
public enum AudioV1ArgumentError
{
    None = 0,
    Count = 1,
    Required = 2,
    Type = 3,
}

/// <summary>AUDIO_PLAY 的原子入场参数（参考 AudioV1PlayRequest）。</summary>
public sealed class AudioV1PlayRequest
{
    public string Track = string.Empty;
    public string Bus = string.Empty;
    public int Volume = 100;
    public bool Loop;
    public int FadeInMs;
    public int Priority = 128;
    public int MaxInstances;
    public bool StealNone;
    public bool IfChanged;
    public int CrossfadeFrom;
    public int CrossfadeMs = -1;
    public bool PreservePitch = true;
    public double Rate = 1.0;
}

public sealed class AudioV1ControlRequest
{
    public int Handle;
    public int Op;
    public double Value;
    public int DurationMs;
    public bool HasPreservePitch;
    public bool PreservePitch;
    public string Curve = string.Empty;
}

public sealed class AudioV1BusRequest
{
    public string Bus = string.Empty;
    public int Op;
    public int Value;
    public int DurationMs;
}

public sealed class AudioV1DuckRequest
{
    public string SourceBus = string.Empty;
    public string TargetBus = string.Empty;
    public int DuckDb;
    public int AttackMs = 80;
    public int ReleaseMs = 300;
}

public sealed class AudioV1QueryRequest
{
    public int Handle;
    public int Field;
}

public sealed class AudioV1BusQueryRequest
{
    public string Bus = string.Empty;
    public int Field;
}

/// <summary>
/// 六个 AUDIO_* 内建的形状校验与默认值应用（参考 Creator.Method.cs 的
/// argumentTypeArrayEx/OmitStart 语义，但本仓库按任务要求用 <c>argumentTypeArray = null</c>
/// + 手写 CheckArgumentType，因此把形状矩阵收敛到纯 Core 以便 xUnit 直测）。
/// </summary>
public static class AudioV1CallParser
{
    private const int PlayMinArgs = 5;
    private const int PlayMaxArgs = 12;

    public static bool TryParsePlay(IReadOnlyList<AudioV1Argument> args,
        out AudioV1PlayRequest request, out AudioV1ArgumentError error, out int errorIndex)
    {
        request = new AudioV1PlayRequest();
        if (!CheckCount(args, PlayMinArgs, PlayMaxArgs, out error, out errorIndex))
            return false;
        if (!RequireString(args, 0, out error, out errorIndex)) return false;
        if (!RequireString(args, 1, out error, out errorIndex)) return false;
        if (!RequireInt(args, 2, out error, out errorIndex)) return false;
        if (!RequireInt(args, 3, out error, out errorIndex)) return false;
        if (!RequireInt(args, 4, out error, out errorIndex)) return false;

        if (!OptionalInt(args, 5, 128, out int priority, out error, out errorIndex)) return false;
        if (!OptionalInt(args, 6, 0, out int maxInstances, out error, out errorIndex)) return false;
        if (!OptionalInt(args, 7, 0, out int flags, out error, out errorIndex)) return false;
        if (!OptionalInt(args, 8, 0, out int crossfadeFrom, out error, out errorIndex)) return false;
        if (!OptionalInt(args, 9, -1, out int crossfadeMs, out error, out errorIndex)) return false;
        if (!OptionalInt(args, 10, 1, out int preservePitch, out error, out errorIndex)) return false;
        if (!OptionalNumber(args, 11, 1.0, out double rate, out error, out errorIndex)) return false;

        request.Track = args[0].StringValue ?? string.Empty;
        request.Bus = args[1].StringValue ?? string.Empty;
        request.Volume = (int)args[2].IntValue;
        request.Loop = args[3].IntValue != 0;
        request.FadeInMs = (int)args[4].IntValue;
        request.Priority = priority;
        request.MaxInstances = maxInstances;
        request.StealNone = (flags & 1) != 0;
        request.IfChanged = (flags & 2) != 0;
        request.CrossfadeFrom = crossfadeFrom;
        request.CrossfadeMs = crossfadeMs;
        request.PreservePitch = preservePitch != 0;
        request.Rate = rate;
        return true;
    }

    public static AudioV1PlayRequest ParsePlay(IReadOnlyList<AudioV1Argument> args)
    {
        if (!TryParsePlay(args, out var request, out _, out _))
            throw new ArgumentException("AUDIO_PLAY argument shape is invalid.", nameof(args));
        return request;
    }

    public static bool TryParseControl(IReadOnlyList<AudioV1Argument> args,
        out AudioV1ControlRequest request, out AudioV1ArgumentError error, out int errorIndex)
    {
        request = new AudioV1ControlRequest();
        if (!CheckCount(args, 2, 6, out error, out errorIndex))
            return false;
        if (!RequireInt(args, 0, out error, out errorIndex)) return false;
        if (!RequireInt(args, 1, out error, out errorIndex)) return false;
        if (!OptionalNumber(args, 2, 0.0, out double value, out error, out errorIndex)) return false;
        if (!OptionalInt(args, 3, 0, out int durationMs, out error, out errorIndex)) return false;
        if (!OptionalInt(args, 4, 0, out int pitch, out error, out errorIndex)) return false;
        if (!OptionalString(args, 5, out string? curve, out error, out errorIndex)) return false;

        request.Handle = (int)args[0].IntValue;
        request.Op = (int)args[1].IntValue;
        request.Value = value;
        request.DurationMs = durationMs;
        request.HasPreservePitch = args.Count > 4 && args[4].Kind != AudioV1ArgumentKind.Missing;
        request.PreservePitch = request.HasPreservePitch && pitch != 0;
        request.Curve = curve ?? string.Empty;
        return true;
    }

    public static AudioV1ControlRequest ParseControl(IReadOnlyList<AudioV1Argument> args)
    {
        if (!TryParseControl(args, out var request, out _, out _))
            throw new ArgumentException("AUDIO_CTL argument shape is invalid.", nameof(args));
        return request;
    }

    public static bool TryParseBus(IReadOnlyList<AudioV1Argument> args,
        out AudioV1BusRequest request, out AudioV1ArgumentError error, out int errorIndex)
    {
        request = new AudioV1BusRequest();
        if (!CheckCount(args, 2, 4, out error, out errorIndex))
            return false;
        if (!RequireString(args, 0, out error, out errorIndex)) return false;
        if (!RequireInt(args, 1, out error, out errorIndex)) return false;
        if (!OptionalInt(args, 2, 0, out int value, out error, out errorIndex)) return false;
        if (!OptionalInt(args, 3, 0, out int durationMs, out error, out errorIndex)) return false;

        request.Bus = args[0].StringValue ?? string.Empty;
        request.Op = (int)args[1].IntValue;
        request.Value = value;
        request.DurationMs = durationMs;
        return true;
    }

    public static AudioV1BusRequest ParseBus(IReadOnlyList<AudioV1Argument> args)
    {
        if (!TryParseBus(args, out var request, out _, out _))
            throw new ArgumentException("AUDIO_BUS argument shape is invalid.", nameof(args));
        return request;
    }

    public static bool TryParseDuck(IReadOnlyList<AudioV1Argument> args,
        out AudioV1DuckRequest request, out AudioV1ArgumentError error, out int errorIndex)
    {
        request = new AudioV1DuckRequest();
        if (!CheckCount(args, 3, 5, out error, out errorIndex))
            return false;
        if (!RequireString(args, 0, out error, out errorIndex)) return false;
        if (!RequireString(args, 1, out error, out errorIndex)) return false;
        if (!RequireInt(args, 2, out error, out errorIndex)) return false;
        if (!OptionalInt(args, 3, 80, out int attackMs, out error, out errorIndex)) return false;
        if (!OptionalInt(args, 4, 300, out int releaseMs, out error, out errorIndex)) return false;

        request.SourceBus = args[0].StringValue ?? string.Empty;
        request.TargetBus = args[1].StringValue ?? string.Empty;
        request.DuckDb = (int)args[2].IntValue;
        request.AttackMs = attackMs;
        request.ReleaseMs = releaseMs;
        return true;
    }

    public static AudioV1DuckRequest ParseDuck(IReadOnlyList<AudioV1Argument> args)
    {
        if (!TryParseDuck(args, out var request, out _, out _))
            throw new ArgumentException("AUDIO_DUCK argument shape is invalid.", nameof(args));
        return request;
    }

    public static bool TryParseQuery(IReadOnlyList<AudioV1Argument> args,
        out AudioV1QueryRequest request, out AudioV1ArgumentError error, out int errorIndex)
    {
        request = new AudioV1QueryRequest();
        if (!CheckCount(args, 2, 2, out error, out errorIndex))
            return false;
        if (!RequireInt(args, 0, out error, out errorIndex)) return false;
        if (!RequireInt(args, 1, out error, out errorIndex)) return false;

        request.Handle = (int)args[0].IntValue;
        request.Field = (int)args[1].IntValue;
        return true;
    }

    public static AudioV1QueryRequest ParseQuery(IReadOnlyList<AudioV1Argument> args)
    {
        if (!TryParseQuery(args, out var request, out _, out _))
            throw new ArgumentException("AUDIO_QUERY argument shape is invalid.", nameof(args));
        return request;
    }

    public static bool TryParseBusQuery(IReadOnlyList<AudioV1Argument> args,
        out AudioV1BusQueryRequest request, out AudioV1ArgumentError error, out int errorIndex)
    {
        request = new AudioV1BusQueryRequest();
        if (!CheckCount(args, 2, 2, out error, out errorIndex))
            return false;
        if (!RequireString(args, 0, out error, out errorIndex)) return false;
        if (!RequireInt(args, 1, out error, out errorIndex)) return false;

        request.Bus = args[0].StringValue ?? string.Empty;
        request.Field = (int)args[1].IntValue;
        return true;
    }

    public static AudioV1BusQueryRequest ParseBusQuery(IReadOnlyList<AudioV1Argument> args)
    {
        if (!TryParseBusQuery(args, out var request, out _, out _))
            throw new ArgumentException("AUDIO_BUS_QUERY argument shape is invalid.", nameof(args));
        return request;
    }

    // ---------------- shared shape helpers ----------------

    private static bool CheckCount(IReadOnlyList<AudioV1Argument> args, int min, int max,
        out AudioV1ArgumentError error, out int errorIndex)
    {
        error = AudioV1ArgumentError.None;
        errorIndex = -1;
        if (args.Count >= min && args.Count <= max)
            return true;
        error = AudioV1ArgumentError.Count;
        return false;
    }

    private static bool RequireString(IReadOnlyList<AudioV1Argument> args, int index,
        out AudioV1ArgumentError error, out int errorIndex)
    {
        error = AudioV1ArgumentError.None;
        errorIndex = -1;
        if (index >= args.Count || args[index].Kind == AudioV1ArgumentKind.Missing)
        {
            error = AudioV1ArgumentError.Required;
            errorIndex = index;
            return false;
        }
        if (args[index].Kind != AudioV1ArgumentKind.String)
        {
            error = AudioV1ArgumentError.Type;
            errorIndex = index;
            return false;
        }
        return true;
    }

    private static bool RequireInt(IReadOnlyList<AudioV1Argument> args, int index,
        out AudioV1ArgumentError error, out int errorIndex)
    {
        error = AudioV1ArgumentError.None;
        errorIndex = -1;
        if (index >= args.Count || args[index].Kind == AudioV1ArgumentKind.Missing)
        {
            error = AudioV1ArgumentError.Required;
            errorIndex = index;
            return false;
        }
        if (args[index].Kind != AudioV1ArgumentKind.Integer)
        {
            error = AudioV1ArgumentError.Type;
            errorIndex = index;
            return false;
        }
        return true;
    }

    private static bool OptionalInt(IReadOnlyList<AudioV1Argument> args, int index, int fallback,
        out int value, out AudioV1ArgumentError error, out int errorIndex)
    {
        error = AudioV1ArgumentError.None;
        errorIndex = -1;
        value = fallback;
        if (index >= args.Count || args[index].Kind == AudioV1ArgumentKind.Missing)
            return true;
        if (args[index].Kind != AudioV1ArgumentKind.Integer)
        {
            error = AudioV1ArgumentError.Type;
            errorIndex = index;
            return false;
        }
        value = (int)args[index].IntValue;
        return true;
    }

    private static bool OptionalNumber(IReadOnlyList<AudioV1Argument> args, int index, double fallback,
        out double value, out AudioV1ArgumentError error, out int errorIndex)
    {
        error = AudioV1ArgumentError.None;
        errorIndex = -1;
        value = fallback;
        if (index >= args.Count || args[index].Kind == AudioV1ArgumentKind.Missing)
            return true;
        if (!args[index].IsNumeric)
        {
            error = AudioV1ArgumentError.Type;
            errorIndex = index;
            return false;
        }
        value = args[index].Kind == AudioV1ArgumentKind.Float
            ? args[index].FloatValue
            : args[index].IntValue;
        return true;
    }

    private static bool OptionalString(IReadOnlyList<AudioV1Argument> args, int index,
        out string? value, out AudioV1ArgumentError error, out int errorIndex)
    {
        error = AudioV1ArgumentError.None;
        errorIndex = -1;
        value = null;
        if (index >= args.Count || args[index].Kind == AudioV1ArgumentKind.Missing)
            return true;
        if (args[index].Kind != AudioV1ArgumentKind.String)
        {
            error = AudioV1ArgumentError.Type;
            errorIndex = index;
            return false;
        }
        value = args[index].StringValue;
        return true;
    }
}

/// <summary>
/// AUDIO_*（EWP audio v1）的纯逻辑账本：句柄表、四总线表、duck 表、状态机与返回码。
/// 不引用 Godot；真实音频由 <see cref="IAudioV1Backend"/> 驱动。
///
/// 与参考实现（AudioV1Ledger）保持一致的语义：单调递增句柄、终态保留、8 状态机、
/// CTL/BUS/DUCK 返回码、查询字段值。差异点统一以 DIVERGENCE 注释标出。
/// </summary>
public sealed class AudioV1Session
{
    private const int MaxHandle = int.MaxValue;

    private sealed class Instance
    {
        public int Handle;
        public AudioV1Bus Bus;
        public string Track = string.Empty;
        public AudioV1State State;
        public bool Loop;
        public int Volume = 100;
        public double Rate = 1.0;
        public int Priority = 128;
        public int MaxInstances;
        public bool PreservePitch = true;
        public long PosMs;
        public long DurMs;
        public int StolenBy;
    }

    private sealed class BusState
    {
        public int Volume = 100;
        public bool Mute;
    }

    private sealed class DuckRule
    {
        public AudioV1Bus Source;
        public AudioV1Bus Target;
        public int DuckDb;
        public int AttackMs = 80;
        public int ReleaseMs = 300;
        public bool Enabled;
    }

    private readonly object gate = new object();
    private readonly IAudioV1Backend? backend;
    private readonly Dictionary<int, Instance> instances = new Dictionary<int, Instance>();
    private readonly BusState[] buses = CreateBuses();
    private readonly Dictionary<int, DuckRule> ducks = new Dictionary<int, DuckRule>();
    private int nextHandle = 1;

    public AudioV1Session(IAudioV1Backend? backend)
    {
        this.backend = backend;
    }

    /// <summary>最近一次真正进入 Failed 的句柄（PLAY 返回 0 时用于诊断/测试）。0＝无。</summary>
    public int LastFailedHandle { get; private set; }

    private static BusState[] CreateBuses()
    {
        var result = new BusState[4];
        for (int i = 0; i < result.Length; i++)
            result[i] = new BusState();
        return result;
    }

    private bool BackendAvailable => backend != null && backend.IsAvailable;

    // ---------------- 查询辅助 ----------------

    public AudioV1State GetState(int handle)
    {
        lock (gate)
            return instances.TryGetValue(handle, out var instance) ? instance.State : AudioV1State.Unknown;
    }

    private static bool IsLive(AudioV1State state)
        => state == AudioV1State.Pending || state == AudioV1State.Playing
            || state == AudioV1State.Paused || state == AudioV1State.Deferred;

    private static bool TryParseBus(string? name, out AudioV1Bus bus)
    {
        switch ((name ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "master": bus = AudioV1Bus.Master; return true;
            case "bgm": bus = AudioV1Bus.Bgm; return true;
            case "se": bus = AudioV1Bus.Se; return true;
            case "voice": bus = AudioV1Bus.Voice; return true;
            default: bus = AudioV1Bus.Master; return false;
        }
    }

    private static int Clamp(int value, int min, int max)
        => value < min ? min : value > max ? max : value;

    private static int ClampVolume(int value) => Clamp(value, 0, 100);

    private static double ClampRate(double value)
        => double.IsNaN(value) ? 1.0 : value < 0.1 ? 0.1 : value > 10.0 ? 10.0 : value;

    private static int ClampDuckDb(int value) => Clamp(value, -80, 0);

    private static int ClampDuration(int value) => Clamp(value, 0, 60000);

    private static long RoundAwayFromZero(double value)
        => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    private double MasterGain()
        => buses[(int)AudioV1Bus.Master].Mute ? 0.0 : buses[(int)AudioV1Bus.Master].Volume / 100.0;

    private double BusGain(AudioV1Bus bus)
    {
        if (bus == AudioV1Bus.Master)
            return buses[(int)AudioV1Bus.Master].Mute ? 0.0 : buses[(int)AudioV1Bus.Master].Volume / 100.0;
        var state = buses[(int)bus];
        return state.Mute ? 0.0 : state.Volume / 100.0;
    }

    // DIVERGENCE: 参考实现由 FE 根据 source bus 是否有活跃声部实时混音；
    // 本引擎没有独立音频总线，duck 规则在设置时静态作用于目标总线（立即生效），
    // attack/release 只入账本不参与包络。
    private int DuckDbFor(AudioV1Bus target)
    {
        int best = 0;
        foreach (var rule in ducks.Values)
        {
            if (!rule.Enabled || rule.Target != target)
                continue;
            if (rule.DuckDb < best)
                best = rule.DuckDb;
        }
        return best;
    }

    private double DuckGain(AudioV1Bus target)
        => Math.Pow(10.0, DuckDbFor(target) / 20.0);

    private long EffectiveVolume(Instance instance)
    {
        double gain = MasterGain() * BusGain(instance.Bus) * DuckGain(instance.Bus);
        return RoundAwayFromZero(instance.Volume * gain);
    }

    private long EffectiveBusVolume(AudioV1Bus bus)
    {
        if (bus == AudioV1Bus.Master)
            return RoundAwayFromZero(MasterGain() * 100.0);
        // BusGain 已包含 bus mute；再串 master 与 duck 规则。
        return RoundAwayFromZero(BusGain(bus) * MasterGain() * DuckGain(bus) * 100.0);
    }

    private int VoicesOnBus(AudioV1Bus bus)
    {
        int count = 0;
        foreach (var instance in instances.Values)
        {
            if (!IsLive(instance.State))
                continue;
            if (bus != AudioV1Bus.Master && instance.Bus != bus)
                continue;
            count++;
        }
        return count;
    }

    // 与参考实现 AudioV1Ledger.DuckTenthsNoLock 对齐：返回正的十分之一 dB 衰减量
    // （AUDIO_DUCK(...,-12) → 120），0 表示无规则。
    private int DuckDbTenths(AudioV1Bus bus)
    {
        int db = DuckDbFor(bus);
        return db == 0 ? 0 : -db * 10;
    }

    private Instance? FindLive(int handle)
    {
        if (handle <= 0 || !instances.TryGetValue(handle, out var instance) || !IsLive(instance.State))
            return null;
        return instance;
    }

    private int AllocHandle()
    {
        if (nextHandle <= 0)
            return 0;
        int handle = nextHandle;
        // 参考实现：int.MaxValue 可作为最后一个句柄分配一次，随后句柄空间耗尽。
        nextHandle = nextHandle == MaxHandle ? 0 : nextHandle + 1;
        return handle;
    }

    // ---------------- AUDIO_PLAY ----------------

    public long Play(AudioV1PlayRequest request)
    {
        if (request == null)
            return 0;
        lock (gate)
        {
            if (!TryParseBus(request.Bus, out var bus) || bus == AudioV1Bus.Master)
                return 0;
            if (!BackendAvailable)
                return 0;

            // if_changed：同 bus 同 track 且仍 Playing → 返回现句柄、不重播。
            if (request.IfChanged)
            {
                var existing = FindOldestPlaying(bus, request.Track);
                if (existing != null)
                    return existing.Handle;
            }

            int volume = ClampVolume(request.Volume);
            int priority = Clamp(request.Priority, 0, 255);
            int maxInstances = Clamp(request.MaxInstances, 0, 255);
            double rate = ClampRate(request.Rate);

            // crossfade：桥不支持真正的交叉淡变；按 DIVERGENCE 先停旧句柄再播新，
            // crossfadeMs 只入参不入账（忽略）。
            if (request.CrossfadeFrom != 0)
            {
                Instance? cross = request.CrossfadeFrom == -1
                    ? FindOldestLive(bus, null)
                    : FindLive(request.CrossfadeFrom);
                if (cross != null)
                    StopInstance(cross);
            }

            // DIVERGENCE: 桥只有单条 BGM 通道，播放新 BGM 会停掉旧 BGM；
            // 参考实现允许最多 4 条 bgm 实例。
            if (bus == AudioV1Bus.Bgm)
                StopOtherLiveOnBus(AudioV1Bus.Bgm);

            Instance? victim = null;
            if (maxInstances > 0)
            {
                var candidates = LiveOnBusAndTrack(bus, request.Track);
                if (candidates.Count >= maxInstances)
                {
                    // steal:none 时拒绝新播放，保留旧实例。
                    if (request.StealNone)
                        return 0;
                    // DIVERGENCE: 优先级只入账本；抢占按最旧句柄，不按 priority 选受害者。
                    victim = candidates[0];
                }
            }

            int handle = AllocHandle();
            if (handle == 0)
                return 0;

            if (victim != null)
            {
                victim.State = AudioV1State.Stolen;
                victim.StolenBy = handle;
                SafeBackendStop(victim.Handle);
            }

            var instance = new Instance
            {
                Handle = handle,
                Bus = bus,
                Track = request.Track,
                State = AudioV1State.Pending,
                Loop = request.Loop,
                Volume = volume,
                Rate = rate,
                Priority = priority,
                MaxInstances = maxInstances,
                PreservePitch = request.PreservePitch,
            };
            instances[handle] = instance;

            // fadeInMs：桥不支持时间性淡入；立即到目标音量（DIVERGENCE）。
            // 该字段仅保留意图，适配器 Play 拿到的是 request.Volume。
            _ = request.FadeInMs;
            _ = request.CrossfadeMs;

            // 传给后端的意图必须是夹取后的值；否则 adapter 会用未夹取的音量/速率/优先级
            // 直接映射物理通道，而查询侧看到的是夹取后的账本值，两者漂移。
            var backendRequest = new AudioV1PlayRequest
            {
                Track = request.Track,
                Bus = request.Bus,
                Volume = volume,
                Loop = request.Loop,
                FadeInMs = request.FadeInMs,
                Priority = priority,
                MaxInstances = maxInstances,
                StealNone = request.StealNone,
                IfChanged = request.IfChanged,
                CrossfadeFrom = request.CrossfadeFrom,
                CrossfadeMs = request.CrossfadeMs,
                PreservePitch = request.PreservePitch,
                Rate = rate,
            };

            bool ok;
            try
            {
                ok = backend!.Play(backendRequest, handle);
            }
            catch
            {
                ok = false;
            }
            if (!ok)
            {
                instance.State = AudioV1State.Failed;
                LastFailedHandle = handle;
                // 后端可能在失败前已建立部分通道映射；这里做一次幂等清理。
                SafeBackendStop(handle);
                return 0;
            }

            instance.State = AudioV1State.Playing;
            return handle;
        }
    }

    private Instance? FindOldestPlaying(AudioV1Bus bus, string track)
    {
        Instance? best = null;
        foreach (var instance in instances.Values)
        {
            if (instance.State != AudioV1State.Playing || instance.Bus != bus)
                continue;
            if (!string.Equals(instance.Track, track, StringComparison.Ordinal))
                continue;
            if (best == null || instance.Handle < best.Handle)
                best = instance;
        }
        return best;
    }

    private Instance? FindOldestLive(AudioV1Bus bus, string? track)
    {
        Instance? best = null;
        foreach (var instance in instances.Values)
        {
            if (!IsLive(instance.State) || instance.Bus != bus)
                continue;
            if (track != null && !string.Equals(instance.Track, track, StringComparison.Ordinal))
                continue;
            if (best == null || instance.Handle < best.Handle)
                best = instance;
        }
        return best;
    }

    private List<Instance> LiveOnBusAndTrack(AudioV1Bus bus, string track)
    {
        var result = new List<Instance>();
        foreach (var instance in instances.Values)
        {
            if (!IsLive(instance.State) || instance.Bus != bus)
                continue;
            if (!string.Equals(instance.Track, track, StringComparison.Ordinal))
                continue;
            result.Add(instance);
        }
        result.Sort((left, right) => left.Handle.CompareTo(right.Handle));
        return result;
    }

    private void StopOtherLiveOnBus(AudioV1Bus bus)
    {
        foreach (var instance in instances.Values)
        {
            if (!IsLive(instance.State) || instance.Bus != bus)
                continue;
            StopInstance(instance);
        }
    }

    private void StopInstance(Instance instance)
    {
        instance.State = AudioV1State.Stopped;
        SafeBackendStop(instance.Handle);
    }

    private void SafeBackendStop(int handle)
    {
        try { backend!.Stop(handle); }
        catch { /* 账本状态已确定，宿主停止失败不回滚状态机。 */ }
    }

    // ---------------- AUDIO_CTL ----------------

    public long Control(AudioV1ControlRequest request)
    {
        if (request == null)
            return -1;
        lock (gate)
        {
            Instance? instance = FindLive(request.Handle);
            if (instance == null)
                return -1;
            if (!BackendAvailable)
                return -2;

            int durationMs = ClampDuration(request.DurationMs);
            switch ((AudioV1Op)request.Op)
            {
                case AudioV1Op.Stop:
                    StopInstance(instance);
                    _ = durationMs; // DIVERGENCE: no timed fade-out
                    return 1;
                case AudioV1Op.Pause:
                    if (instance.State != AudioV1State.Playing)
                        return 0;
                    instance.State = AudioV1State.Paused;
                    SafeBackend(() => backend!.Pause(instance.Handle));
                    return 1;
                case AudioV1Op.Resume:
                    if (instance.State != AudioV1State.Paused)
                        return 0;
                    instance.State = AudioV1State.Playing;
                    SafeBackend(() => backend!.Resume(instance.Handle));
                    return 1;
                case AudioV1Op.Seek:
                    // DIVERGENCE: 桥不支持 seek，恒返回 -2（参考码表 unsupported）。
                    return -2;
                case AudioV1Op.Volume:
                {
                    int volume = ClampVolume((int)request.Value);
                    instance.Volume = volume;
                    SafeBackend(() => backend!.SetVolume(instance.Handle, volume));
                    return 1;
                }
                case AudioV1Op.Fade:
                {
                    // DIVERGENCE: no timed fade；按目标值立即生效。
                    int volume = ClampVolume((int)request.Value);
                    instance.Volume = volume;
                    SafeBackend(() => backend!.SetVolume(instance.Handle, volume));
                    return 1;
                }
                case AudioV1Op.Rate:
                {
                    // 参考实现：CTL Rate 的 value 是百分数（100＝原速）。
                    double rate = ClampRate(request.Value / 100.0);
                    instance.Rate = rate;
                    if (request.HasPreservePitch)
                        instance.PreservePitch = request.PreservePitch;
                    bool preservePitch = instance.PreservePitch;
                    SafeBackend(() => backend!.SetRate(instance.Handle, rate, preservePitch));
                    return 1;
                }
                case AudioV1Op.Priority:
                    // DIVERGENCE: 优先级只入账本，不驱动真实抢占。
                    instance.Priority = Clamp((int)request.Value, 0, 255);
                    return 1;
                case AudioV1Op.Loop:
                    instance.Loop = request.Value != 0;
                    SafeBackend(() => backend!.SetLoop(instance.Handle, instance.Loop));
                    return 1;
                default:
                    return -2;
            }
        }
    }

    // ---------------- AUDIO_BUS ----------------

    public long Bus(AudioV1BusRequest request)
    {
        if (request == null)
            return -1;
        lock (gate)
        {
            if (!TryParseBus(request.Bus, out var bus))
                return -1;
            if (!BackendAvailable)
                return -2;

            _ = ClampDuration(request.DurationMs); // DIVERGENCE: 总线级淡变不支持，立即生效。
            switch ((AudioV1BusOp)request.Op)
            {
                case AudioV1BusOp.Volume:
                {
                    int volume = ClampVolume(request.Value);
                    var state = buses[(int)bus];
                    if (state.Volume == volume)
                        return 0;
                    state.Volume = volume;
                    SafeBackend(() => backend!.SetBusVolume(bus, volume));
                    return 1;
                }
                case AudioV1BusOp.Mute:
                {
                    bool mute = request.Value != 0;
                    var state = buses[(int)bus];
                    if (state.Mute == mute)
                        return 0;
                    state.Mute = mute;
                    SafeBackend(() => backend!.SetBusMute(bus, mute));
                    return 1;
                }
                case AudioV1BusOp.StopAll:
                {
                    // 参考实现：value!=0 时连 Paused 一起停；默认只停播放中的实例。
                    bool includePaused = request.Value != 0;
                    bool any = false;
                    foreach (var instance in instances.Values)
                    {
                        if (!IsLive(instance.State))
                            continue;
                        if (bus != AudioV1Bus.Master && instance.Bus != bus)
                            continue;
                        if (instance.State == AudioV1State.Paused && !includePaused)
                            continue;
                        instance.State = AudioV1State.Stopped;
                        any = true;
                    }
                    if (!any)
                        return 0;
                    SafeBackend(() => backend!.StopAll(bus));
                    return 1;
                }
                default:
                    return -2;
            }
        }
    }

    // ---------------- AUDIO_DUCK ----------------

    public long Duck(AudioV1DuckRequest request)
    {
        if (request == null)
            return -1;
        lock (gate)
        {
            if (!TryParseBus(request.SourceBus, out var source)
                || !TryParseBus(request.TargetBus, out var target)
                || source == AudioV1Bus.Master || target == AudioV1Bus.Master
                || source == target)
                return -1;
            if (!BackendAvailable)
                return -2;

            int db = ClampDuckDb(request.DuckDb);
            int attack = Clamp(request.AttackMs, 0, 60000);
            int release = Clamp(request.ReleaseMs, 0, 60000);
            bool enabled = db != 0;
            int key = DuckKey(source, target);

            if (ducks.TryGetValue(key, out var existing))
            {
                if (existing.DuckDb == db && existing.AttackMs == attack
                    && existing.ReleaseMs == release && existing.Enabled == enabled)
                    return 0;
            }
            else if (!enabled)
            {
                return 0;
            }

            if (existing == null)
            {
                existing = new DuckRule { Source = source, Target = target };
                ducks[key] = existing;
            }
            existing.DuckDb = db;
            existing.AttackMs = attack;
            // DIVERGENCE: attack/release 仅入账本，桥没有时间性 duck 包络；增益立即生效。
            existing.ReleaseMs = release;
            existing.Enabled = enabled;

            int targetDb = DuckDbFor(target);
            SafeBackend(() => backend!.SetDuck(target, targetDb));
            return 1;
        }
    }

    private static int DuckKey(AudioV1Bus source, AudioV1Bus target)
        => (int)source * 10 + (int)target;

    // ---------------- AUDIO_QUERY ----------------

    public long Query(int handle, int field)
    {
        lock (gate)
        {
            if (!instances.TryGetValue(handle, out var instance))
                return -1;
            switch ((AudioV1QueryField)field)
            {
                case AudioV1QueryField.State: return (long)instance.State;
                case AudioV1QueryField.Pos:
                    return instance.DurMs > 0
                        ? Math.Min(instance.PosMs, instance.DurMs)
                        : instance.PosMs;
                case AudioV1QueryField.Dur: return instance.DurMs;
                case AudioV1QueryField.Rate:
                    return RoundAwayFromZero(instance.Rate * 100.0);
                case AudioV1QueryField.Volume: return instance.Volume;
                case AudioV1QueryField.EffectiveVolume: return EffectiveVolume(instance);
                case AudioV1QueryField.Priority: return instance.Priority;
                case AudioV1QueryField.StolenBy: return instance.StolenBy;
                case AudioV1QueryField.PitchPreserved: return instance.PreservePitch ? 1 : 0;
                case AudioV1QueryField.Latency: return 0; // 进程内桥，无跨进程延迟。
                case AudioV1QueryField.Bus: return (long)instance.Bus;
                case AudioV1QueryField.Loop: return instance.Loop ? 1 : 0;
                default: return -1;
            }
        }
    }

    // ---------------- AUDIO_BUS_QUERY ----------------

    public long BusQuery(string busName, int field)
    {
        lock (gate)
        {
            if (!TryParseBus(busName, out var bus))
                return -1;
            switch ((AudioV1BusQueryField)field)
            {
                case AudioV1BusQueryField.Volume: return buses[(int)bus].Volume;
                case AudioV1BusQueryField.EffectiveVolume: return EffectiveBusVolume(bus);
                case AudioV1BusQueryField.Mute: return buses[(int)bus].Mute ? 1 : 0;
                case AudioV1BusQueryField.DuckDb: return DuckDbTenths(bus);
                case AudioV1BusQueryField.Voices: return VoicesOnBus(bus);
                default: return -1;
            }
        }
    }

    // ---------------- 后端回调 ----------------

    /// <summary>
    /// 桥的位置回调入口（适配器把物理通道映射回账本句柄后调用）。
    /// 未收到回调时 Pos/Dur 保持 0，符合任务给定的"无回调返回 0"。
    /// </summary>
    public void UpdatePosition(int handle, double currentSec, double totalSec)
    {
        lock (gate)
        {
            if (!instances.TryGetValue(handle, out var instance))
                return;
            double current = double.IsNaN(currentSec) || currentSec < 0 ? 0.0 : currentSec;
            instance.PosMs = (long)(current * 1000.0);
            if (totalSec > 0 && !double.IsNaN(totalSec) && !double.IsInfinity(totalSec))
                instance.DurMs = (long)(totalSec * 1000.0);
        }
    }

    /// <summary>桥报告自然播放结束；把仍 live 的实例收束为 Ended。</summary>
    public void NotifyEnded(int handle)
    {
        lock (gate)
        {
            if (!instances.TryGetValue(handle, out var instance) || !IsLive(instance.State))
                return;
            instance.State = AudioV1State.Ended;
        }
    }

    /// <summary>桥报告异步失败；把仍 live 的实例收束为 Failed。</summary>
    public void NotifyFailed(int handle)
    {
        lock (gate)
        {
            if (!instances.TryGetValue(handle, out var instance) || !IsLive(instance.State))
                return;
            instance.State = AudioV1State.Failed;
            LastFailedHandle = handle;
        }
    }

    /// <summary>会话停止时清空账本并整体停声（适配器在 EmueraMain.StopLegacySession 挂接）。</summary>
    public void Reset()
    {
        lock (gate)
        {
            if (BackendAvailable)
            {
                SafeBackend(() => backend!.StopAll(AudioV1Bus.Master));
                SafeBackend(() => backend!.Reset());
            }
            instances.Clear();
            ducks.Clear();
            for (int i = 0; i < buses.Length; i++)
            {
                buses[i].Volume = 100;
                buses[i].Mute = false;
            }
            nextHandle = 1;
            LastFailedHandle = 0;
        }
    }

    private void SafeBackend(Action action)
    {
        try { action(); }
        catch { /* 宿主原语失败不改变账本已确定的语义。 */ }
    }
}
