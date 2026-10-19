using System;
using System.Collections.Generic;
using MinorShift._Library;
using uEmuera.Drawing;

namespace uEmuera.Forms
{
    public enum FormWindowState
    {
        Normal = 0,
        Minimized = 1,
        Maximized = 2
    }

    public class Timer : IDisposable
    {
        /// <summary>
        /// 返回工作线程下一次检查兼容定时器前最多可等待的毫秒数。
        /// 没有活动定时器时保留调用方给出的低频等待，避免普通 INPUT 空转；
        /// 有 TINPUT/动画定时器时则按最近到期时间唤醒，不能被输入轮询周期限制精度。
        /// </summary>
        public static int GetNextWaitMilliseconds(int fallbackMilliseconds)
        {
            int waitMilliseconds = Math.Max(0, fallbackMilliseconds);
            uint currTick = WinmmTimer.TickCount;
            var iter = timers.GetEnumerator();
            while (iter.MoveNext())
            {
                var timer = iter.Current;
                if (!timer.Enabled)
                    continue;

                int interval = Math.Max(1, timer.Interval);
                uint elapsed = currTick - timer.last_tick;
                if (elapsed >= interval)
                    return 0;
                waitMilliseconds = Math.Min(waitMilliseconds, interval - (int)elapsed);
            }
            return waitMilliseconds;
        }

        public static void Update()
        {
            // 先拍快照再遍历：Tick 回调可能同步 Dispose 定时器（如 DebugDialog 关闭时
            // refreshTimer.Dispose → timers.Remove），直接在 HashSet 枚举中增删元素
            // 会使版本号失效，下一次 MoveNext 抛 InvalidOperationException。
            var curr_tick = WinmmTimer.TickCount;
            var snapshot = new Timer[timers.Count];
            timers.CopyTo(snapshot);
            for (int i = 0; i < snapshot.Length; i++)
            {
                var timer = snapshot[i];
                if(curr_tick - timer.last_tick < timer.Interval)
                    continue;
                timer.last_tick = curr_tick;

                if(!timer.Enabled)
                    continue;
                timer.Tick(timer, EventArgs.Empty);
            }
        }
        static HashSet<Timer> timers = new HashSet<Timer>();

        public Timer()
        {
            timers.Add(this);
        }

        volatile bool enabled;
        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (value && !enabled)
                    last_tick = WinmmTimer.TickCount;
                enabled = value;
            }
        }
        public int Interval { get; set; }
        public object Tag { get; set; }

        public event EventHandler Tick;
        public volatile uint last_tick = 0;

        public void Start()
        {}
        public void Stop()
        {}
        public void Dispose()
        {
            timers.Remove(this);
        }

        /// <summary>
        /// Timers are compatibility objects created by the active ERB
        /// session.  They have no process-wide meaning and must not tick after
        /// a canary switch has stopped the legacy worker.
        /// </summary>
        internal static void ResetSessionState()
        {
            timers.Clear();
        }
    }

    public enum TextFormatFlags
    {
        Default = 0,
        Left = 0,
        Top = 0,
        GlyphOverhangPadding = 0,
        HorizontalCenter = 1,
        Right = 2,
        VerticalCenter = 4,
        Bottom = 8,
        WordBreak = 16,
        SingleLine = 32,
        ExpandTabs = 64,
        NoClipping = 256,
        ExternalLeading = 512,
        NoPrefix = 2048,
        Internal = 4096,
        TextBoxControl = 8192,
        PathEllipsis = 16384,
        EndEllipsis = 32768,
        ModifyString = 65536,
        RightToLeft = 131072,
        WordEllipsis = 262144,
        NoFullWidthCharacterBreak = 524288,
        HidePrefix = 1048576,
        PrefixOnly = 2097152,
        PreserveGraphicsClipping = 16777216,
        PreserveGraphicsTranslateTransform = 33554432,
        NoPadding = 268435456,
        LeftAndRightPadding = 536870912
    }

    public static class TextRenderer
    {
        public static void DrawText(uEmuera.Drawing.Graphics graph, string Str, uEmuera.Drawing.Font font,
                            uEmuera.Drawing.Point pt, uEmuera.Drawing.Color color, TextFormatFlags flags)
        { }
    }

    public enum DialogResult
    {
        None = 0,
        OK = 1,
        Cancel = 2,
        Abort = 3,
        Retry = 4,
        Ignore = 5,
        Yes = 6,
        No = 7
    }

    public enum MessageBoxButtons
    {
        OK = 0,
        OKCancel = 1,
        AbortRetryIgnore = 2,
        YesNoCancel = 3,
        YesNo = 4,
        RetryCancel = 5
    }

    public static class MessageBox
    {
        public static DialogResult Show(string text)
        {
            return Show(text, "提示");
        }
        public static DialogResult Show(string text, string caption)
        {
            return Show(text, caption, MessageBoxButtons.OK);
        }
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
        {
            //todo
            uEmuera.Logger.Info(text);
            return DialogResult.None;
        }
    }

    public class ScrollBar
    {
        public int Value { get; set; }
        public int Maximum { get; set; }
        public int Minimum { get; set; }
        public bool Enabled { get; set; }
    }

    public static class Control
    {
        public static Point MousePosition { get; set; }
    }

    public sealed class PictureBox
    {
        public Point PointToClient(object mousePosition)
        {
            if (mousePosition is Point point)
                return point;
            return Point.Empty;
        }
        public Rectangle ClientRectangle;
        public int Width { get { return ClientRectangle.Width; } }
        public int Height { get { return ClientRectangle.Height; } }
    }

    public sealed class ToolTip
    {
        internal void RemoveAll()
        {
        }

        internal void SetToolTip(PictureBox mainPicBox, string title)
        {
        }

        public uEmuera.Drawing.Color ForeColor;
        public uEmuera.Drawing.Color BackColor;
        public int InitialDelay = 0;
    }

    public sealed class TextBox
    {
        public string Text { get; set; }
        public uEmuera.Drawing.Color ForeColor;
        public uEmuera.Drawing.Color BackColor;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public bool UseCustomPosition { get; set; }
    }
}
