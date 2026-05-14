using System;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal sealed class ModernWhileStatement : ModernStatement
{
	public ModernWhileStatement(AExpression condition, ModernBlockStatement body)
	{
		Condition = condition ?? throw new ArgumentNullException(nameof(condition));
		Body = body ?? throw new ArgumentNullException(nameof(body));
	}

	public AExpression Condition { get; }
	public ModernBlockStatement Body { get; }

	public override void Execute(ModernExpressionContext context)
	{
		while (ModernIfStatement.IsTrue(Condition, context))
		{
			try
			{
				Body.Execute(context);
			}
			catch (ModernLoopContinueException)
			{
				continue;
			}
			catch (ModernLoopBreakException)
			{
				break;
			}
		}
	}
}
