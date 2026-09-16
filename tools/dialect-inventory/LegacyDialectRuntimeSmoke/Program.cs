using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class Program
{
    private static int Main()
    {
        try
        {
            Assembly legacyAssembly = typeof(EmueraContent).Assembly;
            // eraTW games default to IGNORE CASE:YES; the legacy parser must keep
            // resolving mixed-case instruction spellings such as PRINTFORMw/TryCall.
            ForceIgnoreCaseVariable(legacyAssembly);
            Type profileType = RequiredType(legacyAssembly, "MinorShift.Emuera.Compatibility.LegacyCompatibilityProfile");
            object v24 = CreateProfile(profileType, "v24pure", scopedVariableInstructionsEnabled: true);
            object v24WithoutScopedVariables = CreateProfile(profileType, "v24pure", scopedVariableInstructionsEnabled: false);
            object snake = CreateProfile(profileType, "snake", scopedVariableInstructionsEnabled: true);

            Type instructionType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameProc.Function.FunctionIdentifier");
            Type functionType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameData.Function.FunctionMethodCreator");
            var v24Instructions = GetRegistryKeys(instructionType, "GetInstructionNameDic", profileType, v24);
            var v24InstructionsWithoutScopedVariables = GetRegistryKeys(instructionType, "GetInstructionNameDic", profileType, v24WithoutScopedVariables);
            var snakeInstructions = GetRegistryKeys(instructionType, "GetInstructionNameDic", profileType, snake);
            var v24Functions = GetRegistryKeys(functionType, "GetMethodList", profileType, v24);
            var snakeFunctions = GetRegistryKeys(functionType, "GetMethodList", profileType, snake);

            Console.WriteLine($"v24MergedInstructions={v24Instructions.Count}; snakeMergedInstructions={snakeInstructions.Count}; v24Functions={v24Functions.Count}; snakeFunctions={snakeFunctions.Count}");

            // 注册表面快照：365 个总注册中，v24pure 隐藏 99 个 snake 扩展 → 266 可见；
            // snake 会话再隐藏 16 个（SnakeExcludedFunctionNames）→ 349 可见。
            // 计数随 Creator.cs 注册表变化需同步更新（reflection 快照，非语义断言）。
            Assert(v24Functions.Count == 266, $"Unexpected v24 expression function count: {v24Functions.Count}.");
            Assert(snakeFunctions.Count == 349, $"Unexpected Snake expression function count: {snakeFunctions.Count}.");

            Assert(v24Instructions.Contains("CALLSHARP"), "v24 lost a baseline instruction.");
            Assert(!v24Instructions.Contains("CALLSTR"), "Snake instruction CALLSTR leaked into v24.");
            Assert(snakeInstructions.Contains("CALLSTR"), "Snake instruction CALLSTR is unavailable.");
            Assert(!v24Instructions.Contains("OUTPUTLOG") && !snakeInstructions.Contains("OUTPUTLOG"), "Port-only OUTPUTLOG instruction leaked into a dialect surface.");
            Assert(v24Instructions.Contains("VARI") && v24Instructions.Contains("VARS"), "Enabled scoped variables are unavailable in v24.");
            Assert(!v24InstructionsWithoutScopedVariables.Contains("VARI") && !v24InstructionsWithoutScopedVariables.Contains("VARS"), "Disabled scoped variables leaked into v24.");
            AssertInstructionArgumentBuilder(instructionType, profileType, v24, "SETBGIMAGE", "FORM_STR_ANY_ArgumentBuilder");
            AssertInstructionArgumentBuilder(instructionType, profileType, snake, "SETBGIMAGE", "SP_SETBGIMAGE_ArgumentBuilder");

			Assert(!v24Functions.Contains("ACOS") && snakeFunctions.Contains("ACOS"), "Snake-only expression ACOS has incorrect visibility.");
			Assert(!v24Functions.Contains("GROTATE") && !snakeFunctions.Contains("GROTATE"), "Port-only expression GROTATE leaked into an upstream dialect.");
			Assert(!snakeFunctions.Contains("GETARGCOUNT"), "Snake exposed GETARGCOUNT although Snake upstream does not register it.");
			Assert(v24Functions.Contains("BITMAP_CACHE_ENABLE") && !snakeFunctions.Contains("BITMAP_CACHE_ENABLE"), "v24-only BITMAP_CACHE_ENABLE has incorrect visibility.");
			AssertMethodInstructionProjection(profileType, instructionType, v24, "BITMAP_CACHE_ENABLE", expected: true);
			AssertMixedCaseInstructionLookup(legacyAssembly, instructionType, profileType, snake);

            Assert(!v24Instructions.Contains("ACOS") && snakeInstructions.Contains("ACOS"), "Expression function ACOS leaked through the METHOD instruction path.");
            Assert(!v24Instructions.Contains("GROTATE") && !snakeInstructions.Contains("GROTATE"), "Port-only expression GROTATE leaked through the METHOD instruction path.");
            Assert(!v24Instructions.Contains("GETARGCOUNT") && !snakeInstructions.Contains("GETARGCOUNT"), "Current-only GETARGCOUNT leaked through the METHOD instruction path.");
            Assert(v24Instructions.Contains("BITMAP_CACHE_ENABLE") && snakeInstructions.Contains("BITMAP_CACHE_ENABLE"), "BITMAP_CACHE_ENABLE has the wrong v24/Snake instruction visibility.");

            AssertParserInstruction(legacyAssembly, instructionType, profileType, v24, "CALLSHARP", expected: true);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, v24, "BITMAP_CACHE_ENABLE", expected: true);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, v24, "CALLSTR", expected: false);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, v24, "TINPUTNF", expected: false);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, v24, "SETIMAGELAYER", expected: false);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, v24, "ACOS", expected: false);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, v24, "SQL_CONNECT", expected: false);

            AssertParserInstruction(legacyAssembly, instructionType, profileType, snake, "CALLSTR", expected: true);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, snake, "BITMAP_CACHE_ENABLE", expected: true);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, snake, "TINPUTNF", expected: true);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, snake, "SETIMAGELAYER", expected: true);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, snake, "ACOS", expected: true);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, snake, "SQL_CONNECT", expected: true);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, snake, "GETARGCOUNT", expected: false);
            AssertParserInstruction(legacyAssembly, instructionType, profileType, snake, "GROTATE", expected: false);

            AssertV24ScopedVariableParser(legacyAssembly, instructionType, profileType, v24);
            AssertPluginFloatParameter(legacyAssembly);

            Console.WriteLine("Legacy dialect runtime lookup smoke passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static Type RequiredType(Assembly assembly, string fullName) =>
        assembly.GetType(fullName, throwOnError: false) ?? throw new InvalidOperationException($"Missing legacy type: {fullName}.");

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
        object registry = GetRegistry(registryType, methodName, profileType, profile);
        if (registry is not IEnumerable entries)
            throw new InvalidOperationException($"{registryType.FullName}.{methodName} did not return an enumerable registry.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (object entry in entries)
        {
            PropertyInfo? key = entry.GetType().GetProperty("Key", BindingFlags.Instance | BindingFlags.Public);
            string value = key?.GetValue(entry) as string
                ?? throw new InvalidOperationException($"{registryType.FullName}.{methodName} yielded a registry entry without a string key.");
            if (!keys.Add(value))
                throw new InvalidOperationException($"{registryType.FullName}.{methodName} yielded duplicate key '{value}'.");
        }
        return keys;
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

	private static void AssertInstructionArgumentBuilder(
        Type instructionType,
        Type profileType,
        object profile,
        string instructionName,
        string expectedBuilderName)
    {
        object registry = GetRegistry(instructionType, "GetInstructionNameDic", profileType, profile);
        Type registryInterface = typeof(IReadOnlyDictionary<,>).MakeGenericType(typeof(string), instructionType);
        object? identifier = registryInterface.GetMethod("get_Item")?.Invoke(registry, new object[] { instructionName });
        if (identifier is null)
            throw new InvalidOperationException($"Instruction '{instructionName}' is absent from profile '{profile.GetType().GetProperty("ProfileId")?.GetValue(profile)}'.");

        PropertyInfo? argumentBuilder = instructionType.GetProperty("ArgBuilder", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        string actualBuilderName = argumentBuilder?.GetValue(identifier)?.GetType().Name
            ?? throw new InvalidOperationException($"Instruction '{instructionName}' has no argument builder.");
        Assert(string.Equals(actualBuilderName, expectedBuilderName, StringComparison.Ordinal),
            $"Profile '{profile.GetType().GetProperty("ProfileId")?.GetValue(profile)}' exposes {instructionName} with {actualBuilderName}, expected {expectedBuilderName}.");
	}

	private static void AssertMethodInstructionProjection(
		Type profileType,
		Type instructionType,
		object profile,
		string functionName,
		bool expected)
	{
		MethodInfo? shouldProject = profileType.GetMethod(
			"ShouldProjectExpressionFunctionAsInstruction",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			binder: null,
			types: new[] { typeof(string) },
			modifiers: null);
		if (shouldProject is null)
			throw new InvalidOperationException("LegacyCompatibilityProfile.ShouldProjectExpressionFunctionAsInstruction was not found.");
		bool isDeclared = shouldProject.Invoke(profile, new object[] { functionName }) as bool?
			?? throw new InvalidOperationException($"Method projection declaration for '{functionName}' returned null.");
		Assert(isDeclared == expected,
			$"Method projection declaration for '{functionName}' was {isDeclared}, expected {expected}.");

		FieldInfo? handlers = instructionType.GetField("funcDic", BindingFlags.Static | BindingFlags.NonPublic);
		if (handlers?.GetValue(null) is not IDictionary handlerRegistry
			|| !handlerRegistry.Contains(functionName))
		{
			throw new InvalidOperationException($"Legacy instruction handler '{functionName}' was not registered.");
		}

		object handler = handlerRegistry[functionName]
			?? throw new InvalidOperationException($"Legacy instruction handler '{functionName}' is null.");
		PropertyInfo? methodProperty = instructionType.GetProperty("Method", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (methodProperty?.GetValue(handler) is not null)
			throw new InvalidOperationException($"Legacy handler '{functionName}' must be an instruction collision, not an existing METHOD projection.");
	}

    private static void AssertParserInstruction(
        Assembly legacyAssembly,
        Type instructionType,
        Type profileType,
        object profile,
        string source,
        bool expected)
    {
        Type dictionaryType = RequiredType(legacyAssembly, "MinorShift.Emuera.IdentifierDictionary");
        FieldInfo instructionDictionary = dictionaryType.GetField("instructionDic", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("IdentifierDictionary.instructionDic was not found.");
        object dictionary = RuntimeHelpers.GetUninitializedObject(dictionaryType);
        instructionDictionary.SetValue(dictionary, GetRegistry(instructionType, "GetInstructionNameDic", profileType, profile));

        Type globalsType = RequiredType(legacyAssembly, "MinorShift.Emuera.GlobalStatic");
        FieldInfo globalsDictionary = globalsType.GetField("IdentifierDictionary", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("GlobalStatic.IdentifierDictionary was not found.");
        object? previous = globalsDictionary.GetValue(null);
        try
        {
            globalsDictionary.SetValue(null, dictionary);
            Type parserType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameProc.LogicalLineParser");
            Type consoleType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameView.EmueraConsole");
            MethodInfo? parseLine = parserType.GetMethod(
                "ParseLine",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(string), consoleType },
                modifiers: null);
            if (parseLine is null)
                throw new InvalidOperationException("LogicalLineParser.ParseLine(string, EmueraConsole) was not found.");

            object? line = parseLine.Invoke(null, new object?[] { source, null });
            bool isInstruction = string.Equals(
                line?.GetType().FullName,
                "MinorShift.Emuera.GameProc.InstructionLine",
                StringComparison.Ordinal);
            Assert(isInstruction == expected, $"Parser profile '{profile.GetType().GetProperty("ProfileId")?.GetValue(profile)}' parsed '{source}' as instruction={isInstruction}, expected {expected}.");
        }
        finally
        {
            globalsDictionary.SetValue(null, previous);
        }
    }

    private static void AssertPluginFloatParameter(Assembly legacyAssembly)
    {
        Type termType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameData.Expression.SingleTerm");
        object term = Activator.CreateInstance(
            termType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { 1.25d },
            culture: null)
            ?? throw new InvalidOperationException("SingleTerm(double) returned null.");

        Type operandType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameData.Expression.IOperandTerm");
        Type mediatorType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameData.Expression.ExpressionMediator");
        Type builderType = RequiredType(legacyAssembly, "MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginMethodParameterBuilder");
        MethodInfo? convertTerm = builderType.GetMethod(
            "ConvertTerm",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { operandType, mediatorType },
            modifiers: null);
        if (convertTerm is null)
            throw new InvalidOperationException("PluginMethodParameterBuilder.ConvertTerm was not found.");

        object parameter = convertTerm.Invoke(null, new object?[] { term, null })
            ?? throw new InvalidOperationException("PluginMethodParameterBuilder.ConvertTerm returned null.");
        Type parameterType = RequiredType(legacyAssembly, "MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginMethodParameter");
        bool isFloat = (bool)(parameterType.GetField("isFloat", BindingFlags.Instance | BindingFlags.Public)?.GetValue(parameter)
            ?? throw new InvalidOperationException("PluginMethodParameter.isFloat was not found."));
        bool isString = (bool)(parameterType.GetField("isString", BindingFlags.Instance | BindingFlags.Public)?.GetValue(parameter)
            ?? throw new InvalidOperationException("PluginMethodParameter.isString was not found."));
        double value = (double)(parameterType.GetField("floatValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(parameter)
            ?? throw new InvalidOperationException("PluginMethodParameter.floatValue was not found."));
        Assert(isFloat && !isString && value == 1.25d, "Plugin float parameters did not preserve their type and value.");
    }

    private static void AssertV24ScopedVariableParser(
        Assembly legacyAssembly,
        Type instructionType,
        Type profileType,
        object profile)
    {
        Type globalsType = RequiredType(legacyAssembly, "MinorShift.Emuera.GlobalStatic");
        Type programType = RequiredType(legacyAssembly, "MinorShift.Emuera.Program");
        Type dictionaryType = RequiredType(legacyAssembly, "MinorShift.Emuera.IdentifierDictionary");
        Type variableDataType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameData.Variable.VariableData");
        Type processType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameProc.Process");
        Type parserType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameProc.LogicalLineParser");
        Type streamType = RequiredType(legacyAssembly, "MinorShift.Emuera.Sub.StringStream");
        Type positionType = RequiredType(legacyAssembly, "MinorShift.Emuera.Sub.ScriptPosition");
        Type labelType = RequiredType(legacyAssembly, "MinorShift.Emuera.GameProc.FunctionLabelLine");
        Type wordsType = RequiredType(legacyAssembly, "MinorShift.Emuera.Sub.WordCollection");

        FieldInfo instructionDictionary = dictionaryType.GetField("instructionDic", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("IdentifierDictionary.instructionDic was not found.");
        FieldInfo globalsDictionary = globalsType.GetField("IdentifierDictionary", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("GlobalStatic.IdentifierDictionary was not found.");
        FieldInfo globalsVariables = globalsType.GetField("VariableData", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("GlobalStatic.VariableData was not found.");
        FieldInfo globalsProcess = globalsType.GetField("Process", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("GlobalStatic.Process was not found.");
        FieldInfo compatibility = programType.GetField("m1CompatibilityProfile", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Program.m1CompatibilityProfile was not found.");
        MethodInfo? parseLine = parserType.GetMethod(
            "ParseLine",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            types: new[] { streamType, positionType, RequiredType(legacyAssembly, "MinorShift.Emuera.GameView.EmueraConsole"), labelType },
            modifiers: null);
        if (parseLine is null)
            throw new InvalidOperationException("LogicalLineParser.ParseLine(StringStream, ScriptPosition, EmueraConsole, FunctionLabelLine) was not found.");
        MethodInfo getPrivateVariable = labelType.GetMethod("GetPrivateVariable", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FunctionLabelLine.GetPrivateVariable was not found.");

        object dictionary = RuntimeHelpers.GetUninitializedObject(dictionaryType);
        instructionDictionary.SetValue(dictionary, GetRegistry(instructionType, "GetInstructionNameDic", profileType, profile));
        object? previousDictionary = globalsDictionary.GetValue(null);
        object? previousVariables = globalsVariables.GetValue(null);
        object? previousProcess = globalsProcess.GetValue(null);
        object? previousCompatibility = compatibility.GetValue(null);
        try
        {
            globalsDictionary.SetValue(null, dictionary);
            // Private dynamic variable construction does not require VariableData's
            // backing arrays, so an uninitialized instance keeps this parser smoke
            // focused and independent from game CSV initialization.
            globalsVariables.SetValue(null, RuntimeHelpers.GetUninitializedObject(variableDataType));
            globalsProcess.SetValue(null, RuntimeHelpers.GetUninitializedObject(processType));
            compatibility.SetValue(null, profile);

            AssertScopedVariable("VARI SCORE = 42", "SCORE", expectedString: false, expectedLengths: new[] { 1 }, expectedInitialValue: "42");
            AssertScopedVariable("VARS NAME = \"Ada\"", "NAME", expectedString: true, expectedLengths: new[] { 1 }, expectedInitialValue: "Ada");
            AssertScopedVariable("VARI GRID, 2, 3", "GRID", expectedString: false, expectedLengths: new[] { 2, 3 }, expectedInitialValue: "0");
        }
        finally
        {
            compatibility.SetValue(null, previousCompatibility);
            globalsProcess.SetValue(null, previousProcess);
            globalsVariables.SetValue(null, previousVariables);
            globalsDictionary.SetValue(null, previousDictionary);
        }

        void AssertScopedVariable(string source, string name, bool expectedString, int[] expectedLengths, string expectedInitialValue)
        {
            object label = Activator.CreateInstance(
                labelType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new[]
                {
                    Activator.CreateInstance(positionType)!,
                    "SMOKE",
                    null,
                },
                culture: null)
                ?? throw new InvalidOperationException("FunctionLabelLine construction failed.");
            object stream = Activator.CreateInstance(streamType, source)
                ?? throw new InvalidOperationException("StringStream construction failed.");
            object? line = parseLine.Invoke(null, new[] { stream, Activator.CreateInstance(positionType), null, label });
            Assert(line?.GetType().FullName == "MinorShift.Emuera.GameProc.InstructionLine", $"v24 did not parse '{source}' as an instruction.");

            object privateVariable = getPrivateVariable.Invoke(label, new object[] { name })
                ?? throw new InvalidOperationException($"v24 did not register private variable '{name}'.");
            PropertyInfo isString = privateVariable.GetType().GetProperty("IsString", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("UserDefinedVariableToken.IsString was not found.");
            if (isString.GetValue(privateVariable) is not bool actualString)
                throw new InvalidOperationException("UserDefinedVariableToken.IsString did not return a Boolean value.");
            Assert(actualString == expectedString, $"v24 private variable '{name}' has the wrong string type.");
            MethodInfo getLength = privateVariable.GetType().GetMethod("GetLength", BindingFlags.Instance | BindingFlags.Public, binder: null, types: new[] { typeof(int) }, modifiers: null)
                ?? throw new InvalidOperationException("UserDefinedVariableToken.GetLength(int) was not found.");
            for (int index = 0; index < expectedLengths.Length; index++)
            {
                if (getLength.Invoke(privateVariable, new object[] { index }) is not int actualLength)
                    throw new InvalidOperationException($"v24 private variable '{name}' did not return a length at dimension {index}.");
                Assert(actualLength == expectedLengths[index], $"v24 private variable '{name}' has the wrong length at dimension {index}.");
            }

            PropertyInfo argument = line!.GetType().GetProperty("Argument", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("InstructionLine.Argument was not found.");
            object parsedArgument = argument.GetValue(line)
                ?? throw new InvalidOperationException($"v24 '{source}' did not create an instruction argument.");
            string expectedArgumentType = expectedString ? "SnakeVarsArgument" : "SnakeVariArgument";
            Assert(parsedArgument.GetType().Name == expectedArgumentType, $"v24 '{source}' created '{parsedArgument.GetType().Name}', expected '{expectedArgumentType}'.");
            FieldInfo initialValue = parsedArgument.GetType().GetField("InitialValue", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException($"{expectedArgumentType}.InitialValue was not found.");
            object? value = initialValue.GetValue(parsedArgument);
            string actual;
            if (expectedString)
            {
                actual = value as string ?? string.Empty;
            }
            else
            {
                object numericValue = value
                    ?? throw new InvalidOperationException($"v24 '{source}' did not create an integer initial value.");
                MethodInfo getIntValue = numericValue.GetType().GetMethod("GetIntValue", BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new InvalidOperationException($"v24 '{source}' initial value has no GetIntValue method.");
                object result = getIntValue.Invoke(numericValue, new object?[] { null })
                    ?? throw new InvalidOperationException($"v24 '{source}' initial value did not return an integer.");
                actual = Convert.ToInt64(result).ToString();
            }
            Assert(string.Equals(actual, expectedInitialValue, StringComparison.Ordinal), $"v24 '{source}' produced initial value '{actual}', expected '{expectedInitialValue}'.");
        }
    }

    private static void ForceIgnoreCaseVariable(Assembly legacyAssembly)
    {
        Type configType = RequiredType(legacyAssembly, "MinorShift.Emuera.Config");
        PropertyInfo icVariable = configType.GetProperty("ICVariable", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("Config.ICVariable was not found.");
        icVariable.SetValue(null, true);
    }

    private static void AssertMixedCaseInstructionLookup(
        Assembly legacyAssembly,
        Type instructionType,
        Type profileType,
        object profile)
    {
        if (GetRegistry(instructionType, "GetInstructionNameDic", profileType, profile) is not System.Collections.IDictionary registry)
            throw new InvalidOperationException("Instruction surface is not a dictionary.");
        Assert(registry.Contains("PRINTFORMW"), "PRINTFORMW is not registered.");
        Assert(registry.Contains("PRINTFORMw"), "Mixed-case PRINTFORMw must resolve under IGNORE CASE:YES.");
        Assert(registry.Contains("TryCall"), "Mixed-case TryCall must resolve under IGNORE CASE:YES.");
        Assert(registry.Contains("CASe"), "Mixed-case CASe must resolve under IGNORE CASE:YES.");
        Assert(registry.Contains("ENDSELECt"), "Mixed-case ENDSELECt must resolve under IGNORE CASE:YES.");
        Assert(registry.Contains("call"), "Mixed-case call must resolve under IGNORE CASE:YES.");
        Assert(registry.Contains("CALL"), "Uppercase CALL is not registered.");
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
