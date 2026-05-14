using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal enum ModernReturnKind
{
	Integer,
	Value,
}

internal sealed class ModernReturnStatement : ModernStatement
{
	readonly List<AExpression> values;

	public ModernReturnStatement(ModernReturnKind kind, IEnumerable<AExpression> values)
	{
		Kind = kind;
		this.values = values == null ? new List<AExpression>() : new List<AExpression>(values);
	}

	public ModernReturnKind Kind { get; }
	public IReadOnlyList<AExpression> Values { get { return values; } }

	public override void Execute(ModernExpressionContext context)
	{
		if (Kind == ModernReturnKind.Integer)
			throw new ModernReturnException(ReturnInteger(context));

		throw new ModernReturnException(ReturnValue(context));
	}

	SingleTerm ReturnInteger(ModernExpressionContext context)
	{
		if (values.Count == 0)
		{
			SetResult(context, 0, 0);
			return new SingleLongTerm(0);
		}

		long first = 0;
		for (int i = 0; i < values.Count; i++)
		{
			long value = values[i].GetIntValue(context);
			if (i == 0)
				first = value;
			SetResult(context, i, value);
		}

		return new SingleLongTerm(first);
	}

	SingleTerm ReturnValue(ModernExpressionContext context)
	{
		if (values.Count == 0)
			return new SingleLongTerm(0);

		SingleTerm value = values[0].GetValue(context);
		SetTypedResult(context, value);
		return value;
	}

	static void SetResult(ModernExpressionContext context, int index, long value)
	{
		var evaluator = context?.VariableEvaluator;
		if (evaluator == null)
			return;
		var term = evaluator.CreateTerm("RESULT", new SingleLongTerm(index));
		term.SetValue(value, context);
	}

	static void SetTypedResult(ModernExpressionContext context, SingleTerm value)
	{
		var evaluator = context?.VariableEvaluator;
		if (evaluator == null)
			return;
		switch (value)
		{
			case SingleLongTerm longTerm:
				evaluator.CreateTerm("RESULT", new SingleLongTerm(0)).SetValue(longTerm.Int, context);
				break;
			case SingleFloatTerm floatTerm:
				evaluator.CreateTerm("RESULTF").SetValue(floatTerm.Float, context);
				break;
			case SingleStrTerm strTerm:
				evaluator.CreateTerm("RESULTS", new SingleLongTerm(0)).SetValue(strTerm.Str, context);
				break;
		}
	}
}

internal sealed class ModernReturnException : Exception
{
	public ModernReturnException(SingleTerm value)
	{
		Value = value ?? new SingleLongTerm(0);
	}

	public SingleTerm Value { get; }
}
