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
using System.Runtime.InteropServices;

namespace gEmuera.Tools.NativeWidthCheck;

internal static class Program
{
    private const uint ExpectedAbiVersion = 1;

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "gen-tables")
        {
            GenTables(args[1]);
            return 0;
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
            if (abiVersion != ExpectedAbiVersion)
            {
                Console.WriteLine($"FAIL: ABI 版本不匹配 native={abiVersion} expected={ExpectedAbiVersion}");
                return 1;
            }

            var widthUnit = Marshal.GetDelegateForFunctionPointer<WidthUnitDelegate>(
                NativeLibrary.GetExport(lib, "gem_text_width_unit"));
            var widthUtf16 = Marshal.GetDelegateForFunctionPointer<WidthUtf16Delegate>(
                NativeLibrary.GetExport(lib, "gem_text_width_utf16"));

            int failures = 0;

            failures += CheckGolden(widthUnit);
            failures += SweepFullBmp(widthUnit);
            failures += CheckBatch(widthUtf16);

            Console.WriteLine(failures == 0
                ? "PASS: golden + 全 BMP(65536) 扫描 + 批量接口 与托管参考实现逐码点一致。"
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
}
