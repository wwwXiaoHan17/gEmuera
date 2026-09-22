using System;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Expression
{
	/// <summary>
	/// 整数运算语义按会话方言分流（双参考各自原样）：
	/// - snake 系会话（snake/erafl，snake 血统 fork）：checked 溢出保护 + 告警 + 钳制，
	///   / % 除零打警告得 0 继续，MinValue 取负告警后得 MaxValue —— 与 snake 参考
	///   SafeArithmetic.cs 逐点一致（其 CHANGELOG v2.0.0「SafeArithmetic 安全运算：
	///   溢出保护，不再静默溢出」为行为契约）；eraFL 无源码、血统承 snake，保守同门。
	/// - v24 会话：+ - * 溢出静默回绕（参考工程未开 checked）；/ % 除零抛 CodeEE
	///   （致命错误中止脚本）；单目负号对 long.MinValue 打系统行后值不变 —— 与
	///   emuera.em-master OperatorMethod 原样一致。
	/// 方法名保留 Safe* 以维持调用点稳定。
	/// </summary>
	internal static class SafeArithmetic
	{
		private static bool UsesSnakeOverflowGuard =>
			Program.Compatibility.Snake.IsEnabled || Program.Compatibility.EraFl.IsEnabled;

		public static long SafeAdd(long a, long b) => SafeAdd(a, b, null);
		public static long SafeAdd(long a, long b, ScriptPosition pos)
		{
			if (!UsesSnakeOverflowGuard)
				return unchecked(a + b);
			try
			{
				checked { return a + b; }
			}
			catch (OverflowException)
			{
				GlobalStatic.EMediator.Console.PrintWarning(
					$"整数溢出: {a} + {b}", pos, 1);
				return a > 0 ? long.MaxValue : long.MinValue;
			}
		}

		public static long SafeSubtract(long a, long b) => SafeSubtract(a, b, null);
		public static long SafeSubtract(long a, long b, ScriptPosition pos)
		{
			if (!UsesSnakeOverflowGuard)
				return unchecked(a - b);
			try
			{
				checked { return a - b; }
			}
			catch (OverflowException)
			{
				GlobalStatic.EMediator.Console.PrintWarning(
					$"整数溢出: {a} - {b}", pos, 1);
				return a > 0 ? long.MaxValue : long.MinValue;
			}
		}

		public static long SafeMultiply(long a, long b) => SafeMultiply(a, b, null);
		public static long SafeMultiply(long a, long b, ScriptPosition pos)
		{
			if (!UsesSnakeOverflowGuard)
				return unchecked(a * b);
			try
			{
				checked { return a * b; }
			}
			catch (OverflowException)
			{
				GlobalStatic.EMediator.Console.PrintWarning(
					$"整数溢出: {a} * {b}", pos, 1);
				return (a > 0) == (b > 0) ? long.MaxValue : long.MinValue;
			}
		}

		public static long SafeNegate(long a) => SafeNegate(a, null);
		public static long SafeNegate(long a, ScriptPosition pos)
		{
			if (!UsesSnakeOverflowGuard)
			{
				// v24 参考：整数型最小值取负打印系统行，值不变（回绕后仍为 MinValue）
				if (a == long.MinValue)
					GlobalStatic.EMediator.Console.PrintSystemLine(string.Format("整数型最小値({0})は-を取っても値は変化しません", long.MinValue));
				return unchecked(-a);
			}
			try
			{
				checked { return -a; }
			}
			catch (OverflowException)
			{
				GlobalStatic.EMediator.Console.PrintWarning(
					$"整数溢出: -{a}", pos, 1);
				return long.MaxValue;
			}
		}

		public static long SafeDivide(long a, long b) => SafeDivide(a, b, null);
		public static long SafeDivide(long a, long b, ScriptPosition pos)
		{
			if (b == 0)
			{
				if (!UsesSnakeOverflowGuard)
					throw new CodeEE("0による除算が行なわれました");
				GlobalStatic.EMediator.Console.PrintWarning(
					$"零除: {a} / 0", pos, 1);
				return 0;
			}
			return a / b;
		}

		public static long SafeModulo(long a, long b) => SafeModulo(a, b, null);
		public static long SafeModulo(long a, long b, ScriptPosition pos)
		{
			if (b == 0)
			{
				if (!UsesSnakeOverflowGuard)
					throw new CodeEE("0による除算が行なわれました");
				GlobalStatic.EMediator.Console.PrintWarning(
					$"零除: {a} \\ 0", pos, 1);
				return 0;
			}
			return a % b;
		}

		// 浮点路径仅方言（float 生态）可达，两参考无对应分歧，维持现状。
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
