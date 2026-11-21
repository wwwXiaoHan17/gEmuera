// ColorMatrix 像素算子原生化等价门禁（native/gemuera-native/src/colormatrix.rs）：
//   dotnet run --project tools/colormatrix-check -- <native.dll> [game-dll]
//       默认：C# 参考实现（逐行复刻 GraphicsImage.ApplyColorMatrixBytes/ToByte）
//             golden + 随机 sweep 对拍原生；再反射真实游戏程序集的
//             ApplyColorMatrixBytes（毒化 NativeColorMatrix.probeState 强制托管回退）
//             与参考实现三方互证——防参考实现自身漂移。
//   dotnet run --project tools/colormatrix-check -- --bench <native.dll> [game-dll]
//       托管路径 vs 生产接线路径（probeState=0，原生快路径生效）耗时对比。
// 退出码：0 = 全部通过；1 = 有失配。
//
// 注意：接线后反射到的 ApplyColorMatrixBytes 内部含原生快路径；若不毒化 probeState，
// DllImport("gemuera_native") 会命中本工具 NativeLibrary.Load 的同名模块而走原生，
// 对拍变成"原生 vs 原生"自证。毒化（probeState=1）是参考有效性的关键。
using System.Reflection;
using System.Runtime.InteropServices;

namespace gEmuera.Tools.ColormatrixCheck;

internal static class Program
{
    private const uint MinAbiVersion = 4;

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--bench")
        {
            return Bench(args[1], args.Length >= 3 ? args[2] : null);
        }
        string dllPath = args.Length >= 1 ? args[0] : throw new ArgumentException("请传 native.dll 路径");
        string gameDll = args.Length >= 2 ? args[1] : DiscoverGameDll();
        return Sweep(dllPath, gameDll);
    }

    private static string DiscoverGameDll()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".godot", "mono", "temp", "bin", "Debug", "gemuera-c#.dll"),
            Path.Combine(Directory.GetCurrentDirectory(), ".godot", "mono", "temp", "bin", "Debug", "gemuera-c#.dll"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        throw new FileNotFoundException("未找到 gemuera-c#.dll，请显式传第二参数");
    }

    // ===== C# 参考实现：逐行复刻 GraphicsImage.ApplyColorMatrixBytes / ToByte =====
    // 漂移锚点：Sweep 末段把它与反射到的真实托管实现（probeState 毒化后）互证。

    private static byte ToByteReference(float value)
    {
        // 精确复刻 Godot Mathf.RoundToInt：Math.Round(float→double) banker's
        // + (int)（.NET Core 3.0+ 饱和转换：超域→MAX/MIN，NaN→0）
        return (byte)Math.Clamp((int)Math.Round(value * 255.0f), 0, 255);
    }

    private static byte[] ReferenceApply(byte[] data, float[][] cm)
    {
        byte[] outData = new byte[data.Length];
        Array.Copy(data, outData, data.Length);
        float m00 = cm[0][0], m10 = cm[1][0], m20 = cm[2][0], m30 = cm[3][0], m40 = cm[4][0];
        float m01 = cm[0][1], m11 = cm[1][1], m21 = cm[2][1], m31 = cm[3][1], m41 = cm[4][1];
        float m02 = cm[0][2], m12 = cm[1][2], m22 = cm[2][2], m32 = cm[3][2], m42 = cm[4][2];
        float m03 = cm[0][3], m13 = cm[1][3], m23 = cm[2][3], m33 = cm[3][3], m43 = cm[4][3];

        for (int i = 0; i + 3 < outData.Length; i += 4)
        {
            float r = outData[i] / 255.0f;
            float g = outData[i + 1] / 255.0f;
            float b = outData[i + 2] / 255.0f;
            float a = outData[i + 3] / 255.0f;

            float nr = m00 * r + m10 * g + m20 * b + m30 * a + m40;
            float ng = m01 * r + m11 * g + m21 * b + m31 * a + m41;
            float nb = m02 * r + m12 * g + m22 * b + m32 * a + m42;
            float na = m03 * r + m13 * g + m23 * b + m33 * a + m43;

            outData[i] = ToByteReference(nr);
            outData[i + 1] = ToByteReference(ng);
            outData[i + 2] = ToByteReference(nb);
            outData[i + 3] = ToByteReference(na);
        }
        return outData;
    }

    // ===== native FFI =====

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CmApplyDelegate(byte[] data, nuint len, float[] matrix);

    private static (nint Lib, CmApplyDelegate Apply, uint Abi) LoadNative(string dllPath)
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
        var apply = Marshal.GetDelegateForFunctionPointer<CmApplyDelegate>(
            NativeLibrary.GetExport(lib, "gem_cm_apply_rgba8"));
        return (lib, apply, v);
    }

    private static void NativeApply(CmApplyDelegate apply, byte[] data, float[][] cm)
    {
        var m = new float[20];
        for (int row = 0; row < 5; row++)
            for (int col = 0; col < 4; col++)
                m[row * 4 + col] = cm[row][col];
        int rc = apply(data, (nuint)data.Length, m);
        if (rc != 0)
            throw new InvalidOperationException($"native apply 返回 {rc}");
    }

    // ===== 反射真实托管实现（毒化 probeState 强制托管回退） =====

    private static (MethodInfo Apply, FieldInfo ProbeState) ReflectGraphics(GameDll game)
    {
        var t = game.Assembly.GetType("MinorShift.Emuera.Content.GraphicsImage")
            ?? throw new InvalidOperationException("反射未找到 GraphicsImage");
        var mi = t.GetMethod("ApplyColorMatrixBytes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("反射未找到 ApplyColorMatrixBytes");
        var probeT = game.Assembly.GetType("MinorShift._Library.NativeColorMatrix")
            ?? throw new InvalidOperationException("反射未找到 NativeColorMatrix");
        var probe = probeT.GetField("probeState", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("反射未找到 NativeColorMatrix.probeState");
        return (mi, probe);
    }

    private sealed record GameDll(Assembly Assembly, string Path);

    private static GameDll LoadGameDll(string path)
    {
        var asm = Assembly.LoadFrom(path);
        return new GameDll(asm, path);
    }

    // ===== 用例构造 =====

    private sealed record Case(string Name, byte[] Data, float[][] Cm);

    private static uint lcgState = 0x12345678u;
    private static uint Lcg()
    {
        lcgState = lcgState * 1664525u + 1013904223u;
        return lcgState;
    }

    private static float[][] RandomMatrix(int kind)
    {
        // kind 0=现实区间（亮度/对比/着色常见域），1=野生大数，2=病理值注入
        var cm = new float[5][];
        for (int row = 0; row < 5; row++)
        {
            cm[row] = new float[4];
            for (int col = 0; col < 4; col++)
            {
                float v;
                switch (kind)
                {
                    case 0:
                        v = (Lcg() / (float)uint.MaxValue) * 2.0f - 0.5f; // [-0.5, 1.5)
                        break;
                    case 1:
                        v = (Lcg() / (float)uint.MaxValue) * 20000.0f - 10000.0f;
                        break;
                    default:
                        uint pick = Lcg() % 12;
                        v = pick switch
                        {
                            0 => float.NaN,
                            1 => float.PositiveInfinity,
                            2 => float.NegativeInfinity,
                            3 => 0.5f,
                            4 => -0.5f,
                            5 => 90.5f / 255f,
                            6 => 165.5f / 255f,
                            _ => (Lcg() / (float)uint.MaxValue) * 2.0f - 0.5f,
                        };
                        break;
                }
                cm[row][col] = v;
            }
        }
        return cm;
    }

    private static List<Case> BuildCases()
    {
        var cases = new List<Case>();

        // golden：恒等（不动点）、全零（→黑透明）、偏移 ±0.5 tie、上/下溢 clamp、
        // 病理 NaN/±inf、注释确证 tie 值（165.5→166、90.5→90）邻域
        var identity = new float[5][];
        for (int r = 0; r < 5; r++) identity[r] = new float[4];
        identity[0][0] = identity[1][1] = identity[2][2] = identity[3][3] = 1f;
        var data = new byte[256 * 4];
        for (int d = 0; d < 256; d++)
        {
            data[d * 4] = (byte)d;
            data[d * 4 + 1] = (byte)(255 - d);
            data[d * 4 + 2] = (byte)(d / 2);
            data[d * 4 + 3] = 128;
        }
        cases.Add(new Case("identity-full-sweep", (byte[])data.Clone(), identity));

        cases.Add(new Case("zero-matrix", new byte[] { 255, 128, 7, 255, 0, 0, 0, 1 },
            new[] { new float[4], new float[4], new float[4], new float[4], new float[4] }));

        cases.Add(new Case("offset-half-tie",
            new byte[] { 255, 0, 0, 255, 1, 2, 3, 4 },
            MatrixWithOffsets(0.5f, -0.5f, 1.5f, 1f)));

        cases.Add(new Case("comment-tie-values",
            new byte[] { 255, 255, 255, 255, 128, 64, 32, 255 },
            MatrixWithOffsets(165.5f / 255f, 90.5f / 255f, 0.0f, 1f)));

        var pathological = new float[5][];
        for (int r = 0; r < 5; r++) pathological[r] = new float[4];
        pathological[0][0] = float.NaN;
        pathological[1][1] = float.PositiveInfinity;
        pathological[2][2] = float.NegativeInfinity;
        pathological[4][3] = 1f;
        cases.Add(new Case("pathological-nan-inf",
            new byte[] { 255, 255, 255, 255, 3, 9, 200, 7 }, pathological));

        // 随机 sweep：尺寸/矩阵域混合
        for (int k = 0; k < 400; k++)
        {
            int pixels = 1 + (int)(Lcg() % 2000);
            var buf = new byte[pixels * 4];
            for (int i = 0; i < buf.Length; i++)
                buf[i] = (byte)(Lcg() >> 24);
            cases.Add(new Case($"random-{k}", buf, RandomMatrix((int)(Lcg() % 3))));
        }
        return cases;
    }

    private static float[][] MatrixWithOffsets(float rOff, float gOff, float bOff, float aOff)
    {
        var cm = new float[5][];
        for (int r = 0; r < 5; r++) cm[r] = new float[4];
        cm[4][0] = rOff;
        cm[4][1] = gOff;
        cm[4][2] = bOff;
        cm[4][3] = aOff;
        return cm;
    }

    // ===== 主流程 =====

    private static int Sweep(string dllPath, string gameDllPath)
    {
        var (lib, apply, abi) = LoadNative(dllPath);
        var game = LoadGameDll(gameDllPath);
        var (realApply, probeState) = ReflectGraphics(game);
        int failures = 0;
        try
        {
            foreach (var c in BuildCases())
            {
                byte[] expected = ReferenceApply(c.Data, c.Cm);

                byte[] nativeBuf = new byte[c.Data.Length];
                Array.Copy(c.Data, nativeBuf, c.Data.Length);
                NativeApply(apply, nativeBuf, c.Cm);
                if (!nativeBuf.AsSpan().SequenceEqual(expected))
                {
                    int diff = FirstDiff(nativeBuf, expected);
                    Console.WriteLine($"FAIL [native vs reference] {c.Name} first-diff@{diff} native={nativeBuf[diff]} ref={expected[diff]}");
                    failures++;
                }

                // 参考实现漂移锚点：毒化 probeState → 反射真实方法走托管 → 与参考互证
                probeState.SetValue(null, 1);
                try
                {
                    var realBuf = (byte[])(realApply.Invoke(null, new object[] { (byte[])c.Data.Clone(), c.Cm }) ?? throw new InvalidOperationException("反射返回 null"));
                    if (!realBuf.AsSpan().SequenceEqual(expected))
                    {
                        int diff = FirstDiff(realBuf, expected);
                        Console.WriteLine($"FAIL [real-managed vs reference] {c.Name} first-diff@{diff} real={realBuf[diff]} ref={expected[diff]}");
                        failures++;
                    }
                }
                finally
                {
                    probeState.SetValue(null, 0); // 恢复（进程内顺序无关，防患未然）
                }
            }
            // 边界：len % 4 != 0 → native 必须拒绝（C# 包装层因此回退托管）
            var bad = apply(new byte[6], (nuint)6, new float[20]);
            if (bad != -1)
            {
                Console.WriteLine($"FAIL [native-reject-nonmult4] 期望 -1 实得 {bad}");
                failures++;
            }
            var bad2 = apply(null!, (nuint)4, new float[20]);
            if (bad2 != -1)
            {
                Console.WriteLine($"FAIL [native-reject-null] 期望 -1 实得 {bad2}");
                failures++;
            }
            Console.WriteLine(failures == 0
                ? $"colormatrix-check PASS（ABI={abi}，golden+400 随机 case，native/参考/真实托管三方一致）"
                : $"colormatrix-check FAIL：{failures} 项失配");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    private static int Bench(string dllPath, string? gameDllPath)
    {
        var (lib, apply, abi) = LoadNative(dllPath);
        var game = LoadGameDll(gameDllPath ?? DiscoverGameDll());
        var (realApply, probeState) = ReflectGraphics(game);
        try
        {
            const int width = 1024, height = 1024, iterations = 30;
            var buf = new byte[width * height * 4];
            var cm = new float[5][];
            var rngState = 0xC0FFEEu;
            for (int r = 0; r < 5; r++)
            {
                cm[r] = new float[4];
                for (int c = 0; c < 4; c++)
                {
                    rngState = rngState * 1664525u + 1013904223u;
                    cm[r][c] = (rngState / (float)uint.MaxValue) * 2.0f - 0.5f;
                }
            }

            // 托管路径（毒化 probe）
            probeState.SetValue(null, 1);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
                realApply.Invoke(null, new object[] { (byte[])buf.Clone(), cm });
            sw.Stop();
            var managedMs = sw.Elapsed.TotalMilliseconds;

            // 生产路径（probe 复原 → 原生快路径）
            probeState.SetValue(null, 0);
            sw.Restart();
            for (int i = 0; i < iterations; i++)
                realApply.Invoke(null, new object[] { (byte[])buf.Clone(), cm });
            sw.Stop();
            var nativeMs = sw.Elapsed.TotalMilliseconds;

            double mpx = width * (double)height / 1_000_000 * iterations;
            Console.WriteLine($"bench 1Mpx×{iterations}（含反射/克隆固定开销）：");
            Console.WriteLine($"  托管 {managedMs:F1}ms（{managedMs / mpx:F1} ms/Mpx）");
            Console.WriteLine($"  生产接线（原生） {nativeMs:F1}ms（{nativeMs / mpx:F1} ms/Mpx）");
            Console.WriteLine($"  加速 {managedMs / nativeMs:F2}x  ABI={abi}");
            return 0;
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
}
