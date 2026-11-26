// 掩码混合算子原生化等价门禁（native/gemuera-native/src/blend.rs）：
//   dotnet run --project tools/blend-check -- <native.dll> [game-dll]
//       默认：C# 参考实现（逐行复刻 GraphicsImage.BlendWithMaskBytes 托管循环）
//             golden（三分支/裁剪/modified）+ 随机 sweep 对拍原生；再反射真实方法
//             （毒化 NativeBlend.probeState 强制托管回退）三方互证。
//   dotnet run --project tools/blend-check -- --bench <native.dll> [game-dll]
//       托管路径 vs 生产接线路径耗时对比（1Mpx×30，三分支掩码混布）。
// 退出码：0 = 全部通过；1 = 有失配。
//
// 注意：接线后反射到的 BlendWithMaskBytes 内部含原生快路径；若不毒化 probeState，
// DllImport("gemuera_native") 会命中本工具 NativeLibrary.Load 的同名模块而走原生，
// 对拍变成"原生 vs 原生"自证。毒化是参考有效性的关键（与 colormatrix-check 同构）。
using System.Reflection;
using System.Runtime.InteropServices;

namespace gEmuera.Tools.BlendCheck;

internal static class Program
{
    private const uint MinAbiVersion = 6;

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--bench")
        {
            return Bench(args[1], args.Length >= 3 ? args[2] : null!);
        }
        string dllPath = args.Length >= 1 ? args[0] : throw new ArgumentException("请传 native.dll 路径");
        string gameDll = args.Length >= 2 ? args[1] : DiscoverGameDll();
        return Sweep(dllPath, gameDll);
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

    // ===== C# 参考实现：逐行复刻 GraphicsImage.BlendWithMaskBytes 托管循环 =====

    private static bool ReferenceBlend(
        byte[] dstData, int dw, int dh,
        byte[] srcData, int srcW,
        byte[] maskData, int maskW,
        int w, int h, int destX, int destY, bool maskAlphaChannel)
    {
        bool modified = false;
        for (int y = 0; y < h; y++)
        {
            int dy = destY + y;
            if (dy < 0 || dy >= dh) continue;
            for (int x = 0; x < w; x++)
            {
                int dx = destX + x;
                if (dx < 0 || dx >= dw) continue;
                int mi = (y * maskW + x) * 4;
                int maskByte = maskAlphaChannel ? maskData[mi + 3] : maskData[mi + 2];
                if (maskByte == 0) continue;
                int si = (y * srcW + x) * 4;
                int di = (dy * dw + dx) * 4;
                if (maskByte == 255)
                {
                    dstData[di] = srcData[si]; dstData[di + 1] = srcData[si + 1];
                    dstData[di + 2] = srcData[si + 2]; dstData[di + 3] = srcData[si + 3];
                }
                else
                {
                    int ma = maskByte + 1;
                    int ia = 256 - ma;
                    dstData[di] = (byte)((srcData[si] * ma + dstData[di] * ia) >> 8);
                    dstData[di + 1] = (byte)((srcData[si + 1] * ma + dstData[di + 1] * ia) >> 8);
                    dstData[di + 2] = (byte)((srcData[si + 2] * ma + dstData[di + 2] * ia) >> 8);
                    dstData[di + 3] = (byte)((srcData[si + 3] * ma + dstData[di + 3] * ia) >> 8);
                }
                modified = true;
            }
        }
        return modified;
    }

    // ===== native FFI =====

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int BlendWithMaskDelegate(
        byte[] dst, nuint dw, nuint dh, byte[] src, nuint srcW,
        byte[] mask, nuint maskW, nuint w, nuint h, int destX, int destY, int maskAlpha);

    private static (nint Lib, BlendWithMaskDelegate Blend, uint Abi) LoadNative(string dllPath)
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
        var blend = Marshal.GetDelegateForFunctionPointer<BlendWithMaskDelegate>(
            NativeLibrary.GetExport(lib, "gem_blend_with_mask"))!;
        return (lib, blend, v);
    }

    private static bool NativeBlendCall(
        BlendWithMaskDelegate blend,
        byte[] dst, int dw, int dh, byte[] src, int srcW, byte[] mask, int maskW,
        int w, int h, int destX, int destY, bool maskAlpha, out bool modified)
    {
        modified = false;
        int rc = blend(dst, (nuint)dw, (nuint)dh, src, (nuint)srcW, mask, (nuint)maskW,
            (nuint)w, (nuint)h, destX, destY, maskAlpha ? 1 : 0);
        if (rc < 0)
            throw new InvalidOperationException($"native blend 返回 {rc}");
        modified = rc == 1;
        return true;
    }

    // ===== 用例 =====

    private static uint lcgState = 0x20261122u;
    private static uint Lcg()
    {
        lcgState = lcgState * 1664525u + 1013904223u;
        return lcgState;
    }

    private sealed record Case(
        string Name, byte[] Dst, int Dw, int Dh,
        byte[] Src, int SrcW, byte[] Mask, int MaskW,
        int W, int H, int DestX, int DestY, bool MaskAlpha);

    private static (byte[] Mask, int MaskW) BuildMask(int w, int h, bool alphaMode, int distribution)
    {
        int maskW = w; // 门禁内 mask 与 src 同宽
        var mask = new byte[maskW * h * 4];
        int mOff = alphaMode ? 3 : 2;
        for (int i = 0; i < w * h; i++)
        {
            byte m = distribution switch
            {
                0 => 0,                                   // 全跳过
                1 => 255,                                 // 全直拷
                2 => (byte)(1 + Lcg() % 254),             // 全混合
                _ => (Lcg() % 100) switch                 // 三分支混布（运行段友好：行内连片）
                {
                    < 30 => 0,
                    < 60 => 255,
                    _ => (byte)(1 + Lcg() % 254),
                },
            };
            mask[i * 4 + mOff] = m;
        }
        return (mask, maskW);
    }

    private static List<Case> BuildCases()
    {
        var cases = new List<Case>();
        // golden：三分支单像素、全拷贝运行段、列/行裁剪四象限、全越界、空绘制
        void Add(string name, int dw, int dh, int w, int h, int destX, int destY, bool alpha, int distribution)
        {
            var dst = new byte[dw * dh * 4];
            for (int i = 0; i < dst.Length; i++) dst[i] = (byte)(Lcg() >> 24);
            var src = new byte[w * h * 4];
            for (int i = 0; i < src.Length; i++) src[i] = (byte)(Lcg() >> 24);
            var (mask, maskW) = BuildMask(w, h, alpha, distribution);
            cases.Add(new Case(name, dst, dw, dh, src, w, mask, maskW, w, h, destX, destY, alpha));
        }
        Add("golden-skip", 4, 1, 4, 1, 0, 0, false, 0);
        Add("golden-copy", 4, 1, 4, 1, 0, 0, false, 1);
        Add("golden-blend", 4, 1, 4, 1, 0, 0, false, 2);
        Add("golden-mixed", 16, 4, 16, 4, 0, 0, false, 3);
        Add("golden-mixed-alpha", 16, 4, 16, 4, 0, 0, true, 3);
        Add("clip-neg-x-y", 8, 8, 4, 4, -2, -2, false, 3);
        Add("clip-pos-x-y", 8, 8, 4, 4, 6, 6, false, 3);
        Add("clip-cross", 8, 8, 8, 8, -3, -3, true, 3);
        Add("clip-fully-out", 8, 8, 4, 4, 8, 8, false, 3);
        Add("dest-partial-rows", 6, 6, 10, 10, -2, -3, true, 3);
        Add("dest-offset-1-1", 8, 8, 4, 4, 1, 1, false, 3);
        for (int k = 0; k < 300; k++)
        {
            int dw = 1 + (int)(Lcg() % 32);
            int dh = 1 + (int)(Lcg() % 32);
            int w = 1 + (int)(Lcg() % 32);
            int h = 1 + (int)(Lcg() % 32);
            int destX = (int)(Lcg() % 64) - 32;
            int destY = (int)(Lcg() % 64) - 32;
            bool alpha = Lcg() % 2 == 1;
            Add($"random-{k}", dw, dh, w, h, destX, destY, alpha, 3);
        }
        return cases;
    }

    // ===== 主流程 =====

    private static int Sweep(string dllPath, string gameDllPath)
    {
        var (lib, blend, abi) = LoadNative(dllPath);
        var asm = Assembly.LoadFrom(gameDllPath);
        var giType = asm.GetType("MinorShift.Emuera.Content.GraphicsImage")
            ?? throw new InvalidOperationException("反射未找到 GraphicsImage");
        var realApply = giType.GetMethod("BlendWithMaskBytes",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("反射未找到 BlendWithMaskBytes");
        var codecType = asm.GetType("MinorShift._Library.NativeBlend")
            ?? throw new InvalidOperationException("反射未找到 NativeBlend");
        var probeState = codecType.GetField("probeState", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("反射未找到 NativeBlend.probeState");

        int failures = 0;
        try
        {
            foreach (var c in BuildCases())
            {
                byte[] expected = (byte[])c.Dst.Clone();
                bool refModified = ReferenceBlend(expected, c.Dw, c.Dh, c.Src, c.SrcW,
                    c.Mask, c.MaskW, c.W, c.H, c.DestX, c.DestY, c.MaskAlpha);

                byte[] nativeBuf = (byte[])c.Dst.Clone();
                NativeBlendCall(blend, nativeBuf, c.Dw, c.Dh, c.Src, c.SrcW, c.Mask, c.MaskW,
                    c.W, c.H, c.DestX, c.DestY, c.MaskAlpha, out bool natModified);
                if (!nativeBuf.AsSpan().SequenceEqual(expected) || natModified != refModified)
                {
                    int diff = FirstDiff(nativeBuf, expected);
                    Console.WriteLine($"FAIL [native vs reference] {c.Name} first-diff@{diff} modified(native={natModified}/ref={refModified})");
                    failures++;
                }

                // 参考实现漂移锚点：毒化 probeState → 反射真实方法走托管 → 与参考互证。
                // 仅 R 通道用例（MaskAlpha=false）：真实方法的通道模式绑定全局
                // Program.Compatibility.Snake.UsesMaskAlphaChannel（工具进程恒 false），
                // alpha 用例的托管对等性由参考转写（同一循环文本仅通道下标不同）+ Rust fuzz
                // 200 组 alpha 朴素参考覆盖。
                if (!c.MaskAlpha)
                {
                    probeState.SetValue(null, 1);
                try
                {
                    var realBuf = (byte[])c.Dst.Clone();
                    var realModified = (bool)(realApply.Invoke(null,
                        new object[] { realBuf, c.Dw, c.Dh, c.Src, c.SrcW, c.Mask, c.MaskW, c.W, c.H, c.DestX, c.DestY })
                        ?? throw new InvalidOperationException("反射返回 null"));
                    if (!realBuf.AsSpan().SequenceEqual(expected) || realModified != refModified)
                    {
                        int diff = FirstDiff(realBuf, expected);
                        Console.WriteLine($"FAIL [real-managed vs reference] {c.Name} first-diff@{diff} modified(real={realModified}/ref={refModified})");
                        failures++;
                    }
                }
                    finally
                    {
                        probeState.SetValue(null, 0);
                    }
                }
            }
            Console.WriteLine(failures == 0
                ? $"blend-check PASS（ABI={abi}，golden+300 随机 case，native/参考逐字节一致；R 通道用例三方互证）"
                : $"blend-check FAIL：{failures} 项失配");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    private static int Bench(string dllPath, string? gameDll)
    {
        gameDll ??= DiscoverGameDll();
        var (lib, blend, abi) = LoadNative(dllPath);
        var asm = Assembly.LoadFrom(gameDll);
        var giType = asm.GetType("MinorShift.Emuera.Content.GraphicsImage")
            ?? throw new InvalidOperationException("反射未找到 GraphicsImage");
        var realApply = giType.GetMethod("BlendWithMaskBytes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("反射未找到 BlendWithMaskBytes");
        var codecType = asm.GetType("MinorShift._Library.NativeBlend");
        var probeState = codecType?.GetField("probeState", BindingFlags.NonPublic | BindingFlags.Static);

        const int size = 1024; // 1Mpx
        const int iterations = 30;
        var dst = new byte[size * size * 4];
        var src = new byte[size * size * 4];
        var mask = new byte[size * size * 4];
        var rnd = new Random(20260912);
        rnd.NextBytes(dst);
        rnd.NextBytes(src);
        // 三分支掩码（行内连片，贴近真实绘制），alpha 模式混入 <255 触发通道切换
        for (int i = 0; i < size * size; i++)
        {
            mask[i * 4 + 2] = (byte)(rnd.Next(100) switch
            {
                < 30 => 0,
                < 60 => 255,
                _ => rnd.Next(1, 255),
            });
        }
        object[] invokeArgs = { dst, size, size, src, size, mask, size, size, size, 0, 0 };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        probeState?.SetValue(null, 1);
        try
        {
            for (int i = 0; i < iterations; i++)
            {
                invokeArgs[0] = (byte[])dst.Clone();
                realApply.Invoke(null, invokeArgs);
            }
        }
        finally
        {
            probeState?.SetValue(null, 0);
        }
        sw.Stop();
        double managedMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            invokeArgs[0] = (byte[])dst.Clone();
            realApply.Invoke(null, invokeArgs);
        }
        sw.Stop();
        double nativeMs = sw.Elapsed.TotalMilliseconds;

        double mpx = size * (double)size / 1_000_000 * iterations;
        Console.WriteLine($"bench 1Mpx×{iterations}（含反射/克隆固定开销）：");
        Console.WriteLine($"  托管 {managedMs:F1}ms（{managedMs / mpx:F1} ms/Mpx）");
        Console.WriteLine($"  生产接线（原生） {nativeMs:F1}ms（{nativeMs / mpx:F1} ms/Mpx）");
        Console.WriteLine($"  加速 {managedMs / nativeMs:F2}x  ABI={abi}");
        NativeLibrary.Free(lib);
        return 0;
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
