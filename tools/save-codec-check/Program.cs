// SPDX-License-Identifier: MIT
// legacy 存档 Int64 数组段原生存码器等价门禁：
//   dotnet run --project tools/save-codec-check -- <native.dll 路径>
//       默认：C# 参考实现（逐行复刻 EraBinaryDataWriter.writeData(Int64[])/m_WriteInt）
//             随机 sweep + 边界用例 + golden 字节序列 对拍原生
//   dotnet run --project tools/save-codec-check -- --vs-writer <gemuera-c#.dll 路径> [native.dll]
//       反射加载真实游戏程序集的 EraBinaryDataWriter（internal/private）对拍原生——
//       这是"参考实现漂移"的终极锚点
//   dotnet run --project tools/save-codec-check -- --bench <native.dll 路径>
//       旧托管循环 vs 原生编码耗时对比
// 退出码：0 = 全部通过；1 = 有失配。
using System.Reflection;
using System.Runtime.InteropServices;

namespace gEmuera.Tools.SaveCodecCheck;

internal static class Program
{
    private const uint MinAbiVersion = 3;
    private const byte MarkerInt16 = 0xD0;
    private const byte MarkerInt32 = 0xD1;
    private const byte MarkerInt64 = 0xD2;
    private const byte MarkerZero = 0xF0;
    private const byte MarkerEod = 0xFF;

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--vs-writer")
        {
            return VsWriter(args[1], args.Length >= 3 ? args[2] : null);
        }
        if (args.Length >= 2 && args[0] == "--bench")
        {
            return Bench(args[1]);
        }
        string dllPath = args.Length >= 1 ? args[0] : throw new ArgumentException("请传 native.dll 路径");
        return Sweep(dllPath);
    }

    // ===== C# 参考实现：逐行复刻 EraBinaryDataWriter.writeData(Int64[]) / m_WriteInt =====

    private static void ReferenceWriteInt(BinaryWriter w, long v)
    {
        if (v >= 0 && v <= 0xCF)
            w.Write((byte)v);
        else if (v >= short.MinValue && v <= short.MaxValue)
        {
            w.Write(MarkerInt16);
            w.Write((short)v);
        }
        else if (v >= int.MinValue && v <= int.MaxValue)
        {
            w.Write(MarkerInt32);
            w.Write((int)v);
        }
        else
        {
            w.Write(MarkerInt64);
            w.Write(v);
        }
    }

    private static byte[] ReferenceEncodeInt64Array(long[] array)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((int)array.Length);
        int countZero = 0;
        for (int x = 0; x < array.Length; x++)
        {
            if (array[x] == 0)
                countZero++;
            else
            {
                if (countZero > 0)
                {
                    w.Write(MarkerZero);
                    ReferenceWriteInt(w, countZero);
                    countZero = 0;
                }
                ReferenceWriteInt(w, array[x]);
            }
        }
        // 記憶途中で配列の残りが全部0であるなら0の数も記憶せず配列の終わりを記憶
        w.Write(MarkerEod);
        w.Flush();
        return ms.ToArray();
    }

    // ===== native FFI =====

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EncodeInt64ArrayDelegate(long[] array, nuint len, byte[] outBuf, nuint cap);

    private static (nint Lib, EncodeInt64ArrayDelegate Encode, uint Abi) LoadNative(string dllPath)
    {
        nint lib = NativeLibrary.Load(dllPath);
        var abi = Marshal.GetDelegateForFunctionPointer<AbiVersionDelegate>(
            NativeLibrary.GetExport(lib, "gem_native_abi_version"));
        uint v = abi();
        if (v < MinAbiVersion)
        {
            NativeLibrary.Free(lib);
            throw new InvalidOperationException($"ABI 版本过低: {v} < {MinAbiVersion}");
        }
        var encode = Marshal.GetDelegateForFunctionPointer<EncodeInt64ArrayDelegate>(
            NativeLibrary.GetExport(lib, "gem_save_encode_int64_array"));
        return (lib, encode, v);
    }

    private static byte[] NativeEncode(EncodeInt64ArrayDelegate encode, long[] array)
    {
        byte[] buf = new byte[array.Length * 10 + 16];
        int written = encode(array, (nuint)array.Length, buf, (nuint)buf.Length);
        if (written < 0 || written > buf.Length)
            throw new InvalidOperationException($"native encode 返回异常: {written}");
        byte[] result = new byte[written];
        Array.Copy(buf, result, written);
        return result;
    }

    // ===== 用例构造 =====

    private static readonly long[][] GoldenCases =
    {
        Array.Empty<long>(),
        new long[] { 0, 0, 0 },
        new long[] { 0, 0, 7, 0, 0, 0, 0 },
        new long[] { 5 },
        new long[] { -1 },
        new long[] { 207, 208, 209 },
        new long[] { 32767, 32768, -32768, -32769 },
        new long[] { int.MaxValue, int.MinValue, 70000 },
        new long[] { 5_000_000_000L, long.MaxValue, long.MinValue },
        Enumerable.Repeat(0L, 207).ToArray(),
        Enumerable.Repeat(0L, 208).ToArray(),
        Enumerable.Range(0, 209).Select(i => i == 207 ? 9L : 0L).ToArray(),
        Enumerable.Range(0, 210).Select(i => i == 208 ? 9L : 0L).ToArray(),
    };

    private static IEnumerable<long[]> RandomCases()
    {
        var rnd = new Random(1808); // 固定种子可复现
        long[] edge = { 0, 1, 206, 207, 208, 209, short.MinValue, short.MaxValue, -1, 32768,
            int.MaxValue, int.MinValue, 70000, 5_000_000_000L, long.MaxValue, long.MinValue };
        for (int i = 0; i < 300; i++)
        {
            int len = rnd.Next(0, 2000);
            var a = new long[len];
            for (int j = 0; j < len; j++)
            {
                int roll = rnd.Next(100);
                a[j] = roll switch
                {
                    < 60 => 0,
                    < 75 => edge[rnd.Next(edge.Length)],
                    < 90 => rnd.Next(-1000, 1000),
                    _ => ((long)rnd.Next() << 20) ^ rnd.Next(),
                };
            }
            // 长零游程（跨 207/208 边界）
            if (len > 400 && i % 3 == 0)
            {
                int start = rnd.Next(len - 400);
                for (int j = start; j < start + 300; j++)
                    a[j] = 0;
            }
            yield return a;
        }
    }

    private static int Sweep(string dllPath)
    {
        var (lib, encode, abi) = LoadNative(dllPath);
        try
        {
            int failures = 0;
            foreach (var array in GoldenCases.Concat(RandomCases()))
            {
                byte[] expected = ReferenceEncodeInt64Array(array);
                byte[] actual = NativeEncode(encode, array);
                if (!expected.AsSpan().SequenceEqual(actual))
                {
                    if (failures < 5)
                    {
                        Console.WriteLine($"MISMATCH len={array.Length}: managed={expected.Length}B native={actual.Length}B first-diff={FirstDiff(expected, actual)}");
                    }
                    failures++;
                }
            }
            Console.WriteLine(failures == 0
                ? "PASS: golden + 300 组随机/边界 sweep 与 C# 参考实现逐字节一致。"
                : $"FAIL: {failures} 组失配。");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    private static int FirstDiff(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i])
                return i;
        return n;
    }

    /// <summary>
    /// 反射真实游戏程序集的 EraBinaryDataWriter（internal sealed，writeData 私有），
    /// 同一组用例经真实 Writer 写 MemoryStream 取字节 vs 原生编码——参考实现漂移的终极锚点。
    /// </summary>
    private static int VsWriter(string gameDllPath, string? nativeDllPath)
    {
        nativeDllPath ??= FindDefaultNativeDll()
            ?? throw new FileNotFoundException("未找到 gemuera_native.dll，请传第二参数或先运行构建脚本。");
        string gameDir = Path.GetDirectoryName(Path.GetFullPath(gameDllPath))!;
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string name = new AssemblyName(e.Name).Name! + ".dll";
            string candidate = Path.Combine(gameDir, name);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };

        Assembly game = Assembly.LoadFrom(gameDllPath);
        Type writerType = game.GetType("MinorShift.Emuera.Sub.EraBinaryDataWriter")
            ?? throw new InvalidOperationException("游戏程序集中未找到 EraBinaryDataWriter");
        ConstructorInfo ctor = writerType.GetConstructor(new[] { typeof(Stream) })
            ?? throw new InvalidOperationException("未找到 EraBinaryDataWriter(Stream) 构造器");
        MethodInfo writeData = writerType.GetMethod("writeData", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(long[]) }, null)
            ?? throw new InvalidOperationException("未找到私有方法 writeData(Int64[])");

        var (lib, encode, _) = LoadNative(nativeDllPath);
        try
        {
            int failures = 0;
            foreach (var array in GoldenCases.Concat(RandomCases()))
            {
                using var ms = new MemoryStream();
                using (var writer = (IDisposable)ctor.Invoke(new object[] { ms }))
                {
                    writeData.Invoke(writer, new object[] { array });
                }
                byte[] expected = ms.ToArray();
                byte[] actual = NativeEncode(encode, array);
                byte[] reference = ReferenceEncodeInt64Array(array);
                // 三方互证：真实 Writer == 原生 == 参考实现（Writer 可能已接线原生，此时 vs 参考实现对拍兜住快路径漂移）
                if (!expected.AsSpan().SequenceEqual(actual) || !expected.AsSpan().SequenceEqual(reference))
                {
                    if (failures < 5)
                    {
                        Console.WriteLine($"MISMATCH(vs-writer) len={array.Length}: writer={expected.Length}B native={actual.Length}B reference={reference.Length}B first-diff={FirstDiff(expected, actual)}");
                    }
                    failures++;
                }
            }
            Console.WriteLine(failures == 0
                ? "PASS: --vs-writer 真实 EraBinaryDataWriter 对拍逐字节一致。"
                : $"FAIL: {failures} 组失配。");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    private static int Bench(string dllPath)
    {
        var (_, encode, _) = LoadNative(dllPath);
        var rnd = new Random(1808);
        long[] array = new long[1000];
        for (int i = 0; i < array.Length; i++)
            array[i] = rnd.Next(100) < 60 ? 0 : rnd.Next(-1000, 1000);

        const int Iterations = 5000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long checksum = 0;
        for (int i = 0; i < Iterations; i++)
            checksum += ReferenceEncodeInt64Array(array).Length;
        sw.Stop();
        long managedMs = sw.ElapsedMilliseconds;

        byte[] buf = new byte[array.Length * 10 + 16];
        sw.Restart();
        for (int i = 0; i < Iterations; i++)
            checksum += encode(array, (nuint)array.Length, buf, (nuint)buf.Length);
        sw.Stop();
        long nativeMs = sw.ElapsedMilliseconds;

        Console.WriteLine($"EncodeInt64Array（{array.Length} 元素 × {Iterations / 1000}k 次）:");
        Console.WriteLine($"  托管逐元素（旧）: {managedMs,6} ms   checksum={checksum}");
        Console.WriteLine($"  原生批量  （新）: {nativeMs,6} ms   加速 {((double)managedMs / Math.Max(nativeMs, 1)):F1}x");
        return 0;
    }

    private static string? FindDefaultNativeDll()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "gemuera-c#.csproj")))
            dir = dir.Parent;
        if (dir is null)
            return null;
        string candidate = Path.Combine(dir.FullName, "export", "NativeLibs", "windows-x64", "gemuera_native.dll");
        return File.Exists(candidate) ? candidate : null;
    }
}
