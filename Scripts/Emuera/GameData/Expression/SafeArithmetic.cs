using System;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Expression
{
	/// <summary>
	/// 对齐参考实现的整数运算语义：
	/// + - * 溢出静默回绕（unchecked，参考工程未开 checked）；
	/// / % 除零抛 CodeEE（参考侧为致命错误，脚本中止）；
	/// 单目负号对 long.MinValue 打印系统行后原样返回（回绕仍为 MinValue）。
	/// 方法名保留 Safe* 以维持调用点稳定。
	/// </summary>
	internal static class SafeArithmetic
	{
		public static long SafeAdd(long a, long b) => unchecked(a + b);
		public static long SafeAdd(long a, long b, ScriptPosition pos) => unchecked(a + b);

		public static long SafeSubtract(long a, long b) => unchecked(a - b);
		public static long SafeSubtract(long a, long b, ScriptPosition pos) => unchecked(a - b);

		public static long SafeMultiply(long a, long b) => unchecked(a * b);
		public static long SafeMultiply(long a, long b, ScriptPosition pos) => unchecked(a * b);

		public static long SafeNegate(long a) => SafeNegate(a, null);
		public static long SafeNegate(long a, ScriptPosition pos)
		{
			// 参考侧：整数型最小值取负打印系统行，值不变（回绕后仍为 MinValue）
			if (a == long.MinValue)
				GlobalStatic.EMediator.Console.PrintSystemLine(string.Format("整数型最小値({0})は-を取っても値は変化しません", long.MinValue));
			return unchecked(-a);
		}

		public static long SafeDivide(long a, long b) => SafeDivide(a, b, null);
		public static long SafeDivide(long a, long b, ScriptPosition pos)
		{
			if (b == 0)
				throw new CodeEE("0による除算が行なわれました");
			return a / b;
		}

		public static long SafeModulo(long a, long b) => SafeModulo(a, b, null);
		public static long SafeModulo(long a, long b, ScriptPosition pos)
		{
			if (b == 0)
				throw new CodeEE("0による除算が行なわれました");
			return a % b;
		}

		// 浮点路径仅方言（float 生态）可达，参考侧无对应语义，维持现状。
		public static double SafeFloatDivide(double a, double b) => SafeFloatDivide(a, b, null);
		public static double SafeFloatDivide(double a, double b, ScriptPosition pos)
		{
			if (b == 0.0)
			{
				GlobalStatic.EMediator.Console.PrintWarning(
					$"浮点零除: {a} / 0", pos, 1);
				return double.NaN;
			}
			return a / b;
		}
	}
}
