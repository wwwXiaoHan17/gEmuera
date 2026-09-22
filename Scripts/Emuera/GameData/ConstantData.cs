using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData.Variable;

namespace MinorShift.Emuera.GameData
{
	//難読化用属性。enum.ToString()やenum.Parse()を行うなら(Exclude=true)にすること。
	[global::System.Reflection.Obfuscation(Exclude = false)]
	internal enum CharacterStrData
	{
		NAME = 0,
		CALLNAME = 1,
		NICKNAME = 2,
		MASTERNAME = 3,
		CSTR = 4,
	}
	
	//難読化用属性。enum.ToString()やenum.Parse()を行うなら(Exclude=true)にすること。
	[global::System.Reflection.Obfuscation(Exclude = false)]
	internal enum CharacterIntData
	{
		BASE = 0,
		ABL = 1,
		TALENT = 2,
		MARK = 3,
		EXP = 4,
		RELATION = 5,
		CFLAG = 6,
		EQUIP = 7,
		JUEL = 8,
		
	}
	
	internal sealed partial class ConstantData
	{

		private const int ablIndex = (int)(VariableCode.ABLNAME & VariableCode.__LOWERCASE__);
		private const int expIndex = (int)(VariableCode.EXPNAME & VariableCode.__LOWERCASE__);
		private const int talentIndex = (int)(VariableCode.TALENTNAME & VariableCode.__LOWERCASE__);
		private const int paramIndex = (int)(VariableCode.PALAMNAME & VariableCode.__LOWERCASE__);
		private const int trainIndex = (int)(VariableCode.TRAINNAME & VariableCode.__LOWERCASE__);
		private const int markIndex = (int)(VariableCode.MARKNAME & VariableCode.__LOWERCASE__);
		private const int itemIndex = (int)(VariableCode.ITEMNAME & VariableCode.__LOWERCASE__);
		private const int baseIndex = (int)(VariableCode.BASENAME & VariableCode.__LOWERCASE__);
		private const int sourceIndex = (int)(VariableCode.SOURCENAME & VariableCode.__LOWERCASE__);
		private const int exIndex = (int)(VariableCode.EXNAME & VariableCode.__LOWERCASE__);
		private const int strIndex = (int)(VariableCode.__DUMMY_STR__ & VariableCode.__LOWERCASE__);
		private const int equipIndex = (int)(VariableCode.EQUIPNAME & VariableCode.__LOWERCASE__);
		private const int tequipIndex = (int)(VariableCode.TEQUIPNAME & VariableCode.__LOWERCASE__);
		private const int flagIndex = (int)(VariableCode.FLAGNAME & VariableCode.__LOWERCASE__);
		private const int tflagIndex = (int)(VariableCode.TFLAGNAME & VariableCode.__LOWERCASE__);
		private const int cflagIndex = (int)(VariableCode.CFLAGNAME & VariableCode.__LOWERCASE__);
		private const int tcvarIndex = (int)(VariableCode.TCVARNAME & VariableCode.__LOWERCASE__);
		private const int cstrIndex = (int)(VariableCode.CSTRNAME & VariableCode.__LOWERCASE__);
		private const int stainIndex = (int)(VariableCode.STAINNAME & VariableCode.__LOWERCASE__);
		private const int cdflag1Index = (int)(VariableCode.CDFLAGNAME1 & VariableCode.__LOWERCASE__);
		private const int cdflag2Index = (int)(VariableCode.CDFLAGNAME2 & VariableCode.__LOWERCASE__);
		private const int strnameIndex = (int)(VariableCode.STRNAME & VariableCode.__LOWERCASE__);
		private const int tstrnameIndex = (int)(VariableCode.TSTRNAME & VariableCode.__LOWERCASE__);
		private const int savestrnameIndex = (int)(VariableCode.SAVESTRNAME & VariableCode.__LOWERCASE__);
		private const int globalIndex = (int)(VariableCode.GLOBALNAME & VariableCode.__LOWERCASE__);
		private const int globalsIndex = (int)(VariableCode.GLOBALSNAME & VariableCode.__LOWERCASE__);
		private const int dayIndex = (int)(VariableCode.DAYNAME & VariableCode.__LOWERCASE__);
		private const int timeIndex = (int)(VariableCode.TIMENAME & VariableCode.__LOWERCASE__);
		private const int moneyIndex = (int)(VariableCode.MONEYNAME & VariableCode.__LOWERCASE__);
		private const int countNameCsv = (int)VariableCode.__COUNT_CSV_STRING_ARRAY_1D__;
		
		public int[] MaxDataList = new int[countNameCsv];
        readonly HashSet<VariableCode> changedCode = new HashSet<VariableCode>();
		
		public int[] VariableIntArrayLength;
		public int[] VariableStrArrayLength;
		public int[] VariableFloatArrayLength;
		public Int64[] VariableIntArray2DLength;
		public Int64[] VariableStrArray2DLength;
		public Int64[] VariableIntArray3DLength;
		public Int64[] VariableStrArray3DLength;
		public int[] CharacterIntArrayLength;
		public int[] CharacterStrArrayLength;
		public Int64[] CharacterIntArray2DLength;
		public Int64[] CharacterStrArray2DLength;

		//private readonly GameBase gamebase;
		private readonly string[][] names = new string[(int)VariableCode.__COUNT_CSV_STRING_ARRAY_1D__][];
		private readonly Dictionary<string, int>[] nameToIntDics = new Dictionary<string, int>[(int)VariableCode.__COUNT_CSV_STRING_ARRAY_1D__];
		private readonly Dictionary<string, int>[] aliases = new Dictionary<string, int>[(int)VariableCode.__COUNT_CSV_STRING_ARRAY_1D__];
		private readonly Dictionary<string, int> relationDic = new Dictionary<string, int>();
		private readonly Dictionary<string, Int64> nameToTemplateMap = new Dictionary<string, Int64>();
		private readonly Dictionary<string, Int64> nicknameToTemplateMap = new Dictionary<string, Int64>();
		private readonly Dictionary<string, Int64> callnameToTemplateMap = new Dictionary<string, Int64>();
		private readonly Dictionary<string, Int64> masternameToTemplateMap = new Dictionary<string, Int64>();

		// VarExt 名册：参考侧为默认（区分大小写）HashSet，仅大小写不同的名字视为两个
		public HashSet<string> GlobalSaveMaps { get; private set; } = [];
		public HashSet<string> SaveMaps { get; private set; } = [];
		public HashSet<string> GlobalSaveXmls { get; private set; } = [];
		public HashSet<string> SaveXmls { get; private set; } = [];
		public HashSet<string> GlobalSaveDTs { get; private set; } = [];
		public HashSet<string> SaveDTs { get; private set; } = [];
		public HashSet<string> StaticMaps { get; private set; } = [];
		public HashSet<string> StaticXmls { get; private set; } = [];
		public HashSet<string> StaticDTs { get; private set; } = [];

		public IReadOnlyDictionary<string, Int64> NameToTemplateMap { get { return nameToTemplateMap; } }
		public IReadOnlyDictionary<string, Int64> NicknameToTemplateMap { get { return nicknameToTemplateMap; } }
		public IReadOnlyDictionary<string, Int64> CallnameToTemplateMap { get { return callnameToTemplateMap; } }
		public IReadOnlyDictionary<string, Int64> MasternameToTemplateMap { get { return masternameToTemplateMap; } }

		public string[] GetCsvNameList(VariableCode code)
		{
			return names[(int)(code & VariableCode.__LOWERCASE__)];
		}

		public Int64[] ItemPrice;
		
		private readonly List<CharacterTemplate> CharacterTmplList;
		private EmueraConsole output;

		public ConstantData()
		{
			//this.gamebase = gamebase;
			setDefaultArrayLength();

			CharacterTmplList = new List<CharacterTemplate>();
			useCompatiName = Config.CompatiCALLNAME;
		}

		readonly bool useCompatiName;

		private void setDefaultArrayLength()
		{
			MaxDataList[ablIndex] = 100;
			MaxDataList[talentIndex] = 1000;
			MaxDataList[expIndex] = 100;
			MaxDataList[markIndex] = 100;
			MaxDataList[trainIndex] = 1000;
			MaxDataList[paramIndex] = 200;
			MaxDataList[itemIndex] = 1000;
			MaxDataList[baseIndex] = 100;
			MaxDataList[sourceIndex] = 1000;
			MaxDataList[exIndex] = 100;
			MaxDataList[equipIndex] = 100;
			MaxDataList[tequipIndex] = 100;
			MaxDataList[flagIndex] = 10000;
			MaxDataList[tflagIndex] = 1000;
			MaxDataList[cflagIndex] = 1000;
			MaxDataList[tcvarIndex] = 100;
			MaxDataList[cstrIndex] = 100;
			MaxDataList[stainIndex] = 1000;
			MaxDataList[strIndex] = 20000;
			MaxDataList[cdflag1Index] = 1;
			MaxDataList[cdflag2Index] = 1;
			MaxDataList[strnameIndex] = 20000;
			MaxDataList[tstrnameIndex] = 100;
			MaxDataList[savestrnameIndex] = 100;
			MaxDataList[globalIndex] = 1000;
			MaxDataList[globalsIndex] = 100;
			MaxDataList[dayIndex] = 100;
			MaxDataList[timeIndex] = 100;
			MaxDataList[moneyIndex] = 100;

			VariableIntArrayLength = new int[(int)VariableCode.__COUNT_INTEGER_ARRAY__];
			VariableStrArrayLength = new int[(int)VariableCode.__COUNT_STRING_ARRAY__];
			VariableFloatArrayLength = new int[(int)VariableCode.__COUNT_FLOAT_ARRAY__];
			VariableIntArray2DLength = new Int64[(int)VariableCode.__COUNT_INTEGER_ARRAY_2D__];
			VariableStrArray2DLength = new Int64[(int)VariableCode.__COUNT_STRING_ARRAY_2D__];
			VariableIntArray3DLength = new Int64[(int)VariableCode.__COUNT_INTEGER_ARRAY_3D__];
			VariableStrArray3DLength = new Int64[(int)VariableCode.__COUNT_STRING_ARRAY_3D__];
			CharacterIntArrayLength = new int[(int)VariableCode.__COUNT_CHARACTER_INTEGER_ARRAY__];
			CharacterStrArrayLength = new int[(int)VariableCode.__COUNT_CHARACTER_STRING_ARRAY__];
			CharacterIntArray2DLength = new Int64[(int)VariableCode.__COUNT_CHARACTER_INTEGER_ARRAY_2D__];
			CharacterStrArray2DLength = new Int64[(int)VariableCode.__COUNT_CHARACTER_STRING_ARRAY_2D__];
			for (int i = 0; i < VariableIntArrayLength.Length; i++)
				VariableIntArrayLength[i] = 1000;
			VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.FLAG)] = 10000;
			VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.ITEMPRICE)] = MaxDataList[itemIndex];

			VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.RANDDATA)] = 625;

			for (int i = 0; i < VariableStrArrayLength.Length; i++)
				VariableStrArrayLength[i] = 100;
			VariableStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.STR)] = MaxDataList[strIndex];
			for (int i = 0; i < VariableFloatArrayLength.Length; i++)
				VariableFloatArrayLength[i] = 10;

			for (int i = 0; i < VariableIntArray2DLength.Length; i++)
				VariableIntArray2DLength[i] = (100L << 32) + 100L;
			for (int i = 0; i < VariableStrArray2DLength.Length; i++)
				VariableStrArray2DLength[i] = (100L << 32) + 100L;

			for (int i = 0; i < VariableIntArray3DLength.Length; i++)
				VariableIntArray3DLength[i] = (100L << 40) + (100L << 20) + 100L;
			for (int i = 0; i < VariableStrArray3DLength.Length; i++)
				VariableStrArray3DLength[i] = (100L << 40) + (100L << 20) + 100L;

			for (int i = 0; i < CharacterIntArrayLength.Length; i++)
				CharacterIntArrayLength[i] = 100;
			CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.TALENT)] = 1000;
			CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.CFLAG)] = 1000;
			CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)] = 200;
			CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.GOTJUEL)] = 200;

			for (int i = 0; i < CharacterStrArrayLength.Length; i++)
				CharacterStrArrayLength[i] = 100;

			for (int i = 0; i < CharacterIntArray2DLength.Length; i++)
				CharacterIntArray2DLength[i] = (1L << 32) + 1L;
			for (int i = 0; i < CharacterStrArray2DLength.Length; i++)
				CharacterStrArray2DLength[i] = (1L << 32) + 1L;
		}

		private void loadVariableSizeData(string csvPath, bool disp)
		{
			if (!uEmuera.Utils.FileExists(csvPath))
				return;
			EraStreamReader eReader = new EraStreamReader(false);
			if (!eReader.OpenOnCache(csvPath))
			{
				output.PrintError(eReader.Filename + "のオープンに失敗しました");
				return;
			}
			ScriptPosition position = null;
			if (disp)
				output.PrintSystemLine(eReader.Filename + "読み込み中・・・");
			try
			{
				StringStream st = null;
				while ((st = eReader.ReadEnabledLine()) != null)
				{
					position = new ScriptPosition(eReader.Filename, eReader.LineNo);
					changeVariableSizeData(st, position);
				}
				position = new ScriptPosition(eReader.Filename, -1);
			}
			catch
			{
				uEmuera.Media.SystemSounds.Hand.Play();
				if (position != null)
					ParserMediator.Warn("予期しないエラーが発生しました", position, 3);
				else
					output.PrintError("予期しないエラーが発生しました");
				return;
			}
			finally
			{
				eReader.Close();
			}
			decideActualArraySize(position);
		}


		private void changeVariableSizeData(StringStream st, ScriptPosition position)
		{
			string source = st.RowString;
			int startOffset = st.CurrentPosition;
			Span<CsvFieldRange> fields = stackalloc CsvFieldRange[5];
			int tokenCount = ReadCsvHeadFields(source, startOffset, fields);
			if (tokenCount < 2)
			{
				ParserMediator.Warn("\",\"が必要です", position, 1);
				return;
			}
			string idtoken = GetTrimmedFieldString(source, fields[0]);
			VariableIdentifier id = VariableIdentifier.GetVariableId(idtoken);
			if (id == null)
			{
				ParserMediator.Warn("一つ目の値を変数名として認識できません", position, 1);
				return;
			}
			if ((!id.IsArray1D) && (!id.IsArray2D) && (!id.IsArray3D))
			{
				ParserMediator.Warn("配列変数でない変数" + id.ToString() + "のサイズを変更できません", position, 1);
				return;
			}
			if ((id.IsCalc) || (id.Code == VariableCode.RANDDATA))
			{
				ParserMediator.Warn(id.ToString() + "のサイズは変更できません", position, 1);
				return;
			}
            int length2 = 0;
            int length3 = 0;
			if (!int.TryParse(source.AsSpan(fields[1].Start, fields[1].Length), out int length))
			{
				ParserMediator.Warn("二つ目の値を整数値として認識できません", position, 1);
				return;
			}
            //1820a16 変数禁止指定 負の値を指定する
			if (length <= 0)
			{
				if (length == 0)
				{
					ParserMediator.Warn("配列長に0は指定できません（変数を使用禁止にするには配列長に負の値を指定してください）", position, 2);
					return;
				}
				if(!id.CanForbid)
				{
					ParserMediator.Warn("使用禁止にできない変数に対して負の配列長が指定されています", position, 2);
					return;
				}
                if (tokenCount > 2 && StartsWithDigitAfterTrim(source, fields[2]))
                {
                    ParserMediator.Warn("一次元配列のサイズ指定に不必要なデータは無視されます", position, 0);
                }
				length = 0;
				goto check1break;
			}
			if (id.IsArray1D)
			{
                if (tokenCount > 2 && StartsWithDigitAfterTrim(source, fields[2]))
                {
                    ParserMediator.Warn("一次元配列のサイズ指定に不必要なデータは無視されます", position, 0);
                }
				if (id.IsLocal && length < 1)
				{
					ParserMediator.Warn("ローカル変数のサイズを1未満にはできません", position, 1);
					return;
				}
				if (!id.IsLocal && length < 100)
				{
					ParserMediator.Warn("ローカル変数でない一次元配列のサイズを100未満にはできません", position, 1);
					return;
				}
				if (length > 1000000)
				{
					ParserMediator.Warn("一次元配列のサイズを1000000より大きくすることはできません", position, 1);
					return;
				}
			}
			else if (id.IsArray2D)
			{
				if (tokenCount < 3)
				{
					ParserMediator.Warn("二次元配列のサイズ指定には2つの数値が必要です", position, 1);
					return;
				}
                if (tokenCount > 3 && StartsWithDigitAfterTrim(source, fields[3]))
                {
                    ParserMediator.Warn("二次元配列のサイズ指定に不必要なデータは無視されます", position, 0);
                }
                if (!int.TryParse(source.AsSpan(fields[2].Start, fields[2].Length), out length2))
				{
					ParserMediator.Warn("三つ目の値を整数値として認識できません", position, 1);
					return;
				}
				if ((length < 1) || (length2 < 1))
				{
					ParserMediator.Warn("配列サイズを1未満にはできません", position, 1);
					return;
				}
				if ((length > 1000000) || (length2 > 1000000))
				{
					ParserMediator.Warn("配列サイズを1000000より大きくすることはできません", position, 1);
					return;
				}
				if (length * length2 > 1000000)
				{
					ParserMediator.Warn("二次元配列の要素数は最大で100万個までです", position, 1);
					return;
				}
			}
			else if (id.IsArray3D)
			{
				if (tokenCount < 4)
				{
					ParserMediator.Warn("三次元配列のサイズ指定には3つの数値が必要です", position, 1);
					return;
				}
                if (tokenCount > 4 && StartsWithDigitAfterTrim(source, fields[4]))
                {
                    ParserMediator.Warn("三次元配列のサイズ指定に不必要なデータは無視されます", position, 0);
                }
                if (!int.TryParse(source.AsSpan(fields[2].Start, fields[2].Length), out length2))
				{
					ParserMediator.Warn("三つ目の値を整数値として認識できません", position, 1);
					return;
				}
				if (!int.TryParse(source.AsSpan(fields[3].Start, fields[3].Length), out length3))
				{
					ParserMediator.Warn("四つ目の値を整数値として認識できません", position, 1);
					return;
				}
				if ((length < 1) || (length2 < 1) || (length3 < 1))
				{
					ParserMediator.Warn("配列サイズを1未満にはできません", position, 1);
					return;
				}
				//1802 サイズ保存の都合上、2^20超えるとバグる
				if ((length > 1000000) || (length2 > 1000000) || (length3 > 1000000))
				{
					ParserMediator.Warn("配列サイズを1000000より大きくすることはできません", position, 1);
					return;
				}
				if (length * length2 * length3 > 10000000)
				{
					ParserMediator.Warn("三次元配列の要素数は最大で1000万個までです", position, 1);
					return;
				}
			}
check1break:
			switch (id.Code)
			{
				//1753a PALAMだけ仕様が違うのはかえって問題なので、変数と要素文字列配列数の同期は全部バックアウト
				//基本的には旧来の処理に戻しただけ
				case VariableCode.ITEMNAME:
				case VariableCode.ITEMPRICE:
					VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.ITEMPRICE)] = length;
					MaxDataList[itemIndex] = length;
					break;
				case VariableCode.STR:
					VariableStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.STR)] = length;
					MaxDataList[strIndex] = length;
					break;
				case VariableCode.ABLNAME:
				case VariableCode.TALENTNAME:
				case VariableCode.EXPNAME:
				case VariableCode.MARKNAME:
				case VariableCode.PALAMNAME:
				case VariableCode.TRAINNAME:
				case VariableCode.BASENAME:
				case VariableCode.SOURCENAME:
				case VariableCode.EXNAME:
				case VariableCode.EQUIPNAME:
				case VariableCode.TEQUIPNAME:
				case VariableCode.FLAGNAME:
				case VariableCode.TFLAGNAME:
				case VariableCode.CFLAGNAME:
				case VariableCode.TCVARNAME:
				case VariableCode.CSTRNAME:
				case VariableCode.STAINNAME:
				case VariableCode.CDFLAGNAME1:
				case VariableCode.CDFLAGNAME2:
				case VariableCode.TSTRNAME:
				case VariableCode.SAVESTRNAME:
				case VariableCode.STRNAME:
				case VariableCode.GLOBALNAME:
				case VariableCode.GLOBALSNAME:
				case VariableCode.DAYNAME:
				case VariableCode.TIMENAME:
				case VariableCode.MONEYNAME:
					MaxDataList[(int)(id.Code & VariableCode.__LOWERCASE__)] = length;
					break;
				default:
					{
						if (id.IsCharacterData)
						{
							if (id.IsArray2D)
							{
								Int64 length64 = (((Int64)length) << 32) + ((Int64)length2);
								if (id.IsInteger)
									CharacterIntArray2DLength[id.CodeInt] = length64;
								else if (id.IsString)
									CharacterStrArray2DLength[id.CodeInt] = length64;
							}
							else
							{
								if (id.IsInteger)
									CharacterIntArrayLength[id.CodeInt] = length;
								else if (id.IsString)
									CharacterStrArrayLength[id.CodeInt] = length;
							}
						}
						else if (id.IsArray2D)
						{
							Int64 length64 = (((Int64)length) << 32) + ((Int64)length2);
							if (id.IsInteger)
								VariableIntArray2DLength[id.CodeInt] = length64;
							else if (id.IsString)
								VariableStrArray2DLength[id.CodeInt] = length64;
						}
						else if (id.IsArray3D)
						{
							//Int64 length3d = ((Int64)length << 32) + ((Int64)length2 << 16) + (Int64)length3;
							Int64 length3d = ((Int64)length << 40) + ((Int64)length2 << 20) + (Int64)length3;
							if (id.IsInteger)
								VariableIntArray3DLength[id.CodeInt] = length3d;
							else
								VariableStrArray3DLength[id.CodeInt] = length3d;
						}
						else
						{
							if (id.IsInteger)
								VariableIntArrayLength[id.CodeInt] = length;
							else if (id.IsString)
								VariableStrArrayLength[id.CodeInt] = length;
							else if (id.IsFloat)
								VariableFloatArrayLength[id.CodeInt] = length;
						}
					}
					break;
			}
			//1803beta004 二重定義を警告対象に
			if (!changedCode.Add(id.Code))
				ParserMediator.Warn(id.Code.ToString() + "の要素数は既に定義されています（上書きします）", position, 1);
		}

		private void _decideActualArraySize_sub(VariableCode mainCode, VariableCode nameCode, int[] arraylength, ScriptPosition position)
		{
			int nameIndex = (int)(nameCode & VariableCode.__LOWERCASE__);
			int mainLengthIndex = (int)(mainCode & VariableCode.__LOWERCASE__);
			if (changedCode.Contains(nameCode) && changedCode.Contains(mainCode))
			{
				if (MaxDataList[nameIndex] != arraylength[mainLengthIndex])
				{
					int i = Math.Max(MaxDataList[nameIndex], arraylength[mainLengthIndex]);
					arraylength[mainLengthIndex] = i;
					MaxDataList[nameIndex] = i;
					//1803beta004 不適切な指定として警告Lv1の対象にする
					if (MaxDataList[nameIndex] == 0 || arraylength[mainLengthIndex] == 0)
						ParserMediator.Warn(mainCode.ToString() +"と" + nameCode.ToString() + "の禁止設定が異なります（使用禁止を解除します）", position, 1);
					else
						ParserMediator.Warn(mainCode.ToString() +"と" + nameCode.ToString() + "の要素数が異なります（大きい方に合わせます）", position, 1);
				}
			}
			else if (changedCode.Contains(nameCode) && !changedCode.Contains(mainCode))
				arraylength[mainLengthIndex] = MaxDataList[nameIndex];
			else if (!changedCode.Contains(nameCode) && changedCode.Contains(mainCode))
				MaxDataList[nameIndex] = arraylength[mainLengthIndex];
		}
		
		private void decideActualArraySize(ScriptPosition position)
		{
			_decideActualArraySize_sub(VariableCode.ABL, VariableCode.ABLNAME, CharacterIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.TALENT, VariableCode.TALENTNAME, CharacterIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.EXP, VariableCode.EXPNAME, CharacterIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.MARK, VariableCode.MARKNAME, CharacterIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.BASE, VariableCode.BASENAME, CharacterIntArrayLength, position);
            _decideActualArraySize_sub(VariableCode.SOURCE, VariableCode.SOURCENAME, CharacterIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.EX, VariableCode.EXNAME, CharacterIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.EQUIP, VariableCode.EQUIPNAME, CharacterIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.TEQUIP, VariableCode.TEQUIPNAME, CharacterIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.FLAG, VariableCode.FLAGNAME, VariableIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.TFLAG, VariableCode.TFLAGNAME, VariableIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.CFLAG, VariableCode.CFLAGNAME, CharacterIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.TCVAR, VariableCode.TCVARNAME, CharacterIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.CSTR, VariableCode.CSTRNAME, CharacterStrArrayLength, position);
			_decideActualArraySize_sub(VariableCode.STAIN, VariableCode.STAINNAME, CharacterIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.STR, VariableCode.STRNAME, VariableStrArrayLength, position);
			_decideActualArraySize_sub(VariableCode.TSTR, VariableCode.TSTRNAME, VariableStrArrayLength, position);
			_decideActualArraySize_sub(VariableCode.SAVESTR, VariableCode.SAVESTRNAME, VariableStrArrayLength, position);
			_decideActualArraySize_sub(VariableCode.GLOBAL, VariableCode.GLOBALNAME, VariableIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.GLOBALS, VariableCode.GLOBALSNAME, VariableStrArrayLength, position);
			_decideActualArraySize_sub(VariableCode.DAY, VariableCode.DAYNAME, VariableIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.TIME, VariableCode.TIMENAME, VariableIntArrayLength, position);
			_decideActualArraySize_sub(VariableCode.MONEY, VariableCode.MONEYNAME, VariableIntArrayLength, position);


			//PALAM(JUEL込み)
			//PALAMかJUELが変わっているときは大きい方をとる
			if (changedCode.Contains(VariableCode.PALAM) || changedCode.Contains(VariableCode.JUEL))
			{
				int palamJuelMax = Math.Max(CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.PALAM)]
						, CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)]);
				//PALAMNAMEが変わっているなら、それと比較して大きい方を採用
				if(changedCode.Contains(VariableCode.PALAMNAME))
				{
					if (MaxDataList[paramIndex] != palamJuelMax)
					{
						int i = Math.Max(MaxDataList[paramIndex], palamJuelMax);
						MaxDataList[paramIndex] = i;
						if(CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.PALAM)] == palamJuelMax)
							CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.PALAM)] = i;
						if(CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)] == palamJuelMax)
							CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)] = i;
						//1803beta004 不適切な指定として警告Lv1の対象にする
						ParserMediator.Warn("PALAMとJUELとPALAMNAMEの要素数が不適切です", position, 1);
					}
				}
				else//PALAMNAMEの指定がないなら大きい方にPALAMNAMEをあわせる
					MaxDataList[paramIndex] = palamJuelMax;
			}
			//PALAMとJUEL不変でPALAMNAMEが変わっている場合
			else if (changedCode.Contains(VariableCode.PALAMNAME))
			{
				//PALAMを指定のPALAMNAMEにあわせる
				CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.PALAM)] = MaxDataList[paramIndex];
				//指定のPALAMNAMEがJUELより小さければ警告出してJUELにあわせる
				if (MaxDataList[paramIndex] < CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)])
				{
					ParserMediator.Warn("PALAMNAMEの要素数がJUELより少なくなっています（JUELに合わせます）", position, 1);
					MaxDataList[paramIndex] = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)];
				}
			}
			//CDFLAG
			//一部変更されたら双方変更されたと扱う
			bool cdflagNameLengthChanged = changedCode.Contains(VariableCode.CDFLAGNAME1) || changedCode.Contains(VariableCode.CDFLAGNAME2);
			int mainLengthIndex = (int)(VariableCode.__LOWERCASE__ & VariableCode.CDFLAG);
			Int64 length64 = CharacterIntArray2DLength[mainLengthIndex];
			int length1 = (int)(length64 >> 32);
			int length2 = (int)(length64 & 0x7FFFFFFF);
			if (changedCode.Contains(VariableCode.CDFLAG) && cdflagNameLengthChanged)
			{
				//調整が面倒なので投げる
				if ((length1 != MaxDataList[cdflag1Index]) || (length2 != MaxDataList[cdflag2Index]))
					throw new CodeEE("CDFLAGの要素数とCDFLAGNAME1及びCDFLAGNAME2の要素数が一致していません", position);
			}
			else if (cdflagNameLengthChanged && !changedCode.Contains(VariableCode.CDFLAG))
			{
				length1 = MaxDataList[cdflag1Index];
				length2 = MaxDataList[cdflag2Index];
				if (length1 * length2 > 1000000)
				{
					//調整が面倒なので投げる
					throw new CodeEE("CDFLAGの要素数が多すぎます（CDFLAGNAME1とCDFLAGNAME2の要素数の積が100万を超えています）", position);
				}
				CharacterIntArray2DLength[mainLengthIndex] = (((Int64)length1) << 32) + ((Int64)length2);
			}
			else if (!cdflagNameLengthChanged && changedCode.Contains(VariableCode.CDFLAG))
			{
				MaxDataList[cdflag1Index] = length1;
				MaxDataList[cdflag2Index] = length2;
			}
			//もう使わないのでデータ破棄
			changedCode.Clear();
		}


		public void LoadData(string csvDir, EmueraConsole console, bool disp)
		{
			output = console;
			loadVariableSizeData(csvDir + "VariableSize.CSV", disp);
			for(int i = 0; i< countNameCsv;i++)
			{
				names[i] = new string[MaxDataList[i]];
				nameToIntDics[i] = new Dictionary<string, int>(MaxDataList[i]);
				aliases[i] = null;
			}
			ItemPrice = new Int64[MaxDataList[itemIndex]];
			//N5: 名称/别名 CSV 每文件状态完全独立（各自的 names[i]/aliases[i]/ItemPrice），改为并行解析。
			//警告/控制台输出等副作用按原文件顺序捕获，解析完成后统一串行重放，顺序语义与串行完全一致。
			loadNameCsvsInParallel(csvDir, disp);
			//逆引き辞書を作成
			for (int i = 0; i < names.Length; i++)
			{
				if (i == 10)//Strは逆引き無用
					continue;
				string[] nameArray = names[i];
				for (int j = 0; j < nameArray.Length; j++)
				{
					if (!string.IsNullOrEmpty(nameArray[j]))
						nameToIntDics[i].TryAdd(nameArray[j], j);
				}
				Dictionary<string, int> aliasDict = aliases[i];
				if (aliasDict == null)
					continue;
				foreach (var alias in aliasDict)
				{
					if (!string.IsNullOrEmpty(alias.Key))
						nameToIntDics[i].TryAdd(alias.Key, alias.Value);
				}
			}
			//if (!Program.AnalysisMode)
			loadCharacterData(csvDir, disp);
			loadGlobalVarExSetting(csvDir, disp);
			//M8: CSV/ALS 解析已全部完成（VariableSize/名称 CSV/ALS/CHARA/VarExt），
			//释放 Preload 中整会话驻留的 CSV/ALS 解码文本；后续任何再读都会回退到直接磁盘读取。
			Preload.RemoveByExtension(".csv");
			Preload.RemoveByExtension(".als");

			//逆引き辞書を作成2 (RELATION)
			relationDic.EnsureCapacity(CharacterTmplList.Count * 4);
			for (int i = 0; i < CharacterTmplList.Count; i++)
			{
				CharacterTemplate tmpl = CharacterTmplList[i];
				if (!string.IsNullOrEmpty(tmpl.Name))
					relationDic.TryAdd(tmpl.Name, (int)tmpl.No);
				if (!string.IsNullOrEmpty(tmpl.Callname))
                    relationDic.TryAdd(tmpl.Callname, (int)tmpl.No);
				if (!string.IsNullOrEmpty(tmpl.Nickname))
                    relationDic.TryAdd(tmpl.Nickname, (int)tmpl.No);
				// v24 参考侧同时注册 Mastername（RELATION 字符串索引与 GETNUM 均可用）；
                // snake 参考侧 relationDic 不含 Mastername（走独立 masternameToTemplateMap），故按方言门控
                if (!Program.Compatibility.Snake.UsesRelationWithoutMastername && !string.IsNullOrEmpty(tmpl.Mastername))
                    relationDic.TryAdd(tmpl.Mastername, (int)tmpl.No);
			}
		}

		/// <summary>
		/// N5: 名称 CSV 与别名 ALS 的并行解析。每文件状态独立（各自的 names[i]/aliases[i]/ItemPrice），
		/// 警告/打印等副作用捕获到 CsvLoadContext，全部完成后按原文件顺序重放。
		/// </summary>
		private void loadNameCsvsInParallel(string csvDir, bool disp)
		{
			var csvList = new (string BaseName, int TargetIndex, Int64[] TargetI)[]
			{
				("ABL", ablIndex, null),
				("EXP", expIndex, null),
				("TALENT", talentIndex, null),
				("PALAM", paramIndex, null),
				("TRAIN", trainIndex, null),
				("MARK", markIndex, null),
				("ITEM", itemIndex, ItemPrice),
				("BASE", baseIndex, null),
				("SOURCE", sourceIndex, null),
				("EX", exIndex, null),
				("STR", strIndex, null),
				("EQUIP", equipIndex, null),
				("TEQUIP", tequipIndex, null),
				("FLAG", flagIndex, null),
				("TFLAG", tflagIndex, null),
				("CFLAG", cflagIndex, null),
				("TCVAR", tcvarIndex, null),
				("CSTR", cstrIndex, null),
				("STAIN", stainIndex, null),
				("CDFLAG1", cdflag1Index, null),
				("CDFLAG2", cdflag2Index, null),
				("STRNAME", strnameIndex, null),
				("TSTR", tstrnameIndex, null),
				("SAVESTR", savestrnameIndex, null),
				("GLOBAL", globalIndex, null),
				("GLOBALS", globalsIndex, null),
				("DAY", dayIndex, null),
				("TIME", timeIndex, null),
				("MONEY", moneyIndex, null),
			};
			CsvLoadContext[] contexts = new CsvLoadContext[csvList.Length];
			Parallel.For(0, csvList.Length, GetCsvParallelOptions(), i =>
			{
				CsvLoadContext ctx = new CsvLoadContext();
				contexts[i] = ctx;
				loadDataWithAliases(csvDir, csvList[i].BaseName, csvList[i].TargetIndex, csvList[i].TargetI, disp, ctx);
			});
			for (int i = 0; i < contexts.Length; i++)
				contexts[i].Replay(output);
		}

		private void loadGlobalVarExSetting(string csvDir, bool disp)
		{
			GlobalSaveMaps.Clear();
			SaveMaps.Clear();
			GlobalSaveXmls.Clear();
			SaveXmls.Clear();
			GlobalSaveDTs.Clear();
			SaveDTs.Clear();
			StaticMaps.Clear();
			StaticXmls.Clear();
			StaticDTs.Clear();

			if (!uEmuera.Utils.DirectoryExists(csvDir))
				return;

			// 原核心对 VarExt*.csv 固定递归搜索，不受“搜索子目录”配置影响。
			// 这里仍走 uEmuera 文件 API，保证 Android/Godot 路径和大小写回退逻辑可用。
			List<string> csvPaths = uEmuera.Utils.GetFilePaths(csvDir, "VarExt*.csv", SearchOption.AllDirectories);
			if (Config.SortWithFilename)
				csvPaths.Sort(StringComparer.OrdinalIgnoreCase);
			//N5: VarExt*.csv 每文件独立解析，名字记录与副作用按原文件顺序串行回填/重放。
			int fileCount = csvPaths.Count;
			var nameRecords = new List<KeyValuePair<HashSet<string>, string>>[fileCount];
			var contexts = new CsvLoadContext[fileCount];
			Parallel.For(0, fileCount, GetCsvParallelOptions(), i =>
			{
				CsvLoadContext ctx = new CsvLoadContext();
				contexts[i] = ctx;
				nameRecords[i] = loadGlobalVarExSettingFile(csvPaths[i], getRelativeVarExtPath(csvDir, csvPaths[i]), disp, ctx);
			});
			for (int i = 0; i < fileCount; i++)
			{
				List<KeyValuePair<HashSet<string>, string>> records = nameRecords[i];
				for (int j = 0; j < records.Count; j++)
					records[j].Key.Add(records[j].Value);
				contexts[i].Replay(output);
			}
		}

		private static string getRelativeVarExtPath(string rootDir, string fullPath)
		{
			if (string.IsNullOrEmpty(rootDir) || string.IsNullOrEmpty(fullPath))
				return fullPath;
			string normalizedRoot = rootDir.Replace('\\', '/');
			string normalizedPath = fullPath.Replace('\\', '/');
			if (!normalizedRoot.EndsWith("/"))
				normalizedRoot += "/";
			if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
				return normalizedPath.Substring(normalizedRoot.Length);
			return Path.GetFileName(fullPath);
		}

		private List<KeyValuePair<HashSet<string>, string>> loadGlobalVarExSettingFile(string csvPath, string csvName, bool disp, CsvLoadContext ctx)
		{
			List<KeyValuePair<HashSet<string>, string>> records = new List<KeyValuePair<HashSet<string>, string>>();
			EraStreamReader eReader = new EraStreamReader(false);
			if (!eReader.OpenOnCache(csvPath, csvName))
			{
				ctx.PrintError(eReader.Filename + "のオープンに失敗しました");
				return records;
			}
			ScriptPosition position = null;
			if (disp)
				ctx.Print(eReader.Filename + "読み込み中・・・");
			try
			{
				StringStream st = null;
				//stackalloc をループ外へ退避（CA2014）。各イテレーションで上書きするため再利用は安全。
				Span<CsvFieldRange> fields = stackalloc CsvFieldRange[1];
				while ((st = eReader.ReadEnabledLine()) != null)
				{
					position = new ScriptPosition(eReader.Filename, eReader.LineNo);
					string source = st.RowString;
					int startOffset = st.CurrentPosition;
					int tokenCount = ReadCsvHeadFields(source, startOffset, fields);
					if (tokenCount < 2)
					{
						ctx.Warn("\",\"が必要です", position, 1);
						continue;
					}
					CsvFieldRange f0 = fields[0];
					if (f0.Length == 0)
					{
						ctx.Warn("\",\"で始まっています", position, 1);
						continue;
					}

					// key = token0.Trim() 的 span 比较（零分配，不创建 key 字符串）
					int keyStart = f0.Start;
					int keyEnd = f0.Start + f0.Length;
					while (keyStart < keyEnd && char.IsWhiteSpace(source[keyStart]))
						keyStart++;
					while (keyEnd > keyStart && char.IsWhiteSpace(source[keyEnd - 1]))
						keyEnd--;
					if (FieldEqualsRange(source, keyStart, keyEnd - keyStart, "GLOBAL_MAPS", Config.SCVariable))
						addVarExtNamesCollect(GlobalSaveMaps, source, startOffset, records);
					else if (FieldEqualsRange(source, keyStart, keyEnd - keyStart, "SAVE_MAPS", Config.SCVariable))
						addVarExtNamesCollect(SaveMaps, source, startOffset, records);
					else if (FieldEqualsRange(source, keyStart, keyEnd - keyStart, "GLOBAL_XMLS", Config.SCVariable))
						addVarExtNamesCollect(GlobalSaveXmls, source, startOffset, records);
					else if (FieldEqualsRange(source, keyStart, keyEnd - keyStart, "SAVE_XMLS", Config.SCVariable))
						addVarExtNamesCollect(SaveXmls, source, startOffset, records);
					else if (FieldEqualsRange(source, keyStart, keyEnd - keyStart, "GLOBAL_DTS", Config.SCVariable))
						addVarExtNamesCollect(GlobalSaveDTs, source, startOffset, records);
					else if (FieldEqualsRange(source, keyStart, keyEnd - keyStart, "SAVE_DTS", Config.SCVariable))
						addVarExtNamesCollect(SaveDTs, source, startOffset, records);
					else if (FieldEqualsRange(source, keyStart, keyEnd - keyStart, "STATIC_MAPS", Config.SCVariable))
						addVarExtNamesCollect(StaticMaps, source, startOffset, records);
					else if (FieldEqualsRange(source, keyStart, keyEnd - keyStart, "STATIC_XMLS", Config.SCVariable))
						addVarExtNamesCollect(StaticXmls, source, startOffset, records);
					else if (FieldEqualsRange(source, keyStart, keyEnd - keyStart, "STATIC_DTS", Config.SCVariable))
						addVarExtNamesCollect(StaticDTs, source, startOffset, records);
				}
			}
			catch
			{
				ctx.PlayHandSound();
				if (position != null)
					ctx.Warn("予期しないエラーが発生しました", position, 3);
				else
					ctx.PrintError("予期しないエラーが発生しました");
			}
			finally
			{
				eReader.Close();
			}
			return records;
		}

		/// <summary>
		/// 与 addVarExtNames 完全相同的逐字段扫描语义，但把 (目标 HashSet, 名字) 记录收集到列表，
		/// 由调用方在并行阶段结束后按原文件顺序串行回填，避免并行写共享 HashSet。
		/// </summary>
		private static void addVarExtNamesCollect(HashSet<string> target, string source, int startOffset, List<KeyValuePair<HashSet<string>, string>> records)
		{
			if (source == null)
				source = "";
			int fieldIndex = 0;
			int start = startOffset;
			for (int i = startOffset; i <= source.Length; i++)
			{
				if (i < source.Length && source[i] != ',')
					continue;
				if (fieldIndex > 0)
				{
					int s = start;
					int e = i;
					while (s < e && char.IsWhiteSpace(source[s]))
						s++;
					while (e > s && char.IsWhiteSpace(source[e - 1]))
						e--;
					records.Add(new KeyValuePair<HashSet<string>, string>(target, source.Substring(s, e - s)));
				}
				fieldIndex++;
				start = i + 1;
			}
		}

		private static void addVarExtNames(HashSet<string> target, string source, int startOffset)
		{
			if (source == null)
				source = "";

			// VarExt*.csv 允许一行声明多个保存域。原核心用裸逗号 Split，
			// 这里逐字段扫描并保留空字段语义，避免为每行创建完整 string[]。
			// 只在真正需要（字段0 之后的字段会存入 HashSet）时创建字段字符串，且先 Trim 再 materialize 少一次分配。
			int fieldIndex = 0;
			int start = startOffset;
			for (int i = startOffset; i <= source.Length; i++)
			{
				if (i < source.Length && source[i] != ',')
					continue;
				if (fieldIndex > 0)
				{
					int s = start;
					int e = i;
					while (s < e && char.IsWhiteSpace(source[s]))
						s++;
					while (e > s && char.IsWhiteSpace(source[e - 1]))
						e--;
					target.Add(source.Substring(s, e - s));
				}
				fieldIndex++;
				start = i + 1;
			}
		}

		public CharacterTemplate GetCharacterTemplate(Int64 index)
		{
			//foreach (CharacterTemplate chara in CharacterTmplList)
			//{
			//	if (chara.No == index)
			//		return chara;
			//}
			//return null;

            int high = CharacterTmplList.Count - 1;
            int low = 0;
            int mid = 0;
            CharacterTemplate ct = null;
            while(low <= high)
            {
                mid = (low + high) / 2;
                ct = CharacterTmplList[mid];
                var k = ct.No;
                if(k > index)
                    high = mid - 1;
                else if(k < index)
                    low = mid + 1;
                else
                {
                    return ct;
                }
            }
            return null;
		}
		
		public CharacterTemplate GetCharacterTemplate_UseSp(Int64 index, bool sp)
		{
            //foreach (CharacterTemplate chara in CharacterTmplList)
            //{
            //	if (chara.No != index)
            //		continue;
            //	if (Config.CompatiSPChara && sp != chara.IsSpchara)
            //		continue;
            //	return chara;
            //}
            //return null;

            if(!Config.CompatiSPChara)
            {
                return GetCharacterTemplate(index);
            }
            int count = CharacterTmplList.Count;
            int high = count - 1;
            int low = 0;
            int mid = 0;
            bool found = false;
            CharacterTemplate ct = null;
            while(low <= high)
            {
                mid = (low + high) / 2;
                ct = CharacterTmplList[mid];
                var k = ct.No;
                if(k > index)
                    high = mid - 1;
                else if(k < index)
                    low = mid + 1;
                else
                {
                    found = true;
                    break;
                }
            }
            if(!found)
                return null;
            if(ct.IsSpchara == sp)
                return ct;
            for(var i = mid - 1; i >= 0; --i)
            {
                ct = CharacterTmplList[i];
                if(ct.No != index)
                    break;
                if(ct.IsSpchara == sp)
                    return ct;
            }
            for(var i = mid + 1; i < count; ++i)
            {
                ct = CharacterTmplList[i];
                if(ct.No != index)
                    break;
                if(ct.IsSpchara == sp)
                    return ct;
            }
            return null;
        }

		public CharacterTemplate GetCharacterTemplateFromCsvNo(Int64 index)
		{
            // 按 CSV 文件编号（csvNo）查找，而不是内部模板 No。
            // 与 snake 参考实现一致：chara*.csv 的文件名数字可能与其 NO 字段不同，
            // 用 No 二分查找会解析到错误的模板或静默回退到伪角色。
            foreach (CharacterTemplate chara in CharacterTmplList)
            {
                if (chara.csvNo != index)
                    continue;
                return chara;
            }
            return null;
        }

		public Int64 GetCsvNoByCharacterStr(CharacterStrData type, string name)
		{
			if (string.IsNullOrEmpty(name))
				return -1;
			for (int i = 0; i < CharacterTmplList.Count; i++)
			{
				CharacterTemplate tmpl = CharacterTmplList[i];
				string cmp = null;
				switch (type)
				{
					case CharacterStrData.NAME:
						cmp = tmpl.Name;
						break;
					case CharacterStrData.CALLNAME:
						cmp = tmpl.Callname;
						break;
					case CharacterStrData.NICKNAME:
						cmp = tmpl.Nickname;
						break;
					case CharacterStrData.MASTERNAME:
						cmp = tmpl.Mastername;
						break;
				}
				if (!string.IsNullOrEmpty(cmp) && string.Equals(cmp, name, StringComparison.OrdinalIgnoreCase))
					return tmpl.csvNo;
			}
			return -1;
		}

		public CharacterTemplate GetPseudoChara()
		{
			return new CharacterTemplate(0, this);
		}

		//private CharacterData dummyChara = null;
		//public CharacterData DummyChara
		//{
		//    get { if (dummyChara == null) dummyChara = new CharacterData(GlobalStatic.VEvaluator.Constant, GetPseudoChara(),varData); return dummyChara; }
		//    set { dummyChara = value; }
		//}

		private void loadCharacterData(string csvDir, bool disp)
		{
			// 参考侧 csv 目录缺失时静默返回
			if (!uEmuera.Utils.DirectoryExists(csvDir))
			{
				return;
			}
			List<KeyValuePair<string, string>> csvPaths = Config.GetFiles(csvDir, "CHARA*.CSV");
			EnsureCharacterTemplateListCapacity(csvPaths.Count);
#if(UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            csvPaths.AddRange(Config.GetFiles(csvDir, "Chara*.CSV"));
            EnsureCharacterTemplateListCapacity(csvPaths.Count);
            csvPaths.AddRange(Config.GetFiles(csvDir, "CHARA*.csv"));
            EnsureCharacterTemplateListCapacity(csvPaths.Count);
            csvPaths.AddRange(Config.GetFiles(csvDir, "Chara*.csv"));
            EnsureCharacterTemplateListCapacity(csvPaths.Count);
#endif
            //N5: 角色 CSV 每文件独立解析（各自产出 CharacterTemplate 与副作用记录），
            //并行完成后按原文件顺序合并进 CharacterTmplList 并按原顺序重放警告/打印，
            //CharacterTmplList 顺序（含 SortCharacterTmplList 的输入）与串行完全一致。
            int fileCount = csvPaths.Count;
            var templateLists = new List<CharacterTemplate>[fileCount];
            var contexts = new CsvLoadContext[fileCount];
            Parallel.For(0, fileCount, GetCsvParallelOptions(), i =>
            {
                CsvLoadContext ctx = new CsvLoadContext();
                contexts[i] = ctx;
                templateLists[i] = loadCharacterDataFile(csvPaths[i].Value, csvPaths[i].Key, disp, ctx);
            });
            for (int i = 0; i < fileCount; i++)
            {
                CharacterTmplList.AddRange(templateLists[i]);
                contexts[i].Replay(output);
            }
            SortCharacterTmplList();

            var count = CharacterTmplList.Count;
            CharacterTemplate tmpl = null;
            if(useCompatiName)
			{
                for(int i=0; i<count; ++i)
                {
                    tmpl = CharacterTmplList[i];
                    if(string.IsNullOrEmpty(tmpl.Callname))
                        tmpl.Callname = tmpl.Name;
                }
			}
            for(int i = 0; i < count; ++i)
            {
                tmpl = CharacterTmplList[i];
                tmpl.SetSpFlag();
            }
			//M3: テンプレートを疎 Dictionary から密配列へ折り畳む（CharacterData 生成を Array.Copy 直コピー化）
			for (int i = 0; i < count; ++i)
				CharacterTmplList[i].FoldArrays();
			Dictionary<Int64, CharacterTemplate> nList = new Dictionary<Int64, CharacterTemplate>(count);
			Dictionary<Int64, CharacterTemplate> spList = new Dictionary<Int64, CharacterTemplate>(Config.CompatiSPChara ? count : 0);
            for(int i = 0; i < count; ++i)
            {
                tmpl = CharacterTmplList[i];
                Dictionary<Int64, CharacterTemplate>  targetList = nList;
				if(Config.CompatiSPChara && tmpl.IsSpchara)
				{
					targetList = spList;
				}
                CharacterTemplate ct = null;
                if (targetList.TryGetValue(tmpl.No, out ct))
				{
					if (!Config.CompatiSPChara && (tmpl.IsSpchara!= ct.IsSpchara))
						ParserMediator.Warn("番号" + tmpl.No.ToString() + "のキャラが複数回定義されています(SPキャラとして定義するには互換性オプション「SPキャラを使用する」をONにしてください)", null, 1);
					else
						ParserMediator.Warn("番号" + tmpl.No.ToString() + "のキャラが複数回定義されています", null, 1);
				}
				else
					targetList.Add(tmpl.No, tmpl);
			}
		}

		private void EnsureCharacterTemplateListCapacity(int additionalFiles)
		{
			if (additionalFiles <= 0)
				return;
			int target = CharacterTmplList.Count + additionalFiles;
			if (CharacterTmplList.Capacity < target)
				CharacterTmplList.Capacity = target;
		}

		private List<CharacterTemplate> loadCharacterDataFile(string csvPath, string csvName, bool disp, CsvLoadContext ctx)
		{
			List<CharacterTemplate> templates = new List<CharacterTemplate>();
			CharacterTemplate tmpl = null;
			EraStreamReader eReader = new EraStreamReader(false);
			if (!eReader.OpenOnCache(csvPath, csvName))
			{
				ctx.PrintError(eReader.Filename + "のオープンに失敗しました");
				return templates;
			}
			ScriptPosition position = null;
			if (disp)
				ctx.Print(eReader.Filename + "読み込み中・・・");
			try
			{
				Int64 index = -1;
				StringStream st = null;
				//stackalloc をループ外へ退避（CA2014）。各イテレーションで上書きするため再利用は安全。
				Span<CsvFieldRange> fields = stackalloc CsvFieldRange[3];
				while ((st = eReader.ReadEnabledLine()) != null)
				{
					position = new ScriptPosition(eReader.Filename, eReader.LineNo);
					string source = st.RowString;
					int startOffset = st.CurrentPosition;
					int tokenCount = ReadCsvHeadFields(source, startOffset, fields);
					if (tokenCount < 2)
					{
						ctx.Warn("\",\"が必要です", position, 1);
						continue;
					}
					CsvFieldRange f0 = fields[0];
					if (f0.Length == 0)
					{
						ctx.Warn("\",\"で始まっています", position, 1);
						continue;
					}
					if ((FieldEquals(source, f0, "NO", Config.SCVariable))
						|| (FieldEquals(source, f0, "番号", Config.SCVariable)))
					{
						if (tmpl != null)
						{
							ctx.Warn("番号が二重に定義されました", position, 1);
							continue;
						}
						int noTrimEnd = TrimEndLength(source, fields[1]);
						if (!Int64.TryParse(source.AsSpan(fields[1].Start, noTrimEnd), out index))
						{
							ctx.Warn(GetFieldString(source, fields[1]) + "を整数値に変換できません", position, 1);
							continue;
						}
						tmpl = new CharacterTemplate(index, this);
						string no = eReader.Filename.ToUpper();
						no = no.Substring(no.IndexOf("CHARA") + 5);
						StringBuilder sb = new StringBuilder();
						StringStream ss = new StringStream(no);
						while (!ss.EOS && char.IsDigit(ss.Current))
						{
							sb.Append(ss.Current);
							ss.ShiftNext();
						}
						if (sb.Length > 0)
							tmpl.csvNo = Convert.ToInt64(sb.ToString());
						else
							tmpl.csvNo = 0;
							//tmpl.csvNo = index;
						templates.Add(tmpl);
						continue;
					}
					if (tmpl == null)
					{
						ctx.Warn("番号が定義される前に他のデータが始まりました", position, 1);
						continue;
					}
					toCharacterTemplate(position, tmpl, source, fields, tokenCount, ctx);
				}
			}
			catch
			{
				ctx.PlayHandSound();
				if (position != null)
					ctx.Warn("予期しないエラーが発生しました", position, 3);
				else
					ctx.PrintError("予期しないエラーが発生しました");
				return templates;
			}
			finally
			{
				eReader.Dispose();
			}
			return templates;
		}

        private void SortCharacterTmplList()
        {
            CharacterTmplList.Sort((l, r) =>
            {
                return (int)(l.No - r.No);
            });

			nameToTemplateMap.Clear();
			nicknameToTemplateMap.Clear();
			callnameToTemplateMap.Clear();
			masternameToTemplateMap.Clear();
			int count = CharacterTmplList.Count;
			nameToTemplateMap.EnsureCapacity(count);
			nicknameToTemplateMap.EnsureCapacity(count);
			callnameToTemplateMap.EnsureCapacity(count);
			masternameToTemplateMap.EnsureCapacity(count);
			for (int i = CharacterTmplList.Count - 1; i >= 0; i--)
			{
				CharacterTemplate tmpl = CharacterTmplList[i];
				if (tmpl.Name != null)
					nameToTemplateMap[tmpl.Name] = tmpl.No;
				if (tmpl.Nickname != null)
					nicknameToTemplateMap[tmpl.Nickname] = tmpl.No;
				if (tmpl.Callname != null)
					callnameToTemplateMap[tmpl.Callname] = tmpl.No;
				if (tmpl.Mastername != null)
					masternameToTemplateMap[tmpl.Mastername] = tmpl.No;
			}
        }

		private void toCharacterTemplate(ScriptPosition position, CharacterTemplate chara, string source, Span<CsvFieldRange> fields, int tokenCount, CsvLoadContext ctx)
		{
			if (chara == null)
				return;
			int length;
            Dictionary<int, Int64> intArray = null;
            Dictionary<int, string> strArray = null;
			Dictionary<string, int> namearray;

			string errPos = null;
			CsvFieldRange f0 = fields[0];
			// varname 判断：直接按 (start,len) 与 ASCII 大写/日文常量逐字符比较（零分配），
			// 结果与 token0.ToUpper() 的 switch 等价。警告信息用到的 varname 在错误路径才 materialize。
			if (FieldMatchesUpper(source, f0, "NAME") || FieldMatchesUpper(source, f0, "名前"))
			{
				chara.Name = GetFieldString(source, fields[1]);
				return;
			}
			if (FieldMatchesUpper(source, f0, "CALLNAME") || FieldMatchesUpper(source, f0, "呼び名"))
			{
				chara.Callname = GetFieldString(source, fields[1]);
				return;
			}
			if (FieldMatchesUpper(source, f0, "NICKNAME") || FieldMatchesUpper(source, f0, "あだ名"))
			{
				chara.Nickname = GetFieldString(source, fields[1]);
				return;
			}
			if (FieldMatchesUpper(source, f0, "MASTERNAME") || FieldMatchesUpper(source, f0, "主人の呼び方"))
			{
				chara.Mastername = GetFieldString(source, fields[1]);
				return;
			}
			if (FieldMatchesUpper(source, f0, "MARK") || FieldMatchesUpper(source, f0, "刻印"))
			{
				length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.MARK)];
				intArray = chara.Mark;
				namearray = nameToIntDics[markIndex];
				errPos = "mark.csv";
			}
			else if (FieldMatchesUpper(source, f0, "EXP") || FieldMatchesUpper(source, f0, "経験"))
			{
				length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.EXP)];
				intArray = chara.Exp;
				namearray = nameToIntDics[expIndex];//ExpName;
				errPos = "exp.csv";
			}
			else if (FieldMatchesUpper(source, f0, "ABL") || FieldMatchesUpper(source, f0, "能力"))
			{
				length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.ABL)];
				intArray = chara.Abl;
				namearray = nameToIntDics[ablIndex];//AblName;
				errPos = "abl.csv";
			}
			else if (FieldMatchesUpper(source, f0, "BASE") || FieldMatchesUpper(source, f0, "基礎"))
			{
				length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.MAXBASE)];
				intArray = chara.Maxbase;
				namearray = nameToIntDics[baseIndex];//BaseName;
				errPos = "base.csv";
			}
			else if (FieldMatchesUpper(source, f0, "TALENT") || FieldMatchesUpper(source, f0, "素質"))
			{
				length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.TALENT)];
				intArray = chara.Talent;
				namearray = nameToIntDics[talentIndex];//TalentName;
				errPos = "talent.csv";
			}
			else if (FieldMatchesUpper(source, f0, "RELATION") || FieldMatchesUpper(source, f0, "相性"))
			{
				length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.RELATION)];
				intArray = chara.Relation;
				namearray = null;
			}
			else if (FieldMatchesUpper(source, f0, "CFLAG") || FieldMatchesUpper(source, f0, "フラグ"))
			{
				length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.CFLAG)];
				intArray = chara.CFlag;
				namearray = nameToIntDics[cflagIndex];//CFlagName;
				errPos = "cflag.csv";
			}
			else if (FieldMatchesUpper(source, f0, "EQUIP") || FieldMatchesUpper(source, f0, "装着物"))
			{
				length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.EQUIP)];
				intArray = chara.Equip;
				namearray = nameToIntDics[equipIndex];//EquipName;
				errPos = "equip.csv";
			}
			else if (FieldMatchesUpper(source, f0, "JUEL") || FieldMatchesUpper(source, f0, "珠"))
			{
				length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)];
				intArray = chara.Juel;
				namearray = nameToIntDics[paramIndex];//ParamName;
				errPos = "palam.csv";
			}
			else if (FieldMatchesUpper(source, f0, "CSTR"))
			{
				length = CharacterStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.CSTR)];
				strArray = chara.CStr;
				namearray = nameToIntDics[cstrIndex];//CStrName;
				errPos = "cstr.csv";
			}
			// 参考侧对 ISASSI/助手 行静默忽略
			else if (FieldMatchesUpper(source, f0, "ISASSI") || FieldMatchesUpper(source, f0, "助手"))
			{
				return;
			}
			else
			{
				ctx.Warn("\"" + GetFieldString(source, f0) + "\"は解釈できない識別子です", position, 1);
				return;
			}
			if (length < 0)
			{
				ctx.Warn("プログラムミス", position, 3);
				return;
			}
			if (length == 0)
			{
				ctx.Warn(GetFieldString(source, f0).ToUpper() + "は禁止設定された変数です", position, 2);
				return;
			}
			CsvFieldRange f1 = fields[1];
			bool p1isNumeric = tryToInt64(source, f1.Start, TrimEndLength(source, f1), out long p1);
			if (p1isNumeric && ((p1 < 0) || (p1 >= length)))
			{
				ctx.Warn(p1.ToString() + "は配列の範囲外です", position, 1);
				return;
			}
			int index = (int)p1;
			string token1 = null;
			if ((!p1isNumeric) && (namearray != null))
			{
				token1 = GetFieldString(source, f1);
				if (!namearray.TryGetValue(token1, out index))
				{
					ctx.Warn(errPos + "に\"" + token1 + "\"の定義がありません", position, 1);
					//ParserMediator.Warn("\"" + tokens[1] + "\"は解釈できない識別子です", position, 1);
					return;
				}
				else if (index >= length)
				{
					ctx.Warn("\"" + token1 + "\"は配列の範囲外です", position, 1);
					return;
				}
			}

			if ((index < 0) || (index >= length))
			{
				if (p1isNumeric)
					ctx.Warn(index.ToString() + "は配列の範囲外です", position, 1);
				else if (f1.Length == 0)
					ctx.Warn("二つ目の識別子がありません", position, 1);
				else
				{
					if (token1 == null)
						token1 = GetFieldString(source, f1);
					ctx.Warn("\"" + token1 + "\"は解釈できない識別子です", position, 1);
				}
				return;
			}
			if (strArray != null)
			{
				if (tokenCount < 3)
					ctx.Warn("三つ目の識別子がありません", position, 1);
				if (strArray.ContainsKey(index))
					ctx.Warn(GetFieldString(source, f0).ToUpper() + "の" + index.ToString() + "番目の要素は既に定義されています(上書きします)", position, 1);
				strArray[index] = tokenCount >= 3 ? GetFieldString(source, fields[2]) : "";
			}
			else
			{
				if ((tokenCount < 3) || !tryToInt64(source, fields[2].Start, fields[2].Length, out long p2))
					p2 = 1;
				if (intArray.ContainsKey(index))
					ctx.Warn(GetFieldString(source, f0).ToUpper() + "の" + index.ToString() + "番目の要素は既に定義されています(上書きします)", position, 1);
				intArray[index] = p2;
			}
		}

		private void loadDataWithAliases(string csvDir, string baseName, int targetIndex, Int64[] targetI, bool disp, CsvLoadContext ctx)
		{
			// 参考侧别名加载位于 loadDataTo 内部、CSV 存在性检查之后：CSV 缺失时 .ALS 也不加载
			string csvPath = Path.Combine(csvDir, baseName + ".CSV");
			string resolvedCsvPath = uEmuera.Utils.ResolveExistingFilePath(csvPath);
			if (!string.IsNullOrEmpty(resolvedCsvPath))
				csvPath = resolvedCsvPath;
			if (!uEmuera.Utils.FileExists(csvPath))
				return;
			loadDataTo(Path.Combine(csvDir, baseName + ".CSV"), targetIndex, targetI, disp, ctx);
			loadAliases(Path.Combine(csvDir, baseName + ".ALS"), targetIndex, ctx);
		}

		private void loadDataTo(string csvPath, int targetIndex, Int64[] targetI, bool disp, CsvLoadContext ctx)
		{

			string resolvedCsvPath = uEmuera.Utils.ResolveExistingFilePath(csvPath);
			if (!string.IsNullOrEmpty(resolvedCsvPath))
				csvPath = resolvedCsvPath;
			if (!uEmuera.Utils.FileExists(csvPath))
				return;
			string[] target = names[targetIndex];
            HashSet<int> defined = new HashSet<int>();
			EraStreamReader eReader = new EraStreamReader(false);
			if (!eReader.OpenOnCache(csvPath))
			{
				ctx.PrintError(eReader.Filename + "のオープンに失敗しました");
				return;
			}
			ScriptPosition position = null;

			if (disp || Program.AnalysisMode)
				ctx.Print(eReader.Filename + "読み込み中・・・");
			try
			{
				StringStream st = null;
				//stackalloc をループ外へ退避（CA2014）。各イテレーションで上書きするため再利用は安全。
				Span<CsvFieldRange> fields = stackalloc CsvFieldRange[3];
				while ((st = eReader.ReadEnabledLine()) != null)
				{
					position = new ScriptPosition(eReader.Filename, eReader.LineNo);
					string source = st.RowString;
					int startOffset = st.CurrentPosition;
					int tokenCount = ReadCsvHeadFields(source, startOffset, fields);
					if (tokenCount < 2)
					{
						ctx.Warn("\",\"が必要です", position, 1);
						continue;
					}
                    if (!Int32.TryParse(source.AsSpan(fields[0].Start, fields[0].Length), out int index))
                    {
                        ctx.Warn("一つ目の値を整数値に変換できません", position, 1);
                        continue;
                    }
                    if (target.Length == 0)
					{
						ctx.Warn("禁止設定された名前配列です", position, 2);
						break;
					}
					if ((index < 0) || (target.Length <= index))
					{
						ctx.Warn(index.ToString() + "は配列の範囲外です", position, 1);
						continue;
                    }
                    if (!defined.Add(index))
                        ctx.Warn(index.ToString() + "番目の要素はすでに定義されています（新しい値で上書きします）", position, 1);
					target[index] = GetFieldString(source, fields[1]);
					if ((targetI != null) && (tokenCount >= 3))
					{

                        int priceTrimEnd = TrimEndLength(source, fields[2]);
                        if (!Int64.TryParse(source.AsSpan(fields[2].Start, priceTrimEnd), out long price))
                        {
                            ctx.Warn("金額が読み取れません", position, 1);
                            continue;
                        }

                        targetI[index] = price;
					}
				}
			}
			catch
			{
				ctx.PlayHandSound();
				if (position != null)
					ctx.Warn("予期しないエラーが発生しました", position, 3);
				else
					ctx.PrintError("予期しないエラーが発生しました");
				return;
			}
			finally
			{
				eReader.Close();
			}


		}

		private void loadAliases(string aliasPath, int targetIndex, CsvLoadContext ctx)
		{
			string resolvedAliasPath = uEmuera.Utils.ResolveExistingFilePath(aliasPath);
			if (!string.IsNullOrEmpty(resolvedAliasPath))
				aliasPath = resolvedAliasPath;
			if (!uEmuera.Utils.FileExists(aliasPath))
				return;
			if (aliases[targetIndex] == null)
				aliases[targetIndex] = new Dictionary<string, int>();
			Dictionary<string, int> target = aliases[targetIndex];
			EraStreamReader eReader = new EraStreamReader(false);
			if (!eReader.OpenOnCache(aliasPath))
			{
				ctx.PrintError(eReader.Filename + "のオープンに失敗しました");
				return;
			}
			ScriptPosition position = null;
			try
			{
				StringStream st = null;
				//stackalloc をループ外へ退避（CA2014）。各イテレーションで上書きするため再利用は安全。
				Span<CsvFieldRange> fields = stackalloc CsvFieldRange[2];
				while ((st = eReader.ReadEnabledLine()) != null)
				{
					position = new ScriptPosition(eReader.Filename, eReader.LineNo);
					string source = st.RowString;
					int startOffset = st.CurrentPosition;
					int tokenCount = ReadCsvHeadFields(source, startOffset, fields);
					if (tokenCount < 2)
					{
						ctx.Warn("\",\"が必要です", position, 1);
						continue;
					}
					if (!Int32.TryParse(source.AsSpan(fields[0].Start, fields[0].Length), out int index))
					{
						ctx.Warn("一つ目の値を整数値に変換できません", position, 1);
						continue;
					}
					string aliasName = GetTrimmedFieldString(source, fields[1]);
					if (string.IsNullOrEmpty(aliasName))
						continue;
						if (!target.TryAdd(aliasName, index))
						{
							ctx.Warn("別名\"" + aliasName + "\"は既に定義されています", position, 1);
							continue;
						}
					}
			}
			catch
			{
				ctx.PlayHandSound();
				if (position != null)
					ctx.Warn("予期しないエラーが発生しました", position, 3);
				else
					ctx.PrintError("予期しないエラーが発生しました");
				return;
			}
			finally
			{
				eReader.Close();
			}
		}
	}

	internal sealed class CharacterTemplate
	{
        readonly int[] arraySize;
        readonly int cstrSize;

		public string Name;
		public string Callname;
		public string Nickname;
		public string Mastername;
		public readonly Int64 No;
		public readonly Dictionary<Int32, Int64> Maxbase = new Dictionary<Int32, Int64>();
		public readonly Dictionary<Int32, Int64> Mark = new Dictionary<Int32, Int64>();
		public readonly Dictionary<Int32, Int64> Exp = new Dictionary<Int32, Int64>();
		public readonly Dictionary<Int32, Int64> Abl = new Dictionary<Int32, Int64>();
		public readonly Dictionary<Int32, Int64> Talent = new Dictionary<Int32, Int64>();
		public readonly Dictionary<Int32, Int64> Relation = new Dictionary<Int32, Int64>();
		public readonly Dictionary<Int32, Int64> CFlag = new Dictionary<Int32, Int64>();
		public readonly Dictionary<Int32, Int64> Equip = new Dictionary<Int32, Int64>();
		public readonly Dictionary<Int32, Int64> Juel = new Dictionary<Int32, Int64>();
		public readonly Dictionary<Int32, string> CStr = new Dictionary<Int32, string>();
		public Int64 csvNo;
		public bool IsSpchara { get; private set; }

		//M3: CSV 読込完了後に 10 個の疎 Dictionary から折り畳まれた密配列。
		//既定値（数値は 0、文字列は null、RELATION のみ Config.RelationDef）は折り畳み時に焼き込むため、
		//CharacterData 生成はこれを Array.Copy で直コピーすれば旧実装の逐条辞書コピーと同結果になる。
		//Dictionary フィールドは公開挙動維持のため残す（他モジュールが辞書として読む）。未折り畳み（擬似キャラ）は null。
		Int64[] foldedMaxbase;
		Int64[] foldedMark;
		Int64[] foldedExp;
		Int64[] foldedAbl;
		Int64[] foldedTalent;
		Int64[] foldedRelation;
		Int64[] foldedCFlag;
		Int64[] foldedEquip;
		Int64[] foldedJuel;
		string[] foldedCStr;
		
		public CharacterTemplate(Int64 index, ConstantData constant)
		{
			arraySize = constant.CharacterIntArrayLength;
			cstrSize = constant.CharacterStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.CSTR)];
			No = index;
		}
		public int ArrayStrLength(CharacterStrData type)
		{
			switch (type)
			{
				case CharacterStrData.CSTR:
					return cstrSize;
				default:
					throw new CodeEE("存在しないキーを参照しました");
			}
		}

		public int ArrayLength(CharacterIntData type)
		{
			switch (type)
			{
				case CharacterIntData.BASE:
					{
						int size = arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.BASE)];
						int maxSize = arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.MAXBASE)];
						return size > maxSize ? size : maxSize;
					}
				case CharacterIntData.MARK:
					return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.MARK)];
				case CharacterIntData.ABL:
					return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.ABL)];
				case CharacterIntData.EXP:
					return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.EXP)];
				case CharacterIntData.RELATION:
					return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.RELATION)];
				case CharacterIntData.TALENT:
					return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.TALENT)];
				case CharacterIntData.CFLAG:
					return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.CFLAG)];
				case CharacterIntData.EQUIP:
					return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.EQUIP)];
				case CharacterIntData.JUEL:
					return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)];
				default:
					throw new CodeEE("存在しないキーを参照しました");
			}
		}

		internal void SetSpFlag()
		{
			//bool res;
			if (CFlag.ContainsKey(0) && CFlag[0] != 0L)
				IsSpchara = true;
		}

		/// <summary>CSV 読込完了後に疎 Dictionary を密配列へ折り畳む。折り畳み後は Dictionary を変更しないこと。</summary>
		internal void FoldArrays()
		{
			//Maxbase は toCharacterTemplate で MAXBASE の配列長で検証済み（BASE 側と共用）。
			foldedMaxbase = FoldIntArray(Maxbase, arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.MAXBASE)]);
			foldedMark = FoldIntArray(Mark, arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.MARK)]);
			foldedExp = FoldIntArray(Exp, arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.EXP)]);
			foldedAbl = FoldIntArray(Abl, arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.ABL)]);
			foldedTalent = FoldIntArray(Talent, arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.TALENT)]);
			//RELATION は既定値 Config.RelationDef を焼き込む（CharacterData 生成時の全域初期化と同値）。
			foldedRelation = FoldIntArray(Relation, arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.RELATION)], Config.RelationDef);
			foldedCFlag = FoldIntArray(CFlag, arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.CFLAG)]);
			foldedEquip = FoldIntArray(Equip, arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.EQUIP)]);
			foldedJuel = FoldIntArray(Juel, arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)]);
			foldedCStr = FoldStringArray(CStr, cstrSize);
		}

		static Int64[] FoldIntArray(Dictionary<int, Int64> src, int length, Int64 defaultValue = 0L)
		{
			Int64[] arr = new Int64[length];
			if (defaultValue != 0L)
				for (int i = 0; i < length; i++)
					arr[i] = defaultValue;
			if (src != null)
				foreach (KeyValuePair<int, Int64> pair in src)
					arr[pair.Key] = pair.Value;
			return arr;
		}

		static string[] FoldStringArray(Dictionary<int, string> src, int length)
		{
			string[] arr = new string[length];
			if (src != null)
				foreach (KeyValuePair<int, string> pair in src)
					arr[pair.Key] = pair.Value;
			return arr;
		}

		/// <summary>折り畳まれた密配列を返す。未折り畳み（擬似キャラ等）は null。</summary>
		internal Int64[] GetFoldedIntArray(CharacterIntData type)
		{
			switch (type)
			{
				case CharacterIntData.BASE: return foldedMaxbase;
				case CharacterIntData.MARK: return foldedMark;
				case CharacterIntData.ABL: return foldedAbl;
				case CharacterIntData.EXP: return foldedExp;
				case CharacterIntData.RELATION: return foldedRelation;
				case CharacterIntData.TALENT: return foldedTalent;
				case CharacterIntData.CFLAG: return foldedCFlag;
				case CharacterIntData.EQUIP: return foldedEquip;
				case CharacterIntData.JUEL: return foldedJuel;
				default: return null;
			}
		}

		internal string[] GetFoldedStrArray()
		{
			return foldedCStr;
		}
	}
}
