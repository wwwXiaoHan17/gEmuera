using System;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Variables;

internal sealed class ModernVariableReference
{
	readonly ModernVariableToken token;
	readonly long[] arguments;
	readonly ModernExpressionContext context;

	public ModernVariableReference(ModernVariableToken token, long[] arguments, ModernExpressionContext context)
	{
		this.token = token ?? throw new ArgumentNullException(nameof(token));
		this.arguments = arguments ?? Array.Empty<long>();
		this.context = context;
	}

	public EraType EraType { get { return token.GetEraType(); } }
	public VariableDimension Dimension { get { return token.Dimension; } }

	public long GetIntValue()
	{
		return token.GetIntValue(context, arguments);
	}

	public string GetStrValue()
	{
		return token.GetStrValue(context, arguments) ?? "";
	}

	public double GetFloatValue()
	{
		return token.GetFloatValue(context, arguments);
	}

	public void SetValue(long value)
	{
		token.SetValue(value, context, arguments);
	}

	public void SetValue(string value)
	{
		token.SetValue(value, context, arguments);
	}

	public void SetValue(double value)
	{
		token.SetValue(value, context, arguments);
	}
}
