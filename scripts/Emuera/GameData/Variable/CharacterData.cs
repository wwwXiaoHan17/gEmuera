using System;
using System.Collections.Generic;
using System.Text;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameProc;

namespace MinorShift.Emuera.GameData.Variable
{

	internal sealed class CharacterData : IDisposable
	{
		readonly Int64[] dataInteger;
		readonly string[] dataString;
		readonly double[] dataFloat;
		readonly SparseArray<Int64>[] dataIntegerArray;
		readonly SparseArray<string>[] dataStringArray;
		readonly double[][] dataFloatArray;
		readonly Int64[][,] dataIntegerArray2D;
		readonly string[][,] dataStringArray2D;
		public Int64[] DataInteger { get { return dataInteger; } }
		public string[] DataString { get { return dataString; } }
		public double[] DataFloat { get { return dataFloat; } }
		public SparseArray<Int64>[] DataIntegerArray { get { return dataIntegerArray; } }
		public SparseArray<string>[] DataStringArray { get { return dataStringArray; } }
		public double[][] DataFloatArray { get { return dataFloatArray; } }
		public Int64[][,] DataIntegerArray2D { get { return dataIntegerArray2D; } }
		public string[][,] DataStringArray2D { get { return dataStringArray2D; } }

		public List<object> UserDefCVarDataList { get; set; }

		// 保存キーの静的キャッシュ。code.ToString()（列挙体のボックス化＋フォーマット）の実結果を
		// キャッシュするため、生成文字列は ToString() と完全に同一。セーブはメインスレッドでのみ実行される。
		static Dictionary<VariableCode, string> varCodeNameCache = null;
		static string GetVarCodeName(VariableCode code)
		{
			Dictionary<VariableCode, string> cache = varCodeNameCache;
			if (cache == null)
				cache = varCodeNameCache = new Dictionary<VariableCode, string>();
			if (cache.TryGetValue(code, out string name))
				return name;
			name = code.ToString();
			cache[code] = name;
			return name;
		}

		// ロード時の変数トークン解決キャッシュ。GetVariableToken は string ハッシュ＋複数辞書引きを
		// 変数ごとに行うため、同一キー（キャラ数分繰り返し現れる）をキャッシュで打ち切る。
		// IdentifierDictionary はセッション（プロセス再生成）ごとに再構築されるため、
		// 参照が変わった時点でキャッシュを破棄して再構築する。ロードはメインスレッドのみで実行される。
		static IdentifierDictionary loadTokenCacheOwner = null;
		static Dictionary<string, VariableToken> loadTokenCache = null;
		static VariableToken GetLoadVariableToken(string key)
		{
			IdentifierDictionary idDic = GlobalStatic.IdentifierDictionary;
			if (!object.ReferenceEquals(loadTokenCacheOwner, idDic))
			{
				loadTokenCacheOwner = idDic;
				loadTokenCache = new Dictionary<string, VariableToken>();
			}
			if (loadTokenCache.TryGetValue(key, out VariableToken cached))
				return cached;
			VariableToken token = null;
			if (!idDic.getVarTokenIsForbid(key))
				token = idDic.GetVariableToken(key, null, false);
			loadTokenCache[key] = token;
			return token;
		}

		public CharacterData(ConstantData constant, VariableData varData)
		{
			dataInteger = new Int64[(int)VariableCode.__COUNT_CHARACTER_INTEGER__];
			dataString = new string[(int)VariableCode.__COUNT_CHARACTER_STRING__];
			dataFloat = new double[0];
			dataIntegerArray = new SparseArray<Int64>[(int)VariableCode.__COUNT_CHARACTER_INTEGER_ARRAY__];
			dataStringArray = new SparseArray<string>[(int)VariableCode.__COUNT_CHARACTER_STRING_ARRAY__];
			dataFloatArray = new double[(int)VariableCode.__COUNT_FLOAT_ARRAY__][];
			dataIntegerArray2D = new Int64[(int)VariableCode.__COUNT_CHARACTER_INTEGER_ARRAY_2D__][,];
			dataStringArray2D = new string[(int)VariableCode.__COUNT_CHARACTER_STRING_ARRAY_2D__][,];
			for (int i = 0; i < dataIntegerArray.Length; i++)
				dataIntegerArray[i] = new SparseArray<Int64>() { Length = constant.CharacterIntArrayLength[i] };
			for (int i = 0; i < dataStringArray.Length; i++)
				dataStringArray[i] = new SparseArray<string>() { Length = constant.CharacterStrArrayLength[i] };
			for (int i = 0; i < dataIntegerArray2D.Length; i++)
			{
				Int64 length64 = constant.CharacterIntArray2DLength[i];
				int length = (int)(length64 >> 32);
				int length2 = (int)(length64 & 0x7FFFFFFF);
				dataIntegerArray2D[i] = new Int64[length, length2];
			}
			for (int i = 0; i < dataStringArray2D.Length; i++)
			{
				Int64 length64 = constant.CharacterStrArray2DLength[i];
				int length = (int)(length64 >> 32);
				int length2 = (int)(length64 & 0x7FFFFFFF);
				dataStringArray2D[i] = new string[length, length2];
			}
			UserDefCVarDataList = new List<object>();
			for (int i = 0; i < varData.UserDefinedCharaVarList.Count; i++)
			{
				UserDefinedVariableData d = varData.UserDefinedCharaVarList[i].DimData;
				object array = null;
				if (d.TypeIsStr)
				{
					switch (d.Dimension)
					{
						case 1:
							array = new string[d.Lengths[0]];
							break;
						case 2:
							array = new string[d.Lengths[0], d.Lengths[1]];
							break;
						case 3:
							array = new string[d.Lengths[0], d.Lengths[1], d.Lengths[2]];
							break;
					}
				}
				else if (d.TypeIsFloat)
				{
					switch (d.Dimension)
					{
						case 1:
							array = new double[d.Lengths[0]];
							break;
						case 2:
							array = new double[d.Lengths[0], d.Lengths[1]];
							break;
						case 3:
							array = new double[d.Lengths[0], d.Lengths[1], d.Lengths[2]];
							break;
					}
				}
				else
				{
					switch (d.Dimension)
					{
						case 1:
							array = new Int64[d.Lengths[0]];
							break;
						case 2:
							array = new Int64[d.Lengths[0], d.Lengths[1]];
							break;
						case 3:
							array = new Int64[d.Lengths[0], d.Lengths[1], d.Lengths[2]];
							break;
					}
				}
				if (array == null)
					throw new ExeEE("");
				UserDefCVarDataList.Add(array);
			}
		}


		public CharacterData(ConstantData constant, CharacterTemplate tmpl, VariableData varData)
			: this(constant, varData)
		{

			dataInteger[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.NO] = tmpl.No;
			dataString[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.NAME] = tmpl.Name;
			dataString[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CALLNAME] = tmpl.Callname;
			dataString[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.NICKNAME] = tmpl.Nickname;
			dataString[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MASTERNAME] = tmpl.Mastername;
			//M3: テンプレートは CSV 読込後に密配列へ折り畳まれている（既定値は焼き込み済み）ので、
			//Array.Copy で直コピーし、旧来の Dictionary 逐条コピーを排除する。
			//折り畳まれていないテンプレート（GetPseudoChara の擬似キャラ）は folded 配列が null → 既定値のまま（旧実装の空辞書と同じ）。
			Int64[] folded;
			//MAXBASE と BASE は同じ Maxbase データを共用する。
			folded = tmpl.GetFoldedIntArray(CharacterIntData.BASE);
			if (folded != null)
			{
				dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MAXBASE].FromArray(folded);
				CopyIntArrayClamped(dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.BASE], folded);
			}
			folded = tmpl.GetFoldedIntArray(CharacterIntData.MARK);
			if (folded != null)
				dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MARK].FromArray(folded);
			folded = tmpl.GetFoldedIntArray(CharacterIntData.EXP);
			if (folded != null)
				dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EXP].FromArray(folded);
			folded = tmpl.GetFoldedIntArray(CharacterIntData.ABL);
			if (folded != null)
				dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.ABL].FromArray(folded);
			folded = tmpl.GetFoldedIntArray(CharacterIntData.TALENT);
			if (folded != null)
				dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.TALENT].FromArray(folded);
			folded = tmpl.GetFoldedIntArray(CharacterIntData.RELATION);
			if (folded != null)
			{
				//折り畳み時に関係既定値 Config.RelationDef を焼き込んであるのでそのままコピー。
				dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.RELATION].FromArray(folded);
			}
			else
			{
				//未折り畳み（擬似キャラ）：旧実装どおり全域を Config.RelationDef で初期化。
				SparseArray<Int64> relation = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.RELATION];
				for (int i = 0; i < relation.Length; i++)
					relation[i] = Config.RelationDef;
			}
			folded = tmpl.GetFoldedIntArray(CharacterIntData.CFLAG);
			if (folded != null)
				dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CFLAG].FromArray(folded);
			folded = tmpl.GetFoldedIntArray(CharacterIntData.EQUIP);
			if (folded != null)
				dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EQUIP].FromArray(folded);
			folded = tmpl.GetFoldedIntArray(CharacterIntData.JUEL);
			if (folded != null)
				dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.JUEL].FromArray(folded);
			string[] foldedStr = tmpl.GetFoldedStrArray();
			if (foldedStr != null)
				dataStringArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CSTR].FromArray(foldedStr);
		}

		/// <summary>
		/// BASE 側へ Maxbase の密配列をコピーする。ターゲット配列長でクランプする。
		/// MAXBASE より短い場合、範囲外キーは SparseArray の overflow へ退避される旧実装の挙動を再現する
		/// （密配列は「定義値 or 0」を区別しないが、値 0 の範囲外書込みは SparseArray が除去するため旧実装と完全一致する）。
		/// </summary>
		static void CopyIntArrayClamped(SparseArray<Int64> target, Int64[] folded)
		{
			target.CopyFrom(folded);
			if (folded.Length > target.Length)
			{
				for (int i = target.Length; i < folded.Length; i++)
					if (folded[i] != 0L)
						target[i] = folded[i];
			}
		}

		public static int[] CharacterVarLength(VariableCode code, ConstantData constant)
		{
			int[] ret = null;
			VariableCode type = code & (VariableCode.__ARRAY_1D__ | VariableCode.__ARRAY_2D__ |
				VariableCode.__ARRAY_3D__ | VariableCode.__INTEGER__ | VariableCode.__STRING__);
			int i = (int)(code & VariableCode.__LOWERCASE__);
			if (i >= 0xF0)
				return null;
			Int64 length64 = 0;
			switch (type)
			{
				case VariableCode.__STRING__:
				case VariableCode.__INTEGER__:
					ret = new int[0];
					break;
				case VariableCode.__INTEGER__ | VariableCode.__ARRAY_1D__:
					ret = new int[1];
					ret[0] = constant.CharacterIntArrayLength[i];
					break;
				case VariableCode.__STRING__ | VariableCode.__ARRAY_1D__:
					ret = new int[1];
					ret[0] = constant.CharacterStrArrayLength[i];
					break;
				case VariableCode.__INTEGER__ | VariableCode.__ARRAY_2D__:
					ret = new int[2];
					length64 = constant.CharacterIntArray2DLength[i];
					ret[0] = (int)(length64 >> 32);
					ret[1] = (int)(length64 & 0x7FFFFFFF);
					break;
				case VariableCode.__STRING__ | VariableCode.__ARRAY_2D__:
					ret = new int[2];
					length64 = constant.CharacterStrArray2DLength[i];
					ret[0] = (int)(length64 >> 32);
					ret[1] = (int)(length64 & 0x7FFFFFFF);
					break;
				case VariableCode.__INTEGER__ | VariableCode.__ARRAY_3D__:
					throw new NotImplCodeEE();
				case VariableCode.__STRING__ | VariableCode.__ARRAY_3D__:
					throw new NotImplCodeEE();
			}
			return ret;
		}
		public void CopyTo(CharacterData other, VariableData varData)
		{
			for (int i = 0; i < dataInteger.Length; i++)
				other.dataInteger[i] = dataInteger[i];
			for (int i = 0; i < dataString.Length; i++)
				other.dataString[i] = dataString[i];

			for (int i = 0; i < dataIntegerArray.Length; i++)
				for (int j = 0; j < dataIntegerArray[i].Length; j++)
					other.dataIntegerArray[i][j] = dataIntegerArray[i][j];
			for (int i = 0; i < dataStringArray.Length; i++)
				for (int j = 0; j < dataStringArray[i].Length; j++)
					other.dataStringArray[i][j] = dataStringArray[i][j];

			for (int i = 0; i < dataIntegerArray2D.Length; i++)
			{
				int length1 = dataIntegerArray2D[i].GetLength(0);
				int length2 = dataIntegerArray2D[i].GetLength(1);
				for (int j = 0; j < length1; j++)
					for (int k = 0; k < length2; k++)
						other.dataIntegerArray2D[i][j, k] = dataIntegerArray2D[i][j, k];
			}
			for (int i = 0; i < dataStringArray2D.Length; i++)
			{
				int length1 = dataStringArray2D[i].GetLength(0);
				int length2 = dataStringArray2D[i].GetLength(1);
				for (int j = 0; j < length1; j++)
					for (int k = 0; k < length2; k++)
						other.dataStringArray2D[i][j, k] = dataStringArray2D[i][j, k];
			}
            if (UserDefCVarDataList.Count > 0)
            {
                foreach (UserDefinedCharaVariableToken var in varData.UserDefinedCharaVarList)
                {
                    if (!var.IsCharacterData)
                        continue;
                    if (var.IsString)
                    {
                        if (var.IsArray1D)
                        {
                            int length = ((string[])(UserDefCVarDataList[var.ArrayIndex])).GetLength(0);
                            for (int i = 0; i < length; i++)
                                ((string[])(other.UserDefCVarDataList[var.ArrayIndex]))[i] = ((string[])(UserDefCVarDataList[var.ArrayIndex]))[i];
                        }
                        else if (var.IsArray2D)
                        {
                            int length1 = ((string[,])UserDefCVarDataList[var.ArrayIndex]).GetLength(0);
                            int length2 = ((string[,])UserDefCVarDataList[var.ArrayIndex]).GetLength(1);
                            for (int i = 0; i < length1; i++)
                                for (int j = 0; j < length2; j++)
                                    ((string[,])(other.UserDefCVarDataList[var.ArrayIndex]))[i, j] = ((string[,])(UserDefCVarDataList[var.ArrayIndex]))[i, j];
                        }
                    }
                    else if (var.IsFloat)
                    {
                        if (var.IsArray1D)
                        {
                            int length = ((double[])(UserDefCVarDataList[var.ArrayIndex])).GetLength(0);
                            for (int i = 0; i < length; i++)
                                ((double[])(other.UserDefCVarDataList[var.ArrayIndex]))[i] = ((double[])(UserDefCVarDataList[var.ArrayIndex]))[i];
                        }
                        else if (var.IsArray2D)
                        {
                            int length1 = ((double[,])UserDefCVarDataList[var.ArrayIndex]).GetLength(0);
                            int length2 = ((double[,])UserDefCVarDataList[var.ArrayIndex]).GetLength(1);
                            for (int i = 0; i < length1; i++)
                                for (int j = 0; j < length2; j++)
                                    ((double[,])(other.UserDefCVarDataList[var.ArrayIndex]))[i, j] = ((double[,])(UserDefCVarDataList[var.ArrayIndex]))[i, j];
                        }
                    }
                    else
                    {
                        if (var.IsArray1D)
                        {
                            int length = ((Int64[])(UserDefCVarDataList[var.ArrayIndex])).GetLength(0);
                            for (int i = 0; i < length; i++)
                                ((Int64[])(other.UserDefCVarDataList[var.ArrayIndex]))[i] = ((Int64[])(UserDefCVarDataList[var.ArrayIndex]))[i];
                        }
                        else if (var.IsArray2D)
                        {
                            int length1 = ((Int64[,])(UserDefCVarDataList[var.ArrayIndex])).GetLength(0);
                            int length2 = ((Int64[,])(UserDefCVarDataList[var.ArrayIndex])).GetLength(1);
                            for (int i = 0; i < length1; i++)
                                for (int j = 0; j < length2; j++)
                                    ((Int64[,])(other.UserDefCVarDataList[var.ArrayIndex]))[i, j] = ((Int64[,])(UserDefCVarDataList[var.ArrayIndex]))[i, j];
                        }
                    }
                }
            }
		}

		const int strCount = (int)VariableCode.__COUNT_SAVE_CHARACTER_STRING__;
		const int intCount = (int)VariableCode.__COUNT_SAVE_CHARACTER_INTEGER__;
		const int intArrayCount = (int)VariableCode.__COUNT_SAVE_CHARACTER_INTEGER_ARRAY__;
		const int strArrayCount = (int)VariableCode.__COUNT_SAVE_CHARACTER_STRING_ARRAY__;

		public void SaveToStream(EraDataWriter writer)
		{

			for (int i = 0; i < strCount; i++)
				writer.Write(dataString[i]);
			for (int i = 0; i < intCount; i++)
				writer.Write(dataInteger[i]);
			for (int i = 0; i < intArrayCount; i++)
				writer.Write(dataIntegerArray[i]);
			for (int i = 0; i < strArrayCount; i++)
				writer.Write(dataStringArray[i]);
		}

		public void LoadFromStream(EraDataReader reader)
		{

			for (int i = 0; i < strCount; i++)
				dataString[i] = reader.ReadString();
			for (int i = 0; i < intCount; i++)
				dataInteger[i] = reader.ReadInt64();
			for (int i = 0; i < intArrayCount; i++)
				reader.ReadInt64Array(dataIntegerArray[i]);
			for (int i = 0; i < strArrayCount; i++)
				reader.ReadStringArray(dataStringArray[i]);
		}
		public void SaveToStreamExtended(EraDataWriter writer)
		{
			List<VariableCode> codeList = null;

			//dataString
			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__STRING__);
			foreach (VariableCode code in codeList)
				writer.WriteExtended(code.ToString(), dataString[(int)VariableCode.__LOWERCASE__ & (int)code]);
			writer.EmuSeparete();

			//datainteger
			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__INTEGER__);
			foreach (VariableCode code in codeList)
				writer.WriteExtended(code.ToString(), dataInteger[(int)VariableCode.__LOWERCASE__ & (int)code]);
			writer.EmuSeparete();

			//dataStringArray
			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__ARRAY_1D__ | VariableCode.__STRING__);
			foreach (VariableCode code in codeList)
				writer.WriteExtended(code.ToString(), dataStringArray[(int)VariableCode.__LOWERCASE__ & (int)code]);
			writer.EmuSeparete();

			//dataIntegerArray
			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__ARRAY_1D__ | VariableCode.__INTEGER__);
			foreach (VariableCode code in codeList)
				writer.WriteExtended(code.ToString(), dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)code]);
			writer.EmuSeparete();

			//dataStringArray2D
			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__ARRAY_2D__ | VariableCode.__STRING__);
			foreach (VariableCode code in codeList)
				writer.WriteExtended(code.ToString(), dataStringArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);
			writer.EmuSeparete();

			//dataIntegerArray2D
			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__ARRAY_2D__ | VariableCode.__INTEGER__);
			foreach (VariableCode code in codeList)
				writer.WriteExtended(code.ToString(), dataIntegerArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);
			writer.EmuSeparete();
		}

		public void LoadFromStreamExtended(EraDataReader reader)
		{
			Dictionary<string, string> strDic = reader.ReadStringExtended();
			Dictionary<string, Int64> intDic = reader.ReadInt64Extended();
			Dictionary<string, List<string>> strListDic = reader.ReadStringArrayExtended();
			Dictionary<string, List<Int64>> intListDic = reader.ReadInt64ArrayExtended();
			Dictionary<string, List<string[]>> str2DListDic = reader.ReadStringArray2DExtended();
			Dictionary<string, List<Int64[]>> int2DListDic = reader.ReadInt64Array2DExtended();

			List<VariableCode> codeList = null;

            codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__STRING__);
			foreach (VariableCode code in codeList)
				if (strDic.TryGetValue(code.ToString(), out var sfound))
					dataString[(int)VariableCode.__LOWERCASE__ & (int)code] = sfound;

			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__INTEGER__);
			foreach (VariableCode code in codeList)
				if (intDic.TryGetValue(code.ToString(), out var lfound))
					dataInteger[(int)VariableCode.__LOWERCASE__ & (int)code] = lfound;


			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__ARRAY_1D__ | VariableCode.__STRING__);
			foreach (VariableCode code in codeList)
				if (strListDic.TryGetValue(code.ToString(), out var listfound))
					copyListToArray(listfound, dataStringArray[(int)VariableCode.__LOWERCASE__ & (int)code]);

			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__ARRAY_1D__ | VariableCode.__INTEGER__);
			foreach (VariableCode code in codeList)
				if (intListDic.TryGetValue(code.ToString(), out var listlongfound))
					copyListToArray(listlongfound, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)code]);

			//dataStringArray2D
			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__ARRAY_2D__ | VariableCode.__STRING__);
			foreach (VariableCode code in codeList)
				if (str2DListDic.TryGetValue(code.ToString(), out var liststrarrfound))
					copyListToArray2D(liststrarrfound, dataStringArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);

			//dataIntegerArray2D
			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__ARRAY_2D__ | VariableCode.__INTEGER__);
			foreach (VariableCode code in codeList)
				if (int2DListDic.TryGetValue(code.ToString(), out var listlongarrfound))
					copyListToArray2D(listlongarrfound, dataIntegerArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);
		}

		public void LoadFromStreamExtended_Old1802(EraDataReader reader)
		{
			Dictionary<string, string> strDic = reader.ReadStringExtended();
			Dictionary<string, Int64> intDic = reader.ReadInt64Extended();
			Dictionary<string, List<string>> strListDic = reader.ReadStringArrayExtended();
			Dictionary<string, List<Int64>> intListDic = reader.ReadInt64ArrayExtended();

			List<VariableCode> codeList = null;

            codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__STRING__);
			foreach (VariableCode code in codeList)
				if (strDic.TryGetValue(code.ToString(), out var sfound))
					dataString[(int)VariableCode.__LOWERCASE__ & (int)code] = sfound;

			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__INTEGER__);
			foreach (VariableCode code in codeList)
				if (intDic.TryGetValue(code.ToString(), out var lfound))
					dataInteger[(int)VariableCode.__LOWERCASE__ & (int)code] = lfound;


			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__ARRAY_1D__ | VariableCode.__STRING__);
			foreach (VariableCode code in codeList)
				if (strListDic.TryGetValue(code.ToString(), out var listfound))
					copyListToArray(listfound, dataStringArray[(int)VariableCode.__LOWERCASE__ & (int)code]);

			codeList = VariableIdentifier.GetExtSaveList(VariableCode.__CHARACTER_DATA__ | VariableCode.__ARRAY_1D__ | VariableCode.__INTEGER__);
			foreach (VariableCode code in codeList)
				if (intListDic.TryGetValue(code.ToString(), out var listlongfound))
					copyListToArray(listlongfound, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)code]);

		}

		public void SaveToStreamBinary(EraBinaryDataWriter writer, VariableData varData)
		{
			//eramaker変数の保存
			foreach (KeyValuePair<string, VariableToken> pair in varData.GetVarTokenDic())
			{
				VariableToken var = pair.Value;
				if (!var.IsSavedata || !var.IsCharacterData || var.IsGlobal)
					continue;
				VariableCode code = var.Code;
				VariableCode flag = code & (VariableCode.__ARRAY_1D__ | VariableCode.__ARRAY_2D__ | VariableCode.__ARRAY_3D__ | VariableCode.__STRING__ | VariableCode.__INTEGER__);
				int CodeInt = var.CodeInt;
				switch (flag)
				{
					case VariableCode.__INTEGER__:
						writer.WriteWithKey(GetVarCodeName(code), dataInteger[CodeInt]);
						break;
					case VariableCode.__STRING__:
						writer.WriteWithKey(GetVarCodeName(code), dataString[CodeInt]);
						break;
					case VariableCode.__FLOAT__:
						writer.WriteWithKey(GetVarCodeName(code), dataFloat[CodeInt]);
						break;
					case VariableCode.__INTEGER__ | VariableCode.__ARRAY_1D__:
						writer.WriteWithKey(GetVarCodeName(code), dataIntegerArray[CodeInt]);
						break;
					case VariableCode.__STRING__ | VariableCode.__ARRAY_1D__:
						writer.WriteWithKey(GetVarCodeName(code), dataStringArray[CodeInt]);
						break;
					case VariableCode.__FLOAT__ | VariableCode.__ARRAY_1D__:
						writer.WriteWithKey(GetVarCodeName(code), dataFloatArray[CodeInt]);
						break;
					case VariableCode.__INTEGER__ | VariableCode.__ARRAY_2D__:
						writer.WriteWithKey(GetVarCodeName(code), dataIntegerArray2D[CodeInt]);
						break;
					case VariableCode.__STRING__ | VariableCode.__ARRAY_2D__:
						writer.WriteWithKey(GetVarCodeName(code), dataStringArray2D[CodeInt]);
						break;
					//case VariableCode.__INTEGER__ | VariableCode.__ARRAY_3D__:
					//    writer.Write(code.ToString(), dataIntegerArray3D[CodeInt]);
					//    break;
					//case VariableCode.__STRING__ | VariableCode.__ARRAY_3D__:
					//    writer.Write(code.ToString(), dataStringArray3D[CodeInt]);
					//    break;
				}
			}

			//1813追加
			if (UserDefCVarDataList.Count != 0)
			{
				writer.WriteSeparator();
				//#DIM宣言変数の保存
				foreach (UserDefinedCharaVariableToken var in varData.UserDefinedCharaVarList)
				{
					if (!var.IsSavedata || !var.IsCharacterData || var.IsGlobal)
						continue;
					writer.WriteWithKey(var.Name, UserDefCVarDataList[var.ArrayIndex]);
				}
			}

			writer.WriteEOC();
		}

		public void LoadFromStreamBinary(EraBinaryDataReader reader)
		{
			int codeInt = 0;
			bool userDefineData = false;
			while (true)
			{
				KeyValuePair<string, EraSaveDataType> nameAndType = reader.ReadVariableCode();
				VariableToken vToken = null;
				object array = null;
				if (nameAndType.Key != null)
				{
					// getVarTokenIsForbid + GetVariableToken の解決結果を per-load キャッシュする
					vToken = GetLoadVariableToken(nameAndType.Key);
					if (userDefineData)
					{
						if (vToken == null || !vToken.IsSavedata || !vToken.IsCharacterData || !(vToken is UserDefinedCharaVariableToken))
							array = null;
						else
							array = UserDefCVarDataList[((UserDefinedCharaVariableToken)vToken).ArrayIndex];
						vToken = null;
					}
					else
					{
                        if (vToken != null)
                            codeInt = (int)VariableCode.__LOWERCASE__ & (int)vToken.Code;
						array = null;
					}
				}
				switch (nameAndType.Value)
				{
					case EraSaveDataType.Separator:
						userDefineData = true;
						continue;
					case EraSaveDataType.EOF:
					case EraSaveDataType.EOC:
						goto whilebreak;
					case EraSaveDataType.PcFloat:
						if (vToken == null || !vToken.IsFloat || vToken.Dimension != 0)
							reader.ReadFloat();
						else
							dataFloat[codeInt] = reader.ReadFloat();
						break;
					case EraSaveDataType.PcFloatArray:
						if (userDefineData && array != null)
							reader.ReadPcFloatArray(array as double[], true);
						else if (vToken == null || !vToken.IsFloat || vToken.Dimension != 1)
							reader.ReadPcFloatArray((double[])null, true);
						else
							reader.ReadPcFloatArray(dataFloatArray[codeInt], true);
						break;
					case EraSaveDataType.PcFloatArray2D:
						if (userDefineData && array != null)
							reader.ReadPcFloatArray2D(array as double[,], true);
						else if (vToken == null || !vToken.IsFloat || vToken.Dimension != 2)
							reader.ReadPcFloatArray2D(null, true);
						else
							throw new FileEE("組み込みの2次元小数配列型キャラ変数は存在しません");
						break;
					case EraSaveDataType.PcFloatArray3D:
						if (userDefineData && array != null)
							reader.ReadPcFloatArray3D(array as double[, ,], true);
						else
							reader.ReadPcFloatArray3D(null, true);
						break;
					case EraSaveDataType.Int:
						if (vToken == null || !vToken.IsInteger || vToken.Dimension != 0)
							reader.ReadInt();
						else
							dataInteger[codeInt] = reader.ReadInt();
						break;
					case EraSaveDataType.Str:
						if (vToken == null || !vToken.IsString || vToken.Dimension != 0)
							reader.ReadString();
						else
							dataString[codeInt] = reader.ReadString();
						break;
					case EraSaveDataType.Float:
						if (vToken == null || !vToken.IsFloat || vToken.Dimension != 0)
							reader.ReadFloat();
						else
							dataFloat[codeInt] = reader.ReadFloat();
						break;
					case EraSaveDataType.FloatArray:
						if (userDefineData && array != null)
							reader.ReadFloatArray(array as double[], true);
						else if (vToken == null || !vToken.IsFloat || vToken.Dimension != 1)
							reader.ReadFloatArray((double[])null, true);
						else
							reader.ReadFloatArray(dataFloatArray[codeInt], true);
						break;
					case EraSaveDataType.FloatArray2D:
						if (userDefineData && array != null)
							reader.ReadFloatArray2D(array as double[,], true);
						else if (vToken == null || !vToken.IsFloat || vToken.Dimension != 2)
							reader.ReadFloatArray2D(null, true);
						else
							throw new FileEE("組み込みの2次元小数配列型キャラ変数は存在しません");
						break;
					case EraSaveDataType.IntArray:
						if (userDefineData && array != null)
						{
							if (array is SparseArray<Int64> sparseArray)
								reader.ReadIntArray(sparseArray, true);
							else
								reader.ReadIntArray(array as Int64[], true);
						}
						else if (vToken == null || !vToken.IsInteger || vToken.Dimension != 1)
							reader.ReadIntArray((Int64[])null, true);
						else
							reader.ReadIntArray(dataIntegerArray[codeInt], true);
						break;
					case EraSaveDataType.StrArray:
						if (userDefineData && array != null)
						{
							if (array is SparseArray<string> sparseArray)
								reader.ReadStrArray(sparseArray, true);
							else
								reader.ReadStrArray(array as string[], true);
						}
						else if (vToken == null || !vToken.IsString || vToken.Dimension != 1)
							reader.ReadStrArray((string[])null, true);
						else
							reader.ReadStrArray(dataStringArray[codeInt], true);
						break;
					case EraSaveDataType.IntArray2D:
						if (userDefineData && array != null)
							reader.ReadIntArray2D(array as Int64[,], true);
						else if (vToken == null || !vToken.IsInteger || vToken.Dimension != 2)
							reader.ReadIntArray2D(null, true);
						else
							reader.ReadIntArray2D(dataIntegerArray2D[codeInt], true);
						break;
					case EraSaveDataType.StrArray2D:
						if (userDefineData && array != null)
							reader.ReadStrArray2D(array as string[,], true);
						else if (vToken == null || !vToken.IsString || vToken.Dimension != 2)
							reader.ReadStrArray2D(null, true);
						else
							reader.ReadStrArray2D(dataStringArray2D[codeInt], true);
						break;
					//case EraSaveDataType.IntArray3D:
					//    if (vToken == null || !vToken.IsInteger || vToken.Dimension != 3)
					//        reader.ReadIntArray3D(null, true);
					//    else
					//        reader.ReadIntArray3D(dataIntegerArray3D[codeInt], true);
					//    break;
					//case EraSaveDataType.StrArray3D:
					//    if (vToken == null || !vToken.IsString || vToken.Dimension != 3)
					//        reader.ReadStrArray3D(null, true);
					//    else
					//        reader.ReadStrArray3D(dataStringArray3D[codeInt], true);
					//    break;
					default:
						throw new FileEE("データ異常");
				}
			}
		whilebreak:
			return;
		}


		private void copyListToArray<T>(List<T> srcList, T[] destArray)
		{
			int count = Math.Min(srcList.Count, destArray.Length);
			srcList.CopyTo(0, destArray, 0, count);
			//for (int i = 0; i < count; i++)
			//{
			//    destArray[i] = srcList[i];
			//}
		}

		private void copyListToArray<T>(List<T> srcList, SparseArray<T> destArray)
		{
			destArray.Clear();
			int count = Math.Min(srcList.Count, destArray.Length);
			for (int i = 0; i < count; i++)
				destArray[i] = srcList[i];
		}
		private void copyListToArray2D<T>(List<T[]> srcList, T[,] destArray)
		{
			int countX = Math.Min(srcList.Count, destArray.GetLength(0));
			int dLength = destArray.GetLength(1);
			for (int x = 0; x < countX; x++)
			{
				T[] srcArray = srcList[x];
				int countY = Math.Min(srcArray.Length, dLength);
				for (int y = 0; y < countY; y++)
				{
					destArray[x, y] = srcArray[y];
				}
			}
		}

		public void setValueAll(int varInt, Int64 value)
		{
			dataInteger[varInt] = value;
		}

		public void setValueAll(int varInt, string value)
		{
			dataString[varInt] = value;
		}

		public void setValueAll1D(int varInt, Int64 value, int start, int end)
		{
			SparseArray<Int64> array = dataIntegerArray[varInt];
			for (int i = start; i < end; i++)
				array[i] = value;
		}

		public void setValueAll1D(int varInt, string value, int start, int end)
		{
			SparseArray<string> array = dataStringArray[varInt];
			for (int i = start; i < end; i++)
				array[i] = value;
		}

		public void setValueAll2D(int varInt, Int64 value)
		{
			Int64[,] array = dataIntegerArray2D[varInt];
			int a1 = array.GetLength(0);
			int a2 = array.GetLength(1);
			for (int i = 0; i < a1; i++)
				for (int j = 0; j < a2; j++)
					array[i, j] = value;
		}

		public void setValueAll2D(int varInt, string value)
		{
			string[,] array = dataStringArray2D[varInt];
			int a1 = array.GetLength(0);
			int a2 = array.GetLength(1);
			for (int i = 0; i < a1; i++)
				for (int j = 0; j < a2; j++)
					array[i, j] = value;
		}

		#region IDisposable メンバ

		public void Dispose()
		{
			for (int i = 0; i < dataIntegerArray.Length; i++)
				dataIntegerArray[i] = null;
			for (int i = 0; i < dataStringArray.Length; i++)
				dataStringArray[i] = null;
			for (int i = 0; i < dataIntegerArray2D.Length; i++)
				dataIntegerArray2D[i] = null;
		}

		#endregion
		public SparseArray<Int64> CFlag
		{
			get { return dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CFLAG]; }
		}
		public Int64 NO
		{
			get { return dataInteger[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.NO]; }
		}

		#region sort
		public IComparable temp_SortKey;
		public int temp_CurrentOrder;
		//Comparison<CharacterData>
		public static int AscCharacterComparison(CharacterData x, CharacterData y)
		{
			int ret = x.temp_SortKey.CompareTo(y.temp_SortKey);
			if (ret != 0)
				return ret;
			return x.temp_CurrentOrder.CompareTo(y.temp_CurrentOrder);
		}
		public static int DescCharacterComparison(CharacterData x, CharacterData y)
		{
			int ret = x.temp_SortKey.CompareTo(y.temp_SortKey);
			if (ret != 0)
				return -ret;
			return x.temp_CurrentOrder.CompareTo(y.temp_CurrentOrder);
		}

		public void SetSortKey(VariableToken sortkey, Int64 elem64)
		{
			//チェック済み
			//if (!sortkey.IsCharacterData)
			//    throw new ExeEE("キャラクタ変数でない");
			if (sortkey.IsString)
			{
                if (sortkey.IsArray2D)
                {
                    string[,] array;
                    if (sortkey is UserDefinedCharaVariableToken)
                        array = (string[,])UserDefCVarDataList[((UserDefinedCharaVariableToken)sortkey).ArrayIndex];
                    else
                        array = dataStringArray2D[sortkey.CodeInt];
                    int elem1 = (int)(elem64 >> 32);
                    int elem2 = (int)(elem64 & 0x7FFFFFFF);
                    if (elem1 < 0 || elem1 >= array.GetLength(0) || elem2 < 0 || elem2 >= array.GetLength(1))
                        throw new CodeEE("ソートキーが配列外を参照しています");
                    temp_SortKey = array[elem1, elem2];
                }
                else if (sortkey.IsArray1D)
                {
                    object array;
                    if (sortkey is UserDefinedCharaVariableToken)
                        array = UserDefCVarDataList[((UserDefinedCharaVariableToken)sortkey).ArrayIndex];
                    else
                        array = dataStringArray[sortkey.CodeInt];
                    int length = Get1DLength(array);
                    if (elem64 < 0 || elem64 >= length)
                        throw new CodeEE("ソートキーが配列外を参照しています");
                    string value = GetString1D(array, elem64);
                    if (value != null)
                        temp_SortKey = value;
                    else
                        temp_SortKey = "";
                }
                else
                {
                    //ユーザー定義キャラ変数は非配列がない
                    if (dataString[sortkey.CodeInt] != null)
                        temp_SortKey = dataString[sortkey.CodeInt];
                    else
                        temp_SortKey = "";
                }
			}
			else
			{
				if (sortkey.IsArray2D)
				{
                    Int64[,] array;
                    if (sortkey is UserDefinedCharaVariableToken)
                        array = (Int64[,])UserDefCVarDataList[((UserDefinedCharaVariableToken)sortkey).ArrayIndex];
                    else
                        array = dataIntegerArray2D[sortkey.CodeInt];
					int elem1 = (int)(elem64 >> 32);
					int elem2 = (int)(elem64 & 0x7FFFFFFF);
					if (elem1 < 0 || elem1 >= array.GetLength(0) || elem2 < 0 || elem2 >= array.GetLength(1))
						throw new CodeEE("ソートキーが配列外を参照しています");
					temp_SortKey = array[elem1, elem2];
				}
				else if (sortkey.IsArray1D)
				{
                    object array;
                    if (sortkey is UserDefinedCharaVariableToken)
                        array = UserDefCVarDataList[((UserDefinedCharaVariableToken)sortkey).ArrayIndex];
                    else
                        array = dataIntegerArray[sortkey.CodeInt];
					int length = Get1DLength(array);
					if (elem64 < 0 || elem64 >= length)
						throw new CodeEE("ソートキーが配列外を参照しています");
					temp_SortKey = GetInt1D(array, elem64);
				}
				else
				{
					temp_SortKey = dataInteger[sortkey.CodeInt];
				}
			}
		}
		#endregion

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
			throw new ExeEE("キャラクタ配列データの型が不正です");
		}

		static Int64 GetInt1D(object array, long index)
		{
			if (array is SparseArray<Int64> sparseArray)
				return sparseArray[index];
			return ((Int64[])array)[index];
		}

		static string GetString1D(object array, long index)
		{
			if (array is SparseArray<string> sparseArray)
				return sparseArray[index];
			return ((string[])array)[index];
		}
	}
}
