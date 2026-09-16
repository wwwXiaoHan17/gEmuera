using System;

namespace MinorShift.Emuera.GameView
{
	internal sealed partial class EmueraConsole : IDisposable
	{
		internal ConsoleDisplayLine GetDisplayLinesForuEmuera(int index)
		{
			lock (displayLineLock)
			{
				if(index < 0 || index >= displayLineList.Count)
					return null;
				return displayLineList[index];
			}
		}
		internal int GetDisplayLinesCount()
		{
			lock (displayLineLock)
				return displayLineList.Count;
		}
		internal ConsoleDisplayLine[] GetDisplayLinesSnapshotForuEmuera()
		{
			return GetDisplayLinesSnapshotForuEmuera(-1, out _, out _);
		}

		internal ConsoleDisplayLine[] GetDisplayLinesSnapshotForuEmuera(
			int minimumLineNo, out int totalCount, out int snapshotStartIndex)
		{
			lock (displayLineLock)
			{
				totalCount = displayLineList.Count;
				snapshotStartIndex = 0;
				if (totalCount == 0)
					return Array.Empty<ConsoleDisplayLine>();

				// Godot 侧会主动裁掉较早的历史行；这些行之后不会参与更新或点击，
				// 无需在每次动态地图刷新时重新复制。清屏、行号回绕或序列异常时
				// TryFindSnapshotStartIndex 返回 false，继续走完整快照保证兼容行为。
				if (minimumLineNo >= 0
					&& TryFindSnapshotStartIndex(minimumLineNo, out int startIndex))
					snapshotStartIndex = startIndex;

				int snapshotCount = totalCount - snapshotStartIndex;
				var snapshot = new ConsoleDisplayLine[snapshotCount];
				for (int i = 0; i < snapshotCount; i++)
					snapshot[i] = displayLineList[snapshotStartIndex + i];
				return snapshot;
			}
		}

		bool TryFindSnapshotStartIndex(int minimumLineNo, out int startIndex)
		{
			startIndex = 0;
			int count = displayLineList.Count;
			if (count == 0)
				return true;

			var first = displayLineList[0];
			var last = displayLineList[count - 1];
			if (first == null || last == null
				|| first.LineNo > last.LineNo
				|| last.LineNo < minimumLineNo)
				return false;

			int low = 0;
			int high = count;
			while (low < high)
			{
				int mid = low + ((high - low) / 2);
				var line = displayLineList[mid];
				if (line == null)
					return false;
				if (line.LineNo < minimumLineNo)
					low = mid + 1;
				else
					high = mid;
			}

			if (low >= count)
				return false;
			startIndex = low;
			return true;
		}
		internal bool IsInitializing
		{
			get { return state == ConsoleState.Initializing; }
		}
		internal int LastButtonGeneration
		{
			get { return lastButtonGeneration; }
		}
		internal bool IsWaitingInput
		{
			get { return IsWaitInputState; }
		}
		internal bool IsWaitingInputSomething
		{
			get {
				return IsWaitInputState &&
						  (inputReq.InputType == GameProc.InputType.IntValue ||
						  inputReq.InputType == GameProc.InputType.StrValue);
			}
		}
		// 当前等待是否为"值/按钮选择"等待（INPUT/INPUTS/TINPUT 等带值输入）。
		// quick 面板据此判定核心是否重新进入按钮选择点：EnterKey/AnyKey 等
		// 纯推进等待不应重新弹出快捷按钮面板。
		internal bool IsWaitingValueSelection
		{
			get
			{
				return IsWaitInputState && inputReq != null &&
					(inputReq.InputType == GameProc.InputType.IntValue ||
					inputReq.InputType == GameProc.InputType.StrValue ||
					inputReq.InputType == GameProc.InputType.AnyValue ||
					inputReq.InputType == GameProc.InputType.IntButton ||
					inputReq.InputType == GameProc.InputType.StrButton);
			}
		}
		internal GameProc.InputType InputType
		{
			get
			{
				if(inputReq == null)
					return GameProc.InputType.Void;
				return inputReq.InputType;
			}
		}
		internal bool IsWaitingOnePhrase
		{
			get { return IsWaitInputState && inputReq != null && inputReq.OneInput; }
		}
	}
}
