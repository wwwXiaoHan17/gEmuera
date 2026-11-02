using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 包 gameIdentity 与游戏 GameBase.csv 的比对（设计 §3.3：声明身份的包在加载时比对，
/// 不匹配拒绝加载并回退纯 v24）。纯文本函数：宿主读文件，本类只解析与判定，
/// 编码责任在调用方（v1 按 UTF-8 读取；行名匹配日文原版与常见变体）。
/// </summary>
public static class CompatPackGameIdentityCheck
{
    /// <summary>
    /// 从 GameBase.csv 文本提取 コード/バージョン 两行（第一列为行名、第二列为值，
    /// 去引号去空白）。任一行缺失返回 false；文本为 null/空返回 false。
    /// </summary>
    public static bool TryParseGameBaseCsv(string? csvText, out string gameCode, out string version)
    {
        gameCode = "";
        version = "";
        if (string.IsNullOrEmpty(csvText))
            return false;

        string parsedCode = "";
        string parsedVersion = "";
        foreach (string rawLine in csvText.Split('\n'))
        {
            string line = rawLine.Trim('\r').Trim();
            if (line.Length == 0)
                continue;
            int comma = line.IndexOf(',');
            if (comma <= 0)
                continue;
            string rowName = Unquote(line[..comma].Trim());
            string value = Unquote(line[(comma + 1)..].Trim());
            if (parsedCode.Length == 0 && IsCodeRow(rowName))
                parsedCode = value;
            else if (parsedVersion.Length == 0 && IsVersionRow(rowName))
                parsedVersion = value;
            if (parsedCode.Length > 0 && parsedVersion.Length > 0)
            {
                // 全部找到才回填 out：失败路径不留下半解析值（TryXxx 惯例）。
                gameCode = parsedCode;
                version = parsedVersion;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 包身份声明与游戏实际值比对。versionAccept=exact 始终全串相等；minor 是真正的
    /// major.minor 语义（spec §5.3）：双方都至少有 major.minor 两段时比较前两段
    ///（补丁段不参与，"3.43" 与 "3.43.7" 通过，"3.43" 与 "3.50"/"4.0" 失败）；
    /// 任一方只有单段时退化为全串相等（"305" 只与 "305" 相等）。
    /// 任一值缺失即不匹配（fail-closed）。
    /// </summary>
    public static bool Matches(CompatPackGameIdentity identity, string? gameCode, string? version)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrEmpty(gameCode) || string.IsNullOrEmpty(version))
            return false;
        if (!string.Equals(identity.GameCode, gameCode, StringComparison.Ordinal))
            return false;
        if (string.Equals(identity.Version, version, StringComparison.Ordinal))
            return true;
        if (!string.Equals(identity.VersionAccept, "minor", StringComparison.Ordinal))
            return false;
        return MinorSegmentsEqual(identity.Version, version);
    }

    static bool MinorSegmentsEqual(string declared, string actual)
    {
        string[] declaredParts = declared.Split('.');
        string[] actualParts = actual.Split('.');
        // 单段版本（如 "305"）无法取 major.minor：退化为全串相等，不做模糊匹配。
        if (declaredParts.Length < 2 || actualParts.Length < 2)
            return string.Equals(declared, actual, StringComparison.Ordinal);

        string declaredMajor = declaredParts[0].Trim();
        string declaredMinor = declaredParts[1].Trim();
        string actualMajor = actualParts[0].Trim();
        string actualMinor = actualParts[1].Trim();
        return declaredMajor.Length > 0
            && declaredMinor.Length > 0
            && string.Equals(declaredMajor, actualMajor, StringComparison.Ordinal)
            && string.Equals(declaredMinor, actualMinor, StringComparison.Ordinal);
    }

    static bool IsCodeRow(string rowName) =>
        rowName is "コード" or "代码" or "CODE" or "code";

    static bool IsVersionRow(string rowName) =>
        rowName is "バージョン" or "版本" or "VERSION" or "version";

    static string Unquote(string value)
    {
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
            return value[1..^1];
        return value;
    }
}
