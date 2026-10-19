using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MinorShift._Library
{
	internal sealed class WinInput
	{
		static readonly Dictionary<int, short> keyStateCache = new Dictionary<int, short>();
		static readonly Dictionary<int, long> virtualPressedUntilMs = new Dictionary<int, long>();
		// 虚拟鼠标长按按住键集合：SetVirtualKeyPressed 置位，SetVirtualKeyReleased 清位。
		// GetKeyState 在轮询缓存之外优先读取它，因此不受 UpdateKeyState 真实键轮询覆盖。
		static readonly HashSet<int> virtualHeldKeys = new HashSet<int>();
		static readonly Dictionary<int, bool> pressedStates = new Dictionary<int, bool>();
		static readonly Dictionary<int, short> toggleStates = new Dictionary<int, short>();
		static readonly Dictionary<int, int> keyLatch = new Dictionary<int, int>();
		static readonly object syncRoot = new object();
		// 按需轮询标记：VM 线程在 GETKEY/GETKEYTRIGGERED 前请求一次轮询，
		// Godot 主线程仅在收到请求的帧执行 UpdateKeyState()，INPUT 等待期间不再
		// 每帧空转轮询 42 个键。语义保持：主线程轮询（Godot.Input 只能主线程访问）
		// 后缓存照常被 VM 线程读取，GETKEY 结果最多滞后一帧——与原有逐帧轮询一致。
		static volatile bool keyRefreshRequested;

		/// <summary>
		/// 请求主线程在下一帧补一次按键轮询。可在任意线程调用。
		/// </summary>
		public static void RequestKeyRefresh()
		{
			keyRefreshRequested = true;
		}

		/// <summary>
		/// 主线程每帧调用：有轮询请求则消费并执行轮询，否则跳过。
		/// </summary>
		public static bool ConsumeKeyRefreshRequest()
		{
			if (!keyRefreshRequested)
				return false;
			keyRefreshRequested = false;
			return true;
		}

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
			if (pressed && (!pressedStates.TryGetValue(vk, out bool wasPressed) || !wasPressed))
				keyLatch[vk] = 1;
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
				keyLatch[nVirtKey] = 1;
				keyStateCache[nVirtKey] = ComposeState(nVirtKey, true);
			}
		}

		/// <summary>
		/// 虚拟鼠标长按：把虚拟键置为按下态并记录一次 latch，直到 SetVirtualKeyReleased 才松开。
		/// 对照源码 WinInput.SetKeyPressed：GETKEY 读到按下、GETKEYTRIGGERED 读到一次触发。
		/// </summary>
		public static void SetVirtualKeyPressed(int nVirtKey)
		{
			if (nVirtKey < 0 || nVirtKey > 255)
				return;
			lock (syncRoot)
			{
				virtualHeldKeys.Add(nVirtKey);
				keyLatch[nVirtKey] = 1;
			}
		}

		/// <summary>虚拟鼠标长按结束：清除虚拟按住态（对照源码 WinInput.SetKeyReleased）。</summary>
		public static void SetVirtualKeyReleased(int nVirtKey)
		{
			if (nVirtKey < 0 || nVirtKey > 255)
				return;
			lock (syncRoot)
			{
				virtualHeldKeys.Remove(nVirtKey);
			}
		}

		public static int ConsumeKeyLatch(int nVirtKey)
		{
			// 读取前请求主线程补一次轮询（按需轮询）。
			RequestKeyRefresh();
			lock (syncRoot)
			{
				if (!keyLatch.TryGetValue(nVirtKey, out int value))
					return 0;
				keyLatch[nVirtKey] = 0;
				return value;
			}
		}

		public static void ClearLatches()
		{
			lock (syncRoot)
			{
				keyLatch.Clear();
			}
		}

		/// <summary>
		/// Clears compatibility input state between canary sessions.  In
		/// particular, virtual key pulses and toggle/latch values must not be
		/// observed by the first input wait of the next game.
		/// </summary>
		internal static void ResetCanarySessionState()
		{
			lock (syncRoot)
			{
				keyStateCache.Clear();
				virtualPressedUntilMs.Clear();
				virtualHeldKeys.Clear();
				pressedStates.Clear();
				toggleStates.Clear();
				keyLatch.Clear();
			}
			keyRefreshRequested = false;
		}

		public static short GetKeyState(int nVirtKey)
		{
			// 读取前请求主线程补一次轮询（按需轮询）。返回的缓存值最多滞后一帧，
			// 与原有逐帧轮询的读取时机一致，GETKEY 语义不变。
			RequestKeyRefresh();
			lock (syncRoot)
			{
				// 虚拟长按按住优先于轮询缓存，避免真实键轮询覆盖按住态。
				if (virtualHeldKeys.Contains(nVirtKey))
					return unchecked((short)0x8000);
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
