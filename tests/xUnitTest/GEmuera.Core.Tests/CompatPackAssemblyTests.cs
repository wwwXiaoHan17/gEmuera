using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// DOD-3 hello-world 端到端（Core 层）：显式启用（加载测试程序集兼任的包）→ 表面变化 →
/// plan 哈希变化 → 禁用（空包集）后与纯 v24 基线逐字节等价（同实例同哈希）。
/// 引擎侧投影接线（handler/变体生效于解析器）不在本层，属宿主接线增量。
/// </summary>
public class CompatPackAssemblyTests
{
    static string PackPath => typeof(CompatPackAssemblyTests).Assembly.Location;

    static CompatPackValidationContext Context() => new(
        1,
        new HashSet<string>(StringComparer.Ordinal)
        {
            "input.pointer-button.v1", "startup.continue-after-fault.v1",
            "markup.div-v2.v1", "parse.diagnostics.v1",
        },
        new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

    static CompatibilityPlan Baseline() => BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");

    [Fact]
    public void EndToEnd_EnabledPack_ChangesSurfacePlanAndHash()
    {
        var baseline = Baseline();
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var handle, out var loadErrors),
            string.Join("; ", loadErrors));
        try
        {
            Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, new[] { handle! }, out var assembled, out var assemblyErrors),
                string.Join("; ", assemblyErrors));

            // 表面变化：包注册 SETANIMETIMER/SQL_CONNECT、隐藏 CALLSHARP/EXISTVAR。
            Assert.True(assembled.Dialect.TryGetInstruction("SETANIMETIMER", out var addedInstruction));
            Assert.Equal("test.hello-pack", addedInstruction.ModuleId);
            Assert.False(assembled.Dialect.TryGetInstruction("CALLSHARP", out _));
            Assert.True(assembled.Dialect.TryGetFunction("SQL_CONNECT", out var addedFunction));
            Assert.Equal("Int64", addedFunction.ReturnType);
            Assert.False(assembled.Dialect.TryGetFunction("EXISTVAR", out _));

            // 计数守恒：v24 基线 561/266，包 +1/-1 恰好平衡。
            Assert.Equal(baseline.Dialect.Instructions.Count, assembled.Dialect.Instructions.Count);
            Assert.Equal(baseline.Dialect.Functions.Count, assembled.Dialect.Functions.Count);

            // capability 账本并入（manifest 2 + capability 贡献 1 + policy 绑定 1）。
            Assert.Contains("input.pointer-button.v1", assembled.CapabilityIds);
            Assert.Contains("markup.div-v2.v1", assembled.CapabilityIds);
            Assert.Contains("parse.diagnostics.v1", assembled.CapabilityIds);

            // 模块闭包 = 基线 + 包合成快照。
            Assert.Equal(baseline.Dialect.Modules.Count + 1, assembled.Dialect.Modules.Count);
            Assert.Contains(assembled.Dialect.Modules, module => module.ModuleId == "test.hello-pack");

            // 哈希变化：启用包的会话计划不同于纯 v24。
            Assert.NotEqual(baseline.CanonicalHash, assembled.CanonicalHash);
            Assert.NotEqual(baseline.Dialect.CanonicalHash, assembled.Dialect.CanonicalHash);
            Assert.Equal(baseline.ProfileId, assembled.ProfileId);
        }
        finally
        {
            handle!.Unload();
        }
    }

    [Fact]
    public void EndToEnd_SamePackContent_SameAssembledHash()
    {
        // 同包内容同哈希（PackSha256 进哈希链；诊断可复现）。
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var first, out _));
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var second, out _));
        var baseline = Baseline();
        try
        {
            Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, new[] { first! }, out var planA, out _));
            Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, new[] { second! }, out var planB, out _));
            Assert.Equal(planA!.CanonicalHash, planB!.CanonicalHash);
        }
        finally
        {
            first!.Unload();
            second!.Unload();
        }
    }

    [Fact]
    public void EndToEnd_DisabledPacks_ReturnsBaselineInstanceByteEqual()
    {
        // 禁用任何包：组装器不被需要，空集调用原样返回基线——同实例、同哈希，
        // 「未启用任何包的会话 = 纯 v24」的不变量入口。
        var baseline = Baseline();
        Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, Array.Empty<CompatPackHandle>(), out var assembled, out var errors));
        Assert.Empty(errors);
        Assert.Same(baseline, assembled);
        Assert.Equal(baseline.CanonicalHash, assembled.CanonicalHash);
    }

    [Fact]
    public void EndToEnd_EnableThenDisable_PlanEqualsPureV24()
    {
        // 完整生命周期：启用 → 哈希变化 → 卸载 → 重新组装空集 → 回到基线哈希。
        var baseline = Baseline();
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var handle, out _));
        Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, new[] { handle! }, out var enabled, out _));
        Assert.NotEqual(baseline.CanonicalHash, enabled!.CanonicalHash);

        handle!.Unload();
        Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, Array.Empty<CompatPackHandle>(), out var disabled, out _));
        Assert.Equal(baseline.CanonicalHash, disabled!.CanonicalHash);
    }

    [Fact]
    public void Assemble_DuplicateRegistrationAcrossPacks_Rejects()
    {
        // 两个同包句柄（模拟两包注册同名指令）：跨包对账拒载。
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var first, out _));
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var second, out _));
        try
        {
            Assert.False(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { first!, second! }, out var assembled, out var errors));
            Assert.Null(assembled);
            Assert.Contains(errors!, e => e.Contains("被多个包注册"));
        }
        finally
        {
            first!.Unload();
            second!.Unload();
        }
    }

    [Fact]
    public void Assemble_NullHandle_Rejects()
    {
        Assert.False(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { (CompatPackHandle)null! }, out var assembled, out var errors));
        Assert.Null(assembled);
        Assert.Contains(errors!, e => e.Contains("null 元素"));
    }
}
