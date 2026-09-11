using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;

// LegacyDialectInventoryGenerator —— 方言描述符清单的唯一生成器。
// 从引擎真实投影（FunctionIdentifier.GetInstructionNameDic / FunctionMethodCreator.GetMethodList，
// 经 LegacyCompatibilityProfile.CreateForProfile 组合）导出各方言模块"自有"的指令/函数清单：
//   gemuera.v24 = v24pure 会话表面；game.snake = snake 表面 − v24 表面；game.erafl = erafl 表面 − v24 表面。
// 产物：
//   1. src/Core/Compatibility/LegacyDialectInventories.Generated.cs（Core 模块贡献数据，提交入库）
//   2. tools/legacy-runner/profiles.generated.json（工具消费的 profile 清单，替代手工白名单）
// 引擎注册表或方言名单变化后必须重新运行本工具（dotnet run --project 本目录 -- <repo-root>）。
internal static class Program
{
    private const string V24ModuleId = "gemuera.v24";
    private const string SnakeModuleId = "game.snake";
    private const string EraFlModuleId = "game.erafl";
    private const string V18ModuleId = "gemuera.v18";
    private const string EraBlueModuleId = "game.erablue";

    private static int Main(string[] args)
    {
        try
        {
            string repoRoot = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Directory.GetCurrentDirectory();
            if (!File.Exists(Path.Combine(repoRoot, "gemuera-c#.csproj")))
                throw new InvalidOperationException($"Not a gEmuera repo root: {repoRoot}");

            Assembly legacyAssembly = typeof(EmueraContent).Assembly;
            // eraTW games default to IGNORE CASE:YES; keep the registry comparer deterministic.
            ForceIgnoreCaseVariable(legacyAssembly);
            Type profileType = RequiredType(legacyAssembly, "MinorShift.Emuera.Compatibility.LegacyCompatibilityProfile");
            Type instructionType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameProc.Function.FunctionIdentifier");
            Type functionType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameData.Function.FunctionMethodCreator");

            object v24 = CreateProfile(profileType, "v24pure", scopedVariableInstructionsEnabled: true);
            object snake = CreateProfile(profileType, "snake", scopedVariableInstructionsEnabled: true);
            object erafl = CreateProfile(profileType, "erafl", scopedVariableInstructionsEnabled: true);
            object v18 = CreateProfile(profileType, "v18", scopedVariableInstructionsEnabled: true);
            object erablue = CreateProfile(profileType, "erablue", scopedVariableInstructionsEnabled: true);

            var v24Instructions = GetRegistryKeys(instructionType, "GetInstructionNameDic", profileType, v24);
            var snakeInstructions = GetRegistryKeys(instructionType, "GetInstructionNameDic", profileType, snake);
            var eraflInstructions = GetRegistryKeys(instructionType, "GetInstructionNameDic", profileType, erafl);
            var v18Instructions = GetRegistryKeys(instructionType, "GetInstructionNameDic", profileType, v18);
            var v24Functions = GetRegistry(functionType, "GetMethodList", profileType, v24);
            var snakeFunctions = GetRegistry(functionType, "GetMethodList", profileType, snake);
            var eraflFunctions = GetRegistry(functionType, "GetMethodList", profileType, erafl);
            var v18Functions = GetRegistry(functionType, "GetMethodList", profileType, v18);
            var erablueInstructions = GetRegistryKeys(instructionType, "GetInstructionNameDic", profileType, erablue);
            var erablueFunctions = GetRegistry(functionType, "GetMethodList", profileType, erablue);

            // 模块自有清单 = 表面差集（v24 基线 = v24pure 表面本身）。
            var snakeDeltaInstructions = ExceptSorted(snakeInstructions, v24Instructions);
            var eraflDeltaInstructions = ExceptSorted(eraflInstructions, v24Instructions);
            var snakeDeltaFunctions = ExceptSorted(Keys(snakeFunctions), Keys(v24Functions));
            var eraflDeltaFunctions = ExceptSorted(Keys(eraflFunctions), Keys(v24Functions));
            var erablueDeltaInstructions = ExceptSorted(erablueInstructions, v24Instructions);
            var erablueDeltaFunctions = ExceptSorted(Keys(erablueFunctions), Keys(v24Functions));

            // 生成前哨兵校验：与 LegacyCompatibilityModules 的桥层名单锚定，防止投影语义意外漂移。
            Assert(v24Instructions.Contains("PRINT") && !v24Instructions.Contains("CALLSTR"),
                "v24pure instruction surface drifted (PRINT missing or CALLSTR leaked).");
            Assert(!v24Instructions.Contains("OUTPUTLOG") && !snakeInstructions.Contains("OUTPUTLOG"),
                "Port-only OUTPUTLOG leaked into an upstream surface.");
            Assert(snakeDeltaInstructions.Contains("CALLSTR") && snakeDeltaInstructions.Contains("SETANIMETIMER"),
                "snake instruction delta lost CALLSTR/SETANIMETIMER.");
            Assert(eraflDeltaInstructions.SetEquals(new HashSet<string>(StringComparer.Ordinal) { "SETANIMETIMER" }),
                $"erafl instruction delta must be exactly SETANIMETIMER, got: {string.Join(',', eraflDeltaInstructions)}.");
            Assert(eraflDeltaFunctions.Count == 0,
                $"erafl function delta must be empty, got: {string.Join(',', eraflDeltaFunctions)}.");
            Assert(!v24Functions.ContainsKeySafe("SQL_CONNECT") && Keys(snakeFunctions).Contains("SQL_CONNECT"),
                "snake-only function SQL_CONNECT visibility drifted.");
            Assert(Keys(v24Functions).Contains("陥落状態") || Keys(snakeFunctions).Contains("陥落状態"),
                "CJK built-in function 陥落状態 vanished from every surface.");
            // v18 哨兵：独立基线面 = v24 引擎投影减 v24 后增差集（双源取证 2026-09-12）。
            Assert(v18Instructions.Contains("PRINT") && v18Functions is not null,
                "v18 surface lost baseline PRINT.");
            Assert(!v18Instructions.Contains("SETBGIMAGE") && !v18Instructions.Contains("PRINTN")
                && !v18Instructions.Contains("VARI") && !v18Instructions.Contains("VARS")
                && !v18Instructions.Contains("CALLSTR"),
                "v18 surface leaked v24-era or snake-only instructions.");
            Assert(!Keys(v18Functions).Contains("GETVAR") && !Keys(v18Functions).Contains("DT_CREATE")
                && !Keys(v18Functions).Contains("XML_DOCUMENT"),
                "v18 surface leaked v24-era functions.");
            Assert(Keys(v18Functions).Contains("ABS") && Keys(v18Functions).Contains("SQRT"),
                "v18 lost baseline expression functions.");

            Assert(erablueDeltaFunctions.Count == 0,
                $"erablue function delta must be empty, got: {string.Join(',', erablueDeltaFunctions)}.");
            Assert(erablueDeltaInstructions.SetEquals(new HashSet<string>(StringComparer.Ordinal) { "SETANIMETIMER" }),
                $"erablue instruction delta must be exactly SETANIMETIMER, got: {string.Join(',', erablueDeltaInstructions)}.");

            var functionReturnTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            CollectReturnTypes(v24Functions, functionReturnTypes);
            CollectReturnTypes(snakeFunctions, functionReturnTypes);
            CollectReturnTypes(eraflFunctions, functionReturnTypes);
            CollectReturnTypes(v18Functions, functionReturnTypes);
            CollectReturnTypes(erablueFunctions, functionReturnTypes);

            string generatedCs = BuildGeneratedCs(
                v24Instructions,
                snakeDeltaInstructions,
                eraflDeltaInstructions,
                v18Instructions,
                Keys(v24Functions),
                snakeDeltaFunctions,
                eraflDeltaFunctions,
                Keys(v18Functions),
                erablueDeltaInstructions,
                erablueDeltaFunctions,
                functionReturnTypes);
            string corePath = Path.Combine(repoRoot, "src", "Core", "Compatibility", "LegacyDialectInventories.Generated.cs");
            File.WriteAllText(corePath, generatedCs, new UTF8Encoding(false));

            string profilesJson = BuildProfilesJson(
                v24Instructions.Count, Keys(v24Functions).Count,
                snakeInstructions.Count, Keys(snakeFunctions).Count,
                eraflInstructions.Count, Keys(eraflFunctions).Count,
                v18Instructions.Count, Keys(v18Functions).Count,
                erablueInstructions.Count, Keys(erablueFunctions).Count);
            string runnerPath = Path.Combine(repoRoot, "tools", "legacy-runner", "profiles.generated.json");
            File.WriteAllText(runnerPath, profilesJson, new UTF8Encoding(false));

            Console.WriteLine($"v24: {v24Instructions.Count} instructions / {Keys(v24Functions).Count} functions");
            Console.WriteLine($"snake delta: {snakeDeltaInstructions.Count} instructions / {snakeDeltaFunctions.Count} functions");
            Console.WriteLine($"erafl delta: {eraflDeltaInstructions.Count} instructions / {eraflDeltaFunctions.Count} functions");
            Console.WriteLine($"written: {corePath}");
            Console.WriteLine($"written: {runnerPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }

    private static HashSet<string> Keys(object registry)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (object entry in (IEnumerable)registry)
        {
            PropertyInfo? key = entry.GetType().GetProperty("Key", BindingFlags.Instance | BindingFlags.Public);
            string value = key?.GetValue(entry) as string
                ?? throw new InvalidOperationException("Registry entry without a string key.");
            if (!keys.Add(value))
                throw new InvalidOperationException($"Duplicate registry key '{value}'.");
        }
        return keys;
    }

    private static void CollectReturnTypes(object registry, Dictionary<string, string> into)
    {
        PropertyInfo? returnProperty = null;
        foreach (object entry in (IEnumerable)registry)
        {
            PropertyInfo? value = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            object? method = value?.GetValue(entry);
            if (method is null)
                continue;
            PropertyInfo? key = entry.GetType().GetProperty("Key", BindingFlags.Instance | BindingFlags.Public);
            string? name = key?.GetValue(entry) as string;
            if (string.IsNullOrEmpty(name))
                continue;
            returnProperty ??= method.GetType().GetProperty("ReturnType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("FunctionMethod has no ReturnType property.");
            if (!into.TryGetValue(name!, out string? existing))
                into[name!] = returnProperty.GetValue(method)?.ToString() ?? "Unknown";
            else if (!string.Equals(existing, returnProperty.GetValue(method)?.ToString() ?? "Unknown", StringComparison.Ordinal))
                throw new InvalidOperationException($"Return type conflict for function '{name}'.");
        }
    }

        private static string BuildGeneratedCs(
            HashSet<string> v24Instructions,
            HashSet<string> snakeDeltaInstructions,
            HashSet<string> eraflDeltaInstructions,
            HashSet<string> v18Instructions,
            HashSet<string> v24Functions,
            HashSet<string> snakeDeltaFunctions,
            HashSet<string> eraflDeltaFunctions,
            HashSet<string> v18Functions,
            HashSet<string> erablueDeltaInstructions,
            HashSet<string> erablueDeltaFunctions,
            Dictionary<string, string> functionReturnTypes)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated>");
            builder.AppendLine("// 由 tools/dialect-inventory/LegacyDialectInventoryGenerator 从引擎真实投影生成。");
            builder.AppendLine("// 引擎注册表或方言桥层名单变化后必须重新生成：dotnet run --project tools/dialect-inventory/LegacyDialectInventoryGenerator -- <repo-root>");
            builder.AppendLine("// 手工修改会被下次生成覆盖。函数清单含 CJK 标识符（陥落状態/陷落状态），文件必须保持 UTF-8。");
            builder.AppendLine("// </auto-generated>");
            builder.AppendLine("#nullable enable");
            builder.AppendLine();
            builder.AppendLine("namespace GEmuera.Core.Compatibility;");
            builder.AppendLine();
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// 方言模块的自有指令/函数清单（描述符贡献数据）。v24 = v24pure 会话表面；");
            builder.AppendLine("/// snake/erafl 增量为对应会话表面相对 v24 表面的差集；v18 为独立基线的完整表面");
            builder.AppendLine("///（v24 引擎投影减 v24 后增差集）。单一事实源是引擎投影，本文件是生成镜像。");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("public static class LegacyDialectInventories");
            builder.AppendLine("{");
            AppendNameArray(builder, "V24InstructionNames", v24Instructions, V24ModuleId);
            AppendNameArray(builder, "SnakeDeltaInstructionNames", snakeDeltaInstructions, SnakeModuleId);
            AppendNameArray(builder, "EraFlDeltaInstructionNames", eraflDeltaInstructions, EraFlModuleId);
            AppendNameArray(builder, "V18InstructionNames", v18Instructions, V18ModuleId);
            AppendFunctionArray(builder, "V24Functions", v24Functions, functionReturnTypes, V24ModuleId);
            AppendFunctionArray(builder, "SnakeDeltaFunctions", snakeDeltaFunctions, functionReturnTypes, SnakeModuleId);
            AppendFunctionArray(builder, "EraFlDeltaFunctions", eraflDeltaFunctions, functionReturnTypes, EraFlModuleId);
            AppendFunctionArray(builder, "V18Functions", v18Functions, functionReturnTypes, V18ModuleId);
            AppendNameArray(builder, "EraBlueDeltaInstructionNames", erablueDeltaInstructions, EraBlueModuleId);
            AppendFunctionArray(builder, "EraBlueDeltaFunctions", erablueDeltaFunctions, functionReturnTypes, EraBlueModuleId);
            builder.AppendLine("}");
            builder.AppendLine();
            return builder.ToString();
        }

    private static void AppendNameArray(StringBuilder builder, string fieldName, HashSet<string> names, string moduleId)
    {
        builder.AppendLine($"    /// <summary>模块 {moduleId} 的自有指令名（Ordinal 排序）。</summary>");
        builder.AppendLine($"    public static readonly string[] {fieldName} =");
        builder.AppendLine("    {");
        var sorted = names.Order(StringComparer.Ordinal).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            string comma = i + 1 < sorted.Length ? "," : ",";
            builder.AppendLine($"        \"{sorted[i]}\"{comma}");
        }
        builder.AppendLine("    };");
        builder.AppendLine();
    }

    private static void AppendFunctionArray(
        StringBuilder builder,
        string fieldName,
        HashSet<string> names,
        Dictionary<string, string> returnTypes,
        string moduleId)
    {
        builder.AppendLine($"    /// <summary>模块 {moduleId} 的自有表达式函数（名字 + 引擎真实返回类型，Ordinal 排序）。</summary>");
        builder.AppendLine($"    public static readonly LegacyFunctionInventoryEntry[] {fieldName} =");
        builder.AppendLine("    {");
        var sorted = names.Order(StringComparer.Ordinal).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            string returnType = returnTypes.TryGetValue(sorted[i], out string? type) ? type : "Unknown";
            builder.AppendLine($"        new LegacyFunctionInventoryEntry(\"{sorted[i]}\", \"{returnType}\"),");
        }
        builder.AppendLine("    };");
        builder.AppendLine();
    }

        private static string BuildProfilesJson(
            int v24Instructions, int v24Functions,
            int snakeInstructions, int snakeFunctions,
            int eraflInstructions, int eraflFunctions,
            int v18Instructions, int v18Functions,
            int erablueInstructions, int erablueFunctions)
        {
            var root = new
            {
                schemaVersion = "1.0.0",
                generatedBy = "tools/dialect-inventory/LegacyDialectInventoryGenerator",
                profileIds = new[] { "erablue", "erafl", "snake", "v18", "v24pure" },
                profiles = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["v24pure"] = new { modules = new[] { V24ModuleId }, instructionCount = v24Instructions, functionCount = v24Functions },
                    ["snake"] = new { modules = new[] { V24ModuleId, SnakeModuleId }, instructionCount = snakeInstructions, functionCount = snakeFunctions },
                    ["erafl"] = new { modules = new[] { V24ModuleId, EraFlModuleId }, instructionCount = eraflInstructions, functionCount = eraflFunctions },
                    ["v18"] = new { modules = new[] { V18ModuleId }, instructionCount = v18Instructions, functionCount = v18Functions },
                    ["erablue"] = new { modules = new[] { V24ModuleId, EraBlueModuleId }, instructionCount = erablueInstructions, functionCount = erablueFunctions },
                },
            };
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(root, options) + "\n";
        }

    private static HashSet<string> ExceptSorted(HashSet<string> from, HashSet<string> subtract)
    {
        var result = new HashSet<string>(from, StringComparer.Ordinal);
        result.ExceptWith(subtract);
        return result;
    }

    private static void ForceIgnoreCaseVariable(Assembly legacyAssembly)
    {
        Type? configType = legacyAssembly.GetType("MinorShift.Emuera.Config");
        PropertyInfo? property = configType?.GetProperty("ICVariable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true)
            property.SetValue(null, true);
    }

    private static Type RequiredType(Assembly assembly, string fullName)
    {
        return assembly.GetType(fullName)
            ?? throw new InvalidOperationException($"Type '{fullName}' was not found in {assembly.GetName().Name}.");
    }

    private static object CreateProfile(Type profileType, string profileId, bool scopedVariableInstructionsEnabled)
    {
        MethodInfo? method = profileType.GetMethod(
            "CreateForProfile",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(bool) },
            modifiers: null);
        if (method is null)
            throw new InvalidOperationException("LegacyCompatibilityProfile.CreateForProfile was not found.");
        return method.Invoke(null, new object[] { profileId, scopedVariableInstructionsEnabled })
            ?? throw new InvalidOperationException("LegacyCompatibilityProfile.CreateForProfile returned null.");
    }

    private static HashSet<string> GetRegistryKeys(Type registryType, string methodName, Type profileType, object profile)
    {
        return Keys(GetRegistry(registryType, methodName, profileType, profile));
    }

    private static object GetRegistry(Type registryType, string methodName, Type profileType, object profile)
    {
        MethodInfo? method = registryType.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { profileType },
            modifiers: null);
        if (method is null)
            throw new InvalidOperationException($"{registryType.FullName}.{methodName} was not found.");
        return method.Invoke(null, new[] { profile })
            ?? throw new InvalidOperationException($"{registryType.FullName}.{methodName} returned null.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

internal static class DictionaryExtensions
{
    public static bool ContainsKeySafe(this object registry, string key)
    {
        foreach (object entry in (IEnumerable)registry)
        {
            PropertyInfo? keyProperty = entry.GetType().GetProperty("Key", BindingFlags.Instance | BindingFlags.Public);
            if (keyProperty?.GetValue(entry) as string == key)
                return true;
        }
        return false;
    }
}
