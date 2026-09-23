using GEmuera.Core.Compatibility;
using GEmuera.Core.Session;
using GEmuera.Core.Runtime;
using CoreContractSmoke;
using gEmuera.GodotHost;
using MinorShift.Emuera.Compatibility;
using System.Data;
using System.IO;
using System.Text;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

var threadEntered = new ManualResetEventSlim(false);
var releaseThread = new ManualResetEventSlim(false);
var blockingLegacyWorker = new Thread(() =>
{
    threadEntered.Set();
    releaseThread.Wait();
});
blockingLegacyWorker.Start();
Assert(threadEntered.Wait(TimeSpan.FromSeconds(2)), "Thread quiescence fixture did not start its worker.");
AssertThrows<TimeoutException>(
    () => LegacyThreadQuiescence.WaitForStopOrThrow(
        blockingLegacyWorker,
        TimeSpan.Zero,
        "testing quiescence timeout"),
    "A still-running legacy worker was accepted as quiescent.");
Assert(blockingLegacyWorker.IsAlive, "Thread quiescence timeout changed the worker lifecycle.");
releaseThread.Set();
LegacyThreadQuiescence.WaitForStopOrThrow(
    blockingLegacyWorker,
    TimeSpan.FromSeconds(2),
    "testing successful quiescence");
Assert(!blockingLegacyWorker.IsAlive, "Thread quiescence did not wait for the released worker.");
threadEntered.Dispose();
releaseThread.Dispose();

var baseModule = new DialectModuleSnapshot(
    "gemuera.v24",
    "1.0.0",
    1,
    portTypeIds: Array.Empty<string>());
var snakeModule = new DialectModuleSnapshot(
    "game.snake",
    "1.0.0",
    1,
    dependencies: new[] { new ModuleDependencySnapshot("gemuera.v24", "[1.0.0,2.0.0)") },
    portTypeIds: new[] { "IExtraArgumentPolicy" });
var port = new BehaviorPortSnapshot(
    "call.extra-arguments.v1",
    "IExtraArgumentPolicy",
    PortContractKind.PolicyDecision,
    "IExtraArgumentPolicy",
    "policy.call.extra-arguments.v1",
    "game.snake",
    "DIA-CALL-EXTRA-001");

var first = CompatibilityPlanSnapshot.Create(
    "snake",
    new[] { snakeModule, baseModule },
    new[] { port });
var second = CompatibilityPlanSnapshot.Create(
    "snake",
    new[] { baseModule, snakeModule },
    new[] { port });
Assert(first.PlanSemanticHash == second.PlanSemanticHash, "Plan hash depends on input enumeration order.");
Assert(first.Modules.Count == 2 && first.Ports.Count == 1, "Plan snapshot lost contract entries.");

AssertThrows<ArgumentException>(
    () => CompatibilityPlanSnapshot.Create("snake", new[] { baseModule, baseModule }, new[] { port }),
    "Duplicate module ids were accepted.");
AssertThrows<ArgumentException>(
    () => CompatibilityPlanSnapshot.Create(
        "snake",
        new[]
        {
            new DialectModuleSnapshot(
                "game.bad",
                "1.0.0",
                1,
                dependencies: new[] { new ModuleDependencySnapshot("missing.module", "[1.0.0,2.0.0)") }),
        },
        Array.Empty<BehaviorPortSnapshot>()),
    "Missing module dependency was accepted.");
AssertThrows<ArgumentException>(
    () => CompatibilityPlanSnapshot.Create(
        "snake",
        new[]
        {
            new DialectModuleSnapshot(
                "game.cycle-a",
                "1.0.0",
                1,
                dependencies: new[] { new ModuleDependencySnapshot("game.cycle-b", "[1.0.0,2.0.0)") }),
            new DialectModuleSnapshot(
                "game.cycle-b",
                "1.0.0",
                1,
                dependencies: new[] { new ModuleDependencySnapshot("game.cycle-a", "[1.0.0,2.0.0)") }),
        },
        Array.Empty<BehaviorPortSnapshot>()),
    "Cyclic module dependencies were accepted.");
AssertThrows<ArgumentException>(
    () => new ModuleDependencySnapshot("gemuera.v24", "[2.0.0,1.0.0)"),
    "Inverted module dependency version ranges were accepted.");
AssertThrows<ArgumentException>(
    () => new ModuleDependencySnapshot("gemuera.v24", "(1.0.0,1.0.0]"),
    "Empty module dependency version ranges were accepted.");
var snapshotRangeBaseModule = new DialectModuleSnapshot(
    "game.snapshot-range-base",
    "1.0.0",
    1);
var snapshotRangeExactModule = new DialectModuleSnapshot(
    "game.snapshot-range-exact",
    "1.0.0",
    1,
    dependencies: new[] { new ModuleDependencySnapshot("game.snapshot-range-base", "[1.0.0,1.0.0]") });
var exactRangeSnapshot = CompatibilityPlanSnapshot.Create(
    "snake",
    new[] { snapshotRangeBaseModule, snapshotRangeExactModule },
    Array.Empty<BehaviorPortSnapshot>());
Assert(exactRangeSnapshot.Modules.Count == 2, "Closed singleton dependency range was rejected.");
AssertThrows<ArgumentException>(
    () => CompatibilityPlanSnapshot.Create(
        "snake",
        new[]
        {
            snapshotRangeBaseModule,
            new DialectModuleSnapshot(
                "game.snapshot-range-mismatch",
                "1.0.0",
                1,
                dependencies: new[] { new ModuleDependencySnapshot("game.snapshot-range-base", "(1.0.0,2.0.0]") }),
        },
        Array.Empty<BehaviorPortSnapshot>()),
    "Plan snapshot accepted a dependency version outside its declared range.");
AssertThrows<ArgumentException>(
    () => CompatibilityPlanSnapshot.Create(
        "snake",
        new[] { baseModule, snakeModule },
        new[]
        {
            new BehaviorPortSnapshot(
                "call.extra-arguments.v1",
                "IUndeclaredPort",
                PortContractKind.PolicyDecision,
                "IExtraArgumentPolicy",
                "policy.call.extra-arguments.v1",
                "game.snake",
                "DIA-CALL-EXTRA-001"),
        }),
    "Undeclared module port was accepted.");
AssertThrows<ArgumentException>(
    () => CompatibilityPlanSnapshot.Create("snake", new[] { baseModule, snakeModule }, new[] { port, port }),
    "Duplicate port ids were accepted.");
var multiPortModule = new DialectModuleSnapshot(
    "game.multi",
    "1.0.0",
    1,
    portTypeIds: new[] { "IExtraArgumentPolicy", "IPrivateArgumentShapePolicy" });
var duplicateBehaviorKeyPorts = new[]
{
    port,
    new BehaviorPortSnapshot(
        "call.extra-arguments.v1",
        "IPrivateArgumentShapePolicy",
        PortContractKind.PolicyDecision,
        "IPrivateArgumentShapePolicy",
        "policy.call.private-argument-shape.v1",
        "game.multi",
        "DIA-CALL-PRIVATE-001"),
};
AssertThrows<ArgumentException>(
    () => CompatibilityPlanSnapshot.Create("snake", new[] { baseModule, snakeModule, multiPortModule }, duplicateBehaviorKeyPorts),
    "Duplicate behavior keys were accepted.");
AssertThrows<ArgumentOutOfRangeException>(
    () => new BehaviorPortSnapshot(
        "call.invalid-kind.v1",
        "IExtraArgumentPolicy",
        (PortContractKind)999,
        "IExtraArgumentPolicy",
        "policy.call.invalid-kind.v1",
        "game.snake",
        "DIA-CALL-EXTRA-001"),
    "Unknown port contract kinds were accepted.");

var mutableContributionList = new List<IDialectContribution>
{
    new TestInstructionContribution(
        "mutable.initial",
        new InstructionDescriptor("MUTABLE_INITIAL", "", "game.mutable", VmCompletionMode.CommitThenProject)),
};
var immutableModuleCatalog = new DialectModuleCatalog();
immutableModuleCatalog.Register(new TestDialectModule(
    new DialectModuleDefinition("game.mutable", "1.0.0", 1),
    mutableContributionList));
var immutableModulePlanBuilder = new CompatibilityPlanBuilder(immutableModuleCatalog);
mutableContributionList.Add(new TestInstructionContribution(
    "mutable.late",
    new InstructionDescriptor("MUTABLE_LATE", "", "game.mutable", VmCompletionMode.CommitThenProject)));
var immutableModulePlan = immutableModulePlanBuilder.Build("mutable", new[] { "game.mutable" });
Assert(
    immutableModulePlan.Dialect.Instructions.ContainsKey("MUTABLE_INITIAL"),
    "Catalog snapshot lost the registered contribution.");
Assert(
    !immutableModulePlan.Dialect.Instructions.ContainsKey("MUTABLE_LATE"),
    "Catalog retained a mutable module contribution collection after composition.");
AssertThrows<InvalidOperationException>(
    () => immutableModuleCatalog.Register(new TestDialectModule(
        new DialectModuleDefinition("game.mutable.late", "1.0.0", 1),
        Array.Empty<IDialectContribution>())),
    "A catalog accepted a module registration after its plan builder froze composition.");

var duplicateContributionCatalog = new DialectModuleCatalog();
AssertThrows<InvalidOperationException>(
    () => duplicateContributionCatalog.Register(new TestDialectModule(
        new DialectModuleDefinition("game.duplicate-contribution", "1.0.0", 1),
        new IDialectContribution[]
        {
            new TestInstructionContribution(
                "duplicate.contribution",
                new InstructionDescriptor("DUPLICATE_FIRST", "", "game.duplicate-contribution", VmCompletionMode.CommitThenProject)),
            new TestInstructionContribution(
                "duplicate.contribution",
                new InstructionDescriptor("DUPLICATE_SECOND", "", "game.duplicate-contribution", VmCompletionMode.CommitThenProject)),
        })),
    "A module accepted duplicate contribution identities.");

var moduleCatalog = new DialectModuleCatalog();
moduleCatalog.Register(new TestDialectModule(
    new DialectModuleDefinition("gemuera.v24", "1.0.0", 1),
    new IDialectContribution[]
    {
        new TestInstructionContribution(
            "v24.print",
            new InstructionDescriptor("PRINT", "text", "gemuera.v24", VmCompletionMode.CommitThenProject)),
        new TestFunctionContribution(
            "v24.result",
            new FunctionDescriptor("RESULT", "()", "gemuera.v24", "Integer")),
    }));
AssertThrows<InvalidOperationException>(
    () => moduleCatalog.Register(new TestDialectModule(
        new DialectModuleDefinition("gemuera.v24", "1.0.0", 1),
        Array.Empty<IDialectContribution>())),
    "Duplicate runtime module ids were accepted.");
moduleCatalog.Register(new TestDialectModule(
    new DialectModuleDefinition(
        "game.snake",
        "1.0.0",
        1,
        dependencies: new[] { new ModuleDependencySnapshot("gemuera.v24", "[1.0.0,2.0.0)") },
        portTypeIds: new[] { "IExtraArgumentPolicy" }),
    new IDialectContribution[]
    {
        new TestInstructionContribution(
            "snake.print",
            new InstructionDescriptor("SNAKE_PRINT", "text", "game.snake", VmCompletionMode.CommitThenProject)),
    }));

var planBuilder = new CompatibilityPlanBuilder(moduleCatalog);

var profileCatalog = new CompatibilityProfileCatalog();
var mutableProfileModules = new[] { "game.snake" };
profileCatalog.Register(new CompatibilityProfileDefinition("snake", mutableProfileModules));
mutableProfileModules[0] = "gemuera.v24";
Assert(
    profileCatalog.Resolve("snake").RootModuleIds.SequenceEqual(new[] { "game.snake" }),
    "Compatibility profile retained a mutable caller collection.");
AssertThrows<InvalidOperationException>(
    () => profileCatalog.Register(new CompatibilityProfileDefinition("snake", new[] { "game.snake" })),
    "Duplicate compatibility profile ids were accepted.");
AssertThrows<InvalidOperationException>(
    () => profileCatalog.Resolve("missing.profile"),
    "Unknown compatibility profile was accepted.");
AssertThrows<ArgumentException>(
    () => new CompatibilityProfileDefinition("empty.profile", Array.Empty<string>()),
    "A compatibility profile without a root module was accepted.");

var frozenProfileCatalog = new CompatibilityProfileCatalog();
frozenProfileCatalog.Register(new CompatibilityProfileDefinition(
    "frozen.v24",
    new[] { "gemuera.v24" }));
var frozenProfileFacade = new LegacySessionFacade(
    planBuilder,
    frozenProfileCatalog,
    new TestLegacyBackend());
AssertThrows<InvalidOperationException>(
    () => frozenProfileCatalog.Register(new CompatibilityProfileDefinition(
        "frozen.snake",
        new[] { "game.snake" })),
    "A session accepted profile registrations after composition completed.");
await frozenProfileFacade.DisposeAsync();

var builtInProfiles = BuiltInDialectCatalog.CreateLegacyProfileCatalog();
Assert(
    builtInProfiles.Resolve("v24pure").RootModuleIds.SequenceEqual(new[] { "gemuera.v24" }),
    "Built-in v24 profile does not declare its root module.");
Assert(
    builtInProfiles.Resolve("snake").RootModuleIds.SequenceEqual(new[] { "game.snake" }),
    "Built-in Snake profile does not declare its root module.");

var legacyV24Profile = LegacyCompatibilityProfile.CreateForProfile("v24pure", scopedVariableInstructionsEnabled: true);
Assert(
    legacyV24Profile.ProfileId == "v24pure"
    && !legacyV24Profile.Snake.IsEnabled
    && !legacyV24Profile.EraFl.IsEnabled,
    "v24 legacy compatibility profile selected a game-specific policy.");
// CALLSTR/TINPUTNF are Snake-only (absent from the checked-in v24 reference registry).
// NOTE: PRINTN/PRINTVN/PRINTSN/PRINTFORMN/PRINTFORMSN/SKIPLOG are NOT Snake-only —
// both references register them in BuiltInFunctionCode (PRINTN = 改行をしないで入力待ち,
// vanilla-era Emuera); their v24pure visibility is correct despite the SNAKE_ handler
// prefix in the shared store ("handler class prefixes are not dialect ownership evidence").
Assert(
    !legacyV24Profile.IsInstructionVisible("CALLSTR")
    && !legacyV24Profile.IsInstructionVisible("TINPUTNF")
    && legacyV24Profile.IsInstructionVisible("PRINTN")
    && !legacyV24Profile.IsFunctionVisible("陷落状态"),
    "v24 legacy compatibility profile leaked a Snake-only registry member.");

var legacySnakeProfile = LegacyCompatibilityProfile.CreateForProfile("snake", scopedVariableInstructionsEnabled: true);
Assert(
    legacySnakeProfile.Plan.Dialect.Modules.Any(module => module.ModuleId == "game.snake")
    && legacySnakeProfile.Snake.IsEnabled
	&& legacySnakeProfile.Snake.AllowsScopedVariablePreRegistration
    && !legacySnakeProfile.EraFl.IsEnabled,
    "Snake legacy compatibility profile did not select only its game module.");
Assert(
    legacySnakeProfile.IsInstructionVisible("PRINTN")
    && legacySnakeProfile.IsInstructionVisible("VARI")
    && legacySnakeProfile.IsFunctionVisible("陷落状态"),
    "Snake legacy compatibility profile did not expose its scoped registry surface.");

var legacySnakeProfileWithoutScopedVariables = LegacyCompatibilityProfile.CreateForProfile(
    "snake",
    scopedVariableInstructionsEnabled: false);
Assert(
    legacySnakeProfile.Plan.CanonicalHash == legacySnakeProfileWithoutScopedVariables.Plan.CanonicalHash
    && legacySnakeProfile.RegistrySurfaceHash != legacySnakeProfileWithoutScopedVariables.RegistrySurfaceHash
	&& legacySnakeProfileWithoutScopedVariables.Snake.AllowsScopedVariablePreRegistration
    && !legacySnakeProfileWithoutScopedVariables.IsInstructionVisible("VARI")
    && !legacySnakeProfileWithoutScopedVariables.IsInstructionVisible("VARS"),
    "Snake scoped-variable registry surface is not frozen independently from the Core module plan.");

var legacyEraFlProfile = LegacyCompatibilityProfile.CreateForProfile("erafl", scopedVariableInstructionsEnabled: true);
Assert(
    legacyEraFlProfile.Plan.Dialect.Modules.Any(module => module.ModuleId == "game.erafl")
    && !legacyEraFlProfile.Snake.IsEnabled
    && legacyEraFlProfile.EraFl.IsEnabled
    && legacyEraFlProfile.UsesExtendedDisplayHistory
    && legacyEraFlProfile.UsesLazyResourceIndex,
    "eraFL legacy compatibility profile did not select its module policy.");
Assert(
    !legacyEraFlProfile.IsInstructionVisible("CALLSTR")
    && !legacyEraFlProfile.IsInstructionVisible("TINPUTNF")
    && legacyEraFlProfile.IsInstructionVisible("PRINTN")
    && !legacyEraFlProfile.IsFunctionVisible("陷落状态"),
    "eraFL legacy compatibility profile leaked a Snake-only registry member.");

var invalidLegacyProfilePlan = new CompatibilityPlanBuilder(BuiltInDialectCatalog.CreateLegacyBaseline())
    .Build("v24pure", new[] { "game.snake" });
AssertThrows<InvalidOperationException>(
    () => LegacyCompatibilityProfile.Create(invalidLegacyProfilePlan, scopedVariableInstructionsEnabled: true),
    "Legacy compatibility profile accepted a v24 profile composed with the Snake module.");

var catalogWithUnexpectedLegacyModule = BuiltInDialectCatalog.CreateLegacyBaseline();
catalogWithUnexpectedLegacyModule.Register(new TestDialectModule(
    new DialectModuleDefinition("test.legacy-extra", "1.0.0", 1),
    Array.Empty<IDialectContribution>()));
var legacyPlanWithUnexpectedModule = new CompatibilityPlanBuilder(catalogWithUnexpectedLegacyModule)
    .Build("v24pure", new[] { "gemuera.v24", "test.legacy-extra" });
AssertThrows<InvalidOperationException>(
    () => LegacyCompatibilityProfile.Create(legacyPlanWithUnexpectedModule, scopedVariableInstructionsEnabled: true),
    "Legacy compatibility profile accepted an unclassified module in a built-in profile closure.");

var extensionProfiles = new CompatibilityProfileCatalog();
extensionProfiles.Register(new CompatibilityProfileDefinition(
    "test.v24",
    new[] { "gemuera.v24" }));
var extensionProfileFacade = new LegacySessionFacade(
    planBuilder,
    extensionProfiles,
    new TestLegacyBackend());
var extensionProfileSwitch = await extensionProfileFacade.SwitchAsync(
    new SessionSelection("profile-extension", "test.v24"),
    null);
Assert(
    extensionProfileSwitch.IsCommitted &&
    extensionProfileFacade.CurrentPlan?.Dialect.Modules.Single().ModuleId == "gemuera.v24",
    "Legacy facade did not consume the injected compatibility profile catalog.");
await extensionProfileFacade.DisposeAsync();

var v24Plan = planBuilder.Build("v24pure", new[] { "gemuera.v24" });
Assert(v24Plan.Dialect.Modules.Count == 1, "v24 plan selected an undeclared module.");
Assert(!v24Plan.Dialect.Instructions.ContainsKey("SNAKE_PRINT"), "v24 plan leaked Snake instruction registration.");
Assert(v24Plan.Dialect.TryGetInstruction("PRINT", out _), "v24 instruction was not frozen.");

var mutableEffects = new VmEffect[] { new VmDisplayEffect(1, "display:original") };
var immutableStep = new VmStepResult(
    VmExecutionState.YieldedBudget,
    VmStepStopReason.BudgetExhausted,
    1,
    1,
    mutableEffects);
mutableEffects[0] = new VmDisplayEffect(2, "display:mutated");
Assert(
    immutableStep.Effects.Single() is VmDisplayEffect snapshot && snapshot.DisplayRevision == "display:original",
    "VmStepResult retained a mutable caller effect collection.");

var snakePlan = planBuilder.Build("snake", new[] { "game.snake" }, new[] { port });
Assert(snakePlan.Dialect.Modules.Select(module => module.ModuleId).SequenceEqual(new[] { "gemuera.v24", "game.snake" }), "Module dependency order is unstable.");
Assert(snakePlan.Dialect.Instructions.ContainsKey("SNAKE_PRINT"), "Snake contribution was not applied.");
Assert(snakePlan.CanonicalHash != v24Plan.CanonicalHash, "Different dialect plans share a canonical hash.");

var consumedV24 = LegacyCompatibilityPlanConsumption.Validate(
    v24Plan,
    v24Plan.Dialect.Instructions.Keys,
    v24Plan.Dialect.Functions.Keys);
Assert(consumedV24.ProfileId == "v24pure" && consumedV24.PlanHash == v24Plan.CanonicalHash,
    "Legacy plan consumption did not preserve plan identity.");
Assert(consumedV24.InstructionDescriptorCount == v24Plan.Dialect.Instructions.Count &&
       consumedV24.FunctionDescriptorCount == v24Plan.Dialect.Functions.Count &&
       consumedV24.HasDescriptorOverrides,
    "Legacy plan consumption did not observe the frozen baseline descriptors.");

var consumedSnake = LegacyCompatibilityPlanConsumption.Validate(
    snakePlan,
    snakePlan.Dialect.Instructions.Keys,
    snakePlan.Dialect.Functions.Keys);
Assert(consumedSnake.InstructionDescriptorCount == snakePlan.Dialect.Instructions.Count && consumedSnake.HasDescriptorOverrides,
    "Legacy plan consumption did not validate the selected instruction descriptor.");
// 会话驱动校验（方向已反转）：会话注册表里未被计划声明的名字必须被拒绝；
// 反之，计划多出的名字（如 snake 会话主动排除/条件可见）按条件可见性容忍并计数。
var consumedSnakeNarrowSession = LegacyCompatibilityPlanConsumption.Validate(
    snakePlan,
    new[] { "PRINT" },
    snakePlan.Dialect.Functions.Keys);
Assert(consumedSnakeNarrowSession.ConditionallyHiddenDescriptorCount > 0,
    "Legacy plan consumption did not tolerate conditionally hidden descriptors.");
AssertThrows<InvalidOperationException>(
    () => LegacyCompatibilityPlanConsumption.Validate(
        snakePlan,
        new[] { "PRINT", "UNDECLARED_LEGACY" },
        snakePlan.Dialect.Functions.Keys),
    "A legacy registry name missing from the compatibility plan was not rejected.");

var descriptorRoute = CompatibilityDescriptorRoute<string, string>.Create(
    snakePlan,
    new Dictionary<string, string>
    {
        ["PRINT"] = "legacy-print",
        ["SNAKE_PRINT"] = "legacy-snake-print",
    },
    new Dictionary<string, string> { ["RESULT"] = "legacy-result" });
Assert(
    descriptorRoute.Instructions.Count == snakePlan.Dialect.Instructions.Count &&
    descriptorRoute.Instructions["SNAKE_PRINT"] == "legacy-snake-print",
    "Descriptor route did not expose the selected legacy instruction handler.");
AssertThrows<InvalidOperationException>(
    () => CompatibilityDescriptorRoute<string, string>.Create(
        snakePlan,
        new Dictionary<string, string> { ["PRINT"] = "legacy-print" },
        new Dictionary<string, string> { ["RESULT"] = "legacy-result" }),
    "Descriptor route accepted a missing legacy instruction handler.");
// 内置方言目录的模块贡献已通电（生成清单）：BuiltIn v24 计划携带真实指令/函数描述符。
// 会话驱动路由视图：会话名必须被计划声明（漂移门禁），计划多余名字按条件可见性跳过。
var builtInSessionPlan = new CompatibilityPlanBuilder(BuiltInDialectCatalog.CreateLegacyBaseline())
    .Build("v24pure", new[] { "gemuera.v24" });
Assert(builtInSessionPlan.Dialect.Instructions.Count > 0 && builtInSessionPlan.Dialect.Functions.Count > 0,
    "Built-in v24 module did not contribute the generated instruction/function inventory.");
Assert(
    CompatibilityDescriptorRoute<string, string>.TryCreateSessionView(
        builtInSessionPlan,
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        out var emptySessionRoute)
    && emptySessionRoute.Instructions.Count == 0
    && emptySessionRoute.Functions.Count == 0,
    "Session view did not preserve an empty legacy registry baseline.");
Assert(
    CompatibilityDescriptorRoute<string, string>.TryCreateSessionView(
        builtInSessionPlan,
        new Dictionary<string, string> { ["PRINT"] = "legacy-print" },
        new Dictionary<string, string> { ["SETANIMETIMER"] = "legacy-timer" },
        out var declaredSessionRoute)
    && declaredSessionRoute.Instructions["PRINT"] == "legacy-print"
    && declaredSessionRoute.Functions["SETANIMETIMER"] == "legacy-timer",
    "Session view did not expose declared legacy handlers.");
Assert(
    !CompatibilityDescriptorRoute<string, string>.TryCreateSessionView(
        builtInSessionPlan,
        new Dictionary<string, string> { ["UNDECLARED_LEGACY"] = "drift" },
        new Dictionary<string, string>(),
        out _),
    "Session view accepted a legacy instruction missing from the compatibility plan.");

var v24EngineDescriptor = new ErbInterpreterDescriptor(
    "test.v24.only",
    "1.0.0",
    1,
    new[] { new ErbInterpreterModuleSupport("gemuera.v24", "[1.0.0,2.0.0)") },
    new[] { "gemuera.v24" });
var combinedDescriptorA = new ErbInterpreterDescriptor(
    "test.combined",
    "1.0.0",
    1,
    new[]
    {
        new ErbInterpreterModuleSupport("game.snake", "[1.0.0,2.0.0)"),
        new ErbInterpreterModuleSupport("gemuera.v24", "[1.0.0,2.0.0)"),
    },
    new[] { "gemuera.v24", "game.snake" });
var combinedDescriptorB = new ErbInterpreterDescriptor(
    "test.combined",
    "1.0.0",
    1,
    new[]
    {
        new ErbInterpreterModuleSupport("gemuera.v24", "[1.0.0,2.0.0)"),
        new ErbInterpreterModuleSupport("game.snake", "[1.0.0,2.0.0)"),
    },
    new[] { "gemuera.v24", "game.snake" });
Assert(v24EngineDescriptor.Supports(v24Plan), "v24 interpreter descriptor rejected its supported plan.");
Assert(!v24EngineDescriptor.Supports(snakePlan), "v24 interpreter descriptor accepted a Snake module it did not declare.");
Assert(combinedDescriptorA.Supports(snakePlan), "Combined interpreter descriptor rejected its supported plan.");
Assert(combinedDescriptorA.CanonicalHash == combinedDescriptorB.CanonicalHash, "Interpreter descriptor hash depends on module declaration order.");

var capabilityPlanA = planBuilder.Build("snake", new[] { "game.snake" }, new[] { port }, new[] { "runtime.sql.v1", "runtime.audio.v1" });
var capabilityPlanB = planBuilder.Build("snake", new[] { "game.snake" }, new[] { port }, new[] { "runtime.audio.v1", "runtime.sql.v1" });
Assert(capabilityPlanA.CanonicalHash == capabilityPlanB.CanonicalHash, "Plan hash depends on capability input order.");

var interpreterCatalog = new ErbInterpreterCatalog();
interpreterCatalog.Register(new TestInterpreterFactory("legacy-v24"));
var preFreezeContext = new ErbInterpreterContext(v24Plan, "fixture:pre-freeze");
AssertThrows<InvalidOperationException>(
    () => interpreterCatalog.Create("legacy-v24", preFreezeContext),
    "Interpreter catalog created an engine before it was frozen.");
AssertThrows<InvalidOperationException>(
    () => interpreterCatalog.Register(new TestInterpreterFactory("legacy-v24")),
    "Duplicate interpreter engine ids were accepted.");
var interpreterContext = new ErbInterpreterContext(v24Plan, "fixture:v24");
interpreterCatalog.Freeze();
Assert(interpreterCatalog.IsFrozen, "Interpreter catalog did not freeze.");
AssertThrows<InvalidOperationException>(
    () => interpreterCatalog.Register(new TestInterpreterFactory("legacy-v24-extra")),
    "Frozen interpreter catalog accepted a new engine.");
await using (var interpreter = interpreterCatalog.Create("legacy-v24", interpreterContext))
{
    var step = await interpreter.StepAsync(new VmStepBudget(16, 64));
    Assert(step.State == VmExecutionState.YieldedBudget, "Interpreter host did not honor the typed step contract.");
    Assert(ReferenceEquals(interpreter.Compatibility, v24Plan), "Interpreter host did not retain the frozen plan instance.");
}
AssertThrows<InvalidOperationException>(
    () => interpreterCatalog.Create("missing-engine", interpreterContext),
    "Unknown interpreter engine was accepted.");
AssertThrows<ArgumentException>(
    () => interpreterCatalog.Register(new TestInterpreterFactory("Legacy V24")),
    "Invalid interpreter engine ids were accepted.");

var multiVersionInterpreterCatalog = new ErbInterpreterCatalog();
multiVersionInterpreterCatalog.Register(new TestInterpreterFactory(
    InterpreterTestDescriptors.V24("test.versioned", "1.0.0")));
multiVersionInterpreterCatalog.Register(new TestInterpreterFactory(
    InterpreterTestDescriptors.V24("test.versioned", "2.0.0")));
var snapshotFactory = new MutableDescriptorInterpreterFactory(
    InterpreterTestDescriptors.V24("test.snapshot", "1.0.0"));
multiVersionInterpreterCatalog.Register(snapshotFactory);
snapshotFactory.ReplaceDescriptor(InterpreterTestDescriptors.Snake("test.snapshot", "2.0.0"));
IErbInterpreterCatalog frozenMultiVersionCatalog = multiVersionInterpreterCatalog.Freeze();
Assert(
    ReferenceEquals(frozenMultiVersionCatalog, multiVersionInterpreterCatalog.Freeze()),
    "Interpreter catalog freeze did not return a stable read-only runtime view.");
Assert(
    frozenMultiVersionCatalog.Descriptors.Select(descriptor => descriptor.Key.ToString()).SequenceEqual(new[]
    {
        "test.snapshot@1.0.0",
        "test.versioned@1.0.0",
        "test.versioned@2.0.0",
    }),
    "Frozen interpreter descriptor view was not a stable engine-id/version snapshot.");
await using (var versionOne = frozenMultiVersionCatalog.Create(
    "test.versioned",
    "1.0.0",
    interpreterContext))
{
    Assert(versionOne.Compatibility == v24Plan, "Exact interpreter version selection lost the supplied compatibility plan.");
}
await using (var versionTwo = multiVersionInterpreterCatalog.Create(
    "test.versioned",
    "2.0.0",
    interpreterContext))
{
    Assert(versionTwo.Compatibility == v24Plan, "Mutable catalog exact version selection failed after freeze.");
}
await using (var frozenSnapshot = frozenMultiVersionCatalog.Create(
    new ErbInterpreterKey("test.snapshot", "1.0.0"),
    interpreterContext))
{
    Assert(frozenSnapshot.Compatibility == v24Plan, "Frozen catalog re-read a factory descriptor after registration.");
    Assert(
        frozenSnapshot.Descriptor.CanonicalHash == frozenMultiVersionCatalog.Descriptors
            .Single(descriptor => descriptor.Key.ToString() == "test.snapshot@1.0.0")
            .CanonicalHash,
        "Frozen catalog did not inject its registered descriptor snapshot into the host.");
}
AssertThrows<InvalidOperationException>(
    () => multiVersionInterpreterCatalog.Create("test.versioned", interpreterContext),
    "Ambiguous unversioned interpreter selection was accepted.");
AssertThrows<InvalidOperationException>(
    () => frozenMultiVersionCatalog.Create(
        new ErbInterpreterKey("test.versioned", "3.0.0"),
        interpreterContext),
    "Unknown exact interpreter version was accepted.");

var hostIdentityCatalog = new ErbInterpreterCatalog();
hostIdentityCatalog.Register(new DescriptorIgnoringTestInterpreterFactory(
    InterpreterTestDescriptors.V24("test.host-identity", "1.0.0"),
    InterpreterTestDescriptors.V24("test.host-identity", "2.0.0")));
var frozenHostIdentityCatalog = hostIdentityCatalog.Freeze();
AssertThrows<InvalidOperationException>(
    () => frozenHostIdentityCatalog.Create(
        new ErbInterpreterKey("test.host-identity", "1.0.0"),
        interpreterContext),
    "Interpreter catalog accepted a host whose descriptor does not match the frozen registration.");

var resumableCatalog = new ErbInterpreterCatalog();
resumableCatalog.Register(new ResumableTestInterpreterFactory(
    "test.resumable",
    ResumableHostMode.BudgetThenInputThenComplete));
resumableCatalog.Register(new ResumableTestInterpreterFactory("test.invalid-sequence", ResumableHostMode.InvalidSequence));
resumableCatalog.Register(new ResumableTestInterpreterFactory("test.invalid-wait", ResumableHostMode.InvalidWaitResult));
resumableCatalog.Register(new ResumableTestInterpreterFactory("test.invalid-internal-state", ResumableHostMode.InvalidInternalState));
resumableCatalog.Register(new ResumableTestInterpreterFactory("test.invalid-budget", ResumableHostMode.InvalidBudget));
resumableCatalog.Register(new ResumableTestInterpreterFactory("test.throwing", ResumableHostMode.Throwing));
resumableCatalog.Register(new ResumableTestInterpreterFactory("test.blocking", ResumableHostMode.Blocking));
resumableCatalog.Freeze();
var resumableSession = new SessionStamp(new SessionGeneration(11), new SessionOperationId(3));
var resumableContext = new ErbInterpreterContext(v24Plan, "fixture:resumable", resumableSession);
await using (var resumable = resumableCatalog.Create("test.resumable", resumableContext))
{
    var budget = new VmStepBudget(8, 32);
    var yielded = await resumable.StepAsync(budget);
    Assert(yielded.State == VmExecutionState.YieldedBudget, "Resumable host did not preserve budget yield.");

    var waiting = await resumable.StepAsync(budget);
    Assert(waiting.State == VmExecutionState.WaitingInput, "Resumable host did not enter input wait.");
    Assert(waiting.Effects.Single() is VmInputEffect input && input.OperationId.Value == 7, "Input wait effect was not typed.");

    AssertThrows<InvalidOperationException>(
        () => resumable.StepAsync(budget).AsTask().GetAwaiter().GetResult(),
        "Step was accepted while the interpreter was waiting.");
    AssertThrows<InvalidOperationException>(
        () => resumable.ResumeAsync(
            new VmCompletion(
                new SessionStamp(new SessionGeneration(10), new SessionOperationId(3)),
                new VmOperationId(7),
                true,
                "stale"),
            budget).AsTask().GetAwaiter().GetResult(),
        "Stale completion was accepted by the interpreter.");

    var completed = await resumable.ResumeAsync(
        new VmCompletion(resumableSession, new VmOperationId(7), true, "ok"),
        budget);
    Assert(completed.State == VmExecutionState.Completed, "Matching completion did not resume the interpreter.");
    Assert(completed.Effects.Single().Sequence == 2, "Effect sequence did not continue across resume.");
    AssertThrows<InvalidOperationException>(
        () => resumable.StepAsync(budget).AsTask().GetAwaiter().GetResult(),
        "Terminal interpreter accepted another step.");
}

var unsupportedContext = new ErbInterpreterContext(snakePlan, "fixture:snake");
AssertThrows<InvalidOperationException>(
    () => resumableCatalog.Create("test.resumable", unsupportedContext),
    "Interpreter catalog accepted a plan outside the descriptor module range.");

await using (var failedCompletion = resumableCatalog.Create("test.resumable", resumableContext))
{
    var budget = new VmStepBudget(8, 32);
    _ = await failedCompletion.StepAsync(budget);
    _ = await failedCompletion.StepAsync(budget);
    var faulted = await failedCompletion.ResumeAsync(
        new VmCompletion(
            resumableSession,
            new VmOperationId(7),
            false,
            null,
            new VmFault("port.cancelled", "The host cancelled the input request.")),
        budget);
    Assert(faulted.State == VmExecutionState.Faulted && faulted.Fault?.Code == "port.cancelled", "Failed completion was not returned as a typed VM fault.");
}

await using (var invalidSequence = resumableCatalog.Create("test.invalid-sequence", resumableContext))
{
    _ = await invalidSequence.StepAsync(new VmStepBudget(8, 32));
    var faulted = await invalidSequence.StepAsync(new VmStepBudget(8, 32));
    Assert(faulted.State == VmExecutionState.Faulted && faulted.Fault?.Code == "vm.effect-sequence", "Invalid effect sequence did not become a typed VM fault.");
}

await using (var invalidWait = resumableCatalog.Create("test.invalid-wait", resumableContext))
{
    var faulted = await invalidWait.StepAsync(new VmStepBudget(8, 32));
    Assert(faulted.State == VmExecutionState.Faulted && faulted.Fault?.Code == "vm.wait-effect-count", "Invalid wait result did not become a typed VM fault.");
}

await using (var invalidInternalState = resumableCatalog.Create("test.invalid-internal-state", resumableContext))
{
    var faulted = await invalidInternalState.StepAsync(new VmStepBudget(8, 32));
    Assert(
        faulted.State == VmExecutionState.Faulted && faulted.Fault?.Code == "vm.result-state-invalid",
        "A future interpreter exposed an internal running state instead of a typed contract fault.");
}

await using (var invalidBudget = resumableCatalog.Create("test.invalid-budget", resumableContext))
{
    var faulted = await invalidBudget.StepAsync(new VmStepBudget(8, 32));
    Assert(
        faulted.State == VmExecutionState.Faulted && faulted.Fault?.Code == "vm.budget-exceeded",
        "An interpreter exceeded its requested step budget without a typed contract fault.");
}

await using (var throwing = resumableCatalog.Create("test.throwing", resumableContext))
{
    var faulted = await throwing.StepAsync(new VmStepBudget(8, 32));
    Assert(faulted.State == VmExecutionState.Faulted && faulted.Fault?.Code == "vm.execution-unhandled", "Unhandled engine exception escaped the typed VM fault boundary.");
}

var concurrentHost = (ResumableTestInterpreterHost)resumableCatalog.Create("test.blocking", resumableContext);
await using (concurrentHost)
{
    var firstStep = concurrentHost.StepAsync(new VmStepBudget(8, 32)).AsTask();
    await concurrentHost.ExecutionEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    AssertThrows<InvalidOperationException>(
        () => concurrentHost.StepAsync(new VmStepBudget(8, 32)).AsTask().GetAwaiter().GetResult(),
        "Concurrent interpreter execution was accepted.");
    concurrentHost.ExecutionRelease.TrySetResult(true);
    var firstResult = await firstStep;
    Assert(firstResult.State == VmExecutionState.YieldedBudget, "Blocking interpreter did not complete after release.");
}

var disposalRaceHost = (ResumableTestInterpreterHost)resumableCatalog.Create("test.blocking", resumableContext);
var disposalRaceStep = disposalRaceHost.StepAsync(new VmStepBudget(8, 32)).AsTask();
await disposalRaceHost.ExecutionEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
await disposalRaceHost.DisposeAsync();
disposalRaceHost.ExecutionRelease.TrySetResult(true);
var disposalRaceResult = await disposalRaceStep;
Assert(
    disposalRaceResult.State == VmExecutionState.Cancelled && disposalRaceHost.State == VmExecutionState.Cancelled,
    "An execution result arriving after disposal revived the interpreter state.");

var launchRoot = Path.Combine(Path.GetTempPath(), "gemuera-session-launch-contract");
var launchA = new LegacySessionLaunchConfiguration(
    Path.Combine(launchRoot, "game-a"),
    "v24pure");
var launchAEquivalent = new LegacySessionLaunchConfiguration(
    Path.Combine(launchRoot, "game-a", "."),
    "v24pure");
var launchAWithSnakeProfile = new LegacySessionLaunchConfiguration(
    Path.Combine(launchRoot, "game-a"),
    "snake");
var launchB = new LegacySessionLaunchConfiguration(
    Path.Combine(launchRoot, "game-b"),
    "snake");
Assert(
    launchA.GameId == launchAEquivalent.GameId,
    "Legacy launch identities changed for an equivalent game-root path.");
var launchRegistry = new LegacySessionLaunchRegistry(new[] { launchA, launchAWithSnakeProfile, launchB });
Assert(
    launchRegistry.Resolve(launchA.CreateSelection()).GameRoot == launchA.GameRoot,
    "Legacy launch registry did not resolve the selected game root.");
Assert(
    launchRegistry.Resolve(launchAWithSnakeProfile.CreateSelection()).ProfileId == "snake",
    "Legacy launch registry did not distinguish profiles that share one game root.");
AssertThrows<InvalidOperationException>(
    () => launchRegistry.Resolve(new SessionSelection("legacy-unbound", "v24pure")),
    "Legacy launch registry accepted an unbound game id.");
AssertThrows<InvalidOperationException>(
    () => launchRegistry.Resolve(new SessionSelection(launchB.GameId, "v24pure")),
    "Legacy launch registry accepted a profile that does not match the registered game route.");
AssertThrows<InvalidOperationException>(
    () => new LegacySessionLaunchRegistry(new[] { launchA, launchAEquivalent }),
    "Legacy launch registry accepted duplicate normalized game ids.");

var legacyBackend = new TestLegacyBackend();
var facade = new LegacySessionFacade(planBuilder, legacyBackend);
var facadeV24 = await facade.SwitchAsync(new SessionSelection("fixture-v24", "v24pure"), new[] { "gemuera.v24" });
Assert(facadeV24.IsCommitted, "Legacy facade did not start the v24 backend.");
Assert(facade.CurrentPlan?.ProfileId == "v24pure", "Legacy facade did not expose the committed compatibility plan.");
var facadeSnake = await facade.SwitchAsync(
    new SessionSelection("fixture-snake", "snake"),
    new[] { "game.snake" },
    new[] { port },
    new[] { "runtime.audio.v1" },
    "legacy.save.v1");
Assert(facadeSnake.IsCommitted, "Legacy facade did not switch to the Snake backend.");
Assert(facade.CurrentPlan?.Dialect.Ports.Single().PortTypeId == "IExtraArgumentPolicy", "Legacy facade discarded requested ports.");
Assert(facade.CurrentPlan?.CapabilityIds.Contains("runtime.audio.v1") == true
    && facade.CurrentPlan?.CapabilityIds.Contains(GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.ExtraCallArguments) == true,
    "Legacy facade discarded requested or profile-declared capabilities.");
Assert(facade.CurrentPlan?.SaveProfileId == "legacy.save.v1", "Legacy facade discarded the save profile id.");
Assert(facade.IsBackendRunning && facade.BackendGeneration == facade.Current?.Generation, "Legacy backend generation diverged from the committed plan.");
Assert(legacyBackend.Events.SequenceEqual(new[]
{
    "start:fixture-v24:v24pure:1",
    "stop:1",
    "start:fixture-snake:snake:2",
}), "Legacy facade lifecycle order is not deterministic.");
await facade.DisposeAsync();

var recoveryBackend = new TestLegacyBackend();
var recoveryFacade = new LegacySessionFacade(planBuilder, recoveryBackend);
var recoveryA = await recoveryFacade.SwitchAsync(new SessionSelection("recovery-a", "v24pure"), new[] { "gemuera.v24" });
Assert(recoveryA.IsCommitted, "Recovery fixture could not start its baseline backend.");
recoveryBackend.FailNextStart = true;
var failedRecoveryB = await recoveryFacade.SwitchAsync(new SessionSelection("recovery-b", "snake"), new[] { "game.snake" });
Assert(!failedRecoveryB.IsCommitted && failedRecoveryB.Session.Status == SessionSwitchStatus.Faulted, "Backend start failure was not surfaced as an uncommitted fault.");
Assert(failedRecoveryB.PreviousBackendRestored && recoveryBackend.IsRunning, "Backend start failure did not restore the prior runtime.");
Assert(recoveryFacade.Current?.Selection.GameId == "recovery-a", "Backend start failure replaced Current before activation succeeded.");
var recoveredB = await recoveryFacade.SwitchAsync(new SessionSelection("recovery-b", "snake"), new[] { "game.snake" });
var recoveredA = await recoveryFacade.SwitchAsync(new SessionSelection("recovery-a", "v24pure"), new[] { "gemuera.v24" });
Assert(recoveredB.IsCommitted && recoveredA.IsCommitted, "A-to-B-to-A legacy backend recovery sequence did not commit.");
Assert(recoveryFacade.Current?.Selection.GameId == "recovery-a", "A-to-B-to-A did not end on the expected current session.");
await recoveryFacade.DisposeAsync();

var staleBackend = new TestLegacyBackend();
var staleFacade = new LegacySessionFacade(planBuilder, staleBackend);
var staleA = await staleFacade.SwitchAsync(new SessionSelection("stale-a", "v24pure"), new[] { "gemuera.v24" });
Assert(staleA.IsCommitted, "Stale activation fixture could not start its baseline backend.");
var staleStartGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var staleStartEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
staleBackend.StartGate = staleStartGate;
staleBackend.StartEntered = staleStartEntered;
staleBackend.IgnoreStartCancellation = true;
var staleBTask = staleFacade.SwitchAsync(new SessionSelection("stale-b", "snake"), new[] { "game.snake" }).AsTask();
await staleStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
var staleCTask = staleFacade.SwitchAsync(new SessionSelection("stale-c", "v24pure"), new[] { "gemuera.v24" }).AsTask();
staleStartGate.TrySetResult(true);
var staleB = await staleBTask;
var staleC = await staleCTask;
Assert(staleB.Session.Status == SessionSwitchStatus.Stale && staleB.PreviousBackendRestored, "A stale backend start was not rolled back to the previous runtime.");
Assert(staleC.IsCommitted && staleFacade.Current?.Selection.GameId == "stale-c", "A newer request did not replace a stale backend candidate.");
await staleFacade.DisposeAsync();

var cancellationBackend = new TestLegacyBackend();
var cancellationFacade = new LegacySessionFacade(planBuilder, cancellationBackend);
var cancellationA = await cancellationFacade.SwitchAsync(new SessionSelection("cancel-a", "v24pure"), new[] { "gemuera.v24" });
Assert(cancellationA.IsCommitted, "Cancellation fixture could not start its baseline backend.");
var cancellationStartGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var cancellationStartEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
cancellationBackend.StartGate = cancellationStartGate;
cancellationBackend.StartEntered = cancellationStartEntered;
using (var cancellation = new CancellationTokenSource())
{
    var cancelledTask = cancellationFacade.SwitchAsync(
        new SessionSelection("cancel-b", "snake"),
        new[] { "game.snake" },
        cancellationToken: cancellation.Token).AsTask();
    await cancellationStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellationBackend.StartGate = null;
    cancellation.Cancel();
    cancellationStartGate.TrySetResult(true);
    var cancelled = await cancelledTask;
    Assert(cancelled.Session.Status == SessionSwitchStatus.Cancelled && cancelled.PreviousBackendRestored, "Cancellation did not leave the old backend and Current intact.");
}
Assert(cancellationBackend.IsRunning && cancellationFacade.Current?.Selection.GameId == "cancel-a", "Cancellation replaced the active legacy session.");
await cancellationFacade.DisposeAsync();

var baselineFacade = LegacySessionFacade.CreateLegacyBaseline(new TestLegacyBackend());
var baselineSwitch = await baselineFacade.SwitchAsync(
    new SessionSelection("fixture-baseline", "snake"),
    null,
    new[] { port });
Assert(baselineSwitch.IsCommitted, "Built-in legacy baseline facade could not build a Snake plan.");
Assert(baselineFacade.CurrentPlan?.Dialect.Modules.Count == 2, "Built-in baseline facade did not resolve module dependencies.");
Assert(baselineFacade.CurrentPlan?.Dialect.Ports.Single().PortTypeId == "IExtraArgumentPolicy", "Built-in baseline did not declare the reviewed Snake port surface.");
Assert(
    baselineFacade.CurrentPlan?.Dialect.Modules.Single(module => module.ModuleId == "game.snake").PortTypeIds.Count == 10,
    "Built-in baseline Snake module did not preserve the complete reviewed port declaration surface.");
await baselineFacade.DisposeAsync();

var eraFlFacade = LegacySessionFacade.CreateLegacyBaseline(new TestLegacyBackend());
var eraFlSwitch = await eraFlFacade.SwitchAsync(
    new SessionSelection("fixture-erafl", "erafl"),
    null);
Assert(eraFlSwitch.IsCommitted, "Built-in eraFL profile could not build a candidate.");
Assert(eraFlFacade.CurrentPlan?.ProfileId == "erafl", "eraFL plan did not preserve its profile id.");
Assert(
    eraFlFacade.CurrentPlan?.Dialect.Modules.Select(module => module.ModuleId)
        .SequenceEqual(new[] { "gemuera.v24", "game.erafl" }) == true,
    "eraFL plan did not resolve the v24 dependency closure.");
Assert(
    eraFlFacade.CurrentPlan?.Dialect.Ports.Count == 5,
    "eraFL plan did not include all independent typed policy declarations.");
Assert(
    eraFlFacade.CurrentPlan?.CapabilityIds.SequenceEqual(new[]
    {
        "arith.safe-arithmetic-guard.v1",
        "declare.out-keyword.v1",
        "display.dynamic-map-transaction.v1",
        "display.extended-history.v1",
        "input.omitted-default-argument.v1",
        "input.pointer-blank-string.v1",
        "input.pointer-button.v1",
        "markup.div-v2.v1",
        "markup.font-extended-attributes.v1",
        "markup.image-dual-src.v1",
        "resource.dynamic-sprite.v1",
        "type.float-literals.v1",
    }) == true,
    "eraFL plan did not include the required capability set.");
Assert(
    eraFlFacade.CurrentPlan?.SaveProfileId == EraFlCompatibilityModule.SaveProfileId,
    "eraFL plan did not select its independent save profile.");
Assert(
    EraFlCompatibilityModule.IsOmittedDefaultArgument(',')
        && !EraFlCompatibilityModule.IsOmittedDefaultArgument('1'),
    "eraFL omitted-argument policy drifted.");
Assert(
    EraFlCompatibilityModule.IsPointerInputMetadataOption("1")
        && EraFlCompatibilityModule.IsPointerInputMetadataOption(" 1 ")
        && !EraFlCompatibilityModule.IsPointerInputMetadataOption("")
        && !EraFlCompatibilityModule.IsPointerInputMetadataOption("0")
        && !EraFlCompatibilityModule.IsPointerInputMetadataOption("1,2"),
    "eraFL INPUTS pointer metadata option policy drifted.");
Assert(
    EraFlCompatibilityModule.NormalizePointerIntegerSubmission("", 2, true) == "-1"
        && EraFlCompatibilityModule.NormalizePointerIntegerSubmission("", 1, true) == ""
        && EraFlCompatibilityModule.NormalizePointerIntegerSubmission("", 2, false) == "",
    "eraFL pointer blank-integer policy drifted.");
Assert(
    EraFlCompatibilityModule.ShouldSubmitBlankPointerStringInput(1, true)
        && EraFlCompatibilityModule.ShouldSubmitBlankPointerStringInput(2, true)
        && EraFlCompatibilityModule.ShouldSubmitBlankPointerStringInput(4, true)
        && !EraFlCompatibilityModule.ShouldSubmitBlankPointerStringInput(0, true)
        && !EraFlCompatibilityModule.ShouldSubmitBlankPointerStringInput(1, false),
    "eraFL pointer blank-string policy drifted.");
Assert(
    EraFlCompatibilityModule.NormalizePointerButtonResult(1) == 1
        && EraFlCompatibilityModule.NormalizePointerButtonResult(2) == 2
        && EraFlCompatibilityModule.NormalizePointerButtonResult(4) == 3,
    "eraFL pointer button result mapping drifted.");
var eraFlQuestMap = new string[2, 4];
eraFlQuestMap[0, 1] = "[ROOM_ID:2000]";
eraFlQuestMap[1, 2] = "[ROOM_ID:200][ROOM_PATH:1,]";
Assert(
    EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "HO_FIND_ROOM_BY_TAG", -1, "任务開始地点", 1, eraFlQuestMap, out var recoveredQuestStart)
        && recoveredQuestStart == 2,
    "eraFL task-start recovery did not return the current map's room index.");
Assert(
    legacyEraFlProfile.EraFl.TaskStartRoomLookupFunction == "HO_FIND_ROOM_BY_TAG"
        && legacyEraFlProfile.EraFl.GMapQuestType == "GMAP"
        && legacyEraFlProfile.EraFl.TryRecoverQuestStartRoomIndex(
            "HO_FIND_ROOM_BY_TAG", -1, "任务開始地点", 1, "MAP", eraFlQuestMap, out var bridgedQuestStart)
        && bridgedQuestStart == 2,
    "eraFL legacy policy did not project its task-start recovery boundary.");
Assert(
    !EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "HO_FIND_ROOM_BY_TAG", 7, "任务開始地点", 1, eraFlQuestMap, out var retainedQuestStart)
        && retainedQuestStart == 7,
    "eraFL task-start recovery rewrote a successful room lookup.");
Assert(
    !EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "OTHER_FIND_ROOM", -1, "任务開始地点", 1, eraFlQuestMap, out _)
    && !EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "HO_FIND_ROOM_BY_TAG", -1, "其它标签", 1, eraFlQuestMap, out _)
    && !EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "HO_FIND_ROOM_BY_TAG", -1, "任务開始地点", 2, eraFlQuestMap, out _)
    && !EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "HO_FIND_ROOM_BY_TAG", -1, "任务開始地点", 0, eraFlQuestMap, out _),
    "eraFL task-start recovery escaped its exact function, tag, or map-data boundary.");
var ambiguousEraFlQuestMap = new string[1, 2];
ambiguousEraFlQuestMap[0, 0] = "[ROOM_ID:200]";
ambiguousEraFlQuestMap[0, 1] = "[ROOM_ID:200][ROOM_PATH:1,]";
Assert(
    !EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "HO_FIND_ROOM_BY_TAG", -1, "任务開始地点", 0, ambiguousEraFlQuestMap, out var ambiguousQuestStart)
        && ambiguousQuestStart == -1,
    "eraFL task-start recovery guessed between multiple start rooms.");
var eraFlGMapWithoutRoomId = new string[1, 2];
eraFlGMapWithoutRoomId[0, 0] = "[EVENT_LIST:0,][ROOM_NAME:城镇入口][ROOM_PATH:1,]";
Assert(
    !EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "HO_FIND_ROOM_BY_TAG", -1, "任务開始地点", 0, "GMAP", eraFlGMapWithoutRoomId, out _),
    "eraFL GMAP task-start recovery accepted an unmaterialized node zero.");
Assert(
    !EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "HO_FIND_ROOM_BY_TAG", -1, "任务開始地点", 0, "MAP", eraFlGMapWithoutRoomId, out _),
    "eraFL GMAP node-zero fallback escaped into ordinary MAP quests.");
Assert(
    !EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "HO_FIND_ROOM_BY_TAG", -1, "任务開始地点", -1, "GMAP", null, out _),
    "eraFL GMAP task-start recovery guessed without runtime map data.");
var emptyEraFlGMap = new string[1, 1];
Assert(
    !EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "HO_FIND_ROOM_BY_TAG", -1, "任务開始地点", 0, "GMAP", emptyEraFlGMap, out _),
    "eraFL GMAP task-start recovery accepted an empty node-zero slot.");
var eraFlGMapNodes = new[]
{
    new EraFlCompatibilityModule.GMapNodeData(1, "林荫道", "0,2,"),
    new EraFlCompatibilityModule.GMapNodeData(0, "城镇入口", "1,"),
};
var eraFlXmlTable = new DataTable("GMAPDATA");
var eraFlXmlId = eraFlXmlTable.Columns.Add("id", typeof(long));
eraFlXmlTable.Columns.Add("NODE_ID", typeof(short));
eraFlXmlTable.Columns.Add("NODE_NAME", typeof(string));
eraFlXmlTable.Columns.Add("POS_X", typeof(short));
eraFlXmlTable.Columns.Add("POS_Y", typeof(short));
eraFlXmlTable.Columns.Add("NODE_ICON", typeof(string));
eraFlXmlTable.Columns.Add("NODE_COLOR", typeof(short));
eraFlXmlTable.Columns.Add("PATH_LIST", typeof(string));
eraFlXmlTable.Columns.Add("EVENT_LIST", typeof(string));
eraFlXmlTable.PrimaryKey = new[] { eraFlXmlId };
eraFlXmlTable.Rows.Add(10L, (short)1, "林荫道", (short)420, (short)100, DBNull.Value, DBNull.Value, "0,2,", DBNull.Value);
eraFlXmlTable.Rows.Add(11L, (short)0, "城镇入口", (short)520, (short)40, DBNull.Value, DBNull.Value, "1,", DBNull.Value);
string eraFlSchemaXml;
string eraFlDataXml;
using (var writer = new StringWriter())
{
    eraFlXmlTable.WriteXmlSchema(writer);
    eraFlSchemaXml = writer.ToString();
}
using (var writer = new StringWriter())
{
    eraFlXmlTable.WriteXml(writer);
    eraFlDataXml = writer.ToString();
}
Assert(
    EraFlCompatibilityModule.TryParseGMapDataTableFromXml(
        eraFlSchemaXml,
        eraFlDataXml,
        out var parsedEraFlGMapTable,
        out var parsedEraFlGMapTableNodes)
        && parsedEraFlGMapTable is not null
        && parsedEraFlGMapTable.TableName == "GMAPDATA"
        && parsedEraFlGMapTable.Columns.Contains("POS_X")
        && parsedEraFlGMapTable.Columns.Contains("POS_Y")
        && parsedEraFlGMapTable.Rows.Count == 2
        && Convert.ToInt64(parsedEraFlGMapTable.Rows[1]["POS_X"]) == 520
        && Convert.ToInt64(parsedEraFlGMapTable.Rows[1]["POS_Y"]) == 40
        && parsedEraFlGMapTableNodes.Count == 2,
    "eraFL GMAP schema/XML fallback discarded the complete drawing table.");
parsedEraFlGMapTable?.Dispose();
Assert(
    legacyEraFlProfile.EraFl.TryParseGMapDataTableFromXml(
        eraFlSchemaXml,
        eraFlDataXml,
        out var bridgedEraFlGMapTable,
        out var bridgedEraFlGMapNodes)
        && bridgedEraFlGMapTable.TableName == "GMAPDATA"
        && bridgedEraFlGMapNodes.Count == 2
        && bridgedEraFlGMapNodes[1].NodeId == 0
        && bridgedEraFlGMapNodes[1].NodeName == "城镇入口",
    "eraFL legacy policy did not project GMAP XML data into the bridge DTO.");
bridgedEraFlGMapTable.Dispose();
Assert(
    EraFlCompatibilityModule.TryParseGMapNodesFromXml(
        eraFlSchemaXml,
        eraFlDataXml,
        out var parsedEraFlGMapNodes)
        && parsedEraFlGMapNodes.Count == 2
        && parsedEraFlGMapNodes[1].NodeId == 0
        && parsedEraFlGMapNodes[1].NodeName == "城镇入口"
        && parsedEraFlGMapNodes[1].PathList == "1,",
    "eraFL GMAP schema/XML fallback did not parse the node contract.");
Assert(
    !EraFlCompatibilityModule.TryParseGMapNodesFromXml("<bad>", "<bad>", out _),
    "eraFL GMAP schema/XML fallback accepted malformed XML.");
var eraFlRoomOnlyXmlTable = new DataTable("GMAPDATA");
eraFlRoomOnlyXmlTable.Columns.Add("NODE_ID", typeof(short));
eraFlRoomOnlyXmlTable.Columns.Add("NODE_NAME", typeof(string));
eraFlRoomOnlyXmlTable.Columns.Add("PATH_LIST", typeof(string));
eraFlRoomOnlyXmlTable.Rows.Add((short)0, "入口", "1,");
string eraFlRoomOnlySchemaXml;
string eraFlRoomOnlyDataXml;
using (var writer = new StringWriter())
{
    eraFlRoomOnlyXmlTable.WriteXmlSchema(writer);
    eraFlRoomOnlySchemaXml = writer.ToString();
}
using (var writer = new StringWriter())
{
    eraFlRoomOnlyXmlTable.WriteXml(writer);
    eraFlRoomOnlyDataXml = writer.ToString();
}
Assert(
    EraFlCompatibilityModule.TryParseGMapNodesFromXml(
        eraFlRoomOnlySchemaXml,
        eraFlRoomOnlyDataXml,
        out var eraFlRoomOnlyNodes)
        && eraFlRoomOnlyNodes.Count == 1
        && !EraFlCompatibilityModule.TryParseGMapDataTableFromXml(
            eraFlRoomOnlySchemaXml,
            eraFlRoomOnlyDataXml,
            out _,
            out _),
    "eraFL room-only node parsing and complete drawing-table validation were not separated.");
Assert(
    EraFlCompatibilityModule.TryPopulateGMapRoomData(0, eraFlGMapWithoutRoomId, eraFlGMapNodes)
        && eraFlGMapWithoutRoomId[0, 0].Contains("[EVENT_LIST:0,]", StringComparison.Ordinal)
        && eraFlGMapWithoutRoomId[0, 0].Contains("[ROOM_NAME:城镇入口]", StringComparison.Ordinal)
        && eraFlGMapWithoutRoomId[0, 0].Contains("[ROOM_PATH:1,]", StringComparison.Ordinal)
        && eraFlGMapWithoutRoomId[0, 0].Contains("[ROOM_ID:200]", StringComparison.Ordinal)
        && eraFlGMapWithoutRoomId[0, 1].Contains("[ROOM_ID:201]", StringComparison.Ordinal),
    "eraFL GMAP DT bridge did not materialize the runtime room dictionary.");
Assert(
    EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
        "HO_FIND_ROOM_BY_TAG", -1, "任务開始地点", 0, "GMAP", eraFlGMapWithoutRoomId, out var recoveredGMapNodeZero)
        && recoveredGMapNodeZero == 0,
    "eraFL GMAP task-start recovery rejected a materialized node zero.");
var duplicateEraFlGMap = new string[1, 2];
Assert(
    !EraFlCompatibilityModule.TryPopulateGMapRoomData(
        0,
        duplicateEraFlGMap,
        new[]
        {
            new EraFlCompatibilityModule.GMapNodeData(0, "入口A", "1,"),
            new EraFlCompatibilityModule.GMapNodeData(0, "入口B", "1,"),
        })
        && duplicateEraFlGMap[0, 0] == null,
    "eraFL GMAP DT bridge partially mutated an invalid node set.");
await eraFlFacade.DisposeAsync();

AssertThrows<ArgumentException>(
    () => planBuilder.Build("snake", new[] { "game.snake" }, new[]
    {
        new BehaviorPortSnapshot(
            "call.private-argument-shape.v1",
            "IPrivateArgumentShapePolicy",
            PortContractKind.PolicyDecision,
            "IPrivateArgumentShapePolicy",
            "policy.call.private-argument-shape.v1",
            "game.snake",
            "DIA-CALL-PRIVATE-001"),
    }),
    "A port undeclared by the selected module was accepted.");

var gated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var coordinator = new SessionCoordinator(async (selection, generation, cancellationToken) =>
{
    if (selection.GameId == "game.a")
        await gated.Task.WaitAsync(cancellationToken);
    var plan = selection.GameId == "game.a" ? v24Plan : snakePlan;
    return new SessionCandidate(selection, generation, plan);
});

var switchA = coordinator.SwitchGameAsync(new SessionSelection("game.a", "v24pure")).AsTask();
await Task.Delay(20);
var resultB = await coordinator.SwitchGameAsync(new SessionSelection("game.b", "snake"));
gated.TrySetResult(true);
var resultA = await switchA;
Assert(resultB.IsCommitted && resultB.Session?.Selection.GameId == "game.b", "Latest session was not committed.");
Assert(resultA.Status is SessionSwitchStatus.Stale or SessionSwitchStatus.Cancelled, "Stale candidate was committed.");
Assert(coordinator.Current?.Selection.GameId == "game.b", "Current session was overwritten by a stale completion.");
await coordinator.DisposeAsync();

var leaseCoordinator = new SessionCoordinator((selection, generation, cancellationToken) =>
{
    var plan = selection.GameId == "lease-a" ? v24Plan : snakePlan;
    return ValueTask.FromResult(new SessionCandidate(selection, generation, plan));
});
var preparedA = await leaseCoordinator.PrepareSwitchAsync(new SessionSelection("lease-a", "v24pure"));
Assert(preparedA.IsPrepared && leaseCoordinator.Current is null, "Prepared candidate mutated Current before activation/commit.");
var preparedB = await leaseCoordinator.PrepareSwitchAsync(new SessionSelection("lease-b", "snake"));
Assert(preparedB.IsPrepared, "Second prepared candidate was not created.");
var committedLeaseB = await preparedB.Lease!.CommitAsync();
var staleLeaseA = await preparedA.Lease!.CommitAsync();
Assert(committedLeaseB.IsCommitted && staleLeaseA.Status == SessionSwitchStatus.Stale, "Lease commit did not reject an obsolete prepared candidate.");
Assert(leaseCoordinator.Current?.Selection.GameId == "lease-b", "Lease commit did not preserve the latest candidate as Current.");
await leaseCoordinator.DisposeAsync();

var generationClock = new SessionGenerationClock();
var generationA = generationClock.PublishNext();
var operationA = new SessionOperationId(1);
var stampA = new SessionStamp(generationA, operationA);
var generationB = generationClock.PublishNext();
var stampB = new SessionStamp(generationB, new SessionOperationId(2));
Assert(!SessionCompletionGuard.IsCurrent(stampA, stampB), "Stale completion was accepted.");
Assert(SessionCompletionGuard.IsCurrent(stampB, stampB), "Current completion was rejected.");

var mutablePorts = new[] { port };
var isolated = CompatibilityPlanSnapshot.Create("snake", new[] { baseModule, snakeModule }, mutablePorts);
mutablePorts[0] = new BehaviorPortSnapshot(
    "call.private-argument-shape.v1",
    "IExtraArgumentPolicy",
    PortContractKind.PolicyDecision,
    "IExtraArgumentPolicy",
    "policy.call.private-argument-shape.v1",
    "game.snake",
    "DIA-CALL-PRIVATE-001");
Assert(isolated.Ports[0].BehaviorKeyId == "call.extra-arguments.v1", "Snapshot retained mutable caller collection.");

var eraflEvidence = new GameCompatibilityProbeEvidence(
    1,
    new GameBaseProbeEvidence(9224518, "eraFL", true),
    new[]
    {
        new GameCompatibilityAnchorEvidence("erafl.system-title", true),
        new GameCompatibilityAnchorEvidence("erafl.init-loader", true),
    });
var eraflResolution = BuiltInGameCompatibilityResolver.Resolve(eraflEvidence);
Assert(eraflResolution.CanAutoSelect, "eraFL evidence was not auto-resolved.");
Assert(eraflResolution.GameFamilyId == BuiltInGameCompatibilityResolver.EraFlGameFamilyId,
    "eraFL evidence resolved to the wrong game family.");
Assert(eraflResolution.ProfileId == BuiltInGameCompatibilityResolver.EraFlProfileId,
    "eraFL did not select its independent profile.");

var eratwEvidence = new GameCompatibilityProbeEvidence(
    2,
    new GameBaseProbeEvidence(7153, "eraThe World【画蛇添足版】", true),
    new[] { new GameCompatibilityAnchorEvidence("eratw.version", true) });
var eratwResolution = BuiltInGameCompatibilityResolver.Resolve(eratwEvidence);
Assert(eratwResolution.CanAutoSelect && eratwResolution.ProfileId == BuiltInGameCompatibilityResolver.SnakeProfileId,
    "eraTW evidence did not resolve to the Snake profile.");

var ambiguousEvidence = new GameCompatibilityProbeEvidence(
    3,
    new GameBaseProbeEvidence(7153, "eraFL", true),
    new[] { new GameCompatibilityAnchorEvidence("erafl.system-title", true) });
var ambiguousResolution = BuiltInGameCompatibilityResolver.Resolve(ambiguousEvidence);
Assert(!ambiguousResolution.CanAutoSelect && ambiguousResolution.Status == GameCompatibilityResolutionStatus.Ambiguous,
    "Conflicting game identity evidence was silently auto-selected.");

var unknownResolution = BuiltInGameCompatibilityResolver.Resolve(
    new GameCompatibilityProbeEvidence(
        4,
        new GameBaseProbeEvidence(null, "unknown game", true)));
Assert(!unknownResolution.CanAutoSelect && unknownResolution.Status == GameCompatibilityResolutionStatus.Unknown,
    "Insufficient identity evidence was auto-selected.");

var probeRoot = Path.Combine(Path.GetTempPath(), "gemuera-compat-probe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(probeRoot, "CSV"));
Directory.CreateDirectory(Path.Combine(probeRoot, "ERB", "SYSTEM"));
Directory.CreateDirectory(Path.Combine(probeRoot, "ERB", "TRAIN"));
File.WriteAllText(
    Path.Combine(probeRoot, "CSV", "GameBase.csv"),
    "コード,9224518\nタイトル,eraFL\n",
    Encoding.UTF8);
File.WriteAllText(
    Path.Combine(probeRoot, "ERB", "SYSTEM", "NEWGAME.ERB"),
    "@SYSTEM_TITLE\nPRINTL eraFL\n",
    Encoding.UTF8);
File.WriteAllText(
    Path.Combine(probeRoot, "ERB", "SYSTEM", "FL_INIT_LOADER.ERB"),
    "@FL_INIT_LOADER\n",
    Encoding.UTF8);
File.WriteAllText(
    Path.Combine(probeRoot, "ERB", "TRAIN", "USERCOM_INPUT.ERB"),
    "@FL_USERCOM\n",
    Encoding.UTF8);
try
{
    Assert(GameCompatibilityDetector.TryDetectProfile(probeRoot, out string detectedProbeProfile, out var detectedProbe),
        "Host probe did not detect the synthetic eraFL fixture.");
    Assert(detectedProbeProfile == BuiltInGameCompatibilityResolver.EraFlProfileId
        && detectedProbe.GameFamilyId == BuiltInGameCompatibilityResolver.EraFlGameFamilyId,
        "Host probe selected the wrong synthetic eraFL profile.");
}
finally
{
    Directory.Delete(probeRoot, recursive: true);
}

var eraTwProbeRoot = Path.Combine(Path.GetTempPath(), "gemuera-eratw-probe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(eraTwProbeRoot, "ERB", "NEWGAME"));
File.WriteAllText(
    Path.Combine(eraTwProbeRoot, "ERB", "DIM.ERH"),
    "#DIMS CONST eraTW_Version = \"4.981\"\n",
    Encoding.UTF8);
File.WriteAllText(
    Path.Combine(eraTwProbeRoot, "ERB", "SYSTEM.ERB"),
    "@EVENTFIRST\nCALL NEWGAME\nSIF SAVESTR:version != eraTW_Version\n",
    Encoding.UTF8);
File.WriteAllText(
    Path.Combine(eraTwProbeRoot, "ERB", "NEWGAME", "NEWGAME.ERB"),
    "@NEWGAME\nSAVESTR:version = %eraTW_Version%\n",
    Encoding.UTF8);
File.WriteAllText(
    Path.Combine(eraTwProbeRoot, "ERB", "TITLE.ERB"),
    "@SYSTEM_TITLE\nPRINTL %eraTW_Version%\n",
    Encoding.UTF8);
try
{
    Assert(GameCompatibilityDetector.TryDetectProfile(eraTwProbeRoot, out string detectedEraTwProfile, out var detectedEraTw),
        "Host probe did not detect the no-GAMEBASE eraTW fixture.");
    Assert(
        detectedEraTwProfile == BuiltInGameCompatibilityResolver.SnakeProfileId
        && detectedEraTw.GameFamilyId == BuiltInGameCompatibilityResolver.EraTwGameFamilyId
        && detectedEraTw.Confidence == GameCompatibilityConfidence.StrongFallback,
        "Host probe selected the wrong no-GAMEBASE eraTW profile.");
}
finally
{
    Directory.Delete(eraTwProbeRoot, recursive: true);
}

var coreAssembly = typeof(CompatibilityPlanSnapshot).Assembly;
Assert(
    coreAssembly.GetReferencedAssemblies().All(reference =>
        !reference.Name!.StartsWith("Godot", StringComparison.OrdinalIgnoreCase) &&
        !reference.Name!.Contains("GodotSharp", StringComparison.OrdinalIgnoreCase)),
    "Core assembly references Godot or GodotSharp.");

DisplayContracts.Run();
await ContractSmoke.RunAsync();

Console.WriteLine("Core contract smoke passed.");
