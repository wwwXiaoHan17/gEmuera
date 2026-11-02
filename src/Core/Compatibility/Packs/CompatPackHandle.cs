using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 一次成功加载的不可变结果：清单、入口实例、冻结差量、依赖闭包与哈希。
/// <see cref="AssemblySha256"/> 是包程序集文件字节哈希；<see cref="PackSha256"/> 是加载器按
/// "主程序集 + 内嵌清单原始字节 + 依赖闭包"计算的包内容哈希（规范格式见
/// <see cref="CompatPackLoader"/>），供组装期并入 CompatibilityPlan 哈希链——同包内容必同哈希
///（诊断可复现）。
/// T5 起句柄不再保存"可再次执行的贡献对象"：组装器只读 <see cref="ValidatedDelta"/>，
/// 校验期回放的贡献实例不会在组装期第二次执行。
/// </summary>
public sealed class CompatPackHandle : ICompatPackLease
{
    internal CompatPackHandle(
        CompatPackManifest manifest,
        ICompatPack pack,
        string packAssemblyPath,
        string assemblySha256,
        ValidatedPackDelta validatedDelta,
        IReadOnlyList<CompatPackDependencyInfo> dependencyClosure,
        CompatPackLoadContext loadContext)
    {
        Manifest = manifest;
        Pack = pack;
        PackAssemblyPath = packAssemblyPath;
        AssemblySha256 = assemblySha256;
        ValidatedDelta = validatedDelta ?? throw new ArgumentNullException(nameof(validatedDelta));
        // M-9：防御性复制并包成只读视图，句柄对外不可变的承诺不依赖调用方自律。
        Dependencies = dependencyClosure is null
            ? Array.Empty<CompatPackDependencyInfo>()
            : Array.AsReadOnly(dependencyClosure.ToArray());
        LoadContext = loadContext;
    }

    public CompatPackManifest Manifest { get; }

    /// <summary>
    /// 包入口实例：仅用于 ABI/诊断（例如宿主查询包身份）。T5 起组装期只读
    /// <see cref="ValidatedDelta"/>；**禁止在组装期读取 <see cref="ICompatPack.Contributions"/>**
    ///——那等于第二次执行包作者代码，破坏"校验一次、组装只读冻结差量"的不变量（M-10）。
    /// </summary>
    public ICompatPack Pack { get; }

    public string PackAssemblyPath { get; }
    public string AssemblySha256 { get; }

    /// <summary>包身份（<see cref="CompatPackManifest.PackId"/> 的转发；组合解析按此建图去重）。</summary>
    public string PackId => Manifest.PackId;

    /// <summary>包版本（<see cref="CompatPackManifest.PackVersion"/> 的转发；依赖版本范围匹配以此为准）。</summary>
    public string PackVersion => Manifest.PackVersion;

    /// <summary>包内容哈希（<see cref="ValidatedPackDelta.PackSha256"/> 的转发，组装期一等输入）。</summary>
    public string PackSha256 => ValidatedDelta.PackSha256;

    /// <summary>
    /// 校验期冻结的包差量：能力闭包、规范化表面状态、变体选择与语义哈希。组装器唯一输入；
    /// 加载器产出的句柄必然非 null。
    /// </summary>
    public ValidatedPackDelta ValidatedDelta { get; }

    /// <summary>
    /// 加载期依赖闭包诊断（不含主程序集；只含文件基名 + SHA256，不含完整路径），
    /// 宿主写 <c>[LOAD]</c> 日志时据此复现包部署内容。
    /// </summary>
    public IReadOnlyList<CompatPackDependencyInfo> Dependencies { get; }

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

/// <summary>
/// 加载期依赖闭包的一项诊断记录：文件基名 + 内容 SHA256。只记录 basename（不含完整路径），
/// 供 <c>[LOAD]</c> 日志复现包部署内容；主程序集不进本列表（其哈希是 AssemblySha256）。
/// </summary>
public sealed record CompatPackDependencyInfo(string FileName, string Sha256);

/// <summary>一次会话启用包集合的全量结果（全部成功才存在；任一包失败则整体为 null 并回退纯 v24）。</summary>
public sealed class CompatPackSet : ICompatPackLease
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

    /// <summary>租约语义的集合回收：等价 <see cref="UnloadAll"/>（对每个句柄恰好一次 Unload）。</summary>
    public void Unload() => UnloadAll();
}
