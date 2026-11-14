#nullable disable
using System;

namespace MinorShift.Emuera.GameData.Function
{
	/// <summary>
	/// 方言函数参数形状的纯判定（不依赖 IOperandTerm/EraType），供
	/// <see cref="DialectFunctionContracts"/> 在 v24/snake 之间投影同一份规则，
	/// 同时让 xUnit 契约测试可以直链源码而不必装配整个解释器。
	/// </summary>
	internal static class DialectArgumentShape
	{
		/// <summary>
		/// 参考实现 ARGLEN = argumentTypeArrayEx([{ ArgTypes = { } }])：恰好 0 个实参。
		/// 返回 null 表示通过，否则返回错误消息。
		/// </summary>
		internal static string CheckNoArguments(string name, int argumentCount)
		{
			if (argumentCount == 0)
				return null;
			return name + " has an invalid argument count";
		}
	}
}
