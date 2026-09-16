using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 加载器全链路矩阵（docs/designs/compat-pack-interface.md §5）：以本测试程序集为真实包
/// 样本（内嵌清单 + HelloCompatPack 入口），经独立 ALC 加载自身文件验证
/// 显式发现→隔离→清单解析→入口发现→语义校验→哈希固化。fail-closed 三原则
/// （未知 capability 拒载 / 对账冲突拒载 / targetEngineApi 不匹配拒载）各有正反向用例。
/// </summary>
public class CompatPackLoaderTests
{
    static string PackPath => typeof(CompatPackLoaderTests).Assembly.Location;

    static CompatPackValidationContext RealContext() => new(
        engineModuleApiVersion: 1,
        knownCapabilityIds: new HashSet<string>(StringComparer.Ordinal)
        {
            "input.pointer-button.v1",
            "startup.continue-after-fault.v1",
            "markup.div-v2.v1",
            "parse.diagnostics.v1",
        },
        knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        baselineInstructions: new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        baselineFunctions: new HashSet<string>(
            LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

    public CompatPackLoaderTests()
    {
        // 前置事实自检：夹具贡献与真实 v24 基线的关系（防生成清单漂移后测试假绿/假红）。
        Assert.Contains("CALLSHARP", LegacyDialectInventories.V24InstructionNames);
        Assert.DoesNotContain("SETANIMETIMER", LegacyDialectInventories.V24InstructionNames);
        Assert.Contains("EXISTVAR", LegacyDialectInventories.V24Functions.Select(entry => entry.Name));
        Assert.DoesNotContain("SQL_CONNECT", LegacyDialectInventories.V24Functions.Select(entry => entry.Name));
        // 变体绑定/清单选择的目标指令必须在基线内（交叉对账前置事实）。
        Assert.Contains("PRINT", LegacyDialectInventories.V24InstructionNames);
        Assert.Contains("SETBGIMAGE", LegacyDialectInventories.V24InstructionNames);
    }

    [Fact]
    public void TryLoad_HelloPack_WithRealV24Baseline_Succeeds()
    {
        Assert.True(CompatPackLoader.TryLoad(PackPath, RealContext(), out var handle, out var errors),
            string.Join("; ", errors));

        Assert.Equal("test.hello-pack", handle!.Manifest.PackId);
        Assert.Single(handle.Surface);
        Assert.Single(handle.Capabilities);
        Assert.Single(handle.Variants);
        Assert.Single(handle.Policies);
        Assert.Matches("^[0-9a-f]{64}$", handle.AssemblySha256);
        Assert.Matches("^[0-9a-f]{64}$", handle.PackSha256);
        Assert.NotEqual(handle.AssemblySha256, handle.PackSha256);
        handle.Unload();
    }

    [Fact]
    public void TryLoad_SamePack_Twice_HashesDeterministic()
    {
        Assert.True(CompatPackLoader.TryLoad(PackPath, RealContext(), out var first, out _));
        Assert.True(CompatPackLoader.TryLoad(PackPath, RealContext(), out var second, out _));

        Assert.Equal(first!.PackSha256, second!.PackSha256);
        Assert.Equal(first.AssemblySha256, second.AssemblySha256);
        first.Unload();
        second!.Unload();
    }

    [Fact]
    public void TryLoad_UnknownCapability_Rejects()
    {
        var context = new CompatPackValidationContext(
            1,
            new HashSet<string>(StringComparer.Ordinal) { "startup.continue-after-fault.v1", "markup.div-v2.v1", "parse.diagnostics.v1" },
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
            new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

        Assert.False(CompatPackLoader.TryLoad(PackPath, context, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("input.pointer-button.v1"));
    }

    static string[] RealCaps() => new[]
    {
        "input.pointer-button.v1", "startup.continue-after-fault.v1", "markup.div-v2.v1", "parse.diagnostics.v1",
    };

    [Fact]
    public void TryLoad_EngineApiMismatch_Rejects()
    {
        var context = new CompatPackValidationContext(
            2,
            new HashSet<string>(RealCaps(), StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
            new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

        Assert.False(CompatPackLoader.TryLoad(PackPath, context, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("targetEngineApi"));
    }

    [Fact]
    public void TryLoad_UnknownBuiltinVariant_Rejects()
    {
        var context = new CompatPackValidationContext(
            1,
            new HashSet<string>(RealCaps(), StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
            new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

        Assert.False(CompatPackLoader.TryLoad(PackPath, context, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("builtin:snake"));
    }

    [Fact]
    public void TryLoad_HideOutsideBaseline_Rejects()
    {
        var context = new CompatPackValidationContext(
            1,
            new HashSet<string>(RealCaps(), StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(StringComparer.Ordinal), // 空指令基线 → HideInstruction("CALLSHARP") 必然失配
            new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

        Assert.False(CompatPackLoader.TryLoad(PackPath, context, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("CALLSHARP"));
    }

    [Fact]
    public void TryLoad_RegisterConflictsBaseline_Rejects()
    {
        var context = new CompatPackValidationContext(
            1,
            new HashSet<string>(RealCaps(), StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(LegacyDialectInventories.V24InstructionNames.Append("SETANIMETIMER"), StringComparer.Ordinal),
            new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

        Assert.False(CompatPackLoader.TryLoad(PackPath, context, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("SETANIMETIMER"));
    }

    [Fact]
    public void TryLoad_AssemblyWithoutManifest_Rejects()
    {
        // GEmuera.Core.dll 是真实存在、无内嵌清单的程序集 → 拒载并指名资源。
        string corePath = typeof(CompatPackRules).Assembly.Location;
        Assert.False(CompatPackLoader.TryLoad(corePath, RealContext(), out _, out var errors));
        Assert.Contains(errors, e => e.Contains(CompatPackManifest.ManifestResourceName));
    }

    [Fact]
    public void TryLoad_MissingFileOrNotDll_Rejects()
    {
        Assert.False(CompatPackLoader.TryLoad("Z:/definitely/not/there.dll", RealContext(), out _, out var errors));
        Assert.Contains(errors, e => e.Contains("不存在"));

        string notDll = Path.Combine(Path.GetTempPath(), "compat-pack-not-dll-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(notDll, "x");
        try
        {
            Assert.False(CompatPackLoader.TryLoad(notDll, RealContext(), out _, out errors));
            Assert.Contains(errors, e => e.Contains(".dll"));
        }
        finally
        {
            File.Delete(notDll);
        }
    }

    [Fact]
    public void TryLoadSet_SinglePack_Succeeds()
    {
        Assert.True(CompatPackLoader.TryLoadSet(new[] { PackPath }, RealContext(), out var set, out var errors),
            string.Join("; ", errors));
        Assert.Single(set!.Handles);
        set.UnloadAll();
    }

    [Fact]
    public void TryLoadSet_DuplicatePackId_RejectsWholeSet()
    {
        Assert.False(CompatPackLoader.TryLoadSet(new[] { PackPath, PackPath }, RealContext(), out var set, out var errors));
        Assert.Null(set);
        Assert.Contains(errors, e => e.Contains("packId 重复"));
    }

    [Fact]
    public void ValidatePackEntry_ThrowingManifestGetter_RejectsWithoutException()
    {
        string json = "{\"packId\":\"test.hello-pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));
        // DispatchProxy 运行时生成代理类型（不进本程序集元数据，不干扰"恰好一个入口"扫描），
        // 任意成员访问即抛——验证包作者可控 getter 的异常不逃逸。
        var evil = System.Reflection.DispatchProxy.Create<ICompatPack, ThrowingPackProxy>();

        var exception = Record.Exception(() =>
        {
            Assert.False(CompatPackLoader.ValidatePackEntry(evil, manifest, RealContext(), out _, out var errors));
            Assert.Contains(errors!, e => e.Contains("抛出异常"));
        });
        Assert.Null(exception);
    }

    class ThrowingPackProxy : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
            => throw new InvalidOperationException("boom");
    }
}
