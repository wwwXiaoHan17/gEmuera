using MinorShift._Library;
using MinorShift.Emuera.Sub;
using System;
using System.Collections.Generic;
//using System.Drawing;
using System.IO;
using System.Text;
//using System.Windows.Forms;
using uEmuera.Drawing;
using uEmuera.Forms;

namespace MinorShift.Emuera.GameView
{
	//1820 EmueraConsoleのうちdisplayLineListやprintBufferに触るもの
	//いつかEmueraConsoleから分離したい
	internal sealed partial class EmueraConsole : IDisposable
	{
        private readonly DisplayLineList displayLineList;
		public bool noOutputLog = false;
		public Color bgColor = Config.BackColor;

		private readonly PrintStringBuffer printBuffer;
		readonly StringMeasure stringMeasure = new StringMeasure();

		public void ClearDisplay()
		{
			lock (displayLineLock)
			{
				displayLineList.Clear();
				logicalLineCount = 0;
				lineNo = 0;
				lastDrawnLineNo = -1;
			}
			ConsumeDisplayRewriteRefresh();
			verticalScrollBarUpdate();
			window.Refresh();//OnPaint発行
		}


		#region Print系

		//private bool useUserStyle = true;
		public bool UseUserStyle { get; set; }
		public bool UseSetColorStyle { get; set; }
		private StringStyle defaultStyle = new StringStyle(Config.ForeColor, FontStyle.Regular, null);
		private StringStyle userStyle = new StringStyle(Config.ForeColor, FontStyle.Regular, null);
		//private StringStyle style = new StringStyle(Config.ForeColor, FontStyle.Regular, null);
		private StringStyle Style
		{
			get
			{
				if (!UseUserStyle)
					return defaultStyle;
				if (UseSetColorStyle)
					return userStyle;
				//PRINTD系(SETCOLORを無視する)
				if (userStyle.Color == defaultStyle.Color)
					return userStyle;
				return new StringStyle(defaultStyle.Color, userStyle.FontStyle, userStyle.Fontname);
			}
		}
		//private StringStyle Style { get { return (useUserStyle ? userStyle : defaultStyle); } }
		public StringStyle StringStyle { get { return userStyle; } }
		public void SetStringStyle(FontStyle fs) { userStyle.FontStyle = fs; }
		public void SetStringStyle(Color color) { userStyle.Color = color; userStyle.ColorChanged = (color != Config.ForeColor); }
		public void SetFont(string fontname) { if (!string.IsNullOrEmpty(fontname)) userStyle.Fontname = fontname; else userStyle.Fontname = Config.FontName; }
		private DisplayLineAlignment alignment = DisplayLineAlignment.LEFT;
		public DisplayLineAlignment Alignment { get { return alignment; } set { alignment = value; } }
		public void ResetStyle()
		{
			userStyle = defaultStyle;
			alignment = DisplayLineAlignment.LEFT;
		}

		public bool EmptyLine { get { return printBuffer.IsEmpty; } }

		/// <summary>
		/// DRAWLINE用文字列
		/// </summary>
		string stBar = null;

		uint lastBgColorChange = 0;
		bool forceTextBoxColor = false;
		public void SetBgColor(Color color)
		{
			this.bgColor = color;
			forceTextBoxColor = true;
			//REDRAWされない場合はTextBoxの色は変えずにフラグだけ立てる
			//最初の再描画時に現在の背景色に合わせる
			if (redraw == ConsoleRedraw.None && window.ScrollBar.Value == window.ScrollBar.Maximum)
				return;
			uint sec = WinmmTimer.TickCount - lastBgColorChange;
			//色変化が速くなりすぎないように一定時間以内の再呼び出しは強制待ちにする
			//while (sec < 200)
			//{
			//	//Application.DoEvents();
			//	sec = WinmmTimer.TickCount - lastBgColorChange;
			//}
			RefreshStrings(true);
			lastBgColorChange = WinmmTimer.TickCount;
		}

		/// <summary>
		/// 最後に描画した時にlineNoの値
		/// </summary>
		int lastDrawnLineNo = -1;
		int lineNo = 0;
		Int64 logicalLineCount = 0;
		public long LineCount { get { return logicalLineCount; } }
		public int GetLineNo { get { return lineNo; } }
		private void addRangeDisplayLine(ConsoleDisplayLine[] lineList)
		{
			for (int i = 0; i < lineList.Length; i++)
				addDisplayLine(lineList[i], false);
		}

		private void applyCurrentLineMetadata(ConsoleDisplayLine[] lineList)
		{
			if (lineList == null)
				return;
			for (int i = 0; i < lineList.Length; i++)
			{
				if (lineList[i] == null)
					continue;
				ApplyCurrentLineMetadata(lineList[i]);
			}
		}

		internal void ApplyCurrentLineMetadata(ConsoleDisplayLine line)
		{
			if (line == null)
				return;
			line.TextBackgroundColor = TextBackgroundColor;
			if (line.Buttons == null)
				return;
			for (int i = 0; i < line.Buttons.Length; i++)
			{
				var parts = line.Buttons[i]?.StrArray;
				if (parts == null)
					continue;
				for (int j = 0; j < parts.Length; j++)
				{
					if (parts[j] is ConsoleDivPart div && div.Children != null)
					{
						for (int k = 0; k < div.Children.Length; k++)
							ApplyCurrentLineMetadata(div.Children[k]);
					}
				}
			}
		}

		private void addDisplayLine(ConsoleDisplayLine line, bool force_LEFT)
		{
			//不適正なFontのチェック
			AConsoleDisplayPart errorStr = null;
            AConsoleDisplayPart css = null;

            var button_count = line.Buttons.Length;
            var button_strcount = 0;
            for(var b=0; b<button_count; ++b)
			{
                ConsoleButtonString button = line.Buttons[b];

                button_strcount = button.StrArray.Length;
                for(var i=0; i<button_strcount; ++i)
				{
                    css = button.StrArray[i];
                    if (css.Error)
					{
						errorStr = css;
						break;
					}
				}
			}
			if (errorStr != null)
			{
				MessageBox.Show("Emueraの表示処理中に不適正なフォントを検出しました\n描画処理を続行できないため強制終了します", "フォント不適正");
				this.Quit();
				return;
			}
			lock (displayLineLock)
			{
				if (LastLineIsTemporary)
					deleteLine(1);
				if (force_LEFT)
					line.SetAlignment(DisplayLineAlignment.LEFT);
				else
					line.SetAlignment(alignment);
				line.LineNo = lineNo;
				// PRINT/PRINTFORM 等不换行输出要和下一次 Flush 的内容保持同一逻辑行。
				// LINECOUNT/CLEARLINE 依赖这个边界；Godot 侧还要沿用旧 LineNo，才能替换已渲染的未结束行。
				if (displayLineList.Count != 0 && !displayLineList[displayLineList.Count - 1].IsLineEnd)
				{
					ConsoleDisplayLine lastLine = displayLineList[displayLineList.Count - 1];
					int mergedLineNo = lastLine.LineNo;
					ConsoleButtonString[] lastButtons = lastLine.Buttons ?? new ConsoleButtonString[0];
					ConsoleButtonString[] currentButtons = line.Buttons ?? new ConsoleButtonString[0];
					if (lastButtons.Length > 0 && currentButtons.Length > 0)
					{
						ConsoleButtonString lastButton = lastButtons[lastButtons.Length - 1];
						line.ShiftPositionX(lastButton.PointX + lastButton.Width);
					}
					deleteLine(1);
					line.LineNo = mergedLineNo;
					line.ChangeStr(mergeDisplayLineButtons(lastButtons, currentButtons));
				}
				displayLineList.Add(line);
				lineNo++;
				if (line.IsLogicalLine && line.IsLineEnd)
					logicalLineCount++;
				if (lineNo == int.MaxValue)
				{
					lastDrawnLineNo = -1;
					lineNo = 0;
				}
				if (logicalLineCount == long.MaxValue)
				{
					logicalLineCount = 0;
				}
				if (displayLineList.Count > Config.MaxLog)
					displayLineList.RemoveAt(0);
			}
		}

		private static ConsoleButtonString[] mergeDisplayLineButtons(ConsoleButtonString[] first, ConsoleButtonString[] second)
		{
			int firstLength = first == null ? 0 : first.Length;
			int secondLength = second == null ? 0 : second.Length;
			ConsoleButtonString[] merged = new ConsoleButtonString[firstLength + secondLength];
			if (firstLength > 0)
				Array.Copy(first, 0, merged, 0, firstLength);
			if (secondLength > 0)
				Array.Copy(second, 0, merged, firstLength, secondLength);
			return merged;
		}


		public void deleteLine(int argNum)
		{
			int delNum = 0;
			lock (displayLineLock)
			{
				int num = argNum;
				while (delNum < num)
				{
					if (displayLineList.Count == 0)
						break;
					ConsoleDisplayLine line = displayLineList[displayLineList.Count - 1];
					displayLineList.RemoveAt(displayLineList.Count - 1);
					lineNo--;
					if (line.IsLogicalLine)
					{
						delNum++;
						if (line.IsLineEnd)
							logicalLineCount--;
					}
					// 参考侧 GETDISPLAYLINE 修正：深度历史删除时从打印缓冲生成 dummy 行插入顶部，
					// 防止 lineNo 与显示行错位（BufferToSingleLine 消费打印缓冲）
					if (displayLineList.Count == Config.MaxLog - 2 && lineNo > displayLineList.Count)
					{
						ConsoleDisplayLine dummyline = BufferToSingleLine(true, false);
						displayLineList.Insert(0, dummyline);
					}
				}
				// 参考侧下溢补偿：列表已空但未删够时，把差值从逻辑行数中扣除（LINECOUNT 语义）
				if (delNum < num)
				{
					lineNo = 0;
					logicalLineCount -= num - delNum;
				}
				if (lineNo < 0)
					lineNo += int.MaxValue;
				lastDrawnLineNo = -1;
				// 参考侧 MaxLog 超额补偿：CLEARLINE 会补充新行使列表超 MaxLog，此时只移除最旧一行
				if (displayLineList.Count == Config.MaxLog)
					displayLineList.RemoveAt(0);
			}
			if (delNum > 0)
				MarkDisplayRewriteInProgress();
			//RefreshStrings(true);
		}

		public bool LastLineIsTemporary
		{
			get
			{
				lock (displayLineLock)
				{
					if (displayLineList.Count == 0)
						return false;
					return displayLineList[displayLineList.Count - 1].IsTemporary;
				}
			}
		}

        //空行であるかのチェック
        public bool LastLineIsEmpty
        {
            get
            {
                lock (displayLineLock)
                {
                    if (displayLineList.Count == 0)
                        return false;
                    return string.IsNullOrEmpty(displayLineList[displayLineList.Count - 1].ToString().Trim());
                }
            }
        }

        //最終行を書き換え＋次の行追加時にはその行を再利用するように設定
        public void PrintTemporaryLine(string str)
		{
			PrintSingleLine(str, true);
		}

		//最終行だけを書き換える
		private void changeLastLine(string str)
		{
			deleteLine(1);
			PrintSingleLine(str, false);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="str"></param>
		/// <param name="position"></param>
		/// <param name="level">警告レベル.0:軽微なミス.1:無視できる行.2:行が実行されなければ無害.3:致命的</param>
		public void PrintWarning(string str, ScriptPosition position, int level)
		{
			if (level < Config.DisplayWarningLevel && !Program.AnalysisMode)
				return;
			//警告だけは強制表示
			bool b = force_temporary;
			force_temporary = false;
			if (position != null)
			{
				if (position.LineNo >= 0)
				{
					PrintErrorButton(string.Format("警告Lv{0}:{1}:{2}行目:{3}", level, position.Filename, position.LineNo, str), position, level);
					GlobalStatic.Process.printRawLine(position);
				}
				else
					PrintErrorButton(string.Format("警告Lv{0}:{1}:{2}", level, position.Filename, str), position, level);

			}
			else
			{
				PrintError(string.Format("警告Lv{0}:{1}", level, str));
			}
			force_temporary = b;
		}



		/// <summary>
		/// ユーザー指定のフォントを無視する。ウィンドウサイズを考慮せず確実に一行で書く。システム用。
		/// </summary>
		/// <param name="str"></param>
		public void PrintSystemLine(string str)
		{
			PrintFlush(false);
			//RefreshStrings(false);
			UseUserStyle = false;
			PrintSingleLine(str, false);
		}
		public void PrintError(string str)
		{
			if (string.IsNullOrEmpty(str))
				return;
			Program.AppendSnakeStartupErrorLog(str);
			if (Program.DebugMode)
			{
				this.DebugPrint(str);
				this.DebugNewLine();
			}
			PrintFlush(false);
			UseUserStyle = false;
			ConsoleDisplayLine dispLine = PrintPlainwithSingleLine(str);
			if (dispLine == null)
				return;
			addDisplayLine(dispLine, true);
			RefreshStrings(false);
		}

		internal void PrintErrorButton(string str, ScriptPosition pos, int level = 0)
		{
			if (string.IsNullOrEmpty(str))
				return;
			Program.AppendSnakeStartupErrorLog(str);
			if (Program.DebugMode)
			{
				this.DebugPrint(str);
				this.DebugNewLine();
			}
			UseUserStyle = false;
			// 参考侧按警告级别着色：Lv0-2 淡黄 (255,255,255,160)、Lv3+ 红。
			// 参考侧通过引用别名直接改写 Style（连带污染默认样式），此处用结构体副本保持语义、避免副作用。
			StringStyle errStyle = Style;
			errStyle.Color = level >= 3 ? Color.FromArgb(255, 255, 0, 0) : Color.FromArgb(255, 255, 255, 160);
			ConsoleDisplayLine dispLine = printBuffer.AppendAndFlushErrButton(str, errStyle, ErrorButtonsText, pos, stringMeasure);
			if (dispLine == null)
				return;
			addDisplayLine(dispLine, true);
			RefreshStrings(false);
		}

		/// <summary>
		/// 1813 従来のPrintLineを用途を考慮してPrintSingleLineとPrintSystemLineに分割
		/// </summary>
		/// <param name="str"></param>
		public void PrintSingleLine(string str) { PrintSingleLine(str, false); }
		public void PrintSingleLine(string str, bool temporary)
		{
			if (string.IsNullOrEmpty(str))
				return;
			PrintFlush(false);
			printBuffer.Append(str, Style);
			ConsoleDisplayLine dispLine = BufferToSingleLine(true, temporary);
			if (dispLine == null)
				return;
			addDisplayLine(dispLine, false);
			RefreshStrings(false);
		}

		public void Print(string str)
		{
			Print(str, true);
		}

		public void Print(string str, bool lineEnd)
		{
			if (string.IsNullOrEmpty(str))
				return;
			if (str.Contains("\n"))
			{
				int newline = str.IndexOf('\n');
				string upper = str.Substring(0, newline);
				printBuffer.Append(upper, Style);
				NewLine();
				if (newline < str.Length - 1)
				{
					string lower = str.Substring(newline + 1);
					Print(lower);
				}
				return;
			}
			printBuffer.Append(str, Style, false, lineEnd);
			return;
		}

		
			public void PrintImg(string str)
			{
				printBuffer.Append(new ConsoleImagePart(str, null, 0, 0, 0));
			}

			public void PrintImg(string name, string buttonName, string mappingName, MixedNum height, MixedNum width, MixedNum ypos)
			{
				printBuffer.Append(new ConsoleImagePart(name, buttonName, mappingName, height, width, ypos));
			}

			public void PrintShape(string type, int[] param)
			{
				ConsoleShapePart part = ConsoleShapePart.CreateShape(type, param, userStyle.Color, userStyle.ButtonColor, false);
				printBuffer.Append(part);
			}

			public void PrintShape(string type, MixedNum[] param)
			{
				ConsoleShapePart part = ConsoleShapePart.CreateShape(type, param, userStyle.Color, userStyle.ButtonColor, false);
				printBuffer.Append(part);
			}

		public void PrintHtml(string str)
		{
			PrintHtml(str, false);
		}

		public void PrintHtml(string str, bool toPrintBuffer)
		{
			if (string.IsNullOrEmpty(str))
				return;
			if (!this.Enabled)
				return;
			if (toPrintBuffer)
			{
				foreach (ConsoleButtonString button in HtmlManager.Html2ButtonList(str, stringMeasure, this))
					printBuffer.AppendButton(button);
			}
			else
			{
				if (!printBuffer.IsEmpty)
				{
					ConsoleDisplayLine[] dispList = printBuffer.Flush(stringMeasure, force_temporary);
					addRangeDisplayLine(dispList);
				}
				ConsoleDisplayLine[] htmlLines = HtmlManager.Html2DisplayLine(str, stringMeasure, this);
				applyCurrentLineMetadata(htmlLines);
				addRangeDisplayLine(htmlLines);
			}
			RefreshStrings(false);
		}

		public void PrintHtmlC(string str, bool alignmentRight, int cellWidthPx)
		{
			if (string.IsNullOrEmpty(str))
				return;
			if (!this.Enabled)
				return;

			if (cellWidthPx <= 0)
				cellWidthPx = Config.PrintCLength * Config.FontSize / 2;

			ConsoleButtonString[] buttons = HtmlManager.Html2ButtonList(str, stringMeasure, this);
			if (buttons.Length == 0)
				return;

			int contentWidth = 0;
			foreach (ConsoleButtonString button in buttons)
			{
				if (button == null)
					continue;
				button.CalcWidth(stringMeasure, 0);
				contentWidth += Math.Max(0, button.Width);
			}

			appendPrintCCell(contentWidth, cellWidthPx, alignmentRight, () =>
			{
				foreach (ConsoleButtonString button in buttons)
				{
					if (button == null)
						continue;
					printBuffer.AppendButton(button);
				}
			});
		}

		private void appendHtmlCellSpace(int width)
		{
			RectangleF spaceRect = new RectangleF(0, 0, width, Config.FontSize);
			ConsoleSpacePart spacePart = new ConsoleSpacePart(spaceRect);
			spacePart.SetWidth(stringMeasure, 0);
			ConsoleButtonString spaceButton = new ConsoleButtonString(this, new AConsoleDisplayPart[] { spacePart });
			spaceButton.CalcWidth(stringMeasure, 0);
			printBuffer.AppendButton(spaceButton);
		}

		private int printCWidth = -1;
		// PRINTC/PRINTBUTTONC 在 Godot 侧按像素宽度分栏，避免全半角混排时列宽漂移。
		// 这里单独跟踪缓冲中由 PRINTC 累积的宽度，因为 force_button 路径的按钮宽度要到 Flush 前才会补齐。
		private int printCCurrentLinePx = 0;
		public void PrintC(string str, bool alignmentRight)
		{
			if (string.IsNullOrEmpty(str))
				return;

			// v24 参考：PRINTC 列宽按 SJIS 字节基准补空格（CreateTypeCString），
			// 像素测量仅用于超宽回删；snake 参考为像素分栏（appendPrintCCell）。
			if (!Program.Compatibility.Snake.IsEnabled)
			{
				printBuffer.Append(CreateTypeCV24String(str, alignmentRight), Style, true);
				return;
			}

			if (printCWidth == -1)
				calcPrintCWidth(stringMeasure);

			Font font = Config.Font;
			int contentWidth = stringMeasure.GetDisplayLength(str, font);
			appendPrintCCell(contentWidth, printCWidth, alignmentRight, () => printBuffer.Append(str, Style, true));
		}

		private int printCWidthL = -1;

		/// <summary>
		/// v24 参考的 CreateTypeCString 移植：列宽按字节宽（LangManager.GetStrlenLang，
		/// SJIS 半角 1/全角 2 口径；与参考硬编码 Shift-JIS GetByteCount 在代理对/稀有字上
		/// 有 ±1 字节级近似）补空格——右对齐补 PrintCLength、左对齐补 PrintCLength+1；
		/// 补出后按像素宽度回删前导/尾随空格（printCWidth/printCWidthL 阈值；测量字体用
		/// Config.Font，参考按样式字体 new Font(Style.Fontname,...)——非默认样式下回删
		/// 阈值的已知近似）。
		/// </summary>
		private string CreateTypeCV24String(string str, bool alignmentRight)
		{
			if (printCWidth == -1 || printCWidthL == -1)
				calcPrintCWidth(stringMeasure);
			int length = LangManager.GetStrlenLang(str);
			int printcLength = Config.PrintCLength;
			Font font = Config.Font;
			if (alignmentRight && length < printcLength)
			{
				str = new string(' ', printcLength - length) + str;
				int width = stringMeasure.GetDisplayLength(str, font);
				while (width > printCWidth)
				{
					if (str[0] != ' ')
						break;
					str = str.Remove(0, 1);
					width = stringMeasure.GetDisplayLength(str, font);
				}
			}
			else if (!alignmentRight && length < printcLength + 1)
			{
				str += new string(' ', printcLength + 1 - length);
				int width = stringMeasure.GetDisplayLength(str, font);
				while (width > printCWidthL)
				{
					if (str[str.Length - 1] != ' ')
						break;
					str = str.Remove(str.Length - 1, 1);
					width = stringMeasure.GetDisplayLength(str, font);
				}
			}
			return str;
		}

		private void calcPrintCWidth(StringMeasure stringMeasure)
		{
			string str = new string(' ', Config.PrintCLength);
			Font font = Config.Font;
			printCWidth = stringMeasure.GetDisplayLength(str, font);
			printCWidthL = stringMeasure.GetDisplayLength(str, font);
		}

		private void appendPrintCCell(int contentWidth, int cellWidth, bool alignmentRight, Action appendContent)
		{
			if (appendContent == null)
				return;

			int currentPx = getPrintCCurrentLinePx();
			int maxLineWidth = Config.DrawableWidth;
			bool fullColumnFits = currentPx + cellWidth <= maxLineWidth;
			bool contentFits = currentPx + contentWidth <= maxLineWidth;

			// 参考侧（snake PrintC/ButtonC 三处同款）：剩余宽度装不下"内容"即换行；
			// 装得下内容但装不下整列时不换行、不补白（fullColumnFits=false 抑制 padding），
			// 并把行累计标记为满行（maxLineWidth）。
			if (currentPx > 0 && !contentFits)
			{
				flushPrintBufferForPrintC();
				currentPx = 0;
				fullColumnFits = true;
			}

			int padPx = cellWidth - contentWidth;
			if (alignmentRight && padPx > 0 && fullColumnFits)
				appendHtmlCellSpace(padPx);

			appendContent();

			if (!alignmentRight && padPx > 0 && fullColumnFits)
				appendHtmlCellSpace(padPx);

			// HTML_PRINTC/PRINTC 可以混用。列累计以本 helper 为准，CurrentLineWidth
			// 只作为外部按钮已经计算过宽度时的下限，避免结算表格后续列回到错误的起点。
			printCCurrentLinePx = fullColumnFits ? currentPx + cellWidth : maxLineWidth;
		}

		private int getPrintCCurrentLinePx()
		{
			if (printBuffer.IsEmpty)
			{
				printCCurrentLinePx = 0;
				return 0;
			}

			int actualWidth = printBuffer.CurrentLineWidth;
			if (actualWidth > printCCurrentLinePx)
				printCCurrentLinePx = actualWidth;
			return printCCurrentLinePx;
		}

		private void flushPrintBufferForPrintC()
		{
			ConsoleDisplayLine[] dispList = printBuffer.Flush(stringMeasure, force_temporary);
			addRangeDisplayLine(dispList);
			printCCurrentLinePx = 0;
		}

		internal void PrintButton(string str, string p)
		{
			if (string.IsNullOrEmpty(str))
				return;
			printBuffer.AppendButton(str, Style, p);
		}
		internal void PrintButton(string str, long p)
		{
			if (string.IsNullOrEmpty(str))
				return;
			printBuffer.AppendButton(str, Style, p);
		}
		internal void PrintButtonC(string str, string p, bool isRight)
		{
			if (string.IsNullOrEmpty(str))
				return;

			// v24 参考：PRINTBUTTONC 同样走 CreateTypeCString 字节基准补白
			if (!Program.Compatibility.Snake.IsEnabled)
			{
				printBuffer.AppendButton(CreateTypeCV24String(str, isRight), Style, p);
				return;
			}

			if (printCWidth == -1)
				calcPrintCWidth(stringMeasure);

			Font font = Config.Font;
			int contentWidth = stringMeasure.GetDisplayLength(str, font);
			appendPrintCCell(contentWidth, printCWidth, isRight, () => printBuffer.AppendButton(str, Style, p));
		}
		internal void PrintButtonC(string str, long p, bool isRight)
		{
			if (string.IsNullOrEmpty(str))
				return;

			// v24 参考：PRINTBUTTONC 同样走 CreateTypeCString 字节基准补白
			if (!Program.Compatibility.Snake.IsEnabled)
			{
				printBuffer.AppendButton(CreateTypeCV24String(str, isRight), Style, p);
				return;
			}

			if (printCWidth == -1)
				calcPrintCWidth(stringMeasure);

			Font font = Config.Font;
			int contentWidth = stringMeasure.GetDisplayLength(str, font);
			appendPrintCCell(contentWidth, printCWidth, isRight, () => printBuffer.AppendButton(str, Style, p));
		}

		internal void PrintPlain(string str)
		{
			if (string.IsNullOrEmpty(str))
				return;
			printBuffer.AppendPlainText(str, Style);
		}

		public void NewLine()
		{
			PrintFlush(true);
			RefreshStrings(false);
		}

		public ConsoleDisplayLine BufferToSingleLine(bool force, bool temporary)
		{
			if (!this.Enabled)
				return null;
			if (!force && printBuffer.IsEmpty)
				return null;
			if (force && printBuffer.IsEmpty)
				printBuffer.Append(" ", Style);
			ConsoleDisplayLine dispLine = printBuffer.FlushSingleLine(stringMeasure, temporary | force_temporary);
			return dispLine;
		}

		internal ConsoleDisplayLine PrintPlainwithSingleLine(string str)
		{
			if (!this.Enabled)
				return null;
			if (string.IsNullOrEmpty(str))
				return null;
			printBuffer.AppendPlainText(str, Style);
			ConsoleDisplayLine dispLine = printBuffer.FlushSingleLine(stringMeasure, false);
			return dispLine;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="force">バッファーが空でも改行する</param>
		public void PrintFlush(bool force)
		{
			if (!this.Enabled)
				return;
			if (!force && printBuffer.IsEmpty)
				return;
			if (force && printBuffer.IsEmpty)
				printBuffer.Append(" ", Style);
			ConsoleDisplayLine[] dispList = printBuffer.Flush(stringMeasure, force_temporary);
			//ConsoleDisplayLine[] dispList = printBuffer.Flush(stringMeasure, temporary | force_temporary);
			addRangeDisplayLine(dispList);
			printCCurrentLinePx = 0;
			//1819描画命令は分離
			//RefreshStrings(false);
		}

		/// <summary>
		/// DRAWLINE命令に対応。これのフォントを変更できると面倒なことになるのでRegularに固定する。
		/// </summary>
		public void PrintBar()
		{
			//初期に設定済みなので見る必要なし
			//if (stBar == null)
			//    setStBar(StaticConfig.DrawLineString);

			//1806beta001 CompatiDRAWLINEの廃止、CompatiLinefeedAs1739へ移行
			//CompatiLinefeedAs1739の処理はPrintStringBuffer.csで行う
			//if (Config.CompatiDRAWLINE)
			//	PrintFlush(false);
			StringStyle ss = userStyle;
			userStyle.FontStyle = FontStyle.Regular;
			Print(stBar);
			userStyle = ss;
		}

		public void printCustomBar(string barStr)
		{
			printCustomBar(barStr, false);
		}

		public void printCustomBar(string barStr, bool isConst)
		{
			if (string.IsNullOrEmpty(barStr))
				throw new CodeEE("空文字列によるDRAWLINEが行われました");
			StringStyle ss = userStyle;
			userStyle.FontStyle = FontStyle.Regular;
			if (isConst)
				Print(barStr);
			else
				Print(getStBar(barStr));
			userStyle = ss;
		}

		public string getDefStBar()
		{
			return stBar;
		}

		public string getStBar(string barStr)
		{
			StringBuilder bar = new StringBuilder();
			bar.Append(barStr);
			int width = 0;
			Font font = Config.Font;
			while (width < Config.DrawableWidth)
			{//境界を越えるまで一文字ずつ増やす
				bar.Append(barStr);
				width = stringMeasure.GetDisplayLength(bar.ToString(), font);
			}
			while (width > Config.DrawableWidth)
			{//境界を越えたら、今度は超えなくなるまで一文字ずつ減らす（barStrに複数字の文字列がきた場合に対応するため）
				bar.Remove(bar.Length - 1, 1);
				width = stringMeasure.GetDisplayLength(bar.ToString(), font);
			}
			return bar.ToString();
		}

		public void setStBar(string barStr)
		{
			stBar = getStBar(barStr);
		}
		#endregion


		private bool outputLog(string fullpath, bool hideInfo)
		{
			StreamWriter writer = null;
			try
			{
				writer = new StreamWriter(fullpath, false, Encoding.UTF8);
				if (!hideInfo)
				{
					writer.WriteLine("Environment Information");
					writer.WriteLine("gemuera Godot runtime");
					if (GlobalStatic.GameBaseData != null)
						writer.WriteLine(GlobalStatic.GameBaseData.ScriptWindowTitle);
					writer.WriteLine();
					writer.WriteLine("Log");
					writer.WriteLine();
				}
				ConsoleDisplayLine[] lines;
					lock (displayLineLock)
					{
						lines = new ConsoleDisplayLine[displayLineList.Count];
						displayLineList.CopyTo(lines, 0);
					}
					foreach (ConsoleDisplayLine line in lines)
				{
					writer.WriteLine(line.ToLogString());
				}
			}
			catch (Exception)
			{
				MessageBox.Show("ログの出力に失敗しました", "ログ出力失敗");
				return false;
			}
			finally
			{
				if (writer != null)
					writer.Close();
			}
			return true;
		}


		public bool OutputLog(string filename)
		{
			return OutputLog(filename, false);
		}

		public bool OutputLog(string filename, bool hideInfo)
		{
			string baseDir = Path.GetFullPath(Program.ExeDir ?? "");
			if (!baseDir.EndsWith(Path.DirectorySeparatorChar.ToString()) && !baseDir.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
				baseDir += Path.DirectorySeparatorChar;

			bool runnerDefaultLogRedirected = Program.TryResolveLegacyRunnerDefaultOutputLogPath(filename, out string runnerDefaultLogPath);
			if (runnerDefaultLogRedirected)
				filename = runnerDefaultLogPath;
			else if (string.IsNullOrEmpty(filename))
				filename = Path.Combine(baseDir, "emuera.log");
			else if (!Path.IsPathRooted(filename))
				filename = Path.Combine(baseDir, filename);
			filename = Path.GetFullPath(filename);

			if (!runnerDefaultLogRedirected && !filename.StartsWith(baseDir, StringComparison.CurrentCultureIgnoreCase))
            {
                MessageBox.Show("ログファイルは実行ファイル以下のディレクトリにのみ保存できます", "ログ出力失敗");
                return false;
            }

			if (outputLog(filename, hideInfo))
			{
				// 游戏报错自动触发 save_log：仅引擎自发的 emuera.log（basename 精确匹配且
				// 非隐藏信息模式），不误触游戏 OUTPUTLOG 自定义名与菜单带时间戳快照。
				// 会话内首次触发；导出经主线程队列执行（legacy-runner 场景导出落在
				// runtime-game 副本内，随副本清理，不污染真机目录）。
				if (!hideInfo
					&& string.Equals(Path.GetFileName(filename), "emuera.log", StringComparison.OrdinalIgnoreCase))
				{
					global::GenericUtils.AutoExportDiagnosticOnEmueraLog(filename);
				}
				if (window.Created)
				{
					string displayFilename = runnerDefaultLogRedirected
						? Path.Combine(baseDir, "emuera.log")
						: filename;
					PrintSystemLine("※※※ログファイルを" + displayFilename + "に出力しました※※※");
					RefreshStrings(true);
				}
				return true;
			}
			else
				return false;
		}

		public void GetDisplayStrings(StringBuilder builder)
		{
			ConsoleDisplayLine[] lines;
			lock (displayLineLock)
			{
				if (displayLineList.Count == 0)
					return;
				lines = new ConsoleDisplayLine[displayLineList.Count];
				displayLineList.CopyTo(lines, 0);
			}
			for (int i = 0; i < lines.Length; i++)
			{
				builder.AppendLine(lines[i].ToString());
			}
		}

		public ConsoleDisplayLine[] GetDisplayLines(Int64 lineNo)
		{
			ConsoleDisplayLine[] lines;
			lock (displayLineLock)
			{
				if (lineNo < 0 || lineNo > displayLineList.Count)
					return null;
				lines = new ConsoleDisplayLine[displayLineList.Count];
				displayLineList.CopyTo(lines, 0);
			}
			int count = 0;
			List<ConsoleDisplayLine> list = new List<ConsoleDisplayLine>();
			for (int i = lines.Length - 1; i >= 0; i--)
			{
				if (count == lineNo)
					list.Insert(0, lines[i]);
				if (lines[i].IsLogicalLine)
					count++;
				if (count > lineNo)
					break;
			}
			if (list.Count == 0)
				return null;
			ConsoleDisplayLine[] ret = new ConsoleDisplayLine[list.Count];
			list.CopyTo(ret);
			return ret;
		}

		public string GetDisplayLineText(Int64 lineNo)
		{
			lock (displayLineLock)
			{
				if (lineNo < 0 || lineNo >= displayLineList.Count)
					return "";
				return displayLineList[(int)lineNo].ToString();
			}
		}

		public ConsoleDisplayLine[] PopDisplayingLines()
		{
			if (!this.Enabled)
				return null;
			if (printBuffer.IsEmpty)
				return null;
			return  printBuffer.Flush(stringMeasure, force_temporary);
		}
		
	}
}
