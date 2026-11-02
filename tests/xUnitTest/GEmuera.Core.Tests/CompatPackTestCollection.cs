using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// CompatPack 加载相关测试的共享串行集合（DisableParallelization = true）。
/// WHY：<see cref="CompatPackLoader"/> 的测试 seam（BytesReaderOverrideForTest）是
/// 进程级静态字段而非实例状态；xUnit v2 默认按测试类并行执行，若并发测试类同时调用
/// CompatPackLoader.TryLoad，可能读到 CompatPackLoaderTests 刚设置的 override 字节
/// （真实契约夹具 DLL），加载到错误清单/哈希而偶发假红。因此凡调用 TryLoad /
/// TryLoadSet 或触碰 CompatPackLoader 静态成员的测试类必须挂入本集合串行执行：
/// CompatPackLoaderTests、CompatPackAssemblyTests、V18PackManifestTests、
/// CommunityPackTemplateTests、CompatPackAlcIsolationTests、CompatPackSessionTests（T8）、
/// CompatPackLegacyFacadeTests（T8）、CompatPackAlcReclamationTests（T8）。
/// </summary>
[CollectionDefinition("CompatPack", DisableParallelization = true)]
public sealed class CompatPackTestCollection
{
}
