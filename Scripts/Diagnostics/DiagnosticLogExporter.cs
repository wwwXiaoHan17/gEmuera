using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Godot;

namespace gEmuera.Diagnostics
{
	/// <summary>
	/// 企业级说明：诊断导出器只在用户主动触发时执行文件 I/O，不会在热路径写盘。
	/// 负责生成诊断包（日志、配置快照、设备/屏幕/Godot/APK/游戏信息、摘要）。
	/// Android 上所有默认写操作限制在本次启动的游戏目录，不得越界到其它游戏、存档或系统目录。
	/// </summary>
	public static class DiagnosticLogExporter
	{
		public const string GameDirectoryPathPrefix = "game://";
		const string GameDirectoryUnavailableReason = "game_directory_not_selected";
		static readonly object _breadcrumbLock = new object();
		static readonly StringBuilder _breadcrumbBuilder = new StringBuilder(4096);
		static string _lastGamePath = "";
		static int _errorCount = 0;
		static string _lastEventId = "";

		public static void NotifyGamePathSelected(string path)
		{
			_lastGamePath = NormalizeGameDirectory(path);
		}

		public static string GetCurrentGameDirectoryForDisplay()
		{
			return TryGetCurrentGameDirectory(out string gameDirectory, out _)
				? gameDirectory
				: "尚未进入游戏主场景，未确定启动游戏目录";
		}

		public static string ResolvePathForDisplay(string path)
		{
			try
			{
				string normalized = NormalizeDiagnosticsPath(path);
				if (normalized.StartsWith(GameDirectoryPathPrefix, StringComparison.OrdinalIgnoreCase))
				{
					if (!TryBuildGameDirectoryPath(normalized, "", out string resolved, out string reason))
						return reason;
					return resolved;
				}
				if (normalized.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
					|| normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
					return ProjectSettings.GlobalizePath(normalized);
				return normalized;
			}
			catch
			{
				return path ?? "";
			}
		}

		public static void NotifyError(string eventId)
		{
			Interlocked.Increment(ref _errorCount);
			_lastEventId = eventId ?? "";
		}

		public static void NotifyEvent(string eventId)
		{
			_lastEventId = eventId ?? "";
		}

		/// <summary>
		/// 企业级说明：基础设施诊断事件用于启动、自检、导出、retention 等低频链路。
		/// 这些事件默认写入内存 ring buffer，不受 debug_model 的 Error 级热路径闸门影响，
		/// 但仍经过统一限流，避免 Android 启动异常时形成重复刷屏。
		/// </summary>
		public static void WriteInfrastructureRecord(EmueraLogLevel level, EmueraLogCategory category,
			string eventId, string message, string data)
		{
			if (!DiagnosticLogRouter.IsLoggingEnabled())
				return;
			// 2026-09-19 审计修复：level=none 契约修补——基础设施记录此前绕过等级门，
			// “关闭全部日志（错误也不记录）”语义下仍持续写入。none 即一条不留。
			if (global::GenericUtils.RuntimeLogLevel == EmueraLogLevel.None)
				return;
			if (!DiagnosticLogRouter.CheckRateLimit(eventId, category))
				return;

			var record = DiagnosticLogRouter.BuildRecord(
				level,
				category,
				eventId ?? "",
				message ?? "",
				data ?? "",
				nameof(WriteInfrastructureRecord),
				"Scripts/Diagnostics/DiagnosticLogExporter.cs",
				0);
			DiagnosticLogSinks.Write(record);
			if (level >= EmueraLogLevel.Error)
				NotifyError(eventId);
			else
				NotifyEvent(eventId);
		}

		// ---------- breadcrumb ----------

		public static void WriteBreadcrumb(RuntimeDiagnosticsConfig config, string eventId, string data)
		{
			if (config == null || !config.LoggingEnabled || !config.BreadcrumbEnabled)
				return;
			lock (_breadcrumbLock)
			{
				var sb = _breadcrumbBuilder;
				if (sb.Length == 0)
				{
					sb.AppendLine("# gEmuera breadcrumb");
					sb.AppendLine("# session=" + DiagnosticLogRouter.SessionId);
				}
				sb.Append("utc=").Append(DateTimeOffset.UtcNow.ToString("O"));
				sb.Append(" event_id=").Append(eventId ?? "");
				if (!string.IsNullOrEmpty(data))
					sb.Append(" data=").Append(data);
				sb.AppendLine();
				TrimBreadcrumb(sb, config.BreadcrumbMaxBytes);
				if (sb.Length > 0)
					FlushBreadcrumbToDisk(config.BreadcrumbPath, sb.ToString());
			}
		}

		static void TrimBreadcrumb(StringBuilder sb, int maxBytes)
		{
			if (sb.Length == 0)
				return;
			int byteCount = Encoding.UTF8.GetByteCount(sb.ToString());
			if (byteCount <= maxBytes)
				return;
			string text = sb.ToString();
			int linesToSkip = 0;
			while (Encoding.UTF8.GetByteCount(text) > maxBytes && linesToSkip < 100)
			{
				int nextLine = text.IndexOf('\n');
				if (nextLine < 0)
					break;
				text = text.Substring(nextLine + 1);
				linesToSkip++;
			}
			sb.Clear();
			sb.Append(text);
		}

		static void FlushBreadcrumbToDisk(string path, string content)
		{
			try
			{
				if (!TryResolveDiagnosticsFilePath(path, "last-session-breadcrumb.log", out string normalized, out string reason))
				{
					if (!string.Equals(reason, GameDirectoryUnavailableReason, StringComparison.OrdinalIgnoreCase))
						GD.PushWarning("[BREADCRUMB.WRITE_REJECTED] " + reason);
					return;
				}

				string dir = Path.GetDirectoryName(normalized);
				if (!string.IsNullOrEmpty(dir))
					Directory.CreateDirectory(dir);
				File.WriteAllText(normalized, content, Encoding.UTF8);
			}
			catch (Exception ex)
			{
				GD.PushWarning("[BREADCRUMB.WRITE_FAIL] " + ex.GetType().Name + ": " + ex.Message);
			}
		}

		// ---------- retention ----------

		public static void RunRetentionCleanup(RuntimeDiagnosticsConfig config)
		{
			if (config == null || !config.LoggingEnabled || !config.RetentionEnabled)
				return;
			try
			{
				if (!TryResolveDiagnosticsDirectory(config.RetentionDirectory, out string dir, out string reason))
				{
					if (string.Equals(reason, GameDirectoryUnavailableReason, StringComparison.OrdinalIgnoreCase))
						return;
					WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.FileSystem,
						"RETENTION.FAIL", "retention directory rejected", "reason=" + reason);
					return;
				}
				if (Directory.Exists(dir))
					RunRetentionCleanupOnDirectory(dir, config);
			}
			catch (Exception ex)
			{
				WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.FileSystem,
					"RETENTION.FAIL", "retention cleanup failed",
					"error=" + ex.GetType().Name + " message=" + Clip(ex.Message, 160));
			}
		}

		static void RunRetentionCleanupOnDirectory(string dir, RuntimeDiagnosticsConfig config)
		{
			// 企业级说明：诊断目录现在默认就是游戏根目录，retention 绝不能按“目录内所有文件”清理。
			// 这里只允许处理 gEmuera 诊断导出命名规则下的文件，避免误删 ERB、CSV、图片、存档等游戏资源。
			var files = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
				.Select(f => new FileInfo(f))
				.Where(f => IsManagedDiagnosticFile(f.Name))
				.Where(f => !IsProtectedSessionFile(f.Name))
				.OrderBy(f => f.LastWriteTimeUtc)
				.ToList();
			long totalBytes = files.Sum(f => f.Length);
			long maxBytes = (long)config.RetentionMaxTotalMb * 1024 * 1024;
			int maxPackages = config.RetentionMaxPackages;
			WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.FileSystem,
				"RETENTION.SCAN", "retention scan",
				$"dir={dir} files={files.Count} bytes={totalBytes} max_packages={maxPackages} max_bytes={maxBytes}");
			int deleted = 0;
			long freedBytes = 0;
			while ((files.Count > maxPackages || totalBytes > maxBytes) && files.Count > 0)
			{
				var oldest = files[0];
				try
				{
					File.Delete(oldest.FullName);
					totalBytes -= oldest.Length;
					freedBytes += oldest.Length;
					deleted++;
				}
				catch { }
				files.RemoveAt(0);
			}
			if (deleted > 0)
			{
				WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.FileSystem,
					"RETENTION.DELETE_OLD", "old diagnostics deleted",
					$"deleted={deleted} freed_bytes={freedBytes} remaining={files.Count}");
			}
		}

		static bool IsManagedDiagnosticFile(string name)
		{
			if (string.IsNullOrEmpty(name))
				return false;
			if (name.Equals("gemuera-last-session-breadcrumb.log", StringComparison.OrdinalIgnoreCase))
				return true;
			if (name.Equals("last-session-breadcrumb.log", StringComparison.OrdinalIgnoreCase))
				return true;
			if (name.StartsWith("gemuera_", StringComparison.OrdinalIgnoreCase)
				&& name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
				return true;
			if (!name.StartsWith("gemuera-", StringComparison.OrdinalIgnoreCase))
				return false;
			return name.Contains("diagnostic", StringComparison.OrdinalIgnoreCase)
				|| name.Contains("breadcrumb", StringComparison.OrdinalIgnoreCase);
		}

		static bool IsProtectedSessionFile(string name)
		{
			string session = DiagnosticLogRouter.SessionId ?? "";
			if (!string.IsNullOrEmpty(session) && name.Contains(session, StringComparison.OrdinalIgnoreCase))
				return true;
			if (name.Contains("breadcrumb", StringComparison.OrdinalIgnoreCase))
				return true;
			return false;
		}

		// ---------- export package ----------

		public static bool ExportDiagnosticPackage(RuntimeDiagnosticsConfig config, string outputDirectory,
			string gamePath, string coreProfile, bool useLazyLoading, InputReplayBuffer inputReplay,
			out string errorMessage)
		{
			errorMessage = "";
			if (config == null || !config.LoggingEnabled)
			{
				errorMessage = "diagnostics_disabled";
				return false;
			}
			if (!config.DiagnosticPackageEnabled)
			{
				errorMessage = "diagnostic_package_disabled";
				WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.Save,
					"DIAGNOSTIC_PACKAGE.FAIL", "diagnostic package export disabled",
					"reason=diagnostic_package_disabled");
				return false;
			}

			if (string.IsNullOrEmpty(outputDirectory))
				outputDirectory = NormalizeDiagnosticsPath(config?.LoggingExportDirectory ?? GameDirectoryPathPrefix);

			if (!TryResolveDiagnosticsDirectory(outputDirectory, out string resolvedOutputDirectory, out string outputReason))
			{
				errorMessage = outputReason;
				WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.Save,
					"DIAGNOSTIC_PACKAGE.FAIL", "diagnostic package export directory rejected",
					"reason=" + outputReason);
				return false;
			}
			outputDirectory = resolvedOutputDirectory;

			try
			{
				Directory.CreateDirectory(outputDirectory);
			}
			catch (Exception ex)
			{
				errorMessage = "create_dir: " + ex.Message;
				return false;
			}

			if (config?.RetentionCleanupBeforeExport ?? true)
				RunRetentionCleanup(config);
			string session = DiagnosticLogRouter.SessionId ?? DateTime.Now.ToString("yyyyMMdd-HHmmss");
			string prefix = $"gemuera-{session}";

			try
			{
				string zipPath = Path.Combine(outputDirectory, $"{prefix}-diagnostic.zip");
				WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Save,
					"DIAGNOSTIC_PACKAGE.START", "diagnostic package export started",
					"path=" + zipPath);
				var records = DiagnosticLogSinks.Snapshot();

				using (var zipStream = new FileStream(zipPath, FileMode.Create, System.IO.FileAccess.Write))
				using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true, Encoding.UTF8))
				{
					void AddEntry(string entryName, string content)
					{
						var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
						using (var entryStream = entry.Open())
						using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
							writer.Write(content);
						WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Save,
							"DIAGNOSTIC_PACKAGE.WRITE_FILE", "diagnostic package file written",
							"entry=" + entryName);
					}

					if (config?.DiagnosticPackageIncludeLog ?? true)
						AddEntry("diagnostic.log", BuildDiagnosticLogText(config, records, gamePath, coreProfile, useLazyLoading));

					if (config?.DiagnosticPackageIncludeConfigSnapshot ?? true)
						AddEntry("config.snapshot.toml", BuildConfigSnapshot(config));

					if (config?.DiagnosticPackageIncludeDeviceInfo ?? true)
						AddEntry("device.txt", BuildDeviceInfo());

					if (config?.DiagnosticPackageIncludeScreenInfo ?? true)
						AddEntry("screen.txt", BuildScreenInfo());

					if (config?.DiagnosticPackageIncludeGodotInfo ?? true)
						AddEntry("godot.txt", BuildGodotInfo());

					if (config?.DiagnosticPackageIncludeApkInfo ?? true)
						AddEntry("apk.txt", BuildApkInfo());

					if (config?.DiagnosticPackageIncludeGamePath ?? true)
						AddEntry("game.txt", BuildGameInfo(gamePath, coreProfile, useLazyLoading));

					if (config?.DiagnosticPackageIncludeErrorSummary ?? true)
						AddEntry("summary.txt", BuildSummary(records, config));

					if (inputReplay != null && inputReplay.Count > 0)
					{
						AddEntry("input-replay.txt", inputReplay.BuildExportText());
						WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Input,
							"REPLAY.INPUT.EXPORT", "input replay exported",
							"count=" + inputReplay.Count);
					}

					if (config?.SnapshotEnabled ?? false)
						AddSnapshotEntries(config, records, AddEntry);
				}

				WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.Save,
					"DIAGNOSTIC_PACKAGE.OK", "diagnostic package export completed",
					"path=" + zipPath);
				return true;
			}
			catch (Exception ex)
			{
				errorMessage = ex.GetType().Name + ": " + ex.Message;
				WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.Save,
					"DIAGNOSTIC_PACKAGE.FAIL", "diagnostic package export failed",
					"error=" + ex.GetType().Name + " message=" + Clip(ex.Message, 160));
				return false;
			}
		}

		public static bool ExportDiagnosticLog(RuntimeDiagnosticsConfig config, string path,
			string gamePath, string coreProfile, bool useLazyLoading,
			out string errorMessage)
		{
			return ExportDiagnosticLog(config, path, gamePath, coreProfile, useLazyLoading, null, out errorMessage);
		}

		public static bool ExportDiagnosticLog(RuntimeDiagnosticsConfig config, string path,
			string gamePath, string coreProfile, bool useLazyLoading, SaveLogOperationTrail operationTrail,
			out string errorMessage)
		{
			errorMessage = "";
			if (config == null || !config.LoggingEnabled)
			{
				errorMessage = "diagnostics_disabled";
				return false;
			}
			try
			{
				if (!TryResolveDiagnosticsFilePath(path, "diagnostic.log", out string resolvedPath, out string reason))
				{
					errorMessage = reason;
					return false;
				}

				var records = DiagnosticLogSinks.Snapshot();
				string text = BuildDiagnosticLogText(config, records, gamePath, coreProfile, useLazyLoading, operationTrail);
				string dir = Path.GetDirectoryName(resolvedPath);
				if (!string.IsNullOrEmpty(dir))
					Directory.CreateDirectory(dir);
				File.WriteAllText(resolvedPath, text, Encoding.UTF8);
				return true;
			}
			catch (Exception ex)
			{
				errorMessage = ex.GetType().Name + ": " + ex.Message;
				return false;
			}
		}

		// ---------- report builders ----------

		static string BuildDiagnosticLogText(RuntimeDiagnosticsConfig config, DiagnosticLogRecord[] records,
			string gamePath, string coreProfile, bool useLazyLoading, SaveLogOperationTrail operationTrail = null)
		{
			var sb = new StringBuilder(records.Length * 200 + 1024);
			sb.AppendLine("# gEmuera diagnostic log");
			sb.AppendLine("# SessionId=" + DiagnosticLogRouter.SessionId);
			sb.AppendLine("# Platform=" + OS.GetName()
				+ " DebugBuild=" + OS.IsDebugBuild()
				+ " VerboseLogBuild=" + IsVerboseBuild()
				+ " ActiveDebugModel=" + (config?.ActiveDebugModel ?? "default")
				+ " Language=" + (config?.GetActiveDebugModel()?.Language ?? "zh_cn"));
			sb.AppendLine("# RuntimeLevel=" + (config?.GetRuntimeLogLevel().ToString().ToUpperInvariant() ?? "ERROR")
				+ " Categories=" + FormatActiveCategories(config));
			sb.AppendLine("# RingBuffer=" + DiagnosticLogSinks.RingCapacity
				+ " Records=" + records.Length
				+ " Overwritten=" + DiagnosticLogSinks.OverwrittenTotal);
			sb.AppendLine("# DroppedTotal=" + DiagnosticLogRouter.GetDroppedTotal());
			// 企业级说明：诊断日志默认写入本次启动的游戏目录，报告头保留实际目录，便于 APK/桌面排障后精确找文件。
			sb.AppendLine("# GameDirectoryActual=" + GetCurrentGameDirectoryForDisplay());
			sb.AppendLine("# DiagnosticsPathActual=" + ResolvePathForDisplay(GameDirectoryPathPrefix));
			sb.AppendLine("# GamePath=" + (gamePath ?? ""));
			sb.AppendLine("# CoreProfile=" + (coreProfile ?? "")
				+ " UseLazyLoading=" + useLazyLoading);
			sb.AppendLine("# SchemaVersion=" + (config?.LoggingSchemaVersion ?? 1));
			sb.AppendLine("# HowToRead=grep event_id=..., category=..., level=ERROR");
			sb.AppendLine();
			if (operationTrail != null)
			{
				sb.Append(operationTrail.BuildExportText(5));
				sb.AppendLine();
			}
			sb.AppendLine("# gdprint ring buffer records");
			foreach (var r in records)
				sb.AppendLine(r.FormatForExport());
			return sb.ToString();
		}

		static string BuildConfigSnapshot(RuntimeDiagnosticsConfig config)
		{
			if (config == null)
				return "# config null\n";
			var sb = new StringBuilder(2048);
			sb.AppendLine("# config snapshot");
			sb.AppendLine("active_debug_model = \"" + config.ActiveDebugModel + "\"");
			sb.AppendLine("[migration]");
			sb.AppendLine("session_isolation = " + config.MigrationSessionIsolationEnabled.ToString().ToLowerInvariant());
			sb.AppendLine("[quick_debug]");
			sb.AppendLine("enabled = " + config.QuickDebugEnabled.ToString().ToLowerInvariant());
			sb.AppendLine("preset = \"" + config.QuickDebugPreset + "\"");
			sb.AppendLine("effective_preset = \"" + config.QuickDebugEffectivePreset + "\"");
			sb.AppendLine("language = \"" + config.QuickDebugLanguage + "\"");
			sb.AppendLine("effective_language = \"" + config.QuickDebugEffectiveLanguage + "\"");
			sb.AppendLine("apk_safe = " + config.QuickDebugApkSafe.ToString().ToLowerInvariant());
			sb.AppendLine("[quick_debug.modules]");
			sb.AppendLine("touch = " + config.QuickModules.Touch.ToString().ToLowerInvariant());
			sb.AppendLine("input = " + config.QuickModules.Input.ToString().ToLowerInvariant());
			sb.AppendLine("image = " + config.QuickModules.Image.ToString().ToLowerInvariant());
			sb.AppendLine("ui_layout = " + config.QuickModules.UiLayout.ToString().ToLowerInvariant());
			sb.AppendLine("resource = " + config.QuickModules.Resource.ToString().ToLowerInvariant());
			sb.AppendLine("load_save = " + config.QuickModules.LoadSave.ToString().ToLowerInvariant());
			sb.AppendLine("android_storage = " + config.QuickModules.AndroidStorage.ToString().ToLowerInvariant());
			sb.AppendLine("performance_sampling = " + config.QuickModules.PerformanceSampling.ToString().ToLowerInvariant());
			sb.AppendLine("snapshot = " + config.QuickModules.Snapshot.ToString().ToLowerInvariant());
			sb.AppendLine("input_replay = " + config.QuickModules.InputReplay.ToString().ToLowerInvariant());
			sb.AppendLine("[logging]");
			sb.AppendLine("level = \"" + config.LoggingLevel + "\"");
			sb.AppendLine("mirror_non_error_to_godot = " + config.LoggingMirrorNonErrorToGodot.ToString().ToLowerInvariant());
			sb.AppendLine("diagnostic_ring_capacity = " + config.LoggingDiagnosticRingCapacity);
			sb.AppendLine("max_message_chars = " + config.LoggingMaxMessageChars);
			sb.AppendLine("schema_version = " + config.LoggingSchemaVersion);
			sb.AppendLine("[logging.categories]");
			sb.AppendLine("general = " + config.Categories.General.ToString().ToLowerInvariant());
			sb.AppendLine("touch = " + config.Categories.Touch.ToString().ToLowerInvariant());
			sb.AppendLine("statement_recognition = " + config.Categories.StatementRecognition.ToString().ToLowerInvariant());
			sb.AppendLine("[debug.touch]");
			sb.AppendLine("enabled = " + config.TouchEnabled.ToString().ToLowerInvariant());
			sb.AppendLine("[debug.input]");
			sb.AppendLine("enabled = " + config.InputDebugEnabled.ToString().ToLowerInvariant());
			sb.AppendLine("[debug.image]");
			sb.AppendLine("enabled = " + config.ImageDebugEnabled.ToString().ToLowerInvariant());
			sb.AppendLine("[debug.ui_layout]");
			sb.AppendLine("enabled = " + config.UiLayoutEnabled.ToString().ToLowerInvariant());
			return sb.ToString();
		}

		static string BuildDeviceInfo()
		{
			var sb = new StringBuilder(256);
			sb.AppendLine("# device info");
			sb.AppendLine("os_name=" + OS.GetName());
			sb.AppendLine("os_model=" + OS.GetModelName());
			sb.AppendLine("locale=" + OS.GetLocale());
			sb.AppendLine("processor_count=" + OS.GetProcessorCount());
			sb.AppendLine("device_id_hash=" + HashDeviceId(OS.GetUniqueId()));
			return sb.ToString();
		}

		static string BuildScreenInfo()
		{
			var sb = new StringBuilder(256);
			sb.AppendLine("# screen info");
			Vector2 size = DisplayServer.ScreenGetSize();
			sb.AppendLine("screen_size=" + size.X + "x" + size.Y);
			sb.AppendLine("dpi=" + DisplayServer.ScreenGetDpi());
			sb.AppendLine("scale=" + DisplayServer.ScreenGetScale());
			sb.AppendLine("window_size=" + DisplayServer.WindowGetSize());
			return sb.ToString();
		}

		static string BuildGodotInfo()
		{
			var sb = new StringBuilder(256);
			sb.AppendLine("# godot info");
			sb.AppendLine("engine_version=" + Engine.GetVersionInfo()["string"].AsString());
			sb.AppendLine("is_debug_build=" + OS.IsDebugBuild());
			sb.AppendLine("is_userfs_persistent=" + OS.IsUserfsPersistent());
			sb.AppendLine("static_memory=" + OS.GetStaticMemoryUsage());
			sb.AppendLine("static_memory_peak=" + OS.GetStaticMemoryPeakUsage());
			return sb.ToString();
		}

		static string BuildApkInfo()
		{
			// 企业级说明：APK 信息只记录平台、特性和路径摘要，不读取 APK/PCK 二进制内容。
			// 诊断包导出应保持轻量，避免在 Android 手机上因为排障动作本身产生额外 I/O 峰值。
			var sb = new StringBuilder(384);
			sb.AppendLine("# apk info");
			sb.AppendLine("os_name=" + OS.GetName());
			sb.AppendLine("debug_build=" + OS.IsDebugBuild());
			sb.AppendLine("feature_android=" + OS.HasFeature("android"));
			sb.AppendLine("feature_mobile=" + OS.HasFeature("mobile"));
			sb.AppendLine("feature_editor=" + OS.HasFeature("editor"));
			sb.AppendLine("executable_path=" + DiagnosticLogRouter.RedactPath(OS.GetExecutablePath()));
			sb.AppendLine("res_path=" + DiagnosticLogRouter.RedactPath(ProjectSettings.GlobalizePath("res://")));
			// 企业级说明：这里额外输出未脱敏的游戏目录，目标是让测试人员能在设备文件系统中准确定位导出结果。
			sb.AppendLine("game_directory_actual=" + GetCurrentGameDirectoryForDisplay());
			sb.AppendLine("diagnostics_path_actual=" + ResolvePathForDisplay(GameDirectoryPathPrefix));
			return sb.ToString();
		}

		static string BuildGameInfo(string gamePath, string coreProfile, bool useLazyLoading)
		{
			var sb = new StringBuilder(256);
			sb.AppendLine("# game info");
			sb.AppendLine("game_path=" + (gamePath ?? ""));
			sb.AppendLine("core_profile=" + (coreProfile ?? ""));
			sb.AppendLine("use_lazy_loading=" + useLazyLoading);
			sb.AppendLine("last_event_id=" + (_lastEventId ?? ""));
			sb.AppendLine("error_count=" + _errorCount);
			return sb.ToString();
		}

		static string BuildSummary(DiagnosticLogRecord[] records, RuntimeDiagnosticsConfig config)
		{
			var sb = new StringBuilder(1024);
			sb.AppendLine("# diagnostic summary");
			sb.AppendLine("records=" + records.Length);
			sb.AppendLine("dropped_total=" + DiagnosticLogRouter.GetDroppedTotal());
			sb.AppendLine("overwritten=" + DiagnosticLogSinks.OverwrittenTotal);

			var eventCounts = new Dictionary<string, int>();
			var categoryCounts = new Dictionary<string, int>();
			var imageFailures = new List<(string eventId, string data)>();
			var uiMismatches = new List<(string kind, string source, int line, string delta)>();
			foreach (var r in records)
			{
				string eid = r.EventId ?? "";
				string cat = r.Category.ToString();
				eventCounts[eid] = eventCounts.GetValueOrDefault(eid) + 1;
				categoryCounts[cat] = categoryCounts.GetValueOrDefault(cat) + 1;
				if (eid.StartsWith("IMAGE.", StringComparison.Ordinal)
					&& (eid.Contains("FAIL", StringComparison.Ordinal) || r.Data.Contains("failure_kind=", StringComparison.Ordinal)))
				{
					imageFailures.Add((eid, r.Data));
				}
				if (eid == "UI_LAYOUT.MISMATCH")
					uiMismatches.Add((ExtractKind(r.Data), r.Source, r.Line, ExtractDelta(r.Data)));
			}

			int topEventIds = config?.DiagnosticSummaryTopEventIds ?? 20;
			sb.AppendLine("# top event_ids");
			foreach (var kv in eventCounts.OrderByDescending(kv => kv.Value).Take(topEventIds))
				sb.AppendLine("event_id=" + kv.Key + " count=" + kv.Value);

			sb.AppendLine("# category counts");
			foreach (var kv in categoryCounts.OrderByDescending(kv => kv.Value))
				sb.AppendLine("category=" + kv.Key + " count=" + kv.Value);

			int topMismatches = config?.DiagnosticSummaryTopUiLayoutMismatches ?? 20;
			sb.AppendLine("# top ui_layout mismatches");
			foreach (var g in uiMismatches.GroupBy(m => m.kind).OrderByDescending(g => g.Count()).Take(topMismatches))
				sb.AppendLine("kind=" + g.Key + " count=" + g.Count());
			foreach (var g in uiMismatches.GroupBy(m => m.source).OrderByDescending(g => g.Count()).Take(topMismatches))
				sb.AppendLine("source=" + g.Key + " count=" + g.Count());
			foreach (var g in uiMismatches.GroupBy(m => m.line).OrderByDescending(g => g.Count()).Take(topMismatches))
				sb.AppendLine("line_no=" + g.Key + " count=" + g.Count());
			foreach (var m in uiMismatches.Take(topMismatches))
				sb.AppendLine("mismatch kind=" + m.kind + " source=" + m.source + " line_no=" + m.line + " delta=" + m.delta);

			int topImageFailures = config?.DiagnosticSummaryTopImageFailures ?? 20;
			sb.AppendLine("# top image failures");
			foreach (var g in imageFailures.GroupBy(f => f.eventId).OrderByDescending(g => g.Count()).Take(topImageFailures))
				sb.AppendLine("event_id=" + g.Key + " count=" + g.Count());
			foreach (var g in imageFailures.GroupBy(f => ExtractDataValue(f.data, "failure_kind")).OrderByDescending(g => g.Count()).Take(topImageFailures))
				sb.AppendLine("failure_kind=" + g.Key + " count=" + g.Count());

			var droppedSummary = DiagnosticLogRouter.GetDroppedSummary();
			if (droppedSummary.Count > 0)
			{
				sb.AppendLine("# dropped by event_id");
				foreach (var kv in droppedSummary.OrderByDescending(kv => kv.Value).Take(topEventIds))
					sb.AppendLine("event_id=" + kv.Key + " dropped=" + kv.Value);
			}
			var droppedCategorySummary = DiagnosticLogRouter.GetDroppedCategorySummary();
			if (droppedCategorySummary.Count > 0)
			{
				sb.AppendLine("# dropped by category");
				foreach (var kv in droppedCategorySummary.OrderByDescending(kv => kv.Value).Take(topEventIds))
					sb.AppendLine("category=" + kv.Key + " dropped=" + kv.Value);
			}

			return sb.ToString();
		}

		static void AddSnapshotEntries(RuntimeDiagnosticsConfig config, DiagnosticLogRecord[] records, Action<string, string> addEntry)
		{
			// 企业级说明：snapshot 只在用户主动导出诊断包时从 ring buffer 派生文本快照。
			// 当前实现不遍历或修改 Godot UI 树，避免为了排障快照影响 APK 正常布局和触摸输入。
			int maxRecords = Math.Max(1, config?.SnapshotMaxNodes ?? 512);
			if (config?.SnapshotIncludeScreenshot ?? false)
			{
				addEntry("screen-capture.txt", "SNAPSHOT.SCREEN.FAIL reason=screenshot_capture_not_enabled_in_safe_export_path\n");
				WriteInfrastructureRecord(EmueraLogLevel.Warn, EmueraLogCategory.UI,
					"SNAPSHOT.SCREEN.FAIL", "screen capture skipped",
					"reason=screenshot_capture_not_enabled_in_safe_export_path");
			}

			if ((config?.SnapshotIncludeLayoutSnapshot ?? false) || (config?.SnapshotIncludeVisibleUiRects ?? true))
			{
				var sb = new StringBuilder(4096);
				sb.AppendLine("# layout snapshot derived from UI_LAYOUT records");
				foreach (var r in records.Where(r => r.EventId.StartsWith("UI_LAYOUT.", StringComparison.Ordinal)).Take(maxRecords))
					sb.AppendLine(r.FormatForExport());
				addEntry("layout-snapshot.txt", LimitTextBytes(sb.ToString(), config?.SnapshotMaxFileKb ?? 1024));
				WriteInfrastructureRecord(EmueraLogLevel.Info, EmueraLogCategory.UI,
					"SNAPSHOT.LAYOUT.OK", "layout snapshot exported",
					"records=" + Math.Min(maxRecords, records.Count(r => r.EventId.StartsWith("UI_LAYOUT.", StringComparison.Ordinal))));
			}

			if (config?.SnapshotIncludeImageRects ?? true)
			{
				var sb = new StringBuilder(4096);
				sb.AppendLine("# image rect snapshot derived from IMAGE/UI_LAYOUT image records");
				foreach (var r in records.Where(r =>
					r.EventId.StartsWith("IMAGE.", StringComparison.Ordinal)
					|| (r.EventId.StartsWith("UI_LAYOUT.", StringComparison.Ordinal) && r.Data.Contains("kind=image", StringComparison.Ordinal))).Take(maxRecords))
				{
					sb.AppendLine(r.FormatForExport());
				}
				addEntry("image-rects.txt", LimitTextBytes(sb.ToString(), config?.SnapshotMaxFileKb ?? 1024));
			}
		}

		static string ExtractKind(string data)
		{
			if (string.IsNullOrEmpty(data))
				return "";
			foreach (var part in data.Split(' '))
			{
				if (part.StartsWith("kind=", StringComparison.Ordinal))
					return part.Substring(5);
			}
			return "";
		}

		static string ExtractDelta(string data)
		{
			if (string.IsNullOrEmpty(data))
				return "";
			foreach (var part in data.Split(' '))
			{
				if (part.StartsWith("delta=", StringComparison.Ordinal))
					return part.Substring(6);
			}
			return "";
		}

		static string ExtractDataValue(string data, string key)
		{
			if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(key))
				return "";
			string prefix = key + "=";
			foreach (var part in data.Split(' '))
			{
				if (part.StartsWith(prefix, StringComparison.Ordinal))
					return part.Substring(prefix.Length);
			}
			return "";
		}

		static string LimitTextBytes(string text, int maxKb)
		{
			if (string.IsNullOrEmpty(text))
				return "";
			int maxBytes = Math.Max(1, maxKb) * 1024;
			if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
				return text;
			var sb = new StringBuilder(text.Length);
			int bytes = 0;
			foreach (char c in text)
			{
				int charBytes = Encoding.UTF8.GetByteCount(new[] { c });
				if (bytes + charBytes > maxBytes)
					break;
				sb.Append(c);
				bytes += charBytes;
			}
			sb.AppendLine();
			sb.AppendLine("SNAPSHOT.LAYOUT.TRUNCATED");
			return sb.ToString();
		}

		static string FormatActiveCategories(RuntimeDiagnosticsConfig config)
		{
			if (config == null)
				return "All";
			var mask = config.GetActiveDebugModelCategoryMask();
			if (mask == EmueraLogCategory.None)
				return "None";
			var parts = new List<string>();
			if ((mask & EmueraLogCategory.General) != 0) parts.Add("General");
			if ((mask & EmueraLogCategory.Sprite) != 0) parts.Add("Sprite");
			if ((mask & EmueraLogCategory.Audio) != 0) parts.Add("Audio");
			if ((mask & EmueraLogCategory.Input) != 0) parts.Add("Input");
			if ((mask & EmueraLogCategory.Script) != 0) parts.Add("Script");
			if ((mask & EmueraLogCategory.UI) != 0) parts.Add("UI");
			if ((mask & EmueraLogCategory.FileSystem) != 0) parts.Add("FileSystem");
			if ((mask & EmueraLogCategory.Load) != 0) parts.Add("Load");
			if ((mask & EmueraLogCategory.Save) != 0) parts.Add("Save");
			if ((mask & EmueraLogCategory.Config) != 0) parts.Add("Config");
			if ((mask & EmueraLogCategory.Performance) != 0) parts.Add("Performance");
			if ((mask & EmueraLogCategory.Touch) != 0) parts.Add("Touch");
			if ((mask & EmueraLogCategory.StatementRecognition) != 0) parts.Add("StatementRecognition");
			return string.Join(",", parts);
		}

		static string HashDeviceId(string rawId)
		{
			if (string.IsNullOrEmpty(rawId))
				return "unknown";
			// 用 session 盐值做简单混合哈希，同一 session 内稳定，跨 session 不可追踪原始设备 ID。
			string salted = rawId + "::" + (DiagnosticLogRouter.SessionId ?? "");
			using (var sha = System.Security.Cryptography.SHA256.Create())
			{
				var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(salted));
				var sb = new StringBuilder(16);
				for (int i = 0; i < 8; i++)
					sb.Append(bytes[i].ToString("x2"));
				return sb.ToString();
			}
		}

		static bool IsVerboseBuild()
		{
#if DEBUG || GEMUERA_DIAGNOSTIC_LOGS
			return true;
#else
			return false;
#endif
		}

		static string NormalizeDiagnosticsPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return GameDirectoryPathPrefix;

			string normalized = path.Trim().Replace('\\', '/');
			const string legacyUserDiagnostics = "user://diagnostics";
			if (normalized.StartsWith(legacyUserDiagnostics, StringComparison.OrdinalIgnoreCase))
			{
				string suffix = normalized.Substring(legacyUserDiagnostics.Length).TrimStart('/');
				return string.IsNullOrEmpty(suffix)
					? GameDirectoryPathPrefix
					: GameDirectoryPathPrefix + suffix;
			}
			return normalized;
		}

		static string NormalizeGameDirectory(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return "";
			try
			{
				string normalized = path.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
				return Path.GetFullPath(normalized).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			}
			catch
			{
				return path.Trim().TrimEnd('/', '\\');
			}
		}

		static bool TryGetCurrentGameDirectory(out string gameDirectory, out string reason)
		{
			gameDirectory = "";
			reason = "";
			string current = NormalizeGameDirectory(_lastGamePath);
			if (string.IsNullOrWhiteSpace(current))
			{
				reason = GameDirectoryUnavailableReason;
				return false;
			}
			gameDirectory = current;
			return true;
		}

		static bool TryBuildGameDirectoryPath(string normalizedPath, string defaultFileName, out string absolutePath, out string reason)
		{
			absolutePath = "";
			reason = "";
			if (!TryGetCurrentGameDirectory(out string gameRoot, out reason))
				return false;

			string suffix = normalizedPath.StartsWith(GameDirectoryPathPrefix, StringComparison.OrdinalIgnoreCase)
				? normalizedPath.Substring(GameDirectoryPathPrefix.Length)
				: normalizedPath;
			suffix = suffix.TrimStart('/', '\\');

			if (!string.IsNullOrEmpty(defaultFileName)
				&& (string.IsNullOrEmpty(suffix)
					|| suffix.EndsWith("/", StringComparison.Ordinal)
					|| suffix.EndsWith("\\", StringComparison.Ordinal)))
				suffix += defaultFileName;

			string candidate = string.IsNullOrEmpty(suffix)
				? gameRoot
				: Path.Combine(gameRoot, suffix.Replace('/', Path.DirectorySeparatorChar));
			candidate = Path.GetFullPath(candidate);

			// 企业级说明：game:// 只能落在本次启动的游戏目录内。即使 TOML 被手动写入 ../，
			// 也不能把诊断文件写出游戏目录，避免 APK 实机排障时污染其它游戏或系统目录。
			string rootWithSlash = gameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			string candidateWithSlash = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			bool isRoot = string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
				gameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
				StringComparison.OrdinalIgnoreCase);
			if (!isRoot && !candidateWithSlash.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
			{
				reason = "outside_game_directory candidate=" + candidate + " game=" + gameRoot;
				return false;
			}

			absolutePath = candidate;
			return true;
		}

		static bool TryResolveDiagnosticsDirectory(string configuredPath, out string absolutePath, out string reason)
		{
			absolutePath = "";
			reason = "";
			string normalized = NormalizeDiagnosticsPath(configuredPath);
			if (normalized.StartsWith(GameDirectoryPathPrefix, StringComparison.OrdinalIgnoreCase))
				return TryBuildGameDirectoryPath(normalized, "", out absolutePath, out reason);
			if (normalized.Contains("://", StringComparison.Ordinal))
			{
				reason = "unsupported_diagnostics_path scheme=" + normalized;
				return false;
			}

			if (!TryGetCurrentGameDirectory(out string gameRoot, out reason))
				return false;
			// 企业级说明：无协议相对路径也视为相对启动游戏目录，避免运行时面板手填 logs/
			// 时落到 Godot 进程工作目录；绝对路径仍必须通过下方游戏目录边界校验。
			string candidate = Path.IsPathRooted(normalized)
				? Path.GetFullPath(normalized)
				: Path.GetFullPath(Path.Combine(gameRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
			string rootWithSlash = gameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			string candidateWithSlash = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			bool isRoot = string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
				gameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
				StringComparison.OrdinalIgnoreCase);
			if (!isRoot && !candidateWithSlash.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
			{
				reason = "outside_game_directory candidate=" + candidate + " game=" + gameRoot;
				return false;
			}
			absolutePath = candidate;
			return true;
		}

		static bool TryResolveDiagnosticsFilePath(string configuredPath, string defaultFileName, out string absolutePath, out string reason)
		{
			absolutePath = "";
			reason = "";
			string normalized = NormalizeDiagnosticsPath(configuredPath);
			if (string.IsNullOrWhiteSpace(normalized))
				normalized = GameDirectoryPathPrefix + defaultFileName;
			if (normalized.StartsWith(GameDirectoryPathPrefix, StringComparison.OrdinalIgnoreCase))
				return TryBuildGameDirectoryPath(normalized, defaultFileName, out absolutePath, out reason);
			if (normalized.Contains("://", StringComparison.Ordinal))
			{
				reason = "unsupported_diagnostics_path scheme=" + normalized;
				return false;
			}

			if (!TryGetCurrentGameDirectory(out string gameRoot, out reason))
				return false;
			if (normalized.EndsWith("/", StringComparison.Ordinal) || normalized.EndsWith("\\", StringComparison.Ordinal))
				normalized += defaultFileName;
			// 企业级说明：无协议文件名同样落在启动游戏目录下，例如 gemuera.log 会解析为
			// <启动游戏目录>/gemuera.log；绝对路径仅在位于当前游戏目录内部时允许写入。
			string candidate = Path.IsPathRooted(normalized)
				? Path.GetFullPath(normalized)
				: Path.GetFullPath(Path.Combine(gameRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
			string rootWithSlash = gameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			if (!candidate.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
			{
				// 企业级说明：单独日志导出和 breadcrumb 同样会写盘，必须限制在启动游戏目录内。
				// 这样即使运行时面板或配置被误填成外部存储根目录，也不会写入其它游戏、存档或任意系统路径。
				reason = "outside_game_directory candidate=" + candidate + " game=" + gameRoot;
				return false;
			}

			absolutePath = candidate;
			return true;
		}

		static string Clip(string text, int maxChars)
		{
			if (string.IsNullOrEmpty(text))
				return "";
			text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
			if (text.Length <= maxChars)
				return text;
			return text.Substring(0, maxChars) + "...";
		}

	}
}
