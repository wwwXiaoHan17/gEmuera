using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text;
using CompatPackLegacyFacadeFixture;
using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 旧 facade 包两向验证（T8，docs/plans/2026-10-03-compatpack-contract-assembly-split-adr.md）：
/// 拆分前编译的包按简单名 'Emuera' 引用契约类型，运行期依赖 facade 的 TypeForwardedTo 把
/// 类型链到 Emuera.CompatPack 的同一实例。
///
/// 夹具的二进制形态：工程只引用 src/EmueraFacade/Emuera.csproj，但 Roslyn 解析
/// TypeForwardedTo 时必须能看到转发目标，因此编译产物实际引用 Emuera.CompatPack；
/// 测试把隔离副本 #Strings 堆里的 AssemblyRef 名称改写为 'Emuera'，精确复现拆分前旧包
/// 的元数据绑定（改写前断言恰好一处、改写后断言引用表只剩 Emuera）。这不是模拟：
/// 加载器看到的就是一个只按 'Emuera' 绑定的包程序集。
///
/// 两向：
///   1. facade 已预载（本进程 Default ALC）→ TryLoad 成功，贡献进入冻结差量，
///      入口类型经转发与宿主契约类型同一；若转发链失效，GetTypes 会失败，测试变红；
///   2. facade 未预载（CompatPackLegacyFacadeProbe 干净进程）→ 拒载，错误含
///      "Emuera facade/契约程序集" 的可操作提示；同进程的未改写对照组必须加载成功，
///      证明失败归因于 facade 绑定而不是探针/加载器环境。
/// </summary>
[Collection("CompatPack")]
public class CompatPackLegacyFacadeTests
{
    const string FacadePackId = "community.legacy-facade";
    const string ExpectedSurfaceHash =
        "950dbeb9028a8a2fc73ac3699411a05b8e367458219b40bd862066c8902888c9";

    static string FixturePath => typeof(LegacyFacadePack).Assembly.Location;

    static CompatPackValidationContext RealContext() => new(
        currentContractAbi: new CompatVersion(1, 0),
        currentEngineApi: new CompatVersion(1, 0),
        capabilities: EngineCapabilityCatalog.All,
        knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        baselineInstructions: new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        baselineFunctions: new HashSet<string>(
            LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal),
        methodProjectedInstructionNames: new HashSet<string>(
            LegacyDialectInventories.MethodProjectedInstructionNames, StringComparer.Ordinal));

    [Fact]
    public void LegacyFacadePack_LoadsWhenFacadeIsPreloaded()
    {
        // 预载真实 facade：ALC 规则 1b 只扫描 Default 中已加载的 Emuera，不做落盘探测。
        Assembly facade = typeof(MinorShift.Emuera.Runtime.Utils.PluginSystem.IPluginMethod).Assembly;
        Assert.Equal("Emuera", facade.GetName().Name);
        Assert.Contains(
            AssemblyLoadContext.Default.Assemblies,
            loaded => string.Equals(loaded.GetName().Name, "Emuera", StringComparison.Ordinal));

        using var directory = new IsolatedPackDirectory(FixturePath);
        // 编译期形态是 Emuera.CompatPack（Roslyn 对转发类型绑定定义程序集）；
        // 改写后才等价于拆分前旧包（唯一 AssemblyRef=Emuera）。
        Assert.Contains("Emuera.CompatPack", LegacyPackBinary.ReferencedAssemblyNames(directory.MainAssemblyPath));
        LegacyPackBinary.RewriteBindingToFacade(directory.MainAssemblyPath);
        IReadOnlyList<string> references = LegacyPackBinary.ReferencedAssemblyNames(directory.MainAssemblyPath);
        // 拆分前旧包只按 'Emuera' 绑定：恰好一条，避免未来夹具新增真实 facade 引用后
        // 出现两条同名 AssemblyRef 而 Contains 仍然通过（T8 review Minor #6）。
        Assert.Equal(1, references.Count(name => string.Equals(name, "Emuera", StringComparison.Ordinal)));
        Assert.DoesNotContain("Emuera.CompatPack", references);
        // 拆分前旧包引用的 facade 身份是 1.824.0.0（不是契约程序集的 1.0.0.0）。
        Assert.Contains(
            LegacyPackBinary.ReferencedAssemblyVersions(directory.MainAssemblyPath),
            reference => reference.Name == "Emuera" && reference.Version == new Version(1, 824, 0, 0));

        Assert.True(
            CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var handle, out var errors),
            string.Join("; ", errors));
        try
        {
            Assert.Equal(FacadePackId, handle!.Manifest.PackId);
            Assert.Equal(ExpectedSurfaceHash, handle.Manifest.BaseSurfaceHash);
            Assert.Equal("CompatPackLegacyFacadeFixture.LegacyFacadePack", handle.Pack.GetType().FullName);
            // 隔离装载：夹具在测试进程 Default ALC 里也有一份（typeof 取路径用），
            // 包 ALC 内是另一个 Assembly 实例。
            Assert.NotSame(typeof(LegacyFacadePack).Assembly, handle.Pack.GetType().Assembly);
            // 转发链的同一性证据：入口类型看到的 ICompatPack 就是宿主契约类型实例。
            Assert.Contains(typeof(ICompatPack), handle.Pack.GetType().GetInterfaces());

            Assert.Contains(handle.ValidatedDelta.SurfaceStates, state =>
                state.Key == new CompatSymbolKey(CompatSymbolKind.Instruction, "SETANIMETIMER")
                && state.Kind == CompatSymbolStateKind.Present
                && state.OwnerPackId == FacadePackId);
            Assert.Contains(handle.ValidatedDelta.SurfaceStates, state =>
                state.Key == new CompatSymbolKey(CompatSymbolKind.Instruction, "CALLSHARP")
                && state.Kind == CompatSymbolStateKind.Hidden);
            Assert.Contains(handle.ValidatedDelta.SurfaceStates, state =>
                state.Key == new CompatSymbolKey(CompatSymbolKind.Function, "SQL_CONNECT")
                && state.Kind == CompatSymbolStateKind.Present
                && state.ReturnType == "Int64");
            Assert.Contains(handle.ValidatedDelta.SurfaceStates, state =>
                state.Key == new CompatSymbolKey(CompatSymbolKind.Function, "EXISTVAR")
                && state.Kind == CompatSymbolStateKind.Hidden);
            // 清单 + 代码贡献的 capability 都进入冻结闭包。
            Assert.Contains("input.pointer-button.v1", handle.ValidatedDelta.CapabilityIds);
            Assert.Contains("math.times-clamp.v1", handle.ValidatedDelta.CapabilityIds);
        }
        finally
        {
            handle?.Unload();
        }
    }

    [Fact]
    public void FacadeFixtureSurfaceHash_MatchesEngineAndManifest()
    {
        // 三处重复值（夹具 manifest 字面量、本类 ExpectedSurfaceHash、
        // LegacySurfaceHashTests 的 pinned 常量）必须一致。T7 的算法/投影变更曾让三者
        // 同时漂移（review Concern ④），这里把"三处同值"本身钉成测试，而不是靠人工同步。
        Assert.Equal(ExpectedSurfaceHash, LegacySurfaceHash.ComputeV24SurfaceHash());

        Assert.True(
            CompatPackManifest.TryLoadFromAssembly(
                typeof(LegacyFacadePack).Assembly, out CompatPackManifest? manifest, out IReadOnlyList<string> errors),
            string.Join("; ", errors));
        Assert.Equal(ExpectedSurfaceHash, manifest!.BaseSurfaceHash);
    }

    [Fact]
    public void LegacyFacadePack_WithoutFacadePreload_FailsWithActionableError()
    {
        // 对照组（干净进程）：未改写的夹具直接绑定 Emuera.CompatPack，必须加载成功。
        // 这条同时证明探针进程、runtimeconfig、隔离目录与真实加载链路本身正常。
        using var controlDirectory = new IsolatedPackDirectory(FixturePath);
        ProbeResult control = RunProbe(controlDirectory.MainAssemblyPath);
        Assert.Equal(0, control.ExitCode);
        Assert.Contains("facadePreloaded=False", control.StdOut);
        Assert.Contains("loaded=True", control.StdOut);

        // 实验组：改写成拆分前旧包绑定（AssemblyRef=Emuera）后，同一条加载链路必须
        // fail-closed 拒载，且错误指出需要 facade / 契约程序集已拆分。
        using var legacyDirectory = new IsolatedPackDirectory(FixturePath);
        LegacyPackBinary.RewriteBindingToFacade(legacyDirectory.MainAssemblyPath);
        ProbeResult legacy = RunProbe(legacyDirectory.MainAssemblyPath);
        Assert.Equal(0, legacy.ExitCode);
        Assert.Contains("facadePreloaded=False", legacy.StdOut);
        Assert.Contains("loaded=False", legacy.StdOut);
        Assert.DoesNotContain("probeException=", legacy.StdOut);
        string[] errors = ParseErrors(legacy.StdOut);
        Assert.Contains(errors, error =>
            error.Contains("Emuera", StringComparison.Ordinal)
            && error.Contains("facade", StringComparison.Ordinal)
            && error.Contains("契约程序集", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Emuera.CompatPack", StringComparison.Ordinal));
    }

    static string[] ParseErrors(string stdout) => stdout
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Where(line => line.StartsWith("error=", StringComparison.Ordinal))
        .Select(line => line.Substring("error=".Length))
        .ToArray();

    static ProbeResult RunProbe(string packPath)
    {
        string probePath = Path.Combine(AppContext.BaseDirectory, "CompatPackLegacyFacadeProbe.dll");
        string runtimeConfig = Path.Combine(AppContext.BaseDirectory, "GEmuera.Core.Tests.runtimeconfig.json");
        Assert.True(File.Exists(probePath), "探针程序集未复制到测试输出：" + probePath);
        Assert.True(File.Exists(runtimeConfig), "测试 runtimeconfig 缺失：" + runtimeConfig);

        var startInfo = new ProcessStartInfo
        {
            FileName = DotnetHost(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 两端显式 UTF-8：探针输出含中文错误文案，默认代码页会让断言随平台漂移。
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // 用测试程序集的 runtimeconfig 启动探针：probe 自己的 deps 不参与，
        // 依赖从探针所在目录按默认探测解析（facade 不会进 TPA，除非显式引用）。
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--runtimeconfig");
        startInfo.ArgumentList.Add(runtimeConfig);
        startInfo.ArgumentList.Add(probePath);
        startInfo.ArgumentList.Add(packPath);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动干净进程探针。");
        }
        catch (Win32Exception exception)
        {
            // 缺 dotnet / DOTNET_HOST_PATH 时给出可操作的失败原因，而不是裸 Win32Exception
            //（review Minor #1）。不静默 skip：探针是干净进程唯一证据，缺运行时应显式红。
            throw new InvalidOperationException(
                "无法启动干净进程探针（dotnet host = '" + startInfo.FileName + "'）：" + exception.Message
                + "。请安装 .NET 8+ 运行时并把 dotnet 加入 PATH，或设置 DOTNET_HOST_PATH 指向 dotnet 可执行文件。",
                exception);
        }

        using (process)
        {
            // 先启动异步读取再等待退出：ReadToEnd() 先于 WaitForExit 时，子进程写满
            // 管道会与父进程互等，120s 超时永远走不到（review Minor #1）。
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
                Task.WaitAll(new Task[] { stdout, stderr }, TimeSpan.FromSeconds(5));
                Assert.Fail("干净进程探针超时（120s）：" + packPath);
            }
            // WaitForExit(int) 返回时异步流可能仍在刷新；无参重载等到输出完全排空。
            process.WaitForExit();
            return new ProbeResult(
                process.ExitCode,
                stdout.GetAwaiter().GetResult(),
                stderr.GetAwaiter().GetResult());
        }
    }

    static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    static string DotnetHost()
    {
        string? configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured)
            ? configured
            : "dotnet";
    }

    sealed record ProbeResult(int ExitCode, string StdOut, string StdErr);
}

/// <summary>
/// 旧包二进制改写 helper：只改 #Strings 堆中 NUL 结尾的 AssemblyRef 名称字符串。
/// 'Emuera.CompatPack' 比 'Emuera' 长，就地覆写后剩余字节成为无引用的堆尾字符串，
/// 不移动任何偏移/表行；TypeRef 的 ResolutionScope 仍指向同一个 AssemblyRef 行，
/// 因此整个程序集的契约类型引用一起改绑到 facade。改写前断言恰好一处，避免误伤。
/// </summary>
internal static class LegacyPackBinary
{
    const string ContractAssemblyName = "Emuera.CompatPack";
    const string FacadeAssemblyName = "Emuera";

    public static void RewriteBindingToFacade(string assemblyPath)
    {
        byte[] bytes = File.ReadAllBytes(assemblyPath);
        byte[] needle = Encoding.ASCII.GetBytes(ContractAssemblyName + "\0");
        int index = IndexOf(bytes, needle, 0);
        if (index < 0)
            throw new InvalidOperationException("夹具程序集不含契约程序集引用名，无法复现旧包绑定：" + assemblyPath);
        if (IndexOf(bytes, needle, index + 1) >= 0)
            throw new InvalidOperationException("夹具程序集的契约程序集引用名出现多次，拒绝模糊改写：" + assemblyPath);

        // 拆分前旧包的 AssemblyRef 指向 facade 身份（Emuera, 1.824.0.0）。版本字段是
        // AssemblyRef 行首的 4 个 ushort，与堆索引宽度无关，可安全就地改写。
        RewriteAssemblyReferenceVersion(
            bytes, ContractAssemblyName, new Version(1, 824, 0, 0), assemblyPath);

        byte[] replacement = Encoding.ASCII.GetBytes(FacadeAssemblyName + "\0");
        Array.Copy(replacement, 0, bytes, index, replacement.Length);
        File.WriteAllBytes(assemblyPath, bytes);
    }

    public static IReadOnlyList<(string Name, Version Version)> ReferencedAssemblyVersions(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        return metadata.AssemblyReferences
            .Select(handle =>
            {
                AssemblyReference reference = metadata.GetAssemblyReference(handle);
                return (metadata.GetString(reference.Name), reference.Version);
            })
            .OrderBy(reference => reference.Item1, StringComparer.Ordinal)
            .ToArray();
    }

    static void RewriteAssemblyReferenceVersion(
        byte[] bytes, string assemblyName, Version version, string assemblyPath)
    {
        using var image = new MemoryStream(bytes, writable: false);
        using var peReader = new PEReader(image);
        MetadataReader metadata = peReader.GetMetadataReader();
        AssemblyReferenceHandle target = metadata.AssemblyReferences.Single(handle =>
            string.Equals(metadata.GetString(metadata.GetAssemblyReference(handle).Name),
                assemblyName, StringComparison.Ordinal));
        int rowOffset = metadata.GetTableMetadataOffset(TableIndex.AssemblyRef)
            + (MetadataTokens.GetRowNumber(target) - 1) * metadata.GetTableRowSize(TableIndex.AssemblyRef);
        int fileOffset = peReader.PEHeaders.MetadataStartOffset + rowOffset;
        if (fileOffset < 0 || fileOffset + 8 > bytes.Length)
            throw new InvalidOperationException("AssemblyRef 行偏移越界，拒绝改写：" + assemblyPath);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(fileOffset), (ushort)version.Major);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(fileOffset + 2), (ushort)version.Minor);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(fileOffset + 4), (ushort)version.Build);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(fileOffset + 6), (ushort)version.Revision);
    }

    public static IReadOnlyList<string> ReferencedAssemblyNames(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        return metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (int index = start; index <= haystack.Length - needle.Length; index++)
        {
            bool match = true;
            for (int offset = 0; offset < needle.Length; offset++)
            {
                if (haystack[index + offset] == needle[offset])
                    continue;
                match = false;
                break;
            }
            if (match)
                return index;
        }
        return -1;
    }
}
