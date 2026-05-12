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

    public static bool HasPendingUIWork => Volatile.Read(ref pendingUiActions) > 0;
    public static bool HasPendingDisplayWork => Volatile.Read(ref pendingDisplayActions) > 0;

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
}
