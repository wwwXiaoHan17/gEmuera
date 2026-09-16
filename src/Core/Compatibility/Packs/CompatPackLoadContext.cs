using System.Reflection;
using System.Runtime.Loader;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 每包一个的可回收加载上下文（isCollectible，会话边界 Unload）。绑定规则与宿主
/// PluginLoadContext（Scripts/Emuera/Runtime/Utils/PluginSystem）同源的姊妹实现：
///   1. Emuera/emuera → 契约程序集（src/EmueraFacade，与包共享同一类型标识；.NET 绑定
///      按简单名匹配，2026-09-04 Phase C 复现实验）；
///   2. GEmuera.Core → 本程序集（包不应引用，但引用了也回落到宿主已加载实例）；
///   3. netstandard/System.*/Microsoft.* → 宿主已加载的同名框架程序集（不在回调里发起绑定）；
///   4. 其余 → 包目录探测 &lt;name&gt;.dll；否则返回 null 落回运行时默认解析。
/// </summary>
internal sealed class CompatPackLoadContext : AssemblyLoadContext
{
    static readonly Assembly contractAssembly = typeof(Emuera.Compatibility.Packs.ICompatPack).Assembly;
    static readonly Assembly coreAssembly = typeof(CompatPackLoadContext).Assembly;

    readonly string packDirectory;

    public CompatPackLoadContext(string packAssemblyPath)
        : base(name: "CompatPack:" + Path.GetFileNameWithoutExtension(packAssemblyPath), isCollectible: true)
    {
        packDirectory = Path.GetDirectoryName(Path.GetFullPath(packAssemblyPath)) ?? string.Empty;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string name = assemblyName.Name ?? "";
        if (name == "Emuera" || name == "emuera")
            return contractAssembly;
        if (name == "GEmuera.Core")
            return coreAssembly;
        if (name == "netstandard" || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal))
        {
            foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, name, StringComparison.Ordinal))
                    return loaded;
            }
            return null;
        }
        string candidate = Path.Combine(packDirectory, name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }
}
