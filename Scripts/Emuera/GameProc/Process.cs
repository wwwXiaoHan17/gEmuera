using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
//using System.Windows.Forms;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.GameData.Function;
using System.Linq;
using System.Threading.Tasks;
using uEmuera.Forms;
using MinorShift.Emuera.Runtime.Utils.PluginSystem;
using System.Diagnostics;

namespace MinorShift.Emuera.GameProc
{

	internal sealed partial class Process
	{
		public Process(EmueraConsole view)
		{
			console = view;
		}

        public LogicalLine getCurrentLine { get { return state.CurrentLine; } }

		/// <summary>
		/// @~~と$~~を集めたもの。CALL命令などで使う
		/// 実行順序はLogicalLine自身が保持する。
		/// </summary>
		LabelDictionary labelDic;
		public LabelDictionary LabelDictionary { get { return labelDic; } }

		/// <summary>
		/// 変数全部。スクリプト中で必要になる変数は（ユーザーが直接触れないものも含め）この中にいれる
		/// </summary>
		private VariableEvaluator vEvaluator;
		public VariableEvaluator VEvaluator { get { return vEvaluator; } }
		private ExpressionMediator exm;
		private GameBase gamebase;
		readonly EmueraConsole console;
		private IdentifierDictionary idDic;
		ProcessState state;
		public ProcessState State { get { return state; } }
		ProcessState originalState;//リセットする時のために
        bool noError = false;
        //色々あって復活させてみる
        bool initialiing;
        public bool inInitializeing { get { return initialiing;  } }

		public bool Initialize()
		{
			return InitializeAsync().GetAwaiter().GetResult();
		}

		public async Task<bool> InitializeAsync()
		{
			LexicalAnalyzer.UseMacro = false;
            state = new ProcessState(console);
            originalState = state;
            initialiing = true;
            Stopwatch loadStopwatch = Stopwatch.StartNew();
            void MarkLoad(string stage)
            {
                long ms = loadStopwatch.ElapsedMilliseconds;
                if (Config.DisplayReport)
                    GenericUtils.Info($"[LOADTIME] {stage}: {ms}ms");
                // 始终落盘（Release APK 也生效）：定位 30k 图片加载耗时构成，避免盲优化。
                try
                {
                    string dir = Program.DebugDir;
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                        File.AppendAllText(Path.Combine(dir, "load_times.log"),
                            $"[LOADTIME] {stage}: {ms}ms\n");
                    }
                }
                catch { }
            }
			try
			{
				ParserMediator.Initialize(console);
				ParserMediator.BindCompatibilityPlan(Program.CurrentCompatibilityPlan);
				if (ParserMediator.CurrentCompatibilityPlan != null)
				{
					// capability id 是方言模块的差异账本（如 erafl 的 markup.div-v2.v1），
					// 在加载日志落一份，让真机诊断与无头 fixture 能直接核对会话能力面。
					var capabilityPlan = ParserMediator.CurrentCompatibilityPlan;
					GenericUtils.Info(
						capabilityPlan.CapabilityIds.Count == 0
							? $"[LOAD] CompatibilityPlan={capabilityPlan.CanonicalHash}"
							: $"[LOAD] CompatibilityPlan={capabilityPlan.CanonicalHash} Capabilities={string.Join(',', capabilityPlan.CapabilityIds)}");
				}
				Preload.Clear();
				Preload.Load(Program.CsvDir);
				Preload.Load(Program.ErbDir, !Config.UseLazyLoading);
				MarkLoad("preload");
				//コンフィグファイルに関するエラーの処理（コンフィグファイルはこの関数に入る前に読込済み）
				if (ParserMediator.HasWarning)
				{
					ParserMediator.FlushWarningList();
					if(MessageBox.Show("コンフィグファイルに異常があります\nEmueraを終了しますか","コンフィグエラー", MessageBoxButtons.YesNo)
						== DialogResult.Yes)
					{
						console.PrintSystemLine("コンフィグファイルに異常があり、終了が選択されたため処理を終了しました");
						return false;
					}
				}
				//リソースフォルダ読み込み
				if (!Content.AppContents.LoadContents())
				{
					ParserMediator.FlushWarningList();
					console.PrintSystemLine("リソースフォルダ読み込み中に異常が発見されたため処理を終了します");
					return false;
				}
				MarkLoad("resources");
				ParserMediator.FlushWarningList();
				//キーマクロ読み込み
                if (Config.UseKeyMacro && !Program.AnalysisMode)
                {
                    if (uEmuera.Utils.FileExists(Program.ExeDir + "macro.txt"))
                    {
                        if (Config.DisplayReport)
							console.PrintSystemLine("macro.txt読み込み中・・・");
                        KeyMacro.LoadMacroFile(Program.ExeDir + "macro.txt");
                    }
				}
				//_replace.csv読み込み
                if (Config.UseReplaceFile && !Program.AnalysisMode)
                {
					if (uEmuera.Utils.FileExists(Program.CsvDir + "_Replace.csv"))
					{
						if (Config.DisplayReport)
							console.PrintSystemLine("_Replace.csv読み込み中・・・");
						ConfigData.Instance.LoadReplaceFile(Program.CsvDir + "_Replace.csv");
						if (ParserMediator.HasWarning)
						{
							ParserMediator.FlushWarningList();
							if (MessageBox.Show("_Replace.csvに異常があります\nEmueraを終了しますか", "_Replace.csvエラー", MessageBoxButtons.YesNo)
								== DialogResult.Yes)
							{
								console.PrintSystemLine("_Replace.csvに異常があり、終了が選択されたため処理を終了しました");
								return false;
							}
						}
					}
                }
                Config.SetReplace(ConfigData.Instance);
                //ここでBARを設定すれば、いいことに気づいた予感
                console.setStBar(Config.DrawLineString);

				//_rename.csv読み込み
				if (Config.UseRenameFile)
                {
					if (uEmuera.Utils.FileExists(Program.CsvDir + "_Rename.csv"))
                    {
                        if (Config.DisplayReport || Program.AnalysisMode)
							console.PrintSystemLine("_Rename.csv読み込み中・・・");
						ParserMediator.LoadEraExRenameFile(Program.CsvDir + "_Rename.csv");
                    }
                    else
                        console.PrintError("csv\\_Rename.csvが見つかりません");
                }
                if (!Config.DisplayReport)
                {
                    console.PrintSingleLine(Config.LoadLabel);
                    console.RefreshStrings(true);
				}
				//gamebase.csv読み込み
				gamebase = new GameBase();
				if (!gamebase.LoadGameBaseCsv(Program.CsvDir + "GAMEBASE.CSV"))
                {
					ParserMediator.FlushWarningList();
                    console.PrintSystemLine("GAMEBASE.CSVの読み込み中に問題が発生したため処理を終了しました");
                    return false;
                }
				GenericUtils.Info($"[LOAD] GAMEBASE loaded: {gamebase.ScriptWindowTitle}");
				MarkLoad("gamebase");
				console.SetWindowTitle(gamebase.ScriptWindowTitle);
				GlobalStatic.GameBaseData = gamebase;

				//前記以外のcsvを全て読み込み
				ConstantData constant = new ConstantData();
				constant.LoadData(Program.CsvDir, console, Config.DisplayReport);
				MarkLoad("csv");
				GlobalStatic.ConstantData = constant;
				TrainName = constant.GetCsvNameList(VariableCode.TRAINNAME);

                Int64? m0RunnerRandomSeed = null;
                if (global::gEmuera.LegacyRunner.LegacyRunnerDeterminism.TryGetRandomSeed(out int configuredRandomSeed))
                    m0RunnerRandomSeed = configuredRandomSeed;
                vEvaluator = new VariableEvaluator(gamebase, constant, m0RunnerRandomSeed);
				GlobalStatic.VEvaluator = vEvaluator;

				idDic = new IdentifierDictionary(vEvaluator.VariableData);
				GlobalStatic.IdentifierDictionary = idDic;
				if (Program.CurrentCompatibilityPlan != null)
				{
					ParserMediator.ConsumeCompatibilityPlan(
						idDic.GetLegacyInstructionNames(),
						idDic.GetLegacyFunctionNames());
					idDic.BindCompatibilityPlan(Program.CurrentCompatibilityPlan);
				}

				StrForm.Initialize();
				VariableParser.Initialize();

				exm = new ExpressionMediator(this, vEvaluator, console);
				GlobalStatic.EMediator = exm;

				labelDic = new LabelDictionary();
				GlobalStatic.LabelDictionary = labelDic;
				HeaderFileLoader hLoader = new HeaderFileLoader(console, idDic, this);

				LexicalAnalyzer.UseMacro = false;

				PluginManager.GetInstance().SetParent(this, state, exm);
				PluginManager.GetInstance().LoadPlugins();
				if (GlobalStatic.ExistPlugin && Config.PluginAvailableWarn)
					console.PrintSingleLine("注意：外部プラグイン機能が有効になっています。この機能で生じた不具合等はEmueraのサポート対象外となります");

				//ERH読込
				GenericUtils.Info($"[LOAD] Loading ERH from: {Program.ErbDir}");
				if (!hLoader.LoadHeaderFiles(Program.ErbDir, Config.DisplayReport))
				{
					ParserMediator.FlushWarningList();
					console.PrintSystemLine("ERHの読み込み中にエラーが発生したため処理を終了しました");
					GenericUtils.Error("[LOAD] ERH loading failed");
					return false;
				}
				GenericUtils.Info("[LOAD] ERH loaded OK");
				MarkLoad("erh");
				LexicalAnalyzer.UseMacro = idDic.UseMacro();

				//TODO:ユーザー定義変数用のcsvの適用

				//ERB読込
				GenericUtils.Info("[LOAD] Loading ERB files...");
				ErbLoader loader = new ErbLoader(console, exm, this);
                if (Program.AnalysisMode)
                    noError = await loader.LoadErbsAsync(Program.AnalysisFiles, labelDic);
                else
                    noError = await loader.LoadErbFilesAsync(Program.ErbDir, Config.DisplayReport, labelDic, Config.UseLazyLoading && Program.SupportsLazyLoading);
				GenericUtils.Info($"[LOAD] ERB loaded, noError={noError}");
				MarkLoad("erb");
                initSystemProcess();
                initialiing = false;
            }
			catch (Exception e)
			{
				GenericUtils.Error($"[LOAD] Fatal exception: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                handleException(e, null, true);
				console.PrintSystemLine("初期化中に致命的なエラーが発生したため処理を終了しました");
				return false;
			}
			if (labelDic == null)
			{
				return false;
			}
			GenericUtils.Info("[LOAD] Initialization complete, starting TITLE");
			MarkLoad("total-before-title");
			// 图片尺寸磁盘缓存：启动期已读取的图片头尺寸落盘，二次启动免读头。
			uEmuera.Drawing.ImageSizeCache.Save();
			state.Begin(BeginType.TITLE);
			GC.Collect();
            return true;
		}

		public void ReloadErb()
		{
			ReloadErbAsync().GetAwaiter().GetResult();
		}

		public async Task ReloadErbAsync()
		{
			saveCurrentState(false);
			state.SystemState = SystemStateCode.System_Reloaderb;
			// 会话中途 reload 必须重新枚举 ERB 目录：目录快照缓存（启动期 M4 优化）
			// 若不清除会看不到新增/删除的 .ERB 文件。
			uEmuera.Utils.InvalidateRecursiveDirListing(Program.ErbDir);
			ErbLoader loader = new ErbLoader(console, exm, this);
            await loader.LoadErbFilesAsync(Program.ErbDir, false, labelDic, Config.UseLazyLoading && Program.SupportsLazyLoading);
			console.ReadAnyKey();
		}

		public void ReloadPartialErb(List<string> path)
		{
			ReloadPartialErbAsync(path).GetAwaiter().GetResult();
		}

		public async Task ReloadPartialErbAsync(List<string> path)
		{
			saveCurrentState(false);
			state.SystemState = SystemStateCode.System_Reloaderb;
			ErbLoader loader = new ErbLoader(console, exm, this);
			await loader.LoadErbsAsync(path, labelDic);
			console.ReadAnyKey();
		}

		public void SetCommnds(Int64 count)
		{
			coms = new List<long>((int)count);
			isCTrain = true;
			var selectcom = vEvaluator.SELECTCOM_ARRAY;
			if (count >= selectcom.Length)
			{
				throw new CodeEE("CALLTRAIN命令の引数の値がSELECTCOMの要素数を超えています");
			}
			for (int i = 0; i < (int)count; i++)
			{
				coms.Add(selectcom[i + 1]);
			}
		}

        public bool ClearCommands()
        {
            coms.Clear();
            count = 0;
            isCTrain = false;
            skipPrint = true;
            return (callFunction("CALLTRAINEND", false, false));
        }

		public void InputResult5(int r0, int r1, int r2, int r3, int r4, long r5)
		{
			var result = vEvaluator.RESULT_ARRAY;
			result[0] = r0;
			result[1] = r1;
			result[2] = r2;
			result[3] = r3;
			result[4] = r4;
			result[5] = r5;
		}
		public void InputInteger(Int64 i)
		{
			GlobalStatic.ctrlZ.Add(i.ToString());
			vEvaluator.RESULT = i;
		}
		public void InputInteger(Int64 index, Int64 i)
		{
			if (GlobalStatic.ctrlZ != null)
				GlobalStatic.ctrlZ.Add(i.ToString());
			if (index >= 0 && index < vEvaluator.RESULT_ARRAY.Length)
				vEvaluator.RESULT_ARRAY[index] = i;
		}
		public void InputSystemInteger(Int64 i)
		{
			GlobalStatic.ctrlZ.Add(i.ToString());
			systemResult = i;
		}
		public void InputString(string s)
		{
			GlobalStatic.ctrlZ.Add(s);
			vEvaluator.RESULTS = s;
		}
		public void InputStringWithPointerMetadata(string s)
		{
			// eraFL 的 INPUTS ,1 兼容：多数界面仍读取 RESULTS:0，战斗技能则读取 RESULTS:1。
			// 两个槽位必须在一次提交中同步写入，并且 ctrl-Z 输入历史只能记录一次。
			GlobalStatic.ctrlZ.Add(s);
			vEvaluator.RESULTS = s;
			if (vEvaluator.RESULTS_ARRAY.Length > 1)
				vEvaluator.RESULTS_ARRAY[1] = s;
		}
		public void InputString(long idx, string i)
		{
			if (GlobalStatic.ctrlZ != null)
				GlobalStatic.ctrlZ.Add(i);
			if (idx < vEvaluator.RESULTS_ARRAY.Length)
				vEvaluator.RESULTS_ARRAY[idx] = i;
		}

		private uint startTime = 0;
		
		public void DoScript()
		{
			startTime = _Library.WinmmTimer.TickCount;
			state.lineCount = 0;
			bool systemProcRunning = true;
			while (true)
			{
				try
				{
					methodStack = 0;
					systemProcRunning = true;
					while (state.ScriptEnd && console.IsRunning)
						runSystemProc();
					if (!console.IsRunning)
						break;
					systemProcRunning = false;
					runScriptProc();
				}
				catch (Exception ec)
				{
					LogicalLine currentLine = state.ErrorLine;
					if (currentLine != null && currentLine is NullLine)
						currentLine = null;
					if (state.InBeforeError)
					{
						LogicalLine errorLine = state.PendingErrorCurrentLine;
						if (errorLine != null && errorLine is NullLine)
							errorLine = null;
						if (errorLine == null)
							errorLine = currentLine;
						if (state.PendingErrorSystemProc)
							handleExceptionInSystemProc(ec, errorLine, true);
						else
							handleException(ec, errorLine, true);
						state.ClearFunctionListPreserveTrace();
						return;
					}
					if (state.SkipBeforeError)
					{
						state.SkipBeforeError = false;
						LogicalLine throwLine = state.PendingThrowLine ?? currentLine;
						state.PendingThrowLine = null;
						if (systemProcRunning)
							handleExceptionInSystemProc(ec, throwLine, true);
						else
							handleException(ec, throwLine, true);
						state.ClearFunctionListPreserveTrace();
						return;
					}
					if (state.InBeforeThrow)
					{
						state.InBeforeThrow = false;
						string throwMessage = state.PendingThrowMessage ?? ec.Message;
						LogicalLine throwLine = state.PendingThrowLine ?? currentLine;
						state.PendingThrowMessage = null;
						state.PendingThrowLine = null;
						var throwException = new CodeEE(throwMessage);
						if (systemProcRunning)
							handleExceptionInSystemProc(throwException, throwLine, true);
						else
							handleException(throwException, throwLine, true);
						state.ClearFunctionListPreserveTrace();
						return;
					}
					// BEFORE_ERROR 事件机制为 snake 独有；v24 参考侧异常直接停机处理
					if (Program.Compatibility.Snake.IsEnabled)
					{
						state.InBeforeError = true;
						var beforeError = Config.DisableBeforeErrorThrow
							? null
							: CalledFunction.CallEventFunction(this, "BEFORE_ERROR", null);
						if (beforeError != null)
						{
							state.IntoFunction(beforeError, null, null);
							state.PendingErrorException = ec;
							state.PendingErrorCurrentLine = currentLine;
							state.PendingErrorSystemProc = systemProcRunning;
							continue;
						}
						state.InBeforeError = false;
					}
					if (systemProcRunning)
						handleExceptionInSystemProc(ec, currentLine, true);
					else
						handleException(ec, currentLine, true);
					return;
				}
			}
		}
		
		public void BeginTitle()
		{
			vEvaluator.ResetData();
			state = originalState;
			state.Begin(BeginType.TITLE);
		}

		public void UpdateCheckInfiniteLoopState()
		{
			startTime = _Library.WinmmTimer.TickCount;
			state.lineCount = 0;
		}

		private void checkInfiniteLoop()
		{
			//うまく動かない。BEEP音が鳴るのを止められないのでこの処理なかったことに（1.51）
			////フリーズ防止。処理中でも履歴を見たりできる
			//System.Windows.Forms.Application.DoEvents();
			////System.Threading.Thread.Sleep(0);

			//if (!console.Enabled)
			//{
			//    //DoEvents()の間にウインドウが閉じられたらおしまい。
			//    console.ReadAnyKey();
			//    return;
			//}
			uint time = _Library.WinmmTimer.TickCount - startTime;
			if (time < Config.InfiniteLoopAlertTime)
				return;
			LogicalLine currentLine = state.CurrentLine;
			if ((currentLine == null) || (currentLine is NullLine))
				return;//現在の行が特殊な状態ならスルー
			if (!console.Enabled)
				return;//クローズしてるとMessageBox.Showができないので。
			string caption = string.Format("無限ループの可能性があります");
			string text = string.Format(
				"現在、{0}の{1}行目を実行中です。\n最後の入力から{3}ミリ秒経過し{2}行が実行されました。\n処理を中断し強制終了しますか？",
				currentLine.Position.Filename, currentLine.Position.LineNo, state.lineCount, time);
			DialogResult result = MessageBox.Show(text, caption, MessageBoxButtons.YesNo);
			if (result == DialogResult.Yes)
			{
				throw new CodeEE("無限ループの疑いにより強制終了が選択されました");
			}
			else
			{
				state.lineCount = 0;
				startTime = _Library.WinmmTimer.TickCount;
			}
		}

		int methodStack = 0;
		public SingleTerm GetValue(SuperUserDefinedMethodTerm udmt)
		{
			methodStack++;
            if (methodStack > 100)
            {
                //StackOverflowExceptionはcatchできない上に再現性がないので発生前に一定数で打ち切る。
                //環境によっては100以前にStackOverflowExceptionがでるかも？
                throw new CodeEE("関数の呼び出しスタックが溢れました(無限に再帰呼び出しされていませんか？)");
            }
			SingleTerm ret = null;
            int temp_current = state.currentMin;
            state.currentMin = state.functionCount;
			// UserDefinedMethodTerm 会被表达式树缓存，CalledFunction 只能作为模板复用。
			// v24/Snake 参考实现均直接复用模板（只 updateRetAddress），不每次 Clone。
			// 但 GetValue 重入（递归/嵌套的同函数表达式）会改写模板的可变字段
			// （returnAddress / VariadicArgCount / EraFlQuestStartLookup / IsJump），
			// 因此进入前保存、退出后恢复，确保外层帧与下一次调用从一致状态开始。
			CalledFunction call = udmt.Call;
			LogicalLine savedReturnAddress = call.ReturnAddress;
			bool savedIsJump = call.IsJump;
			int savedVariadicArgCount = call.VariadicArgCount;
			EraFlQuestStartLookupContext savedEraFlLookup = call.EraFlQuestStartLookup;
            call.updateRetAddress(state.CurrentLine);
			bool success = false;
			var savedState = state.CaptureCallState();
            try
            {
				// fallthrough や隠れた早期リターンで前回の RETURNF 値が残らないよう事前にクリア
			state.MethodReturnValue = null;
			state.IntoFunction(call, udmt.Argument, exm);
                //do whileの中でthrow されたエラーはここではキャッチされない。
				//#functionを全て抜けてDoScriptでキャッチされる。
    			runScriptProc();
                ret = state.MethodReturnValue;
				success = true;
			}
			finally
			{
				if (success)
				{
					if (call.TopLabel.hasPrivDynamicVar)
						call.TopLabel.Out();
					// RETURNF 已在 ProcessState.ReturnF() 中移除当前函数帧。
					// 这里不能再 PopContext，否则会误弹父调用栈，导致后续 LOCAL/ARG 和返回流程错乱。
					state.CurrentLine = savedState.currentLine;
				}
				else
				{
					state.RollbackToState(savedState.funcCount, savedState.ctxCount, savedState.currentLine);
				}
				// 恢复模板可变字段（成功与失败路径都要恢复）
				call.updateRetAddress(savedReturnAddress);
				call.IsJump = savedIsJump;
				call.VariadicArgCount = savedVariadicArgCount;
				call.EraFlQuestStartLookup = savedEraFlLookup;
                //1756beta2+v3:こいつらはここにないとデバッグコンソールで式中関数が事故った時に大事故になる
                state.currentMin = temp_current;
                methodStack--;
            }
			return ret;
		}

        public void clearMethodStack()
        {
            methodStack = 0;
        }

        public int MethodStack()
        {
            return methodStack;
        }

		public ScriptPosition GetRunningPosition()
		{
			LogicalLine line = state.ErrorLine;
			if (line == null)
				return null;
			return line.Position;
		}
/*
		private readonly string scaningScope = null;
		private string GetScaningScope()
		{
			if (scaningScope != null)
				return scaningScope;
			return state.Scope;
		}
*/
		public LogicalLine scaningLine = null;
		internal LogicalLine GetScaningLine()
		{
			if (scaningLine != null)
				return scaningLine;
			LogicalLine line = state.ErrorLine;
			if (line == null)
				return null;
			return line;
		}
		
		
		private void handleExceptionInSystemProc(Exception exc, LogicalLine current, bool playSound)
		{
			console.ThrowError(playSound);
			if (exc is CodeEE)
			{
				console.PrintError("関数の終端でエラーが発生しました:" + Program.ExeName);
				console.PrintError(exc.Message);
			}
			else if (exc is ExeEE)
			{
				console.PrintError("関数の終端でEmueraのエラーが発生しました:" + Program.ExeName);
				console.PrintError(exc.Message);
			}
			else
			{
				console.PrintError("関数の終端で予期しないエラーが発生しました:" + Program.ExeName);
				console.PrintError(exc.GetType().ToString() + ":" + exc.Message);
				string[] stack = exc.StackTrace.Split('\n');
				for (int i = 0; i < stack.Length; i++)
				{
					console.PrintError(stack[i]);
				}
			}
		}
		
		private void handleException(Exception exc, LogicalLine current, bool playSound)
		{
            uEmuera.Logger.Error(exc, EmueraLogCategory.Script);

			console.ThrowError(playSound);
			ScriptPosition position = null;
            if ((exc is EmueraException ee) && (ee.Position != null))
                position = ee.Position;
            else if ((current != null) && (current.Position != null))
				position = current.Position;
			string posString = "";
			if (position != null)
			{
				if (position.LineNo >= 0)
					posString = position.Filename + "の" + position.LineNo.ToString() + "行目で";
				else
					posString = position.Filename + "で";
					
			}
			if (exc is CodeEE)
			{
                if (position != null)
				{
                    if (current is InstructionLine procline && procline.FunctionCode == FunctionCode.THROW)
                    {
                        console.PrintErrorButton(posString + "THROWが発生しました", position);
                        printRawLine(position);
                        console.PrintError("THROW内容：" + exc.Message);
                    }
                    else
                    {
                        // 参考侧错误尾串使用引擎版本文本（AssemblyData.EmueraVersionText），而非 exe 名
                        console.PrintErrorButton(posString + "エラーが発生しました:" + GlobalStatic.MainWindow.EmueraVerText, position);
						printRawLine(position);
						console.PrintError("エラー内容：" + exc.Message);
                    }
                    console.PrintError("現在の関数：@" + current.ParentLabelLine.LabelName + "（" + current.ParentLabelLine.Position.Filename + "の" + current.ParentLabelLine.Position.LineNo.ToString() + "行目）");
                    console.PrintError("関数呼び出しスタック：");
                    LogicalLine parent;
                    int depth = 0;
                    while ((parent = state.GetReturnAddressSequensial(depth++)) != null)
                    {
                        if (parent.Position != null)
                        {
                            console.PrintErrorButton("↑" + parent.Position.Filename + "の" + parent.Position.LineNo.ToString() + "行目（関数@" + parent.ParentLabelLine.LabelName + "内）", parent.Position);
                        }
                    } 
				}
				else
				{
					console.PrintError(posString + "エラーが発生しました:" + Program.ExeName);
					console.PrintError(exc.Message);
				}
			}
			else if (exc is ExeEE)
			{
				console.PrintError(posString + "Emueraのエラーが発生しました:" + Program.ExeName);
				console.PrintError(exc.Message);
			}
			else
            {
				console.PrintError(posString + "予期しないエラーが発生しました:" + Program.ExeName);
				console.PrintError(exc.GetType().ToString() + ":" + exc.Message);
				string[] stack = exc.StackTrace.Split('\n');
				for (int i = 0; i < stack.Length; i++)
				{
					console.PrintError(stack[i]);
				}
			}
		}

		public void printRawLine(ScriptPosition position)
		{
			string str = getRawTextFormFilewithLine(position);
			if (str != "")
				console.PrintError(str);
		}

		public string getRawTextFormFilewithLine(ScriptPosition position)
        {
			string extents = position.Filename.Substring(position.Filename.Length - 4).ToLower();
			if (extents == ".erb")
			{
				return uEmuera.Utils.FileExists(Program.ErbDir + position.Filename)
					? position.LineNo > 0 ? uEmuera.Utils.ReadAllLines(Program.ErbDir + position.Filename, Config.Encode).Skip(position.LineNo - 1).First() : ""
					: "";
			}
			else if (extents == ".csv")
			{
				return uEmuera.Utils.FileExists(Program.CsvDir + position.Filename)
					? position.LineNo > 0 ? uEmuera.Utils.ReadAllLines(Program.CsvDir + position.Filename, Config.Encode).Skip(position.LineNo - 1).First() : ""
					: "";
			}
			else
				return "";
		}

	}
}
