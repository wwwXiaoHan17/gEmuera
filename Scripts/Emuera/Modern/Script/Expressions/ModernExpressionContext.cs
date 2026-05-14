namespace MinorShift.Emuera.Modern.Script.Expressions;

using MinorShift.Emuera.Modern.Script.Variables;

internal sealed class ModernExpressionContext
{
	public ModernExpressionContext()
	{
	}

	public ModernExpressionContext(ExecutionContext executionContext)
	{
		ExecutionContext = executionContext;
	}

	public ModernExpressionContext(ModernVariableEvaluator variableEvaluator, ExecutionContext executionContext = null)
	{
		VariableEvaluator = variableEvaluator;
		ExecutionContext = executionContext;
	}

	public ExecutionContext ExecutionContext { get; set; }
	public ModernVariableEvaluator VariableEvaluator { get; set; }
	public ModernPrivateVariableStore PrivateVariables { get; set; }
}
