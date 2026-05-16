using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Data.Sqlite;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Function
{
	internal static class SnakeSqlManager
	{
		private sealed class ReaderContext
		{
			public SqliteCommand Command;
			public SqliteDataReader Reader;
		}

		private static readonly Dictionary<string, SqliteConnection> connections = new Dictionary<string, SqliteConnection>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<long, ReaderContext> readers = new Dictionary<long, ReaderContext>();
		private static long nextReaderId = 1;

		public static long ConnectionOpen(string name)
		{
			if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(".."))
				throw new CodeEE("SQL_CONNECTION_OPEN: invalid database name");

			string dir = Path.Combine(Config.SavDir, "sql");
			Directory.CreateDirectory(dir);
			string dbPath = Path.Combine(dir, name + ".db");
			return Connect(name, new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString, true);
		}

		public static long Connect(string dbName, string connectionString, bool replaceExisting)
		{
			if (string.IsNullOrWhiteSpace(dbName))
				throw new CodeEE("SQL_CONNECT: database name is empty");
			if (connections.ContainsKey(dbName))
			{
				if (!replaceExisting)
					return 1;
				Disconnect(dbName);
			}

			SqliteConnection conn = null;
			try
			{
				SqliteRuntime.EnsureInitialized();
				connectionString = SqliteRuntime.NormalizeConnectionString(connectionString, MinorShift.Emuera.Program.ExeDir);
				conn = new SqliteConnection(connectionString);
				conn.Open();
				using (var cmd = conn.CreateCommand())
				{
					cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
					cmd.ExecuteNonQuery();
				}
				connections[dbName] = conn;
				return 1;
			}
			catch (Exception ex)
			{
				conn?.Dispose();
				throw new CodeEE("SQL_CONNECT failed: " + SqliteRuntime.FormatException(ex));
			}
		}

		public static long Disconnect(string dbName)
		{
			if (string.IsNullOrWhiteSpace(dbName))
				return 1;

			CloseReadersFor(dbName);
			if (connections.TryGetValue(dbName, out var conn))
			{
				conn.Dispose();
				connections.Remove(dbName);
			}
			return 1;
		}

		public static long ExecuteNonQuery(string dbName, string sql, object[] parameters = null)
		{
			using var cmd = CreateCommand(dbName, sql, parameters);
			return cmd.ExecuteNonQuery();
		}

		public static long ExecuteReader(string dbName, string sql, object[] parameters = null)
		{
			var cmd = CreateCommand(dbName, sql, parameters);
			try
			{
				var reader = cmd.ExecuteReader();
				long id = nextReaderId++;
				readers[id] = new ReaderContext { Command = cmd, Reader = reader };
				return id;
			}
			catch
			{
				cmd.Dispose();
				throw;
			}
		}

		public static long ReaderRead(long readerId)
		{
			return readers.TryGetValue(readerId, out var ctx) && ctx.Reader.Read() ? 1 : 0;
		}

		public static long ReaderGetLong(long readerId, int columnIndex)
		{
			var reader = GetReader(readerId);
			return reader.IsDBNull(columnIndex) ? 0 : Convert.ToInt64(reader.GetValue(columnIndex));
		}

		public static long ReaderGetFloatAsLong(long readerId, int columnIndex)
		{
			var reader = GetReader(readerId);
			return reader.IsDBNull(columnIndex) ? 0 : Convert.ToInt64(Math.Round(Convert.ToDouble(reader.GetValue(columnIndex))));
		}

		public static string ReaderGetString(long readerId, int columnIndex)
		{
			var reader = GetReader(readerId);
			return reader.IsDBNull(columnIndex) ? "" : Convert.ToString(reader.GetValue(columnIndex));
		}

		public static long ReaderIsNull(long readerId, int columnIndex)
		{
			return GetReader(readerId).IsDBNull(columnIndex) ? 1 : 0;
		}

		public static long ReaderClose(long readerId)
		{
			if (readers.TryGetValue(readerId, out var ctx))
			{
				ctx.Reader.Dispose();
				ctx.Command.Dispose();
				readers.Remove(readerId);
			}
			return 1;
		}

		public static long ExecuteScalarLong(string dbName, string sql, object[] parameters = null)
		{
			object value = ExecuteScalar(dbName, sql, parameters);
			return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value);
		}

		public static long ExecuteScalarFloatAsLong(string dbName, string sql, object[] parameters = null)
		{
			object value = ExecuteScalar(dbName, sql, parameters);
			return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(Math.Round(Convert.ToDouble(value)));
		}

		public static string ExecuteScalarString(string dbName, string sql, object[] parameters = null)
		{
			object value = ExecuteScalar(dbName, sql, parameters);
			return value == null || value == DBNull.Value ? "" : Convert.ToString(value);
		}

		public static string Escape(string value)
		{
			return (value ?? "").Replace("'", "''");
		}

		public static long ImportMapXml(string dbName, string tableName, string filePath)
		{
			if (!connections.TryGetValue(dbName ?? "", out var conn))
				return 0;

			string resolvedPath = ResolveSqlXmlPath(filePath, "SQL_IMPORT_MAP_XML");
			string table = QuoteIdentifier(tableName, "SQL_IMPORT_MAP_XML table name");
			try
			{
				XmlDocument doc = LoadXmlDocument(resolvedPath);
				using var trans = conn.BeginTransaction();

				using (var cmd = conn.CreateCommand())
				{
					cmd.Transaction = trans;
					cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {table} (k TEXT PRIMARY KEY, v TEXT);";
					cmd.ExecuteNonQuery();
				}

				using (var cmd = conn.CreateCommand())
				{
					cmd.Transaction = trans;
					cmd.CommandText = $"INSERT OR REPLACE INTO {table} (k, v) VALUES (@k, @v);";
					var keyParam = cmd.Parameters.Add("@k", SqliteType.Text);
					var valueParam = cmd.Parameters.Add("@v", SqliteType.Text);

					XmlNodeList nodes = doc.SelectNodes("/map/p");
					if (nodes != null)
					{
						foreach (XmlNode node in nodes)
						{
							XmlNode keyNode = node.SelectSingleNode("./k");
							XmlNode valueNode = node.SelectSingleNode("./v");
							if (keyNode == null || valueNode == null)
								continue;

							keyParam.Value = keyNode.InnerText;
							valueParam.Value = valueNode.InnerXml;
							cmd.ExecuteNonQuery();
						}
					}
				}

				trans.Commit();
				return 1;
			}
			catch (CodeEE)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new CodeEE("SQL_IMPORT_MAP_XML failed: " + SqliteRuntime.FormatException(ex));
			}
		}

		public static long ImportDtXml(string dbName, string tableName, string schemaPath, string dataPath)
		{
			if (!connections.TryGetValue(dbName ?? "", out var conn))
				return 0;

			string resolvedSchema = ResolveSqlXmlPath(schemaPath, "SQL_IMPORT_DT_XML");
			string resolvedData = ResolveSqlXmlPath(dataPath, "SQL_IMPORT_DT_XML");
			string table = QuoteIdentifier(tableName, "SQL_IMPORT_DT_XML table name");
			try
			{
				DataTable schemaTable = new DataTable(tableName ?? "");
				using (var schemaStream = new MemoryStream(uEmuera.Utils.ReadAllBytes(resolvedSchema)))
					schemaTable.ReadXmlSchema(schemaStream);

				using var trans = conn.BeginTransaction();

				string columnDefs = string.Join(", ", schemaTable.Columns.Cast<DataColumn>().Select(column =>
				{
					string type = column.DataType == typeof(long) || column.DataType == typeof(int) ? "INTEGER" : "TEXT";
					string pk = string.Equals(column.ColumnName, "id", StringComparison.OrdinalIgnoreCase) ? " PRIMARY KEY" : "";
					return QuoteIdentifier(column.ColumnName, "SQL_IMPORT_DT_XML column name") + " " + type + pk;
				}));

				using (var cmd = conn.CreateCommand())
				{
					cmd.Transaction = trans;
					cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {table} ({columnDefs});";
					cmd.ExecuteNonQuery();
				}

				string columnNames = string.Join(", ", schemaTable.Columns.Cast<DataColumn>().Select(c => QuoteIdentifier(c.ColumnName, "SQL_IMPORT_DT_XML column name")));
				string parameterNames = string.Join(", ", schemaTable.Columns.Cast<DataColumn>().Select(c => "@" + c.ColumnName));

				using (var cmd = conn.CreateCommand())
				{
					cmd.Transaction = trans;
					cmd.CommandText = $"INSERT OR REPLACE INTO {table} ({columnNames}) VALUES ({parameterNames});";
					foreach (DataColumn column in schemaTable.Columns)
					{
						SqliteType type = column.DataType == typeof(long) || column.DataType == typeof(int) ? SqliteType.Integer : SqliteType.Text;
						cmd.Parameters.Add("@" + column.ColumnName, type);
					}

					using var dataStream = new MemoryStream(uEmuera.Utils.ReadAllBytes(resolvedData));
					using XmlReader reader = XmlReader.Create(dataStream);
					while (reader.Read())
					{
						if (reader.NodeType != XmlNodeType.Element || reader.Name != tableName)
							continue;

						using XmlReader rowReader = reader.ReadSubtree();
						foreach (SqliteParameter parameter in cmd.Parameters)
							parameter.Value = DBNull.Value;

						while (rowReader.Read())
						{
							if (rowReader.NodeType != XmlNodeType.Element || rowReader.Name == tableName)
								continue;
							string paramName = "@" + rowReader.Name;
							if (cmd.Parameters.Contains(paramName))
								cmd.Parameters[paramName].Value = rowReader.ReadElementContentAsString();
						}
						cmd.ExecuteNonQuery();
					}
				}

				trans.Commit();
				return 1;
			}
			catch (CodeEE)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new CodeEE("SQL_IMPORT_DT_XML failed: " + SqliteRuntime.FormatException(ex));
			}
		}

		public static long ExportMapXml(string dbName, string tableName, string filePath)
		{
			if (!connections.TryGetValue(dbName ?? "", out var conn))
				return 0;

			string outputPath = ResolveSqlOutputPath(filePath);
			string table = QuoteIdentifier(tableName, "SQL_EXPORT_MAP_XML table name");
			try
			{
				EnsureOutputDirectory(outputPath);
				using XmlWriter writer = XmlWriter.Create(outputPath, new XmlWriterSettings { Indent = true });
				writer.WriteStartElement("map");
				using (var cmd = conn.CreateCommand())
				{
					cmd.CommandText = $"SELECT k, v FROM {table};";
					using var reader = cmd.ExecuteReader();
					while (reader.Read())
					{
						writer.WriteStartElement("p");
						writer.WriteElementString("k", reader.IsDBNull(0) ? "" : reader.GetString(0));
						writer.WriteStartElement("v");
						writer.WriteRaw(reader.IsDBNull(1) ? "" : reader.GetString(1));
						writer.WriteEndElement();
						writer.WriteEndElement();
					}
				}
				writer.WriteEndElement();
				return 1;
			}
			catch (Exception ex)
			{
				throw new CodeEE("SQL_EXPORT_MAP_XML failed: " + SqliteRuntime.FormatException(ex));
			}
		}

		public static long ExportDtXml(string dbName, string tableName, string schemaPath, string dataPath)
		{
			if (!connections.TryGetValue(dbName ?? "", out var conn))
				return 0;

			string schemaOutput = ResolveSqlOutputPath(schemaPath);
			string dataOutput = ResolveSqlOutputPath(dataPath);
			string table = QuoteIdentifier(tableName, "SQL_EXPORT_DT_XML table name");
			try
			{
				EnsureOutputDirectory(schemaOutput);
				EnsureOutputDirectory(dataOutput);
				DataTable dataTable = new DataTable(tableName ?? "");
				using (var cmd = conn.CreateCommand())
				{
					cmd.CommandText = $"SELECT * FROM {table} LIMIT 0;";
					using var reader = cmd.ExecuteReader();
					dataTable.Load(reader);
				}
				dataTable.WriteXmlSchema(schemaOutput);

				using XmlWriter writer = XmlWriter.Create(dataOutput, new XmlWriterSettings { Indent = true });
				writer.WriteStartElement("DocumentElement");
				using (var cmd = conn.CreateCommand())
				{
					cmd.CommandText = $"SELECT * FROM {table};";
					using var reader = cmd.ExecuteReader();
					while (reader.Read())
					{
						writer.WriteStartElement(tableName);
						for (int i = 0; i < reader.FieldCount; i++)
						{
							if (!reader.IsDBNull(i))
								writer.WriteElementString(reader.GetName(i), Convert.ToString(reader.GetValue(i)));
						}
						writer.WriteEndElement();
					}
				}
				writer.WriteEndElement();
				return 1;
			}
			catch (Exception ex)
			{
				throw new CodeEE("SQL_EXPORT_DT_XML failed: " + SqliteRuntime.FormatException(ex));
			}
		}

		public static long ImportXmlCustom(string dbName, string tableName, string filePath, string rowXPath, string columnMappings)
		{
			if (!connections.TryGetValue(dbName ?? "", out var conn))
				return 0;

			string resolvedPath = ResolveSqlXmlPath(filePath, "SQL_IMPORT_XML_CUSTOM");
			string table = QuoteIdentifier(tableName, "SQL_IMPORT_XML_CUSTOM table name");
			try
			{
				Dictionary<string, string> mappings = ParseColumnMappings(columnMappings);
				using var trans = conn.BeginTransaction();

				string columnDefs = string.Join(", ", mappings.Keys.Select(k => QuoteIdentifier(k, "SQL_IMPORT_XML_CUSTOM column name") + " TEXT"));
				using (var cmd = conn.CreateCommand())
				{
					cmd.Transaction = trans;
					cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {table} ({columnDefs});";
					cmd.ExecuteNonQuery();
				}

				string columnNames = string.Join(", ", mappings.Keys.Select(k => QuoteIdentifier(k, "SQL_IMPORT_XML_CUSTOM column name")));
				string parameterNames = string.Join(", ", mappings.Keys.Select(k => "@" + k));
				using (var cmd = conn.CreateCommand())
				{
					cmd.Transaction = trans;
					cmd.CommandText = $"INSERT INTO {table} ({columnNames}) VALUES ({parameterNames});";
					foreach (string column in mappings.Keys)
						cmd.Parameters.Add("@" + column, SqliteType.Text);

					string targetNodeName = (rowXPath ?? "").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
					if (string.IsNullOrEmpty(targetNodeName))
						throw new CodeEE("SQL_IMPORT_XML_CUSTOM: row XPath is empty");

					using var stream = new MemoryStream(uEmuera.Utils.ReadAllBytes(resolvedPath));
					using XmlReader reader = XmlReader.Create(stream);
					while (reader.Read())
					{
						if (reader.NodeType != XmlNodeType.Element || reader.Name != targetNodeName)
							continue;

						using XmlReader subReader = reader.ReadSubtree();
						XmlDocument rowDoc = new XmlDocument();
						rowDoc.Load(subReader);
						XmlNode row = rowDoc.DocumentElement;
						foreach (var mapping in mappings)
						{
							string expr = mapping.Value;
							string value = null;
							if (expr.StartsWith("@", StringComparison.Ordinal))
								value = row?.Attributes?[expr.Substring(1)]?.Value;
							else if (expr.EndsWith("(xml)", StringComparison.OrdinalIgnoreCase))
								value = row?.SelectSingleNode(expr.Substring(0, expr.Length - 5))?.InnerXml;
							else
								value = row?.SelectSingleNode(expr)?.InnerText;
							cmd.Parameters["@" + mapping.Key].Value = (object)value ?? DBNull.Value;
						}
						cmd.ExecuteNonQuery();
					}
				}

				trans.Commit();
				return 1;
			}
			catch (CodeEE)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new CodeEE("SQL_IMPORT_XML_CUSTOM failed: " + SqliteRuntime.FormatException(ex));
			}
		}

		public static void CloseAll()
		{
			foreach (var ctx in readers.Values)
			{
				ctx.Reader.Dispose();
				ctx.Command.Dispose();
			}
			readers.Clear();

			foreach (var conn in connections.Values)
				conn.Dispose();
			connections.Clear();
		}

		private static object ExecuteScalar(string dbName, string sql, object[] parameters)
		{
			using var cmd = CreateCommand(dbName, sql, parameters);
			return cmd.ExecuteScalar();
		}

		private static SqliteCommand CreateCommand(string dbName, string sql, object[] parameters)
		{
			if (!connections.TryGetValue(dbName ?? "", out var conn))
				throw new CodeEE("SQL database is not connected: " + (dbName ?? ""));

			var cmd = conn.CreateCommand();
			cmd.CommandText = sql ?? "";
			if (parameters != null)
			{
				for (int i = 0; i < parameters.Length; i++)
				{
					var param = cmd.CreateParameter();
					param.ParameterName = "@" + i.ToString();
					param.Value = parameters[i] ?? DBNull.Value;
					cmd.Parameters.Add(param);
				}
			}
			return cmd;
		}

		private static SqliteDataReader GetReader(long readerId)
		{
			if (!readers.TryGetValue(readerId, out var ctx))
				throw new CodeEE("Invalid SQL reader id: " + readerId.ToString());
			return ctx.Reader;
		}

		private static void CloseReadersFor(string dbName)
		{
			var closeIds = new List<long>();
			foreach (var pair in readers)
			{
				if (pair.Value.Command.Connection == connections.GetValueOrDefault(dbName))
					closeIds.Add(pair.Key);
			}
			foreach (long id in closeIds)
				ReaderClose(id);
		}

		private static XmlDocument LoadXmlDocument(string path)
		{
			var doc = new XmlDocument();
			using var stream = new MemoryStream(uEmuera.Utils.ReadAllBytes(path));
			doc.Load(stream);
			return doc;
		}

		private static string ResolveSqlXmlPath(string path, string commandName)
		{
			foreach (string candidate in BuildSqlPathCandidates(path))
			{
				string resolved = uEmuera.Utils.ResolveExistingFilePath(candidate);
				if (!string.IsNullOrEmpty(resolved) && uEmuera.Utils.FileExists(resolved))
					return resolved;
			}
			throw new CodeEE(commandName + ": file not found: " + (path ?? ""));
		}

		private static IEnumerable<string> BuildSqlPathCandidates(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				yield break;

			if (!Path.IsPathRooted(path))
			{
				if (!string.IsNullOrEmpty(MinorShift.Emuera.Program.WorkingDir))
					yield return Path.Combine(MinorShift.Emuera.Program.WorkingDir, path);
				if (!string.IsNullOrEmpty(MinorShift.Emuera.Program.ExeDir))
					yield return Path.Combine(MinorShift.Emuera.Program.ExeDir, path);
				if (!string.IsNullOrEmpty(MinorShift.Emuera.Program.ContentDir))
					yield return Path.Combine(MinorShift.Emuera.Program.ContentDir, path);

				if (Godot.OS.GetName() == "Android")
					yield break;
			}
			yield return path;
		}

		private static string ResolveSqlOutputPath(string path)
		{
			if (Path.IsPathRooted(path ?? ""))
				return uEmuera.Utils.NormalizePath(path);
			string baseDir = !string.IsNullOrEmpty(MinorShift.Emuera.Program.ExeDir)
				? MinorShift.Emuera.Program.ExeDir
				: Directory.GetCurrentDirectory();
			return uEmuera.Utils.NormalizePath(Path.Combine(baseDir, path ?? ""));
		}

		private static void EnsureOutputDirectory(string path)
		{
			string dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir))
				uEmuera.Utils.CreateDirectory(dir);
		}

		private static Dictionary<string, string> ParseColumnMappings(string columnMappings)
		{
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string item in (columnMappings ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string[] pair = item.Split(new[] { '=' }, 2);
				if (pair.Length != 2)
					continue;
				string column = pair[0].Trim();
				string expression = pair[1].Trim();
				if (!string.IsNullOrEmpty(column) && !string.IsNullOrEmpty(expression))
					result[column] = expression;
			}
			if (result.Count == 0)
				throw new CodeEE("SQL_IMPORT_XML_CUSTOM: no column mappings");
			return result;
		}

		private static string QuoteIdentifier(string identifier, string description)
		{
			if (string.IsNullOrWhiteSpace(identifier) || identifier.IndexOf('\0') >= 0)
				throw new CodeEE(description + " is empty or invalid");
			return "\"" + identifier.Replace("\"", "\"\"") + "\"";
		}
	}
}
