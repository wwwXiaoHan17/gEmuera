using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 语义级基线表面哈希的完整输入（T7）：profile、engineApi、指令/函数描述符、
/// 内置变体映射与 capability 目录。指令/函数描述符串由调用方按
/// <c>name|signature|completion|module</c> / <c>name|signature|returnType|module</c>
/// 构造（字典键 = name）；哈希文本规范见 <see cref="LegacySurfaceHash.BuildCanonicalText"/>。
/// </summary>
public sealed record SurfaceHashInput(
    string BaseProfileId,
    CompatVersion EngineApi,
    IReadOnlyDictionary<string, string> InstructionDescriptors,
    IReadOnlyDictionary<string, string> FunctionDescriptors,
    IReadOnlyDictionary<string, IReadOnlySet<string>> BuiltinVariants,
    IReadOnlyDictionary<string, CapabilityDescriptor> Capabilities);

/// <summary>
/// v24 基线表面快照哈希（v1.1 语义级，T7）：把基线 profile 的指令/函数描述符、
/// engineApi、内置变体映射与 capability 目录折叠成一个确定性 SHA256 指纹，供兼容包
/// manifest 的 <c>baseSurfaceHash</c> 精确对账（声明非空且不等于当前基线即拒载）。
/// 与旧版"只哈希名字/返回类型"不同，签名、completion mode、module、变体组合与
/// capability 接线状态/版本都进入哈希——任何一项漂移都会改变哈希值。
/// 宿主的 <see cref="SurfaceHashInput"/> 构造与官方包生成器共用
/// <see cref="BuildV24SurfaceHashInput(string, CompatVersion, IReadOnlyDictionary{string, IReadOnlySet{string}}?)"/>，
/// 禁止两处手写 hash（R3）；引擎侧变体注册表与本类
/// <see cref="CanonicalBuiltinVariants"/> 的一致性由 LegacyDialectSurfaceSmoke 断言。
/// </summary>
public static class LegacySurfaceHash
{
    // Core 侧 canonical 变体组合表（与引擎 BuiltinCompatPackVariants.SupportedSelections()
    // 逐项相等；见该类注释与 smoke 断言）。生成器只依赖 Core，故必须在此固化一份
    // canonical 镜像；宿主显式传入引擎导出的同一张表，两者由 smoke 钉住一致。
    static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> CanonicalVariants = BuildCanonicalVariants();

    /// <summary>Core 侧 canonical 内置变体映射（变体名 → 可作用指令集，Ordinal）。</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> CanonicalBuiltinVariants => CanonicalVariants;

    /// <summary>
    /// 按基线 profile 构造语义哈希输入，engineApi 取
    /// <see cref="EngineCapabilityCatalog.CurrentEngineApi"/>、变体表取 Core canonical 映射。
    /// </summary>
    public static SurfaceHashInput BuildV24SurfaceHashInput(string baseProfileId = "v24pure")
        => BuildV24SurfaceHashInput(baseProfileId, EngineCapabilityCatalog.CurrentEngineApi);

    /// <summary>
    /// 按基线 profile + 显式 engineApi 构造语义哈希输入。指令/函数描述符来自
    /// <see cref="BuiltInDialectCatalog.CreateLegacySessionPlan(string)"/> 的 dialect 投影
    ///（唯一事实源：生成清单 → 模块声明 → 描述符）；变体表缺省用 Core canonical 映射。
    /// 宿主传入引擎导出的 <c>BuiltinCompatPackVariants.SupportedSelections()</c>，
    /// 生成器使用本方法的 canonical 缺省值。
    /// </summary>
    public static SurfaceHashInput BuildV24SurfaceHashInput(
        string baseProfileId,
        CompatVersion engineApi,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? builtinVariants = null)
    {
        if (string.IsNullOrWhiteSpace(baseProfileId))
            throw new ArgumentException("基线 profile id 不能为空。", nameof(baseProfileId));

        CompatibilityPlan plan = BuiltInDialectCatalog.CreateLegacySessionPlan(baseProfileId);

        var instructionDescriptors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (InstructionDescriptor descriptor in plan.Dialect.Instructions.Values)
        {
            instructionDescriptors[descriptor.Name] = descriptor.Name
                + "|" + descriptor.Signature
                + "|" + descriptor.CompletionMode
                + "|" + descriptor.ModuleId;
        }

        var functionDescriptors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (FunctionDescriptor descriptor in plan.Dialect.Functions.Values)
        {
            functionDescriptors[descriptor.Name] = descriptor.Name
                + "|" + descriptor.Signature
                + "|" + descriptor.ReturnType
                + "|" + descriptor.ModuleId;
        }

        return new SurfaceHashInput(
            plan.ProfileId,
            engineApi,
            instructionDescriptors,
            functionDescriptors,
            builtinVariants ?? CanonicalVariants,
            EngineCapabilityCatalog.All);
    }

    /// <summary>
    /// 计算语义级 v24 基线表面哈希：对 <see cref="BuildCanonicalText"/> 的 UTF-8（无 BOM）
    /// 文本取 SHA256，输出小写十六进制 64 字符。纯函数：不读取文件/环境/时钟。
    /// </summary>
    public static string ComputeV24SurfaceHash(SurfaceHashInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Convert.ToHexString(
            SHA256.HashData(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(BuildCanonicalText(input))))
            .ToLowerInvariant();
    }

    /// <summary>
    /// 旧无参入口（保留给既有测试/调用点迁移）：等价于显式 v24pure + 当前 engineApi 的
    /// 语义输入。新代码应显式构造 <see cref="SurfaceHashInput"/>，该重载不再接受新语义。
    /// </summary>
    public static string ComputeV24SurfaceHash() => ComputeV24SurfaceHash(BuildV24SurfaceHashInput("v24pure"));

    /// <summary>
    /// 按规范生成哈希文本（UTF-8、LF、无 BOM；每行以单个 <c>\n</c> 结尾）：
    /// <code>
    /// profile=&lt;baseProfileId&gt;
    /// engineApi=&lt;major.minor&gt;
    /// instruction=&lt;name&gt;|&lt;signature&gt;|&lt;completion&gt;|&lt;module&gt;
    /// function=&lt;name&gt;|&lt;signature&gt;|&lt;returnType&gt;|&lt;module&gt;
    /// variant=&lt;instruction&gt;|&lt;variant&gt;
    /// capability=&lt;id&gt;|&lt;status&gt;|&lt;consumerContractId&gt;|&lt;introducedInEngineApi&gt;
    /// </code>
    /// 节顺序固定；指令/函数按名字键、capability 按 id、variant 先按指令名再按变体名
    /// 做 Ordinal 排序（不是整行排序）。字段值不得含 CR/LF。
    /// </summary>
    public static string BuildCanonicalText(SurfaceHashInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.BaseProfileId))
            throw new ArgumentException("SurfaceHashInput.BaseProfileId 不能为空。", nameof(input));
        ArgumentNullException.ThrowIfNull(input.InstructionDescriptors);
        ArgumentNullException.ThrowIfNull(input.FunctionDescriptors);
        ArgumentNullException.ThrowIfNull(input.BuiltinVariants);
        ArgumentNullException.ThrowIfNull(input.Capabilities);

        var builder = new StringBuilder(256 * 1024);
        AppendLine(builder, "profile=" + input.BaseProfileId);
        AppendLine(builder, "engineApi=" + input.EngineApi);

        foreach (KeyValuePair<string, string> entry in input.InstructionDescriptors
            .OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            RequireKey(entry.Key, nameof(input.InstructionDescriptors));
            AppendLine(builder, "instruction=" + entry.Value);
        }
        foreach (KeyValuePair<string, string> entry in input.FunctionDescriptors
            .OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            RequireKey(entry.Key, nameof(input.FunctionDescriptors));
            AppendLine(builder, "function=" + entry.Value);
        }

        var variantLines = new List<(string Instruction, string Variant)>();
        foreach (KeyValuePair<string, IReadOnlySet<string>> variant in input.BuiltinVariants)
        {
            if (string.IsNullOrWhiteSpace(variant.Key))
                throw new ArgumentException("内置变体名不能为空。", nameof(input));
            if (variant.Value is null)
                throw new ArgumentException("内置变体 " + variant.Key + " 的指令集为 null。", nameof(input));
            foreach (string instruction in variant.Value)
            {
                if (string.IsNullOrWhiteSpace(instruction))
                    throw new ArgumentException("内置变体 " + variant.Key + " 含空指令名。", nameof(input));
                variantLines.Add((instruction, variant.Key));
            }
        }
        foreach ((string instruction, string variantName) in variantLines
            .OrderBy(entry => entry.Instruction, StringComparer.Ordinal)
            .ThenBy(entry => entry.Variant, StringComparer.Ordinal))
        {
            AppendLine(builder, "variant=" + instruction + "|" + variantName);
        }

        foreach (KeyValuePair<string, CapabilityDescriptor> entry in input.Capabilities
            .OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            RequireKey(entry.Key, nameof(input.Capabilities));
            CapabilityDescriptor descriptor = entry.Value
                ?? throw new ArgumentException("capability " + entry.Key + " 的描述符为 null。", nameof(input));
            if (!string.Equals(entry.Key, descriptor.CapabilityId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "capability 字典键必须等于 descriptor.CapabilityId：" + entry.Key + " vs " + descriptor.CapabilityId,
                    nameof(input));
            }
            AppendLine(builder,
                "capability=" + entry.Key
                + "|" + descriptor.Status
                + "|" + descriptor.ConsumerContractId
                + "|" + descriptor.IntroducedInEngineApi);
        }

        return builder.ToString();
    }

    static void AppendLine(StringBuilder builder, string line)
    {
        if (line is null)
            throw new ArgumentNullException(nameof(line));
        if (line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0)
            throw new ArgumentException("canonical 哈希文本的字段值不得包含换行符。", nameof(line));
        builder.Append(line).Append('\n');
    }

    static void RequireKey(string key, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("哈希输入的字典键不能为空。", parameterName);
    }

    static IReadOnlyDictionary<string, IReadOnlySet<string>> BuildCanonicalVariants()
    {
        var map = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["builtin:v24"] = new HashSet<string>(new[] { "FOR", "SETBGIMAGE" }, StringComparer.Ordinal),
            ["builtin:snake"] = new HashSet<string>(new[] { "FOR", "SETBGIMAGE" }, StringComparer.Ordinal),
        };
        return new ReadOnlyDictionary<string, IReadOnlySet<string>>(map);
    }
}
