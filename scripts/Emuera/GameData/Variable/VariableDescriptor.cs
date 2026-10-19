using System;
using System.Collections.Generic;
using MinorShift.Emuera.GameData;

namespace MinorShift.Emuera.GameData.Variable
{
	[Flags]
	internal enum VariableKind
	{
		Integer = 0x01,
		String = 0x02,
		Float = 0x04,
	}

	internal enum VariableDimension
	{
		Scalar = 0,
		Array1D = 1,
		Array2D = 2,
		Array3D = 3,
	}

	[Flags]
	internal enum VariableAttribute
	{
		None = 0,
		CanForbid = 0x01,
		CharacterData = 0x02,
		Global = 0x04,
		Save = 0x08,
		Local = 0x10,
		Unchangeable = 0x20,
		Calc = 0x40,
		Extended = 0x80,
		Constant = 0x100,
	}

	/// <summary>
	/// 变量描述符把 VariableCode 的位标志集中解释成类型、维度和属性。
	/// 当前阶段只作为元数据层接入，不改变变量数组布局和存档格式，降低架构迁移风险。
	/// </summary>
	internal readonly struct VariableDescriptor
	{
		public readonly VariableCode Code;
		public readonly VariableKind Kind;
		public readonly VariableDimension Dimension;
		public readonly VariableAttribute Attributes;

		public VariableDescriptor(VariableCode code, VariableKind kind, VariableDimension dimension, VariableAttribute attributes)
		{
			Code = code;
			Kind = kind;
			Dimension = dimension;
			Attributes = attributes;
		}

		public bool IsInteger { get { return (Kind & VariableKind.Integer) != 0; } }
		public bool IsString { get { return (Kind & VariableKind.String) != 0; } }
		public bool IsFloat { get { return (Kind & VariableKind.Float) != 0; } }

		public bool HasAttribute(VariableAttribute attribute)
		{
			return (Attributes & attribute) != 0;
		}

		public EraType GetEraType()
		{
			if (IsInteger)
				return EraType.Integer;
			if (IsFloat)
				return EraType.Float;
			return EraType.String;
		}

		public static VariableDescriptor FromCode(VariableCode code)
		{
			VariableKind kind;
			if ((code & VariableCode.__FLOAT__) == VariableCode.__FLOAT__)
				kind = VariableKind.Float;
			else if ((code & VariableCode.__STRING__) == VariableCode.__STRING__)
				kind = VariableKind.String;
			else
				kind = VariableKind.Integer;

			VariableDimension dimension = VariableDimension.Scalar;
			if ((code & VariableCode.__ARRAY_3D__) == VariableCode.__ARRAY_3D__)
				dimension = VariableDimension.Array3D;
			else if ((code & VariableCode.__ARRAY_2D__) == VariableCode.__ARRAY_2D__)
				dimension = VariableDimension.Array2D;
			else if ((code & VariableCode.__ARRAY_1D__) == VariableCode.__ARRAY_1D__)
				dimension = VariableDimension.Array1D;

			VariableAttribute attributes = VariableAttribute.None;
			if ((code & VariableCode.__CAN_FORBID__) == VariableCode.__CAN_FORBID__)
				attributes |= VariableAttribute.CanForbid;
			if ((code & VariableCode.__CHARACTER_DATA__) == VariableCode.__CHARACTER_DATA__)
				attributes |= VariableAttribute.CharacterData;
			if ((code & VariableCode.__GLOBAL__) == VariableCode.__GLOBAL__)
				attributes |= VariableAttribute.Global;
			if ((code & VariableCode.__SAVE_EXTENDED__) == VariableCode.__SAVE_EXTENDED__)
				attributes |= VariableAttribute.Save;
			if ((code & VariableCode.__LOCAL__) == VariableCode.__LOCAL__)
				attributes |= VariableAttribute.Local;
			if ((code & VariableCode.__UNCHANGEABLE__) == VariableCode.__UNCHANGEABLE__)
				attributes |= VariableAttribute.Unchangeable;
			if ((code & VariableCode.__CALC__) == VariableCode.__CALC__)
				attributes |= VariableAttribute.Calc;
			if ((code & VariableCode.__EXTENDED__) == VariableCode.__EXTENDED__)
				attributes |= VariableAttribute.Extended;
			if ((code & VariableCode.__CONSTANT__) == VariableCode.__CONSTANT__)
				attributes |= VariableAttribute.Constant;

			return new VariableDescriptor(code, kind, dimension, attributes);
		}
	}

	internal static class VariableDescriptorTable
	{
		static readonly Dictionary<VariableCode, VariableDescriptor> codeIndex = new Dictionary<VariableCode, VariableDescriptor>();
		static readonly Dictionary<string, VariableDescriptor> nameIndex = new Dictionary<string, VariableDescriptor>(StringComparer.OrdinalIgnoreCase);

		static VariableDescriptorTable()
		{
			Array values = Enum.GetValues(typeof(VariableCode));
			foreach (object value in values)
			{
				VariableCode code = (VariableCode)value;
				string name = code.ToString();
				if (!ShouldRegisterName(name, code))
					continue;
				Register(name, VariableDescriptor.FromCode(code));
			}
		}

		static bool ShouldRegisterName(string name, VariableCode code)
		{
			if (string.IsNullOrEmpty(name))
				return false;

			// VariableCode 里混有位掩码、计数边界和少量双下划线调试变量。
			// descriptor 的名字索引只收脚本可见变量，避免后续按名查询时命中纯标志项。
			if (name.StartsWith("__") && name.EndsWith("__") &&
				name != "__FILE__" && name != "__LINE__" && name != "__FUNCTION__")
				return false;

			return (code & (VariableCode.__INTEGER__ | VariableCode.__STRING__ | VariableCode.__FLOAT__)) != 0;
		}

		static void Register(string name, VariableDescriptor descriptor)
		{
			codeIndex[descriptor.Code] = descriptor;
			nameIndex[name] = descriptor;
		}

		public static bool TryGetDescriptor(string name, out VariableDescriptor descriptor)
		{
			if (string.IsNullOrEmpty(name))
			{
				descriptor = default;
				return false;
			}
			return nameIndex.TryGetValue(name, out descriptor);
		}

		public static bool TryGetDescriptorByCode(VariableCode code, out VariableDescriptor descriptor)
		{
			if (codeIndex.TryGetValue(code, out descriptor))
				return true;
			descriptor = VariableDescriptor.FromCode(code);
			return false;
		}

		public static VariableDescriptor GetDescriptorByCode(VariableCode code)
		{
			if (codeIndex.TryGetValue(code, out VariableDescriptor descriptor))
				return descriptor;
			descriptor = VariableDescriptor.FromCode(code);
			codeIndex[code] = descriptor;
			return descriptor;
		}
	}
}
