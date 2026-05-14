using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Variables;

internal sealed class ModernPrivateVariableStore
{
	readonly Dictionary<string, ModernPrivateVariableSlot> slots = new(StringComparer.OrdinalIgnoreCase);
	readonly ModernPrivateVariableStore parent;

	public ModernPrivateVariableStore(IEnumerable<ModernUserVariableDefinition> definitions, ModernPrivateVariableStore parent = null)
	{
		this.parent = parent;
		if (definitions == null)
			return;
		foreach (var definition in definitions)
			slots[definition.Name] = new ModernPrivateVariableSlot(definition);
	}

	public ModernPrivateVariableSlot GetSlot(string name)
	{
		if (slots.TryGetValue(name, out var slot))
			return slot;
		if (parent != null)
			return parent.GetSlot(name);
		throw new KeyNotFoundException($"Unknown private variable: {name}");
	}

	public void SetReference(string name, ModernVariableReference reference)
	{
		GetSlot(name).SetReference(reference);
	}
}

internal sealed class ModernPrivateVariableSlot
{
	readonly SparseArray<long> integers;
	readonly SparseArray<string> strings;
	readonly SparseArray<double> floats;
	ModernVariableReference reference;

	public ModernPrivateVariableSlot(ModernUserVariableDefinition definition)
	{
		Definition = definition ?? throw new ArgumentNullException(nameof(definition));
		Length = CalculateLength(definition.Lengths);
		if (definition.IsString)
		{
			strings = new SparseArray<string>("");
			strings.Length = Length;
		}
		else if (definition.IsFloat)
		{
			floats = new SparseArray<double>();
			floats.Length = Length;
		}
		else
		{
			integers = new SparseArray<long>();
			integers.Length = Length;
		}

		ApplyDefaults(definition);
	}

	public ModernUserVariableDefinition Definition { get; }
	public int Length { get; }
	public bool IsReference { get { return (Definition.Attributes & ModernUserVariableAttributes.Reference) != 0; } }

	public void SetReference(ModernVariableReference reference)
	{
		if (!IsReference)
			throw new InvalidOperationException($"{Definition.Name} is not a reference variable.");
		if (reference != null && reference.EraType != ReadEraType(Definition))
			throw new InvalidOperationException($"{Definition.Name} reference type does not match.");
		this.reference = reference;
	}

	public long GetInt(long[] indices)
	{
		if (reference != null)
			return reference.GetIntValue();
		EnsureNotUnboundReference();
		return integers[Flatten(indices)];
	}

	public void SetInt(long[] indices, long value)
	{
		if (reference != null)
		{
			reference.SetValue(value);
			return;
		}
		EnsureNotUnboundReference();
		integers[Flatten(indices)] = value;
	}

	public string GetString(long[] indices)
	{
		if (reference != null)
			return reference.GetStrValue();
		EnsureNotUnboundReference();
		return strings[Flatten(indices)] ?? "";
	}

	public void SetString(long[] indices, string value)
	{
		if (reference != null)
		{
			reference.SetValue(value);
			return;
		}
		EnsureNotUnboundReference();
		strings[Flatten(indices)] = value ?? "";
	}

	public double GetFloat(long[] indices)
	{
		if (reference != null)
			return reference.GetFloatValue();
		EnsureNotUnboundReference();
		return floats[Flatten(indices)];
	}

	public void SetFloat(long[] indices, double value)
	{
		if (reference != null)
		{
			reference.SetValue(value);
			return;
		}
		EnsureNotUnboundReference();
		floats[Flatten(indices)] = value;
	}

	void EnsureNotUnboundReference()
	{
		if (IsReference)
			throw new InvalidOperationException($"{Definition.Name} reference is not bound.");
	}

	long Flatten(long[] indices)
	{
		int expected = Definition.Dimension == VariableDimension.Scalar ? 0 : (int)Definition.Dimension;
		if (expected == 0)
			return 0;
		if (indices == null || indices.Length != expected)
			throw new ArgumentException($"{Definition.Name} needs {expected} indices.");

		long flat = 0;
		long stride = 1;
		for (int i = expected - 1; i >= 0; i--)
		{
			long index = indices[i];
			int length = Definition.Lengths[i];
			if (index < 0 || index >= length)
				throw new IndexOutOfRangeException($"{Definition.Name}: index {index} is outside 0..{length - 1}.");
			flat += index * stride;
			stride *= length;
		}

		return flat;
	}

	void ApplyDefaults(ModernUserVariableDefinition definition)
	{
		var defaults = definition.DefaultValues;
		if (defaults.Count > Length)
			throw new FormatException($"{definition.Name} has more initial values than storage slots.");
		for (int i = 0; i < defaults.Count; i++)
		{
			if (definition.IsString && defaults[i] is SingleStrTerm strTerm)
				strings[i] = strTerm.Str;
			else if (definition.IsFloat)
			{
				if (defaults[i] is SingleFloatTerm floatTerm)
					floats[i] = floatTerm.Float;
				else if (defaults[i] is SingleLongTerm longTerm)
					floats[i] = longTerm.Int;
				else
					throw new FormatException($"{definition.Name} has a non-float default value.");
			}
			else if (defaults[i] is SingleLongTerm longTerm)
				integers[i] = longTerm.Int;
			else
				throw new FormatException($"{definition.Name} has an incompatible default value.");
		}
	}

	static int CalculateLength(IReadOnlyList<int> lengths)
	{
		if (lengths == null || lengths.Count == 0)
			return 1;
		long total = 1;
		for (int i = 0; i < lengths.Count; i++)
			total *= lengths[i];
		if (total <= 0 || total > int.MaxValue)
			throw new FormatException("Private variable length is outside Int32 range.");
		return (int)total;
	}

	static EraType ReadEraType(ModernUserVariableDefinition definition)
	{
		if (definition.IsString)
			return EraType.String;
		if (definition.IsFloat)
			return EraType.Float;
		return EraType.Integer;
	}
}
