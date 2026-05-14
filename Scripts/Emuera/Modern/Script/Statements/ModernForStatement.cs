using System;
using MinorShift.Emuera.Modern.Script.Expressions;
using MinorShift.Emuera.Modern.Script.Variables;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal sealed class ModernForStatement : ModernStatement
{
	public ModernForStatement(
		ModernVariableTerm counter,
		AExpression start,
		AExpression end,
		AExpression step,
		ModernBlockStatement body)
	{
		Counter = counter ?? throw new ArgumentNullException(nameof(counter));
		Start = start ?? throw new ArgumentNullException(nameof(start));
		End = end ?? throw new ArgumentNullException(nameof(end));
		Step = step ?? throw new ArgumentNullException(nameof(step));
		Body = body ?? throw new ArgumentNullException(nameof(body));

		if (!Counter.Identifier.IsInteger)
			throw new FormatException("FOR counter must be an integer variable.");
		if (Counter.Identifier.IsConst)
			throw new FormatException("FOR counter must be changeable.");
	}

	public ModernVariableTerm Counter { get; }
	public AExpression Start { get; }
	public AExpression End { get; }
	public AExpression Step { get; }
	public ModernBlockStatement Body { get; }

	public override void Execute(ModernExpressionContext context)
	{
		long start = Start.GetIntValue(context);
		long end = End.GetIntValue(context);
		long step = Step.GetIntValue(context);

		Counter.SetValue(start, context);
		if (step == 0)
			return;

		while (ShouldContinue(Counter.GetIntValue(context), end, step))
		{
			try
			{
				Body.Execute(context);
			}
			catch (ModernLoopContinueException)
			{
				AdvanceCounter(context, step);
				continue;
			}
			catch (ModernLoopBreakException)
			{
				AdvanceCounter(context, step);
				break;
			}

			AdvanceCounter(context, step);
		}
	}

	static bool ShouldContinue(long counter, long end, long step)
	{
		return step > 0 ? counter < end : counter > end;
	}

	void AdvanceCounter(ModernExpressionContext context, long step)
	{
		long current = Counter.GetIntValue(context);
		unchecked
		{
			Counter.SetValue(current + step, context);
		}
	}
}
