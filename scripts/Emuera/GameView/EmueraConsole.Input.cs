// EmueraConsole.Input.cs —— 承载输入事件入口（鼠标/按键/系统命令 → 脚本）功能域，自 EmueraConsole.cs 拆出（原因：主文件超 2000 行只减不增约束）。
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
	internal sealed partial class EmueraConsole :IDisposable
	{
		#region 入力系
		readonly string[] spliter = new string[] { "\\n", "\r\n", "\n", "\r" };//本物の改行コードが来ることは無いはずだけど一応

		public bool MesSkip = false;
		volatile private bool inProcess = false;
		volatile public bool KillMacro = false;
		
		internal void MouseWheel(Point point, int delta)
		{
			if (!IsWaitingPrimitive)
				return;
			//pointはクライアント左上基準の座標。
			//clientPointをクライアント左下基準の座標に置き換え
			Point clientPoint = point;
			clientPoint.Y = point.Y - ClientHeight;
			InputMouseKey(2, delta, clientPoint.X, clientPoint.Y, 0, 0);
		}

		internal void MouseDown(Point point, MouseButtons button)
		{
			if (!IsWaitingPrimitive)
				return;
			//pointはクライアント左上基準の座標。
			//clientPointをクライアント左下基準の座標に置き換え
			Point clientPoint = point;
			clientPoint.Y = point.Y - ClientHeight;
			int buttonNum = -1;
			if(cbgButtonMap != null && cbgButtonMap.IsCreated)
			{
				//マップ画像の左上基準の座標に置き換え
				Point mapPoint = clientPoint;
				mapPoint.Y = clientPoint.Y + cbgButtonMap.Height;
				if(mapPoint.X >= 0 && mapPoint.Y >= 0 && mapPoint.X < cbgButtonMap.Width && mapPoint.Y < cbgButtonMap.Height)
				{
					uEmuera.Drawing.Color c = cbgButtonMap.Bitmap.GetPixel(mapPoint.X, mapPoint.Y);
					if(c.A == 255)
					{
						buttonNum = c.ToArgb() & 0xFFFFFF;
					}
				}

			}
			InputMouseKey(1, (int)button, clientPoint.X, clientPoint.Y, buttonNum, 0);
		}

		// 对照源码 MoveMouse 的 CBG 命中：在 cbgButtonMap 上做像素查找，返回命中的按钮号
		// （透明像素 = 未命中 → -1）。point 为客户端左上基准坐标（同 MouseDown 入参）。
		// 供 EmueraContent hover 路径读取 CBG 按钮的 tooltip。
		internal int GetCBGButtonAtClientPoint(Point point)
		{
			if (cbgButtonMap == null || !cbgButtonMap.IsCreated)
				return -1;
			Point clientPoint = point;
			clientPoint.Y = point.Y - ClientHeight;
			Point mapPoint = clientPoint;
			mapPoint.Y = clientPoint.Y + cbgButtonMap.Height;
			if (mapPoint.X >= 0 && mapPoint.Y >= 0 && mapPoint.X < cbgButtonMap.Width && mapPoint.Y < cbgButtonMap.Height)
			{
				uEmuera.Drawing.Color c = cbgButtonMap.Bitmap.GetPixel(mapPoint.X, mapPoint.Y);
				if (c.A == 255)
					return c.ToArgb() & 0xFFFFFF;
			}
			return -1;
		}

		// 对照源码 MoveMouse 的 CBG tooltip 查找：给定按钮号返回首个非空 tooltipString。
		internal string GetCBGTooltip(int buttonValue)
		{
			if (buttonValue <= 0)
				return null;
			lock (cbgLock)
			{
				for (int i = 0; i < cbgList.Count; i++)
				{
					ClientBackGroundImage cbg = cbgList[i];
					if (!cbg.isButton || cbg.buttonValue != buttonValue)
						continue;
					if (string.IsNullOrEmpty(cbg.tooltipString))
						continue;
					return cbg.tooltipString;
				}
			}
			return null;
		}

		//1823 Key入力を捕まえる
		internal void PressPrimitiveKey(int keycode, int keydata, int keymod)
		{
			if (IsWaitingPrimitive)
				InputMouseKey(3, keycode, keydata, 0, 0, 0);
		}

		//1823 Key入力を捕まえる
		internal void InputMouseKey(int type, int result1, int result2, int result3, int result4, long result5)
		{
			emuera.InputResult5(type, result1, result2, result3, result4, result5);

			inProcess = true;
			try
			{
				//1823 Escキーもマクロも右クリックも不可。単純に押されたキーを送るのみ。
				callEmueraProgram(null);
				if (IsWaitInputState && inputReq.NeedValue)
				{
					Point point = window.MainPicBox.PointToClient(uEmuera.Forms.Control.MousePosition);
					if (window.MainPicBox.ClientRectangle.Contains(point))
						MoveMouse(point);
				}
			}
			finally
			{
				inProcess = false;
			}
			RefreshStrings(true);
		}

		public void PressEnterKey(bool keySkip, string str, bool changedByMouse)
		{
			MesSkip = keySkip;
			if ((state == ConsoleState.Running) || (state == ConsoleState.Initializing))
				return;
			else if ((state == ConsoleState.Quit))
			{
				window.Close();
				return;
			}
			else if (state == ConsoleState.Error)
			{
				if (str == ErrorButtonsText && selectingButton != null && selectingButton.ErrPos != null)
				{
					openErrorFile(selectingButton.ErrPos);
					return;
				}
				window.Close();
				return;
			}
#if UEMUERA_DEBUG
			if (!IsWaitInputState || inputReq == null)
				throw new ExeEE("");
#endif
			KillMacro = false;
			try
			{
				string[] text;
				bool inputMacroEnabled = emuera == null || emuera.InputMacroEnabled;
				if (changedByMouse || !inputMacroEnabled) // Snake/EE can feed the sequence literally.
				{ text = new string[] { str }; }
				else
				{
					if (str.Length > 1 && str.StartsWith("@") && !inputReq.OneInput)
					{
						doSystemCommand(str);
						return;
					}
					if (inputReq.InputType == InputType.Void)
						return;
					if (timer.Enabled &&
						(inputReq.InputType == InputType.AnyKey || inputReq.InputType == InputType.EnterKey))
						stopTimer();
					//if((inputReq.InputType == InputType.IntValue || inputReq.InputType == InputType.StrValue)
					if (str.Contains("(") && inputMacroEnabled)
						str = parseInput(new StringStream(str), false);
					text = str.Split(spliter, StringSplitOptions.None);
				}
				
				inProcess = true;
				if (!inputMacroEnabled)
				{
					callEmueraProgram(str, changedByMouse);
					RefreshStrings(false);
					goto endMacro;
				}
				for (int i = 0; i < text.Length; i++)
				{
					string inputs = text[i];
					if (inputs.IndexOf("\\e") >= 0)
					{
						inputs = inputs.Replace("\\e", "");//\eの除去
						MesSkip = true;
					}

					if (inputReq.OneInput && (!Config.AllowLongInputByMouse || !changedByMouse) && inputs.Length > 1)
						inputs = inputs.Remove(1);
					//1819 TODO:入力無効系（強制待ちTWAIT）でスキップとマクロを止めるかそのままか
					//現在はそのまま。強制待ち中はスキップの開始もできないのにスキップ中なら飛ばせる。
					if (inputReq.InputType == InputType.Void)
					{
						i--;
						inputs = "";
					}
					callEmueraProgram(inputs, changedByMouse);
					RefreshStrings(false);
					while (MesSkip && IsWaitInputState)
					{
						//TODO:入力無効を通していいか？スキップ停止をマクロでは飛ばせていいのか？
						if (inputReq.NeedValue)
							break;
						if (inputReq.StopMesskip)
							break;
						callEmueraProgram("");
						RefreshStrings(false);
						//DoEventを呼ばないと描画処理すらまったく行われない
						//Application.DoEvents();
						//EscがマクロストップかつEscがスキップ開始だからEscでスキップを止められても即開始しちゃったりするからあんまり意味ないよね
						//if (KillMacro)
						//	goto endMacro;
					}
					MesSkip = false;
					if (!IsWaitInputState)
						break;
					//マクロループ時は待ち処理が起こらないのでここでシステムキューを捌く
					//Application.DoEvents();
#if UEMUERA_DEBUG
					if (!IsWaitInputState || inputReq == null)
						throw new ExeEE("");
#endif
					if (KillMacro)
						goto endMacro;
				}
			}
			finally
			{
				inProcess = false;
			}
			endMacro:
			if (IsWaitInputState && inputReq.NeedValue)
			{
				Point point = window.MainPicBox.PointToClient(uEmuera.Forms.Control.MousePosition);
				if (window.MainPicBox.ClientRectangle.Contains(point))
					MoveMouse(point);
			}
			RefreshStrings(true);
		}

		private void openErrorFile(ScriptPosition pos)
		{
			ProcessStartInfo pInfo = new ProcessStartInfo();
			pInfo.FileName = Config.TextEditor;
			string fname = pos.Filename.ToUpper();
			if (fname.EndsWith(".CSV"))
			{
				if (fname.Contains(Program.CsvDir.ToUpper()))
					fname = fname.Replace(Program.CsvDir.ToUpper(), "");
				fname = Program.CsvDir + fname;
			}
			else
			{
				//解析モードの場合は見ているファイルがERB\の下にあるとは限らないかつフルパスを持っているのでこの補正はしなくてよい
				if (!Program.AnalysisMode)
				{
					if (fname.Contains(Program.ErbDir.ToUpper()))
						fname = fname.Replace(Program.ErbDir.ToUpper(), "");
					fname = Program.ErbDir + fname;
				}
			}
			switch (Config.EditorType)
			{
				case TextEditorType.SAKURA:
					pInfo.Arguments = "-Y=" + pos.LineNo.ToString() + " \"" + fname + "\"";
					break;
				case TextEditorType.TERAPAD:
					pInfo.Arguments = "/jl=" + pos.LineNo.ToString() + " \"" + fname + "\"";
					break;
				case TextEditorType.EMEDITOR:
					pInfo.Arguments = "/l " + pos.LineNo.ToString() + " \"" + fname + "\"";
					break;
				case TextEditorType.USER_SETTING:
					if (Config.EditorArg != "" && Config.EditorArg != null)
						pInfo.Arguments = Config.EditorArg + pos.LineNo.ToString() + " \"" + fname + "\"";
					else
						pInfo.Arguments = fname;
					break;
			}
			try
			{
				System.Diagnostics.Process.Start(pInfo);
			}
			catch (System.ComponentModel.Win32Exception)
			{
				uEmuera.Media.SystemSounds.Hand.Play();
				PrintError("エディタを開くことができませんでした");
				forceUpdateGeneration();
			}
			return;
		}

        string parseInput(StringStream st, bool isNest)
        {
            StringBuilder sb = new StringBuilder(20);
            StringBuilder num = new StringBuilder(20);
            bool hasRet = false;
            int res = 0;
            while (!st.EOS && (!isNest || st.Current != ')'))
            {
                if (st.Current == '(')
                {
                    st.ShiftNext();
                    string tstr = parseInput(st, true);

                    if (!st.EOS)
                    {
                        st.ShiftNext();
                        if (st.Current == '*')
                        {
                            st.ShiftNext();
                            while (char.IsNumber(st.Current))
                            {
                                num.Append(st.Current);
                                st.ShiftNext();
                            }
                            if (num.ToString() != "" && num.ToString() != null)
                            {
                                int.TryParse(num.ToString(), out res);
                                for (int i = 0; i < res; i++)
                                    sb.Append(tstr);
                                num.Remove(0, num.Length);
                            }
                        }
                        else
                            sb.Append(tstr);
                        continue;
                    }
                    else
                    {
                        sb.Append(tstr);
                        break;
                    }
                }
                else if (st.Current == '\\')
                {
                    st.ShiftNext();
                    switch (st.Current)
                    {
                        case 'n':
                            if (!hasRet)
                                sb.Append('\n');
                            else
                                hasRet = false;
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 'e':
                            sb.Append("\\e\n");
                            hasRet = true;
                            break;
                        case '\n':
                            break;
                        default:
                            sb.Append(st.Current);
                            break;
                    }
                }
                else
                    sb.Append(st.Current);
                st.ShiftNext();
            }
            return sb.ToString();
        }


		volatile bool runningERBfromMemory = false;
		/// <summary>
		/// 通常コンソールからのDebugコマンド、及びデバッグウインドウの変数ウォッチなど、
		/// *.ERBファイルが存在しないスクリプトを実行中
		/// 1750 IsDebugから改名
		/// </summary>
		public bool RunERBFromMemory { get { return runningERBfromMemory; } set { runningERBfromMemory = value; } }
		void doSystemCommand(string command)
		{
			if(timer.Enabled)
			{
				PrintError("タイマー系命令の待ち時間中はコマンドを入力できません");
				PrintError("");//タイマー表示処理に消されちゃうかもしれないので
				RefreshStrings(true);
				return;
			}
			if (IsInProcess)
			{
				PrintError("スクリプト実行中はコマンドを入力できません");
				RefreshStrings(true);
				return;
			}
			StringComparison sc = Config.SCVariable;
			Print(command);
			PrintFlush(false);
			RefreshStrings(true);
			string com = command.Substring(1);
			if (com.Length == 0)
				return;
			if (com.Equals("REBOOT", sc))
			{
				window.Reboot();
				return;
			}
			else if (com.Equals("OUTPUT", sc) || com.Equals("OUTPUTLOG", sc))
			{
				this.OutputLog(Program.ExeDir + "emuera.log");
				return;
			}
			else if ((com.Equals("QUIT", sc)) || (com.Equals("EXIT", sc)))
			{
				window.Close();
				return;
			}
			else if (com.Equals("CONFIG", sc))
			{
				window.ShowConfigDialog();
				return;
			}
			else if (com.Equals("DEBUG", sc))
			{
				if (!Program.DebugMode)
				{
					PrintError("デバッグウインドウは-Debug引数付きで起動したときのみ使えます");
					RefreshStrings(true);
					return;
				}
				OpenDebugDialog();
			}
			else
			{
				if (!Config.UseDebugCommand)
				{
					PrintError("デバッグコマンドを使用できない設定になっています");
					RefreshStrings(true);
					return;
				}
				//処理をDebugMode系へ移動
				DebugCommand(com, Config.ChangeMasterNameIfDebug, false);
				PrintFlush(false);
			}
			RefreshStrings(true);
		}
		#endregion
	}
}
