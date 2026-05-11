using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Godot;
using MinorShift.Emuera.GameView;

internal static class GenericUtils
{
    static readonly ConcurrentQueue<(int Level, string Message)> logQueue = new ConcurrentQueue<(int, string)>();
    static readonly ConcurrentQueue<Action> uiQueue = new ConcurrentQueue<Action>();
    static int mainThreadId = -1;

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
        while (logQueue.TryDequeue(out var item))
            WriteLog(item.Level, item.Message);
    }

    public static void FlushUI()
    {
        int count = 0;
        while (uiQueue.TryDequeue(out var action) && count < 200)
        {
            action();
            count++;
        }
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
        var lines = new List<byte[]>();
        int start = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0x0A)
            {
                int len = i - start;
                if (len > 0 && bytes[i - 1] == 0x0D)
                    len--;
                if (len > 0)
                {
                    var lineBytes = new byte[len];
                    Array.Copy(bytes, start, lineBytes, 0, len);
                    lines.Add(lineBytes);
                }
                start = i + 1;
            }
        }
        if (start < bytes.Length)
        {
            var lineBytes = new byte[bytes.Length - start];
            Array.Copy(bytes, start, lineBytes, 0, bytes.Length - start);
            lines.Add(lineBytes);
        }

        using(var md5 = MD5.Create())
        {
            foreach(var lineBytes in lines)
            {
                if (lineBytes.Length == 0)
                    continue;
                bool allWhite = true;
                for (int i = 0; i < lineBytes.Length; i++)
                {
                    if (lineBytes[i] != (byte)' ' && lineBytes[i] != (byte)'\t')
                    {
                        allWhite = false;
                        break;
                    }
                }
                if (allWhite)
                    continue;
                var hash = md5.ComputeHash(lineBytes);
                var sb = new StringBuilder();
                for(int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                result.Add(sb.ToString());
            }
        }
        return result;
    }

    // Shim bridge methods — connected to EmueraContent
    // All UI operations are queued when called from background thread
    public static void SetBackgroundColor(uEmuera.Drawing.Color color)
    {
        if (IsMainThread())
            EmueraContent.instance?.SetBackgroundColor(color);
        else
            uiQueue.Enqueue(() => EmueraContent.instance?.SetBackgroundColor(color));
    }

    public static void ClearText()
    {
        if (IsMainThread())
            EmueraContent.instance?.Clear();
        else
            uiQueue.Enqueue(() => EmueraContent.instance?.Clear());
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
        if (IsMainThread())
            EmueraContent.instance?.RemoveBottomLines(count);
        else
            uiQueue.Enqueue(() => EmueraContent.instance?.RemoveBottomLines(count));
    }

    public static void AddText(ConsoleDisplayLine line, bool update)
    {
        if (IsMainThread())
            EmueraContent.instance?.AddLine(line, update);
        else
            uiQueue.Enqueue(() => EmueraContent.instance?.AddLine(line, update));
    }

    public static void SetLastButtonGeneration(int generation)
    {
        if (IsMainThread())
            EmueraContent.instance?.SetLastButtonGeneration(generation);
        else
            uiQueue.Enqueue(() => EmueraContent.instance?.SetLastButtonGeneration(generation));
    }

    public static void TextUpdate()
    {
        if (IsMainThread())
            EmueraContent.instance?.UpdateDisplay();
        else
            uiQueue.Enqueue(() => EmueraContent.instance?.UpdateDisplay());
    }

    public static void ShowIsInProcess(bool show)
    {
        if (IsMainThread())
            EmueraContent.instance?.ShowIsInProcess(show);
        else
            uiQueue.Enqueue(() => EmueraContent.instance?.ShowIsInProcess(show));
    }

    public static void RefreshCBG(EmueraConsole console)
    {
        if (console == null)
            return;
        if (IsMainThread())
            EmueraContent.instance?.RefreshCBG(console.GetCBGList());
        else
        {
            var cbgList = console.GetCBGList();
            uiQueue.Enqueue(() => EmueraContent.instance?.RefreshCBG(cbgList));
        }
    }
}
