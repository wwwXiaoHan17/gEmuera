using System;

namespace MinorShift.Emuera.Modern.Script.Expressions;

internal sealed class ModernUnaryExpression : AExpression
{
	readonly AExpression operand;
	readonly char op;

	public ModernUnaryExpression(AExpression operand, char op)
		: base(GetResultType(operand, op))
	{
		this.operand = operand ?? throw new ArgumentNullException(nameof(operand));
		this.op = op;
	}

	public override long GetIntValue(ModernExpressionContext context)
	{
		return op switch
		{
			'-' => checked(-operand.GetIntValue(context)),
			'!' => GetNumericTruthValue(operand, context) ? 0L : 1L,
			_ => operand.GetIntValue(context),
		};
	}

	public override double GetFloatValue(ModernExpressionContext context)
	{
		return op switch
		{
			'-' => -GetNumericFloatValue(operand, context),
			'!' => GetIntValue(context),
			_ => GetNumericFloatValue(operand, context),
		};
	}

	public override SingleTerm GetValue(ModernExpressionContext context)
	{
		if (IsFloat)
			return new SingleFloatTerm(GetFloatValue(context));
		return new SingleLongTerm(GetIntValue(context));
	}

	static EraType GetResultType(AExpression operand, char op)
	{
		if (operand == null)
			throw new ArgumentNullException(nameof(operand));
		if (operand.IsString)
			throw new InvalidOperationException($"Unary {op} cannot be applied to strings.");
		if (op == '!')
			return EraType.Integer;
		return operand.IsFloat ? EraType.Float : EraType.Integer;
	}

	static double GetNumericFloatValue(AExpression expression, ModernExpressionContext context)
	{
		return expression.IsFloat ? expression.GetFloatValue(context) : expression.GetIntValue(context);
	}

	static bool GetNumericTruthValue(AExpression expression, ModernExpressionContext context)
	{
		return expression.IsFloat ? expression.GetFloatValue(context) != 0.0 : expression.GetIntValue(context) != 0;
	}
}
