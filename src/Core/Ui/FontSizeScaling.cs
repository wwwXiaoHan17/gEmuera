using System;

namespace GEmuera.Core.Ui;

/// <summary>
/// 运行时字号缩放纯数学（无 Godot 依赖，供 Config.SetRuntimeFontSize 与 xUnit 共用）。
/// 语义镜像 Config.SetConfig 的加载期钳制规则：字号下限 8；行高不得小于字号。
/// </summary>
public static class FontSizeScaling
{
    /// <summary>字号下限（镜像 Config.SetConfig 的「フォントサイズが小さすぎます(8が下限)」规则）。</summary>
    public const int MinFontSize = 8;

    /// <summary>钳制字号到合法下限。</summary>
    public static int ClampFontSize(int fontSize)
        => Math.Max(MinFontSize, fontSize);

    /// <summary>
    /// 字号变化后的行高等比换算：newLineHeight = round(oldLineHeight * newFontSize / oldFontSize)，
    /// 且不小于新字号（镜像 Config.SetConfig 的「行の高さがフォントサイズより小さい」钳制）。
    /// oldFontSize 非法（&lt;=0）时直接返回 newFontSize，保证结果永不低于新字号。
    /// </summary>
    public static int ComputeScaledLineHeight(int oldFontSize, int oldLineHeight, int newFontSize)
    {
        if (oldFontSize <= 0)
            return Math.Max(MinFontSize, newFontSize);
        double scaled = (double)oldLineHeight * newFontSize / oldFontSize;
        int rounded = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        return Math.Max(rounded, Math.Max(MinFontSize, newFontSize));
    }
}
