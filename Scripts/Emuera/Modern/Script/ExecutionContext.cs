namespace MinorShift.Emuera.Modern.Script;

internal sealed class ExecutionContext
{
	public ExecutionContext(
		ExecutionContext parent,
		int localIntegerLength,
		int localStringLength,
		int localFloatLength,
		int argIntegerLength,
		int argStringLength,
		int argFloatLength)
	{
		Parent = parent;
		LocalIntegers = new long[NormalizeLength(localIntegerLength)];
		LocalStrings = new string[NormalizeLength(localStringLength)];
		LocalFloats = new double[NormalizeLength(localFloatLength)];
		ArgIntegers = new long[NormalizeLength(argIntegerLength)];
		ArgStrings = new string[NormalizeLength(argStringLength)];
		ArgFloats = new double[NormalizeLength(argFloatLength)];
	}

	public ExecutionContext Parent { get; }
	public long[] LocalIntegers { get; }
	public string[] LocalStrings { get; }
	public double[] LocalFloats { get; }
	public long[] ArgIntegers { get; set; }
	public string[] ArgStrings { get; set; }
	public double[] ArgFloats { get; set; }
	public int CurrentVariadicArgCount { get; set; }

	static int NormalizeLength(int length)
	{
		return length > 0 ? length : 0;
	}
}
