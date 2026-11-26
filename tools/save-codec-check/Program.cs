// legacy 存档 Int64 数组段原生存码器等价门禁：
//   dotnet run --project tools/save-codec-check -- <native.dll 路径>
//       默认：C# 参考实现（逐行复刻 EraBinaryDataWriter.writeData(Int64[])/m_WriteInt）
//             随机 sweep + 边界用例 + golden 字节序列 对拍原生**编码**；
//             同批用例对拍原生**读侧解码**（save.rs decode_int64_segment_into，
//             语义 = EraBinaryDataReader.ReadIntArray(Int64[])，2026-11-22 起）。
//   dotnet run --project tools/save-codec-check -- --vs-writer <gemuera-c#.dll 路径> [native.dll]
//       反射加载真实游戏程序集的 EraBinaryDataWriter（internal/private）对拍原生。
//   dotnet run --project tools/save-codec-check -- --vs-reader <gemuera-c#.dll 路径> [native.dll]
//       反射真实 EraBinaryDataReader1808.ReadIntArray 对拍原生——毒化 NativeSaveCodec
//       探针静态字段强制托管回退（否则 DllImport 命中已加载模块 → 原生 vs 原生自证）。
//   dotnet run --project tools/save-codec-check -- --bench <native.dll 路径> [game-dll]
//       托管 ReadIntArray（毒化探针）vs 生产读路径（探针复原）耗时对比。
// 退出码：0 = 全部通过；1 = 有失配。
using System.Reflection;
using System.Runtime.InteropServices;

namespace gEmuera.Tools.SaveCodecCheck;

internal static class Program
{
    private const uint MinAbiVersion = 3;
    private const uint DecodeMinAbiVersion = 5;
    private const byte MarkerInt16 = 0xD0;
    private const byte MarkerInt32 = 0xD1;
    private const byte MarkerInt64 = 0xD2;
    private const byte MarkerZero = 0xF0;
    private const byte MarkerEod = 0xFF;

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--vs-writer")
        {
            return VsWriter(args[1], args.Length >= 3 ? args[2] : null!);
        }
        if (args.Length >= 2 && args[0] == "--vs-reader")
        {
            return VsReader(args[1], args.Length >= 3 ? args[2] : null!);
        }
        if (args.Length >= 2 && args[0] == "--bench")
        {
            return Bench(args[1], args.Length >= 3 ? args[2] : null);
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

    // ===== C# 读侧参考实现：逐行复刻 EraBinaryDataReader.ReadIntArray(Int64[]) 的解码循环 =====
    // seg = Int32 长度前缀之后的负载 + EoD。越界/坏 marker 抛异常（与托管异常路径同义）。

    private static int ReferenceDecodeSegment(byte[] seg, long[] work, int length0, bool writeZeros)
    {
        int pos = 0;
        long x = 0;
        long ReadInt()
        {
            byte b = seg[pos++];
            if (b <= 0xCF) return b;
            if (b == MarkerInt16) { var v = BitConverter.ToInt16(seg, pos); pos += 2; return v; }
            if (b == MarkerInt32) { var v = BitConverter.ToInt32(seg, pos); pos += 4; return v; }
            if (b == MarkerInt64) { var v = BitConverter.ToInt64(seg, pos); pos += 8; return v; }
            throw new InvalidDataException("バイナリデータの異常");
        }
        while (true)
        {
            byte b = seg[pos++];
            if (b == MarkerEod)
                break;
            if (b == MarkerZero)
            {
                int cnt = (int)ReadInt();
                if (writeZeros && cnt > 0)
                {
                    if (x < 0 || x + cnt > work.Length)
                        throw new IndexOutOfRangeException();
                    for (int i = 0; i < cnt; i++)
                        work[x + i] = 0;
                }
                x += cnt;
                continue;
            }
            long v;
            if (b <= 0xCF) v = b;
            else { pos--; v = ReadInt(); }
            if (x < 0 || x >= work.Length)
                throw new IndexOutOfRangeException();
            work[x] = v;
            x++;
        }
        if (writeZeros)
        {
            if (x < 0)
                throw new IndexOutOfRangeException();
            for (long i = x; i < Math.Min((long)length0, work.Length); i++)
                work[i] = 0;
        }
        return pos;
    }

    // ===== native FFI =====

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EncodeInt64ArrayDelegate(long[] array, nuint len, byte[] outBuf, nuint cap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DecodeInt64SegmentDelegate(
        byte[] seg, nuint segLen, long[] outArr, nuint outLen, nuint length0, int writeZeros, out nuint consumed);

    private sealed record NativeLib(
        nint Handle, EncodeInt64ArrayDelegate Encode, DecodeInt64SegmentDelegate Decode, uint Abi);

    private static NativeLib LoadNative(string dllPath, bool needDecode)
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
        DecodeInt64SegmentDelegate? decode = null;
        if (needDecode)
        {
            if (v < DecodeMinAbiVersion)
            {
                NativeLibrary.Free(lib);
                throw new InvalidOperationException($"ABI 版本过低（读侧需 ≥{DecodeMinAbiVersion}）: {v}");
            }
            decode = Marshal.GetDelegateForFunctionPointer<DecodeInt64SegmentDelegate>(
                NativeLibrary.GetExport(lib, "gem_save_decode_int64_segment"))!;
        }
        return new NativeLib(lib, encode, decode!, v);
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

    // ===== 默认 sweep：编码对拍 + 读侧解码对拍 =====

    private static int Sweep(string dllPath)
    {
        var native = LoadNative(dllPath, needDecode: true);
        try
        {
            int failures = 0;
            foreach (var array in GoldenCases.Concat(RandomCases()))
            {
                byte[] full = ReferenceEncodeInt64Array(array);
                byte[] actual = NativeEncode(native.Encode, array);
                if (!full.AsSpan().SequenceEqual(actual))
                {
                    if (failures < 5)
                    {
                        Console.WriteLine($"MISMATCH(encode) len={array.Length}: managed={full.Length}B native={actual.Length}B first-diff={FirstDiff(full, actual)}");
                    }
                    failures++;
                }

                // 读侧解码：段 = 剥掉 Int32 长度前缀
                byte[] seg = full[4..];

                // 变体 1：新数组语义（write_zeros=false，工作数组预置零）
                var work1 = new long[array.Length];
                int consumed = native.Decode(seg, (nuint)seg.Length, work1, (nuint)work1.Length,
                    (nuint)work1.Length, 0, out nuint n1);
                if (consumed != 0 || (int)n1 != seg.Length || !work1.AsSpan().SequenceEqual(array))
                {
                    if (failures < 5)
                        Console.WriteLine($"MISMATCH(decode-new) len={array.Length} rc={consumed} consumed={n1}/{seg.Length} first-diff={FirstDiff(work1, array)}");
                    failures++;
                }

                // 参考解码互证（防参考漂移）
                var workRef = new long[array.Length];
                int refConsumed = ReferenceDecodeSegment(seg, workRef, workRef.Length, writeZeros: false);
                if (refConsumed != seg.Length || !workRef.AsSpan().SequenceEqual(array))
                {
                    if (failures < 5)
                        Console.WriteLine($"MISMATCH(ref-decode) len={array.Length} first-diff={FirstDiff(workRef, array)}");
                    failures++;
                }

                // 变体 2：needInit && !isNewArray（既有数组 + 尾部补零；工作数组比存档长 5）
                if (array.Length > 0)
                {
                    var work2 = Enumerable.Repeat(0x55L, array.Length + 5).ToArray();
                    long[] expect2 = new long[work2.Length];
                    Array.Copy(array, expect2, array.Length); // 零游程/尾部由语义补 0，值不变
                    int rc2 = native.Decode(seg, (nuint)seg.Length, work2, (nuint)work2.Length,
                        (nuint)work2.Length, 1, out _);
                    if (rc2 != 0 || !work2.AsSpan().SequenceEqual(expect2))
                    {
                        if (failures < 5)
                            Console.WriteLine($"MISMATCH(decode-existing) len={array.Length} rc={rc2} first-diff={FirstDiff(work2, expect2)}");
                        failures++;
                    }
                    var workRef2 = Enumerable.Repeat(0x55L, work2.Length).ToArray();
                    ReferenceDecodeSegment(seg, workRef2, workRef2.Length, writeZeros: true);
                    if (!workRef2.AsSpan().SequenceEqual(work2))
                    {
                        if (failures < 5)
                            Console.WriteLine($"MISMATCH(ref-vs-native-existing) len={array.Length} first-diff={FirstDiff(workRef2, work2)}");
                        failures++;
                    }
                }
            }
            Console.WriteLine(failures == 0
                ? "PASS: golden + 300 组随机/边界 sweep 编码与读侧解码均与 C# 参考实现一致（含 needInit 既有数组语义）。"
                : $"FAIL: {failures} 组失配。");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            NativeLibrary.Free(native.Handle);
        }
    }

    // ===== --vs-writer：反射真实 EraBinaryDataWriter 对拍 =====

    private static int VsWriter(string gameDll, string dllPath)
    {
        dllPath ??= DiscoverNative();
        var native = LoadNative(dllPath, needDecode: false);
        var asm = Assembly.LoadFrom(gameDll);
        var writerType = asm.GetType("MinorShift.Emuera.Sub.EraBinaryDataWriter")
            ?? throw new InvalidOperationException("反射未找到 EraBinaryDataWriter");
        var mi = writerType.GetMethod("writeData", BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(long[]) }, null)
            ?? throw new InvalidOperationException("反射未找到 writeData(Int64[])");
        int failures = 0;
        try
        {
            foreach (var array in GoldenCases.Concat(RandomCases()))
            {
                using var ms = new MemoryStream();
                var writer = Activator.CreateInstance(writerType, (Stream)ms);
                mi.Invoke(writer, new object[] { array });
                byte[] real;
                // zip 配置下 writeData 落内部 memoryStream；否则直接写 fs
                var innerMs = (MemoryStream?)writerType
                    .GetField("memoryStream", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(writer);
                if (innerMs != null && innerMs.Length > 0)
                    real = innerMs.ToArray();
                else
                    real = ms.ToArray();
                byte[] expected = ReferenceEncodeInt64Array(array);
                if (!real.AsSpan().SequenceEqual(expected))
                {
                    if (failures < 5)
                        Console.WriteLine($"MISMATCH(vs-writer) len={array.Length} first-diff={FirstDiff(expected, real)}");
                    failures++;
                }
                byte[] nativeOut = NativeEncode(native.Encode, array);
                if (!nativeOut.AsSpan().SequenceEqual(expected))
                {
                    if (failures < 5)
                        Console.WriteLine($"MISMATCH(vs-writer-native) len={array.Length} first-diff={FirstDiff(expected, nativeOut)}");
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
            NativeLibrary.Free(native.Handle);
        }
    }

    // ===== --vs-reader：反射真实 EraBinaryDataReader1808.ReadIntArray 对拍 =====
    // 毒化 NativeSaveCodec 探针静态字段强制托管回退（真参考），复原后走原生快路径，两者互证。

    private static int VsReader(string gameDll, string dllPath)
    {
        dllPath ??= DiscoverNative();
        var native = LoadNative(dllPath, needDecode: true);
        var asm = Assembly.LoadFrom(gameDll);
        var readerType = asm.GetType("MinorShift.Emuera.Sub.EraBinaryDataReader+EraBinaryDataReader1808")
            ?? throw new InvalidOperationException("反射未找到 EraBinaryDataReader+EraBinaryDataReader1808");
        var ctor = readerType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(BinaryReader), typeof(int), typeof(uint[]) }, null)
            ?? throw new InvalidOperationException("反射未找到 EraBinaryDataReader1808 构造器");
        var mi = readerType.GetMethod("ReadIntArray", new[] { typeof(long[]), typeof(bool) })
            ?? throw new InvalidOperationException("反射未找到 ReadIntArray(Int64[], bool)");
        var codecType = asm.GetType("MinorShift._Library.NativeSaveCodec")
            ?? throw new InvalidOperationException("反射未找到 NativeSaveCodec");
        var probeState = codecType.GetField("probeState", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("反射未找到 probeState");
        var decodeProbeState = codecType.GetField("decodeProbeState", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("反射未找到 decodeProbeState");

        int failures = 0;
        int cases = 0;
        // 大数组（段约 850KB）：强制走"64KB 窗口 → -2 → 4 倍扩窗重试"路径的字节级校验
        var bigRetryCase = Enumerable.Range(0, 300_000)
            .Select(i => i % 1000 == 0 ? (long)(i * 7919) : 0L)
            .ToArray();
        try
        {
            foreach (var array in GoldenCases.Concat(RandomCases()).Take(120).Append(bigRetryCase))
            {
                byte[] full = ReferenceEncodeInt64Array(array);
                byte[] seg = full[4..];

                foreach (var (needInit, shorter) in new[] { (true, false), (false, false), (true, true) })
                {
                    // 托管参考：毒化两个探针 → reader 回退托管循环
                    probeState.SetValue(null, 1);
                    decodeProbeState.SetValue(null, 1);
                    long[] workManaged;
                    try
                    {
                        workManaged = RunReaderOnce(ctor, mi, full, array, needInit, shorter);
                    }
                    finally
                    {
                        probeState.SetValue(null, 0);
                        decodeProbeState.SetValue(null, 0);
                    }
                    // 生产路径：探针复原 → reader 走原生
                    long[] workNative = RunReaderOnce(ctor, mi, full, array, needInit, shorter);
                    cases++;
                    if (!workManaged.AsSpan().SequenceEqual(workNative))
                    {
                        if (failures < 5)
                            Console.WriteLine($"MISMATCH(vs-reader) len={array.Length} needInit={needInit} shorter={shorter} first-diff={FirstDiff(workManaged, workNative)}");
                        failures++;
                    }
                }
            }
            Console.WriteLine(failures == 0
                ? $"PASS: --vs-reader 真实 EraBinaryDataReader1808.ReadIntArray 托管/原生互证 {cases} 组一致（毒化探针取真参考）。"
                : $"FAIL: {failures} 组失配。");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            NativeLibrary.Free(native.Handle);
        }
    }

    /// <summary>构造 reader 消费一个段并返回工作数组终态。
    /// shorter=true 模拟"现有数组比存档短"（oriArray 复制回路径）。</summary>
    private static long[] RunReaderOnce(
        ConstructorInfo ctor, MethodInfo mi, byte[] full, long[] array, bool needInit, bool shorter)
    {
        var ms = new MemoryStream(full);
        using var br = new BinaryReader(ms);
        var reader = ctor.Invoke(new object[] { br, 1808, Array.Empty<uint>() });
        long[] work = shorter && array.Length > 8
            ? Enumerable.Repeat(0x55L, array.Length - 8).ToArray()
            : Enumerable.Repeat(0x55L, array.Length).ToArray();
        mi.Invoke(reader, new object[] { work, needInit });
        return work;
    }

    // ===== --bench：托管读路径 vs 生产读路径 =====

    private static int Bench(string dllPath, string? gameDll)
    {
        gameDll ??= DiscoverGameDll();
        var native = LoadNative(dllPath, needDecode: true);
        var asm = Assembly.LoadFrom(gameDll);
        var readerType = asm.GetType("MinorShift.Emuera.Sub.EraBinaryDataReader+EraBinaryDataReader1808")
            ?? throw new InvalidOperationException("反射未找到 EraBinaryDataReader1808");
        var ctor = readerType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(BinaryReader), typeof(int), typeof(uint[]) }, null)
            ?? throw new InvalidOperationException("反射未找到构造器");
        var mi = readerType.GetMethod("ReadIntArray", new[] { typeof(long[]), typeof(bool) })
            ?? throw new InvalidOperationException("反射未找到 ReadIntArray");
        var codecType = asm.GetType("MinorShift._Library.NativeSaveCodec");
        var probeState = codecType?.GetField("probeState", BindingFlags.NonPublic | BindingFlags.Static);
        var decodeProbeState = codecType?.GetField("decodeProbeState", BindingFlags.NonPublic | BindingFlags.Static);

        // 200k 元素、60% 零（含长游程）+ 各宽度值的现实混合
        var rnd = new Random(2026);
        var big = new long[200_000];
        for (int i = 0; i < big.Length; i++)
        {
            int roll = rnd.Next(100);
            big[i] = roll switch
            {
                < 60 => 0,
                < 75 => rnd.Next(-1000, 1000),
                < 95 => rnd.Next(int.MinValue, int.MaxValue),
                _ => ((long)rnd.Next() << 20) ^ rnd.Next(),
            };
        }
        for (int i = 50000; i < 52000; i++)
            big[i] = 0;
        byte[] full = ReferenceEncodeInt64Array(big);
        Console.WriteLine($"bench ReadIntArray 200k 元素（段 {full.Length}B）×50 次：");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        probeState?.SetValue(null, 1);
        decodeProbeState?.SetValue(null, 1);
        try
        {
            for (int i = 0; i < 50; i++)
                RunReaderOnce(ctor, mi, full, big, needInit: true, shorter: false);
        }
        finally
        {
            probeState?.SetValue(null, 0);
            decodeProbeState?.SetValue(null, 0);
        }
        sw.Stop();
        double managedMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        for (int i = 0; i < 50; i++)
            RunReaderOnce(ctor, mi, full, big, needInit: true, shorter: false);
        sw.Stop();
        double nativeMs = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"  托管循环 {managedMs:F1}ms");
        Console.WriteLine($"  生产读路径（原生） {nativeMs:F1}ms");
        Console.WriteLine($"  加速 {managedMs / nativeMs:F2}x  ABI={native.Abi}");
        NativeLibrary.Free(native.Handle);
        return 0;
    }

    private static string DiscoverNative()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "export", "NativeLibs", "windows-x64", "gemuera_native.dll"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "export", "NativeLibs", "windows-x64", "gemuera_native.dll"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        throw new FileNotFoundException("未找到 gemuera_native.dll，请显式传参");
    }

    private static string DiscoverGameDll()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ".godot", "mono", "temp", "bin", "Debug", "gemuera-c#.dll"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".godot", "mono", "temp", "bin", "Debug", "gemuera-c#.dll"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        throw new FileNotFoundException("未找到 gemuera-c#.dll，请显式传参");
    }

    private static int FirstDiff(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i])
                return i;
        return n;
    }

    private static int FirstDiff(long[] a, long[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i])
                return i;
        return n;
    }
}
