using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MinorShift._Library;
using MinorShift.Emuera.Content;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Modern.Script.Expressions;
using MinorShift.Emuera.Modern.Script.Statements;
using MinorShift.Emuera.Modern.Script.Variables;
using MinorShift.Emuera.Sub;
using uEmuera.Drawing;
using uEmuera.VisualBasic;

namespace MinorShift.Emuera.Modern.Script.Functions;

internal sealed class ModernFunctionEvaluator
{
	readonly Dictionary<string, ModernFunctionMethod> methods = new(StringComparer.OrdinalIgnoreCase);

	public ModernFunctionEvaluator()
	{
		RegisterBuiltIns();
	}

	public bool TryGetMethod(string name, out ModernFunctionMethod method)
	{
		return methods.TryGetValue(name, out method);
	}

	public ModernFunctionCallExpression CreateCall(string name, IReadOnlyList<AExpression> arguments)
	{
		if (!TryGetMethod(name, out var method))
			throw new KeyNotFoundException($"Unknown function: {name}");
		return new ModernFunctionCallExpression(method, arguments);
	}

	public IReadOnlyDictionary<string, ModernFunctionMethod> Methods { get { return methods; } }

	public void Register(ModernFunctionMethod method)
	{
		if (method == null)
			throw new ArgumentNullException(nameof(method));
		methods[method.Name] = method;
	}

	public ModernScriptFunctionMethod RegisterScriptFunction(
		string name,
		EraType returnType,
		ModernBlockStatement body,
		ModernVariableEvaluator variableEvaluator,
		int minArgumentCount = 0,
		int maxArgumentCount = int.MaxValue,
		ModernVariableSizing sizing = null,
		int localIntegerLength = 0,
		int localStringLength = 0,
		int localFloatLength = 0,
		int argIntegerLength = 0,
		int argStringLength = 0,
		int argFloatLength = 0,
		IEnumerable<ModernUserVariableDefinition> privateVariableDefinitions = null,
		IEnumerable<ModernFunctionArgumentBinding> argumentBindings = null)
	{
		var method = new ModernScriptFunctionMethod(
			name,
			returnType,
			body,
			variableEvaluator,
			minArgumentCount,
			maxArgumentCount,
			sizing,
			localIntegerLength,
			localStringLength,
			localFloatLength,
			argIntegerLength,
			argStringLength,
			argFloatLength,
			privateVariableDefinitions,
			argumentBindings);
		Register(method);
		return method;
	}

	void RegisterBuiltIns()
	{
		Register(new ToFloatMethod());
		Register(new ToStrMethod());
		Register(new ToStrfMethod());
		Register(new ToIntMethod());
		Register(new StringCaseMethod("TOUPPER", true));
		Register(new StringCaseMethod("TOLOWER", false));
		Register(new StringWidthMethod("TOHALF", false));
		Register(new StringWidthMethod("TOFULL", true));
		Register(new StringLengthMethod("STRLENS", false));
		Register(new StringLengthMethod("STRLENSU", true));
		Register(new SubstringMethod("SUBSTRING", false));
		Register(new SubstringMethod("SUBSTRINGU", true));
		Register(new StrFindMethod("STRFIND", false));
		Register(new StrFindMethod("STRFINDU", true));
		Register(new StrCountMethod());
		Register(new StrJoinMethod());
		Register(new ReplaceMethod());
		Register(new UnicodeMethod());
		Register(new UnicodeByteMethod());
		Register(new IsNumericMethod());
		Register(new RegexEscapeMethod());
		Register(new NumericConvertMethod());
		Register(new EncodeToUniMethod());
		Register(new CharAtUMethod());
		Register(new EvalMethod("EVAL", EraType.Integer, this));
		Register(new EvalMethod("EVALF", EraType.Float, this));
		Register(new EvalMethod("EVALS", EraType.String, this));
		Register(new BarStringMethod());
		Register(new MoneyStringMethod());
		Register(new PrintCPerLineMethod());
		Register(new PrintCLengthMethod());
		Register(new SaveNosMethod());
		Register(new CheckFontMethod());
		Register(new CheckDataMethod("CHKDATA", MinorShift.Emuera.Sub.EraSaveFileType.Normal, false));
		Register(new CheckDataMethod("CHKGLOBALDATA", MinorShift.Emuera.Sub.EraSaveFileType.Global, false));
		Register(new CheckDataMethod("CHKVARDATA", MinorShift.Emuera.Sub.EraSaveFileType.Var, true));
		Register(new CheckDataMethod("CHKCHARADATA", MinorShift.Emuera.Sub.EraSaveFileType.CharVar, true));
		Register(new FindDataFilesMethod("FIND_VARDATA", MinorShift.Emuera.Sub.EraSaveFileType.Var));
		Register(new FindDataFilesMethod("FIND_CHARADATA", MinorShift.Emuera.Sub.EraSaveFileType.CharVar));
		Register(new CsvStringMethod("CSVNAME", MinorShift.Emuera.GameData.CharacterStrData.NAME));
		Register(new CsvStringMethod("CSVCALLNAME", MinorShift.Emuera.GameData.CharacterStrData.CALLNAME));
		Register(new CsvStringMethod("CSVNICKNAME", MinorShift.Emuera.GameData.CharacterStrData.NICKNAME));
		Register(new CsvStringMethod("CSVMASTERNAME", MinorShift.Emuera.GameData.CharacterStrData.MASTERNAME));
		Register(new CsvCStrMethod());
		Register(new CsvIntegerMethod("CSVBASE", MinorShift.Emuera.GameData.CharacterIntData.BASE));
		Register(new CsvIntegerMethod("CSVABL", MinorShift.Emuera.GameData.CharacterIntData.ABL));
		Register(new CsvIntegerMethod("CSVMARK", MinorShift.Emuera.GameData.CharacterIntData.MARK));
		Register(new CsvIntegerMethod("CSVEXP", MinorShift.Emuera.GameData.CharacterIntData.EXP));
		Register(new CsvIntegerMethod("CSVRELATION", MinorShift.Emuera.GameData.CharacterIntData.RELATION));
		Register(new CsvIntegerMethod("CSVTALENT", MinorShift.Emuera.GameData.CharacterIntData.TALENT));
		Register(new CsvIntegerMethod("CSVCFLAG", MinorShift.Emuera.GameData.CharacterIntData.CFLAG));
		Register(new CsvIntegerMethod("CSVEQUIP", MinorShift.Emuera.GameData.CharacterIntData.EQUIP));
		Register(new CsvIntegerMethod("CSVJUEL", MinorShift.Emuera.GameData.CharacterIntData.JUEL));
		Register(new GetCharaMethod());
		Register(new GetSpCharaMethod());
		Register(new FindCharaMethod("FINDCHARA", false));
		Register(new FindCharaMethod("FINDLASTCHARA", true));
		Register(new GetCsvNoMethod("GETCSVNOBYNAME", MinorShift.Emuera.GameData.CharacterStrData.NAME));
		Register(new GetCsvNoMethod("GETCSVNOBYNICKNAME", MinorShift.Emuera.GameData.CharacterStrData.NICKNAME));
		Register(new GetCsvNoMethod("GETCSVNOBYCALLNAME", MinorShift.Emuera.GameData.CharacterStrData.CALLNAME));
		Register(new GetCsvNoMethod("GETCSVNOBYMASTERNAME", MinorShift.Emuera.GameData.CharacterStrData.MASTERNAME));
		Register(new ExistCsvMethod());
		Register(new GetLineStrMethod());
		Register(new GetTimeMethod());
		Register(new GetTimesMethod());
		Register(new GetMillisecondMethod());
		Register(new GetSecondMethod());
		Register(new SaveTextMethod());
		Register(new LoadTextMethod());
		Register(new GetConsoleColorMethod("GETCOLOR", ConsoleColorSource.CurrentForeground));
		Register(new GetConsoleColorMethod("GETDEFCOLOR", ConsoleColorSource.DefaultForeground));
		Register(new GetConsoleColorMethod("GETFOCUSCOLOR", ConsoleColorSource.FocusForeground));
		Register(new GetConsoleColorMethod("GETBGCOLOR", ConsoleColorSource.CurrentBackground));
		Register(new GetConsoleColorMethod("GETDEFBGCOLOR", ConsoleColorSource.DefaultBackground));
		Register(new GetConsoleStyleMethod());
		Register(new GetConsoleFontMethod());
		Register(new CurrentAlignMethod());
		Register(new CurrentRedrawMethod());
		Register(new IsSkipMethod());
		Register(new MesSkipMethod("MESSKIP"));
		Register(new MesSkipMethod("MOUSESKIP"));
		Register(new IsActiveMethod());
		Register(new GetKeyStateMethod("GETKEY", false));
		Register(new GetKeyStateMethod("GETKEYTRIGGERED", true));
		Register(new HotkeyStateInitMethod());
		Register(new HotkeyStateMethod());
		Register(new MousePositionMethod("MOUSEX", true));
		Register(new MousePositionMethod("MOUSEY", false));
		Register(new MouseButtonMethod());
		Register(new GetAnimeTimerMethod());
		Register(new ExistSoundMethod());
		Register(new GetSoundOrBgmInfoMethod());
		Register(new IsPlayingSoundMethod());
		Register(new SoundControlMethod());
		Register(new IsPlayingBgmMethod());
		Register(new BgmControlMethod());
		Register(new SetTextDrawingModeMethod());
		Register(new GetTextDrawingModeMethod());
		Register(new SetSkiaQualityMethod());
		Register(new GetSkiaQualityMethod());
		Register(new ColorFromNameMethod());
		Register(new ColorFromRgbMethod());
		Register(new GetConfigMethod("GETCONFIG", EraType.Integer));
		Register(new GetConfigMethod("GETCONFIGS", EraType.String));
		Register(new HtmlStringLenMethod());
		Register(new HtmlSubStringMethod());
		Register(new HtmlStringLinesMethod());
		Register(new HtmlToPlainTextMethod());
		Register(new HtmlEscapeMethod());
		Register(new HtmlGetPrintedStringMethod());
		Register(new HtmlPopPrintingStringMethod());
		Register(new GetDisplayLineMethod());
		Register(new ExistsImageLayerMethod());
		Register(new GraphicsStateMethod("GCREATED"));
		Register(new GraphicsStateMethod("GWIDTH"));
		Register(new GraphicsStateMethod("GHEIGHT"));
		Register(new GraphicsGetColorMethod());
		Register(new GraphicsSetColorMethod());
		Register(new GraphicsSetBrushMethod());
		Register(new GraphicsSetPenMethod());
		Register(new GraphicsSetFontMethod());
		Register(new GraphicsDashStyleMethod());
		Register(new GraphicsGetStateMethod("GGETBRUSH"));
		Register(new GraphicsGetStateMethod("GGETPEN"));
		Register(new GraphicsGetStateMethod("GGETPENWIDTH"));
		Register(new GraphicsGetStateMethod("GGETFONTSIZE"));
		Register(new GraphicsGetStateMethod("GGETFONTSTYLE"));
		Register(new GraphicsGetFontMethod());
		Register(new GraphicsGetTextSizeMethod());
		Register(new GraphicsDrawTextMethod());
		Register(new GraphicsCreateMethod());
		Register(new GraphicsCreateFromFileMethod("GCREATEFROMFILE", false));
		Register(new GraphicsCreateFromFileMethod("GLOAD", true));
		Register(new GraphicsSaveMethod());
		Register(new GraphicsDisposeMethod());
		Register(new GraphicsClearMethod());
		Register(new GraphicsFillRectangleMethod());
		Register(new GraphicsDrawLineMethod());
		Register(new GraphicsPolygonPointAddMethod());
		Register(new GraphicsPolygonPointClearMethod());
		Register(new GraphicsPolygonDrawMethod());
		Register(new GraphicsPolygonFillMethod());
		Register(new GraphicsDrawGMethod());
		Register(new GraphicsDrawGWithMaskMethod());
		Register(new GraphicsDrawGWithRotateMethod());
		Register(new GraphicsRotateMethod());
		Register(new GraphicsDrawSpriteMethod());
		Register(new SpriteStateMethod("SPRITECREATED"));
		Register(new SpriteStateMethod("SPRITEWIDTH"));
		Register(new SpriteStateMethod("SPRITEHEIGHT"));
		Register(new SpriteStateMethod("SPRITEPOSX"));
		Register(new SpriteStateMethod("SPRITEPOSY"));
		Register(new SpriteSetPosMethod("SPRITEMOVE"));
		Register(new SpriteSetPosMethod("SPRITESETPOS"));
		Register(new SpriteGetColorMethod());
		Register(new SpriteCreateMethod());
		Register(new SpriteCreateFromFileMethod());
		Register(new SpriteAnimeCreateMethod());
		Register(new SpriteAnimeAddFrameMethod());
		Register(new SpriteDisposeMethod());
		Register(new SpriteDisposeAllMethod());
		Register(new CbgClearMethod());
		Register(new CbgClearButtonMethod());
		Register(new CbgRemoveRangeMethod());
		Register(new CbgRemoveBMapMethod());
		Register(new CbgSetGraphicsMethod());
		Register(new CbgSetButtonMapGraphicsMethod());
		Register(new CbgSetSpriteMethod());
		Register(new CbgSetButtonSpriteMethod());
		Register(new TextBoxMethod("GETTEXTBOX", false));
		Register(new TextBoxMethod("SETTEXTBOX", true));
		Register(new MoveTextBoxMethod(false));
		Register(new MoveTextBoxMethod(true));
		Register(new ErdNameMethod());
		Register(new StrFormMethod());
		Register(new FlowInputMethod());
		Register(new FlowInputsMethod());
		Register(new AbsMethod());
		Register(new PowerMethod());
		Register(new RandMethod());
		Register(new MinMaxMethod("MAX", true));
		Register(new MinMaxMethod("MIN", false));
		Register(new LimitMethod());
		Register(new InRangeMethod());
		Register(new SumArrayMethod());
		Register(new CharaSumArrayMethod());
		Register(new MatchMethod());
		Register(new CharaMatchMethod());
		Register(new InRangeArrayMethod());
		Register(new CharaInRangeArrayMethod());
		Register(new MinMaxArrayMethod("MAXARRAY", true));
		Register(new MinMaxArrayMethod("MINARRAY", false));
		Register(new CharaMinMaxArrayMethod("MAXCARRAY", true));
		Register(new CharaMinMaxArrayMethod("MINCARRAY", false));
		Register(new FindElementMethod("FINDELEMENT", false));
		Register(new FindElementMethod("FINDLASTELEMENT", true));
		Register(new MatchAllMethod("MATCHALL", false));
		Register(new MatchAllMethod("MATCHALLEX", true));
		Register(new GroupMatchMethod());
		Register(new SameCheckMethod("NOSAMES", false));
		Register(new SameCheckMethod("ALLSAMES", true));
		Register(new ArrayMultiSortMethod());
		Register(new ArrayMultiSortExMethod());
		Register(new SqrtMethod());
		Register(new CbrtMethod());
		Register(new LogMethod("LOG", Math.E));
		Register(new LogMethod("LOG10", 10.0d));
		Register(new ExpMethod());
		Register(new SignMethod());
		Register(new TrigMethod("SIN", Math.Sin));
		Register(new TrigMethod("COS", Math.Cos));
		Register(new TrigMethod("TAN", Math.Tan));
		Register(new TrigMethod("ASIN", Math.Asin));
		Register(new TrigMethod("ACOS", Math.Acos));
		Register(new TrigMethod("ATAN", Math.Atan));
		Register(new RoundLikeMethod("FLOOR", Math.Floor));
		Register(new RoundLikeMethod("CEIL", Math.Ceiling));
		Register(new RoundLikeMethod("ROUND", Math.Round));
		Register(new UncheckedBinaryMethod("UNCHECKED_ADD", UncheckedBinaryOperation.Add));
		Register(new UncheckedBinaryMethod("UNCHECKED_SUB", UncheckedBinaryOperation.Subtract));
		Register(new UncheckedBinaryMethod("UNCHECKED_MUL", UncheckedBinaryOperation.Multiply));
		Register(new UncheckedNegateMethod());
		Register(new ArgLengthMethod("ARGLEN"));
		Register(new ArgLengthMethod("GETARGCOUNT"));
		Register(new RegexpMatchMethod());
		Register(new GetMemoryUsageMethod());
		Register(new ClearMemoryMethod());
		Register(new OutputLogMethod());
		Register(new GetDoingFunctionMethod());
		Register(new LineIsEmptyMethod());
		Register(new ExistVarMethod());
		Register(new IsDefinedMethod());
		Register(new ExistFunctionMethod(this));
		Register(new ExistMethMethod(this));
		Register(new VarSizeMethod());
		Register(new GetMethMethod("GETMETH", this, EraType.Integer));
		Register(new GetMethMethod("GETMETHF", this, EraType.Float));
		Register(new GetMethMethod("GETMETHS", this, EraType.String));
		Register(new EnumNameMethod("ENUMFUNCBEGINSWITH", this, EnumNameTarget.Function, EnumNameAction.BeginsWith));
		Register(new EnumNameMethod("ENUMFUNCENDSWITH", this, EnumNameTarget.Function, EnumNameAction.EndsWith));
		Register(new EnumNameMethod("ENUMFUNCWITH", this, EnumNameTarget.Function, EnumNameAction.Contains));
		Register(new EnumNameMethod("ENUMVARBEGINSWITH", this, EnumNameTarget.Variable, EnumNameAction.BeginsWith));
		Register(new EnumNameMethod("ENUMVARENDSWITH", this, EnumNameTarget.Variable, EnumNameAction.EndsWith));
		Register(new EnumNameMethod("ENUMVARWITH", this, EnumNameTarget.Variable, EnumNameAction.Contains));
		Register(new EnumNameMethod("ENUMMACROBEGINSWITH", this, EnumNameTarget.Macro, EnumNameAction.BeginsWith));
		Register(new EnumNameMethod("ENUMMACROENDSWITH", this, EnumNameTarget.Macro, EnumNameAction.EndsWith));
		Register(new EnumNameMethod("ENUMMACROWITH", this, EnumNameTarget.Macro, EnumNameAction.Contains));
		Register(new EnumFilesMethod());
		Register(new ExistFileMethod());
		Register(new GetVarMethod("GETVAR", EraType.Integer));
		Register(new GetVarMethod("GETVARF", EraType.Float));
		Register(new GetVarMethod("GETVARS", EraType.String));
		Register(new SetVarMethod());
		Register(new VarSetExMethod());
		Register(new GetBitMethod());
		Register(new GetNumMethod());
		Register(new GetNumByNameMethod());
		Register(new GetLevelMethod("GETPALAMLV", "PALAMLV"));
		Register(new GetLevelMethod("GETEXPLV", "EXPLV"));
		Register(new BitSetMethod());
		Register(new BitGetMethod());
		Register(new BitToggleMethod());
		Register(new BitIndexOfFirstMethod());
		Register(new XmlDocumentMethod("XML_DOCUMENT", XmlDocumentOperation.Create));
		Register(new XmlDocumentMethod("XML_EXIST", XmlDocumentOperation.Check));
		Register(new XmlDocumentMethod("XML_RELEASE", XmlDocumentOperation.Release));
		Register(new XmlGetMethod("XML_GET", false));
		Register(new XmlGetMethod("XML_GET_BYNAME", true));
		Register(new XmlSetMethod("XML_SET", false));
		Register(new XmlSetMethod("XML_SET_BYNAME", true));
		Register(new XmlToStrMethod());
		Register(new XmlAddNodeMethod("XML_ADDNODE", XmlAddOperation.Node, false));
		Register(new XmlAddNodeMethod("XML_ADDNODE_BYNAME", XmlAddOperation.Node, true));
		Register(new XmlAddNodeMethod("XML_ADDATTRIBUTE", XmlAddOperation.Attribute, false));
		Register(new XmlAddNodeMethod("XML_ADDATTRIBUTE_BYNAME", XmlAddOperation.Attribute, true));
		Register(new XmlRemoveNodeMethod("XML_REMOVENODE", XmlRemoveOperation.Node, false));
		Register(new XmlRemoveNodeMethod("XML_REMOVENODE_BYNAME", XmlRemoveOperation.Node, true));
		Register(new XmlRemoveNodeMethod("XML_REMOVEATTRIBUTE", XmlRemoveOperation.Attribute, false));
		Register(new XmlRemoveNodeMethod("XML_REMOVEATTRIBUTE_BYNAME", XmlRemoveOperation.Attribute, true));
		Register(new XmlReplaceMethod("XML_REPLACE", false));
		Register(new XmlReplaceMethod("XML_REPLACE_BYNAME", true));
		Register(new DataTableManagementMethod("DT_CREATE", DataTableManagementOperation.Create));
		Register(new DataTableManagementMethod("DT_EXIST", DataTableManagementOperation.Check));
		Register(new DataTableManagementMethod("DT_RELEASE", DataTableManagementOperation.Release));
		Register(new DataTableManagementMethod("DT_CLEAR", DataTableManagementOperation.Clear));
		Register(new DataTableManagementMethod("DT_NOCASE", DataTableManagementOperation.NoCase));
		Register(new DataTableColumnManagementMethod("DT_COLUMN_ADD", DataTableColumnOperation.Create));
		Register(new DataTableColumnManagementMethod("DT_COLUMN_EXIST", DataTableColumnOperation.Check));
		Register(new DataTableColumnManagementMethod("DT_COLUMN_REMOVE", DataTableColumnOperation.Remove));
		Register(new DataTableColumnManagementMethod("DT_COLUMN_NAMES", DataTableColumnOperation.Names));
		Register(new DataTableLengthMethod("DT_COLUMN_LENGTH", DataTableLengthOperation.Column));
		Register(new DataTableRowSetMethod("DT_ROW_ADD", DataTableRowSetOperation.Add));
		Register(new DataTableRowSetMethod("DT_ROW_SET", DataTableRowSetOperation.Set));
		Register(new DataTableRowRemoveMethod());
		Register(new DataTableLengthMethod("DT_ROW_LENGTH", DataTableLengthOperation.Row));
		Register(new DataTableCellGetMethod("DT_CELL_GET", DataTableCellGetOperation.GetInteger));
		Register(new DataTableCellGetMethod("DT_CELL_GETS", DataTableCellGetOperation.GetString));
		Register(new DataTableCellGetMethod("DT_CELL_GETF", DataTableCellGetOperation.GetFloat));
		Register(new DataTableCellGetMethod("DT_CELL_ISNULL", DataTableCellGetOperation.IsNull));
		Register(new DataTableCellSetMethod("DT_CELL_SET", DataTableCellSetOperation.SetExpression));
		Register(new DataTableCellSetMethod("DT_CELL_SETF", DataTableCellSetOperation.SetFloat));
		Register(new DataTableSelectMethod());
		Register(new DataTableToXmlMethod());
		Register(new DataTableFromXmlMethod());
		Register(new MapManagementMethod("MAP_CREATE", MapManagementOperation.Create));
		Register(new MapManagementMethod("MAP_EXIST", MapManagementOperation.Check));
		Register(new MapManagementMethod("MAP_RELEASE", MapManagementOperation.Release));
		Register(new MapGetMethod());
		Register(new MapDataOperationMethod("MAP_CLEAR", MapDataOperation.Clear));
		Register(new MapDataOperationMethod("MAP_SIZE", MapDataOperation.Size));
		Register(new MapDataOperationMethod("MAP_HAS", MapDataOperation.Has));
		Register(new MapDataOperationMethod("MAP_SET", MapDataOperation.Set));
		Register(new MapDataOperationMethod("MAP_REMOVE", MapDataOperation.Remove));
		Register(new MapGetStringListMethod("MAP_GETKEYS", MapStringListOperation.Keys));
		Register(new MapGetStringListMethod("MAP_VALUES", MapStringListOperation.Values));
		Register(new MapToXmlMethod());
		Register(new MapFromXmlMethod());
		Register(new MapMergeMethod());
		Register(new MapRemoveIfMethod());
		Register(new MapFindKeyMethod());
		Register(new MapToStringMethod());
		Register(new MapFromStringMethod());
		Register(new SqlIntMethod("SQL_CONNECTION_OPEN", SqlIntOperation.ConnectionOpen, 1, 1));
		Register(new SqlIntMethod("SQL_CONNECT", SqlIntOperation.Connect, 2, 2));
		Register(new SqlIntMethod("SQL_DISCONNECT", SqlIntOperation.Disconnect, 1, 1));
		Register(new SqlIntMethod("SQL_EXECUTE_NONQUERY", SqlIntOperation.ExecuteNonQuery, 2, 2));
		Register(new SqlIntMethod("SQL_EXECUTE_READER", SqlIntOperation.ExecuteReader, 2, 2));
		Register(new SqlIntMethod("SQL_READER_READ", SqlIntOperation.ReaderRead, 1, 1));
		Register(new SqlIntMethod("SQL_READER_GET_LONG", SqlIntOperation.ReaderGetLong, 2, 2));
		Register(new SqlFloatMethod("SQL_READER_GET_FLOAT", SqlFloatOperation.ReaderGetFloat, 2, 2));
		Register(new SqlStringMethod("SQL_READER_GET_STRING", SqlStringOperation.ReaderGetString, 2, 2));
		Register(new SqlIntMethod("SQL_READER_ISNULL", SqlIntOperation.ReaderIsNull, 2, 2));
		Register(new SqlIntMethod("SQL_READER_CLOSE", SqlIntOperation.ReaderClose, 1, 1));
		Register(new SqlIntMethod("SQL_EXECUTE_SCALAR_LONG", SqlIntOperation.ExecuteScalarLong, 2, 2));
		Register(new SqlFloatMethod("SQL_EXECUTE_SCALAR_FLOAT", SqlFloatOperation.ExecuteScalarFloat, 2, 2));
		Register(new SqlStringMethod("SQL_EXECUTE_SCALAR_STRING", SqlStringOperation.ExecuteScalarString, 2, 2));
		Register(new SqlIntMethod("SQL_IMPORT_MAP_XML", SqlIntOperation.ImportMapXml, 3, 3));
		Register(new SqlIntMethod("SQL_IMPORT_DT_XML", SqlIntOperation.ImportDtXml, 4, 4));
		Register(new SqlIntMethod("SQL_EXPORT_MAP_XML", SqlIntOperation.ExportMapXml, 3, 3));
		Register(new SqlIntMethod("SQL_EXPORT_DT_XML", SqlIntOperation.ExportDtXml, 4, 4));
		Register(new SqlIntMethod("SQL_IMPORT_XML_CUSTOM", SqlIntOperation.ImportXmlCustom, 5, 5));
		Register(new SqlStringMethod("SQL_ESCAPE", SqlStringOperation.Escape, 1, 1));
		Register(new SqlIntMethod("SQL_P_EXECUTE_NONQUERY", SqlIntOperation.ExecuteNonQueryWithParameters, 2, int.MaxValue));
		Register(new SqlIntMethod("SQL_P_EXECUTE_READER", SqlIntOperation.ExecuteReaderWithParameters, 2, int.MaxValue));
		Register(new SqlIntMethod("SQL_P_EXECUTE_SCALAR_LONG", SqlIntOperation.ExecuteScalarLongWithParameters, 2, int.MaxValue));
		Register(new SqlFloatMethod("SQL_P_EXECUTE_SCALAR_FLOAT", SqlFloatOperation.ExecuteScalarFloatWithParameters, 2, int.MaxValue));
		Register(new SqlStringMethod("SQL_P_EXECUTE_SCALAR_STRING", SqlStringOperation.ExecuteScalarStringWithParameters, 2, int.MaxValue));
		Register(new ClientSizeMethod("CLIENTWIDTH"));
		Register(new ClientSizeMethod("CLIENTHEIGHT"));
	}

	sealed class ToFloatMethod : ModernFunctionMethod
	{
		public ToFloatMethod() : base("TOFLOAT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Float;
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!arguments[0].IsString)
				return ToDouble(arguments[0], context);
			string value = arguments[0].GetStrValue(context);
			if (string.IsNullOrEmpty(value) || HasWideCharacters(value))
				return 0.0d;
			return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
				|| double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)
				? parsed
				: 0.0d;
		}
	}

	sealed class ToStrMethod : ModernFunctionMethod
	{
		public ToStrMethod() : base("TOSTR", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long value = ToLong(arguments[0], context);
			if (arguments.Count < 2)
				return value.ToString(CultureInfo.InvariantCulture);
			string format = arguments[1].GetStrValue(context);
			try
			{
				return value.ToString(format, CultureInfo.InvariantCulture);
			}
			catch (FormatException)
			{
				throw new FormatException($"{Name} received an invalid format string.");
			}
		}
	}

	sealed class ToStrfMethod : ModernFunctionMethod
	{
		public ToStrfMethod() : base("TOSTRF", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			double value = ToDouble(arguments[0], context);
			if (arguments.Count < 2)
				return value.ToString(CultureInfo.InvariantCulture);
			string format = arguments[1].GetStrValue(context);
			try
			{
				return value.ToString(format, CultureInfo.InvariantCulture);
			}
			catch (FormatException)
			{
				throw new FormatException($"{Name} received an invalid format string.");
			}
		}
	}

	sealed class ToIntMethod : ModernFunctionMethod
	{
		public ToIntMethod() : base("TOINT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (arguments[0].IsFloat)
				return (long)arguments[0].GetFloatValue(context);
			if (arguments[0].IsInteger)
				return arguments[0].GetIntValue(context);
			return TryParseEraInteger(arguments[0].GetStrValue(context), out long value) ? value : 0;
		}
	}

	sealed class StringCaseMethod : ModernFunctionMethod
	{
		readonly bool upper;

		public StringCaseMethod(string name, bool upper) : base(name, 1, 1)
		{
			this.upper = upper;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string value = arguments[0].GetStrValue(context) ?? "";
			return upper ? value.ToUpper(CultureInfo.InvariantCulture) : value.ToLower(CultureInfo.InvariantCulture);
		}
	}

	sealed class StringWidthMethod : ModernFunctionMethod
	{
		readonly bool wide;

		public StringWidthMethod(string name, bool wide) : base(name, 1, 1)
		{
			this.wide = wide;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string value = arguments[0].GetStrValue(context) ?? "";
			return Strings.StrConv(value, wide ? VbStrConv.Wide : VbStrConv.Narrow, Config.Language);
		}
	}

	sealed class StringLengthMethod : ModernFunctionMethod
	{
		readonly bool unicode;

		public StringLengthMethod(string name, bool unicode) : base(name, 1, 1)
		{
			this.unicode = unicode;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string value = arguments[0].GetStrValue(context) ?? "";
			return unicode ? value.Length : LangManager.GetStrlenLang(value);
		}
	}

	sealed class SubstringMethod : ModernFunctionMethod
	{
		readonly bool unicode;

		public SubstringMethod(string name, bool unicode) : base(name, 1, 3)
		{
			this.unicode = unicode;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string value = arguments[0].GetStrValue(context) ?? "";
			int start = arguments.Count > 1 ? (int)arguments[1].GetIntValue(context) : 0;
			int length = arguments.Count > 2 ? (int)arguments[2].GetIntValue(context) : -1;
			if (!unicode)
				return LangManager.GetSubStringLang(value, start, length);
			if (start >= value.Length || length == 0)
				return "";
			if (length < 0 || length > value.Length)
				length = value.Length;
			if (start <= 0)
			{
				if (length == value.Length)
					return value;
				start = 0;
			}
			if (start + length > value.Length)
				length = value.Length - start;
			return length <= 0 ? "" : value.Substring(start, length);
		}
	}

	sealed class StrFindMethod : ModernFunctionMethod
	{
		readonly bool unicode;

		public StrFindMethod(string name, bool unicode) : base(name, 2, 3)
		{
			this.unicode = unicode;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string target = arguments[0].GetStrValue(context) ?? "";
			string word = arguments[1].GetStrValue(context) ?? "";
			int start = 0;
			if (arguments.Count >= 3)
			{
				start = (int)arguments[2].GetIntValue(context);
				if (!unicode)
					start = LangManager.GetUFTIndex(target, start);
			}
			if (start < 0 || start >= target.Length)
				return -1;
			int index = target.IndexOf(word, start, StringComparison.Ordinal);
			if (index > 0 && !unicode)
				index = LangManager.GetStrlenLang(target.Substring(0, index));
			return index;
		}
	}

	sealed class StrCountMethod : ModernFunctionMethod
	{
		public StrCountMethod() : base("STRCOUNT", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			try
			{
				return Regex.Matches(arguments[0].GetStrValue(context) ?? "", arguments[1].GetStrValue(context) ?? "").Count;
			}
			catch (ArgumentException e)
			{
				throw new ArgumentException($"{Name} received an invalid regex pattern: {e.Message}", e);
			}
		}
	}

	sealed class StrJoinMethod : ModernFunctionMethod
	{
		public StrJoinMethod() : base("STRJOIN", 1, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = GetWholeArrayArgument(arguments[0], Name);
			string delimiter = arguments.Count >= 2 ? arguments[1].GetStrValue(context) ?? "" : ",";
			long start = arguments.Count >= 3 ? arguments[2].GetIntValue(context) : 0;
			long count = arguments.Count == 4 ? arguments[3].GetIntValue(context) : GetVariableArrayLength(term.Identifier, context) - start;
			if (count < 0)
				throw new ArgumentOutOfRangeException(nameof(arguments), "STRJOIN length cannot be negative.");
			ValidateArrayRange(term.Identifier, context, start, start + count);

			var builder = new StringBuilder();
			for (long i = 0; i < count; i++)
			{
				if (i > 0)
					builder.Append(delimiter);
				builder.Append(GetArrayValueAsString(term.Identifier, context, start + i));
			}
			return builder.ToString();
		}
	}

	sealed class ReplaceMethod : ModernFunctionMethod
	{
		public ReplaceMethod() : base("REPLACE", 3, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string source = arguments[0].GetStrValue(context) ?? "";
			string pattern = arguments[1].GetStrValue(context) ?? "";
			int mode = arguments.Count == 4 ? (int)arguments[3].GetIntValue(context) : 0;
			if (mode == 2)
				return source.Replace(pattern, arguments[2].GetStrValue(context) ?? "");

			Regex regex;
			try
			{
				regex = new Regex(pattern);
			}
			catch (ArgumentException e)
			{
				throw new ArgumentException($"{Name} received an invalid regex pattern: {e.Message}", e);
			}

			if (mode == 1 && arguments[2] is ModernVariableTerm replacementArray && replacementArray.Identifier.IsString)
			{
				int index = 0;
				int length = GetVariableArrayLength(replacementArray.Identifier, context);
				return regex.Replace(source, _ =>
				{
					if (index >= length)
						return "";
					return replacementArray.Identifier.GetStrValue(context, new long[] { index++ }) ?? "";
				});
			}

			return regex.Replace(source, arguments[2].GetStrValue(context) ?? "");
		}
	}

	sealed class UnicodeMethod : ModernFunctionMethod
	{
		public UnicodeMethod() : base("UNICODE", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long value = arguments[0].GetIntValue(context);
			if (value < 0 || value > 0xFFFF)
				throw new ArgumentOutOfRangeException(nameof(arguments), "UNICODE code point must be between 0 and 0xFFFF.");
			if ((value < 0x001F && value != 0x000A && value != 0x000D) || (value >= 0x007F && value <= 0x009F))
				return "";
			return new string((char)value, 1);
		}
	}

	sealed class UnicodeByteMethod : ModernFunctionMethod
	{
		public UnicodeByteMethod() : base("UNICODEBYTE", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string value = arguments[0].GetStrValue(context) ?? "";
			if (value.Length == 0)
				throw new ArgumentException("UNICODEBYTE needs a non-empty string.");
			return char.ConvertToUtf32(value, 0);
		}
	}

	sealed class IsNumericMethod : ModernFunctionMethod
	{
		public IsNumericMethod() : base("ISNUMERIC", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return IsEraNumeric(arguments[0].GetStrValue(context)) ? 1 : 0;
		}
	}

	sealed class RegexEscapeMethod : ModernFunctionMethod
	{
		public RegexEscapeMethod() : base("ESCAPE", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Regex.Escape(arguments[0].GetStrValue(context) ?? "");
		}
	}

	sealed class NumericConvertMethod : ModernFunctionMethod
	{
		public NumericConvertMethod() : base("CONVERT", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			int radix = (int)arguments[1].GetIntValue(context);
			if (radix != 2 && radix != 8 && radix != 10 && radix != 16)
				throw new ArgumentOutOfRangeException(nameof(arguments), "CONVERT base must be 2, 8, 10, or 16.");
			return Convert.ToString(arguments[0].GetIntValue(context), radix);
		}
	}

	sealed class EncodeToUniMethod : ModernFunctionMethod
	{
		public EncodeToUniMethod() : base("ENCODETOUNI", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string value = arguments[0].GetStrValue(context) ?? "";
			if (value.Length == 0)
				return -1;
			long position = arguments.Count > 1 ? arguments[1].GetIntValue(context) : 0;
			if (position < 0)
				throw new ArgumentOutOfRangeException(nameof(arguments), "ENCODETOUNI position cannot be negative.");
			if (position >= value.Length)
				throw new ArgumentOutOfRangeException(nameof(arguments), "ENCODETOUNI position is outside the string.");
			return char.ConvertToUtf32(value, (int)position);
		}
	}

	sealed class CharAtUMethod : ModernFunctionMethod
	{
		public CharAtUMethod() : base("CHARATU", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string value = arguments[0].GetStrValue(context) ?? "";
			long position = arguments[1].GetIntValue(context);
			if (position < 0 || position >= value.Length)
				return "";
			return value[(int)position].ToString();
		}
	}

	sealed class EvalMethod : ModernFunctionMethod
	{
		readonly EraType returnType;
		readonly ModernFunctionEvaluator evaluator;

		public EvalMethod(string name, EraType returnType, ModernFunctionEvaluator evaluator) : base(name, 1, 2)
		{
			this.returnType = returnType;
			this.evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => returnType;

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long defaultValue = arguments.Count > 1 ? arguments[1].GetIntValue(context) : 0;
			if (!TryEvaluate(context, arguments[0], out var expression))
				return defaultValue;
			if (expression.IsInteger)
				return expression.GetIntValue(context);
			if (expression.IsFloat)
				return (long)expression.GetFloatValue(context);
			return defaultValue;
		}

		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			double defaultValue = arguments.Count > 1 ? ToDouble(arguments[1], context) : 0.0d;
			if (!TryEvaluate(context, arguments[0], out var expression))
				return defaultValue;
			if (expression.IsFloat)
				return expression.GetFloatValue(context);
			if (expression.IsInteger)
				return expression.GetIntValue(context);
			return defaultValue;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string defaultValue = arguments.Count > 1 ? arguments[1].GetStrValue(context) ?? "" : "";
			if (!TryEvaluate(context, arguments[0], out var expression))
				return defaultValue;
			if (expression.IsString)
				return expression.GetStrValue(context) ?? "";
			if (expression.IsFloat)
				return expression.GetFloatValue(context).ToString(CultureInfo.InvariantCulture);
			return defaultValue;
		}

		bool TryEvaluate(ModernExpressionContext context, AExpression source, out AExpression expression)
		{
			expression = null;
			string text = source.GetStrValue(context);
			if (string.IsNullOrWhiteSpace(text))
				return false;
			try
			{
				var variableEvaluator = context?.VariableEvaluator ?? throw new InvalidOperationException("No variable evaluator is available.");
				expression = new ModernExpressionParser(variableEvaluator, evaluator).Parse(text).Restructure(context);
				return expression != null;
			}
			catch
			{
				expression = null;
				return false;
			}
		}
	}

	sealed class BarStringMethod : ModernFunctionMethod
	{
		public BarStringMethod() : base("BARSTR", 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long value = arguments[0].GetIntValue(context);
			long max = arguments[1].GetIntValue(context);
			long length = arguments[2].GetIntValue(context);
			if (max <= 0)
				throw new ArgumentOutOfRangeException(nameof(arguments), "BARSTR maximum must be positive.");
			if (length <= 0 || length >= 100)
				throw new ArgumentOutOfRangeException(nameof(arguments), "BARSTR length must be between 1 and 99.");
			int filled = unchecked((int)(value * length / max));
			if (filled < 0)
				filled = 0;
			if (filled > length)
				filled = (int)length;
			var builder = new StringBuilder();
			builder.Append('[');
			builder.Append(Config.BarChar1, filled);
			builder.Append(Config.BarChar2, (int)length - filled);
			builder.Append(']');
			return builder.ToString();
		}
	}

	sealed class MoneyStringMethod : ModernFunctionMethod
	{
		public MoneyStringMethod() : base("MONEYSTR", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long money = arguments[0].GetIntValue(context);
			string value;
			if (arguments.Count < 2)
			{
				value = money.ToString(CultureInfo.InvariantCulture);
			}
			else
			{
				try
				{
					value = money.ToString(arguments[1].GetStrValue(context), CultureInfo.InvariantCulture);
				}
				catch (FormatException e)
				{
					throw new FormatException("MONEYSTR received an invalid format string.", e);
				}
			}
			string label = Config.MoneyLabel ?? "";
			return Config.MoneyFirst ? label + value : value + label;
		}
	}

	sealed class PrintCPerLineMethod : ModernFunctionMethod
	{
		public PrintCPerLineMethod() : base("PRINTCPERLINE", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => Config.PrintCPerLine;
	}

	sealed class PrintCLengthMethod : ModernFunctionMethod
	{
		public PrintCLengthMethod() : base("PRINTCLENGTH", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => Config.PrintCLength;
	}

	sealed class SaveNosMethod : ModernFunctionMethod
	{
		public SaveNosMethod() : base("SAVENOS", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => Config.SaveDataNos;
	}

	sealed class CheckFontMethod : ModernFunctionMethod
	{
		public CheckFontMethod() : base("CHKFONT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string fontName = arguments[0].GetStrValue(context);
			if (string.IsNullOrWhiteSpace(fontName))
				return 0;
			if (string.Equals(fontName, Config.FontName, StringComparison.OrdinalIgnoreCase))
				return 1;
			if (string.Equals(fontName, "MS Gothic", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(fontName, "MS PGothic", StringComparison.OrdinalIgnoreCase))
				return 1;

			string[] candidates =
			{
				Path.Combine(Program.ExeDir ?? "", "Fonts", fontName),
				Path.Combine(Program.ExeDir ?? "", "Fonts", fontName + ".ttf"),
				Path.Combine(Program.ExeDir ?? "", "Fonts", fontName + ".otf"),
				Path.Combine("Fonts", fontName),
				Path.Combine("Fonts", fontName + ".ttf"),
				Path.Combine("Fonts", fontName + ".otf"),
			};
			foreach (string candidate in candidates)
			{
				if (!string.IsNullOrWhiteSpace(candidate) && uEmuera.Utils.FileExists(candidate))
					return 1;
			}
			return 0;
		}
	}

	sealed class CheckDataMethod : ModernFunctionMethod
	{
		readonly MinorShift.Emuera.Sub.EraSaveFileType type;
		readonly bool filenameArgument;

		public CheckDataMethod(string name, MinorShift.Emuera.Sub.EraSaveFileType type, bool filenameArgument) : base(name, 1, 1)
		{
			this.type = type;
			this.filenameArgument = filenameArgument;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var evaluator = RequireLegacyVariableEvaluator();
			MinorShift.Emuera.Sub.EraDataResult result;
			if (filenameArgument)
			{
				result = evaluator.CheckData(arguments[0].GetStrValue(context) ?? "", type);
			}
			else
			{
				long target = arguments[0].GetIntValue(context);
				if (target < 0 || target > int.MaxValue)
					throw new ArgumentOutOfRangeException(nameof(arguments), $"{Name} save index is outside the valid Int32 range.");
				result = evaluator.CheckData((int)target, type);
			}
			WriteStringResults(context, null, new[] { result.DataMes ?? "" });
			return (long)result.State;
		}
	}

	sealed class FindDataFilesMethod : ModernFunctionMethod
	{
		readonly MinorShift.Emuera.Sub.EraSaveFileType type;

		public FindDataFilesMethod(string name, MinorShift.Emuera.Sub.EraSaveFileType type) : base(name, 0, 1)
		{
			this.type = type;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string pattern = arguments.Count > 0 ? arguments[0].GetStrValue(context) : "*";
			List<string> files = RequireLegacyVariableEvaluator().GetDatFiles(type == MinorShift.Emuera.Sub.EraSaveFileType.CharVar, pattern ?? "*");
			WriteStringResults(context, null, files);
			return files.Count;
		}
	}

	sealed class CsvStringMethod : ModernFunctionMethod
	{
		readonly MinorShift.Emuera.GameData.CharacterStrData type;

		public CsvStringMethod(string name, MinorShift.Emuera.GameData.CharacterStrData type) : base(name, 1, 2)
		{
			this.type = type;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long charaNo = arguments[0].GetIntValue(context);
			bool isSp = GetOptionalSpFlag(context, arguments, 1);
			return RequireLegacyVariableEvaluator().GetCharacterStrfromCSVData(charaNo, type, isSp, 0);
		}
	}

	sealed class CsvCStrMethod : ModernFunctionMethod
	{
		public CsvCStrMethod() : base("CSVCSTR", 2, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long charaNo = arguments[0].GetIntValue(context);
			long index = arguments[1].GetIntValue(context);
			bool isSp = GetOptionalSpFlag(context, arguments, 2);
			return RequireLegacyVariableEvaluator().GetCharacterStrfromCSVData(
				charaNo,
				MinorShift.Emuera.GameData.CharacterStrData.CSTR,
				isSp,
				index);
		}
	}

	sealed class CsvIntegerMethod : ModernFunctionMethod
	{
		readonly MinorShift.Emuera.GameData.CharacterIntData type;

		public CsvIntegerMethod(string name, MinorShift.Emuera.GameData.CharacterIntData type) : base(name, 2, 3)
		{
			this.type = type;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long charaNo = arguments[0].GetIntValue(context);
			long index = arguments[1].GetIntValue(context);
			bool isSp = GetOptionalSpFlag(context, arguments, 2);
			return RequireLegacyVariableEvaluator().GetCharacterIntfromCSVData(charaNo, type, isSp, index);
		}
	}

	sealed class GetCsvNoMethod : ModernFunctionMethod
	{
		readonly MinorShift.Emuera.GameData.CharacterStrData type;

		public GetCsvNoMethod(string name, MinorShift.Emuera.GameData.CharacterStrData type) : base(name, 1, 1)
		{
			this.type = type;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string key = arguments[0].GetStrValue(context) ?? "";
			var constantData = RequireLegacyConstantData();
			IReadOnlyDictionary<string, long> map = type switch
			{
				MinorShift.Emuera.GameData.CharacterStrData.NAME => constantData.NameToTemplateMap,
				MinorShift.Emuera.GameData.CharacterStrData.NICKNAME => constantData.NicknameToTemplateMap,
				MinorShift.Emuera.GameData.CharacterStrData.CALLNAME => constantData.CallnameToTemplateMap,
				MinorShift.Emuera.GameData.CharacterStrData.MASTERNAME => constantData.MasternameToTemplateMap,
				_ => throw new InvalidOperationException($"{Name} does not support {type}."),
			};
			return map.TryGetValue(key, out long charaNo) ? charaNo : -1;
		}
	}

	sealed class ExistCsvMethod : ModernFunctionMethod
	{
		public ExistCsvMethod() : base("EXISTCSV", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long charaNo = arguments[0].GetIntValue(context);
			bool isSp = GetOptionalSpFlag(context, arguments, 1);
			return RequireLegacyVariableEvaluator().ExistCsv(charaNo, isSp);
		}
	}

	sealed class GetCharaMethod : ModernFunctionMethod
	{
		public GetCharaMethod() : base("GETCHARA", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long charaNo = arguments[0].GetIntValue(context);
			var evaluator = RequireLegacyVariableEvaluator();
			if (!Config.CompatiSPChara)
				return evaluator.GetChara(charaNo);

			bool checkSp = arguments.Count > 1 && arguments[1].GetIntValue(context) != 0;
			if (!checkSp)
				return evaluator.GetChara_UseSp(charaNo, false);
			long normalChara = evaluator.GetChara_UseSp(charaNo, false);
			return normalChara != -1 ? normalChara : evaluator.GetChara_UseSp(charaNo, true);
		}
	}

	sealed class GetSpCharaMethod : ModernFunctionMethod
	{
		public GetSpCharaMethod() : base("GETSPCHARA", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!Config.CompatiSPChara)
				throw new InvalidOperationException("SP character functions require the compatibility option to be enabled.");
			return RequireLegacyVariableEvaluator().GetChara_UseSp(arguments[0].GetIntValue(context), true);
		}
	}

	sealed class FindCharaMethod : ModernFunctionMethod
	{
		readonly bool isLast;

		public FindCharaMethod(string name, bool isLast) : base(name, 2, 4)
		{
			this.isLast = isLast;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments[0] is not ModernVariableTerm term || !term.Identifier.IsCharacterData)
				throw new FormatException($"{Name} first argument must be a character variable.");
			if (term.Identifier.Dimension != VariableDimension.Scalar
				&& term.Identifier.Dimension != VariableDimension.Array1D
				&& term.Identifier.Dimension != VariableDimension.Array2D)
				throw new FormatException($"{Name} does not support this character variable dimension.");
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = (ModernVariableTerm)arguments[0];
			long start = arguments.Count > 2 ? arguments[2].GetIntValue(context) : 0;
			long end = arguments.Count > 3 ? arguments[3].GetIntValue(context) : GetCharacterCount();
			ValidateCharacterRange(start, end, Name);
			if (start >= end)
				return -1;

			if (term.Identifier.IsString)
			{
				string target = arguments[1].GetStrValue(context) ?? "";
				return FindCharacterValue(term, context, target, start, end, isLast);
			}
			long longTarget = arguments[1].GetIntValue(context);
			return FindCharacterValue(term, context, longTarget, start, end, isLast);
		}
	}

	sealed class GetLineStrMethod : ModernFunctionMethod
	{
		public GetLineStrMethod() : base("GETLINESTR", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string value = arguments[0].GetStrValue(context);
			if (string.IsNullOrEmpty(value))
				throw new ArgumentException("GETLINESTR first argument must not be an empty string.");
			return RequireConsole().getStBar(value);
		}
	}

	sealed class GetTimeMethod : ModernFunctionMethod
	{
		public GetTimeMethod() : base("GETTIME", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			DateTime now = DateTime.Now;
			long date = now.Year;
			date = date * 100 + now.Month;
			date = date * 100 + now.Day;
			date = date * 100 + now.Hour;
			date = date * 100 + now.Minute;
			date = date * 100 + now.Second;
			return date * 1000 + now.Millisecond;
		}
	}

	sealed class GetTimesMethod : ModernFunctionMethod
	{
		public GetTimesMethod() : base("GETTIMES", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
		}
	}

	sealed class GetMillisecondMethod : ModernFunctionMethod
	{
		public GetMillisecondMethod() : base("GETMILLISECOND", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => DateTime.Now.Ticks / 10000;
	}

	sealed class GetSecondMethod : ModernFunctionMethod
	{
		public GetSecondMethod() : base("GETSECOND", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => DateTime.Now.Ticks / 10000000;
	}

	sealed class SaveTextMethod : ModernFunctionMethod
	{
		public SaveTextMethod() : base("SAVETEXT", 2, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string saveText = arguments[0].GetStrValue(context) ?? "";
			bool forceSavDir = arguments.Count > 2 && arguments[2].GetIntValue(context) != 0;
			bool forceUtf8 = arguments.Count > 3 && arguments[3].GetIntValue(context) != 0;
			if (!TryGetTextPath(arguments[1], context, forceSavDir, forSave: true, out string path, out bool indexedPath))
				return 0;

			Encoding encoding = forceUtf8 ? Encoding.UTF8 : Config.SaveEncode;
			try
			{
				if (indexedPath)
				{
					if (forceSavDir)
						Config.ForceCreateSavDir();
					else
						Config.CreateSavDir();
				}
				else
				{
					string directory = Path.GetDirectoryName(path);
					if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
						Directory.CreateDirectory(directory);
				}
				File.WriteAllText(path, saveText, encoding);
				return 1;
			}
			catch
			{
				return 0;
			}
		}
	}

	sealed class LoadTextMethod : ModernFunctionMethod
	{
		public LoadTextMethod() : base("LOADTEXT", 1, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			bool forceSavDir = arguments.Count > 1 && arguments[1].GetIntValue(context) != 0;
			bool forceUtf8 = arguments.Count > 2 && arguments[2].GetIntValue(context) != 0;
			if (!TryGetTextPath(arguments[0], context, forceSavDir, forSave: false, out string path, out _))
				return "";
			if (!File.Exists(path))
				return "";
			Encoding encoding = forceUtf8 ? Encoding.UTF8 : Config.SaveEncode;
			try
			{
				return File.ReadAllText(path, encoding).Replace("\r", "");
			}
			catch
			{
				return "";
			}
		}
	}

	enum ConsoleColorSource
	{
		CurrentForeground,
		DefaultForeground,
		FocusForeground,
		CurrentBackground,
		DefaultBackground,
	}

	sealed class GetConsoleColorMethod : ModernFunctionMethod
	{
		readonly ConsoleColorSource source;

		public GetConsoleColorMethod(string name, ConsoleColorSource source) : base(name, 0, 0)
		{
			this.source = source;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			Color color = source switch
			{
				ConsoleColorSource.CurrentForeground => RequireConsole().StringStyle.Color,
				ConsoleColorSource.DefaultForeground => Config.ForeColor,
				ConsoleColorSource.FocusForeground => Config.FocusColor,
				ConsoleColorSource.CurrentBackground => RequireConsole().bgColor,
				ConsoleColorSource.DefaultBackground => Config.BackColor,
				_ => Config.ForeColor,
			};
			return color.ToArgb() & 0xFFFFFF;
		}
	}

	sealed class GetConsoleStyleMethod : ModernFunctionMethod
	{
		public GetConsoleStyleMethod() : base("GETSTYLE", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			FontStyle fontStyle = RequireConsole().StringStyle.FontStyle;
			long value = 0;
			if ((fontStyle & FontStyle.Bold) == FontStyle.Bold)
				value |= 1;
			if ((fontStyle & FontStyle.Italic) == FontStyle.Italic)
				value |= 2;
			if ((fontStyle & FontStyle.Strikeout) == FontStyle.Strikeout)
				value |= 4;
			if ((fontStyle & FontStyle.Underline) == FontStyle.Underline)
				value |= 8;
			return value;
		}
	}

	sealed class GetConsoleFontMethod : ModernFunctionMethod
	{
		public GetConsoleFontMethod() : base("GETFONT", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return RequireConsole().StringStyle.Fontname ?? Config.FontName ?? "";
		}
	}

	sealed class CurrentAlignMethod : ModernFunctionMethod
	{
		public CurrentAlignMethod() : base("CURRENTALIGN", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return RequireConsole().Alignment switch
			{
				DisplayLineAlignment.LEFT => "LEFT",
				DisplayLineAlignment.CENTER => "CENTER",
				_ => "RIGHT",
			};
		}
	}

	sealed class CurrentRedrawMethod : ModernFunctionMethod
	{
		public CurrentRedrawMethod() : base("CURRENTREDRAW", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return RequireConsole().Redraw == ConsoleRedraw.None ? 0 : 1;
		}
	}

	sealed class IsSkipMethod : ModernFunctionMethod
	{
		public IsSkipMethod() : base("ISSKIP", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var process = GlobalStatic.Process ?? throw new InvalidOperationException("A running process is required for ISSKIP.");
			return process.SkipPrint ? 1 : 0;
		}
	}

	sealed class MesSkipMethod : ModernFunctionMethod
	{
		public MesSkipMethod(string name) : base(name, 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return RequireConsole().MesSkip ? 1 : 0;
		}
	}

	sealed class IsActiveMethod : ModernFunctionMethod
	{
		public IsActiveMethod() : base("ISACTIVE", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return RequireConsole().IsActive ? 1 : 0;
		}
	}

	sealed class GetKeyStateMethod : ModernFunctionMethod
	{
		static readonly short[] keyToggle = new short[256];
		readonly bool triggered;

		public GetKeyStateMethod(string name, bool triggered) : base(name, 1, 1)
		{
			this.triggered = triggered;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!RequireConsole().IsActive)
				return 0;
			long keyCode = arguments[0].GetIntValue(context);
			if (keyCode < 0 || keyCode > 255)
				return 0;

			short state = WinInput.GetKeyState((int)keyCode);
			short previous = keyToggle[keyCode];
			keyToggle[keyCode] = (short)((state & 1) + 1);
			if (!triggered)
				return state < 0 ? 1 : 0;
			return state < 0 && previous != keyToggle[keyCode] ? 1 : 0;
		}
	}

	static class ModernHotkeyState
	{
		static long[] state;

		public static void Initialize(long size)
		{
			if (size < 0 || size > int.MaxValue)
				throw new ArgumentOutOfRangeException(nameof(size), "HOTKEY_STATE_INIT size is out of range.");
			state = new long[(int)size];
		}

		public static void Set(long index, long value)
		{
			if (state == null)
				throw new InvalidOperationException("Use HOTKEY_STATE_INIT before using HOTKEY_STATE.");
			if (index < 0 || index >= state.Length)
				throw new IndexOutOfRangeException("HOTKEY_STATE index is out of range.");
			state[index] = value;
		}

		public static long Get(long index)
		{
			if (state == null)
				throw new InvalidOperationException("Use HOTKEY_STATE_INIT before reading hotkey state.");
			if (index < 0 || index >= state.Length)
				throw new IndexOutOfRangeException("HOTKEY_STATE index is out of range.");
			return state[index];
		}
	}

	sealed class HotkeyStateInitMethod : ModernFunctionMethod
	{
		public HotkeyStateInitMethod() : base("HOTKEY_STATE_INIT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			ModernHotkeyState.Initialize(arguments[0].GetIntValue(context));
			return 0;
		}
	}

	sealed class HotkeyStateMethod : ModernFunctionMethod
	{
		public HotkeyStateMethod() : base("HOTKEY_STATE", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			ModernHotkeyState.Set(arguments[0].GetIntValue(context), arguments[1].GetIntValue(context));
			return 0;
		}
	}

	sealed class MousePositionMethod : ModernFunctionMethod
	{
		readonly bool xAxis;

		public MousePositionMethod(string name, bool xAxis) : base(name, 0, 0)
		{
			this.xAxis = xAxis;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var point = RequireConsole().GetMousePosition();
			return xAxis ? point.X : point.Y;
		}
	}

	sealed class MouseButtonMethod : ModernFunctionMethod
	{
		public MouseButtonMethod() : base("MOUSEB", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return global::GenericUtils.GetPointingButtonInput();
		}
	}

	sealed class GetAnimeTimerMethod : ModernFunctionMethod
	{
		public GetAnimeTimerMethod() : base("GETANIMETIMER", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return RequireConsole().AnimeTimer;
		}
	}

	sealed class ExistSoundMethod : ModernFunctionMethod
	{
		public ExistSoundMethod() : base("EXISTSOUND", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return global::GenericUtils.SoundFileExists(arguments[0].GetStrValue(context)) ? 1 : 0;
		}
	}

	sealed class GetSoundOrBgmInfoMethod : ModernFunctionMethod
	{
		public GetSoundOrBgmInfoMethod() : base("GETSOUNDORBGMINFO", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var info = global::GenericUtils.GetAudioInfo((int)arguments[0].GetIntValue(context));
			if (arguments.Count < 2)
			{
				WriteIntegerResults(context, null, new[] { info.TotalMs, info.CurrentMs, info.Playing, info.Volume, info.Speed });
				return info.TotalMs;
			}
			return arguments[1].GetIntValue(context) switch
			{
				1 => info.TotalMs,
				2 => info.CurrentMs,
				3 => info.Playing,
				4 => info.Volume,
				5 => info.Speed,
				_ => 0,
			};
		}
	}

	sealed class IsPlayingSoundMethod : ModernFunctionMethod
	{
		public IsPlayingSoundMethod() : base("ISPLAYINGSOUND", 0, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			int channel = arguments.Count == 0 ? -1 : (int)arguments[0].GetIntValue(context);
			return global::GenericUtils.FindPlayingSound(channel);
		}
	}

	sealed class SoundControlMethod : ModernFunctionMethod
	{
		public SoundControlMethod() : base("SOUNDCONTROL", 2, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			int channel = (int)arguments[0].GetIntValue(context);
			int action = (int)arguments[1].GetIntValue(context);
			int speed = arguments.Count >= 3 ? (int)arguments[2].GetIntValue(context) : 100;
			return global::GenericUtils.ControlSound(channel, action, speed);
		}
	}

	sealed class IsPlayingBgmMethod : ModernFunctionMethod
	{
		public IsPlayingBgmMethod() : base("ISPLAYINGBGM", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return global::GenericUtils.IsPlayingBgm() ? 1 : 0;
		}
	}

	sealed class BgmControlMethod : ModernFunctionMethod
	{
		public BgmControlMethod() : base("BGMCONTROL", 1, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			int action = (int)arguments[0].GetIntValue(context);
			int speed = arguments.Count >= 2 ? (int)arguments[1].GetIntValue(context) : 100;
			return global::GenericUtils.ControlBgm(action, speed);
		}
	}

	sealed class ClientSizeMethod : ModernFunctionMethod
	{
		public ClientSizeMethod(string name) : base(name, 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var console = RequireConsole();
			return string.Equals(Name, "CLIENTWIDTH", StringComparison.OrdinalIgnoreCase)
				? console.ClientWidth
				: console.ClientHeight;
		}
	}

	sealed class SetTextDrawingModeMethod : ModernFunctionMethod
	{
		public SetTextDrawingModeMethod() : base("SET_TEXT_DRAWING_MODE", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			RequireConsole().SnakeTextDrawingMode = (int)arguments[0].GetIntValue(context);
			return 1;
		}
	}

	sealed class GetTextDrawingModeMethod : ModernFunctionMethod
	{
		public GetTextDrawingModeMethod() : base("GET_TEXT_DRAWING_MODE", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return RequireConsole().SnakeTextDrawingMode;
		}
	}

	sealed class SetSkiaQualityMethod : ModernFunctionMethod
	{
		public SetSkiaQualityMethod() : base("SET_SKIA_QUALITY", 0, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var console = RequireConsole();
			int imageQuality = arguments.Count > 0 ? (int)arguments[0].GetIntValue(context) : console.SnakeImageQuality;
			int fontHinting = arguments.Count > 1 ? (int)arguments[1].GetIntValue(context) : console.SnakeFontHinting;
			int fontEdging = arguments.Count > 2 ? (int)arguments[2].GetIntValue(context) : console.SnakeFontEdging;
			console.SetSnakeSkiaQuality(imageQuality, fontHinting, fontEdging);
			return 1;
		}
	}

	sealed class GetSkiaQualityMethod : ModernFunctionMethod
	{
		public GetSkiaQualityMethod() : base("GET_SKIA_QUALITY", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var console = RequireConsole();
			return arguments[0].GetIntValue(context) switch
			{
				0 => console.SnakeImageQuality,
				1 => console.SnakeFontHinting,
				2 => console.SnakeFontEdging,
				_ => -1,
			};
		}
	}

	sealed class ColorFromNameMethod : ModernFunctionMethod
	{
		public ColorFromNameMethod() : base("COLOR_FROMNAME", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string colorName = arguments[0].GetStrValue(context) ?? "";
			if (string.Equals(colorName, "Transparent", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Transparent is not supported as an era color.");
			return TryGetKnownColorRgb(colorName, out int rgb) ? rgb : -1;
		}
	}

	sealed class ColorFromRgbMethod : ModernFunctionMethod
	{
		public ColorFromRgbMethod() : base("COLOR_FROMRGB", 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long r = ReadRgbByte(arguments[0], context, 1, Name);
			long g = ReadRgbByte(arguments[1], context, 2, Name);
			long b = ReadRgbByte(arguments[2], context, 3, Name);
			return (r << 16) + (g << 8) + b;
		}
	}

	sealed class GetConfigMethod : ModernFunctionMethod
	{
		readonly EraType returnType;

		public GetConfigMethod(string name, EraType returnType) : base(name, 1, 1)
		{
			this.returnType = returnType;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => returnType;

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			SingleTerm term = GetConfigTerm(arguments[0].GetStrValue(context), Name);
			if (term is not SingleLongTerm integerTerm)
				throw new FormatException($"{Name} received a non-integer config value. Use GETCONFIGS for string config values.");
			return integerTerm.Int;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			SingleTerm term = GetConfigTerm(arguments[0].GetStrValue(context), Name);
			if (term is not SingleStrTerm stringTerm)
				throw new FormatException($"{Name} received a non-string config value. Use GETCONFIG for numeric config values.");
			return stringTerm.Str;
		}
	}

	sealed class HtmlStringLenMethod : ModernFunctionMethod
	{
		public HtmlStringLenMethod() : base("HTML_STRINGLEN", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			int len = GetHtmlDisplayLength(arguments[0].GetStrValue(context) ?? "");
			if (arguments.Count == 1 || arguments[1].GetIntValue(context) == 0)
			{
				if (Config.FontSize <= 0)
					return len;
				return len >= 0
					? 2 * len / Config.FontSize + ((2 * len % Config.FontSize != 0) ? 1 : 0)
					: 2 * len / Config.FontSize - ((2 * len % Config.FontSize != 0) ? 1 : 0);
			}
			return len;
		}
	}

	sealed class HtmlSubStringMethod : ModernFunctionMethod
	{
		public HtmlSubStringMethod() : base("HTML_SUBSTRING", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string[] values = GetHtmlSubStrings(arguments[0].GetStrValue(context) ?? "", (int)arguments[1].GetIntValue(context));
			WriteStringResults(context, null, values);
			return values.Length == 0 ? "" : values[0];
		}
	}

	sealed class HtmlStringLinesMethod : ModernFunctionMethod
	{
		public HtmlStringLinesMethod() : base("HTML_STRINGLINES", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string value = arguments[0].GetStrValue(context) ?? "";
			if (value.Length == 0)
				return 0;
			int length = (int)arguments[1].GetIntValue(context);
			long count = 0;
			do
			{
				string[] values = GetHtmlSubStrings(value, length);
				value = values.Length > 1 ? values[1] : "";
				count++;
			}
			while (value.Length > 0);
			return count;
		}
	}

	sealed class HtmlToPlainTextMethod : ModernFunctionMethod
	{
		public HtmlToPlainTextMethod() : base("HTML_TOPLAINTEXT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return HtmlManager.Html2PlainText(arguments[0].GetStrValue(context) ?? "");
		}
	}

	sealed class HtmlEscapeMethod : ModernFunctionMethod
	{
		public HtmlEscapeMethod() : base("HTML_ESCAPE", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return HtmlManager.Escape(arguments[0].GetStrValue(context) ?? "");
		}
	}

	sealed class HtmlGetPrintedStringMethod : ModernFunctionMethod
	{
		public HtmlGetPrintedStringMethod() : base("HTML_GETPRINTEDSTR", 0, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long lineNo = arguments.Count > 0 ? arguments[0].GetIntValue(context) : 0;
			if (lineNo < 0)
				throw new ArgumentOutOfRangeException(nameof(arguments), "HTML_GETPRINTEDSTR line number must not be negative.");
			var displayLines = RequireConsole().GetDisplayLines(lineNo);
			return displayLines == null ? "" : HtmlManager.DisplayLine2Html(displayLines, true);
		}
	}

	sealed class HtmlPopPrintingStringMethod : ModernFunctionMethod
	{
		public HtmlPopPrintingStringMethod() : base("HTML_POPPRINTINGSTR", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var displayLines = RequireConsole().PopDisplayingLines();
			return displayLines == null ? "" : HtmlManager.DisplayLine2Html(displayLines, false);
		}
	}

	sealed class GetDisplayLineMethod : ModernFunctionMethod
	{
		public GetDisplayLineMethod() : base("GETDISPLAYLINE", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return RequireConsole().GetDisplayLineText(arguments[0].GetIntValue(context));
		}
	}

	sealed class ExistsImageLayerMethod : ModernFunctionMethod
	{
		public ExistsImageLayerMethod() : base("EXISTSIMAGELAYER", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return RequireConsole().ExistsImageLayer(arguments[0].GetIntValue(context)) ? 1 : 0;
		}
	}

	sealed class GraphicsStateMethod : ModernFunctionMethod
	{
		public GraphicsStateMethod(string name) : base(name, 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			return Name.ToUpperInvariant() switch
			{
				"GCREATED" => 1,
				"GWIDTH" => g.Width,
				_ => g.Height,
			};
		}
	}

	sealed class GraphicsGetColorMethod : ModernFunctionMethod
	{
		public GraphicsGetColorMethod() : base("GGETCOLOR", 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return -1;
			Point p = ReadPoint(arguments, context, 1);
			if (p.X < 0 || p.X >= g.Width || p.Y < 0 || p.Y >= g.Height)
				return -1;
			return ((long)g.GGetColor(p.X, p.Y).ToArgb()) & 0xFFFFFFFFL;
		}
	}

	sealed class GraphicsSetColorMethod : ModernFunctionMethod
	{
		public GraphicsSetColorMethod() : base("GSETCOLOR", 4, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			Point p = ReadPoint(arguments, context, 2);
			if (p.X < 0 || p.X >= g.Width || p.Y < 0 || p.Y >= g.Height)
				return 0;
			g.GSetColor(ReadArgb(arguments[1], context), p.X, p.Y);
			return 1;
		}
	}

	sealed class GraphicsSetBrushMethod : ModernFunctionMethod
	{
		public GraphicsSetBrushMethod() : base("GSETBRUSH", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			g.GSetBrush(new SolidBrush(ReadArgb(arguments[1], context)));
			return 1;
		}
	}

	sealed class GraphicsSetPenMethod : ModernFunctionMethod
	{
		public GraphicsSetPenMethod() : base("GSETPEN", 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			long width = Math.Max(1, arguments[2].GetIntValue(context));
			g.GSetPen(new Pen(ReadArgb(arguments[1], context), width));
			return 1;
		}
	}

	sealed class GraphicsSetFontMethod : ModernFunctionMethod
	{
		public GraphicsSetFontMethod() : base("GSETFONT", 3, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			string fontName = arguments[1].GetStrValue(context);
			long size = arguments[2].GetIntValue(context);
			if (string.IsNullOrEmpty(fontName) || size <= 0 || size > int.MaxValue)
				return 0;
			FontStyle style = arguments.Count > 3 ? ReadFontStyle(arguments[3].GetIntValue(context)) : FontStyle.Regular;
			g.GSetFont(new Font(fontName, size, style, GraphicsUnit.Pixel));
			return 1;
		}
	}

	sealed class GraphicsDashStyleMethod : ModernFunctionMethod
	{
		public GraphicsDashStyleMethod() : base("GDASHSTYLE", 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			g.GDashStyle(arguments[1].GetIntValue(context), arguments[2].GetIntValue(context));
			return 1;
		}
	}

	sealed class GraphicsGetStateMethod : ModernFunctionMethod
	{
		public GraphicsGetStateMethod(string name) : base(name, 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			return Name.ToUpperInvariant() switch
			{
				"GGETBRUSH" => ((long)g.BrushColor.ToArgb()) & 0xFFFFFFFFL,
				"GGETPEN" => ((long)g.PenColor.ToArgb()) & 0xFFFFFFFFL,
				"GGETPENWIDTH" => g.PenWidth,
				"GGETFONTSIZE" => g.Fontsize,
				_ => g.Fontstyle,
			};
		}
	}

	sealed class GraphicsGetFontMethod : ModernFunctionMethod
	{
		public GraphicsGetFontMethod() : base("GGETFONT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			return g.IsCreated ? g.Fontname : "";
		}
	}

	sealed class GraphicsGetTextSizeMethod : ModernFunctionMethod
	{
		public GraphicsGetTextSizeMethod() : base("GGETTEXTSIZE", 3, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string text = arguments[0].GetStrValue(context) ?? "";
			string fontName = arguments[1].GetStrValue(context);
			long fontSize = arguments[2].GetIntValue(context);
			if (fontSize <= 0 || fontSize > int.MaxValue)
				return 0;
			FontStyle style = arguments.Count > 3 ? ReadFontStyle(arguments[3].GetIntValue(context)) : FontStyle.Regular;
			using var font = new Font(string.IsNullOrEmpty(fontName) ? Config.FontName : fontName, fontSize, style, GraphicsUnit.Pixel);
			long width = uEmuera.Utils.GetDisplayLength(text, font);
			SetIntegerResult(context, 1, fontSize);
			return width;
		}
	}

	sealed class GraphicsDrawTextMethod : ModernFunctionMethod
	{
		public GraphicsDrawTextMethod() : base("GDRAWTEXT", 2, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			string text = arguments[1].GetStrValue(context) ?? "";
			Point p = arguments.Count >= 4 ? ReadPoint(arguments, context, 2) : Point.Empty;
			bool drawn = g.GDrawString(text, p.X, p.Y);
			using var font = new Font(g.Fontname, g.Fontsize, ReadFontStyle(g.Fontstyle), GraphicsUnit.Pixel);
			long width = uEmuera.Utils.GetDisplayLength(text, font);
			SetIntegerResult(context, 1, width);
			SetIntegerResult(context, 2, g.Fontsize);
			return drawn ? 1 : 0;
		}
	}

	sealed class GraphicsCreateMethod : ModernFunctionMethod
	{
		public GraphicsCreateMethod() : base("GCREATE", 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (g.IsCreated)
				return 0;
			Point size = ReadPoint(arguments, context, 1);
			if (size.X <= 0 || size.Y <= 0 || size.X > AbstractImage.MAX_IMAGESIZE || size.Y > AbstractImage.MAX_IMAGESIZE)
				return 0;
			g.GCreate(size.X, size.Y, false);
			return 1;
		}
	}

	sealed class GraphicsCreateFromFileMethod : ModernFunctionMethod
	{
		readonly bool fromSaveData;

		public GraphicsCreateFromFileMethod(string name, bool fromSaveData) : base(name, 2, 2)
		{
			this.fromSaveData = fromSaveData;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (g.IsCreated)
				return 0;
			string path = fromSaveData
				? GetSaveDataGraphicsPath(arguments[1].GetIntValue(context))
				: ResolveImagePath(arguments[1].GetStrValue(context), false);
			if (string.IsNullOrEmpty(path) || !uEmuera.Utils.FileExists(path))
				return 0;
			using BitmapTexture bmp = new BitmapTexture(path);
			if (bmp.Width <= 0 || bmp.Height <= 0 || bmp.Width > AbstractImage.MAX_IMAGESIZE || bmp.Height > AbstractImage.MAX_IMAGESIZE)
				return 0;
			g.GCreateFromF(bmp, false);
			return g.IsCreated ? 1 : 0;
		}
	}

	sealed class GraphicsSaveMethod : ModernFunctionMethod
	{
		public GraphicsSaveMethod() : base("GSAVE", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated || g.Bitmap == null)
				return 0;
			string path = GetSaveDataGraphicsPath(arguments[1].GetIntValue(context));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				g.Bitmap.Save(path);
				return 1;
			}
			catch
			{
				return 0;
			}
		}
	}

	sealed class GraphicsDisposeMethod : ModernFunctionMethod
	{
		public GraphicsDisposeMethod() : base("GDISPOSE", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			g.GDispose();
			return 1;
		}
	}

	sealed class GraphicsClearMethod : ModernFunctionMethod
	{
		public GraphicsClearMethod() : base("GCLEAR", 2, 6) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count != 2 && arguments.Count != 6)
				throw new FormatException("GCLEAR expects 2 or 6 arguments.");
		}
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			if (arguments.Count == 2)
			{
				g.GClear(ReadArgb(arguments[1], context));
				return 1;
			}
			FillImageRect(g, ReadArgb(arguments[1], context), ReadRectangle(arguments, context, 2));
			return 1;
		}
	}

	sealed class GraphicsFillRectangleMethod : ModernFunctionMethod
	{
		public GraphicsFillRectangleMethod() : base("GFILLRECTANGLE", 5, 5) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			g.GFillRectangle(ReadRectangle(arguments, context, 1));
			return 1;
		}
	}

	sealed class GraphicsDrawLineMethod : ModernFunctionMethod
	{
		public GraphicsDrawLineMethod() : base("GDRAWLINE", 5, 5) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			Point from = ReadPoint(arguments, context, 1);
			Point to = ReadPoint(arguments, context, 3);
			g.GDrawLine(from.X, from.Y, to.X, to.Y);
			return 1;
		}
	}

	sealed class GraphicsPolygonPointAddMethod : ModernFunctionMethod
	{
		public GraphicsPolygonPointAddMethod() : base("G_POLYGON_POINT_ADD", 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			g.GDrawPolygonAddPoint(ReadPoint(arguments, context, 1));
			return 1;
		}
	}

	sealed class GraphicsPolygonPointClearMethod : ModernFunctionMethod
	{
		public GraphicsPolygonPointClearMethod() : base("G_POLYGON_POINT_CLEAR", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			g.GDrawPolygonClearPoint();
			return 1;
		}
	}

	sealed class GraphicsPolygonDrawMethod : ModernFunctionMethod
	{
		public GraphicsPolygonDrawMethod() : base("G_POLYGON_DRAW", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			g.GDrawPolygon();
			return 1;
		}
	}

	sealed class GraphicsPolygonFillMethod : ModernFunctionMethod
	{
		public GraphicsPolygonFillMethod() : base("G_POLYGON_FILL", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			g.GFillPolygon();
			return 1;
		}
	}

	sealed class GraphicsDrawGMethod : ModernFunctionMethod
	{
		public GraphicsDrawGMethod() : base("GDRAWG", 10, 11) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage dest = ReadGraphics(arguments[0], context);
			GraphicsImage src = ReadGraphics(arguments[1], context);
			if (!dest.IsCreated || !src.IsCreated)
				return 0;
			Rectangle destRect = ReadRectangle(arguments, context, 2);
			Rectangle srcRect = ReadRectangle(arguments, context, 6);
			if (arguments.Count == 10)
				dest.GDrawG(src, destRect, srcRect);
			else
				dest.GDrawG(src, destRect, srcRect, ReadColorMatrix(arguments[10], context));
			return 1;
		}
	}

	sealed class GraphicsDrawGWithMaskMethod : ModernFunctionMethod
	{
		public GraphicsDrawGWithMaskMethod() : base("GDRAWGWITHMASK", 5, 5) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage dest = ReadGraphics(arguments[0], context);
			GraphicsImage src = ReadGraphics(arguments[1], context);
			GraphicsImage mask = ReadGraphics(arguments[2], context);
			if (!dest.IsCreated || !src.IsCreated || !mask.IsCreated || src.Width != mask.Width || src.Height != mask.Height)
				return 0;
			Point p = ReadPoint(arguments, context, 3);
			dest.GDrawGWithMask(src, mask, p);
			return 1;
		}
	}

	sealed class GraphicsDrawGWithRotateMethod : ModernFunctionMethod
	{
		public GraphicsDrawGWithRotateMethod() : base("GDRAWGWITHROTATE", 3, 5) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count != 3 && arguments.Count != 5)
				throw new FormatException("GDRAWGWITHROTATE expects 3 or 5 arguments.");
		}
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage dest = ReadGraphics(arguments[0], context);
			GraphicsImage src = ReadGraphics(arguments[1], context);
			if (!dest.IsCreated || !src.IsCreated)
				return 0;
			long angle = arguments[2].GetIntValue(context);
			Point pivot = arguments.Count == 5 ? ReadPoint(arguments, context, 3) : new Point(src.Width / 2, src.Height / 2);
			dest.GDrawGWithRotate(src, angle, pivot.X, pivot.Y);
			return 1;
		}
	}

	sealed class GraphicsRotateMethod : ModernFunctionMethod
	{
		public GraphicsRotateMethod() : base("GROTATE", 2, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count != 2 && arguments.Count != 4)
				throw new FormatException("GROTATE expects 2 or 4 arguments.");
		}
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			if (!g.IsCreated)
				return 0;
			long angle = arguments[1].GetIntValue(context);
			Point pivot = arguments.Count == 4 ? ReadPoint(arguments, context, 2) : new Point(g.Width / 2, g.Height / 2);
			g.GRotate(angle, pivot.X, pivot.Y);
			return 1;
		}
	}

	sealed class GraphicsDrawSpriteMethod : ModernFunctionMethod
	{
		public GraphicsDrawSpriteMethod() : base("GDRAWSPRITE", 2, 7) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count != 2 && arguments.Count != 4 && arguments.Count != 6 && arguments.Count != 7)
				throw new FormatException("GDRAWSPRITE expects 2, 4, 6, or 7 arguments.");
		}
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage dest = ReadGraphics(arguments[0], context);
			if (!dest.IsCreated)
				return 0;
			ASprite sprite = AppContents.GetSprite(arguments[1].GetStrValue(context));
			if (sprite == null || !sprite.IsCreated)
				return 0;
			Rectangle destRect = new Rectangle(0, 0, sprite.DestBaseSize.Width, sprite.DestBaseSize.Height);
			if (arguments.Count == 4)
			{
				Point p = ReadPoint(arguments, context, 2);
				destRect.X = p.X;
				destRect.Y = p.Y;
			}
			else if (arguments.Count >= 6)
			{
				destRect = ReadRectangle(arguments, context, 2);
			}
			if (arguments.Count == 7)
				dest.GDrawCImg(sprite, destRect, ReadColorMatrix(arguments[6], context));
			else
				dest.GDrawCImg(sprite, destRect);
			return 1;
		}
	}

	sealed class SpriteStateMethod : ModernFunctionMethod
	{
		public SpriteStateMethod(string name) : base(name, 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			ASprite sprite = AppContents.GetSprite(arguments[0].GetStrValue(context));
			if (sprite == null || !sprite.IsCreated || sprite.DestBaseSize.Width <= 0 || sprite.DestBaseSize.Height <= 0)
				return 0;
			return Name.ToUpperInvariant() switch
			{
				"SPRITECREATED" => 1,
				"SPRITEWIDTH" => sprite.DestBaseSize.Width,
				"SPRITEHEIGHT" => sprite.DestBaseSize.Height,
				"SPRITEPOSX" => sprite.DestBasePosition.X,
				_ => sprite.DestBasePosition.Y,
			};
		}
	}

	sealed class SpriteSetPosMethod : ModernFunctionMethod
	{
		public SpriteSetPosMethod(string name) : base(name, 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			ASprite sprite = AppContents.GetSprite(arguments[0].GetStrValue(context));
			if (sprite == null || !sprite.IsCreated)
				return 0;
			Point p = ReadPoint(arguments, context, 1);
			if (string.Equals(Name, "SPRITEMOVE", StringComparison.OrdinalIgnoreCase))
				sprite.DestBasePosition.Offset(p);
			else
				sprite.DestBasePosition = p;
			return 1;
		}
	}

	sealed class SpriteGetColorMethod : ModernFunctionMethod
	{
		public SpriteGetColorMethod() : base("SPRITEGETCOLOR", 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			ASprite sprite = AppContents.GetSprite(arguments[0].GetStrValue(context));
			if (sprite == null || !sprite.IsCreated)
				return -1;
			Point p = ReadPoint(arguments, context, 1);
			if (p.X < 0 || p.X >= sprite.DestBaseSize.Width || p.Y < 0 || p.Y >= sprite.DestBaseSize.Height)
				return -1;
			try
			{
				return ((long)sprite.SpriteGetColor(p.X, p.Y).ToArgb()) & 0xFFFFFFFFL;
			}
			catch
			{
				return -1;
			}
		}
	}

	sealed class SpriteCreateMethod : ModernFunctionMethod
	{
		public SpriteCreateMethod() : base("SPRITECREATE", 2, 6) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count != 2 && arguments.Count != 6)
				throw new FormatException("SPRITECREATE expects 2 or 6 arguments.");
		}
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(context);
			if (string.IsNullOrEmpty(name))
				return 0;
			ASprite existing = AppContents.GetSprite(name);
			if (existing != null && existing.IsCreated)
				return 0;
			GraphicsImage g = ReadGraphics(arguments[1], context);
			if (!g.IsCreated)
				return 0;
			Rectangle rect = arguments.Count == 6 ? ReadRectangle(arguments, context, 2) : new Rectangle(0, 0, g.Width, g.Height);
			if (!ClipPositiveRectangleToGraphics(g, ref rect))
				return 0;
			if (rect.Width <= 0 || rect.Height <= 0)
				return 0;
			AppContents.CreateSpriteG(name, g, rect);
			return 1;
		}

		static bool ClipPositiveRectangleToGraphics(GraphicsImage g, ref Rectangle rect)
		{
			if (rect.Width <= 0 || rect.Height <= 0)
				return false;
			int left = Math.Max(0, rect.X);
			int top = Math.Max(0, rect.Y);
			int right = Math.Min(g.Width, rect.X + rect.Width);
			int bottom = Math.Min(g.Height, rect.Y + rect.Height);
			if (right <= left || bottom <= top)
				return false;
			rect = new Rectangle(left, top, right - left, bottom - top);
			return true;
		}
	}

	sealed class SpriteCreateFromFileMethod : ModernFunctionMethod
	{
		public SpriteCreateFromFileMethod() : base("SPRITECREATEFROMFILE", 2, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(context);
			string path = ResolveImagePath(arguments[1].GetStrValue(context), arguments.Count > 2 && arguments[2].GetIntValue(context) != 0);
			return AppContents.CreateSpriteFromFileDynamic(name, path) ? 1 : 0;
		}
	}

	sealed class SpriteAnimeCreateMethod : ModernFunctionMethod
	{
		public SpriteAnimeCreateMethod() : base("SPRITEANIMECREATE", 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(context);
			if (string.IsNullOrEmpty(name) || AppContents.GetSprite(name)?.IsCreated == true)
				return 0;
			Point size = ReadPoint(arguments, context, 1);
			if (size.X <= 0 || size.Y <= 0 || size.X > AbstractImage.MAX_IMAGESIZE || size.Y > AbstractImage.MAX_IMAGESIZE)
				return 0;
			AppContents.CreateSpriteAnime(name, size.X, size.Y);
			return 1;
		}
	}

	sealed class SpriteAnimeAddFrameMethod : ModernFunctionMethod
	{
		public SpriteAnimeAddFrameMethod() : base("SPRITEANIMEADDFRAME", 9, 9) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (AppContents.GetSprite(arguments[0].GetStrValue(context)) is not SpriteAnime anime)
				return 0;
			GraphicsImage g = ReadGraphics(arguments[1], context);
			if (!g.IsCreated)
				return 0;
			Rectangle rect = ReadRectangle(arguments, context, 2);
			Point offset = ReadPoint(arguments, context, 6);
			long delay = arguments[8].GetIntValue(context);
			if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 || rect.X + rect.Width > g.Width || rect.Y + rect.Height > g.Height || delay <= 0 || delay > int.MaxValue)
				return 0;
			return anime.AddFrame(g, rect, offset, (int)delay) ? 1 : 0;
		}
	}

	sealed class SpriteDisposeMethod : ModernFunctionMethod
	{
		public SpriteDisposeMethod() : base("SPRITEDISPOSE", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(context);
			ASprite sprite = AppContents.GetSprite(name);
			if (sprite == null || !sprite.IsCreated)
				return 0;
			AppContents.SpriteDispose(name);
			return 1;
		}
	}

	sealed class SpriteDisposeAllMethod : ModernFunctionMethod
	{
		public SpriteDisposeAllMethod() : base("SPRITEDISPOSEALL", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return AppContents.SpriteDisposeAll(arguments[0].GetIntValue(context) != 0);
		}
	}

	sealed class CbgClearMethod : ModernFunctionMethod
	{
		public CbgClearMethod() : base("CBGCLEAR", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			RequireConsole().CBG_Clear();
			return 1;
		}
	}

	sealed class CbgClearButtonMethod : ModernFunctionMethod
	{
		public CbgClearButtonMethod() : base("CBGCLEARBUTTON", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			RequireConsole().CBG_ClearButton();
			return 1;
		}
	}

	sealed class CbgRemoveRangeMethod : ModernFunctionMethod
	{
		public CbgRemoveRangeMethod() : base("CBGREMOVERANGE", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			RequireConsole().CBG_ClearRange((int)arguments[0].GetIntValue(context), (int)arguments[1].GetIntValue(context));
			return 1;
		}
	}

	sealed class CbgRemoveBMapMethod : ModernFunctionMethod
	{
		public CbgRemoveBMapMethod() : base("CBGREMOVEBMAP", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			RequireConsole().CBG_ClearBMap();
			return 1;
		}
	}

	sealed class CbgSetGraphicsMethod : ModernFunctionMethod
	{
		public CbgSetGraphicsMethod() : base("CBGSETG", 4, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			GraphicsImage g = ReadGraphics(arguments[0], context);
			Point p = ReadPoint(arguments, context, 1);
			long z = arguments[3].GetIntValue(context);
			return z != 0 && RequireConsole().CBG_SetGraphics(g, p.X, p.Y, (int)z) ? 1 : 0;
		}
	}

	sealed class CbgSetButtonMapGraphicsMethod : ModernFunctionMethod
	{
		public CbgSetButtonMapGraphicsMethod() : base("CBGSETBMAPG", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return RequireConsole().CBG_SetButtonMap(ReadGraphics(arguments[0], context)) ? 1 : 0;
		}
	}

	sealed class CbgSetSpriteMethod : ModernFunctionMethod
	{
		public CbgSetSpriteMethod() : base("CBGSETSPRITE", 4, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			ASprite sprite = AppContents.GetSprite(arguments[0].GetStrValue(context));
			Point p = ReadPoint(arguments, context, 1);
			long z = arguments[3].GetIntValue(context);
			return z != 0 && RequireConsole().CBG_SetImage(sprite, p.X, p.Y, (int)z) ? 1 : 0;
		}
	}

	sealed class CbgSetButtonSpriteMethod : ModernFunctionMethod
	{
		public CbgSetButtonSpriteMethod() : base("CBGSETBUTTONSPRITE", 6, 7) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long button = arguments[0].GetIntValue(context);
			if (button < 0 || button > 0xFFFFFF)
				return 0;
			ASprite normal = AppContents.GetSprite(arguments[1].GetStrValue(context));
			ASprite hover = AppContents.GetSprite(arguments[2].GetStrValue(context));
			Point p = ReadPoint(arguments, context, 3);
			long z = arguments[5].GetIntValue(context);
			string tooltip = arguments.Count > 6 ? arguments[6].GetStrValue(context) : null;
			return z != 0 && RequireConsole().CBG_SetButtonImage((int)button, normal, hover, p.X, p.Y, (int)z, tooltip) ? 1 : 0;
		}
	}

	sealed class TextBoxMethod : ModernFunctionMethod
	{
		readonly bool setter;

		public TextBoxMethod(string name, bool setter) : base(name, setter ? 1 : 0, setter ? 1 : 0)
		{
			this.setter = setter;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return setter ? EraType.Integer : EraType.String;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			RequireConsole().SetTextBoxText(arguments[0].GetStrValue(context));
			return 1;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return RequireConsole().GetTextBoxText();
		}
	}

	sealed class MoveTextBoxMethod : ModernFunctionMethod
	{
		readonly bool resume;

		public MoveTextBoxMethod(bool resume) : base(resume ? "RESUMETEXTBOX" : "MOVETEXTBOX", resume ? 0 : 3, resume ? 0 : 3)
		{
			this.resume = resume;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var window = GlobalStatic.MainWindow;
			if (window == null)
				return 0;
			if (resume)
				window.ResetTextBoxPos();
			else
				window.SetTextBoxPos(
					(int)arguments[0].GetIntValue(context),
					(int)arguments[1].GetIntValue(context),
					(int)arguments[2].GetIntValue(context));
			return 1;
		}
	}

	sealed class ErdNameMethod : ModernFunctionMethod
	{
		public ErdNameMethod() : base("ERDNAME", 2, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (arguments[0] is not ModernVariableTerm term)
				throw new ArgumentException("ERDNAME first argument must be a variable.");
			long value = arguments[1].GetIntValue(context);
			int index = arguments.Count > 2 ? (int)arguments[2].GetIntValue(context) : -1;
			return TryIntegerToKeyword(term.Identifier.Name, value, index, out string keyword) ? keyword : "";
		}
	}

	sealed class StrFormMethod : ModernFunctionMethod
	{
		public StrFormMethod() : base("STRFORM", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string source = arguments[0].GetStrValue(context) ?? "";
			try
			{
				StrFormWord word = LexicalAnalyzer.AnalyseFormattedString(new StringStream(source), FormStrEndWith.EoL, false);
				StrForm strForm = StrForm.FromWordToken(word);
				return strForm.GetString(RequireLegacyExpressionMediator());
			}
			catch (Exception e)
			{
				throw new InvalidOperationException($"STRFORM failed to expand \"{source}\": {e.Message}", e);
			}
		}
	}

	sealed class FlowInputMethod : ModernFunctionMethod
	{
		public FlowInputMethod() : base("FLOWINPUT", 1, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var process = RequireLegacyProcess();
			process.flowinputDef = arguments[0].GetIntValue(context);
			if (arguments.Count > 1)
				process.flowinput = arguments[1].GetIntValue(context) != 0;
			if (arguments.Count > 2)
				process.flowinputCanSkip = arguments[2].GetIntValue(context) != 0;
			if (arguments.Count > 3)
				process.flowinputForceSkip = arguments[3].GetIntValue(context) != 0;
			return 0;
		}
	}

	sealed class FlowInputsMethod : ModernFunctionMethod
	{
		public FlowInputsMethod() : base("FLOWINPUTS", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var process = RequireLegacyProcess();
			process.flowinputString = arguments[0].GetIntValue(context) != 0;
			if (arguments.Count > 1)
				process.flowinputDefString = arguments[1].GetStrValue(context) ?? "";
			return 0;
		}
	}

	sealed class RandMethod : ModernFunctionMethod
	{
		readonly Random random = new();

		public RandMethod() : base("RAND", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long min = 0;
			long max;
			if (arguments.Count == 1)
				max = arguments[0].GetIntValue(context);
			else
			{
				min = arguments[0].GetIntValue(context);
				max = arguments[1].GetIntValue(context);
			}
			if (max <= min)
				throw new ArgumentOutOfRangeException(nameof(arguments), "RAND maximum must be greater than minimum.");
			long range = max - min;
			if (range <= int.MaxValue)
				return random.Next((int)range) + min;
			var bytes = new byte[8];
			random.NextBytes(bytes);
			long value = BitConverter.ToInt64(bytes, 0) & long.MaxValue;
			return (value % range) + min;
		}

		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			double min = 0.0d;
			double max;
			if (arguments.Count == 1)
				max = ToDouble(arguments[0], context);
			else
			{
				min = ToDouble(arguments[0], context);
				max = ToDouble(arguments[1], context);
			}
			if (max <= min)
				throw new ArgumentOutOfRangeException(nameof(arguments), "RAND maximum must be greater than minimum.");
			return random.NextDouble() * (max - min) + min;
		}
	}

	sealed class MinMaxMethod : ModernFunctionMethod
	{
		readonly bool isMax;

		public MinMaxMethod(string name, bool isMax) : base(name, 1, int.MaxValue)
		{
			this.isMax = isMax;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long value = arguments[0].GetIntValue(context);
			for (int i = 1; i < arguments.Count; i++)
			{
				long next = arguments[i].GetIntValue(context);
				value = isMax ? Math.Max(value, next) : Math.Min(value, next);
			}
			return value;
		}

		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			double value = ToDouble(arguments[0], context);
			for (int i = 1; i < arguments.Count; i++)
			{
				double next = ToDouble(arguments[i], context);
				value = isMax ? Math.Max(value, next) : Math.Min(value, next);
			}
			return value;
		}
	}

	sealed class LimitMethod : ModernFunctionMethod
	{
		public LimitMethod() : base("LIMIT", 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long value = arguments[0].GetIntValue(context);
			long min = arguments[1].GetIntValue(context);
			long max = arguments[2].GetIntValue(context);
			if (value < min)
				return min;
			return value > max ? max : value;
		}

		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			double value = ToDouble(arguments[0], context);
			double min = ToDouble(arguments[1], context);
			double max = ToDouble(arguments[2], context);
			if (value < min)
				return min;
			return value > max ? max : value;
		}
	}

	sealed class InRangeMethod : ModernFunctionMethod
	{
		public InRangeMethod() : base("INRANGE", 3, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
			{
				double value = ToDouble(arguments[0], context);
				return value >= ToDouble(arguments[1], context) && value <= ToDouble(arguments[2], context) ? 1 : 0;
			}
			long longValue = arguments[0].GetIntValue(context);
			return longValue >= arguments[1].GetIntValue(context) && longValue <= arguments[2].GetIntValue(context) ? 1 : 0;
		}
	}

	sealed class MatchMethod : ModernFunctionMethod
	{
		public MatchMethod() : base("MATCH", 2, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = GetWholeArrayArgument(arguments[0], Name);
			var (start, end) = GetArrayRange(term.Identifier, context, arguments, 2);
			long count = 0;
			for (long i = start; i < end; i++)
			{
				if (ExpressionEqualsArrayValue(term.Identifier, context, i, arguments[1]))
					count++;
			}
			return count;
		}
	}

	sealed class CharaMatchMethod : ModernFunctionMethod
	{
		public CharaMatchMethod() : base("CMATCH", 2, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments[0] is not ModernVariableTerm term || !term.Identifier.IsCharacterData)
				throw new FormatException($"{Name} first argument must be a character variable.");
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = (ModernVariableTerm)arguments[0];
			var (start, end) = GetCharacterArrayRange(context, arguments, 2, Name);
			long count = 0;
			for (long i = start; i < end; i++)
			{
				if (ExpressionEqualsCharaValue(term, context, i, arguments[1]))
					count++;
			}
			return count;
		}
	}

	sealed class InRangeArrayMethod : ModernFunctionMethod
	{
		public InRangeArrayMethod() : base("INRANGEARRAY", 3, 5) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = GetWholeArrayArgument(arguments[0], Name);
			var (start, end) = GetArrayRange(term.Identifier, context, arguments, 3);
			long count = 0;
			for (long i = start; i < end; i++)
			{
				if (term.Identifier.IsFloat)
				{
					double value = term.Identifier.GetFloatValue(context, new[] { i });
					if (value >= ToDouble(arguments[1], context) && value < ToDouble(arguments[2], context))
						count++;
				}
				else
				{
					long value = term.Identifier.GetIntValue(context, new[] { i });
					if (value >= arguments[1].GetIntValue(context) && value < arguments[2].GetIntValue(context))
						count++;
				}
			}
			return count;
		}
	}

	sealed class CharaInRangeArrayMethod : ModernFunctionMethod
	{
		public CharaInRangeArrayMethod() : base("INRANGECARRAY", 3, 5) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments[0] is not ModernVariableTerm term || !term.Identifier.IsCharacterData || !term.Identifier.IsInteger)
				throw new FormatException($"{Name} first argument must be an integer character variable.");
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = (ModernVariableTerm)arguments[0];
			long min = arguments[1].GetIntValue(context);
			long max = arguments[2].GetIntValue(context);
			var (start, end) = GetCharacterArrayRange(context, arguments, 3, Name);
			long count = 0;
			for (long i = start; i < end; i++)
			{
				long value = term.Identifier.GetIntValue(context, BuildCharaArguments(term, context, i));
				if (value >= min && value < max)
					count++;
			}
			return count;
		}
	}

	sealed class SumArrayMethod : ModernFunctionMethod
	{
		public SumArrayMethod() : base("SUMARRAY", 1, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return arguments[0] is ModernVariableTerm term && term.Identifier.IsFloat ? EraType.Float : EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = GetWholeArrayArgument(arguments[0], Name);
			var (start, end) = GetArrayRange(term.Identifier, context, arguments, 1);
			long sum = 0;
			for (long i = start; i < end; i++)
				sum += term.Identifier.GetIntValue(context, new[] { i });
			return sum;
		}

		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = GetWholeArrayArgument(arguments[0], Name);
			var (start, end) = GetArrayRange(term.Identifier, context, arguments, 1);
			double sum = 0.0d;
			for (long i = start; i < end; i++)
				sum += term.Identifier.GetFloatValue(context, new[] { i });
			return sum;
		}
	}

	sealed class CharaSumArrayMethod : ModernFunctionMethod
	{
		public CharaSumArrayMethod() : base("SUMCARRAY", 1, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments[0] is not ModernVariableTerm term || !term.Identifier.IsCharacterData || !term.Identifier.IsInteger)
				throw new FormatException($"{Name} first argument must be an integer character variable.");
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = (ModernVariableTerm)arguments[0];
			var (start, end) = GetCharacterArrayRange(context, arguments, 1, Name);
			long sum = 0;
			for (long i = start; i < end; i++)
				sum += term.Identifier.GetIntValue(context, BuildCharaArguments(term, context, i));
			return sum;
		}
	}

	sealed class MinMaxArrayMethod : ModernFunctionMethod
	{
		readonly bool isMax;

		public MinMaxArrayMethod(string name, bool isMax) : base(name, 1, 3)
		{
			this.isMax = isMax;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return arguments[0] is ModernVariableTerm term && term.Identifier.IsFloat ? EraType.Float : EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = GetWholeArrayArgument(arguments[0], Name);
			var (start, end) = GetArrayRange(term.Identifier, context, arguments, 1);
			RequireNonEmptyRange(start, end, Name);
			long value = term.Identifier.GetIntValue(context, new[] { start });
			for (long i = start + 1; i < end; i++)
			{
				long next = term.Identifier.GetIntValue(context, new[] { i });
				value = isMax ? Math.Max(value, next) : Math.Min(value, next);
			}
			return value;
		}

		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = GetWholeArrayArgument(arguments[0], Name);
			var (start, end) = GetArrayRange(term.Identifier, context, arguments, 1);
			RequireNonEmptyRange(start, end, Name);
			double value = term.Identifier.GetFloatValue(context, new[] { start });
			for (long i = start + 1; i < end; i++)
			{
				double next = term.Identifier.GetFloatValue(context, new[] { i });
				value = isMax ? Math.Max(value, next) : Math.Min(value, next);
			}
			return value;
		}
	}

	sealed class CharaMinMaxArrayMethod : ModernFunctionMethod
	{
		readonly bool isMax;

		public CharaMinMaxArrayMethod(string name, bool isMax) : base(name, 1, 3)
		{
			this.isMax = isMax;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments[0] is not ModernVariableTerm term || !term.Identifier.IsCharacterData || !term.Identifier.IsInteger)
				throw new FormatException($"{Name} first argument must be an integer character variable.");
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = (ModernVariableTerm)arguments[0];
			var (start, end) = GetCharacterArrayRange(context, arguments, 1, Name);
			RequireNonEmptyRange(start, end, Name);
			long value = term.Identifier.GetIntValue(context, BuildCharaArguments(term, context, start));
			for (long i = start + 1; i < end; i++)
			{
				long next = term.Identifier.GetIntValue(context, BuildCharaArguments(term, context, i));
				value = isMax ? Math.Max(value, next) : Math.Min(value, next);
			}
			return value;
		}
	}

	sealed class ArrayMultiSortMethod : ModernFunctionMethod
	{
		public ArrayMultiSortMethod() : base("ARRAYMSORT", 2, int.MaxValue) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var baseTerm = GetSortableWholeArrayArgument(arguments[0], Name);
			int[] sortedIndices = GetSortedIndicesUntilDefault(baseTerm, context, true, -1);
			foreach (var argument in arguments)
			{
				var target = GetSortableWholeArrayArgument(argument, Name);
				if (!ApplySortToOneDimensionalArray(target, context, sortedIndices))
					return 0;
			}
			return 1;
		}
	}

	sealed class ArrayMultiSortExMethod : ModernFunctionMethod
	{
		public ArrayMultiSortExMethod() : base("ARRAYMSORTEX", 2, 4) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			bool isAscending = arguments.Count < 3 || arguments[2].GetIntValue(context) != 0;
			long fixedLengthInput = arguments.Count < 4 ? -1 : arguments[3].GetIntValue(context);
			if (fixedLengthInput == 0)
				return 0;
			if (fixedLengthInput < -1 || fixedLengthInput > int.MaxValue)
				throw new InvalidOperationException($"{Name}: fixedLength parameter must be between -1 and {int.MaxValue}.");

			ModernVariableTerm baseTerm = arguments[0] is ModernVariableTerm term
				? GetSortableWholeArrayArgument(term, Name)
				: GetSortableWholeArrayArgument(ParseVariable(arguments[0].GetStrValue(context), context), Name);

			var namesTerm = arguments[1] as ModernVariableTerm;
			if (namesTerm == null || !namesTerm.Identifier.IsString || namesTerm.Identifier.Dimension != VariableDimension.Array1D)
				throw new InvalidOperationException($"{Name} needs a string array variable as the second argument.");

			int[] sortedIndices = GetSortedIndicesUntilDefault(baseTerm, context, isAscending, (int)fixedLengthInput);
			int targetNameCount = GetVariableArrayLength(namesTerm.Identifier, context);
			for (int i = 0; i < targetNameCount; i++)
			{
				string variableName = namesTerm.Identifier.GetStrValue(context, new long[] { i }) ?? "";
				var target = GetSortableWholeArrayArgument(ParseVariable(variableName, context), Name);
				if (!ApplySortToOneDimensionalArray(target, context, sortedIndices))
					return 0;
			}
			return 1;
		}
	}

	sealed class FindElementMethod : ModernFunctionMethod
	{
		readonly bool isLast;

		public FindElementMethod(string name, bool isLast) : base(name, 2, 5)
		{
			this.isLast = isLast;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var term = GetWholeArrayArgument(arguments[0], Name);
			long start = arguments.Count > 2 ? arguments[2].GetIntValue(context) : 0;
			long end = arguments.Count > 3 ? arguments[3].GetIntValue(context) : GetVariableArrayLength(term.Identifier, context);
			bool exact = arguments.Count > 4 && arguments[4].GetIntValue(context) != 0;
			ValidateArrayRange(term.Identifier, context, start, end);
			if (start >= end)
				return -1;

			if (term.Identifier.IsString)
				return FindStringElement(term.Identifier, context, arguments[1].GetStrValue(context) ?? "", start, end, exact, isLast);

			if (term.Identifier.IsFloat)
			{
				double target = ToDouble(arguments[1], context);
				return FindNumericElement(term.Identifier, context, target, start, end, isLast);
			}

			long longTarget = arguments[1].GetIntValue(context);
			return FindNumericElement(term.Identifier, context, longTarget, start, end, isLast);
		}
	}

	sealed class MatchAllMethod : ModernFunctionMethod
	{
		readonly bool useStringName;

		public MatchAllMethod(string name, bool useStringName) : base(name, 2, 5)
		{
			this.useStringName = useStringName;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			ModernVariableTerm term = useStringName
				? GetWholeArrayArgument(ParseVariable(arguments[0].GetStrValue(context), context), Name)
				: GetWholeArrayArgument(arguments[0], Name);
			long start = arguments.Count >= 3 ? arguments[2].GetIntValue(context) : 0;
			long end = arguments.Count >= 4 ? arguments[3].GetIntValue(context) : GetVariableArrayLength(term.Identifier, context);
			long length = GetVariableArrayLength(term.Identifier, context);
			if (start < 0 || end < 0 || start > end)
				throw new IndexOutOfRangeException($"{Name}: invalid array range {start}..{end}.");
			if (end > length)
				end = length;

			ModernVariableTerm output = null;
			if (arguments.Count >= 5)
				output = GetIntegerVariableArgument(arguments[4], Name);

			long count = 0;
			for (long i = start; i < end; i++)
			{
				if (!ExpressionEqualsArrayValue(term.Identifier, context, i, arguments[1]))
					continue;
				if (output != null && count < GetVariableArrayLength(output.Identifier, context))
					output.Identifier.SetValue(i, context, new[] { count });
				count++;
			}
			return count;
		}
	}

	sealed class GroupMatchMethod : ModernFunctionMethod
	{
		public GroupMatchMethod() : base("GROUPMATCH", 2, int.MaxValue) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long count = 0;
			for (int i = 1; i < arguments.Count; i++)
			{
				if (ExpressionEquals(arguments[0], arguments[i], context))
					count++;
			}
			return count;
		}
	}

	sealed class SameCheckMethod : ModernFunctionMethod
	{
		readonly bool allSame;

		public SameCheckMethod(string name, bool allSame) : base(name, 2, int.MaxValue)
		{
			this.allSame = allSame;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (allSame)
			{
				for (int i = 1; i < arguments.Count; i++)
				{
					if (!ExpressionEquals(arguments[0], arguments[i], context))
						return 0;
				}
				return 1;
			}

			for (int i = 0; i < arguments.Count; i++)
			{
				for (int j = i + 1; j < arguments.Count; j++)
				{
					if (ExpressionEquals(arguments[i], arguments[j], context))
						return 0;
				}
			}
			return 1;
		}
	}

	sealed class AbsMethod : ModernFunctionMethod
	{
		public AbsMethod() : base("ABS", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => Math.Abs(ToLong(arguments[0], context));
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => Math.Abs(ToDouble(arguments[0], context));
	}

	sealed class PowerMethod : ModernFunctionMethod
	{
		public PowerMethod() : base("POWER", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, Math.Pow(ToLong(arguments[0], context), ToLong(arguments[1], context)));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Math.Pow(ToDouble(arguments[0], context), ToDouble(arguments[1], context));
		}
	}

	sealed class SqrtMethod : ModernFunctionMethod
	{
		public SqrtMethod() : base("SQRT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, Math.Sqrt(ToLong(arguments[0], context)));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Math.Sqrt(ToDouble(arguments[0], context));
		}
	}

	sealed class CbrtMethod : ModernFunctionMethod
	{
		public CbrtMethod() : base("CBRT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, Math.Pow(ToLong(arguments[0], context), 1.0d / 3.0d));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Math.Pow(ToDouble(arguments[0], context), 1.0d / 3.0d);
		}
	}

	sealed class LogMethod : ModernFunctionMethod
	{
		readonly double logBase;
		public LogMethod(string name, double logBase) : base(name, 1, 1)
		{
			this.logBase = logBase;
		}
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, Calculate(ToLong(arguments[0], context)));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Calculate(ToDouble(arguments[0], context));
		}
		double Calculate(double value)
		{
			if (value <= 0)
				throw new ArgumentOutOfRangeException(nameof(value), $"{Name} argument must be greater than zero.");
			return logBase == Math.E ? Math.Log(value) : Math.Log10(value);
		}
	}

	sealed class ExpMethod : ModernFunctionMethod
	{
		public ExpMethod() : base("EXPONENT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, Math.Exp(ToLong(arguments[0], context)));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Math.Exp(ToDouble(arguments[0], context));
		}
	}

	sealed class SignMethod : ModernFunctionMethod
	{
		public SignMethod() : base("SIGN", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => Math.Sign(ToLong(arguments[0], context));
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => Math.Sign(ToDouble(arguments[0], context));
	}

	sealed class TrigMethod : ModernFunctionMethod
	{
		readonly Func<double, double> func;
		public TrigMethod(string name, Func<double, double> func) : base(name, 1, 1)
		{
			this.func = func;
		}
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, func(ToLong(arguments[0], context)));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return func(ToDouble(arguments[0], context));
		}
	}

	sealed class RoundLikeMethod : ModernFunctionMethod
	{
		readonly Func<double, double> func;
		public RoundLikeMethod(string name, Func<double, double> func) : base(name, 1, 1)
		{
			this.func = func;
		}
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, func(ToDouble(arguments[0], context)));
		}
	}

	enum UncheckedBinaryOperation
	{
		Add,
		Subtract,
		Multiply,
	}

	sealed class UncheckedBinaryMethod : ModernFunctionMethod
	{
		readonly UncheckedBinaryOperation operation;

		public UncheckedBinaryMethod(string name, UncheckedBinaryOperation operation) : base(name, 2, 2)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long left = arguments[0].GetIntValue(context);
			long right = arguments[1].GetIntValue(context);
			return operation switch
			{
				UncheckedBinaryOperation.Add => unchecked(left + right),
				UncheckedBinaryOperation.Subtract => unchecked(left - right),
				_ => unchecked(left * right),
			};
		}
	}

	sealed class UncheckedNegateMethod : ModernFunctionMethod
	{
		public UncheckedNegateMethod() : base("UNCHECKED_NEG", 1, 1) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return unchecked(-arguments[0].GetIntValue(context));
		}
	}

	sealed class ArgLengthMethod : ModernFunctionMethod
	{
		public ArgLengthMethod(string name) : base(name, 0, 0) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return context?.ExecutionContext?.CurrentVariadicArgCount ?? 0;
		}
	}

	sealed class RegexpMatchMethod : ModernFunctionMethod
	{
		public RegexpMatchMethod() : base("REGEXPMATCH", 2, 4) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count == 4)
			{
				if (arguments[2] is not ModernVariableTerm groupCountTerm || !groupCountTerm.Identifier.IsInteger)
					throw new FormatException("REGEXPMATCH third argument must be an integer variable when four arguments are supplied.");
				if (arguments[3] is not ModernVariableTerm outputTerm || !outputTerm.Identifier.IsString)
					throw new FormatException("REGEXPMATCH fourth argument must be a string array variable.");
			}
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string baseString = arguments[0].GetStrValue(context) ?? "";
			Regex regex;
			try
			{
				regex = new Regex(arguments[1].GetStrValue(context) ?? "");
			}
			catch (ArgumentException e)
			{
				throw new ArgumentException($"REGEXPMATCH received an invalid regex pattern: {e.Message}", e);
			}

			var matches = regex.Matches(baseString);
			int groupCount = regex.GetGroupNumbers().Length;
			if (arguments.Count == 3 && arguments[2].GetIntValue(context) != 0)
			{
				SetResultGroupCount(context, groupCount);
				if (matches.Count > 0)
					WriteStringResults(context, null, FlattenRegexCaptures(matches, regex));
			}
			else if (arguments.Count == 4)
			{
				((ModernVariableTerm)arguments[2]).SetValue(groupCount, context);
				if (matches.Count > 0)
					WriteStringResults(context, arguments[3], FlattenRegexCaptures(matches, regex));
			}

			return matches.Count;
		}
	}

	sealed class GetMemoryUsageMethod : ModernFunctionMethod
	{
		public GetMemoryUsageMethod() : base("GETMEMORYUSAGE", 0, 0) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Process.GetCurrentProcess().WorkingSet64;
		}
	}

	sealed class ClearMemoryMethod : ModernFunctionMethod
	{
		public ClearMemoryMethod() : base("CLEARMEMORY", 0, 0) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long before = Process.GetCurrentProcess().WorkingSet64;
			GC.Collect();
			long after = Process.GetCurrentProcess().WorkingSet64;
			return before - after;
		}
	}

	sealed class OutputLogMethod : ModernFunctionMethod
	{
		public OutputLogMethod() : base("OUTPUTLOG", 0, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string filename = arguments.Count > 0 ? arguments[0].GetStrValue(context) : "";
			bool hideInfo = arguments.Count > 1 && arguments[1].GetIntValue(context) == 1;
			var console = MinorShift.Emuera.GlobalStatic.Console;
			if (console == null)
				throw new InvalidOperationException("OUTPUTLOG requires an active console.");
			return console.OutputLog(filename, hideInfo) ? 1 : 0;
		}
	}

	sealed class GetDoingFunctionMethod : ModernFunctionMethod
	{
		public GetDoingFunctionMethod() : base("GETDOINGFUNCTION", 0, 0) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return context?.ExecutionContext?.CurrentFunctionName ?? "";
		}
	}

	sealed class LineIsEmptyMethod : ModernFunctionMethod
	{
		public LineIsEmptyMethod() : base("LINEISEMPTY", 0, 0) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return MinorShift.Emuera.GlobalStatic.Console?.EmptyLine == true ? 1 : 0;
		}
	}

	sealed class IsDefinedMethod : ModernFunctionMethod
	{
		public IsDefinedMethod() : base("ISDEFINED", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(context);
			return !string.IsNullOrEmpty(name) && MinorShift.Emuera.GlobalStatic.IdentifierDictionary?.GetMacro(name) != null ? 1 : 0;
		}
	}

	sealed class ExistVarMethod : ModernFunctionMethod
	{
		public ExistVarMethod() : base("EXISTVAR", 1, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(context);
			long mode = arguments.Count > 1 ? arguments[1].GetIntValue(context) : 0;
			var evaluator = context?.VariableEvaluator;
			if (evaluator == null)
				return 0;

			if (mode != 0)
			{
				try
				{
					new ModernExpressionParser(evaluator).Parse(name);
					return 1;
				}
				catch
				{
					return 0;
				}
			}

			if (!evaluator.TryGetToken(name, out var token))
				return 0;

			long result = token.GetEraType() switch
			{
				EraType.Integer => 1,
				EraType.String => 2,
				EraType.Float => 32,
				_ => 0,
			};
			if (token.IsConst)
				result |= 4;
			if (token.Dimension == VariableDimension.Array2D)
				result |= 8;
			if (token.Dimension == VariableDimension.Array3D)
				result |= 16;
			return result;
		}
	}

	sealed class VarSizeMethod : ModernFunctionMethod
	{
		public VarSizeMethod() : base("VARSIZE", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var evaluator = context?.VariableEvaluator ?? throw new InvalidOperationException("No variable evaluator is available.");
			string name = arguments[0].GetStrValue(context);
			if (!evaluator.TryGetToken(name, out var token))
				throw new KeyNotFoundException($"Unknown variable: {name}");

			int dim = arguments.Count == 2 ? (int)arguments[1].GetIntValue(context) : 0;
			if (dim < 0)
				throw new ArgumentOutOfRangeException(nameof(arguments), "VARSIZE dimension cannot be negative.");
			if (token.Dimension == VariableDimension.Scalar)
				return dim == 0 ? 1 : throw new IndexOutOfRangeException($"{name} has no dimension {dim}.");
			if (token.Dimension == VariableDimension.Array1D)
				return dim == 0 ? GetVariableArrayLength(token, context) : throw new IndexOutOfRangeException($"{name} has no dimension {dim}.");
			throw new NotSupportedException("VARSIZE currently supports scalar and one-dimensional variables in the modern mobile core.");
		}
	}

	sealed class GetVarMethod : ModernFunctionMethod
	{
		readonly EraType returnType;

		public GetVarMethod(string name, EraType returnType) : base(name, 1, 2)
		{
			this.returnType = returnType;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return returnType;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetVariable(context, arguments, out var term))
				return arguments[1].GetIntValue(context);
			if (!term.IsInteger)
				return DefaultOrThrow(arguments, context, $"GETVAR target {term.Identifier.Name} is not integer.").GetIntValue(context);
			return term.GetIntValue(context);
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetVariable(context, arguments, out var term))
				return arguments[1].GetStrValue(context) ?? "";
			if (!term.IsString)
				return DefaultOrThrow(arguments, context, $"GETVARS target {term.Identifier.Name} is not string.").GetStrValue(context);
			return term.GetStrValue(context) ?? "";
		}

		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetVariable(context, arguments, out var term))
				return ToDouble(arguments[1], context);
			if (!term.IsFloat)
				return ToDouble(DefaultOrThrow(arguments, context, $"GETVARF target {term.Identifier.Name} is not float."), context);
			return term.GetFloatValue(context);
		}
	}

	sealed class SetVarMethod : ModernFunctionMethod
	{
		public SetVarMethod() : base("SETVAR", 2, 3) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			bool hasDefault = arguments.Count > 2;
			long defaultValue = hasDefault ? arguments[2].GetIntValue(context) : 0;
			try
			{
				string name = arguments[0].GetStrValue(context);
				var term = ParseVariable(name, context);
				if (term.Identifier.IsConst)
					return hasDefault ? defaultValue : throw new InvalidOperationException($"{name} is read-only.");
				term.SetValue(arguments[1], context);
				return 1;
			}
			catch
			{
				if (hasDefault)
					return defaultValue;
				throw;
			}
		}
	}

	sealed class VarSetExMethod : ModernFunctionMethod
	{
		public VarSetExMethod() : base("VARSETEX", 2, 5) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(context);
			var term = ParseVariable(name, context);
			if (term.Identifier.IsConst)
				throw new InvalidOperationException($"{name} is read-only.");

			if (term.Identifier.Dimension == VariableDimension.Scalar)
			{
				SetVariableValue(term, arguments[1], context);
				return 1;
			}

			if (term.Identifier.Dimension != VariableDimension.Array1D)
				throw new NotSupportedException("VARSETEX currently supports scalar and one-dimensional variables in the modern mobile core.");

			long start = arguments.Count >= 4 ? arguments[3].GetIntValue(context) : 0;
			long end = arguments.Count == 5 ? arguments[4].GetIntValue(context) : GetVariableArrayLength(term.Identifier, context);
			var range = ValidateArrayRange(term.Identifier, context, start, end);
			for (long i = range.Start; i < range.End; i++)
				SetVariableValue(term.Identifier, context, i, arguments[1]);
			return 1;
		}
	}

	sealed class GetBitMethod : ModernFunctionMethod
	{
		public GetBitMethod() : base("GETBIT", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long value = arguments[0].GetIntValue(context);
			long bit = arguments[1].GetIntValue(context);
			if (bit < 0 || bit > 63)
				throw new ArgumentOutOfRangeException(nameof(arguments), "GETBIT bit index must be between 0 and 63.");
			return (value & (1L << (int)bit)) != 0 ? 1 : 0;
		}
	}

	sealed class GetNumMethod : ModernFunctionMethod
	{
		public GetNumMethod() : base("GETNUM", 2, 3) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (arguments[0] is not ModernVariableTerm term)
				throw new InvalidOperationException("GETNUM first argument must be a variable.");
			if (!TryMapLegacyVariableCode(term.Identifier.Code, out var legacyCode))
				return -1;
			string key = arguments[1].GetStrValue(context);
			int index = arguments.Count > 2 ? (int)arguments[2].GetIntValue(context) : -1;
			return TryKeywordToInteger(legacyCode, key, index, out int value) ? value : -1;
		}
	}

	sealed class GetNumByNameMethod : ModernFunctionMethod
	{
		public GetNumByNameMethod() : base("GETNUMB", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string variableName = arguments[0].GetStrValue(context);
			if (!TryMapLegacyVariableCode(variableName, out var legacyCode))
				return -1;
			string key = arguments[1].GetStrValue(context);
			return TryKeywordToInteger(legacyCode, key, -1, out int value) ? value : -1;
		}
	}

	sealed class GetLevelMethod : ModernFunctionMethod
	{
		readonly string variableName;

		public GetLevelMethod(string name, string variableName) : base(name, 2, 2)
		{
			this.variableName = variableName;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (context?.VariableEvaluator == null)
				throw new InvalidOperationException($"{Name} requires a variable evaluator.");
			long value = arguments[0].GetIntValue(context);
			long maxLevel = arguments[1].GetIntValue(context);
			var token = context.VariableEvaluator.GetToken(variableName);
			for (long i = 0; i < maxLevel; i++)
			{
				if (value < token.GetIntValue(context, new[] { i + 1 }))
					return i;
			}
			return maxLevel;
		}
	}

	sealed class BitSetMethod : ModernFunctionMethod
	{
		public BitSetMethod() : base("BITSET", 2, 4) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var target = GetIntegerVariableArgument(arguments[0], "BITSET");
			long index = arguments[1].GetIntValue(context);
			long value = arguments.Count > 2 ? arguments[2].GetIntValue(context) : 1;
			long length = arguments.Count > 3 ? arguments[3].GetIntValue(context) : 1;
			BitSet(target, context, index, value, length);
			return 1;
		}
	}

	sealed class BitGetMethod : ModernFunctionMethod
	{
		public BitGetMethod() : base("BITGET", 2, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var target = GetIntegerVariableArgument(arguments[0], "BITGET");
			return BitGet(target, context, arguments[1].GetIntValue(context));
		}
	}

	sealed class BitIndexOfFirstMethod : ModernFunctionMethod
	{
		public BitIndexOfFirstMethod() : base("BITINDEXOFFIRST", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var target = GetIntegerVariableArgument(arguments[0], Name);
			bool searchForSet = arguments.Count <= 1 || arguments[1].GetIntValue(context) != 0;
			long slots = GetIntegerSlotLength(target.Identifier, context);
			for (long slot = 0; slot < slots; slot++)
			{
				long value = target.Identifier.GetIntValue(context, new[] { slot });
				if (searchForSet && value == 0)
					continue;
				if (!searchForSet && value == -1)
					continue;
				for (int bit = 0; bit < 64; bit++)
				{
					bool set = (value & (1L << bit)) != 0;
					if (set == searchForSet)
						return slot * 64 + bit;
				}
			}
			return -1;
		}
	}

	sealed class BitToggleMethod : ModernFunctionMethod
	{
		public BitToggleMethod() : base("BITTOGGLE", 2, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var target = GetIntegerVariableArgument(arguments[0], "BITTOGGLE");
			long index = arguments[1].GetIntValue(context);
			long slot = index / 64;
			int bit = (int)(index % 64);
			if (index < 0 || slot >= GetIntegerSlotLength(target.Identifier, context))
				return 0;
			long value = target.Identifier.GetIntValue(context, new[] { slot });
			target.Identifier.SetValue(value ^ (1L << bit), context, new[] { slot });
			return 1;
		}
	}

	enum XmlDocumentOperation
	{
		Create,
		Check,
		Release,
	}

	sealed class XmlDocumentMethod : ModernFunctionMethod
	{
		readonly XmlDocumentOperation operation;

		public XmlDocumentMethod(string name, XmlDocumentOperation operation)
			: base(name, operation == XmlDocumentOperation.Create ? 2 : 1, operation == XmlDocumentOperation.Create ? 2 : 1)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string key = GetXmlDocumentKey(arguments[0], context);
			var documents = GetXmlDocuments(context);
			if (operation == XmlDocumentOperation.Create)
			{
				if (documents.ContainsKey(key))
					return 0;
				documents.Add(key, ParseXml(arguments[1].GetStrValue(context), Name));
				return 1;
			}
			if (!documents.ContainsKey(key))
				return 0;
			if (operation == XmlDocumentOperation.Release)
				documents.Remove(key);
			return 1;
		}
	}

	sealed class XmlToStrMethod : ModernFunctionMethod
	{
		public XmlToStrMethod() : base("XML_TOSTR", 1, 1) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return TryGetXmlDocument(context, GetXmlDocumentKey(arguments[0], context), out var document)
				? document.OuterXml
				: "";
		}
	}

	sealed class XmlGetMethod : ModernFunctionMethod
	{
		readonly bool byName;

		public XmlGetMethod(string name, bool byName) : base(name, 2, 4)
		{
			this.byName = byName;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count >= 3 && !arguments[2].IsInteger && (arguments[2] is not ModernVariableTerm term || !term.Identifier.IsString))
				throw new FormatException($"{Name} third argument must be a flag or string array variable.");
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryLoadXmlSource(context, arguments[0], byName, out var document, out _))
				return -1;
			var nodes = SelectXmlNodes(document, arguments[1].GetStrValue(context), Name);
			long outputStyle = arguments.Count == 4 ? arguments[3].GetIntValue(context) : 0;
			if (arguments.Count >= 3)
			{
				var values = new List<string>(nodes.Count);
				for (int i = 0; i < nodes.Count; i++)
					values.Add(ReadXmlNode(nodes[i], outputStyle));
				if (arguments[2].IsInteger)
				{
					if (arguments[2].GetIntValue(context) != 0)
						WriteStringResults(context, null, values);
				}
				else
					WriteStringResults(context, arguments[2], values);
			}
			return nodes.Count;
		}
	}

	sealed class XmlSetMethod : ModernFunctionMethod
	{
		readonly bool byName;

		public XmlSetMethod(string name, bool byName) : base(name, 3, 5)
		{
			this.byName = byName;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryLoadXmlSource(context, arguments[0], byName, out var document, out bool saveToSource))
				return -1;
			var nodes = SelectXmlNodes(document, arguments[1].GetStrValue(context), Name);
			bool setAllNodes = arguments.Count >= 4 && arguments[3].GetIntValue(context) != 0;
			long style = arguments.Count == 5 ? arguments[4].GetIntValue(context) : 0;
			string value = arguments[2].GetStrValue(context) ?? "";
			if (nodes.Count == 1)
				SetXmlNode(nodes[0], value, style);
			else if (nodes.Count > 1 && setAllNodes)
			{
				for (int i = 0; i < nodes.Count; i++)
					SetXmlNode(nodes[i], value, style);
			}
			SaveXmlSourceIfNeeded(arguments[0], context, document, saveToSource);
			return nodes.Count;
		}
	}

	enum XmlAddOperation
	{
		Node,
		Attribute,
	}

	sealed class XmlAddNodeMethod : ModernFunctionMethod
	{
		readonly XmlAddOperation operation;
		readonly bool byName;

		public XmlAddNodeMethod(string name, XmlAddOperation operation, bool byName)
			: base(name, operation == XmlAddOperation.Node ? 3 : 3, operation == XmlAddOperation.Node ? 5 : 6)
		{
			this.operation = operation;
			this.byName = byName;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryLoadXmlSource(context, arguments[0], byName, out var document, out bool saveToSource))
				return -1;
			int methodPosition = operation == XmlAddOperation.Node ? 4 : 5;
			int setAllPosition = operation == XmlAddOperation.Node ? 5 : 6;
			int method = arguments.Count >= methodPosition ? NormalizeXmlInsertMethod(arguments[methodPosition - 1].GetIntValue(context)) : 0;
			bool setAllNodes = arguments.Count == setAllPosition && arguments[setAllPosition - 1].GetIntValue(context) != 0;
			var nodes = SelectXmlNodes(document, arguments[1].GetStrValue(context), Name);
			if (nodes.Count == 0)
				return 0;

			Func<XmlNode> createChild;
			if (operation == XmlAddOperation.Node)
			{
				var sourceDocument = ParseXml(arguments[2].GetStrValue(context), Name);
				var sourceNode = sourceDocument.DocumentElement;
				createChild = () => document.ImportNode(sourceNode, true);
			}
			else
			{
				string attributeName = arguments[2].GetStrValue(context) ?? "";
				string attributeValue = arguments.Count >= 4 ? arguments[3].GetStrValue(context) ?? "" : "";
				createChild = () =>
				{
					var attribute = document.CreateAttribute(attributeName);
					attribute.Value = attributeValue;
					return attribute;
				};
			}

			if (nodes.Count == 1)
			{
				if (!InsertXmlNode(nodes[0], createChild(), method, operation) && method > 0)
					return 0;
			}
			else if (setAllNodes)
			{
				for (int i = 0; i < nodes.Count; i++)
					InsertXmlNode(nodes[i], createChild(), method, operation);
			}
			SaveXmlSourceIfNeeded(arguments[0], context, document, saveToSource);
			return nodes.Count;
		}
	}

	enum XmlRemoveOperation
	{
		Node,
		Attribute,
	}

	sealed class XmlRemoveNodeMethod : ModernFunctionMethod
	{
		readonly XmlRemoveOperation operation;
		readonly bool byName;

		public XmlRemoveNodeMethod(string name, XmlRemoveOperation operation, bool byName) : base(name, 2, 3)
		{
			this.operation = operation;
			this.byName = byName;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryLoadXmlSource(context, arguments[0], byName, out var document, out bool saveToSource))
				return -1;
			var nodes = SelectXmlNodes(document, arguments[1].GetStrValue(context), Name);
			bool setAllNodes = arguments.Count == 3 && arguments[2].GetIntValue(context) != 0;
			if (nodes.Count == 1)
			{
				if (!RemoveXmlNode(nodes[0], operation))
					return 0;
			}
			else if (nodes.Count > 1 && setAllNodes)
			{
				for (int i = 0; i < nodes.Count; i++)
					RemoveXmlNode(nodes[i], operation);
			}
			SaveXmlSourceIfNeeded(arguments[0], context, document, saveToSource);
			return nodes.Count;
		}
	}

	sealed class XmlReplaceMethod : ModernFunctionMethod
	{
		readonly bool byName;

		public XmlReplaceMethod(string name, bool byName) : base(name, 2, 4)
		{
			this.byName = byName;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var newDocument = ParseXml(arguments.Count > 2 ? arguments[2].GetStrValue(context) : arguments[1].GetStrValue(context), Name);
			if (arguments.Count == 2)
			{
				if (!TryGetStoredXmlDocument(context, arguments[0], true, out string key, out _))
					return -1;
				GetXmlDocuments(context)[key] = newDocument;
				return 1;
			}

			if (!TryLoadXmlSource(context, arguments[0], byName, out var document, out bool saveToSource))
				return -1;
			var nodes = SelectXmlNodes(document, arguments[1].GetStrValue(context), Name);
			bool setAllNodes = arguments.Count >= 4 && arguments[3].GetIntValue(context) != 0;
			if (nodes.Count == 1)
			{
				if (!ReplaceXmlNode(nodes[0], document.ImportNode(newDocument.DocumentElement, true)))
					return 0;
			}
			else if (nodes.Count > 1 && setAllNodes)
			{
				for (int i = 0; i < nodes.Count; i++)
					ReplaceXmlNode(nodes[i], document.ImportNode(newDocument.DocumentElement, true));
			}
			SaveXmlSourceIfNeeded(arguments[0], context, document, saveToSource);
			return nodes.Count;
		}
	}

	enum DataTableManagementOperation
	{
		Create,
		Check,
		Release,
		Clear,
		NoCase,
	}

	sealed class DataTableManagementMethod : ModernFunctionMethod
	{
		readonly DataTableManagementOperation operation;

		public DataTableManagementMethod(string name, DataTableManagementOperation operation)
			: base(name, operation == DataTableManagementOperation.NoCase ? 2 : 1, operation == DataTableManagementOperation.NoCase ? 2 : 1)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string key = arguments[0].GetStrValue(context) ?? "";
			var tables = GetDataTables(context);
			bool contains = tables.ContainsKey(key);
			switch (operation)
			{
				case DataTableManagementOperation.Clear:
					if (!contains)
						return -1;
					tables[key].Clear();
					return 1;
				case DataTableManagementOperation.NoCase:
					if (!contains)
						return -1;
					tables[key].CaseSensitive = arguments[1].GetIntValue(context) == 0;
					return 1;
				case DataTableManagementOperation.Check:
					return contains ? 1 : 0;
				case DataTableManagementOperation.Release:
					if (contains)
						tables.Remove(key);
					return 1;
				default:
					if (contains)
						return 0;
					tables[key] = CreateDataTable(key);
					return 1;
			}
		}
	}

	enum DataTableColumnOperation
	{
		Create,
		Check,
		Remove,
		Names,
	}

	sealed class DataTableColumnManagementMethod : ModernFunctionMethod
	{
		readonly DataTableColumnOperation operation;

		public DataTableColumnManagementMethod(string name, DataTableColumnOperation operation)
			: base(name, operation == DataTableColumnOperation.Names ? 1 : 2, operation == DataTableColumnOperation.Create ? 4 : operation == DataTableColumnOperation.Names ? 2 : 2)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (operation == DataTableColumnOperation.Names && arguments.Count == 2 && (arguments[1] is not ModernVariableTerm term || !term.Identifier.IsString))
				throw new FormatException("DT_COLUMN_NAMES second argument must be a string array variable.");
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetDataTable(context, arguments[0].GetStrValue(context), out var table))
				return -1;
			if (operation == DataTableColumnOperation.Names)
			{
				var names = table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToArray();
				WriteStringResults(context, arguments.Count > 1 ? arguments[1] : null, names);
				return names.Length;
			}

			string columnName = arguments[1].GetStrValue(context) ?? "";
			bool contains = table.Columns.Contains(columnName);
			if (operation == DataTableColumnOperation.Check)
				return contains ? DataTableTypeToInt(table.Columns[columnName].DataType) : 0;
			if (operation == DataTableColumnOperation.Remove)
			{
				if (!contains || string.Equals(columnName, "id", StringComparison.OrdinalIgnoreCase))
					return 0;
				table.Columns.Remove(columnName);
				return 1;
			}

			if (contains)
				return 0;
			Type type = typeof(string);
			if (arguments.Count >= 3)
				type = arguments[2].IsString ? DataTableNameToType(arguments[2].GetStrValue(context)) : DataTableIntToType(arguments[2].GetIntValue(context));
			if (type == null)
				throw new FormatException($"{Name} received an unsupported DataTable column type.");
			var column = table.Columns.Add(columnName, type);
			column.AllowDBNull = arguments.Count != 4 || arguments[3].GetIntValue(context) != 0;
			return 1;
		}
	}

	enum DataTableLengthOperation
	{
		Row,
		Column,
	}

	sealed class DataTableLengthMethod : ModernFunctionMethod
	{
		readonly DataTableLengthOperation operation;

		public DataTableLengthMethod(string name, DataTableLengthOperation operation) : base(name, 1, 1)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetDataTable(context, arguments[0].GetStrValue(context), out var table))
				return -1;
			return operation == DataTableLengthOperation.Row ? table.Rows.Count : table.Columns.Count;
		}
	}

	enum DataTableRowSetOperation
	{
		Add,
		Set,
	}

	sealed class DataTableRowSetMethod : ModernFunctionMethod
	{
		readonly DataTableRowSetOperation operation;

		public DataTableRowSetMethod(string name, DataTableRowSetOperation operation)
			: base(name, operation == DataTableRowSetOperation.Add ? 1 : 2, int.MaxValue)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetDataTable(context, arguments[0].GetStrValue(context), out var table))
				return -1;
			int offset = operation == DataTableRowSetOperation.Add ? 1 : 2;
			DataRow row;
			if (operation == DataTableRowSetOperation.Set)
			{
				row = table.Rows.Find(arguments[1].GetIntValue(context));
				if (row == null)
					return -2;
			}
			else
			{
				row = table.NewRow();
				row["id"] = NextDataTableRowId(context);
			}

			long changed = SetDataTableRowValues(row, table, context, arguments, offset);
			if (operation == DataTableRowSetOperation.Add)
			{
				table.Rows.Add(row);
				return Convert.ToInt64(row["id"], CultureInfo.InvariantCulture);
			}
			return changed;
		}
	}

	sealed class DataTableRowRemoveMethod : ModernFunctionMethod
	{
		public DataTableRowRemoveMethod() : base("DT_ROW_REMOVE", 2, 3) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count == 3 && (arguments[1] is not ModernVariableTerm term || !term.Identifier.IsInteger))
				throw new FormatException("DT_ROW_REMOVE second argument must be an integer array variable when three arguments are supplied.");
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetDataTable(context, arguments[0].GetStrValue(context), out var table))
				return -1;
			var rows = new List<DataRow>();
			if (arguments.Count == 2)
			{
				var row = table.Rows.Find(arguments[1].GetIntValue(context));
				if (row != null)
					rows.Add(row);
			}
			else
			{
				var ids = ReadIntegerArray(arguments[1], context, arguments[2].GetIntValue(context));
				foreach (long id in ids)
				{
					var row = table.Rows.Find(id);
					if (row != null)
						rows.Add(row);
				}
			}
			foreach (var row in rows)
				table.Rows.Remove(row);
			return rows.Count;
		}
	}

	enum DataTableCellGetOperation
	{
		GetInteger,
		GetString,
		GetFloat,
		IsNull,
	}

	sealed class DataTableCellGetMethod : ModernFunctionMethod
	{
		readonly DataTableCellGetOperation operation;

		public DataTableCellGetMethod(string name, DataTableCellGetOperation operation) : base(name, 3, 4)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return operation switch
			{
				DataTableCellGetOperation.GetString => EraType.String,
				DataTableCellGetOperation.GetFloat => EraType.Float,
				_ => EraType.Integer,
			};
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetDataTableCell(context, arguments, out _, out _, out var value))
				return operation == DataTableCellGetOperation.IsNull ? -2 : 0;
			if (operation == DataTableCellGetOperation.IsNull)
				return value == DBNull.Value ? 1 : 0;
			return value == DBNull.Value ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetDataTableCell(context, arguments, out _, out _, out var value) || value == DBNull.Value)
				return "";
			return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
		}

		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetDataTableCell(context, arguments, out _, out _, out var value) || value == DBNull.Value)
				return 0.0d;
			return Convert.ToDouble(value, CultureInfo.InvariantCulture);
		}
	}

	enum DataTableCellSetOperation
	{
		SetExpression,
		SetFloat,
	}

	sealed class DataTableCellSetMethod : ModernFunctionMethod
	{
		readonly DataTableCellSetOperation operation;

		public DataTableCellSetMethod(string name, DataTableCellSetOperation operation) : base(name, 3, 5)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetDataTable(context, arguments[0].GetStrValue(context), out var table))
				return -1;
			bool asId = arguments.Count == 5 && arguments[4].GetIntValue(context) != 0;
			long index = arguments[1].GetIntValue(context);
			string columnName = arguments[2].GetStrValue(context) ?? "";
			if (string.Equals(columnName, "id", StringComparison.OrdinalIgnoreCase))
				return 0;
			var row = GetDataTableRow(table, index, asId);
			if (row == null || !table.Columns.Contains(columnName))
				return -3;
			if (arguments.Count < 4)
			{
				row[columnName] = DBNull.Value;
				return 1;
			}
			try
			{
				if (operation == DataTableCellSetOperation.SetFloat)
					row[columnName] = arguments[3].GetFloatValue(context);
				else
					SetDataTableValue(row, table.Columns[columnName], arguments[3], context);
				return 1;
			}
			catch
			{
				return -2;
			}
		}
	}

	sealed class DataTableSelectMethod : ModernFunctionMethod
	{
		public DataTableSelectMethod() : base("DT_SELECT", 1, 4) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count == 4 && (arguments[3] is not ModernVariableTerm term || !term.Identifier.IsInteger))
				throw new FormatException("DT_SELECT fourth argument must be an integer array variable.");
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetDataTable(context, arguments[0].GetStrValue(context), out var table))
				return -1;
			string filter = arguments.Count > 1 ? arguments[1].GetStrValue(context) : null;
			string sort = arguments.Count > 2 ? arguments[2].GetStrValue(context) : null;
			DataRow[] rows = sort != null ? table.Select(filter, sort) : filter != null ? table.Select(filter) : table.Select();
			var ids = rows.Select(row => Convert.ToInt64(row["id"], CultureInfo.InvariantCulture)).ToArray();
			if (arguments.Count == 4)
			{
				WriteIntegerResults(context, arguments[3], ids);
			}
			else
			{
				SetIntegerResult(context, 0, ids.Length);
				for (int i = 0; i < ids.Length; i++)
					SetIntegerResult(context, i + 1, ids[i]);
			}
			return ids.Length;
		}
	}

	sealed class DataTableToXmlMethod : ModernFunctionMethod
	{
		public DataTableToXmlMethod() : base("DT_TOXML", 1, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count == 2 && (arguments[1] is not ModernVariableTerm term || !term.Identifier.IsString))
				throw new FormatException("DT_TOXML second argument must be a string array variable.");
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetDataTable(context, arguments[0].GetStrValue(context), out var table))
				return "";
			var schemaBuilder = new StringBuilder();
			using (var writer = new StringWriter(schemaBuilder, CultureInfo.InvariantCulture))
				table.WriteXmlSchema(writer);
			if (arguments.Count == 2)
				WriteStringResults(context, arguments[1], new[] { schemaBuilder.ToString() });
			else
				WriteStringResults(context, null, new[] { "", schemaBuilder.ToString() });

			var dataBuilder = new StringBuilder();
			using (var writer = new StringWriter(dataBuilder, CultureInfo.InvariantCulture))
				table.WriteXml(writer);
			return dataBuilder.ToString();
		}
	}

	sealed class DataTableFromXmlMethod : ModernFunctionMethod
	{
		public DataTableFromXmlMethod() : base("DT_FROMXML", 3, 3) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string key = arguments[0].GetStrValue(context) ?? "";
			try
			{
				var table = new DataTable(key);
				using (var reader = new StringReader(arguments[1].GetStrValue(context) ?? ""))
					table.ReadXmlSchema(reader);
				using (var reader = new StringReader(arguments[2].GetStrValue(context) ?? ""))
					table.ReadXml(reader);
				GetDataTables(context)[key] = table;
				if (table.PrimaryKey == null || table.PrimaryKey.Length == 0)
				{
					if (table.Columns.Contains("id"))
						table.PrimaryKey = new[] { table.Columns["id"] };
				}
				return 1;
			}
			catch
			{
				return 0;
			}
		}
	}

	enum MapManagementOperation
	{
		Create,
		Check,
		Release,
	}

	sealed class MapManagementMethod : ModernFunctionMethod
	{
		readonly MapManagementOperation operation;

		public MapManagementMethod(string name, MapManagementOperation operation) : base(name, 1, 1)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var maps = GetMaps(context);
			string mapName = arguments[0].GetStrValue(context) ?? "";
			bool contains = maps.ContainsKey(mapName);
			switch (operation)
			{
				case MapManagementOperation.Check:
					return contains ? 1 : 0;
				case MapManagementOperation.Release:
					if (contains)
						maps.Remove(mapName);
					return 1;
				default:
					if (contains)
						return 0;
					maps[mapName] = new Dictionary<string, string>();
					return 1;
			}
		}
	}

	enum MapDataOperation
	{
		Set,
		Has,
		Remove,
		Clear,
		Size,
	}

	sealed class MapDataOperationMethod : ModernFunctionMethod
	{
		readonly MapDataOperation operation;

		public MapDataOperationMethod(string name, MapDataOperation operation)
			: base(name, operation == MapDataOperation.Set ? 3 : operation == MapDataOperation.Clear || operation == MapDataOperation.Size ? 1 : 2, operation == MapDataOperation.Set ? 3 : operation == MapDataOperation.Clear || operation == MapDataOperation.Size ? 1 : 2)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return -1;
			if (operation == MapDataOperation.Clear)
			{
				map.Clear();
				return 1;
			}
			if (operation == MapDataOperation.Size)
				return map.Count;

			string key = arguments[1].GetStrValue(context) ?? "";
			bool contains = map.ContainsKey(key);
			if (operation == MapDataOperation.Has)
				return contains ? 1 : 0;
			if (operation == MapDataOperation.Remove)
			{
				map.Remove(key);
				return 1;
			}

			map[key] = arguments[2].GetStrValue(context) ?? "";
			return 1;
		}
	}

	sealed class MapGetMethod : ModernFunctionMethod
	{
		public MapGetMethod() : base("MAP_GET", 2, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return "";
			string key = arguments[1].GetStrValue(context) ?? "";
			return map.TryGetValue(key, out var value) ? value ?? "" : "";
		}
	}

	enum MapStringListOperation
	{
		Keys,
		Values,
	}

	sealed class MapGetStringListMethod : ModernFunctionMethod
	{
		readonly MapStringListOperation operation;

		public MapGetStringListMethod(string name, MapStringListOperation operation) : base(name, 1, 3)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count == 3 && (arguments[1] is not ModernVariableTerm term || !term.Identifier.IsString))
				throw new FormatException($"{Name} second argument must be a string array variable when three arguments are supplied.");
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return "";
			var values = operation == MapStringListOperation.Keys ? map.Keys.ToArray() : map.Values.ToArray();
			if (arguments.Count == 1)
				return string.Join(",", values);
			if (arguments.Count == 2)
			{
				if (arguments[1].GetIntValue(context) == 0)
					return "";
				WriteStringResults(context, null, values);
				SetIntegerResult(context, 0, values.Length);
				return values.Length > 0 ? values[0] : "";
			}
			if (arguments[2].GetIntValue(context) == 0)
				return "";
			WriteStringResults(context, arguments[1], values);
			SetIntegerResult(context, 0, values.Length);
			return "";
		}
	}

	sealed class MapToXmlMethod : ModernFunctionMethod
	{
		public MapToXmlMethod() : base("MAP_TOXML", 1, 1) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return "";
			var builder = new StringBuilder();
			builder.Append("<map>");
			foreach (var pair in map)
				builder.Append("<p><k>").Append(pair.Key).Append("</k><v>").Append(pair.Value).Append("</v></p>");
			builder.Append("</map>");
			return builder.ToString();
		}
	}

	sealed class MapFromXmlMethod : ModernFunctionMethod
	{
		public MapFromXmlMethod() : base("MAP_FROMXML", 2, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return 0;
			var document = new XmlDocument();
			string xml = arguments[1].GetStrValue(context) ?? "";
			try
			{
				document.LoadXml(xml);
			}
			catch (XmlException e)
			{
				throw new FormatException($"MAP_FROMXML received invalid XML: {e.Message}", e);
			}

			var nodes = document.SelectNodes("/map/p");
			if (nodes == null)
				return 1;
			foreach (XmlNode node in nodes)
			{
				var key = node.SelectSingleNode("./k");
				var value = node.SelectSingleNode("./v");
				if (key == null || value == null)
					continue;
				map[key.InnerText] = value.InnerXml;
			}
			return 1;
		}
	}

	sealed class MapMergeMethod : ModernFunctionMethod
	{
		public MapMergeMethod() : base("MAP_MERGE", 2, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var destination))
				return 0;
			if (!TryGetMap(context, arguments[1].GetStrValue(context), out var source))
				return 0;
			foreach (var pair in source)
				destination[pair.Key] = pair.Value;
			return 1;
		}
	}

	sealed class MapRemoveIfMethod : ModernFunctionMethod
	{
		public MapRemoveIfMethod() : base("MAP_REMOVEIF", 3, 3) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return 0;
			string matchValue = arguments[1].GetStrValue(context) ?? "";
			string mode = arguments[2].GetStrValue(context) ?? "";
			var toRemove = map.Where(pair => MapPredicate(pair, matchValue, mode)).Select(pair => pair.Key).ToArray();
			if (toRemove.Length == 0 && !IsKnownMapPredicateMode(mode))
				return -1;
			for (int i = 0; i < toRemove.Length; i++)
				map.Remove(toRemove[i]);
			return toRemove.Length;
		}
	}

	sealed class MapFindKeyMethod : ModernFunctionMethod
	{
		public MapFindKeyMethod() : base("MAP_FINDKEY", 3, 3) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return "";
			string matchValue = arguments[1].GetStrValue(context) ?? "";
			string mode = arguments[2].GetStrValue(context) ?? "";
			if (!IsKnownMapPredicateMode(mode))
			{
				SetIntegerResult(context, 0, 0);
				return "";
			}
			var keys = map.Where(pair => MapPredicate(pair, matchValue, mode)).Select(pair => pair.Key).ToArray();
			SetIntegerResult(context, 0, keys.Length);
			return string.Join(",", keys);
		}
	}

	sealed class MapToStringMethod : ModernFunctionMethod
	{
		public MapToStringMethod() : base("MAP_TOSTRING", 1, 3) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return "";
			string entrySeparator = arguments.Count > 1 ? arguments[1].GetStrValue(context) ?? "" : ",";
			string keyValueSeparator = arguments.Count > 2 ? arguments[2].GetStrValue(context) ?? "" : "=";
			return string.Join(entrySeparator, map.Select(pair => pair.Key + keyValueSeparator + pair.Value));
		}
	}

	sealed class MapFromStringMethod : ModernFunctionMethod
	{
		public MapFromStringMethod() : base("MAP_FROMSTRING", 2, 4) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return 0;
			string data = arguments[1].GetStrValue(context) ?? "";
			if (data.Length == 0)
				return 0;
			string entrySeparator = arguments.Count > 2 ? arguments[2].GetStrValue(context) ?? "" : ",";
			string keyValueSeparator = arguments.Count > 3 ? arguments[3].GetStrValue(context) ?? "" : "=";
			if (entrySeparator.Length == 0 || keyValueSeparator.Length == 0)
				return 0;

			int count = 0;
			var entries = data.Split(new[] { entrySeparator }, StringSplitOptions.None);
			foreach (string entry in entries)
			{
				if (entry.Length == 0)
					continue;
				int index = entry.IndexOf(keyValueSeparator, StringComparison.Ordinal);
				if (index < 0)
					continue;
				map[entry.Substring(0, index)] = entry.Substring(index + keyValueSeparator.Length);
				count++;
			}
			return count;
		}
	}

	enum SqlIntOperation
	{
		ConnectionOpen,
		Connect,
		Disconnect,
		ExecuteNonQuery,
		ExecuteReader,
		ReaderRead,
		ReaderGetLong,
		ReaderIsNull,
		ReaderClose,
		ExecuteScalarLong,
		ImportMapXml,
		ImportDtXml,
		ExportMapXml,
		ExportDtXml,
		ImportXmlCustom,
		ExecuteNonQueryWithParameters,
		ExecuteReaderWithParameters,
		ExecuteScalarLongWithParameters,
	}

	sealed class SqlIntMethod : ModernFunctionMethod
	{
		readonly SqlIntOperation operation;

		public SqlIntMethod(string name, SqlIntOperation operation, int minArgumentCount, int maxArgumentCount)
			: base(name, minArgumentCount, maxArgumentCount)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return operation switch
			{
				SqlIntOperation.ConnectionOpen => ModernSqlManager.ConnectionOpen(arguments[0].GetStrValue(context)),
				SqlIntOperation.Connect => ModernSqlManager.Connect(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context), false),
				SqlIntOperation.Disconnect => ModernSqlManager.Disconnect(arguments[0].GetStrValue(context)),
				SqlIntOperation.ExecuteNonQuery => ModernSqlManager.ExecuteNonQuery(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context)),
				SqlIntOperation.ExecuteReader => ModernSqlManager.ExecuteReader(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context)),
				SqlIntOperation.ReaderRead => ModernSqlManager.ReaderRead(arguments[0].GetIntValue(context)),
				SqlIntOperation.ReaderGetLong => ModernSqlManager.ReaderGetLong(arguments[0].GetIntValue(context), (int)arguments[1].GetIntValue(context)),
				SqlIntOperation.ReaderIsNull => ModernSqlManager.ReaderIsNull(arguments[0].GetIntValue(context), (int)arguments[1].GetIntValue(context)),
				SqlIntOperation.ReaderClose => ModernSqlManager.ReaderClose(arguments[0].GetIntValue(context)),
				SqlIntOperation.ExecuteScalarLong => ModernSqlManager.ExecuteScalarLong(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context)),
				SqlIntOperation.ImportMapXml => ModernSqlManager.ImportMapXml(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context), arguments[2].GetStrValue(context)),
				SqlIntOperation.ImportDtXml => ModernSqlManager.ImportDtXml(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context), arguments[2].GetStrValue(context), arguments[3].GetStrValue(context)),
				SqlIntOperation.ExportMapXml => ModernSqlManager.ExportMapXml(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context), arguments[2].GetStrValue(context)),
				SqlIntOperation.ExportDtXml => ModernSqlManager.ExportDtXml(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context), arguments[2].GetStrValue(context), arguments[3].GetStrValue(context)),
				SqlIntOperation.ImportXmlCustom => ModernSqlManager.ImportXmlCustom(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context), arguments[2].GetStrValue(context), arguments[3].GetStrValue(context), arguments[4].GetStrValue(context)),
				SqlIntOperation.ExecuteNonQueryWithParameters => ModernSqlManager.ExecuteNonQuery(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context), ReadSqlParameters(arguments, context, 2)),
				SqlIntOperation.ExecuteReaderWithParameters => ModernSqlManager.ExecuteReader(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context), ReadSqlParameters(arguments, context, 2)),
				_ => ModernSqlManager.ExecuteScalarLong(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context), ReadSqlParameters(arguments, context, 2)),
			};
		}
	}

	enum SqlFloatOperation
	{
		ReaderGetFloat,
		ExecuteScalarFloat,
		ExecuteScalarFloatWithParameters,
	}

	sealed class SqlFloatMethod : ModernFunctionMethod
	{
		readonly SqlFloatOperation operation;

		public SqlFloatMethod(string name, SqlFloatOperation operation, int minArgumentCount, int maxArgumentCount)
			: base(name, minArgumentCount, maxArgumentCount)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Float;
		}

		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return operation switch
			{
				SqlFloatOperation.ReaderGetFloat => ModernSqlManager.ReaderGetFloat(arguments[0].GetIntValue(context), (int)arguments[1].GetIntValue(context)),
				SqlFloatOperation.ExecuteScalarFloat => ModernSqlManager.ExecuteScalarFloat(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context)),
				_ => ModernSqlManager.ExecuteScalarFloat(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context), ReadSqlParameters(arguments, context, 2)),
			};
		}
	}

	enum SqlStringOperation
	{
		ReaderGetString,
		ExecuteScalarString,
		ExecuteScalarStringWithParameters,
		Escape,
	}

	sealed class SqlStringMethod : ModernFunctionMethod
	{
		readonly SqlStringOperation operation;

		public SqlStringMethod(string name, SqlStringOperation operation, int minArgumentCount, int maxArgumentCount)
			: base(name, minArgumentCount, maxArgumentCount)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return operation switch
			{
				SqlStringOperation.ReaderGetString => ModernSqlManager.ReaderGetString(arguments[0].GetIntValue(context), (int)arguments[1].GetIntValue(context)),
				SqlStringOperation.ExecuteScalarString => ModernSqlManager.ExecuteScalarString(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context)),
				SqlStringOperation.ExecuteScalarStringWithParameters => ModernSqlManager.ExecuteScalarString(arguments[0].GetStrValue(context), arguments[1].GetStrValue(context), ReadSqlParameters(arguments, context, 2)),
				_ => ModernSqlManager.Escape(arguments[0].GetStrValue(context)),
			};
		}
	}

	sealed class ExistFunctionMethod : ModernFunctionMethod
	{
		readonly ModernFunctionEvaluator evaluator;

		public ExistFunctionMethod(ModernFunctionEvaluator evaluator) : base("EXISTFUNCTION", 1, 2)
		{
			this.evaluator = evaluator;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(context);
			if (!evaluator.TryGetMethod(name, out var method))
				return 0;

			return method.GetReturnType(Array.Empty<AExpression>()) switch
			{
				EraType.Integer => 2,
				EraType.String => 3,
				EraType.Float => 4,
				_ => 1,
			};
		}
	}

	sealed class ExistMethMethod : ModernFunctionMethod
	{
		readonly ModernFunctionEvaluator evaluator;

		public ExistMethMethod(ModernFunctionEvaluator evaluator) : base("EXISTMETH", 1, 1)
		{
			this.evaluator = evaluator;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(context);
			if (!evaluator.TryGetMethod(name, out var method))
				return 0;

			return method.GetReturnType(Array.Empty<AExpression>()) switch
			{
				EraType.Integer => 1,
				EraType.String => 2,
				EraType.Float => 32,
				_ => 0,
			};
		}
	}

	sealed class GetMethMethod : ModernFunctionMethod
	{
		readonly ModernFunctionEvaluator evaluator;
		readonly EraType returnType;

		public GetMethMethod(string name, ModernFunctionEvaluator evaluator, EraType returnType)
			: base(name, 1, int.MaxValue)
		{
			this.evaluator = evaluator;
			this.returnType = returnType;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return returnType;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var methodArguments = GetMethodArguments(arguments);
			if (!TryGetTargetMethod(context, arguments, methodArguments, out var method))
				return arguments[1].GetIntValue(context);
			EnsureReturnType(method, methodArguments);
			method.Validate(methodArguments);
			return method.GetIntValue(context, methodArguments);
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var methodArguments = GetMethodArguments(arguments);
			if (!TryGetTargetMethod(context, arguments, methodArguments, out var method))
				return arguments[1].GetStrValue(context) ?? "";
			EnsureReturnType(method, methodArguments);
			method.Validate(methodArguments);
			return method.GetStrValue(context, methodArguments) ?? "";
		}

		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var methodArguments = GetMethodArguments(arguments);
			if (!TryGetTargetMethod(context, arguments, methodArguments, out var method))
				return ToDouble(arguments[1], context);
			EnsureReturnType(method, methodArguments);
			method.Validate(methodArguments);
			return method.GetFloatValue(context, methodArguments);
		}

		bool TryGetTargetMethod(
			ModernExpressionContext context,
			IReadOnlyList<AExpression> arguments,
			IReadOnlyList<AExpression> methodArguments,
			out ModernFunctionMethod method)
		{
			string name = arguments[0].GetStrValue(context);
			if (evaluator.TryGetMethod(name, out method))
				return true;
			if (arguments.Count > 1)
				return false;
			throw new KeyNotFoundException($"Unknown function: {name}");
		}

		void EnsureReturnType(ModernFunctionMethod method, IReadOnlyList<AExpression> methodArguments)
		{
			if (method.GetReturnType(methodArguments) != returnType)
				throw new InvalidOperationException($"{method.Name} does not return {returnType}.");
		}

		static IReadOnlyList<AExpression> GetMethodArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count <= 2)
				return Array.Empty<AExpression>();
			var methodArguments = new AExpression[arguments.Count - 2];
			for (int i = 2; i < arguments.Count; i++)
				methodArguments[i - 2] = arguments[i];
			return methodArguments;
		}
	}

	enum EnumNameTarget
	{
		Function,
		Variable,
		Macro,
	}

	enum EnumNameAction
	{
		BeginsWith,
		EndsWith,
		Contains,
	}

	sealed class EnumNameMethod : ModernFunctionMethod
	{
		readonly ModernFunctionEvaluator functionEvaluator;
		readonly EnumNameTarget target;
		readonly EnumNameAction action;

		public EnumNameMethod(string name, ModernFunctionEvaluator functionEvaluator, EnumNameTarget target, EnumNameAction action)
			: base(name, 1, 2)
		{
			this.functionEvaluator = functionEvaluator;
			this.target = target;
			this.action = action;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string pattern = arguments[0].GetStrValue(context) ?? "";
			IEnumerable<string> names = target switch
			{
				EnumNameTarget.Function => functionEvaluator.Methods.Keys,
				EnumNameTarget.Macro => MinorShift.Emuera.GlobalStatic.IdentifierDictionary?.MacroNames ?? Array.Empty<string>(),
				_ => context?.VariableEvaluator?.Tokens.Keys ?? Array.Empty<string>(),
			};

			var matches = names.Where(name => Matches(name, pattern)).ToArray();
			WriteStringResults(context, arguments.Count > 1 ? arguments[1] : null, matches);
			return matches.Length;
		}

		bool Matches(string name, string pattern)
		{
			if (pattern.Length == 0)
				return false;
			return action switch
			{
				EnumNameAction.BeginsWith => name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
				EnumNameAction.EndsWith => name.EndsWith(pattern, StringComparison.OrdinalIgnoreCase),
				_ => name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0,
			};
		}
	}

	sealed class EnumFilesMethod : ModernFunctionMethod
	{
		public EnumFilesMethod() : base("ENUMFILES", 1, 4) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string dir = arguments[0].GetStrValue(context);
			if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
				return -1;

			string pattern = arguments.Count > 1 ? arguments[1].GetStrValue(context) : "*";
			var option = arguments.Count > 2 && arguments[2].GetIntValue(context) != 0
				? SearchOption.AllDirectories
				: SearchOption.TopDirectoryOnly;
			string[] files;
			try
			{
				files = Directory.EnumerateFiles(dir, string.IsNullOrEmpty(pattern) ? "*" : pattern, option).ToArray();
			}
			catch
			{
				return -1;
			}

			WriteStringResults(context, arguments.Count > 3 ? arguments[3] : null, files);
			return files.Length;
		}
	}

	sealed class ExistFileMethod : ModernFunctionMethod
	{
		public ExistFileMethod() : base("EXISTFILE", 1, 1) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string path = arguments[0].GetStrValue(context);
			return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? 1 : 0;
		}
	}

	static bool TryGetTextPath(AExpression pathExpression, ModernExpressionContext context, bool forceSavDir, bool forSave, out string path, out bool indexedPath)
	{
		path = null;
		indexedPath = false;
		if (pathExpression.IsInteger)
		{
			long index = pathExpression.GetIntValue(context);
			if (index < 0 || index > int.MaxValue)
				return false;
			string dir = forceSavDir ? Config.ForceSavDir : Config.SavDir;
			path = string.Format(CultureInfo.InvariantCulture, "{0}txt{1:00}.txt", dir ?? "", (int)index);
			indexedPath = true;
			return true;
		}
		if (!pathExpression.IsString)
			return false;

		path = GetValidRelativePath(pathExpression.GetStrValue(context));
		if (string.IsNullOrEmpty(path))
			return false;

		string extension = Path.HasExtension(path) ? Path.GetExtension(path).TrimStart('.').ToLowerInvariant() : "";
		if (!IsValidTextExtension(extension))
		{
			if (!forSave)
				return false;
			path = Path.ChangeExtension(path, "txt");
		}
		return true;
	}

	static string GetValidRelativePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return null;
		string sanitized = path.Replace('/', Path.DirectorySeparatorChar);
		string parentSegment = ".." + Path.DirectorySeparatorChar;
		while (sanitized.Contains(parentSegment, StringComparison.Ordinal))
			sanitized = sanitized.Replace(parentSegment, "", StringComparison.Ordinal);
		try
		{
			if (Path.IsPathRooted(sanitized))
				return null;
			string baseDir = Path.GetFullPath(Program.ExeDir);
			if (!baseDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
				baseDir += Path.DirectorySeparatorChar;
			string candidate = Path.GetFullPath(Path.Combine(baseDir, sanitized));
			return candidate.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) ? candidate : null;
		}
		catch
		{
			return null;
		}
	}

	static bool IsValidTextExtension(string extension)
	{
		return string.Equals(extension, "txt", StringComparison.OrdinalIgnoreCase);
	}

	static readonly Dictionary<string, int> KnownColorRgb = new(StringComparer.OrdinalIgnoreCase)
	{
		["AliceBlue"] = 0xF0F8FF,
		["AntiqueWhite"] = 0xFAEBD7,
		["Aqua"] = 0x00FFFF,
		["Aquamarine"] = 0x7FFFD4,
		["Azure"] = 0xF0FFFF,
		["Beige"] = 0xF5F5DC,
		["Bisque"] = 0xFFE4C4,
		["Black"] = 0x000000,
		["BlanchedAlmond"] = 0xFFEBCD,
		["Blue"] = 0x0000FF,
		["BlueViolet"] = 0x8A2BE2,
		["Brown"] = 0xA52A2A,
		["BurlyWood"] = 0xDEB887,
		["CadetBlue"] = 0x5F9EA0,
		["Chartreuse"] = 0x7FFF00,
		["Chocolate"] = 0xD2691E,
		["Coral"] = 0xFF7F50,
		["CornflowerBlue"] = 0x6495ED,
		["Cornsilk"] = 0xFFF8DC,
		["Crimson"] = 0xDC143C,
		["Cyan"] = 0x00FFFF,
		["DarkBlue"] = 0x00008B,
		["DarkCyan"] = 0x008B8B,
		["DarkGoldenrod"] = 0xB8860B,
		["DarkGray"] = 0xA9A9A9,
		["DarkGreen"] = 0x006400,
		["DarkGrey"] = 0xA9A9A9,
		["DarkKhaki"] = 0xBDB76B,
		["DarkMagenta"] = 0x8B008B,
		["DarkOliveGreen"] = 0x556B2F,
		["DarkOrange"] = 0xFF8C00,
		["DarkOrchid"] = 0x9932CC,
		["DarkRed"] = 0x8B0000,
		["DarkSalmon"] = 0xE9967A,
		["DarkSeaGreen"] = 0x8FBC8F,
		["DarkSlateBlue"] = 0x483D8B,
		["DarkSlateGray"] = 0x2F4F4F,
		["DarkSlateGrey"] = 0x2F4F4F,
		["DarkTurquoise"] = 0x00CED1,
		["DarkViolet"] = 0x9400D3,
		["DeepPink"] = 0xFF1493,
		["DeepSkyBlue"] = 0x00BFFF,
		["DimGray"] = 0x696969,
		["DimGrey"] = 0x696969,
		["DodgerBlue"] = 0x1E90FF,
		["Firebrick"] = 0xB22222,
		["FloralWhite"] = 0xFFFAF0,
		["ForestGreen"] = 0x228B22,
		["Fuchsia"] = 0xFF00FF,
		["Gainsboro"] = 0xDCDCDC,
		["GhostWhite"] = 0xF8F8FF,
		["Gold"] = 0xFFD700,
		["Goldenrod"] = 0xDAA520,
		["Gray"] = 0x808080,
		["Green"] = 0x008000,
		["GreenYellow"] = 0xADFF2F,
		["Grey"] = 0x808080,
		["Honeydew"] = 0xF0FFF0,
		["HotPink"] = 0xFF69B4,
		["IndianRed"] = 0xCD5C5C,
		["Indigo"] = 0x4B0082,
		["Ivory"] = 0xFFFFF0,
		["Khaki"] = 0xF0E68C,
		["Lavender"] = 0xE6E6FA,
		["LavenderBlush"] = 0xFFF0F5,
		["LawnGreen"] = 0x7CFC00,
		["LemonChiffon"] = 0xFFFACD,
		["LightBlue"] = 0xADD8E6,
		["LightCoral"] = 0xF08080,
		["LightCyan"] = 0xE0FFFF,
		["LightGoldenrodYellow"] = 0xFAFAD2,
		["LightGray"] = 0xD3D3D3,
		["LightGreen"] = 0x90EE90,
		["LightGrey"] = 0xD3D3D3,
		["LightPink"] = 0xFFB6C1,
		["LightSalmon"] = 0xFFA07A,
		["LightSeaGreen"] = 0x20B2AA,
		["LightSkyBlue"] = 0x87CEFA,
		["LightSlateGray"] = 0x778899,
		["LightSlateGrey"] = 0x778899,
		["LightSteelBlue"] = 0xB0C4DE,
		["LightYellow"] = 0xFFFFE0,
		["Lime"] = 0x00FF00,
		["LimeGreen"] = 0x32CD32,
		["Linen"] = 0xFAF0E6,
		["Magenta"] = 0xFF00FF,
		["Maroon"] = 0x800000,
		["MediumAquamarine"] = 0x66CDAA,
		["MediumBlue"] = 0x0000CD,
		["MediumOrchid"] = 0xBA55D3,
		["MediumPurple"] = 0x9370DB,
		["MediumSeaGreen"] = 0x3CB371,
		["MediumSlateBlue"] = 0x7B68EE,
		["MediumSpringGreen"] = 0x00FA9A,
		["MediumTurquoise"] = 0x48D1CC,
		["MediumVioletRed"] = 0xC71585,
		["MidnightBlue"] = 0x191970,
		["MintCream"] = 0xF5FFFA,
		["MistyRose"] = 0xFFE4E1,
		["Moccasin"] = 0xFFE4B5,
		["NavajoWhite"] = 0xFFDEAD,
		["Navy"] = 0x000080,
		["OldLace"] = 0xFDF5E6,
		["Olive"] = 0x808000,
		["OliveDrab"] = 0x6B8E23,
		["Orange"] = 0xFFA500,
		["OrangeRed"] = 0xFF4500,
		["Orchid"] = 0xDA70D6,
		["PaleGoldenrod"] = 0xEEE8AA,
		["PaleGreen"] = 0x98FB98,
		["PaleTurquoise"] = 0xAFEEEE,
		["PaleVioletRed"] = 0xDB7093,
		["PapayaWhip"] = 0xFFEFD5,
		["PeachPuff"] = 0xFFDAB9,
		["Peru"] = 0xCD853F,
		["Pink"] = 0xFFC0CB,
		["Plum"] = 0xDDA0DD,
		["PowderBlue"] = 0xB0E0E6,
		["Purple"] = 0x800080,
		["RebeccaPurple"] = 0x663399,
		["Red"] = 0xFF0000,
		["RosyBrown"] = 0xBC8F8F,
		["RoyalBlue"] = 0x4169E1,
		["SaddleBrown"] = 0x8B4513,
		["Salmon"] = 0xFA8072,
		["SandyBrown"] = 0xF4A460,
		["SeaGreen"] = 0x2E8B57,
		["SeaShell"] = 0xFFF5EE,
		["Sienna"] = 0xA0522D,
		["Silver"] = 0xC0C0C0,
		["SkyBlue"] = 0x87CEEB,
		["SlateBlue"] = 0x6A5ACD,
		["SlateGray"] = 0x708090,
		["SlateGrey"] = 0x708090,
		["Snow"] = 0xFFFAFA,
		["SpringGreen"] = 0x00FF7F,
		["SteelBlue"] = 0x4682B4,
		["Tan"] = 0xD2B48C,
		["Teal"] = 0x008080,
		["Thistle"] = 0xD8BFD8,
		["Tomato"] = 0xFF6347,
		["Turquoise"] = 0x40E0D0,
		["Violet"] = 0xEE82EE,
		["Wheat"] = 0xF5DEB3,
		["White"] = 0xFFFFFF,
		["WhiteSmoke"] = 0xF5F5F5,
		["Yellow"] = 0xFFFF00,
		["YellowGreen"] = 0x9ACD32,
	};

	static long ReadRgbByte(AExpression expression, ModernExpressionContext context, int argumentIndex, string name)
	{
		long value = expression.GetIntValue(context);
		if (value < 0 || value > 255)
			throw new ArgumentOutOfRangeException(nameof(expression), $"{name} argument {argumentIndex} must be between 0 and 255.");
		return value;
	}

	static bool TryGetKnownColorRgb(string colorName, out int rgb)
	{
		return KnownColorRgb.TryGetValue(colorName ?? "", out rgb);
	}

	static bool TryMapLegacyVariableCode(VariableCode modernCode, out MinorShift.Emuera.GameData.Variable.VariableCode legacyCode)
	{
		return TryMapLegacyVariableCode(modernCode.ToString(), out legacyCode);
	}

	static bool TryMapLegacyVariableCode(string variableName, out MinorShift.Emuera.GameData.Variable.VariableCode legacyCode)
	{
		legacyCode = default;
		if (string.IsNullOrWhiteSpace(variableName))
			return false;
		string name = variableName.Trim();
		int atIndex = name.IndexOf('@');
		if (atIndex >= 0)
			name = name.Substring(0, atIndex);
		int colonIndex = name.IndexOf(':');
		if (colonIndex >= 0)
			name = name.Substring(0, colonIndex);
		return Enum.TryParse(name, true, out legacyCode);
	}

	static bool TryKeywordToInteger(MinorShift.Emuera.GameData.Variable.VariableCode legacyCode, string key, int index, out int value)
	{
		value = -1;
		var constantData = MinorShift.Emuera.GlobalStatic.ConstantData;
		if (constantData == null)
			return false;
		return constantData.TryKeywordToInteger(out value, legacyCode, key, index);
	}

	static bool TryIntegerToKeyword(string variableName, long value, int index, out string key)
	{
		key = "";
		if (!TryMapLegacyVariableCode(variableName, out var legacyCode))
			return false;
		var constantData = MinorShift.Emuera.GlobalStatic.ConstantData;
		if (constantData == null)
			return false;
		Dictionary<string, int> dictionary;
		try
		{
			dictionary = constantData.GetKeywordDictionary(out _, legacyCode, index);
		}
		catch
		{
			return false;
		}
		if (dictionary == null)
			return false;
		foreach (var pair in dictionary)
		{
			if (pair.Value == value)
			{
				key = pair.Key;
				return true;
			}
		}
		return false;
	}

	static SingleTerm GetConfigTerm(string key, string functionName)
	{
		if (string.IsNullOrEmpty(key))
			throw new ArgumentException($"{functionName} needs a non-empty config name.");
		AConfigItem item = ConfigData.Instance.GetItem(key);
		if (item == null)
			throw new ArgumentException($"{key} is not a valid config name.");

		switch (item.Code)
		{
			case ConfigCode.AutoSave:
			case ConfigCode.MoneyFirst:
				return new SingleLongTerm(item.GetValue<bool>() ? 1 : 0);
			case ConfigCode.WindowX:
			case ConfigCode.PrintCPerLine:
			case ConfigCode.PrintCLength:
			case ConfigCode.FontSize:
			case ConfigCode.LineHeight:
			case ConfigCode.SaveDataNos:
			case ConfigCode.MaxShopItem:
			case ConfigCode.ComAbleDefault:
				return new SingleLongTerm(item.GetValue<int>());
			case ConfigCode.ForeColor:
			case ConfigCode.BackColor:
			case ConfigCode.FocusColor:
			case ConfigCode.LogColor:
				var color = item.GetValue<uEmuera.Drawing.Color>();
				return new SingleLongTerm(((color.R * 256L) + color.G) * 256L + color.B);
			case ConfigCode.pbandDef:
			case ConfigCode.RelationDef:
				return new SingleLongTerm(item.GetValue<long>());
			case ConfigCode.FontName:
			case ConfigCode.MoneyLabel:
			case ConfigCode.LoadLabel:
			case ConfigCode.DrawLineString:
			case ConfigCode.TitleMenuString0:
			case ConfigCode.TitleMenuString1:
			case ConfigCode.TimeupLabel:
				return new SingleStrTerm(item.GetValue<string>() ?? "");
			case ConfigCode.BarChar1:
			case ConfigCode.BarChar2:
				return new SingleStrTerm(item.GetValue<char>().ToString());
			case ConfigCode.TextDrawingMode:
				return new SingleStrTerm(item.GetValue<TextDrawingMode>().ToString());
			default:
				throw new ArgumentException($"{key} cannot be read from ERB config functions.");
		}
	}

	static int GetHtmlDisplayLength(string value)
	{
		string plain = GetHtmlPlainTextFallback(value);
		return LangManager.GetStrlenLang(plain) * Math.Max(Config.FontSize, 1) / 2;
	}

	static string[] GetHtmlSubStrings(string value, int length)
	{
		return SplitPlainTextByDisplayLength(GetHtmlPlainTextFallback(value), length);
	}

	static string GetHtmlPlainTextFallback(string value)
	{
		try
		{
			return HtmlManager.Html2PlainText(value ?? "");
		}
		catch
		{
			return Regex.Replace(value ?? "", "\\<[^<]*\\>", "");
		}
	}

	static string[] SplitPlainTextByDisplayLength(string value, int length)
	{
		if (string.IsNullOrEmpty(value))
			return new[] { "", "" };
		if (length <= 0)
			return new[] { "", value };

		var builder = new StringBuilder();
		int used = 0;
		int index = 0;
		while (index < value.Length)
		{
			string current = value[index].ToString();
			int width = Math.Max(1, LangManager.GetStrlenLang(current));
			if (used + width > length)
				break;
			builder.Append(current);
			used += width;
			index++;
		}
		return new[] { builder.ToString(), index >= value.Length ? "" : value.Substring(index) };
	}

	static void WriteStringResults(ModernExpressionContext context, AExpression destination, IReadOnlyList<string> values)
	{
		if (context?.VariableEvaluator == null)
			return;
		ModernVariableToken token = null;
		if (destination is ModernVariableTerm term && term.Identifier.IsString)
			token = term.Identifier;
		if (token == null && context.VariableEvaluator.TryGetToken("RESULTS", out var resultsToken))
			token = resultsToken;
		if (token == null)
			return;

		int length = 0;
		try
		{
			length = token.GetLength();
		}
		catch
		{
			length = values.Count;
		}
		int count = Math.Min(length, values.Count);
		for (int i = 0; i < count; i++)
			token.SetValue(values[i], context, new long[] { i });
	}

	static void SetResultGroupCount(ModernExpressionContext context, int groupCount)
	{
		SetIntegerResult(context, 1, groupCount);
	}

	static IReadOnlyList<string> FlattenRegexCaptures(MatchCollection matches, Regex regex)
	{
		var groupNames = regex.GetGroupNames();
		var values = new List<string>(matches.Count * groupNames.Length);
		foreach (Match match in matches)
		{
			foreach (string name in groupNames)
				values.Add(match.Groups[name].Value);
		}
		return values;
	}

	static Dictionary<string, XmlDocument> GetXmlDocuments(ModernExpressionContext context)
	{
		if (context?.VariableEvaluator?.VariableData == null)
			throw new InvalidOperationException("No modern variable data is available.");
		return context.VariableEvaluator.VariableData.DataXmlDocuments;
	}

	static string GetXmlDocumentKey(AExpression expression, ModernExpressionContext context)
	{
		return expression.IsString ? expression.GetStrValue(context) ?? "" : expression.GetIntValue(context).ToString(CultureInfo.InvariantCulture);
	}

	static bool TryGetXmlDocument(ModernExpressionContext context, string key, out XmlDocument document)
	{
		return GetXmlDocuments(context).TryGetValue(key ?? "", out document);
	}

	static bool TryGetStoredXmlDocument(ModernExpressionContext context, AExpression source, bool byName, out string key, out XmlDocument document)
	{
		key = null;
		document = null;
		if (!source.IsInteger && !(byName && source.IsString))
			return false;
		key = GetXmlDocumentKey(source, context);
		return TryGetXmlDocument(context, key, out document);
	}

	static bool TryLoadXmlSource(ModernExpressionContext context, AExpression source, bool byName, out XmlDocument document, out bool saveToSource)
	{
		saveToSource = false;
		if (TryGetStoredXmlDocument(context, source, byName, out _, out document))
			return true;
		if (source.IsInteger || byName)
		{
			document = null;
			return false;
		}
		document = ParseXml(source.GetStrValue(context), "XML");
		saveToSource = true;
		return true;
	}

	static XmlDocument ParseXml(string xml, string functionName)
	{
		var document = new XmlDocument();
		try
		{
			document.LoadXml(xml ?? "");
			return document;
		}
		catch (XmlException e)
		{
			throw new FormatException($"{functionName} received invalid XML: {e.Message}", e);
		}
	}

	static XmlNodeList SelectXmlNodes(XmlDocument document, string path, string functionName)
	{
		try
		{
			return document.SelectNodes(path ?? "");
		}
		catch (System.Xml.XPath.XPathException e)
		{
			throw new FormatException($"{functionName} received invalid XPath: {e.Message}", e);
		}
	}

	static string ReadXmlNode(XmlNode node, long style)
	{
		return style switch
		{
			1 => node.InnerText,
			2 => node.InnerXml,
			3 => node.OuterXml,
			4 => node.Name,
			_ => node.Value ?? "",
		};
	}

	static void SetXmlNode(XmlNode node, string value, long style)
	{
		switch (style)
		{
			case 1:
				node.InnerText = value;
				break;
			case 2:
				node.InnerXml = value;
				break;
			default:
				node.Value = value;
				break;
		}
	}

	static int NormalizeXmlInsertMethod(long method)
	{
		return method < 0 || method > 2 ? 0 : (int)method;
	}

	static bool InsertXmlNode(XmlNode targetNode, XmlNode newChild, int method, XmlAddOperation operation)
	{
		if (operation == XmlAddOperation.Node)
		{
			switch (method)
			{
				case 0:
					targetNode.AppendChild(newChild);
					return true;
				case 1:
					if (targetNode.ParentNode == null)
						return false;
					targetNode.ParentNode.InsertBefore(newChild, targetNode);
					return true;
				case 2:
					if (targetNode.ParentNode == null)
						return false;
					targetNode.ParentNode.InsertAfter(newChild, targetNode);
					return true;
				default:
					return false;
			}
		}

		if (newChild is not XmlAttribute attribute)
			return false;
		if (method > 0 && targetNode is not XmlAttribute)
			return false;
		switch (method)
		{
			case 0:
				if (targetNode is not XmlElement element)
					return false;
				element.Attributes.Append(attribute);
				return true;
			case 1:
				if (targetNode is not XmlAttribute before || before.OwnerElement == null)
					return false;
				before.OwnerElement.Attributes.InsertBefore(attribute, before);
				return true;
			case 2:
				if (targetNode is not XmlAttribute after || after.OwnerElement == null)
					return false;
				after.OwnerElement.Attributes.InsertAfter(attribute, after);
				return true;
			default:
				return false;
		}
	}

	static bool RemoveXmlNode(XmlNode node, XmlRemoveOperation operation)
	{
		if (operation == XmlRemoveOperation.Attribute)
		{
			if (node is not XmlAttribute attribute || attribute.OwnerElement == null)
				return false;
			attribute.OwnerElement.Attributes.Remove(attribute);
			return true;
		}
		if (node.ParentNode == null)
			return false;
		node.ParentNode.RemoveChild(node);
		return true;
	}

	static bool ReplaceXmlNode(XmlNode node, XmlNode newNode)
	{
		if (node.ParentNode == null)
			return false;
		node.ParentNode.ReplaceChild(newNode, node);
		return true;
	}

	static void SaveXmlSourceIfNeeded(AExpression source, ModernExpressionContext context, XmlDocument document, bool saveToSource)
	{
		if (!saveToSource)
			return;
		if (source is ModernVariableTerm term && term.Identifier.IsString)
			term.SetValue(document.OuterXml, context);
	}

	static void SetIntegerResult(ModernExpressionContext context, long index, long value)
	{
		if (context?.VariableEvaluator == null)
			return;
		var result = context.VariableEvaluator.CreateTerm("RESULT", new SingleLongTerm(index));
		result.SetValue(value, context);
	}

	static void WriteIntegerResults(ModernExpressionContext context, AExpression destination, IReadOnlyList<long> values)
	{
		if (context?.VariableEvaluator == null)
			return;
		ModernVariableToken token = null;
		if (destination is ModernVariableTerm term && term.Identifier.IsInteger)
			token = term.Identifier;
		if (token == null && context.VariableEvaluator.TryGetToken("RESULT", out var resultToken))
			token = resultToken;
		if (token == null)
			return;

		int length = 0;
		try
		{
			length = token.GetLength();
		}
		catch
		{
			length = values.Count;
		}
		int count = Math.Min(length, values.Count);
		for (int i = 0; i < count; i++)
			token.SetValue(values[i], context, new long[] { i });
	}

	static Dictionary<string, DataTable> GetDataTables(ModernExpressionContext context)
	{
		if (context?.VariableEvaluator?.VariableData == null)
			throw new InvalidOperationException("No modern variable data is available.");
		return context.VariableEvaluator.VariableData.DataTables;
	}

	static bool TryGetDataTable(ModernExpressionContext context, string key, out DataTable table)
	{
		return GetDataTables(context).TryGetValue(key ?? "", out table);
	}

	static DataTable CreateDataTable(string key)
	{
		var table = new DataTable(key)
		{
			CaseSensitive = true
		};
		var idColumn = table.Columns.Add("id", typeof(long));
		idColumn.AllowDBNull = false;
		idColumn.Unique = true;
		table.PrimaryKey = new[] { idColumn };
		return table;
	}

	static long NextDataTableRowId(ModernExpressionContext context)
	{
		var data = context.VariableEvaluator.VariableData;
		return data.NextDataTableRowId++;
	}

	static long SetDataTableRowValues(DataRow row, DataTable table, ModernExpressionContext context, IReadOnlyList<AExpression> arguments, int offset)
	{
		if (arguments.Count == offset)
			return 0;
		if (arguments.Count == offset + 3 && arguments[offset] is ModernVariableTerm namesTerm && namesTerm.Identifier.IsString)
		{
			long requested = arguments[offset + 2].GetIntValue(context);
			if (requested <= 0)
				return 0;
			var names = ReadStringArray(namesTerm, context, requested);
			long count = 0;
			if (arguments[offset + 1] is ModernVariableTerm valuesTerm)
			{
				if (valuesTerm.Identifier.IsString)
				{
					var values = ReadStringArray(valuesTerm, context, names.Count);
					for (int i = 0; i < Math.Min(names.Count, values.Count); i++)
					{
						SetDataTableStringValue(row, table.Columns[names[i]], values[i]);
						count++;
					}
					return count;
				}
				if (valuesTerm.Identifier.IsFloat)
				{
					var values = ReadFloatArray(valuesTerm, context, names.Count);
					for (int i = 0; i < Math.Min(names.Count, values.Count); i++)
					{
						SetDataTableFloatValue(row, table.Columns[names[i]], values[i]);
						count++;
					}
					return count;
				}
				if (valuesTerm.Identifier.IsInteger)
				{
					var values = ReadIntegerArray(valuesTerm, context, names.Count);
					for (int i = 0; i < Math.Min(names.Count, values.Count); i++)
					{
						SetDataTableIntegerValue(row, table.Columns[names[i]], values[i]);
						count++;
					}
					return count;
				}
			}
		}

		if (((arguments.Count - offset) % 2) != 0)
			throw new FormatException("DT_ROW_ADD/DT_ROW_SET need column/value pairs.");
		long changed = 0;
		for (int i = offset; i < arguments.Count; i += 2)
		{
			string columnName = arguments[i].GetStrValue(context) ?? "";
			if (!table.Columns.Contains(columnName))
				throw new InvalidOperationException($"{columnName} is not a DataTable column.");
			SetDataTableValue(row, table.Columns[columnName], arguments[i + 1], context);
			changed++;
		}
		return changed;
	}

	static void SetDataTableValue(DataRow row, DataColumn column, AExpression value, ModernExpressionContext context)
	{
		if (string.Equals(column.ColumnName, "id", StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("DataTable id column is read-only.");
		if (value == null)
		{
			row[column] = DBNull.Value;
			return;
		}
		if (column.DataType == typeof(string))
			row[column] = value.GetStrValue(context) ?? "";
		else if (column.DataType == typeof(double))
			row[column] = ExpressionToDouble(value, context);
		else
			row[column] = ConvertDataTableInteger(value.GetIntValue(context), column.DataType);
	}

	static void SetDataTableStringValue(DataRow row, DataColumn column, string value)
	{
		if (column == null)
			throw new InvalidOperationException("DataTable column does not exist.");
		if (column.DataType != typeof(string))
			throw new InvalidOperationException($"{column.ColumnName} is not a string column.");
		row[column] = value ?? "";
	}

	static void SetDataTableIntegerValue(DataRow row, DataColumn column, long value)
	{
		if (column == null)
			throw new InvalidOperationException("DataTable column does not exist.");
		if (column.DataType == typeof(string))
			throw new InvalidOperationException($"{column.ColumnName} is not an integer column.");
		row[column] = ConvertDataTableInteger(value, column.DataType);
	}

	static void SetDataTableFloatValue(DataRow row, DataColumn column, double value)
	{
		if (column == null)
			throw new InvalidOperationException("DataTable column does not exist.");
		row[column] = value;
	}

	static DataRow GetDataTableRow(DataTable table, long index, bool asId)
	{
		if (asId)
			return table.Rows.Find(index);
		return index >= 0 && index < table.Rows.Count ? table.Rows[(int)index] : null;
	}

	static bool TryGetDataTableCell(ModernExpressionContext context, IReadOnlyList<AExpression> arguments, out DataTable table, out DataRow row, out object value)
	{
		table = null;
		row = null;
		value = null;
		if (!TryGetDataTable(context, arguments[0].GetStrValue(context), out table))
			return false;
		bool asId = arguments.Count == 4 && arguments[3].GetIntValue(context) != 0;
		row = GetDataTableRow(table, arguments[1].GetIntValue(context), asId);
		string columnName = arguments[2].GetStrValue(context) ?? "";
		if (row == null || !table.Columns.Contains(columnName))
			return false;
		value = row[columnName];
		return true;
	}

	static IReadOnlyList<string> ReadStringArray(ModernVariableTerm term, ModernExpressionContext context, long maxCount)
	{
		int count = Math.Min(GetVariableArrayLength(term.Identifier, context), ClampCount(maxCount));
		var values = new string[count];
		for (int i = 0; i < count; i++)
			values[i] = term.Identifier.GetStrValue(context, new long[] { i }) ?? "";
		return values;
	}

	static IReadOnlyList<long> ReadIntegerArray(AExpression expression, ModernExpressionContext context, long maxCount)
	{
		var term = expression as ModernVariableTerm ?? throw new InvalidOperationException("Integer array argument expected.");
		int count = Math.Min(GetVariableArrayLength(term.Identifier, context), ClampCount(maxCount));
		var values = new long[count];
		for (int i = 0; i < count; i++)
			values[i] = term.Identifier.GetIntValue(context, new long[] { i });
		return values;
	}

	static IReadOnlyList<double> ReadFloatArray(ModernVariableTerm term, ModernExpressionContext context, long maxCount)
	{
		int count = Math.Min(GetVariableArrayLength(term.Identifier, context), ClampCount(maxCount));
		var values = new double[count];
		for (int i = 0; i < count; i++)
			values[i] = term.Identifier.GetFloatValue(context, new long[] { i });
		return values;
	}

	static int GetVariableArrayLength(ModernVariableToken token, ModernExpressionContext context)
	{
		try
		{
			object array = token.GetArray(context);
			return array switch
			{
				long[] longArray => longArray.Length,
				string[] stringArray => stringArray.Length,
				double[] doubleArray => doubleArray.Length,
				SparseArray<long> sparseLong => sparseLong.Length,
				SparseArray<string> sparseString => sparseString.Length,
				SparseArray<double> sparseDouble => sparseDouble.Length,
				_ => token.GetLength(),
			};
		}
		catch
		{
			return token.GetLength();
		}
	}

	static int ClampCount(long count)
	{
		if (count <= 0)
			return 0;
		return count > int.MaxValue ? int.MaxValue : (int)count;
	}

	static long DataTableTypeToInt(Type type)
	{
		if (type == typeof(sbyte))
			return 1;
		if (type == typeof(short))
			return 2;
		if (type == typeof(int))
			return 3;
		if (type == typeof(long))
			return 4;
		if (type == typeof(string))
			return 5;
		if (type == typeof(double))
			return 6;
		return long.MaxValue;
	}

	static Type DataTableIntToType(long value)
	{
		return value switch
		{
			1 => typeof(sbyte),
			2 => typeof(short),
			3 => typeof(int),
			4 => typeof(long),
			5 => typeof(string),
			6 => typeof(double),
			_ => null,
		};
	}

	static Type DataTableNameToType(string name)
	{
		return (name ?? "").ToLowerInvariant() switch
		{
			"int8" => typeof(sbyte),
			"int16" => typeof(short),
			"int32" => typeof(int),
			"int64" => typeof(long),
			"string" => typeof(string),
			"float" => typeof(double),
			"double" => typeof(double),
			_ => null,
		};
	}

	static object ConvertDataTableInteger(long value, Type type)
	{
		if (type == typeof(sbyte))
			return (sbyte)Math.Min(Math.Max(value, sbyte.MinValue), sbyte.MaxValue);
		if (type == typeof(short))
			return (short)Math.Min(Math.Max(value, short.MinValue), short.MaxValue);
		if (type == typeof(int))
			return (int)Math.Min(Math.Max(value, int.MinValue), int.MaxValue);
		if (type == typeof(double))
			return (double)value;
		return value;
	}

	static double ExpressionToDouble(AExpression expression, ModernExpressionContext context)
	{
		if (expression.IsFloat)
			return expression.GetFloatValue(context);
		if (expression.IsInteger)
			return expression.GetIntValue(context);
		string value = expression.GetStrValue(context);
		if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
			return parsed;
		if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
			return parsed;
		throw new FormatException($"Cannot convert \"{value}\" to float.");
	}

	static MinorShift.Emuera.GameData.Variable.VariableEvaluator RequireLegacyVariableEvaluator()
	{
		return GlobalStatic.VEvaluator ?? throw new InvalidOperationException("Legacy variable evaluator data is required for this modern function.");
	}

	static MinorShift.Emuera.GameData.Expression.ExpressionMediator RequireLegacyExpressionMediator()
	{
		return GlobalStatic.EMediator ?? throw new InvalidOperationException("Legacy expression mediator is required for this modern function.");
	}

	static MinorShift.Emuera.GameProc.Process RequireLegacyProcess()
	{
		return GlobalStatic.Process ?? throw new InvalidOperationException("Legacy process state is required for this modern function.");
	}

	static MinorShift.Emuera.GameData.ConstantData RequireLegacyConstantData()
	{
		return GlobalStatic.ConstantData ?? throw new InvalidOperationException("Legacy constant data is required for this modern function.");
	}

	static bool GetOptionalSpFlag(ModernExpressionContext context, IReadOnlyList<AExpression> arguments, int index)
	{
		bool isSp = arguments.Count > index && arguments[index] != null && arguments[index].GetIntValue(context) != 0;
		if (isSp && !Config.CompatiSPChara)
			throw new InvalidOperationException("SP character functions require the compatibility option to be enabled.");
		return isSp;
	}

	static EmueraConsole RequireConsole()
	{
		return GlobalStatic.Console ?? throw new InvalidOperationException("A Godot console instance is required for this modern function.");
	}

	static GraphicsImage ReadGraphics(AExpression expression, ModernExpressionContext context)
	{
		long id = expression.GetIntValue(context);
		if (id < int.MinValue || id > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(expression), "Graphics id is out of Int32 range.");
		return AppContents.GetGraphics((int)id);
	}

	static Point ReadPoint(IReadOnlyList<AExpression> arguments, ModernExpressionContext context, int offset)
	{
		return new Point((int)arguments[offset].GetIntValue(context), (int)arguments[offset + 1].GetIntValue(context));
	}

	static Rectangle ReadRectangle(IReadOnlyList<AExpression> arguments, ModernExpressionContext context, int offset)
	{
		return new Rectangle(
			(int)arguments[offset].GetIntValue(context),
			(int)arguments[offset + 1].GetIntValue(context),
			(int)arguments[offset + 2].GetIntValue(context),
			(int)arguments[offset + 3].GetIntValue(context));
	}

	static uEmuera.Drawing.Color ReadArgb(AExpression expression, ModernExpressionContext context)
	{
		return uEmuera.Drawing.Color.FromArgb(unchecked((int)expression.GetIntValue(context)));
	}

	static FontStyle ReadFontStyle(long value)
	{
		FontStyle style = FontStyle.Regular;
		if ((value & (long)FontStyle.Bold) != 0)
			style |= FontStyle.Bold;
		if ((value & (long)FontStyle.Italic) != 0)
			style |= FontStyle.Italic;
		if ((value & (long)FontStyle.Underline) != 0)
			style |= FontStyle.Underline;
		if ((value & (long)FontStyle.Strikeout) != 0)
			style |= FontStyle.Strikeout;
		return style;
	}

	static void FillImageRect(GraphicsImage g, uEmuera.Drawing.Color color, Rectangle rect)
	{
		if (g?.godotImage == null)
			return;
		int x1 = Math.Max(0, rect.X);
		int y1 = Math.Max(0, rect.Y);
		int x2 = Math.Min(g.Width, rect.X + rect.Width);
		int y2 = Math.Min(g.Height, rect.Y + rect.Height);
		var godotColor = new Godot.Color(color.r, color.g, color.b, color.a);
		for (int y = y1; y < y2; y++)
		{
			for (int x = x1; x < x2; x++)
				g.godotImage.SetPixel(x, y, godotColor);
		}
	}

	static float[][] ReadColorMatrix(AExpression expression, ModernExpressionContext context)
	{
		if (expression is not ModernVariableTerm term)
			throw new InvalidOperationException("Color matrix must be an integer variable.");
		var matrix = new float[5][];
		for (int row = 0; row < 5; row++)
		{
			matrix[row] = new float[5];
			for (int col = 0; col < 5; col++)
				matrix[row][col] = term.Identifier.GetIntValue(context, new long[] { row, col }) / 1000.0f;
		}
		return matrix;
	}

	static string ResolveImagePath(string filename, bool keepRelative)
	{
		if (string.IsNullOrEmpty(filename))
			return null;
		string path = filename;
		if (!Path.IsPathRooted(path) && !keepRelative)
			path = Path.Combine(Program.ContentDir ?? "", filename);
		string resolved = uEmuera.Utils.ResolveExistingFilePath(path);
		if (!string.IsNullOrEmpty(resolved) && uEmuera.Utils.FileExists(resolved))
			return resolved;
		return path;
	}

	static string GetSaveDataGraphicsPath(long fileNo)
	{
		if (fileNo < 0 || fileNo > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(fileNo));
		string dir = Config.SavDir;
		if (string.IsNullOrEmpty(dir))
			dir = Path.Combine(Program.ExeDir ?? "", "save");
		if (!Path.IsPathRooted(dir))
			dir = Path.Combine(Program.ExeDir ?? "", dir);
		return Path.Combine(dir, $"img{fileNo:0000}.png");
	}

	static Dictionary<string, Dictionary<string, string>> GetMaps(ModernExpressionContext context)
	{
		if (context?.VariableEvaluator?.VariableData == null)
			throw new InvalidOperationException("No modern variable data is available.");
		return context.VariableEvaluator.VariableData.DataStringMaps;
	}

	static bool TryGetMap(ModernExpressionContext context, string name, out Dictionary<string, string> map)
	{
		return GetMaps(context).TryGetValue(name ?? "", out map);
	}

	static bool IsKnownMapPredicateMode(string mode)
	{
		return mode == "KEY_CONTAINS"
			|| mode == "KEY_PREFIX"
			|| mode == "KEY_SUFFIX"
			|| mode == "VAL_CONTAINS"
			|| mode == "VAL_EQ"
			|| mode == "VAL_NE";
	}

	static bool MapPredicate(KeyValuePair<string, string> pair, string matchValue, string mode)
	{
		return mode switch
		{
			"KEY_CONTAINS" => pair.Key.Contains(matchValue),
			"KEY_PREFIX" => pair.Key.StartsWith(matchValue),
			"KEY_SUFFIX" => pair.Key.EndsWith(matchValue),
			"VAL_CONTAINS" => (pair.Value ?? "").Contains(matchValue),
			"VAL_EQ" => pair.Value == matchValue,
			"VAL_NE" => pair.Value != matchValue,
			_ => false,
		};
	}

	static object[] ReadSqlParameters(IReadOnlyList<AExpression> arguments, ModernExpressionContext context, int startIndex)
	{
		if (arguments.Count <= startIndex)
			return Array.Empty<object>();
		var values = new object[arguments.Count - startIndex];
		for (int i = startIndex; i < arguments.Count; i++)
		{
			var argument = arguments[i];
			if (argument == null)
				values[i - startIndex] = null;
			else if (argument.IsString)
				values[i - startIndex] = argument.GetStrValue(context);
			else if (argument.IsFloat)
				values[i - startIndex] = argument.GetFloatValue(context);
			else
				values[i - startIndex] = argument.GetIntValue(context);
		}
		return values;
	}

	static bool HasWideCharacters(string value)
	{
		return !string.IsNullOrEmpty(value) && value.Length < LangManager.GetStrlenLang(value);
	}

	static bool IsEraNumeric(string value)
	{
		if (string.IsNullOrEmpty(value) || HasWideCharacters(value))
			return false;
		int index = 0;
		if (value[index] == '+' || value[index] == '-')
		{
			index++;
			if (index >= value.Length || !char.IsDigit(value[index]))
				return false;
		}
		while (index < value.Length && char.IsDigit(value[index]))
			index++;
		if (index == value.Length)
			return true;
		if (value[index] != '.')
			return false;
		index++;
		while (index < value.Length)
		{
			if (!char.IsDigit(value[index]))
				return false;
			index++;
		}
		return true;
	}

	static bool TryParseEraInteger(string value, out long parsed)
	{
		parsed = 0;
		if (!IsEraNumeric(value))
			return false;
		int dotIndex = value.IndexOf('.');
		string integerPart = dotIndex >= 0 ? value.Substring(0, dotIndex) : value;
		return long.TryParse(integerPart, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed);
	}

	static ModernVariableTerm GetWholeArrayArgument(AExpression expression, string functionName)
	{
		if (expression is not ModernVariableTerm term)
			throw new InvalidOperationException($"{functionName} needs an array variable as the first argument.");
		if (term.Identifier.Dimension != VariableDimension.Array1D)
			throw new InvalidOperationException($"{functionName} only supports one-dimensional arrays in the modern mobile core.");
		return term;
	}

	static ModernVariableTerm GetSortableWholeArrayArgument(AExpression expression, string functionName)
	{
		var term = GetWholeArrayArgument(expression, functionName);
		if (term.Identifier.IsConst)
			throw new InvalidOperationException($"{functionName} cannot sort a const variable.");
		return term;
	}

	static int[] GetSortedIndicesUntilDefault(ModernVariableTerm term, ModernExpressionContext context, bool ascending, int fixedLength)
	{
		int length = GetVariableArrayLength(term.Identifier, context);
		if (fixedLength > 0)
			length = Math.Min(length, fixedLength);
		else if (fixedLength == -1)
			length = GetLengthUntilDefault(term.Identifier, context, length);

		var indices = Enumerable.Range(0, length).ToArray();
		if (term.Identifier.IsString)
		{
			Array.Sort(indices, (a, b) =>
			{
				string left = term.Identifier.GetStrValue(context, new long[] { a }) ?? "";
				string right = term.Identifier.GetStrValue(context, new long[] { b }) ?? "";
				return ascending
					? string.Compare(left, right, StringComparison.Ordinal)
					: string.Compare(right, left, StringComparison.Ordinal);
			});
			return indices;
		}

		if (term.Identifier.IsFloat)
		{
			Array.Sort(indices, (a, b) =>
			{
				double left = term.Identifier.GetFloatValue(context, new long[] { a });
				double right = term.Identifier.GetFloatValue(context, new long[] { b });
				return ascending ? left.CompareTo(right) : right.CompareTo(left);
			});
			return indices;
		}

		Array.Sort(indices, (a, b) =>
		{
			long left = term.Identifier.GetIntValue(context, new long[] { a });
			long right = term.Identifier.GetIntValue(context, new long[] { b });
			return ascending ? left.CompareTo(right) : right.CompareTo(left);
		});
		return indices;
	}

	static int GetLengthUntilDefault(ModernVariableToken token, ModernExpressionContext context, int maxLength)
	{
		for (int i = 0; i < maxLength; i++)
		{
			if (token.IsString)
			{
				if (string.IsNullOrEmpty(token.GetStrValue(context, new long[] { i })))
					return i;
			}
			else if (token.IsFloat)
			{
				if (token.GetFloatValue(context, new long[] { i }).Equals(0.0d))
					return i;
			}
			else if (token.GetIntValue(context, new long[] { i }) == 0)
			{
				return i;
			}
		}
		return maxLength;
	}

	static bool ApplySortToOneDimensionalArray(ModernVariableTerm term, ModernExpressionContext context, IReadOnlyList<int> sortedIndices)
	{
		int length = GetVariableArrayLength(term.Identifier, context);
		if (length < sortedIndices.Count)
			return false;

		if (term.Identifier.IsString)
		{
			var clone = new string[length];
			for (int i = 0; i < length; i++)
				clone[i] = term.Identifier.GetStrValue(context, new long[] { i }) ?? "";
			for (int i = 0; i < sortedIndices.Count; i++)
				term.Identifier.SetValue(clone[sortedIndices[i]], context, new long[] { i });
			return true;
		}

		if (term.Identifier.IsFloat)
		{
			var clone = new double[length];
			for (int i = 0; i < length; i++)
				clone[i] = term.Identifier.GetFloatValue(context, new long[] { i });
			for (int i = 0; i < sortedIndices.Count; i++)
				term.Identifier.SetValue(clone[sortedIndices[i]], context, new long[] { i });
			return true;
		}

		var longClone = new long[length];
		for (int i = 0; i < length; i++)
			longClone[i] = term.Identifier.GetIntValue(context, new long[] { i });
		for (int i = 0; i < sortedIndices.Count; i++)
			term.Identifier.SetValue(longClone[sortedIndices[i]], context, new long[] { i });
		return true;
	}

	static (long Start, long End) GetArrayRange(
		ModernVariableToken token,
		ModernExpressionContext context,
		IReadOnlyList<AExpression> arguments,
		int startArgumentIndex)
	{
		long start = arguments.Count > startArgumentIndex ? arguments[startArgumentIndex].GetIntValue(context) : 0;
		long end = arguments.Count > startArgumentIndex + 1
			? arguments[startArgumentIndex + 1].GetIntValue(context)
			: GetVariableArrayLength(token, context);
		long length = GetVariableArrayLength(token, context);
		if (start < 0 || end < 0 || start > end || end > length)
			throw new IndexOutOfRangeException($"{token.Name}: invalid array range {start}..{end}.");
		return (start, end);
	}

	static (long Start, long End) ValidateArrayRange(ModernVariableToken token, ModernExpressionContext context, long start, long end)
	{
		long length = GetVariableArrayLength(token, context);
		if (start < 0 || end < 0 || start > end || end > length)
			throw new IndexOutOfRangeException($"{token.Name}: invalid array range {start}..{end}.");
		return (start, end);
	}

	static int GetCharacterCount()
	{
		var data = GlobalStatic.VariableData ?? GlobalStatic.VEvaluator?.VariableData;
		if (data == null)
			throw new InvalidOperationException("Legacy character data is required for character functions.");
		return data.CharacterList.Count;
	}

	static (long Start, long End) GetCharacterArrayRange(
		ModernExpressionContext context,
		IReadOnlyList<AExpression> arguments,
		int startArgumentIndex,
		string functionName)
	{
		long start = arguments.Count > startArgumentIndex ? arguments[startArgumentIndex].GetIntValue(context) : 0;
		long end = arguments.Count > startArgumentIndex + 1
			? arguments[startArgumentIndex + 1].GetIntValue(context)
			: GetCharacterCount();
		return ValidateCharacterRange(start, end, functionName);
	}

	static (long Start, long End) ValidateCharacterRange(long start, long end, string functionName)
	{
		int count = GetCharacterCount();
		if (start < 0 || end < 0 || start > end || end > count)
			throw new IndexOutOfRangeException($"{functionName}: invalid character range {start}..{end}.");
		return (start, end);
	}

	static long[] BuildCharaArguments(ModernVariableTerm term, ModernExpressionContext context, long charaIndex)
	{
		var source = term.GetArgumentValues(context);
		if (source.Length == 0)
			throw new InvalidOperationException($"{term.Identifier.Name} needs a character index.");
		var arguments = new long[source.Length];
		Array.Copy(source, arguments, source.Length);
		arguments[0] = charaIndex;
		return arguments;
	}

	static void RequireNonEmptyRange(long start, long end, string functionName)
	{
		if (start >= end)
			throw new InvalidOperationException($"{functionName} needs at least one array element.");
	}

	static bool ExpressionEqualsArrayValue(
		ModernVariableToken token,
		ModernExpressionContext context,
		long index,
		AExpression expression)
	{
		if (token.IsString)
			return string.Equals(token.GetStrValue(context, new[] { index }), expression.GetStrValue(context), StringComparison.Ordinal);
		if (token.IsFloat)
			return token.GetFloatValue(context, new[] { index }).Equals(ExpressionToDouble(expression, context));
		return token.GetIntValue(context, new[] { index }) == expression.GetIntValue(context);
	}

	static bool ExpressionEqualsCharaValue(
		ModernVariableTerm term,
		ModernExpressionContext context,
		long charaIndex,
		AExpression expression)
	{
		long[] arguments = BuildCharaArguments(term, context, charaIndex);
		if (term.Identifier.IsString)
		{
			string left = term.Identifier.GetStrValue(context, arguments);
			string right = expression.GetStrValue(context);
			return string.Equals(left, right, StringComparison.Ordinal)
				|| (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right));
		}
		if (term.Identifier.IsFloat)
			return term.Identifier.GetFloatValue(context, arguments).Equals(ExpressionToDouble(expression, context));
		return term.Identifier.GetIntValue(context, arguments) == expression.GetIntValue(context);
	}

	static long FindCharacterValue(
		ModernVariableTerm term,
		ModernExpressionContext context,
		long target,
		long start,
		long end,
		bool isLast)
	{
		if (isLast)
		{
			for (long i = end - 1; i >= start; i--)
			{
				if (term.Identifier.GetIntValue(context, BuildCharaArguments(term, context, i)) == target)
					return i;
			}
			return -1;
		}

		for (long i = start; i < end; i++)
		{
			if (term.Identifier.GetIntValue(context, BuildCharaArguments(term, context, i)) == target)
				return i;
		}
		return -1;
	}

	static long FindCharacterValue(
		ModernVariableTerm term,
		ModernExpressionContext context,
		string target,
		long start,
		long end,
		bool isLast)
	{
		if (isLast)
		{
			for (long i = end - 1; i >= start; i--)
			{
				if (string.Equals(term.Identifier.GetStrValue(context, BuildCharaArguments(term, context, i)), target, StringComparison.Ordinal))
					return i;
			}
			return -1;
		}

		for (long i = start; i < end; i++)
		{
			if (string.Equals(term.Identifier.GetStrValue(context, BuildCharaArguments(term, context, i)), target, StringComparison.Ordinal))
				return i;
		}
		return -1;
	}

	static bool ExpressionEquals(AExpression left, AExpression right, ModernExpressionContext context)
	{
		if (left.IsString || right.IsString)
			return string.Equals(left.GetStrValue(context), right.GetStrValue(context), StringComparison.Ordinal);
		if (left.IsFloat || right.IsFloat)
			return ExpressionToDouble(left, context).Equals(ExpressionToDouble(right, context));
		return left.GetIntValue(context) == right.GetIntValue(context);
	}

	static string GetArrayValueAsString(ModernVariableToken token, ModernExpressionContext context, long index)
	{
		if (token.IsString)
			return token.GetStrValue(context, new[] { index }) ?? "";
		if (token.IsFloat)
			return token.GetFloatValue(context, new[] { index }).ToString(CultureInfo.InvariantCulture);
		return token.GetIntValue(context, new[] { index }).ToString(CultureInfo.InvariantCulture);
	}

	static long FindNumericElement(ModernVariableToken token, ModernExpressionContext context, long target, long start, long end, bool isLast)
	{
		if (isLast)
		{
			for (long i = end - 1; i >= start; i--)
			{
				if (token.GetIntValue(context, new[] { i }) == target)
					return i;
			}
			return -1;
		}

		for (long i = start; i < end; i++)
		{
			if (token.GetIntValue(context, new[] { i }) == target)
				return i;
		}
		return -1;
	}

	static long FindNumericElement(ModernVariableToken token, ModernExpressionContext context, double target, long start, long end, bool isLast)
	{
		if (isLast)
		{
			for (long i = end - 1; i >= start; i--)
			{
				if (token.GetFloatValue(context, new[] { i }).Equals(target))
					return i;
			}
			return -1;
		}

		for (long i = start; i < end; i++)
		{
			if (token.GetFloatValue(context, new[] { i }).Equals(target))
				return i;
		}
		return -1;
	}

	static long FindStringElement(
		ModernVariableToken token,
		ModernExpressionContext context,
		string pattern,
		long start,
		long end,
		bool exact,
		bool isLast)
	{
		Regex regex;
		try
		{
			regex = new Regex(pattern ?? "");
		}
		catch (ArgumentException e)
		{
			throw new ArgumentException($"Invalid regex pattern for string element search: {e.Message}", e);
		}

		if (isLast)
		{
			for (long i = end - 1; i >= start; i--)
			{
				if (StringElementMatches(token.GetStrValue(context, new[] { i }) ?? "", regex, exact))
					return i;
			}
			return -1;
		}

		for (long i = start; i < end; i++)
		{
			if (StringElementMatches(token.GetStrValue(context, new[] { i }) ?? "", regex, exact))
				return i;
		}
		return -1;
	}

	static bool StringElementMatches(string value, Regex regex, bool exact)
	{
		if (!exact)
			return regex.IsMatch(value);
		Match match = regex.Match(value);
		return match.Success && match.Length == value.Length;
	}

	static ModernVariableTerm GetIntegerVariableArgument(AExpression expression, string functionName)
	{
		if (expression is not ModernVariableTerm term || !term.Identifier.IsInteger)
			throw new InvalidOperationException($"{functionName} needs an integer variable as the first argument.");
		if (term.Identifier.Dimension != VariableDimension.Array1D)
			throw new InvalidOperationException($"{functionName} needs a whole integer array variable.");
		return term;
	}

	static void SetVariableValue(ModernVariableTerm term, AExpression value, ModernExpressionContext context)
	{
		SetVariableValue(term.Identifier, context, 0, value);
	}

	static void SetVariableValue(ModernVariableToken token, ModernExpressionContext context, long index, AExpression value)
	{
		if (token.IsString)
		{
			if (!value.IsString)
				throw new InvalidOperationException($"{token.Name} cannot receive a numeric value.");
			token.SetValue(value.GetStrValue(context) ?? "", context, token.Dimension == VariableDimension.Scalar ? Array.Empty<long>() : new[] { index });
		}
		else if (token.IsFloat)
		{
			if (value.IsString)
				throw new InvalidOperationException($"{token.Name} cannot receive a string value.");
			token.SetValue(ExpressionToDouble(value, context), context, token.Dimension == VariableDimension.Scalar ? Array.Empty<long>() : new[] { index });
		}
		else
		{
			if (!value.IsInteger)
				throw new InvalidOperationException($"{token.Name} cannot receive a non-integer value.");
			token.SetValue(value.GetIntValue(context), context, token.Dimension == VariableDimension.Scalar ? Array.Empty<long>() : new[] { index });
		}
	}

	static void BitSet(ModernVariableTerm target, ModernExpressionContext context, long index, long value, long length)
	{
		if (length <= 0)
			return;
		long slots = GetIntegerSlotLength(target.Identifier, context);
		long bitSize = slots * 64;
		for (long i = 0; i < length; i++)
		{
			long bitIndex = index + i;
			if (bitIndex < 0)
				continue;
			if (bitIndex >= bitSize)
				break;
			long slot = bitIndex / 64;
			int bit = (int)(bitIndex % 64);
			long current = target.Identifier.GetIntValue(context, new[] { slot });
			long next = value != 0 ? current | (1L << bit) : current & ~(1L << bit);
			target.Identifier.SetValue(next, context, new[] { slot });
		}
	}

	static long BitGet(ModernVariableTerm target, ModernExpressionContext context, long index)
	{
		long slots = GetIntegerSlotLength(target.Identifier, context);
		if (index < 0 || index >= slots * 64)
			return -1;
		long slot = index / 64;
		int bit = (int)(index % 64);
		long value = target.Identifier.GetIntValue(context, new[] { slot });
		return (value & (1L << bit)) != 0 ? 1 : 0;
	}

	static int GetIntegerSlotLength(ModernVariableToken token, ModernExpressionContext context)
	{
		object array = token.GetArray(context);
		return array switch
		{
			long[] dense => dense.Length,
			SparseArray<long> sparse => sparse.Length,
			_ => token.GetLength(),
		};
	}

	static bool TryGetVariable(ModernExpressionContext context, IReadOnlyList<AExpression> arguments, out ModernVariableTerm term)
	{
		try
		{
			term = ParseVariable(arguments[0].GetStrValue(context), context);
			return true;
		}
		catch
		{
			if (arguments.Count > 1)
			{
				term = null;
				return false;
			}
			throw;
		}
	}

	static ModernVariableTerm ParseVariable(string source, ModernExpressionContext context)
	{
		var evaluator = context?.VariableEvaluator ?? throw new InvalidOperationException("No variable evaluator is available.");
		var expression = new ModernExpressionParser(evaluator).Parse(source);
		if (expression is ModernVariableTerm term)
			return term;
		throw new FormatException($"{source} is not a variable expression.");
	}

	static AExpression DefaultOrThrow(IReadOnlyList<AExpression> arguments, ModernExpressionContext context, string message)
	{
		if (arguments.Count > 1)
			return arguments[1];
		throw new InvalidOperationException(message);
	}
}
