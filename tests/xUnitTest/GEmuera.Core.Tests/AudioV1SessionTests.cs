using System;
using System.Collections.Generic;
using GEmuera.Core.Audio;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// AUDIO_* (EWP audio v1) 的 Core 账本契约测试。
/// 这里刻意不依赖 Godot：所有播放原语都由内存 FakeBackend 承担，
/// 从而把"形状校验 / 默认值 / 返回码 / 状态机 / 查询"钉在纯逻辑层。
/// </summary>
public sealed class AudioV1SessionTests
{
    // ---------------- helpers ----------------

    static AudioV1Argument I(long value) => AudioV1Argument.FromInt(value);
    static AudioV1Argument F(double value) => AudioV1Argument.FromFloat(value);
    static AudioV1Argument S(string value) => AudioV1Argument.FromString(value);
    static AudioV1Argument M => AudioV1Argument.Missing;

    static bool TryParsePlay(AudioV1Argument[] args,
        out AudioV1PlayRequest request, out AudioV1ArgumentError error, out int index)
        => AudioV1CallParser.TryParsePlay(args, out request, out error, out index);

    static AudioV1PlayRequest ParsePlay(params AudioV1Argument[] args)
        => AudioV1CallParser.ParsePlay(args);

    static AudioV1ControlRequest ParseCtl(params AudioV1Argument[] args)
        => AudioV1CallParser.ParseControl(args);

    static AudioV1BusRequest ParseBus(params AudioV1Argument[] args)
        => AudioV1CallParser.ParseBus(args);

    static AudioV1DuckRequest ParseDuck(params AudioV1Argument[] args)
        => AudioV1CallParser.ParseDuck(args);

    static AudioV1Session Create(out FakeBackend backend)
    {
        backend = new FakeBackend();
        return new AudioV1Session(backend);
    }

    static long Play(AudioV1Session session, string track = "track.ogg", string bus = "se",
        int volume = 100, int loop = 0, int fadeMs = 0, int priority = 128,
        int maxInstances = 0, int flags = 0, int crossfadeFrom = 0,
        int crossfadeMs = -1, int preservePitch = 1, double rate = 1.0)
    {
        AudioV1Argument[] args =
        {
            S(track), S(bus), I(volume), I(loop), I(fadeMs), I(priority),
            I(maxInstances), I(flags), I(crossfadeFrom), I(crossfadeMs),
            I(preservePitch), rate == 1.0 ? I(1) : F(rate),
        };
        return session.Play(ParsePlay(args));
    }

    static long Query(AudioV1Session session, long handle, AudioV1QueryField field)
        => session.Query((int)handle, (int)field);

    static long BusQuery(AudioV1Session session, string bus, AudioV1BusQueryField field)
        => session.BusQuery(bus, (int)field);

    // ---------------- shape validation ----------------

    [Fact]
    public void PlayShape_RejectsTooFewArguments()
    {
        Assert.False(TryParsePlay(new[] { S("a"), S("se"), I(100), I(0) }, out _, out var error, out _));
        Assert.Equal(AudioV1ArgumentError.Count, error);
    }

    [Fact]
    public void PlayShape_RejectsTooManyArguments()
    {
        var args = new AudioV1Argument[13];
        args[0] = S("a");
        args[1] = S("se");
        for (int i = 2; i < args.Length; i++) args[i] = I(0);
        Assert.False(TryParsePlay(args, out _, out var error, out _));
        Assert.Equal(AudioV1ArgumentError.Count, error);
    }

    [Fact]
    public void PlayShape_RejectsWrongRequiredTypes()
    {
        Assert.False(TryParsePlay(new[] { I(1), S("se"), I(100), I(0), I(0) }, out _, out var error, out var index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(0, index);

        Assert.False(TryParsePlay(new[] { S("a"), I(1), I(100), I(0), I(0) }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(1, index);

        Assert.False(TryParsePlay(new[] { S("a"), S("se"), S("100"), I(0), I(0) }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(2, index);
    }

    [Fact]
    public void PlayShape_RejectsMissingRequiredArgument()
    {
        Assert.False(TryParsePlay(new[] { M, S("se"), I(100), I(0), I(0) }, out _, out var error, out var index));
        Assert.Equal(AudioV1ArgumentError.Required, error);
        Assert.Equal(0, index);
    }

    [Fact]
    public void PlayShape_RejectsWrongOptionalTypes()
    {
        AudioV1Argument[] BaseArgs() => new[]
        {
            S("a"), S("se"), I(100), I(0), I(0), I(128), I(0), I(0), I(0), I(-1), I(1), I(1),
        };

        var args = BaseArgs();
        args[5] = S("128");
        Assert.False(TryParsePlay(args, out _, out var error, out var index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(5, index);

        args = BaseArgs();
        args[6] = S("0");
        Assert.False(TryParsePlay(args, out _, out error, out index));
        Assert.Equal(6, index);

        args = BaseArgs();
        args[7] = S("0");
        Assert.False(TryParsePlay(args, out _, out error, out index));
        Assert.Equal(7, index);

        args = BaseArgs();
        args[8] = S("0");
        Assert.False(TryParsePlay(args, out _, out error, out index));
        Assert.Equal(8, index);

        args = BaseArgs();
        args[9] = S("-1");
        Assert.False(TryParsePlay(args, out _, out error, out index));
        Assert.Equal(9, index);

        args = BaseArgs();
        args[10] = S("1");
        Assert.False(TryParsePlay(args, out _, out error, out index));
        Assert.Equal(10, index);
    }

    [Fact]
    public void PlayShape_RejectsNonNumericRate()
    {
        var args = new AudioV1Argument[12];
        args[0] = S("a");
        args[1] = S("se");
        args[2] = I(100);
        args[3] = I(0);
        args[4] = I(0);
        args[5] = M;
        args[6] = M;
        args[7] = M;
        args[8] = M;
        args[9] = M;
        args[10] = M;
        args[11] = S("fast");
        Assert.False(TryParsePlay(args, out _, out var error, out var index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(11, index);
    }

    [Fact]
    public void PlayShape_AllowsMissingOptionalArguments()
    {
        var args = new AudioV1Argument[12];
        args[0] = S("a");
        args[1] = S("se");
        args[2] = I(100);
        args[3] = I(0);
        args[4] = I(0);
        for (int i = 5; i < args.Length; i++) args[i] = M;
        Assert.True(TryParsePlay(args, out var request, out _, out _));
        Assert.Equal(128, request.Priority);
        Assert.Equal(0, request.MaxInstances);
        Assert.False(request.StealNone);
        Assert.False(request.IfChanged);
        Assert.Equal(0, request.CrossfadeFrom);
        Assert.Equal(-1, request.CrossfadeMs);
        Assert.True(request.PreservePitch);
        Assert.Equal(1.0, request.Rate);
    }

    [Fact]
    public void PlayDefaults_AreAppliedInOrder()
    {
        var request = ParsePlay(S("a"), S("se"), I(90), I(1), I(50));
        Assert.Equal("a", request.Track);
        Assert.Equal("se", request.Bus);
        Assert.Equal(90, request.Volume);
        Assert.True(request.Loop);
        Assert.Equal(50, request.FadeInMs);
        Assert.Equal(128, request.Priority);
        Assert.Equal(0, request.MaxInstances);
        Assert.False(request.StealNone);
        Assert.False(request.IfChanged);
        Assert.Equal(0, request.CrossfadeFrom);
        Assert.Equal(-1, request.CrossfadeMs);
        Assert.True(request.PreservePitch);
        Assert.Equal(1.0, request.Rate);
    }

    [Fact]
    public void PlayDefaults_EachOptionalCanBeSet()
    {
        var request = ParsePlay(S("a"), S("se"), I(90), I(1), I(50),
            I(200), I(3), I(3), I(7), I(500), I(0), F(1.5));
        Assert.Equal(200, request.Priority);
        Assert.Equal(3, request.MaxInstances);
        Assert.True(request.StealNone);
        Assert.True(request.IfChanged);
        Assert.Equal(7, request.CrossfadeFrom);
        Assert.Equal(500, request.CrossfadeMs);
        Assert.False(request.PreservePitch);
        Assert.Equal(1.5, request.Rate);
    }

    [Fact]
    public void ControlShape_RejectsWrongCountAndTypes()
    {
        Assert.False(AudioV1CallParser.TryParseControl(new[] { I(1) }, out _, out var error, out _));
        Assert.Equal(AudioV1ArgumentError.Count, error);

        Assert.False(AudioV1CallParser.TryParseControl(new[] { I(1), I(0), I(0), I(0), I(0), S("x"), I(0) }, out _, out error, out _));
        Assert.Equal(AudioV1ArgumentError.Count, error);

        Assert.False(AudioV1CallParser.TryParseControl(new[] { S("1"), I(0) }, out _, out error, out var index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(0, index);

        Assert.False(AudioV1CallParser.TryParseControl(new[] { I(1), I(0), S("loud") }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(2, index);

        Assert.False(AudioV1CallParser.TryParseControl(new[] { I(1), I(0), I(0), S("0") }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(3, index);

        Assert.False(AudioV1CallParser.TryParseControl(new[] { I(1), I(0), I(0), I(0), S("1") }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(4, index);

        Assert.False(AudioV1CallParser.TryParseControl(new[] { I(1), I(0), I(0), I(0), I(0), I(1) }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(5, index);
    }

    [Fact]
    public void ControlDefaults_AreApplied()
    {
        var request = ParseCtl(I(5), I(4));
        Assert.Equal(5, request.Handle);
        Assert.Equal(4, request.Op);
        Assert.Equal(0.0, request.Value);
        Assert.Equal(0, request.DurationMs);
        Assert.False(request.HasPreservePitch);
        Assert.False(request.PreservePitch);
        Assert.Equal(string.Empty, request.Curve);
    }

    [Fact]
    public void ControlDefaults_AllOptionalCanBeSet()
    {
        var request = ParseCtl(I(5), I(6), F(150.5), I(400), I(0), S("linear"));
        Assert.Equal(150.5, request.Value);
        Assert.Equal(400, request.DurationMs);
        Assert.True(request.HasPreservePitch);
        Assert.False(request.PreservePitch);
        Assert.Equal("linear", request.Curve);
    }

    [Fact]
    public void BusShape_AndDefaults()
    {
        Assert.False(AudioV1CallParser.TryParseBus(new[] { S("bgm") }, out _, out var error, out _));
        Assert.Equal(AudioV1ArgumentError.Count, error);

        Assert.False(AudioV1CallParser.TryParseBus(new[] { I(1), I(0) }, out _, out error, out var index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(0, index);

        Assert.False(AudioV1CallParser.TryParseBus(new[] { S("bgm"), S("0") }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(1, index);

        Assert.False(AudioV1CallParser.TryParseBus(new[] { S("bgm"), I(0), S("50") }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(2, index);

        Assert.False(AudioV1CallParser.TryParseBus(new[] { S("bgm"), I(0), I(50), S("0") }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(3, index);

        var request = AudioV1CallParser.ParseBus(new[] { S("bgm"), I(0) });
        Assert.Equal("bgm", request.Bus);
        Assert.Equal(0, request.Op);
        Assert.Equal(0, request.Value);
        Assert.Equal(0, request.DurationMs);
    }

    [Fact]
    public void DuckShape_AndDefaults()
    {
        Assert.False(AudioV1CallParser.TryParseDuck(new[] { S("voice"), S("bgm") }, out _, out var error, out _));
        Assert.Equal(AudioV1ArgumentError.Count, error);

        Assert.False(AudioV1CallParser.TryParseDuck(new[] { I(1), S("bgm"), I(-12) }, out _, out error, out var index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(0, index);

        Assert.False(AudioV1CallParser.TryParseDuck(new[] { S("voice"), S("bgm"), S("-12") }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(2, index);

        Assert.False(AudioV1CallParser.TryParseDuck(new[] { S("voice"), S("bgm"), I(-12), S("80") }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(3, index);

        Assert.False(AudioV1CallParser.TryParseDuck(new[] { S("voice"), S("bgm"), I(-12), I(80), S("300") }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(4, index);

        var request = AudioV1CallParser.ParseDuck(new[] { S("voice"), S("bgm"), I(-12) });
        Assert.Equal("voice", request.SourceBus);
        Assert.Equal("bgm", request.TargetBus);
        Assert.Equal(-12, request.DuckDb);
        Assert.Equal(80, request.AttackMs);
        Assert.Equal(300, request.ReleaseMs);
    }

    [Fact]
    public void QueryShape_AndBusQueryShape()
    {
        Assert.False(AudioV1CallParser.TryParseQuery(new[] { I(1) }, out _, out var error, out _));
        Assert.Equal(AudioV1ArgumentError.Count, error);

        Assert.False(AudioV1CallParser.TryParseQuery(new[] { S("1"), I(0) }, out _, out error, out var index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(0, index);

        Assert.False(AudioV1CallParser.TryParseQuery(new[] { I(1), S("0") }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(1, index);

        Assert.False(AudioV1CallParser.TryParseBusQuery(new[] { I(1), I(0) }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(0, index);

        Assert.False(AudioV1CallParser.TryParseBusQuery(new[] { S("bgm"), S("0") }, out _, out error, out index));
        Assert.Equal(AudioV1ArgumentError.Type, error);
        Assert.Equal(1, index);
    }

    // ---------------- AUDIO_PLAY ----------------

    [Fact]
    public void Play_NormalPath_ReturnsMonotonicHandlesAndPlayingState()
    {
        var session = Create(out var backend);
        long first = Play(session, "one.ogg", "se");
        long second = Play(session, "two.ogg", "se");

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(AudioV1State.Playing, session.GetState((int)first));
        Assert.Equal(AudioV1State.Playing, session.GetState((int)second));
        Assert.Equal(2, backend.PlayCalls);
    }

    [Fact]
    public void Play_PassesIntentToBackend()
    {
        var session = Create(out var backend);
        long handle = Play(session, "voice.ogg", "voice", 60, 1, 250,
            priority: 200, maxInstances: 2, flags: 0, crossfadeFrom: 0,
            crossfadeMs: 500, preservePitch: 0, rate: 1.5);

        Assert.NotEqual(0, handle);
        Assert.Equal("voice.ogg", backend.LastRequest.Track);
        Assert.Equal("voice", backend.LastRequest.Bus);
        Assert.Equal(60, backend.LastRequest.Volume);
        Assert.True(backend.LastRequest.Loop);
        Assert.Equal(250, backend.LastRequest.FadeInMs);
        Assert.Equal(200, backend.LastRequest.Priority);
        Assert.Equal(2, backend.LastRequest.MaxInstances);
        Assert.False(backend.LastRequest.PreservePitch);
        Assert.Equal(1.5, backend.LastRequest.Rate);
    }

    [Fact]
    public void Play_ClampsIntentBeforeBackendAndQuery()
    {
        var session = Create(out var backend);
        long handle = Play(session, "loud.ogg", "se", volume: 150, priority: 999, rate: 20.0);

        Assert.NotEqual(0, handle);
        Assert.Equal(100, backend.LastRequest.Volume);
        Assert.Equal(255, backend.LastRequest.Priority);
        Assert.Equal(10.0, backend.LastRequest.Rate);
        Assert.Equal(100, Query(session, handle, AudioV1QueryField.Volume));
        Assert.Equal(255, Query(session, handle, AudioV1QueryField.Priority));
        Assert.Equal(1000, Query(session, handle, AudioV1QueryField.Rate));
    }

    [Fact]
    public void Play_MissingTrack_ReturnsZeroAndMarksFailed()
    {
        var session = Create(out var backend);
        backend.MissingTracks.Add("missing.ogg");

        long handle = Play(session, "missing.ogg", "se");

        Assert.Equal(0, handle);
        Assert.NotEqual(0, session.LastFailedHandle);
        Assert.Equal(AudioV1State.Failed, session.GetState(session.LastFailedHandle));
    }

    [Fact]
    public void BackendUnavailable_DegradesByCodeTable()
    {
        var session = Create(out var backend);
        long handle = Play(session);
        backend.IsAvailable = false;

        Assert.Equal(0, Play(session, "x.ogg", "se"));
        Assert.Equal(-2, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Stop))));
        Assert.Equal(-2, session.Bus(ParseBus(S("bgm"), I((int)AudioV1BusOp.Volume), I(50))));
        Assert.Equal(-2, session.Duck(ParseDuck(S("voice"), S("bgm"), I(-12))));
        // 查询仍然读账本，不因后端不可用而变成 -1。
        Assert.Equal((long)AudioV1State.Playing, Query(session, handle, AudioV1QueryField.State));
    }

    [Fact]
    public void Play_InvalidBus_ReturnsZeroWithoutBackendPlay()
    {
        var session = Create(out var backend);
        Assert.Equal(0, Play(session, "x.ogg", "bogus"));
        Assert.Equal(0, Play(session, "x.ogg", "master"));
        Assert.Equal(0, backend.PlayCalls);
    }

    [Fact]
    public void Play_IfChanged_ReturnsExistingHandle()
    {
        var session = Create(out var backend);
        long first = Play(session, "same.ogg", "se", flags: 2);
        long second = Play(session, "same.ogg", "se", flags: 2);

        Assert.Equal(first, second);
        Assert.Equal(1, backend.PlayCalls);
    }

    [Fact]
    public void Play_IfChanged_OtherBusGetsNewHandle()
    {
        var session = Create(out var backend);
        long first = Play(session, "same.ogg", "se", flags: 2);
        long second = Play(session, "same.ogg", "voice", flags: 2);

        Assert.NotEqual(first, second);
        Assert.Equal(2, backend.PlayCalls);
    }

    [Fact]
    public void Play_IfChanged_AfterStopGetsNewHandle()
    {
        var session = Create(out var backend);
        long first = Play(session, "same.ogg", "se", flags: 2);
        Assert.Equal(1, session.Control(ParseCtl(I(first), I((int)AudioV1Op.Stop))));
        long second = Play(session, "same.ogg", "se", flags: 2);

        Assert.NotEqual(first, second);
        Assert.Equal(2, backend.PlayCalls);
    }

    [Fact]
    public void Play_MaxInstances_StealsOldestAndRecordsStolenBy()
    {
        var session = Create(out var backend);
        long first = Play(session, "same.ogg", "se", maxInstances: 1);
        long second = Play(session, "same.ogg", "se", maxInstances: 1);

        Assert.NotEqual(0, second);
        Assert.Equal(AudioV1State.Stolen, session.GetState((int)first));
        Assert.Equal(AudioV1State.Playing, session.GetState((int)second));
        Assert.Equal(second, Query(session, first, AudioV1QueryField.StolenBy));
        Assert.True(backend.StopCalls >= 1);
    }

    [Fact]
    public void Play_MaxInstances_StealNoneRejects()
    {
        var session = Create(out var backend);
        long first = Play(session, "same.ogg", "se", maxInstances: 1);
        long second = Play(session, "same.ogg", "se", maxInstances: 1, flags: 1);

        Assert.Equal(0, second);
        Assert.Equal(AudioV1State.Playing, session.GetState((int)first));
        Assert.Equal(1, backend.PlayCalls);
    }

    [Fact]
    public void Play_MaxInstances_DoesNotCountOtherTracks()
    {
        var session = Create(out var backend);
        long first = Play(session, "one.ogg", "se", maxInstances: 1);
        long second = Play(session, "two.ogg", "se", maxInstances: 1);

        Assert.NotEqual(0, second);
        Assert.Equal(AudioV1State.Playing, session.GetState((int)first));
        Assert.Equal(AudioV1State.Playing, session.GetState((int)second));
    }

    // ---------------- AUDIO_CTL ----------------

    [Fact]
    public void Control_Stop_ReturnsOneAndStops()
    {
        var session = Create(out _);
        long handle = Play(session);
        Assert.Equal(1, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Stop))));
        Assert.Equal(AudioV1State.Stopped, session.GetState((int)handle));
        Assert.Equal(-1, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Stop))));
    }

    [Fact]
    public void Control_PauseResume_RoundTrip()
    {
        var session = Create(out _);
        long handle = Play(session);
        Assert.Equal(1, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Pause))));
        Assert.Equal(AudioV1State.Paused, session.GetState((int)handle));
        Assert.Equal(0, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Pause))));
        Assert.Equal(1, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Resume))));
        Assert.Equal(AudioV1State.Playing, session.GetState((int)handle));
        Assert.Equal(0, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Resume))));
    }

    [Fact]
    public void Control_Seek_ReturnsMinusTwoWhenBackendDoesNotSupportIt()
    {
        var session = Create(out _);
        long handle = Play(session);
        Assert.Equal(-2, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Seek), I(1000))));
        Assert.Equal(AudioV1State.Playing, session.GetState((int)handle));
    }

    [Fact]
    public void Control_VolumeAndFade_ApplyImmediately()
    {
        var session = Create(out _);
        long handle = Play(session, volume: 100);
        Assert.Equal(1, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Volume), I(40))));
        Assert.Equal(40, Query(session, handle, AudioV1QueryField.Volume));
        Assert.Equal(1, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Fade), I(25), I(500))));
        Assert.Equal(25, Query(session, handle, AudioV1QueryField.Volume));
    }

    [Fact]
    public void Control_Rate_UsesPercentAndOptionalPreservePitch()
    {
        var session = Create(out _);
        long handle = Play(session);
        Assert.Equal(1, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Rate), I(150), I(0), I(0))));
        Assert.Equal(150, Query(session, handle, AudioV1QueryField.Rate));
        Assert.Equal(0, Query(session, handle, AudioV1QueryField.PitchPreserved));
    }

    [Fact]
    public void Control_PriorityAndLoop_AreLedgerOnly()
    {
        var session = Create(out _);
        long handle = Play(session);
        Assert.Equal(1, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Priority), I(42))));
        Assert.Equal(42, Query(session, handle, AudioV1QueryField.Priority));
        Assert.Equal(1, session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Loop), I(1))));
        Assert.Equal(1, Query(session, handle, AudioV1QueryField.Loop));
    }

    [Fact]
    public void Control_InvalidHandleOrOp_ReturnsMinusOneOrMinusTwo()
    {
        var session = Create(out _);
        Assert.Equal(-1, session.Control(ParseCtl(I(999), I((int)AudioV1Op.Stop))));
        long handle = Play(session);
        Assert.Equal(-2, session.Control(ParseCtl(I(handle), I(99))));
    }

    // ---------------- AUDIO_BUS ----------------

    [Fact]
    public void Bus_VolumeAndMute_ReturnOneOnChangeAndZeroOnRepeat()
    {
        var session = Create(out var backend);
        Assert.Equal(1, session.Bus(ParseBus(S("bgm"), I((int)AudioV1BusOp.Volume), I(70))));
        Assert.Equal(70, BusQuery(session, "bgm", AudioV1BusQueryField.Volume));
        Assert.Equal(0, session.Bus(ParseBus(S("bgm"), I((int)AudioV1BusOp.Volume), I(70))));

        Assert.Equal(1, session.Bus(ParseBus(S("bgm"), I((int)AudioV1BusOp.Mute), I(1))));
        Assert.Equal(1, BusQuery(session, "bgm", AudioV1BusQueryField.Mute));
        Assert.Equal(0, session.Bus(ParseBus(S("bgm"), I((int)AudioV1BusOp.Mute), I(1))));
        Assert.True(backend.BusVolumeCalls > 0);
        Assert.True(backend.BusMuteCalls > 0);
    }

    [Fact]
    public void Bus_StopAll_StopsVoicesOnThatBus()
    {
        var session = Create(out var backend);
        long se = Play(session, "a.ogg", "se");
        long voice = Play(session, "b.ogg", "voice");
        Assert.Equal(1, session.Bus(ParseBus(S("se"), I((int)AudioV1BusOp.StopAll), I(0))));
        Assert.Equal(AudioV1State.Stopped, session.GetState((int)se));
        Assert.Equal(AudioV1State.Playing, session.GetState((int)voice));
        Assert.Equal(0, BusQuery(session, "se", AudioV1BusQueryField.Voices));
        Assert.True(backend.StopAllCalls > 0);
    }

    [Fact]
    public void Bus_StopAllMaster_StopsEveryBus()
    {
        var session = Create(out _);
        long se = Play(session, "a.ogg", "se");
        long bgm = Play(session, "b.ogg", "bgm");
        Assert.Equal(1, session.Bus(ParseBus(S("master"), I((int)AudioV1BusOp.StopAll), I(1))));
        Assert.Equal(AudioV1State.Stopped, session.GetState((int)se));
        Assert.Equal(AudioV1State.Stopped, session.GetState((int)bgm));
        Assert.Equal(0, BusQuery(session, "master", AudioV1BusQueryField.Voices));
    }

    [Fact]
    public void Bus_UnknownBusOrOp_ReturnsMinusOneOrMinusTwo()
    {
        var session = Create(out _);
        Assert.Equal(-1, session.Bus(ParseBus(S("bogus"), I((int)AudioV1BusOp.Volume), I(50))));
        Assert.Equal(-2, session.Bus(ParseBus(S("bgm"), I(99), I(0))));
    }

    // ---------------- AUDIO_DUCK ----------------

    [Fact]
    public void Duck_AttenuatesTargetEffectiveVolume()
    {
        var session = Create(out _);
        long handle = Play(session, "music.ogg", "bgm", volume: 100);

        Assert.Equal(1, session.Duck(ParseDuck(S("voice"), S("bgm"), I(-12))));
        Assert.Equal(25, BusQuery(session, "bgm", AudioV1BusQueryField.EffectiveVolume));
        Assert.Equal(25, Query(session, handle, AudioV1QueryField.EffectiveVolume));
        Assert.Equal(120, BusQuery(session, "bgm", AudioV1BusQueryField.DuckDb));
    }

    [Fact]
    public void Duck_SameRuleTwiceIsNoOp_AndZeroRemovesIt()
    {
        var session = Create(out _);
        Play(session, "music.ogg", "bgm");
        Assert.Equal(1, session.Duck(ParseDuck(S("voice"), S("bgm"), I(-12))));
        Assert.Equal(0, session.Duck(ParseDuck(S("voice"), S("bgm"), I(-12))));
        Assert.Equal(1, session.Duck(ParseDuck(S("voice"), S("bgm"), I(0))));
        Assert.Equal(0, BusQuery(session, "bgm", AudioV1BusQueryField.DuckDb));
        Assert.Equal(100, BusQuery(session, "bgm", AudioV1BusQueryField.EffectiveVolume));
    }

    [Fact]
    public void Duck_MultipleRulesTakeMostNegative()
    {
        var session = Create(out _);
        Play(session, "music.ogg", "bgm");
        Assert.Equal(1, session.Duck(ParseDuck(S("voice"), S("bgm"), I(-6))));
        Assert.Equal(1, session.Duck(ParseDuck(S("se"), S("bgm"), I(-18))));
        Assert.Equal(180, BusQuery(session, "bgm", AudioV1BusQueryField.DuckDb));
    }

    [Fact]
    public void Duck_InvalidBus_ReturnsMinusOne()
    {
        var session = Create(out _);
        Assert.Equal(-1, session.Duck(ParseDuck(S("bogus"), S("bgm"), I(-12))));
        Assert.Equal(-1, session.Duck(ParseDuck(S("voice"), S("bogus"), I(-12))));
        Assert.Equal(-1, session.Duck(ParseDuck(S("master"), S("bgm"), I(-12))));
        Assert.Equal(-1, session.Duck(ParseDuck(S("bgm"), S("bgm"), I(-12))));
    }

    // ---------------- AUDIO_QUERY ----------------

    [Fact]
    public void Query_TwelveFields()
    {
        var session = Create(out _);
        long handle = Play(session, "voice.ogg", "se", volume: 80, loop: 1,
            priority: 200, preservePitch: 1, rate: 1.5);

        Assert.Equal((long)AudioV1State.Playing, Query(session, handle, AudioV1QueryField.State));
        Assert.Equal(0, Query(session, handle, AudioV1QueryField.Pos));
        Assert.Equal(0, Query(session, handle, AudioV1QueryField.Dur));
        Assert.Equal(150, Query(session, handle, AudioV1QueryField.Rate));
        Assert.Equal(80, Query(session, handle, AudioV1QueryField.Volume));
        Assert.Equal(80, Query(session, handle, AudioV1QueryField.EffectiveVolume));
        Assert.Equal(200, Query(session, handle, AudioV1QueryField.Priority));
        Assert.Equal(0, Query(session, handle, AudioV1QueryField.StolenBy));
        Assert.Equal(1, Query(session, handle, AudioV1QueryField.PitchPreserved));
        Assert.Equal(0, Query(session, handle, AudioV1QueryField.Latency));
        Assert.Equal((long)AudioV1Bus.Se, Query(session, handle, AudioV1QueryField.Bus));
        Assert.Equal(1, Query(session, handle, AudioV1QueryField.Loop));

        session.UpdatePosition((int)handle, 1.5, 3.0);
        Assert.Equal(1500, Query(session, handle, AudioV1QueryField.Pos));
        Assert.Equal(3000, Query(session, handle, AudioV1QueryField.Dur));
    }

    [Fact]
    public void Query_UnknownHandleOrField_ReturnsMinusOne()
    {
        var session = Create(out _);
        Assert.Equal(-1, Query(session, 999, AudioV1QueryField.State));
        long handle = Play(session);
        Assert.Equal(-1, session.Query((int)handle, 99));
    }

    [Fact]
    public void Query_EffectiveVolumeIncludesMasterBusAndDuck()
    {
        var session = Create(out _);
        long handle = Play(session, "music.ogg", "bgm", volume: 100);
        Assert.Equal(1, session.Bus(ParseBus(S("master"), I((int)AudioV1BusOp.Volume), I(50))));
        Assert.Equal(1, session.Bus(ParseBus(S("bgm"), I((int)AudioV1BusOp.Volume), I(50))));
        Assert.Equal(1, session.Duck(ParseDuck(S("voice"), S("bgm"), I(-6))));

        Assert.Equal(13, Query(session, handle, AudioV1QueryField.EffectiveVolume));
        Assert.Equal(13, BusQuery(session, "bgm", AudioV1BusQueryField.EffectiveVolume));
    }

    [Fact]
    public void Query_MuteForcesZeroEffectiveVolume()
    {
        var session = Create(out _);
        long handle = Play(session, "music.ogg", "bgm", volume: 100);
        Assert.Equal(1, session.Bus(ParseBus(S("master"), I((int)AudioV1BusOp.Mute), I(1))));
        Assert.Equal(0, Query(session, handle, AudioV1QueryField.EffectiveVolume));

        Assert.Equal(1, session.Bus(ParseBus(S("master"), I((int)AudioV1BusOp.Mute), I(0))));
        Assert.Equal(1, session.Bus(ParseBus(S("bgm"), I((int)AudioV1BusOp.Mute), I(1))));
        Assert.Equal(0, Query(session, handle, AudioV1QueryField.EffectiveVolume));
        Assert.Equal(0, BusQuery(session, "bgm", AudioV1BusQueryField.EffectiveVolume));
    }

    // ---------------- AUDIO_BUS_QUERY ----------------

    [Fact]
    public void BusQuery_FiveFields()
    {
        var session = Create(out _);
        Assert.Equal(100, BusQuery(session, "se", AudioV1BusQueryField.Volume));
        Assert.Equal(100, BusQuery(session, "se", AudioV1BusQueryField.EffectiveVolume));
        Assert.Equal(0, BusQuery(session, "se", AudioV1BusQueryField.Mute));
        Assert.Equal(0, BusQuery(session, "se", AudioV1BusQueryField.DuckDb));
        Assert.Equal(0, BusQuery(session, "se", AudioV1BusQueryField.Voices));

        long handle = Play(session, "a.ogg", "se");
        Assert.Equal(1, BusQuery(session, "se", AudioV1BusQueryField.Voices));
        session.Control(ParseCtl(I(handle), I((int)AudioV1Op.Stop)));
        Assert.Equal(0, BusQuery(session, "se", AudioV1BusQueryField.Voices));
    }

    [Fact]
    public void BusQuery_MasterCountsAllVoices()
    {
        var session = Create(out _);
        Play(session, "a.ogg", "se");
        Play(session, "b.ogg", "bgm");
        Play(session, "c.ogg", "voice");
        Assert.Equal(3, BusQuery(session, "master", AudioV1BusQueryField.Voices));
    }

    [Fact]
    public void BusQuery_UnknownBusOrField_ReturnsMinusOne()
    {
        var session = Create(out _);
        Assert.Equal(-1, BusQuery(session, "bogus", AudioV1BusQueryField.Volume));
        Assert.Equal(-1, session.BusQuery("bgm", 99));
    }

    // ---------------- reset ----------------

    [Fact]
    public void Reset_ClearsHandlesBusesAndDucks()
    {
        var session = Create(out var backend);
        long first = Play(session, "a.ogg", "se", volume: 100);
        Play(session, "b.ogg", "bgm");
        session.Bus(ParseBus(S("bgm"), I((int)AudioV1BusOp.Volume), I(30)));
        session.Duck(ParseDuck(S("voice"), S("bgm"), I(-12)));

        session.Reset();

        Assert.Equal(AudioV1State.Unknown, session.GetState((int)first));
        Assert.Equal(-1, Query(session, first, AudioV1QueryField.State));
        Assert.Equal(100, BusQuery(session, "bgm", AudioV1BusQueryField.Volume));
        Assert.Equal(0, BusQuery(session, "bgm", AudioV1BusQueryField.DuckDb));
        Assert.Equal(0, BusQuery(session, "master", AudioV1BusQueryField.Voices));

        long replacement = Play(session, "c.ogg", "se");
        Assert.Equal(1, replacement);
        Assert.True(backend.StopAllCalls > 0);
        Assert.True(backend.ResetCalls > 0);
    }

    // ---------------- fake backend ----------------

    sealed class FakeBackend : IAudioV1Backend
    {
        public bool IsAvailable { get; set; } = true;
        public int PlayCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int BusVolumeCalls { get; private set; }
        public int BusMuteCalls { get; private set; }
        public int StopAllCalls { get; private set; }
        public int ResetCalls { get; private set; }
        public AudioV1PlayRequest LastRequest { get; private set; } = new AudioV1PlayRequest();
        public HashSet<string> MissingTracks { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool Play(AudioV1PlayRequest request, int handle)
        {
            PlayCalls++;
            LastRequest = request;
            return !MissingTracks.Contains(request.Track);
        }

        public void Stop(int handle) => StopCalls++;
        public void Pause(int handle) { }
        public void Resume(int handle) { }
        public void SetVolume(int handle, int volume) { }
        public void SetRate(int handle, double rate, bool preservePitch) { }
        public void SetLoop(int handle, bool loop) { }
        public void SetBusVolume(AudioV1Bus bus, int volume) => BusVolumeCalls++;
        public void SetBusMute(AudioV1Bus bus, bool mute) => BusMuteCalls++;
        public void StopAll(AudioV1Bus bus) => StopAllCalls++;
        public void Reset() => ResetCalls++;
        public void SetDuck(AudioV1Bus targetBus, int duckDb) { }
    }
}
