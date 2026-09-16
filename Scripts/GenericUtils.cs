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

public enum EmueraLogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
    None = 4
}

[Flags]
public enum EmueraLogCategory
{
    None = 0,
    General = 1 << 0,
    Sprite = 1 << 1,
    Audio = 1 << 2,
    Input = 1 << 3,
    Script = 1 << 4,
    UI = 1 << 5,
    FileSystem = 1 << 6,
    Load = 1 << 7,
    Save = 1 << 8,
    Config = 1 << 9,
    Performance = 1 << 10,
    Touch = 1 << 11,
    StatementRecognition = 1 << 12,
    All = int.MaxValue
}

internal enum EmueraDisplayScrollMode
{
    FollowBottom = 0,
    PreserveViewport = 1,
    KeepRegionVisible = 2,
    KeepChoicesVisible = 3,
    ManualHold = 4
}

internal static partial class GenericUtils
{
    // UI 动作队列改为 lock + 环形缓冲 + 信封对象池：每次入队复用信封对象，不再
    // 为每个动作分配闭包和 ConcurrentQueue 内部节点（Android Mono GC 暂停的主要人为来源）。
    static readonly object uiQueueLock = new object();
    static UiEnvelope[] uiQueueRing = new UiEnvelope[256];
    static readonly Stack<UiEnvelope> uiEnvelopePool = new Stack<UiEnvelope>();
    static int uiQueueHead = 0;
    static int uiQueueCount = 0;

    /// <summary>
    /// 复用信封：承载一个待执行 UI 动作。出队执行后归还池，避免逐动作分配闭包。
    /// 计数递减放在 finally，保证动作抛异常时 pending 计数仍被正确回退。
    /// </summary>
    sealed class UiEnvelope
    {
        public Action Action;
        public bool DisplayWork;

        public void Run()
        {
            try
            {
                Action();
            }
            finally
            {
                if (DisplayWork)
                    Interlocked.Decrement(ref pendingDisplayActions);
                Interlocked.Decrement(ref pendingUiActions);
            }
        }
    }

    static int mainThreadId = -1;
    static int pendingUiActions = 0;
    static int pendingDisplayActions = 0;
    static int uiFrameGeneration = 0;
    const ulong AndroidUiBudgetUsec = 9000;
    const ulong DesktopUiBudgetUsec = 7000;
    const int AndroidMaxUiActionsPerFrame = 128;
    const int DesktopMaxUiActionsPerFrame = 96;
    static readonly object inputStateLock = new object();
    static uEmuera.Drawing.Point pointerPosition = uEmuera.Drawing.Point.Empty;
    static string pointingButtonInput = "";
    static long pointingButtonGeneration = long.MinValue;
    static bool pointingButtonActive = false;
    const string ScrollTracePrefix = "[SCROLL_TRACE]";
    const int ScrollTraceCoreBurstLineCount = 120;
    const int MaxLogMessageChars = 8192;
    static RuntimeDiagnosticsConfig _runtimeConfig;
    static InputReplayBuffer _inputReplay;
    const int SaveLogOperationTrailCapacity = 5;
    static readonly SaveLogOperationTrail _saveLogOperationTrail = new SaveLogOperationTrail(SaveLogOperationTrailCapacity);
    static long _inputIdSequence;
    static long _currentInputId;
    static long _lastPerformanceSampleMs;
    static double _performanceFrameMsTotal;
    static double _performanceFrameMsMax;
    static int _performanceFrameMaxUiPending;
    static int _performanceFrameMaxDisplayPending;
    static int _performanceFrameMaxGpuRenderQueue;
    static int _performanceFrameMaxTextRenderQueue;
    static int _performanceFrameCount;
    static long _lastConsoleRenderSampleMs;
    static int _consoleRenderCallbackCount;
    static readonly ConsoleRenderSamplingWindow _consoleRenderDrawWindow = new ConsoleRenderSamplingWindow();
    static readonly ConsoleRenderSamplingWindow _consoleRenderHitRebuildWindow = new ConsoleRenderSamplingWindow();
    static long _lastDisplayBridgeSampleMs;
    static readonly DisplayBridgeSamplingWindow _displayBridgeSamplingWindow = new DisplayBridgeSamplingWindow();
    static readonly object cbgRefreshStateLock = new object();
    static EmueraConsole lastCbgSnapshotConsole;
    static int lastCbgSnapshotRevision = int.MinValue;

    // 这里只统计 Godot Canvas 回调中的 CPU 工作，不包含 GPU 栅格化、提交或驱动等待。
    // 两类回调必须分开累计，避免点击前命中表重建污染可视绘制的耗时判断。
    sealed class ConsoleRenderSamplingWindow
    {
        const int MaxP95Samples = 512;
        readonly double[] elapsedMsSamples = new double[MaxP95Samples];

        public int Callbacks { get; private set; }
        public double ElapsedMsTotal { get; private set; }
        public double ElapsedMsMax { get; private set; }
        public int VisibleRowsTotal { get; private set; }
        public int VisibleRowsMax { get; private set; }
        public int CanvasRowsTotal { get; private set; }
        public int CanvasRowsMax { get; private set; }
        public int OverlayRowsTotal { get; private set; }
        public int OverlayRowsMax { get; private set; }
        public int DrawnPartsTotal { get; private set; }
        public int DrawnPartsMax { get; private set; }
        public int RebuiltHitRectsTotal { get; private set; }
        public int RebuiltHitRectsMax { get; private set; }
        public int SampleCount { get; private set; }
        public int SampleOverflow { get; private set; }

        public void Add(double elapsedMs, int visibleRows, int canvasRows, int overlayRows, int drawnParts, int rebuiltHitRects)
        {
            Callbacks++;
            ElapsedMsTotal += elapsedMs;
            ElapsedMsMax = Math.Max(ElapsedMsMax, elapsedMs);
            VisibleRowsTotal += Math.Max(0, visibleRows);
            VisibleRowsMax = Math.Max(VisibleRowsMax, visibleRows);
            CanvasRowsTotal += Math.Max(0, canvasRows);
            CanvasRowsMax = Math.Max(CanvasRowsMax, canvasRows);
            OverlayRowsTotal += Math.Max(0, overlayRows);
            OverlayRowsMax = Math.Max(OverlayRowsMax, overlayRows);
            DrawnPartsTotal += Math.Max(0, drawnParts);
            DrawnPartsMax = Math.Max(DrawnPartsMax, drawnParts);
            RebuiltHitRectsTotal += Math.Max(0, rebuiltHitRects);
            RebuiltHitRectsMax = Math.Max(RebuiltHitRectsMax, rebuiltHitRects);
            if (SampleCount < elapsedMsSamples.Length)
                elapsedMsSamples[SampleCount++] = elapsedMs;
            else
                SampleOverflow++;
        }

        public double AverageMs => Callbacks == 0 ? 0.0 : ElapsedMsTotal / Callbacks;
        public int AverageVisibleRows => Callbacks == 0 ? 0 : VisibleRowsTotal / Callbacks;
        public int AverageCanvasRows => Callbacks == 0 ? 0 : CanvasRowsTotal / Callbacks;
        public int AverageOverlayRows => Callbacks == 0 ? 0 : OverlayRowsTotal / Callbacks;
        public int AverageDrawnParts => Callbacks == 0 ? 0 : DrawnPartsTotal / Callbacks;
        public int AverageRebuiltHitRects => Callbacks == 0 ? 0 : RebuiltHitRectsTotal / Callbacks;

        public double GetP95Ms()
        {
            if (SampleCount == 0)
                return 0.0;
            Array.Sort(elapsedMsSamples, 0, SampleCount);
            int p95Index = Math.Clamp((int)Math.Ceiling(SampleCount * 0.95) - 1, 0, SampleCount - 1);
            return elapsedMsSamples[p95Index];
        }

        public void Reset()
        {
            Callbacks = 0;
            ElapsedMsTotal = 0.0;
            ElapsedMsMax = 0.0;
            VisibleRowsTotal = 0;
            VisibleRowsMax = 0;
            CanvasRowsTotal = 0;
            CanvasRowsMax = 0;
            OverlayRowsTotal = 0;
            OverlayRowsMax = 0;
            DrawnPartsTotal = 0;
            DrawnPartsMax = 0;
            RebuiltHitRectsTotal = 0;
            RebuiltHitRectsMax = 0;
            SampleCount = 0;
            SampleOverflow = 0;
            Array.Clear(elapsedMsSamples, 0, elapsedMsSamples.Length);
        }
    }

    // 显示桥由 uEmuera 主窗口和 Godot UI 队列在同一主线程顺序执行。
    // 只在显式性能开关开启时累计，以便把地图刷新中的快照、diff、Control 回退和 CBG
    // 提交成本与 ConsoleRenderSurface 的 _Draw() CPU 回调分开观察。
    sealed class DisplayBridgeSamplingWindow
    {
        public int Refreshes { get; private set; }
        public int SnapshotLinesTotal { get; private set; }
        public int SnapshotLinesMax { get; private set; }
        public int RemoveBottomTotal { get; private set; }
        public int AddLinesTotal { get; private set; }
        public int DataOnlyLinesTotal { get; private set; }
        public int CbgSubmits { get; private set; }
        public int FallbackBuilds { get; private set; }
        public int FallbackReplacements { get; private set; }
        public int ApplyCalls { get; private set; }
        public double SnapshotMsTotal { get; private set; }
        public double SnapshotMsMax { get; private set; }
        public double DiffMsTotal { get; private set; }
        public double DiffMsMax { get; private set; }
        public double ApplyMsTotal { get; private set; }
        public double ApplyMsMax { get; private set; }

        public void AddBridge(double snapshotMs, double diffMs, int snapshotLines, int removeBottom,
            int addLines, int dataOnlyLines, bool cbgSubmitted)
        {
            Refreshes++;
            SnapshotMsTotal += Math.Max(0.0, snapshotMs);
            SnapshotMsMax = Math.Max(SnapshotMsMax, snapshotMs);
            DiffMsTotal += Math.Max(0.0, diffMs);
            DiffMsMax = Math.Max(DiffMsMax, diffMs);
            SnapshotLinesTotal += Math.Max(0, snapshotLines);
            SnapshotLinesMax = Math.Max(SnapshotLinesMax, snapshotLines);
            RemoveBottomTotal += Math.Max(0, removeBottom);
            AddLinesTotal += Math.Max(0, addLines);
            DataOnlyLinesTotal += Math.Max(0, dataOnlyLines);
            if (cbgSubmitted)
                CbgSubmits++;
        }

        public void AddFallbackBuild(bool replacing)
        {
            FallbackBuilds++;
            if (replacing)
                FallbackReplacements++;
        }

        public void AddApply(double elapsedMs)
        {
            ApplyCalls++;
            ApplyMsTotal += Math.Max(0.0, elapsedMs);
            ApplyMsMax = Math.Max(ApplyMsMax, elapsedMs);
        }

        public double AverageSnapshotMs => Refreshes == 0 ? 0.0 : SnapshotMsTotal / Refreshes;
        public double AverageDiffMs => Refreshes == 0 ? 0.0 : DiffMsTotal / Refreshes;
        public double AverageApplyMs => ApplyCalls == 0 ? 0.0 : ApplyMsTotal / ApplyCalls;
        public int AverageSnapshotLines => Refreshes == 0 ? 0 : SnapshotLinesTotal / Refreshes;

        public void Reset()
        {
            Refreshes = 0;
            SnapshotLinesTotal = 0;
            SnapshotLinesMax = 0;
            RemoveBottomTotal = 0;
            AddLinesTotal = 0;
            DataOnlyLinesTotal = 0;
            CbgSubmits = 0;
            FallbackBuilds = 0;
            FallbackReplacements = 0;
            ApplyCalls = 0;
            SnapshotMsTotal = 0.0;
            SnapshotMsMax = 0.0;
            DiffMsTotal = 0.0;
            DiffMsMax = 0.0;
            ApplyMsTotal = 0.0;
            ApplyMsMax = 0.0;
        }
    }

    public static bool HasPendingUIWork => Volatile.Read(ref pendingUiActions) > 0;
    public static bool HasPendingDisplayWork => Volatile.Read(ref pendingDisplayActions) > 0;
    public static int UiFrameGeneration => Volatile.Read(ref uiFrameGeneration);



    public static bool IsSaveLogOperationCaptureEnabled
    {
        get
        {
            var cfg = _runtimeConfig;
            return cfg != null && cfg.LoggingEnabled;
        }
    }


    public static void SetMainThread()
    {
        mainThreadId = System.Environment.CurrentManagedThreadId;
    }

    static bool IsMainThread()
    {
        return mainThreadId < 0 || System.Environment.CurrentManagedThreadId == mainThreadId;
    }

    public static bool IsOnMainThread()
    {
        return IsMainThread();
    }

    public static void ShellOpen(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return;

        void OpenTarget()
        {
            try
            {
                OS.ShellOpen(target);
            }
            catch (Exception ex)
            {
                Warn(EmueraLogCategory.General, () => $"[SHELL] Open failed: {RedactTracePath(target)} - {ex.Message}");
            }
        }

        if (IsMainThread())
            OpenTarget();
        else
            EnqueueUI(OpenTarget);
    }

    public static void SetPointerPosition(float x, float y)
    {
        var point = new uEmuera.Drawing.Point((int)MathF.Round(x), (int)MathF.Round(y));
        lock (inputStateLock)
            pointerPosition = point;
        uEmuera.Forms.Control.MousePosition = point;
    }

    public static uEmuera.Drawing.Point GetPointerPosition()
    {
        lock (inputStateLock)
            return pointerPosition;
    }

    public static void SetPointingButton(string input, long generation)
    {
        lock (inputStateLock)
        {
            pointingButtonInput = input ?? "";
            pointingButtonGeneration = generation;
            pointingButtonActive = true;
        }
    }

    public static void ClearPointingButton(long generation = long.MinValue)
    {
        lock (inputStateLock)
        {
            if (generation != long.MinValue && pointingButtonGeneration != generation)
                return;
            pointingButtonInput = "";
            pointingButtonGeneration = long.MinValue;
            pointingButtonActive = false;
        }
    }

    /// <summary>
    /// Clears bridge-side state that is produced by one legacy session.  This
    /// is deliberately separate from diagnostics initialization: the runner's
    /// sinks, rate limits and ring buffers are process scoped, while audio,
    /// input replay, fallback paths and queued view work belong to the active
    /// candidate.  The canary invokes this only after the legacy worker has
    /// stopped, so draining the UI queue cannot race a producer.
    /// </summary>
    internal static void ResetCanarySessionState()
    {
        lock (soundFallbackResolveCacheLock)
            soundFallbackResolveCache.Clear();

        lock (snakeAudioLock)
        {
            for (int i = 0; i < snakeSounds.Length; i++)
                ResetSnakeAudioStateLocked(snakeSounds[i]);
            ResetSnakeAudioStateLocked(snakeBgm);
        }

        lock (inputStateLock)
        {
            pointerPosition = uEmuera.Drawing.Point.Empty;
            pointingButtonInput = "";
            pointingButtonGeneration = long.MinValue;
            pointingButtonActive = false;
        }
        uEmuera.Forms.Control.MousePosition = uEmuera.Drawing.Point.Empty;
        MinorShift._Library.WinInput.ResetCanarySessionState();
        uEmuera.Forms.Timer.ResetSessionState();

        // All queued actions at this point were produced by the stopped
        // candidate.  A stale action must never mutate the next candidate's
        // Godot view, so discard the envelopes and their accounting together.
        // 主线程调用且生产者已停，清空环形缓冲并把信封全部归还池，不留下悬空引用。
        lock (uiQueueLock)
        {
            while (uiQueueCount > 0)
            {
                UiEnvelope envelope = uiQueueRing[uiQueueHead];
                uiQueueRing[uiQueueHead] = null;
                uiQueueHead = (uiQueueHead + 1) % uiQueueRing.Length;
                uiQueueCount--;
                envelope.Action = null;
                envelope.DisplayWork = false;
                uiEnvelopePool.Push(envelope);
            }
        }
        Interlocked.Exchange(ref pendingUiActions, 0);
        Interlocked.Exchange(ref pendingDisplayActions, 0);
        Interlocked.Increment(ref uiFrameGeneration);

        _currentInputId = 0;
        _runtimeGamePath = "";
        _runtimeCoreProfile = "";
        DiagnosticLogExporter.NotifyGamePathSelected("");
        var cfg = _runtimeConfig;
        _inputReplay = cfg != null && cfg.InputReplayEnabled
            ? new InputReplayBuffer(cfg.InputReplayMaxEvents)
            : null;
        _saveLogOperationTrail.Clear();

        scrollTraceSequence = 0;
        scrollTraceCoreLinesRemaining = 0;
        dynamicMapLastContextTickMs = long.MinValue;
        _lastPerformanceSampleMs = 0;
        _performanceFrameMsTotal = 0.0;
        _performanceFrameMsMax = 0.0;
        _performanceFrameMaxUiPending = 0;
        _performanceFrameMaxDisplayPending = 0;
        _performanceFrameMaxGpuRenderQueue = 0;
        _performanceFrameMaxTextRenderQueue = 0;
        _performanceFrameCount = 0;
        ResetConsoleRenderSampling();
        ResetDisplayBridgeSampling();
        lock (cbgRefreshStateLock)
        {
            lastCbgSnapshotConsole = null;
            lastCbgSnapshotRevision = int.MinValue;
        }
    }


    public static string GetPointingButtonInput()
    {
        lock (inputStateLock)
            return pointingButtonActive ? pointingButtonInput ?? "" : "";
    }

    public static void FlushUI()
    {
        int maxActions = OS.GetName() == "Android" ? AndroidMaxUiActionsPerFrame : DesktopMaxUiActionsPerFrame;
        ulong budgetUsec = OS.GetName() == "Android" ? AndroidUiBudgetUsec : DesktopUiBudgetUsec;
        ulong startUsec = Time.GetTicksUsec();
        int count = 0;
        while (count < maxActions)
        {
            UiEnvelope envelope;
            lock (uiQueueLock)
            {
                if (uiQueueCount == 0)
                    break;
                envelope = uiQueueRing[uiQueueHead];
                uiQueueRing[uiQueueHead] = null;
                uiQueueHead = (uiQueueHead + 1) % uiQueueRing.Length;
                uiQueueCount--;
            }
            try
            {
                envelope.Run();
            }
            catch (Exception ex)
            {
                Error(EmueraLogCategory.UI, () => $"[UI Queue] {ex}");
            }
            finally
            {
                ReleaseUiEnvelope(envelope);
            }
            count++;
            if (Time.GetTicksUsec() - startUsec >= budgetUsec)
                break;
        }
        Interlocked.Increment(ref uiFrameGeneration);
    }

    public static void WaitForUiFrameAfter(int generation, int timeoutMs)
    {
        if (IsMainThread())
            return;
        long deadline = GetTickMs() + Math.Max(0, timeoutMs);
        while (Volatile.Read(ref uiFrameGeneration) <= generation && GetTickMs() < deadline)
            Thread.Sleep(1);
    }

    public static void WaitForDisplayWorkDrained(int timeoutMs)
    {
        if (IsMainThread())
            return;
        long deadline = GetTickMs() + Math.Max(0, timeoutMs);
        while (Volatile.Read(ref pendingDisplayActions) > 0 && GetTickMs() < deadline)
            Thread.Sleep(1);
    }

    static void EnqueueUI(Action action, bool displayWork = false)
    {
        Interlocked.Increment(ref pendingUiActions);
        if (displayWork)
            Interlocked.Increment(ref pendingDisplayActions);

        lock (uiQueueLock)
        {
            if (uiQueueCount == uiQueueRing.Length)
                GrowUiRingLocked();
            UiEnvelope envelope = AcquireUiEnvelope();
            envelope.Action = action;
            envelope.DisplayWork = displayWork;
            int tail = (uiQueueHead + uiQueueCount) % uiQueueRing.Length;
            uiQueueRing[tail] = envelope;
            uiQueueCount++;
        }
    }

    // 从空闲池取一个信封，仅允许在持有 uiQueueLock 时调用。
    static UiEnvelope AcquireUiEnvelope()
    {
        return uiEnvelopePool.Count > 0 ? uiEnvelopePool.Pop() : new UiEnvelope();
    }

    // 环形缓冲已满时扩容（仅在超大突发时发生，不属于逐动作热路径）。
    static void GrowUiRingLocked()
    {
        int oldCapacity = uiQueueRing.Length;
        var newRing = new UiEnvelope[oldCapacity * 2];
        for (int i = 0; i < uiQueueCount; i++)
            newRing[i] = uiQueueRing[(uiQueueHead + i) % oldCapacity];
        uiQueueHead = 0;
        uiQueueRing = newRing;
    }

    // 执行完毕后归还信封：先清空 Action 引用，避免池长期持有闭包/游戏对象引用。
    static void ReleaseUiEnvelope(UiEnvelope envelope)
    {
        envelope.Action = null;
        envelope.DisplayWork = false;
        lock (uiQueueLock)
            uiEnvelopePool.Push(envelope);
    }


    // 企业级说明：诊断日志会在滚动、输入和加载路径频繁调用，统一裁剪逻辑可避免不同日志入口产生不一致的换行/制表符处理。
    static string ClipFlatText(string value, int maxLength, string suffix)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        maxLength = Math.Max(0, maxLength);
        value = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        if (value.Length <= maxLength)
            return value;
        return value.Substring(0, maxLength) + (suffix ?? "");
    }

    // ---------- 模块化调试开关兼容层 ----------

    /// <summary>
    /// 企业级说明：触摸调试开关默认关闭，调用前必须先判断，避免构造日志文本。
    /// 返回 true 时，后续代码可安全构造 TOUCH.* 诊断日志。
    /// </summary>
    /// </summary>
    public static void CaptureInputReplay(string kind, string input, Godot.Vector2 globalPos, Godot.Vector2 localPos,
        string waitState, bool consumed)
    {
        if (_inputReplay == null)
            return;
        long inputId = Interlocked.Increment(ref _inputIdSequence);
        _currentInputId = inputId;
        int maxChars = _runtimeConfig?.InputReplayMaxTextChars ?? 32;
        string clipped = ClipTrace(input, maxChars);
        _inputReplay.Capture(kind, clipped, globalPos, localPos, inputId, waitState, consumed);
        DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Input,
            "REPLAY.INPUT.CAPTURE", "input replay captured",
            $"kind={kind} input_id={inputId} consumed={consumed}");
    }

    public static long CurrentInputId => Interlocked.Read(ref _currentInputId);

    public static InputReplayBuffer GetInputReplayBuffer() => _inputReplay;

    public static bool IsInputReplayCaptureEnabled => _inputReplay != null;

    public static bool IsPerformanceSamplingEnabled => (_runtimeConfig?.LoggingEnabled ?? false) && (_runtimeConfig?.PerformanceSamplingEnabled ?? false);

    /// <summary>
    /// 企业级说明：save_log 操作轨迹始终保持最近 5 次核心输入摘要，独立于专家诊断开关。
    /// 这条路径只在输入被核心消费时调用，不采集拖动采样和渲染事件，保证手机端默认使用时没有持续调试负担。
    /// </summary>
    public static void CaptureSaveLogOperation(string kind, string input, string codeBefore, string codeAfter,
        string waitState, bool consumed)
    {
        if (!IsSaveLogOperationCaptureEnabled)
            return;
        int maxInputChars = _runtimeConfig?.RedactionMaxUserTextChars ?? 64;
        int maxScriptChars = _runtimeConfig?.RedactionMaxScriptTextChars ?? 120;
        string originalBefore = codeBefore ?? "";
        string originalAfter = codeAfter ?? "";
        string safeInput = ClipTrace(input, maxInputChars);
        string safeBefore = DiagnosticLogRouter.RedactText(originalBefore, maxScriptChars);
        string safeAfter = DiagnosticLogRouter.RedactText(originalAfter, maxScriptChars);
        string safeWait = DiagnosticLogRouter.RedactText(waitState ?? "", 160);
        string effect = string.Equals(originalBefore, originalAfter, StringComparison.Ordinal)
            ? "line_unchanged"
            : "line_changed";
        long operationSeq = _saveLogOperationTrail.Capture(kind, safeInput, safeBefore, safeAfter, safeWait, consumed, effect);
        DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Input,
            "SAVE_LOG.OPERATION", "save_log operation captured",
            "operation_seq=" + operationSeq + " kind=" + (kind ?? "") + " consumed=" + consumed + " effect=" + effect);
    }

    public static void NotifyGamePathSelected(string path, string coreProfile = "")
    {
        _runtimeGamePath = path ?? "";
        _runtimeCoreProfile = coreProfile ?? "";
        DiagnosticLogExporter.NotifyGamePathSelected(path);
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.LoggingEnabled)
            return;
        string redactedPath = DiagnosticLogRouter.RedactPath(path ?? "");
        if (cfg.RetentionEnabled && cfg.RetentionCleanupOnStartup)
            DiagnosticLogExporter.RunRetentionCleanup(cfg);
        if (cfg.BreadcrumbEnabled && cfg.BreadcrumbWriteOnGamePathSelected)
            DiagnosticLogExporter.WriteBreadcrumb(cfg, "BOOT.GAME_PATH.SELECTED", "game=" + redactedPath + " core=" + (coreProfile ?? ""));
        if (cfg.AndroidStorageEnabled && cfg.AndroidStorageLogPathSelection)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.FileSystem,
                "ANDROID_STORAGE.PATH_SELECTED", "game path selected",
                "game_path=" + redactedPath + " core=" + (coreProfile ?? ""));
        }
    }

    public static void NotifyLifecycleState(string state)
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.LoggingEnabled)
            return;
        if (cfg.BreadcrumbEnabled && cfg.BreadcrumbWriteOnShutdown && string.Equals(state, "android_pause", StringComparison.Ordinal))
            DiagnosticLogExporter.WriteBreadcrumb(cfg, "BREADCRUMB.WRITE", "event=" + state);
        if (cfg.LifecycleEnabled && cfg.LifecycleAndroidPauseResume)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.General,
                "LIFECYCLE." + (state ?? "UNKNOWN").ToUpperInvariant(), "application lifecycle state",
                "state=" + (state ?? ""));
        }
    }

    public static void NotifyApplicationShutdown()
    {
        var cfg = _runtimeConfig;
        if (cfg != null && cfg.LoggingEnabled && cfg.BreadcrumbEnabled && cfg.BreadcrumbWriteOnShutdown)
            DiagnosticLogExporter.WriteBreadcrumb(cfg, "BREADCRUMB.WRITE", "event=shutdown");
        // WS2：退出前刷新并关闭持续文件 sink，确保 gemuera_runtime_*.log 完整落盘。
        DiagnosticLogSinks.Shutdown();
        // 图片尺寸磁盘缓存：退出前把本次会话读取的图片头尺寸落盘，二次启动免读头。
        uEmuera.Drawing.ImageSizeCache.Save();
    }

    public static void SamplePerformanceFrame(double deltaSeconds, int gpuRenderQueueCount, int textRenderQueueCount,
        string rendererIdentity)
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.LoggingEnabled || !cfg.PerformanceSamplingEnabled)
            return;

        double frameMs = Math.Max(0.0, deltaSeconds * 1000.0);
        _performanceFrameMsTotal += frameMs;
        if (frameMs >= _performanceFrameMsMax)
        {
            _performanceFrameMsMax = frameMs;
            _performanceFrameMaxUiPending = Volatile.Read(ref pendingUiActions);
            _performanceFrameMaxDisplayPending = Volatile.Read(ref pendingDisplayActions);
            _performanceFrameMaxGpuRenderQueue = gpuRenderQueueCount;
            _performanceFrameMaxTextRenderQueue = textRenderQueueCount;
        }
        _performanceFrameCount++;

        long nowMs = GetTickMs();
        long interval = Math.Max(250, cfg.PerformanceSamplingIntervalMs);
        if (_lastPerformanceSampleMs != 0 && nowMs - _lastPerformanceSampleMs < interval)
            return;
        _lastPerformanceSampleMs = nowMs;

        int count = Math.Max(1, _performanceFrameCount);
        double avg = _performanceFrameMsTotal / count;
        double max = _performanceFrameMsMax;
        int maxUiPending = _performanceFrameMaxUiPending;
        int maxDisplayPending = _performanceFrameMaxDisplayPending;
        int maxGpuRenderQueue = _performanceFrameMaxGpuRenderQueue;
        int maxTextRenderQueue = _performanceFrameMaxTextRenderQueue;
        _performanceFrameMsTotal = 0.0;
        _performanceFrameMsMax = 0.0;
        _performanceFrameMaxUiPending = 0;
        _performanceFrameMaxDisplayPending = 0;
        _performanceFrameMaxGpuRenderQueue = 0;
        _performanceFrameMaxTextRenderQueue = 0;
        _performanceFrameCount = 0;

        var data = new StringBuilder(160);
        if (cfg.PerformanceSamplingIncludeFps)
            data.Append("fps=").Append(Engine.GetFramesPerSecond()).Append(' ');
        if (cfg.PerformanceSamplingIncludeFrameMs)
            data.Append("frame_ms_avg=").Append(avg.ToString("0.###")).Append(" frame_ms_max=").Append(max.ToString("0.###")).Append(' ');
        if (cfg.PerformanceSamplingIncludeUiQueue)
        {
            data.Append("ui_pending=").Append(Volatile.Read(ref pendingUiActions))
                .Append(" display_pending=").Append(Volatile.Read(ref pendingDisplayActions));
            if (cfg.PerformanceSamplingIncludeFrameMs)
                data.Append(" frame_max_ui_pending=").Append(maxUiPending)
                    .Append(" frame_max_display_pending=").Append(maxDisplayPending);
            data.Append(' ');
        }
        if (cfg.PerformanceSamplingIncludeTextureQueue)
        {
            data.Append("gpu_render_queue=").Append(gpuRenderQueueCount)
                .Append(" text_render_queue=").Append(textRenderQueueCount);
            if (cfg.PerformanceSamplingIncludeFrameMs)
                data.Append(" frame_max_gpu_render_queue=").Append(maxGpuRenderQueue)
                    .Append(" frame_max_text_render_queue=").Append(maxTextRenderQueue);
            data.Append(' ');
        }
        if (cfg.PerformanceSamplingIncludeRingBuffer)
            data.Append("ring_count=").Append(DiagnosticLogSinks.RingCount)
                .Append(" ring_capacity=").Append(DiagnosticLogSinks.RingCapacity).Append(' ');
        if (cfg.PerformanceSamplingIncludeDroppedCount)
            data.Append("dropped=").Append(DiagnosticLogRouter.GetDroppedTotal()).Append(' ');
        if (cfg.PerformanceSamplingIncludeMemory)
            data.Append("static_memory=").Append(OS.GetStaticMemoryUsage()).Append(' ');
        if (!string.IsNullOrWhiteSpace(rendererIdentity))
            data.Append(rendererIdentity).Append(' ');

        // 企业级说明：性能采样是低频诊断事件，只在显式开启后每 interval 输出一次。
        // 采样数据写入 ring buffer，不在每帧构造日志文本，避免诊断系统反向拖慢 APK。
        DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Performance,
            "PERF.SAMPLE", "performance sample", data.ToString().TrimEnd());
    }

    static void ResetConsoleRenderSampling()
    {
        _lastConsoleRenderSampleMs = 0;
        _consoleRenderCallbackCount = 0;
        _consoleRenderDrawWindow.Reset();
        _consoleRenderHitRebuildWindow.Reset();
    }

    static void ResetDisplayBridgeSampling()
    {
        _lastDisplayBridgeSampleMs = 0;
        _displayBridgeSamplingWindow.Reset();
    }

    /// <summary>
    /// 记录一次 uEmuera 显示桥刷新。传入值只在性能采样显式开启时聚合，不生成高频日志。
    /// </summary>
    public static void SampleDisplayBridge(double snapshotMs, double diffMs, int snapshotLines, int removeBottom,
        int addLines, int dataOnlyLines, bool cbgSubmitted)
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.LoggingEnabled || !cfg.PerformanceSamplingEnabled)
            return;

        _displayBridgeSamplingWindow.AddBridge(snapshotMs, diffMs, snapshotLines, removeBottom,
            addLines, dataOnlyLines, cbgSubmitted);

        long nowMs = GetTickMs();
        long interval = Math.Max(250, cfg.PerformanceSamplingIntervalMs);
        if (_lastDisplayBridgeSampleMs != 0 && nowMs - _lastDisplayBridgeSampleMs < interval)
            return;
        _lastDisplayBridgeSampleMs = nowMs;

        var window = _displayBridgeSamplingWindow;
        var data = new StringBuilder(420);
        data.Append("refreshes=").Append(window.Refreshes)
            .Append(" snapshot_ms_avg=").Append(FormatDiagnosticNumber(window.AverageSnapshotMs))
            .Append(" snapshot_ms_max=").Append(FormatDiagnosticNumber(window.SnapshotMsMax))
            .Append(" diff_ms_avg=").Append(FormatDiagnosticNumber(window.AverageDiffMs))
            .Append(" diff_ms_max=").Append(FormatDiagnosticNumber(window.DiffMsMax))
            .Append(" apply_calls=").Append(window.ApplyCalls)
            .Append(" apply_ms_avg=").Append(FormatDiagnosticNumber(window.AverageApplyMs))
            .Append(" apply_ms_max=").Append(FormatDiagnosticNumber(window.ApplyMsMax))
            .Append(" snapshot_lines_avg=").Append(window.AverageSnapshotLines)
            .Append(" snapshot_lines_max=").Append(window.SnapshotLinesMax)
            .Append(" remove_bottom=").Append(window.RemoveBottomTotal)
            .Append(" add_lines=").Append(window.AddLinesTotal)
            .Append(" data_only=").Append(window.DataOnlyLinesTotal)
            .Append(" fallback_built=").Append(window.FallbackBuilds)
            .Append(" fallback_replaced=").Append(window.FallbackReplacements)
            .Append(" cbg_submits=").Append(window.CbgSubmits);

        DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Performance,
            "PERF.DISPLAY_BRIDGE", "display bridge performance sample", data.ToString());
        ResetDisplayBridgeSampling();
        _lastDisplayBridgeSampleMs = nowMs;
    }

    public static void RecordDisplayFallbackLineBuild(bool replacing)
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.LoggingEnabled || !cfg.PerformanceSamplingEnabled)
            return;
        _displayBridgeSamplingWindow.AddFallbackBuild(replacing);
    }

    public static void RecordDisplayApplyBatch(double elapsedMs)
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.LoggingEnabled || !cfg.PerformanceSamplingEnabled)
            return;
        _displayBridgeSamplingWindow.AddApply(elapsedMs);
    }

    public static void SampleConsoleRenderFrame(double elapsedMs, bool draw, bool rebuildHits,
        int visibleRows, int canvasRows, int overlayRows, int drawnParts, int rebuiltHitRects,
        Func<string> snapshotDataFactory)
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.LoggingEnabled || !cfg.PerformanceSamplingEnabled)
            return;
        if (!draw && !rebuildHits)
            return;

        double safeElapsedMs = Math.Max(0.0, elapsedMs);
        _consoleRenderCallbackCount++;
        if (draw)
            _consoleRenderDrawWindow.Add(safeElapsedMs, visibleRows, canvasRows, overlayRows, drawnParts, rebuiltHitRects);
        if (rebuildHits)
            _consoleRenderHitRebuildWindow.Add(safeElapsedMs, visibleRows, canvasRows, overlayRows, drawnParts, rebuiltHitRects);

        long nowMs = GetTickMs();
        long interval = Math.Max(250, cfg.PerformanceSamplingIntervalMs);
        if (_lastConsoleRenderSampleMs != 0 && nowMs - _lastConsoleRenderSampleMs < interval)
            return;
        _lastConsoleRenderSampleMs = nowMs;

        string snapshotData = "";
        try
        {
            snapshotData = snapshotDataFactory?.Invoke() ?? "";
        }
        catch (Exception ex)
        {
            snapshotData = "snapshot_error=" + ex.GetType().Name;
        }

        var drawWindow = _consoleRenderDrawWindow;
        var hitRebuildWindow = _consoleRenderHitRebuildWindow;
        var data = new StringBuilder(560);
        data.Append("backend=canvas")
            .Append(" sample_callbacks=").Append(_consoleRenderCallbackCount)
            .Append(" draw_callbacks=").Append(drawWindow.Callbacks)
            .Append(" draw_ms_avg=").Append(FormatDiagnosticNumber(drawWindow.AverageMs))
            .Append(" draw_ms_p95=").Append(FormatDiagnosticNumber(drawWindow.GetP95Ms()))
            .Append(" draw_ms_max=").Append(FormatDiagnosticNumber(drawWindow.ElapsedMsMax))
            .Append(" draw_sample_overflow=").Append(drawWindow.SampleOverflow)
            .Append(" draw_visible_rows_avg=").Append(drawWindow.AverageVisibleRows)
            .Append(" draw_visible_rows_max=").Append(drawWindow.VisibleRowsMax)
            .Append(" draw_canvas_rows_avg=").Append(drawWindow.AverageCanvasRows)
            .Append(" draw_canvas_rows_max=").Append(drawWindow.CanvasRowsMax)
            .Append(" draw_overlay_rows_avg=").Append(drawWindow.AverageOverlayRows)
            .Append(" draw_overlay_rows_max=").Append(drawWindow.OverlayRowsMax)
            .Append(" draw_parts_avg=").Append(drawWindow.AverageDrawnParts)
            .Append(" draw_parts_max=").Append(drawWindow.DrawnPartsMax)
            .Append(" hit_rebuild_callbacks=").Append(hitRebuildWindow.Callbacks)
            .Append(" hit_rebuild_ms_avg=").Append(FormatDiagnosticNumber(hitRebuildWindow.AverageMs))
            .Append(" hit_rebuild_ms_p95=").Append(FormatDiagnosticNumber(hitRebuildWindow.GetP95Ms()))
            .Append(" hit_rebuild_ms_max=").Append(FormatDiagnosticNumber(hitRebuildWindow.ElapsedMsMax))
            .Append(" hit_rebuild_sample_overflow=").Append(hitRebuildWindow.SampleOverflow)
            .Append(" hit_rebuild_visible_rows_avg=").Append(hitRebuildWindow.AverageVisibleRows)
            .Append(" hit_rebuild_visible_rows_max=").Append(hitRebuildWindow.VisibleRowsMax)
            .Append(" hit_rebuild_canvas_rows_avg=").Append(hitRebuildWindow.AverageCanvasRows)
            .Append(" hit_rebuild_canvas_rows_max=").Append(hitRebuildWindow.CanvasRowsMax)
            .Append(" hit_rebuild_overlay_rows_avg=").Append(hitRebuildWindow.AverageOverlayRows)
            .Append(" hit_rebuild_overlay_rows_max=").Append(hitRebuildWindow.OverlayRowsMax)
            .Append(" hit_rects_avg=").Append(hitRebuildWindow.AverageRebuiltHitRects)
            .Append(" hit_rects_max=").Append(hitRebuildWindow.RebuiltHitRectsMax);
        if (!string.IsNullOrEmpty(snapshotData))
            data.Append(' ').Append(snapshotData.Trim());

        // 性能采样打开时才聚合输出，默认 APK 不会进入这里。draw_* 和 hit_rebuild_*
        // 都是 Canvas 回调的 CPU 时间；真实 GPU draw call 需要另读 Godot Performance monitor。
        DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Performance,
            "PERF.CONSOLE_RENDER", "console render performance sample", data.ToString());
        ResetConsoleRenderSampling();
        _lastConsoleRenderSampleMs = nowMs;
    }

    static string FormatDiagnosticNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
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
        if (gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
            gEmuera.LegacyRunner.LegacyTrace.TryRecordDisplayProjection("remove_bottom", count, 0, false, -1, "preserve_viewport", 0);
        EnqueueUI(() => EmueraContent.instance?.RemoveBottomLines(count), true);
    }

    public static void AddText(ConsoleDisplayLine line, bool update)
    {
        if (gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
            gEmuera.LegacyRunner.LegacyTrace.TryRecordDisplayProjection("append", 0, line == null ? 0 : 1, update, -1, "unspecified", 0);
        EnqueueUI(() => EmueraContent.instance?.AddLine(line, update), true);
    }

    public static void AddTexts(IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines)
    {
        if (gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
            gEmuera.LegacyRunner.LegacyTrace.TryRecordDisplayProjection("append_batch", 0, lines?.Count ?? 0, false, -1, "unspecified", 0);
        EnqueueUI(() => EmueraContent.instance?.AddLines(lines), true);
    }

    public static void ApplyTextChanges(int removeBottomCount, IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines, bool update,
        int lastButtonGeneration, bool scrollToBottom = true, IReadOnlyList<ConsoleDisplayLine> dataOnlyLines = null)
    {
        ApplyTextChanges(removeBottomCount, lines, update, lastButtonGeneration,
            scrollToBottom ? EmueraDisplayScrollMode.FollowBottom : EmueraDisplayScrollMode.PreserveViewport,
            dataOnlyLines);
    }

	public static void ApplyTextChanges(int removeBottomCount, IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines, bool update,
		int lastButtonGeneration, EmueraDisplayScrollMode scrollMode, IReadOnlyList<ConsoleDisplayLine> dataOnlyLines = null)
	{
		if (gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
		{
			gEmuera.LegacyRunner.LegacyTrace.TryRecordDisplayProjection("apply_text_changes", removeBottomCount, lines?.Count ?? 0,
				update, lastButtonGeneration, scrollMode.ToString(), dataOnlyLines?.Count ?? 0);
		}
		EnqueueUI(() => EmueraContent.instance?.ApplyTextChanges(removeBottomCount, lines, update, lastButtonGeneration, scrollMode, dataOnlyLines), true);
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

    /// <summary>
    /// 将 CBG 提交限制为实际展示内容发生变化的时刻。文本/地图计时器可以高频刷新，
    /// 但静态背景不应因此重复复制图层、重新 pin 纹理并让同一批 CanvasItem 失效。
    /// </summary>
    public static bool RefreshCBG(EmueraConsole console)
    {
        if (console == null)
            return false;

        List<EmueraConsole.ClientBackGroundImage> cbgList;
        lock (cbgRefreshStateLock)
        {
            if (!ReferenceEquals(lastCbgSnapshotConsole, console))
            {
                lastCbgSnapshotConsole = console;
                lastCbgSnapshotRevision = int.MinValue;
            }
            if (!console.TryGetCBGListSnapshot(lastCbgSnapshotRevision, out int revision, out cbgList))
                return false;
            lastCbgSnapshotRevision = revision;
        }

        EnqueueUI(() => EmueraContent.instance?.RefreshCBG(cbgList), true);
        return true;
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
