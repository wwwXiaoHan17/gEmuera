namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 兼容包表面符号的形态维度：同一标识符可能在引擎里以指令、表达式函数（经 METHOD
/// 投影共享语句形态）或 handler 变体三种身份存在。双形态闭包要求按 (Kind, Name)
/// 而不是裸名字记账——裸名字会把"指令:ABS 隐藏"与"函数:ABS 仍然可见"折叠成同一条。
/// </summary>
public enum CompatSymbolKind
{
    Instruction,
    Function,
    Variant,
}

/// <summary>
/// 表面符号的规范化键。契约字符串格式为 <c>&lt;kind&gt;:&lt;name&gt;</c>
/// （小写 kind 前缀 + Trim 后的名字）：<c>instruction:ABS</c> / <c>function:ABS</c> /
/// <c>variant:SETBGIMAGE</c>。指令/变体名按引擎 IsInstructionVisible 同语义
/// Trim+UpperInvariant；函数名按引擎 IsFunctionVisible 同语义只做 Trim（保留 CJK 原样）。
/// </summary>
public readonly record struct CompatSymbolKey
{
    public CompatSymbolKey(CompatSymbolKind kind, string name)
    {
        if (kind is not (CompatSymbolKind.Instruction or CompatSymbolKind.Function or CompatSymbolKind.Variant))
            throw new ArgumentOutOfRangeException(nameof(kind));
        string normalized = (name ?? "").Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("表面符号名不能为空。", nameof(name));
        Kind = kind;
        Name = kind == CompatSymbolKind.Function ? normalized : normalized.ToUpperInvariant();
    }

    public CompatSymbolKind Kind { get; }

    public string Name { get; }

    /// <summary>解析契约字符串；未知前缀、缺失名字、空输入一律返回 false（不做静默猜测）。</summary>
    public static bool TryParse(string? text, out CompatSymbolKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        int separator = text.IndexOf(':');
        if (separator <= 0 || separator == text.Length - 1)
            return false;

        string prefix = text.Substring(0, separator).Trim();
        string name = text.Substring(separator + 1).Trim();
        // 契约字符串只有 kind:name 两段：名字里的 ':' 会让 ToContractString 产生歧义
        //（"instruction:A:B" 无法区分名字 A:B 与三段契约）；内部空白/全角空白同理不可解析，
        // 一律拒绝而不是静默猜测。
        if (name.Length == 0 || name.IndexOf(':') >= 0)
            return false;
        foreach (char c in name)
        {
            if (char.IsWhiteSpace(c))
                return false;
        }

        CompatSymbolKind kind;
        if (string.Equals(prefix, "instruction", StringComparison.OrdinalIgnoreCase))
            kind = CompatSymbolKind.Instruction;
        else if (string.Equals(prefix, "function", StringComparison.OrdinalIgnoreCase))
            kind = CompatSymbolKind.Function;
        else if (string.Equals(prefix, "variant", StringComparison.OrdinalIgnoreCase))
            kind = CompatSymbolKind.Variant;
        else
            return false;

        key = new CompatSymbolKey(kind, name);
        return true;
    }

    /// <summary>规范契约字符串（确定性诊断/哈希输入）。</summary>
    public string ToContractString()
    {
        string prefix = Kind switch
        {
            CompatSymbolKind.Instruction => "instruction",
            CompatSymbolKind.Function => "function",
            CompatSymbolKind.Variant => "variant",
            _ => throw new InvalidOperationException("Unsupported compat symbol kind: " + Kind + "."),
        };
        return prefix + ":" + Name;
    }

    public override string ToString() => ToContractString();
}

/// <summary>规范化后的表面状态种类：存在（注册/暴露）、隐藏、变体选择。</summary>
public enum CompatSymbolStateKind
{
    Present,
    Hidden,
    VariantSelection,
}

/// <summary>
/// 单个表面符号的规范化状态（冻结差量的最小单元）。<see cref="OwnerPackId"/> 记录
/// 声明该状态的包；<see cref="ReturnType"/> 仅函数 Present 使用；<see cref="BuiltinVariant"/>
/// 仅变体选择使用。T5 的 ValidatedPackDelta 会以本类型为表面状态的唯一载体。
/// </summary>
public sealed record CompatSymbolState(
    CompatSymbolKey Key,
    CompatSymbolStateKind Kind,
    string? OwnerPackId,
    string? ReturnType,
    string? BuiltinVariant);
