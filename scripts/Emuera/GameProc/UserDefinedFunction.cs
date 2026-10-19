using System;
using System.Collections.Generic;
using System.Text;
using MinorShift.Emuera.Sub;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameProc.Function;

namespace MinorShift.Emuera.GameProc
{
	internal enum UserDifinedFunctionDataArgType
	{
		Null,
		Int = 0x10,
		Str = 0x20,
		Float = 0x30,

		RefInt0 = 0x50,
		RefInt1 = 0x51,
		RefInt2 = 0x52,
		RefInt3 = 0x53,
		RefStr0 = 0x60,
		RefStr1 = 0x61,
		RefStr2 = 0x62,
		RefStr3 = 0x63,
		RefFloat0 = 0x70,
		RefFloat1 = 0x71,
		RefFloat2 = 0x72,
		RefFloat3 = 0x73,
		__Ref = 0x40,
		__Variadic = 0x80,
		__Out = 0x100,
		__Dimention = 0x0F,
	}

	internal sealed class UserDefinedFunctionData
	{
		private UserDefinedFunctionData()
		{
		}
		public string Name = null;
		public bool TypeIsStr = false;
		public bool TypeIsFloat = false;
		public UserDifinedFunctionDataArgType[] ArgList;

		public static UserDefinedFunctionData Create(WordCollection wc, bool dims, ScriptPosition sc)
		{
			return Create(wc, dims, false, sc);
		}

		public static UserDefinedFunctionData Create(WordCollection wc, bool dims, bool isFloat, ScriptPosition sc)
		{
			string dimtype = isFloat ? "#FUNCTIONF" : (dims ? "#FUNCTIONS" : "#FUNCTION");
			UserDefinedFunctionData ret = new UserDefinedFunctionData();
			ret.TypeIsStr = dims;
			ret.TypeIsFloat = isFloat;
			IdentifierWord idw;
			string keyword = dimtype;
			while (!wc.EOL && (idw = wc.Current as IdentifierWord) != null)
			{
				wc.ShiftNext();
				keyword = idw.Code;
				if (Config.ICVariable)
					keyword = keyword.ToUpper();
				switch (keyword)
				{
					case "CONST":
					case "REF":
					case "DYNAMIC":
					case "STATIC":
					case "GLOBAL":
					case "SAVEDATA":
					case "CHARADATA":
						throw new CodeEE(dims + "中では" + keyword + "キーワードは指定できません", sc);
					default:
						ret.Name = keyword;
						goto whilebreak;
				}
			}
		whilebreak:
			if (ret.Name == null)
				throw new CodeEE(keyword + "の後に有効な識別子が指定されていません", sc);
			if (wc.EOL || wc.Current.Type != '(')
				throw new CodeEE("識別子の後に引数定義がありません", sc);
			string errMes = "";
			int errLevel = -1;
			GlobalStatic.IdentifierDictionary.CheckUserLabelName(ref errMes, ref errLevel, true, ret.Name);
			if (errLevel == 0)//関数と変数の両方からチェック エラーメッセージが微妙だがひとまず気にしない
				GlobalStatic.IdentifierDictionary.CheckUserVarName(ref errMes, ref errLevel, ret.Name);
			if (errLevel >= 0)
			{
				if (errLevel >= 2)
					throw new CodeEE(errMes, sc);
				ParserMediator.Warn(errMes, sc, errLevel);
			}
			List<UserDifinedFunctionDataArgType> argList = new List<UserDifinedFunctionDataArgType>();
			UserDifinedFunctionDataArgType argType = UserDifinedFunctionDataArgType.Null;

			int state = 0;
			//0=初期状態 1=カンマ括弧閉じ待ち 2=カンマ直後
			//3=REF後INTorSTRorFLOAT待ち 4=':'or','待ち 5=':'or '0'or ','待ち 6=OUT後INTorSTRorFLOAT待ち 7=可変長引数終端待ち
			while (true)// REF OUT INT STR FLOAT 0 ':' ',' ')' '.' のみで構成されるはず
			{
				wc.ShiftNext();
				switch (wc.Current.Type)
				{
					case '\0':
						throw new CodeEE("括弧が閉じられていません", sc);
					case ')':
						if (state == 0 || state == 1)
							goto argend;
						if (state == 4 || state == 5)
						{
							//state = 2;
							argList.Add(argType);
							goto argend;
						}
						if (state == 7)
							goto argend;
						throw new CodeEE("予期しない括弧です", sc);
					case '0':
						if (((LiteralIntegerWord)wc.Current).Int != 0)
							goto argerr;
						if (state == 5)
						{
							state = 4;
							continue;
						}
						goto argerr;
					case ':':
						if (state == 4 || state == 5)
						{
							state = 5;
							argType++; if ((int)(argType & UserDifinedFunctionDataArgType.__Dimention) > 3)
								throw new CodeEE("REF引数は4次元以上の配列にできません", sc);
							continue;
						}
						goto argerr;
					case ',':
						if (state == 1)
						{
							state = 2;
							continue;
						}
						if (state == 4 || state == 5)
						{
							state = 2;
							argList.Add(argType);
							continue;
						}
						goto argerr;
					case 'A':
						{
							string str = ((IdentifierWord)wc.Current).Code;
							if (Config.ICVariable)
								str = str.ToUpper();
							if (str == "REF")
							{
								if (state == 0 || state == 2)
								{
									state = 3;
									continue;
								}
								goto argerr;
							}
							else if (str == "OUT")
							{
								if (state == 0 || state == 2)
								{
									state = 6;
									continue;
								}
								goto argerr;
							}
							else if (str == "INT" || str == "STR" || str == "FLOAT")
							{
								if (str == "INT")
									argType = UserDifinedFunctionDataArgType.Int;
								else if (str == "STR")
									argType = UserDifinedFunctionDataArgType.Str;
								else
									argType = UserDifinedFunctionDataArgType.Float;
								if (state == 0 || state == 2)
								{
									state = 1;
									argList.Add(argType);
									continue;
								}
								if (state == 3)
								{
									argType = argType | UserDifinedFunctionDataArgType.__Ref;
									state = 4;
									continue;
								}
								if (state == 6)
								{
									argType = argType | UserDifinedFunctionDataArgType.__Ref | UserDifinedFunctionDataArgType.__Out;
									state = 4;
									continue;
								}
								goto argerr;
							}
							else
								goto argerr;
						}
					case '.':
						if (state == 1 && wc.PeekNext(1).Type == '.' && wc.PeekNext(2).Type == '.')
						{
							if ((argType & UserDifinedFunctionDataArgType.__Ref) != 0)
								throw new CodeEE("REF引数は可変長引数にできません", sc);
							argType |= UserDifinedFunctionDataArgType.__Variadic;
							argList[argList.Count - 1] = argType;
							wc.ShiftNext();
							wc.ShiftNext();
							state = 7;
							continue;
						}
						goto argerr;
					default:
						goto argerr;
				}
			}
		argend:
			wc.ShiftNext();
			if (!wc.EOL)
				throw new CodeEE("宣言の後に余分な文字があります", sc);
			ret.ArgList = new UserDifinedFunctionDataArgType[argList.Count];
			argList.CopyTo(ret.ArgList);
			return ret;
		argerr:
			if (!wc.EOL)
				throw new CodeEE("引数の解析中に予期しないトークン" + wc.Current.ToString() + "を発見しました", sc);
			throw new CodeEE("引数の解析中にエラーが発生しました", sc);
		}

	}

}
