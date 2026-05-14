using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
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
			if (string.IsNullOrWhiteSpace(connectionString))
				connectionString = "Data Source=:memory:";

			if (connections.ContainsKey(dbName))
			{
				if (!replaceExisting)
					return 1;
				Disconnect(dbName);
			}

			var conn = new SqliteConnection(connectionString);
			try
			{
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
				conn.Dispose();
				throw new CodeEE("SQL_CONNECT failed: " + ex.Message);
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
	}
}
