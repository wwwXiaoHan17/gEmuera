using System.Reflection;
using System.Runtime.Loader;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 每包一个的可回收加载上下文（isCollectible，会话边界 Unload）。契约-only 允许清单
/// （fail-closed，docs/designs/compat-pack-interface.md §5.2；与宿主 PluginLoadContext
/// （scripts/Emuera/Runtime/Utils/PluginSystem）同源的姊妹实现，规则更严）：
///   1a. Emuera.CompatPack（2026-10-03 拆分后的契约程序集，src/EmueraCompatPack）→ 契约
///       程序集（宿主已加载实例，与包共享同一类型标识）；
///   1b. Emuera/emuera（拆分前的历史契约名）→ 宿主 Default 上下文里已加载的 facade 实例，
///       其 TypeForwardedTo 把契约类型链到 1a 的同一实例（旧包兼容，拆分 ADR §2.3/§2.4）；
///       facade 未加载（如纯 Core 测试宿主）→ FileLoadException，不做 null 回落让默认解析
///       去磁盘探测 Emuera.dll（那会重开信任边界缺口）；
///   2. netstandard/System.*/Microsoft.* → 宿主已加载的同名框架程序集（不在回调里发起
///      绑定；未命中返回 null 由运行时默认解析接手，BCL 名字无宿主泄露面）；
///   3. 其余名字 → 只从加载器传入的依赖字节图（file name → bytes）取；
///      命中即 <see cref="AssemblyLoadContext.LoadFromStream"/> 仅在本 ALC 内加载；
///   4. 仍未命中 → 抛出 FileLoadException，不再返回 null 落回运行时默认解析（默认解析
///      可见宿主全部已加载程序集，等于信任边界 §6 的"可见面"约束失效）。
///   宿主内部程序集（如 GEmuera.Core）对包不可见：包需要 Core 数据类型时必须经契约
///   程序集暴露或签新契约，不允许 ALC 回落（2026-10-03 硬化，探针测试钉住）。
/// T5 起依赖只从字节图装载（禁止在 Load() 中按路径二次读盘）：加载器在进入 ALC 之前
/// 一次性读入包目录顶层全部 *.dll，哈希与装载因此共享同一份字节（TOCTOU 不变量）。
/// </summary>
internal sealed class CompatPackLoadContext : AssemblyLoadContext
{
    static readonly Assembly contractAssembly = typeof(Emuera.Compatibility.Packs.ICompatPack).Assembly;

    static readonly string contractAssemblyName = contractAssembly.GetName().Name ?? "Emuera.CompatPack";

    readonly IReadOnlyDictionary<string, byte[]> dependencyBytes;

    public CompatPackLoadContext(
        string packAssemblyPath,
        IReadOnlyDictionary<string, byte[]> dependencyBytes)
        : base(name: "CompatPack:" + Path.GetFileNameWithoutExtension(packAssemblyPath), isCollectible: true)
    {
        this.dependencyBytes = dependencyBytes ?? throw new ArgumentNullException(nameof(dependencyBytes));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string name = assemblyName.Name ?? "";
        if (string.Equals(name, contractAssemblyName, StringComparison.Ordinal))
            return contractAssembly;
        if (name == "Emuera" || name == "emuera")
        {
            // 拆分前编译的包按 'Emuera' 引用契约类型；返回宿主已加载的 facade 实例，
            // 类型经其 TypeForwardedTo 链到 contractAssembly（同一类型标识）。只扫描已加载
            // 程序集，不在回调里发起绑定；未命中即 fail-closed 拒载。
            foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, "Emuera", StringComparison.Ordinal))
                    return loaded;
            }
            throw new FileLoadException(
                "兼容包按历史契约名 'Emuera' 绑定，但宿主未加载 Emuera 契约程序集（1.824 期拆分前形态）。"
                + "契约程序集已拆分为 '" + contractAssemblyName + "'；旧包需宿主提供 Emuera facade 方可加载"
                + "（拆分 ADR：docs/plans/2026-10-03-compatpack-contract-assembly-split-adr.md §2.4）。", name);
        }
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

        string fileName = name + ".dll";
        if (TryGetDependencyBytes(fileName, out byte[] bytes))
        {
            // 每次装载必须使用新的 MemoryStream：同一个流不能被两个 Assembly 共享。
            return LoadFromStream(new MemoryStream(bytes, writable: false));
        }
        throw new FileLoadException(
            "兼容包绑定了允许清单之外的程序集 '" + name + "'：包只允许引用契约程序集"
            + "（" + contractAssemblyName + "；历史名 Emuera 经 facade TypeForwardedTo 兼容）、"
            + "BCL（netstandard/System.*/Microsoft.*）与包目录内的依赖程序集（按加载器读入的"
            + "依赖字节图装载；宿主内部程序集如 GEmuera.Core 对包不可见）。需要 Core 数据类型时"
            + "请经契约程序集暴露或另签契约（docs/designs/compat-pack-interface.md §5.2）。", name);
    }

    bool TryGetDependencyBytes(string fileName, out byte[] bytes)
    {
        if (dependencyBytes.TryGetValue(fileName, out byte[]? found) && found is not null)
        {
            bytes = found;
            return true;
        }
        // M-4：大小写回退只在 Windows 启用——那是文件系统大小写不敏感的平台，依赖程序集名
        // 与文件名的 casing 可能不同。Linux/macOS 允许 Foo.dll 与 foo.dll 并存，IgnoreCase
        // 扫描的结果取决于字典枚举顺序（不确定），因此保持 Ordinal 严格匹配（miss 即按
        // 允许清单 fail-closed 拒载，而不是随机选一个同名不同 casing 的程序集）。
        if (OperatingSystem.IsWindows())
        {
            foreach (KeyValuePair<string, byte[]> entry in dependencyBytes)
            {
                if (string.Equals(entry.Key, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    bytes = entry.Value;
                    return true;
                }
            }
        }
        bytes = null!;
        return false;
    }
}
