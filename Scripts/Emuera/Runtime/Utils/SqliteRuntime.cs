using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MinorShift.Emuera.Runtime.Utils
{
	internal static class SqliteRuntime
	{
		static bool initialized;
		static bool resolverInstalled;
		static readonly object initLock = new object();

		public static void EnsureInitialized()
		{
			if (initialized)
				return;
			lock (initLock)
			{
				if (initialized)
					return;
				InstallNativeResolver();
				SQLitePCL.Batteries_V2.Init();
				initialized = true;
			}
		}

		static void InstallNativeResolver()
		{
			if (resolverInstalled)
				return;

			resolverInstalled = true;
			try
			{
				Assembly providerAssembly = Assembly.Load("SQLitePCLRaw.provider.e_sqlite3");
				NativeLibrary.SetDllImportResolver(providerAssembly, ResolveSqliteNativeLibrary);
			}
			catch (InvalidOperationException)
			{
				// Another startup path already installed a resolver for the provider assembly.
			}
			catch
			{
				// Let SQLitePCLRaw use its default loader; callers will report the concrete failure.
			}
		}

		static IntPtr ResolveSqliteNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
		{
			if (!IsSqliteNativeName(libraryName))
				return IntPtr.Zero;

			foreach (string candidate in GetNativeLibraryCandidates())
			{
				if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out IntPtr handle))
					return handle;
				if (NativeLibrary.TryLoad(candidate, out handle))
					return handle;
			}

			return IntPtr.Zero;
		}

		static bool IsSqliteNativeName(string libraryName)
		{
			return string.Equals(libraryName, "e_sqlite3", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(libraryName, "libe_sqlite3.so", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(libraryName, "e_sqlite3.dll", StringComparison.OrdinalIgnoreCase);
		}

		static IEnumerable<string> GetNativeLibraryCandidates()
		{
			yield return "e_sqlite3";
			yield return "libe_sqlite3.so";

			foreach (string dir in GetNativeSearchDirectories())
			{
				if (string.IsNullOrWhiteSpace(dir))
					continue;
				yield return Path.Combine(dir, "libe_sqlite3.so");
				yield return Path.Combine(dir, "e_sqlite3.dll");
			}
		}

		static IEnumerable<string> GetNativeSearchDirectories()
		{
			yield return AppContext.BaseDirectory;
			yield return Path.GetDirectoryName(typeof(SqliteRuntime).Assembly.Location);
			yield return Directory.GetCurrentDirectory();

			string ldPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
			if (!string.IsNullOrWhiteSpace(ldPath))
			{
				foreach (string dir in ldPath.Split(':'))
					yield return dir;
			}
		}

		public static string NormalizeConnectionString(string connectionString, string baseDirectory)
		{
			if (string.IsNullOrWhiteSpace(connectionString))
				return "Data Source=:memory:";

			try
			{
				var builder = new SqliteConnectionStringBuilder(connectionString);
				string dataSource = builder.DataSource;
				if (!string.IsNullOrWhiteSpace(dataSource)
					&& dataSource != ":memory:"
					&& dataSource.IndexOf(":", StringComparison.Ordinal) < 0
					&& !Path.IsPathRooted(dataSource)
					&& !string.IsNullOrWhiteSpace(baseDirectory))
				{
					string path = Path.Combine(baseDirectory, dataSource.Replace('/', Path.DirectorySeparatorChar));
					string dir = Path.GetDirectoryName(path);
					if (!string.IsNullOrEmpty(dir))
						Directory.CreateDirectory(dir);
					builder.DataSource = path;
				}
				return builder.ConnectionString;
			}
			catch
			{
				return connectionString;
			}
		}

		public static string FormatException(Exception exception)
		{
			var builder = new StringBuilder();
			for (Exception current = exception; current != null; current = current.InnerException)
			{
				if (builder.Length > 0)
					builder.Append(" -> ");
				builder.Append(current.GetType().Name);
				if (!string.IsNullOrEmpty(current.Message))
				{
					builder.Append(": ");
					builder.Append(current.Message);
				}
			}
			return builder.ToString();
		}
	}
}
