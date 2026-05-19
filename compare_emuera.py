import os
import subprocess
import sys

mapping = {
    "Scripts/Emuera/GameData/Expression/ExpressionParser.cs": "Emuera/Runtime/Script/Statements/Expression/ExpressionParser.cs",
    "Scripts/Emuera/GameData/Expression/Term.cs": "Emuera/Runtime/Script/Statements/Expression/Term.cs",
    "Scripts/Emuera/GameData/Expression/OperatorMethod.cs": "Emuera/Runtime/Script/Statements/Expression/OperatorMethod.cs",
    "Scripts/Emuera/GameData/Expression/OperatorCode.cs": "Emuera/Runtime/Script/Statements/Expression/OperatorCode.cs",
    "Scripts/Emuera/GameData/Expression/ExpressionMediator.cs": "Emuera/Runtime/Script/Statements/ExpressionMediator.cs",
    "Scripts/Emuera/GameData/Expression/SafeArithmetic.cs": "Emuera/Runtime/Script/Statements/Expression/SafeArithmetic.cs",
    "Scripts/Emuera/GameData/Expression/CaseExpression.cs": "Emuera/Runtime/Script/Statements/CaseExpression.cs",
    "Scripts/Emuera/GameData/ConstantData.cs": "Emuera/Runtime/Script/Data/ConstantData.cs",
    "Scripts/Emuera/GameData/IdentifierDictionary.cs": "Emuera/Runtime/Script/Data/IdentifierDictionary.cs",
    "Scripts/Emuera/GameData/GameBase.cs": "Emuera/Runtime/Script/Data/GameBase.cs",
    "Scripts/Emuera/GameData/DefineMacro.cs": "Emuera/Runtime/Script/Data/DefineMacro.cs",
    "Scripts/Emuera/GameData/ParserMediator.cs": "Emuera/Runtime/Script/Data/ParserMediator.cs",
    "Scripts/Emuera/GameData/StrForm.cs": "Emuera/Runtime/Script/Data/StrForm.cs",
    "Scripts/Emuera/GameProc/Process.cs": "Emuera/Runtime/Script/Process.cs",
    "Scripts/Emuera/GameProc/Process.State.cs": "Emuera/Runtime/Script/Process.State.cs",
    "Scripts/Emuera/GameProc/Process.ScriptProc.cs": "Emuera/Runtime/Script/Process.ScriptProc.cs",
    "Scripts/Emuera/GameProc/Process.SystemProc.cs": "Emuera/Runtime/Script/Process.SystemProc.cs",
    "Scripts/Emuera/GameProc/Process.CalledFunction.cs": "Emuera/Runtime/Script/Process.CalledFunction.cs",
    "Scripts/Emuera/GameProc/Process.LazyLoading.cs": "Emuera/Runtime/Script/Process.LazyLoading.cs",
    "Scripts/Emuera/GameProc/ErbLoader.cs": "Emuera/Runtime/Script/Loader/ErbLoader.cs",
    "Scripts/Emuera/GameProc/HeaderFileLoader.cs": "Emuera/Runtime/Script/Loader/ErhLoader.cs",
    "Scripts/Emuera/GameProc/LogicalLineParser.cs": "Emuera/Runtime/Script/Parser/LogicalLineParser.cs",
    "Scripts/Emuera/GameProc/LogicalLine.cs": "Emuera/Runtime/Script/Statements/LogicalLine.cs",
    "Scripts/Emuera/GameProc/LabelDictionary.cs": "Emuera/Runtime/Script/Data/LabelDictionary.cs",
    "Scripts/Emuera/GameProc/UserDefinedFunction.cs": "Emuera/Runtime/Script/Data/UserDefinedFunction.cs",
    "Scripts/Emuera/GameProc/UserDefinedVariable.cs": "Emuera/Runtime/Script/Data/UserDefinedVariable.cs",
    "Scripts/Emuera/Sub/LexicalAnalyzer.cs": "Emuera/Runtime/Script/Parser/LexicalAnalyzer.cs",
    "Scripts/Emuera/Sub/WordCollection.cs": "Emuera/Runtime/Script/Parser/WordCollection.cs",
    "Scripts/Emuera/Sub/Word.cs": "Emuera/Runtime/Script/Parser/Word.cs",
    "Scripts/Emuera/Sub/SubWord.cs": "Emuera/Runtime/Script/Parser/SubWord.cs",
    "Scripts/Emuera/GameData/Variable/VariableToken.cs": "Emuera/Runtime/Script/Statements/Variable/VariableToken.cs",
    "Scripts/Emuera/GameData/Variable/VariableTerm.cs": "Emuera/Runtime/Script/Statements/Variable/VariableTerm.cs",
    "Scripts/Emuera/GameData/Variable/VariableParser.cs": "Emuera/Runtime/Script/Statements/Variable/VariableParser.cs",
    "Scripts/Emuera/GameData/Variable/VariableData.cs": "Emuera/Runtime/Script/Statements/Variable/VariableData.cs",
    "Scripts/Emuera/GameData/Variable/VariableEvaluator.cs": "Emuera/Runtime/Script/Statements/Variable/VariableEvaluator.cs",
    "Scripts/Emuera/GameData/Variable/VariableCode.cs": "Emuera/Runtime/Script/Statements/Variable/VariableCode.cs",
    "Scripts/Emuera/GameData/Variable/VariableIdentifier.cs": "Emuera/Runtime/Script/Statements/Variable/VariableIdentifier.cs",
    "Scripts/Emuera/GameData/Variable/VariableLocal.cs": "Emuera/Runtime/Script/Statements/Variable/VariableLocal.cs",
    "Scripts/Emuera/GameData/Variable/CharacterData.cs": "Emuera/Runtime/Script/Statements/Variable/CharacterData.cs",
    "Scripts/Emuera/GameData/Function/Creator.cs": "Emuera/Runtime/Script/Statements/Function/Creator.cs",
    "Scripts/Emuera/GameData/Function/Creator.Method.cs": "Emuera/Runtime/Script/Statements/Function/Creator.Method.cs",
    "Scripts/Emuera/GameData/Function/FunctionMethod.cs": "Emuera/Runtime/Script/Statements/Function/FunctionMethod.cs",
    "Scripts/Emuera/GameData/Function/FunctionMethodTerm.cs": "Emuera/Runtime/Script/Statements/Function/FunctionMethodTerm.cs",
    "Scripts/Emuera/GameData/Function/UserDefinedMethodTerm.cs": "Emuera/Runtime/Script/Statements/Function/UserDefinedMethodTerm.cs",
    "Scripts/Emuera/GameData/Function/UserDefinedRefMethod.cs": "Emuera/Runtime/Script/Statements/Function/UserDefinedRefMethod.cs",
    "Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs": "Emuera/Runtime/Script/Statements/FunctionIdentifier.cs",
    "Scripts/Emuera/GameProc/Function/Argument.cs": "Emuera/Runtime/Script/Statements/Argument.cs",
    "Scripts/Emuera/GameProc/Function/ArgumentBuilder.cs": "Emuera/Runtime/Script/Statements/ArgumentBuilder.cs",
    "Scripts/Emuera/GameProc/Function/ArgumentParser.cs": "Emuera/Runtime/Script/Statements/ArgumentParser.cs",
    "Scripts/Emuera/GameProc/Function/BuiltInFunctionCode.cs": "Emuera/Runtime/Script/Statements/BuiltInFunctionCode.cs",
    "Scripts/Emuera/GameProc/Function/Instraction.Child.cs": "Emuera/Runtime/Script/Statements/Instraction.Child.cs",
    "Scripts/Emuera/GameProc/Function/Instruction.cs": "Emuera/Runtime/Script/Statements/Instruction.cs",
    "Scripts/Emuera/GameProc/Function/FunctionArgType.cs": "Emuera/Runtime/Script/Statements/FunctionArgType.cs",
    "Scripts/Emuera/Config/Config.cs": "Emuera/Runtime/Config/Config.cs",
    "Scripts/Emuera/Config/ConfigData.cs": "Emuera/Runtime/Config/ConfigData.cs",
    "Scripts/Emuera/Config/KeyMacro.cs": "Emuera/Runtime/Script/KeyMacro.cs",
}

base_g = os.getcwd().replace("\\", "/")
base_r = "E:/MyCode/Era/emuera_lazyloading_selfmodified_version-main-skiasharp"

results = []
for gfile, rfile in mapping.items():
    if rfile is None:
        results.append((gfile, None, "NO_MAPPING", 0))
        continue
    gp = os.path.join(base_g, gfile).replace("\\", "/")
    rp = os.path.join(base_r, rfile).replace("\\", "/")
    if not os.path.exists(gp):
        results.append((gfile, rfile, "MISSING_G", 0))
        continue
    if not os.path.exists(rp):
        results.append((gfile, rfile, "MISSING_R", 0))
        continue
    proc = subprocess.run(["diff", "-u", rp, gp], capture_output=True)
    stdout = proc.stdout.decode('utf-8', errors='ignore')
    diff_lines = stdout.count('\n')
    if diff_lines == 0:
        results.append((gfile, rfile, "SAME", 0))
    else:
        results.append((gfile, rfile, "DIFF", diff_lines))

results.sort(key=lambda x: x[3], reverse=True)

for gfile, rfile, status, diff_lines in results:
    if status == "DIFF":
        print(f"DIFF  {diff_lines:5d}  {gfile}")
    elif status == "SAME":
        print(f"SAME  {diff_lines:5d}  {gfile}")
    elif status == "NO_MAPPING":
        print(f"NMAP  {diff_lines:5d}  {gfile}")
    elif status == "MISSING_G":
        print(f"MGIS  {diff_lines:5d}  {gfile}")
    elif status == "MISSING_R":
        print(f"MRIS  {diff_lines:5d}  {gfile}")
