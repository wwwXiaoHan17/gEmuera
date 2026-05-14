using System;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal sealed class ModernDoLoopStatement : ModernStatement
{
	public ModernDoLoopStatement(ModernBlockStatement body, AExpression condition)
	{
		Body = body ?? throw new ArgumentNullException(nameof(body));
		Condition = condition ?? throw new ArgumentNullException(nameof(condition));
	}

	public ModernBlockStatement Body { get; }
	public AExpression Condition { get; }

	public override void Execute(ModernExpressionContext context)
	{
		while (true)
		{
			try
			{
				Body.Execute(context);
			}
			catch (ModernLoopContinueException)
			{
			}
			catch (ModernLoopBreakException)
			{
				break;
			}

			if (!ModernIfStatement.IsTrue(Condition, context))
				break;
		}
	}
}
