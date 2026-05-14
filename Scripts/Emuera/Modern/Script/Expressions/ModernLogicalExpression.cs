using System;

namespace MinorShift.Emuera.Modern.Script.Expressions;

internal sealed class ModernLogicalExpression : AExpression
{
	readonly AExpression left;
	readonly AExpression right;
	readonly string op;

	public ModernLogicalExpression(AExpression left, AExpression right, string op)
		: base(EraType.Integer)
	{
		this.left = left ?? throw new ArgumentNullException(nameof(left));
		this.right = right ?? throw new ArgumentNullException(nameof(right));
		this.op = op ?? throw new ArgumentNullException(nameof(op));
	}

	public override long GetIntValue(ModernExpressionContext context)
	{
		return op switch
		{
			"&&" => IsTrue(left, context) && IsTrue(right, context) ? 1L : 0L,
			"||" => IsTrue(left, context) || IsTrue(right, context) ? 1L : 0L,
			_ => throw new NotSupportedException($"Unsupported logical operator {op}."),
		};
	}

	public override SingleTerm GetValue(ModernExpressionContext context)
	{
		return new SingleLongTerm(GetIntValue(context));
	}

	static bool IsTrue(AExpression expression, ModernExpressionContext context)
	{
		if (expression.IsString)
			throw new InvalidOperationException("String expressions cannot be used as logical conditions.");
		return expression.IsFloat ? expression.GetFloatValue(context) != 0.0 : expression.GetIntValue(context) != 0;
	}
}
