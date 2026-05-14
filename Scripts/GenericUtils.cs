using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Godot;
using MinorShift.Emuera.GameView;

internal static class GenericUtils
{
    static readonly ConcurrentQueue<(int Level, string Message)> logQueue = new ConcurrentQueue<(int, string)>();
    static readonly ConcurrentQueue<Action> uiQueue = new ConcurrentQueue<Action>();
    static int mainThreadId = -1;
    static int pendingUiActions = 0;
    static int pendingDisplayActions = 0;
    const ulong AndroidUiBudgetUsec = 4000;
    const ulong DesktopUiBudgetUsec = 7000;
    const int AndroidMaxUiActionsPerFrame = 32;
    const int DesktopMaxUiActionsPerFrame = 96;
    const int SnakeSoundChannelCount = 10;
    static readonly object snakeAudioLock = new object();
    static readonly SnakeAudioState[] snakeSounds = CreateSnakeAudioStates();
    static readonly SnakeAudioState snakeBgm = new SnakeAudioState();

    public static bool HasPendingUIWork => Volatile.Read(ref pendingUiActions) > 0;
    public static bool HasPendingDisplayWork => Volatile.Read(ref pendingDisplayActions) > 0;

    sealed class SnakeAudioState
    {
        public string Path;
        public bool Playing;
        public bool Paused;
        public int Volume = 100;
        public int Speed = 100;
        public long StartedAtMs;
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

    public static void SetMainThread()
    {
        mainThreadId = System.Environment.CurrentManagedThreadId;
    }

    static bool IsMainThread()
    {
        return mainThreadId < 0 || System.Environment.CurrentManagedThreadId == mainThreadId;
    }

    public static void FlushLogs()
    {
        int maxLogs = OS.IsDebugBuild() ? 64 : 16;
        int count = 0;
        while (count < maxLogs && logQueue.TryDequeue(out var item))
        {
            WriteLog(item.Level, item.Message);
            count++;
        }
    }

    public static void FlushUI()
    {
        int maxActions = OS.GetName() == "Android" ? AndroidMaxUiActionsPerFrame : DesktopMaxUiActionsPerFrame;
        ulong budgetUsec = OS.GetName() == "Android" ? AndroidUiBudgetUsec : DesktopUiBudgetUsec;
        ulong startUsec = Time.GetTicksUsec();
        int count = 0;
        while (count < maxActions && uiQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                GD.PushError($"[UI Queue] {ex}");
            }
            count++;
            if (Time.GetTicksUsec() - startUsec >= budgetUsec)
                break;
        }
    }

    static void EnqueueUI(Action action, bool displayWork = false)
    {
        Interlocked.Increment(ref pendingUiActions);
        if (displayWork)
            Interlocked.Increment(ref pendingDisplayActions);

        uiQueue.Enqueue(() =>
        {
            try
            {
                action();
            }
            finally
            {
                if (displayWork)
                    Interlocked.Decrement(ref pendingDisplayActions);
                Interlocked.Decrement(ref pendingUiActions);
            }
        });
    }

    public static void Info(object content)
    {
        Log(0, content);
    }
    public static void Warn(object content)
    {
        Log(1, content);
    }
    public static void Error(object content)
    {
        Log(2, content);
    }

    static void Log(int level, object content)
    {
        if (level == 0 && !OS.IsDebugBuild())
            return;

        var message = content?.ToString();
        if (IsMainThread())
            WriteLog(level, message);
        else
            logQueue.Enqueue((level, message));
    }

    static void WriteLog(int level, string message)
    {
        switch (level)
        {
            case 1:
                GD.PushWarning(message ?? "");
                break;
            case 2:
                GD.PushError(message ?? "");
                break;
            default:
                GD.Print(message ?? "");
                break;
        }
    }

    public static List<string> CalcMd5List(byte[] bytes)
    {
        return CalcMd5ListForConfig(bytes);
    }

    public static List<string> CalcMd5ListForConfig(byte[] bytes)
    {
        var result = new List<string>();
        int start = 0;

        using(var md5 = MD5.Create())
        {
            for (int i = 0; i <= bytes.Length; i++)
            {
                if (i < bytes.Length && bytes[i] != 0x0A)
                    continue;

                int len = i - start;
                if (len > 0 && bytes[start + len - 1] == 0x0D)
                    len--;

                if (len > 0 && !IsWhiteSpaceBytes(bytes, start, len))
                {
                    var hash = md5.ComputeHash(bytes, start, len);
                    var sb = new StringBuilder();
                    for(int h = 0; h < hash.Length; h++)
                        sb.Append(hash[h].ToString("x2"));
                    result.Add(sb.ToString());
                }

                start = i + 1;
            }
        }

        return result;
    }

    static bool IsWhiteSpaceBytes(byte[] bytes, int start, int len)
    {
        for (int i = 0; i < len; i++)
        {
            byte b = bytes[start + i];
            if (b != (byte)' ' && b != (byte)'\t')
            {
                return false;
            }
        }
        return true;
    }

    // Shim bridge methods — connected to EmueraContent
    // All UI operations are queued when called from background thread
    public static void SetBackgroundColor(uEmuera.Drawing.Color color)
    {
        if (IsMainThread())
            EmueraContent.instance?.SetBackgroundColor(color);
        else
            EnqueueUI(() => EmueraContent.instance?.SetBackgroundColor(color));
    }

    public static void ClearText()
    {
        EnqueueUI(() => EmueraContent.instance?.Clear(), true);
    }

    public static int GetTextMaxLineNo()
    {
        return EmueraContent.instance?.GetMaxLineNo() ?? 0;
    }

    public static int GetTextMinLineNo()
    {
        return EmueraContent.instance?.GetMinLineNo() ?? 0;
    }

    public static ConsoleDisplayLine GetText(int lineno)
    {
        return EmueraContent.instance?.GetLine(lineno);
    }

    public static void RemoveTextCount(int count)
    {
        EnqueueUI(() => EmueraContent.instance?.RemoveBottomLines(count), true);
    }

    public static void AddText(ConsoleDisplayLine line, bool update)
    {
        EnqueueUI(() => EmueraContent.instance?.AddLine(line, update), true);
    }

    public static void SetLastButtonGeneration(int generation)
    {
        EnqueueUI(() => EmueraContent.instance?.SetLastButtonGeneration(generation), true);
    }

    public static void TextUpdate()
    {
        EnqueueUI(() => EmueraContent.instance?.UpdateDisplay(), true);
    }

    public static void ShowIsInProcess(bool show)
    {
        EnqueueUI(() => EmueraContent.instance?.ShowIsInProcess(show));
    }

    public static void RefreshCBG(EmueraConsole console)
    {
        if (console == null)
            return;
        var cbgList = console.GetCBGList();
        EnqueueUI(() => EmueraContent.instance?.RefreshCBG(cbgList), true);
    }

    public static void PlaySoundFile(string path, bool loop)
    {
        lock (snakeAudioLock)
        {
            int channel = 0;
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
            state.Paused = false;
            state.StartedAtMs = GetTickMs();
        }
        EnqueueUI(() => EmueraContent.instance?.PlaySoundFile(path, loop));
    }

    public static void StopSounds()
    {
        lock (snakeAudioLock)
        {
            foreach (var state in snakeSounds)
            {
                state.Playing = false;
                state.Paused = false;
            }
        }
        EnqueueUI(() => EmueraContent.instance?.StopSounds());
    }

    public static void PlayBgmFile(string path)
    {
        lock (snakeAudioLock)
        {
            snakeBgm.Path = path;
            snakeBgm.Playing = true;
            snakeBgm.Paused = false;
            snakeBgm.StartedAtMs = GetTickMs();
        }
        EnqueueUI(() => EmueraContent.instance?.PlayBgmFile(path));
    }

    public static void StopBgm()
    {
        lock (snakeAudioLock)
        {
            snakeBgm.Playing = false;
            snakeBgm.Paused = false;
        }
        EnqueueUI(() => EmueraContent.instance?.StopBgm());
    }

    public static void SetSoundVolume(int volume)
    {
        lock (snakeAudioLock)
        {
            foreach (var state in snakeSounds)
                state.Volume = ClampEraVolume(volume);
        }
        EnqueueUI(() => EmueraContent.instance?.SetSoundVolume(volume));
    }

    public static void SetBgmVolume(int volume)
    {
        lock (snakeAudioLock)
            snakeBgm.Volume = ClampEraVolume(volume);
        EnqueueUI(() => EmueraContent.instance?.SetBgmVolume(volume));
    }

    public static bool SoundFileExists(string name)
    {
        string path = ResolveSoundPath(name);
        return !string.IsNullOrEmpty(path) && System.IO.File.Exists(path);
    }

    public static int FindPlayingSound(int channel)
    {
        lock (snakeAudioLock)
        {
            if (channel >= 0)
                return channel < snakeSounds.Length && snakeSounds[channel].Playing && !snakeSounds[channel].Paused ? channel : -1;
            for (int i = 0; i < snakeSounds.Length; i++)
            {
                if (snakeSounds[i].Playing && !snakeSounds[i].Paused)
                    return i;
            }
        }
        return -1;
    }

    public static bool IsPlayingBgm()
    {
        lock (snakeAudioLock)
            return snakeBgm.Playing && !snakeBgm.Paused;
    }

    public static int ControlSound(int channel, int action, int speed = 100)
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
                    EnqueueUI(() => EmueraContent.instance?.PauseSoundChannel(channel, true));
                    return 1;
                case 1:
                    state.Paused = false;
                    if (!string.IsNullOrEmpty(state.Path))
                        state.Playing = true;
                    EnqueueUI(() => EmueraContent.instance?.PauseSoundChannel(channel, false));
                    return 1;
                case 2:
                    state.Playing = false;
                    state.Paused = false;
                    EnqueueUI(() => EmueraContent.instance?.StopSoundChannel(channel));
                    return 1;
                case 3:
                    state.Speed = Math.Max(1, speed);
                    EnqueueUI(() => EmueraContent.instance?.SetSoundChannelSpeed(channel, state.Speed / 100.0f));
                    return 1;
                default:
                    return -2;
            }
        }
    }

    public static int ControlBgm(int action, int speed = 100)
    {
        lock (snakeAudioLock)
        {
            switch (action)
            {
                case 0:
                    snakeBgm.Paused = true;
                    EnqueueUI(() => EmueraContent.instance?.PauseBgm(true));
                    return 1;
                case 1:
                    snakeBgm.Paused = false;
                    if (!string.IsNullOrEmpty(snakeBgm.Path))
                        snakeBgm.Playing = true;
                    EnqueueUI(() => EmueraContent.instance?.PauseBgm(false));
                    return 1;
                case 2:
                    snakeBgm.Playing = false;
                    snakeBgm.Paused = false;
                    EnqueueUI(() => EmueraContent.instance?.StopBgm());
                    return 1;
                case 3:
                    snakeBgm.Speed = Math.Max(1, speed);
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
            SnakeAudioState state = channel == -1 ? snakeBgm : channel >= 0 && channel < snakeSounds.Length ? snakeSounds[channel] : null;
            if (state == null)
                return default;
            return new SnakeAudioInfo
            {
                TotalMs = 0,
                CurrentMs = state.Playing && !state.Paused ? Math.Max(0, GetTickMs() - state.StartedAtMs) : 0,
                Playing = state.Playing && !state.Paused ? 1 : 0,
                Volume = state.Volume,
                Speed = state.Speed
            };
        }
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
            if (!string.IsNullOrEmpty(resolved) && System.IO.File.Exists(resolved))
                return resolved;
        }
        return System.IO.Path.GetFullPath(candidates[0]);
    }

    static int ClampEraVolume(int volume)
    {
        if (volume < 0)
            return 0;
        if (volume > 100)
            return 100;
        return volume;
    }

    static long GetTickMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    internal static void SetHtmlIsland(MinorShift.Emuera.GameView.ConsoleDisplayLine[] lines)
    {
        EnqueueUI(() => EmueraContent.instance?.SetHtmlIsland(lines));
    }

    internal static void ClearHtmlIsland()
    {
        EnqueueUI(() => EmueraContent.instance?.ClearHtmlIsland());
    }

    public static void RestartGame()
    {
        EnqueueUI(() => EmueraContent.instance?.RequestRestartFromErb());
    }
}
