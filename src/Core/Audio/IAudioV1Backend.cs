using System;

namespace GEmuera.Core.Audio;

/// <summary>
/// AUDIO_*（EWP audio v1）账本驱动的宿主原语。
/// 账本只做意图/状态/返回码；真正出声、以及桥能力边界内的降级由宿主适配器实现。
/// 本接口刻意不依赖 Godot：测试用内存实现即可完整覆盖账本契约。
/// </summary>
public interface IAudioV1Backend
{
    /// <summary>宿主音频后端当前是否可用；不可用时 PLAY 返回 0，CTL/BUS/DUCK 返回 -2。</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 开始播放。handle 是账本已分配的句柄，适配器用它建立 句柄→物理通道 映射，
    /// 以便位置回调能把 Progress 送回账本。返回 false 表示音轨解析/加载失败。
    /// </summary>
    bool Play(AudioV1PlayRequest request, int handle);

    void Stop(int handle);
    void Pause(int handle);
    void Resume(int handle);

    /// <summary>每句柄音量（0..100）。桥对声音通道池没有独立音量时允许降级为 no-op（DIVERGENCE）。</summary>
    void SetVolume(int handle, int volume);

    /// <summary>每句柄速率（1.0=原速）与保音高意图。</summary>
    void SetRate(int handle, double rate, bool preservePitch);

    /// <summary>运行时修改循环意图；桥不支持事后修改时允许降级为 no-op（DIVERGENCE）。</summary>
    void SetLoop(int handle, bool loop);

    void SetBusVolume(AudioV1Bus bus, int volume);
    void SetBusMute(AudioV1Bus bus, bool mute);

    /// <summary>停止指定总线上的全部声音；<see cref="AudioV1Bus.Master"/> 表示整体停止。</summary>
    void StopAll(AudioV1Bus bus);

    /// <summary>
    /// 会话结束时清空适配器侧通道映射/总线音量/duck 簿记。
    /// <see cref="StopAll(AudioV1Bus)"/> 已负责停声；本原语只保证下一局不继承上一局的物理混音状态。
    /// </summary>
    void Reset();

    /// <summary>
    /// duck 规则变化后的立即增益（dB，0 表示解除）。桥不支持 attack/release 包络时立即生效（DIVERGENCE）。
    /// </summary>
    void SetDuck(AudioV1Bus targetBus, int duckDb);
}
