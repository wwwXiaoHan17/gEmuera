using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Data.Sqlite;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.Modern.Script.Functions;

internal static class ModernSqlManager
{
	sealed class ReaderContext
	{
		public string DatabaseName;
		public SqliteCommand Command;
		public SqliteDataReader Reader;
	}

	static readonly Dictionary<string, SqliteConnection> connections = new(StringComparer.OrdinalIgnoreCase);
	static readonly Dictionary<long, ReaderContext> readers = new();
	static long nextReaderId = 1;
	static string storageDirectory = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"gemuera-modern",
		"sql");

	public static string StorageDirectory
	{
		get { return storageDirectory; }
		set { storageDirectory = string.IsNullOrWhiteSpace(value) ? storageDirectory : value; }
	}

	public static long ConnectionOpen(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(".."))
			throw new InvalidOperationException($"SQL_CONNECTION_OPEN: invalid database name: {name}");
		Directory.CreateDirectory(StorageDirectory);
		string path = Path.Combine(StorageDirectory, name + ".db");
		var builder = new SqliteConnectionStringBuilder { DataSource = path };
		return Connect(name, builder.ConnectionString, true);
	}

	public static long Connect(string name, string connectionString, bool replaceExisting)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new InvalidOperationException("SQL_CONNECT: database name is empty.");
		if (connections.ContainsKey(name))
		{
			if (!replaceExisting)
				return 1;
			Disconnect(name);
		}

		SqliteConnection connection = null;
		try
		{
			SqliteRuntime.EnsureInitialized();
			connectionString = SqliteRuntime.NormalizeConnectionString(connectionString, MinorShift.Emuera.Program.ExeDir);
			connection = new SqliteConnection(connectionString);
			connection.Open();
			using (var command = connection.CreateCommand())
			{
				command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
				command.ExecuteNonQuery();
			}
			connections[name] = connection;
			return 1;
		}
		catch (Exception ex)
		{
			connection?.Dispose();
			throw new InvalidOperationException("SQL_CONNECT failed: " + SqliteRuntime.FormatException(ex), ex);
		}
	}

	public static long Disconnect(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return 1;
		CloseReadersFor(name);
		if (connections.TryGetValue(name, out var connection))
		{
			connection.Dispose();
			connections.Remove(name);
		}
		return 1;
	}

	public static long ExecuteNonQuery(string dbName, string sql, object[] parameters = null)
	{
		using var command = CreateCommand(dbName, sql, parameters);
		return command.ExecuteNonQuery();
	}

	public static long ExecuteReader(string dbName, string sql, object[] parameters = null)
	{
		var command = CreateCommand(dbName, sql, parameters);
		try
		{
			var reader = command.ExecuteReader();
			long id = nextReaderId++;
			readers[id] = new ReaderContext { DatabaseName = dbName ?? "", Command = command, Reader = reader };
			return id;
		}
		catch
		{
			command.Dispose();
			throw;
		}
	}

	public static long ReaderRead(long readerId)
	{
		return readers.TryGetValue(readerId, out var context) && context.Reader.Read() ? 1 : 0;
	}

	public static long ReaderGetLong(long readerId, int columnIndex)
	{
		var reader = GetReader(readerId);
		return reader.IsDBNull(columnIndex) ? 0 : Convert.ToInt64(reader.GetValue(columnIndex), CultureInfo.InvariantCulture);
	}

	public static double ReaderGetFloat(long readerId, int columnIndex)
	{
		var reader = GetReader(readerId);
		return reader.IsDBNull(columnIndex) ? 0.0d : Convert.ToDouble(reader.GetValue(columnIndex), CultureInfo.InvariantCulture);
	}

	public static string ReaderGetString(long readerId, int columnIndex)
	{
		var reader = GetReader(readerId);
		return reader.IsDBNull(columnIndex) ? "" : Convert.ToString(reader.GetValue(columnIndex), CultureInfo.InvariantCulture) ?? "";
	}

	public static long ReaderIsNull(long readerId, int columnIndex)
	{
		return GetReader(readerId).IsDBNull(columnIndex) ? 1 : 0;
	}

	public static long ReaderClose(long readerId)
	{
		if (readers.TryGetValue(readerId, out var context))
		{
			context.Reader.Dispose();
			context.Command.Dispose();
			readers.Remove(readerId);
		}
		return 1;
	}

	public static long ExecuteScalarLong(string dbName, string sql, object[] parameters = null)
	{
		object value = ExecuteScalar(dbName, sql, parameters);
		return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
	}

	public static double ExecuteScalarFloat(string dbName, string sql, object[] parameters = null)
	{
		object value = ExecuteScalar(dbName, sql, parameters);
		return value == null || value == DBNull.Value ? 0.0d : Convert.ToDouble(value, CultureInfo.InvariantCulture);
	}

	public static string ExecuteScalarString(string dbName, string sql, object[] parameters = null)
	{
		object value = ExecuteScalar(dbName, sql, parameters);
		return value == null || value == DBNull.Value ? "" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
	}

	public static long ImportMapXml(string dbName, string tableName, string filePath)
	{
		var connection = GetConnection(dbName);
		var document = new XmlDocument();
		document.Load(ResolveDataPath(filePath, false));

		using var transaction = connection.BeginTransaction();
		using (var create = connection.CreateCommand())
		{
			create.Transaction = transaction;
			create.CommandText = $"CREATE TABLE IF NOT EXISTS {QuoteIdentifier(tableName)} (k TEXT PRIMARY KEY, v TEXT);";
			create.ExecuteNonQuery();
		}
		using (var insert = connection.CreateCommand())
		{
			insert.Transaction = transaction;
			insert.CommandText = $"INSERT OR REPLACE INTO {QuoteIdentifier(tableName)} (k, v) VALUES (@k, @v);";
			var keyParam = insert.Parameters.Add("@k", SqliteType.Text);
			var valueParam = insert.Parameters.Add("@v", SqliteType.Text);
			var nodes = document.SelectNodes("/map/p");
			if (nodes != null)
			{
				foreach (XmlNode node in nodes)
				{
					var key = node.SelectSingleNode("./k");
					var value = node.SelectSingleNode("./v");
					if (key == null || value == null)
						continue;
					keyParam.Value = key.InnerText;
					valueParam.Value = value.InnerXml;
					insert.ExecuteNonQuery();
				}
			}
		}
		transaction.Commit();
		return 1;
	}

	public static long ExportMapXml(string dbName, string tableName, string filePath)
	{
		var connection = GetConnection(dbName);
		string path = ResolveDataPath(filePath, true);
		using var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true });
		writer.WriteStartElement("map");
		using (var command = connection.CreateCommand())
		{
			command.CommandText = $"SELECT k, v FROM {QuoteIdentifier(tableName)}";
			using var reader = command.ExecuteReader();
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

	public static long ImportDtXml(string dbName, string tableName, string schemaPath, string dataPath)
	{
		var connection = GetConnection(dbName);
		var table = new DataTable(tableName);
		table.ReadXmlSchema(ResolveDataPath(schemaPath, false));
		table.ReadXml(ResolveDataPath(dataPath, false));

		using var transaction = connection.BeginTransaction();
		var columns = table.Columns.Cast<DataColumn>().Select(column =>
		{
			string type = IsIntegerColumn(column.DataType) ? "INTEGER" : column.DataType == typeof(double) ? "REAL" : "TEXT";
			string key = string.Equals(column.ColumnName, "id", StringComparison.OrdinalIgnoreCase) ? " PRIMARY KEY" : "";
			return $"{QuoteIdentifier(column.ColumnName)} {type}{key}";
		}).ToArray();
		using (var create = connection.CreateCommand())
		{
			create.Transaction = transaction;
			create.CommandText = $"CREATE TABLE IF NOT EXISTS {QuoteIdentifier(tableName)} ({string.Join(", ", columns)});";
			create.ExecuteNonQuery();
		}

		string columnNames = string.Join(", ", table.Columns.Cast<DataColumn>().Select(column => QuoteIdentifier(column.ColumnName)));
		string parameterNames = string.Join(", ", table.Columns.Cast<DataColumn>().Select(column => "@" + column.ColumnName));
		using (var insert = connection.CreateCommand())
		{
			insert.Transaction = transaction;
			insert.CommandText = $"INSERT OR REPLACE INTO {QuoteIdentifier(tableName)} ({columnNames}) VALUES ({parameterNames});";
			foreach (DataColumn column in table.Columns)
				insert.Parameters.Add("@" + column.ColumnName, ToSqliteType(column.DataType));
			foreach (DataRow row in table.Rows)
			{
				foreach (DataColumn column in table.Columns)
					insert.Parameters["@" + column.ColumnName].Value = row[column] == DBNull.Value ? DBNull.Value : row[column];
				insert.ExecuteNonQuery();
			}
		}
		transaction.Commit();
		return 1;
	}

	public static long ExportDtXml(string dbName, string tableName, string schemaPath, string dataPath)
	{
		var connection = GetConnection(dbName);
		var table = new DataTable(tableName);
		using (var command = connection.CreateCommand())
		{
			command.CommandText = $"SELECT * FROM {QuoteIdentifier(tableName)}";
			using var reader = command.ExecuteReader();
			table.Load(reader);
		}
		table.WriteXmlSchema(ResolveDataPath(schemaPath, true));
		table.WriteXml(ResolveDataPath(dataPath, true));
		return 1;
	}

	public static long ImportXmlCustom(string dbName, string tableName, string filePath, string rowXPath, string columnMappings)
	{
		var connection = GetConnection(dbName);
		var mapping = (columnMappings ?? "")
			.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(item => item.Split(new[] { '=' }, 2))
			.Where(parts => parts.Length == 2)
			.ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
		if (mapping.Count == 0)
			return 0;

		var document = new XmlDocument();
		document.Load(ResolveDataPath(filePath, false));
		var rows = document.SelectNodes(rowXPath ?? "");
		if (rows == null)
			return 0;

		using var transaction = connection.BeginTransaction();
		using (var create = connection.CreateCommand())
		{
			create.Transaction = transaction;
			create.CommandText = $"CREATE TABLE IF NOT EXISTS {QuoteIdentifier(tableName)} ({string.Join(", ", mapping.Keys.Select(column => QuoteIdentifier(column) + " TEXT"))});";
			create.ExecuteNonQuery();
		}
		using (var insert = connection.CreateCommand())
		{
			insert.Transaction = transaction;
			insert.CommandText = $"INSERT INTO {QuoteIdentifier(tableName)} ({string.Join(", ", mapping.Keys.Select(QuoteIdentifier))}) VALUES ({string.Join(", ", mapping.Keys.Select(column => "@" + column))});";
			foreach (string column in mapping.Keys)
				insert.Parameters.Add("@" + column, SqliteType.Text);
			foreach (XmlNode row in rows)
			{
				foreach (var pair in mapping)
					insert.Parameters["@" + pair.Key].Value = ReadCustomXmlValue(row, pair.Value) ?? (object)DBNull.Value;
				insert.ExecuteNonQuery();
			}
		}
		transaction.Commit();
		return rows.Count;
	}

	public static string Escape(string value)
	{
		return (value ?? "").Replace("'", "''");
	}

	public static void CloseAll()
	{
		foreach (var context in readers.Values)
		{
			context.Reader.Dispose();
			context.Command.Dispose();
		}
		readers.Clear();
		foreach (var connection in connections.Values)
			connection.Dispose();
		connections.Clear();
	}

	static object ExecuteScalar(string dbName, string sql, object[] parameters)
	{
		using var command = CreateCommand(dbName, sql, parameters);
		return command.ExecuteScalar();
	}

	static SqliteConnection GetConnection(string dbName)
	{
		if (!connections.TryGetValue(dbName ?? "", out var connection))
			throw new InvalidOperationException($"SQL database is not connected: {dbName ?? ""}");
		return connection;
	}

	static SqliteCommand CreateCommand(string dbName, string sql, object[] parameters)
	{
		var command = GetConnection(dbName).CreateCommand();
		command.CommandText = sql ?? "";
		if (parameters != null)
		{
			for (int i = 0; i < parameters.Length; i++)
			{
				var parameter = command.CreateParameter();
				parameter.ParameterName = "@" + i.ToString(CultureInfo.InvariantCulture);
				parameter.Value = parameters[i] ?? DBNull.Value;
				command.Parameters.Add(parameter);
			}
		}
		return command;
	}

	static SqliteDataReader GetReader(long readerId)
	{
		if (!readers.TryGetValue(readerId, out var context))
			throw new InvalidOperationException($"Invalid SQL reader id: {readerId}");
		return context.Reader;
	}

	static void CloseReadersFor(string dbName)
	{
		var ids = readers.Where(pair => string.Equals(pair.Value.DatabaseName, dbName, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToArray();
		foreach (long id in ids)
			ReaderClose(id);
	}

	static string ResolveDataPath(string path, bool createParent)
	{
		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException("SQL file path is empty.");
		string resolved = Path.IsPathRooted(path) ? path : Path.Combine(StorageDirectory, path);
		if (createParent)
		{
			string directory = Path.GetDirectoryName(resolved);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);
		}
		return resolved;
	}

	static string QuoteIdentifier(string identifier)
	{
		if (string.IsNullOrWhiteSpace(identifier))
			throw new ArgumentException("SQL identifier is empty.");
		return "\"" + identifier.Replace("\"", "\"\"") + "\"";
	}

	static SqliteType ToSqliteType(Type type)
	{
		if (IsIntegerColumn(type))
			return SqliteType.Integer;
		if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
			return SqliteType.Real;
		return SqliteType.Text;
	}

	static bool IsIntegerColumn(Type type)
	{
		return type == typeof(sbyte) || type == typeof(short) || type == typeof(int) || type == typeof(long) || type == typeof(byte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong);
	}

	static string ReadCustomXmlValue(XmlNode row, string expression)
	{
		if (string.IsNullOrWhiteSpace(expression))
			return null;
		if (expression.StartsWith("@", StringComparison.Ordinal))
			return row.Attributes?[expression.Substring(1)]?.Value;
		if (expression.EndsWith("(xml)", StringComparison.OrdinalIgnoreCase))
		{
			string nodePath = expression.Substring(0, expression.Length - 5);
			return row.SelectSingleNode(nodePath)?.InnerXml;
		}
		return row.SelectSingleNode(expression)?.InnerText;
	}
}
