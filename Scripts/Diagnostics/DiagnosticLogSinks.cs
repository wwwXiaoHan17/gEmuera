using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Godot;

namespace gEmuera.Diagnostics
{
	/// <summary>
	/// 企业级说明：日志 Sink 层负责接收已通过路由和限流的记录，写入内存 ring buffer、Godot 控制台
	/// 以及可选的持续文件 sink（user://gemuera_runtime_*.log，1 MiB 轮转）。
	/// 文件 sink 只在 LoggingEnabled && FileSinkEnabled 时打开；热路径只操作预分配数组、Interlocked 计数和缓存等级，避免 GC。
	/// </summary>
	public static class DiagnosticLogSinks
	{
		/// <summary>单文件上限 1 MiB，写满后轮转为新时间戳文件。</summary>
		public const long MaxFileSinkBytes = 1024 * 1024;
		const string FileSinkRootPath = "user://";

		static RuntimeDiagnosticsConfig _config;
		static long _overwrittenTotal;
		static readonly object _ringLock = new object();
		static DiagnosticLogRecord[] _ring;
		static int _ringStart;
		static int _ringCount;
		static int _ringCapacity;
		static int _mirrorNonErrorToGodot;

		// ---------- 持续文件 sink ----------
		static readonly object _fileSinkLock = new object();
		static int _fileSinkEnabled;
		static int _fileSinkOpenFailures;
		const int MaxFileSinkOpenFailures = 3;
		static int _fileSinkLevel = (int)EmueraLogLevel.Info;
		static StreamWriter _fileSinkWriter;
		static long _fileSinkBytes;
		static string _fileSinkPath = "";
		static string _fileSinkUserRoot = "";
		static string _fileSinkStamp = "";
		static int _fileSinkRotationSuffix;

		public static void Initialize(RuntimeDiagnosticsConfig config)
		{
			_config = config ?? RuntimeDiagnosticsConfig.CreateDefault();
			if (!_config.LoggingEnabled)
			{
				_ringCapacity = 0;
				_ring = null;
				_ringStart = 0;
				_ringCount = 0;
				_overwrittenTotal = 0;
				_mirrorNonErrorToGodot = 0;
				Reload(_config);
				return;
			}
			_ringCapacity = ClampRingCapacity(_config.LoggingDiagnosticRingCapacity);
			_ring = new DiagnosticLogRecord[_ringCapacity];
			_ringStart = 0;
			_ringCount = 0;
			_overwrittenTotal = 0;
			_mirrorNonErrorToGodot = _config.LoggingMirrorNonErrorToGodot ? 1 : 0;
			Reload(_config);
		}

		/// <summary>ring 容量夹在 [64, 100000]：旧实现只有下限，配置手误（多打零）会在启动期分配巨型数组。</summary>
		static int ClampRingCapacity(int capacity)
		{
			return Math.Clamp(capacity, 64, 100_000);
		}

		/// <summary>
		/// 企业级说明：热重载文件 sink 开关与等级（含面板保存后的 ReloadRuntimeDiagnosticsConfig）。
		/// 只更新缓存的 int/布尔，不重建 ring buffer；开关从关→开时补建文件，开→关时立即关闭释放句柄。
		/// </summary>
		public static void Reload(RuntimeDiagnosticsConfig config)
		{
			if (config == null)
				return;
			// 2026-09-19 审计 P0 修复：热重载必须刷新 _config——启动时日志关闭的会话，
			// Write 一直被陈旧 _config.LoggingEnabled 拦截，路由放行的记录全部静默丢失。
			// 与同文件其它热标志一致用 Volatile（主线程写、Emuera worker 线程读）。
			Volatile.Write(ref _config, config);
			// 关→开补建 ring：旧实现只在 Initialize 分配，关闭启动后热重载开启时 ring 仍为 null。
			if (config.LoggingEnabled && (_ring == null || _ringCapacity <= 0))
			{
				lock (_ringLock)
				{
					if (_ring == null || _ringCapacity <= 0)
					{
						_ringCapacity = ClampRingCapacity(config.LoggingDiagnosticRingCapacity);
						_ring = new DiagnosticLogRecord[_ringCapacity];
						_ringStart = 0;
						_ringCount = 0;
					}
				}
			}
			Volatile.Write(ref _fileSinkLevel, (int)RuntimeDiagnosticsConfig.ParseLogLevel(config.FileSinkLevel));
			// 企业级说明：user 数据目录根只在这里（主线程）解析并缓存；
			// 文件写入可能发生在 Emuera worker 线程，不能直接调用 ProjectSettings.GlobalizePath。
			try
			{
				_fileSinkUserRoot = Path.GetFullPath(ProjectSettings.GlobalizePath(FileSinkRootPath));
			}
			catch
			{
				_fileSinkUserRoot = "";
			}
			bool enabled = config.LoggingEnabled && config.FileSinkEnabled;
			if (Volatile.Read(ref _fileSinkEnabled) != 0 && !enabled)
			{
				Volatile.Write(ref _fileSinkEnabled, 0);
				lock (_fileSinkLock)
					CloseFileSinkLocked();
				return;
			}
			Volatile.Write(ref _fileSinkEnabled, enabled ? 1 : 0);
			if (enabled)
			{
				// 显式重新启用（保存/热重载）视为用户新意图，重置连续打开失败计数。
				_fileSinkOpenFailures = 0;
				lock (_fileSinkLock)
					EnsureFileSinkOpenLocked();
			}
		}

		/// <summary>应用退出/暂停时的最终刷新与关闭（GenericUtils.NotifyApplicationShutdown 调用）。</summary>
		public static void Shutdown()
		{
			lock (_fileSinkLock)
				CloseFileSinkLocked();
		}

		public static bool IsFileSinkEnabled => Volatile.Read(ref _fileSinkEnabled) != 0;
		public static string FileSinkCurrentPath => _fileSinkPath;

		public static int RingCapacity => _ringCapacity;
		public static int RingCount => Volatile.Read(ref _ringCount);
		public static long OverwrittenTotal => Interlocked.Read(ref _overwrittenTotal);

		public static void SetMirrorNonErrorToGodot(bool value)
		{
			Volatile.Write(ref _mirrorNonErrorToGodot, value ? 1 : 0);
		}

		/// <summary>
		/// 将单条记录同时写入 ring buffer、持续文件 sink（如启用）和 Godot 控制台（如果等级/策略允许）。
		/// 调用方已确保通过 Router.IsEnabled 限流和开关检查，本方法只做最低开销的写入。
		/// 文件 sink 使用独立 FileSinkLevel（默认 info），与 Godot 镜像等级无关。
		/// </summary>
		public static void Write(in DiagnosticLogRecord record)
		{
			var config = Volatile.Read(ref _config);
			if (config == null || !config.LoggingEnabled)
				return;
			AppendToRing(record);
			if (record.Level >= EmueraLogLevel.Error && global::gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
			{
				global::gEmuera.LegacyRunner.LegacyTrace.TryRecordError("diagnostic_error", record.Level.ToString(),
					record.EventId, record.Message, record.Source + ":" + record.Line + " " + record.Member);
			}
			if (Volatile.Read(ref _fileSinkEnabled) != 0 && record.Level >= (EmueraLogLevel)Volatile.Read(ref _fileSinkLevel))
				AppendToFileSink(record);
			if (ShouldMirrorToGodot(record.Level))
				WriteToGodotConsole(record);
		}

		// ---------- 持续文件 sink ----------

		static void AppendToFileSink(in DiagnosticLogRecord record)
		{
			string line;
			try
			{
				line = record.FormatForExport();
			}
			catch
			{
				return;
			}

			lock (_fileSinkLock)
			{
				if (Volatile.Read(ref _fileSinkEnabled) == 0)
					return;
				if (_fileSinkWriter == null)
					EnsureFileSinkOpenLocked();
				var writer = _fileSinkWriter;
				if (writer == null)
					return;

				// 1 MiB 轮转：写入前检查，写满后关闭并以新时间戳重开文件。
				long lineBytes = Encoding.UTF8.GetByteCount(line) + 2;
				if (_fileSinkBytes > 0 && _fileSinkBytes + lineBytes > MaxFileSinkBytes)
				{
					CloseFileSinkLocked();
					EnsureFileSinkOpenLocked();
					writer = _fileSinkWriter;
					if (writer == null)
						return;
				}
				try
				{
					writer.WriteLine(line);
					_fileSinkBytes += lineBytes;
				}
				catch
				{
					// 写盘失败（磁盘满/权限被回收等）时关闭句柄并降级为纯内存日志，避免热路径反复异常。
					CloseFileSinkLocked();
					Volatile.Write(ref _fileSinkEnabled, 0);
				}
			}
		}

		static bool EnsureFileSinkOpenLocked()
		{
			if (_fileSinkWriter != null)
				return true;
			try
			{
				string fileName = BuildFileSinkFileName();
				if (!TryResolveFileSinkPath(fileName, out string absolutePath, out string reason))
				{
					GD.PushWarning("[FILE_SINK.OPEN_REJECTED] " + reason);
					HandleFileSinkOpenFailure();
					return false;
				}
				string dir = Path.GetDirectoryName(absolutePath);
				if (!string.IsNullOrEmpty(dir))
					Directory.CreateDirectory(dir);
				var stream = new FileStream(absolutePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read);
				_fileSinkWriter = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
				_fileSinkBytes = 0;
				_fileSinkPath = absolutePath;
				_fileSinkOpenFailures = 0;
				PruneFileSinkRotatedFilesLocked(absolutePath);
				return true;
			}
			catch (Exception ex)
			{
				GD.PushWarning("[FILE_SINK.OPEN_FAIL] " + ex.GetType().Name + ": " + ex.Message);
				HandleFileSinkOpenFailure();
				CloseFileSinkLocked();
				return false;
			}
		}

		/// <summary>
		/// 打开失败连续 N 次后把文件 sink 降级为纯内存，与写失败路径的降级一致：
		/// 避免 user:// 不可写等一次性故障后每条记录都重新尝试开文件并刷告警。
		/// 调用方需持有 _fileSinkLock。
		/// </summary>
		static void HandleFileSinkOpenFailure()
		{
			if (++_fileSinkOpenFailures >= MaxFileSinkOpenFailures)
				Volatile.Write(ref _fileSinkEnabled, 0);
		}

		static void CloseFileSinkLocked()
		{
			try
			{
				_fileSinkWriter?.Dispose();
			}
			catch
			{
			}
			_fileSinkWriter = null;
			_fileSinkBytes = 0;
			_fileSinkPath = "";
			// 企业级说明：不清空 _fileSinkStamp/_fileSinkRotationSuffix —— 轮转是“先关后开”，
			// 若在同一秒内重开，BuildFileSinkFileName 需要旧 stamp 来追加 -N 序号，避免覆盖刚关闭的文件。
		}

		static string BuildFileSinkFileName()
		{
			string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
			if (string.Equals(stamp, _fileSinkStamp, StringComparison.Ordinal))
			{
				// 同一秒内多次轮转：追加序号避免覆盖上一份日志。
				_fileSinkRotationSuffix++;
				return string.Format("gemuera_runtime_{0}-{1}.log", stamp, _fileSinkRotationSuffix);
			}
			_fileSinkStamp = stamp;
			_fileSinkRotationSuffix = 0;
			return "gemuera_runtime_" + stamp + ".log";
		}

		/// <summary>
		/// 2026-09-19 审计 P0 修复：user:// 轮转文件原本无总量上限、无任何清理路径
		///（retention 只覆盖 game://），Android 私有存储会静默累积直至耗尽。
		/// 每次新开文件后按修改时间保留最新 file_sink_max_files-1 份历史文件（另加当前文件），
		/// 删除更旧的轮转文件；单文件删除失败静默跳过（尽力而为的清理，不阻断写入）。
		/// 调用方需持有 _fileSinkLock。
		/// </summary>
		static void PruneFileSinkRotatedFilesLocked(string currentAbsolutePath)
		{
			try
			{
				string root = _fileSinkUserRoot;
				if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
					return;
				int maxFiles = Volatile.Read(ref _config)?.LoggingFileSinkMaxFiles ?? 8;
				if (maxFiles <= 0)
					return; // 0 = 显式不限量
				var files = new List<FileInfo>(Math.Max(0, maxFiles * 2));
				foreach (string path in Directory.EnumerateFiles(root, "gemuera_runtime_*.log", SearchOption.TopDirectoryOnly))
				{
					if (string.Equals(path, currentAbsolutePath, StringComparison.OrdinalIgnoreCase))
						continue;
					files.Add(new FileInfo(path));
				}
				int keepOld = maxFiles - 1;
				if (files.Count <= keepOld)
					return;
				files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
				for (int i = keepOld; i < files.Count; i++)
				{
					try { files[i].Delete(); }
					catch { }
				}
			}
			catch
			{
				// 清理自身故障不影响日志写入主路径。
			}
		}

		/// <summary>
		/// 企业级说明：持续文件 sink 固定写 user://，越界约束与 DiagnosticLogExporter.TryResolveDiagnosticsFilePath
		/// 同款（GetFullPath 规范化 + 根目录前缀校验），只是根目录换成本应用的 user 数据目录。
		/// 即使未来配置键被手动注入 ../ 也不能把日志写出 user 数据目录。
		/// </summary>
		static bool TryResolveFileSinkPath(string fileName, out string absolutePath, out string reason)
		{
			absolutePath = "";
			reason = "";
			try
			{
				string userRoot = _fileSinkUserRoot;
				if (string.IsNullOrEmpty(userRoot))
				{
					reason = "user_root_unavailable";
					return false;
				}
				string candidate = Path.GetFullPath(Path.Combine(userRoot, fileName));
				string rootWithSlash = userRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
					+ Path.DirectorySeparatorChar;
				if (!candidate.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
				{
					reason = "outside_user_directory candidate=" + candidate + " root=" + userRoot;
					return false;
				}
				absolutePath = candidate;
				return true;
			}
			catch (Exception ex)
			{
				reason = ex.GetType().Name + ": " + ex.Message;
				return false;
			}
		}

		static void AppendToRing(in DiagnosticLogRecord record)
		{
			lock (_ringLock)
			{
				if (_ring == null)
					return;
				if (_ringCount >= _ringCapacity)
				{
					_ring[_ringStart] = record;
					_ringStart = (_ringStart + 1) % _ringCapacity;
					Interlocked.Increment(ref _overwrittenTotal);
				}
				else
				{
					int index = (_ringStart + _ringCount) % _ringCapacity;
					_ring[index] = record;
					_ringCount++;
				}
			}
		}

		static bool ShouldMirrorToGodot(EmueraLogLevel level)
		{
			return level >= EmueraLogLevel.Error || Volatile.Read(ref _mirrorNonErrorToGodot) != 0;
		}

		static void WriteToGodotConsole(in DiagnosticLogRecord record)
		{
			string message = record.FormatForGodot();
			switch (record.Level)
			{
				case EmueraLogLevel.Warn:
					GD.PushWarning(message);
					break;
				case EmueraLogLevel.Error:
					 GD.PushError(message);
					break;
				default:
					GD.Print(message);
					break;
			}
		}

		public static DiagnosticLogRecord[] Snapshot()
		{
			lock (_ringLock)
			{
				if (_ring == null || _ringCount == 0)
					return Array.Empty<DiagnosticLogRecord>();
				var result = new DiagnosticLogRecord[_ringCount];
				for (int i = 0; i < result.Length; i++)
					result[i] = _ring[(_ringStart + i) % _ringCapacity];
				return result;
			}
		}
	}
}
