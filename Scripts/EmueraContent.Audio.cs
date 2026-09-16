// EmueraContent.Audio.cs —— 承载音频播放/BGM 后台加载/音频流 LRU 缓存功能域，自 EmueraContent.cs 拆出（原因：主文件超 2000 行只减不增约束）。
using Godot;
using System;
using System.Collections.Generic;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Content;
using EmuFont = uEmuera.Drawing.Font;
using EmuColor = uEmuera.Drawing.Color;

public partial class EmueraContent : Control
{
	// Audio players are pooled by logical emuera sound channel. Channel indexes
	// are stable so script commands can pause/stop/speed-change the same channel.
	AudioStreamPlayer bgmPlayer;
	List<AudioStreamPlayer> soundPlayers = new List<AudioStreamPlayer>();
	List<int> soundRepeatRemaining = new List<int>();
	// Stream 总时长缓存：GetLength() 是每帧 native 互操作，时长只随 Stream 实例变化，
	// 缓存后空闲帧不再为每个播放器跨 native 边界查询。
	AudioStream bgmLengthCachedStream;
	double bgmLengthCachedTotalSec = -1.0;
	List<AudioStream> soundLengthCachedStreams = new List<AudioStream>();
	List<double> soundLengthCachedTotals = new List<double>();
	float soundVolume = 1.0f;
	float bgmVolume = 1.0f;
	bool applicationPauseActive = false;
	bool bgmPausedBeforeApplicationPause = false;
	List<bool> soundPausedBeforeApplicationPause = new List<bool>();
	// ---- AudioStream LRU 缓存（N2）----
	// LoadAudioStream 原本每次 PLAYSOUND/PLAYBGM 都主线程同步读盘+解码；这里按
	// (path, loop) 键缓存（BGM 与 SFX 共用），带字节预算与周期清扫，参照
	// graphicsImageTextureCache 的 LRU 模式。语义不变：同 path 不同 loop 分条目，
	// 播放行为不受影响；正在播放的流不淘汰（播放器持有引用，淘汰只影响后续重载）。
	readonly struct AudioStreamCacheKey : IEquatable<AudioStreamCacheKey>
	{
		public readonly string Path;
		public readonly bool Loop;
		public AudioStreamCacheKey(string path, bool loop) { Path = path; Loop = loop; }
		public bool Equals(AudioStreamCacheKey other) => Loop == other.Loop && string.Equals(Path, other.Path, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is AudioStreamCacheKey other && Equals(other);
		public override int GetHashCode() => (Path != null ? Path.GetHashCode() : 0) ^ (Loop ? 1 : 0);
	}
	sealed class AudioStreamCacheEntry
	{
		public AudioStream Stream;
		public ulong LastUsedMs;
		public long EstimatedBytes;
	}
	readonly Dictionary<AudioStreamCacheKey, AudioStreamCacheEntry> audioStreamCache = new Dictionary<AudioStreamCacheKey, AudioStreamCacheEntry>();
	const ulong AudioStreamCacheCleanupIntervalMs = 5000;
	const long MobileAudioStreamCacheBudgetBytes = 32L * 1024L * 1024L;
	const long DesktopAudioStreamCacheBudgetBytes = 96L * 1024L * 1024L;
	const int MaxAudioStreamCacheEntries = 32;
	ulong lastAudioStreamCacheCleanupMs = 0;
	// BGM 后台加载状态（M3）。PlayBgmFile 只发起 ResourceLoader 后台加载请求，
	// _Process 轮询 LoadThreadedGetStatus，加载完成且仍是最新请求时才接上播放器；
	// 期间的新请求（含 StopBgm）会令旧请求失效（pendingBgmPath 置空即取消）。
	// 所有状态变更都在主线程串行发生（PlayBgmFile/StopBgm 经 EnqueueUI 到达）。
	string pendingBgmPath;
	void ClearSessionAudioState()
	{
		applicationPauseActive = false;
		bgmPausedBeforeApplicationPause = false;
		soundPausedBeforeApplicationPause.Clear();
		// M3/N2：取消未完成的 BGM 后台加载并清空音频流缓存，避免跨会话残留播放/引用。
		pendingBgmPath = null;
		audioStreamCache.Clear();
		lastAudioStreamCacheCleanupMs = 0;
		if (bgmPlayer != null)
		{
			bgmPlayer.Stop();
			bgmPlayer.Stream = null;
			bgmPlayer.StreamPaused = false;
		}
		for (int i = 0; i < soundPlayers.Count; i++)
		{
			var player = soundPlayers[i];
			if (player == null)
				continue;
			player.Stop();
			player.Stream = null;
			player.StreamPaused = false;
			player.PitchScale = 1.0f;
		}
		for (int i = 0; i < soundRepeatRemaining.Count; i++)
			soundRepeatRemaining[i] = 0;
		soundVolume = 1.0f;
		bgmVolume = 1.0f;
	}

	// Compatibility overload for legacy callers that pass a loop flag.
	public void PlaySoundFile(string path, bool loop, int channel)
	{
		PlaySoundFile(path, loop ? -1 : 1, channel);
	}

	// Play a sound effect on a logical emuera channel. repeat < 0 means loop,
	// repeat > 0 means replay a fixed number of times.
	public void PlaySoundFile(string path, int repeat, int channel)
	{
		bool loop = repeat < 0;
		var stream = LoadAudioStream(path, loop);
		if (stream == null)
		{
			GenericUtils.NotifySoundPlaybackFailed(channel, path);
			return;
		}
		while (soundPlayers.Count <= channel)
		{
			int newChannel = soundPlayers.Count;
			var newPlayer = new AudioStreamPlayer();
			newPlayer.Finished += () => OnSoundPlayerFinished(newChannel);
			soundPlayers.Add(newPlayer);
			soundRepeatRemaining.Add(0);
			AddChild(newPlayer);
		}
		AudioStreamPlayer player = soundPlayers[channel];
		player.Stop();
		player.Stream = stream;
		player.VolumeDb = LinearToDb(soundVolume);
		player.PitchScale = 1.0f;
		player.StreamPaused = false;
		soundRepeatRemaining[channel] = loop ? -1 : Math.Max(repeat, 1);
		player.Play();
		GenericUtils.NotifySoundPlaybackStarted(channel, path, GetAudioStreamLengthMs(stream));
	}

	// Replay or stop a channel when Godot reports that playback finished.
	void OnSoundPlayerFinished(int channel)
	{
		if (channel < 0 || channel >= soundPlayers.Count || channel >= soundRepeatRemaining.Count)
			return;
		int remaining = soundRepeatRemaining[channel];
		if (remaining < 0)
			return;
		remaining--;
		soundRepeatRemaining[channel] = remaining;
		if (remaining > 0)
		{
			soundPlayers[channel].Play();
			GenericUtils.NotifySoundPlaybackRepeated(channel);
			return;
		}
		GenericUtils.NotifySoundPlaybackFinished(channel);
	}

	// Stop all sound effect channels without touching BGM.
	public void StopSounds()
	{
		for (int i = 0; i < soundPlayers.Count; i++)
		{
			soundRepeatRemaining[i] = 0;
			soundPlayers[i].Stop();
		}
	}

	// Stop one sound effect channel if it exists.
	public void StopSoundChannel(int channel)
	{
		if (channel < 0 || channel >= soundPlayers.Count)
			return;
		if (channel < soundRepeatRemaining.Count)
			soundRepeatRemaining[channel] = 0;
		soundPlayers[channel].Stop();
	}

	// Pause/resume one sound channel for emuera SOUNDSTOP/SOUNDPLAY semantics.
	public void PauseSoundChannel(int channel, bool paused)
	{
		if (channel < 0 || channel >= soundPlayers.Count)
			return;
		soundPlayers[channel].StreamPaused = paused;
	}

	// Adjust playback speed for one sound effect channel.
	public void SetSoundChannelSpeed(int channel, float speed)
	{
		if (channel < 0 || channel >= soundPlayers.Count)
			return;
		soundPlayers[channel].PitchScale = Mathf.Max(0.01f, speed);
	}

	// Start BGM, replacing any existing track.
	// M3：主线程不再同步读盘+解码（1-10MB 文件会阻塞 UI 帧）。命中缓存立即播放；
	// 未命中则发起 ResourceLoader.LoadThreadedRequest 后台加载，由
	// ProcessPendingBgmLoad 在 _Process 里轮询完成并接线播放。语义不变：
	// 播放/切换行为与原来一致，播放开始时机允许略延迟。
	public void PlayBgmFile(string path)
	{
		string resolved = string.IsNullOrEmpty(path) ? null : uEmuera.Utils.ResolveExistingFilePath(path);
		if (string.IsNullOrEmpty(resolved) || !uEmuera.Utils.FileExists(resolved))
		{
			GenericUtils.NotifyBgmPlaybackFailed(path);
			return;
		}
		var cacheKey = new AudioStreamCacheKey(resolved, true);
		if (audioStreamCache.TryGetValue(cacheKey, out var cached) && cached != null && cached.Stream != null)
		{
			cached.LastUsedMs = Time.GetTicksMsec();
			StartBgmStream(cached.Stream, path);
			return;
		}
		// 只有最新请求生效；旧请求完成时发现路径已被替换/清除则丢弃。
		pendingBgmPath = resolved;
		Error startError = ResourceLoader.LoadThreadedRequest(resolved);
		if (startError == Error.Ok)
			return;
		// 该路径不被 ResourceLoader 支持（无对应格式加载器）——回退同步加载保持行为。
		pendingBgmPath = null;
		var fallback = LoadAudioStream(resolved, true);
		if (fallback == null)
			GenericUtils.NotifyBgmPlaybackFailed(path);
		else
			StartBgmStream(fallback, path);
	}

	// Wire a loaded stream onto the BGM player (replaces the old track).
	void StartBgmStream(AudioStream stream, string path)
	{
		if (bgmPlayer == null)
		{
			bgmPlayer = new AudioStreamPlayer();
			AddChild(bgmPlayer);
		}
		bgmPlayer.Stop();
		bgmPlayer.Stream = stream;
		bgmPlayer.VolumeDb = LinearToDb(bgmVolume);
		bgmPlayer.PitchScale = 1.0f;
		bgmPlayer.StreamPaused = false;
		bgmPlayer.Play();
		GenericUtils.NotifyBgmPlaybackStarted(path, GetAudioStreamLengthMs(stream));
	}

	// Main-thread poll for the pending background BGM load.
	void ProcessPendingBgmLoad()
	{
		if (pendingBgmPath == null)
			return;
		string path = pendingBgmPath;
		ResourceLoader.ThreadLoadStatus status;
		try
		{
			status = ResourceLoader.LoadThreadedGetStatus(path);
		}
		catch
		{
			status = ResourceLoader.ThreadLoadStatus.Failed;
		}
		if (status == ResourceLoader.ThreadLoadStatus.InProgress)
			return;
		pendingBgmPath = null;
		if (status != ResourceLoader.ThreadLoadStatus.Loaded)
		{
			GenericUtils.NotifyBgmPlaybackFailed(path);
			return;
		}
		Resource loaded = null;
		try
		{
			loaded = ResourceLoader.LoadThreadedGet(path);
		}
		catch
		{
			// 加载结果不可用时按失败处理。
		}
		if (loaded is not AudioStream stream)
		{
			GenericUtils.NotifyBgmPlaybackFailed(path);
			return;
		}
		// 与同步路径一致：BGM 恒为 loop=true。
		ApplyAudioStreamLoop(stream, true);
		// 并入 N2 缓存，后续同 path 播放（BGM/SFX）直接命中。
		audioStreamCache[new AudioStreamCacheKey(path, true)] = new AudioStreamCacheEntry
		{
			Stream = stream,
			LastUsedMs = Time.GetTicksMsec(),
			EstimatedBytes = EstimateAudioStreamBytes(path, stream)
		};
		StartBgmStream(stream, path);
	}

	// Stop the active BGM track and cancel any pending background load.
	public void StopBgm()
	{
		// M3：取消未完成的异步加载请求，防止加载完成后又把停掉的 BGM 播起来。
		pendingBgmPath = null;
		bgmPlayer?.Stop();
	}

	// Pause or resume BGM without releasing the stream.
	public void PauseBgm(bool paused)
	{
		if (bgmPlayer == null)
			return;
		bgmPlayer.StreamPaused = paused;
	}

	// Android can suspend or throttle apps aggressively in the background. Keep
	// audio state reversible so a script-paused channel stays paused after resume.
	public void SetApplicationPaused(bool paused)
	{
		if (applicationPauseActive == paused)
			return;
		applicationPauseActive = paused;
		if (paused)
		{
			// Preserve each logical channel's script-controlled pause state before
			// forcing an Android lifecycle pause, so resume does not accidentally
			// unpause audio that the era script had already paused.
			bgmPausedBeforeApplicationPause = bgmPlayer != null && bgmPlayer.StreamPaused;
			if (bgmPlayer != null)
				bgmPlayer.StreamPaused = true;
			soundPausedBeforeApplicationPause.Clear();
			for (int i = 0; i < soundPlayers.Count; i++)
			{
				var player = soundPlayers[i];
				bool wasPaused = player != null && player.StreamPaused;
				soundPausedBeforeApplicationPause.Add(wasPaused);
				if (player != null)
					player.StreamPaused = true;
			}
			StopContentInertia();
			return;
		}

		if (bgmPlayer != null)
			bgmPlayer.StreamPaused = bgmPausedBeforeApplicationPause;
		for (int i = 0; i < soundPlayers.Count && i < soundPausedBeforeApplicationPause.Count; i++)
		{
			var player = soundPlayers[i];
			if (player != null)
				player.StreamPaused = soundPausedBeforeApplicationPause[i];
		}
		soundPausedBeforeApplicationPause.Clear();
	}

	// Adjust BGM pitch/tempo through Godot's pitch scale.
	public void SetBgmSpeed(float speed)
	{
		if (bgmPlayer == null)
			return;
		bgmPlayer.PitchScale = Mathf.Max(0.01f, speed);
	}

	// Set global sound effect volume from emuera's 0-100 scale.
	public void SetSoundVolume(int volume)
	{
		soundVolume = NormalizeEraVolume(volume);
		foreach (var player in soundPlayers)
			player.VolumeDb = LinearToDb(soundVolume);
	}

	// Set BGM volume from emuera's 0-100 scale.
	public void SetBgmVolume(int volume)
	{
		bgmVolume = NormalizeEraVolume(volume);
		if (bgmPlayer != null)
			bgmPlayer.VolumeDb = LinearToDb(bgmVolume);
	}

	// Load a Godot AudioStream from an emuera file path. This stays synchronous
	// because sound commands expect immediate playback, so callers should avoid
	// using very large audio files in hot loops on mobile.
	// N2：按 (path, loop) LRU 缓存复用已加载的 AudioStream，重复 PLAYSOUND/PLAYBGM
	// 不再反复读盘+解码；MB 预算 + 周期清扫防止大文件占满（参照纹理 LRU 模式）。
	AudioStream LoadAudioStream(string path, bool loop)
	{
		if (string.IsNullOrEmpty(path))
			return null;
		path = uEmuera.Utils.ResolveExistingFilePath(path);
		if (!uEmuera.Utils.FileExists(path))
		{
			GenericUtils.Warn(EmueraLogCategory.Audio, () => $"[AUDIO] File not found: {path}");
			return null;
		}
		var cacheKey = new AudioStreamCacheKey(path, loop);
		ulong nowMs = Time.GetTicksMsec();
		if (audioStreamCache.TryGetValue(cacheKey, out var cached)
			&& cached != null && cached.Stream != null)
		{
			cached.LastUsedMs = nowMs;
			return cached.Stream;
		}
		var stream = LoadAudioStreamFromFile(path, loop);
		if (stream == null)
			return null;
		long estimatedBytes = EstimateAudioStreamBytes(path, stream);
		long budget = OS.HasFeature("mobile") ? MobileAudioStreamCacheBudgetBytes : DesktopAudioStreamCacheBudgetBytes;
		if (estimatedBytes > budget)
		{
			// 单文件超过整个预算时不入缓存，避免大文件占满缓存。
			return stream;
		}
		EvictAudioStreamCacheEntries(nowMs, estimatedBytes);
		audioStreamCache[cacheKey] = new AudioStreamCacheEntry
		{
			Stream = stream,
			LastUsedMs = nowMs,
			EstimatedBytes = estimatedBytes
		};
		return stream;
	}

	// Actual per-extension file load (no cache).
	AudioStream LoadAudioStreamFromFile(string path, bool loop)
	{
		string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
		switch (ext)
		{
			case ".wav":
				var wav = AudioStreamWav.LoadFromFile(path);
				if (wav != null)
					wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
				return wav;
			case ".ogg":
				var ogg = AudioStreamOggVorbis.LoadFromFile(path);
				if (ogg != null)
					ogg.Loop = loop;
				return ogg;
			case ".mp3":
				var mp3 = AudioStreamMP3.LoadFromFile(path);
				if (mp3 != null)
					mp3.Loop = loop;
				else
					GenericUtils.Warn(EmueraLogCategory.Audio, () => $"[AUDIO] Failed to load MP3: {path}");
				return mp3;
			default:
				GenericUtils.Warn(EmueraLogCategory.Audio, () => $"[AUDIO] Unsupported audio extension \"{ext}\": {path}");
				return null;
		}
	}

	// Set the loop flag on a stream by its concrete type (BGM 后台加载完成后使用，
	// 与 LoadAudioStreamFromFile 的语义一致)。
	static void ApplyAudioStreamLoop(AudioStream stream, bool loop)
	{
		if (stream is AudioStreamWav wav)
			wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
		else if (stream is AudioStreamOggVorbis ogg)
			ogg.Loop = loop;
		else if (stream is AudioStreamMP3 mp3)
			mp3.Loop = loop;
	}

	// Estimated cached memory for budget accounting. Falls back to the on-disk
	// file size, which is a good approximation of the decoded footprint.
	static long EstimateAudioStreamBytes(string path, AudioStream stream)
	{
		try
		{
			var info = new System.IO.FileInfo(path);
			if (info.Exists)
				return Math.Max(1024L, info.Length);
		}
		catch
		{
			// 文件信息不可读时按 1MB 估算，避免缓存无界增长。
		}
		return 1024L * 1024L;
	}

	bool IsAudioStreamInUse(AudioStream stream)
	{
		if (stream == null)
			return false;
		if (bgmPlayer != null && bgmPlayer.Stream == stream)
			return true;
		for (int i = 0; i < soundPlayers.Count; i++)
		{
			if (soundPlayers[i] != null && soundPlayers[i].Stream == stream)
				return true;
		}
		return false;
	}

	// LRU 淘汰：最久未用者先出；正在播放的流保留（播放器持有引用，淘汰只会造成
	// 后续重复加载，不会中断当前播放）。
	void EvictAudioStreamCacheEntries(ulong nowMs, long incomingBytes)
	{
		long budget = OS.HasFeature("mobile") ? MobileAudioStreamCacheBudgetBytes : DesktopAudioStreamCacheBudgetBytes;
		long totalBytes = incomingBytes;
		foreach (var pair in audioStreamCache)
			totalBytes += pair.Value.EstimatedBytes;
		// 入缓存前调用时 incoming 已计入：达到条目上限（将在插入后超限）时也要先腾位。
		if (audioStreamCache.Count < MaxAudioStreamCacheEntries && totalBytes <= budget)
			return;
		var entries = new List<KeyValuePair<AudioStreamCacheKey, AudioStreamCacheEntry>>(audioStreamCache);
		entries.Sort((a, b) => a.Value.LastUsedMs.CompareTo(b.Value.LastUsedMs));
		foreach (var pair in entries)
		{
			if (audioStreamCache.Count < MaxAudioStreamCacheEntries && totalBytes <= budget)
				break;
			if (IsAudioStreamInUse(pair.Value.Stream))
				continue;
			audioStreamCache.Remove(pair.Key);
			totalBytes -= pair.Value.EstimatedBytes;
		}
	}

	// 主线程周期清扫：与纹理缓存清理同步点执行，按预算/条目上限做 LRU 淘汰。
	void ProcessAudioStreamCacheCleanup()
	{
		if (audioStreamCache.Count == 0)
			return;
		ulong nowMs = Time.GetTicksMsec();
		if (nowMs - lastAudioStreamCacheCleanupMs < AudioStreamCacheCleanupIntervalMs)
			return;
		lastAudioStreamCacheCleanupMs = nowMs;
		EvictAudioStreamCacheEntries(nowMs, 0);
	}

	// Convert Godot stream length to milliseconds for status callbacks.
	static long GetAudioStreamLengthMs(AudioStream stream)
	{
		if (stream == null)
			return 0;
		double length = stream.GetLength();
		if (length <= 0 || double.IsNaN(length) || double.IsInfinity(length))
			return 0;
		return (long)(length * 1000.0);
	}

	// Convert emuera integer volume to Godot linear volume.
	static float NormalizeEraVolume(int volume)
	{
		// 企业级说明：音量边界由 GenericUtils 统一维护，避免核心状态与 Godot 播放器出现 0-100 规则漂移。
		return GenericUtils.ClampEraVolume(volume) / 100.0f;
	}

	// Godot audio buses use dB, with a hard mute floor for zero volume.
	static float LinearToDb(float linear)
	{
		if (linear <= 0.0001f)
			return -80.0f;
		return Mathf.LinearToDb(linear);
	}
	// Publish audio playback positions to the bridge for status queries.
	void PublishAudioPlaybackPositions()
	{
		if (bgmPlayer != null)
		{
			GenericUtils.NotifyBgmPlaybackPosition(bgmPlayer.GetPlaybackPosition(), GetBgmStreamLengthSec(), bgmPlayer.Playing && !bgmPlayer.StreamPaused);
		}
		for (int i = 0; i < soundPlayers.Count; i++)
		{
			var player = soundPlayers[i];
			if (player == null)
				continue;
			GenericUtils.NotifySoundPlaybackPosition(i, player.GetPlaybackPosition(), GetSoundStreamLengthSec(i), player.Playing && !player.StreamPaused);
		}
	}

	// Cached stream length (seconds); AudioStream is an immutable resource, so the
	// total only changes when a different stream instance is assigned to the player.
	double GetBgmStreamLengthSec()
	{
		var stream = bgmPlayer.Stream;
		if (!ReferenceEquals(bgmLengthCachedStream, stream))
		{
			bgmLengthCachedStream = stream;
			bgmLengthCachedTotalSec = stream?.GetLength() ?? 0.0;
		}
		return bgmLengthCachedTotalSec;
	}

	double GetSoundStreamLengthSec(int channel)
	{
		var stream = soundPlayers[channel].Stream;
		while (soundLengthCachedStreams.Count <= channel)
		{
			soundLengthCachedStreams.Add(null);
			soundLengthCachedTotals.Add(-1.0);
		}
		if (!ReferenceEquals(soundLengthCachedStreams[channel], stream))
		{
			soundLengthCachedStreams[channel] = stream;
			soundLengthCachedTotals[channel] = stream?.GetLength() ?? 0.0;
		}
		return soundLengthCachedTotals[channel];
	}
}
