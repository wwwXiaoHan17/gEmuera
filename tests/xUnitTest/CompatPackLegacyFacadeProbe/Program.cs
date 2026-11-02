using System.Runtime.Loader;
using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;

namespace CompatPackLegacyFacadeProbe;

/// <summary>
/// 旧 facade 包"未预载"干净进程探针（T8）。只引用 GEmuera.Core，从不加载 facade；
/// 用真实 <see cref="CompatPackLoader.TryLoad"/> 加载命令行给出的包程序集，把结果按
/// 稳定单行格式写到 stdout，由 xUnit 用例断言。退出码语义：
///   0 = 探针正常完成（加载成功/失败都算完成，结果在 stdout）；
///   64 = 参数错误；65 = 进程不干净（Default ALC 已加载 Emuera facade）。
/// 不使用 System.Environment.Exit 之外的流程控制，错误也走 stdout/stderr 明确输出。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: CompatPackLegacyFacadeProbe <legacy-pack.dll>");
            return 64;
        }

        bool facadePreloaded = AssemblyLoadContext.Default.Assemblies.Any(assembly =>
            string.Equals(assembly.GetName().Name, "Emuera", StringComparison.Ordinal));
        Console.WriteLine("facadePreloaded=" + (facadePreloaded ? "True" : "False"));
        if (facadePreloaded)
        {
            Console.Error.WriteLine("probe 进程不干净：Emuera facade 已在 Default ALC 中加载。");
            return 65;
        }

        CompatPackValidationContext context = new(
            currentContractAbi: new CompatVersion(1, 0),
            currentEngineApi: new CompatVersion(1, 0),
            capabilities: EngineCapabilityCatalog.All,
            knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            baselineInstructions: new HashSet<string>(
                LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
            baselineFunctions: new HashSet<string>(
                LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal),
            // 宿主口径：METHOD 投影名并集参与双形态闭包（hide 函数镜像隐藏同名指令）。
            methodProjectedInstructionNames: new HashSet<string>(
                LegacyDialectInventories.MethodProjectedInstructionNames, StringComparer.Ordinal));

        bool loaded;
        CompatPackHandle? handle;
        IReadOnlyList<string> errors;
        try
        {
            loaded = CompatPackLoader.TryLoad(args[0], context, out handle, out errors);
        }
        catch (Exception exception)
        {
            // 加载器契约是"异常绝不逃逸"；探针若观察到异常必须显式上报，不能伪装成拒载。
            Console.WriteLine("probeException=" + exception.GetType().Name + ": " + exception.Message);
            return 0;
        }

        Console.WriteLine("loaded=" + (loaded ? "True" : "False"));
        foreach (string error in errors)
            Console.WriteLine("error=" + error);
        if (handle is not null)
        {
            Console.WriteLine("packId=" + handle.Manifest.PackId);
            handle.Unload();
        }
        return 0;
    }
}
