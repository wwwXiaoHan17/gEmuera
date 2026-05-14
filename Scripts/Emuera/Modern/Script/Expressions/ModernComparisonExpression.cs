using System;
using System.Globalization;

namespace MinorShift.Emuera.Modern.Script.Expressions;

internal sealed class ModernComparisonExpression : AExpression
{
	readonly AExpression left;
	readonly AExpression right;
	readonly string op;

	public ModernComparisonExpression(AExpression left, AExpression right, string op)
		: base(EraType.Integer)
	{
		this.left = left ?? throw new ArgumentNullException(nameof(left));
		this.right = right ?? throw new ArgumentNullException(nameof(right));
		this.op = op ?? throw new ArgumentNullException(nameof(op));
	}

	public override long GetIntValue(ModernExpressionContext context)
	{
		return Compare(context) ? 1L : 0L;
	}

	public override SingleTerm GetValue(ModernExpressionContext context)
	{
		return new SingleLongTerm(GetIntValue(context));
	}

	bool Compare(ModernExpressionContext context)
	{
		if (left.IsString || right.IsString)
			return CompareStrings(context);
		return CompareNumbers(context);
	}

	bool CompareNumbers(ModernExpressionContext context)
	{
		double l = GetNumericFloatValue(left, context);
		double r = GetNumericFloatValue(right, context);
		return op switch
		{
			"==" => l == r,
			"!=" => l != r,
			">" => l > r,
			"<" => l < r,
			">=" => l >= r,
			"<=" => l <= r,
			_ => throw new NotSupportedException($"Unsupported comparison operator {op}."),
		};
	}

	bool CompareStrings(ModernExpressionContext context)
	{
		string l = GetStringValue(left, context);
		string r = GetStringValue(right, context);
		int compare = string.Compare(l, r, StringComparison.Ordinal);
		return op switch
		{
			"==" => compare == 0,
			"!=" => compare != 0,
			">" => compare > 0,
			"<" => compare < 0,
			">=" => compare >= 0,
			"<=" => compare <= 0,
			_ => throw new NotSupportedException($"Unsupported comparison operator {op}."),
		};
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
