using CompatPackTypeLoadDependency;
using Emuera.Compatibility.Packs;

namespace CompatPackTypeLoadFixture;

/// <summary>
/// 类型加载失败夹具（T5 fail-closed 负向样本）：入口类继承 <see cref="TypeLoadDependencyBase"/>，
/// 只要部署目录内的依赖 DLL 缺失或字节损坏，独立 ALC 就无法解析基类，
/// <c>Assembly.GetTypes()</c> 抛 ReflectionTypeLoadException。加载器必须拒载整个包，
/// 不允许只挑出"仍然可读的入口类型"继续（旧实现的部分枚举容错会让半坏包变成 manifest-only）。
/// </summary>
public sealed class TypeLoadFailingPack : TypeLoadDependencyBase, ICompatPack
{
    public TypeLoadFailingPack()
    {
        if (!CompatPackManifest.TryLoadFromAssembly(typeof(TypeLoadFailingPack).Assembly, out var manifest, out _))
            throw new InvalidOperationException("类型加载失败夹具内嵌清单缺失。");
        Manifest = manifest!;
    }

    public CompatPackManifest Manifest { get; }

    public IReadOnlyList<ICompatPackContribution> Contributions { get; } = Array.Empty<ICompatPackContribution>();
}
