using GEmuera.Core.Runtime;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// 方言模块自有函数清单的一条生成数据：名字 + 引擎真实返回类型。
/// 返回类型用于 FunctionDescriptor.ReturnType，参与 plan 语义哈希。
/// </summary>
public sealed record LegacyFunctionInventoryEntry(string Name, string ReturnType);

/// <summary>
/// 把生成清单（LegacyDialectInventories）注册为指令描述符贡献。
/// 描述符只声明"该名字存在于该方言"（VmCompletionMode.CoreImmediate 表示旧 VM 同步完成）；
/// handler 仍由 legacy 注册表在会话投影时提供，变体选择由方言模块的替换贡献负责。
/// </summary>
internal sealed class LegacyInstructionInventoryContribution : IInstructionContribution
{
    private readonly string _moduleId;
    private readonly IReadOnlyList<string> _names;

    public LegacyInstructionInventoryContribution(string contributionId, string moduleId, IReadOnlyList<string> names)
    {
        ContributionId = ContractText.RequiredIdentifier(contributionId, nameof(contributionId));
        _moduleId = ContractText.RequiredIdentifier(moduleId, nameof(moduleId));
        _names = names ?? throw new ArgumentNullException(nameof(names));
    }

    public string ContributionId { get; }

    public void Apply(InstructionRegistryBuilder builder)
    {
        foreach (string name in _names)
            builder.Register(new InstructionDescriptor(name, "legacy", _moduleId, VmCompletionMode.CoreImmediate));
    }
}

/// <summary>
/// 把生成清单（LegacyDialectInventories）注册为表达式函数描述符贡献。
/// ReturnType 来自引擎真实 FunctionMethod.ReturnType，使 plan 哈希对函数签名漂移敏感。
/// </summary>
internal sealed class LegacyFunctionInventoryContribution : IFunctionContribution
{
    private readonly string _moduleId;
    private readonly IReadOnlyList<LegacyFunctionInventoryEntry> _entries;

    public LegacyFunctionInventoryContribution(string contributionId, string moduleId, IReadOnlyList<LegacyFunctionInventoryEntry> entries)
    {
        ContributionId = ContractText.RequiredIdentifier(contributionId, nameof(contributionId));
        _moduleId = ContractText.RequiredIdentifier(moduleId, nameof(moduleId));
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    public string ContributionId { get; }

    public void Apply(FunctionRegistryBuilder builder)
    {
        foreach (LegacyFunctionInventoryEntry entry in _entries)
            builder.Register(new FunctionDescriptor(entry.Name, "legacy", _moduleId, entry.ReturnType));
    }
}
