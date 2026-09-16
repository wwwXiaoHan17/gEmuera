// GenericUtils.Audio.cs —— 承载旧版 Snake 音频声道/BGM/音量/回放进度与音效路径回退解析功能域，自 GenericUtils.cs 拆出（原因：主文件超 2000 行只减不增约束）。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Godot;
using gEmuera.Diagnostics;
using MinorShift.Emuera.GameView;

internal static partial class GenericUtils
{
    const int SnakeSoundChannelCount = 10;
    static readonly object snakeAudioLock = new object();
    static readonly SnakeAudioState[] snakeSounds = CreateSnakeAudioStates();
    static readonly SnakeAudioState snakeBgm = new SnakeAudioState();
    static readonly object soundFallbackResolveCacheLock = new object();
    static readonly Dictionary<string, string> soundFallbackResolveCache = new Dictionary<string, string>();

    sealed class SnakeAudioState
    {
        public string Path;
        public bool Playing;
        public bool PendingStart;
        public bool Paused;
        public int Volume = 100;
        public int Speed = 100;
        public int Repeat = 1;
        public long StartedAtMs;
        public long TotalMs;
        public long LastKnownCurrentMs;
    }

    public struct SnakeAudioInfo
    {
        public long TotalMs;
        public long CurrentMs;
        public long Playing;
        public long Volume;
        public long Speed;
    }

    static SnakeAudioState[] CreateSnakeAudioStates()
    {
        var states = new SnakeAudioState[SnakeSoundChannelCount];
        for (int i = 0; i < states.Length; i++)
            states[i] = new SnakeAudioState();
        return states;
    }

    static void ResetSnakeAudioStateLocked(SnakeAudioState state)
    {
        state.Path = null;
        state.Playing = false;
        state.PendingStart = false;
        state.Paused = false;
        state.Volume = 100;
        state.Speed = 100;
        state.Repeat = 1;
        state.StartedAtMs = 0;
        state.TotalMs = 0;
        state.LastKnownCurrentMs = 0;
    }

    public static void PlaySoundFile(string path, bool loop)
    {
        PlaySoundFile(path, loop ? -1 : 1);
    }

    public static void PlaySoundFile(string path, int repeat)
    {
        int channel = 0;
        lock (snakeAudioLock)
        {
            FlushCompletedAudioStatesLocked();
            for (int i = 0; i < snakeSounds.Length; i++)
            {
                if (!snakeSounds[i].Playing)
                {
                    channel = i;
                    break;
                }
            }
            var state = snakeSounds[channel];
            state.Path = path;
            state.Playing = true;
            state.PendingStart = true;
            state.Paused = false;
            state.Speed = 100;
            state.Repeat = repeat < 0 ? -1 : Math.Max(repeat, 1);
            state.StartedAtMs = 0;
            state.TotalMs = 0;
            state.LastKnownCurrentMs = 0;
        }
        if (gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
            gEmuera.LegacyRunner.LegacyTrace.TryRecordEffect("audio_enqueued", "sound", path, "play", channel, repeat);
        EnqueueUI(() => EmueraContent.instance?.PlaySoundFile(path, repeat, channel));
    }

    public static void StopSounds()
    {
        lock (snakeAudioLock)
        {
            foreach (var state in snakeSounds)
            {
                state.Playing = false;
                state.PendingStart = false;
                state.Paused = false;
                state.Repeat = 1;
                state.LastKnownCurrentMs = 0;
            }
        }
        if (gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
            gEmuera.LegacyRunner.LegacyTrace.TryRecordEffect("audio_enqueued", "sound", "", "stop_all", -1, 0);
        EnqueueUI(() => EmueraContent.instance?.StopSounds());
    }

    public static void PlayBgmFile(string path)
    {
        lock (snakeAudioLock)
        {
            snakeBgm.Path = path;
            snakeBgm.Playing = true;
            snakeBgm.PendingStart = true;
            snakeBgm.Paused = false;
            snakeBgm.Speed = 100;
            snakeBgm.Repeat = -1;
            snakeBgm.StartedAtMs = 0;
            snakeBgm.TotalMs = 0;
            snakeBgm.LastKnownCurrentMs = 0;
        }
        if (gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
            gEmuera.LegacyRunner.LegacyTrace.TryRecordEffect("audio_enqueued", "bgm", path, "play", -1, -1);
        EnqueueUI(() => EmueraContent.instance?.PlayBgmFile(path));
    }

    public static void StopBgm()
    {
        lock (snakeAudioLock)
        {
            snakeBgm.Playing = false;
            snakeBgm.PendingStart = false;
            snakeBgm.Paused = false;
            snakeBgm.Repeat = 1;
            snakeBgm.LastKnownCurrentMs = 0;
        }
        if (gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
            gEmuera.LegacyRunner.LegacyTrace.TryRecordEffect("audio_enqueued", "bgm", "", "stop", -1, 0);
        EnqueueUI(() => EmueraContent.instance?.StopBgm());
    }

    public static void SetSoundVolume(int volume)
    {
        lock (snakeAudioLock)
        {
            foreach (var state in snakeSounds)
                state.Volume = ClampEraVolume(volume);
        }
        if (gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
            gEmuera.LegacyRunner.LegacyTrace.TryRecordEffect("audio_enqueued", "sound", "", "set_volume", -1, volume);
        EnqueueUI(() => EmueraContent.instance?.SetSoundVolume(volume));
    }

    public static void SetBgmVolume(int volume)
    {
        lock (snakeAudioLock)
            snakeBgm.Volume = ClampEraVolume(volume);
        if (gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
            gEmuera.LegacyRunner.LegacyTrace.TryRecordEffect("audio_enqueued", "bgm", "", "set_volume", -1, volume);
        EnqueueUI(() => EmueraContent.instance?.SetBgmVolume(volume));
    }

    public static bool SoundFileExists(string name)
    {
        string path = ResolveSoundPath(name);
        return !string.IsNullOrEmpty(path) && uEmuera.Utils.FileExists(path);
    }

    public static int FindPlayingSound(int channel)
    {
        lock (snakeAudioLock)
        {
            FlushCompletedAudioStatesLocked();
            return channel >= 0 && channel < snakeSounds.Length && snakeSounds[channel].Playing && !snakeSounds[channel].Paused ? channel : -1;
        }
    }

    public static bool IsPlayingBgm()
    {
        lock (snakeAudioLock)
        {
            FlushCompletedAudioStatesLocked();
            return snakeBgm.Playing && !snakeBgm.Paused;
        }
    }

    public static int ControlSound(int channel, int action, int speed = 100)
    {
        return ControlSound(channel, action, speed, true);
    }

    public static int ControlSound(int channel, int action, int speed, bool preservePitch)
    {
        if (channel < 0 || channel >= SnakeSoundChannelCount)
            return -1;
        lock (snakeAudioLock)
        {
            SnakeAudioState state = snakeSounds[channel];
            switch (action)
            {
                case 0:
                    state.Paused = true;
                    state.LastKnownCurrentMs = GetCurrentAudioMs(state);
                    EnqueueUI(() => EmueraContent.instance?.PauseSoundChannel(channel, true));
                    return 1;
                case 1:
                    state.Paused = false;
                    if (!string.IsNullOrEmpty(state.Path))
                        state.Playing = true;
                    state.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(state.LastKnownCurrentMs, state.Speed);
                    EnqueueUI(() => EmueraContent.instance?.PauseSoundChannel(channel, false));
                    return 1;
                case 2:
                    state.Playing = false;
                    state.PendingStart = false;
                    state.Paused = false;
                    state.Repeat = 1;
                    state.LastKnownCurrentMs = 0;
                    EnqueueUI(() => EmueraContent.instance?.StopSoundChannel(channel));
                    return 1;
                case 3:
                    state.LastKnownCurrentMs = GetCurrentAudioMs(state);
                    state.Speed = ClampSnakeAudioSpeed(speed);
                    state.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(state.LastKnownCurrentMs, state.Speed);
                    // Godot AudioStreamPlayer 只能通过 PitchScale 做跨平台变速，preservePitch 参数按 snake API 接收但无法完全保真。
                    EnqueueUI(() => EmueraContent.instance?.SetSoundChannelSpeed(channel, state.Speed / 100.0f));
                    return 1;
                default:
                    return -2;
            }
        }
    }

    public static int ControlBgm(int action, int speed = 100)
    {
        return ControlBgm(action, speed, true);
    }

    public static int ControlBgm(int action, int speed, bool preservePitch)
    {
        lock (snakeAudioLock)
        {
            switch (action)
            {
                case 0:
                    snakeBgm.LastKnownCurrentMs = GetCurrentAudioMs(snakeBgm);
                    snakeBgm.Paused = true;
                    EnqueueUI(() => EmueraContent.instance?.PauseBgm(true));
                    return 1;
                case 1:
                    snakeBgm.Paused = false;
                    if (!string.IsNullOrEmpty(snakeBgm.Path))
                        snakeBgm.Playing = true;
                    snakeBgm.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(snakeBgm.LastKnownCurrentMs, snakeBgm.Speed);
                    EnqueueUI(() => EmueraContent.instance?.PauseBgm(false));
                    return 1;
                case 2:
                    snakeBgm.Playing = false;
                    snakeBgm.PendingStart = false;
                    snakeBgm.Paused = false;
                    snakeBgm.Repeat = 1;
                    snakeBgm.LastKnownCurrentMs = 0;
                    EnqueueUI(() => EmueraContent.instance?.StopBgm());
                    return 1;
                case 3:
                    snakeBgm.LastKnownCurrentMs = GetCurrentAudioMs(snakeBgm);
                    snakeBgm.Speed = ClampSnakeAudioSpeed(speed);
                    snakeBgm.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(snakeBgm.LastKnownCurrentMs, snakeBgm.Speed);
                    // Godot 后端无 SoundTouch 等价能力，保留参数仅保证脚本接口兼容。
                    EnqueueUI(() => EmueraContent.instance?.SetBgmSpeed(snakeBgm.Speed / 100.0f));
                    return 1;
                default:
                    return -2;
            }
        }
    }

    public static SnakeAudioInfo GetAudioInfo(int channel)
    {
        lock (snakeAudioLock)
        {
            FlushCompletedAudioStatesLocked();
            SnakeAudioState state = channel == -1 ? snakeBgm : channel >= 0 && channel < snakeSounds.Length ? snakeSounds[channel] : null;
            if (state == null)
                return default;
            return new SnakeAudioInfo
            {
                TotalMs = state.TotalMs,
                CurrentMs = GetCurrentAudioMs(state),
                Playing = state.Playing && !state.Paused ? 1 : 0,
                Volume = state.Volume,
                Speed = state.Speed
            };
        }
    }

    public static void NotifySoundPlaybackStarted(int channel, string path, long totalMs)
    {
        if (channel < 0 || channel >= SnakeSoundChannelCount)
            return;
        lock (snakeAudioLock)
        {
            var state = snakeSounds[channel];
            if (!string.Equals(state.Path, path, StringComparison.OrdinalIgnoreCase))
                return;
            state.PendingStart = false;
            state.Playing = true;
            state.Paused = false;
            state.StartedAtMs = GetTickMs();
            state.TotalMs = Math.Max(0, totalMs);
            state.LastKnownCurrentMs = 0;
        }
    }

    public static void NotifySoundPlaybackFailed(int channel, string path)
    {
        if (channel < 0 || channel >= SnakeSoundChannelCount)
            return;
        lock (snakeAudioLock)
        {
            var state = snakeSounds[channel];
            if (!string.Equals(state.Path, path, StringComparison.OrdinalIgnoreCase))
                return;
            state.Playing = false;
            state.PendingStart = false;
            state.Paused = false;
            state.Repeat = 1;
            state.LastKnownCurrentMs = 0;
        }
    }

    public static void NotifySoundPlaybackRepeated(int channel)
    {
        if (channel < 0 || channel >= SnakeSoundChannelCount)
            return;
        lock (snakeAudioLock)
        {
            var state = snakeSounds[channel];
            if (!state.Playing || state.PendingStart)
                return;
            if (state.Repeat > 1)
                state.Repeat--;
            state.Paused = false;
            state.StartedAtMs = GetTickMs();
            state.LastKnownCurrentMs = 0;
        }
    }

    public static void NotifySoundPlaybackPosition(int channel, double currentSec, double totalSec, bool playing)
    {
        if (channel < 0 || channel >= SnakeSoundChannelCount)
            return;
        lock (snakeAudioLock)
        {
            var state = snakeSounds[channel];
            state.LastKnownCurrentMs = Math.Max(0, (long)(currentSec * 1000.0));
            if (totalSec > 0)
                state.TotalMs = (long)(totalSec * 1000.0);
            if (!state.PendingStart)
                state.Playing = playing;
            if (!playing && !state.PendingStart && !state.Paused)
                state.LastKnownCurrentMs = ClampAudioPositionMs(state.LastKnownCurrentMs, state.TotalMs);
            if (playing)
                state.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(state.LastKnownCurrentMs, state.Speed);
        }
    }

    public static void NotifySoundPlaybackFinished(int channel)
    {
        if (channel < 0 || channel >= SnakeSoundChannelCount)
            return;
        lock (snakeAudioLock)
        {
            var state = snakeSounds[channel];
            state.Playing = false;
            state.PendingStart = false;
            state.Paused = false;
            state.Repeat = 1;
            state.LastKnownCurrentMs = 0;
        }
    }

    public static void NotifyBgmPlaybackStarted(string path, long totalMs)
    {
        lock (snakeAudioLock)
        {
            if (!string.Equals(snakeBgm.Path, path, StringComparison.OrdinalIgnoreCase))
                return;
            snakeBgm.PendingStart = false;
            snakeBgm.Playing = true;
            snakeBgm.Paused = false;
            snakeBgm.StartedAtMs = GetTickMs();
            snakeBgm.TotalMs = Math.Max(0, totalMs);
            snakeBgm.LastKnownCurrentMs = 0;
        }
    }

    public static void NotifyBgmPlaybackFailed(string path)
    {
        lock (snakeAudioLock)
        {
            if (!string.Equals(snakeBgm.Path, path, StringComparison.OrdinalIgnoreCase))
                return;
            snakeBgm.Playing = false;
            snakeBgm.PendingStart = false;
            snakeBgm.Paused = false;
            snakeBgm.Repeat = 1;
            snakeBgm.LastKnownCurrentMs = 0;
        }
    }

    public static void NotifyBgmPlaybackPosition(double currentSec, double totalSec, bool playing)
    {
        lock (snakeAudioLock)
        {
            snakeBgm.LastKnownCurrentMs = Math.Max(0, (long)(currentSec * 1000.0));
            if (totalSec > 0)
                snakeBgm.TotalMs = (long)(totalSec * 1000.0);
            if (!snakeBgm.PendingStart)
                snakeBgm.Playing = playing;
            if (!playing && !snakeBgm.PendingStart && !snakeBgm.Paused)
                snakeBgm.LastKnownCurrentMs = ClampAudioPositionMs(snakeBgm.LastKnownCurrentMs, snakeBgm.TotalMs);
            if (playing)
                snakeBgm.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(snakeBgm.LastKnownCurrentMs, snakeBgm.Speed);
        }
    }

    static long GetCurrentAudioMs(SnakeAudioState state)
    {
        if (state == null || !state.Playing)
            return 0;
        if (state.PendingStart || state.StartedAtMs <= 0 || state.Paused)
            return Math.Max(0, state.LastKnownCurrentMs);
        long elapsedMs = Math.Max(0, GetTickMs() - state.StartedAtMs);
        long currentMs = elapsedMs * Math.Max(1, state.Speed) / 100;
        if (state.Repeat < 0 && state.TotalMs > 0)
            return currentMs % state.TotalMs;
        return ClampAudioPositionMs(currentMs, state.TotalMs);
    }

    static long ScaleFromPlaybackMs(long playbackMs, int speed)
    {
        return playbackMs * 100 / Math.Max(1, speed);
    }

    static int ClampSnakeAudioSpeed(int speed)
    {
        if (speed < 10)
            return 10;
        if (speed > 1000)
            return 1000;
        return speed;
    }

    static long ClampAudioPositionMs(long currentMs, long totalMs)
    {
        currentMs = Math.Max(0, currentMs);
        if (totalMs <= 0)
            return currentMs;
        return Math.Min(currentMs, totalMs);
    }

    static void FlushCompletedAudioStatesLocked()
    {
        foreach (var state in snakeSounds)
            FlushCompletedAudioStateLocked(state);
        FlushCompletedAudioStateLocked(snakeBgm);
    }

    static void FlushCompletedAudioStateLocked(SnakeAudioState state)
    {
        // Godot 的 Finished 信号偶尔会和脚本查询错帧；这里用总时长兜底，避免一次性音效结束后通道长期占用。
        if (state == null || !state.Playing || state.PendingStart || state.Paused || state.Repeat < 0 || state.Repeat > 1 || state.TotalMs <= 0)
            return;
        if (GetCurrentAudioMs(state) < state.TotalMs)
            return;
        state.Playing = false;
        state.PendingStart = false;
        state.Paused = false;
        state.Repeat = 1;
        state.LastKnownCurrentMs = 0;
    }

    public static string ResolveSoundPath(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        if (System.IO.Path.IsPathRooted(name))
            return name;
        string exeDir = MinorShift.Emuera.Program.ExeDir ?? "";
        string[] candidates =
        {
            System.IO.Path.Combine(exeDir, "sound", name),
            System.IO.Path.Combine(exeDir, "Sound", name),
            System.IO.Path.Combine(exeDir, name),
            System.IO.Path.Combine("sound", name),
            name
        };
        foreach (string candidate in candidates)
        {
            string resolved = uEmuera.Utils.ResolveExistingFilePath(candidate);
            if (!string.IsNullOrEmpty(resolved) && uEmuera.Utils.FileExists(resolved))
                return resolved;
        }
        string soundDir = System.IO.Path.Combine(exeDir, "sound");
        if (uEmuera.Utils.DirectoryExists(soundDir))
        {
            string cacheKey = BuildSoundFallbackResolveCacheKey(soundDir, name);
            if (TryGetSoundFallbackResolveCache(cacheKey, out string cached))
            {
                if (!string.IsNullOrEmpty(cached) && uEmuera.Utils.FileExists(cached))
                    return cached;
                return System.IO.Path.GetFullPath(candidates[0]);
            }

            string found = uEmuera.Utils.FindFileRecursive(soundDir, name);
            if (!string.IsNullOrEmpty(found) && uEmuera.Utils.FileExists(found))
            {
                SetSoundFallbackResolveCache(cacheKey, found);
                return found;
            }
            found = FindSimilarSoundFile(soundDir, name);
            if (!string.IsNullOrEmpty(found) && uEmuera.Utils.FileExists(found))
            {
                SetSoundFallbackResolveCache(cacheKey, found);
                Info(EmueraLogCategory.Audio, () => $"[AUDIO] Resolved similar sound \"{name}\" -> \"{found}\"");
                return found;
            }
            SetSoundFallbackResolveCache(cacheKey, "");
        }
        return System.IO.Path.GetFullPath(candidates[0]);
    }

    static string BuildSoundFallbackResolveCacheKey(string soundDir, string requestedName)
    {
        string root = "";
        try
        {
            root = System.IO.Path.GetFullPath(soundDir ?? "");
        }
        catch
        {
            root = soundDir ?? "";
        }
        return root + "\n" + (requestedName ?? "");
    }

    static bool TryGetSoundFallbackResolveCache(string key, out string resolved)
    {
        lock (soundFallbackResolveCacheLock)
            return soundFallbackResolveCache.TryGetValue(key, out resolved);
    }

    static void SetSoundFallbackResolveCache(string key, string resolved)
    {
        // fallback 只处理直接候选路径未命中的音频。缓存未命中结果可以避免脚本循环播放缺失音效时
        // 反复递归扫描 Android 外部存储；直接候选路径仍会在缓存前检查，因此新增同名文件后仍可命中。
        lock (soundFallbackResolveCacheLock)
            soundFallbackResolveCache[key] = resolved ?? "";
    }

    static string FindSimilarSoundFile(string soundDir, string requestedName)
    {
        if (string.IsNullOrEmpty(soundDir) || string.IsNullOrEmpty(requestedName))
            return null;
        string requestedBase = System.IO.Path.GetFileNameWithoutExtension(requestedName);
        string requestedExt = System.IO.Path.GetExtension(requestedName);
        if (string.IsNullOrEmpty(requestedBase))
            return null;

        string best = null;
        int bestScore = 0;
        try
        {
            foreach (string file in System.IO.Directory.EnumerateFiles(soundDir, "*", System.IO.SearchOption.AllDirectories))
            {
                string ext = System.IO.Path.GetExtension(file);
                if (!IsPlayableAudioExtension(ext))
                    continue;
                if (!string.IsNullOrEmpty(requestedExt) && !string.Equals(requestedExt, ext, StringComparison.OrdinalIgnoreCase))
                    continue;
                string candidateBase = System.IO.Path.GetFileNameWithoutExtension(file);
                int score = SharedCjkCharScore(requestedBase, candidateBase);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = file;
                }
            }
        }
        catch
        {
            return null;
        }
        return bestScore >= 3 ? best : null;
    }

    static bool IsPlayableAudioExtension(string ext)
    {
        return string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".ogg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".mp3", StringComparison.OrdinalIgnoreCase);
    }

    static int SharedCjkCharScore(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return 0;
        var seen = new HashSet<char>();
        int score = 0;
        foreach (char c in left)
        {
            if (!IsCjk(c) || !seen.Add(c))
                continue;
            if (right.IndexOf(c) >= 0)
                score++;
        }
        return score;
    }

    static bool IsCjk(char c)
    {
        return (c >= 0x3400 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF);
    }

    public static int ClampEraVolume(int volume)
    {
        if (volume < 0)
            return 0;
        if (volume > 100)
            return 100;
        return volume;
    }

}
