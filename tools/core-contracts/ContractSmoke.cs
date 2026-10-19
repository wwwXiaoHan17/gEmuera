using GEmuera.Core.Compatibility;
using GEmuera.Core.Experiments;
using GEmuera.Core.Governance;
using GEmuera.Core.Parsing;
using GEmuera.Core.Ports;
using GEmuera.Core.Resources;
using GEmuera.Core.Runtime;
using GEmuera.Core.Save;
using GEmuera.Core.Session;
using GEmuera.Core.State;

namespace CoreContractSmoke;

internal static class ContractSmoke
{
    public static async Task RunAsync()
    {
        ParserContract();
        VariableAndSaveContract();
        ResourceContract();
        await PortContractAsync();
        await CooperativeContractAsync();
        GovernanceContract();
    }

    private static void ParserContract()
    {
        var parser = new ErbParser();
        var source = "; comment\nLABEL START\nPRINT \"hello world\"\nBAD? value";
        var first = parser.Parse(source, new ContentToken("fixture://parser"));
        var second = parser.Parse(source.Replace("\n", "\r\n", StringComparison.Ordinal), new ContentToken("fixture://parser"));
        Assert(first.Lines.Count == second.Lines.Count, "Parser line count changed with newline normalization.");
        Assert(first.Lines[1].Kind == ErbLineKind.Label && first.Lines[1].Label == "START", "Label boundary was not parsed.");
        var print = first.Lines[2].Instruction;
        Assert(print is not null && print.Name == "PRINT", "Instruction name was not normalized.");
        Assert(print is not null && print.Arguments.SequenceEqual(new[] { "hello world" }), "Quoted ERB argument was split incorrectly.");
        Assert(first.Diagnostics.Count == 1 && first.Diagnostics[0].Code == "erb.invalid-instruction", "Invalid instruction was not diagnosed.");
        Assert(first.Lines.Select(line => line.Span).SequenceEqual(second.Lines.Select(line => line.Span)), "Parser spans are not deterministic.");
    }

    private static void VariableAndSaveContract()
    {
        AssertThrows<ArgumentOutOfRangeException>(
            () => _ = new SessionGeneration(-1),
            "A negative session generation was accepted.");

        var generation = new SessionGeneration(1);
        var store = new VariableStore(generation);
        var key = new VariableKey(VariableScope.Global, "score");
        store.Set(key, CoreValue.FromInteger(10));
        var candidate = store.PrepareLoad(store.Capture());
        candidate.Set(key, CoreValue.FromInteger(20));
        Assert(store.TryGet(key, out var before) && before.Integer == 10, "Variable candidate mutated the active store before commit.");
        store.CommitLoad(candidate, generation);
        Assert(store.TryGet(key, out var after) && after.Integer == 20, "Variable candidate did not commit atomically.");
        AssertThrows<InvalidOperationException>(() => store.CommitLoad(candidate, generation), "A variable candidate was committed twice.");
        Assert(store.TryGet(key, out var afterRetry) && afterRetry.Integer == 20, "A rejected duplicate variable commit changed active state.");

        var profile = new SaveProfileId("legacy.save.v1");
        var codec = new DeterministicSaveCodec(profile);
        var bytes = codec.Encode(SaveSnapshot.From(store, profile));
        var decoded = codec.Decode(bytes, profile);
        Assert(decoded.Values[key].Integer == 20, "Save codec did not round-trip typed variable state.");
        AssertThrows<InvalidDataException>(() => codec.Decode(bytes, new SaveProfileId("other.save.v1")), "Save codec accepted a conflicting profile.");
    }

    private static void ResourceContract()
    {
        var source = new SourceToken("content://fixture/image");
        using var store = new PixelStore();
        var handle = store.CreateEmpty(2, 2, sourceToken: source);
        var initial = store.Read(handle);
        store.SetPixel(handle, 1, 1, PixelColor.FromRgba(1, 2, 3, 255));
        var current = store.Read(handle);
        Assert(current.Revision.Value > initial.Revision.Value && current.GetPixel(1, 1).Red == 1, "PixelStore did not publish a new CPU revision.");
        Assert(initial.GetPixel(1, 1).Alpha == 0, "A published PixelStore revision was mutated in place.");

        using var budget = new MemoryBudget(100);
        Assert(budget.TryReserve(60, out var reservation) && reservation is not null, "Memory budget rejected an available reservation.");
        reservation!.Dispose(); reservation.Dispose();
        Assert(budget.Snapshot.ReservedBytes == 0 && budget.Snapshot.ActiveReservations == 0, "Memory reservation was not released exactly once.");
        var ledger = new ResourceBridgeLedger(new SessionGeneration(1));
        var upload = new ResourceUploadDescriptor(handle, current.Revision, new SessionGeneration(1), source, current.ByteLength);
        Assert(ledger.Accept(upload) == ResourceUploadDecision.Accepted, "Resource bridge rejected the current revision.");
        Assert(ledger.Accept(upload) == ResourceUploadDecision.OlderRevision, "Resource bridge accepted a duplicate revision.");
        Assert(ledger.Accept(new ResourceUploadDescriptor(handle, new PixelRevision(current.Revision.Value + 1), new SessionGeneration(0), source, current.ByteLength)) == ResourceUploadDecision.StaleGeneration, "Stale resource upload was accepted.");
        ledger.Dispose();
    }

    private static async Task PortContractAsync()
    {
        var generation = new SessionGeneration(1);
        var scheduler = new QueuedOwnerScheduler();
        var dispatcher = new CompletionDispatcher(generation, scheduler);
        var request = new PortRequest<string>(DefaultPortManifest.Input, DefaultPortManifest.InputCapability, generation, new SessionOperationId(7), "input");
        var completion = PortCompletion<string>.From(request, "ok");
        var applied = false;
        var pending = dispatcher.DispatchAsync(request, completion, value => { applied = value.IsSuccess; return ValueTask.CompletedTask; }).AsTask();
        dispatcher.AdvanceGeneration(new SessionGeneration(2));
        var stale = await pending;
        Assert(stale.Status == CompletionDispatchStatus.Stale && !applied, "A stale completion reached the Core continuation.");

        var failing = new CompletionDispatcher(generation, new QueuedOwnerScheduler { ThrowOnEnqueue = true });
        var failure = await failing.DispatchAsync(request, completion, _ => ValueTask.CompletedTask);
        Assert(failure.Status == CompletionDispatchStatus.OwnerUnavailable, "Owner enqueue failure was not completed.");
        dispatcher.Close(); failing.Close();

        using var input = new InputCoordinator(generation);
        var action = new NormalizedAction("confirm", InputDeviceKind.VirtualCursor, 1, 2, PointerButton.Right, new SessionStamp(generation, new SessionOperationId(8)));
        Assert(input.Submit(action) == InputSubmissionStatus.Accepted, "Normalized input was rejected.");
        Assert(input.Submit(action) == InputSubmissionStatus.Duplicate, "Duplicate input operation was accepted.");
        input.AdvanceGeneration(new SessionGeneration(2));
        Assert(input.Submit(action) == InputSubmissionStatus.Stale, "Stale input was accepted.");

        var journal = new ImportJournal(new StorageContentToken("android.provider", "opaque-token"), generation);
        journal.Revoke();
        Assert(journal.State == ImportJournalState.Revoked, "Cancelling an import before writing did not revoke its journal.");
        AssertThrows<InvalidOperationException>(journal.BeginWrite, "A revoked import journal could be restarted.");

        journal = new ImportJournal(new StorageContentToken("android.provider", "opaque-token-committed"), generation);
        journal.BeginWrite(); journal.Append(4); journal.MarkVerified(); journal.Commit();
        Assert(journal.State == ImportJournalState.Committed, "Storage import journal did not reach atomic commit.");
        journal.Revoke(); Assert(journal.State == ImportJournalState.Revoked, "Storage revoke did not invalidate the import journal.");
    }

    private static async Task CooperativeContractAsync()
    {
        var plan = new CompatibilityPlanBuilder(BuiltInDialectCatalog.CreateLegacyBaseline()).Build("v24pure", new[] { "gemuera.v24" });
        var identity = new ExperimentIdentity(
            "m6-smoke", "1.0.0", "source", "toolchain", "runtime", "fixture", "v24pure", "desktop", "candidate", 0, 1, 77,
            new SessionStamp(new SessionGeneration(1), new SessionOperationId(1)),
            FeatureFlags.Default with { Scheduler = true });
        var audit = new YieldabilityAuditInventory(Enum.GetValues<YieldabilityPathCategory>().Select((category, index) =>
            new YieldabilityAuditRecord($"path.{index}", category, YieldabilityDisposition.BoundedSynchronous, 1, false, true, false, 1, 1, new string('0', 64))));
        var definition = new ExperimentDefinition(identity, "smoke", "baseline", "rollback", new PerformanceThresholds(10, 20, 1000, 10), audit);
        await using var runner = new CooperativeVmExperimentRunner(definition, new TestInterpreterHost(InterpreterTestDescriptors.V24("m6-smoke-host"), plan));
        var step = await runner.StepAsync(new VmStepBudget(10, 10));
        Assert(step.State == VmExecutionState.YieldedBudget && runner.State == VmExecutionState.YieldedBudget, "Cooperative VM did not enforce the experimental Step contract.");
    }

    private static void GovernanceContract()
    {
        var hash = new string('a', 64);
        var entry = new RemovalInventoryEntry("legacy.renderer", "scripts/LegacyRenderer.cs", RemovalLifecycleStatus.ZeroTrafficObserved, new[] { "M7-REL-03" }, 0);
        var now = DateTimeOffset.UtcNow;
        var cycles = new[] { new ReleaseCycle("release-a", now.AddDays(-2), now.AddDays(-1), 0, 0, hash), new ReleaseCycle("release-b", now.AddHours(-12), now, 0, 0, hash) };
        var packet = new RemovalPacket("M7-REL-03-smoke", new[] { entry }, cycles, new RollbackDrill("stable", "restore stable", true, hash), new RemovalApproval("prepare", "review", "approve", now, "Approved", new[] { hash }));
        Assert(packet.CanMarkRemoved(), "M7 removal packet did not require and accept two clean cycles plus rollback evidence.");
        Assert(!new RemovalPacket("blocked", new[] { entry }, cycles.Take(1), packet.Rollback, packet.Approval).CanMarkRemoved(), "M7 removal packet accepted a single release cycle.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try { action(); } catch (TException) { return; }
        throw new InvalidOperationException(message);
    }

    private sealed class QueuedOwnerScheduler : IPortOwnerScheduler
    {
        private readonly Queue<Func<ValueTask>> _queue = new();
        public bool ThrowOnEnqueue { get; init; }
        public bool IsOwnerThread => true;
        public ValueTask EnqueueAsync(Func<ValueTask> callback, CancellationToken cancellationToken = default)
        {
            if (ThrowOnEnqueue) throw new InvalidOperationException("synthetic owner failure");
            cancellationToken.ThrowIfCancellationRequested(); _queue.Enqueue(callback); return ValueTask.CompletedTask;
        }
    }
}
