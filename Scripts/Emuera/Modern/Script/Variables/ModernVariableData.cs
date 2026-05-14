using System.Collections.Generic;

namespace MinorShift.Emuera.Modern.Script.Variables;

internal sealed class ModernVariableData
{
	public ModernVariableData()
		: this(ModernVariableSizing.Default)
	{
	}

	public ModernVariableData(ModernVariableSizing sizing)
	{
		sizing ??= ModernVariableSizing.Default;

		DataInteger = new long[sizing.IntegerScalarCount];
		DataString = new string[sizing.StringScalarCount];
		DataFloat = new double[sizing.FloatScalarCount];

		DataIntegerArray = new SparseArray<long>[sizing.IntegerArrayCount];
		for (int i = 0; i < DataIntegerArray.Length; i++)
		{
			DataIntegerArray[i] = new SparseArray<long>();
			DataIntegerArray[i].Length = sizing.DefaultArrayLength;
		}

		DataStringArray = new SparseArray<string>[sizing.StringArrayCount];
		for (int i = 0; i < DataStringArray.Length; i++)
		{
			DataStringArray[i] = new SparseArray<string>("");
			DataStringArray[i].Length = sizing.DefaultStringArrayLength;
		}

		DataFloatArray = new SparseArray<double>[sizing.FloatArrayCount];
		for (int i = 0; i < DataFloatArray.Length; i++)
		{
			DataFloatArray[i] = new SparseArray<double>();
			DataFloatArray[i].Length = sizing.DefaultFloatArrayLength;
		}
	}

	public long[] DataInteger { get; }
	public string[] DataString { get; }
	public double[] DataFloat { get; }
	public SparseArray<long>[] DataIntegerArray { get; }
	public SparseArray<string>[] DataStringArray { get; }
	public SparseArray<double>[] DataFloatArray { get; }
	public Dictionary<string, Dictionary<string, string>> DataStringMaps { get; } = new();

	public double RESULTF
	{
		get { return DataFloat[(int)(VariableCode.RESULTF & VariableCode.__LOWERCASE__)]; }
		set { DataFloat[(int)(VariableCode.RESULTF & VariableCode.__LOWERCASE__)] = value; }
	}

	public SparseArray<double> LOCALF
	{
		get { return DataFloatArray[(int)(VariableCode.LOCALF & VariableCode.__LOWERCASE__)]; }
	}

	public SparseArray<double> ARGF
	{
		get { return DataFloatArray[(int)(VariableCode.ARGF & VariableCode.__LOWERCASE__)]; }
	}
}
