using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.GameData.Variable
{
	/// <summary>
	/// 1D 变量用の配列。eraTW 系ゲームは配列のほぼ全要素が書き込まれる（密集）ため、
	/// 内部を密集配列 T[] で保持する（O(1) 索引・読み出し時の無分配・Clear/FromArray は Array.Clear/Array.Copy）。
	/// これは v24 参考実装の long[][]/string[][] 直索引構造と同じモデルであり、毎回のハッシュ参照と
	/// 「0 クリア → Dictionary.Remove 連打」の Remove ストームを排除する。
	/// 旧 Dictionary 実装が許容していた「範囲外インデックスへの書き込み」は overflow 辞書で保持し、
	/// 挙動を完全に一致させる。2D/3D は既存コードが CLR 多次元配列契約に強く依存するため、本クラスは 1D 専用に留める。
	/// </summary>
	internal sealed class SparseArray<T>
	{
		readonly T defaultValue;
		// 密集バッファ。data.Length は常に logicalLength と一致する。
		T[] data;
		int logicalLength;
		// 旧実装で Dictionary が許容していた範囲外キー（index < 0 または index >= logicalLength）の保持用。
		// 通常の ERB では範囲外書き込みは発生しないため実質空であり、ホットパスにオーバーヘッドを加えない。
		Dictionary<long, T> overflow;

		public SparseArray(T defaultValue = default)
		{
			this.defaultValue = defaultValue;
			data = Array.Empty<T>();
		}

		public int Length
		{
			get { return logicalLength; }
			set
			{
				if (value == logicalLength)
					return;
				if (value < logicalLength)
				{
					// 旧実装は Length を変えても辞書の要素を保持する（縮小後の index 読み出しも値が残る）。
					// 縮小時に区間 [value, logicalLength) の非既定値要素を overflow へ退避して挙動を一致させる。
					for (int i = value; i < logicalLength; i++)
					{
						if (!EqualityComparer<T>.Default.Equals(data[i], defaultValue))
						{
							if (overflow == null)
								overflow = new Dictionary<long, T>();
							overflow[i] = data[i];
						}
					}
				}
				T[] newData = new T[value];
				if (logicalLength > 0 && value > 0)
					Array.Copy(data, 0, newData, 0, Math.Min(value, logicalLength));
				data = newData;
				logicalLength = value;
				MergeInRangeOverflowIntoData();
			}
		}

		// overflow キーのうち [0, logicalLength) に入ったもの（Length 拡大や RemoveRange による詰め替えで
		// 範囲内へ入った範囲外書き込み）を data へマージし、overflow から除去する。
		// これにより「overflow は常に範囲外キーのみ」という不変条件が保たれ、
		// 範囲内の this[] 読み出しは data だけを見れば旧実装と一致する。
		void MergeInRangeOverflowIntoData()
		{
			if (overflow == null || overflow.Count == 0)
				return;
			List<long> toRemove = null;
			foreach (KeyValuePair<long, T> pair in overflow)
			{
				if (pair.Key >= 0 && pair.Key < logicalLength)
				{
					data[pair.Key] = pair.Value;
					if (toRemove == null)
						toRemove = new List<long>();
					toRemove.Add(pair.Key);
				}
			}
			if (toRemove != null)
				foreach (long key in toRemove)
					overflow.Remove(key);
		}

		/// <summary>格納されている非既定値要素の数（旧実装の Dictionary.Count）。</summary>
		public int Count
		{
			get
			{
				int count = 0;
				EqualityComparer<T> comparer = EqualityComparer<T>.Default;
				for (int i = 0; i < logicalLength; i++)
					if (!comparer.Equals(data[i], defaultValue))
						count++;
				if (overflow != null)
					count += overflow.Count;
				return count;
			}
		}

		public IEnumerable<KeyValuePair<long, T>> Entries
		{
			get
			{
				EqualityComparer<T> comparer = EqualityComparer<T>.Default;
				for (int i = 0; i < logicalLength; i++)
					if (!comparer.Equals(data[i], defaultValue))
						yield return new KeyValuePair<long, T>(i, data[i]);
				if (overflow != null)
					foreach (KeyValuePair<long, T> pair in overflow)
						yield return pair;
			}
		}

		public T this[long index]
		{
			get
			{
				// ホットパス（FLAG/CFLAG/TALENT など）向け：1 回の unsigned 比較で範囲チェックし、
				// 範囲内なら密集配列へ直接アクセス（旧実装のハッシュ参照を排除）。
				if ((ulong)index < (ulong)logicalLength)
					return data[index];
				if (overflow != null && overflow.TryGetValue(index, out T value))
					return value;
				return defaultValue;
			}
			set
			{
				if ((ulong)index < (ulong)logicalLength)
				{
					data[index] = value;
				}
				else
				{
					if (overflow == null)
						overflow = new Dictionary<long, T>();
					if (EqualityComparer<T>.Default.Equals(value, defaultValue))
						overflow.Remove(index);
					else
						overflow[index] = value;
				}
			}
		}

		public void Clear()
		{
			if (logicalLength > 0)
				Array.Clear(data, 0, logicalLength);
			overflow = null;
		}

		public bool ContainsIndex(long index)
		{
			if (index >= 0 && index < logicalLength)
				return !EqualityComparer<T>.Default.Equals(data[index], defaultValue);
			return overflow != null && overflow.ContainsKey(index);
		}

		public void Remove(long index)
		{
			if (index >= 0 && index < logicalLength)
				data[index] = defaultValue;
			else if (overflow != null)
				overflow.Remove(index);
		}

		public void FromArray(T[] source)
		{
			if (source == null)
			{
				// 旧実装と同じく「クリアして何もしない（logicalLength は変更しない）」
				if (logicalLength > 0)
					Array.Clear(data, 0, logicalLength);
				overflow = null;
				return;
			}
			if (data.Length != source.Length)
				data = new T[source.Length];
			else
				Array.Clear(data, 0, data.Length);
			Array.Copy(source, data, source.Length);
			logicalLength = source.Length;
			overflow = null;
		}

		/// <summary>
		/// 存档直書き用の内部アクセス。data.Length は常に Length（logicalLength）と一致し、
		/// 不変条件（overflow は常に [0, logicalLength) の外のキーのみ）により
		/// ToArray() の返却配列と要素内容が一致する。読み取り専用で使うこと（変更禁止）。
		/// </summary>
		internal T[] RawData => data;

		public T[] ToArray()
		{
			return ToArray(logicalLength);
		}

		public T[] ToArray(int length)
		{
			T[] result = new T[length];
			int copy = Math.Min(length, logicalLength);
			if (copy > 0)
				Array.Copy(data, 0, result, 0, copy);
			if (overflow != null)
			{
				// 旧実装の this[i] と等価：length 内に収まる範囲外エントリも反映する
				foreach (KeyValuePair<long, T> pair in overflow)
				{
					if (pair.Key >= 0 && pair.Key < length)
						result[pair.Key] = pair.Value;
				}
			}
			return result;
		}

		public void CopyFrom(IList<T> source)
		{
			Clear();
			if (source == null)
				return;
			int count = Math.Min(source.Count, logicalLength);
			for (int i = 0; i < count; i++)
				data[i] = source[i];
		}

		public void CopyTo(T[] destination)
		{
			if (destination == null)
				return;
			int count = Math.Min(destination.Length, logicalLength);
			if (count > 0)
				Array.Copy(data, 0, destination, 0, count);
		}

		public void Shift(int offset, T fillValue, int start, int count)
		{
			if (offset == 0 || count <= 0)
				return;
			if (start < 0)
				start = 0;
			int end = Math.Min(start + count, logicalLength);
			if (end <= start)
				return;
			// 旧実装と同じく [start, end) 区間のみを移動し、区間外はそのまま。
			// 区間からはみ出した要素は破棄（既定値になる）、空いた区間は fillValue で埋める。
			if (offset > 0)
			{
				int shiftLen = Math.Min(offset, end - start);
				if (shiftLen >= end - start)
				{
					// 区間全体が右にはみ出す → 全区間 fillValue（既定値なら既定値）
					for (int i = start; i < end; i++)
						data[i] = fillValue;
				}
				else
				{
					for (int i = end - 1; i >= start + shiftLen; i--)
						data[i] = data[i - shiftLen];
					for (int i = start; i < start + shiftLen; i++)
						data[i] = fillValue;
				}
			}
			else
			{
				int shiftLen = Math.Min(-offset, end - start);
				if (shiftLen >= end - start)
				{
					for (int i = start; i < end; i++)
						data[i] = fillValue;
				}
				else
				{
					for (int i = start; i < end - shiftLen; i++)
						data[i] = data[i + shiftLen];
					for (int i = end - shiftLen; i < end; i++)
						data[i] = fillValue;
				}
			}
		}

		public void RemoveRange(int start, int count)
		{
			if (count <= 0)
				return;
			if (start < 0)
				start = 0;
			int end = Math.Min(start + count, logicalLength);
			// 旧実装：key >= end の要素を count だけ左へ詰め、[start, end) は削除。
			// start >= logicalLength でも、範囲外キー（overflow）は詰め替えが行われるため早期 return してはならない。
			// 密集配列の区間操作は start < logicalLength かつ end > start のときのみ意味を持つ。
			if (end > start && start < logicalLength)
			{
				Array.Copy(data, end, data, start, logicalLength - end);
				for (int i = logicalLength - (end - start); i < logicalLength; i++)
					data[i] = defaultValue;
			}
			if (overflow != null && overflow.Count > 0)
			{
				// 旧実装と同様に範囲外キーも count だけ左へ詰める
				Dictionary<long, T> compacted = new Dictionary<long, T>();
				foreach (KeyValuePair<long, T> pair in overflow)
				{
					long key = pair.Key;
					if (key < start)
						compacted[key] = pair.Value;
					else if (key >= end)
						compacted[key - count] = pair.Value;
				}
				overflow = compacted;
				// 詰め替えで範囲内へ入ったキーを data へマージ
				MergeInRangeOverflowIntoData();
			}
		}

		public void Sort(bool ascending, int start, int count)
		{
			if (start < 0)
				start = 0;
			int end = Math.Min(start + count, logicalLength);
			if (end <= start)
				return;
			T[] values = new T[end - start];
			Array.Copy(data, start, values, 0, end - start);
			Array.Sort(values);
			if (!ascending)
				Array.Reverse(values);
			Array.Copy(values, 0, data, start, end - start);
		}
	}
}
