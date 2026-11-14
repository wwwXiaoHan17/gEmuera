// GenericUtils.AudioV1.cs —— EWP audio v1（AUDIO_* 六内建）的宿主适配器：
// 把 Core 层 AudioV1Session 的抽象原语映射到既有 Snake 音频桥（10 条声音通道 + 单条 BGM）。
// 账本本身不引用 Godot；本文件是唯一接触桥/UI 队列的边界。
using System;
using System.Collections.Generic;
using GEmuera.Core.Audio;

internal static partial class GenericUtils
{
    static readonly AudioV1Backend audioV1Backend = new AudioV1Backend();
    static readonly AudioV1Session audioV1Session = new AudioV1Session(audioV1Backend);

    /// <summary>六内建共享的账本实例（函数方法类委托到它）。</summary>
    internal static AudioV1Session AudioV1 => audioV1Session;

    /// <summary>会话停止路径（EmueraMain.StopLegacySession）调用：清账本并整体停声。</summary>
    internal static void ResetAudioV1() => audioV1Session.Reset();

    // 以下回调由 GenericUtils.Audio.cs 在既有 Notify* 路径的锁外调用，
    // 避免 snakeAudioLock → 账本锁 与 账本锁 → snakeAudioLock 的锁序反转。
    static void AudioV1OnSoundPosition(int channel, double currentSec, double totalSec)
        => audioV1Backend.UpdateSoundPosition(channel, currentSec, totalSec);

    static void AudioV1OnBgmPosition(double currentSec, double totalSec)
        => audioV1Backend.UpdateBgmPosition(currentSec, totalSec);

    static void AudioV1OnSoundFinished(int channel)
        => audioV1Backend.ReleaseFinishedSound(channel);

    static void AudioV1OnSoundFailed(int channel)
        => audioV1Backend.ReleaseFailedSound(channel);

    static void AudioV1OnBgmFailed()
        => audioV1Backend.ReleaseFailedBgm();

    static void AudioV1OnBgmStarted()
        => audioV1Backend.ReapplyBgmAfterStart();

    /// <summary>
    /// 既有桥的原语映射。桥能力边界（全部以 DIVERGENCE 标注）：
    /// - 声音池固定 10 条 + 单条 BGM；没有独立于总线的每句柄音量；
    /// - 不支持 seek / 时间性淡变 / duck attack-release / crossfade / 运行期改 loop；
    /// - BGM 恒循环（PlayBgmFile 语义）。
    /// </summary>
    sealed class AudioV1Backend : IAudioV1Backend
    {
        readonly object gate = new object();
        readonly Dictionary<int, int> channelByHandle = new Dictionary<int, int>();
        readonly Dictionary<int, int> handleByChannel = new Dictionary<int, int>();
        readonly Dictionary<int, AudioV1Bus> busByHandle = new Dictionary<int, AudioV1Bus>();
        readonly int[] busVolumes = { 100, 100, 100, 100 };
        readonly bool[] busMutes = new bool[4];
        readonly int[] duckDb = new int[4];
        int bgmHandle;
        int bgmHandleVolume = 100;
        double bgmRate = 1.0;
        bool bgmPreservePitch = true;

        public bool IsAvailable => true;

        public bool Play(AudioV1PlayRequest request, int handle)
        {
            // 音轨解析放在锁外：ResolveSoundPath 可能递归扫描目录。
            string resolved = GenericUtils.ResolveSoundPath(request.Track);
            if (string.IsNullOrEmpty(resolved) || !uEmuera.Utils.FileExists(resolved))
                return false;

            lock (gate)
            {
                string busName = (request.Bus ?? string.Empty).Trim();
                if (string.Equals(busName, "bgm", StringComparison.OrdinalIgnoreCase))
                {
                    // 单 BGM 通道：会话层已把旧 BGM 实例收敛为 Stopped，这里只做防御性停声。
                    if (bgmHandle != 0)
                    {
                        GenericUtils.StopBgm();
                        busByHandle.Remove(bgmHandle);
                        bgmHandle = 0;
                    }
                    bgmHandle = handle;
                    bgmHandleVolume = ClampEraVolume(request.Volume);
                    bgmRate = request.Rate;
                    bgmPreservePitch = request.PreservePitch;
                    busByHandle[handle] = AudioV1Bus.Bgm;
                    GenericUtils.PlayBgmFile(resolved);
                    // DIVERGENCE: PlayBgmFile 恒循环，request.Loop 只入账本。
                    // BGM 后台加载完成前 ControlBgm 可能没有 player 可作用，
                    // NotifyBgmPlaybackStarted 的 ReapplyBgmAfterStart 会补一次速率/音量。
                    GenericUtils.ControlBgm(3, ToSpeed(bgmRate), bgmPreservePitch);
                    GenericUtils.SetBgmVolume(PhysicalBgmVolume(bgmHandleVolume));
                    return true;
                }

                var mappedBus = string.Equals(busName, "voice", StringComparison.OrdinalIgnoreCase)
                    ? AudioV1Bus.Voice : AudioV1Bus.Se;
                int channel = FindFreeChannelLocked();
                if (channel < 0)
                {
                    // DIVERGENCE: 参考实现 se/voice 有 32/8 声部；桥只有 10 条通道，
                    // 用尽时拒绝新播放（PLAY 返回 0 / Failed），而不是静默覆写旧实例。
                    return false;
                }
                channelByHandle[handle] = channel;
                handleByChannel[channel] = handle;
                busByHandle[handle] = mappedBus;
                GenericUtils.PlaySoundFile(resolved, request.Loop ? -1 : 1, channel);
                GenericUtils.ControlSound(channel, 3, ToSpeed(request.Rate), request.PreservePitch);
                // DIVERGENCE: bridge 的声音音量是全局的（SetSoundVolume），没有每通道音量；
                // 每句柄音量只入账本，物理输出按目标总线音量近似。
                GenericUtils.SetSoundVolume(PhysicalSoundVolume(mappedBus));
                return true;
            }
        }

        public void Stop(int handle)
        {
            lock (gate)
            {
                if (handle == 0)
                    return;
                if (bgmHandle == handle)
                {
                    GenericUtils.StopBgm();
                    bgmHandle = 0;
                    busByHandle.Remove(handle);
                    return;
                }
                if (channelByHandle.TryGetValue(handle, out int channel))
                {
                    // ControlSound(action=2) 同时更新 snake 音频账本并排入 StopSoundChannel，
                    // 避免只用 EmueraContent 停声导致 GETSOUNDORBGMINFO 仍报 playing。
                    GenericUtils.ControlSound(channel, 2);
                    ReleaseChannelLocked(handle, channel);
                }
            }
        }

        public void Pause(int handle)
        {
            lock (gate)
            {
                if (bgmHandle == handle)
                    GenericUtils.ControlBgm(0);
                else if (channelByHandle.TryGetValue(handle, out int channel))
                    GenericUtils.ControlSound(channel, 0);
            }
        }

        public void Resume(int handle)
        {
            lock (gate)
            {
                if (bgmHandle == handle)
                    GenericUtils.ControlBgm(1);
                else if (channelByHandle.TryGetValue(handle, out int channel))
                    GenericUtils.ControlSound(channel, 1);
            }
        }

        public void SetVolume(int handle, int volume)
        {
            lock (gate)
            {
                if (bgmHandle == handle)
                {
                    bgmHandleVolume = ClampEraVolume(volume);
                    GenericUtils.SetBgmVolume(PhysicalBgmVolume(bgmHandleVolume));
                }
                // DIVERGENCE: no per-channel volume on the bridge for se/voice.
            }
        }

        public void SetRate(int handle, double rate, bool preservePitch)
        {
            // DIVERGENCE: 既有 ControlSound/ControlBgm 接收 preservePitch 但桥用 PitchScale
            // 改速率，无法真正保音高；账本只记录该意图供 AUDIO_QUERY(PitchPreserved) 查询。
            lock (gate)
            {
                int speed = ToSpeed(rate);
                if (bgmHandle == handle)
                {
                    bgmRate = rate;
                    bgmPreservePitch = preservePitch;
                    GenericUtils.ControlBgm(3, speed, preservePitch);
                }
                else if (channelByHandle.TryGetValue(handle, out int channel))
                    GenericUtils.ControlSound(channel, 3, speed, preservePitch);
            }
        }

        public void SetLoop(int handle, bool loop)
        {
            // DIVERGENCE: Godot AudioStream 的 loop 在加载时确定，桥不支持运行期切换。
            _ = handle;
            _ = loop;
        }

        public void SetBusVolume(AudioV1Bus bus, int volume)
        {
            lock (gate)
            {
                busVolumes[(int)bus] = ClampEraVolume(volume);
                ApplyPhysicalLocked(bus);
            }
        }

        public void SetBusMute(AudioV1Bus bus, bool mute)
        {
            lock (gate)
            {
                busMutes[(int)bus] = mute;
                ApplyPhysicalLocked(bus);
            }
        }

        public void StopAll(AudioV1Bus bus)
        {
            lock (gate)
            {
                if (bus == AudioV1Bus.Master)
                {
                    GenericUtils.StopSounds();
                    GenericUtils.StopBgm();
                    channelByHandle.Clear();
                    handleByChannel.Clear();
                    busByHandle.Clear();
                    bgmHandle = 0;
                    return;
                }
                if (bus == AudioV1Bus.Bgm)
                {
                    GenericUtils.StopBgm();
                    if (bgmHandle != 0)
                    {
                        busByHandle.Remove(bgmHandle);
                        bgmHandle = 0;
                    }
                    return;
                }

                var toRelease = new List<int>();
                foreach (var pair in channelByHandle)
                {
                    if (busByHandle.TryGetValue(pair.Key, out var handleBus) && handleBus == bus)
                        toRelease.Add(pair.Key);
                }
                foreach (int handle in toRelease)
                {
                    int channel = channelByHandle[handle];
                    GenericUtils.ControlSound(channel, 2);
                    ReleaseChannelLocked(handle, channel);
                }
            }
        }

        public void SetDuck(AudioV1Bus targetBus, int db)
        {
            lock (gate)
            {
                duckDb[(int)targetBus] = db;
                // DIVERGENCE: no attack/release envelope; apply the target bus gain now.
                ApplyPhysicalLocked(targetBus);
            }
        }

        public void Reset()
        {
            lock (gate)
            {
                channelByHandle.Clear();
                handleByChannel.Clear();
                busByHandle.Clear();
                bgmHandle = 0;
                bgmHandleVolume = 100;
                bgmRate = 1.0;
                bgmPreservePitch = true;
                for (int i = 0; i < busVolumes.Length; i++)
                {
                    busVolumes[i] = 100;
                    busMutes[i] = false;
                    duckDb[i] = 0;
                }
            }
        }

        // ---------------- position / lifecycle callbacks ----------------

        internal void UpdateSoundPosition(int channel, double currentSec, double totalSec)
        {
            int handle;
            lock (gate)
            {
                if (!handleByChannel.TryGetValue(channel, out handle))
                    return;
            }
            // 锁外回调账本，避免 adapter gate → session gate 与 backend 路径的锁序反转。
            GenericUtils.AudioV1.UpdatePosition(handle, currentSec, totalSec);
        }

        internal void UpdateBgmPosition(double currentSec, double totalSec)
        {
            int handle;
            lock (gate)
            {
                handle = bgmHandle;
            }
            if (handle != 0)
                GenericUtils.AudioV1.UpdatePosition(handle, currentSec, totalSec);
        }

        internal void ReleaseFinishedSound(int channel)
        {
            int handle = 0;
            lock (gate)
            {
                if (handleByChannel.TryGetValue(channel, out handle))
                    ReleaseChannelLocked(handle, channel);
            }
            if (handle != 0)
                GenericUtils.AudioV1.NotifyEnded(handle);
        }

        internal void ReleaseFailedSound(int channel)
        {
            int handle = 0;
            lock (gate)
            {
                if (handleByChannel.TryGetValue(channel, out handle))
                    ReleaseChannelLocked(handle, channel);
            }
            if (handle != 0)
                GenericUtils.AudioV1.NotifyFailed(handle);
        }

        internal void ReapplyBgmAfterStart()
        {
            lock (gate)
            {
                if (bgmHandle == 0)
                    return;
                GenericUtils.ControlBgm(3, ToSpeed(bgmRate), bgmPreservePitch);
                GenericUtils.SetBgmVolume(PhysicalBgmVolume(bgmHandleVolume));
            }
        }

        internal void ReleaseFailedBgm()
        {
            int handle;
            lock (gate)
            {
                handle = bgmHandle;
                if (handle != 0)
                {
                    busByHandle.Remove(handle);
                    bgmHandle = 0;
                }
            }
            if (handle != 0)
                GenericUtils.AudioV1.NotifyFailed(handle);
        }

        // ---------------- helpers ----------------

        int FindFreeChannelLocked()
        {
            for (int i = 0; i < SnakeSoundChannelCount; i++)
            {
                if (!handleByChannel.ContainsKey(i))
                    return i;
            }
            return -1;
        }

        void ReleaseChannelLocked(int handle, int channel)
        {
            channelByHandle.Remove(handle);
            handleByChannel.Remove(channel);
            busByHandle.Remove(handle);
        }

        static int ToSpeed(double rate)
        {
            if (double.IsNaN(rate) || rate <= 0)
                return 100;
            int speed = (int)Math.Round(rate * 100.0, MidpointRounding.AwayFromZero);
            // 与 GenericUtils.ControlSound 的 ClampSnakeAudioSpeed 保持一致（10..1000）。
            if (speed < 10)
                return 10;
            if (speed > 1000)
                return 1000;
            return speed;
        }

        void ApplyPhysicalLocked(AudioV1Bus bus)
        {
            if (bus == AudioV1Bus.Bgm || bus == AudioV1Bus.Master)
                GenericUtils.SetBgmVolume(PhysicalBgmVolume(bgmHandleVolume));
            if (bus == AudioV1Bus.Se || bus == AudioV1Bus.Voice || bus == AudioV1Bus.Master)
            {
                var target = bus == AudioV1Bus.Master ? AudioV1Bus.Se : bus;
                GenericUtils.SetSoundVolume(PhysicalSoundVolume(target));
            }
        }

        double MasterGain()
            => busMutes[(int)AudioV1Bus.Master]
                ? 0.0
                : busVolumes[(int)AudioV1Bus.Master] / 100.0;

        double BusGain(AudioV1Bus bus)
            => bus == AudioV1Bus.Master
                ? MasterGain()
                : busMutes[(int)bus] ? 0.0 : busVolumes[(int)bus] / 100.0;

        double DuckGain(AudioV1Bus bus)
            => Math.Pow(10.0, duckDb[(int)bus] / 20.0);

        int PhysicalBgmVolume(int handleVolume)
        {
            double linear = handleVolume / 100.0 * BusGain(AudioV1Bus.Bgm)
                * MasterGain() * DuckGain(AudioV1Bus.Bgm);
            return ClampEraVolume((int)Math.Round(linear * 100.0, MidpointRounding.AwayFromZero));
        }

        int PhysicalSoundVolume(AudioV1Bus bus)
        {
            // DIVERGENCE: 桥只有全局 SetSoundVolume，se/voice 无法独立物理混音；
            // 这里按目标总线的意图设置全局值，最后一次调用者胜出。
            double linear = BusGain(bus) * MasterGain() * DuckGain(bus);
            return ClampEraVolume((int)Math.Round(linear * 100.0, MidpointRounding.AwayFromZero));
        }
    }
}
