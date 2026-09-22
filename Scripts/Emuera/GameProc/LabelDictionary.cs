using System;
using System.Collections.Generic;
using System.Text;
//using System.Windows.Forms;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameData.Variable;

namespace MinorShift.Emuera.GameProc
{
	//1.713 LogicalLine.csから分割
	/// <summary>
	/// ラベルのジャンプ先の辞書。Erbファイル読み込み時に作成
	/// </summary>
	internal sealed class LabelDictionary
	{
		public LabelDictionary()
		{
			Initialized = false;
		}
		/// <summary>
		/// 本体。全てのFunctionLabelLineを記録
		/// </summary>
		Dictionary<string, List<FunctionLabelLine>> labelAtDic = new Dictionary<string, List<FunctionLabelLine>>();
		List<FunctionLabelLine> invalidList = new List<FunctionLabelLine>();
		//v24/snake 参照実装と同じく $ラベルは (LabelName, ParentLabelLine) 複合キーで辞書化する。
		//外層比較器は v24 の Config.StrComper 相当（ICVariable 時 OrdinalIgnoreCase、それ以外 Ordinal）。
		//内層キーは参照等価（従来の == と同じ）。ParseLabelLine が ICVariable 時のみラベル名を大文字化するため、
		//ICVariable=false では $Foo と $foo を別ラベルとして扱う必要があり、常時 OrdinalIgnoreCase にすると意味が変わる。
		Dictionary<string, Dictionary<FunctionLabelLine, GotoLabelLine>> labelDollarList =
			new Dictionary<string, Dictionary<FunctionLabelLine, GotoLabelLine>>(StringComparer.FromComparison(Config.SCVariable));
		int count;

		Dictionary<string, int> loadedFileDic = new Dictionary<string, int>();
		int currentFileCount = 0;
		int totalFileCount = 0;

		public int Count { get { return count; } }

		/// <summary>
		/// これがfalseである間は式中関数は呼べない
		/// （つまり関数宣言の初期値として式中関数は使えない）
		/// </summary>
		public bool Initialized { get; set; }
		#region Initialized 前用
		public FunctionLabelLine GetSameNameLabel(FunctionLabelLine point)
		{
			string id = point.LabelName;
			if (!labelAtDic.ContainsKey(id))
				return null;
			if (point.IsError)
				return null;
			List<FunctionLabelLine> labelList = labelAtDic[id];
			if (labelList.Count <= 1)
				return null;
			return labelList[0];
		}


		Dictionary<string, List<FunctionLabelLine>[]> eventLabelDic = new Dictionary<string, List<FunctionLabelLine>[]>();
		Dictionary<string, FunctionLabelLine> noneventLabelDic = new Dictionary<string, FunctionLabelLine>();

		public IEnumerable<string> NoneventKeys
		{
			get { return noneventLabelDic.Keys; }
		}

		public void SortLabel(FunctionLabelLine label)
		{
			string key = label.LabelName;
			noneventLabelDic.Remove(key);
			eventLabelDic.Remove(key);

			if (!labelAtDic.TryGetValue(key, out List<FunctionLabelLine> list) || list.Count == 0)
				return;
			if (list.Count > 1)
				list.Sort();

			if (!list[0].IsEvent)
			{
				noneventLabelDic[key] = list[0];
				GlobalStatic.IdentifierDictionary.resizeLocalVars("ARG", list[0].LabelName, list[0].ArgLength);
				GlobalStatic.IdentifierDictionary.resizeLocalVars("ARGS", list[0].LabelName, list[0].ArgsLength);
				GlobalStatic.IdentifierDictionary.resizeLocalVars("ARGF", list[0].LabelName, list[0].ArgFloatLength);
				return;
			}

			if (Config.CompatiCallEvent)
				noneventLabelDic[key] = list[0];

			List<FunctionLabelLine>[] eventLabels = new List<FunctionLabelLine>[4];
			for (int i = 0; i < eventLabels.Length; i++)
				eventLabels[i] = new List<FunctionLabelLine>();

			foreach (FunctionLabelLine line in list)
			{
				if (line.IsOnly)
					eventLabels[0].Add(line);
				if (line.IsPri)
					eventLabels[1].Add(line);
				if (line.IsLater)
					eventLabels[3].Add(line);
				if (!line.IsPri && !line.IsLater)
					eventLabels[2].Add(line);
			}

			int localMax = 0;
			int localsMax = 0;
			int localFloatMax = 0;
			for (int i = 0; i < eventLabels.Length; i++)
			{
				for (int j = 0; j < eventLabels[i].Count; j++)
				{
					if (eventLabels[i][j].LocalLength > localMax)
						localMax = eventLabels[i][j].LocalLength;
					if (eventLabels[i][j].LocalsLength > localsMax)
						localsMax = eventLabels[i][j].LocalsLength;
					if (eventLabels[i][j].LocalFloatLength > localFloatMax)
						localFloatMax = eventLabels[i][j].LocalFloatLength;
				}
			}
			if (localMax < GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCAL"))
				localMax = GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCAL");
			if (localsMax < GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCALS"))
				localsMax = GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCALS");
			if (localFloatMax < GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCALF"))
				localFloatMax = GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCALF");
			for (int i = 0; i < eventLabels.Length; i++)
			{
				for (int j = 0; j < eventLabels[i].Count; j++)
				{
					eventLabels[i][j].LocalLength = localMax;
					eventLabels[i][j].LocalsLength = localsMax;
					eventLabels[i][j].LocalFloatLength = localFloatMax;
				}
			}
			eventLabelDic.Add(key, eventLabels);
		}

		public void SortLabels()
		{
			foreach (KeyValuePair<string, List<FunctionLabelLine>[]> pair in eventLabelDic)
				foreach (List<FunctionLabelLine> list in pair.Value)
					list.Clear();
			eventLabelDic.Clear();
			noneventLabelDic.Clear();
			foreach (KeyValuePair<string, List<FunctionLabelLine>> pair in labelAtDic)
			{
				string key = pair.Key;
				List<FunctionLabelLine> list = pair.Value;
				if(list.Count > 1)
					list.Sort();
				if (!list[0].IsEvent)
				{
					noneventLabelDic.Add(key, list[0]);
                    GlobalStatic.IdentifierDictionary.resizeLocalVars("ARG", list[0].LabelName, list[0].ArgLength);
                    GlobalStatic.IdentifierDictionary.resizeLocalVars("ARGS", list[0].LabelName, list[0].ArgsLength);
                    GlobalStatic.IdentifierDictionary.resizeLocalVars("ARGF", list[0].LabelName, list[0].ArgFloatLength);
					continue;
				}
				//1810alpha010 オプションによりイベント関数をイベント関数でないかのように呼び出すことを許可
				//eramaker仕様 - #PRI #LATER #SINGLE等を無視し、最先に定義された関数1つのみを呼び出す
				if (Config.CompatiCallEvent)
					noneventLabelDic.Add(key, list[0]);
				List<FunctionLabelLine>[] eventLabels = new List<FunctionLabelLine>[4];
                List<FunctionLabelLine> onlylist = new List<FunctionLabelLine>();
				List<FunctionLabelLine> prilist = new List<FunctionLabelLine>();
				List<FunctionLabelLine> normallist = new List<FunctionLabelLine>();
                List<FunctionLabelLine> laterlist = new List<FunctionLabelLine>();
                int localMax = 0;
                int localsMax = 0;
                int localFloatMax = 0;
				for (int i = 0; i < list.Count; i++)
				{
                    if (list[i].LocalLength > localMax)
                        localMax = list[i].LocalLength;
                    if (list[i].LocalsLength > localsMax)
                        localsMax = list[i].LocalsLength;
                    if (list[i].LocalFloatLength > localFloatMax)
                        localFloatMax = list[i].LocalFloatLength;
                    if (list[i].IsOnly)
                        onlylist.Add(list[i]);
					if (list[i].IsPri)
						prilist.Add(list[i]);
					if (list[i].IsLater)
						laterlist.Add(list[i]);//#PRIかつ#LATERなら二重に登録する。eramakerの仕様
					if ((!list[i].IsPri) && (!list[i].IsLater))
						normallist.Add(list[i]);
				}
                if (localMax < GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCAL"))
                    localMax = GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCAL");
                if (localsMax < GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCALS"))
                    localsMax = GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCALS");
                if (localFloatMax < GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCALF"))
                    localFloatMax = GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCALF");
                eventLabels[0] = onlylist;
				eventLabels[1] = prilist;
				eventLabels[2] = normallist;
				eventLabels[3] = laterlist;
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < eventLabels[i].Count; j++)
                    {
                        eventLabels[i][j].LocalLength = localMax;
                        eventLabels[i][j].LocalsLength = localsMax;
                        eventLabels[i][j].LocalFloatLength = localFloatMax;
                    }
                }
                eventLabelDic.Add(key, eventLabels);
			}
		}

		public void RemoveAll()
		{
			Initialized = false;
			count = 0;
			foreach (KeyValuePair<string, List<FunctionLabelLine>[]> pair in eventLabelDic)
				foreach (List<FunctionLabelLine> list in pair.Value)
					list.Clear();
			eventLabelDic.Clear();
			noneventLabelDic.Clear();

			foreach (KeyValuePair<string, List<FunctionLabelLine>> pair in labelAtDic)
				pair.Value.Clear();
			labelAtDic.Clear();
			labelDollarList.Clear();
			loadedFileDic.Clear();
			invalidList.Clear();
			currentFileCount = 0;
			totalFileCount = 0;
		}

		public void EnsureCapacity(int estimatedLabels, int estimatedFiles = 0)
		{
			if (estimatedLabels > 0)
			{
				labelAtDic.EnsureCapacity(estimatedLabels);
				eventLabelDic.EnsureCapacity(estimatedLabels);
				noneventLabelDic.EnsureCapacity(estimatedLabels);
				int sideListCapacity = Math.Max(4, estimatedLabels / 8);
				if (invalidList.Capacity < sideListCapacity)
					invalidList.Capacity = sideListCapacity;
				labelDollarList.EnsureCapacity(sideListCapacity);
			}
			if (estimatedFiles > 0)
				loadedFileDic.EnsureCapacity(estimatedFiles);
		}

		public void RemoveLabelWithPath(string fname)
		{
			bool removed = false;
			List<FunctionLabelLine> labelLines;
			List<FunctionLabelLine> removeLine = new List<FunctionLabelLine>();
			List<string> removeKey = new List<string>();
			foreach (KeyValuePair<string, List<FunctionLabelLine>> pair in labelAtDic)
			{
				string key = pair.Key;
				labelLines = pair.Value;
				foreach (FunctionLabelLine labelLine in labelLines)
				{
					if (string.Equals(labelLine.Position.Filename, fname, Config.SCIgnoreCase))
						removeLine.Add(labelLine);
				}
				foreach (FunctionLabelLine remove in removeLine)
				{
					labelLines.Remove(remove);
					count--;
					removed = true;
					if (labelLines.Count == 0)
						removeKey.Add(key);
				}
				removeLine.Clear();
			}
			foreach (string rKey in removeKey)
			{
				labelAtDic.Remove(rKey);
			}
			for (int i = 0; i < invalidList.Count; i++)
			{
				if (string.Equals(invalidList[i].Position.Filename, fname, Config.SCIgnoreCase))
				{
					invalidList.RemoveAt(i);
					removed = true;
					i--;
				}
			}

			{
				List<string> removeDollarKeys = null;
				foreach (KeyValuePair<string, Dictionary<FunctionLabelLine, GotoLabelLine>> pair in labelDollarList)
				{
					List<FunctionLabelLine> removeInnerKeys = null;
					foreach (KeyValuePair<FunctionLabelLine, GotoLabelLine> inner in pair.Value)
					{
						GotoLabelLine label = inner.Value;
						if (string.Equals(label.Position.Filename, fname, Config.SCIgnoreCase)
							|| (label.ParentLabelLine != null && string.Equals(label.ParentLabelLine.Position.Filename, fname, Config.SCIgnoreCase)))
						{
							if (removeInnerKeys == null)
								removeInnerKeys = new List<FunctionLabelLine>();
							removeInnerKeys.Add(inner.Key);
							removed = true;
						}
					}
					if (removeInnerKeys != null)
					{
						for (int i = 0; i < removeInnerKeys.Count; i++)
							pair.Value.Remove(removeInnerKeys[i]);
						if (pair.Value.Count == 0)
						{
							if (removeDollarKeys == null)
								removeDollarKeys = new List<string>();
							removeDollarKeys.Add(pair.Key);
						}
					}
				}
				if (removeDollarKeys != null)
				{
					for (int i = 0; i < removeDollarKeys.Count; i++)
						labelDollarList.Remove(removeDollarKeys[i]);
				}
			}

			if (removed && count < 0)
				count = 0;
			if (removed)
				SortLabels();
		}


		public void AddFilename(string filename)
		{
            if (loadedFileDic.TryGetValue(filename, out int curCount))
            {
                // 参考侧：重载已载文件时 FileIndex 取已载文件总数（loadedFileSet.Count），
                // 使重载文件在同名标签排序中排在更晚载入的文件之后
                currentFileCount = loadedFileDic.Count;
                RemoveLabelWithPath(filename);
                return;
            }
            totalFileCount++;
			currentFileCount = totalFileCount;
			loadedFileDic.Add(filename, totalFileCount);
		}
		public void AddLabel(FunctionLabelLine point)
		{
			point.Index = count;
			point.FileIndex = currentFileCount;
			count++;
			string id = point.LabelName;
            List<FunctionLabelLine> function_label_line_list = null;
			if(!labelAtDic.TryGetValue(id, out function_label_line_list))
			{
                function_label_line_list = new List<FunctionLabelLine>();
				labelAtDic.Add(id, function_label_line_list);
			}
            function_label_line_list.Add(point);
        }

		public bool AddLabelDollar(GotoLabelLine point)
		{
			string id = point.LabelName;
			if (!labelDollarList.TryGetValue(id, out Dictionary<FunctionLabelLine, GotoLabelLine> dollarDic))
			{
				dollarDic = new Dictionary<FunctionLabelLine, GotoLabelLine>();
				labelDollarList.Add(id, dollarDic);
			}
			//TryAdd：既に同じ関数内に同名$ラベルがあれば複製とみなす（従来の線形走査と同じ意味）。
			return dollarDic.TryAdd(point.ParentLabelLine, point);
		}

		#endregion

		
		public List<FunctionLabelLine>[] GetEventLabels(string key)
		{
            if (eventLabelDic.TryGetValue(key, out List<FunctionLabelLine>[] ret))
                return ret;
            else
                return null;
        }

		public FunctionLabelLine GetNonEventLabel(string key)
		{
            if (noneventLabelDic.TryGetValue(key, out FunctionLabelLine ret))
                return ret;
            else
                return null;
        }

		public List<FunctionLabelLine> GetAllLabels(bool getInvalidList)
		{
			List<FunctionLabelLine> ret = new List<FunctionLabelLine>();
			foreach (List<FunctionLabelLine> list in labelAtDic.Values)
				ret.AddRange(list);
			if(getInvalidList)
				ret.AddRange(invalidList);
			return ret;
		}

		public GotoLabelLine GetLabelDollar(string key, FunctionLabelLine labelAtLine)
		{
			if (labelDollarList.TryGetValue(key, out Dictionary<FunctionLabelLine, GotoLabelLine> dollarDic))
			{
				if (dollarDic.TryGetValue(labelAtLine, out GotoLabelLine label))
					return label;
			}
			return null;
		}
		
		internal void AddInvalidLabel(FunctionLabelLine invalidLabelLine)
		{
			invalidList.Add(invalidLabelLine);
		}
    }
}
