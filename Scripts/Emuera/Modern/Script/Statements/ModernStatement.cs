using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal abstract class ModernStatement
{
	public abstract void Execute(ModernExpressionContext context);
}
