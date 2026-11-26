using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>预检结论。</summary>
public enum CompatPackPeekVerdict
{
    /// <summary>头部规则全部通过（不等于一定可加载：能力词汇、表面符号等规则需要完整校验）。</summary>
    Compatible,

    /// <summary>命中至少一条头部规则——真实加载必然拒载。</summary>
    Rejected,

    /// <summary>读不出内嵌清单（不是托管程序集、没有该资源、清单 JSON 非法）——真实加载同样拒载。</summary>
    Unreadable,
}

/// <summary>
/// 一次预检的结果：结论、读到的清单（读不出时为 null）、命中的头部规则与对应的错误文本。
/// </summary>
public sealed record CompatPackPeekResult(
    CompatPackPeekVerdict Verdict,
    CompatPackManifest? Manifest,
    IReadOnlyList<CompatPackHeaderError> HeaderErrors,
    IReadOnlyList<string> Errors)
{
    public bool IsCompatible => Verdict == CompatPackPeekVerdict.Compatible;

    /// <summary>命中的首条头部规则；没有则为 null。</summary>
    public CompatPackHeaderRule? FirstRule => HeaderErrors.Count > 0 ? HeaderErrors[0].Rule : null;

    /// <summary>给启动器状态栏用的一句话原因（兼容时为空串）。</summary>
    public string ShortReason => Verdict switch
    {
        CompatPackPeekVerdict.Compatible => "",
        CompatPackPeekVerdict.Unreadable => "内嵌清单缺失或无法读取",
        _ => FirstRule switch
        {
            CompatPackHeaderRule.SurfaceHashMismatch => "表面哈希过期（引擎表面已变）",
            CompatPackHeaderRule.SurfaceHashMissing => "缺少 baseSurfaceHash",
            CompatPackHeaderRule.ContractAbiMismatch => "契约版本 contractAbi 不匹配",
            CompatPackHeaderRule.EngineApiMismatch => "引擎能力版本 engineApi 不匹配",
            CompatPackHeaderRule.BaseProfileNotV24Pure => "基线档案不是 v24pure",
            CompatPackHeaderRule.BaseProfileMismatch => "基线档案不是 v24pure",
            CompatPackHeaderRule.ReservedModuleId => "packId 与内置保留名冲突",
            CompatPackHeaderRule.SaveProfileDeclared => "声明了尚不支持的 saveProfileId",
            _ => "与当前引擎不匹配",
        },
    };
}

/// <summary>
/// 「只看清单、不执行」的包可用性预检：直接从包程序集字节读出内嵌清单
/// （资源名 <see cref="CompatPackManifest.ManifestResourceName"/>，不经 ALC 装载、不运行包代码），
/// 再按与规则层同源的头部规则（<see cref="CompatPackRules.CollectHeaderErrors"/>）判定。
/// <para>
/// 用途是启动前的自愈与提示：引擎表面变化（如新增 capability 后重钉 baseSurfaceHash）会让
/// 旧包必然被拒，而真实加载要等到游戏启动才在日志里留一条错误、界面无感。预检不通过
/// 等价于「这个包文件对当前引擎一定不可用」，调用方据此改绑内置包或提示用户；
/// 预检通过只说明头部无误，最终仍以真实加载为准。
/// </para>
/// </summary>
public static class CompatPackManifestPeek
{
    /// <summary>预检一个包程序集文件的内容。</summary>
    public static CompatPackPeekResult Assess(byte[] assemblyBytes, CompatPackEngineHeader engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (assemblyBytes is null || assemblyBytes.Length == 0)
            return Unreadable("包文件为空。");
        if (!TryReadEmbeddedManifest(assemblyBytes, out byte[] manifestBytes, out string readError))
            return Unreadable("内嵌清单缺失或无法读取：" + readError);
        if (!CompatPackManifest.TryParse(
                Encoding.UTF8.GetString(manifestBytes), out CompatPackManifest? manifest, out IReadOnlyList<string> parseErrors)
            || manifest is null)
        {
            return Unreadable("内嵌清单缺失或无法读取：清单 JSON 非法——" + string.Join("；", parseErrors));
        }
        return Assess(manifest, engine);
    }

    /// <summary>预检一份已解析的清单。</summary>
    public static CompatPackPeekResult Assess(CompatPackManifest manifest, CompatPackEngineHeader engine)
    {
        List<CompatPackHeaderError> headerErrors = CompatPackRules.CollectHeaderErrors(manifest, engine);
        return new CompatPackPeekResult(
            headerErrors.Count == 0 ? CompatPackPeekVerdict.Compatible : CompatPackPeekVerdict.Rejected,
            manifest,
            headerErrors,
            headerErrors.Select(headerError => headerError.Message).ToArray());
    }

    /// <summary>构造「读不出清单」的预检结果，供文件缺失/读取失败的调用方复用同一套文案。</summary>
    public static CompatPackPeekResult CreateUnreadable(string error) => Unreadable(error);

    static CompatPackPeekResult Unreadable(string error) => new(
        CompatPackPeekVerdict.Unreadable,
        null,
        Array.Empty<CompatPackHeaderError>(),
        new[] { error });

    /// <summary>
    /// 从程序集字节里取出内嵌清单的原始字节。只读 PE 元数据与资源目录，不装载、不执行；
    /// 任何不可解析输入都返回 false 并给出原因（TryX 契约，不抛异常）。
    /// </summary>
    public static bool TryReadEmbeddedManifest(
        byte[] assemblyBytes,
        out byte[] manifestBytes,
        out string error)
    {
        manifestBytes = Array.Empty<byte>();
        error = "";
        if (assemblyBytes is null || assemblyBytes.Length == 0)
        {
            error = "内容为空。";
            return false;
        }
        try
        {
            using var stream = new MemoryStream(assemblyBytes, writable: false);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                error = "不是托管程序集。";
                return false;
            }
            CorHeader? corHeader = peReader.PEHeaders.CorHeader;
            if (corHeader is null || corHeader.ResourcesDirectory.Size == 0)
            {
                error = "程序集没有内嵌资源（缺 " + CompatPackManifest.ManifestResourceName + "）。";
                return false;
            }
            // 资源目录 = 一条条「4 字节小端长度 + 内容」首尾相接；ManifestResource.Offset 是
            // 本条记录在目录内的起始偏移（与 Assembly.GetManifestResourceStream 读到的一致）。
            DirectoryEntry resourcesDirectory = corHeader.ResourcesDirectory;
            PEMemoryBlock resourcesBlock = peReader.GetSectionData(resourcesDirectory.RelativeVirtualAddress);
            MetadataReader metadata = peReader.GetMetadataReader();
            foreach (ManifestResourceHandle handle in metadata.ManifestResources)
            {
                ManifestResource resource = metadata.GetManifestResource(handle);
                if (!string.Equals(
                        metadata.GetString(resource.Name), CompatPackManifest.ManifestResourceName, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!resource.Implementation.IsNil)
                {
                    error = "内嵌清单被声明为外部链接文件（非内嵌资源）。";
                    return false;
                }
                long offset = resource.Offset;
                if (offset < 0 || offset > resourcesDirectory.Size - sizeof(int)
                    || offset > resourcesBlock.Length - sizeof(int))
                {
                    error = "内嵌清单资源的偏移非法。";
                    return false;
                }
                BlobReader reader = resourcesBlock.GetReader(
                    (int)offset, resourcesBlock.Length - (int)offset);
                long length = reader.ReadUInt32();
                if (length > reader.RemainingBytes)
                {
                    error = "内嵌清单资源的长度前缀非法。";
                    return false;
                }
                manifestBytes = reader.ReadBytes(checked((int)length));
                error = "";
                return true;
            }
            error = "程序集没有内嵌资源 " + CompatPackManifest.ManifestResourceName + "。";
            return false;
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException
            or InvalidOperationException or ArgumentOutOfRangeException or OverflowException)
        {
            manifestBytes = Array.Empty<byte>();
            error = exception.Message;
            return false;
        }
    }
}
