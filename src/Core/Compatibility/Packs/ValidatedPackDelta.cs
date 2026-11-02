using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 一次校验通过的冻结包差量（T5），加载/组装之间唯一的数据载体：组装器只读本对象，
/// 不再回放包作者的贡献对象（<see cref="Emuera.Compatibility.Packs.ICompatPack.Contributions"/>）。
/// 全部字段都是规范化/排序后的不可变快照：
///   - PackId/PackVersion：内嵌清单身份；
///   - PackSha256：加载器按依赖闭包计算的包内容哈希（规范格式见
///     <see cref="CompatPackLoader"/>：主程序集 + 内嵌清单原始字节 + 依赖文件名/长度/字节）；
///   - CapabilityIds：清单 + 贡献显式声明经 engine capability closure
///     （Requires/Implies 展开）后的结果，Ordinal 排序去重；
///   - SurfaceStates：T4 双形态闭包后的规范化符号状态，按 (Kind, Name) 规范排序；
///   - VariantSelections：清单变体选择的不可变副本；
///   - DeltaSemanticHash：以上字段按规范文本计算的 SHA256（见 <see cref="DeltaSemanticHash"/>）。
/// </summary>
public sealed class ValidatedPackDelta
{
    /// <summary>DeltaSemanticHash 的规范文本版本标记（格式变更必须升版本）。</summary>
    const string HashDomain = "gemuera.compatpack.delta.v1";

    const char NewLine = (char)10;
    const char Backslash = (char)92;
    const char Pipe = (char)124;
    const char CarriageReturn = (char)13;

    readonly ReadOnlyCollection<string> capabilityIds;
    readonly ReadOnlyCollection<CompatSymbolState> surfaceStates;
    readonly ReadOnlyDictionary<string, string> variantSelections;

    /// <summary>
    /// 冻结一份校验结果。构造器本身不做语义校验（规则层负责）：调用方必须只在
    /// <see cref="CompatPackRules.Validate"/> 返回 true 时构造；能力 id 与表面状态会在此
    /// 按规范顺序排序去重，保证同一语义输入得到同一 <see cref="DeltaSemanticHash"/>。
    /// </summary>
    /// <param name="packId">包身份（来自内嵌清单）。</param>
    /// <param name="packVersion">包版本（来自内嵌清单）。</param>
    /// <param name="packSha256">包内容哈希；规则层直测（无加载器）时允许空串表示"未计算"。</param>
    /// <param name="capabilityIds">capability closure 结果（含 Requires/Implies 展开）。</param>
    /// <param name="surfaceStates">规范化表面状态。</param>
    /// <param name="variantSelections">变体选择（指令 → builtin:* 变体）。</param>
    public ValidatedPackDelta(
        string packId,
        string packVersion,
        string packSha256,
        IEnumerable<string> capabilityIds,
        IEnumerable<CompatSymbolState> surfaceStates,
        IReadOnlyDictionary<string, string> variantSelections)
    {
        ArgumentNullException.ThrowIfNull(packId);
        ArgumentNullException.ThrowIfNull(packVersion);
        ArgumentNullException.ThrowIfNull(capabilityIds);
        ArgumentNullException.ThrowIfNull(surfaceStates);
        ArgumentNullException.ThrowIfNull(variantSelections);
        PackId = packId;
        PackVersion = packVersion;
        PackSha256 = packSha256 ?? string.Empty;

        this.capabilityIds = new ReadOnlyCollection<string>(
            capabilityIds.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
        this.surfaceStates = new ReadOnlyCollection<CompatSymbolState>(
            CanonicalizeStates(surfaceStates).ToArray());
        this.variantSelections = new ReadOnlyDictionary<string, string>(
            new SortedDictionary<string, string>(
                variantSelections.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal));

        DeltaSemanticHash = ComputeSemanticHash();
    }

    public string PackId { get; }
    public string PackVersion { get; }
    public string PackSha256 { get; }
    public IReadOnlyList<string> CapabilityIds => capabilityIds;
    public IReadOnlyList<CompatSymbolState> SurfaceStates => surfaceStates;
    public IReadOnlyDictionary<string, string> VariantSelections => variantSelections;

    /// <summary>
    /// 冻结差量的语义哈希：对 PackId/PackVersion/PackSha256、排序后的 capability id、
    /// 排序后的表面状态（键/种类/归属/返回类型/变体）与排序后的变体选择做规范化文本
    /// SHA256。任何字段变化都必须改变该值；与输入成员的顺序无关。
    /// 规范文本（UTF-8，字段间以 LF 分隔，值内反斜杠/换行/竖线转义）：
    /// <code>
    /// gemuera.compatpack.delta.v1
    /// packId=&lt;escaped&gt;
    /// packVersion=&lt;escaped&gt;
    /// packSha256=&lt;escaped&gt;
    /// capability=&lt;escaped&gt;                              (Ordinal 排序)
    /// symbol=&lt;key&gt;|&lt;state&gt;|&lt;owner&gt;|&lt;returnType&gt;|&lt;variant&gt;  (规范排序)
    /// variant=&lt;instruction&gt;|&lt;variant&gt;                  (Ordinal 排序)
    /// </code>
    /// 可选字段用 "n" 前缀表示 null、"v" 前缀表示非 null 值，避免 null 与字面量歧义。
    /// </summary>
    public string DeltaSemanticHash { get; }

    static IEnumerable<CompatSymbolState> CanonicalizeStates(IEnumerable<CompatSymbolState> states)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CompatSymbolState state in states
            .Where(state => state is not null)
            .OrderBy(state => state.Key.Kind)
            .ThenBy(state => state.Key.Name, StringComparer.Ordinal)
            .ThenBy(state => state.Kind)
            .ThenBy(state => state.OwnerPackId, StringComparer.Ordinal)
            .ThenBy(state => state.ReturnType, StringComparer.Ordinal)
            .ThenBy(state => state.BuiltinVariant, StringComparer.Ordinal))
        {
            string identity = state.Key.ToContractString() + Pipe + state.Kind
                + Pipe + Field(state.OwnerPackId) + Pipe + Field(state.ReturnType)
                + Pipe + Field(state.BuiltinVariant);
            if (seen.Add(identity))
                yield return state;
        }
    }

    string ComputeSemanticHash()
    {
        var canonical = new StringBuilder();
        canonical.Append(HashDomain).Append(NewLine);
        canonical.Append("packId=").Append(Escape(PackId)).Append(NewLine);
        canonical.Append("packVersion=").Append(Escape(PackVersion)).Append(NewLine);
        canonical.Append("packSha256=").Append(Escape(PackSha256)).Append(NewLine);
        foreach (string capability in capabilityIds)
            canonical.Append("capability=").Append(Escape(capability)).Append(NewLine);
        foreach (CompatSymbolState state in surfaceStates)
        {
            canonical.Append("symbol=").Append(Escape(state.Key.ToContractString()))
                .Append(Pipe).Append(state.Kind.ToString())
                .Append(Pipe).Append(Field(state.OwnerPackId))
                .Append(Pipe).Append(Field(state.ReturnType))
                .Append(Pipe).Append(Field(state.BuiltinVariant))
                .Append(NewLine);
        }
        foreach (KeyValuePair<string, string> selection in variantSelections)
        {
            canonical.Append("variant=").Append(Escape(selection.Key))
                .Append(Pipe).Append(Escape(selection.Value)).Append(NewLine);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    static string Field(string? value) => value is null ? "n" : "v" + Escape(value);

    static string Escape(string value)
    {
        if (value.Length == 0)
            return string.Empty;
        var escaped = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case Backslash:
                    escaped.Append(Backslash).Append(Backslash);
                    break;
                case NewLine:
                    escaped.Append(Backslash).Append('n');
                    break;
                case CarriageReturn:
                    escaped.Append(Backslash).Append('r');
                    break;
                case Pipe:
                    escaped.Append(Backslash).Append(Pipe);
                    break;
                default:
                    escaped.Append(c);
                    break;
            }
        }
        return escaped.ToString();
    }
}
