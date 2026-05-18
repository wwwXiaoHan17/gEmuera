using System;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Expression
{
	internal static class SafeArithmetic
	{
		public static long SafeAdd(long a, long b) => SafeAdd(a, b, null);
		public static long SafeAdd(long a, long b, ScriptPosition pos)
		{
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
				GlobalStatic.EMediator.Console.PrintWarning(
					$"零除: {a} \\ 0", pos, 1);
				return 0;
			}
			return a % b;
		}

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
