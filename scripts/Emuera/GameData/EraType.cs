using System;

namespace MinorShift.Emuera.GameData
{
	internal enum EraType
	{
		Void,
		Integer,
		String,
		Float,
	}

	internal static class EraTypeHelper
	{
		public static EraType FromClrType(Type type)
		{
			if (type == typeof(long))
				return EraType.Integer;
			if (type == typeof(double) || type == typeof(float))
				return EraType.Float;
			if (type == typeof(string))
				return EraType.String;
			return EraType.Void;
		}

		public static Type ToClrType(EraType type)
		{
			return type switch
			{
				EraType.Integer => typeof(long),
				EraType.Float => typeof(double),
				EraType.String => typeof(string),
				_ => typeof(void),
			};
		}
	}
}
