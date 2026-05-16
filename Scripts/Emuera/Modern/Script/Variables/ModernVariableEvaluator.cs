using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Variables;

internal sealed class ModernVariableEvaluator
{
	readonly Dictionary<string, ModernVariableToken> tokens;

	public ModernVariableEvaluator()
		: this(new ModernVariableData())
	{
	}

	public ModernVariableEvaluator(ModernVariableData variableData)
	{
		VariableData = variableData ?? throw new ArgumentNullException(nameof(variableData));
		tokens = new Dictionary<string, ModernVariableToken>(StringComparer.OrdinalIgnoreCase);
		RegisterBuiltInTokens();
	}

	public ModernVariableData VariableData { get; }

	public bool TryGetToken(string name, out ModernVariableToken token)
	{
		return tokens.TryGetValue(name, out token);
	}

	public ModernVariableToken GetToken(string name)
	{
		if (TryGetToken(name, out var token))
			return token;
		throw new KeyNotFoundException($"Unknown variable: {name}");
	}

	public ModernVariableTerm CreateTerm(string name, params AExpression[] arguments)
	{
		return new ModernVariableTerm(GetToken(name), arguments);
	}

	public IReadOnlyDictionary<string, ModernVariableToken> Tokens { get { return tokens; } }

	public ModernVariableEvaluator CreateScopedEvaluator(IEnumerable<ModernUserVariableDefinition> privateVariables)
	{
		var evaluator = new ModernVariableEvaluator(VariableData);
		evaluator.RegisterPrivateVariables(privateVariables);
		return evaluator;
	}

	public void RegisterPrivateVariables(IEnumerable<ModernUserVariableDefinition> privateVariables)
	{
		if (privateVariables == null)
			return;
		foreach (var definition in privateVariables)
			Register(new ModernPrivateVariableToken(definition, VariableData));
	}

	void RegisterBuiltInTokens()
	{
		RegisterInt1D("DAY", VariableCode.DAY);
		RegisterInt1D("MONEY", VariableCode.MONEY);
		RegisterInt1D("ITEM", VariableCode.ITEM);
		RegisterInt1D("FLAG", VariableCode.FLAG);
		RegisterInt1D("TFLAG", VariableCode.TFLAG);
		RegisterInt1D("UP", VariableCode.UP);
		RegisterInt1D("DOWN", VariableCode.DOWN);
		RegisterInt1D("PALAMLV", VariableCode.PALAMLV);
		RegisterInt1D("EXPLV", VariableCode.EXPLV);
		RegisterInt1D("EJAC", VariableCode.EJAC);
		RegisterInt1D("RESULT", VariableCode.RESULT);
		RegisterInt1D("COUNT", VariableCode.COUNT);
		RegisterInt1D("TARGET", VariableCode.TARGET);
		RegisterInt1D("ASSI", VariableCode.ASSI);
		RegisterInt1D("MASTER", VariableCode.MASTER);
		RegisterInt1D("NOITEM", VariableCode.NOITEM);
		RegisterInt1D("LOSEBASE", VariableCode.LOSEBASE);
		RegisterInt1D("SELECTCOM", VariableCode.SELECTCOM);
		RegisterInt1D("ASSIPLAY", VariableCode.ASSIPLAY);
		RegisterInt1D("PREVCOM", VariableCode.PREVCOM);
		RegisterInt1D("TIME", VariableCode.TIME);
		RegisterInt1D("ITEMSALES", VariableCode.ITEMSALES);
		RegisterInt1D("PLAYER", VariableCode.PLAYER);
		RegisterInt1D("NEXTCOM", VariableCode.NEXTCOM);
		RegisterInt1D("PBAND", VariableCode.PBAND);
		RegisterInt1D("BOUGHT", VariableCode.BOUGHT);
		for (char c = 'A'; c <= 'Z'; c++)
		{
			var code = (VariableCode)Enum.Parse(typeof(VariableCode), c.ToString());
			RegisterInt1D(c.ToString(), code);
		}

		RegisterInt1D("ITEMPRICE", VariableCode.ITEMPRICE);
		RegisterInt1D("GLOBAL", VariableCode.GLOBAL);
		RegisterInt1D("RANDDATA", VariableCode.RANDDATA);
		RegisterLocalInt1D("LOCAL", VariableCode.LOCAL);
		RegisterLocalInt1D("ARG", VariableCode.ARG);

		RegisterString1D("SAVESTR", VariableCode.SAVESTR);
		RegisterString1D("STR", VariableCode.STR);
		RegisterString1D("RESULTS", VariableCode.RESULTS);
		RegisterString1D("GLOBALS", VariableCode.GLOBALS);
		RegisterString1D("TSTR", VariableCode.TSTR);
		RegisterLocalString1D("LOCALS", VariableCode.LOCALS);
		RegisterLocalString1D("ARGS", VariableCode.ARGS);
		Register(new ModernStringScalarVariableToken("SAVEDATA_TEXT", VariableCode.SAVEDATA_TEXT, VariableData));

		Register(new ModernFloatScalarVariableToken("RESULTF", VariableCode.RESULTF, VariableData));
		RegisterLocalFloat1D("LOCALF", VariableCode.LOCALF);
		RegisterLocalFloat1D("ARGF", VariableCode.ARGF);

		RegisterCharaIntScalar("ISASSI", VariableCode.ISASSI);
		RegisterCharaIntScalar("NO", VariableCode.NO);
		RegisterCharaStringScalar("NAME", VariableCode.NAME);
		RegisterCharaStringScalar("CALLNAME", VariableCode.CALLNAME);
		RegisterCharaStringScalar("NICKNAME", VariableCode.NICKNAME);
		RegisterCharaStringScalar("MASTERNAME", VariableCode.MASTERNAME);
		RegisterCharaInt1D("BASE", VariableCode.BASE);
		RegisterCharaInt1D("MAXBASE", VariableCode.MAXBASE);
		RegisterCharaInt1D("ABL", VariableCode.ABL);
		RegisterCharaInt1D("TALENT", VariableCode.TALENT);
		RegisterCharaInt1D("EXP", VariableCode.EXP);
		RegisterCharaInt1D("MARK", VariableCode.MARK);
		RegisterCharaInt1D("PALAM", VariableCode.PALAM);
		RegisterCharaInt1D("SOURCE", VariableCode.SOURCE);
		RegisterCharaInt1D("EX", VariableCode.EX);
		RegisterCharaInt1D("CFLAG", VariableCode.CFLAG);
		RegisterCharaInt1D("JUEL", VariableCode.JUEL);
		RegisterCharaInt1D("RELATION", VariableCode.RELATION);
		RegisterCharaInt1D("EQUIP", VariableCode.EQUIP);
		RegisterCharaInt1D("TEQUIP", VariableCode.TEQUIP);
		RegisterCharaInt1D("STAIN", VariableCode.STAIN);
		RegisterCharaInt1D("GOTJUEL", VariableCode.GOTJUEL);
		RegisterCharaInt1D("NOWEX", VariableCode.NOWEX);
		RegisterCharaInt1D("DOWNBASE", VariableCode.DOWNBASE);
		RegisterCharaInt1D("CUP", VariableCode.CUP);
		RegisterCharaInt1D("CDOWN", VariableCode.CDOWN);
		RegisterCharaInt1D("TCVAR", VariableCode.TCVAR);
		RegisterCharaString1D("CSTR", VariableCode.CSTR);
		RegisterCharaInt2D("CDFLAG", VariableCode.CDFLAG);
	}

	void RegisterInt1D(string name, VariableCode code)
	{
		Register(new ModernInt1DVariableToken(name, code, VariableData));
	}

	void RegisterString1D(string name, VariableCode code)
	{
		Register(new ModernString1DVariableToken(name, code, VariableData));
	}

	void RegisterLocalInt1D(string name, VariableCode code)
	{
		Register(new ModernLocalInt1DVariableToken(name, code, VariableData));
	}

	void RegisterLocalString1D(string name, VariableCode code)
	{
		Register(new ModernLocalString1DVariableToken(name, code, VariableData));
	}

	void RegisterLocalFloat1D(string name, VariableCode code)
	{
		Register(new ModernLocalFloat1DVariableToken(name, code, VariableData));
	}

	void RegisterCharaIntScalar(string name, VariableCode code)
	{
		Register(new ModernLegacyCharaIntScalarVariableToken(name, code, VariableData));
	}

	void RegisterCharaStringScalar(string name, VariableCode code)
	{
		Register(new ModernLegacyCharaStringScalarVariableToken(name, code, VariableData));
	}

	void RegisterCharaInt1D(string name, VariableCode code)
	{
		Register(new ModernLegacyCharaInt1DVariableToken(name, code, VariableData));
	}

	void RegisterCharaString1D(string name, VariableCode code)
	{
		Register(new ModernLegacyCharaString1DVariableToken(name, code, VariableData));
	}

	void RegisterCharaInt2D(string name, VariableCode code)
	{
		Register(new ModernLegacyCharaInt2DVariableToken(name, code, VariableData));
	}

	public void Register(ModernVariableToken token)
	{
		tokens[token.Name] = token;
	}
}
