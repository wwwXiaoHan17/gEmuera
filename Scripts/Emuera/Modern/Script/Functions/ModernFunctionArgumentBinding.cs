using System;
using MinorShift.Emuera.Modern.Script.Expressions;
using MinorShift.Emuera.Modern.Script.Variables;

namespace MinorShift.Emuera.Modern.Script.Functions;

internal sealed class ModernFunctionArgumentBinding
{
	public ModernFunctionArgumentBinding(ModernVariableTerm destination, SingleTerm defaultValue, bool isVariadic)
	{
		Destination = destination ?? throw new ArgumentNullException(nameof(destination));
		DefaultValue = defaultValue;
		IsVariadic = isVariadic;
	}

	public ModernVariableTerm Destination { get; }
	public SingleTerm DefaultValue { get; }
	public bool IsVariadic { get; }
	public EraType EraType { get { return Destination.Identifier.GetEraType(); } }

	public VariableCode VariableCode { get { return Destination.Identifier.Code; } }

	public bool IsArgArray
	{
		get
		{
			return VariableCode == VariableCode.ARG
				|| VariableCode == VariableCode.ARGS
				|| VariableCode == VariableCode.ARGF;
		}
	}

	public long FirstIndex
	{
		get
		{
			if (!Destination.TryGetConstantArguments(out var arguments) || arguments.Length == 0)
				return 0;
			return arguments[0];
		}
	}

	public ModernVariableTerm CreateIndexedDestination(ModernVariableEvaluator evaluator, long index)
	{
		return evaluator.CreateTerm(Destination.Identifier.Name, new SingleLongTerm(index));
	}
}
