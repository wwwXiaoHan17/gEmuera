// SPDX-License-Identifier: MIT
// 等价性校验工具：native gemuera_native 宽度函数 vs 托管参考实现（逐行移植自
// Scripts/uEmuera/Utils.cs:894-1055）。修改 Utils 宽度规则后必须同步本文件参考实现
// 并重新生成 Rust 表格。
//
// 用法：
//   dotnet run --project tools/native-width-check -- <native.dll 路径>
//   dotnet run --project tools/native-width-check -- gen-tables <输出 .rs 路径>
// 退出码：0 = 全部通过；1 = 有失配或加载失败。
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace gEmuera.Tools.NativeWidthCheck;

internal static class Program
{
    private const uint MinAbiVersion = 2; // 宽度导出于 v2 引入，向后兼容

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "gen-tables")
        {
            GenTables(args[1]);
            return 0;
        }

        if (args.Length >= 2 && args[0] == "--vs-managed")
        {
            // 对照真实游戏程序集的 uEmuera.Utils.GetByteCount（进程内，非参考实现副本）
            return VsManaged(args[1], args.Length >= 3 ? args[2] : null);
        }

        if (args.Length >= 2 && args[0] == "--bench")
        {
            return Bench(args[1]);
        }

        string dllPath = args.Length >= 1
            ? args[0]
            : FindDefaultNativeDll()
              ?? throw new FileNotFoundException("未找到 gemuera_native.dll，请先运行 tools/native-build/Build-GemueraNative.ps1 或显式传入路径。");

        Console.WriteLine($"加载原生库: {dllPath}");
        nint lib = NativeLibrary.Load(dllPath);
        try
        {
            var abi = Marshal.GetDelegateForFunctionPointer<AbiVersionDelegate>(
                NativeLibrary.GetExport(lib, "gem_native_abi_version"));
            uint abiVersion = abi();
            if (abiVersion < MinAbiVersion)
            {
                Console.WriteLine($"FAIL: ABI 版本过低 native={abiVersion} min={MinAbiVersion}");
                return 1;
            }

            var widthUnit = Marshal.GetDelegateForFunctionPointer<WidthUnitDelegate>(
                NativeLibrary.GetExport(lib, "gem_text_width_unit"));
            var widthUtf16 = Marshal.GetDelegateForFunctionPointer<WidthUtf16Delegate>(
                NativeLibrary.GetExport(lib, "gem_text_width_utf16"));
            var widthUnits = Marshal.GetDelegateForFunctionPointer<WidthUnitsUtf16Delegate>(
                NativeLibrary.GetExport(lib, "gem_text_width_units_utf16"));

            int failures = 0;

            failures += CheckGolden(widthUnit);
            failures += SweepFullBmp(widthUnit);
            failures += CheckBatch(widthUtf16);
            failures += CheckUnitsApi(widthUnit, widthUnits);

            Console.WriteLine(failures == 0
                ? "PASS: golden + 全 BMP(65536) 扫描 + 批量接口 + 宽度序列 与托管参考实现逐码点一致。"
                : $"FAIL: 共 {failures} 处失配。");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    // ===== 托管参考实现：逐行移植 Scripts/uEmuera/Utils.cs:894-1055 =====

    private static readonly HashSet<char> Fullsize = new()
    {
        '¢', '£', '¬', '§', '¨', '°', '±', '´', '¶', '·', '×', '÷',
    };

    private static readonly HashSet<char> Halfsize = new()
    {
        '▀','▁','▂','▃','▄','▅',
        '▆','▇','█','▉','▊','▋',
        '▌','▍','▎','▏','▐','░',
        '▒','▓','▔','▕', '▮',
        '◮', '♮', '❮',
        '⟮', '⠮','⡮','⢮', '⣮',
        '▤','▥','▦', '▧', '▨', '▩',
        '▪', '▫','~', '´', 'ﾄ', '｡', '･',
    };

    private static bool CheckFullSize(char c) => Fullsize.Contains(c) || IsFullWidthForm(c);

    private static bool IsFullWidthForm(char c)
        => (c >= '\uFF01' && c <= '\uFF60') || (c >= '\uFFE0' && c <= '\uFFE6');

    private static bool IsSingleCellUnicodeArt(char c)
        => (c >= '\u2580' && c <= '\u259F') || (c >= '\u2800' && c <= '\u28FF');

    private static bool IsHalfWidthKatakana(char c) => c >= '\uFF61' && c <= '\uFF9F';

    private static bool CheckZeroWidth(char c)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
        if (category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format)
        {
            return true;
        }
        return c == '\u200B'
            || c == '\u200C'
            || c == '\u200D'
            || c == '\u2060'
            || c == '\uFEFF'
            || c == '\u180E'
            || c == '\u00AD'
            || (c >= '\uFE00' && c <= '\uFE0F');
    }

    private static bool CheckHalfSize(char c)
    {
        if (c == '\u2022')
        {
            return true;
        }
        if (CheckFullSize(c))
        {
            return false;
        }
        if (IsSingleCellUnicodeArt(c))
        {
            return true;
        }
        return c < 0x127 || IsHalfWidthKatakana(c) || Halfsize.Contains(c);
    }

    private static int ReferenceWidthUnit(char c) => CheckZeroWidth(c) ? 0 : CheckHalfSize(c) ? 1 : 2;

    // ===== 校验 =====

    private static readonly (char Char, int Width, string Why)[] Golden =
    {
        ('A', 1, "ASCII"),
        ('あ', 2, "平假名"),
        ('¢', 2, "fullsize 表"),
        ('§', 2, "fullsize 表（曾因表乱序漏判）"),
        ('¬', 2, "fullsize 表（曾因表乱序漏判）"),
        ('¨', 2, "fullsize 表"),
        ('×', 2, "fullsize 表"),
        ('÷', 2, "fullsize 表"),
        ('´', 2, "fullsize 表（halfsize 成员被遮蔽，Utils.cs:898 先判 fullsize）"),
        ('\u2022', 1, "• CP932 不可映射特例（Utils.cs:921）"),
        ('█', 1, "Block Elements 单格 art（Utils.cs:936）"),
        ('▮', 1, "halfsize 表"),
        ('▤', 1, "halfsize 表"),
        ('ｱ', 1, "半角片假名"),
        ('א', 2, "希伯来字母 → 全宽"),
        ('а', 2, "西里尔字母 → 全宽"),
        ('\u0126', 1, "Ħ < 0x127 → 半宽"),
        ('\u0127', 2, "ħ 不满足 c < 0x127 → 全宽"),
        ('\u0301', 0, "组合附加符号 Mn"),
        ('\uFEFF', 0, "BOM"),
        ('\u200D', 0, "ZWJ"),
        ('\u2060', 0, "WORD JOINER"),
        ('\u180E', 0, "蒙古元音分隔符"),
        ('\u00AD', 0, "软连字符"),
        ('\uFE0F', 0, "VS16"),
        ('\uFF61', 1, "｡ 半角片假名下界"),
        ('\uFF9F', 1, "半角片假名上界"),
        ('\uFFA0', 2, "超出半角片假名 → 全宽"),
        ('\uFF01', 2, "！全角形式下界"),
        ('\uFF60', 2, "全角形式上界"),
        ('\uFFE6', 2, "全角形式附加区上界"),
        ('\uD83C', 2, "高代理单元非零宽非半宽 → 2"),
        ('\n', 1, "LF 控制字符 < 0x127 → 1（保持 C# 行为）"),
    };

    private static int CheckGolden(WidthUnitDelegate widthUnit)
    {
        int failures = 0;
        foreach (var (ch, width, why) in Golden)
        {
            int managed = ReferenceWidthUnit(ch);
            int native = widthUnit(ch);
            if (managed != width)
            {
                Console.WriteLine($"FAIL golden(U+{(int)ch:X4} {why}): 参考实现漂移 managed={managed} expected={width}");
                failures++;
            }
            if (native != width)
            {
                Console.WriteLine($"FAIL golden(U+{(int)ch:X4} {why}): native={native} expected={width}");
                failures++;
            }
        }
        return failures;
    }

    private static int SweepFullBmp(WidthUnitDelegate widthUnit)
    {
        int failures = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int cp = 0; cp <= 0xFFFF; cp++)
        {
            char c = (char)cp;
            int expected = ReferenceWidthUnit(c);
            int actual = widthUnit(c);
            if (expected != actual)
            {
                if (failures < 20)
                {
                    Console.WriteLine($"MISMATCH U+{cp:X4}: managed={expected} native={actual}");
                }
                failures++;
            }
        }
        sw.Stop();
        Console.WriteLine($"全 BMP 扫描 65536 码元耗时 {sw.ElapsedMilliseconds} ms，失配 {failures} 处。");
        return failures;
    }

    private static int CheckBatch(WidthUtf16Delegate widthUtf16)
    {
        int failures = 0;
        string[] samples =
        {
            "ABあ•█　入れ子", // 全角/半角混合
            "🇯🇵🄰あ", // 代理对：按单元计（与 C# str[i] 逐单元一致）
            "a\u0301\uFE0F", // 组合附加 + VS16 → 零宽
            string.Empty,
        };
        foreach (string s in samples)
        {
            long expected = 0;
            foreach (char c in s)
            {
                expected += ReferenceWidthUnit(c);
            }
            ushort[] units = s.Select(c => (ushort)c).ToArray();
            long actual = widthUtf16(units, (nuint)units.Length);
            if (expected != actual)
            {
                Console.WriteLine($"FAIL batch(\"{s}\"): managed={expected} native={actual}");
                failures++;
            }
        }
        return failures;
    }

    // ===== 表格生成 =====

    /// <summary>
    /// 用当前 .NET 运行时的 CharUnicodeInfo 生成 BMP 内 Mn/Me/Cf（零宽分类）区间表，
    /// 供 Rust 侧使用（native/gemuera-native/src/zero_width_tables.rs）。
    /// 从实际运行时生成（而非 Rust 生态的 Unicode 数据）以保证分类逐码点一致。
    /// </summary>
    private static void GenTables(string outPath)
    {
        var ranges = new List<(int Lo, int Hi)>();
        for (int cp = 0; cp <= 0xFFFF; cp++)
        {
            char c = (char)cp;
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            bool zeroWidth = category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format;
            if (!zeroWidth)
            {
                continue;
            }
            if (ranges.Count > 0 && ranges[^1].Hi == cp - 1)
            {
                ranges[^1] = (ranges[^1].Lo, cp);
            }
            else
            {
                ranges.Add((cp, cp));
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"// GENERATED FILE — 由 tools/native-width-check gen-tables 生成（.NET {Environment.Version}）。");
        sb.AppendLine("// 不要手改。BMP(0x0000-0xFFFF) 内 UnicodeCategory ∈ {NonSpacingMark, EnclosingMark, Format} 区间，");
        sb.AppendLine("// 升序且互不重叠。Unicode 数据刷新时重新生成并重跑 tools/native-width-check。");
        sb.AppendLine($"pub static UNICODE_ZERO_WIDTH_RANGES: [(u16, u16); {ranges.Count}] = [");
        foreach (var (lo, hi) in ranges)
        {
            sb.AppendLine($"    (0x{lo:X4}, 0x{hi:X4}),");
        }
        sb.AppendLine("];");
        sb.AppendLine();
        sb.AppendLine("pub fn is_unicode_zero_width_category(unit: u16) -> bool {");
        sb.AppendLine("    UNICODE_ZERO_WIDTH_RANGES");
        sb.AppendLine("        .binary_search_by(|&(lo, hi)| {");
        sb.AppendLine("            if hi < unit {");
        sb.AppendLine("                core::cmp::Ordering::Less");
        sb.AppendLine("            } else if lo > unit {");
        sb.AppendLine("                core::cmp::Ordering::Greater");
        sb.AppendLine("            } else {");
        sb.AppendLine("                core::cmp::Ordering::Equal");
        sb.AppendLine("            }");
        sb.AppendLine("        })");
        sb.AppendLine("        .is_ok()");
        sb.AppendLine("}");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"已生成 {ranges.Count} 个区间 → {outPath}");
    }

    private static string? FindDefaultNativeDll()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && File.Exists(Path.Combine(dir.FullName, "gemuera-c#.csproj")) == false)
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            return null;
        }
        string candidate = Path.Combine(dir.FullName, "export", "NativeLibs", "windows-x64", "gemuera_native.dll");
        return File.Exists(candidate) ? candidate : null;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WidthUnitDelegate(ushort unit);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long WidthUtf16Delegate(ushort[] units, nuint len);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long WidthUnitsUtf16Delegate(ushort[] units, nuint len, byte[]? widths);

    /// <summary>
    /// 性能对比：旧托管逐字符路径（参考实现复刻原 Utils/LangManager 逻辑，含每字符 ToString 分配）
    /// vs 新原生路径（与本仓库 C# 接线相同的成本结构：数组 marshal + 单次 FFI）。
    /// 用法：dotnet run --project tools/native-width-check -- --bench <native.dll>
    /// </summary>
    private static int Bench(string nativeDllPath)
    {
        nint lib = NativeLibrary.Load(nativeDllPath);
        try
        {
            var widthUnit = Marshal.GetDelegateForFunctionPointer<WidthUnitDelegate>(
                NativeLibrary.GetExport(lib, "gem_text_width_unit"));
            var widthUtf16 = Marshal.GetDelegateForFunctionPointer<WidthUtf16Delegate>(
                NativeLibrary.GetExport(lib, "gem_text_width_utf16"));
            var widthUnits = Marshal.GetDelegateForFunctionPointer<WidthUnitsUtf16Delegate>(
                NativeLibrary.GetExport(lib, "gem_text_width_units_utf16"));

            // 典型 era 混合文本：ASCII + 全角假名/汉字 + •（CP932 特例）+ 零宽 + 单格 art，约 220 单元
            string text = "BGM:♪lovetheつくしんぼ♥ISAO．\uFEFF▶『日本語フルウェイ』■前沿[№04]•─────────┤地区地图├──★ＭＡＸＨＰ✔体力▲気力◎精力◆ humongous stats 1234567890 あいうえおかきくけこさしすせそ漢字テスト・ＡＩの反応待ち───▶";

            int failures = 0;

            // ===== 场景 1：GetByteCount（STRLEN/SUBSTRING/PRINTC 填充）=====
            {
                const int Iterations = 200_000;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long managedChecksum = 0;
                for (int i = 0; i < Iterations; i++)
                {
                    // 忠实旧版（LangManager 形状）：每字符 ToString() 分配 + 单字符宽度判定
                    int count = 0;
                    for (int j = 0; j < text.Length; j++)
                    {
                        string single = text[j].ToString();
                        foreach (char ch in single)
                            count += ReferenceWidthUnit(ch);
                    }
                    managedChecksum += count;
                }
                sw.Stop();
                long managedMs = sw.ElapsedMilliseconds;

                sw.Restart();
                long nativeChecksum = 0;
                for (int i = 0; i < Iterations; i++)
                {
                    ushort[] units = new ushort[text.Length];
                    for (int j = 0; j < text.Length; j++)
                        units[j] = text[j];
                    nativeChecksum += widthUtf16(units, (nuint)units.Length);
                }
                sw.Stop();
                long nativeMs = sw.ElapsedMilliseconds;

                Console.WriteLine($"GetByteCount（{text.Length} 字混合串 × {Iterations / 1000}k 次）:");
                Console.WriteLine($"  托管逐字符（旧）: {managedMs,6} ms   checksum={managedChecksum}");
                Console.WriteLine($"  原生批量  （新）: {nativeMs,6} ms   checksum={nativeChecksum}   加速 {((double)managedMs / Math.Max(nativeMs, 1)):F1}x");
                if (managedChecksum != nativeChecksum)
                {
                    Console.WriteLine("FAIL: checksum 不一致！");
                    failures++;
                }
            }

            // ===== 场景 2：GetUFTIndex（SUBSTRING 定位，逐字符累加找索引）=====
            {
                const int Iterations = 20_000;
                int langIndex = 150;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long managedChecksum = 0;
                for (int i = 0; i < Iterations; i++)
                {
                    // 旧 LangManager.GetUFTIndex：每字符 ToString 分配 + GetByteCount
                    int jis = 0, utf = 0;
                    for (int j = 0; j < text.Length; j++)
                    {
                        jis += ReferenceWidthUnit(text[j]);
                        utf++;
                        if (jis >= langIndex)
                            break;
                    }
                    managedChecksum += utf;
                }
                sw.Stop();
                long managedMs = sw.ElapsedMilliseconds;

                sw.Restart();
                long nativeChecksum = 0;
                for (int i = 0; i < Iterations; i++)
                {
                    // 新路径（与 LangManager 实际接线一致）：逐字符单次 FFI + 同样的提前 break
                    int jis = 0, utf = 0;
                    for (int j = 0; j < text.Length; j++)
                    {
                        jis += widthUnit(text[j]);
                        utf++;
                        if (jis >= langIndex)
                            break;
                    }
                    nativeChecksum += utf;
                }
                sw.Stop();
                long nativeMs = sw.ElapsedMilliseconds;

                // 预留优化上限（遗留 L7 方向）：缓冲复用 + 单次批量 FFI 取宽度序列
                ushort[] reusedUnits = new ushort[text.Length];
                byte[] reusedWidths = new byte[text.Length];
                long batchChecksum = 0;
                sw.Restart();
                for (int i = 0; i < Iterations; i++)
                {
                    for (int j = 0; j < text.Length; j++)
                        reusedUnits[j] = text[j];
                    widthUnits(reusedUnits, (nuint)reusedUnits.Length, reusedWidths);
                    int jis = 0, utf = 0;
                    for (int j = 0; j < reusedWidths.Length; j++)
                    {
                        jis += reusedWidths[j];
                        utf++;
                        if (jis >= langIndex)
                            break;
                    }
                    batchChecksum += utf;
                }
                sw.Stop();
                long batchMs = sw.ElapsedMilliseconds;
                if (batchChecksum != managedChecksum)
                {
                    Console.WriteLine("FAIL: batch checksum 不一致！");
                    failures++;
                }

                Console.WriteLine($"GetUFTIndex（定位宽度 {langIndex} × {Iterations / 1000}k 次）:");
                Console.WriteLine($"  托管逐字符（旧）: {managedMs,6} ms   checksum={managedChecksum}");
                Console.WriteLine($"  原生逐字符（现状接线）: {nativeMs,6} ms   checksum={nativeChecksum}   加速 {((double)managedMs / Math.Max(nativeMs, 1)):F1}x");
                Console.WriteLine($"  原生批量（L7 上限）: {batchMs,6} ms   checksum={batchChecksum}   加速 {((double)managedMs / Math.Max(batchMs, 1)):F1}x");
                if (managedChecksum != nativeChecksum)
                {
                    Console.WriteLine("FAIL: checksum 不一致！");
                    failures++;
                }
            }

            Console.WriteLine(failures == 0 ? "BENCH PASS（两路径结果一致）" : "BENCH FAIL");
            return failures;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    /// <summary>
    /// 宽度序列 API 对照：gem_text_width_units_utf16 填充值必须与逐单元参考宽度一致，返回值 = 总宽。
    /// </summary>
    private static int CheckUnitsApi(WidthUnitDelegate widthUnit, WidthUnitsUtf16Delegate widthUnits)
    {
        int failures = 0;
        string[] samples =
        {
            "ABあ•█　入れ子",
            "🇯🇵🄰あ",
            "a\u0301\uFE0F",
            "´§¬±",
            string.Empty,
        };
        foreach (string s in samples)
        {
            ushort[] units = s.Select(c => (ushort)c).ToArray();
            byte[] widths = new byte[units.Length];
            long total = widthUnits(units, (nuint)units.Length, widths);
            long expectedTotal = 0;
            for (int i = 0; i < units.Length; i++)
            {
                int expected = ReferenceWidthUnit((char)units[i]);
                if (widths[i] != expected)
                {
                    Console.WriteLine($"FAIL units(\"{s}\"[{i}] U+{units[i]:X4}): native={widths[i]} managed={expected}");
                    failures++;
                }
                expectedTotal += expected;
            }
            if (total != expectedTotal)
            {
                Console.WriteLine($"FAIL units total(\"{s}\"): native={total} managed={expectedTotal}");
                failures++;
            }
        }
        // out=null 时只算总宽（约定行为）。
        ushort[] probe = { (ushort)'A', 0x3042 };
        if (widthUnits(probe, 2, null) != 3)
        {
            Console.WriteLine("FAIL units out=null total: expected 3");
            failures++;
        }
        return failures;
    }

    /// <summary>
    /// 对照真实游戏程序集的 uEmuera.Utils.GetByteCount（进程内反射调用，非参考实现副本）：
    /// dotnet run --project tools/native-width-check -- --vs-managed <gemuera-c#.dll 路径>
    /// 全 BMP 逐码点比对；GodotSharp 等依赖从游戏 dll 同目录解析。
    /// </summary>
    private static int VsManaged(string gameDllPath, string? nativeDllPath)
    {
        nativeDllPath ??= FindDefaultNativeDll()
            ?? throw new FileNotFoundException("未找到 gemuera_native.dll，请传入第二参数或先运行构建脚本。");
        string gameDir = Path.GetDirectoryName(Path.GetFullPath(gameDllPath))!;

        // 托管回退解析：游戏程序集引用 GodotSharp/GEmuera.Core 等，统一从其输出目录加载
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string name = new AssemblyName(e.Name).Name! + ".dll";
            string candidate = Path.Combine(gameDir, name);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };

        Assembly gameAssembly = Assembly.LoadFrom(gameDllPath);
        Type utilsType = gameAssembly.GetType("uEmuera.Utils")
            ?? throw new InvalidOperationException("游戏程序集中未找到 uEmuera.Utils");
        MethodInfo getByteCount = utilsType.GetMethod("GetByteCount", new[] { typeof(string) })
            ?? throw new InvalidOperationException("未找到 uEmuera.Utils.GetByteCount(string)");

        nint lib = NativeLibrary.Load(nativeDllPath);
        try
        {
            var widthUnit = Marshal.GetDelegateForFunctionPointer<WidthUnitDelegate>(
                NativeLibrary.GetExport(lib, "gem_text_width_unit"));

            int failures = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int cp = 0; cp <= 0xFFFF; cp++)
            {
                char c = (char)cp;
                int managed = (int)getByteCount.Invoke(null, new object[] { c.ToString() })!;
                int native = widthUnit(c);
                if (managed != native)
                {
                    if (failures < 20)
                    {
                        Console.WriteLine($"MISMATCH(vs-managed) U+{cp:X4}: managed={managed} native={native}");
                    }
                    failures++;
                }
            }
            sw.Stop();
            Console.WriteLine($"--vs-managed：真实 Utils.GetByteCount 全 BMP 对照完成（{sw.ElapsedMilliseconds} ms），失配 {failures} 处。");
            return failures;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }
}
