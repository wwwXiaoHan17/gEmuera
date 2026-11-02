using CompatPackContractOnlyFixture;
using CompatPackHostBindingProbe;
using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// ALC 允许清单隔离用例（docs/designs/compat-pack-interface.md §5.2）：包加载上下文对
/// 绑定名 fail-closed——只放行 Emuera 契约程序集、BCL（netstandard/System.*/Microsoft.*）
/// 与包目录依赖字节图；宿主内部程序集（如 GEmuera.Core）不再经 ALC 分支或默认解析回落泄露给包。
/// 正向对照 = Task 1 契约夹具（只引用契约程序集）完整加载 + 表面贡献折叠进 v24pure 会话计划；
/// 负向探针 = CompatPackHostBindingProbe（故意引用 GEmuera.Core）必须在 TryLoad 阶段拒载。
/// T5 起所有加载经隔离临时包目录：加载器把包目录顶层全部 *.dll 读成依赖闭包。
/// 类型跨 ALC 不统一：断言一律按贡献 id / 指令名 / 冻结状态，禁止 is Type。
/// </summary>
[Collection("CompatPack")]
public class CompatPackAlcIsolationTests
{
    static CompatPackValidationContext RealContext() => new(
        currentContractAbi: new CompatVersion(1, 0),
        currentEngineApi: new CompatVersion(1, 0),
        capabilities: EngineCapabilityCatalog.All,
        knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        baselineInstructions: new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        baselineFunctions: new HashSet<string>(
            LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

    public CompatPackAlcIsolationTests()
    {
        // 前置事实自检：SETANIMETIMER 在 v24 基线外（两夹具的注册名合法），探针的 Core
        // 类型引用真实存在（防止重命名后探针静默失去绑定需求、测试假绿）。
        Assert.DoesNotContain("SETANIMETIMER", LegacyDialectInventories.V24InstructionNames);
        Assert.Equal("GEmuera.Core", typeof(CompatibilityPlan).Assembly.GetName().Name);
    }

    [Fact]
    public void Alc_HostBindingProbePack_IsRejected()
    {
        // 部署形态模拟：把探针包文件单独复制进空目录再加载。真实部署的包目录只含包自身与
        // 包作者显式携带的依赖；bin 输出目录里的 GEmuera.Core.dll 是构建产物（引擎侧
        // ProjectReference 依赖流），按规则加载它属于"包自带私有副本"语义，不是宿主
        // 内部程序集泄露。空目录下探针绑定 GEmuera.Core：硬化前 null 回落默认解析会命中
        // 宿主已加载的 GEmuera.Core 实例（RED 证据：本用例修复前以"探针加载成功"失败），
        // 硬化后必须 FileLoadException 拒载（fail-closed）。
        using var directory = new IsolatedPackDirectory(typeof(HostBindingProbePack).Assembly.Location);

        Assert.False(
            CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var handle, out var errors),
            "探针在硬化前被成功加载（宿主内部程序集经 ALC 回落泄露给包）：" + string.Join("; ", errors));

        Assert.Null(handle);
        // 拒载错误必须可归因：含 FileLoadException 消息或 GEmuera.Core 诊断。
        Assert.Contains(errors, e => e.Contains("GEmuera.Core") || e.Contains("FileLoadException"));
    }

    [Fact]
    public void Alc_ContractOnlyFixture_Loads()
    {
        using var directory = new IsolatedPackDirectory(typeof(ContractOnlyPack).Assembly.Location);

        Assert.True(
            CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var handle, out var errors),
            string.Join("; ", errors));
        try
        {
            // 允许清单硬化不得误伤契约-only 包：表面贡献经冻结差量断言
            //（包 ALC 内的类型与测试工程静态引用类型非同一 Type，契约接口跨 ALC 统一）。
            Assert.Contains(handle!.ValidatedDelta.SurfaceStates, state =>
                state.Key == new CompatSymbolKey(CompatSymbolKind.Instruction, "SETANIMETIMER")
                && state.Kind == CompatSymbolStateKind.Present
                && state.OwnerPackId == "community.contract-fixture");

            // 组装正向：表面贡献真实折叠进 v24pure 会话计划（指令落位 + 模块归属 + 哈希变化）。
            CompatibilityPlan baseline = BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");
            Assert.True(
                CompatPackPlanAssembler.TryAssemble(baseline, new[] { handle }, RealContext(), out var assembled, out var assemblyErrors),
                string.Join("; ", assemblyErrors));
            Assert.True(assembled!.Dialect.TryGetInstruction("SETANIMETIMER", out var addedInstruction));
            Assert.Equal("community.contract-fixture", addedInstruction.ModuleId);
            Assert.NotEqual(baseline.CanonicalHash, assembled.CanonicalHash);
            Assert.Equal(baseline.ProfileId, assembled.ProfileId);
        }
        finally
        {
            handle!.Unload();
        }
    }
}
