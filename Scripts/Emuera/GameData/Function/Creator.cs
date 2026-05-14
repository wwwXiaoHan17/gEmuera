using System;
using System.Collections.Generic;
using System.Text;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameProc;


namespace MinorShift.Emuera.GameData.Function
{
	internal static partial class FunctionMethodCreator
	{
		static FunctionMethodCreator()
		{
            methodList = new Dictionary<string, FunctionMethod>
            {
                //キャラクタデータ系
                ["GETCHARA"] = new GetcharaMethod(),
                ["GETSPCHARA"] = new GetspcharaMethod(),
                ["CSVNAME"] = new CsvStrDataMethod(CharacterStrData.NAME),
                ["CSVCALLNAME"] = new CsvStrDataMethod(CharacterStrData.CALLNAME),
                ["CSVNICKNAME"] = new CsvStrDataMethod(CharacterStrData.NICKNAME),
                ["CSVMASTERNAME"] = new CsvStrDataMethod(CharacterStrData.MASTERNAME),
                ["CSVCSTR"] = new CsvcstrMethod(),
                ["CSVBASE"] = new CsvDataMethod(CharacterIntData.BASE),
                ["CSVABL"] = new CsvDataMethod(CharacterIntData.ABL),
                ["CSVMARK"] = new CsvDataMethod(CharacterIntData.MARK),
                ["CSVEXP"] = new CsvDataMethod(CharacterIntData.EXP),
                ["CSVRELATION"] = new CsvDataMethod(CharacterIntData.RELATION),
                ["CSVTALENT"] = new CsvDataMethod(CharacterIntData.TALENT),
                ["CSVCFLAG"] = new CsvDataMethod(CharacterIntData.CFLAG),
                ["CSVEQUIP"] = new CsvDataMethod(CharacterIntData.EQUIP),
                ["CSVJUEL"] = new CsvDataMethod(CharacterIntData.JUEL),
                ["FINDCHARA"] = new FindcharaMethod(false),
                ["FINDLASTCHARA"] = new FindcharaMethod(true),
                ["EXISTCSV"] = new ExistCsvMethod(),

                //汎用処理系
                ["VARSIZE"] = new VarsizeMethod(),
                ["CHKFONT"] = new CheckfontMethod(),
                ["CHKDATA"] = new CheckdataMethod(EraSaveFileType.Normal),
                ["ISSKIP"] = new IsSkipMethod(),
                ["MOUSESKIP"] = new MesSkipMethod(true),
                ["MESSKIP"] = new MesSkipMethod(false),
                ["GETCOLOR"] = new GetColorMethod(false),
                ["GETDEFCOLOR"] = new GetColorMethod(true),
                ["GETFOCUSCOLOR"] = new GetFocusColorMethod(),
                ["GETBGCOLOR"] = new GetBGColorMethod(false),
                ["GETDEFBGCOLOR"] = new GetBGColorMethod(true),
                ["GETSTYLE"] = new GetStyleMethod(),
                ["GETFONT"] = new GetFontMethod(),
                ["BARSTR"] = new BarStringMethod(),
                ["CURRENTALIGN"] = new CurrentAlignMethod(),
                ["CURRENTREDRAW"] = new CurrentRedrawMethod(),
                ["COLOR_FROMNAME"] = new ColorFromNameMethod(),
                ["COLOR_FROMRGB"] = new ColorFromRGBMethod(),

                //TODO:1810
                //methodList["CHKVARDATA"] = new CheckdataStrMethod(EraSaveFileType.Var);
                ["CHKCHARADATA"] = new CheckdataStrMethod(EraSaveFileType.CharVar),
                //methodList["CHKGLOBALDATA"] = new CheckdataMethod(EraSaveFileType.Global);
                //methodList["FIND_VARDATA"] = new FindFilesMethod(EraSaveFileType.Var);
                ["FIND_CHARADATA"] = new FindFilesMethod(EraSaveFileType.CharVar),

                //定数取得
                ["MONEYSTR"] = new MoneyStrMethod(),
                ["PRINTCPERLINE"] = new GetPrintCPerLineMethod(),
                ["PRINTCLENGTH"] = new PrintCLengthMethod(),
                ["SAVENOS"] = new GetSaveNosMethod(),
                ["GETTIME"] = new GettimeMethod(),
                ["GETTIMES"] = new GettimesMethod(),
                ["GETMILLISECOND"] = new GetmsMethod(),
                ["GETSECOND"] = new GetSecondMethod(),

                //数学関数
                ["RAND"] = new RandMethod(),
                ["MIN"] = new MaxMethod(false),
                ["MAX"] = new MaxMethod(true),
                ["ABS"] = new AbsMethod(),
                ["POWER"] = new PowerMethod(),
                ["SQRT"] = new SqrtMethod(),
                ["CBRT"] = new CbrtMethod(),
                ["LOG"] = new LogMethod(),
                ["LOG10"] = new LogMethod(10.0d),
                ["EXPONENT"] = new ExpMethod(),
                ["SIGN"] = new SignMethod(),
                ["LIMIT"] = new GetLimitMethod(),

                //変数操作系
                ["SUMARRAY"] = new SumArrayMethod(),
                ["SUMCARRAY"] = new SumArrayMethod(true),
                ["MATCH"] = new MatchMethod(),
                ["CMATCH"] = new MatchMethod(true),
                ["GROUPMATCH"] = new GroupMatchMethod(),
                ["NOSAMES"] = new NosamesMethod(),
                ["ALLSAMES"] = new AllsamesMethod(),
                ["MAXARRAY"] = new MaxArrayMethod(),
                ["MAXCARRAY"] = new MaxArrayMethod(true),
                ["MINARRAY"] = new MaxArrayMethod(false, false),
                ["MINCARRAY"] = new MaxArrayMethod(true, false),
                ["GETBIT"] = new GetbitMethod(),
                ["GETNUM"] = new GetnumMethod(),
                ["GETPALAMLV"] = new GetPalamLVMethod(),
                ["GETEXPLV"] = new GetExpLVMethod(),
                ["FINDELEMENT"] = new FindElementMethod(false),
                ["FINDLASTELEMENT"] = new FindElementMethod(true),
                ["INRANGE"] = new InRangeMethod(),
                ["INRANGEARRAY"] = new InRangeArrayMethod(),
                ["INRANGECARRAY"] = new InRangeArrayMethod(true),
                ["GETNUMB"] = new GetnumMethod(),

                ["ARRAYMSORT"] = new ArrayMultiSortMethod(),

                //文字列操作系
                ["STRLENS"] = new StrlenMethod(),
                ["STRLENSU"] = new StrlenuMethod(),
                ["SUBSTRING"] = new SubstringMethod(),
                ["SUBSTRINGU"] = new SubstringuMethod(),
                ["STRFIND"] = new StrfindMethod(false),
                ["STRFINDU"] = new StrfindMethod(true),
                ["STRCOUNT"] = new StrCountMethod(),
                ["TOSTR"] = new ToStrMethod(),
                ["TOINT"] = new ToIntMethod(),
                ["TOUPPER"] = new StrChangeStyleMethod(StrFormType.Upper),
                ["TOLOWER"] = new StrChangeStyleMethod(StrFormType.Lower),
                ["TOHALF"] = new StrChangeStyleMethod(StrFormType.Half),
                ["TOFULL"] = new StrChangeStyleMethod(StrFormType.Full),
                ["LINEISEMPTY"] = new LineIsEmptyMethod(),
                ["REPLACE"] = new ReplaceMethod(),
                ["UNICODE"] = new UnicodeMethod(),
                ["UNICODEBYTE"] = new UnicodeByteMethod(),
                ["CONVERT"] = new ConvertIntMethod(),
                ["ISNUMERIC"] = new IsNumericMethod(),
                ["ESCAPE"] = new EscapeMethod(),
                ["ENCODETOUNI"] = new EncodeToUniMethod(),
                ["CHARATU"] = new CharAtMethod(),
                ["GETLINESTR"] = new GetLineStrMethod(),
                ["STRFORM"] = new StrFormMethod(),
                ["STRJOIN"] = new JoinMethod(),

                ["GETCONFIG"] = new GetConfigMethod(true),
                ["GETCONFIGS"] = new GetConfigMethod(false),

                //html系
                ["HTML_GETPRINTEDSTR"] = new HtmlGetPrintedStrMethod(),
                ["HTML_POPPRINTINGSTR"] = new HtmlPopPrintingStrMethod(),
                ["HTML_TOPLAINTEXT"] = new HtmlToPlainTextMethod(),
                ["HTML_ESCAPE"] = new HtmlEscapeMethod(),


                //画像処理系
                ["SPRITECREATED"] = new SpriteStateMethod(),
                ["SPRITEWIDTH"] = new SpriteStateMethod(),
                ["SPRITEHEIGHT"] = new SpriteStateMethod(),
                ["SPRITEMOVE"] = new SpriteSetPosMethod(),
                ["SPRITESETPOS"] = new SpriteSetPosMethod(),
                ["SPRITEPOSX"] = new SpriteStateMethod(),
                ["SPRITEPOSY"] = new SpriteStateMethod(),

                ["CLIENTWIDTH"] = new ClientSizeMethod(),
                ["CLIENTHEIGHT"] = new ClientSizeMethod(),

                ["GETKEY"] = new GetKeyStateMethod(),
                ["GETKEYTRIGGERED"] = new GetKeyStateMethod(),
                ["MOUSEX"] = new MousePosMethod(),
                ["MOUSEY"] = new MousePosMethod(),
                ["ISACTIVE"] = new IsActiveMethod(),
                ["SAVETEXT"] = new SaveTextMethod(),
                ["LOADTEXT"] = new LoadTextMethod(),

                ["GCREATED"] = new GraphicsStateMethod(),// ("GCREATED");
                ["GWIDTH"] = new GraphicsStateMethod(),//("GWIDTH");
                ["GHEIGHT"] = new GraphicsStateMethod(),//("GHEIGHT");
                ["GGETCOLOR"] = new GraphicsGetColorMethod(),
                ["SPRITEGETCOLOR"] = new SpriteGetColorMethod(),

                ["GCREATE"] = new GraphicsCreateMethod(),
                ["GCREATEFROMFILE"] = new GraphicsCreateFromFileMethod(),
                ["GDISPOSE"] = new GraphicsDisposeMethod(),
                ["GCLEAR"] = new GraphicsClearMethod(),
                ["GFILLRECTANGLE"] = new GraphicsFillRectangleMethod(),
                ["GDRAWSPRITE"] = new GraphicsDrawSpriteMethod(),
                ["GSETCOLOR"] = new GraphicsSetColorMethod(),
                ["GDRAWG"] = new GraphicsDrawGMethod(),
                ["GDRAWGWITHMASK"] = new GraphicsDrawGWithMaskMethod(),

                ["GSETBRUSH"] = new GraphicsSetBrushMethod(),
                ["GSETFONT"] = new GraphicsSetFontMethod(),
                ["GSETPEN"] = new GraphicsSetPenMethod(),

                ["SPRITECREATE"] = new SpriteCreateMethod(),
                ["SPRITEDISPOSE"] = new SpriteDisposeMethod(),

                ["CBGSETG"] = new CBGSetGraphicsMethod(),
                ["CBGSETSPRITE"] = new CBGSetCIMGMethod(),
                ["CBGCLEAR"] = new CBGClearMethod(),

                ["CBGCLEARBUTTON"] = new CBGClearButtonMethod(),
                ["CBGREMOVERANGE"] = new CBGRemoveRangeMethod(),
                ["CBGREMOVEBMAP"] = new CBGRemoveBMapMethod(),
                ["CBGSETBMAPG"] = new CBGSetBMapGMethod(),
                ["CBGSETBUTTONSPRITE"] = new CBGSETButtonSpriteMethod(),

                ["GSAVE"] = new GraphicsSaveMethod(),
                ["GLOAD"] = new GraphicsLoadMethod(),


                ["SPRITEANIMECREATE"] = new SpriteAnimeCreateMethod(),
                ["SPRITEANIMEADDFRAME"] = new SpriteAnimeAddFrameMethod(),
                ["SETANIMETIMER"] = new SetAnimeTimerMethod()
            };

            if (Program.UseLegacySnakeCompatibilityFallbacks)
                AddSnakeCompatibilityMethods(methodList);

            //1823 自分の関数名を知っていた方が何かと便利なので覚えさせることにした
            foreach (var pair in methodList)
				pair.Value.SetMethodName(pair.Key);
        }

		private static void AddSnakeCompatibilityMethods(Dictionary<string, FunctionMethod> methods)
		{
			methods["GETANIMETIMER"] = new GetAnimeTimerMethod();
			methods["HTML_STRINGLEN"] = new HtmlStringLenMethod();
			methods["HTML_SUBSTRING"] = new HtmlSubstringMethod();
			methods["HTML_STRINGLINES"] = new HtmlStringLinesMethod();
			methods["EXISTSOUND"] = new ExistSoundMethod();
			methods["EXISTSIMAGELAYER"] = new ExistsImageLayerMethod();
			methods["GETSOUNDORBGMINFO"] = new GetSoundOrBgmInfoMethod();
			methods["ISPLAYINGSOUND"] = new IsPlayingSoundMethod();
			methods["SOUNDCONTROL"] = new SoundControlMethod();
			methods["ISPLAYINGBGM"] = new IsPlayingBgmMethod();
			methods["BGMCONTROL"] = new BgmControlMethod();
			methods["GET_TEXT_DRAWING_MODE"] = new GetTextDrawingModeMethod();
			methods["GET_SKIA_QUALITY"] = new GetSkiaQualityMethod();
			methods["SIN"] = new SnakeTrigMethod("SIN");
			methods["COS"] = new SnakeTrigMethod("COS");
			methods["TAN"] = new SnakeTrigMethod("TAN");
			methods["ASIN"] = new SnakeTrigMethod("ASIN");
			methods["ACOS"] = new SnakeTrigMethod("ACOS");
			methods["ATAN"] = new SnakeTrigMethod("ATAN");
			methods["FLOOR"] = new SnakeUnaryMathMethod("FLOOR");
			methods["CEIL"] = new SnakeUnaryMathMethod("CEIL");
			methods["ROUND"] = new SnakeUnaryMathMethod("ROUND");
			methods["ARGLEN"] = new SnakeArgLenMethod();
			methods["TOFLOAT"] = new ToIntMethod();
			methods["TOSTRF"] = new ToStrMethod();
			methods["UNCHECKED_ADD"] = new SnakeUncheckedMathMethod("ADD");
			methods["UNCHECKED_SUB"] = new SnakeUncheckedMathMethod("SUB");
			methods["UNCHECKED_MUL"] = new SnakeUncheckedMathMethod("MUL");
			methods["UNCHECKED_NEG"] = new SnakeUncheckedMathMethod("NEG");
			methods["BITSET"] = new SnakeBitMethod("SET");
			methods["BITGET"] = new SnakeBitMethod("GET");
			methods["BITTOGGLE"] = new SnakeBitMethod("TOGGLE");
			methods["BITINDEXOFFIRST"] = new SnakeBitMethod("INDEX");
			methods["SQL_CONNECTION_OPEN"] = new SnakeSqlIntMethod("CONNECTION_OPEN");
			methods["SQL_CONNECT"] = new SnakeSqlIntMethod("CONNECT");
			methods["SQL_DISCONNECT"] = new SnakeSqlIntMethod("DISCONNECT");
			methods["SQL_EXECUTE_NONQUERY"] = new SnakeSqlIntMethod("EXECUTE_NONQUERY");
			methods["SQL_EXECUTE_READER"] = new SnakeSqlIntMethod("EXECUTE_READER");
			methods["SQL_READER_READ"] = new SnakeSqlIntMethod("READER_READ");
			methods["SQL_READER_GET_LONG"] = new SnakeSqlIntMethod("READER_GET_LONG");
			methods["SQL_READER_GET_FLOAT"] = new SnakeSqlIntMethod("READER_GET_FLOAT");
			methods["SQL_READER_ISNULL"] = new SnakeSqlIntMethod("READER_ISNULL");
			methods["SQL_READER_CLOSE"] = new SnakeSqlIntMethod("READER_CLOSE");
			methods["SQL_EXECUTE_SCALAR_LONG"] = new SnakeSqlIntMethod("EXECUTE_SCALAR_LONG");
			methods["SQL_EXECUTE_SCALAR_FLOAT"] = new SnakeSqlIntMethod("EXECUTE_SCALAR_FLOAT");
			methods["SQL_P_EXECUTE_NONQUERY"] = new SnakeSqlIntMethod("P_EXECUTE_NONQUERY");
			methods["SQL_P_EXECUTE_READER"] = new SnakeSqlIntMethod("P_EXECUTE_READER");
			methods["SQL_P_EXECUTE_SCALAR_LONG"] = new SnakeSqlIntMethod("P_EXECUTE_SCALAR_LONG");
			methods["SQL_P_EXECUTE_SCALAR_FLOAT"] = new SnakeSqlIntMethod("P_EXECUTE_SCALAR_FLOAT");
			methods["SQL_ESCAPE"] = new SnakeSqlStringMethod("ESCAPE");
			methods["SQL_EXECUTE_SCALAR_STRING"] = new SnakeSqlStringMethod("EXECUTE_SCALAR_STRING");
			methods["SQL_P_EXECUTE_SCALAR_STRING"] = new SnakeSqlStringMethod("P_EXECUTE_SCALAR_STRING");
			methods["SQL_READER_GET_STRING"] = new SnakeSqlStringMethod("READER_GET_STRING");

			string[] intFallbacks =
			{
				"ACOS", "ARGLEN", "ARRAYMSORTEX", "ASIN", "ATAN", "CEIL", "CLEARMEMORY", "COS",
				"DT_CELL_GET", "DT_CELL_GETF", "DT_CELL_ISNULL", "DT_CELL_SET", "DT_CELL_SETF",
				"DT_CLEAR", "DT_COLUMN_ADD", "DT_COLUMN_EXIST", "DT_COLUMN_LENGTH", "DT_COLUMN_REMOVE",
				"DT_CREATE", "DT_EXIST", "DT_FROMXML", "DT_NOCASE", "DT_RELEASE", "DT_ROW_ADD",
				"DT_ROW_LENGTH", "DT_ROW_REMOVE", "DT_ROW_SET", "DT_SELECT", "DT_TOXML",
				"ENUMFUNCBEGINSWITH", "ENUMFUNCENDSWITH", "ENUMFUNCWITH", "ENUMMACROBEGINSWITH",
				"ENUMMACROENDSWITH", "ENUMMACROWITH", "ENUMVARBEGINSWITH", "ENUMVARENDSWITH",
				"ENUMVARWITH", "EVAL", "EVALF", "EXISTFILE", "EXISTFUNCTION", "EXISTMETH",
				"EXISTVAR", "FLOOR", "FLOWINPUT", "FLOWINPUTS", "G_POLYGON_DRAW", "G_POLYGON_FILL",
				"G_POLYGON_POINT_ADD", "G_POLYGON_POINT_CLEAR", "GDASHSTYLE", "GDRAWGWITHROTATE",
				"GDRAWLINE", "GDRAWTEXT", "GETCSVNOBYCALLNAME", "GETCSVNOBYMASTERNAME",
				"GETCSVNOBYNAME", "GETCSVNOBYNICKNAME", "GETMEMORYUSAGE", "GETMETH", "GETMETHF",
				"GGETBRUSH", "GGETFONTSIZE", "GGETFONTSTYLE", "GGETPEN", "GGETPENWIDTH",
				"GGETTEXTSIZE", "GROTATE", "HOTKEY_STATE", "HOTKEY_STATE_INIT", "ISDEFINED",
				"MAP_CLEAR", "MAP_CREATE", "MAP_EXIST", "MAP_FINDKEY", "MAP_FROMSTRING",
				"MAP_FROMXML", "MAP_HAS", "MAP_MERGE", "MAP_RELEASE", "MAP_REMOVE",
				"MAP_REMOVEIF", "MAP_SET", "MAP_SIZE", "MATCHALL", "MATCHALLEX", "MOUSEB",
				"MOVETEXTBOX", "OUTPUTLOG", "REGEXPMATCH", "RESUMETEXTBOX", "ROUND", "SETTEXTBOX",
				"SETVAR", "SIN", "SPRITECREATEFROMFILE", "SPRITEDISPOSEALL", "SQL_CONNECT",
				"SQL_CONNECTION_OPEN", "SQL_DISCONNECT", "SQL_EXECUTE_NONQUERY", "SQL_EXECUTE_READER",
				"SQL_EXECUTE_SCALAR_FLOAT", "SQL_EXECUTE_SCALAR_LONG",
				"SQL_EXPORT_DT_XML", "SQL_EXPORT_MAP_XML", "SQL_IMPORT_DT_XML", "SQL_IMPORT_MAP_XML",
				"SQL_IMPORT_XML_CUSTOM", "SQL_P_EXECUTE_NONQUERY", "SQL_P_EXECUTE_READER",
				"SQL_P_EXECUTE_SCALAR_FLOAT", "SQL_P_EXECUTE_SCALAR_LONG",
				"SQL_READER_CLOSE", "SQL_READER_GET_FLOAT", "SQL_READER_GET_LONG", "SQL_READER_ISNULL",
				"SQL_READER_READ", "TAN", "UNCHECKED_ADD", "UNCHECKED_MUL", "UNCHECKED_NEG",
				"UNCHECKED_SUB", "VARSETEX", "XML_ADDATTRIBUTE", "XML_ADDATTRIBUTE_BYNAME",
				"XML_ADDNODE", "XML_ADDNODE_BYNAME", "XML_DOCUMENT", "XML_EXIST", "XML_GET",
				"XML_GET_BYNAME", "XML_RELEASE", "XML_REMOVEATTRIBUTE", "XML_REMOVEATTRIBUTE_BYNAME",
				"XML_REMOVENODE", "XML_REMOVENODE_BYNAME", "XML_REPLACE", "XML_REPLACE_BYNAME",
				"XML_SET", "XML_SET_BYNAME"
			};
			foreach (string name in intFallbacks)
			{
				if (!methods.ContainsKey(name))
					methods[name] = new SnakeIntFallbackMethod();
			}

			string[] strFallbacks =
			{
				"DT_CELL_GETS", "DT_COLUMN_NAMES", "ENUMFILES", "ERDNAME", "EVALS", "GETDISPLAYLINE",
				"GETDOINGFUNCTION", "GETMETHS", "GETTEXTBOX", "GETVAR", "GETVARF", "GETVARS",
				"GGETFONT", "MAP_GET", "MAP_GETKEYS", "MAP_TOSTRING", "MAP_TOXML", "MAP_VALUES",
				"SQL_ESCAPE", "SQL_EXECUTE_SCALAR_STRING", "SQL_P_EXECUTE_SCALAR_STRING",
				"SQL_READER_GET_STRING", "TOSTRF", "XML_TOSTR"
			};
			foreach (string name in strFallbacks)
			{
				if (!methods.ContainsKey(name))
					methods[name] = new SnakeStringFallbackMethod();
			}
		}

		private static readonly Dictionary<string, FunctionMethod> methodList;
		public static Dictionary<string, FunctionMethod> GetMethodList()
		{
			return methodList;
		}
	}
}
