using System;
using System.Collections.Generic;
using System.IO;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameView
{
	internal sealed class HotkeyState
	{
		const int ShiftModifier = 0x00010000;
		const int ControlModifier = 0x00020000;
		const int AltModifier = 0x00040000;

		readonly object syncRoot = new object();
		bool enabled;
		bool availableStateArray;
		bool availableHotkeyFile;
		long[] state = Array.Empty<long>();
		List<int> bytecode = new List<int>(0x200);

		enum Eval
		{
			Number = 0,
			KeyCompare = 1,
			StateCompare = 2,
			StateGet = 3,
		}

		enum Command
		{
			Return = 0,
			GotoIfNot = 1,
		}

		public void Initialize(long size)
		{
			if (size < 0 || size > int.MaxValue)
				throw new CodeEE("HOTKEY_STATE_INITのサイズが範囲外です");
			lock (syncRoot)
			{
				state = new long[(int)size];
				availableStateArray = true;
			}
		}

		public void Set(long index, long value)
		{
			lock (syncRoot)
			{
				if (!availableStateArray)
					throw new CodeEE("HOTKEY_STATE_INITを先に呼び出す必要があります");
				if (index < 0 || index >= state.Length)
					throw new CodeEE("HOTKEY_STATEのインデックスが範囲外です");
				state[index] = value;
			}
		}

		public bool Toggle(out string message)
		{
			lock (syncRoot)
			{
				if (enabled)
				{
					enabled = false;
					message = "hotkeys OFF";
					return true;
				}

				if (availableHotkeyFile)
				{
					enabled = true;
					message = availableStateArray ? "hotkeys ON" : "hotkeys ON, HOTKEY_STATE_INIT not called.";
					return true;
				}

				string filename = ResolveHotkeyFile();
				if (string.IsNullOrEmpty(filename))
				{
					message = "HOTKEY.ERB not found.";
					return false;
				}

				try
				{
					string[] lines = uEmuera.Utils.ReadAllLines(filename, Config.Encode);
					List<int> parsed = Parse(lines);
					bytecode = parsed;
					availableHotkeyFile = true;
					enabled = true;
					message = availableStateArray ? "hotkeys ON" : "hotkeys ON, HOTKEY_STATE_INIT not called.";
					return true;
				}
				catch (Exception ex)
				{
					enabled = false;
					availableHotkeyFile = false;
					bytecode.Clear();
					message = "HOTKEY.ERB parsing failed: " + ex.Message;
					return false;
				}
			}
		}

		public bool TryEvaluate(int keyData, out long result)
		{
			lock (syncRoot)
			{
				result = -1;
				if (!enabled || !availableStateArray || !availableHotkeyFile || bytecode.Count == 0)
					return false;

				try
				{
					for (int i = 0; i < bytecode.Count;)
					{
						Command command = (Command)ReadBytecode(ref i);
						if (command == Command.GotoIfNot)
						{
							int target = ReadBytecode(ref i);
							long eval = InterpreterEval(ref i, keyData);
							if (eval == 0)
							{
								if (target < 0 || target > bytecode.Count)
									throw new CodeEE("HOTKEY.ERB bytecode jump is out of range");
								i = target;
							}
						}
						else if (command == Command.Return)
						{
							result = InterpreterEval(ref i, keyData);
							return result != -1;
						}
						else
						{
							throw new CodeEE("HOTKEY.ERB bytecode command is invalid");
						}
					}
				}
				catch (Exception ex)
				{
					global::GenericUtils.Warn(global::EmueraLogCategory.Input,
						() => "[HOTKEY] interpreter failed: " + ex.GetType().Name + ": " + ex.Message);
				}
				result = -1;
				return false;
			}
		}

		static string ResolveHotkeyFile()
		{
			string filename = Path.Combine(Program.ExeDir ?? "", "HOTKEY.ERB");
			if (!uEmuera.Utils.FileExists(filename))
				return null;
			return uEmuera.Utils.ResolveExistingFilePath(filename);
		}

		static List<int> Parse(string[] lines)
		{
			if (lines == null || lines.Length == 0)
				throw new CodeEE("HOTKEY.ERB is empty");

			int startLine = FindHeaderLine(lines);
			if (startLine < 0)
				throw new CodeEE("HOTKEY.ERB must start with @HOTKEY(KEY)");

			var parsed = new List<int>(0x200);
			var gotoStack = new Stack<int>();
			bool lastLineWasSif = false;

			for (int lineIndex = startLine + 1; lineIndex < lines.Length; lineIndex++)
			{
				string line = StripComment(lines[lineIndex]);
				if (line.Length == 0)
					continue;

				if (line.StartsWith("IF ", StringComparison.OrdinalIgnoreCase))
				{
					parsed.Add((int)Command.GotoIfNot);
					gotoStack.Push(parsed.Count);
					parsed.Add(-1);
					ParseEval(line.Substring(3).Trim(), parsed);
				}
				else if (line.StartsWith("RETURN ", StringComparison.OrdinalIgnoreCase))
				{
					parsed.Add((int)Command.Return);
					ParseEval(line.Substring(7).Trim(), parsed);
				}
				else if (line.StartsWith("SIF ", StringComparison.OrdinalIgnoreCase))
				{
					if (lastLineWasSif)
						PatchLastGoto(parsed, gotoStack);
					lastLineWasSif = true;
					parsed.Add((int)Command.GotoIfNot);
					gotoStack.Push(parsed.Count);
					parsed.Add(-1);
					ParseEval(line.Substring(4).Trim(), parsed);
					continue;
				}
				else if (string.Equals(line, "ENDIF", StringComparison.OrdinalIgnoreCase))
				{
					PatchLastGoto(parsed, gotoStack);
				}
				else
				{
					throw new CodeEE("HOTKEY.ERB invalid line: " + line);
				}

				if (lastLineWasSif)
				{
					PatchLastGoto(parsed, gotoStack);
					lastLineWasSif = false;
				}
			}

			if (lastLineWasSif)
				PatchLastGoto(parsed, gotoStack);
			if (gotoStack.Count != 0)
				throw new CodeEE("HOTKEY.ERB has unmatched IF");

			return parsed;
		}

		static int FindHeaderLine(string[] lines)
		{
			for (int i = 0; i < lines.Length; i++)
			{
				string line = StripComment(lines[i]);
				if (line.Length == 0)
					continue;
				return string.Equals(line.TrimStart('\uFEFF'), "@HOTKEY(KEY)", StringComparison.OrdinalIgnoreCase) ? i : -1;
			}
			return -1;
		}

		static string StripComment(string line)
		{
			if (line == null)
				return "";
			int semicolonIndex = line.IndexOf(';');
			if (semicolonIndex >= 0)
				line = line.Substring(0, semicolonIndex);
			return line.Trim().TrimStart('\uFEFF');
		}

		static void PatchLastGoto(List<int> parsed, Stack<int> gotoStack)
		{
			if (gotoStack.Count == 0)
				throw new CodeEE("HOTKEY.ERB has unmatched ENDIF");
			parsed[gotoStack.Pop()] = parsed.Count;
		}

		static void ParseEval(string line, List<int> parsed)
		{
			if (line.StartsWith("KEY == KEYS:", StringComparison.OrdinalIgnoreCase))
			{
				string keyName = line.Substring("KEY == KEYS:".Length).Trim();
				if (!TryParseWindowsKeyData(keyName, out int keyData))
					throw new CodeEE("HOTKEY.ERB unknown key: " + keyName);
				parsed.Add((int)Eval.KeyCompare);
				parsed.Add(keyData);
				return;
			}

			if (line.StartsWith("STATE:", StringComparison.OrdinalIgnoreCase))
			{
				string body = line.Substring("STATE:".Length).Trim();
				int equalIndex = body.IndexOf(" == ", StringComparison.Ordinal);
				if (equalIndex >= 0)
				{
					int stateIndex = ParseInt(body.Substring(0, equalIndex).Trim());
					int compareValue = ParseInt(body.Substring(equalIndex + 4).Trim());
					parsed.Add((int)Eval.StateCompare);
					parsed.Add(stateIndex);
					parsed.Add(compareValue);
					return;
				}
				parsed.Add((int)Eval.StateGet);
				parsed.Add(ParseInt(body));
				return;
			}

			parsed.Add((int)Eval.Number);
			parsed.Add(ParseInt(line));
		}

		static int ParseInt(string value)
		{
			if (!int.TryParse(value, out int parsed))
				throw new CodeEE("HOTKEY.ERB invalid number: " + value);
			return parsed;
		}

		long InterpreterEval(ref int i, int keyData)
		{
			Eval eval = (Eval)ReadBytecode(ref i);
			if (eval == Eval.Number)
				return ReadBytecode(ref i);
			if (eval == Eval.KeyCompare)
				return keyData == ReadBytecode(ref i) ? 1 : 0;
			if (eval == Eval.StateCompare)
			{
				int index = ReadBytecode(ref i);
				int compareValue = ReadBytecode(ref i);
				return GetStateValue(index) == compareValue ? 1 : 0;
			}
			if (eval == Eval.StateGet)
				return GetStateValue(ReadBytecode(ref i));
			throw new CodeEE("HOTKEY.ERB bytecode eval is invalid");
		}

		int ReadBytecode(ref int i)
		{
			if (i < 0 || i >= bytecode.Count)
				throw new CodeEE("HOTKEY.ERB bytecode read is out of range");
			return bytecode[i++];
		}

		long GetStateValue(int index)
		{
			if (index < 0 || index >= state.Length)
				return 0;
			return state[index];
		}

		static bool TryParseWindowsKeyData(string value, out int keyData)
		{
			keyData = 0;
			if (int.TryParse(value, out int numeric))
			{
				keyData = numeric;
				return true;
			}

			int modifiers = 0;
			int keyCode = 0;
			string[] parts = value.Split(new[] { '+', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0)
				parts = new[] { value };

			foreach (string rawPart in parts)
			{
				string part = rawPart.Trim();
				if (part.StartsWith("KEYS:", StringComparison.OrdinalIgnoreCase))
					part = part.Substring(5).Trim();
				if (part.StartsWith("Keys.", StringComparison.OrdinalIgnoreCase))
					part = part.Substring(5).Trim();

				if (TryParseModifier(part, out int modifier))
				{
					modifiers |= modifier;
					continue;
				}
				if (!TryParseKeyCode(part, out keyCode))
					return false;
			}

			if (keyCode == 0)
				return false;
			keyData = keyCode | modifiers;
			return true;
		}

		static bool TryParseModifier(string part, out int modifier)
		{
			modifier = 0;
			if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
				modifier = ShiftModifier;
			else if (string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase))
				modifier = ControlModifier;
			else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
				modifier = AltModifier;
			else
				return false;
			return true;
		}

		static bool TryParseKeyCode(string part, out int keyCode)
		{
			keyCode = 0;
			if (string.IsNullOrWhiteSpace(part))
				return false;
			if (part.Length == 1)
			{
				char c = char.ToUpperInvariant(part[0]);
				if (c >= 'A' && c <= 'Z')
				{
					keyCode = c;
					return true;
				}
				if (c >= '0' && c <= '9')
				{
					keyCode = c;
					return true;
				}
			}
			if (part.Length == 2 && part[0] == 'D' && part[1] >= '0' && part[1] <= '9')
			{
				keyCode = part[1];
				return true;
			}
			if (part.StartsWith("F", StringComparison.OrdinalIgnoreCase)
				&& int.TryParse(part.Substring(1), out int functionIndex)
				&& functionIndex >= 1
				&& functionIndex <= 24)
			{
				keyCode = 0x70 + functionIndex - 1;
				return true;
			}
			if (part.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase)
				&& int.TryParse(part.Substring(6), out int numpadIndex)
				&& numpadIndex >= 0
				&& numpadIndex <= 9)
			{
				keyCode = 0x60 + numpadIndex;
				return true;
			}

			return TryParseNamedKeyCode(part, out keyCode);
		}

		static bool TryParseNamedKeyCode(string part, out int keyCode)
		{
			keyCode = 0;
			switch (part.ToUpperInvariant())
			{
				case "RETURN":
				case "ENTER":
					keyCode = 0x0D;
					return true;
				case "ESC":
				case "ESCAPE":
					keyCode = 0x1B;
					return true;
				case "SPACE":
					keyCode = 0x20;
					return true;
				case "TAB":
					keyCode = 0x09;
					return true;
				case "BACK":
				case "BACKSPACE":
					keyCode = 0x08;
					return true;
				case "LEFT":
					keyCode = 0x25;
					return true;
				case "UP":
					keyCode = 0x26;
					return true;
				case "RIGHT":
					keyCode = 0x27;
					return true;
				case "DOWN":
					keyCode = 0x28;
					return true;
				case "HOME":
					keyCode = 0x24;
					return true;
				case "END":
					keyCode = 0x23;
					return true;
				case "PAGEUP":
				case "PRIOR":
					keyCode = 0x21;
					return true;
				case "PAGEDOWN":
				case "NEXT":
					keyCode = 0x22;
					return true;
				case "INSERT":
					keyCode = 0x2D;
					return true;
				case "DELETE":
				case "DEL":
					keyCode = 0x2E;
					return true;
				case "OEM3":
				case "OEMTILDE":
					keyCode = 0xC0;
					return true;
				default:
					return false;
			}
		}
	}
}
