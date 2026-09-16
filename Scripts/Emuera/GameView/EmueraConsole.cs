using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Threading;
using Godot;
using MinorShift._Library;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameProc;
//using System.Drawing.Imaging;
//using MinorShift.Emuera.Forms;
using MinorShift.Emuera.Content;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameProc.Function;
using uEmuera.Forms;
using uEmuera.Drawing;
using uEmuera.Window;

namespace MinorShift.Emuera.GameView
{
	//入出力待ちの状況。
	//難読化用属性。enum.ToString()やenum.Parse()を行うなら(Exclude=true)にすること。
	[global::System.Reflection.Obfuscation(Exclude=false)]
	internal enum ConsoleState
	{
		Initializing = 0,
		Quit = 5,//QUIT
		Error = 6,//Exceptionによる強制終了
		Running = 7,
		WaitInput = 20,
        Sleep = 21,//DoEvents
		WaitInputNoFocus = 22,

        //WaitKey = 1,//WAIT
        //WaitSystemInteger = 2,//Systemが要求するInput
        //WaitInteger = 3,//INPUT
        //WaitString = 4,//INPUTS
        //WaitIntegerWithTimer = 8,
        //WaitStringWithTimer = 9,
        //Timeout = 10,
        //Timeouts = 11,
        //WaitKeyWithTimer = 12,
        //WaitKeyWithTimerF = 13,
        //WaitOneInteger = 14,
        //WaitOneString = 15,
        //WaitOneIntegerWithTimer = 16,
        //WaitOneStringWithTimer = 17,
        //WaitAnyKey = 18,

    }

	//難読化用属性。enum.ToString()やenum.Parse()を行うなら(Exclude=true)にすること。
	[global::System.Reflection.Obfuscation(Exclude=false)]
	internal enum ConsoleRedraw
	{
		None = 0,
		Normal = 1,
	}

	internal class ChangedEventArgs : EventArgs
	{
		public ConsoleDisplayLine ConsoleDisplayLine;

        public ChangedEventArgs(ConsoleDisplayLine cdl)
            : base()
        { ConsoleDisplayLine = cdl; }
	}

	internal class DisplayLineList : IList<ConsoleDisplayLine>
	{
        public DisplayLineList()
        {
            list = new List<ConsoleDisplayLine>();
        }

		private readonly List<ConsoleDisplayLine> list;

		public event EventHandler<ChangedEventArgs> Changed = null;

        protected virtual void OnChanged(ChangedEventArgs e)
        {
            if(Changed != null)
                Changed.Invoke(this, e);
        }

		public ConsoleDisplayLine this[int index]
		{
			get { return list[index]; }
			set { list[index] = value; }
		}

		public int Count { get { return list.Count; } }

		public bool IsReadOnly { get { return false; } }

		public void Add(ConsoleDisplayLine item)
		{
			list.Add(item);
			OnChanged(new ChangedEventArgs(item));
		}

        public void Clear() { list.Clear(); }

        public bool Contains(ConsoleDisplayLine item) { return list.Contains(item); }

        public void CopyTo(ConsoleDisplayLine[] array, int arrayIndex) { list.CopyTo(array, arrayIndex); }

        public IEnumerator<ConsoleDisplayLine> GetEnumerator() { return list.GetEnumerator(); }

        public int IndexOf(ConsoleDisplayLine item) { return list.IndexOf(item); }

        public void Insert(int index, ConsoleDisplayLine item) { list.Insert(index, item); }

        public bool Remove(ConsoleDisplayLine item) { return list.Remove(item); }

        public void RemoveAt(int index) { list.RemoveAt(index); }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return list.GetEnumerator(); }
    }

	internal sealed partial class EmueraConsole :IDisposable
	{
		StreamWriter debuglog = null;
		// 调试日志订阅句柄：闭包捕获的是字段，Dispose 置空 debuglog 前必须退订，
		// 否则 displayLineList.Add 再触发 Changed 时会在 handler 内抛 NullReferenceException。
		EventHandler<ChangedEventArgs> debugLoggingHandler = null;
		public EmueraConsole(MainWindow parent)
		{
			window = parent;
			hotkeyState = new HotkeyState();

			//1.713 この段階でsetStBarを使用してはいけない
			//setStBar(StaticConfig.DrawLineString);
			state = ConsoleState.Initializing;
			if (Config.FPS > 0)
			{
				int effectiveFps = Config.FPS;
				if (Program.Compatibility.Snake.UsesFastDisplayRefresh && effectiveFps < 60)
					effectiveFps = 60;
				msPerFrame = 1000 / (uint)effectiveFps;
			}
			//displayLineList = new List<ConsoleDisplayLine>();
            displayLineList = new DisplayLineList();
            //DEBUG模式:全描画ログをdebugフォルダに出力する
            if (Program.DebugMode)
            {
                debuglog = new StreamWriter(Program.DebugDir + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log", true, Encoding.UTF8)
                {
                    AutoFlush = true,
                };

                debugLoggingHandler = (sender, e) =>
                {
                    var s = e.ConsoleDisplayLine.ToString();
                    debuglog.WriteLine(s);
                };
                displayLineList.Changed += debugLoggingHandler;
            }

			printBuffer = new PrintStringBuffer(this);

			timer = new uEmuera.Forms.Timer();
			timer.Enabled = false;
			timer.Tick += new EventHandler(tickTimer);
			timer.Interval = 10;
			CBG_Clear();//文字列描画用ダミー追加

			redrawTimer = new uEmuera.Forms.Timer();
			redrawTimer.Enabled = false;//TODO:1824アニメ用再描画タイマー有効化関数の追加
			redrawTimer.Tick += new EventHandler(tickRedrawTimer);
			redrawTimer.Interval = 10;
        }
#region 1823 cbg関連
		private readonly object displayLineLock = new object();
		public int ClientWidth { get { return Config.WindowX; } }
		public int ClientHeight { get { return Config.WindowY; } }
		public int GetLinePointY(int lineNo)
		{
			int pointY = ClientHeight - Config.LineHeight;
			int bottomLineNo = window.ScrollBar.Value - 1;
			lock (displayLineLock)
			{
				if (displayLineList.Count - 1 < bottomLineNo)
					bottomLineNo = displayLineList.Count - 1;
			}
			pointY -= (bottomLineNo - lineNo) * Config.LineHeight;
			return pointY;
		}

#endregion

		const string ErrorButtonsText = "__openFileWithDebug__";
        private readonly MainWindow window;

		MinorShift.Emuera.GameProc.Process emuera;
		volatile ConsoleState state = ConsoleState.Initializing;
		public bool Enabled { get { return window.Created; } }

		/// <summary>
		/// 現在、Emueraがアクティブかどうか
		/// </summary>
		internal bool IsActive
		{
            get
            {
                return !(
                    window == null || 
                    !window.Created 
                    //|| Form.ActiveForm == null
                    );
            }
        }

		/// <summary>
		/// スクリプトが継続中かどうか
		/// 入力系はメッセージスキップやマクロも含めてIsInProcessを参照すべき
		/// </summary>
		internal bool IsRunning
		{
			get
			{
				if (state == ConsoleState.Initializing)
					return true;
				if (IsWaitInputState)
					return false;
				return (state == ConsoleState.Running || runningERBfromMemory);
			}
		}

		/// <summary>
		/// NF 输入和普通输入都属于同一类等待态，避免只更新一半分支导致输入流程失配。
		/// </summary>
		internal bool IsWaitInputState
		{
			get { return state == ConsoleState.WaitInput || state == ConsoleState.WaitInputNoFocus; }
		}

		internal bool IsInProcess
		{
			get
			{
				if (state == ConsoleState.Initializing)
					return true;
				if (state == ConsoleState.Sleep)
					return true;
				if (IsWaitInputState)
					return false;
				if (inProcess)
					return true;
				return (state == ConsoleState.Running || runningERBfromMemory);
			}
		}

		internal bool IsError
		{
			get
			{
				return state == ConsoleState.Error;
			}
		}

		internal bool IsWaitingEnterKey
		{
			get
			{
				if ((state == ConsoleState.Quit) || (state == ConsoleState.Error))
					return true;
				if (IsWaitInputState)
					return (inputReq.InputType == InputType.AnyKey || inputReq.InputType == InputType.EnterKey);
				return false;
			}
		}

        internal bool IsWaitAnyKey
        {
            get
			{
				return (IsWaitInputState && inputReq.InputType == InputType.AnyKey);
            }
        }

        internal bool IsWaintingOnePhrase
        {
            get
            {
				return (IsWaitInputState && inputReq.OneInput);
            }
        }

		internal bool IsRunningTimer
		{
			get
			{
				return (IsWaitInputState && inputReq.Timelimit > 0 && !isTimeout);
			}
		}

		internal bool IsWaitingPrimitive
		{
			get
			{
				if (IsWaitInputState)
					return (inputReq.InputType == InputType.PrimitiveMouseKey);
				return false;
			}
		}

		// 对照源码 IsWaintingInputWithMouse（IsWaitInputState && inputReq.MouseInput）：
		// 当前等待是否接受鼠标指针输入（TINPUT 的 MOUSE 参数置 MouseInput；INPUT 系
		// 的指针元数据选项置 EnablePointerInputMetadata）。用于决定空白右键/中键是否
		// 提交——普通数值 INPUT 没有该标志时不应提交，避免与源码行为偏差。
		internal bool IsWaitingInputWithMouse
		{
			get
			{
				return IsWaitInputState
					&& inputReq != null
					&& (inputReq.MouseInput || inputReq.EnablePointerInputMetadata);
			}
		}

		internal bool IsWaitingDefaultableIntValue
		{
			get
			{
				return IsWaitInputState
					&& inputReq != null
					&& inputReq.InputType == InputType.IntValue
					&& inputReq.HasDefValue
					&& !IsRunningTimer;
			}
		}
		
		internal string SelectedString
		{
			get
			{
				if (selectingButton == null)
					return null;
				if (state == ConsoleState.Error)
					return selectingButton.Inputs;
				if (!IsWaitInputState)
					return null;
				if (inputReq.InputType == InputType.IntValue && (selectingButton.IsInteger))
					return selectingButton.Input.ToString();
				if (inputReq.InputType == InputType.IntButton && (selectingButton.IsInteger))
					return selectingButton.Input.ToString();
				if (inputReq.InputType == InputType.StrValue)
					return selectingButton.Inputs;
				if (inputReq.InputType == InputType.StrButton)
					return selectingButton.Inputs;
				if (inputReq.InputType == InputType.AnyValue && selectingButton.IsInteger)
					return selectingButton.Input.ToString();
				if (inputReq.InputType == InputType.AnyValue)
					return selectingButton.Inputs;
				return null;
			}
		}

		public void Initialize()
		{
			GlobalStatic.Console = this;
			GlobalStatic.MainWindow = window;
            emuera = new GameProc.Process(this);
			GlobalStatic.Process = emuera;
			if (Program.DebugMode && Config.DebugShowWindow)
			{
				OpenDebugDialog();
				window.Focus();
			}
			ClearDisplay();
			if (!emuera.InitializeAsync().GetAwaiter().GetResult())
			{
				state = ConsoleState.Error;
				OutputLog(null);
				PrintFlush(false);
				RefreshStrings(true);
				return;
			}
			callEmueraProgram("");
			RefreshStrings(true);
		}
		

        public void Quit() { state = ConsoleState.Quit; }
        public void ForceQuit() { state = ConsoleState.Quit; }
        public void QuitAndRestart()
        {
            state = ConsoleState.Quit;
            global::GenericUtils.RestartGame();
        }
		public void ThrowTitleError(bool error)
		{
			state = ConsoleState.Error;
			notToTitle = true;
			byError = error;
		}
		public void ThrowError(bool playSound)
		{
			if (playSound)
				uEmuera.Media.SystemSounds.Hand.Play();
			forceUpdateGeneration();
			UseUserStyle = false;
			PrintFlush(false);
			RefreshStrings(false);
			state = ConsoleState.Error;
		}

        public bool notToTitle = false;
        public bool byError = false;
        //public ScriptPosition ErrPos = null;

		#region button関連
		bool lastButtonIsInput = true;
        public volatile bool updatedGeneration = false;
		volatile int lastButtonGeneration = 0;//最後に追加された選択肢の世代。これと世代が一致しない選択肢は選択できない。
		volatile int newButtonGeneration = 0;//次に追加される選択肢の世代。Input又はInputsごとに増加
		//public int LastButtonGeneration { get { return lastButtonGeneration; } }
		public int NewButtonGeneration { get { return newButtonGeneration; } }
        public void UpdateGeneration() { lastButtonGeneration = newButtonGeneration; updatedGeneration = true; }
        public void forceUpdateGeneration() { newButtonGeneration++; lastButtonGeneration = newButtonGeneration; updatedGeneration = true; }
        LogicalLine lastInputLine;

		private void newGeneration()
		{
            //値の入力を求められない時は更新は必要ないはず
			if (!IsWaitInputState || !inputReq.NeedValue)
				return;
            if (!updatedGeneration && emuera.getCurrentLine != lastInputLine)
            {
                //ボタン無しで次の入力に来たなら強制で世代更新
                lastButtonGeneration = newButtonGeneration;
            }
            else
                updatedGeneration = false;
            lastInputLine = emuera.getCurrentLine;
			//古い選択肢を選択できないように。INPUTで使った選択肢をINPUTSには流用できないように。
			if (inputReq.InputType == InputType.IntValue || inputReq.InputType == InputType.IntButton)
			{
				if (lastButtonGeneration == newButtonGeneration)
					unchecked { newButtonGeneration++; }
				else if (!lastButtonIsInput)
					lastButtonGeneration = newButtonGeneration;
				lastButtonIsInput = true;
			}
			if (inputReq.InputType == InputType.StrValue || inputReq.InputType == InputType.StrButton || inputReq.InputType == InputType.AnyValue)
			{
				if (lastButtonGeneration == newButtonGeneration)
					unchecked { newButtonGeneration++; }
				else if (lastButtonIsInput)
					lastButtonGeneration = newButtonGeneration;
				lastButtonIsInput = false;
			}
		}

		/// <summary>
		/// 選択中のボタン。INPUTやINPUTSに対応したものでなければならない
		/// </summary>
		ConsoleButtonString selectingButton = null;
		ConsoleButtonString lastSelectingButton = null;
		public ConsoleButtonString SelectingButton { get { return selectingButton; } }
		public bool ButtonIsSelected(ConsoleButtonString button) { return selectingButton == button; }

		internal bool HasCurrentGenerationButton(bool integerOnly)
		{
			lock (displayLineLock)
			{
				for (int i = displayLineList.Count - 1; i >= 0; i--)
				{
					bool generationEnded;
					if (LineHasCurrentGenerationButton(displayLineList[i], integerOnly, out generationEnded))
						return true;
					if (generationEnded)
						return false;
				}
			}
			return false;
		}

		private bool LineHasCurrentGenerationButton(ConsoleDisplayLine line, bool integerOnly, out bool generationEnded)
		{
			generationEnded = false;
			if (line == null || line.Buttons == null)
				return false;
			foreach (ConsoleButtonString button in line.Buttons)
			{
				if (button.Generation != 0 && button.Generation != lastButtonGeneration)
				{
					generationEnded = true;
					return false;
				}
				if (button.IsButton && (!integerOnly || button.IsInteger))
					return true;
				foreach (AConsoleDisplayPart part in button.StrArray)
				{
					if (!(part is ConsoleDivPart div) || div.Children == null)
						continue;
					for (int i = div.Children.Length - 1; i >= 0; i--)
					{
						if (LineHasCurrentGenerationButton(div.Children[i], integerOnly, out generationEnded))
							return true;
						if (generationEnded)
							return false;
					}
				}
			}
			return false;
		}

		private bool CurrentGenerationButtonAcceptsInt(long value)
		{
			lock (displayLineLock)
			{
				for (int i = displayLineList.Count - 1; i >= 0; i--)
				{
					bool generationEnded;
					if (LineAcceptsCurrentGenerationInt(displayLineList[i], value, out generationEnded))
						return true;
					if (generationEnded)
						return false;
				}
			}
			return false;
		}

		private bool LineAcceptsCurrentGenerationInt(ConsoleDisplayLine line, long value, out bool generationEnded)
		{
			generationEnded = false;
			if (line == null || line.Buttons == null)
				return false;
			foreach (ConsoleButtonString button in line.Buttons)
			{
				if (button.Generation != 0 && button.Generation != lastButtonGeneration)
				{
					generationEnded = true;
					return false;
				}
				if (button.IsButton && button.IsInteger && button.Input == value)
					return true;
				foreach (AConsoleDisplayPart part in button.StrArray)
				{
					if (!(part is ConsoleDivPart div) || div.Children == null)
						continue;
					for (int i = div.Children.Length - 1; i >= 0; i--)
					{
						if (LineAcceptsCurrentGenerationInt(div.Children[i], value, out generationEnded))
							return true;
						if (generationEnded)
							return false;
					}
				}
			}
			return false;
		}

		private bool CurrentGenerationButtonAcceptsString(string value)
		{
			lock (displayLineLock)
			{
				for (int i = displayLineList.Count - 1; i >= 0; i--)
				{
					bool generationEnded;
					if (LineAcceptsCurrentGenerationString(displayLineList[i], value, out generationEnded))
						return true;
					if (generationEnded)
						return false;
				}
			}
			return false;
		}

		private bool LineAcceptsCurrentGenerationString(ConsoleDisplayLine line, string value, out bool generationEnded)
		{
			generationEnded = false;
			if (line == null || line.Buttons == null)
				return false;
			foreach (ConsoleButtonString button in line.Buttons)
			{
				if (button.Generation != 0 && button.Generation != lastButtonGeneration)
				{
					generationEnded = true;
					return false;
				}
				if (button.IsButton && (button.Inputs == value || (button.IsInteger && button.Input.ToString() == value)))
					return true;
				foreach (AConsoleDisplayPart part in button.StrArray)
				{
					if (!(part is ConsoleDivPart div) || div.Children == null)
						continue;
					for (int i = div.Children.Length - 1; i >= 0; i--)
					{
						if (LineAcceptsCurrentGenerationString(div.Children[i], value, out generationEnded))
							return true;
						if (generationEnded)
							return false;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// マウスの直下にあるテキスト。ボタンであってもよい。
		/// ToolTip表示用。世代無視、履歴中も表示
		/// </summary>
		ConsoleButtonString pointingString = null;
		#endregion

		#region Input & Timer系

		//bool hasDefValue = false;
		//Int64 defNum;
		//string defStr;

		volatile private InputRequest inputReq = null;
		public void Await(int time)
		{
			if (!Enabled || state != ConsoleState.Running)
			{
				this.Quit();
				return;
			}
			uint awaitStart = WinmmTimer.TickCount;
			bool usesFastDisplayRefresh = Program.Compatibility.Snake.UsesFastDisplayRefresh;
			int refreshWaitMs = usesFastDisplayRefresh ? 4 : 40;
			int frameWaitMs = usesFastDisplayRefresh ? (time > 0 ? Math.Min(8, time) : 0) : 20;
			int uiFrame = global::GenericUtils.UiFrameGeneration;
			RefreshStrings(true);
			int refreshGeneration = window.RefreshRequestGeneration;
			window.WaitForRefreshProcessed(refreshGeneration, refreshWaitMs);
			global::GenericUtils.WaitForDisplayWorkDrained(refreshWaitMs);
			if (frameWaitMs > 0)
				global::GenericUtils.WaitForUiFrameAfter(uiFrame, frameWaitMs);
			state = ConsoleState.Sleep;
			WinInput.ClearLatches();
			emuera.UpdateCheckInfiniteLoopState();

			if (time > 0)
			{
				if (usesFastDisplayRefresh)
				{
					int elapsed = (int)(WinmmTimer.TickCount - awaitStart);
					int remaining = time - elapsed;
					if (remaining > 0)
						System.Threading.Thread.Sleep(remaining);
					else
						System.Threading.Thread.Yield();
				}
				else
				{
					System.Threading.Thread.Sleep(time);
				}
			}
			else if (usesFastDisplayRefresh)
				System.Threading.Thread.Yield();

			////DoEvents()の間にウインドウが閉じられたらおしまい。
			//if (!Enabled || state != ConsoleState.Sleep)
			//{
			//	ReadAnyKey();
			//	return;
			//}

			state = ConsoleState.Running;
		}

		private void SimulateSequenceInput(InputRequest req)
		{
			string raw = emuera.SequenceInputValue ?? string.Empty;
			emuera.HasSequenceInput = false;
			emuera.SequenceInputValue = null;
			inputReq = req;
			state = ConsoleState.WaitInput;
			PressEnterKey(false, raw, false);
		}

		public void WaitInput(InputRequest req)
		{
			// SEQUENCEINPUT schedules a synthetic input on the next wait; consume it once.
			if (emuera != null && emuera.HasSequenceInput)
			{
				SimulateSequenceInput(req);
				return;
			}

			state = req.NoFocus ? ConsoleState.WaitInputNoFocus : ConsoleState.WaitInput;
			inputReq = req;
			if (global::gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
			{
				global::gEmuera.LegacyRunner.LegacyTrace.TryRecordWait("request_pending", req.ID, req.InputType.ToString(),
					req.NeedValue, req.OneInput, req.NoFocus, req.Timelimit, NewButtonGeneration);
			}
			bool flushDeferredRewrite = ConsumeDisplayRewriteRefresh();
			if (req.NoFocus || flushDeferredRewrite)
				RefreshStrings(true);
			if (req.Timelimit > 0)
			{
				if (req.OneInput)
					window.update_lastinput();
				presetTimer();
//				setTimer();
			}
			//updateMousePosition();
			//Point point = window.MainPicBox.PointToClient(Control.MousePosition);
			//if (window.MainPicBox.ClientRectangle.Contains(point))
			//{
			//	PrintFlush(false);
			//	MoveMouse(point);
			//}
		}

		public void ReadAnyKey(bool anykey = false, bool stopMesskip = false)
		{
			InputRequest req = new InputRequest();
			if (!anykey)
				req.InputType = InputType.EnterKey;
			else
				req.InputType = InputType.AnyKey;
			req.StopMesskip = stopMesskip;
			inputReq = req;
			state = ConsoleState.WaitInput;
			if (global::gEmuera.LegacyRunner.LegacyTrace.IsEnabled)
			{
				global::gEmuera.LegacyRunner.LegacyTrace.TryRecordWait("request_pending", req.ID, req.InputType.ToString(),
					req.NeedValue, req.OneInput, req.NoFocus, req.Timelimit, NewButtonGeneration);
			}
			emuera.NeedWaitToEventComEnd = false;
			if (ConsumeDisplayRewriteRefresh())
				RefreshStrings(true);
		}


		/// <summary>
		/// INPUT中のアニメーション用タイマー
		/// </summary>
		uEmuera.Forms.Timer redrawTimer = null;

		private void tickRedrawTimer(object sender, EventArgs e)
		{
			// Godot 管线中本定时器为 no-op：SETANIMETIMER 期间的动画重绘已由 Godot 侧
			// 每帧自驱动（RefreshCanvasImageAnimations / EmueraImage._Process 推进
			// SpriteAnime / AnimatedWebp 帧并 QueueRedraw），不再需要 window.Refresh()
			// 置 dirty 触发 MainWindow.Update() 的全量显示快照+diff（INPUT 等待时每帧
			// 扫描全部显示行是纯开销）。定时器状态机（setRedrawTimer / AnimeTimer /
			// GETANIMETIMER）语义保持原样：Enable/Interval 照常维护。
			// 原实现（Windows 版）：
			//   if (!redrawTimer.Enabled) return;
			//   if (!IsWaitInputState || timer.Enabled) return;
			//   window.Refresh();//OnPaint発行
		}

		/// <summary>
		/// アニメーション用タイマーの設定。0以下の値を指定するとタイマー停止
		/// </summary>
		public void setRedrawTimer(int tickcount)
		{
			if (tickcount <= 0)
			{
				redrawTimer.Enabled = false;
				return;
			}
			if (tickcount < 10)
				tickcount = 10;
			redrawTimer.Interval = tickcount;
			redrawTimer.Enabled = true;
		}

		public int AnimeTimer
		{
			get { return redrawTimer != null && redrawTimer.Enabled ? redrawTimer.Interval : 0; }
		}



		uEmuera.Forms.Timer timer = null;
		Int64 timerID = -1;
		Int64 timer_startTime;//現在のタイマーを開始した時のミリ秒数（WinmmTimer.TickCount基準）
		Int64 timer_nextDisplayTime;//TINPUT系で次に残り時間を表示する時のTickCountミリ秒数
		Int64 timer_endTime;//現在のタイマーを終了する時のTickCountミリ秒数
        bool wait_timeout = false;
        bool isTimeout = false;
        public bool IsTimeOut { get { return isTimeout; } }

		/// <summary>
		/// 1824 TINPUT時に直接タイマーをセットせずに最初の再描画が終わってからタイマーをセットする（そうしないとTINPUTと再描画だけでループしてしまうので）
		/// </summary>
		bool need_settimer = false;

		private void presetTimer()
		{
			need_settimer = true;
			if (inputReq.DisplayTime)
			{
				//100ms未満の場合、一瞬だけ残り0が表示されて終了
				//timer_nextDisplayTime = timer_startTime + 100;
				long start = inputReq.Timelimit / 100;
				string timeString1 = "残り ";
				string timeString2 = ((double)start / 10.0).ToString();
				PrintSingleLine(timeString1 + timeString2);
			}
		}
		private void setTimer()
		{
			isTimeout = false;
			timerID = inputReq.ID;
			timer.Enabled = true;
			timer_startTime = WinmmTimer.TickCount;
			timer_endTime = timer_startTime + inputReq.Timelimit;
			//if (inputReq.DisplayTime)
			//次に残り時間を表示するタイミングの設定。inputReq.DisplayTime==tureでないなら設定するだけで参照はされない（はず
			timer_nextDisplayTime = timer_startTime + 100;

		}
        public bool NeedSetTimer()
        {
            if(need_settimer)
            {
                need_settimer = false;
                setTimer();
                return true;
            }
            return false;
        }

		//汎用
		private void tickTimer(object sender, EventArgs e)
		{
			if (!timer.Enabled)
				return;
			if (!IsWaitInputState || inputReq.Timelimit <= 0 || timerID != inputReq.ID)
			{
#if UEMUERA_DEBUG
				throw new ExeEE("");
#else
				stopTimer();
				return;
#endif
			}
			long curtime = WinmmTimer.TickCount;
			if (curtime >= timer_endTime)
			{
				endTimer();
				return;
			}
			if (inputReq.DisplayTime && curtime >= timer_nextDisplayTime)
			{
				//表示に時間がかかってタイマーが止まるので次の描画は100ms後。場合によっては表示が0.2一気に飛ぶ。
				timer_nextDisplayTime = curtime + 100;
				long time = (timer_endTime - curtime) / 100;
				string timeString1 = "残り ";
				string timeString2 = ((double)time / 10.0).ToString();
				changeLastLine(timeString1 + timeString2);
			}
		}

		private void stopTimer()
		{
			//if (state == ConsoleState.WaitKeyWithTimerF && countTime < timeLimit)
			//{
			//	wait_timeout = true;
			//	while (countTime < timeLimit)
			//	{
			//		Application.DoEvents();
			//	}
			//	wait_timeout = false;
			//}
			timer.Enabled = false;
            //timer.Dispose();
		}

		/// <summary>
		/// tickTimerからのみ呼ぶ
		/// </summary>
		private void endTimer()
		{
            if (wait_timeout)
                return;
			stopTimer();
            isTimeout = true;
			if(IsWaitingPrimitive)
			{
				//callEmueraProgramは呼び出し先で行う。
				InputMouseKey(4, 0, 0, 0, 0, 0);
				return;
			}
			if (inputReq.DisplayTime)
				changeLastLine(inputReq.TimeUpMes);
			else if (inputReq.TimeUpMes != null)
				PrintSingleLine(inputReq.TimeUpMes);
			callEmueraProgram("");//ディフォルト入力の処理はcallEmueraProgram側で
			if (IsWaitInputState && inputReq.NeedValue)
			{
				Point point = window.MainPicBox.PointToClient(uEmuera.Forms.Control.MousePosition);
				if (window.MainPicBox.ClientRectangle.Contains(point))
					MoveMouse(point);
			}
			RefreshStrings(true);
		}

        public void forceStopTimer()
        {
            if (timer.Enabled)
            {
                timer.Enabled = false;
            }
        }
		#endregion

		#region Call系
		/// <summary>
		/// スクリプト実行。RefreshStringsはしないので呼び出し側がすること
		/// </summary>
		/// <param name="str"></param>
		private void callEmueraProgram(string str, bool changedByMouse = false)
		{
			//入力文字列の表示処理を行わない場合はstr == null
			if (str != null)
			{
				//INPUT文字列をPRINTする処理など
				if (!doInputToEmueraProgram(str, changedByMouse))
					return;
				if (state == ConsoleState.Error)
					return;
			}
			if (global::gEmuera.LegacyRunner.LegacyTrace.IsEnabled && inputReq != null)
			{
				global::gEmuera.LegacyRunner.LegacyTrace.TryRecordWait("completion_consumed", inputReq.ID,
					inputReq.InputType.ToString(), inputReq.NeedValue, inputReq.OneInput, inputReq.NoFocus,
					inputReq.Timelimit, NewButtonGeneration);
			}
			state = ConsoleState.Running;
			emuera.DoScript();
			if (state == ConsoleState.Running)
			{//RunningならProcessは処理を継続するべき
				state = ConsoleState.Error;
                PrintError("emueraのエラー：プログラムの状態を特定できません");
			}
			if (state == ConsoleState.Error && !noOutputLog)
				OutputLog(Program.ExeDir + "emuera.log");
			PrintFlush(false);
			//1819 Refreshは呼び出し側で行う
			//RefreshStrings(false);
			newGeneration();
		}

		private bool doInputToEmueraProgram(string str, bool changedByMouse)
		{
			bool suppressInputEcho = false;
			if (IsWaitInputState)
			{
				Int64 inputValue;

				switch (inputReq.InputType)
				{
					case InputType.IntValue:
						if (string.IsNullOrEmpty(str) && inputReq.HasDefValue && !IsRunningTimer)
						{
							inputValue = inputReq.DefIntValue;
							str = inputValue.ToString();
						}
						else if (!Int64.TryParse(str, out inputValue))
							return false;
						if (inputReq.IsSystemInput)
							emuera.InputSystemInteger(inputValue);
						else
							emuera.InputInteger(inputValue);
						break;
					case InputType.IntButton:
						if (string.IsNullOrEmpty(str) && inputReq.HasDefValue && !IsRunningTimer)
						{
							inputValue = inputReq.DefIntValue;
							str = inputValue.ToString();
						}
						else if (!Int64.TryParse(str, out inputValue))
							return false;
						if (!CurrentGenerationButtonAcceptsInt(inputValue))
							return false;
						if (inputReq.IsSystemInput)
							emuera.InputSystemInteger(inputValue);
						else
							emuera.InputInteger(inputValue);
						break;
					case InputType.StrValue:
						if (string.IsNullOrEmpty(str) && inputReq.HasDefValue && !IsRunningTimer)
							str = inputReq.DefStrValue;
						//空入力と時間切れ
						if (str == null)
							str = "";
						if (changedByMouse && inputReq.EnablePointerInputMetadata)
						{
							// INPUTS ,1 的指针值是脚本内部协议，不是要显示给玩家的输入正文。
							emuera.InputStringWithPointerMetadata(str);
							suppressInputEcho = true;
						}
						else
							emuera.InputString(str);
						break;
					case InputType.StrButton:
						if (string.IsNullOrEmpty(str) && inputReq.HasDefValue && !IsRunningTimer)
							str = inputReq.DefStrValue;
						if (str == null)
							str = "";
						if (!CurrentGenerationButtonAcceptsString(str))
							return false;
						emuera.InputString(str);
						break;
					case InputType.AnyValue:
						if (str == null)
							str = "";
						if (Int64.TryParse(str, out inputValue))
						{
							if (inputReq.IsSystemInput)
								emuera.InputSystemInteger(inputValue);
							else
								emuera.InputInteger(inputValue);
						}
						else
						{
							emuera.InputString(str);
						}
						break;
				}
				stopTimer();
			}
			if (!suppressInputEcho)
				Print(str);
			PrintFlush(false);
			return true;
		}
		#endregion


		#region 描画系
		uint lastUpdate = 0;
		uint msPerFrame = 1000 / 60;//60FPS
		bool deferDisplayRewriteRefresh = false;
		bool displayRewriteRefreshDeferred = false;
		ConsoleRedraw redraw = ConsoleRedraw.Normal;
        public ConsoleRedraw Redraw { get { return redraw; } }
		public void SetRedraw(Int64 i)
		{
			if ((i & 1) == 0)
				redraw = ConsoleRedraw.None;
			else
				redraw = ConsoleRedraw.Normal;
			if ((i & 2) != 0)
				RefreshStrings(true);
		}

		internal void MarkDisplayRewriteInProgress()
		{
			// Godot 版 UI 通过异步队列提交。动态地图这类 CLEARLINE 后逐行重画的内容，
			// 如果在 Running 中途提交普通刷新，Android 会看见旧菜单、半张地图等中间态。
			// 这里只合并非强制刷新，等 INPUT/TINPUT/WAIT 或显式强制刷新时提交完整画面。
			if (state == ConsoleState.Running)
				deferDisplayRewriteRefresh = true;
		}

		bool ShouldDeferDisplayRewriteRefresh(bool forcePaint)
		{
			return !forcePaint && deferDisplayRewriteRefresh && state == ConsoleState.Running;
		}

		bool ConsumeDisplayRewriteRefresh()
		{
			bool hadDeferredRefresh = deferDisplayRewriteRefresh || displayRewriteRefreshDeferred;
			deferDisplayRewriteRefresh = false;
			displayRewriteRefreshDeferred = false;
			return hadDeferredRefresh;
		}

		string debugTitle = null;
		public void SetWindowTitle(string str)
		{
			if (Program.DebugMode)
			{
				debugTitle = str;
				window.Text = str + " (Debug Mode)";
			}
			else
				window.Text = str;
		}

        public void SetEmueraVersionInfo(string str)
        {
            window.TextBox.Text = str;
        }
		public string GetWindowTitle()
		{
			if (Program.DebugMode && debugTitle != null)
				return debugTitle;
			return window.Text;
		}

		public string GetTextBoxText()
		{
			return window?.TextBox?.Text ?? "";
		}

		public void SetTextBoxText(string text)
		{
			if (window?.TextBox == null)
				return;
			window.TextBox.Text = text ?? "";
		}

		public string GetDisplayLine(int index)
		{
			if (index < 0 || index >= displayLineList.Count)
				return "";
			return displayLineList[index].ToString() ?? "";
		}


		/// <summary>
		/// 1818以前のRefreshStringsからselectingButton部分を抽出
		/// ここでOnPaintを発行
		/// </summary>
		public void RefreshStrings(bool force_Paint)
		{
			bool isBackLog = window.ScrollBar.Value != window.ScrollBar.Maximum;
			//ログ表示はREDRAWの設定に関係なく行うようにする
			if ((redraw == ConsoleRedraw.None) && (!force_Paint) && (!isBackLog))
				return;
			//選択中ボタンの適性チェック
			if (selectingButton != null)
			{
				//履歴表示中は選択肢無効→画面外に出てしまったボタンも履歴から選択できるように
				//if (isBackLog)
				//	selectingButton = null;
				//数値か文字列の入力待ち状態でなければ無効
				if (state != ConsoleState.Error && !IsWaitInputState)
					selectingButton = null;
				else if (IsWaitInputState && !inputReq.NeedValue)
					selectingButton = null;
				//選択肢が最新でないなら無効
				else if (selectingButton.Generation != lastButtonGeneration)
					selectingButton = null;
			}
			if (ShouldDeferDisplayRewriteRefresh(force_Paint))
			{
				displayRewriteRefreshDeferred = true;
				return;
			}
			if (force_Paint)
				ConsumeDisplayRewriteRefresh();
			if (!force_Paint)
			{//forceならば確実に再描画。
				//履歴表示中でなく、最終行を表示済みであり、選択中ボタンが変更されていないなら更新不要
				if ((!isBackLog) && (lastDrawnLineNo == lineNo) && (lastSelectingButton == selectingButton))
					return;
				//Environment.TickCountは分解能が悪すぎるのでwinmmのタイマーを呼んで来る
				uint sec = WinmmTimer.TickCount - lastUpdate;
				//まだ書き換えるタイミングでないなら次の更新を待ってみる
				//ただし、入力待ちなど、しばらく更新のタイミングがない場合には強制的に書き換えてみる
				if (sec < msPerFrame && (state == ConsoleState.Running || state == ConsoleState.Initializing))
					return;
			}
			if (forceTextBoxColor)
			{
				uint sec = WinmmTimer.TickCount - lastBgColorChange;
				//色変化が速くなりすぎないように一定時間以内の再呼び出しは強制待ちにする
				//while (sec < 200)
				//{
				//	//Application.DoEvents();
				//	sec = WinmmTimer.TickCount - lastBgColorChange;
				//}
				window.TextBox.BackColor = this.bgColor;
				lastBgColorChange = WinmmTimer.TickCount;
			}
			verticalScrollBarUpdate();
			window.Refresh();//OnPaint発行
			lastUpdate = WinmmTimer.TickCount;
			lastDrawnLineNo = lineNo;
			lastSelectingButton = selectingButton;
			if (state != ConsoleState.Running)
				ConsumeDisplayRewriteRefresh();

		}

		///// <summary>
		///// 1818以前のRefreshStringsの後半とm_RefreshStringsを融合
		///// 全面Clear法のみにしたのでさっぱりした。ダブルバッファリングはOnPaintが勝手にやるはず
		///// </summary>
		///// <param name="graph"></param>
		//public void OnPaint(Graphics graph)
		//{
		//	//描画中にEmueraが閉じられると廃棄されたPictureBoxにアクセスしてしまったりするので
		//	//OnPaintからgraphをもらった直後だから大丈夫だとは思うけど一応
		//	if (!this.Enabled)
		//		return;

		//	//描画命令を発行したRefresh時にすべきか、OnPaintの開始にすべきか、OnPaintの終了にするか
		//	lastUpdate = WinmmTimer.TickCount;

		//	bool isBackLog = window.ScrollBar.Value != window.ScrollBar.Maximum;
		//	int pointY = window.MainPicBox.Height - Config.LineHeight;

		//	int bottomLineNo = window.ScrollBar.Value - 1;
		//	if (displayLineList.Count - 1 < bottomLineNo)
		//		bottomLineNo = displayLineList.Count - 1;//1820 この処理不要な気がするけどエラー報告があったので入れとく
		//	int topLineNo = bottomLineNo - (pointY / Config.LineHeight + 1);
		//	if (topLineNo < 0)
		//		topLineNo = 0;
		//	pointY -= (bottomLineNo - topLineNo) * Config.LineHeight;

            
		//	if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
		//	{
		//		GDI.GDIStart(graph, this.bgColor);
		//		GDI.FillRect(new Rectangle(0, 0, window.MainPicBox.Width, window.MainPicBox.Height));
		//		//for (int i = bottomLineNo; i >= topLineNo; i--)
		//		//{
		//		//	displayLineList[i].GDIDrawTo(pointY, isBackLog);
		//		//	pointY -= Config.LineHeight;
		//		//}
		//		//1820a12 上から下へ描画する方向へ変更
		//		for (int i =topLineNo ; i <= bottomLineNo; i++)
		//		{
		//			displayLineList[i].GDIDrawTo(pointY, isBackLog);
		//			pointY += Config.LineHeight;
		//		}
		//		GDI.GDIEnd(graph);
		//	}
		//	else
		//	{
		//		graph.Clear(this.bgColor);
		//		//for (int i = bottomLineNo; i >= topLineNo; i--)
		//		//{
		//		//	displayLineList[i].DrawTo(graph, pointY, isBackLog, true, Config.TextDrawingMode);
		//		//	pointY -= Config.LineHeight;
		//		//}
		//		//1820a12 上から下へ描画する方向へ変更
		//		for (int i =topLineNo ; i <= bottomLineNo; i++)
		//		{
		//			displayLineList[i].DrawTo(graph, pointY, isBackLog, true, Config.TextDrawingMode);
		//			pointY += Config.LineHeight;
		//		}

		//	}

		//	//ToolTip描画

		//	if (lastPointingString != pointingString)
		//	{
		//		if (tooltipUsed)
		//			window.ToolTip.RemoveAll();
		//		if (pointingString != null && !string.IsNullOrEmpty(pointingString.Title))
		//		{
		//			window.ToolTip.SetToolTip(window.MainPicBox, pointingString.Title);
		//			tooltipUsed = true;
		//		}
		//		lastPointingString = pointingString;
		//	}
		//	if (isBackLog)
		//		lastDrawnLineNo = -1;
		//	else
		//		lastDrawnLineNo = lineNo;
		//	lastSelectingButton = selectingButton;
		//	/*デバッグ用。描画が超重い環境を想定
		//	System.Threading.Thread.Sleep(50);
		//	*/
		//	forceTextBoxColor = false;
		//}

		public void SetToolTipColor(uEmuera.Drawing.Color foreColor, uEmuera.Drawing.Color backColor)
		{
			window.ToolTip.ForeColor = foreColor;
			window.ToolTip.BackColor = backColor;

		}
		public void SetToolTipDelay(int delay)
		{
			window.ToolTip.InitialDelay = delay;
		}

        int tooltip_duration = 0;
        public void SetToolTipDuration(int duration)
        {
            tooltip_duration = duration;
        }

		public uEmuera.Drawing.Color? TextBackgroundColor { get; set; }
		public bool BitmapCacheEnabledForNextLine
		{
			set
			{
				// ERB 兼容入口必须保留，否则现有游戏会因未知指令中断。
				// 这里只把成对的 BITMAP_CACHE_ENABLE 当作整帧重写提示，不保存任何缓存状态。
				if (value)
					MarkDisplayRewriteInProgress();
			}
		}
		public bool StrictFontFallback { get; set; }
		readonly HotkeyState hotkeyState;
		// Godot 版不使用 SkiaSharp，但 v24 脚本会通过这些 API 探测渲染后端。
		// 保留与改版 emuera 默认值一致的可见状态，避免迁移脚本误判为旧 GDI 模式。
		public int SnakeTextDrawingMode { get; private set; } = 3;
		public int SnakeImageQuality { get; private set; } = 3;
		public int SnakeFontHinting { get; private set; }
		public int SnakeFontEdging { get; private set; } = 2;

		public void SetSnakeTextDrawingMode(int mode)
		{
			if (mode == 1 || mode == 3)
				SnakeTextDrawingMode = mode;
		}

		public void SetSnakeSkiaQuality(int imageQuality, int fontHinting, int fontEdging)
		{
			SnakeImageQuality = imageQuality;
			SnakeFontHinting = fontHinting;
			SnakeFontEdging = fontEdging;
		}

		public void HotkeyStateInitialize(long size)
		{
			hotkeyState.Initialize(size);
		}

		public void HotkeyStateSet(long index, long value)
		{
			hotkeyState.Set(index, value);
		}

		public bool ToggleHotkeyState(out string message)
		{
			return hotkeyState.Toggle(out message);
		}

		public bool TryEvaluateHotkey(int keyData, out long result)
		{
			return hotkeyState.TryEvaluate(keyData, out result);
		}

		public void PrintHTMLIsland(string html)
		{
			if (string.IsNullOrEmpty(html))
				return;
			global::GenericUtils.SetHtmlIsland(HtmlManager.Html2DisplayLine(html, stringMeasure, this));
		}

		public void ClearHTMLIsland()
		{
			global::GenericUtils.ClearHtmlIsland();
		}

		string tooltipFontName = null;
		long tooltipFontSize = 0;
		bool tooltipCustom = false;
		long tooltipFormat = 0;
		bool tooltipImg = false;

		public void SetToolTipFontName(string fontName)
		{
			tooltipFontName = fontName;
		}

		public void SetToolTipFontSize(long fontSize)
		{
			tooltipFontSize = fontSize;
		}

		public void CustomToolTip(bool enabled)
		{
			tooltipCustom = enabled;
		}

		public void SetToolTipFormat(long format)
		{
			tooltipFormat = format;
		}

		public void SetToolTipImg(bool enabled)
		{
			tooltipImg = enabled;
		}


        //private Graphics getGraphics()
        //{
        //	//消したいが怖いので残し
        //	if (!window.Created)
        //		throw new ExeEE("存在しないウィンドウにアクセスした");
        //	//if (Config.UseImageBuffer)
        //	//	return Graphics.FromImage(window.MainPicBox.Image);
        //	//else
        //		return window.MainPicBox.CreateGraphics();
        //}

        #endregion

        #region DebugMode系
        DebugDialog dd = null;
		public DebugDialog DebugDialog { get { return dd; } }
		StringBuilder dConsoleLog = new StringBuilder("");
		public string DebugConsoleLog { get { return dConsoleLog.ToString(); } }
		List<string> dTraceLogList = new List<string>();
#pragma warning disable CS0414 // フィールド 'EmueraConsole.dTraceLogChanged' が割り当てられていますが、値は使用されていません。
		bool dTraceLogChanged = true;
#pragma warning restore CS0414 // フィールド 'EmueraConsole.dTraceLogChanged' が割り当てられていますが、値は使用されていません。
		public string GetDebugTraceLog(bool force)
		{
			//if (!dTraceLogChanged && !force)
			//	return null;
			StringBuilder builder = new StringBuilder("");
			LogicalLine line = emuera.GetScaningLine();
			builder.AppendLine("*実行中の行");
			if ((line == null) || (line.Position == null))
			{
				builder.AppendLine("ファイル名:なし");
				builder.AppendLine("行番号:なし 関数名:なし");
				builder.AppendLine("");
			}
			else
			{
				builder.AppendLine("ファイル名:" + line.Position.Filename);
				builder.AppendLine("行番号:" + line.Position.LineNo.ToString() + " 関数名:" + line.ParentLabelLine.LabelName);
				builder.AppendLine("");
			}
			builder.AppendLine("*スタックトレース");
			for (int i = dTraceLogList.Count - 1; i >= 0; i--)
			{
				builder.AppendLine(dTraceLogList[i]);
			}
			return builder.ToString();
		}
		public void OpenDebugDialog()
		{
			if (!Program.DebugMode)
				return;
			if (dd != null)
			{
				if (dd.Created)
				{
					dd.Focus();
					return;
				}
				else
				{
					dd.Dispose();
					dd = null;
				}
			}
			dd = new DebugDialog();
			dd.SetParent(this, emuera);
			dd.Show();
		}

		public void DebugPrint(string str)
		{
			if (!Program.DebugMode)
				return;
			dConsoleLog.Append(str);
		}

		public void DebugClear()
		{
			dConsoleLog.Remove(0, dConsoleLog.Length);
		}

		public void DebugNewLine()
		{
			if (!Program.DebugMode)
				return;
			dConsoleLog.Append(System.Environment.NewLine);
		}

		public void DebugAddTraceLog(string str)
		{
			//Emueraがデバッグモードで起動されていないなら無視
			//ERBファイル以外のもの(デバッグコマンド、変数ウォッチ)を実行中なら無視
			if (!Program.DebugMode || runningERBfromMemory)
				return;
			dTraceLogChanged = true;
			dTraceLogList.Add(str);
		}
		public void DebugRemoveTraceLog()
		{
			if (!Program.DebugMode || runningERBfromMemory)
				return;
			dTraceLogChanged = true;
			if(dTraceLogList.Count > 0)
				dTraceLogList.RemoveAt(dTraceLogList.Count-1);
		}
		public void DebugClearTraceLog()
		{
			if (!Program.DebugMode || runningERBfromMemory)
				return;
			dTraceLogChanged = true;
			dTraceLogList.Clear();
		}

		public void DebugCommand(string com, bool munchkin, bool outputDebugConsole)
		{
			ConsoleState temp_state = state;
			runningERBfromMemory = true;
            //スクリプト等が失敗した場合に備えて念のための保存
            GlobalStatic.Process.saveCurrentState(false);
            try
			{
				LogicalLine line = null;
				if (!com.StartsWith("@") && !com.StartsWith("\"") && !com.StartsWith("\\"))
					line = LogicalLineParser.ParseLine(com, null);
				if (line == null || (line is InvalidLine))
				{
					WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(com), LexEndWith.EoL, LexAnalyzeFlag.None);
					IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
					if (term == null)
						throw new CodeEE("解釈不能なコードです");
					if (term.GetEraType() == EraType.Integer)
					{
						if (outputDebugConsole)
							com = "DEBUGPRINTFORML {" + com + "}";
						else
							com = "PRINTVL " + com;
					}
					else
					{
						if (outputDebugConsole)
							com = "DEBUGPRINTFORML %" + com + "%";
						else
							com = "PRINTFORMSL " + com;
					}
					line = LogicalLineParser.ParseLine(com, null);
				}
				if (line == null)
					throw new CodeEE("解釈不能なコードです");
				if (line is InvalidLine)
					throw new CodeEE(line.ErrMes);
				if (!(line is InstructionLine))
					throw new CodeEE("デバッグコマンドで使用できるのは代入文か命令文だけです");
				InstructionLine func = (InstructionLine)line;
				if (func.Function.IsFlowContorol())
					throw new CodeEE("フロー制御命令は使用できません");
				//__METHOD_SAFE__をみるならいらないかも
				if (func.Function.IsWaitInput())
					throw new CodeEE(func.Function.Name + "命令は使用できません");
				//1750 __METHOD_SAFE__とほぼ条件同じだよねってことで
				if (!func.Function.IsMethodSafe())
					throw new CodeEE(func.Function.Name + "命令は使用できません");
				//1756 SIFの次に来てはいけないものはここでも不可。
				if (func.Function.IsPartial())
					throw new CodeEE(func.Function.Name + "命令は使用できません");
				switch (func.FunctionCode)
				{//取りこぼし
					//逆にOUTPUTLOG、QUITはDebugCommandの前に捕まえる
					case FunctionCode.PUTFORM:
					case FunctionCode.UPCHECK:
					case FunctionCode.CUPCHECK:
					case FunctionCode.SAVEDATA:
						throw new CodeEE(func.Function.Name + "命令は使用できません");
				}
				ArgumentParser.SetArgumentTo(func);
				if (func.IsError)
					throw new CodeEE(func.ErrMes);
				emuera.DoDebugNormalFunction(func, munchkin);
				if (func.FunctionCode == FunctionCode.SET)
				{
					if (!outputDebugConsole)
						PrintSingleLine(com);
					//DebugWindowのほうは少しくどくなるのでいらないかな
				}
			}
			catch (Exception e)
			{
				if (outputDebugConsole)
				{
					DebugPrint(e.Message);
					DebugNewLine();
				}
				else
					PrintError(e.Message);
				emuera.clearMethodStack();
			}
			finally
			{
                //確実に元の状態に戻す
                GlobalStatic.Process.loadPrevState();
                runningERBfromMemory = false;
				state = temp_state;
			}
		}
		#endregion

		#region Window.Form系

		internal Point GetMousePosition()
		{
            if (window == null || !window.Created)
                return new Point();
            Point pos = window.MainPicBox.PointToClient(global::GenericUtils.GetPointerPosition());
            pos.Y = pos.Y - ClientHeight;
            return pos;
		}

		/// <summary>
		/// マウス位置をボタンの選択状態に反映させる
		/// </summary>
		/// <param name="point"></param>
		/// <returns>この後でRefreshStringsが必要かどうか</returns>
		public bool MoveMouse(Point point)
		{
            return false;
		//	if (cbgButtonMap != null && cbgButtonMap.IsCreated)
		//	{
		//		//pointはクライアント左上基準の座標。
		//		//clientPointをクライアント左下基準の座標に置き換え
		//		Point clientPoint = point;
		//		clientPoint.Y = point.Y - ClientHeight;
		//		int buttonNum = -1;
		//		//マップ画像の左上基準の座標に置き換え
		//		Point mapPoint = clientPoint;
		//		mapPoint.Y = mapPoint.Y + cbgButtonMap.Height;
		//		if (mapPoint.X >= 0 && mapPoint.Y >= 0 && mapPoint.X < cbgButtonMap.Width && mapPoint.Y < cbgButtonMap.Height)
		//		{
		//			Color c = cbgButtonMap.Bitmap.GetPixel(mapPoint.X, mapPoint.Y);
		//			if (c.A == 255)
		//			{
		//				buttonNum = c.ToArgb() & 0xFFFFFF;
		//			}
		//		}
		//		if (buttonNum >= 0)
		//		{
		//			bool ret = (pointingString != null || selectingButton != null || buttonNum != selectingCBGButtonInt);
		//			selectingCBGButtonInt = buttonNum;
		//			pointingString = null;
		//			selectingButton = null;
		//			return ret;
		//		}
		//		else if (selectingCBGButtonInt >= 0)
		//		{
		//			selectingCBGButtonInt = -1;
		//			pointingString = null;
		//			selectingButton = null;
		//			return true;
		//		}
		//	}
		//	selectingCBGButtonInt = -1;
		//	ConsoleButtonString select = null;
		//	ConsoleButtonString pointing = null;
		//	bool canSelect = false;
		//	//数値か文字列の入力待ち状態でなければ選択中にはならない
		//	if (state == ConsoleState.Error)
		//		canSelect = true;
		//	else if (state == ConsoleState.WaitInput && inputReq.NeedValue)
		//		canSelect = true;
		//	//スクリプト実行中は無視//入力・マクロ処理中は無視
		//	if(this.IsInProcess)
		//		goto end;
		//	//履歴表示中は無視
		//	//if (window.ScrollBar.Value != window.ScrollBar.Maximum)
		//	//	goto end;
		//	int pointX = point.X;
		//	int pointY = point.Y;
		//	ConsoleDisplayLine curLine = null;

		//	int bottomLineNo = window.ScrollBar.Value - 1;
		//	if (displayLineList.Count - 1 < bottomLineNo)
		//		bottomLineNo = displayLineList.Count - 1;//1820 この処理不要な気がするけどエラー報告があったので入れとく
		//	int topLineNo = bottomLineNo - (window.MainPicBox.Height/ Config.LineHeight);
		//	if (topLineNo < 0)
		//		topLineNo = 0;
		//	int relPointY = pointY - window.MainPicBox.Height;
		//	//下から上へ探索し発見次第打ち切り
		//	for (int i = bottomLineNo; i >= topLineNo; i--)
		//	{
		//		relPointY += Config.LineHeight;
		//		curLine = displayLineList[i];
				
		//		for (int b = 0; b < curLine.Buttons.Length; b++)
		//		{
		//			ConsoleButtonString button = curLine.Buttons[curLine.Buttons.Length - b - 1];
		//			if(button == null || button.StrArray == null)
		//				continue;
		//			if ((button.PointX <= pointX) && (button.PointX + button.Width >= pointX))
		//			{
		//				//if (relPointY >= 0 && relPointY <= Config.FontSize)
		//				//{
		//				//	pointing = button;
		//				//	if(pointing.IsButton)
		//				//		goto breakfor;
		//				//}
		//				foreach(AConsoleDisplayPart part in button.StrArray)
		//				{
		//					if(part == null)
		//						continue;
		//					if ((part.PointX <= pointX) && (part.PointX + part.Width >= pointX)
		//						&& (relPointY >= part.Top) && (relPointY <= part.Bottom))
		//					{
		//						pointing = button;
		//						if (pointing.IsButton)
		//							goto breakfor;
		//					}
		//				}
		//			}
		//		}
		//	}


		//	//int posy_bottom2up = window.MainPicBox.Height - pointY;
		//	//int logNum = window.ScrollBar.Maximum - window.ScrollBar.Value;
		//	////表示中の一番下の行番号
		//	//int curBottomLineNo = displayLineList.Count - logNum;
		//	//int curPointingLineNo = curBottomLineNo - (posy_bottom2up / Config.LineHeight + 1);
		//	//if ((curPointingLineNo < 0) || (curPointingLineNo >= displayLineList.Count))
		//	//	curLine = null;
		//	//else
		//	//	curLine =  displayLineList[curPointingLineNo];
		//	//if (curLine == null)
		//	//	goto end;
			
		//	//pointing = curLine.GetPointingButton(pointX);
		//breakfor:
		//	if ((pointing == null) || (pointing.Generation != lastButtonGeneration))
		//		canSelect = false;
		//	else if (!pointing.IsButton)
		//		canSelect = false;
		//	else if ((state == ConsoleState.WaitInput && inputReq.InputType == InputType.IntValue) && (!pointing.IsInteger))
		//		canSelect = false;
		//end:
		//	if (canSelect)
		//		select = pointing;
		//	bool needRefresh = select != selectingButton || pointing != pointingString;
		//	pointingString = pointing;
		//	selectingButton = select;
		//	return needRefresh;
		}


		public void LeaveMouse()
		{
			bool needRefresh = selectingButton != null || pointingString != null;
			selectingButton = null;
			pointingString = null;
			if(needRefresh)
			{
				RefreshStrings(true);
			}
		}

		private void verticalScrollBarUpdate()
		{
			int max;
			lock (displayLineLock)
				max = displayLineList.Count;
			int move = max - window.ScrollBar.Maximum;
			if (move == 0)
				return;
			if (move > 0)
			{
				window.ScrollBar.Maximum = max;
				window.ScrollBar.Value += move;
			}
			else
			{
				if (max > window.ScrollBar.Value)
					window.ScrollBar.Value = max;
				window.ScrollBar.Maximum = max;
			}
			window.ScrollBar.Enabled = max > 0;
		}
		#endregion

		public void GotoTitle()
		{
			//if (state == ConsoleState.Error)
			//{
			//    MessageBox.Show("エラー発生時はこの機能は使えません");
			//}
            forceStopTimer();
			ClearDisplay();
            redraw = ConsoleRedraw.Normal;
            UseUserStyle = false;
            userStyle = new StringStyle(Config.ForeColor, FontStyle.Regular, null);
            uEmuera.Utils.ResourcePrepareSimple();
            emuera.BeginTitle();
			ReadAnyKey(false, false);
			callEmueraProgram("");
			RefreshStrings(true);
		}

		bool force_temporary = false;
        bool timer_suspended = false;
		ConsoleState prevState;
		InputRequest prevReq;

		public void ReloadErb()
		{
			if (state == ConsoleState.Error)
			{
				MessageBox.Show("エラー発生時はこの機能は使えません");
				return;
			}
			if (state == ConsoleState.Initializing)
			{
				MessageBox.Show("初期化中はこの機能は使えません");
				return;
			}
            bool notRedraw = false;
            if (redraw == ConsoleRedraw.None)
            {
                notRedraw = true;
                redraw = ConsoleRedraw.Normal;
            }
            if (timer.Enabled)
            {
				timer.Enabled = false;
                timer_suspended = true;
            }
            prevState = state;
			prevReq = inputReq;
			state = ConsoleState.Initializing;
			PrintSingleLine("ERB再読み込み中……", true);
			force_temporary = true;
			emuera.ReloadErbAsync().GetAwaiter().GetResult();
			force_temporary = false;
            PrintSingleLine("再読み込み完了", true);
			RefreshStrings(true);
            //強制的にボタン世代が切り替わるのを防ぐ
            updatedGeneration = true;
            if (notRedraw)
                redraw = ConsoleRedraw.None;
        }

		public void ReloadErbFinished()
		{
			state = prevState;
			inputReq = prevReq;
			PrintSingleLine(" ");
            if (timer_suspended)
            {
                timer_suspended = false;
                timer.Enabled = true;
            }
		}

		public void ReloadPartialErb(List<string> path)
		{
			if (state == ConsoleState.Error)
			{
				MessageBox.Show("エラー発生時はこの機能は使えません");
				return;
			}
			if (state == ConsoleState.Initializing)
			{
				MessageBox.Show("初期化中はこの機能は使えません");
				return;
			}
            bool notRedraw = false;
            if (redraw == ConsoleRedraw.None)
            {
                notRedraw = true;
                redraw = ConsoleRedraw.Normal;
            }
            if (timer.Enabled)
            {
				timer.Enabled = false;
                timer_suspended = true;
            }
			prevState = state;
			prevReq = inputReq;
			state = ConsoleState.Initializing;
            PrintSingleLine("ERB再読み込み中……", true);
			force_temporary = true;
			emuera.ReloadPartialErbAsync(path).GetAwaiter().GetResult();
			force_temporary = false;
            PrintSingleLine("再読み込み完了", true);
			RefreshStrings(true);
            //強制的にボタン世代が切り替わるのを防ぐ
            updatedGeneration = true;
            if (notRedraw)
                redraw = ConsoleRedraw.None;
        }

		public void ReloadFolder(string erbPath)
		{
            if (state == ConsoleState.Error)
			{
				MessageBox.Show("エラー発生時はこの機能は使えません");
				return;
			}
			if (state == ConsoleState.Initializing)
			{
				MessageBox.Show("初期化中はこの機能は使えません");
				return;
			}
            if (timer.Enabled)
            {
				timer.Enabled = false;
                timer_suspended = true;
            }
            List<string> paths = new List<string>();
			SearchOption op = SearchOption.AllDirectories;
			if (!Config.SearchSubdirectory)
				op = SearchOption.TopDirectoryOnly;
			var fnames = new List<string>(Directory.GetFiles(erbPath, "*.ERB", op));
#if UNITY_ANDROID && !UNITY_EDITOR
            fnames.AddRange(Directory.GetFiles(erbPath, "*.erb", op));
#endif
            for (int i = 0; i < fnames.Count; i++)
				if (Path.GetExtension(fnames[i]).ToUpper() == ".ERB")
					paths.Add(fnames[i]);
            fnames.Clear();

            bool notRedraw = false;
            if (redraw == ConsoleRedraw.None)
            {
                notRedraw = true;
                redraw = ConsoleRedraw.Normal;
            }
			prevState = state;
			prevReq = inputReq;
			state = ConsoleState.Initializing;
            PrintSingleLine("ERB再読み込み中……", true);
			force_temporary = true;
            emuera.ReloadPartialErbAsync(paths).GetAwaiter().GetResult();
			force_temporary = false;
            PrintSingleLine("再読み込み完了", true);
			RefreshStrings(true);
            //強制的にボタン世代が切り替わるのを防ぐ
            updatedGeneration = true;
            if (notRedraw)
                redraw = ConsoleRedraw.None;
        }

		public void Dispose()
		{
			if(debugLoggingHandler != null)
			{
				displayLineList.Changed -= debugLoggingHandler;
				debugLoggingHandler = null;
			}
			if(debuglog != null)
			{
				debuglog.Dispose();
				debuglog = null;
			}
			if(timer != null)
				timer.Dispose();
			//timer = null;
			//stringMeasure.Dispose();
		}
	}
}
