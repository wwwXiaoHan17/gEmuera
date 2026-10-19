using System;
using System.Collections.Generic;
using System.Collections;
using System.Data;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameProc;
using MinorShift._Library;
using MinorShift.Emuera.GameProc.Function;
//using System.Windows.Forms;
using uEmuera.Forms;

namespace MinorShift.Emuera.GameData.Variable
{
	internal sealed class VariableEvaluator : IDisposable
	{
		readonly GameBase gamebase;
		readonly ConstantData constant;
		readonly VariableData varData;
		MTRandom rand;
		Random newRand;
		const string RuntimeDataStoreBinaryMarker = "__RDS__";
		const string RuntimeDataStoreTextMarker = "__RDS_TEXT__";
		const string RuntimeDataStoreTextEndMarker = "__RDS_TEXT_END__";
		const byte EmMapDataType = 0x20;
		const byte EmXmlDataType = 0x21;
		const byte EmDataTableDataType = 0x22;

		public VariableData VariableData { get { return varData; } }
		public ConstantData Constant { get { return constant; } }
		public MTRandom Rand { get { return rand; } }

		public VariableEvaluator(GameBase gamebase, ConstantData constant, Int64? randomSeed = null)
		{
			this.gamebase = gamebase;
			this.constant = constant;
			rand = randomSeed.HasValue ? new MTRandom(randomSeed.Value) : new MTRandom();
			newRand = randomSeed.HasValue ? new Random((int)randomSeed.Value) : new Random();
			RuntimeDataStore.Clear();
			varData = new VariableData(gamebase, constant);
			GlobalStatic.VariableData = varData;
		}
		#region set/get

		public void Randomize(Int64 seed)
		{
			rand = new MTRandom(seed);
			// snake 参考：Randomize 同时重播种 newRand；v24 参考只动 rand（UseNewRandom 时指令层直接跳过）
			if (Program.Compatibility.Snake.UsesRandomizeReseed)
				newRand = new Random((int)seed);
		}

		public void InitRanddata()
		{
			SparseArray<Int64> randData = this.RANDDATA;
			rand.SetRand(randData.ToArray(randData.Length));
		}

		public void DumpRanddata()
		{
			SparseArray<Int64> randData = this.RANDDATA;
			Int64[] denseRandData = randData.ToArray(randData.Length);
			rand.GetRand(denseRandData);
			randData.FromArray(denseRandData);
		}
		public Int64 GetNextRand(Int64 max)
		{
			if (Config.UseNewRandom)
				return newRand.NextInt64(max);
			return rand.NextInt64(max);
		}

		// snake 参考实现：浮点 RAND（RANDF / RAND 浮点路径）使用的 [0,1) 双精度随机数。
		public double GetNextRandDouble()
		{
			if (Config.UseNewRandom)
				return newRand.NextDouble();
			return rand.NextDouble();
		}

		public Int64 getPalamLv(Int64 pl, Int64 maxlv)
		{
			for (int i = 0; i < (int)maxlv; i++)
			{
				if (pl < varData.DataIntegerArray[(int)(VariableCode.PALAMLV & VariableCode.__LOWERCASE__)][i + 1])
					return i;
			}
			return maxlv;
		}

		public Int64 getExpLv(Int64 pl, Int64 maxlv)
		{
			for (int i = 0; i < (int)maxlv; i++)
			{
				if (pl < varData.DataIntegerArray[(int)(VariableCode.EXPLV & VariableCode.__LOWERCASE__)][i + 1])
					return i;
			}
			return maxlv;
		}

		public void SetValueAll(FixedVariableTerm p, Int64 srcValue, int start, int end)
		{
            var identifier = p.Identifier;
            //呼び出し元で判定済み
            //if (!p.Identifier.IsInteger)
            //    throw new CodeEE("整数型でない変数" + p.Identifier.Name + "に整数値を代入しようとしました");
            //if (p.Identifier.Readonly)
            //    throw new CodeEE("読み取り専用の変数" + p.Identifier.Name + "に代入しようとしました");
            if (identifier.IsCalc)
				return;
			//一応チェック済み
			//throw new ExeEE("READONLYでないCALC変数の代入処理が設定されていない");

			else
			{
				if (identifier.IsArray1D)
				{
					if (start != 0 || end != identifier.GetLength())
						p.IsArrayRangeValid((Int64)start, (Int64)end, "VARSET", 3L, 4L);
					else if (identifier.IsCharacterData)
						identifier.CheckElement(new Int64[] { p.Index1, p.Index2 });
				}
				else if (identifier.IsCharacterData)
				{
                    identifier.CheckElement(new Int64[] { p.Index1, p.Index2, p.Index3 });
				}
                identifier.SetValueAll(srcValue, start, end, (int)p.Index1);
				return;
			}
		}

		public void SetValueAll(FixedVariableTerm p, string srcValue, int start, int end)
		{
            var identifier = p.Identifier;
            //呼び出し元で判定済み
            //if (!identifier.IsString)
            //    throw new CodeEE("文字列型でない変数" + identifier.Name + "に文字列型を代入しようとしました");
            //if (identifier.Readonly)
            //    throw new CodeEE("読み取り専用の変数" + identifier.Name + "に代入しようとしました");
            if (identifier.IsCalc)
			{
				if (identifier.Code == VariableCode.WINDOW_TITLE)
				{
					GlobalStatic.Console.SetWindowTitle(srcValue);
					return;
				}
				return;
				//一応チェック済み
				//throw new ExeEE("READONLYでないCALC変数の代入処理が設定されていない");
			}
			else
			{
				if (identifier.IsArray1D)
				{
					if (start != 0 || end != identifier.GetLength())
						p.IsArrayRangeValid((Int64)start, (Int64)end, "VARSET", 3L, 4L);
					else if (identifier.IsCharacterData)
						identifier.CheckElement(new Int64[] { p.Index1, p.Index2 });
				}
				else if (identifier.IsCharacterData)
				{
                    identifier.CheckElement(new Int64[] { p.Index1, p.Index2, p.Index3 });
				}
				identifier.SetValueAll(srcValue, start, end, (int)p.Index1);
				return;
			}
		}

		public void SetValueAll(FixedVariableTerm p, double srcValue, int start, int end)
		{
			var identifier = p.Identifier;
			if (identifier.IsCalc)
				return;
			if (identifier.IsArray1D)
			{
				if (start != 0 || end != identifier.GetLength())
					p.IsArrayRangeValid((Int64)start, (Int64)end, "VARSET", 3L, 4L);
				else if (identifier.IsCharacterData)
					identifier.CheckElement(new Int64[] { p.Index1, p.Index2 });
			}
			else if (identifier.IsCharacterData)
			{
				identifier.CheckElement(new Int64[] { p.Index1, p.Index2, p.Index3 });
			}
			identifier.SetValueAll(srcValue, start, end, (int)p.Index1);
		}

		public void SetValueAllEachChara(FixedVariableTerm p, SingleTerm index, Int64 srcValue, int start, int end)
		{
            var identifier = p.Identifier;
            if (!identifier.IsInteger && !identifier.IsFloat)
				throw new CodeEE("整数型でない変数" + identifier.Name + "に整数値を代入しようとしました");
			if (identifier.IsConst)
				throw new CodeEE("読み取り専用の変数" + identifier.Name + "に代入しようとしました");
			if (identifier.IsCalc)
				return;
			//一応チェック済み
			//throw new ExeEE("READONLYでないCALC変数の代入処理が設定されていない");
			if (varData.CharacterList.Count == 0)
				return;

			CharacterData chara = varData.CharacterList[0];
			Int64 indexNum = -1;

			if (identifier.IsArray1D)
			{
				if (index.GetEraType() == EraType.Integer)
					indexNum = index.Int;
				else
					indexNum = constant.KeywordToInteger(identifier.Code, index.Str, 1);
                if (indexNum < 0 || indexNum >= Get1DLength(identifier.GetArrayChara(0)))
					throw new CodeEE("キャラクタ配列変数" + identifier.Name + "の第２引数(" + indexNum.ToString() + ")は配列の範囲外です");
			}

            long[] arguments = new long[] { -1, indexNum };
            for (int i = start; i < end; i++)
            {
                arguments[0] = i;
                identifier.SetValue(srcValue, arguments);
            }
		}

		public void SetValueAllEachChara(FixedVariableTerm p, SingleTerm index, string srcValue, int start, int end)
		{
            var identifier = p.Identifier;
            if (!identifier.IsString)
				throw new CodeEE("文字列型でない変数" + identifier.Name + "に文字列型を代入しようとしました");
			if (identifier.IsConst)
				throw new CodeEE("読み取り専用の変数" + identifier.Name + "に代入しようとしました");
			if (identifier.IsCalc)
			{
				if (identifier.Code == VariableCode.WINDOW_TITLE)
				{
					GlobalStatic.Console.SetWindowTitle(srcValue);
					return;
				}
				//一応チェック済み
				//throw new ExeEE("READONLYでないCALC変数の代入処理が設定されていない");
				return;
			}
			if (varData.CharacterList.Count == 0)
				return;

			Int64 indexNum = -1;

			if (identifier.IsArray1D)
			{
				if (index.GetEraType() == EraType.Integer)
					indexNum = index.Int;
				else
					indexNum = constant.KeywordToInteger(identifier.Code, index.Str, 1);
                if (indexNum < 0 || indexNum >= Get1DLength(identifier.GetArrayChara(0)))
					throw new CodeEE("キャラクタ配列変数" + identifier.Name + "の第２引数(" + indexNum.ToString() + ")は配列の範囲外です");
			}

            long[] arguments = new long[] { -1, indexNum };
            for (int i = start; i < end; ++i)
			{
                arguments[0] = i;
                identifier.SetValue(srcValue, arguments);
            }
		}

		public void SetValueAllEachChara(FixedVariableTerm p, SingleTerm index, double srcValue, int start, int end)
		{
			var identifier = p.Identifier;
			if (identifier.IsConst)
				throw new CodeEE("読み取り専用の変数" + identifier.Name + "に代入しようとしました");
			if (identifier.IsCalc)
				return;
			if (varData.CharacterList.Count == 0)
				return;

			Int64 indexNum = -1;
			if (identifier.IsArray1D)
			{
				if (index.GetEraType() == EraType.Integer)
					indexNum = index.Int;
				else
					indexNum = constant.KeywordToInteger(identifier.Code, index.Str, 1);
				if (indexNum < 0 || indexNum >= Get1DLength(identifier.GetArrayChara(0)))
					throw new CodeEE("キャラクタ配列変数" + identifier.Name + "の第２引数(" + indexNum.ToString() + ")は配列の範囲外です");
			}

			long[] arguments = new long[] { -1, indexNum };
			for (int i = start; i < end; ++i)
			{
				arguments[0] = i;
				identifier.SetValue(srcValue, arguments);
			}
		}

		public Int64 GetArraySum(FixedVariableTerm p, Int64 index1, Int64 index2)
		{
			Int64 sum = 0;
            var identifier = p.Identifier;

            if (identifier.IsCharacterData)
			{
                if (identifier.IsArray1D)
                {
                    long[] arguments = new long[] { p.Index1, -1 };
                    for(int i = (int)index1; i < (int)index2; ++i)
                    {
                        arguments[1] = i;
                        sum += identifier.GetIntValue(GlobalStatic.EMediator, arguments);
                    }
                }
                else
                {
                    long[] arguments = new long[] { p.Index1, p.Index2, -1 };
                    for(int i = (int)index1; i < (int)index2; ++i)
                    {
                        arguments[2] = i;
                        sum += identifier.GetIntValue(GlobalStatic.EMediator, arguments);
                    }
                }
			}
			else
			{
				if (identifier.IsArray1D)
				{
                    long[] arguments = new long[] { -1 };
                    for(int i = (int)index1; i < (int)index2; ++i)
                    {
                        arguments[0] = i;
                        sum += identifier.GetIntValue(GlobalStatic.EMediator, arguments);
                    }
				}
				else if (identifier.IsArray2D)
				{
                    long[] arguments = new long[] { p.Index1, -1 };
                    for(int i = (int)index1; i < (int)index2; ++i)
                    {
                        arguments[1] = i;
                        sum += identifier.GetIntValue(GlobalStatic.EMediator, arguments);
                    }
				}
				else
				{
                    long[] arguments = new long[] { p.Index1, p.Index2, -1 };
                    for(int i = (int)index1; i < (int)index2; ++i)
                    {
                        arguments[2] = i;
                        sum += identifier.GetIntValue(GlobalStatic.EMediator, arguments);
                    }
				}
			}

			return sum;
		}

		public Int64 GetArraySumChara(FixedVariableTerm p, Int64 index1, Int64 index2)
		{
			Int64 sum = 0;
            var identifier = p.Identifier;
            long[] arguments = new long[2] { -1, p.Index2 }; 

            for (int i = (int)index1; i < (int)index2; ++i)
            {
                arguments[0] = i;
                sum += identifier.GetIntValue(GlobalStatic.EMediator, arguments);
            }
            return sum;
		}

		public double GetArraySumDouble(FixedVariableTerm p, Int64 index1, Int64 index2)
		{
			double sum = 0;
			var identifier = p.Identifier;

			if (identifier.IsCharacterData)
			{
				if (identifier.IsArray1D)
				{
					long[] arguments = new long[] { p.Index1, -1 };
					for (int i = (int)index1; i < (int)index2; ++i)
					{
						arguments[1] = i;
						sum += identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
					}
				}
				else
				{
					long[] arguments = new long[] { p.Index1, p.Index2, -1 };
					for (int i = (int)index1; i < (int)index2; ++i)
					{
						arguments[2] = i;
						sum += identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
					}
				}
			}
			else
			{
				if (identifier.IsArray1D)
				{
					long[] arguments = new long[] { -1 };
					for (int i = (int)index1; i < (int)index2; ++i)
					{
						arguments[0] = i;
						sum += identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
					}
				}
				else if (identifier.IsArray2D)
				{
					long[] arguments = new long[] { p.Index1, -1 };
					for (int i = (int)index1; i < (int)index2; ++i)
					{
						arguments[1] = i;
						sum += identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
					}
				}
				else
				{
					long[] arguments = new long[] { p.Index1, p.Index2, -1 };
					for (int i = (int)index1; i < (int)index2; ++i)
					{
						arguments[2] = i;
						sum += identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
					}
				}
			}

			return sum;
		}

		public double GetArraySumCharaDouble(FixedVariableTerm p, Int64 index1, Int64 index2)
		{
			double sum = 0;
			var identifier = p.Identifier;
			long[] arguments = new long[2] { -1, p.Index2 };

			for (int i = (int)index1; i < (int)index2; ++i)
			{
				arguments[0] = i;
				sum += identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
			}
			return sum;
		}

        public string GetJoinedStr(FixedVariableTerm p, string delimiter, Int64 index1, Int64 length)
        {
            var pIdentifier = p.Identifier;
            int count = (int)length;

            if (p.IsString)
            {
                if (pIdentifier.IsArray1D)
                {
                    return JoinString1D(delimiter, pIdentifier.GetArray(), (int)index1, count);
                }
                // JOIN系関数は大量配列で呼ばれるため、+= による累積コピーを避ける。
                // Android/Mono では短命な中間文字列が GC スパイクに直結する。
                var builder = new StringBuilder();
                if (pIdentifier.IsArray2D)
                {
                    var arguments = new long[] { p.Index1, 0 };
                    for(int i = 0; i < count; i++)
                    {
                        arguments[1] = index1 + i;
                        if (i > 0)
                            builder.Append(delimiter);
                        builder.Append(pIdentifier.GetStrValue(GlobalStatic.EMediator, arguments));
                    }
                }
                else
                {
                    var arguments = new long[] { p.Index1, p.Index2, 0 };
                    for(int i = 0; i < count; i++)
                    {
                        arguments[2] = index1 + i;
                        if (i > 0)
                            builder.Append(delimiter);
                        builder.Append(pIdentifier.GetStrValue(GlobalStatic.EMediator, arguments));
                    }
                }
                return builder.ToString();
            }
            else
            {
                var builder = new StringBuilder();
                if (pIdentifier.IsArray1D)
                {
                    var arguments = new long[] { 0 };
                    for(int i = 0; i < count; i++)
                    {
                        arguments[0] = index1 + i;
                        if (i > 0)
                            builder.Append(delimiter);
                        builder.Append(pIdentifier.GetIntValue(GlobalStatic.EMediator, arguments));
                    }
                }
                else if (pIdentifier.IsArray2D)
                {
                    var arguments = new long[] { p.Index1, 0 };
                    for(int i = 0; i < count; i++)
                    {
                        arguments[1] = index1 + i;
                        if (i > 0)
                            builder.Append(delimiter);
                        builder.Append(pIdentifier.GetIntValue(GlobalStatic.EMediator, arguments));
                    }
                }
                else
                {
                    var arguments = new long[] { p.Index1, p.Index2, 0 };
                    for(int i = 0; i < count; i++)
                    {
                        arguments[2] = index1 + i;
                        if (i > 0)
                            builder.Append(delimiter);
                        builder.Append(pIdentifier.GetIntValue(GlobalStatic.EMediator, arguments));
                    }
                }
                return builder.ToString();
            }
        }

        public Int64 GetMatch(FixedVariableTerm p, Int64 target, Int64 start, Int64 end)
        {
            Int64 ret = 0;
            var identifier = p.Identifier;
            long[] arguments = null;
            int idx = 0;
            if(identifier.IsCharacterData)
            {
                arguments = new long[] { p.Index1, -1 };
                idx = 1;
            }
            else
                arguments = new long[] { -1 };

            for(int i = (int)start; i < (int)end; ++i)
            {
                arguments[idx] = i;
                if(identifier.GetIntValue(GlobalStatic.EMediator, arguments) == target)
                    ++ret;
            }
			return ret;
		}

		public Int64 GetMatch(FixedVariableTerm p, string target, Int64 start, Int64 end)
		{
            Int64 ret = 0;
            var identifier = p.Identifier;
            bool targetIsNullOrEmpty = string.IsNullOrEmpty(target);
            long[] arguments = null;
            int idx = 0;
            if(identifier.IsCharacterData)
            {
                arguments = new long[] { p.Index1, -1 };
                idx = 1;
            }
            else
                arguments = new long[] { -1 };

            for(int i = (int)start; i < (int)end; ++i)
            {
                arguments[idx] = i;
                string value = identifier.GetStrValue(GlobalStatic.EMediator, arguments);
                if((value == target) ||
                    (targetIsNullOrEmpty && string.IsNullOrEmpty(value)))
                    ++ret;
            }

			return ret;
		}

		public Int64 GetMatch(FixedVariableTerm p, double target, Int64 start, Int64 end)
		{
			Int64 ret = 0;
			var identifier = p.Identifier;
			long[] arguments = null;
			int idx = 0;
			if (identifier.IsCharacterData)
			{
				arguments = new long[] { p.Index1, -1 };
				idx = 1;
			}
			else
				arguments = new long[] { -1 };

			for (int i = (int)start; i < (int)end; ++i)
			{
				arguments[idx] = i;
				if (identifier.GetFloatValue(GlobalStatic.EMediator, arguments) == target)
					++ret;
			}
			return ret;
		}

        public Int64 GetMatchChara(FixedVariableTerm p, Int64 target, Int64 start, Int64 end)
        {
            Int64 ret = 0;
            var identifier = p.Identifier;
            long[] arguments = new long[3] { -1, p.Index2, p.Index3 };

            for (int i = (int)start; i < (int)end; ++i)
            {
                arguments[0] = i;
                if (identifier.GetIntValue(GlobalStatic.EMediator, arguments) == target)
                    ret++;
            }

            return ret;
        }

		public Int64 GetMatchChara(FixedVariableTerm p, string target, Int64 start, Int64 end)
		{
			Int64 ret = 0;
            var identifier = p.Identifier;
            bool targetIsNullOrEmpty = string.IsNullOrEmpty(target);
            long[] arguments = new long[3] { -1, p.Index2, p.Index3 };

            for (int i = (int)start; i < (int)end; ++i)
            {
                arguments[0] = i;
                string value = identifier.GetStrValue(GlobalStatic.EMediator, arguments);
                if ((value == target) ||
                    (targetIsNullOrEmpty && string.IsNullOrEmpty(value)))
                    ret++;
            }

			return ret;
		}

		public Int64 GetMatchChara(FixedVariableTerm p, double target, Int64 start, Int64 end)
		{
			Int64 ret = 0;
			var identifier = p.Identifier;
			long[] arguments = new long[3] { -1, p.Index2, p.Index3 };

			for (int i = (int)start; i < (int)end; ++i)
			{
				arguments[0] = i;
				if (identifier.GetFloatValue(GlobalStatic.EMediator, arguments) == target)
					ret++;
			}

			return ret;
		}

		public Int64 FindElement(FixedVariableTerm p, Int64 target, Int64 start, Int64 end, bool isExact, bool isLast)
		{
			object array;
            var identifier = p.Identifier;

            //指定値の配列要素の範囲外かのチェックは済んでるので、これだけでよい
            if (start >= end)
				return -1;

			if (identifier.IsCharacterData)
                array = identifier.GetArrayChara((int)p.Index1);
			else
				array = identifier.GetArray();

			if (isLast)
			{
				for (int i = (int)end - 1; i >= (int)start; --i)
				{
					if (target == GetInt1D(array, i))
						return (Int64)i;
				}
			}
			else
			{
				for (int i = (int)start; i < (int)end; ++i)
				{
					if (target == GetInt1D(array, i))
						return (Int64)i;
				}
			}
			return -1;
		}

		public Int64 FindElement(FixedVariableTerm p, Regex target, Int64 start, Int64 end, bool isExact, bool isLast)
		{
			object array;

			//指定値の配列要素の範囲外かのチェックは済んでるので、これだけでよい
			if (start >= end)
				return -1;
            var identifier = p.Identifier;
            if (identifier.IsCharacterData)
                array = identifier.GetArrayChara((int)p.Index1);
			else
				array = identifier.GetArray();

			if (isLast)
			{
				for (int i = (int)end - 1; i >= (int)start; --i)
				{
					//1823 Nullなら空文字列として扱う
					string str = GetStr1D(array, i) ?? "";
					if (isExact)
					{
						Match match = target.Match(str);
						//正規表現に引っかかった文字列の長さ＝元の文字列の長さなら完全一致
						if (match.Success && str.Length == match.Length)
							return (Int64)i;
					}
					else
					{
						//部分一致なのでひっかかればOK
						if (target.IsMatch(str))
							return (Int64)i;
					}
				}
			}
			else
			{
				for (int i = (int)start; i < (int)end; ++i)
				{
					//1823 Nullなら空文字列として扱う
					string str = GetStr1D(array, i) ?? "";
					if (isExact)
					{
						//正規表現に引っかかった文字列の長さ＝元の文字列の長さなら完全一致
						Match match = target.Match(str);
						if (match.Success && str.Length == match.Length)
							return (Int64)i;
					}
					else
					{
						//部分一致なのでひっかかればOK
						if (target.IsMatch(str))
							return (Int64)i;
					}
				}
			}
			return -1;
		}

		public Int64 GetMaxArray(FixedVariableTerm p, Int64 start, Int64 end, bool isMax)
		{
            Int64 value;
            var identifier = p.Identifier;
            int idx = 0;
            long[] arguments = new long[2] { -1, -1 };
            if(identifier.IsCharacterData)
            {
                arguments = new long[] { p.Index1, start };
                idx = 1;
            }
            else
                arguments = new long[] { start };

            Int64 ret = p.Identifier.GetIntValue(GlobalStatic.EMediator, arguments);
            if(isMax)
            {
                for(int i = (int)start + 1; i < (int)end; ++i)
                {
                    arguments[idx] = i;
                    value = identifier.GetIntValue(GlobalStatic.EMediator, arguments);
                    if(value > ret)
                        ret = value;
                }
            }
            else
            { 
                for(int i = (int)start + 1; i < (int)end; ++i)
                {
                    arguments[idx] = i;
                    value = identifier.GetIntValue(GlobalStatic.EMediator, arguments);
                    if(value < ret)
                        ret = value;
                }
            }
			return ret;
		}

        public Int64 GetMaxArrayChara(FixedVariableTerm p, Int64 start, Int64 end, bool isMax)
        {
            Int64 value;
            var identifier = p.Identifier;
            long[] arguments = new long[3] { start, p.Index2, p.Index3 };

            Int64 ret = identifier.GetIntValue(GlobalStatic.EMediator, arguments);
            if(isMax)
            {
                for(int i = (int)start + 1; i < (int)end; ++i)
                {
                    arguments[0] = i;
                    value = identifier.GetIntValue(GlobalStatic.EMediator, arguments);
                    if(value > ret)
                        ret = value;
                }
            }
            else
            {
                for(int i = (int)start + 1; i < (int)end; ++i)
                {
                    arguments[0] = i;
                    value = identifier.GetIntValue(GlobalStatic.EMediator, arguments);
                    if(value < ret)
                        ret = value;
                }
            }

            return ret;
        }

		public double GetMaxArrayDouble(FixedVariableTerm p, Int64 start, Int64 end, bool isMax)
		{
			double value;
			var identifier = p.Identifier;
			int idx = 0;
			long[] arguments;
			if (identifier.IsCharacterData)
			{
				arguments = new long[] { p.Index1, start };
				idx = 1;
			}
			else
				arguments = new long[] { start };

			double ret = identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
			if (isMax)
			{
				for (int i = (int)start + 1; i < (int)end; ++i)
				{
					arguments[idx] = i;
					value = identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
					if (value > ret)
						ret = value;
				}
			}
			else
			{
				for (int i = (int)start + 1; i < (int)end; ++i)
				{
					arguments[idx] = i;
					value = identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
					if (value < ret)
						ret = value;
				}
			}
			return ret;
		}

		public double GetMaxArrayCharaDouble(FixedVariableTerm p, Int64 start, Int64 end, bool isMax)
		{
			double value;
			var identifier = p.Identifier;
			long[] arguments = new long[3] { start, p.Index2, p.Index3 };

			double ret = identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
			if (isMax)
			{
				for (int i = (int)start + 1; i < (int)end; ++i)
				{
					arguments[0] = i;
					value = identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
					if (value > ret)
						ret = value;
				}
			}
			else
			{
				for (int i = (int)start + 1; i < (int)end; ++i)
				{
					arguments[0] = i;
					value = identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
					if (value < ret)
						ret = value;
				}
			}

			return ret;
		}

		public Int64 GetInRangeArray(FixedVariableTerm p, Int64 min, Int64 max, Int64 start, Int64 end)
		{
            Int64 value;
			Int64 ret = 0;
            var identifier = p.Identifier;
            long[] arguments = null;
            int idx = 0;
            if(identifier.IsCharacterData)
            {
                arguments = new long[] { p.Index1, -1 };
                idx = 1;
            }
            else
                arguments = new long[] { -1 };

            for (int i = (int)start; i < (int)end; ++i)
            {
                arguments[idx] = i;
                value = identifier.GetIntValue(GlobalStatic.EMediator, arguments);
                if (value >= min && value < max)
                    ret++;
            }
			return ret;
		}

		public Int64 GetInRangeArrayChara(FixedVariableTerm p, Int64 min, Int64 max, Int64 start, Int64 end)
		{
			Int64 ret = 0;
            Int64 value;
            var identifier = p.Identifier;
            long[] arguments = new long[3] { -1, p.Index2, p.Index3 };
            for (int i = (int)start; i < (int)end; i++)
            {
                arguments[0] = i;
                value = identifier.GetIntValue(GlobalStatic.EMediator, arguments);
                if (value >= min && value < max)
                    ret++;
            }

			return ret;
		}

		public Int64 GetInRangeArrayDouble(FixedVariableTerm p, double min, double max, Int64 start, Int64 end)
		{
            double value;
			Int64 ret = 0;
            var identifier = p.Identifier;
            long[] arguments = null;
            int idx = 0;
            if(identifier.IsCharacterData)
            {
                arguments = new long[] { p.Index1, -1 };
                idx = 1;
            }
            else
                arguments = new long[] { -1 };

            for (int i = (int)start; i < (int)end; ++i)
            {
                arguments[idx] = i;
                value = identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
                if (value >= min && value < max)
                    ret++;
            }
			return ret;
		}

		public Int64 GetInRangeArrayCharaDouble(FixedVariableTerm p, double min, double max, Int64 start, Int64 end)
		{
			Int64 ret = 0;
            double value;
            var identifier = p.Identifier;
            long[] arguments = new long[3] { -1, p.Index2, p.Index3 };
            for (int i = (int)start; i < (int)end; i++)
            {
                arguments[0] = i;
                value = identifier.GetFloatValue(GlobalStatic.EMediator, arguments);
                if (value >= min && value < max)
                    ret++;
            }

			return ret;
		}

		public void ShiftArray(FixedVariableTerm p, int shift, Int64 def, int start, int num)
		{
			object array;
            var identifier = p.Identifier;
            if (identifier.IsCharacterData)
                array = identifier.GetArrayChara((int)p.Index1);
			else
				array = identifier.GetArray();

			int arrayLength = Get1DLength(array);
			if (start >= arrayLength)
				throw new CodeEE("命令ARRAYSHIFTの第４引数(" + start.ToString() + ")が配列" + p.Identifier.Name + "の範囲を超えています");

			if (num == -1)
				num = arrayLength - start;
			if ((start + num) > arrayLength)
				num = arrayLength - start;

			if (array is SparseArray<Int64> sparseArray)
			{
				sparseArray.Shift(shift, def, start, num);
				return;
			}

			Int64[] denseArray = (Int64[])array;
			if (Math.Abs(shift) >= denseArray.Length && start == 0 && num >= denseArray.Length)
			{
				for (int i = 0; i < denseArray.Length; i++)
					denseArray[i] = def;
				return;
			}

			int sourceStart = 0;
			int destStart = start + shift;
			int length = num - Math.Abs(shift);
			if (shift < 0)
			{
				sourceStart = -shift;
				destStart = start;
			}
			Int64[] temp = new Int64[num];
			Buffer.BlockCopy(denseArray, start * 8, temp, 0, 8 * num);

			//これを満たすのはshift > 0であることは自明
			if (sourceStart == 0)
			{
				if (length > 0)
					for (int i = start; i < (start + shift); i++)
						denseArray[i] = def;
				else
				{
					for (int i = start; i < (start + num); i++)
						denseArray[i] = def;
					return;
				}
			}
			else
			{
				if (length > 0)
					for (int i = (start + length); i < (start + num); i++)
						denseArray[i] = def;
				else
				{
					for (int i = start; i < (start + num); i++)
						denseArray[i] = def;
					return;
				}
			}

			//if (start > 0)
			//    //Array.Copy(temp, 0, array, 0, start);
			//    Buffer.BlockCopy(temp, 0, array, 0, 8 * start);

			if (length > 0)
				//Array.Copy(temp, sourceStart, array, destStart, length);
				Buffer.BlockCopy(temp, sourceStart * 8, denseArray, destStart * 8, length * 8);

			//if ((start + num) < array.Length)
			//    //Array.Copy(temp, (start + num), array, (start + num), array.Length - (start + num));
			//    Buffer.BlockCopy(temp, (start + num) * 8, array, (start + num) * 8, (array.Length - (start + num)) * 8);
		}

		public void ShiftArray(FixedVariableTerm p, int shift, double def, int start, int num)
		{
			object array;
			var identifier = p.Identifier;
			if (identifier.IsCharacterData)
				array = identifier.GetArrayChara((int)p.Index1);
			else
				array = identifier.GetArray();

			int arrayLength = Get1DLength(array);
			if (start >= arrayLength)
				throw new CodeEE("命令ARRAYSHIFTの第４引数(" + start.ToString() + ")が配列" + p.Identifier.Name + "の範囲を超えています");

			if (num == -1)
				num = arrayLength - start;
			if ((start + num) > arrayLength)
				num = arrayLength - start;

			if (array is SparseArray<double> sparseArray)
			{
				sparseArray.Shift(shift, def, start, num);
				return;
			}

			double[] denseArray = (double[])array;
			if (Math.Abs(shift) >= denseArray.Length && start == 0 && num >= denseArray.Length)
			{
				for (int i = 0; i < denseArray.Length; i++)
					denseArray[i] = def;
				return;
			}

			int sourceStart = 0;
			int destStart = start + shift;
			int length = num - Math.Abs(shift);
			if (shift < 0)
			{
				sourceStart = -shift;
				destStart = start;
			}
			double[] temp = new double[num];
			Buffer.BlockCopy(denseArray, start * 8, temp, 0, 8 * num);

			if (sourceStart == 0)
			{
				if (length > 0)
					for (int i = start; i < (start + shift); i++)
						denseArray[i] = def;
				else
				{
					for (int i = start; i < (start + num); i++)
						denseArray[i] = def;
					return;
				}
			}
			else
			{
				if (length > 0)
					for (int i = (start + length); i < (start + num); i++)
						denseArray[i] = def;
				else
				{
					for (int i = start; i < (start + num); i++)
						denseArray[i] = def;
					return;
				}
			}

			if (length > 0)
				Buffer.BlockCopy(temp, sourceStart * 8, denseArray, destStart * 8, length * 8);
		}

		public void ShiftArray(FixedVariableTerm p, int shift, string def, int start, int num)
		{
			object arrays;
            var identifier = p.Identifier;
            if (identifier.IsCharacterData)
                arrays = identifier.GetArrayChara((int)p.Index1);
			else
				arrays = identifier.GetArray();

			int arrayLength = Get1DLength(arrays);
			if (start >= arrayLength)
				throw new CodeEE("命令ARRAYSHIFTの第４引数(" + start.ToString() + ")が配列" + p.Identifier.Name + "の範囲を超えています");

			//for (int i = 0; i < arrays.Length; i++)
			//    arrays[i] = "";
			//Array.Clear(arrays, 0, arrays.Length);

			if (num == -1)
				num = arrayLength - start;
			if ((start + num) > arrayLength)
				num = arrayLength - start;

			if (arrays is SparseArray<string> sparseArrays)
			{
				sparseArrays.Shift(shift, def, start, num);
				return;
			}

			string[] denseArrays = (string[])arrays;
			if (Math.Abs(shift) >= denseArrays.Length && start == 0 && num >= denseArrays.Length)
			{
				for (int i = 0; i < denseArrays.Length; i++)
					denseArrays[i] = def;
				return;
			}

			//if (start > 0)
			//    Array.Copy(temps, 0, arrays, 0, start);

			int sourceStart = 0;
			int destStart = start + shift;
			int length = num - Math.Abs(shift);
			if (shift < 0)
			{
				sourceStart = -shift;
				destStart = start;
			}
			string[] temps = new string[num];
			Array.Copy(denseArrays, start, temps, 0, num);

			if (destStart > start)
			{
				if (length > 0)
					for (int i = start; i < (start + shift); i++)
						denseArrays[i] = def;
				else
				{
					for (int i = start; i < (start + num); i++)
						denseArrays[i] = def;
					return;
				}
			}
			else
			{
				if (length > 0)
					for (int i = (start + length); i < (start + num); i++)
						denseArrays[i] = def;
				else
				{
					for (int i = start; i < (start + num); i++)
						denseArrays[i] = def;
					return;
				}
			}

			if (length > 0)
				Array.Copy(temps, sourceStart, denseArrays, destStart, length);
			//if ((start + num) < arrays.Length)
			//    Array.Copy(temps, (start + num), arrays, (start + num), arrays.Length - (start + num));
		}

		public void RemoveArray(FixedVariableTerm p, int start, int num)
		{
            var identifier = p.Identifier;
			if (identifier.IsInteger)
			{
				object array;
				if (identifier.IsCharacterData)
                    array = identifier.GetArrayChara((int)p.Index1);
				else
					array = identifier.GetArray();

				int arrayLength = Get1DLength(array);
                if (start >= arrayLength)
					throw new CodeEE("命令ARRAYREMOVEの第２引数(" + start.ToString() + ")が配列" + p.Identifier.Name + "の範囲を超えています");
				if (num <= 0)
					num = arrayLength;
				if (array is SparseArray<Int64> sparseArray)
				{
					sparseArray.RemoveRange(start, num);
					return;
				}

				Int64[] denseArray = (Int64[])array;
				Int64[] temp = new Int64[denseArray.Length];
				//array.CopyTo(temp, 0);
				//for (int i = 0; i < array.Length; i++)
				//    array[i] = 0;
				//Array.Clear(array, 0, array.Length);
				if (start > 0)
					//Array.Copy(array, 0, temp, 0, start);
					Buffer.BlockCopy(denseArray, 0, temp, 0, start * 8);
				if ((start + num) < denseArray.Length)
					//Array.Copy(array, (start + num), temp, start, (array.Length - (start + num)));
					Buffer.BlockCopy(denseArray, (start + num) * 8, temp, start * 8, (denseArray.Length - (start + num)) * 8);
				//temp.CopyTo(array, 0);
				Buffer.BlockCopy(temp, 0, denseArray, 0, temp.Length * 8);
			}
			else if (identifier.IsFloat)
			{
				object array;
				if (identifier.IsCharacterData)
					array = identifier.GetArrayChara((int)p.Index1);
				else
					array = identifier.GetArray();

				int arrayLength = Get1DLength(array);
				if (start >= arrayLength)
					throw new CodeEE("命令ARRAYREMOVEの第２引数(" + start.ToString() + ")が配列" + p.Identifier.Name + "の範囲を超えています");
				if (num <= 0)
					num = arrayLength;
				if (array is SparseArray<double> sparseArray)
				{
					sparseArray.RemoveRange(start, num);
					return;
				}

				double[] denseArray = (double[])array;
				double[] temp = new double[denseArray.Length];
				if (start > 0)
					Buffer.BlockCopy(denseArray, 0, temp, 0, start * 8);
				if ((start + num) < denseArray.Length)
					Buffer.BlockCopy(denseArray, (start + num) * 8, temp, start * 8, (denseArray.Length - (start + num)) * 8);
				Buffer.BlockCopy(temp, 0, denseArray, 0, temp.Length * 8);
			}
			else
			{
				object arrays;
				if (identifier.IsCharacterData)
                    arrays = identifier.GetArrayChara((int)p.Index1);
				else
					arrays = identifier.GetArray();

				// 参考侧 string 分支与 int/float 分支同样做 start 越界检查
				if (start >= Get1DLength(arrays))
					throw new CodeEE("命令ARRAYREMOVEの第２引数(" + start.ToString() + ")が配列" + p.Identifier.Name + "の範囲を超えています");
                if (num <= 0)
					num = Get1DLength(arrays);
				if (arrays is SparseArray<string> sparseArrays)
				{
					sparseArrays.RemoveRange(start, num);
					return;
				}

				string[] denseArrays = (string[])arrays;
				string[] temps = new string[denseArrays.Length];
				//arrays.CopyTo(temps, 0);
				//for (int i = 0; i < arrays.Length; i++)
				//    arrays[i] = "";
				if (start > 0)
					Array.Copy(denseArrays, 0, temps, 0, start);
				if ((start + num) < denseArrays.Length)
					Array.Copy(denseArrays, (start + num), temps, start, (denseArrays.Length - (start + num)));
				temps.CopyTo(denseArrays, 0);
			}
		}

		public void SortArray(FixedVariableTerm p, SortOrder order, int start, int num)
		{
			if (order == SortOrder.UNDEF)
				order = SortOrder.ASCENDING;
            var identifier = p.Identifier;
            if (identifier.IsInteger)
			{
				object array;
				if (identifier.IsCharacterData)
                    array = identifier.GetArrayChara((int)p.Index1);
				else
					array = identifier.GetArray();

				int arrayLength = Get1DLength(array);
                if (start >= arrayLength)
					throw new CodeEE("命令ARRAYSORTの第３引数(" + start.ToString() + ")が配列" + identifier.Name + "の範囲を超えています");
				if (num <= 0)
					num = arrayLength - start;
				if (array is SparseArray<Int64> sparseArray)
				{
					sparseArray.Sort(order == SortOrder.ASCENDING, start, num);
					return;
				}

				Int64[] denseArray = (Int64[])array;
				Int64[] temp = new Int64[num];
				Array.Copy(denseArray, start, temp, 0, num);

				if (order == SortOrder.ASCENDING)
					Array.Sort(temp);
				else if (order == SortOrder.DESENDING)
					Array.Sort(temp, delegate(Int64 a, Int64 b) { return b.CompareTo(a); });
				Array.Copy(temp, 0, denseArray, start, num);
			}
			else if (identifier.IsFloat)
			{
				object array;
				if (identifier.IsCharacterData)
					array = identifier.GetArrayChara((int)p.Index1);
				else
					array = identifier.GetArray();

				int arrayLength = Get1DLength(array);
				if (start >= arrayLength)
					throw new CodeEE("命令ARRAYSORTの第３引数(" + start.ToString() + ")が配列" + identifier.Name + "の範囲を超えています");
				if (num <= 0)
					num = arrayLength - start;
				if (array is SparseArray<double> sparseArray)
				{
					sparseArray.Sort(order == SortOrder.ASCENDING, start, num);
					return;
				}

				double[] denseArray = (double[])array;
				double[] temp = new double[num];
				Array.Copy(denseArray, start, temp, 0, num);

				if (order == SortOrder.ASCENDING)
					Array.Sort(temp);
				else if (order == SortOrder.DESENDING)
					Array.Sort(temp, delegate(double a, double b) { return b.CompareTo(a); });
				Array.Copy(temp, 0, denseArray, start, num);
			}
			else
			{
				object array;
				if (identifier.IsCharacterData)
                    array = identifier.GetArrayChara((int)p.Index1);
                else
					array = identifier.GetArray();

				int arrayLength = Get1DLength(array);
                if (start >= arrayLength)
					throw new CodeEE("命令ARRAYSORTの第３引数(" + start.ToString() + ")が配列" + identifier.Name + "の範囲を超えています");
				if (num <= 0)
					num = arrayLength - start;
				if (array is SparseArray<string> sparseArray)
				{
					sparseArray.Sort(order == SortOrder.ASCENDING, start, num);
					return;
				}

				string[] denseArray = (string[])array;
				string[] temp = new string[num];
				Array.Copy(denseArray, start, temp, 0, num);

				if (order == SortOrder.ASCENDING)
					Array.Sort(temp);
				else if (order == SortOrder.DESENDING)
					Array.Sort(temp, delegate(string a, string b) { return b.CompareTo(a); });
				Array.Copy(temp, 0, denseArray, start, num);
			}
		}



		public void CopyArray(VariableToken var1, VariableToken var2)
		{
			if (var1.IsInteger)
			{
				if (var1.IsArray1D)
				{
					object array1 = var1.GetArray();
					object array2 = var2.GetArray();
					int length = Math.Min(Get1DLength(array1), Get1DLength(array2));
					for (int i = 0; i < length; i++)
						SetInt1D(array2, i, GetInt1D(array1, i));
				}
				else if (var1.IsArray2D)
				{
					Int64[,] array1 = (Int64[,])var1.GetArray();
					Int64[,] array2 = (Int64[,])var2.GetArray();
					int length1 = (array1.GetLength(0) >= array2.GetLength(0)) ? array2.GetLength(0) : array1.GetLength(0);
					int length2 = (array1.GetLength(1) >= array2.GetLength(1)) ? array2.GetLength(1) : array1.GetLength(1);
					for (int i = 0; i < length1; i++)
					{
						for (int j = 0; j < length2; j++)
							array2[i, j] = array1[i, j];
					}
				}
				else
				{
					Int64[, ,] array1 = (Int64[, ,])var1.GetArray();
					Int64[, ,] array2 = (Int64[, ,])var2.GetArray();
					int length1 = (array1.GetLength(0) >= array2.GetLength(0)) ? array2.GetLength(0) : array1.GetLength(0);
					int length2 = (array1.GetLength(1) >= array2.GetLength(1)) ? array2.GetLength(1) : array1.GetLength(1);
					int length3 = (array1.GetLength(2) >= array2.GetLength(2)) ? array2.GetLength(2) : array1.GetLength(2);
					for (int i = 0; i < length1; i++)
					{
						for (int j = 0; j < length2; j++)
						{
							for (int k = 0; k < length3; k++)
								array2[i, j, k] = array1[i, j, k];
						}
					}
				}
			}
			else if (var1.IsFloat)
			{
				if (var1.IsArray1D)
				{
					object array1 = var1.GetArray();
					object array2 = var2.GetArray();
					int length = Math.Min(Get1DLength(array1), Get1DLength(array2));
					for (int i = 0; i < length; i++)
						SetFloat1D(array2, i, GetFloat1D(array1, i));
				}
				else if (var1.IsArray2D)
				{
					double[,] array1 = (double[,])var1.GetArray();
					double[,] array2 = (double[,])var2.GetArray();
					int length1 = (array1.GetLength(0) >= array2.GetLength(0)) ? array2.GetLength(0) : array1.GetLength(0);
					int length2 = (array1.GetLength(1) >= array2.GetLength(1)) ? array2.GetLength(1) : array1.GetLength(1);
					for (int i = 0; i < length1; i++)
					{
						for (int j = 0; j < length2; j++)
							array2[i, j] = array1[i, j];
					}
				}
				else
				{
					double[, ,] array1 = (double[, ,])var1.GetArray();
					double[, ,] array2 = (double[, ,])var2.GetArray();
					int length1 = (array1.GetLength(0) >= array2.GetLength(0)) ? array2.GetLength(0) : array1.GetLength(0);
					int length2 = (array1.GetLength(1) >= array2.GetLength(1)) ? array2.GetLength(1) : array1.GetLength(1);
					int length3 = (array1.GetLength(2) >= array2.GetLength(2)) ? array2.GetLength(2) : array1.GetLength(2);
					for (int i = 0; i < length1; i++)
					{
						for (int j = 0; j < length2; j++)
						{
							for (int k = 0; k < length3; k++)
								array2[i, j, k] = array1[i, j, k];
						}
					}
				}
			}
			else
			{
				if (var1.IsArray1D)
				{
					object array1 = var1.GetArray();
					object array2 = var2.GetArray();
					int length = Math.Min(Get1DLength(array1), Get1DLength(array2));
					for (int i = 0; i < length; i++)
						SetStr1D(array2, i, GetStr1D(array1, i));
				}
				else if (var1.IsArray2D)
				{
					string[,] array1 = (string[,])var1.GetArray();
					string[,] array2 = (string[,])var2.GetArray();
					int length1 = (array1.GetLength(0) >= array2.GetLength(0)) ? array2.GetLength(0) : array1.GetLength(0);
					int length2 = (array1.GetLength(1) >= array2.GetLength(1)) ? array2.GetLength(1) : array1.GetLength(1);
					for (int i = 0; i < length1; i++)
					{
						for (int j = 0; j < length2; j++)
							array2[i, j] = array1[i, j];
					}
				}
				else
				{
					string[, ,] array1 = (string[, ,])var1.GetArray();
					string[, ,] array2 = (string[, ,])var2.GetArray();
					int length1 = (array1.GetLength(0) >= array2.GetLength(0)) ? array2.GetLength(0) : array1.GetLength(0);
					int length2 = (array1.GetLength(1) >= array2.GetLength(1)) ? array2.GetLength(1) : array1.GetLength(1);
					int length3 = (array1.GetLength(2) >= array2.GetLength(2)) ? array2.GetLength(2) : array1.GetLength(2);
					for (int i = 0; i < length1; i++)
					{
						for (int j = 0; j < length2; j++)
						{
							for (int k = 0; k < length3; k++)
								array2[i, j, k] = array1[i, j, k];
						}
					}
				}
			}
		}

		static int Get1DLength(object array)
		{
			if (array is SparseArray<Int64> sparseLong)
				return sparseLong.Length;
			if (array is SparseArray<string> sparseString)
				return sparseString.Length;
			if (array is SparseArray<double> sparseDouble)
				return sparseDouble.Length;
			if (array is Array denseArray)
				return denseArray.Length;
			throw new ExeEE("配列データの型が不正です");
		}

		static Int64 GetInt1D(object array, long index)
		{
			if (array is SparseArray<Int64> sparseArray)
				return sparseArray[index];
			return ((Int64[])array)[index];
		}

		static void SetInt1D(object array, long index, Int64 value)
		{
			if (array is SparseArray<Int64> sparseArray)
				sparseArray[index] = value;
			else
				((Int64[])array)[index] = value;
		}

		static double GetFloat1D(object array, long index)
		{
			if (array is SparseArray<double> sparseArray)
				return sparseArray[index];
			return ((double[])array)[index];
		}

		static void SetFloat1D(object array, long index, double value)
		{
			if (array is SparseArray<double> sparseArray)
				sparseArray[index] = value;
			else
				((double[])array)[index] = value;
		}

		static string GetStr1D(object array, long index)
		{
			if (array is SparseArray<string> sparseArray)
				return sparseArray[index];
			return ((string[])array)[index];
		}

		static void SetStr1D(object array, long index, string value)
		{
			if (array is SparseArray<string> sparseArray)
				sparseArray[index] = value;
			else
				((string[])array)[index] = value;
		}

		static string JoinString1D(string delimiter, object array, int start, int count)
		{
			if (array is string[] denseArray)
				return string.Join(delimiter, denseArray, start, count);

			var sparseArray = (SparseArray<string>)array;
			var builder = new StringBuilder();
			for (int i = 0; i < count; i++)
			{
				if (i > 0)
					builder.Append(delimiter);
				builder.Append(sparseArray[start + i]);
			}
			return builder.ToString();
		}


		public string GetHavingItemsString()
		{
			SparseArray<Int64> array = this.ITEM;
			string[] itemnames = this.ITEMNAME;
			int length = Math.Min(array.Length, itemnames.Length);
			int count = 0;
			StringBuilder builder = new StringBuilder(100);
			builder.Append("所持アイテム：");
			for (int i = 0; i < length; i++)
			{
				if (array[i] == 0)
					continue;
				count++;
				if (itemnames[i] != null)
					builder.Append(itemnames[i]);
				builder.Append("(");
				builder.Append(array[i].ToString());
				builder.Append(") ");
			}
			if (count == 0)
				builder.Append("なし");
			return builder.ToString();
		}

		//public string GetItemSalesString()
		//{
		//	Int64[] itemsales = varData.DataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.ITEMSALES];
		//	string[] itemname = constant.GetCsvNameList(VariableCode.ITEMNAME);
		//	StringBuilder builder = new StringBuilder(100);
		//	for (int i = 0; i < itemsales.Length; i++)
		//	{
		//		if (itemsales[i] != 0)
		//			continue;
		//		builder.Append(itemname[i]);
		//		builder.Append("(");
		//		builder.Append(itemsales[i].ToString());
		//		builder.Append(")");
		//	}
		//	return builder.ToString();
		//}

		public string GetCharacterDataString(Int64 target, FunctionCode func)
		{
			StringBuilder builder = new StringBuilder(100);
			if ((target < 0) || (target >= varData.CharacterList.Count))
				throw new CodeEE("存在しない登録キャラクタを参照しようとしました");
			CharacterData chara = varData.CharacterList[(int)target];
			SparseArray<Int64> array = null;
			string[] arrayName = null;
			int i = 0;
			switch (func)
			{
				case FunctionCode.PRINT_ABL:
					array = chara.DataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.ABL];
					arrayName = constant.GetCsvNameList(VariableCode.ABLNAME);
					for (i = 0; i < array.Length; i++)
					{
						if (i >= arrayName.Length)
							break;
						if (array[i] == 0)
							continue;
						if (string.IsNullOrEmpty(arrayName[i]))
							continue;
						builder.Append(arrayName[i]);
						builder.Append("LV");
						builder.Append(array[i].ToString());
						builder.Append(" ");

					}
					break;
				case FunctionCode.PRINT_TALENT:
					array = chara.DataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.TALENT];
					arrayName = constant.GetCsvNameList(VariableCode.TALENTNAME);
					for (i = 0; i < array.Length; i++)
					{
						if (i >= arrayName.Length)
							break;
						if (array[i] == 0)
							continue;
						if (string.IsNullOrEmpty(arrayName[i]))
							continue;
						builder.Append("[");
						builder.Append(arrayName[i]);
						builder.Append("]");
					}
					break;
				case FunctionCode.PRINT_MARK:
					array = chara.DataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MARK];
					arrayName = constant.GetCsvNameList(VariableCode.MARKNAME);
					for (i = 0; i < array.Length; i++)
					{
						if (i >= arrayName.Length)
							break;
						if (array[i] == 0)
							continue;
						if (string.IsNullOrEmpty(arrayName[i]))
							continue;
						builder.Append(arrayName[i]);
						builder.Append("LV");
						builder.Append(array[i].ToString());
						builder.Append(" ");
					}
					break;
				case FunctionCode.PRINT_EXP:
					array = chara.DataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EXP];
					arrayName = constant.GetCsvNameList(VariableCode.EXPNAME);
					for (i = 0; i < array.Length; i++)
					{
						if (i >= arrayName.Length)
							break;
						if (array[i] == 0)
							continue;
						if (string.IsNullOrEmpty(arrayName[i]))
							continue;
						builder.Append(arrayName[i]);
						builder.Append(array[i].ToString());
						builder.Append(" ");
					}
					break;
				//現状ここに来ることはないはず
				//default:
				//    throw new ExeEE("未定義の関数");
			}
			return builder.ToString();
		}

		public string GetCharacterParamString(Int64 target, int paramCode)
		{
			if ((target < 0) || (target >= varData.CharacterList.Count))
				throw new CodeEE("存在しない登録キャラクタを参照しようとしました");
			//そもそも呼び出し元がint i = 0; i < 100; i++)でこの条件が満たされる可能性0
			//if ((paramCode < 0) || (paramCode >= constant.ParamName.Length))
			//    throw new ExeEE("存在しない名称を取得しようとした");
			CharacterData chara = varData.CharacterList[(int)target];
			Int64 param = chara.DataIntegerArray[(int)(VariableCode.PALAM & VariableCode.__LOWERCASE__)][paramCode];
			SparseArray<Int64> paramlv = varData.DataIntegerArray[(int)(VariableCode.PALAMLV & VariableCode.__LOWERCASE__)];
			string paramName = constant.GetCsvNameList(VariableCode.PALAMNAME)[paramCode];
			if ((param == 0) && (string.IsNullOrEmpty(paramName)))
				return null;
			if (paramName == null)
				paramName = "";
			char c = '-';
			Int64 border = paramlv[1];
			if (param >= border)
			{
				c = '=';
				border = paramlv[2];
			}
			if (param >= border)
			{
				c = '>';
				border = paramlv[3];
			}
			if (param >= border)
			{
				c = '*';
				border = paramlv[4];
			}
			StringBuilder bar = new StringBuilder(100);

			bar.Append('[');
			if ((border <= 0) || (border <= param))
				bar.Append(c, 10);
			else if (param <= 0)
				bar.Append('.', 10);
			else
			{
				unchecked
				{
					int count = (int)(param * 10 / border);
					bar.Append(c, count);
					bar.Append('.', 10 - count);
				}
			}
			bar.Append(']');
			return string.Format("{0}{1}{2,6}", paramName, bar.ToString(), param);

		}

		public void AddCharacter(Int64 charaTmplNo)
		{
			CharacterTemplate tmpl = constant.GetCharacterTemplate(charaTmplNo);
			if (tmpl == null)
			{
				GenericUtils.Error($"[CHARA] ADDCHARA failed: template {charaTmplNo} not found");
				throw new CodeEE("定義していないキャラクタを作成しようとしました");
			}
			CharacterData chara = new CharacterData(constant, tmpl, varData);
			varData.CharacterList.Add(chara);
		}

		public void AddCharacter_UseSp(Int64 charaTmplNo, bool isSp)
		{
			CharacterTemplate tmpl = constant.GetCharacterTemplate_UseSp(charaTmplNo, isSp);
			if (tmpl == null)
				throw new CodeEE("定義していないキャラクタを作成しようとしました");
			CharacterData chara = new CharacterData(constant, tmpl, varData);
			varData.CharacterList.Add(chara);
		}

		public void AddCharacterFromCsvNo(Int64 CsvNo)
		{
			CharacterTemplate tmpl = constant.GetCharacterTemplateFromCsvNo(CsvNo);
			if (tmpl == null)
				//throw new CodeEE("定義していないキャラクタを作成しようとしました");
				tmpl = constant.GetPseudoChara();
			CharacterData chara = new CharacterData(constant, tmpl, varData);
			varData.CharacterList.Add(chara);
		}

		public void AddPseudoCharacter()
		{
			CharacterTemplate tmpl = constant.GetPseudoChara();
			CharacterData chara = new CharacterData(constant, tmpl, varData);
			varData.CharacterList.Add(chara);
		}

		public void DelCharacter(Int64 charaNo)
		{
			if ((charaNo < 0) || (charaNo >= varData.CharacterList.Count))
				throw new CodeEE("存在しない登録キャラクタ(" + charaNo.ToString() + ")を削除しようとしました");
			varData.CharacterList[(int)charaNo].Dispose();
			varData.CharacterList.RemoveAt((int)charaNo);
		}

		public void DelCharacter(Int64[] charaNoList)
		{
			List<CharacterData> DelList = new List<CharacterData>();
			foreach(Int64 charaNo in charaNoList)
			{
				if ((charaNo < 0) || (charaNo >= varData.CharacterList.Count))
					throw new CodeEE("存在しない登録キャラクタ(" + charaNoList.ToString() + ")を削除しようとしました");
				CharacterData chara = varData.CharacterList[(int)charaNo];
				if (DelList.Contains(chara))
					throw new CodeEE("同一の登録キャラクタ番号(" + charaNo.ToString() + ")が複数回指定されました");
				DelList.Add(chara);
				chara.Dispose();
			}
			foreach (CharacterData chara in DelList)
				varData.CharacterList.Remove(chara);
		}

		public void DelAllCharacter()
		{
			if (varData.CharacterList.Count == 0)
				return;
			foreach (CharacterData chara in varData.CharacterList)
				chara.Dispose();
			varData.CharacterList.Clear();
		}

		public void PickUpChara(Int64[] NoList)
		{
			List<Int64> pickList = new List<long>();
			Int64 oldTarget = this.TARGET;
			Int64 oldAssi = this.ASSI;
			Int64 oldMaster = this.MASTER;
			this.TARGET = -1;
			this.ASSI = -1;
			this.MASTER = -1;
			//同じキャラが複数出てこないようにリストを整理
			for (int i = 0; i < NoList.Length; i++)
			{
				if (!pickList.Contains(NoList[i]) && NoList[i] >= 0)
					pickList.Add(NoList[i]);
			}
			for (int i = 0; i < pickList.Count; i++)
			{
				if (i != pickList[i])
				{
					SwapChara(pickList[i], (Int64)i);
					if (pickList.IndexOf((Int64)i) > i)
						pickList[pickList.IndexOf((Int64)i)] = pickList[i];
				}
				if (this.TARGET < 0 && pickList[i] == oldTarget)
					this.TARGET = i;
				if (this.ASSI < 0 && pickList[i] == oldAssi)
					this.ASSI = i;
				if (this.MASTER < 0 && pickList[i] == oldMaster)
					this.MASTER = i;
			}
			if (pickList.Count < varData.CharacterList.Count)
			{
				for (int i = (varData.CharacterList.Count - 1); i >= pickList.Count; i--)
					DelCharacter((Int64)i);
			}
		}

		public void ResetData()
		{
			//グローバルは初期化しない方が都合がよい。
			//varData.SetDefaultGlobalValue();
			RuntimeDataStore.ClearSaveData(constant);
			varData.SetDefaultLocalValue();
			varData.SetDefaultValue(constant);
			foreach (CharacterData chara in varData.CharacterList)
				chara.Dispose();
			varData.CharacterList.Clear();
		}

		public void ResetGlobalData()
		{
			RuntimeDataStore.ClearGlobalData(constant);
			RuntimeDataStore.ClearStaticData(constant);
			varData.SetDefaultGlobalValue();
		}

		public void CopyChara(Int64 x, Int64 y)
		{
			if ((x < 0) || (x >= varData.CharacterList.Count))
				throw new CodeEE("コピー元のキャラクタが存在しません");
			if ((y < 0) || (y >= varData.CharacterList.Count))
				throw new CodeEE("コピー先のキャラクタが存在しません");
			varData.CharacterList[(int)x].CopyTo(varData.CharacterList[(int)y], varData);
		}

		public void AddCopyChara(Int64 x)
		{
			if ((x < 0) || (x >= varData.CharacterList.Count))
				throw new CodeEE("コピー元のキャラクタが存在しません");
			AddPseudoCharacter();
			varData.CharacterList[(int)x].CopyTo(varData.CharacterList[varData.CharacterList.Count - 1], varData);
		}

		public void SwapChara(Int64 x, Int64 y)
		{
			if (((x < 0) || (x >= varData.CharacterList.Count)) || ((y < 0) || (y >= varData.CharacterList.Count)))
				throw new CodeEE("存在しない登録キャラクタを入れ替えようとしました");
			if (x == y)
				return;
			CharacterData data = varData.CharacterList[(int)y];
			varData.CharacterList[(int)y] = varData.CharacterList[(int)x];
			varData.CharacterList[(int)x] = data;
		}

		public void SortChara(VariableToken sortkey, Int64 elem, SortOrder sortorder, bool fixMaster)
		{
			if (varData.CharacterList.Count <= 1)
				return;
			if (sortorder == SortOrder.UNDEF)
				sortorder = SortOrder.ASCENDING;
			if (sortkey == null)
				sortkey = GlobalStatic.VariableData.GetSystemVariableToken("NO");
			CharacterData masterChara = null;
			CharacterData targetChara = null;
			CharacterData assiChara = null;
			if (this.MASTER >= 0 && this.MASTER < varData.CharacterList.Count)
				masterChara = varData.CharacterList[(int)this.MASTER];
			if (this.TARGET >= 0 && this.TARGET < varData.CharacterList.Count)
				targetChara = varData.CharacterList[(int)this.TARGET];
			if (this.ASSI >= 0 && this.ASSI < varData.CharacterList.Count)
				assiChara = varData.CharacterList[(int)this.ASSI];

			for (int i = 0; i < varData.CharacterList.Count; i++)
			{
				varData.CharacterList[i].temp_CurrentOrder = i;
				varData.CharacterList[i].SetSortKey(sortkey, elem);
			}
			if ((fixMaster) && (masterChara != null))
			{
				if (varData.CharacterList.Count <= 2)
					return;
				varData.CharacterList.Remove(masterChara);
			}
			if (sortorder == SortOrder.ASCENDING)
				varData.CharacterList.Sort(CharacterData.AscCharacterComparison);
			else// if (sortorder == SortOrder.DESENDING)
				varData.CharacterList.Sort(CharacterData.DescCharacterComparison);
			//引数解析でチェック済み
			//else
			//    throw new ExeEE("ソート順序不明");

			if ((fixMaster) && (masterChara != null))
			{
				varData.CharacterList.Insert((int)this.MASTER, masterChara);
			}
			for (int i = 0; i < varData.CharacterList.Count; i++)
				varData.CharacterList[i].temp_CurrentOrder = i;
			if ((masterChara != null) && (!fixMaster))
				this.MASTER = masterChara.temp_CurrentOrder;
			if (targetChara != null)
				this.TARGET = targetChara.temp_CurrentOrder;
			if (assiChara != null)
				this.ASSI = assiChara.temp_CurrentOrder;
		}


		internal Int64 FindChara(VariableToken varID, Int64 elem64, string word, Int64 startIndex, Int64 lastIndex, bool isLast)
		{
			if (startIndex >= lastIndex)
				return -1;
			FixedVariableTerm fvp = new FixedVariableTerm(varID);
			if (varID.IsArray1D)
				fvp.Index2 = elem64;
			else if (varID.IsArray2D)
			{
				fvp.Index2 = elem64 >> 32;
				fvp.Index3 = elem64 & 0x7FFFFFFF;
			}
			//int count = varData.CharacterList.Count;
			if (isLast)
			{
				for (Int64 i = lastIndex - 1; i >= startIndex; i--)
				{
					fvp.Index1 = i;
					if (word == fvp.GetStrValue(null))
						return i;
				}
			}
			else
			{
				for (Int64 i = startIndex; i < lastIndex; i++)
				{
					fvp.Index1 = i;
					if (word == fvp.GetStrValue(null))
						return i;
				}
			}
			return -1;
		}

		internal Int64 FindChara(VariableToken varID, Int64 elem64, Int64 word, Int64 startIndex, Int64 lastIndex, bool isLast)
		{
			if (startIndex >= lastIndex)
				return -1;
			FixedVariableTerm fvp = new FixedVariableTerm(varID);
			if (varID.IsArray1D)
				fvp.Index2 = elem64;
			else if (varID.IsArray2D)
			{
				fvp.Index2 = elem64 >> 32;
				fvp.Index3 = elem64 & 0x7FFFFFFF;
			}
			//int count = varData.CharacterList.Count;
			if (isLast)
			{
				for (Int64 i = lastIndex - 1; i >= startIndex; i--)
				{
					fvp.Index1 = i;
					if (word == fvp.GetIntValue(null))
						return i;
				}
			}
			else
			{
				for (Int64 i = startIndex; i < lastIndex; i++)
				{
					fvp.Index1 = i;
					if (word == fvp.GetIntValue(null))
						return i;
				}
			}
			return -1;
		}

		public Int64 GetChara(Int64 charaNo)
		{
			int i;
			for (i = 0; i < varData.CharacterList.Count; i++)
			{
				if (varData.CharacterList[i].NO == charaNo)
					return (Int64)i;
			}
			return -1;
		}
		
		public Int64 GetChara_UseSp(Int64 charaNo, bool getSp)
		{
			//後天的にNOを変更する場合も考慮し、chara*.csvで定義されているかどうかは調べない。
			//CharacterTemplate tmpl = constant.GetCharacterTemplate(charaNo, false);
			//if (tmpl == null)
			//    return -1;
			int i;
			for (i = 0; i < varData.CharacterList.Count; i++)
			{
				if (varData.CharacterList[i].NO == charaNo)
				{
					bool isSp = varData.CharacterList[i].CFlag[0] != 0;
					if (isSp == getSp)
						return (Int64)i;
				}
			}
			return -1;
		}

		public Int64 ExistCsv(Int64 charaNo, bool getSp)
		{
			//SPキャラ廃止に伴う問題は呼び出し元で処理
			CharacterTemplate tmpl = constant.GetCharacterTemplate_UseSp(charaNo, getSp);
			if (tmpl == null)
				return 0;
			else
				return 1;
		}

		public string GetCharacterStrfromCSVData(Int64 charaTmplNo, CharacterStrData type, bool isSp, Int64 arg2Long)
		{
			//SPキャラ廃止に伴う問題は呼び出し元で処理
			CharacterTemplate tmpl = constant.GetCharacterTemplate_UseSp(charaTmplNo, isSp);
			if (tmpl == null)
				throw new CodeEE("定義していないキャラクタを参照しようとしました");
			int arg2 = (int)arg2Long;
			switch (type)
			{
				case CharacterStrData.CALLNAME:
					if (tmpl.Callname != null)
						return tmpl.Callname;
					else
						return "";
				case CharacterStrData.NAME:
					if (tmpl.Name != null)
						return tmpl.Name;
					else
						return "";
				case CharacterStrData.NICKNAME:
					if (tmpl.Nickname != null)
						return tmpl.Nickname;
					else
						return "";
				case CharacterStrData.MASTERNAME:
					if (tmpl.Mastername != null)
						return tmpl.Mastername;
					else
						return "";
				case CharacterStrData.CSTR:
					if (tmpl.CStr != null)
					{
						string ret = null;
						if (arg2 >= tmpl.ArrayStrLength(CharacterStrData.CSTR) || arg2 < 0)
							throw new CodeEE("CSTRの参照可能範囲外を参照しました");
						if (tmpl.CStr.TryGetValue(arg2, out ret))
							return ret;
						else
							return "";
					}
					else
						return "";
				default:
					throw new CodeEE("存在しないデータを参照しようとしました");
			}
		}

		public Int64 GetCharacterIntfromCSVData(Int64 charaTmplNo, CharacterIntData type, bool isSp, Int64 arg2Long)
		{
			//SPキャラ廃止に伴う問題は呼び出し元で処理
			CharacterTemplate tmpl = constant.GetCharacterTemplate_UseSp(charaTmplNo, isSp);
			if (tmpl == null)
				throw new CodeEE("定義していないキャラクタを参照しようとしました");
			if (arg2Long >= tmpl.ArrayLength(type) || arg2Long < 0)
				throw new CodeEE("参照可能範囲外を参照しました");
			int arg2 = (int)arg2Long;
			Dictionary<int, Int64> intDic = null;
			switch (type)
			{
				case CharacterIntData.BASE:
					intDic = tmpl.Maxbase; break;
				case CharacterIntData.MARK:
					intDic = tmpl.Mark; break;
				case CharacterIntData.ABL:
					intDic = tmpl.Abl; break;
				case CharacterIntData.EXP:
					intDic = tmpl.Exp; break;
				case CharacterIntData.RELATION:
					intDic = tmpl.Relation; break;
				case CharacterIntData.TALENT:
					intDic = tmpl.Talent; break;
				case CharacterIntData.CFLAG:
					intDic = tmpl.CFlag; break;
				case CharacterIntData.EQUIP:
					intDic = tmpl.Equip; break;
				case CharacterIntData.JUEL:
					intDic = tmpl.Juel; break;
				default:
					throw new CodeEE("存在しないデータを参照しようとしました");
			}
			Int64 ret;
			if (intDic.TryGetValue(arg2, out ret))
				return ret;
			return 0L;
		}

		public void UpdateInBeginTrain()
		{
			ASSIPLAY = 0;
			PREVCOM = -1;
			NEXTCOM = -1;
			SparseArray<Int64> array;
			SparseArray<string> sarray;
			array = varData.DataIntegerArray[(int)(VariableCode.TFLAG & VariableCode.__LOWERCASE__)];
			for (int i = 0; i < array.Length; i++)
				array[i] = 0;
			sarray = varData.DataStringArray[(int)(VariableCode.TSTR & VariableCode.__LOWERCASE__)];
			for (int i = 0; i < sarray.Length; i++)
				sarray[i] = "";
			//本家の仕様にあわせ、選択中以外のキャラクタも全部リセット。
			foreach (CharacterData chara in varData.CharacterList)
			{
				array = chara.DataIntegerArray[(int)(VariableCode.GOTJUEL & VariableCode.__LOWERCASE__)];
				for (int i = 0; i < array.Length; i++)
					array[i] = 0;
				array = chara.DataIntegerArray[(int)(VariableCode.TEQUIP & VariableCode.__LOWERCASE__)];
				for (int i = 0; i < array.Length; i++)
					array[i] = 0;
				array = chara.DataIntegerArray[(int)(VariableCode.EX & VariableCode.__LOWERCASE__)];
				for (int i = 0; i < array.Length; i++)
					array[i] = 0;
				//STAINは関数に切り出す（RESET_STAIN対応のため）
				setDefaultStain(chara);
				array = chara.DataIntegerArray[(int)(VariableCode.PALAM & VariableCode.__LOWERCASE__)];
				for (int i = 0; i < array.Length; i++)
					array[i] = 0;
				//1.728 このタイミングでSOURCEも更新されていた
				array = chara.DataIntegerArray[(int)(VariableCode.SOURCE & VariableCode.__LOWERCASE__)];
				for (int i = 0; i < array.Length; i++)
					array[i] = 0;
				//1.728 NOWEXはここでは更新されていない
				//1736f CTFLAGはTFLAGと同じ仕様で
				array = chara.DataIntegerArray[(int)(VariableCode.TCVAR & VariableCode.__LOWERCASE__)];
				for (int i = 0; i < array.Length; i++)
					array[i] = 0;
			}
		}

		public void UpdateAfterShowUsercom()
		{
			//UP = 0,DOWN = 0,LOSEBASE = 0
			SparseArray<Int64> array;
			array = varData.DataIntegerArray[(int)(VariableCode.UP & VariableCode.__LOWERCASE__)];
			for (int i = 0; i < array.Length; i++)
				array[i] = 0;
			array = varData.DataIntegerArray[(int)(VariableCode.DOWN & VariableCode.__LOWERCASE__)];
			for (int i = 0; i < array.Length; i++)
				array[i] = 0;
			array = varData.DataIntegerArray[(int)(VariableCode.LOSEBASE & VariableCode.__LOWERCASE__)];
			for (int i = 0; i < array.Length; i++)
				array[i] = 0;
			foreach (CharacterData chara in varData.CharacterList)
			{
				array = chara.DataIntegerArray[(int)(VariableCode.DOWNBASE & VariableCode.__LOWERCASE__)];
				for (int i = 0; i < array.Length; i++)
					array[i] = 0;
				array = chara.DataIntegerArray[(int)(VariableCode.CUP & VariableCode.__LOWERCASE__)];
				for (int i = 0; i < array.Length; i++)
					array[i] = 0;
				array = chara.DataIntegerArray[(int)(VariableCode.CDOWN & VariableCode.__LOWERCASE__)];
				for (int i = 0; i < array.Length; i++)
					array[i] = 0;
			}

			//SOURCEはリセットタイミングが違うので消し
			//1.728 NOWEXも微妙に違うので移動
		}

		//1.728 NOWEXもリセットタイミングが違うので移動
		//UP,DOWN,LOSEBASEはUSERCOMに移動する場合にもリセットされるがNOWEXはCOMが実行される場合のみ更新される
		//なのでEVENTCOM直前に呼ぶ
		public void UpdateAfterInputCom()
		{
			//本家の仕様にあわせ、選択中以外のキャラクタも全部リセット。
			SparseArray<Int64> array;
			foreach (CharacterData chara in varData.CharacterList)
			{
				array = chara.DataIntegerArray[(int)(VariableCode.NOWEX & VariableCode.__LOWERCASE__)];
				for (int i = 0; i < array.Length; i++)
					array[i] = 0;
			}
		}

		//SOURCEのリセットタイミングはUP、DOWN、LOSEBASE、NOWEXと違いSOURCECHECK終了後なので切り分け
		public void UpdateAfterSourceCheck()
		{
			//本家の仕様にあわせ、選択中以外のキャラクタも全部リセット。
			SparseArray<Int64> array;
			foreach (CharacterData chara in varData.CharacterList)
			{
				array = chara.DataIntegerArray[(int)(VariableCode.SOURCE & VariableCode.__LOWERCASE__)];
				for (int i = 0; i < array.Length; i++)
					array[i] = 0;
			}
		}

		//PREVCOMは更新されない。スクリプトの方で更新する必要がある。
		//Data側からEmueraConsoleを操作するのはここだけ。
		//1756 ↑だったのは今は昔の話である
		public void UpdateInUpcheck(EmueraConsole window, bool skipPrint)
		{
			SparseArray<Int64> up, down, param;
			string[] paramname = constant.GetCsvNameList(VariableCode.PALAMNAME);
			up = varData.DataIntegerArray[(int)(VariableCode.UP & VariableCode.__LOWERCASE__)];
			down = varData.DataIntegerArray[(int)(VariableCode.DOWN & VariableCode.__LOWERCASE__)];
			Int64 target = TARGET;
			if ((target < 0) || (target >= varData.CharacterList.Count))
				goto end;
			CharacterData chara = varData.CharacterList[(int)target];
			param = chara.DataIntegerArray[(int)(VariableCode.PALAM & VariableCode.__LOWERCASE__)];
			int length = param.Length;
			if (param.Length > up.Length)
				length = up.Length;
			if (param.Length > down.Length)
				length = down.Length;

			for (int i = 0; i < length; i++)
			{
				//本家の仕様では負の値は無効。
				if ((up[i] <= 0) && (down[i] <= 0))
					continue;
				StringBuilder builder = new StringBuilder();
				if (!skipPrint)
				{
					builder.Append(paramname[i]);
					builder.Append(' ');
					builder.Append(param[i].ToString());
					if (up[i] > 0)
					{
						builder.Append('+');
						builder.Append(up[i].ToString());
					}
					if (down[i] > 0)
					{
						builder.Append('-');
						builder.Append(down[i].ToString());
					}
				}
				unchecked { param[i] += up[i] - down[i]; }
				if (!skipPrint)
				{
					builder.Append('=');
					builder.Append(param[i].ToString());
					window.Print(builder.ToString());
					window.NewLine();
				}

			}
		end:
			for (int i = 0; i < up.Length; i++)
				up[i] = 0;
			for (int i = 0; i < down.Length; i++)
				down[i] = 0;
		}

		public void CUpdateInUpcheck(EmueraConsole window, Int64 target, bool skipPrint)
		{
			SparseArray<Int64> up, down, param;
			string[] paramname = constant.GetCsvNameList(VariableCode.PALAMNAME);
			if ((target < 0) || (target >= varData.CharacterList.Count))
				return;
			CharacterData chara = varData.CharacterList[(int)target];
			up = chara.DataIntegerArray[(int)(VariableCode.CUP & VariableCode.__LOWERCASE__)];
			down = chara.DataIntegerArray[(int)(VariableCode.CDOWN & VariableCode.__LOWERCASE__)];
			param = chara.DataIntegerArray[(int)(VariableCode.PALAM & VariableCode.__LOWERCASE__)];
			int length = param.Length;
			if (param.Length > up.Length)
				length = up.Length;
			if (param.Length > down.Length)
				length = down.Length;

			for (int i = 0; i < length; i++)
			{
				//本家の仕様では負の値は無効。
				if ((up[i] <= 0) && (down[i] <= 0))
					continue;
				StringBuilder builder = new StringBuilder();
				if (!skipPrint)
				{
					builder.Append(paramname[i]);
					builder.Append(' ');
					builder.Append(param[i].ToString());
					if (up[i] > 0)
					{
						builder.Append('+');
						builder.Append(up[i].ToString());
					}
					if (down[i] > 0)
					{
						builder.Append('-');
						builder.Append(down[i].ToString());
					}
				}
				unchecked { param[i] += up[i] - down[i]; }
				if (!skipPrint)
				{
					builder.Append('=');
					builder.Append(param[i].ToString());
					window.Print(builder.ToString());
					window.NewLine();
				}
			}
			for (int i = 0; i < up.Length; i++)
				up[i] = 0;
			for (int i = 0; i < down.Length; i++)
				down[i] = 0;
		}

		private void setDefaultStain(CharacterData chara)
		{
			SparseArray<Int64> array = chara.DataIntegerArray[(int)(VariableCode.STAIN & VariableCode.__LOWERCASE__)];
			//STAINの配列要素数 < _REPLACE.CSVのSTAIN初期値の指定数の時エラーになるのを対処
			if (array.Length >= Config.StainDefault.Count)
			{
				for (int i = 0; i < Config.StainDefault.Count; i++)
					array[i] = Config.StainDefault[i];
				for (int i = Config.StainDefault.Count; i < array.Length; i++)
					array[i] = 0;
			}
			else
			{
				for (int i = 0; i < array.Length; i++)
					array[i] = Config.StainDefault[i];
			}
		}

		public void SetDefaultStain(Int64 no)
		{
			if (no < 0 || no >= varData.CharacterList.Count)
				throw new CodeEE("存在しないキャラクターを参照しようとしました");
			CharacterData chara = varData.CharacterList[(int)no];
			setDefaultStain(chara);
		}

		/// <summary>
		/// RESULTに配列のサイズを代入。二次元配列ならRESULT:1に二番目のサイズを代入。三次元配列ならRESULT:1に二番目、RESULT:2に三番目のサイズを代入
		/// </summary>
		/// <param name="varID"></param>
		/// <returns></returns>
		public void VarSize(VariableToken varID)
		{
			SparseArray<Int64> resultArray = RESULT_ARRAY;
			if (varID.IsArray2D)
			{
				resultArray[0] = varID.GetLength(0);
				resultArray[1] = varID.GetLength(1);
			}
			else if (varID.IsArray3D)
			{
				resultArray[0] = varID.GetLength(0);
				resultArray[1] = varID.GetLength(1);
				resultArray[2] = varID.GetLength(2);
			}
			else
			{
				resultArray[0] = varID.GetLength();
			}
		}

		public bool ItemSales(Int64 itemNo)
		{
			SparseArray<Int64> itemSales = ITEMSALES;
			string[] itemNames = constant.GetCsvNameList(VariableCode.ITEMNAME);
			if ((itemNo < 0) || (itemNo >= itemSales.Length) || (itemNo >= itemNames.Length))
				return false;
			int index = (int)itemNo;
			return ((itemSales[index] != 0) && (itemNames[index] != null));
		}

		public bool BuyItem(Int64 itemNo)
		{
			if (!ItemSales(itemNo))
				return false;
			Int64[] itemPrice = constant.ItemPrice;
			if (itemNo >= itemPrice.Length)
				return false;
			int index = (int)itemNo;
			if (MONEY < itemPrice[index])
				return false;
			MONEY -= itemPrice[index];
			ITEM[index]++;
			BOUGHT = itemNo;
			return true;
		}


		public void SetEncodingResult(int[] ary)
		{
			SparseArray<Int64> resary = varData.DataIntegerArray[(int)(VariableCode.RESULT & VariableCode.__LOWERCASE__)];
			resary[0] = ary.Length;
			for (int i = 0; i < ary.Length; i++)
				resary[i + 1] = ary[i];
		}

		#endregion

		//ちーと
		public void IamaMunchkin()
		{
			if ((MASTER < 0) || (MASTER >= varData.CharacterList.Count))
				return;
			varData.CharacterList[(int)MASTER].DataString[(int)(VariableCode.NAME & VariableCode.__LOWERCASE__)] = "イカサマ";
			varData.CharacterList[(int)MASTER].DataString[(int)(VariableCode.CALLNAME & VariableCode.__LOWERCASE__)] = "イカサマ";
			varData.CharacterList[(int)MASTER].DataString[(int)(VariableCode.NICKNAME & VariableCode.__LOWERCASE__)] = "イカサマ";

		}

		public void SetResultX(List<long> values)
		{
			for (int i = 0; i < values.Count; i++)
			{
				if (i >= varData.DataIntegerArray[(int)(VariableCode.RESULT & VariableCode.__LOWERCASE__)].Length)
					return;
				varData.DataIntegerArray[(int)(VariableCode.RESULT & VariableCode.__LOWERCASE__)][i] = values[i];
			}
		}

		#region File操作


		private string getSaveDataPathG() { return Config.SavDir + "global.sav"; }
		private string getSaveDataPath(int index) { return string.Format("{0}save{1:00}.sav", Config.SavDir, index); }
		private string getSaveDataPath(string s) { return string.Format("{0}save{1:00}.sav", Config.SavDir, s); }

		private string getSaveDataPathV(int index) { return Program.DatDir + string.Format("var_{0:00}.dat", index); }
		private string getSaveDataPathC(int index) { return Program.DatDir + string.Format("chara_{0:00}.dat", index); }
		private string getSaveDataPathV(string s) { return Program.DatDir + "var_" + s + ".dat"; }
		private string getSaveDataPathC(string s) { return Program.DatDir + "chara_" + s + ".dat"; }

		/// <summary>
		/// DatFolderが存在せず、かつ作成に失敗したらエラーを投げる
		/// </summary>
		/// <returns></returns>
		public void CreateDatFolder()
		{
			if (Directory.Exists(Program.DatDir))
				return;
			try
			{
				Directory.CreateDirectory(Program.DatDir);
			}
			catch
			{
				MessageBox.Show("datフォルダーの作成に失敗しました");
				throw new CodeEE("datフォルダーの作成に失敗しました");
			}
		}

		public List<string> GetDatFiles(bool charadat, string pattern)
		{
			List<string> files = new List<string>();
			if (!Directory.Exists(Program.DatDir))
				return files;
			string searchPattern = "var_" + pattern + ".dat";
			if (charadat)
				searchPattern = "chara_" + pattern + ".dat";
			string[] pathes = Directory.GetFiles(Program.DatDir, searchPattern, SearchOption.TopDirectoryOnly);
			foreach (string path in pathes)
			{
				if (!Path.GetExtension(path).Equals(".dat", StringComparison.OrdinalIgnoreCase))
					continue;
				string filename = Path.GetFileNameWithoutExtension(path);
				if (charadat)
					filename = filename.Substring(6);
				else
					filename = filename.Substring(4);
				if (string.IsNullOrEmpty(filename))
					continue;
				files.Add(filename);
			}
			return files;
		}

		/// <summary>
		/// 文字列がファイル名の一部として適切かどうか調べる
		/// </summary>
		/// <param name="datfilename"></param>
		/// <returns>適切ならnull、不適切ならエラーメッセージ</returns>
		public string CheckDatFilename(string datfilename)
		{
			if (string.IsNullOrEmpty(datfilename))
				return "ファイル名が指定されていません";
			if (datfilename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
				return "ファイル名に不正な文字が含まれています";
			return null;
		}

		public EraDataResult CheckData(string savename, EraSaveFileType type)
		{
			string filename = null;
			switch (type)
			{
				case EraSaveFileType.Normal:
					filename = getSaveDataPath(savename); break;
				case EraSaveFileType.Global:
					filename = getSaveDataPathG(); break;
				case EraSaveFileType.Var:
					filename = getSaveDataPathV(savename); break;
				case EraSaveFileType.CharVar:
					filename = getSaveDataPathC(savename); break;
			}
			return CheckDataByFilename(filename, type);
		}

		public EraDataResult CheckData(int saveIndex, EraSaveFileType type)
		{
			string filename = null;
			switch (type)
			{
				case EraSaveFileType.Normal:
					filename = getSaveDataPath(saveIndex); break;
				case EraSaveFileType.Global:
					filename = getSaveDataPathG(); break;
				case EraSaveFileType.Var:
					filename = getSaveDataPathV(saveIndex); break;
				case EraSaveFileType.CharVar:
					filename = getSaveDataPathC(saveIndex); break;
			}
			return CheckDataByFilename(filename, type);
		}

		public EraDataResult CheckDataByFilename(string filename, EraSaveFileType type)
		{
			EraDataResult result = new EraDataResult();
			if (!File.Exists(filename))
			{
				result.State = EraDataState.FILENOTFOUND;
				result.DataMes = "----";
				result.Version = 0;
				return result;
			}
			FileStream fs = null;
			EraBinaryDataReader bReader = null;
			EraDataReader reader = null;
			Int64 version = 0;
			try
			{
				fs = new FileStream(filename, FileMode.Open, FileAccess.Read);
				bReader = EraBinaryDataReader.CreateReader(fs);
				if (bReader == null)//eramaker形式
				{
					reader = new EraDataReader(fs);
					if (!gamebase.UniqueCodeEqualTo(reader.ReadInt64()))
					{
						result.State = EraDataState.GAME_ERROR;
						result.DataMes = "異なるゲームのセーブデータです";
						return result;
					}
					version = reader.ReadInt64();
					if (!gamebase.CheckVersion(version))
					{
						result.State = EraDataState.VIRSION_ERROR;
						result.DataMes = "セーブデータのバーションが異なります";
						result.Version = version;
						return result;
					}
					result.State = EraDataState.OK;
					result.DataMes = reader.ReadString();
					result.Version = version;
					return result;
					//result.State = EraDataState.ETC_ERROR;
					//result.DataMes = "セーブデータが壊れています";
					//return result;
				}
				EraSaveFileType fileType = bReader.ReadFileType();
				if (type != fileType)
				{
					result.State = EraDataState.ETC_ERROR;
					result.DataMes = "セーブデータが壊れています";
					return result;
				}
				if (!gamebase.UniqueCodeEqualTo(bReader.ReadInt64()))
				{
					result.State = EraDataState.GAME_ERROR;
					result.DataMes = "異なるゲームのセーブデータです";
					return result;
				}
				version = bReader.ReadInt64();
				if (!gamebase.CheckVersion(version))
				{
					result.State = EraDataState.VIRSION_ERROR;
					result.DataMes = "セーブデータのバーションが異なります";
					result.Version = version;
					return result;
				}
				result.State = EraDataState.OK;
				result.DataMes = bReader.ReadString();
				result.Version = version;
				return result;
			}
			catch (FileEE fee)
			{
				result.State = EraDataState.ETC_ERROR;
				result.DataMes = fee.Message;
			}
			catch (Exception)
			{
				result.State = EraDataState.ETC_ERROR;
				result.DataMes = "読み込み中にエラーが発生しました";
			}
			finally
			{
				if (reader != null)
					reader.Close();
				else if (bReader != null)
					bReader.Close();
				else if (fs != null)
					fs.Close();
			}
			return result;
		}

		////これは理屈上VariableEvaluator上で動くはず
		//public EraDataResult checkData(int saveIndex)
		//{
		//    string filename = getSaveDataPath(saveIndex);
		//    EraDataResult result = new EraDataResult();
		//    EraDataReader reader = null;
		//    try
		//    {
		//        if (!File.Exists(filename))
		//        {
		//            result.State = EraDataState.FILENOTFOUND;
		//            result.DataMes = "----";
		//            return result;
		//        }
		//        reader = new EraDataReader(filename);
		//        if (!gamebase.UniqueCodeEqualTo(reader.ReadInt64()))
		//        {
		//            result.State = EraDataState.GAME_ERROR;
		//            result.DataMes = "異なるゲームのセーブデータです";
		//            return result;
		//        }
		//        Int64 version = reader.ReadInt64();
		//        if (!gamebase.CheckVersion(version))
		//        {
		//            result.State = EraDataState.VIRSION_ERROR;
		//            result.DataMes = "セーブデータのバーションが異なります";
		//            return result;
		//        }
		//        result.State = EraDataState.OK;
		//        result.DataMes = reader.ReadString();
		//        return result;
		//    }
		//    catch (FileEE fee)
		//    {
		//        result.State = EraDataState.ETC_ERROR;
		//        result.DataMes = fee.Message;
		//    }
		//    catch (Exception)
		//    {
		//        result.State = EraDataState.ETC_ERROR;
		//        result.DataMes = "読み込み中にエラーが発生しました";
		//    }
		//    finally
		//    {
		//        if (reader != null)
		//            reader.Close();
		//    }
		//    return result;
		//}

		public void SaveChara(string savename, string savMes, int[] charas)
		{
			CreateDatFolder();
			CheckDatFilename(savename);
			string filepath = getSaveDataPathC(savename);
			EraBinaryDataWriter bWriter = null;
			FileStream fs = null;
			try
			{
				Config.CreateSavDir();
				fs = new FileStream(filepath, FileMode.Create, FileAccess.Write);
				bWriter = new EraBinaryDataWriter(fs);
				bWriter.WriteHeader();
				bWriter.WriteFileType(EraSaveFileType.CharVar);
				bWriter.WriteInt64(gamebase.ScriptUniqueCode);
				bWriter.WriteInt64(gamebase.ScriptVersion);
				bWriter.WriteString(savMes);
				bWriter.WriteInt64(charas.Length);//保存するキャラ数
				for (int i = 0; i < charas.Length; i++)
				{
					varData.CharacterList[charas[i]].SaveToStreamBinary(bWriter, varData);
				}
				bWriter.WriteEOF();
				//RESULT = 1;
				return;
			}
			//catch (Exception)
			//{
			//	throw new CodeEE("セーブ中にエラーが発生しました");
			//}
			finally
			{
				if (bWriter != null)
					bWriter.Close();
				else if (fs != null)
					fs.Close();
			}
		}

		public void LoadChara(string savename)
		{
			string filepath = getSaveDataPathC(savename);
			RESULT = 0;
			if (!File.Exists(filepath))
				return;
			EraBinaryDataReader bReader = null;
			FileStream fs = null;
			try
			{
				List<CharacterData> addCharaList = new List<CharacterData>();
				fs = new FileStream(filepath, FileMode.Open, FileAccess.Read);
				bReader = EraBinaryDataReader.CreateReader(fs);
				if (bReader == null)
					return;
				if (bReader.ReadFileType() != EraSaveFileType.CharVar)
					return;

				if (!gamebase.UniqueCodeEqualTo(bReader.ReadInt64()))
					return;
				Int64 version = bReader.ReadInt64();
				if (!gamebase.CheckVersion(version))
					return;
				bReader.ReadString();//saveMes
				Int64 loadnum = bReader.ReadInt64();
				for (int i = 0; i < loadnum; i++)
				{
					CharacterData chara = new CharacterData(constant, varData);
					chara.LoadFromStreamBinary(bReader);
					addCharaList.Add(chara);
				}
				varData.CharacterList.AddRange(addCharaList);
				RESULT = 1;
				return;
			}
			//catch (Exception)
			//{
			//	return;
			//}
			finally
			{
				if (bReader != null)
					bReader.Close();
				else if (fs != null)
					fs.Close();
			}
		}

		public void SaveVariable(string savename, string savMes, VariableToken[] vars)
		{
			CreateDatFolder();
			CheckDatFilename(savename);
			string filepath = getSaveDataPathV(savename);
			EraBinaryDataWriter bWriter = null;
			FileStream fs = null;
			try
			{
				Config.CreateSavDir();
				fs = new FileStream(filepath, FileMode.Create, FileAccess.Write);
				bWriter = new EraBinaryDataWriter(fs);
				bWriter.WriteHeader();
				bWriter.WriteFileType(EraSaveFileType.Var);
				bWriter.WriteInt64(gamebase.ScriptUniqueCode);
				bWriter.WriteInt64(gamebase.ScriptVersion);
				bWriter.WriteString(savMes);

				for (int i = 0; i < vars.Length; i++)
					bWriter.WriteWithKey(vars[i].Name, vars[i].GetSaveValue());
				bWriter.WriteEOF();
				//RESULT = 1;
				return;
			}
			//catch (Exception)
			//{
			//	throw new CodeEE("セーブ中にエラーが発生しました");
			//}
			finally
			{
				if (bWriter != null)
					bWriter.Close();
				else if (fs != null)
					fs.Close();
			}
		}

		public void LoadVariable(string savename)
		{
			string filepath = getSaveDataPathV(savename);
			RESULT = 0;
			if (!File.Exists(filepath))
				return;
			EraBinaryDataReader bReader = null;
			FileStream fs = null;
			try
			{
				fs = new FileStream(filepath, FileMode.Open, FileAccess.Read);
				bReader = EraBinaryDataReader.CreateReader(fs);
				if (bReader == null)
					return;
				if (bReader.ReadFileType() != EraSaveFileType.Var)
					return;

				if (!gamebase.UniqueCodeEqualTo(bReader.ReadInt64()))
					return;
				Int64 version = bReader.ReadInt64();
				if (!gamebase.CheckVersion(version))
					return;
				bReader.ReadString();//saveMes
				while (varData.LoadVariableBinary(bReader)) { }
				RESULT = 1;
				return;
			}
			//catch (Exception)
			//{
			//	return;
			//}
			finally
			{
				if (bReader != null)
					bReader.Close();
				else if (fs != null)
					fs.Close();
			}
		}

		public void SaveToStream(EraDataWriter writer, string saveDataText)
		{
			writer.Write(gamebase.ScriptUniqueCode);
			writer.Write(gamebase.ScriptVersion);
			writer.Write(saveDataText);
			writer.Write(varData.CharacterList.Count);
			for (int i = 0; i < varData.CharacterList.Count; i++)
			{
				varData.CharacterList[i].SaveToStream(writer);
			}
			varData.SaveToStream(writer);
			writer.EmuStart();
			for (int i = 0; i < varData.CharacterList.Count; i++)
			{
				varData.CharacterList[i].SaveToStreamExtended(writer);
			}
			varData.SaveToStreamExtended(writer);
			varData.SaveFloatToStreamExtended(writer);
			SaveRuntimeDataStoreText(writer, false);
		}

		public void LoadFromStream(EraDataReader reader)
		{
			if (!gamebase.UniqueCodeEqualTo(reader.ReadInt64()))
				throw new FileEE("異なるゲームのセーブデータです");
			Int64 version = reader.ReadInt64();
			if (!gamebase.CheckVersion(version))
				throw new FileEE("セーブデータのバーションが異なります");
			string text = reader.ReadString();//PUTFORM
			varData.SetDefaultValue(constant);
			varData.SetDefaultLocalValue();
			varData.LastLoadVersion = version;
			//varData.LastLoadNo = dataIndex;
			varData.LastLoadText = text;

			int charaCount = (int)reader.ReadInt64();
			varData.CharacterList.Clear();
			for (int i = 0; i < charaCount; i++)
			{
				CharacterData chara = new CharacterData(constant, varData);
				varData.CharacterList.Add(chara);
				chara.LoadFromStream(reader);
			}
			varData.LoadFromStream(reader);
			if (reader.SeekEmuStart())
			{
				if (reader.DataVersion < 1803)//キャラ2次元配列追加以前
					for (int i = 0; i < charaCount; i++)
						varData.CharacterList[i].LoadFromStreamExtended_Old1802(reader);
				else
					for (int i = 0; i < charaCount; i++)
						varData.CharacterList[i].LoadFromStreamExtended(reader);
				varData.LoadFromStreamExtended(reader, reader.DataVersion);
				varData.TryLoadFloatFromStreamExtended(reader);
				TryLoadRuntimeDataStoreText(reader, false);
			}
			else
			{
				RuntimeDataStore.ClearSaveData(constant);
			}
		}

		public bool SaveGlobal()
		{
			string filepath = getSaveDataPathG();
			bool isAndroid = IsAndroidRuntime();
			try
			{
				Config.CreateSavDir();
				if (isAndroid)
					WriteGlobalSaveFile(filepath);
				else
					WriteGlobalSaveFileWithDesktopLock(filepath);
			}
			catch (SystemException ex)
			{
				if (!isAndroid && IsSaveFileLockException(ex))
				{
					// A desktop reader may transiently retain global.sav after the bounded
					// atomic-replace retry. Preserve the legacy false return without emitting
					// a fatal diagnostic for a recoverable external lock.
					GenericUtils.Warn(EmueraLogCategory.Save, () =>
						"[SAVEGLOBAL] Skipped global save because global.sav remained locked after retries: \"" + filepath + "\"");
					return false;
				}
				if (!isAndroid)
					GenericUtils.Error(EmueraLogCategory.Save, () =>
						"[SAVEGLOBAL] Failed to save global data to \"" + filepath + "\": "
						+ ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
				throw new CodeEE("グローバルデータの保存中にエラーが発生しました");
				//console.PrintError(
				//console.NewLine();
				//return false;
			}
			//finally
			//{
			//	if (writer != null)
			//		writer.Close();
			//	else if (bWriter != null)
			//		bWriter.Close();
			//	else if (fs != null)
			//		fs.Close();
			//}
			return true;
		}

		private void WriteGlobalSaveFile(string filepath)
		{
			using (FileStream fs = new FileStream(filepath, FileMode.Create, FileAccess.Write))
				WriteGlobalSaveStream(fs);
		}

		private void WriteGlobalSaveFileWithDesktopLock(string filepath)
		{
			string lockFilePath = filepath + ".lock";
			using (FileStream lockFile = OpenDesktopSaveLock(lockFilePath))
				WriteGlobalSaveFileAtomically(filepath);
		}

		private static FileStream OpenDesktopSaveLock(string lockFilePath)
		{
			const int timeoutMs = 15000;
			int delayMs = 40;
			DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (true)
			{
				try
				{
					return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
				}
				catch (IOException) when (DateTime.UtcNow < deadline)
				{
					System.Threading.Thread.Sleep(delayMs);
					if (delayMs < 250)
						delayMs *= 2;
				}
			}
		}

		private void WriteGlobalSaveFileAtomically(string filepath)
		{
			string tempFilePath = filepath + ".tmp." + Guid.NewGuid().ToString("N");
			bool committed = false;
			try
			{
				using (FileStream fs = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					WriteGlobalSaveStream(fs);
				CommitSaveFileWithRetry(tempFilePath, filepath);
				committed = true;
			}
			finally
			{
				if (!committed)
					TryDeleteTempSaveFile(tempFilePath);
			}
		}

		private void WriteGlobalSaveStream(FileStream fs)
		{
			if (Config.SystemSaveInBinary)
			{

				using (EraBinaryDataWriter bWriter = new EraBinaryDataWriter(fs))
				{
					bWriter.WriteHeader();
					bWriter.WriteFileType(EraSaveFileType.Global);
					bWriter.WriteInt64(gamebase.ScriptUniqueCode);
					bWriter.WriteInt64(gamebase.ScriptVersion);
					bWriter.WriteString("");//saveMes
					varData.SaveGlobalToStreamBinary(bWriter);
					bWriter.WriteEOF();
					SaveRuntimeDataStore(bWriter, true);
					bWriter.WriteEOF();
					bWriter.Close();
				}
			}
			else
			{
				using (EraDataWriter writer = new EraDataWriter(fs))
				{
					writer.Write(gamebase.ScriptUniqueCode);
					writer.Write(gamebase.ScriptVersion);
					varData.SaveGlobalToStream(writer);
					writer.EmuStart();
					varData.SaveGlobalToStream1808(writer);
					SaveRuntimeDataStoreText(writer, true);
					writer.Close();
				}
			}
		}

		private static void CommitSaveFileWithRetry(string tempFilePath, string filepath)
		{
			const int maxAttempts = 9;
			int delayMs = 25;
			for (int attempt = 0; attempt < maxAttempts; attempt++)
			{
				try
				{
					CommitSaveFile(tempFilePath, filepath);
					return;
				}
				catch (IOException) when (attempt + 1 < maxAttempts)
				{
					System.Threading.Thread.Sleep(delayMs);
					delayMs *= 2;
				}
			}
			throw new IOException("Unable to atomically replace the save file after bounded retries.");
		}

		private static void CommitSaveFile(string tempFilePath, string filepath)
		{
			if (File.Exists(filepath))
				File.Replace(tempFilePath, filepath, null);
			else
				File.Move(tempFilePath, filepath);
		}

		private static void TryDeleteTempSaveFile(string tempFilePath)
		{
			try
			{
				if (File.Exists(tempFilePath))
					File.Delete(tempFilePath);
			}
			catch (SystemException)
			{
			}
		}

		private static bool IsSaveFileLockException(SystemException ex)
		{
			if (!(ex is IOException))
				return false;
			int errorCode = ex.HResult & 0xFFFF;
			return errorCode == 32 || errorCode == 33;
		}

		private static bool IsAndroidRuntime()
		{
			return string.Equals(global::Godot.OS.GetName(), "Android", StringComparison.OrdinalIgnoreCase);
		}

		public bool LoadGlobal()
		{
			string filepath = getSaveDataPathG();
			if (!File.Exists(filepath))
				return false;
			EraDataReader reader = null;
			EraBinaryDataReader bReader = null;
			FileStream fs = null;
			RuntimeDataStore.ClearGlobalData(constant);
			try
			{
				fs = new FileStream(filepath, FileMode.Open, FileAccess.Read);
				bReader = EraBinaryDataReader.CreateReader(fs);
				if (bReader != null)
				{
					EraSaveFileType fileType = bReader.ReadFileType();
					if (fileType != EraSaveFileType.Global)
						return false;
					if (!gamebase.UniqueCodeEqualTo(bReader.ReadInt64()))
						return false;
					Int64 version = bReader.ReadInt64();
					if (!gamebase.CheckVersion(version))
						return false;
					bReader.ReadString();//saveMes
					varData.LoadFromStreamBinary(bReader);
					LoadRuntimeDataStoreTail(bReader, true);
				}
				else
				{
					reader = new EraDataReader(fs);
					if (!gamebase.UniqueCodeEqualTo(reader.ReadInt64()))
						return false;
					Int64 version = reader.ReadInt64();
					if (!gamebase.CheckVersion(version))
						return false;
					varData.LoadGlobalFromStream(reader);
					if (reader.SeekEmuStart())
					{
						varData.LoadGlobalFromStream1808(reader);
						TryLoadRuntimeDataStoreText(reader, true);
					}
				}
				return true;
			}
			catch
			{
				return false;
			}
			finally
			{
				if (reader != null)
					reader.Close();
				else if (bReader != null)
					bReader.Close();
				else if (fs != null)
					fs.Close();
			}
		}

		public void SaveToStreamBinary(EraBinaryDataWriter bWriter, string saveDataText)
		{
			bWriter.WriteHeader();
			bWriter.WriteFileType(EraSaveFileType.Normal);
			bWriter.WriteInt64(gamebase.ScriptUniqueCode);
			bWriter.WriteInt64(gamebase.ScriptVersion);
			bWriter.WriteString(saveDataText);
			bWriter.WriteInt64(varData.CharacterList.Count);
			for (int i = 0; i < varData.CharacterList.Count; i++)
			{
				varData.CharacterList[i].SaveToStreamBinary(bWriter, varData);
			}
			varData.SaveToStreamBinary(bWriter);
			bWriter.WriteEOF();
			SaveRuntimeDataStore(bWriter, false);
			bWriter.WriteEOF();
		}

		public void LoadFromStreamBinary(EraBinaryDataReader bReader)
		{
			EraSaveFileType fileType = bReader.ReadFileType();
			if (fileType != EraSaveFileType.Normal)
				throw new FileEE("セーブデータが壊れています");
			if (!gamebase.UniqueCodeEqualTo(bReader.ReadInt64()))
				throw new FileEE("異なるゲームのセーブデータです");
			Int64 version = bReader.ReadInt64();
			if (!gamebase.CheckVersion(version))
				throw new FileEE("セーブデータのバーションが異なります");
			string text = bReader.ReadString();//PUTFORM
			varData.SetDefaultValue(constant);
			varData.SetDefaultLocalValue();
			varData.LastLoadVersion = version;
			//varData.LastLoadNo = dataIndex;
			varData.LastLoadText = text;

			int charaCount = (int)bReader.ReadInt64();
			varData.CharacterList.Clear();
			for (int i = 0; i < charaCount; i++)
			{
				CharacterData chara = new CharacterData(constant, varData);
				varData.CharacterList.Add(chara);
				chara.LoadFromStreamBinary(bReader);
			}
			varData.LoadFromStreamBinary(bReader);
			LoadRuntimeDataStoreTail(bReader, false);
		}

		void SaveRuntimeDataStore(EraBinaryDataWriter bWriter, bool globalData)
		{
			IEnumerable<string> mapKeys = globalData ? constant.GlobalSaveMaps : constant.SaveMaps;
			foreach (string key in mapKeys)
				if (RuntimeDataStore.Maps.TryGetValue(key, out var map))
					bWriter.WriteWithKey(key, map);
			IEnumerable<string> xmlKeys = globalData ? constant.GlobalSaveXmls : constant.SaveXmls;
			foreach (string key in xmlKeys)
				if (RuntimeDataStore.XmlDocuments.TryGetValue(key, out XmlDocument document) && document != null)
					bWriter.WriteWithKey(key, document);
			IEnumerable<string> dtKeys = globalData ? constant.GlobalSaveDTs : constant.SaveDTs;
			foreach (string key in dtKeys)
				if (RuntimeDataStore.DataTables.TryGetValue(key, out DataTable table) && table != null)
					bWriter.WriteWithKey(key, table);
		}

		void LoadRuntimeDataStoreTail(EraBinaryDataReader bReader, bool globalData)
		{
			if (bReader.EOF())
				return;

			byte next = bReader.PeekByte();
			if (next == (byte)EraSaveDataType.EOF)
			{
				bReader.ReadDataType();
				return;
			}

			if (next == EmMapDataType || next == EmXmlDataType || next == EmDataTableDataType)
			{
				LoadRuntimeDataStoreEmData(bReader, globalData);
				return;
			}

			LoadRuntimeDataStore(bReader, globalData);
		}

		void LoadRuntimeDataStoreEmData(EraBinaryDataReader bReader, bool globalData)
		{
			while (!bReader.EOF())
			{
				byte type = (byte)bReader.ReadDataType();
				if (type == (byte)EraSaveDataType.EOF)
				{
					RuntimeDataStore.RefreshNextDataTableRowId();
					return;
				}

				string key = bReader.ReadString();
				switch (type)
				{
					case EmMapDataType:
						Dictionary<string, string> map = bReader.ReadMap();
						if (globalData ? RuntimeDataStore.IsGlobalMap(constant, key) : RuntimeDataStore.IsSaveMap(constant, key))
							RuntimeDataStore.Maps[key] = map;
						break;
					case EmXmlDataType:
						XmlDocument document = bReader.ReadXml();
						if (globalData ? RuntimeDataStore.IsGlobalXml(constant, key) : RuntimeDataStore.IsSaveXml(constant, key))
							RuntimeDataStore.XmlDocuments[key] = document;
						break;
					case EmDataTableDataType:
						DataTable table = bReader.ReadDataTable();
						RuntimeDataStore.NormalizeDataTable(table);
						if (globalData ? RuntimeDataStore.IsGlobalDataTable(constant, key) : RuntimeDataStore.IsSaveDataTable(constant, key))
							RuntimeDataStore.DataTables[key] = table;
						break;
					default:
						throw new FileEE("セーブデータのRuntimeDataStoreデータ型が異常です");
				}
			}
			RuntimeDataStore.RefreshNextDataTableRowId();
		}

		void LoadRuntimeDataStore(EraBinaryDataReader bReader, bool globalData)
		{
			string marker = bReader.ReadString();
			if (marker != RuntimeDataStoreBinaryMarker)
				throw new FileEE("セーブデータのRuntimeDataStoreマーカーが異常です");
			long mapCount = bReader.ReadInt64();
			for (long i = 0; i < mapCount; i++)
			{
				string mapName = bReader.ReadString();
				long entryCount = bReader.ReadInt64();
				var map = new Dictionary<string, string>();
				for (long j = 0; j < entryCount; j++)
				{
					string key = bReader.ReadString();
					string value = bReader.ReadString();
					map[key] = value;
				}
				if (globalData ? RuntimeDataStore.IsGlobalMap(constant, mapName) : RuntimeDataStore.IsSaveMap(constant, mapName))
					RuntimeDataStore.Maps[mapName] = map;
			}
			long xmlCount = bReader.ReadInt64();
			for (long i = 0; i < xmlCount; i++)
			{
				string xmlName = bReader.ReadString();
				string xmlContent = bReader.ReadString();
				if (!string.IsNullOrEmpty(xmlContent))
				{
					var doc = new XmlDocument();
					doc.LoadXml(xmlContent);
					if (globalData ? RuntimeDataStore.IsGlobalXml(constant, xmlName) : RuntimeDataStore.IsSaveXml(constant, xmlName))
						RuntimeDataStore.XmlDocuments[xmlName] = doc;
				}
			}
			long dtCount = bReader.ReadInt64();
			for (long i = 0; i < dtCount; i++)
			{
				string dtName = bReader.ReadString();
				string dtXml = bReader.ReadString();
				if (!string.IsNullOrEmpty(dtXml))
				{
					var dt = new DataTable();
					using (var sr = new System.IO.StringReader(dtXml))
						dt.ReadXml(sr);
					RuntimeDataStore.NormalizeDataTable(dt);
					if (globalData ? RuntimeDataStore.IsGlobalDataTable(constant, dtName) : RuntimeDataStore.IsSaveDataTable(constant, dtName))
						RuntimeDataStore.DataTables[dtName] = dt;
				}
			}
			RuntimeDataStore.NextDataTableRowId = bReader.ReadInt64();
			RuntimeDataStore.RefreshNextDataTableRowId();
		}

		void SaveRuntimeDataStoreText(EraDataWriter writer, bool globalData)
		{
			writer.Write(RuntimeDataStoreTextMarker);
			writeRuntimeDataStoreMapText(writer, globalData ? constant.GlobalSaveMaps : constant.SaveMaps);
			writeRuntimeDataStoreXmlText(writer, globalData ? constant.GlobalSaveXmls : constant.SaveXmls);
			writeRuntimeDataStoreDataTableText(writer, globalData ? constant.GlobalSaveDTs : constant.SaveDTs);
			writer.Write(RuntimeDataStore.NextDataTableRowId);
			writer.Write(RuntimeDataStoreTextEndMarker);
		}

		void writeRuntimeDataStoreMapText(EraDataWriter writer, IEnumerable<string> keys)
		{
			var entries = new List<KeyValuePair<string, Dictionary<string, string>>>();
			foreach (string key in keys)
				if (RuntimeDataStore.Maps.TryGetValue(key, out var map))
					entries.Add(new KeyValuePair<string, Dictionary<string, string>>(key, map));
			writer.Write((Int64)entries.Count);
			foreach (var mapPair in entries)
			{
				WriteEncodedString(writer, mapPair.Key);
				writer.Write((Int64)mapPair.Value.Count);
				foreach (var entry in mapPair.Value)
				{
					WriteEncodedString(writer, entry.Key);
					WriteEncodedString(writer, entry.Value ?? "");
				}
			}
		}

		void writeRuntimeDataStoreXmlText(EraDataWriter writer, IEnumerable<string> keys)
		{
			var entries = new List<KeyValuePair<string, XmlDocument>>();
			foreach (string key in keys)
				if (RuntimeDataStore.XmlDocuments.TryGetValue(key, out XmlDocument document) && document != null)
					entries.Add(new KeyValuePair<string, XmlDocument>(key, document));
			writer.Write((Int64)entries.Count);
			foreach (var xmlPair in entries)
			{
				WriteEncodedString(writer, xmlPair.Key);
				WriteEncodedString(writer, xmlPair.Value?.OuterXml ?? "");
			}
		}

		void writeRuntimeDataStoreDataTableText(EraDataWriter writer, IEnumerable<string> keys)
		{
			var entries = new List<KeyValuePair<string, DataTable>>();
			foreach (string key in keys)
				if (RuntimeDataStore.DataTables.TryGetValue(key, out DataTable table) && table != null)
					entries.Add(new KeyValuePair<string, DataTable>(key, table));
			writer.Write((Int64)entries.Count);
			foreach (var dtPair in entries)
			{
				WriteEncodedString(writer, dtPair.Key);
				using (var sw = new System.IO.StringWriter())
				{
					dtPair.Value.WriteXml(sw, System.Data.XmlWriteMode.WriteSchema);
					WriteEncodedString(writer, sw.ToString());
				}
			}
		}

		bool TryLoadRuntimeDataStoreText(EraDataReader reader, bool globalData)
		{
			string marker;
			try
			{
				marker = reader.ReadString();
			}
			catch (FileEE)
			{
				return false;
			}
			if (marker != RuntimeDataStoreTextMarker)
				return false;

			long mapCount = reader.ReadInt64();
			for (long i = 0; i < mapCount; i++)
			{
				string mapName = ReadEncodedString(reader);
				long entryCount = reader.ReadInt64();
				var map = new Dictionary<string, string>();
				for (long j = 0; j < entryCount; j++)
				{
					string key = ReadEncodedString(reader);
					string value = ReadEncodedString(reader);
					map[key] = value;
				}
				if (globalData ? RuntimeDataStore.IsGlobalMap(constant, mapName) : RuntimeDataStore.IsSaveMap(constant, mapName))
					RuntimeDataStore.Maps[mapName] = map;
			}

			long xmlCount = reader.ReadInt64();
			for (long i = 0; i < xmlCount; i++)
			{
				string xmlName = ReadEncodedString(reader);
				string xmlContent = ReadEncodedString(reader);
				if (!string.IsNullOrEmpty(xmlContent))
				{
					var doc = new XmlDocument();
					doc.LoadXml(xmlContent);
					if (globalData ? RuntimeDataStore.IsGlobalXml(constant, xmlName) : RuntimeDataStore.IsSaveXml(constant, xmlName))
						RuntimeDataStore.XmlDocuments[xmlName] = doc;
				}
			}

			long dtCount = reader.ReadInt64();
			for (long i = 0; i < dtCount; i++)
			{
				string dtName = ReadEncodedString(reader);
				string dtXml = ReadEncodedString(reader);
				if (!string.IsNullOrEmpty(dtXml))
				{
					var dt = new DataTable();
					using (var sr = new System.IO.StringReader(dtXml))
						dt.ReadXml(sr);
					RuntimeDataStore.NormalizeDataTable(dt);
					if (globalData ? RuntimeDataStore.IsGlobalDataTable(constant, dtName) : RuntimeDataStore.IsSaveDataTable(constant, dtName))
						RuntimeDataStore.DataTables[dtName] = dt;
				}
			}
			RuntimeDataStore.NextDataTableRowId = reader.ReadInt64();
			RuntimeDataStore.RefreshNextDataTableRowId();
			string endMarker = reader.ReadString();
			if (endMarker != RuntimeDataStoreTextEndMarker)
				throw new FileEE("セーブデータのRuntimeDataStore終端マーカーが異常です");
			return true;
		}

		static void WriteEncodedString(EraDataWriter writer, string value)
		{
			writer.Write(Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? "")));
		}

		static string ReadEncodedString(EraDataReader reader)
		{
			try
			{
				return Encoding.UTF8.GetString(Convert.FromBase64String(reader.ReadString()));
			}
			catch (FormatException ex)
			{
				throw new FileEE("セーブデータのRuntimeDataStore文字列が異常です: " + ex.Message);
			}
		}

		public bool SaveTo(int saveIndex, string saveText)
		{
			string filepath = getSaveDataPath(saveIndex);
			// 主线程はメモリに一括バッファしてから一時ファイルへ同期で一括書き込み、原子リネームで確定する。
			// SAVEDATA の「保存完了」同期セマンティクス（戻り時にディスクへ確定）とエラー処理（失敗時 false）を
			// 保つため、バックグラウンドスレッド化は行わない（一時ファイルは失敗時に削除される）。
			string tmpPath = filepath + ".tmp";
			try
			{
				Config.CreateSavDir();
				byte[] saveBytes;
				using (MemoryStream ms = new MemoryStream())
				{
					if (Config.SystemSaveInBinary)
					{
						using (EraBinaryDataWriter bWriter = new EraBinaryDataWriter(ms))
							SaveToStreamBinary(bWriter, saveText);
					}
					else
					{
						using (EraDataWriter writer = new EraDataWriter(ms))
							SaveToStream(writer, saveText);
					}
					// ライタの Close で ms が閉じられても（非zip/テキスト時）、ToArray は内部バッファを
					// そのまま返すので有効な完全データを取得できる。
					saveBytes = ms.ToArray();
				}
				using (FileStream fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
					fs.Write(saveBytes, 0, saveBytes.Length);
				File.Move(tmpPath, filepath, true);
				return true;
			}
			catch (Exception)
			{
				try
				{
					if (File.Exists(tmpPath))
						File.Delete(tmpPath);
				}
				catch
				{
				}
				return false;
			}
		}

		public bool LoadFrom(int dataIndex)
		{
			string filepath = getSaveDataPath(dataIndex);
			if (!File.Exists(filepath))
				throw new ExeEE("存在しないパスを呼び出した");
			EraDataReader reader = null;
			EraBinaryDataReader bReader = null;
			FileStream fs = null;
			// 原核心在 LOADDATA 前只清理 VarExt 声明为 SAVE_* 的 EM 扩展数据。
			// 非保存域 MAP/XML/DT 是标题阶段等脚本初始化出的运行期缓存，读旧档时必须保留。
			RuntimeDataStore.ClearSaveData(constant);
			try
			{
				fs = new FileStream(filepath, FileMode.Open, FileAccess.Read);
				bReader = EraBinaryDataReader.CreateReader(fs);
				if (bReader != null)
				{
					LoadFromStreamBinary(bReader);
				}
				else
				{
					reader = new EraDataReader(fs);
					LoadFromStream(reader);
				}
				varData.LastLoadNo = dataIndex;
			}
			finally
			{
				if (reader != null)
					reader.Close();
				else if (bReader != null)
					bReader.Close();
				else if (fs != null)
					fs.Close();
			}
			return true;
		}

		public void DelData(int dataIndex)
		{
			string filepath = getSaveDataPath(dataIndex);
			if (!File.Exists(filepath))
				return;
			FileAttributes att = File.GetAttributes(filepath);
			if ((att & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
				throw new CodeEE("指定されたファイル\"" + filepath + "\"は読み込み専用のため削除できません");
			//{

			//    console.PrintError("指定されたファイル\"" + filepath + "\"は読み込み専用のため削除できません");
			//    return;
			//}
			File.Delete(filepath);
			return;
		}



		#endregion
		#region IDisposable メンバ

		public void Dispose()
		{
			varData.Dispose();
		}

		#endregion
		#region Property
		public SparseArray<Int64> RESULT_ARRAY
		{
			get { return varData.DataIntegerArray[(int)(VariableCode.RESULT & VariableCode.__LOWERCASE__)]; }
		}
		public Int64 RESULT
		{
			get { return varData.DataIntegerArray[(int)(VariableCode.RESULT & VariableCode.__LOWERCASE__)][0]; }
			set { varData.DataIntegerArray[(int)(VariableCode.RESULT & VariableCode.__LOWERCASE__)][0] = value; }
		}
		public double RESULTF
		{
			get { return varData.DataFloat[(int)(VariableCode.RESULTF & VariableCode.__LOWERCASE__)]; }
			set { varData.DataFloat[(int)(VariableCode.RESULTF & VariableCode.__LOWERCASE__)] = value; }
		}
		public Int64 COUNT
		{
			get { return varData.DataIntegerArray[(int)(VariableCode.COUNT & VariableCode.__LOWERCASE__)][0]; }
			set { varData.DataIntegerArray[(int)(VariableCode.COUNT & VariableCode.__LOWERCASE__)][0] = value; }
		}
		public string RESULTS
		{
			get
			{
				string ret = varData.DataStringArray[(int)(VariableCode.RESULTS & VariableCode.__LOWERCASE__)][0];
				if (ret == null)
					return "";
				return ret;
			}
			set { varData.DataStringArray[(int)(VariableCode.RESULTS & VariableCode.__LOWERCASE__)][0] = value; }
		}
		public SparseArray<string> RESULTS_ARRAY
		{
			get { return varData.DataStringArray[(int)(VariableCode.RESULTS & VariableCode.__LOWERCASE__)]; }
		}

		public Int64 TARGET
		{
			get { return varData.DataIntegerArray[(int)(VariableCode.TARGET & VariableCode.__LOWERCASE__)][0]; }
			set { varData.DataIntegerArray[(int)(VariableCode.TARGET & VariableCode.__LOWERCASE__)][0] = value; }
		}
		public SparseArray<Int64> SELECTCOM_ARRAY
		{
			get { return varData.DataIntegerArray[(int)(VariableCode.SELECTCOM & VariableCode.__LOWERCASE__)]; }
		}
		public Int64 SELECTCOM
		{
			get { return varData.DataIntegerArray[(int)(VariableCode.SELECTCOM & VariableCode.__LOWERCASE__)][0]; }
			set { varData.DataIntegerArray[(int)(VariableCode.SELECTCOM & VariableCode.__LOWERCASE__)][0] = value; }
		}
		public string[] ITEMNAME
		{
			get { return constant.GetCsvNameList(VariableCode.ITEMNAME); }
		}

		public SparseArray<Int64> ITEMSALES
		{
			get { return varData.DataIntegerArray[(int)(VariableCode.ITEMSALES & VariableCode.__LOWERCASE__)]; }
		}

		public Int64[] ITEMPRICE
		{
			get { return constant.ItemPrice; }
		}

		private SparseArray<Int64> ITEM
		{
			get { return varData.DataIntegerArray[(int)(VariableCode.ITEM & VariableCode.__LOWERCASE__)]; }
		}

		public SparseArray<Int64> RANDDATA
		{
			get { return varData.DataIntegerArray[(int)(VariableCode.RANDDATA & VariableCode.__LOWERCASE__)]; }
		}

		public string SAVEDATA_TEXT
		{
			get { return varData.DataString[(int)(VariableCode.SAVEDATA_TEXT & VariableCode.__LOWERCASE__)]; }
			set { varData.DataString[(int)(VariableCode.SAVEDATA_TEXT & VariableCode.__LOWERCASE__)] = value; }
		}
		public Int64 CHARANUM
		{
			get { return varData.CharacterList.Count; }
		}




		private Int64 get_Variable_canforbid(VariableCode code)
		{
			SparseArray<Int64> array = varData.DataIntegerArray[(int)(code & VariableCode.__LOWERCASE__)];
			if (array.Length == 0)
				return -1;
			return array[0];
		}
		private void set_Variable_canforbid(VariableCode code, Int64 value)
		{
			SparseArray<Int64> array = varData.DataIntegerArray[(int)(code & VariableCode.__LOWERCASE__)];
			if (array.Length == 0)
				return;
			array[0] = value;
		}

		public Int64 MASTER
		{
			get { return get_Variable_canforbid(VariableCode.MASTER); }
			set { set_Variable_canforbid(VariableCode.MASTER, value); }
		}
		public Int64 ASSI
		{
			get { return get_Variable_canforbid(VariableCode.ASSI); }
			set { set_Variable_canforbid(VariableCode.ASSI, value); }
		}
		public Int64 ASSIPLAY
		{
			set { set_Variable_canforbid(VariableCode.ASSIPLAY, value); }
		}
		public Int64 PREVCOM
		{
			get { return get_Variable_canforbid(VariableCode.PREVCOM); }
			set { set_Variable_canforbid(VariableCode.PREVCOM, value); }
		}
		public Int64 NEXTCOM
		{
			get { return get_Variable_canforbid(VariableCode.NEXTCOM); }
			set { set_Variable_canforbid(VariableCode.NEXTCOM, value); }
		}
		private Int64 MONEY
		{
			get { return get_Variable_canforbid(VariableCode.MONEY); }
			set { set_Variable_canforbid(VariableCode.MONEY, value); }
		}

		private Int64 BOUGHT
		{
			set { set_Variable_canforbid(VariableCode.BOUGHT, value); }
		}

		//public Int64 MASTER
		//{
		//	get { return varData.DataIntegerArray[(int)(VariableCode.MASTER & VariableCode.__LOWERCASE__)][0]; }
		//	set { varData.DataIntegerArray[(int)(VariableCode.MASTER & VariableCode.__LOWERCASE__)][0] = value; }
		//}
		//public Int64 ASSI
		//{
		//	get { return varData.DataIntegerArray[(int)(VariableCode.ASSI & VariableCode.__LOWERCASE__)][0]; }
		//	set { varData.DataIntegerArray[(int)(VariableCode.ASSI & VariableCode.__LOWERCASE__)][0] = value; }
		//}
		//public Int64 ASSIPLAY
		//{
		//	//get { return varData.DataIntegerArray[(int)(VariableCode.ASSIPLAY & VariableCode.__LOWERCASE__)][0]; }
		//	set { varData.DataIntegerArray[(int)(VariableCode.ASSIPLAY & VariableCode.__LOWERCASE__)][0] = value; }
		//}
		//public Int64 PREVCOM
		//{
		//	//get { return varData.DataIntegerArray[(int)(VariableCode.PREVCOM & VariableCode.__LOWERCASE__)][0]; }
		//	set { varData.DataIntegerArray[(int)(VariableCode.PREVCOM & VariableCode.__LOWERCASE__)][0] = value; }
		//}
		//public Int64 NEXTCOM
		//{
		//	get { return varData.DataIntegerArray[(int)(VariableCode.NEXTCOM & VariableCode.__LOWERCASE__)][0]; }
		//	set { varData.DataIntegerArray[(int)(VariableCode.NEXTCOM & VariableCode.__LOWERCASE__)][0] = value; }
		//}
		//private Int64 MONEY
		//{
		//	get { return varData.DataIntegerArray[(int)(VariableCode.MONEY & VariableCode.__LOWERCASE__)][0]; }
		//	set { varData.DataIntegerArray[(int)(VariableCode.MONEY & VariableCode.__LOWERCASE__)][0] = value; }
		//}

		//private Int64 BOUGHT
		//{
		//	//get { return varData.DataIntegerArray[(int)(VariableCode.BOUGHT & VariableCode.__LOWERCASE__)][0]; }
		//	set { varData.DataIntegerArray[(int)(VariableCode.BOUGHT & VariableCode.__LOWERCASE__)][0] = value; }
		//}
		#endregion

	}
}
