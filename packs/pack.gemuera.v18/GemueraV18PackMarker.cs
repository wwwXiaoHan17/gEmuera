namespace GemueraV18Pack;

/// <summary>
/// v18 第一方数据包的定位标记：仅用于测试/宿主取得包程序集路径
/// （typeof(...).Assembly.Location）。本程序集故意不包含 ICompatPack 实现类——
/// manifest-only 数据包不需要任何 C# 入口，表面声明全部来自内嵌的
/// compatpack.manifest.json（生成物，由 tools/compat-pack/Generate-V18PackManifest 维护）。
/// </summary>
public sealed class GemueraV18PackMarker
{
}
