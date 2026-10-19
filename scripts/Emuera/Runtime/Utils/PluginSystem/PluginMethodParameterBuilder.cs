using MinorShift.Emuera.GameData.Expression;

namespace MinorShift.Emuera.Runtime.Utils.PluginSystem
{
	/// <summary>
	/// 表达式项到插件参数的转换（依赖游戏侧类型，留在宿主程序集；契约类型已移至 Emuera 契约程序集）。
	/// </summary>
	internal static class PluginMethodParameterBuilder
	{
		internal static PluginMethodParameter ConvertTerm(IOperandTerm term, ExpressionMediator exm)
		{
			if (term.IsString)
				return new PluginMethodParameter(term.GetStrValue(exm));
			if (term.IsFloat)
				return new PluginMethodParameter(term.GetFloatValue(exm));
			return new PluginMethodParameter(term.GetIntValue(exm));
		}
	}
}
