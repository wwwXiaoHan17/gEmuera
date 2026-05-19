path = 'Scripts/Emuera/GameData/Function/Creator.Method.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

marker = (
    '\t\t\t\tcatch { }\n'
    '\t\t\t\treturn defaultValue;\n'
    '\t\t\t\t}\n'
    '\t\t\t}\n'
    '\n'
    '\n'
    '\t\t\t#endregion\n'
    '\t\t}\n'
    '}\n'
)

replacement = (
    '\t\t\t\tcatch { }\n'
    '\t\t\t\treturn defaultValue;\n'
    '\t\t\t\t}\n'
    '\t\t\t}\n'
    '\n'
    '\t\t\tpublic sealed class GetVarFMethod : FunctionMethod\n'
    '\t\t\t\t{\n'
    '\t\t\t\tpublic GetVarFMethod()\n'
    '\t\t\t\t\t{\n'
    '\t\t\t\t\tReturnType = typeof(double);\n'
    '\t\t\s\t\targumentTypeArray = null;\n'
    '\t\t\s\t\tCanRestructure = false;\n'
    '\t\t\s\t\t}\n'
    '\t\t\s\t\tpublic override string CheckArgumentType(string name, IOperandTerm[] arguments)\n'
    '\t\t\s\t\t{\n'
    '\t\t\s\t\tif (arguments.Length < 1)\n'
    '\t\t\s\t\t\treturn name + "関数には少なくとも1つの引数が必要です";\n'
    '\t\t\s\t\tif (arguments.Length > 2)\n'
    '\t\t\s\t\t\treturn name + "関数の引数が多すぎます";\n'
    '\t\t\s\t\tif (arguments[0] == null || arguments[0].GetOperandType() != typeof(string))\n'
    '\t\t\s\t\t\treturn name + "関数の1番目の引数の型が正しくありません";\n'
    '\t\t\s\t\treturn null;\n'
    '\t\t\s\t\t}\n'
    '\t\t\s\t\tpublic override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)\n'
    '\t\t\s\t\t{\n'
    '\t\t\s\t\tstring name = arguments[0].GetStrValue(exm) ?? "";\n'
    '\t\t\s\t\tVariableTerm varTerm = null;\n'
    '\t\t\s\t\ttry\n'
    '\t\t\s\t\t{\n'
    '\t\t\s\t\t\tWordCollection wc = LexicalAnalyzer.Analyse(new StringStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);\n'
    '\t\t\s\t\t\tIOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);\n'
    '\t\t\s\t\t\tvarTerm = term as VariableTerm;\n'
    '\t\t\s\t\t}\n'
    '\t\t\s\t\tcatch { }\n'
    '\t\t\s\t\tif (varTerm == null || varTerm.Identifier == null)\n'
    '\t\t\s\t\t{\n'
    '\t\t\s\t\t\tif (arguments.Length > 1 && arguments[1] != null)\n'
    '\t\t\s\t\t\t\treturn new SingleTerm(arguments[1].GetFloatValue(exm));\n'
    '\t\t\s\t\t\treturn new SingleTerm(0.0);\n'
    '\t\t\s\t\t}\n'
    '\t\t\s\t\ttry\n'
    '\t\t\s\t\t{\n'
    '\t\t\s\t\t\tif (varTerm.Identifier.IsFloat)\n'
    '\t\t\s\t\t\t\treturn new SingleTerm(varTerm.GetFloatValue(exm));\n'
    '\t\t\s\t\t}\n'
    '\t\t\s\t\tcatch { }\n'
    '\t\t\s\t\tif (arguments.Length > 1 && arguments[1] != null)\n'
    '\t\t\s\t\t\treturn new SingleTerm(arguments[1].GetFloatValue(exm));\n'
    '\t\t\s\t\treturn new SingleTerm(0.0);\n'
    '\t\t\s\t\t}\n'
    '\t\t\s\t}\n'
    '\n'
    '\t\t\s\t#endregion\n'
    '\t\t}\n'
    '}\n'
)

if marker in content:
    content = content.replace(marker, replacement)
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
    print('Success')
else:
    print('Pattern not found')
    import os
    with open(path, 'rb') as f:
        f.seek(-200, os.SEEK_END)
        print(repr(f.read()))
