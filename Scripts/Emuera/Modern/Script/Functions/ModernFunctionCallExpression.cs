using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Functions;

internal sealed class ModernFunctionCallExpression : AExpression
{
	readonly ModernFunctionMethod method;
	readonly IReadOnlyList<AExpression> arguments;

	public ModernFunctionCallExpression(ModernFunctionMethod method, IReadOnlyList<AExpression> arguments)
		: base(method.GetReturnType(arguments))
	{
		this.method = method ?? throw new ArgumentNullException(nameof(method));
		this.arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
		method.Validate(arguments);
	}

	public override long GetIntValue(ModernExpressionContext context)
	{
		return method.GetIntValue(context, arguments);
	}

	public override string GetStrValue(ModernExpressionContext context)
	{
		return method.GetStrValue(context, arguments);
	}

	public override double GetFloatValue(ModernExpressionContext context)
	{
		return method.GetFloatValue(context, arguments);
	}

	public override SingleTerm GetValue(ModernExpressionContext context)
	{
		return GetEraType() switch
		{
			EraType.Integer => new SingleLongTerm(GetIntValue(context)),
			EraType.Float => new SingleFloatTerm(GetFloatValue(context)),
			_ => new SingleStrTerm(GetStrValue(context)),
		};
	}
}
