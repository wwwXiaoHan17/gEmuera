using System;
using System.Collections.Generic;
using System.Data;
using System.Xml;
using MinorShift.Emuera.GameData;

namespace MinorShift.Emuera.GameData.Function
{
	internal static class RuntimeDataStore
	{
		public static readonly Dictionary<string, DataTable> DataTables = new();
		public static readonly Dictionary<string, Dictionary<string, string>> Maps = new();
		public static readonly Dictionary<string, XmlDocument> XmlDocuments = new();
		public static long NextDataTableRowId = 1;

		public static void Clear()
		{
			DataTables.Clear();
			Maps.Clear();
			XmlDocuments.Clear();
			NextDataTableRowId = 1;
		}

		public static void ClearSaveData(ConstantData constant)
		{
			if (constant == null)
				return;
			clearMaps(constant.SaveMaps);
			clearXmls(constant.SaveXmls);
			clearDataTables(constant.SaveDTs);
			RefreshNextDataTableRowId();
		}

		public static void ClearGlobalData(ConstantData constant)
		{
			if (constant == null)
				return;
			clearMaps(constant.GlobalSaveMaps);
			clearXmls(constant.GlobalSaveXmls);
			clearDataTables(constant.GlobalSaveDTs);
			RefreshNextDataTableRowId();
		}

		public static void ClearStaticData(ConstantData constant)
		{
			if (constant == null)
				return;
			clearMaps(constant.StaticMaps);
			clearXmls(constant.StaticXmls);
			clearDataTables(constant.StaticDTs);
			RefreshNextDataTableRowId();
		}

		public static bool IsSaveMap(ConstantData constant, string key)
		{
			return constant != null && constant.SaveMaps.Contains(key);
		}

		public static bool IsGlobalMap(ConstantData constant, string key)
		{
			return constant != null && constant.GlobalSaveMaps.Contains(key);
		}

		public static bool IsKnownPersistentMap(ConstantData constant, string key)
		{
			return IsSaveMap(constant, key) || IsGlobalMap(constant, key);
		}

		public static bool IsSaveXml(ConstantData constant, string key)
		{
			return constant != null && constant.SaveXmls.Contains(key);
		}

		public static bool IsGlobalXml(ConstantData constant, string key)
		{
			return constant != null && constant.GlobalSaveXmls.Contains(key);
		}

		public static bool IsKnownPersistentXml(ConstantData constant, string key)
		{
			return IsSaveXml(constant, key) || IsGlobalXml(constant, key);
		}

		public static bool IsSaveDataTable(ConstantData constant, string key)
		{
			return constant != null && constant.SaveDTs.Contains(key);
		}

		public static bool IsGlobalDataTable(ConstantData constant, string key)
		{
			return constant != null && constant.GlobalSaveDTs.Contains(key);
		}

		public static bool IsKnownPersistentDataTable(ConstantData constant, string key)
		{
			return IsSaveDataTable(constant, key) || IsGlobalDataTable(constant, key);
		}

		public static void RefreshNextDataTableRowId()
		{
			long nextId = Math.Max(1, NextDataTableRowId);
			foreach (DataTable table in DataTables.Values)
			{
				NormalizeDataTable(table);
				if (table == null || !table.Columns.Contains("id"))
					continue;
				foreach (DataRow row in table.Rows)
				{
					if (row == null || row.IsNull("id"))
						continue;
					try
					{
						long id = Convert.ToInt64(row["id"]);
						if (id >= nextId)
							nextId = id + 1;
					}
					catch (InvalidCastException) { }
					catch (FormatException) { }
					catch (OverflowException) { }
				}
			}
			NextDataTableRowId = nextId;
		}

		public static void NormalizeDataTable(DataTable table)
		{
			if (table == null)
				return;
			if ((table.PrimaryKey == null || table.PrimaryKey.Length == 0) && table.Columns.Contains("id"))
				table.PrimaryKey = new[] { table.Columns["id"] };
		}

		static void clearMaps(IEnumerable<string> keys)
		{
			foreach (string key in keys)
				if (Maps.TryGetValue(key, out var map))
					map.Clear();
		}

		static void clearXmls(IEnumerable<string> keys)
		{
			foreach (string key in keys)
				XmlDocuments.Remove(key);
		}

		static void clearDataTables(IEnumerable<string> keys)
		{
			foreach (string key in keys)
				if (DataTables.TryGetValue(key, out DataTable table))
					table.Clear();
		}
	}
}
