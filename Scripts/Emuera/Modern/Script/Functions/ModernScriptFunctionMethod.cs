using System;
using System.Collections.Generic;
using System.Globalization;
using MinorShift.Emuera.Modern.Script.Expressions;
using MinorShift.Emuera.Modern.Script.Statements;
using MinorShift.Emuera.Modern.Script.Variables;

namespace MinorShift.Emuera.Modern.Script.Functions;

internal sealed class ModernScriptFunctionMethod : ModernFunctionMethod
{
	readonly EraType returnType;
	ModernBlockStatement body;
	readonly ModernVariableEvaluator variableEvaluator;
	readonly ModernVariableSizing sizing;
	readonly int localIntegerLength;
	readonly int localStringLength;
	readonly int localFloatLength;
	readonly int argIntegerLength;
	readonly int argStringLength;
	readonly int argFloatLength;
	readonly List<ModernUserVariableDefinition> dynamicPrivateDefinitions;
	readonly ModernPrivateVariableStore staticPrivateStore;
	readonly List<ModernFunctionArgumentBinding> argumentBindings;

	public ModernScriptFunctionMethod(
		string name,
		EraType returnType,
		ModernBlockStatement body,
		ModernVariableEvaluator variableEvaluator,
		int minArgumentCount = 0,
		int maxArgumentCount = int.MaxValue,
		ModernVariableSizing sizing = null,
		int localIntegerLength = 0,
		int localStringLength = 0,
		int localFloatLength = 0,
		int argIntegerLength = 0,
		int argStringLength = 0,
		int argFloatLength = 0,
		IEnumerable<ModernUserVariableDefinition> privateVariableDefinitions = null,
		IEnumerable<ModernFunctionArgumentBinding> argumentBindings = null)
		: base(name, minArgumentCount, maxArgumentCount)
	{
		this.returnType = returnType == EraType.Void ? EraType.Integer : returnType;
		this.body = body ?? new ModernBlockStatement(Array.Empty<ModernStatement>());
		this.variableEvaluator = variableEvaluator ?? throw new ArgumentNullException(nameof(variableEvaluator));
		this.sizing = sizing ?? ModernVariableSizing.Default;
		this.localIntegerLength = localIntegerLength;
		this.localStringLength = localStringLength;
		this.localFloatLength = localFloatLength;
		this.argIntegerLength = argIntegerLength;
		this.argStringLength = argStringLength;
		this.argFloatLength = argFloatLength;
		var staticDefinitions = new List<ModernUserVariableDefinition>();
		dynamicPrivateDefinitions = new List<ModernUserVariableDefinition>();
		if (privateVariableDefinitions != null)
		{
			foreach (var definition in privateVariableDefinitions)
			{
				if (IsDynamicPrivate(definition))
					dynamicPrivateDefinitions.Add(definition);
				else
					staticDefinitions.Add(definition);
			}
		}
		staticPrivateStore = new ModernPrivateVariableStore(staticDefinitions);
		this.argumentBindings = argumentBindings == null
			? new List<ModernFunctionArgumentBinding>()
			: new List<ModernFunctionArgumentBinding>(argumentBindings);
	}

	public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
	{
		return returnType;
	}

	public void SetBody(ModernBlockStatement body)
	{
		this.body = body ?? throw new ArgumentNullException(nameof(body));
	}

	public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
	{
		var value = Invoke(context, arguments);
		return value is SingleFloatTerm floatTerm ? (long)floatTerm.Float : value.GetIntValue(context);
	}

	public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
	{
		return Invoke(context, arguments).GetStrValue(context) ?? "";
	}

	public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
	{
		var value = Invoke(context, arguments);
		return value switch
		{
			SingleFloatTerm floatTerm => floatTerm.Float,
			SingleLongTerm longTerm => longTerm.Int,
			_ => value.GetFloatValue(context),
		};
	}

	public SingleTerm Invoke(ModernExpressionContext callerContext, IReadOnlyList<AExpression> arguments)
	{
		Validate(arguments);
		var executionContext = CreateExecutionContext(callerContext, arguments);
		var functionContext = new ModernExpressionContext(variableEvaluator, executionContext);
		functionContext.PrivateVariables = CreatePrivateVariableStore();
		BindArguments(functionContext, callerContext, arguments);
		try
		{
			body.Execute(functionContext);
		}
		catch (ModernReturnException ex)
		{
			return CoerceReturnValue(ex.Value, functionContext);
		}

		return DefaultReturnValue();
	}

	protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
	{
		if (argumentBindings.Count == 0)
			return;

		bool hasVariadic = false;
		for (int i = 0; i < argumentBindings.Count; i++)
		{
			var binding = argumentBindings[i];
			if (!binding.IsVariadic)
				continue;
			if (hasVariadic)
				throw new FormatException($"{Name} has more than one VARIADIC argument.");
			if (i != argumentBindings.Count - 1)
				throw new FormatException($"{Name} has VARIADIC before the final argument.");
			hasVariadic = true;
		}

		if (!hasVariadic && arguments.Count > argumentBindings.Count)
			throw new FormatException($"{Name} expects at most {argumentBindings.Count} arguments, got {arguments.Count}.");
	}

	ModernPrivateVariableStore CreatePrivateVariableStore()
	{
		if (dynamicPrivateDefinitions.Count == 0)
			return staticPrivateStore;
		return new ModernPrivateVariableStore(dynamicPrivateDefinitions, staticPrivateStore);
	}

	static bool IsDynamicPrivate(ModernUserVariableDefinition definition)
	{
		var attributes = definition.Attributes;
		return (attributes & ModernUserVariableAttributes.Dynamic) != 0
			|| (attributes & ModernUserVariableAttributes.Reference) != 0
			|| (attributes & ModernUserVariableAttributes.Out) != 0;
	}

	ExecutionContext CreateExecutionContext(ModernExpressionContext callerContext, IReadOnlyList<AExpression> arguments)
	{
		int argLength = Math.Max(arguments.Count, 1);
		int requiredArgIntegers = argLength;
		int requiredArgStrings = argLength;
		int requiredArgFloats = argLength;
		ApplyDeclaredArgumentLengths(arguments.Count, ref requiredArgIntegers, ref requiredArgStrings, ref requiredArgFloats);
		var parent = callerContext?.ExecutionContext;
		var executionContext = new ExecutionContext(
			parent,
			ResolveLength(localIntegerLength, sizing.DefaultArrayLength),
			ResolveLength(localStringLength, sizing.DefaultStringArrayLength),
			ResolveLength(localFloatLength, sizing.DefaultFloatArrayLength),
			Math.Max(ResolveLength(argIntegerLength, sizing.DefaultArrayLength), requiredArgIntegers),
			Math.Max(ResolveLength(argStringLength, sizing.DefaultStringArrayLength), requiredArgStrings),
			Math.Max(ResolveLength(argFloatLength, sizing.DefaultFloatArrayLength), requiredArgFloats),
			Name);
		return executionContext;
	}

	void ApplyDeclaredArgumentLengths(
		int argumentCount,
		ref int requiredArgIntegers,
		ref int requiredArgStrings,
		ref int requiredArgFloats)
	{
		for (int i = 0; i < argumentBindings.Count; i++)
		{
			var binding = argumentBindings[i];
			if (!binding.IsArgArray)
				continue;
			long baseIndex = binding.FirstIndex;
			if (baseIndex < 0 || baseIndex > int.MaxValue)
				throw new IndexOutOfRangeException($"{Name} argument index is outside Int32 range.");
			int count = binding.IsVariadic ? Math.Max(argumentCount - i, 0) : 1;
			int required = checked((int)baseIndex + Math.Max(count, 1));
			if (binding.VariableCode == VariableCode.ARG)
				requiredArgIntegers = Math.Max(requiredArgIntegers, required);
			else if (binding.VariableCode == VariableCode.ARGS)
				requiredArgStrings = Math.Max(requiredArgStrings, required);
			else if (binding.VariableCode == VariableCode.ARGF)
				requiredArgFloats = Math.Max(requiredArgFloats, required);
		}
	}

	void BindArguments(
		ModernExpressionContext functionContext,
		ModernExpressionContext callerContext,
		IReadOnlyList<AExpression> arguments)
	{
		if (argumentBindings.Count == 0)
		{
			for (int i = 0; i < arguments.Count; i++)
				BindImplicitArgument(functionContext.ExecutionContext, i, arguments[i], callerContext);
			return;
		}

		for (int i = 0; i < argumentBindings.Count; i++)
		{
			var binding = argumentBindings[i];
			if (binding.IsVariadic)
			{
				BindVariadicArguments(functionContext, callerContext, arguments, i, binding);
				return;
			}

			AExpression source = i < arguments.Count ? arguments[i] : binding.DefaultValue ?? DefaultValue(binding.EraType);
			AssignArgument(binding.Destination, source, functionContext, callerContext);
		}
	}

	void BindVariadicArguments(
		ModernExpressionContext functionContext,
		ModernExpressionContext callerContext,
		IReadOnlyList<AExpression> arguments,
		int firstArgumentIndex,
		ModernFunctionArgumentBinding binding)
	{
		if (!binding.IsArgArray)
			throw new FormatException($"{Name} VARIADIC can only target ARG, ARGS, or ARGF.");

		long baseIndex = binding.FirstIndex;
		int count = Math.Max(arguments.Count - firstArgumentIndex, 0);
		functionContext.ExecutionContext.CurrentVariadicArgCount = count;
		for (int i = 0; i < count; i++)
		{
			var destination = binding.CreateIndexedDestination(variableEvaluator, baseIndex + i);
			AssignArgument(destination, arguments[firstArgumentIndex + i], functionContext, callerContext);
		}
	}

	static void AssignArgument(
		ModernVariableTerm destination,
		AExpression source,
		ModernExpressionContext functionContext,
		ModernExpressionContext callerContext)
	{
		if (destination.Identifier.IsReference)
		{
			BindReferenceArgument(destination, source, functionContext, callerContext);
			return;
		}

		switch (destination.Identifier.GetEraType())
		{
			case EraType.Integer:
				if (source.IsFloat)
					destination.SetValue((long)source.GetFloatValue(callerContext), functionContext);
				else
					destination.SetValue(source.GetIntValue(callerContext), functionContext);
				break;
			case EraType.Float:
				if (source.IsInteger)
					destination.SetValue((double)source.GetIntValue(callerContext), functionContext);
				else
					destination.SetValue(source.GetFloatValue(callerContext), functionContext);
				break;
			case EraType.String:
				destination.SetValue(source.GetStrValue(callerContext), functionContext);
				break;
			default:
				throw new InvalidOperationException($"{destination.Identifier.Name} has unsupported argument type.");
		}
	}

	static void BindReferenceArgument(
		ModernVariableTerm destination,
		AExpression source,
		ModernExpressionContext functionContext,
		ModernExpressionContext callerContext)
	{
		if (source is not ModernVariableTerm sourceVariable)
		{
			if (destination.Identifier.IsOut && source is SingleTerm)
				return;
			throw new InvalidOperationException($"{destination.Identifier.Name} needs a variable reference argument.");
		}

		if (sourceVariable.Identifier.GetEraType() != destination.Identifier.GetEraType())
			throw new InvalidOperationException($"{destination.Identifier.Name} reference type does not match {sourceVariable.Identifier.Name}.");

		functionContext.PrivateVariables.SetReference(
			destination.Identifier.Name,
			sourceVariable.CaptureReference(callerContext));
	}

	static AExpression DefaultValue(EraType eraType)
	{
		return eraType switch
		{
			EraType.Float => new SingleFloatTerm(0.0),
			EraType.String => new SingleStrTerm(""),
			_ => new SingleLongTerm(0),
		};
	}

	static int ResolveLength(int declaredLength, int defaultLength)
	{
		return declaredLength > 0 ? declaredLength : defaultLength;
	}

	static void BindImplicitArgument(ExecutionContext executionContext, int index, AExpression expression, ModernExpressionContext callerContext)
	{
		if (expression.IsString)
		{
			string value = expression.GetStrValue(callerContext) ?? "";
			executionContext.ArgStrings[index] = value;
			if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
				executionContext.ArgIntegers[index] = intValue;
			if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
				executionContext.ArgFloats[index] = floatValue;
			return;
		}

		if (expression.IsFloat)
		{
			double value = expression.GetFloatValue(callerContext);
			executionContext.ArgFloats[index] = value;
			executionContext.ArgIntegers[index] = (long)value;
			executionContext.ArgStrings[index] = value.ToString(CultureInfo.InvariantCulture);
			return;
		}

		long longValue = expression.GetIntValue(callerContext);
		executionContext.ArgIntegers[index] = longValue;
		executionContext.ArgFloats[index] = longValue;
		executionContext.ArgStrings[index] = longValue.ToString(CultureInfo.InvariantCulture);
	}

	SingleTerm CoerceReturnValue(SingleTerm value, ModernExpressionContext context)
	{
		return returnType switch
		{
			EraType.Float => value switch
			{
				SingleFloatTerm => value,
				SingleLongTerm longTerm => new SingleFloatTerm(longTerm.Int),
				_ => new SingleFloatTerm(value.GetFloatValue(context)),
			},
			EraType.String => value is SingleStrTerm ? value : new SingleStrTerm(value.GetStrValue(context)),
			_ => value switch
			{
				SingleLongTerm => value,
				SingleFloatTerm floatTerm => new SingleLongTerm((long)floatTerm.Float),
				_ => new SingleLongTerm(value.GetIntValue(context)),
			},
		};
	}

	SingleTerm DefaultReturnValue()
	{
		return returnType switch
		{
			EraType.Float => new SingleFloatTerm(0.0),
			EraType.String => new SingleStrTerm(""),
			_ => new SingleLongTerm(0),
		};
	}
}
