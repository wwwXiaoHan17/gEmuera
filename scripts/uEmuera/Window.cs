using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using uEmuera.Forms;
using uEmuera.Drawing;
using MinorShift.Emuera;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.Sub;
using MinorShift._Library;
using System.Threading;
using gEmuera.GodotHost;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace uEmuera.Window
{
    /// <summary>
    /// Emuera DEBUG 模式调试窗口的引擎侧门面（参考 v24 DebugDialog 移植）。
    /// 本对象全部生命周期运行在 Emuera worker 线程（OpenDebugDialog / @DEBUG 指令）：
    /// - 引擎态访问（DebugConsoleLog、GetDebugTraceLog、watch 表达式求值、DebugCommand）
    ///   一律在 worker 侧完成，通过 EmueraDebugSnapshot 推送给主线程面板；
    /// - 主线程面板只能调用 EnqueueCommand / EnqueueWatchList / EnqueueCloseRequest
    ///   （纯队列投递，由 worker 定时器在引擎空闲边界消费）。
    /// 打开时注册 worker 侧定时器（uEmuera.Forms.Timer，由 EmueraThread 空闲循环驱动），
    /// 每 200ms 在引擎 INPUT/WAIT 边界做一次命令执行 + watch 求值 + 快照推送。
    /// 关闭时保存 debug\watchlist.csv 与 debug\console.log 到 Program.DebugDir。
    /// </summary>
    public class DebugDialog : IDisposable
    {
        const int RefreshIntervalMilliseconds = 200;
        const string WatchListFileName = "watchlist.csv";
        const string ConsoleLogFileName = "console.log";

        EmueraConsole console;
        Process emuera;
        volatile bool created;
        readonly List<string> watchExpressions = new List<string>();
        readonly ConcurrentQueue<string> pendingCommands = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<string[]> pendingWatchList = new ConcurrentQueue<string[]>();
        readonly ConcurrentQueue<bool> pendingCloseRequests = new ConcurrentQueue<bool>();
        uEmuera.Forms.Timer refreshTimer;

        string WatchFilePath { get { return Program.DebugDir + WatchListFileName; } }
        string ConsoleFilePath { get { return Program.DebugDir + ConsoleLogFileName; } }

        public bool Created { get { return created; } }

        internal void SetParent(EmueraConsole emueraConsole, Process emuera)
        {
            console = emueraConsole;
            this.emuera = emuera;
        }

        internal void Show()
        {
            if (created)
                return;
            created = true;
            LoadWatchListFromDisk();
            refreshTimer = new uEmuera.Forms.Timer
            {
                Interval = RefreshIntervalMilliseconds,
                Enabled = true,
            };
            refreshTimer.Tick += OnRefreshTick;
            EmueraDebugDialogPanel.ShowPanel(console, this);
        }

        internal void Focus()
        {
            if (!created)
                return;
            EmueraDebugDialogPanel.FocusPanel();
        }

        public void Close()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!created)
                return;
            created = false;
            if (refreshTimer != null)
            {
                refreshTimer.Enabled = false;
                refreshTimer.Tick -= OnRefreshTick;
                refreshTimer.Dispose();
                refreshTimer = null;
            }
            // 面板在主线程可能已投递 watch 列表更新（✕ 关闭时），先消费再落盘。
            while (pendingWatchList.TryDequeue(out string[] list))
                ApplyWatchList(list);
            SaveWatchListToDisk();
            SaveConsoleLogToDisk();
            EmueraDebugDialogPanel.HidePanel();
        }

        /// <summary>主线程面板 → worker：调试控制台命令（回车触发）。</summary>
        public void EnqueueCommand(string text)
        {
            if (!string.IsNullOrEmpty(text))
                pendingCommands.Enqueue(text);
        }

        /// <summary>主线程面板 → worker：watch 表达式整表替换。</summary>
        public void EnqueueWatchList(string[] expressions)
        {
            if (expressions == null)
                return;
            pendingWatchList.Enqueue(expressions);
        }

        /// <summary>主线程面板 → worker：请求关闭（✕ 按钮，Dispose 仅在 worker 线程执行）。</summary>
        public void EnqueueCloseRequest()
        {
            pendingCloseRequests.Enqueue(true);
        }

        void OnRefreshTick(object sender, EventArgs e)
        {
            if (!created || console == null)
                return;
            // 会话边界自愈：console 已随 GlobalStatic.Reset 解绑或 DEBUG 模式已关闭时
            // 自行释放，避免定时器泄漏到下一个会话。
            if (!Program.DebugMode || !ReferenceEquals(GlobalStatic.Console, console))
            {
                Dispose();
                return;
            }

            while (pendingCloseRequests.TryDequeue(out _))
            {
                Dispose();
                return;
            }
            while (pendingCommands.TryDequeue(out string command))
                ExecuteDebugCommand(command);
            while (pendingWatchList.TryDequeue(out string[] list))
                ApplyWatchList(list);

            EmueraDebugDialogPanel.PushSnapshot(BuildSnapshot());
        }

        void ExecuteDebugCommand(string command)
        {
            if (console == null || console.IsInProcess)
                return; // 参考实现要求引擎空闲才执行；定时器仅在 INPUT/WAIT 边界触发
            console.DebugPrint(command);
            console.DebugNewLine();
            console.DebugCommand(command, false, true);
        }

        void ApplyWatchList(string[] expressions)
        {
            watchExpressions.Clear();
            for (int i = 0; i < expressions.Length; i++)
            {
                if (!string.IsNullOrEmpty(expressions[i]))
                    watchExpressions.Add(expressions[i]);
            }
        }

        EmueraDebugSnapshot BuildSnapshot()
        {
            string consoleLog = console.DebugConsoleLog;
            string traceLog = console.GetDebugTraceLog(false) ?? "";
            string[] expressions = watchExpressions.ToArray();
            string[] values = new string[expressions.Length];
            if (expressions.Length > 0)
                EvaluateWatches(expressions, values);
            return new EmueraDebugSnapshot(consoleLog, traceLog, expressions, values);
        }

        // 参考 v24 DebugDialog.updateVarWatch + getValueString 移植：
        // saveCurrentState(false) → 逐项 LexicalAnalyzer.Analyse +
        // ExpressionParser.ReduceExpressionTerm + term.GetValue(EMediator)
        // （RunERBFromMemory=true 包裹）→ finally clearMethodStack()+loadPrevState()。
        // 只读求值；改值请走调试控制台命令框（@SET）。
        void EvaluateWatches(string[] expressions, string[] values)
        {
            if (emuera == null || GlobalStatic.EMediator == null)
                return;
            GlobalStatic.Process.saveCurrentState(false);
            try
            {
                for (int i = 0; i < expressions.Length; i++)
                    values[i] = GetValueString(expressions[i]);
            }
            finally
            {
                GlobalStatic.Process.clearMethodStack();
                GlobalStatic.Process.loadPrevState();
            }
        }

        string GetValueString(string str)
        {
            if ((emuera == null) || (GlobalStatic.EMediator == null))
                return "";
            if (string.IsNullOrEmpty(str))
                return "";
            console.RunERBFromMemory = true;
            try
            {
                StringStream st = new StringStream(str);
                WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
                IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
                if (term == null)
                    return "";
                SingleTerm value = term.GetValue(GlobalStatic.EMediator);
                return value.ToString();
            }
            catch (CodeEE e)
            {
                return e.Message;
            }
            catch (Exception e)
            {
                return e.GetType().ToString() + ":" + e.Message;
            }
            finally
            {
                console.RunERBFromMemory = false;
            }
        }

        void LoadWatchListFromDisk()
        {
            try
            {
                if (!File.Exists(WatchFilePath))
                    return;
                var lines = new List<string>();
                using (StreamReader reader = new StreamReader(WatchFilePath, Config.Encode))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrEmpty(line))
                            lines.Add(line);
                    }
                }
                watchExpressions.Clear();
                watchExpressions.AddRange(lines);
            }
            catch
            {
                // 参考实现加载失败仅提示；这里静默（debug 工具不阻塞会话）。
            }
        }

        void SaveWatchListToDisk()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(WatchFilePath, false, Config.Encode))
                {
                    for (int i = 0; i < watchExpressions.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(watchExpressions[i]))
                            writer.WriteLine(watchExpressions[i]);
                    }
                }
            }
            catch
            {
            }
        }

        void SaveConsoleLogToDisk()
        {
            if (console == null)
                return;
            try
            {
                using (StreamWriter writer = new StreamWriter(ConsoleFilePath, false, Config.Encode))
                    writer.Write(console.DebugConsoleLog);
            }
            catch
            {
            }
        }
    }

    public class MainWindow : IDisposable
    {
        // 对齐参考侧 EMUERA_VERSION / 错误输出所用的引擎版本文本（emuera.em 参考值 1.824.0.0）
        public static string uEmueraVer = "1.824.0.0";

        public MainWindow()
        {}

        public void Dispose()
        { }

        public void clear_richText()
        {
            //uEmuera.Logger.Info("MainWindow.clear_richText");
            //throw new NotImplementedException();
        }
        public void Focus()
        {
            //uEmuera.Logger.Info("MainWindow.Focus");
            //throw new NotImplementedException();
        }

        public int Refresh()
        {
            //uEmuera.Logger.Info("MainWindow.Refresh");
            dirty_ = true;
            return Interlocked.Increment(ref refreshRequestGeneration);
        }

        public void Close()
        {
            uEmuera.Logger.Info("MainWindow.Close");
            //throw new NotImplementedException();
        }
        public void update_lastinput()
        {
            uEmuera.Logger.Info("MainWindow.update_lastinput");
            //throw new NotImplementedException();
        }

        internal void Reboot()
        {
            uEmuera.Logger.Info("MainWindow.Reboot");
            //throw new NotImplementedException();
        }

        internal void ShowConfigDialog()
        {
            uEmuera.Logger.Info("MainWindow.ShowConfigDialog");
            //throw new NotImplementedException();
        }

        public void Init()
        {
            if(created_)
                return;
            created_ = true;
            console_ = new EmueraConsole(this);
            console_.Initialize();
        }
        public void Update()
        {
            //uEmuera.Logger.Info("MainWindow.Update");
            if(console_ == null)
                return;
            if(GenericUtils.HasPendingDisplayWork)
                return;

            WinmmTimer.FrameStart();

            if(console_.IsInitializing)
            {
                ShowProcess();
                if(!dirty_)
                    return;
            }
            else if(console_.IsInProcess)
            {
                CheckProcess();
                if(wait_process && !EmueraThread.instance.IsSkipFlag)
                    return;
                if(!dirty_)
                    return;
            }
            else if(!dirty_)
            {
                return;
            }

            dirty_ = false;

            GenericUtils.SetBackgroundColor(console_.bgColor);

            int prev = GenericUtils.GetTextMaxLineNo();
            int min_lineno = GenericUtils.GetTextMinLineNo();
            bool sampleDisplayBridge = GenericUtils.IsPerformanceSamplingEnabled;
            long snapshotStartTimestamp = sampleDisplayBridge ? Stopwatch.GetTimestamp() : 0;
            var displayLines = console_.GetDisplayLinesSnapshotForuEmuera(
                min_lineno, out int console_count, out _);
            int snapshotCount = displayLines.Length;
            double snapshotElapsedMs = sampleDisplayBridge
                ? GetElapsedMilliseconds(snapshotStartTimestamp)
                : 0.0;
            if(console_count == 0)
            {
                if(console_.IsInProcess)
                {
                    dirty_ = true;
                    Volatile.Write(ref processedRefreshGeneration, Volatile.Read(ref refreshRequestGeneration));
                    return;
                }
                //清空
                GenericUtils.ClearText();
                Volatile.Write(ref processedRefreshGeneration, Volatile.Read(ref refreshRequestGeneration));
                return;
            }

            bool need_update_flag = false;
            int removeBottomCount = 0;
            System.Collections.Generic.List<(ConsoleDisplayLine Line, bool Update)> linesToAdd = null;
            System.Collections.Generic.List<ConsoleDisplayLine> linesToRefreshData = null;
            long diffStartTimestamp = sampleDisplayBridge ? Stopwatch.GetTimestamp() : 0;
            int newMaxLineNo = GetSnapshotMaxLineNo(displayLines);
            bool fullReset = prev >= 0 && min_lineno >= 0 && newMaxLineNo >= 0 && newMaxLineNo < min_lineno;

            if(prev >= 0 && newMaxLineNo >= 0)
            {
                if(fullReset)
                {
                    removeBottomCount = prev - min_lineno + 1;
                    need_update_flag = true;
                }
                else if(prev > newMaxLineNo)
                {
                    removeBottomCount = prev - newMaxLineNo;
                    need_update_flag = true;
                }
            }

            for(int i = 0; i < snapshotCount; i++)
            {
                var line = displayLines[i];
                if(line == null)
                    continue;

                bool isKnownRenderedLine = !fullReset && prev >= 0 && min_lineno >= 0
                    && line.LineNo >= min_lineno && line.LineNo <= prev;
                if(!fullReset && prev >= 0 && min_lineno >= 0 && line.LineNo < min_lineno)
                    continue;

                bool isUpdate = isKnownRenderedLine;
                if(isKnownRenderedLine)
                {
                    var existing = GenericUtils.GetText(line.LineNo);
                    if(ShouldReuseRenderedLine(existing, line, out bool refreshDataOnly))
                    {
                        if(refreshDataOnly)
                        {
                            if(linesToRefreshData == null)
                                linesToRefreshData = new System.Collections.Generic.List<ConsoleDisplayLine>();
                            linesToRefreshData.Add(line);
                        }
                        continue;
                    }
                    need_update_flag = true;
                }

                if(linesToAdd == null)
                    linesToAdd = new System.Collections.Generic.List<(ConsoleDisplayLine Line, bool Update)>();
                linesToAdd.Add((line, isUpdate));
            }

            EmueraDisplayScrollMode scrollMode = DecideScrollModeForDisplayDelta(prev, removeBottomCount, need_update_flag,
                linesToAdd, linesToRefreshData);

            GenericUtils.ApplyTextChanges(removeBottomCount, linesToAdd, need_update_flag, console_.LastButtonGeneration, scrollMode, linesToRefreshData);
            double diffElapsedMs = sampleDisplayBridge
                ? GetElapsedMilliseconds(diffStartTimestamp)
                : 0.0;

            GenericUtils.ShowIsInProcess(false);
            bool cbgSubmitted = GenericUtils.RefreshCBG(console_);
            if (sampleDisplayBridge)
            {
                GenericUtils.SampleDisplayBridge(snapshotElapsedMs, diffElapsedMs, snapshotCount, removeBottomCount,
                    linesToAdd?.Count ?? 0, linesToRefreshData?.Count ?? 0, cbgSubmitted);
            }
            if (console_.NeedSetTimer())
                EmueraThread.instance.WakeForTimerSchedule();
            last_process_tic = 0;
            Volatile.Write(ref processedRefreshGeneration, Volatile.Read(ref refreshRequestGeneration));
        }

        static double GetElapsedMilliseconds(long startTimestamp)
        {
            if (startTimestamp <= 0)
                return 0.0;
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        }

        static EmueraDisplayScrollMode DecideScrollModeForDisplayDelta(int previousMaxLineNo, int removeBottomCount, bool update,
            System.Collections.Generic.List<(ConsoleDisplayLine Line, bool Update)> linesToAdd,
            System.Collections.Generic.List<ConsoleDisplayLine> dataOnlyLines)
        {
            // 状态面板等页面通常通过删除底部旧行再重画当前屏幕来刷新。
            // 这不是“追加新文本”，因此不能触发 ScrollContainer 自动滚到底；否则 Android 会在重绘时把视口拖走。
            // 只有确实新增了显示行时才追底，避免把内容类型识别重新耦合回滚动策略。
            if ((linesToAdd == null || linesToAdd.Count == 0)
                && (dataOnlyLines == null || dataOnlyLines.Count == 0))
                return EmueraDisplayScrollMode.PreserveViewport;

            bool hasAppendAfterPreviousMax = false;
            int linesToAddCount = linesToAdd?.Count ?? 0;
            for (int i = 0; i < linesToAddCount; i++)
            {
                var item = linesToAdd[i];
                if (item.Line == null)
                    continue;
                if (!item.Update && item.Line.LineNo > previousMaxLineNo)
                {
                    hasAppendAfterPreviousMax = true;
                    break;
                }
            }

            // 普通会话/泡茶等输出可能在追加新文本的同时刷新旧行元数据。
            // 只要确实出现了新行，就按普通 Emuera 输出追到底部。
            if (hasAppendAfterPreviousMax)
                return EmueraDisplayScrollMode.FollowBottom;

            if (removeBottomCount > 0 || update)
                return EmueraDisplayScrollMode.PreserveViewport;

            return EmueraDisplayScrollMode.PreserveViewport;
        }

        static int GetSnapshotMaxLineNo(ConsoleDisplayLine[] lines)
        {
            if(lines == null || lines.Length == 0)
                return -1;
            for(int i = lines.Length - 1; i >= 0; i--)
            {
                if(lines[i] != null)
                    return lines[i].LineNo;
            }
            return -1;
        }

        static bool ShouldReuseRenderedLine(ConsoleDisplayLine current, ConsoleDisplayLine next, out bool refreshDataOnly)
        {
            refreshDataOnly = false;
            if(ReferenceEquals(current, next))
                return true;
            if(current == null || next == null)
                return false;
            if(!DisplayLineVisualEquals(current, next))
                return false;
            refreshDataOnly = HasCommandButton(current) || HasCommandButton(next);
            return true;
        }

        static bool HasCommandButton(ConsoleDisplayLine line)
        {
            if(line?.Buttons == null)
                return false;
            for(int i = 0; i < line.Buttons.Length; i++)
            {
                var button = line.Buttons[i];
                if(button == null)
                    continue;
                if(button.IsButton)
                    return true;
                if(button.StrArray == null)
                    continue;
                for(int j = 0; j < button.StrArray.Length; j++)
                {
                    if(button.StrArray[j] is ConsoleDivPart div && div.Children != null)
                    {
                        for(int k = 0; k < div.Children.Length; k++)
                        {
                            if(HasCommandButton(div.Children[k]))
                                return true;
                        }
                    }
                }
            }
            return false;
        }

        static bool DisplayLineVisualEquals(ConsoleDisplayLine current, ConsoleDisplayLine next)
        {
            if(current.LineNo != next.LineNo
                || current.IsLogicalLine != next.IsLogicalLine
                || current.IsTemporary != next.IsTemporary
                || current.IsLineEnd != next.IsLineEnd
                || current.Align != next.Align
                || current.TextBackgroundColor != next.TextBackgroundColor)
                return false;

            var currentButtons = current.Buttons;
            var nextButtons = next.Buttons;
            if(currentButtons == null || nextButtons == null)
                return currentButtons == nextButtons;
            if(currentButtons.Length != nextButtons.Length)
                return false;

            for(int i = 0; i < currentButtons.Length; i++)
            {
                if(!DisplayButtonVisualEquals(currentButtons[i], nextButtons[i]))
                    return false;
            }
            return true;
        }

        static bool DisplayButtonVisualEquals(ConsoleButtonString current, ConsoleButtonString next)
        {
            if(current == null || next == null)
                return current == next;
            if(current.IsButton != next.IsButton
                || current.IsInteger != next.IsInteger
                || current.Input != next.Input
                || !string.Equals(current.Inputs ?? "", next.Inputs ?? "", StringComparison.Ordinal)
                || current.PointX != next.PointX
                || current.PointXisLocked != next.PointXisLocked
                || current.RelativePointX != next.RelativePointX
                || current.Width != next.Width
                || current.XsubPixel != next.XsubPixel
                || !string.Equals(current.Title ?? "", next.Title ?? "", StringComparison.Ordinal))
                return false;

            var currentParts = current.StrArray;
            var nextParts = next.StrArray;
            if(currentParts == null || nextParts == null)
                return currentParts == nextParts;
            if(currentParts.Length != nextParts.Length)
                return false;

            for(int i = 0; i < currentParts.Length; i++)
            {
                if(!DisplayPartVisualEquals(currentParts[i], nextParts[i]))
                    return false;
            }
            return true;
        }

        static bool DisplayPartVisualEquals(AConsoleDisplayPart current, AConsoleDisplayPart next)
        {
            if(current == null || next == null)
                return current == next;
            if(current.GetType() != next.GetType()
                || current.Error != next.Error
                || current.PointX != next.PointX
                || current.XsubPixel != next.XsubPixel
                || current.Width != next.Width
                || current.WidthF != next.WidthF
                || current.Top != next.Top
                || current.Bottom != next.Bottom
                || !string.Equals(current.Str ?? "", next.Str ?? "", StringComparison.Ordinal)
                || !string.Equals(current.AltText ?? "", next.AltText ?? "", StringComparison.Ordinal))
                return false;

            if(current is ConsoleStyledString currentText && next is ConsoleStyledString nextText)
                return currentText.StringStyle == nextText.StringStyle
                    && currentText.FontSize == nextText.FontSize
                    && currentText.VerticalAlign == nextText.VerticalAlign
                    && string.Equals(currentText.RenderMode ?? "", nextText.RenderMode ?? "", StringComparison.Ordinal)
                    && string.Equals(currentText.FontEdging ?? "", nextText.FontEdging ?? "", StringComparison.Ordinal)
                    && string.Equals(currentText.FontHinting ?? "", nextText.FontHinting ?? "", StringComparison.Ordinal);

            if(current is ConsoleImagePart currentImage && next is ConsoleImagePart nextImage)
            {
                return string.Equals(currentImage.ResourceName ?? "", nextImage.ResourceName ?? "", StringComparison.Ordinal)
                    && string.Equals(currentImage.ButtonResourceName ?? "", nextImage.ButtonResourceName ?? "", StringComparison.Ordinal)
                    && RectangleEquals(currentImage.dest_rect, nextImage.dest_rect)
                    && currentImage.Display == nextImage.Display
                    && currentImage.PositionX == nextImage.PositionX
                    && currentImage.PositionY == nextImage.PositionY
                    && currentImage.FlipX == nextImage.FlipX
                    && currentImage.FlipY == nextImage.FlipY
                    && string.Equals(currentImage.ColorMatrixVariableName ?? "", nextImage.ColorMatrixVariableName ?? "", StringComparison.Ordinal);
            }

            if(current is ConsoleDivPart currentDiv && next is ConsoleDivPart nextDiv)
            {
                if(currentDiv.X != nextDiv.X
                    || currentDiv.Y != nextDiv.Y
                    || currentDiv.DivWidth != nextDiv.DivWidth
                    || currentDiv.DivHeight != nextDiv.DivHeight
                    || currentDiv.Depth != nextDiv.Depth
                    || currentDiv.BackgroundColor != nextDiv.BackgroundColor
                    || currentDiv.IsRelative != nextDiv.IsRelative
                    || currentDiv.Display != nextDiv.Display)
                    return false;
                var currentChildren = currentDiv.Children;
                var nextChildren = nextDiv.Children;
                if(currentChildren == null || nextChildren == null)
                    return currentChildren == nextChildren;
                if(currentChildren.Length != nextChildren.Length)
                    return false;
                for(int i = 0; i < currentChildren.Length; i++)
                {
                    if(!DisplayLineVisualEquals(currentChildren[i], nextChildren[i]))
                        return false;
                }
            }

            // shape 的颜色字段在兼容层里是 protected；保守起见不复用 shape 行，避免颜色变化被误判为不变。
            if(current is ConsoleShapePart)
                return false;

            return true;
        }

        static bool RectangleEquals(Rectangle current, Rectangle next)
        {
            return current.X == next.X
                && current.Y == next.Y
                && current.Width == next.Width
                && current.Height == next.Height;
        }

        private EmueraConsole console_ = null;
        private volatile bool dirty_ = false;
        private int refreshRequestGeneration = 0;
        private int processedRefreshGeneration = 0;
        public int RefreshRequestGeneration
        {
            get { return Volatile.Read(ref refreshRequestGeneration); }
        }

        public void WaitForRefreshProcessed(int generation, int timeoutMs)
        {
            if (GenericUtils.IsOnMainThread())
                return;
            long deadline = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Math.Max(0, timeoutMs);
            while (Volatile.Read(ref processedRefreshGeneration) < generation
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < deadline)
            {
                Thread.Sleep(1);
            }
        }

        public string InternalEmueraVer { get { return uEmueraVer; } }
        public string EmueraVerText { get { return uEmueraVer; } }

        public bool Created { get { return created_; } }
        bool created_ = false;

        public ScrollBar ScrollBar = new ScrollBar();
        public PictureBox MainPicBox = new PictureBox();
        public string Text { get; set; }
        public ToolTip ToolTip = new ToolTip();
        public TextBox TextBox = new TextBox();

        public void SetTextBoxPos(int x, int y, int width)
        {
            TextBox.X = x;
            TextBox.Y = y;
            TextBox.Width = Math.Max(0, width);
            TextBox.UseCustomPosition = true;
        }

        public void ResetTextBoxPos()
        {
            TextBox.X = 0;
            TextBox.Y = 0;
            TextBox.Width = 0;
            TextBox.UseCustomPosition = false;
        }

        void ShowProcess()
        {
            GenericUtils.ShowIsInProcess(true);
        }
        void CheckProcess()
        {
            var now = MinorShift._Library.WinmmTimer.TickCount;
            if(last_process_tic == 0)
                last_process_tic = now;
            else if(now - last_process_tic > 1500u)
            {
                GenericUtils.ShowIsInProcess(true);
            }
        }
        uint last_process_tic = 0;
        bool wait_process = false;
    }
}
