using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 语义级 v24 基线表面哈希（<see cref="LegacySurfaceHash.ComputeV24SurfaceHash(SurfaceHashInput)"/>）
/// 的稳定性与覆盖度钉子：哈希必须覆盖 profile / engineApi / 指令与函数描述符 /
/// 内置变体映射 / capability 目录；仓内固定常量在生成清单或任一输入漂移时先红，
/// 强制重走生成器/版本轴迁移流程。
/// </summary>
public class LegacySurfaceHashTests
{
    // 由当前 LegacyDialectInventories + EngineCapabilityCatalog + canonical 变体映射计算后钉住。
    const string PinnedV24SemanticSurfaceHash = "c0e85a8ce5fc1990e5aa05fa5303663c09f6d8befbaaddd261b0ea955acda7c6";

    static SurfaceHashInput BuildInput(
        CompatVersion? engineApi = null,
        IReadOnlyDictionary<string, string>? instructionDescriptors = null,
        IReadOnlyDictionary<string, string>? functionDescriptors = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? builtinVariants = null,
        IReadOnlyDictionary<string, CapabilityDescriptor>? capabilities = null)
    {
        SurfaceHashInput canonical = LegacySurfaceHash.BuildV24SurfaceHashInput("v24pure");
        return canonical with
        {
            EngineApi = engineApi ?? canonical.EngineApi,
            InstructionDescriptors = instructionDescriptors ?? canonical.InstructionDescriptors,
            FunctionDescriptors = functionDescriptors ?? canonical.FunctionDescriptors,
            BuiltinVariants = builtinVariants ?? canonical.BuiltinVariants,
            Capabilities = capabilities ?? canonical.Capabilities,
        };
    }

    static Dictionary<string, string> CopyDescriptors(IReadOnlyDictionary<string, string> source)
        => new(source, StringComparer.Ordinal);

    static IReadOnlyDictionary<string, IReadOnlySet<string>> CopyVariants(
        IReadOnlyDictionary<string, IReadOnlySet<string>> source)
        => source.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<string>)new HashSet<string>(pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);

    [Fact]
    public void LegacySurfaceHash_V24_IsStable()
    {
        string hash = LegacySurfaceHash.ComputeV24SurfaceHash(BuildInput());

        // 形态约束：小写十六进制、恰好 64 字符（SHA256）。
        Assert.Matches("^[0-9a-f]{64}$", hash);

        // 钉住的常量：输入不变则恒等；清单/capability/变体漂移时此处先红。
        Assert.Equal(PinnedV24SemanticSurfaceHash, hash);

        // 纯函数确定性：重复计算同值；无参入口与显式 v24pure 输入同源。
        Assert.Equal(hash, LegacySurfaceHash.ComputeV24SurfaceHash(BuildInput()));
        Assert.Equal(hash, LegacySurfaceHash.ComputeV24SurfaceHash());
    }

    [Fact]
    public void V24Hash_ChangesWhenEngineApiChanges()
    {
        var baseline = BuildInput(new CompatVersion(1, 0));
        var changed = BuildInput(new CompatVersion(1, 1));

        Assert.NotEqual(
            LegacySurfaceHash.ComputeV24SurfaceHash(baseline),
            LegacySurfaceHash.ComputeV24SurfaceHash(changed));
    }

    [Fact]
    public void V24Hash_ChangesWhenWiredCapabilitySetChanges()
    {
        SurfaceHashInput baseline = BuildInput();
        Assert.True(baseline.Capabilities.ContainsKey("math.times-clamp.v1"));

        var withoutWired = new Dictionary<string, CapabilityDescriptor>(
            baseline.Capabilities, StringComparer.Ordinal);
        Assert.True(withoutWired.Remove("math.times-clamp.v1"));

        Assert.NotEqual(
            LegacySurfaceHash.ComputeV24SurfaceHash(baseline),
            LegacySurfaceHash.ComputeV24SurfaceHash(BuildInput(capabilities: withoutWired)));
    }

    [Fact]
    public void V24Hash_ChangesWhenBuiltinVariantMapChanges()
    {
        SurfaceHashInput baseline = BuildInput();
        IReadOnlyDictionary<string, IReadOnlySet<string>> variants = CopyVariants(baseline.BuiltinVariants);
        ((HashSet<string>)variants["builtin:snake"]).Add("PRINT");

        Assert.NotEqual(
            LegacySurfaceHash.ComputeV24SurfaceHash(baseline),
            LegacySurfaceHash.ComputeV24SurfaceHash(BuildInput(builtinVariants: variants)));
    }

    [Fact]
    public void V24Hash_ChangesWhenInstructionDescriptorChanges()
    {
        SurfaceHashInput baseline = BuildInput();
        Dictionary<string, string> descriptors = CopyDescriptors(baseline.InstructionDescriptors);
        Assert.True(descriptors.ContainsKey("ABS"));
        descriptors["ABS"] = descriptors["ABS"].Replace("legacy", "legacy-v2", StringComparison.Ordinal);

        Assert.NotEqual(
            LegacySurfaceHash.ComputeV24SurfaceHash(baseline),
            LegacySurfaceHash.ComputeV24SurfaceHash(BuildInput(instructionDescriptors: descriptors)));
    }

    [Fact]
    public void V24Hash_ChangesWhenFunctionReturnTypeChanges()
    {
        SurfaceHashInput baseline = BuildInput();
        Dictionary<string, string> descriptors = CopyDescriptors(baseline.FunctionDescriptors);
        Assert.True(descriptors.ContainsKey("ABS"));
        descriptors["ABS"] = descriptors["ABS"].Replace("|Integer|", "|Float|", StringComparison.Ordinal);

        Assert.NotEqual(
            LegacySurfaceHash.ComputeV24SurfaceHash(baseline),
            LegacySurfaceHash.ComputeV24SurfaceHash(BuildInput(functionDescriptors: descriptors)));
    }

    [Fact]
    public void CanonicalBuiltinVariants_PinEngineVariantVocabulary()
    {
        // R3 的 Core 侧钉子：宿主与生成器共用本映射；引擎
        // BuiltinCompatPackVariants.SupportedSelections() 必须与之逐项相等
        //（LegacyDialectSurfaceSmoke 断言）。集合内容变化必须同步本测试。
        IReadOnlyDictionary<string, IReadOnlySet<string>> variants = LegacySurfaceHash.CanonicalBuiltinVariants;

        Assert.Equal(
            new[] { "builtin:snake", "builtin:v24" },
            variants.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "FOR", "SETBGIMAGE" },
            variants["builtin:v24"].Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "FOR", "SETBGIMAGE" },
            variants["builtin:snake"].Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void CanonicalText_UsesDocumentedLineFormat()
    {
        string text = LegacySurfaceHash.BuildCanonicalText(BuildInput());

        // 节顺序固定、每行 \n 结尾、LF 无 CR、UTF-8 文本无 BOM（string 本身无 BOM 概念）。
        Assert.StartsWith("profile=v24pure\nengineApi=1.0\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text);
        Assert.Contains("\ninstruction=ABS|legacy|CoreImmediate|gemuera.v24\n", text);
        Assert.Contains("\nfunction=ABS|legacy|Integer|gemuera.v24\n", text);
        Assert.Contains("\nvariant=SETBGIMAGE|builtin:snake\n", text);
        Assert.Contains(
            "\ncapability=math.times-clamp.v1|Wired|ISnakeCompatibilityPolicy.UsesTimesClamp|1.0\n",
            text);
        // 不包含绝对路径/时间戳/进程相关值（只允许契约字段）。
        Assert.DoesNotContain(":\\", text);
        Assert.DoesNotContain("/", text);
    }
}
