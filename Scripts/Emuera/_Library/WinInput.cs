using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MinorShift._Library
{
	internal sealed class WinInput
	{
		static readonly Dictionary<int, short> keyStateCache = new Dictionary<int, short>();
		static readonly Dictionary<int, long> virtualPressedUntilMs = new Dictionary<int, long>();
		static readonly Dictionary<int, bool> pressedStates = new Dictionary<int, bool>();
		static readonly Dictionary<int, short> toggleStates = new Dictionary<int, short>();
		static readonly object syncRoot = new object();

		public static void UpdateKeyState()
		{
			lock (syncRoot)
			{
				keyStateCache.Clear();
				// Poll common keys used by era games for skip / shortcuts
				PollButton(0x01, Godot.Input.IsMouseButtonPressed(Godot.MouseButton.Left));
				PollButton(0x02, Godot.Input.IsMouseButtonPressed(Godot.MouseButton.Right));
				PollButton(0x04, Godot.Input.IsMouseButtonPressed(Godot.MouseButton.Middle));
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
				ApplyVirtualKeys();
			}
		}

		static void PollKey(int vk, Godot.Key key)
		{
			PollButton(vk, Godot.Input.IsKeyPressed(key));
		}

		static void PollButton(int vk, bool pressed)
		{
			keyStateCache[vk] = ComposeState(vk, pressed);
		}

		static short ComposeState(int vk, bool pressed)
		{
			bool wasPressed = pressedStates.TryGetValue(vk, out bool previous) && previous;
			if (pressed && !wasPressed)
			{
				short toggle = toggleStates.TryGetValue(vk, out short oldToggle) && oldToggle != 0 ? (short)0 : (short)1;
				toggleStates[vk] = toggle;
			}
			pressedStates[vk] = pressed;
			short toggleValue = toggleStates.TryGetValue(vk, out short currentToggle) ? currentToggle : (short)0;
			return (short)((pressed ? unchecked((short)0x8000) : (short)0) | toggleValue);
		}

		static void ApplyVirtualKeys()
		{
			long now = Environment.TickCount64;
			List<int> expired = null;
			foreach (var pair in virtualPressedUntilMs)
			{
				if (pair.Value >= now)
					keyStateCache[pair.Key] = ComposeState(pair.Key, true);
				else
				{
					expired ??= new List<int>();
					expired.Add(pair.Key);
				}
			}
			if (expired == null)
				return;
			foreach (int key in expired)
				virtualPressedUntilMs.Remove(key);
		}

		public static void PulseVirtualKey(int nVirtKey, int durationMs = 250)
		{
			if (nVirtKey < 0 || nVirtKey > 255)
				return;
			long until = Environment.TickCount64 + Math.Max(1, durationMs);
			lock (syncRoot)
			{
				virtualPressedUntilMs[nVirtKey] = until;
				keyStateCache[nVirtKey] = ComposeState(nVirtKey, true);
			}
		}

		public static short GetKeyState(int nVirtKey)
		{
			lock (syncRoot)
			{
				if (virtualPressedUntilMs.TryGetValue(nVirtKey, out long until) && until >= Environment.TickCount64)
					return keyStateCache.TryGetValue(nVirtKey, out short virtualValue)
						? virtualValue
						: ComposeState(nVirtKey, true);
				if (keyStateCache.TryGetValue(nVirtKey, out short value))
					return value;
				return 0;
			}
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
