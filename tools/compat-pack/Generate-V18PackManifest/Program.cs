using System.Text;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;

// ============================================================================
// v18 第一方数据包清单生成器（packs/gemuera.v18/compatpack.manifest.json）。
//
// 唯一事实源：src/Core/Compatibility/LegacyDialectInventories.Generated.cs（引擎投影
// 的生成镜像）。本工具从 v24/v18 两份生成清单计算差量并落盘为规范化 JSON：
//
//   hideInstructions = V24InstructionNames − V18InstructionNames（Ordinal 排序）
//   hideFunctions    = V24Functions.Name − V18Functions.Name（Ordinal 排序）
//   addInstructions  = V18InstructionNames − V24InstructionNames（必须为空）
//   addFunctions     = V18Functions.Name − V24Functions.Name（必须为空）
//
// add* 非空 = v18 参考拥有 v24 没有的注册名（当前取证：v18 ⊆ v24），包表面通道
// 表达不了"新增名字必须有真实 handler"的对账语义，此时必须显式处理而不是放行——
// 本工具以非零退出码失败并列出违规名。
//
// 输出规范化：对象键 Ordinal 排序、数组 Ordinal 排序、LF 行尾、UTF-8 无 BOM、
// 末尾恰好一个换行。内容未变化时不重写（git 无噪声）。
// 版本轴：新格式双轴 contractAbi/engineApi（格式 major.minor），不再输出 legacy
// targetEngineApi（加载器仍接受其迁移映射，但官方包必须已完成迁移）。
// baseSurfaceHash：与宿主共用 Core LegacySurfaceHash 的语义输入构造（profile/engineApi/
// 指令与函数描述符/内置变体/capability），禁止手写；重跑本工具即恢复。
// 生成物禁止手改：清单漂移由 GEmuera.Core.Tests/V18PackManifestTests 门禁拦截，
// 重跑本工具即可恢复。
//
// 用法：dotnet run --project tools/compat-pack/Generate-V18PackManifest -- <repo-root>
// ============================================================================

string? repoRoot = args.FirstOrDefault(arg => !arg.StartsWith('-'));
if (string.IsNullOrWhiteSpace(repoRoot))
{
    Console.Error.WriteLine("用法：Generate-V18PackManifest <repo-root>");
    return 2;
}
repoRoot = Path.GetFullPath(repoRoot);

// —— 差量计算（全部 Ordinal；生成清单本身已排序，此处不依赖该前提） ——
string[] hideInstructions = LegacyDialectInventories.V24InstructionNames
    .Except(LegacyDialectInventories.V18InstructionNames, StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToArray();
string[] addInstructions = LegacyDialectInventories.V18InstructionNames
    .Except(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToArray();
string[] v24FunctionNames = LegacyDialectInventories.V24Functions.Select(entry => entry.Name).ToArray();
string[] v18FunctionNames = LegacyDialectInventories.V18Functions.Select(entry => entry.Name).ToArray();
string[] hideFunctions = v24FunctionNames
    .Except(v18FunctionNames, StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToArray();
string[] addFunctions = v18FunctionNames
    .Except(v24FunctionNames, StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToArray();

// —— add* 死契约：非空即失败（fail-closed），不生成任何文件 ——
if (addInstructions.Length > 0 || addFunctions.Length > 0)
{
    Console.Error.WriteLine("v18 清单出现 v24 没有的注册名（v18 ⊄ v24），包表面通道无法表达，必须显式处理：");
    foreach (string name in addInstructions)
        Console.Error.WriteLine("  addInstruction: " + name);
    foreach (string name in addFunctions)
        Console.Error.WriteLine("  addFunction: " + name);
    Console.Error.WriteLine("处理方式：确认引擎 handler 后在生成清单/方言模块补声明，或从 v18 清单剔除该名。");
    return 1;
}

// —— 自检不变量：v24 − hide == v18（键集合意义上）。数学上由 Except 定义保证，
//    显式复核防未来把计算改成手工名单/部分名单时静默漂移。——
bool instructionClosureEqual = LegacyDialectInventories.V24InstructionNames
    .Except(hideInstructions, StringComparer.Ordinal)
    .ToHashSet(StringComparer.Ordinal)
    .SetEquals(LegacyDialectInventories.V18InstructionNames);
bool functionClosureEqual = v24FunctionNames
    .Except(hideFunctions, StringComparer.Ordinal)
    .ToHashSet(StringComparer.Ordinal)
    .SetEquals(v18FunctionNames);
if (!instructionClosureEqual || !functionClosureEqual)
{
    Console.Error.WriteLine("自检失败：v24 − hide != v18（键集合），生成清单数据异常，拒绝落盘。");
    return 1;
}

// —— 语义级 v24 基线表面哈希（T7）：与宿主共用 Core 的 SurfaceHashInput 构造（R3），
//    禁止手写 hash；覆盖 profile/engineApi/指令与函数描述符/内置变体/capability。 ——
string baseSurfaceHash = LegacySurfaceHash.ComputeV24SurfaceHash(
    LegacySurfaceHash.BuildV24SurfaceHashInput("v24pure"));

// —— 规范化 JSON（键 Ordinal 排序；数组已排序；LF；UTF-8 无 BOM；末尾单换行） ——
string json = BuildManifestJson(baseSurfaceHash, hideInstructions, hideFunctions);

string outputDirectory = Path.Combine(repoRoot, "packs", "gemuera.v18");
Directory.CreateDirectory(outputDirectory);
string outputPath = Path.Combine(outputDirectory, "compatpack.manifest.json");

// 内容不变不重写（时间戳即"清单与生成清单同步"的可信信号）。
byte[] payload = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
if (File.Exists(outputPath) && File.ReadAllBytes(outputPath).SequenceEqual(payload))
{
    Console.WriteLine("清单未变化：" + outputPath);
}
else
{
    File.WriteAllBytes(outputPath, payload);
    Console.WriteLine("清单已写入：" + outputPath);
}
Console.WriteLine(
    "surface：hideInstructions=" + hideInstructions.Length
    + "，hideFunctions=" + hideFunctions.Length
    + "，addInstructions=0，addFunctions=0（v18 ⊆ v24 取证成立）。");
Console.WriteLine(
    "闭包自检：v24 指令 " + LegacyDialectInventories.V24InstructionNames.Length
    + " − hide " + hideInstructions.Length + " = v18 指令 " + LegacyDialectInventories.V18InstructionNames.Length
    + "；v24 函数 " + v24FunctionNames.Length
    + " − hide " + hideFunctions.Length + " = v18 函数 " + v18FunctionNames.Length + "。");
Console.WriteLine("baseSurfaceHash：" + baseSurfaceHash);
return 0;

static string BuildManifestJson(string baseSurfaceHash, string[] hideInstructions, string[] hideFunctions)
{
    // packId 说明：不能叫 "gemuera.v18"——它与内置方言模块 id 撞名（Compose 的包模块
    // 白名单禁止包含内置模块 id，CompatPackRules 的保留名对账同样拒载），故用 pack. 前缀。
    var builder = new StringBuilder();
    builder.Append("{\n");
    builder.Append("  \"baseProfileId\": \"v24pure\",\n");
    builder.Append("  \"baseSurfaceHash\": \"").Append(baseSurfaceHash).Append("\",\n");
    builder.Append("  \"capabilities\": [],\n");
    builder.Append("  \"contractAbi\": \"1.0\",\n");
    builder.Append("  \"engineApi\": \"1.0\",\n");
    builder.Append("  \"packId\": \"pack.gemuera.v18\",\n");
    builder.Append("  \"packVersion\": \"1.0.0\",\n");
    builder.Append("  \"surface\": {\n");
    builder.Append("    \"addFunctions\": [],\n");
    builder.Append("    \"addInstructions\": [],\n");
    AppendNameArray(builder, "hideFunctions", hideFunctions);
    builder.Append(",\n");
    AppendNameArray(builder, "hideInstructions", hideInstructions);
    builder.Append("\n");
    builder.Append("  }\n");
    builder.Append("}\n");
    return builder.ToString();
}

static void AppendNameArray(StringBuilder builder, string key, string[] names)
{
    builder.Append("    \"").Append(Escape(key)).Append("\": [");
    for (int index = 0; index < names.Length; index++)
    {
        if (index % 4 == 0)
        {
            builder.Append('\n').Append("      ");
        }
        builder.Append('"').Append(Escape(names[index])).Append('"');
        if (index < names.Length - 1)
            builder.Append(',');
    }
    if (names.Length > 0)
        builder.Append('\n').Append("    ");
    builder.Append(']');
}

static string Escape(string text)
{
    // 名字来自引擎注册表（标识符形态），常规路径不触发转义；防御性覆盖 JSON 必须转义的字符。
    return text
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\b", "\\b")
        .Replace("\f", "\\f")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");
}
