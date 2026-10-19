using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using SkiaSharp;

// 动画 WebP 的帧解码必须与 Godot 纹理创建分离：Skia 解码可在后台执行，
// ImageTexture 只能在 Godot 主线程创建，避免 Android 渲染线程竞争。
// 帧序列只保存已经上传的纹理，由现有 EmueraImage 控件在 _Process 中切换当前帧，
// 不额外创建帧播放子节点，保持 HTML 图片的裁剪、翻转与 ColorMatrix 归属不变。
internal sealed class AnimatedWebpFrameSequence
{
	readonly List<Frame> frames = new List<Frame>();

	readonly struct Frame
	{
		internal readonly Texture2D Texture;
		internal readonly double DurationSeconds;

		internal Frame(Texture2D texture, double durationSeconds)
		{
			Texture = texture;
			DurationSeconds = durationSeconds;
		}
	}

	internal int FrameCount => frames.Count;

	internal bool TryGetFrame(int index, out Texture2D texture, out double durationSeconds)
	{
		if (index >= 0 && index < frames.Count)
		{
			Frame frame = frames[index];
			texture = frame.Texture;
			durationSeconds = frame.DurationSeconds;
			return texture != null;
		}
		texture = null;
		durationSeconds = 0.0;
		return false;
	}

	internal void AddFrame(Texture2D texture, double durationSeconds)
	{
		if (texture != null)
			frames.Add(new Frame(texture, Math.Max(durationSeconds, 0.001)));
	}

	internal void Clear()
	{
		// 显式释放已上传的帧纹理，让 GPU 内存及时回收。Release 只在引用计数归零
		// （没有任何 EmueraImage 节点还在用该序列）时调用，此时节点已不再持帧。
		for (int i = 0; i < frames.Count; i++)
			frames[i].Texture?.Dispose();
		frames.Clear();
	}
}

internal static class AnimatedWebpSpriteFrames
{
	const int PendingFrameCapacity = 3;
	// 动画帧 RGBA8 GPU 纹理的内存预算：超过上限后新动画只保留首帧（回退静态显示）。
	// 移动端显存/内存更紧张，给更小预算；预算只约束"已上传保留"的帧，首帧永远保留。
	const long MobileFrameBytesBudget = 64L * 1024L * 1024L;
	const long DesktopFrameBytesBudget = 256L * 1024L * 1024L;
	const int MobileDecodeConcurrency = 1;
	const int DesktopDecodeConcurrency = 2;

	sealed class RawFrame
	{
		public byte[] Pixels;
		public readonly int Width;
		public readonly int Height;
		public readonly int DurationMs;

		public RawFrame(byte[] pixels, int width, int height, int durationMs)
		{
			Pixels = pixels;
			Width = width;
			Height = height;
			DurationMs = durationMs;
		}
	}

	sealed class Entry
	{
		public readonly string Key;
		public readonly string Path;
		public readonly ConcurrentQueue<RawFrame> ReadyFrames = new ConcurrentQueue<RawFrame>();
		public readonly SemaphoreSlim ReadyFrameSlots = new SemaphoreSlim(PendingFrameCapacity, PendingFrameCapacity);
		public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
		public readonly List<Action<AnimatedWebpFrameSequence>> Waiters = new List<Action<AnimatedWebpFrameSequence>>();
		public int ReferenceCount;
		public bool DecodeStarted;
		public bool FirstFramePublished;
		public bool FailureLogged;
		public bool FallbackToStaticFirstFrame;
		public long RetainedFrameBytes;
		public string Failure;
		public AnimatedWebpFrameSequence Frames;

		public Entry(string key, string path)
		{
			Key = key;
			Path = path;
		}
	}

	static readonly object syncRoot = new object();
	static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
	static readonly Dictionary<string, bool> animationHeaderCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

	// 并发解码上限：多张动态立绘同时出现时，限制后台 Skia 解码任务数，避免小内存机型
	// 同时解码多张全幅图。帧序/时长/循环/暂停不受影响，只是首帧就绪时间可能稍晚。
	static readonly SemaphoreSlim decodeGate = new SemaphoreSlim(GetInitialDecodeConcurrency(), GetInitialDecodeConcurrency());
	// 所有 entry 已上传保留的帧纹理字节总和（仅在 Godot 主线程增改）。
	static long retainedFrameBytes;

	static int GetInitialDecodeConcurrency()
	{
		try { return OS.HasFeature("mobile") ? MobileDecodeConcurrency : DesktopDecodeConcurrency; }
		catch { return MobileDecodeConcurrency; }
	}

	static long GetFrameBytesBudget()
	{
		try { return OS.HasFeature("mobile") ? MobileFrameBytesBudget : DesktopFrameBytesBudget; }
		catch { return MobileFrameBytesBudget; }
	}

	static bool WouldExceedFrameBudget(long frameBytes)
	{
		return Interlocked.Read(ref retainedFrameBytes) + frameBytes > GetFrameBytesBudget();
	}

	internal static bool IsAnimatedWebp(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase))
			return false;
		string key = NormalizePath(path);
		lock (syncRoot)
		{
			if (animationHeaderCache.TryGetValue(key, out bool cached))
				return cached;
		}

		bool isAnimated = false;
		try
		{
			using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
			byte[] header = new byte[Math.Min(4096, (int)Math.Min(stream.Length, 4096))];
			int read = stream.Read(header, 0, header.Length);
			isAnimated = read >= 16
				&& HasAscii(header, read, 0, "RIFF")
				&& HasAscii(header, read, 8, "WEBP")
				&& ContainsAscii(header, read, "ANIM");
		}
		catch
		{
			// 文件暂时不可访问时按静态图处理；下次解析资源时仍可重新尝试。
			return false;
		}

		lock (syncRoot)
			animationHeaderCache[key] = isAnimated;
		return isAnimated;
	}

	internal static bool Acquire(string path, Action<AnimatedWebpFrameSequence> onFirstFrameReady)
	{
		if (onFirstFrameReady == null || !IsAnimatedWebp(path))
			return false;

		Entry entry;
		AnimatedWebpFrameSequence immediatelyReady = null;
		bool startDecode = false;
		string key = NormalizePath(path);
		lock (syncRoot)
		{
			if (!entries.TryGetValue(key, out entry))
			{
				entry = new Entry(key, path);
				entries.Add(key, entry);
			}
			entry.ReferenceCount++;
			if (entry.Frames != null && entry.Frames.FrameCount > 0)
				immediatelyReady = entry.Frames;
			else
				entry.Waiters.Add(onFirstFrameReady);
			if (!entry.DecodeStarted)
			{
				entry.DecodeStarted = true;
				startDecode = true;
			}
		}

		if (startDecode)
			_ = Task.Run(() => DecodeFrames(entry));
		if (immediatelyReady != null)
			onFirstFrameReady(immediatelyReady);
		return true;
	}

	internal static void Release(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return;
		Entry entry = null;
		lock (syncRoot)
		{
			if (!entries.TryGetValue(NormalizePath(path), out entry))
				return;
			entry.ReferenceCount--;
			if (entry.ReferenceCount > 0)
				return;
			entries.Remove(entry.Key);
			entry.Waiters.Clear();
			entry.Cancellation.Cancel();
		}

		while (entry.ReadyFrames.TryDequeue(out _))
			entry.ReadyFrameSlots.Release();
		if (entry.RetainedFrameBytes > 0)
			Interlocked.Add(ref retainedFrameBytes, -entry.RetainedFrameBytes);
		entry.RetainedFrameBytes = 0;
		entry.Frames?.Clear();
		entry.Frames = null;
	}

	// 每帧只上传少量帧，防止动态立绘首次出现时占满移动端主线程。
	internal static void ProcessPendingFrameUploads(bool mobile)
	{
		int remainingUploads = mobile ? 1 : 2;
		if (remainingUploads <= 0)
			return;

		Entry[] snapshot;
		lock (syncRoot)
		{
			// 无待上传帧（也没有待记录的解码失败）时直接返回，避免每帧空快照分配。
			// entries 的增删都发生在 syncRoot 内，锁内遍历是安全的。
			bool hasPendingWork = false;
			foreach (Entry candidate in entries.Values)
			{
				if (!candidate.ReadyFrames.IsEmpty || (candidate.Failure != null && !candidate.FailureLogged))
				{
					hasPendingWork = true;
					break;
				}
			}
			if (!hasPendingWork)
				return;

			snapshot = new Entry[entries.Count];
			entries.Values.CopyTo(snapshot, 0);
		}

		for (int i = 0; i < snapshot.Length && remainingUploads > 0; i++)
		{
			Entry entry = snapshot[i];
			if (entry.Failure != null && !entry.FailureLogged)
			{
				entry.FailureLogged = true;
				GenericUtils.Warn(EmueraLogCategory.Sprite, () => $"[AnimatedWebP] {entry.Failure}");
			}
			while (remainingUploads > 0 && entry.ReadyFrames.TryDequeue(out RawFrame raw))
			{
				try
				{
					// 已回退静态首帧时，解码器可能还有排队的后续帧：直接丢弃，不再占用 GPU 内存。
					if (!entry.FallbackToStaticFirstFrame)
						UploadFrame(entry, raw);
				}
				catch (Exception ex)
				{
					entry.Failure = $"创建动画 WebP 帧失败: {Path.GetFileName(entry.Path)}, {ex.Message}";
				}
				finally
				{
					raw.Pixels = null;
					entry.ReadyFrameSlots.Release();
				}
				remainingUploads--;
			}
		}
	}

	static void UploadFrame(Entry entry, RawFrame raw)
	{
		if (raw.Pixels == null || raw.Pixels.Length == 0)
			return;
		entry.Frames ??= new AnimatedWebpFrameSequence();

		long frameBytes = (long)raw.Width * raw.Height * 4L;
		if (entry.Frames.FrameCount >= 1 && WouldExceedFrameBudget(frameBytes))
		{
			// 内存预算超限：保留已上传的首帧作为静态回退，并取消该动画的后续解码。
			// 首帧永远保留，保证至少有一帧可显示；正常预算内动画语义完全不变。
			entry.FallbackToStaticFirstFrame = true;
			entry.Cancellation.Cancel();
			return;
		}

		Image image = Image.CreateFromData(raw.Width, raw.Height, false, Image.Format.Rgba8, raw.Pixels);
		try
		{
			Texture2D texture = ImageTexture.CreateFromImage(image);
			double durationSeconds = Math.Max(raw.DurationMs, 1) / 1000.0;
			entry.Frames.AddFrame(texture, durationSeconds);
		}
		finally
		{
			image.Dispose();
		}
		entry.RetainedFrameBytes += frameBytes;
		Interlocked.Add(ref retainedFrameBytes, frameBytes);

		if (entry.FirstFramePublished)
			return;
		entry.FirstFramePublished = true;
		List<Action<AnimatedWebpFrameSequence>> callbacks;
		lock (syncRoot)
		{
			callbacks = new List<Action<AnimatedWebpFrameSequence>>(entry.Waiters);
			entry.Waiters.Clear();
		}
		for (int i = 0; i < callbacks.Count; i++)
			callbacks[i](entry.Frames);
	}

	static void DecodeFrames(Entry entry)
	{
		// 并发解码上限：同一时刻最多允许 decodeConcurrency 个后台解码任务。
		// 队列中的任务等前一个解码结束（帧序/时长/循环/暂停不受影响）。
		bool gateAcquired = false;
		decodeGate.Wait();
		gateAcquired = true;
		try
		{
			entry.Cancellation.Token.ThrowIfCancellationRequested();
			using var data = SKData.Create(entry.Path);
			using var codec = SKCodec.Create(data);
			if (codec == null || codec.FrameCount <= 1)
				throw new InvalidDataException("资源不是可解码的多帧 WebP");

			SKImageInfo outputInfo = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
			SKCodecFrameInfo[] frameInfos = codec.FrameInfo;
			if (frameInfos == null || frameInfos.Length < codec.FrameCount)
				throw new InvalidDataException("无法读取动画 WebP 帧信息");

			int[] dependentFrameUses = new int[codec.FrameCount];
			for (int i = 0; i < codec.FrameCount; i++)
			{
				int prior = frameInfos[i].RequiredFrame;
				if (prior >= 0 && prior < dependentFrameUses.Length)
					dependentFrameUses[prior]++;
			}
			var dependencyPixels = new Dictionary<int, byte[]>();
			for (int frameIndex = 0; frameIndex < codec.FrameCount; frameIndex++)
			{
				entry.Cancellation.Token.ThrowIfCancellationRequested();
				int priorFrame = frameInfos[frameIndex].RequiredFrame;
				byte[] pixels;
				if (priorFrame >= 0 && dependencyPixels.TryGetValue(priorFrame, out byte[] priorPixels))
				{
					pixels = new byte[priorPixels.Length];
					Buffer.BlockCopy(priorPixels, 0, pixels, 0, pixels.Length);
				}
				else
				{
					pixels = new byte[outputInfo.BytesSize];
				}

				GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
				try
				{
					var result = codec.GetPixels(outputInfo, handle.AddrOfPinnedObject(), new SKCodecOptions(frameIndex, priorFrame));
					if (result != SKCodecResult.Success)
						throw new InvalidDataException($"第 {frameIndex} 帧解码失败: {result}");
				}
				finally
				{
					handle.Free();
				}

				if (dependentFrameUses[frameIndex] > 0)
					dependencyPixels[frameIndex] = pixels;
				if (priorFrame >= 0 && priorFrame < dependentFrameUses.Length && --dependentFrameUses[priorFrame] == 0)
					dependencyPixels.Remove(priorFrame);

				byte[] displayPixels = new byte[pixels.Length];
				Buffer.BlockCopy(pixels, 0, displayPixels, 0, pixels.Length);
				entry.ReadyFrameSlots.Wait(entry.Cancellation.Token);
				entry.Cancellation.Token.ThrowIfCancellationRequested();
				entry.ReadyFrames.Enqueue(new RawFrame(displayPixels, outputInfo.Width, outputInfo.Height, frameInfos[frameIndex].Duration));
			}
		}
		catch (OperationCanceledException)
		{
			// 立绘已离开界面时取消后台解码是正常清理，不记录为错误。
		}
		catch (Exception ex)
		{
			entry.Failure = $"解码动画 WebP 失败: {Path.GetFileName(entry.Path)}, {ex.Message}";
		}
		finally
		{
			if (gateAcquired)
				decodeGate.Release();
		}
	}

	static string NormalizePath(string path)
	{
		try { return Path.GetFullPath(path); }
		catch { return path; }
	}

	static bool HasAscii(byte[] data, int length, int offset, string value)
	{
		if (offset < 0 || offset + value.Length > length)
			return false;
		for (int i = 0; i < value.Length; i++)
		{
			if (data[offset + i] != (byte)value[i])
				return false;
		}
		return true;
	}

	static bool ContainsAscii(byte[] data, int length, string value)
	{
		for (int offset = 0; offset <= length - value.Length; offset++)
		{
			if (HasAscii(data, length, offset, value))
				return true;
		}
		return false;
	}
}
