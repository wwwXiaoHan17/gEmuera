using System;
using System.Globalization;

namespace MinorShift.Emuera.Modern.Script.Expressions;

internal sealed class ModernBinaryExpression : AExpression
{
	readonly AExpression left;
	readonly AExpression right;
	readonly char op;

	public ModernBinaryExpression(AExpression left, AExpression right, char op)
		: base(GetResultType(left, right, op))
	{
		this.left = left ?? throw new ArgumentNullException(nameof(left));
		this.right = right ?? throw new ArgumentNullException(nameof(right));
		this.op = op;
	}

	public override long GetIntValue(ModernExpressionContext context)
	{
		long l = left.GetIntValue(context);
		long r = right.GetIntValue(context);
		return op switch
		{
			'+' => checked(l + r),
			'-' => checked(l - r),
			'*' => checked(l * r),
			'/' => r == 0 ? throw new DivideByZeroException() : l / r,
			'%' => r == 0 ? throw new DivideByZeroException() : l % r,
			_ => throw new NotSupportedException($"Unsupported operator {op}."),
		};
	}

	public override double GetFloatValue(ModernExpressionContext context)
	{
		double l = GetNumericFloatValue(left, context);
		double r = GetNumericFloatValue(right, context);
		return op switch
		{
			'+' => l + r,
			'-' => l - r,
			'*' => l * r,
			'/' => r == 0.0 ? throw new DivideByZeroException() : l / r,
			'%' => r == 0.0 ? throw new DivideByZeroException() : l % r,
			_ => throw new NotSupportedException($"Unsupported operator {op}."),
		};
	}

	public override string GetStrValue(ModernExpressionContext context)
	{
		if (op != '+')
			throw new InvalidOperationException($"Operator {op} cannot be applied to strings.");
		return GetStringValue(left, context) + GetStringValue(right, context);
	}

	public override SingleTerm GetValue(ModernExpressionContext context)
	{
		if (IsString)
			return new SingleStrTerm(GetStrValue(context));
		if (IsFloat)
			return new SingleFloatTerm(GetFloatValue(context));
		return new SingleLongTerm(GetIntValue(context));
	}

	static EraType GetResultType(AExpression left, AExpression right, char op)
	{
		if (left == null)
			throw new ArgumentNullException(nameof(left));
		if (right == null)
			throw new ArgumentNullException(nameof(right));

		if (left.IsString || right.IsString)
		{
			if (op == '+')
				return EraType.String;
			throw new InvalidOperationException($"Operator {op} cannot be applied to strings.");
		}

		return left.IsFloat || right.IsFloat ? EraType.Float : EraType.Integer;
	}

	static double GetNumericFloatValue(AExpression expression, ModernExpressionContext context)
	{
		return expression.IsFloat ? expression.GetFloatValue(context) : expression.GetIntValue(context);
	}

	static string GetStringValue(AExpression expression, ModernExpressionContext context)
	{
		if (expression.IsString)
			return expression.GetStrValue(context) ?? "";
		if (expression.IsFloat)
			return expression.GetFloatValue(context).ToString(CultureInfo.InvariantCulture);
		return expression.GetIntValue(context).ToString(CultureInfo.InvariantCulture);
	}
}
