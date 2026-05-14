using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Variables;

namespace MinorShift.Emuera.Modern.Script;

[System.Flags]
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

[System.Flags]
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
}

internal readonly struct VariableDescriptor
{
    public VariableCode Code { get; init; }
    public VariableKind Kind { get; init; }
    public VariableDimension Dimension { get; init; }
    public VariableAttribute Attributes { get; init; }

    public bool IsInteger => (Kind & VariableKind.Integer) != 0;
    public bool IsString => (Kind & VariableKind.String) != 0;
    public bool IsFloat => (Kind & VariableKind.Float) != 0;

    public static VariableDescriptor FromCode(VariableCode code, string name)
    {
        if (VariableDescriptorTable.TryGetDescriptorByCode(code, out var registered))
            return registered;

        var kind = VariableKind.Integer;
        if ((code & VariableCode.__STRING__) != 0)
            kind = VariableKind.String;

        var dim = VariableDimension.Scalar;
        if ((code & VariableCode.__ARRAY_3D__) != 0)
            dim = VariableDimension.Array3D;
        else if ((code & VariableCode.__ARRAY_2D__) != 0)
            dim = VariableDimension.Array2D;
        else if ((code & VariableCode.__ARRAY_1D__) != 0)
            dim = VariableDimension.Array1D;

        var attr = VariableAttribute.None;
        if ((code & VariableCode.__CAN_FORBID__) != 0)
            attr |= VariableAttribute.CanForbid;
        if ((code & VariableCode.__CHARACTER_DATA__) != 0)
            attr |= VariableAttribute.CharacterData;
        if ((code & VariableCode.__GLOBAL__) != 0)
            attr |= VariableAttribute.Global;
        if ((code & VariableCode.__LOCAL__) != 0)
            attr |= VariableAttribute.Local;
        if ((code & VariableCode.__UNCHANGEABLE__) != 0)
            attr |= VariableAttribute.Unchangeable;
        if ((code & VariableCode.__EXTENDED__) != 0)
            attr |= VariableAttribute.Extended;
        if ((code & VariableCode.__SAVE_EXTENDED__) != 0)
            attr |= VariableAttribute.Save;

        return new VariableDescriptor
        {
            Code = code,
            Kind = kind,
            Dimension = dim,
            Attributes = attr
        };
    }
}

internal static class VariableDescriptorTable
{
    private static readonly Dictionary<string, VariableDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<VariableCode, VariableDescriptor> _codeIndex = new();

    static VariableDescriptorTable()
    {
        Register("DAY", VariableCode.DAY, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("MONEY", VariableCode.MONEY, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("ITEM", VariableCode.ITEM, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("FLAG", VariableCode.FLAG, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("TFLAG", VariableCode.TFLAG, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("UP", VariableCode.UP, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("DOWN", VariableCode.DOWN, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("EJAC", VariableCode.EJAC, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("PALAMLV", VariableCode.PALAMLV, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.None);
        Register("EXPLV", VariableCode.EXPLV, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.None);
        Register("RESULT", VariableCode.RESULT, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.None);
		Register("RESULTF", VariableCode.RESULTF, VariableKind.Float, VariableDimension.Scalar, VariableAttribute.Extended);
        Register("COUNT", VariableCode.COUNT, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("TARGET", VariableCode.TARGET, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.None);
        Register("ASSI", VariableCode.ASSI, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("MASTER", VariableCode.MASTER, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("NOITEM", VariableCode.NOITEM, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("LOSEBASE", VariableCode.LOSEBASE, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("SELECTCOM", VariableCode.SELECTCOM, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.None);
        Register("ASSIPLAY", VariableCode.ASSIPLAY, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("PREVCOM", VariableCode.PREVCOM, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("TIME", VariableCode.TIME, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("ITEMSALES", VariableCode.ITEMSALES, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("PLAYER", VariableCode.PLAYER, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("NEXTCOM", VariableCode.NEXTCOM, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("PBAND", VariableCode.PBAND, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("BOUGHT", VariableCode.BOUGHT, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("A", VariableCode.A, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("B", VariableCode.B, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("C", VariableCode.C, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("D", VariableCode.D, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("E", VariableCode.E, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("F", VariableCode.F, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("G", VariableCode.G, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("H", VariableCode.H, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("I", VariableCode.I, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("J", VariableCode.J, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("K", VariableCode.K, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("L", VariableCode.L, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("M", VariableCode.M, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("N", VariableCode.N, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("O", VariableCode.O, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("P", VariableCode.P, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("Q", VariableCode.Q, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("R", VariableCode.R, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("S", VariableCode.S, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("T", VariableCode.T, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("U", VariableCode.U, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("V", VariableCode.V, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("W", VariableCode.W, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("X", VariableCode.X, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("Y", VariableCode.Y, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("Z", VariableCode.Z, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid);

        Register("ITEMPRICE", VariableCode.ITEMPRICE, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.Unchangeable | VariableAttribute.Extended);
        Register("LOCAL", VariableCode.LOCAL, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.Local | VariableAttribute.Extended);
		Register("ARG", VariableCode.ARG, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.Local | VariableAttribute.Extended);
		Register("LOCALF", VariableCode.LOCALF, VariableKind.Float, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.Local | VariableAttribute.Extended);
		Register("ARGF", VariableCode.ARGF, VariableKind.Float, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.Local | VariableAttribute.Extended);
        Register("GLOBAL", VariableCode.GLOBAL, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.Global | VariableAttribute.Extended);
        Register("RANDDATA", VariableCode.RANDDATA, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.Save | VariableAttribute.Extended);

        Register("SAVESTR", VariableCode.SAVESTR, VariableKind.String, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("STR", VariableCode.STR, VariableKind.String, VariableDimension.Array1D, VariableAttribute.CanForbid);
        Register("RESULTS", VariableCode.RESULTS, VariableKind.String, VariableDimension.Array1D, VariableAttribute.None);
        Register("LOCALS", VariableCode.LOCALS, VariableKind.String, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.Local | VariableAttribute.Extended);
        Register("ARGS", VariableCode.ARGS, VariableKind.String, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.Local | VariableAttribute.Extended);
        Register("GLOBALS", VariableCode.GLOBALS, VariableKind.String, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.Global | VariableAttribute.Extended);
        Register("TSTR", VariableCode.TSTR, VariableKind.String, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.Save | VariableAttribute.Extended);

        Register("SAVEDATA_TEXT", VariableCode.SAVEDATA_TEXT, VariableKind.String, VariableDimension.Scalar, VariableAttribute.Extended);

        Register("ISASSI", VariableCode.ISASSI, VariableKind.Integer, VariableDimension.Scalar, VariableAttribute.CharacterData);
        Register("NO", VariableCode.NO, VariableKind.Integer, VariableDimension.Scalar, VariableAttribute.CharacterData);

        Register("BASE", VariableCode.BASE, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.CharacterData);
        Register("MAXBASE", VariableCode.MAXBASE, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.CharacterData);
        Register("ABL", VariableCode.ABL, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.CharacterData);
        Register("TALENT", VariableCode.TALENT, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.CharacterData);
        Register("EXP", VariableCode.EXP, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.CharacterData);
        Register("MARK", VariableCode.MARK, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.CharacterData);
        Register("PALAM", VariableCode.PALAM, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.CharacterData);
        Register("SOURCE", VariableCode.SOURCE, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.CharacterData);
        Register("EX", VariableCode.EX, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.CharacterData);
        Register("CFLAG", VariableCode.CFLAG, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.CharacterData);
        Register("JUEL", VariableCode.JUEL, VariableKind.Integer, VariableDimension.Array1D, VariableAttribute.CanForbid | VariableAttribute.CharacterData);
    }

    private static void Register(string name, VariableCode code, VariableKind kind,
        VariableDimension dim, VariableAttribute attr)
    {
        var descriptor = new VariableDescriptor
        {
            Code = code, Kind = kind, Dimension = dim, Attributes = attr
        };
        _descriptors[name] = descriptor;
        _codeIndex[code] = descriptor;
    }

    public static bool TryGetDescriptor(string name, out VariableDescriptor descriptor)
    {
        return _descriptors.TryGetValue(name, out descriptor);
    }

    public static bool TryGetDescriptorByCode(VariableCode code, out VariableDescriptor descriptor)
    {
        return _codeIndex.TryGetValue(code, out descriptor);
    }

    public static VariableDescriptor GetDescriptorByCode(VariableCode code)
    {
        if (_codeIndex.TryGetValue(code, out var descriptor))
            return descriptor;
        return VariableDescriptor.FromCode(code, "");
    }
}
