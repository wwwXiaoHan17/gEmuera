using System;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Session;
using System.Threading;
using System.Threading.Tasks;

namespace gEmuera.GodotHost;

/// <summary>
/// Godot bridge for the existing EmueraThread. It intentionally contains no
/// compatibility decisions; the Core facade supplies the frozen plan and the
/// legacy thread remains the behavior owner during migration.
/// </summary>
public sealed class LegacySessionBackend : ILegacySessionBackend
{
    private readonly bool _debug;
    private readonly bool _useCoroutine;
    private readonly LegacySessionLaunchRegistry _launchRegistry;
    private bool _canarySessionActive;

    public LegacySessionBackend(bool debug, bool useCoroutine)
        : this(debug, useCoroutine, null)
    {
    }

    /// <summary>
    /// The registry is an immutable Godot-host allowlist. It maps the Core
    /// selection's opaque game id to the legacy path/profile inputs without
    /// allowing Core to observe or resolve filesystem paths.
    /// </summary>
    public LegacySessionBackend(
        bool debug,
        bool useCoroutine,
        LegacySessionLaunchRegistry launchRegistry)
    {
        _debug = debug;
        _useCoroutine = useCoroutine;
        _launchRegistry = launchRegistry;
    }

    // Running() mirrors legacy Console.IsInProcess and is false during an
    // intentional INPUT/WAIT pause. Session lifecycle must instead observe
    // the actual worker thread so stop/start cannot overlap such a pause.
    public bool IsRunning => global::EmueraThread.instance.IsSessionActive;

    public async ValueTask StartAsync(
        SessionSelection selection,
        CompatibilityPlan compatibility,
        SessionStamp stamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(compatibility);
        cancellationToken.ThrowIfCancellationRequested();

        var launchRegistry = _launchRegistry
            ?? throw new InvalidOperationException(
                "Legacy session startup requires an immutable host launch registry.");
        global::MinorShift.Emuera.Program.ConfigureCompatibilityPlan(compatibility);
        try
        {
            var launch = launchRegistry.Resolve(selection);
            ApplyLaunchConfiguration(launch);
            ResetCanarySessionConfiguration();
            await StartLegacyBaselineAsync(cancellationToken).ConfigureAwait(false);
            _canarySessionActive = true;
        }
        catch
        {
            // A failed candidate must not poison the next switch with a plan
            // that was never paired with a running legacy worker.
            global::MinorShift.Emuera.Program.ClearCompatibilityPlan(compatibility);
            throw;
        }
    }

    /// <summary>
    /// Preserves the Legacy startup order for the default rollout path. The caller
    /// intentionally supplies no compatibility plan, because the legacy VM is
    /// still the owner of profile-dependent behavior on this path.
    /// </summary>
    public ValueTask StartLegacyBaselineAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The legacy runtime is still process-wide. Resetting it is deliberately
        // confined to this bridge so the Core facade never references it.
        global::MinorShift.Emuera.GlobalStatic.Reset();
        global::EmueraThread.instance.Start(_debug, _useCoroutine);
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(
        SessionStamp stamp,
        CancellationToken cancellationToken = default)
    {
        if (_canarySessionActive)
            return StopCanarySessionAsync(cancellationToken);
        return StopLegacyBaselineAsync(cancellationToken);
    }

    private ValueTask StopCanarySessionAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The worker is the only legacy-side writer. It must be quiescent before
        // this main-thread bridge detaches Godot nodes and disposes their resources.
        global::EmueraThread.instance.End();
        EnsureCanaryCleanupRunsOnGodotMainThread();
        global::EmueraContent.instance?.ClearForCanarySessionTransition();
        global::MinorShift.Emuera.Content.AppContents.UnloadContents();
        uEmuera.Utils.ResourceClear();
        global::SpriteManager.ForceClear();
        global::ColorMatrixGPU.ResetCanarySessionState();
        global::EmueraMain.ResetCanarySessionState();
        global::EmueraGpuRenderComponent.ResetCanarySessionState();
        global::EmueraTextRenderComponent.ResetCanarySessionState();
        global::MinorShift.Emuera.GlobalStatic.Reset();
        global::MinorShift.Emuera.GlobalStatic.ResetCanarySessionState();
        global::MinorShift.Emuera.Program.ResetSessionState();
        _canarySessionActive = false;
        return ValueTask.CompletedTask;
    }

    private static void EnsureCanaryCleanupRunsOnGodotMainThread()
    {
        if (!global::GenericUtils.IsOnMainThread())
        {
            throw new InvalidOperationException(
                "Canary session cleanup must run on the Godot main thread.");
        }
    }

    /// <summary>
    /// Counterpart of <see cref="StartLegacyBaselineAsync"/> used when the
    /// session-isolation canary is disabled or after its rollback.
    /// </summary>
    public ValueTask StopLegacyBaselineAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::EmueraThread.instance.End();
        global::MinorShift.Emuera.GlobalStatic.Reset();
        global::MinorShift.Emuera.Program.ClearCompatibilityPlan();
        return ValueTask.CompletedTask;
    }

    private static void ApplyLaunchConfiguration(LegacySessionLaunchConfiguration launch)
    {
        // Per-session pack 通道刷新（E2-R3）：ConfigureLegacyRunnerSession 内部按目标游戏
        // 重算 GEMUERA_COMPAT_PACKS（CompatPackLauncherConfig.MergeSelection：按游戏选择
        // 优先，外部诊断 env 次之，皆空清除）——canary 切换 / in-process cycle 换局不沿用
        // 上一局的启用清单。
        if (!global::FirstWindow.ConfigureLegacyRunnerSession(
                launch.GameRoot,
                launch.ProfileId,
                out var error))
        {
            throw new InvalidOperationException(
                $"Legacy session launch binding '{launch.GameId}' cannot be applied: {error}.");
        }

        // Program.Main snapshots Sys.ExeDir and FirstWindow's profile after the
        // thread starts. Apply both before starting it so a rollback replays
        // the exact prior binding instead of accidentally reusing B's globals.
        global::MinorShift._Library.Sys.ExeDir = uEmuera.Utils.NormalizePath(launch.GameRoot + "/");
        global::GenericUtils.NotifyGamePathSelected(
            global::MinorShift._Library.Sys.ExeDir,
            launch.ProfileId);
    }

    /// <summary>
    /// ConfigData is a process-wide mutable singleton. The legacy loader
    /// overlays files onto it, so leaving B's values in place makes a later A
    /// start depend on switch history. This remains canary-only: the Legacy
    /// baseline path deliberately retains its original startup sequence.
    /// </summary>
    private static void ResetCanarySessionConfiguration()
    {
        // AppContents owns process-static CSV sprite/resource dictionaries.
        // Leaving B's entries alive makes A's resource load emit duplicate
        // warnings and changes script-observable startup output.
        global::MinorShift.Emuera.Content.AppContents.UnloadContents();
        global::MinorShift.Emuera.ConfigData.Instance.Clear();
        // ParserMediator keeps its warning queue and its last console in
        // static fields. A Snake candidate can otherwise leave warnings that
        // become visible only after the next V24 candidate binds a console.
        global::MinorShift.Emuera.ParserMediator.ClearWarningList();
        global::MinorShift.Emuera.ParserMediator.ResetSessionState();
        global::MinorShift.Emuera.ParserMediator.Initialize(null);
    }
}
