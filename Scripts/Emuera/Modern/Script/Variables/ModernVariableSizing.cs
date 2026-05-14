namespace MinorShift.Emuera.Modern.Script.Variables;

internal sealed class ModernVariableSizing
{
	public static ModernVariableSizing Default { get; } = new ModernVariableSizing();

	public int IntegerScalarCount { get; init; } = (int)VariableCode.__COUNT_INTEGER__;
	public int StringScalarCount { get; init; } = (int)VariableCode.__COUNT_STRING__;
	public int FloatScalarCount { get; init; } = 2;
	public int IntegerArrayCount { get; init; } = (int)VariableCode.__COUNT_INTEGER_ARRAY__;
	public int StringArrayCount { get; init; } = (int)VariableCode.__COUNT_STRING_ARRAY__;
	public int FloatArrayCount { get; init; } = (int)VariableCode.__COUNT_FLOAT_ARRAY__;
	public int DefaultArrayLength { get; init; } = 1000;
	public int DefaultStringArrayLength { get; init; } = 100;
	public int DefaultFloatArrayLength { get; init; } = 1000;
}
