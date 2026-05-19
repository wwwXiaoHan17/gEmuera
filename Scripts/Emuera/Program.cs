using System;
//using System.Drawing;
using System.Collections.Generic;
//using System.Windows.Forms;
using MinorShift._Library;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData.Expression;
using System.IO;
using uEmuera;
using uEmuera.Drawing;
using uEmuera.Forms;
using uEmuera.Window;

namespace MinorShift.Emuera
{
	public enum EmueraCoreProfile
	{
		V24Pure,
		Snake,
		SnakeModernMobile,
	}

	public static class Program
	{
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
			CoreProfile = DetectCoreProfile(ExeDir);
#if UEMUERA_DEBUG
			//debugMode = true;

			//ExeDirにバリアントのパスを代入することでテスト実行するためのコード。
			//ローカルパスの末尾には\必須。
			//ローカルパスを記載した場合は頒布前に削除すること。
			ExeDir = @"";

#endif
			WorkingDir = ExeDir;
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
		public static EmueraCoreProfile CoreProfile { get; private set; } = EmueraCoreProfile.V24Pure;
		public static bool IsSnakeProfile
		{
			get { return CoreProfile == EmueraCoreProfile.Snake || CoreProfile == EmueraCoreProfile.SnakeModernMobile; }
		}
		public static bool IsMegatenProfile
		{
			get { return IsMegatenGameDirectory(ExeDir); }
		}
		public static bool SupportsLazyLoading { get { return true; } }
		public static bool IsSnakeModernMobileProfile { get { return CoreProfile == EmueraCoreProfile.SnakeModernMobile; } }

		public static void AppendSnakeStartupErrorLog(string text)
		{
			if (!IsSnakeProfile || string.IsNullOrEmpty(ExeDir) || string.IsNullOrEmpty(text))
				return;

			try
			{
				File.AppendAllText(Path.Combine(ExeDir, "emuera_startup_errors.log"), text + Environment.NewLine);
			}
			catch
			{
			}
		}

		public static void AppendSnakeStartupErrorLog(IEnumerable<string> lines)
		{
			if (!IsSnakeProfile || string.IsNullOrEmpty(ExeDir) || lines == null)
				return;

			try
			{
				var pending = new List<string>();
				foreach (string line in lines)
				{
					if (!string.IsNullOrEmpty(line))
						pending.Add(line);
				}
				if (pending.Count != 0)
					File.AppendAllLines(Path.Combine(ExeDir, "emuera_startup_errors.log"), pending);
			}
			catch
			{
			}
		}

		private static void ResetSnakeStartupErrorLog()
		{
			if (!IsSnakeProfile || string.IsNullOrEmpty(ExeDir))
				return;

			try
			{
				File.WriteAllText(Path.Combine(ExeDir, "emuera_startup_errors.log"),
					"Snake startup errors: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
			}
			catch
			{
			}
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

		private static EmueraCoreProfile DetectCoreProfile(string exeDir)
		{
			if (string.IsNullOrEmpty(exeDir))
				return EmueraCoreProfile.V24Pure;

			string launcherProfile = global::FirstWindow.SelectedCoreProfileName;
			if (string.Equals(launcherProfile, global::FirstWindow.CoreProfileSnake, StringComparison.OrdinalIgnoreCase))
				return EmueraCoreProfile.Snake;

			if (IsModernSnakeCoreRequested(exeDir))
				return EmueraCoreProfile.SnakeModernMobile;
			if (IsLegacySnakeCoreRequested(exeDir))
				return EmueraCoreProfile.Snake;

			return EmueraCoreProfile.V24Pure;
		}

		private static bool IsLegacySnakeCoreRequested(string exeDir)
		{
			try
			{
				string normalized = uEmuera.Utils.NormalizePath(exeDir);
				return uEmuera.Utils.FileExists(Path.Combine(normalized, "snake_core.txt"))
					|| uEmuera.Utils.FileExists(Path.Combine(normalized, "legacy_snake_core.txt"));
			}
			catch
			{
			}
			return false;
		}

		private static bool IsModernSnakeCoreRequested(string exeDir)
		{
			try
			{
				string normalized = uEmuera.Utils.NormalizePath(exeDir);
				if (uEmuera.Utils.FileExists(Path.Combine(normalized, "modern_core.txt"))
					|| uEmuera.Utils.FileExists(Path.Combine(normalized, "snake_modern_core.txt")))
					return true;
			}
			catch
			{
			}
			return false;
		}

		private static bool IsMegatenGameDirectory(string exeDir)
		{
			if (string.IsNullOrEmpty(exeDir))
				return false;
			string normalized = uEmuera.Utils.NormalizePath(exeDir);
			string name = Path.GetFileName(normalized.TrimEnd('/', '\\'));
			return !string.IsNullOrEmpty(name)
				&& (name.IndexOf("MGT", StringComparison.OrdinalIgnoreCase) >= 0
					|| name.IndexOf("Megaten", StringComparison.OrdinalIgnoreCase) >= 0);
		}

	}
}
