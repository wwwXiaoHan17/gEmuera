using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Xml;
using MinorShift.Emuera.GameData.Variable;

namespace MinorShift.Emuera.Sub
{
	//reader/writer共通のデータはreaderの方に


	/// <summary>
	/// 1808追加 新しいデータ保存形式
	/// Reader と違ってWriterは最新の書き込み方式だけ知っていればよい
	/// WriteHeader -> WriteFileType -> ... -> WriteEFO
	/// </summary>
	internal sealed class EraBinaryDataWriter : IDisposable
	{
		public EraBinaryDataWriter(Stream fs)
		{
			if (Config.SystemSaveInBinary && Config.ZipSaveData)
			{
				memoryStream = new MemoryStream();
				writer = new BinaryWriter(memoryStream, Encoding.Unicode, true);
				fileWriter = new BinaryWriter(fs, Encoding.Unicode, true);
			}
			else
			{
				writer = new BinaryWriter(fs, Encoding.Unicode);
				fileWriter = writer;
			}
		}
		BinaryWriter writer = null;
		BinaryWriter fileWriter = null;
		MemoryStream memoryStream = null;
		const byte EmMapDataType = 0x20;
		const byte EmXmlDataType = 0x21;
		const byte EmDataTableDataType = 0x22;
		
		public void WriteHeader()
		{
			fileWriter.Write((Config.SystemSaveInBinary && Config.ZipSaveData) ? EraBDConst.ZipHeader : EraBDConst.Header);
			fileWriter.Write(EraBDConst.Version1808);
			fileWriter.Write(EraBDConst.DataCount);
			for (int i = 0; i < EraBDConst.DataCount; i++)
			{
				fileWriter.Write((UInt32)0);
			}
		}

		public void WriteFileType(EraSaveFileType type)
		{
			writer.Write((byte)type);
		}


		/// <summary>
		/// システム用。keyなしでInt64を保存
		/// </summary>
		/// <param name="v"></param>
		public void WriteInt64(Int64 v)
		{
			//圧縮しない
			writer.Write(v);
		}
		/// <summary>
		/// システム用。keyなしでstringを保存
		/// </summary>
		/// <param name="s"></param>
		public void WriteString(string s)
		{
			writer.Write(s);
		}


		public void WriteSeparator()
		{
			writer.Write((byte)EraSaveDataType.Separator);
		}
		public void WriteEOC()
		{
			writer.Write((byte)EraSaveDataType.EOC);
		}
		public void WriteEOF()
		{
			writer.Write((byte)EraSaveDataType.EOF);
		}

		public void WriteWithKey(string key, object v)
		{
			if (v is Int64)
			{
				writer.Write((byte)EraSaveDataType.Int);
				writer.Write(key);
				writeData((Int64)v);
			}
			else if (v is Int64[])
			{
				writer.Write((byte)EraSaveDataType.IntArray);
				writer.Write(key);
				writeData((Int64[])v);
			}
			else if (v is SparseArray<Int64>)
			{
				writer.Write((byte)EraSaveDataType.IntArray);
				writer.Write(key);
				writeData((SparseArray<Int64>)v);
			}
			else if (v is Int64[,])
			{
				writer.Write((byte)EraSaveDataType.IntArray2D);
				writer.Write(key);
				writeData((Int64[,])v);
			}
			else if (v is Int64[, ,])
			{
				writer.Write((byte)EraSaveDataType.IntArray3D);
				writer.Write(key);
				writeData((Int64[, ,])v);
			}
			else if (v is string)
			{
				writer.Write((byte)EraSaveDataType.Str);
				writer.Write(key);
				writeData((string)v);
			}
			else if (v is string[])
			{
				writer.Write((byte)EraSaveDataType.StrArray);
				writer.Write(key);
				writeData((string[])v);
			}
			else if (v is SparseArray<string>)
			{
				writer.Write((byte)EraSaveDataType.StrArray);
				writer.Write(key);
				writeData((SparseArray<string>)v);
			}
			else if (v is string[,])
			{
				writer.Write((byte)EraSaveDataType.StrArray2D);
				writer.Write(key);
				writeData((string[,])v);
			}
			else if (v is string[, ,])
			{
				writer.Write((byte)EraSaveDataType.StrArray3D);
				writer.Write(key);
				writeData((string[, ,])v);
			}
			else if (v is double)
			{
				writer.Write((byte)EraSaveDataType.PcFloat);
				writer.Write(key);
				writeData((double)v);
			}
			else if (v is double[])
			{
				writer.Write((byte)EraSaveDataType.PcFloatArray);
				writer.Write(key);
				writeData((double[])v);
			}
			else if (v is SparseArray<double>)
			{
				writer.Write((byte)EraSaveDataType.PcFloatArray);
				writer.Write(key);
				writeData((SparseArray<double>)v);
			}
			else if (v is double[,])
			{
				writer.Write((byte)EraSaveDataType.PcFloatArray2D);
				writer.Write(key);
				writeData((double[,])v);
			}
			else if (v is double[, ,])
			{
				writer.Write((byte)EraSaveDataType.PcFloatArray3D);
				writer.Write(key);
				writeData((double[, ,])v);
			}
			else if (v is Dictionary<string, string>)
			{
				var map = (Dictionary<string, string>)v;
				writer.Write(EmMapDataType);
				writer.Write(key);
				writer.Write(map.Count);
				foreach (var pair in map)
				{
					writer.Write(pair.Key ?? "");
					writer.Write(pair.Value ?? "");
				}
			}
			else if (v is XmlDocument)
			{
				var doc = (XmlDocument)v;
				writer.Write(EmXmlDataType);
				writer.Write(key);
				writer.Write(doc.OuterXml ?? "");
			}
			else if (v is DataTable)
			{
				var table = (DataTable)v;
				writer.Write(EmDataTableDataType);
				writer.Write(key);
				var builder = new StringBuilder();
				using (var stringWriter = new StringWriter(builder))
				{
					table.WriteXmlSchema(stringWriter);
					writer.Write(builder.ToString());
					builder.Clear();
					table.WriteXml(stringWriter);
					writer.Write(builder.ToString());
				}
			}
		}

		#region private

		private void m_WriteInt(Int64 v)
		{
			//セーブデータ容量の爆発を避けるためにできるだけWrite(Int64)はしない
			if (v >= 0 && v <= Ebdb.Byte)//0～207まではそのままbyteに詰め込む
				writer.Write((byte)v);
			else if (v >= Int16.MinValue && v <= Int16.MaxValue)//整数の範囲に応じて適当に
			{
				writer.Write(Ebdb.Int16);
				writer.Write((Int16)v);
			}
			else if (v >= Int32.MinValue && v <= Int32.MaxValue)
			{
				writer.Write(Ebdb.Int32);
				writer.Write((Int32)v);
			}
			else
			{
				writer.Write(Ebdb.Int64);
				writer.Write(v);
			}
		}

		private void writeData(Int64 v)
		{
			m_WriteInt(v);
		}

		private void writeData(Int64[] array)
		{
			//配列の記憶。0が連続する場合には圧縮を試みる。
			writer.Write((Int32)array.Length);
			int countZero = 0;//0については0が連続する数を記憶する。その他の数はそのまま記憶する。
			for(int x = 0; x < array.Length; x++)
			{
				if (array[x] == 0)
					countZero++;
				else
				{
					if (countZero > 0)
					{
						writer.Write(Ebdb.Zero);
						this.m_WriteInt(countZero);
						countZero = 0;
					}
					this.m_WriteInt(array[x]);
				}
			}
			//記憶途中で配列の残りが全部0であるなら0の数も記憶せず配列の終わりを記憶
			writer.Write(Ebdb.EoD);
		}

		private void writeData(Int64[,] array)
		{
			int countZero = 0;//0については0が連続する数を記憶する。その他はそのまま記憶する。
			int countAllZero = 0;//列の要素が全て0である列の連続する数を記憶する。列の要素に一つでも非0があるなら通常の記憶方式。
			int length0 = array.GetLength(0);
			int length1 = array.GetLength(1);
			writer.Write(length0);
			writer.Write(length1);
			
			for(int x = 0; x < length0; x++)
			{
				for(int y = 0; y < length1; y++)
				{
					if (array[x,y] == 0)
						countZero++;
					else
					{
						if (countAllZero > 0)
						{
							writer.Write(Ebdb.ZeroA1);
							this.m_WriteInt(countAllZero);
							countAllZero = 0;
						}
						if (countZero > 0)
						{
							writer.Write(Ebdb.Zero);
							this.m_WriteInt(countZero);
							countZero = 0;
						}
						this.m_WriteInt(array[x,y]);
					}
				}
				if (countZero == length1)//列の要素が全部0
					countAllZero++;
				else
					writer.Write(Ebdb.EoA1);//非0があるなら列終端記号を記憶
				countZero = 0;
			}
			writer.Write(Ebdb.EoD);
		}

		private void writeData(Int64[, ,] array)
		{
			int countZero = 0;//0については0が連続する数を記憶する。その他はそのまま記憶する。
			int countAllZero = 0;//列の要素が全て0である列の連続する数を記憶する。列の要素に一つでも非0があるなら通常の記憶方式。
			int countAllZero2D = 0;//行列の要素が全て0である行列の･･･
			int length0 = array.GetLength(0);
			int length1 = array.GetLength(1);
			int length2 = array.GetLength(2);
			writer.Write(length0);
			writer.Write(length1);
			writer.Write(length2);
			for(int x = 0; x < length0; x++)
			{
				for(int y = 0; y < length1; y++)
				{
					for(int z = 0; z < length2; z++)
					{
						if (array[x,y,z] == 0)
							countZero++;
						else
						{
							if (countAllZero2D > 0)
							{
								writer.Write(Ebdb.ZeroA2);
								this.m_WriteInt(countAllZero2D);
								countAllZero2D = 0;
							}
							if (countAllZero > 0)
							{
								writer.Write(Ebdb.ZeroA1);
								this.m_WriteInt(countAllZero);
								countAllZero = 0;
							}
							if (countZero > 0)
							{
								writer.Write(Ebdb.Zero);
								this.m_WriteInt(countZero);
								countZero = 0;
							}
							this.m_WriteInt(array[x,y,z]);
						}
					}
					if (countZero == length2)
						countAllZero++;
					else
						writer.Write(Ebdb.EoA1);
					countZero = 0;
				}
				if (countAllZero == length1)
					countAllZero2D++;
				else
					writer.Write(Ebdb.EoA2);
				countAllZero = 0;
			}
			writer.Write(Ebdb.EoD);
		}

		private void writeData(string v)
		{
			if (v != null)
				writer.Write(v);
			else
				writer.Write("");
		}

		private void writeData(string[] array)
		{
			int countZero = 0;
			writer.Write((int)array.Length);
			for(int x = 0; x < array.Length; x++)
			{
				if (array[x] == null || array[x].Length == 0)
					countZero++;
				else
				{
					if (countZero > 0)
					{
						writer.Write(Ebdb.Zero);
						this.m_WriteInt(countZero);
						countZero = 0;
					}
					writer.Write(Ebdb.String);
					writer.Write(array[x]);
				}
			}
			writer.Write(Ebdb.EoD);
		}

		private void writeData(string[,] array)
		{
			int countZero = 0;
			int countAllZero = 0;
			int length0 = array.GetLength(0);
			int length1 = array.GetLength(1);
			writer.Write(length0);
			writer.Write(length1);
			for(int x = 0; x < length0; x++)
			{
				for(int y = 0; y < length1; y++)
				{
					if (array[x,y] == null || array[x,y].Length == 0)
						countZero++;
					else
					{
						if (countAllZero > 0)
						{
							writer.Write(Ebdb.ZeroA1);
							this.m_WriteInt(countAllZero);
							countAllZero = 0;
						}
						if (countZero > 0)
						{
							writer.Write(Ebdb.Zero);
							this.m_WriteInt(countZero);
							countZero = 0;
						}
						writer.Write(Ebdb.String);
						writer.Write(array[x,y]);
					}
				}
				if (countZero == length1)
					countAllZero++;
				else
					writer.Write(Ebdb.EoA1);
				countZero = 0;
			}
			writer.Write(Ebdb.EoD);
		}

		private void writeData(string[, ,] array)
		{
			int countZero = 0;
			int countAllZero = 0;
			int countAllZero2D = 0;
			int length0 = array.GetLength(0);
			int length1 = array.GetLength(1);
			int length2 = array.GetLength(2);
			writer.Write(length0);
			writer.Write(length1);
			writer.Write(length2);
			for(int x = 0; x < length0; x++)
			{
				for(int y = 0; y < length1; y++)
				{
					for(int z = 0; z < length2; z++)
					{
						if (array[x,y,z] == null || array[x,y,z].Length == 0)
							countZero++;
						else
						{
							if (countAllZero2D > 0)
							{
								writer.Write(Ebdb.ZeroA2);
								this.m_WriteInt(countAllZero2D);
								countAllZero2D = 0;
							}
							if (countAllZero > 0)
							{
								writer.Write(Ebdb.ZeroA1);
								this.m_WriteInt(countAllZero);
								countAllZero = 0;
							}
							if (countZero > 0)
							{
								writer.Write(Ebdb.Zero);
								this.m_WriteInt(countZero);
								countZero = 0;
							}
							writer.Write(Ebdb.String);
							writer.Write(array[x,y,z]);
						}
					}
					if (countZero == length2)
						countAllZero++;
					else
						writer.Write(Ebdb.EoA1);
					countZero = 0;
				}
				if (countAllZero == length1)
					countAllZero2D++;
				else
					writer.Write(Ebdb.EoA2);
				countAllZero = 0;
			}
			writer.Write(Ebdb.EoD);
		}

		private void writeData(double v)
		{
			writer.Write(v);
		}

		private void writeData(double[] array)
		{
			writer.Write((Int32)array.Length);
			for(int x = 0; x < array.Length; x++)
				writer.Write(array[x]);
		}

		private void writeData(double[,] array)
		{
			int length0 = array.GetLength(0);
			int length1 = array.GetLength(1);
			writer.Write(length0);
			writer.Write(length1);
			for(int x = 0; x < length0; x++)
				for(int y = 0; y < length1; y++)
					writer.Write(array[x,y]);
		}

		private void writeData(double[, ,] array)
		{
			int length0 = array.GetLength(0);
			int length1 = array.GetLength(1);
			int length2 = array.GetLength(2);
			writer.Write(length0);
			writer.Write(length1);
			writer.Write(length2);
			for(int x = 0; x < length0; x++)
				for(int y = 0; y < length1; y++)
					for(int z = 0; z < length2; z++)
						writer.Write(array[x,y,z]);
		}

		// 1D 変数は SparseArray が密集化されているため、ToArray() の全量コピーを挟まず
		// 内部の密集バッファ（RawData）を直接走査する（生成バイト列は ToArray() 経由と同一）。
		// 不変条件により RawData.Length == Length かつ overflow は範囲外キーのみなので、
		// ToArray() の結果と要素内容が一致する。
		private void writeData(SparseArray<Int64> array)
		{
			Int64[] data = array.RawData;
			//配列の記憶。0が連続する場合には圧縮を試みる。
			writer.Write((Int32)data.Length);
			int countZero = 0;//0については0が連続する数を記憶する。その他の数はそのまま記憶する。
			for(int x = 0; x < data.Length; x++)
			{
				if (data[x] == 0)
					countZero++;
				else
				{
					if (countZero > 0)
					{
						writer.Write(Ebdb.Zero);
						this.m_WriteInt(countZero);
						countZero = 0;
					}
					this.m_WriteInt(data[x]);
				}
			}
			//記憶途中で配列の残りが全部0であるなら0の数も記憶せず配列の終わりを記憶
			writer.Write(Ebdb.EoD);
		}

		private void writeData(SparseArray<string> array)
		{
			string[] data = array.RawData;
			int countZero = 0;
			writer.Write((int)data.Length);
			for(int x = 0; x < data.Length; x++)
			{
				if (data[x] == null || data[x].Length == 0)
					countZero++;
				else
				{
					if (countZero > 0)
					{
						writer.Write(Ebdb.Zero);
						this.m_WriteInt(countZero);
						countZero = 0;
					}
					writer.Write(Ebdb.String);
					writer.Write(data[x]);
				}
			}
			writer.Write(Ebdb.EoD);
		}

		private void writeData(SparseArray<double> array)
		{
			double[] data = array.RawData;
			writer.Write((Int32)data.Length);
			for(int x = 0; x < data.Length; x++)
				writer.Write(data[x]);
		}
		#endregion
		#region IDisposable メンバ

		public void Dispose()
		{
			if (Config.SystemSaveInBinary && Config.ZipSaveData && writer != null)
			{
				writer.Flush();
				memoryStream.Position = 0;
				using (GZipStream gzip = new GZipStream(fileWriter.BaseStream, CompressionMode.Compress, true))
					memoryStream.CopyTo(gzip);
				writer.Close();
				memoryStream = null;
				fileWriter.Close();
			}
			else if (writer != null)
			{
				writer.Close();
			}
			writer = null;
			fileWriter = null;
		}

		#endregion
		public void Close()
		{
			Dispose();
		}

	}
}
