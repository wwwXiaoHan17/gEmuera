using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MinorShift._Library
{
	internal sealed class WinInput
	{
		static readonly Dictionary<int, short> keyStateCache = new Dictionary<int, short>();

		public static void UpdateKeyState()
		{
			keyStateCache.Clear();
			// Poll common keys used by era games for skip / shortcuts
			PollKey(0x10, Godot.Key.Shift);
			PollKey(0x11, Godot.Key.Ctrl);
			PollKey(0x12, Godot.Key.Alt);
			PollKey(0x20, Godot.Key.Space);
			PollKey(0x1B, Godot.Key.Escape);
			PollKey(0x0D, Godot.Key.Enter);
			PollKey(0x09, Godot.Key.Tab);
			PollKey(0x26, Godot.Key.Up);
			PollKey(0x28, Godot.Key.Down);
			PollKey(0x25, Godot.Key.Left);
			PollKey(0x27, Godot.Key.Right);
			for (int i = 0; i < 26; i++)
			{
				// A-Z
				PollKey(0x41 + i, Godot.Key.A + i);
			}
			for (int i = 0; i < 10; i++)
			{
				// 0-9 (top row)
				PollKey(0x30 + i, Godot.Key.Key0 + i);
			}
		}

		static void PollKey(int vk, Godot.Key key)
		{
			short value = Godot.Input.IsKeyPressed(key) ? unchecked((short)0x8000) : (short)0;
			keyStateCache[vk] = value;
		}

		public static short GetKeyState(int nVirtKey)
		{
			if (keyStateCache.TryGetValue(nVirtKey, out short value))
				return value;
			return 0;
		}
	}

    public enum MouseButtons
    {
        None = 0,
        Left = 1048576,
        Right = 2097152,
        Middle = 4194304,
        XButton1 = 8388608,
        XButton2 = 16777216
    }
}