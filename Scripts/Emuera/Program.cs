using System;
//using System.Drawing;
using System.Collections.Generic;
//using System.Windows.Forms;
using System.Globalization;
using MinorShift._Library;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData.Expression;
using System.IO;
using uEmuera;
using uEmuera.Drawing;
using uEmuera.Forms;
using uEmuera.Window;
using GEmuera.Core.Compatibility;
using MinorShift.Emuera.Compatibility;

namespace MinorShift.Emuera
{
	public enum EmueraCoreProfile
	{
		V24Pure,
		Snake,
		EraFl,
		// megaten：eraMegaten 适配（eraFL 镜像），仅新增枚举值，不改既有分支。
		Megaten,
		SnakeModernMobile,
	}

	public static class Program
	{
		// Legacy runner 的日志落点和兼容计划只由测试/会话宿主配置；普通启动保持现有游戏目录行为。
		static string m0RunnerStartupErrorLogPath = "";
		static string m0RunnerDefaultOutputLogPath = "";
		static CompatibilityPlan m1CompatibilityPlan;
		static LegacyCompatibilityProfile m1CompatibilityProfile;
		static readonly LegacyCompatibilityProfile defaultCompatibilityProfile =
			LegacyCompatibilityProfile.CreateForProfile("v24pure", scopedVariableInstructionsEnabled: true);
		/*
		コードの開始地点。
		ここでMainWindowを作り、
		MainWindowがProcessを作り、
		ProcessがGameBase・ConstantData・Variableを作る。
		
		
		*.ERBの読み込み、実行、その他の処理をProcessが、
		入出力をMainWindowが、
		定数の保存をConstantDataが、
		変数の管理をVariableが行う。
		 
		と言う予定だったが改変するうちに境界が曖昧になってしまった。
		 
		後にEmueraConsoleを追加し、それに入出力を担当させることに。
		
		1750 DebugConsole追加
		 Debugを全て切り離すことはできないので一部EmueraConsoleにも担当させる
		
		TODO: 1819 MainWindow & Consoleの入力・表示組とProcess&Dataのデータ処理組だけでも分離したい

		*/
		/// <summary>
		/// アプリケーションのメイン エントリ ポイントです。
		/// </summary>
		//[STAThread]
		public static void Main(string[] args)
		{

			ExeDir = Sys.ExeDir;
			var boundPlan = CurrentCompatibilityPlan;
			if (boundPlan == null)
			{
				var detectedProfile = DetectCoreProfile();
				ConfigureCompatibilityPlan(BuiltInDialectCatalog.CreateLegacySessionPlan(
					GetCompatibilityProfileId(detectedProfile)));
				boundPlan = CurrentCompatibilityPlan;
			}
#if UEMUERA_DEBUG
			//debugMode = true;

			//ExeDirにバリアントのパスを代入することでテスト実行するためのコード。
			//ローカルパスの末尾には\必須。
			//ローカルパスを記載した場合は頒布前に削除すること。
			ExeDir = @"";

#endif
			WorkingDir = ExeDir;
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
			GenericUtils.Info($"[LOAD] ExeDir={ExeDir}");
			GenericUtils.Info($"[LOAD] CoreProfile={CoreProfile}");
			ResetSnakeStartupErrorLog();
			try
			{
				MinorShift.Emuera.Runtime.Utils.SqliteRuntime.EnsureInitialized();
				GenericUtils.Info("[LOAD] SQLite runtime initialized");
			}
			catch (Exception ex)
			{
				GenericUtils.Warn("[LOAD] SQLite runtime initialization deferred: "
					+ MinorShift.Emuera.Runtime.Utils.SqliteRuntime.FormatException(ex));
			}
			ConfigureModernMobileCoreAdapters();
			CsvDir = ExeDir + "csv/";
			if (!uEmuera.Utils.DirectoryExists(CsvDir)){
				CsvDir = ExeDir + "CSV/";
			}
			CsvDir = uEmuera.Utils.ResolveExistingDirectoryPath(CsvDir);
			ErbDir = ExeDir + "erb/";
			if (!uEmuera.Utils.DirectoryExists(ErbDir)){
				ErbDir = ExeDir + "ERB/";
			}
			ErbDir = uEmuera.Utils.ResolveExistingDirectoryPath(ErbDir);
			DebugDir = ExeDir + "debug/";
			if (!uEmuera.Utils.DirectoryExists(DebugDir)){
				DebugDir = ExeDir + "DEBUG/";
			}
			DatDir = ExeDir + "dat/";
			if (!uEmuera.Utils.DirectoryExists(DatDir)){
				DatDir = ExeDir + "DAT/";
			}
			ContentDir = ExeDir + "resources/";
			if (!uEmuera.Utils.DirectoryExists(ContentDir)){
				ContentDir = ExeDir + "RESOURCES/";
			}
			ContentDir = uEmuera.Utils.ResolveExistingDirectoryPath(ContentDir);
			GenericUtils.Info($"[LOAD] CsvDir={CsvDir}, ErbDir={ErbDir}");
			//エラー出力用
			//1815 .exeが東方板のNGワードに引っかかるそうなので除去
			//ExeName = Path.GetFileNameWithoutExtension(Sys.ExeName);

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			ConfigData.Instance.LoadConfig();
			JSONConfig.Load(ConfigData.Instance);
			// VARI/VARS changes the parser-visible instruction surface. Recompose
			// the legacy projection only after both configuration layers have been
			// loaded, while retaining the same immutable Core module plan.
			ConfigureCompatibilityPlan(boundPlan, Config.UseScopedVariableInstruction);
			ApplyAndroidWindowWidthPolicy();
			global::FrameRateHelper.ApplyConfigFps();
			//二重起動の禁止かつ二重起動
			//if ((!Config.AllowMultipleInstances) && (Sys.PrevInstance()))
			//{
			//	MessageBox.Show("多重起動を許可する場合、emuera.configを書き換えて下さい", "既に起動しています");
			//	return;
			//}
			if (!uEmuera.Utils.DirectoryExists(CsvDir))
			{
				MessageBox.Show("\"" + CsvDir + "\" csvフォルダが見つかりません", "フォルダなし");
				return;
			}
			if (!uEmuera.Utils.DirectoryExists(ErbDir))
			{
				MessageBox.Show("\"" + ErbDir + "\" erbフォルダが見つかりません", "フォルダなし");
				return;
			}
			int argsStart = 0;
			if ((args.Length > 0)&&(args[0].Equals("-DEBUG", StringComparison.CurrentCultureIgnoreCase)))
			{
				argsStart = 1;//デバッグモードかつ解析モード時に最初の1っこ(-DEBUG)を飛ばす
				debugMode = true;
			}
			if(debugMode)
			{
				ConfigData.Instance.LoadDebugConfig();
				if (!uEmuera.Utils.DirectoryExists(DebugDir))
				{
					try
					{
						uEmuera.Utils.CreateDirectory(DebugDir);
					}
					catch
					{
						MessageBox.Show("debugフォルダの作成に失敗しました", "フォルダなし");
						return;
					}
				}
				// launcher（user://launcher.cfg debug_show_window）覆盖 debug.config 的
				// DebugShowWindow，避免宿主手写解析 debug.config。
				if (DebugShowWindowOverride is bool showWindow)
				{
					ConfigData.Instance.GetDebugItem(ConfigCode.DebugShowWindow)?.SetValue(showWindow);
					Config.SetDebugConfig(ConfigData.Instance);
				}
			}
			if (args.Length > argsStart)
			{
				AnalysisFiles = new List<string>();
				for (int i = argsStart; i < args.Length; i++)
				{
					if (!File.Exists(args[i]) && !Directory.Exists(args[i]))
					{
						MessageBox.Show("与えられたファイル・フォルダは存在しません");
						return;
					}
					if ((File.GetAttributes(args[i]) & FileAttributes.Directory) == FileAttributes.Directory)
					{
						List<KeyValuePair<string, string>> fnames = Config.GetFiles(args[i] + "\\", "*.ERB");
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
						fnames.AddRange(Config.GetFiles(args[i] + "\\", "*.erb"));
#endif
						for(int j = 0; j < fnames.Count; j++)
						{
							AnalysisFiles.Add(fnames[j].Value);
						}
					}
					else
					{
						if (Path.GetExtension(args[i]).ToUpper() != ".ERB")
						{
							MessageBox.Show("ドロップ可能なファイルはERBファイルのみです");
							return;
						}
						AnalysisFiles.Add(args[i]);
					}
				}
				AnalysisMode = true;
			}
			MainWindow win = null;


			//while (true)
			//{
				StartTime = WinmmTimer.TickCount;
				//using (win = new MainWindow())
				//{
					win = new MainWindow();
					Application.Run(win);
				//	Content.AppContents.UnloadContents();
				//	if (!Reboot)
				//		break;

				//	RebootWinState = win.WindowState;
				//	if (win.WindowState == FormWindowState.Normal)
				//	{
				//		RebootClientY = win.ClientSize.Height;
				//		RebootLocation = win.Location;
				//	}
				//	else
				//	{
				//		RebootClientY = 0;
				//		RebootLocation = new Point();
				//	}
				//}
				////条件次第ではParserMediatorが空でない状態で再起動になる場合がある
				//ParserMediator.ClearWarningList();
				//ParserMediator.Initialize(null);
				//GlobalStatic.Reset();
				////GC.Collect();
				//Reboot = false;
				//ConfigData.Instance.LoadConfig();
			//}
		}

		/// <summary>
		/// 実行ファイルのディレクトリ。最後に\を付けたstring
		/// </summary>
		public static string ExeDir { get; private set; }
		public static string WorkingDir { get; private set; }
		public static string CsvDir { get; private set; }
		public static string ErbDir { get; private set; }
		public static string DebugDir { get; private set; }
		public static string DatDir { get; private set; }
		public static string ContentDir { get; private set; }
		public static string ExeName { get; private set; }

		public static bool Reboot = false;
		//public static int RebootClientX = 0;
		public static int RebootClientY = 0;
		public static FormWindowState RebootWinState = FormWindowState.Normal;
		public static Point RebootLocation;

		public static bool AnalysisMode = false;
		public static List<string> AnalysisFiles = null;

		public static bool debugMode = false;
		public static bool DebugMode { get { return debugMode; } }
		/// <summary>
		/// launcher（user://launcher.cfg [launcher] debug_show_window）对 DebugShowWindow
		/// 的覆盖值；null 表示不覆盖（沿用 debug.config）。由 EmueraThread.Work 在
		/// Program.Main 之前从宿主透传，仅 DebugMode 分支内消费。
		/// </summary>
		public static bool? DebugShowWindowOverride;
		public static EmueraCoreProfile CoreProfile
		{
			get { return ResolveCompatibilityProfile(Compatibility.ProfileId); }
		}
		public static CompatibilityPlan CurrentCompatibilityPlan
		{
			get { return System.Threading.Volatile.Read(ref m1CompatibilityPlan); }
		}
		internal static LegacyCompatibilityProfile Compatibility
		{
			get
			{
				return System.Threading.Volatile.Read(ref m1CompatibilityProfile)
					?? defaultCompatibilityProfile;
			}
		}
		public static bool IsSnakeProfile
		{
			get { return Compatibility.Snake.IsEnabled; }
		}
		public static bool IsEraFlProfile
		{
			get { return Compatibility.EraFl.IsEnabled; }
		}
		public static bool SupportsLazyLoading { get { return true; } }
		public static bool IsSnakeModernMobileProfile { get { return CoreProfile == EmueraCoreProfile.SnakeModernMobile; } }

		/// <summary>
		/// 兼容计划在启动 legacy 引擎前绑定，避免异步会话运行期间切换方言配置。
		/// </summary>
		internal static void ConfigureCompatibilityPlan(CompatibilityPlan plan)
		{
			// This early binding occurs before the game configuration is available.
			// Program.Main rebuilds the profile with the loaded value before any
			// IdentifierDictionary or parser registry is constructed.
			ConfigureCompatibilityPlan(plan, scopedVariableInstructionsEnabled: true);
		}

		internal static void ConfigureCompatibilityPlan(
			CompatibilityPlan plan,
			bool scopedVariableInstructionsEnabled)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));

			LegacyCompatibilityProfile profile = LegacyCompatibilityProfile.Create(
				plan,
				scopedVariableInstructionsEnabled);
			var existing = System.Threading.Volatile.Read(ref m1CompatibilityPlan);
			if (existing != null && !string.Equals(existing.CanonicalHash, plan.CanonicalHash, StringComparison.Ordinal))
				throw new InvalidOperationException("A different compatibility plan is already bound to the active legacy session.");

			System.Threading.Volatile.Write(ref m1CompatibilityPlan, plan);
			System.Threading.Volatile.Write(ref m1CompatibilityProfile, profile);
		}

		internal static void ClearCompatibilityPlan(CompatibilityPlan plan)
		{
			if (plan == null)
				return;
			var existing = System.Threading.Volatile.Read(ref m1CompatibilityPlan);
			if (existing != null && string.Equals(existing.CanonicalHash, plan.CanonicalHash, StringComparison.Ordinal))
			{
				System.Threading.Volatile.Write(ref m1CompatibilityPlan, null);
				System.Threading.Volatile.Write(ref m1CompatibilityProfile, null);
			}
		}

		/// <summary>
		/// The legacy VM is process-wide, so its compatibility context must be
		/// released after the worker has stopped and before another launcher
		/// selection can bind a new immutable plan.
		/// </summary>
		internal static void ClearCompatibilityPlan()
		{
			System.Threading.Volatile.Write(ref m1CompatibilityPlan, null);
			System.Threading.Volatile.Write(ref m1CompatibilityProfile, null);
		}

		private static void ApplyAndroidWindowWidthPolicy()
		{
			if (Godot.OS.GetName() != "Android")
				return;

			int safeWidth = global::EmueraContent.ContentSafeWidth;
			int viewportWidth = global::EmueraContent.ContentWidth;
			if (safeWidth <= 0)
				safeWidth = viewportWidth;
			if (safeWidth <= 0)
			{
				viewportWidth = Godot.DisplayServer.WindowGetSize().X;
				safeWidth = viewportWidth;
			}
			if (safeWidth > 0 && System.Math.Abs(Config.WindowX - safeWidth) > 1)
			{
				int previousWidth = Config.WindowX;
				Config.UpdateWindowWidth(System.Math.Max(320, safeWidth));
				GenericUtils.Info($"[LOAD] Android dynamic window width: {previousWidth} -> {Config.WindowX}, safe={safeWidth}, viewport={viewportWidth}");
				return;
			}
			GenericUtils.Info($"[LOAD] Android keeps configured window width: {Config.WindowX}, safe={safeWidth}, viewport={viewportWidth}");
		}

		internal static void ConfigureLegacyRunnerStartupErrorLogPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
				throw new ArgumentException("Legacy runner startup error log path must be absolute.", nameof(path));

			string normalized = Path.GetFullPath(path);
			string directory = Path.GetDirectoryName(normalized);
			if (string.IsNullOrEmpty(directory))
				throw new ArgumentException("Legacy runner startup error log path must have a directory.", nameof(path));

			Directory.CreateDirectory(directory);
			System.Threading.Volatile.Write(ref m0RunnerStartupErrorLogPath, normalized);
		}

		internal static void ConfigureLegacyRunnerDefaultOutputLogPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
				throw new ArgumentException("Legacy runner default output log path must be absolute.", nameof(path));

			string normalized = Path.GetFullPath(path);
			string directory = Path.GetDirectoryName(normalized);
			if (string.IsNullOrEmpty(directory))
				throw new ArgumentException("Legacy runner default output log path must have a directory.", nameof(path));

			Directory.CreateDirectory(directory);
			File.WriteAllText(normalized, string.Empty);
			System.Threading.Volatile.Write(ref m0RunnerDefaultOutputLogPath, normalized);
		}

		internal static void ResetSessionState()
		{
			ExeDir = null;
			WorkingDir = null;
			CsvDir = null;
			ErbDir = null;
			DebugDir = null;
			DatDir = null;
			ContentDir = null;
			ExeName = null;
			Reboot = false;
			RebootClientY = 0;
			RebootWinState = FormWindowState.Normal;
			RebootLocation = Point.Empty;
			AnalysisMode = false;
			AnalysisFiles?.Clear();
			AnalysisFiles = null;
			debugMode = false;
			DebugShowWindowOverride = null;
			System.Threading.Volatile.Write(ref m1CompatibilityPlan, null);
			System.Threading.Volatile.Write(ref m1CompatibilityProfile, null);
			StartTime = 0;
		}

		internal static bool TryResolveLegacyRunnerDefaultOutputLogPath(string requestedPath, out string outputPath)
		{
			outputPath = "";
			string runnerPath = System.Threading.Volatile.Read(ref m0RunnerDefaultOutputLogPath);
			if (string.IsNullOrEmpty(runnerPath))
				return false;

			if (string.IsNullOrEmpty(requestedPath))
			{
				outputPath = runnerPath;
				return true;
			}

			if (string.IsNullOrEmpty(ExeDir))
				return false;

			try
			{
				string legacyDefaultPath = Path.GetFullPath(Path.Combine(ExeDir, "emuera.log"));
				string candidatePath = Path.IsPathRooted(requestedPath)
					? Path.GetFullPath(requestedPath)
					: Path.GetFullPath(Path.Combine(ExeDir, requestedPath));
				if (!string.Equals(candidatePath, legacyDefaultPath, StringComparison.OrdinalIgnoreCase))
					return false;

				outputPath = runnerPath;
				return true;
			}
			catch
			{
				return false;
			}
		}

		public static void AppendSnakeStartupErrorLog(string text)
		{
			if (!IsSnakeProfile || string.IsNullOrEmpty(text))
				return;

			try
			{
				string logPath = GetSnakeStartupErrorLogPath();
				if (!string.IsNullOrEmpty(logPath))
					File.AppendAllText(logPath, text + Environment.NewLine);
			}
			catch
			{
			}
		}

		public static void AppendSnakeStartupErrorLog(IEnumerable<string> lines)
		{
			if (!IsSnakeProfile || lines == null)
				return;

			try
			{
				var pending = new List<string>();
				foreach (string line in lines)
				{
					if (!string.IsNullOrEmpty(line))
						pending.Add(line);
				}
				string logPath = GetSnakeStartupErrorLogPath();
				if (pending.Count != 0 && !string.IsNullOrEmpty(logPath))
					File.AppendAllLines(logPath, pending);
			}
			catch
			{
			}
		}

		private static void ResetSnakeStartupErrorLog()
		{
			if (!IsSnakeProfile)
				return;

			try
			{
				string logPath = GetSnakeStartupErrorLogPath();
				if (!string.IsNullOrEmpty(logPath))
					File.WriteAllText(logPath,
						"Snake startup errors: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
			}
			catch
			{
			}
		}

		private static string GetSnakeStartupErrorLogPath()
		{
			string runnerPath = System.Threading.Volatile.Read(ref m0RunnerStartupErrorLogPath);
			if (!string.IsNullOrEmpty(runnerPath))
				return runnerPath;
			return string.IsNullOrEmpty(ExeDir)
				? ""
				: Path.Combine(ExeDir, "emuera_startup_errors.log");
		}

		private static void ConfigureModernMobileCoreAdapters()
		{
			if (!IsSnakeModernMobileProfile)
				return;

			try
			{
				string userRoot = Godot.OS.GetUserDataDir();
				if (string.IsNullOrEmpty(userRoot))
					userRoot = Godot.ProjectSettings.GlobalizePath("user://");
				Modern.Script.Functions.ModernSqlManager.StorageDirectory = Path.Combine(userRoot, "modern_sql");
				GenericUtils.Info($"[LOAD] Modern SQL dir={Modern.Script.Functions.ModernSqlManager.StorageDirectory}");
			}
			catch (Exception ex)
			{
				GenericUtils.Warn($"[LOAD] Failed to configure modern mobile adapters: {ex.Message}");
			}
		}


		public static uint StartTime { get; private set; }

		private static EmueraCoreProfile DetectCoreProfile()
		{
			string launcherProfile = global::FirstWindow.SelectedCoreProfileName;
			return launcherProfile switch
			{
				global::FirstWindow.CoreProfileV24Pure => EmueraCoreProfile.V24Pure,
				global::FirstWindow.CoreProfileSnake => EmueraCoreProfile.Snake,
				global::FirstWindow.CoreProfileEraFl => EmueraCoreProfile.EraFl,
				// megaten：launcher 显式选择时映射到新枚举值。
				global::FirstWindow.CoreProfileMegaten => EmueraCoreProfile.Megaten,
				_ => throw new InvalidOperationException(
					$"Compatibility profile '{launcherProfile}' is not supported by the legacy bridge.")
			};
		}

		private static EmueraCoreProfile ResolveCompatibilityProfile(string profileId)
		{
			return profileId switch
			{
				"v24pure" => EmueraCoreProfile.V24Pure,
				"snake" => EmueraCoreProfile.Snake,
				"erafl" => EmueraCoreProfile.EraFl,
				// megaten：会话 plan 的 profile id 映射（compat\megaten 路由共用）。
				"megaten" => EmueraCoreProfile.Megaten,
				_ => throw new InvalidOperationException(
					$"Compatibility plan profile '{profileId}' is not supported by the legacy bridge.")
			};
		}

		private static string GetCompatibilityProfileId(EmueraCoreProfile profile)
		{
			return profile switch
			{
				EmueraCoreProfile.V24Pure => "v24pure",
				EmueraCoreProfile.Snake => "snake",
				EmueraCoreProfile.EraFl => "erafl",
				// megaten：枚举值 → 内置兼容计划 profile id。
				EmueraCoreProfile.Megaten => "megaten",
				_ => throw new InvalidOperationException(
					$"Legacy core profile '{profile}' has no built-in compatibility plan."),
			};
		}

	}
}
