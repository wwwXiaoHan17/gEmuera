using System;
using System.Collections.Generic;
using System.Data;
using System.Xml;

namespace MinorShift.Emuera.GameData.Function
{
	internal static class RuntimeDataStore
	{
		public static readonly Dictionary<string, DataTable> DataTables = new(StringComparer.OrdinalIgnoreCase);
		public static readonly Dictionary<string, Dictionary<string, string>> Maps = new(StringComparer.OrdinalIgnoreCase);
		public static readonly Dictionary<string, XmlDocument> XmlDocuments = new(StringComparer.OrdinalIgnoreCase);
		public static long NextDataTableRowId = 1;

		public static void Clear()
		{
			DataTables.Clear();
			Maps.Clear();
			XmlDocuments.Clear();
			NextDataTableRowId = 1;
		}
	}
}
