using System;
using CharacterData = MinorShift.Emuera.GameData.Variable.CharacterData;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Variables;

internal abstract class ModernVariableToken
{
	protected ModernVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: this(
			name,
			code,
			variableData,
			VariableDescriptorTable.TryGetDescriptor(name, out var descriptor)
				? descriptor
				: VariableDescriptor.FromCode(code, name))
	{
	}

	protected ModernVariableToken(string name, VariableCode code, ModernVariableData variableData, VariableDescriptor descriptor)
	{
		Name = name;
		Code = code;
		VariableData = variableData ?? throw new ArgumentNullException(nameof(variableData));
		CodeIndex = (int)(code & VariableCode.__LOWERCASE__);
		Descriptor = descriptor;
		Dimension = Descriptor.Dimension;
	}

	public string Name { get; }
	public VariableCode Code { get; }
	public int CodeIndex { get; }
	public VariableDescriptor Descriptor { get; }
	public VariableDimension Dimension { get; }
	public bool IsInteger { get { return Descriptor.IsInteger; } }
	public bool IsString { get { return Descriptor.IsString; } }
	public bool IsFloat { get { return Descriptor.IsFloat; } }
	public bool IsLocal { get { return (Descriptor.Attributes & VariableAttribute.Local) != 0; } }
	public bool IsConst { get { return (Descriptor.Attributes & VariableAttribute.Unchangeable) != 0; } }
	public bool IsCharacterData { get { return (Descriptor.Attributes & VariableAttribute.CharacterData) != 0; } }
	public virtual bool IsReference { get { return false; } }
	public virtual bool IsOut { get { return false; } }
	protected ModernVariableData VariableData { get; }

	public EraType GetEraType()
	{
		if (IsInteger)
			return EraType.Integer;
		if (IsFloat)
			return EraType.Float;
		return EraType.String;
	}

	public virtual long GetIntValue(ModernExpressionContext context, long[] arguments)
	{
		throw WrongType("integer");
	}

	public virtual string GetStrValue(ModernExpressionContext context, long[] arguments)
	{
		throw WrongType("string");
	}

	public virtual double GetFloatValue(ModernExpressionContext context, long[] arguments)
	{
		throw WrongType("float");
	}

	public virtual void SetValue(long value, ModernExpressionContext context, long[] arguments)
	{
		throw WrongType("integer");
	}

	public virtual void SetValue(string value, ModernExpressionContext context, long[] arguments)
	{
		throw WrongType("string");
	}

	public virtual void SetValue(double value, ModernExpressionContext context, long[] arguments)
	{
		throw WrongType("float");
	}

	public virtual long PlusValue(long value, ModernExpressionContext context, long[] arguments)
	{
		var next = checked(GetIntValue(context, arguments) + value);
		SetValue(next, context, arguments);
		return next;
	}

	public virtual double PlusValue(double value, ModernExpressionContext context, long[] arguments)
	{
		var next = GetFloatValue(context, arguments) + value;
		SetValue(next, context, arguments);
		return next;
	}

	public virtual int GetLength()
	{
		throw new InvalidOperationException($"{Name} is not an array.");
	}

	public virtual object GetArray(ModernExpressionContext context)
	{
		throw new InvalidOperationException($"{Name} is not an array.");
	}

	protected static long FirstIndex(long[] arguments)
	{
		if (arguments == null || arguments.Length == 0)
			throw new ArgumentException("A one-dimensional variable needs one index.");
		return arguments[0];
	}

	protected void CheckIndex(long index, int length)
	{
		if (index < 0 || index >= length)
			throw new IndexOutOfRangeException($"{Name}: index {index} is outside 0..{length - 1}.");
	}

	InvalidOperationException WrongType(string requestedType)
	{
		return new InvalidOperationException($"{Name} cannot be used as {requestedType}.");
	}
}

internal sealed class ModernPrivateVariableToken : ModernVariableToken
{
	readonly ModernUserVariableDefinition definition;

	public ModernPrivateVariableToken(ModernUserVariableDefinition definition, ModernVariableData variableData)
		: base(
			definition?.Name ?? throw new ArgumentNullException(nameof(definition)),
			VariableCode.__NULL__,
			variableData,
			CreateDescriptor(definition))
	{
		this.definition = definition;
	}

	public override long GetIntValue(ModernExpressionContext context, long[] arguments)
	{
		RequireInteger();
		return GetSlot(context).GetInt(arguments);
	}

	public override string GetStrValue(ModernExpressionContext context, long[] arguments)
	{
		RequireString();
		return GetSlot(context).GetString(arguments);
	}

	public override double GetFloatValue(ModernExpressionContext context, long[] arguments)
	{
		RequireFloat();
		return GetSlot(context).GetFloat(arguments);
	}

	public override void SetValue(long value, ModernExpressionContext context, long[] arguments)
	{
		RequireInteger();
		GetSlot(context).SetInt(arguments, value);
	}

	public override void SetValue(string value, ModernExpressionContext context, long[] arguments)
	{
		RequireString();
		GetSlot(context).SetString(arguments, value);
	}

	public override void SetValue(double value, ModernExpressionContext context, long[] arguments)
	{
		RequireFloat();
		GetSlot(context).SetFloat(arguments, value);
	}

	public override long PlusValue(long value, ModernExpressionContext context, long[] arguments)
	{
		RequireInteger();
		var slot = GetSlot(context);
		var next = checked(slot.GetInt(arguments) + value);
		slot.SetInt(arguments, next);
		return next;
	}

	public override double PlusValue(double value, ModernExpressionContext context, long[] arguments)
	{
		RequireFloat();
		var slot = GetSlot(context);
		var next = slot.GetFloat(arguments) + value;
		slot.SetFloat(arguments, next);
		return next;
	}

	public override int GetLength()
	{
		long total = 1;
		for (int i = 0; i < definition.Lengths.Count; i++)
			total *= definition.Lengths[i];
		return (int)total;
	}

	public override bool IsReference
	{
		get { return (definition.Attributes & ModernUserVariableAttributes.Reference) != 0; }
	}

	public override bool IsOut
	{
		get { return (definition.Attributes & ModernUserVariableAttributes.Out) != 0; }
	}

	ModernPrivateVariableSlot GetSlot(ModernExpressionContext context)
	{
		if (context?.PrivateVariables == null)
			throw new InvalidOperationException($"{Name} is not bound to a private variable store.");
		return context.PrivateVariables.GetSlot(Name);
	}

	void RequireInteger()
	{
		if (!IsInteger)
			throw new InvalidOperationException($"{Name} cannot be used as integer.");
	}

	void RequireString()
	{
		if (!IsString)
			throw new InvalidOperationException($"{Name} cannot be used as string.");
	}

	void RequireFloat()
	{
		if (!IsFloat)
			throw new InvalidOperationException($"{Name} cannot be used as float.");
	}

	static VariableDescriptor CreateDescriptor(ModernUserVariableDefinition definition)
	{
		var attributes = VariableAttribute.Local | VariableAttribute.Extended;
		if ((definition.Attributes & ModernUserVariableAttributes.Const) != 0)
			attributes |= VariableAttribute.Unchangeable;
		return new VariableDescriptor
		{
			Code = VariableCode.__NULL__,
			Kind = definition.Kind,
			Dimension = definition.Dimension,
			Attributes = attributes
		};
	}
}

internal sealed class ModernInt1DVariableToken : ModernVariableToken
{
	readonly SparseArray<long> array;

	public ModernInt1DVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
		array = variableData.DataIntegerArray[CodeIndex];
	}

	public override long GetIntValue(ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		CheckIndex(index, array.Length);
		return array[index];
	}

	public override void SetValue(long value, ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		CheckIndex(index, array.Length);
		array[index] = value;
	}

	public override long PlusValue(long value, ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		CheckIndex(index, array.Length);
		array[index] = checked(array[index] + value);
		return array[index];
	}

	public override int GetLength()
	{
		return array.Length;
	}

	public override object GetArray(ModernExpressionContext context)
	{
		return array;
	}
}

internal sealed class ModernStringScalarVariableToken : ModernVariableToken
{
	public ModernStringScalarVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
	}

	public override string GetStrValue(ModernExpressionContext context, long[] arguments)
	{
		return VariableData.DataString[CodeIndex] ?? "";
	}

	public override void SetValue(string value, ModernExpressionContext context, long[] arguments)
	{
		VariableData.DataString[CodeIndex] = value ?? "";
	}
}

internal sealed class ModernString1DVariableToken : ModernVariableToken
{
	readonly SparseArray<string> array;

	public ModernString1DVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
		array = variableData.DataStringArray[CodeIndex];
	}

	public override string GetStrValue(ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		CheckIndex(index, array.Length);
		return array[index] ?? "";
	}

	public override void SetValue(string value, ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		CheckIndex(index, array.Length);
		array[index] = value ?? "";
	}

	public override int GetLength()
	{
		return array.Length;
	}

	public override object GetArray(ModernExpressionContext context)
	{
		return array;
	}
}

internal sealed class ModernFloatScalarVariableToken : ModernVariableToken
{
	public ModernFloatScalarVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
	}

	public override double GetFloatValue(ModernExpressionContext context, long[] arguments)
	{
		return VariableData.DataFloat[CodeIndex];
	}

	public override void SetValue(double value, ModernExpressionContext context, long[] arguments)
	{
		VariableData.DataFloat[CodeIndex] = value;
	}

	public override double PlusValue(double value, ModernExpressionContext context, long[] arguments)
	{
		VariableData.DataFloat[CodeIndex] += value;
		return VariableData.DataFloat[CodeIndex];
	}
}

internal sealed class ModernFloat1DVariableToken : ModernVariableToken
{
	readonly SparseArray<double> array;

	public ModernFloat1DVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
		array = variableData.DataFloatArray[CodeIndex];
	}

	public override double GetFloatValue(ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		CheckIndex(index, array.Length);
		return array[index];
	}

	public override void SetValue(double value, ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		CheckIndex(index, array.Length);
		array[index] = value;
	}

	public override double PlusValue(double value, ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		CheckIndex(index, array.Length);
		array[index] += value;
		return array[index];
	}

	public override int GetLength()
	{
		return array.Length;
	}

	public override object GetArray(ModernExpressionContext context)
	{
		return array;
	}
}

internal abstract class ModernLegacyCharaVariableToken : ModernVariableToken
{
	protected ModernLegacyCharaVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
	}

	protected CharacterData GetChara(long[] arguments)
	{
		if (arguments == null || arguments.Length == 0)
			throw new ArgumentException($"{Name} needs a character index.");
		var legacyData = GlobalStatic.VariableData ?? GlobalStatic.VEvaluator?.VariableData;
		if (legacyData == null)
			throw new InvalidOperationException($"{Name} requires legacy character data.");
		long index = arguments[0];
		if (index < 0 || index >= legacyData.CharacterList.Count)
			throw new IndexOutOfRangeException($"{Name}: character index {index} is outside 0..{legacyData.CharacterList.Count - 1}.");
		return legacyData.CharacterList[(int)index];
	}

	protected static int CheckedIndex(long value, int length, string name, string label)
	{
		if (value < 0 || value >= length)
			throw new IndexOutOfRangeException($"{name}: {label} index {value} is outside 0..{length - 1}.");
		return (int)value;
	}

	public override int GetLength()
	{
		var legacyData = GlobalStatic.VariableData ?? GlobalStatic.VEvaluator?.VariableData;
		return legacyData?.CharacterList.Count ?? 0;
	}
}

internal sealed class ModernLegacyCharaIntScalarVariableToken : ModernLegacyCharaVariableToken
{
	public ModernLegacyCharaIntScalarVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
	}

	public override long GetIntValue(ModernExpressionContext context, long[] arguments)
	{
		var chara = GetChara(arguments);
		return chara.DataInteger[CodeIndex];
	}

	public override void SetValue(long value, ModernExpressionContext context, long[] arguments)
	{
		var chara = GetChara(arguments);
		chara.DataInteger[CodeIndex] = value;
	}
}

internal sealed class ModernLegacyCharaStringScalarVariableToken : ModernLegacyCharaVariableToken
{
	public ModernLegacyCharaStringScalarVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
	}

	public override string GetStrValue(ModernExpressionContext context, long[] arguments)
	{
		var chara = GetChara(arguments);
		return chara.DataString[CodeIndex] ?? "";
	}

	public override void SetValue(string value, ModernExpressionContext context, long[] arguments)
	{
		var chara = GetChara(arguments);
		chara.DataString[CodeIndex] = value ?? "";
	}
}

internal sealed class ModernLegacyCharaInt1DVariableToken : ModernLegacyCharaVariableToken
{
	public ModernLegacyCharaInt1DVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
	}

	public override long GetIntValue(ModernExpressionContext context, long[] arguments)
	{
		if (arguments == null || arguments.Length < 2)
			throw new ArgumentException($"{Name} needs character and array indices.");
		var array = GetChara(arguments).DataIntegerArray[CodeIndex];
		return array[CheckedIndex(arguments[1], array.Length, Name, "array")];
	}

	public override void SetValue(long value, ModernExpressionContext context, long[] arguments)
	{
		if (arguments == null || arguments.Length < 2)
			throw new ArgumentException($"{Name} needs character and array indices.");
		var array = GetChara(arguments).DataIntegerArray[CodeIndex];
		array[CheckedIndex(arguments[1], array.Length, Name, "array")] = value;
	}

	public override long PlusValue(long value, ModernExpressionContext context, long[] arguments)
	{
		if (arguments == null || arguments.Length < 2)
			throw new ArgumentException($"{Name} needs character and array indices.");
		var array = GetChara(arguments).DataIntegerArray[CodeIndex];
		int index = CheckedIndex(arguments[1], array.Length, Name, "array");
		array[index] = checked(array[index] + value);
		return array[index];
	}

	public override object GetArray(ModernExpressionContext context)
	{
		return Array.Empty<long>();
	}
}

internal sealed class ModernLegacyCharaString1DVariableToken : ModernLegacyCharaVariableToken
{
	public ModernLegacyCharaString1DVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
	}

	public override string GetStrValue(ModernExpressionContext context, long[] arguments)
	{
		if (arguments == null || arguments.Length < 2)
			throw new ArgumentException($"{Name} needs character and array indices.");
		var array = GetChara(arguments).DataStringArray[CodeIndex];
		return array[CheckedIndex(arguments[1], array.Length, Name, "array")] ?? "";
	}

	public override void SetValue(string value, ModernExpressionContext context, long[] arguments)
	{
		if (arguments == null || arguments.Length < 2)
			throw new ArgumentException($"{Name} needs character and array indices.");
		var array = GetChara(arguments).DataStringArray[CodeIndex];
		array[CheckedIndex(arguments[1], array.Length, Name, "array")] = value ?? "";
	}

	public override object GetArray(ModernExpressionContext context)
	{
		return Array.Empty<string>();
	}
}

internal sealed class ModernLegacyCharaInt2DVariableToken : ModernLegacyCharaVariableToken
{
	public ModernLegacyCharaInt2DVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
	}

	public override long GetIntValue(ModernExpressionContext context, long[] arguments)
	{
		if (arguments == null || arguments.Length < 3)
			throw new ArgumentException($"{Name} needs character and two array indices.");
		var array = GetChara(arguments).DataIntegerArray2D[CodeIndex];
		int index1 = CheckedIndex(arguments[1], array.GetLength(0), Name, "array1");
		int index2 = CheckedIndex(arguments[2], array.GetLength(1), Name, "array2");
		return array[index1, index2];
	}

	public override void SetValue(long value, ModernExpressionContext context, long[] arguments)
	{
		if (arguments == null || arguments.Length < 3)
			throw new ArgumentException($"{Name} needs character and two array indices.");
		var array = GetChara(arguments).DataIntegerArray2D[CodeIndex];
		int index1 = CheckedIndex(arguments[1], array.GetLength(0), Name, "array1");
		int index2 = CheckedIndex(arguments[2], array.GetLength(1), Name, "array2");
		array[index1, index2] = value;
	}

	public override long PlusValue(long value, ModernExpressionContext context, long[] arguments)
	{
		if (arguments == null || arguments.Length < 3)
			throw new ArgumentException($"{Name} needs character and two array indices.");
		var array = GetChara(arguments).DataIntegerArray2D[CodeIndex];
		int index1 = CheckedIndex(arguments[1], array.GetLength(0), Name, "array1");
		int index2 = CheckedIndex(arguments[2], array.GetLength(1), Name, "array2");
		array[index1, index2] = checked(array[index1, index2] + value);
		return array[index1, index2];
	}
}

internal sealed class ModernLocalInt1DVariableToken : ModernVariableToken
{
	readonly SparseArray<long> fallbackArray;

	public ModernLocalInt1DVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
		fallbackArray = variableData.DataIntegerArray[CodeIndex];
	}

	public override long GetIntValue(ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		var array = GetContextArray(context);
		if (array != null)
		{
			CheckIndex(index, array.Length);
			return array[index];
		}

		CheckIndex(index, fallbackArray.Length);
		return fallbackArray[index];
	}

	public override void SetValue(long value, ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		var array = GetContextArray(context);
		if (array != null)
		{
			CheckIndex(index, array.Length);
			array[index] = value;
			return;
		}

		CheckIndex(index, fallbackArray.Length);
		fallbackArray[index] = value;
	}

	public override int GetLength()
	{
		return fallbackArray.Length;
	}

	public override object GetArray(ModernExpressionContext context)
	{
		var array = GetContextArray(context);
		if (array != null)
			return array;
		return fallbackArray;
	}

	long[] GetContextArray(ModernExpressionContext context)
	{
		var executionContext = context?.ExecutionContext;
		if (executionContext == null)
			return null;
		return Code switch
		{
			VariableCode.LOCAL => executionContext.LocalIntegers,
			VariableCode.ARG => executionContext.ArgIntegers,
			_ => null,
		};
	}
}

internal sealed class ModernLocalString1DVariableToken : ModernVariableToken
{
	readonly SparseArray<string> fallbackArray;

	public ModernLocalString1DVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
		fallbackArray = variableData.DataStringArray[CodeIndex];
	}

	public override string GetStrValue(ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		var array = GetContextArray(context);
		if (array != null)
		{
			CheckIndex(index, array.Length);
			return array[index] ?? "";
		}

		CheckIndex(index, fallbackArray.Length);
		return fallbackArray[index] ?? "";
	}

	public override void SetValue(string value, ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		var array = GetContextArray(context);
		if (array != null)
		{
			CheckIndex(index, array.Length);
			array[index] = value ?? "";
			return;
		}

		CheckIndex(index, fallbackArray.Length);
		fallbackArray[index] = value ?? "";
	}

	public override int GetLength()
	{
		return fallbackArray.Length;
	}

	public override object GetArray(ModernExpressionContext context)
	{
		var array = GetContextArray(context);
		if (array != null)
			return array;
		return fallbackArray;
	}

	string[] GetContextArray(ModernExpressionContext context)
	{
		var executionContext = context?.ExecutionContext;
		if (executionContext == null)
			return null;
		return Code switch
		{
			VariableCode.LOCALS => executionContext.LocalStrings,
			VariableCode.ARGS => executionContext.ArgStrings,
			_ => null,
		};
	}
}

internal sealed class ModernLocalFloat1DVariableToken : ModernVariableToken
{
	readonly SparseArray<double> fallbackArray;

	public ModernLocalFloat1DVariableToken(string name, VariableCode code, ModernVariableData variableData)
		: base(name, code, variableData)
	{
		fallbackArray = variableData.DataFloatArray[CodeIndex];
	}

	public override double GetFloatValue(ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		var array = GetContextArray(context);
		if (array != null)
		{
			CheckIndex(index, array.Length);
			return array[index];
		}

		CheckIndex(index, fallbackArray.Length);
		return fallbackArray[index];
	}

	public override void SetValue(double value, ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		var array = GetContextArray(context);
		if (array != null)
		{
			CheckIndex(index, array.Length);
			array[index] = value;
			return;
		}

		CheckIndex(index, fallbackArray.Length);
		fallbackArray[index] = value;
	}

	public override double PlusValue(double value, ModernExpressionContext context, long[] arguments)
	{
		var index = FirstIndex(arguments);
		var array = GetContextArray(context);
		if (array != null)
		{
			CheckIndex(index, array.Length);
			array[index] += value;
			return array[index];
		}

		CheckIndex(index, fallbackArray.Length);
		fallbackArray[index] += value;
		return fallbackArray[index];
	}

	public override int GetLength()
	{
		return fallbackArray.Length;
	}

	public override object GetArray(ModernExpressionContext context)
	{
		var array = GetContextArray(context);
		if (array != null)
			return array;
		return fallbackArray;
	}

	double[] GetContextArray(ModernExpressionContext context)
	{
		var executionContext = context?.ExecutionContext;
		if (executionContext == null)
			return null;
		return Code switch
		{
			VariableCode.LOCALF => executionContext.LocalFloats,
			VariableCode.ARGF => executionContext.ArgFloats,
			_ => null,
		};
	}
}
