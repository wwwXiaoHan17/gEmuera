using System;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal sealed class ModernBreakStatement : ModernStatement
{
	public override void Execute(ModernExpressionContext context)
	{
		throw ModernLoopBreakException.Instance;
	}
}

internal sealed class ModernContinueStatement : ModernStatement
{
	public override void Execute(ModernExpressionContext context)
	{
		throw ModernLoopContinueException.Instance;
	}
}

internal sealed class ModernLoopBreakException : Exception
{
	public static readonly ModernLoopBreakException Instance = new();

	ModernLoopBreakException()
	{
	}
}

internal sealed class ModernLoopContinueException : Exception
{
	public static readonly ModernLoopContinueException Instance = new();

	ModernLoopContinueException()
	{
	}
}
