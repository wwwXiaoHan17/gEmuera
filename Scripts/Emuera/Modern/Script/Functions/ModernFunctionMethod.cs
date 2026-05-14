using System;
using System.Collections.Generic;
using System.Globalization;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Functions;

internal abstract class ModernFunctionMethod
{
	protected ModernFunctionMethod(string name, int minArgumentCount, int maxArgumentCount)
	{
		Name = name;
		MinArgumentCount = minArgumentCount;
		MaxArgumentCount = maxArgumentCount;
	}

	public string Name { get; }
	public int MinArgumentCount { get; }
	public int MaxArgumentCount { get; }

	public void Validate(IReadOnlyList<AExpression> arguments)
	{
		int count = arguments?.Count ?? 0;
		if (count < MinArgumentCount || count > MaxArgumentCount)
			throw new FormatException($"{Name} expects {DescribeArgumentCount()} arguments, got {count}.");
		ValidateArguments(arguments);
	}

	public abstract EraType GetReturnType(IReadOnlyList<AExpression> arguments);

	public virtual long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
	{
		throw new InvalidOperationException($"{Name} does not return an integer.");
	}

	public virtual string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
	{
		throw new InvalidOperationException($"{Name} does not return a string.");
	}

	public virtual double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
	{
		throw new InvalidOperationException($"{Name} does not return a float.");
	}

	protected virtual void ValidateArguments(IReadOnlyList<AExpression> arguments)
	{
	}

	protected static bool HasFloatArg(IReadOnlyList<AExpression> arguments)
	{
		for (int i = 0; i < arguments.Count; i++)
		{
			if (arguments[i].IsFloat)
				return true;
		}
		return false;
	}

	protected static double ToDouble(AExpression expression, ModernExpressionContext context)
	{
		if (expression.IsFloat)
			return expression.GetFloatValue(context);
		if (expression.IsInteger)
			return expression.GetIntValue(context);
		string value = expression.GetStrValue(context);
		if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
			return parsed;
		if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
			return parsed;
		throw new FormatException($"Cannot convert \"{value}\" to float.");
	}

	protected static long ToLong(AExpression expression, ModernExpressionContext context)
	{
		if (expression.IsInteger)
			return expression.GetIntValue(context);
		if (expression.IsFloat)
			return (long)expression.GetFloatValue(context);
		string value = expression.GetStrValue(context);
		if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
			return parsed;
		if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double floatValue))
			return (long)floatValue;
		throw new FormatException($"Cannot convert \"{value}\" to integer.");
	}

	protected static string ToStr(AExpression expression, ModernExpressionContext context)
	{
		if (expression.IsString)
			return expression.GetStrValue(context) ?? "";
		if (expression.IsFloat)
			return expression.GetFloatValue(context).ToString(CultureInfo.InvariantCulture);
		return expression.GetIntValue(context).ToString(CultureInfo.InvariantCulture);
	}

	protected static long CheckedDoubleToLong(string name, double value)
	{
		if (double.IsNaN(value))
			throw new ArithmeticException($"{name} result is NaN.");
		if (double.IsInfinity(value))
			throw new ArithmeticException($"{name} result is infinity.");
		if (value >= long.MaxValue || value <= long.MinValue)
			throw new OverflowException($"{name} result is outside Int64 range.");
		return (long)value;
	}

	string DescribeArgumentCount()
	{
		if (MinArgumentCount == MaxArgumentCount)
			return MinArgumentCount.ToString(CultureInfo.InvariantCulture);
		return $"{MinArgumentCount}..{MaxArgumentCount}";
	}
}
