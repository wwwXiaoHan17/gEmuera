using System.Reflection;
using SkiaSharp;

// LegacyCompositionBitAlignTest —— Skia ColorMatrix 与 legacy CPU 字节循环的位级对齐实测。
//
// 背景：GraphicsImage.ApplyColorMatrix 的 CPU 路径是 byte/255f → 5×4 矩阵乘 →
// ToByte(Godot RoundToInt(v*255), Clamp)。Skia 的 SKColorFilter.CreateColorMatrix 是
// C++ SIMD 实现，舍入/alpha 语义未由规范保证——能否作为零可观测差异替换只能实测裁定。
//
// 方法：固定像素采样（边界值直积 + 全灰度斜线）× 代表性矩阵集（灰度/反色/半透明/
// 亮度/去饱和/固定种子随机），legacy 算术经反射调 GraphicsImage.ApplyColorMatrixBytes，
// Skia 侧全程 Unpremul RGBA8888 光栅路径，逐字节比对并输出首个差异。
internal static class Program
{
    private static int Main()
    {
        try
        {
            var pixels = BuildPixelSamples();
            var matrices = BuildMatrixSamples();
            Console.WriteLine($"pixels={pixels.Length / 4} matrices={matrices.Count}");

            var baseline = ResolveLegacyApply("ApplyColorMatrixBytes");
            int totalComparisons = 0;
            int skiaMismatches = 0;
            foreach (var (name, cm) in matrices)
            {
                byte[] baselineOutput = (byte[])baseline.Invoke(null, new object[] { pixels.Clone(), cm })!;
                byte[] skiaOutput = ApplyWithSkia(pixels, cm);
                if (baselineOutput.Length != skiaOutput.Length)
                    throw new InvalidOperationException($"length mismatch for matrix {name}.");
                for (int i = 0; i < baselineOutput.Length; i += 4)
                {
                    totalComparisons++;
                    if (baselineOutput[i] != skiaOutput[i] || baselineOutput[i + 1] != skiaOutput[i + 1]
                        || baselineOutput[i + 2] != skiaOutput[i + 2] || baselineOutput[i + 3] != skiaOutput[i + 3])
                    {
                        skiaMismatches++;
                        if (skiaMismatches <= 5)
                        {
                            int p = i / 4;
                            Console.WriteLine(
                                $"SKIA MISMATCH matrix={name} pixel[{p}]=({pixels[i]},{pixels[i + 1]},{pixels[i + 2]},{pixels[i + 3]}) " +
                                $"baseline=({baselineOutput[i]},{baselineOutput[i + 1]},{baselineOutput[i + 2]},{baselineOutput[i + 3]}) " +
                                $"skia=({skiaOutput[i]},{skiaOutput[i + 1]},{skiaOutput[i + 2]},{skiaOutput[i + 3]})");
                        }
                    }
                }
            }

            Console.WriteLine($"comparisons={totalComparisons} skiaMismatches={skiaMismatches}");
            // 记录性输出：Skia 滤镜路线的否决证据（零可观测差异约束下不可替换）。
            // 另一否决（gather/scatter SIMD 0.94x 无收益）见 GraphicsImage.ApplyColorMatrixBytes 注释。
            Console.WriteLine(skiaMismatches > 0
                ? "VERDICT: Skia SKColorFilter is NOT bit-aligned — route rejected under the zero-observable-difference mandate."
                : "VERDICT: Skia SKColorFilter aligned in this sample space.");
            BenchmarkMaskBlend(30);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void BenchmarkMaskBlend(int rounds)
    {
        var blend = ResolveLegacyApply("BlendWithMaskBytes");
        var random = new Random(20260912);
        int size = 1024; // 1Mpx 方形
        var dst = new byte[size * size * 4];
        var src = new byte[size * size * 4];
        var mask = new byte[size * size * 4];
        random.NextBytes(dst);
        random.NextBytes(src);
        // mask 覆盖三分支：0（跳过）/255（直拷）/中间（整数混合），alpha 通道混入 <255 触发 R→A 切换。
        for (int i = 0; i < size * size; i++)
        {
            int r = random.Next(100);
            mask[i * 4] = r == 0 ? (byte)0 : r == 1 ? (byte)255 : (byte)random.Next(256);
            mask[i * 4 + 3] = r == 2 ? (byte)200 : (byte)255;
        }
        int destX = 3, destY = 5; // 含边界裁剪路径
        var args = new object[] { dst, size, size, src, size, mask, size, size - 8, size - 10, destX, destY };
        _ = blend.Invoke(null, args)!;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < rounds; i++)
        {
            args[0] = dst;
            _ = blend.Invoke(null, args)!;
        }
        sw.Stop();
        double msPerMpx = sw.ElapsedMilliseconds / (double)rounds;
        Console.WriteLine($"mask-blend baseline: {msPerMpx:F1} ms/Mpx (scalar; {rounds} rounds)");
        // 裁决基准：<10ms/Mpx 即远低于一帧预算的零头，任何替换路线（含理论 SIMD 2-4x）
        // 的绝对收益都在噪声级——与 ColorMatrix 的 0.94x 先验共同构成"不做"的依据。
        Console.WriteLine(msPerMpx < 10.0
            ? "VERDICT: mask-blend is not a hot spot at this cost — replacement routes rejected on magnitude grounds."
            : $"VERDICT: mask-blend costs {msPerMpx:F1} ms/Mpx — replacement may be worth revisiting with full vectorization.");
    }

    private static MethodInfo ResolveLegacyApply(string methodName)
    {
        Assembly assembly = typeof(EmueraContent).Assembly;
        Type type = assembly.GetType("MinorShift.Emuera.Content.GraphicsImage")
            ?? throw new InvalidOperationException("GraphicsImage type not found.");
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException(methodName + " not found.");
        return method;
    }

    private static byte[] BuildPixelSamples()
    {
        // 通道边界/半值直积 + 全灰度斜线 + 每通道全扫（其它通道取中点）。
        var values = new List<byte[]>();
        int[] edges = { 0, 1, 2, 63, 127, 128, 129, 191, 254, 255 };
        foreach (int r in edges)
        foreach (int g in edges)
        foreach (int b in edges)
        foreach (int a in edges)
            values.Add(new[] { (byte)r, (byte)g, (byte)b, (byte)a });
        for (int i = 0; i < 256; i++)
            values.Add(new[] { (byte)i, (byte)i, (byte)i, (byte)i });
        for (int i = 0; i < 256; i++)
            values.Add(new byte[] { (byte)i, 17, 200, 255 });
        for (int i = 0; i < 256; i++)
            values.Add(new byte[] { 31, (byte)i, 99, 128 });

        var buffer = new byte[values.Count * 4];
        for (int i = 0; i < values.Count; i++)
        {
            buffer[i * 4] = values[i][0];
            buffer[i * 4 + 1] = values[i][1];
            buffer[i * 4 + 2] = values[i][2];
            buffer[i * 4 + 3] = values[i][3];
        }
        return buffer;
    }

    private static List<(string Name, float[][] Cm)> BuildMatrixSamples()
    {
        float[][] FromRows(float[] r, float[] g, float[] b, float[] a, float[] o)
            => new[] { r, g, b, a, o };

        var list = new List<(string, float[][] )>
        {
            ("identity-nontrivial-offset", FromRows(
                new[] { 1f, 0, 0, 0 }, new[] { 0f, 1f, 0, 0 }, new[] { 0f, 0, 1f, 0 },
                new[] { 0f, 0, 0, 1f }, new[] { 0.003f, -0.002f, 0.001f, 0f })),
            ("grayscale-luma", FromRows(
                new[] { 0.299f, 0, 0, 0 }, new[] { 0.587f, 0, 0, 0 }, new[] { 0.114f, 0, 0, 0 },
                new[] { 0f, 0, 0, 1f }, new[] { 0f, 0, 0, 0f })),
            ("invert", FromRows(
                new[] { -1f, 0, 0, 0 }, new[] { 0f, -1f, 0, 0 }, new[] { 0f, 0, -1f, 0 },
                new[] { 0f, 0, 0, 1f }, new[] { 1f, 1f, 1f, 0f })),
            ("alpha-half", FromRows(
                new[] { 1f, 0, 0, 0 }, new[] { 0f, 1f, 0, 0 }, new[] { 0f, 0, 1f, 0 },
                new[] { 0f, 0, 0, 0.5f }, new[] { 0f, 0, 0, 0f })),
            ("brightness-lift", FromRows(
                new[] { 1.2f, 0, 0, 0 }, new[] { 0f, 1.2f, 0, 0 }, new[] { 0f, 0, 1.2f, 0 },
                new[] { 0f, 0, 0, 1f }, new[] { 0.1f, -0.1f, 0.05f, 0f })),
            ("desaturate-mix", FromRows(
                new[] { 0.6f, 0.2f, 0.2f, 0 }, new[] { 0.2f, 0.6f, 0.2f, 0 }, new[] { 0.2f, 0.2f, 0.6f, 0 },
                new[] { 0f, 0, 0, 0.9f }, new[] { 0.01f, 0, -0.01f, 0.05f })),
        };
        var random = new Random(20260912);
        for (int k = 0; k < 8; k++)
        {
            var m = new float[5][];
            for (int i = 0; i < 5; i++)
            {
                m[i] = new float[4];
                for (int j = 0; j < 4; j++)
                    m[i][j] = (float)((random.NextDouble() - 0.5) * 1.6);
            }
            list.Add(($"random-{k}", m));
        }
        return list;
    }

    private static byte[] ApplyWithSkia(byte[] pixels, float[][] cm)
    {
        int count = pixels.Length / 4;
        int width = Math.Min(count, 256);
        int height = (count + width - 1) / width;
        int total = width * height;
        var padded = new byte[total * 4];
        Array.Copy(pixels, padded, Math.Min(pixels.Length, padded.Length));

        // legacy cm[输入通道][输出通道] + cm[4][输出] 偏移 → Skia 4×5 行主序（每输出通道一行）。
        var skiaMatrix = new float[20];
        for (int outChannel = 0; outChannel < 4; outChannel++)
        {
            skiaMatrix[outChannel * 5 + 0] = cm[0][outChannel];
            skiaMatrix[outChannel * 5 + 1] = cm[1][outChannel];
            skiaMatrix[outChannel * 5 + 2] = cm[2][outChannel];
            skiaMatrix[outChannel * 5 + 3] = cm[3][outChannel];
            skiaMatrix[outChannel * 5 + 4] = cm[4][outChannel];
        }

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(padded, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
        var info2 = info;
        using var source = new SKBitmap();
        source.InstallPixels(info2, pinned.AddrOfPinnedObject(), width * 4);
        using var surface = SKSurface.Create(info);
        if (surface == null)
            throw new InvalidOperationException("SKSurface.Create returned null.");
        using var paint = new SKPaint();
        using var filter = SKColorFilter.CreateColorMatrix(skiaMatrix);
        paint.ColorFilter = filter;
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawBitmap(source, 0, 0, paint);
        surface.Canvas.Flush();
        using var snapshot = surface.Snapshot();
        var pixmap = new SKPixmap();
        if (!snapshot.PeekPixels(pixmap))
            throw new InvalidOperationException("pixel readback failed.");
        var read = new byte[total * 4];
        System.Runtime.InteropServices.Marshal.Copy(pixmap.GetPixels(), read, 0, total * 4);
        var result = new byte[pixels.Length];
        Array.Copy(read, result, Math.Min(read.Length, result.Length));
        // 采样像素恰好铺满 padding 时末尾可能带入补齐像素，裁回原长。
        return result;
        }
        finally
        {
            pinned.Free();
        }
    }
}
