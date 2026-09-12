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

internal static class GenericUtils
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
    const int SnakeSoundChannelCount = 10;
    static readonly object snakeAudioLock = new object();
    static readonly SnakeAudioState[] snakeSounds = CreateSnakeAudioStates();
    static readonly SnakeAudioState snakeBgm = new SnakeAudioState();
    static readonly object soundFallbackResolveCacheLock = new object();
    static readonly Dictionary<string, string> soundFallbackResolveCache = new Dictionary<string, string>();
    static readonly object inputStateLock = new object();
    static uEmuera.Drawing.Point pointerPosition = uEmuera.Drawing.Point.Empty;
    static string pointingButtonInput = "";
    static long pointingButtonGeneration = long.MinValue;
    static bool pointingButtonActive = false;
    static int scrollTraceEnabled = 0;
    static int scrollTraceSequence = 0;
    static int scrollTraceCoreLinesRemaining = 0;
    static long dynamicMapLastContextTickMs = long.MinValue;
    const string ScrollTracePrefix = "[SCROLL_TRACE]";
    const int ScrollTraceCoreBurstLineCount = 120;
    const int MaxLogMessageChars = 8192;
#if DEBUG || GEMUERA_DIAGNOSTIC_LOGS
    const bool VerboseLogBuild = true;
#else
    const bool VerboseLogBuild = false;
#endif
    static int runtimeLogLevel = (int)EmueraLogLevel.None;
    static int runtimeLogCategories = (int)EmueraLogCategory.None;
    static int mirrorNonErrorLogsToGodot = 0;
    static int loggingInitialized = 0;
    static RuntimeDiagnosticsConfig _runtimeConfig;
    static InputReplayBuffer _inputReplay;
    const int SaveLogOperationTrailCapacity = 5;
    static readonly SaveLogOperationTrail _saveLogOperationTrail = new SaveLogOperationTrail(SaveLogOperationTrailCapacity);
    static string _runtimeGamePath = "";
    static string _runtimeCoreProfile = "";
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
    public static bool ScrollTraceEnabled
    {
        get => Volatile.Read(ref scrollTraceEnabled) != 0;
        set => Volatile.Write(ref scrollTraceEnabled, value ? 1 : 0);
    }

    public static bool IsDiagnosticsLoggingEnabled => _runtimeConfig?.LoggingEnabled ?? false;

    public static bool IsScrollTraceActive => ScrollTraceEnabled && IsLogEnabled(EmueraLogLevel.Debug, EmueraLogCategory.Script);

    public static bool IsSaveLogOperationCaptureEnabled
    {
        get
        {
            var cfg = _runtimeConfig;
            return cfg != null && cfg.LoggingEnabled;
        }
    }

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
        ResetAutoDiagnosticExport();

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

    public static string GetPointingButtonInput()
    {
        lock (inputStateLock)
            return pointingButtonActive ? pointingButtonInput ?? "" : "";
    }

    public static void FlushLogs()
    {
        // 新诊断系统已统一在 LogInternal 中按主线程/非主线程策略直接写入，
        // 旧日志刷新入口保留为空实现，避免调用方编译错误。
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

    public static EmueraLogLevel RuntimeLogLevel
    {
        get => (EmueraLogLevel)Volatile.Read(ref runtimeLogLevel);
        set => Volatile.Write(ref runtimeLogLevel, (int)value);
    }

    public static EmueraLogCategory RuntimeLogCategories
    {
        get => (EmueraLogCategory)Volatile.Read(ref runtimeLogCategories);
        set => Volatile.Write(ref runtimeLogCategories, (int)value);
    }

    public static bool MirrorNonErrorLogsToGodot
    {
        get => Volatile.Read(ref mirrorNonErrorLogsToGodot) != 0;
        set => Volatile.Write(ref mirrorNonErrorLogsToGodot, value ? 1 : 0);
    }

    /// <summary>
    /// 企业级说明：日志初始化只做一次 TOML 配置解析，将开关展开为已解析的布尔值。
    /// 后续热路径只读取展开后的 int/布尔，不再查询字典或解析字符串，避免 Android 帧尖峰。
    /// 默认 Release/APK 只输出 Error，Debug/诊断构建可按 config.toml 开启模块。
    /// </summary>
    public static void InitializeLogging()
    {
        // 企业级说明：Android APK 冷启动可能先进入启动器界面，随后才进入实际模拟器主场景。
        // 日志系统必须允许更早初始化，但不能在场景切换时重置 ring buffer、session 或 breadcrumb。
        if (Interlocked.CompareExchange(ref loggingInitialized, 1, 0) != 0)
            return;

        var loadResult = RuntimeDiagnosticsConfigLoader.Load();
        _runtimeConfig = loadResult.Config ?? RuntimeDiagnosticsConfig.CreateDefault();
        string sessionId = BuildSessionId(_runtimeConfig.LoggingSessionIdFormat);
        DiagnosticLogRouter.Initialize(_runtimeConfig, sessionId);
        DiagnosticLogSinks.Initialize(_runtimeConfig);

        ApplyRuntimeDiagnosticsConfig();
        if (!_runtimeConfig.LoggingEnabled)
            return;

        DiagnosticLogExporter.WriteBreadcrumb(_runtimeConfig, "LOG.INIT", "source=GenericUtils.InitializeLogging");
        WriteConfigSelfCheck(loadResult);
        WriteAndroidStorageDiagnostics();

        if (_runtimeConfig.BreadcrumbEnabled && _runtimeConfig.BreadcrumbWriteOnStartup)
            DiagnosticLogExporter.WriteBreadcrumb(_runtimeConfig, "BREADCRUMB.WRITE", "event=startup");
        if (_runtimeConfig.RetentionEnabled && _runtimeConfig.RetentionCleanupOnStartup)
            DiagnosticLogExporter.RunRetentionCleanup(_runtimeConfig);
    }

    public static RuntimeDiagnosticsConfig GetRuntimeDiagnosticsConfig() => _runtimeConfig;

    /// <summary>
    /// 企业级说明：运行时面板保存 user://config.toml 后调用本方法热重载诊断开关。
    /// 只更新等级、类别、镜像、滚动追踪和输入复现等轻量运行时状态；ring buffer 容量等结构性参数保留下次启动生效。
    /// </summary>
    public static bool ReloadRuntimeDiagnosticsConfig(out string errorMessage)
    {
        errorMessage = "";
        var loadResult = RuntimeDiagnosticsConfigLoader.Load();
        _runtimeConfig = loadResult.Config ?? RuntimeDiagnosticsConfig.CreateDefault();
        ApplyRuntimeDiagnosticsConfig();
        DiagnosticLogRouter.Reload(_runtimeConfig);
        if (!string.IsNullOrEmpty(loadResult.ErrorMessage))
        {
            errorMessage = loadResult.ErrorMessage;
            return false;
        }
        return true;
    }

    static void ApplyRuntimeDiagnosticsConfig()
    {
        if (_runtimeConfig == null || !_runtimeConfig.LoggingEnabled)
        {
            RuntimeLogLevel = EmueraLogLevel.None;
            RuntimeLogCategories = EmueraLogCategory.None;
            MirrorNonErrorLogsToGodot = false;
            ScrollTraceEnabled = false;
            DiagnosticLogSinks.SetMirrorNonErrorToGodot(false);
            DiagnosticLogSinks.Reload(_runtimeConfig);
            RuntimeDiagnosticsPanel.SetDiagnosticsPanelVisible(false);
            _inputReplay = null;
            return;
        }

        var model = _runtimeConfig.GetActiveDebugModel();
        if (model != null && model.Enabled)
        {
            RuntimeLogLevel = RuntimeDiagnosticsConfig.ParseLogLevel(model.LogLevel);
            MirrorNonErrorLogsToGodot = model.MirrorToGodot;
            ScrollTraceEnabled = model.ScrollTrace;
        }
        else
        {
            RuntimeLogLevel = _runtimeConfig.GetRuntimeLogLevel();
            MirrorNonErrorLogsToGodot = _runtimeConfig.LoggingMirrorNonErrorToGodot;
            ScrollTraceEnabled = false;
        }

        RuntimeLogCategories = _runtimeConfig.GetActiveDebugModelCategoryMask();

        DiagnosticLogSinks.SetMirrorNonErrorToGodot(MirrorNonErrorLogsToGodot);
        DiagnosticLogSinks.Reload(_runtimeConfig);
        RuntimeDiagnosticsPanel.SetDiagnosticsPanelVisible(_runtimeConfig.RuntimePanelEnabled);
        _inputReplay = _runtimeConfig.InputReplayEnabled
            ? new InputReplayBuffer(_runtimeConfig.InputReplayMaxEvents)
            : null;
    }

    /// <summary>
    /// 运行时显示/隐藏诊断悬浮球与面板（悬浮球与面板节点均由 RuntimeDiagnosticsPanel 静态管理）。
    /// 悬浮窗未挂载（debug.runtime_panel.enabled=false 或启动时 panel_visible=false）时为空操作，
    /// 此时需重启或开启挂载门后生效。
    /// </summary>
    public static void SetDiagnosticsPanelVisible(bool visible)
    {
        RuntimeDiagnosticsPanel.SetDiagnosticsPanelVisible(visible);
    }

    static void WriteConfigSelfCheck(RuntimeDiagnosticsConfigLoader.LoadResult loadResult)
    {
        var cfg = _runtimeConfig;
        if (cfg == null)
            return;
        string activeModules = BuildActiveDiagnosticModulesSummary(cfg);
        DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Config,
            "CONFIG.SELF_CHECK", "runtime diagnostics config loaded",
            "schema_version=" + cfg.LoggingSchemaVersion
            + " file_found=" + loadResult.FileFound
            + " loaded_from=" + DiagnosticLogRouter.RedactPath(loadResult.LoadedFrom)
            + " quick_enabled=" + cfg.QuickDebugEnabled
            + " quick_preset=" + cfg.QuickDebugPreset
            + " quick_effective=" + cfg.QuickDebugEffectivePreset
            + " quick_language=" + cfg.QuickDebugEffectiveLanguage
            + " active_model=" + cfg.ActiveDebugModel
            + " runtime_level=" + cfg.GetRuntimeLogLevel().ToString().ToLowerInvariant()
            + " modules=" + activeModules);
        DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Config,
            "LOG.MODULES.ACTIVE", "active diagnostic modules",
            "modules=" + activeModules);
        if (!VerboseLogBuild && cfg.GetActiveDebugModel()?.Enabled == true)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.Config,
                "LOG.MODULE.FORCED_OFF", "non-error logs are disabled by build policy",
                "reason=release_build_without_GEMUERA_DIAGNOSTIC_LOGS");
        }
        if (cfg.QuickDebugPresetInvalid)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.Config,
                "CONFIG.QUICK_PRESET.INVALID", "invalid quick_debug preset, fallback to normal",
                "preset=" + DiagnosticLogRouter.RedactText(cfg.QuickDebugPreset, 64)
                + " effective=" + cfg.QuickDebugEffectivePreset);
        }
        if (cfg.QuickDebugLanguageInvalid)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.Config,
                "CONFIG.QUICK_LANGUAGE.INVALID", "invalid quick_debug language, fallback to zh_cn",
                "language=" + DiagnosticLogRouter.RedactText(cfg.QuickDebugLanguage, 32)
                + " effective=" + cfg.QuickDebugEffectiveLanguage);
        }
    }

    static string BuildActiveDiagnosticModulesSummary(RuntimeDiagnosticsConfig cfg)
    {
        var parts = new List<string>(16);
        if (cfg.TouchEnabled) parts.Add("touch");
        if (cfg.InputDebugEnabled) parts.Add("input");
        if (cfg.StatementRecognitionEnabled) parts.Add("statement_recognition");
        if (cfg.ImageDebugEnabled) parts.Add("image");
        if (cfg.UiLayoutEnabled) parts.Add("ui_layout");
        if (cfg.DynamicMapDebugEnabled) parts.Add("dynamic_map");
        if (cfg.RuntimePanelEnabled) parts.Add("runtime_panel");
        if (cfg.InputReplayEnabled) parts.Add("input_replay");
        if (cfg.AndroidStorageEnabled) parts.Add("android_storage");
        if (cfg.PerformanceSamplingEnabled) parts.Add("performance_sampling");
        if (cfg.SnapshotEnabled) parts.Add("snapshot");
        if (cfg.UiOverlayEnabled) parts.Add("ui_overlay");
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }

    static void WriteAndroidStorageDiagnostics()
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.AndroidStorageEnabled || OS.GetName() != "Android")
            return;

        string root = "/storage/emulated/0/emuera";
        bool rootExists = Directory.Exists(root);
        if (cfg.AndroidStorageLogPermissions)
        {
            string permissions = BuildGrantedPermissionSummary();
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.FileSystem,
                "ANDROID_STORAGE.PERMISSION", "android storage permission summary",
                "platform=" + OS.GetName()
                + " mobile=" + OS.HasFeature("mobile")
                + " permissions=" + permissions);
        }
        if (cfg.AndroidStorageLogScopedStorage)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.FileSystem,
                "ANDROID_STORAGE.SCOPED_STORAGE", "android scoped storage summary",
                "root=" + root + " root_exists=" + rootExists + " max_path_records=" + cfg.AndroidStorageMaxPathRecords);
        }
        if (cfg.AndroidStorageLogGameScan)
        {
            int eraCount = CountEraDirectories(root, cfg.AndroidStorageMaxPathRecords, out string sample);
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.FileSystem,
                "ANDROID_STORAGE.GAME_SCAN", "android game directory scan summary",
                "root=" + root + " root_exists=" + rootExists + " era_count=" + eraCount + " sample=" + sample);
        }
        if (cfg.AndroidStorageLogReadWriteFailures)
            TryAndroidStorageWriteProbe(root);
    }

    static string BuildGrantedPermissionSummary()
    {
        try
        {
            var permissions = OS.GetGrantedPermissions();
            if (permissions == null || permissions.Length == 0)
                return "none";
            return string.Join(",", permissions);
        }
        catch (Exception ex)
        {
            return "unavailable:" + ex.GetType().Name;
        }
    }

    static int CountEraDirectories(string root, int maxRecords, out string sample)
    {
        var samples = new List<string>(Math.Max(1, Math.Min(maxRecords, 16)));
        int count = CountEraDirectoriesRecursive(root, 0, 2, Math.Max(1, maxRecords), samples);
        sample = samples.Count == 0 ? "" : string.Join("|", samples);
        return count;
    }

    static int CountEraDirectoriesRecursive(string path, int depth, int maxDepth, int maxRecords, List<string> samples)
    {
        if (string.IsNullOrEmpty(path) || depth > maxDepth || !Directory.Exists(path))
            return 0;
        int count = 0;
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(path))
            {
                if (IsEraDirectory(dir))
                {
                    count++;
                    if (samples.Count < maxRecords)
                        samples.Add(DiagnosticLogRouter.RedactPath(dir));
                }
                if (depth < maxDepth)
                    count += CountEraDirectoriesRecursive(dir, depth + 1, maxDepth, maxRecords, samples);
                if (samples.Count >= maxRecords && count >= maxRecords)
                    break;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.FileSystem,
                "ANDROID_STORAGE.READ_FAIL", "android game directory scan failed",
                "path=" + DiagnosticLogRouter.RedactPath(path) + " error=" + ex.GetType().Name);
        }
        return count;
    }

    static bool IsEraDirectory(string path)
    {
        return Directory.Exists(System.IO.Path.Combine(path, "ERB"))
            || Directory.Exists(System.IO.Path.Combine(path, "erb"));
    }

    static void TryAndroidStorageWriteProbe(string root)
    {
        if (!Directory.Exists(root))
            return;
        string probe = System.IO.Path.Combine(root, ".gemuera_write_probe.tmp");
        try
        {
            File.WriteAllText(probe, "probe", Encoding.UTF8);
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            DiagnosticLogExporter.WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.FileSystem,
                "ANDROID_STORAGE.WRITE_FAIL", "android storage write probe failed",
                "path=" + DiagnosticLogRouter.RedactPath(probe) + " error=" + ex.GetType().Name);
        }
    }

    /// <summary>
    /// 企业级说明：所有诊断日志的运行时总闸门。
    /// Release/APK 默认只允许 Error；诊断构建中也必须同时通过等级、类别和模块开关，防止误开高频日志拖慢手机。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLogEnabled(EmueraLogLevel level, EmueraLogCategory category = EmueraLogCategory.General)
    {
        if (!(_runtimeConfig?.LoggingEnabled ?? false))
            return false;
        if (level == EmueraLogLevel.None)
            return false;
        if (!VerboseLogBuild && level < EmueraLogLevel.Error)
            return false;
        if ((int)level < Volatile.Read(ref runtimeLogLevel))
            return false;
        if (level < EmueraLogLevel.Error
            && category != EmueraLogCategory.None
            && (Volatile.Read(ref runtimeLogCategories) & (int)category) == 0)
        {
            return false;
        }
        return true;
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Debug(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Debug, category, content, null, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Debug(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Debug, category, null, messageFactory, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Info(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Info, category, content, null, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Info(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Info, category, null, messageFactory, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Warn(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Warn, category, content, null, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Warn(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Warn, category, null, messageFactory, member, file, line);
    }

    public static void Error(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Error, category, content, null, member, file, line);
    }

    public static void Error(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Error, category, null, messageFactory, member, file, line);
    }

    public static void LogFromBridge(EmueraLogLevel level, EmueraLogCategory category, object content,
        Func<string> messageFactory, string member, string file, int line)
    {
        LogInternal(level, category, content, messageFactory, member, file, line);
    }

    /// <summary>
    /// 企业级说明：所有日志统一经 DiagnosticLogRouter 做开关/限流判断，再经 DiagnosticLogSinks 写入 ring buffer 和 Godot 控制台。
    /// 关闭日志时不构造 message，不对 category 做额外归一化，避免热路径分配。
    /// 限流检查在 message 构造之前，若被限流则直接返回，不产生字符串。
    /// </summary>
    static void LogInternal(EmueraLogLevel level, EmueraLogCategory category, object content,
        Func<string> messageFactory, string member, string file, int line)
    {
        if (!IsLogEnabled(level, category))
            return;

        // 先用初始 category 推导 event_id 做限流预检，避免构造 message 后被限流浪费分配。
        string preliminaryEventId = DeriveEventId(category, level);
        if (!DiagnosticLogRouter.IsEnabled(level, category, preliminaryEventId))
            return;

        string message = BuildLogMessage(content, messageFactory);
        category = NormalizeLogCategory(category, message);
        string eventId = DeriveEventId(category, level);

        string source = NormalizeSourcePath(file);
        string data = AppendCorrelationData(category, eventId, "", source, line);
        var record = DiagnosticLogRouter.BuildRecord(level, category, eventId, message, data, member, source, line);
        DiagnosticLogSinks.Write(record);

        if (level >= EmueraLogLevel.Error)
            DiagnosticLogExporter.NotifyError(eventId);
        else
            DiagnosticLogExporter.NotifyEvent(eventId);
    }

    /// <summary>
    /// 企业级说明：结构化诊断日志专用入口，接受调用方显式传入的 event_id 和 data。
    /// 不走 DeriveEventId 派生，确保 TOUCH.* / INPUT.* / IMAGE.* / UI_LAYOUT.* 等稳定事件 ID 原样写入记录。
    /// 限流使用显式 eventId，保证同一事件 ID 有独立限流桶。
    /// messageFactory 为延迟构造，仅在开关和限流通过后执行。
    /// </summary>
    static void LogStructured(EmueraLogLevel level, EmueraLogCategory category,
        string eventId, string data,
        Func<string> messageFactory,
        string member, string file, int line)
    {
        LogStructured(level, category, eventId, () => data, messageFactory, member, file, line);
    }

    /// <summary>
    /// 企业级说明：结构化 data 也必须延迟构造。Android 触摸、图片和 UI 几何日志开启后仍可能被限流，
    /// 因此 message/data 都只能在开关、类别和事件限流全部通过后再生成。
    /// </summary>
    static void LogStructured(EmueraLogLevel level, EmueraLogCategory category,
        string eventId, Func<string> dataFactory,
        Func<string> messageFactory,
        string member, string file, int line)
    {
        if (!IsLogEnabled(level, category))
            return;
        if (string.IsNullOrEmpty(eventId))
            eventId = DeriveEventId(category, level);

        // 限流预检：使用显式 event_id，避免构造 message 后被限流浪费分配。
        if (!DiagnosticLogRouter.IsEnabled(level, category, eventId))
            return;

        string source = NormalizeSourcePath(file);
        string message = BuildLogMessage(null, messageFactory);
        string data = AppendCorrelationData(category, eventId, BuildLogData(dataFactory), source, line);
        var record = DiagnosticLogRouter.BuildRecord(level, category, eventId, message, data ?? "", member, source, line);
        DiagnosticLogSinks.Write(record);

        if (level >= EmueraLogLevel.Error)
            DiagnosticLogExporter.NotifyError(eventId);
        else
            DiagnosticLogExporter.NotifyEvent(eventId);
    }

    static string BuildLogMessage(object content, Func<string> messageFactory)
    {
        string message;
        try
        {
            message = messageFactory != null ? messageFactory() : content?.ToString();
        }
        catch (Exception ex)
        {
            message = "[LOGGER] message factory failed: " + ex.GetType().Name + ": " + ex.Message;
        }
        return ClipLogMessage(message);
    }

    static string BuildLogData(Func<string> dataFactory)
    {
        if (dataFactory == null)
            return "";
        try
        {
            return dataFactory() ?? "";
        }
        catch (Exception ex)
        {
            return "[LOGGER] data factory failed: " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    static string AppendCorrelationData(EmueraLogCategory category, string eventId, string data, string source, int line)
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.CorrelationEnabled)
            return data ?? "";

        string result = data ?? "";
        // 企业级说明：关联字段在日志通过开关和限流之后追加，避免关闭调试时产生字符串分配。
        // 字段采用轻量整数或短哈希，不在 Android 热路径生成 GUID。
        if (cfg.CorrelationSessionId && !ContainsDataKey(result, "session_id"))
            result = AppendDataField(result, "session_id=" + DiagnosticLogRouter.SessionId);
        if (cfg.CorrelationInputId && !ContainsDataKey(result, "input_id"))
            result = AppendDataField(result, "input_id=" + CurrentInputId);
        if (cfg.CorrelationRenderBatchId
            && (category == EmueraLogCategory.UI || category == EmueraLogCategory.Sprite)
            && !ContainsDataKey(result, "render_batch_id"))
        {
            result = AppendDataField(result, "render_batch_id=" + UiFrameGeneration);
        }
        if (cfg.CorrelationLinePartId
            && (category == EmueraLogCategory.UI || category == EmueraLogCategory.Sprite || category == EmueraLogCategory.Script)
            && !ContainsDataKey(result, "line_part_id"))
        {
            result = AppendDataField(result, "line_part_id=" + BuildLinePartId(source, line));
        }
        if (cfg.CorrelationImageId && category == EmueraLogCategory.Sprite && !ContainsDataKey(result, "image_id"))
            result = AppendDataField(result, "image_id=" + BuildImageCorrelationId(eventId, result));
        return result;
    }

    static bool ContainsDataKey(string data, string key)
    {
        return !string.IsNullOrEmpty(data) && data.Contains(key + "=", StringComparison.Ordinal);
    }

    static string AppendDataField(string data, string field)
    {
        if (string.IsNullOrEmpty(data))
            return field;
        return data + " " + field;
    }

    static string BuildLinePartId(string source, int line)
    {
        string src = string.IsNullOrEmpty(source) ? "unknown" : System.IO.Path.GetFileNameWithoutExtension(source);
        return src + ":" + line;
    }

    static string BuildImageCorrelationId(string eventId, string data)
    {
        string key = ExtractDataValue(data, "resource");
        if (string.IsNullOrEmpty(key))
            key = ExtractDataValue(data, "name");
        if (string.IsNullOrEmpty(key))
            key = ExtractDataValue(data, "filename");
        if (string.IsNullOrEmpty(key))
            key = eventId ?? "image";
        uint hash = 2166136261u;
        foreach (char c in key)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return "img_" + hash.ToString("x8");
    }

    static string ExtractDataValue(string data, string key)
    {
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(key))
            return "";
        string prefix = key + "=";
        foreach (var part in data.Split(' '))
        {
            if (part.StartsWith(prefix, StringComparison.Ordinal))
                return part.Substring(prefix.Length);
        }
        return "";
    }

    static string DeriveEventId(EmueraLogCategory category, EmueraLogLevel level)
    {
        switch (category)
        {
            case EmueraLogCategory.Sprite: return "SPRITE." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Audio: return "AUDIO." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Input: return "INPUT." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Script: return "SCRIPT." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.UI: return "UI." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.FileSystem: return "FS." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Load: return "LOAD." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Save: return "SAVE." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Config: return "CONFIG." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Performance: return "PERF." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.Touch: return "TOUCH." + level.ToString().ToUpperInvariant();
            case EmueraLogCategory.StatementRecognition: return "PARSER." + level.ToString().ToUpperInvariant();
            default: return "LOG." + level.ToString().ToUpperInvariant();
        }
    }

    public static string GetDefaultDiagnosticLogPath(string stamp = null)
    {
        if (string.IsNullOrEmpty(stamp))
            stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return $"{DiagnosticLogExporter.GameDirectoryPathPrefix}gemuera_{stamp}.log";
    }

    public static string ResolveDiagnosticPathForDisplay(string path)
    {
        return DiagnosticLogExporter.ResolvePathForDisplay(path);
    }

    static string BuildSessionId(string format)
    {
        // 企业级说明：session_id_format 来自可编辑 TOML，不能让格式错误阻断 APK 启动。
        // 格式异常时回退到稳定默认值，并继续让配置自检日志记录实际运行状态。
        if (string.IsNullOrWhiteSpace(format))
            format = "yyyyMMdd-HHmmss";
        try
        {
            return DateTime.Now.ToString(format);
        }
        catch (FormatException)
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }
    }

    /// <summary>
    /// 企业级说明：导出日志只在用户主动触发时写盘，Android 游玩期间不产生文件 I/O。
    /// 委托 DiagnosticLogExporter 生成带报告头的结构化日志。
    /// </summary>
    public static bool ExportDiagnosticLog(string path, out string errorMessage)
    {
        errorMessage = "";
        if (!(_runtimeConfig?.LoggingEnabled ?? false))
        {
            errorMessage = "diagnostics_disabled";
            return false;
        }
        if (string.IsNullOrEmpty(path))
            path = GetDefaultDiagnosticLogPath();

        if (_runtimeConfig != null && _runtimeConfig.BreadcrumbWriteOnExport)
            DiagnosticLogExporter.WriteBreadcrumb(_runtimeConfig, "BREADCRUMB.WRITE", "event=before_export path=" + path);

        string gamePath = _runtimeGamePath;
        string coreProfile = _runtimeCoreProfile;
        //报告头必须反映真实运行配置：此前硬编码 false，误导排障（20260819 华扇口上日志实证）。
        bool useLazyLoading = MinorShift.Emuera.Config.UseLazyLoading && MinorShift.Emuera.Program.SupportsLazyLoading;
        bool ok = DiagnosticLogExporter.ExportDiagnosticLog(_runtimeConfig, path, gamePath, coreProfile, useLazyLoading, _saveLogOperationTrail, out errorMessage);

        if (_runtimeConfig != null && _runtimeConfig.BreadcrumbWriteOnExport)
            DiagnosticLogExporter.WriteBreadcrumb(_runtimeConfig, "BREADCRUMB.WRITE", "event=after_export ok=" + ok);
        return ok;
    }

    // —— emuera.log 自动触发诊断导出（2026-09-12）——
    // 引擎在游戏报错路径自动写出 emuera.log（启动致命错误/解析警告容错/运行中错误）。
    // 此前 gemuera 侧诊断日志只能靠菜单手工导出；现在首个 emuera.log 落盘时会话内
    // 自动导出一次（含错误上下文，最有排障价值），后续报错不再重复导出文件。
    static int autoDiagnosticExported;

    /// <summary>
    /// 引擎自动写出 emuera.log 时调用：会话内首次触发 gemuera 诊断导出（主线程执行）。
    /// 幂等；线程安全（Interlocked 抢占 + EnqueueUI 切主线程，导出与手工触发同路径）。
    /// </summary>
    public static void AutoExportDiagnosticOnEmueraLog(string emueraLogPath)
    {
        if (System.Threading.Interlocked.Exchange(ref autoDiagnosticExported, 1) != 0)
            return;
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        EnqueueUI(() =>
        {
            string diagnosticPath = GetDefaultDiagnosticLogPath("auto_" + stamp);
            // 触发记录写在导出之前：让 auto 日志自身包含触发链（emuera.log 路径），
            // 排障时单文件自足；导出结果另由后续日志/面板可见。
            Info(EmueraLogCategory.General, () =>
                $"[SaveLog] auto-export triggered by emuera.log at {emueraLogPath}; exporting to {diagnosticPath}");
            ExportDiagnosticLog(diagnosticPath, out _);
        });
    }

    /// <summary>会话重置时清自动导出标志（与 _saveLogOperationTrail 等会话态一致）。</summary>
    static void ResetAutoDiagnosticExport()
    {
        System.Threading.Interlocked.Exchange(ref autoDiagnosticExported, 0);
    }

    public static bool ExportDiagnosticPackage(string outputDirectory, out string errorMessage)
    {
        if (!(_runtimeConfig?.LoggingEnabled ?? false))
        {
            errorMessage = "diagnostics_disabled";
            return false;
        }
        string gamePath = _runtimeGamePath;
        string coreProfile = _runtimeCoreProfile;
        bool useLazyLoading = MinorShift.Emuera.Config.UseLazyLoading && MinorShift.Emuera.Program.SupportsLazyLoading;
        return DiagnosticLogExporter.ExportDiagnosticPackage(_runtimeConfig, outputDirectory, gamePath, coreProfile, useLazyLoading, _inputReplay, out errorMessage);
    }

    static string NormalizeSourcePath(string file)
    {
        if (string.IsNullOrEmpty(file))
            return "<unknown>";
        string normalized = file.Replace('\\', '/');
        int scripts = normalized.LastIndexOf("/Scripts/", StringComparison.OrdinalIgnoreCase);
        if (scripts >= 0)
            return normalized.Substring(scripts + 1);
        return Path.GetFileName(normalized);
    }

    static EmueraLogCategory NormalizeLogCategory(EmueraLogCategory category, string message)
    {
        if (category != EmueraLogCategory.General || string.IsNullOrEmpty(message))
            return category;
        if (message.StartsWith("[IMG]", StringComparison.Ordinal) || message.Contains("[SpriteManager]", StringComparison.Ordinal))
            return EmueraLogCategory.Sprite;
        if (message.StartsWith("[AUDIO]", StringComparison.Ordinal))
            return EmueraLogCategory.Audio;
        if (message.StartsWith("[LOADSAVE]", StringComparison.Ordinal))
            return EmueraLogCategory.Save;
        if (message.StartsWith("[LOAD]", StringComparison.Ordinal) || message.StartsWith("[LOADTIME]", StringComparison.Ordinal))
            return EmueraLogCategory.Load;
        if (message.StartsWith("[PROC]", StringComparison.Ordinal))
            return EmueraLogCategory.Script;
        if (message.StartsWith("[CONFIG]", StringComparison.Ordinal))
            return EmueraLogCategory.Config;
        if (message.StartsWith("[FS]", StringComparison.Ordinal))
            return EmueraLogCategory.FileSystem;
        if (message.StartsWith("[UI Queue]", StringComparison.Ordinal))
            return EmueraLogCategory.UI;
        if (message.StartsWith(ScrollTracePrefix, StringComparison.Ordinal))
            return EmueraLogCategory.Script;
        return category;
    }

    static string ClipLogMessage(string value)
    {
        return ClipFlatText(value, MaxLogMessageChars, "...<truncated>");
    }

    public static void ScrollTrace(string category, string message)
    {
        if (!IsScrollTraceActive)
            return;
        WriteScrollTrace(category, message);
    }

    /// <summary>
    /// 企业级说明：滚动/输入追踪属于 Android 高频路径，必须在 ScrollTrace 开关和日志等级通过后再构造文本。
    /// 该重载用于替换调用点的插值字符串，避免调试关闭时仍产生 GC 分配。
    /// </summary>
    public static void ScrollTrace(string category, Func<string> messageFactory)
    {
        if (!IsScrollTraceActive)
            return;
        string message;
        try
        {
            message = messageFactory != null ? messageFactory() : "";
        }
        catch (Exception ex)
        {
            message = "[LOGGER] scroll trace factory failed: " + ex.GetType().Name + ": " + ex.Message;
        }
        WriteScrollTrace(category, message);
    }

    static void WriteScrollTrace(string category, string message)
    {
        int seq = Interlocked.Increment(ref scrollTraceSequence);
        string formatted = $"{ScrollTracePrefix} #{seq} t={GetTickMs()} {category}: {message}";
        LogInternal(EmueraLogLevel.Debug, EmueraLogCategory.Script, formatted, null, nameof(ScrollTrace), "Scripts/GenericUtils.cs", 0);
    }

    public static void StartScrollTraceCoreWindow(string reason)
    {
        if (!IsScrollTraceActive)
            return;
        Interlocked.Exchange(ref scrollTraceCoreLinesRemaining, ScrollTraceCoreBurstLineCount);
        ScrollTrace("core", $"window_start lines={ScrollTraceCoreBurstLineCount} reason={ClipTrace(reason)}");
    }

    public static void StartScrollTraceCoreWindow(Func<string> reasonFactory)
    {
        if (!IsScrollTraceActive)
            return;
        string reason;
        try
        {
            reason = reasonFactory != null ? reasonFactory() : "";
        }
        catch (Exception ex)
        {
            reason = "[LOGGER] core window reason failed: " + ex.GetType().Name + ": " + ex.Message;
        }
        Interlocked.Exchange(ref scrollTraceCoreLinesRemaining, ScrollTraceCoreBurstLineCount);
        ScrollTrace("core", () => $"window_start lines={ScrollTraceCoreBurstLineCount} reason={ClipTrace(reason)}");
    }

    public static bool TryConsumeScrollTraceCoreLine()
    {
        if (!IsScrollTraceActive)
            return false;
        while (true)
        {
            int current = Volatile.Read(ref scrollTraceCoreLinesRemaining);
            if (current <= 0)
                return false;
            if (Interlocked.CompareExchange(ref scrollTraceCoreLinesRemaining, current - 1, current) == current)
                return true;
        }
    }

    public static string ClipTrace(string value, int maxLength = 96)
    {
        return ClipFlatText(value, maxLength, "...");
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTouchTraceEnabled(string subSwitch = "")
    {
        if (string.IsNullOrEmpty(subSwitch))
            return (_runtimeConfig?.TouchEnabled ?? false) && DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.Touch);
        return DiagnosticLogRouter.IsTouchEnabled(subSwitch);
    }

    /// <summary>
    /// 企业级说明：语句识别调试开关默认关闭，未获用户确认前不开启。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsStatementTraceEnabled(string subSwitch = "")
    {
        if (string.IsNullOrEmpty(subSwitch))
            return (_runtimeConfig?.StatementRecognitionEnabled ?? false) && DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.StatementRecognition);
        return DiagnosticLogRouter.IsStatementRecognitionEnabled(subSwitch);
    }

    /// <summary>
    /// 企业级说明：输入调试开关默认关闭，只在输入链路记录 SUBMIT/CONSUME 等关键事件。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInputTraceEnabled(string subSwitch = "")
    {
        if (string.IsNullOrEmpty(subSwitch))
            return (_runtimeConfig?.InputDebugEnabled ?? false) && DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.Input);
        return DiagnosticLogRouter.IsInputDebugEnabled(subSwitch);
    }

    /// <summary>
    /// 企业级说明：图片调试开关默认关闭，成功日志默认不记录，避免图片热路径分配。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsImageDebugEnabled(string subSwitch = "")
    {
        if (string.IsNullOrEmpty(subSwitch))
            return (_runtimeConfig?.ImageDebugEnabled ?? false) && DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.Sprite);
        return DiagnosticLogRouter.IsImageDebugEnabled(subSwitch);
    }

    /// <summary>
    /// 企业级说明：UI 几何调试开关默认关闭，且默认只记录 mismatch，不记录所有 part。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsUiLayoutTraceEnabled(string subSwitch = "")
    {
        if (string.IsNullOrEmpty(subSwitch))
            return (_runtimeConfig?.UiLayoutEnabled ?? false) && DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.UI);
        return DiagnosticLogRouter.IsUiLayoutEnabled(subSwitch);
    }

    public static bool IsUiLayoutTargetRectTraceEnabled()
    {
        return IsUiLayoutTraceEnabled() && !(_runtimeConfig?.UiLayoutMismatchOnly ?? true) && (_runtimeConfig?.UiLayoutTargetRect ?? false);
    }

    public static bool IsUiLayoutActualRectTraceEnabled()
    {
        return IsUiLayoutTraceEnabled() && !(_runtimeConfig?.UiLayoutMismatchOnly ?? true) && (_runtimeConfig?.UiLayoutActualRect ?? false);
    }

    public static bool IsUiLayoutMismatchTraceEnabled()
    {
        return IsUiLayoutTraceEnabled();
    }

    public static int UiLayoutMismatchThresholdPx => _runtimeConfig?.UiLayoutMismatchThresholdPx ?? 2;

    public static bool IsUiOverlayEnabled() => _runtimeConfig?.UiOverlayEnabled ?? false;

    public static bool UiOverlayTargetRectEnabled => _runtimeConfig?.UiOverlayTargetRect ?? false;
    public static bool UiOverlayActualRectEnabled => _runtimeConfig?.UiOverlayActualRect ?? false;
    public static bool UiOverlayMismatchEnabled => _runtimeConfig?.UiOverlayMismatch ?? true;
    public static bool UiOverlayImageRectEnabled => _runtimeConfig?.UiOverlayImageRect ?? true;
    public static bool UiOverlayButtonRectEnabled => _runtimeConfig?.UiOverlayButtonRect ?? true;
    public static int UiOverlayMaxDrawnRects => _runtimeConfig?.UiOverlayMaxDrawnRects ?? 128;

    /// <summary>
    /// 动态地图诊断只记录证据，不改变滚动、按钮或渲染行为。
    /// 调用方需要先判断此开关，避免在 Android 刷新路径中无意义地构造行快照字符串。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDynamicMapTraceEnabled(string subSwitch = "")
    {
        var cfg = _runtimeConfig;
        if (cfg == null || !cfg.LoggingEnabled || !cfg.DynamicMapDebugEnabled)
            return false;
        if (!DiagnosticLogRouter.IsCategoryEnabled(EmueraLogCategory.UI))
            return false;

        if (string.IsNullOrEmpty(subSwitch))
            return true;
        return subSwitch switch
        {
            "line_snapshot" => cfg.DynamicMapLogLineSnapshot,
            "scroll" => cfg.DynamicMapLogScroll,
            "buttons" => cfg.DynamicMapLogButtons,
            _ => false,
        };
    }

    public static bool IsDynamicMapLineSnapshotTraceEnabled => IsDynamicMapTraceEnabled("line_snapshot");
    public static bool IsDynamicMapScrollTraceEnabled => IsDynamicMapTraceEnabled("scroll");
    public static bool IsDynamicMapButtonTraceEnabled => IsDynamicMapTraceEnabled("buttons");

    public static bool ShouldTraceDynamicMap(bool hasBitmapContext)
    {
        var cfg = _runtimeConfig;
        if (!IsDynamicMapTraceEnabled())
            return false;

        long now = DiagnosticLogRouter.GetMonotonicMilliseconds();
        if (hasBitmapContext)
            Volatile.Write(ref dynamicMapLastContextTickMs, now);

        if (!cfg.DynamicMapOnlyBitmapContext)
            return true;
        if (hasBitmapContext)
            return true;

        long last = Volatile.Read(ref dynamicMapLastContextTickMs);
        int windowMs = Math.Max(0, cfg.DynamicMapContextWindowMs);
        return last != long.MinValue && now - last <= windowMs;
    }

    public static void DynamicMapTrace(string eventId, Func<string> messageFactory, Func<string> dataFactory = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsDynamicMapTraceEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.UI, eventId, dataFactory,
            messageFactory, member, file, line);
    }

    public static string BuildDynamicMapLineTailSummary(IReadOnlyList<ConsoleDisplayLine> lines)
    {
        if (lines == null || lines.Count == 0)
            return "none";
        int maxLines = GetDynamicMapMaxLines();
        int start = Math.Max(0, lines.Count - maxLines);
        var sb = new StringBuilder(maxLines * 96);
        for (int i = start; i < lines.Count; i++)
        {
            if (sb.Length > 0)
                sb.Append('|');
            sb.Append(i).Append(':').Append(BuildDynamicMapLineSummary(lines[i], false, false));
        }
        return sb.ToString();
    }

    public static string BuildDynamicMapDeltaLineSummary(IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines)
    {
        if (lines == null || lines.Count == 0)
            return "none";
        int maxLines = GetDynamicMapMaxLines();
        int start = Math.Max(0, lines.Count - maxLines);
        var sb = new StringBuilder(maxLines * 96);
        for (int i = start; i < lines.Count; i++)
        {
            if (sb.Length > 0)
                sb.Append('|');
            sb.Append(i).Append(':').Append(BuildDynamicMapLineSummary(lines[i].Line, true, lines[i].Update));
        }
        return sb.ToString();
    }

    static string BuildDynamicMapLineSummary(ConsoleDisplayLine line, bool includeUpdate, bool update)
    {
        if (line == null)
            return "{null}";

        int commandCount = 0;
        int commandWritten = 0;
        var commands = new StringBuilder(64);
        AccumulateDynamicMapCommandSummary(line, commands, ref commandCount, ref commandWritten, 0);

        int maxTextChars = GetDynamicMapMaxTextChars();
		var sb = new StringBuilder(160);
		sb.Append("{no=").Append(line.LineNo)
			.Append(",logic=").Append(line.IsLogicalLine ? 1 : 0)
            .Append(",tmp=").Append(line.IsTemporary ? 1 : 0)
            .Append(",end=").Append(line.IsLineEnd ? 1 : 0)
            .Append(",seg=").Append(line.Buttons?.Length ?? 0)
            .Append(",cmd=").Append(commandCount);
        if (includeUpdate)
            sb.Append(",upd=").Append(update ? 1 : 0);
        if (commands.Length > 0)
            sb.Append(",cmds=").Append(commands);
        sb.Append(",text=").Append(CompactTraceValue(line.ToString(), maxTextChars)).Append('}');
        return sb.ToString();
    }

    static void AccumulateDynamicMapCommandSummary(ConsoleDisplayLine line, StringBuilder commands, ref int commandCount, ref int commandWritten, int depth)
    {
        if (line?.Buttons == null || depth > 4)
            return;
        for (int i = 0; i < line.Buttons.Length; i++)
        {
            var button = line.Buttons[i];
            if (button == null)
                continue;
            if (button.IsButton)
            {
                commandCount++;
                if (commandWritten < 4)
                {
                    if (commands.Length > 0)
                        commands.Append(',');
                    commands.Append(CompactTraceValue(button.Inputs, 24))
                        .Append(':')
                        .Append(CompactTraceValue(button.ToString(), 24))
                        .Append("@g")
                        .Append(button.Generation);
                    commandWritten++;
                }
            }

            var parts = button.StrArray;
            if (parts == null)
                continue;
            for (int j = 0; j < parts.Length; j++)
            {
                if (parts[j] is ConsoleDivPart div && div.Children != null)
                {
                    for (int k = 0; k < div.Children.Length; k++)
                        AccumulateDynamicMapCommandSummary(div.Children[k], commands, ref commandCount, ref commandWritten, depth + 1);
                }
            }
        }
    }

    static int GetDynamicMapMaxLines()
    {
        return Math.Min(64, Math.Max(1, _runtimeConfig?.DynamicMapMaxLines ?? 12));
    }

    static int GetDynamicMapMaxTextChars()
    {
        return Math.Min(240, Math.Max(8, _runtimeConfig?.DynamicMapMaxTextChars ?? 48));
    }

    static string CompactTraceValue(string value, int maxLength)
    {
        return ClipTrace(value, maxLength)
            .Replace(' ', '_')
            .Replace('|', '/')
            .Replace(';', ',');
    }

    public static string RedactTracePath(string path) => DiagnosticLogRouter.RedactPath(path);


    /// <summary>
    /// 企业级说明：输出结构化触摸诊断日志，event_id 与 data 原样写入结构化记录。
    /// message 延迟构造，仅在开关和限流通过后执行。
    /// 调用前建议先通过 IsTouchTraceEnabled 判断，避免热路径字符串分配。
    /// </summary>
    public static void TouchTrace(string eventId, string message, string data = "",
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsTouchTraceEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Touch, eventId, data,
            () => DiagnosticLogRouter.RedactText(message, _runtimeConfig?.LoggingMaxMessageChars ?? MaxLogMessageChars),
            member, file, line);
    }

    public static void TouchTrace(string eventId, Func<string> messageFactory, Func<string> dataFactory = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsTouchTraceEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Touch, eventId, dataFactory,
            () => DiagnosticLogRouter.RedactText(messageFactory?.Invoke(), _runtimeConfig?.LoggingMaxMessageChars ?? MaxLogMessageChars),
            member, file, line);
    }

    /// <summary>
    /// 企业级说明：输出结构化语句识别诊断日志，event_id 与 data 原样写入结构化记录。
    /// 脚本文本默认截断到 120 字符，防止长行爆炸。
    /// </summary>
    public static void StatementTrace(string eventId, string message, string data = "",
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsStatementTraceEnabled())
            return;
        int maxChars = _runtimeConfig?.RedactionMaxScriptTextChars ?? 120;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.StatementRecognition, eventId, data,
            () => DiagnosticLogRouter.RedactText(message, maxChars), member, file, line);
    }

    /// <summary>
    /// 企业级说明：输出结构化输入诊断日志，event_id 与 data 原样写入结构化记录。
    /// 输入文本截断到 64 字符，不记录完整用户输入。
    /// </summary>
    public static void InputTrace(string eventId, string message, string data = "",
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsInputTraceEnabled())
            return;
        int maxChars = _runtimeConfig?.RedactionMaxUserTextChars ?? 64;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Input, eventId, data,
            () => DiagnosticLogRouter.RedactText(message, maxChars), member, file, line);
    }

    public static void InputTrace(string eventId, Func<string> messageFactory, Func<string> dataFactory = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsInputTraceEnabled())
            return;
        int maxChars = _runtimeConfig?.RedactionMaxUserTextChars ?? 64;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Input, eventId, dataFactory,
            () => DiagnosticLogRouter.RedactText(messageFactory?.Invoke(), maxChars), member, file, line);
    }

    /// <summary>
    /// 企业级说明：输出结构化图片诊断日志，event_id 与 data 原样写入结构化记录。
    /// 路径脱敏，长路径截断，不输出像素或二进制。
    /// </summary>
    public static void ImageTrace(string eventId, string message, string data = "",
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsImageDebugEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Sprite, eventId, data,
            () => message, member, file, line);
    }

    public static void ImageTrace(string eventId, Func<string> messageFactory, Func<string> dataFactory = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsImageDebugEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.Sprite, eventId, dataFactory,
            messageFactory, member, file, line);
    }

    /// <summary>
    /// 企业级说明：输出结构化 UI 几何诊断日志，event_id 与 data 原样写入结构化记录。
    /// 默认只记录 mismatch，不记录所有 part 矩形。
    /// </summary>
    public static void UiLayoutTrace(string eventId, string message, string data = "",
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsUiLayoutTraceEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.UI, eventId, data,
            () => message, member, file, line);
    }

    public static void UiLayoutTrace(string eventId, Func<string> messageFactory, Func<string> dataFactory = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsUiLayoutTraceEnabled())
            return;
        LogStructured(EmueraLogLevel.Debug, EmueraLogCategory.UI, eventId, dataFactory,
            messageFactory, member, file, line);
    }

    /// <summary>
    /// 企业级说明：输入复现轨迹捕获，默认关闭，只在开启后保留最近 N 条。
    /// 不记录完整用户输入文本，按 max_text_chars 截断。
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
