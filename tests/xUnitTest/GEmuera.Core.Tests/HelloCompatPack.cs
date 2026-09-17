using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Tests;

/// <summary>
/// 测试用兼容包入口：本测试程序集内嵌 compatpack.manifest.json（见 csproj），由加载器
/// 经独立 ALC 加载本程序集文件充当"真实包样本"。贡献集合只保留 v1 宿主真实消费的两类
/// （表面 + 能力声明），且表面动作同时含"基线外注册"（SETANIMETIMER/SQL_CONNECT）与
/// "基线内隐藏"（CALLSHARP/EXISTVAR）两向，供校验矩阵用真实 v24 基线做正反向断言。
/// IInstructionVariantContribution/IPolicyContribution 为 v1 死契约（宿主零消费、加载即拒载，
/// v2 预留），正向夹具不得携带——本文件保留 HelloVariantFactory/HelloPolicyContribution
/// 仅供拒载向测试直接构造使用。
/// </summary>
public sealed class HelloCompatPack : ICompatPack
{
    public HelloCompatPack()
    {
        if (!CompatPackManifest.TryLoadFromAssembly(typeof(HelloCompatPack).Assembly, out var manifest, out _))
            throw new InvalidOperationException("测试包内嵌清单缺失。");
        Manifest = manifest!;
    }

    public CompatPackManifest Manifest { get; }

    public IReadOnlyList<ICompatPackContribution> Contributions { get; } = new ICompatPackContribution[]
    {
        new HelloSurfaceContribution(),
        new HelloCapabilityContribution(),
    };
}

public sealed class HelloSurfaceContribution : ISurfaceContribution
{
    public string ContributionId => "test.hello-pack.surface";

    public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
    {
        instructions.RegisterInstruction("SETANIMETIMER");
        functions.RegisterFunction("SQL_CONNECT", "Int64");
        instructions.HideInstruction("CALLSHARP");
        functions.HideFunction("EXISTVAR");
    }
}

public sealed class HelloVariantFactory : ICompatInstructionFactory
{
    public object CreateInstruction() => new object();
}

public sealed class HelloCapabilityContribution : ICapabilityContribution
{
    public string ContributionId => "test.hello-pack.capabilities";

    public IReadOnlyList<string> CapabilityIds { get; } = new[] { "markup.div-v2.v1" };
}

// v1 死契约样本：仅供拒载向测试（CompatPackRulesTests）直接构造，不得进正向夹具贡献集。
public sealed class HelloPolicyContribution : IPolicyContribution
{
    public string ContributionId => "test.hello-pack.policy";

    public IReadOnlyList<EnginePolicyBinding> Policies { get; } = new[]
    {
        new EnginePolicyBinding("parse.diagnostics.v1"),
    };
}
