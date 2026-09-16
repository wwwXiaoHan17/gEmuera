using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 一次成功加载的不可变结果：清单、入口实例、分类贡献与哈希。AssemblySha256 是包程序集
/// 文件字节哈希；PackSha256 在其上叠加内嵌清单原始字节（file‖resource），供 DOD-3 组装期
/// 并入 CompatibilityPlan 哈希链——同包内容必同哈希（诊断可复现）。
/// </summary>
public sealed class CompatPackHandle
{
    internal CompatPackHandle(
        CompatPackManifest manifest,
        ICompatPack pack,
        string packAssemblyPath,
        string assemblySha256,
        string packSha256,
        IReadOnlyList<ISurfaceContribution> surface,
        IReadOnlyList<ICapabilityContribution> capabilities,
        IReadOnlyList<IInstructionVariantContribution> variants,
        IReadOnlyList<IPolicyContribution> policies,
        CompatPackLoadContext loadContext)
    {
        Manifest = manifest;
        Pack = pack;
        PackAssemblyPath = packAssemblyPath;
        AssemblySha256 = assemblySha256;
        PackSha256 = packSha256;
        Surface = surface;
        Capabilities = capabilities;
        Variants = variants;
        Policies = policies;
        LoadContext = loadContext;
    }

    public CompatPackManifest Manifest { get; }
    public ICompatPack Pack { get; }
    public string PackAssemblyPath { get; }
    public string AssemblySha256 { get; }
    public string PackSha256 { get; }
    public IReadOnlyList<ISurfaceContribution> Surface { get; }
    public IReadOnlyList<ICapabilityContribution> Capabilities { get; }
    public IReadOnlyList<IInstructionVariantContribution> Variants { get; }
    public IReadOnlyList<IPolicyContribution> Policies { get; }

    internal CompatPackLoadContext LoadContext { get; }

    /// <summary>会话边界回收包程序集。未启用任何包的会话不持有句柄，天然回到纯 v24。</summary>
    public void Unload()
    {
        try
        {
            LoadContext.Unload();
        }
        catch (InvalidOperationException)
        {
            // 已回收或仍有未释放实例：收集由 GC 收尾，不在此抛错。
        }
    }
}

/// <summary>一次会话启用包集合的全量结果（全部成功才存在；任一包失败则整体为 null 并回退纯 v24）。</summary>
public sealed class CompatPackSet
{
    internal CompatPackSet(IReadOnlyList<CompatPackHandle> handles)
    {
        Handles = handles;
    }

    public IReadOnlyList<CompatPackHandle> Handles { get; }

    public void UnloadAll()
    {
        foreach (CompatPackHandle handle in Handles)
            handle.Unload();
    }
}
