using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GEmuera.Core.Compatibility;

#nullable enable annotations

namespace gEmuera.GodotHost;

/// <summary>
/// legacy 启动前的有界只读探测器。只读取 GAMEBASE 和固定锚点，
/// 不执行 ERB、不扫描完整游戏树；规则解析仍由纯 Core 所有。
/// </summary>
public static class GameContentProbe
{
    private const int MaxDirectoryEntriesPerSegment = 256;
    private const int MaxGameBaseBytes = 256 * 1024;
    private const int MaxAnchorBytes = 512 * 1024;
    private static long _probeId;

    public static GameCompatibilityProbeEvidence Probe(string gameRoot)
    {
        string normalizedRoot = NormalizeRoot(gameRoot);
        var warnings = new List<string>();
        GameBaseProbeEvidence? gameBase = ProbeGameBase(normalizedRoot, warnings);
        var anchors = ProbeAnchors(normalizedRoot, warnings);
        return new GameCompatibilityProbeEvidence(
            System.Threading.Interlocked.Increment(ref _probeId),
            gameBase,
            anchors,
            warnings);
    }

    private static GameBaseProbeEvidence? ProbeGameBase(string root, List<string> warnings)
    {
        string? path = TryFindPath(root, new[] { "CSV", "GAMEBASE.CSV" });
        if (path is null)
        {
            warnings.Add("gamebase.missing");
            return null;
        }

        if (!TryReadBounded(path, MaxGameBaseBytes, out string text, out string readWarning))
        {
            warnings.Add(readWarning);
            return new GameBaseProbeEvidence(null, null, false);
        }

        long? code = null;
        string? title = null;
        foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim().Trim('\uFEFF');
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal))
                continue;

            string[] fields = line.Split(new[] { ',', '\t', '=' }, 2);
            if (fields.Length < 2)
                continue;

            string key = Unquote(fields[0]).Trim();
            string value = Unquote(fields[1]).Trim();
            if (IsGameCodeKey(key))
            {
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                    && parsed >= 0)
                {
                    code = parsed;
                }
                else
                {
                    warnings.Add("gamebase.invalid-code");
                }
            }
            else if (IsTitleKey(key) && !string.IsNullOrWhiteSpace(value))
            {
                title ??= value;
            }
        }

        if (code is null && title is null)
            warnings.Add("gamebase.identity-empty");
        return new GameBaseProbeEvidence(code, title, true);
    }

    private static IReadOnlyList<GameCompatibilityAnchorEvidence> ProbeAnchors(
        string root,
        List<string> warnings)
    {
        var results = new List<GameCompatibilityAnchorEvidence>
        {
            new("erafl.system-title", false),
            new("erafl.init-loader", false),
            new("erafl.usercom", false),
            new("eratw.version", false),
            new("eratw.system-entry", false),
            new("eratw.title", false),
        };

        ProbeAnchorFile(
            root,
            new[] { "ERB", "SYSTEM", "NEWGAME.ERB" },
            text =>
            {
                SetMatch(results, "erafl.system-title", ContainsToken(text, "@SYSTEM_TITLE") && ContainsToken(text, "PRINTL", "eraFL"));
                SetMatch(results, "erafl.init-loader", ContainsToken(text, "FL_INIT_LOADER"));
                SetMatch(results, "erafl.usercom", ContainsToken(text, "FL_USERCOM"));
                bool isEraTwTitle = ContainsToken(text, "SYSTEM_TITLE") && ContainsToken(text, "eraThe World");
                SetMatch(results, "eratw.system-entry", isEraTwTitle);
                SetMatch(results, "eratw.title", isEraTwTitle);
            },
            warnings);

        // eraTW 魔改版本常把入口拆成 ERB/SYSTEM.ERB、ERB/TITLE.ERB 和
        // ERB/NEWGAME/NEWGAME.ERB，不能假设所有版本都使用 SYSTEM/NEWGAME.ERB。
        ProbeAnchorFile(
            root,
            new[] { "ERB", "SYSTEM.ERB" },
            text =>
            {
                bool isEraTwSystemEntry = ContainsToken(text, "@EVENTFIRST", "CALL NEWGAME", "eraTW_Version");
                SetMatch(results, "eratw.system-entry", isEraTwSystemEntry);
                SetMatch(results, "eratw.title", isEraTwSystemEntry);
            },
            warnings);

        ProbeAnchorFile(
            root,
            new[] { "ERB", "NEWGAME", "NEWGAME.ERB" },
            text =>
            {
                bool isEraTwNewGameEntry = ContainsToken(text, "@NEWGAME", "eraTW_Version");
                SetMatch(results, "eratw.system-entry", isEraTwNewGameEntry);
                SetMatch(results, "eratw.title", isEraTwNewGameEntry);
            },
            warnings);

        ProbeAnchorFile(
            root,
            new[] { "ERB", "TITLE.ERB" },
            text => SetMatch(
                results,
                "eratw.title",
                ContainsToken(text, "@SYSTEM_TITLE", "eraTW_Version")),
            warnings);

        ProbeAnchorFile(
            root,
            new[] { "ERB", "DIM.ERH" },
            text => SetMatch(results, "eratw.version", ContainsToken(text, "eraTW_Version")),
            warnings);

        ProbeAnchorFile(
            root,
            new[] { "ERB", "SYSTEM", "FL_INIT_LOADER.ERB" },
            text => SetMatch(results, "erafl.init-loader", ContainsToken(text, "@FL_INIT_LOADER")),
            warnings);

        ProbeAnchorFile(
            root,
            new[] { "ERB", "TRAIN", "USERCOM_INPUT.ERB" },
            text => SetMatch(results, "erafl.usercom", ContainsToken(text, "@FL_USERCOM")),
            warnings);

        // 标题锚点只能来自脚本内容，绝不能从目录名或可执行文件名生成。
        return results;
    }

    private static void ProbeAnchorFile(
        string root,
        IReadOnlyList<string> segments,
        Action<string> inspect,
        List<string> warnings)
    {
        string? path = TryFindPath(root, segments);
        if (path is null)
            return;
        if (!TryReadBounded(path, MaxAnchorBytes, out string text, out string warning))
        {
            warnings.Add(warning);
            return;
        }
        inspect(text);
    }

    private static string? TryFindPath(string root, IReadOnlyList<string> segments)
    {
        string current = root;
        for (int i = 0; i < segments.Count; i++)
        {
            string segment = segments[i];
            string direct = Path.Combine(current, segment);
            if ((i == segments.Count - 1 && File.Exists(direct))
                || (i < segments.Count - 1 && Directory.Exists(direct)))
            {
                current = direct;
                continue;
            }

            if (!Directory.Exists(current))
                return null;

            string? match = null;
            int inspected = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (++inspected > MaxDirectoryEntriesPerSegment)
                    return null;
                if (!string.Equals(Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase))
                    continue;
                match = entry;
                break;
            }

            if (match is null)
                return null;
            current = match;
        }
        return current;
    }

    private static bool TryReadBounded(string path, int maxBytes, out string text, out string warning)
    {
        text = string.Empty;
        warning = string.Empty;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                warning = "probe.file-missing";
                return false;
            }
            if (info.Length > maxBytes)
            {
                warning = "probe.file-too-large";
                return false;
            }

            byte[] bytes = File.ReadAllBytes(path);
            text = Decode(bytes);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            warning = "probe.permission-denied";
            return false;
        }
        catch (IOException)
        {
            warning = "probe.io-failed";
            return false;
        }
        catch (DecoderFallbackException)
        {
            warning = "probe.unsupported-encoding";
            return false;
        }
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                .GetString(bytes);
        }
    }

    private static string NormalizeRoot(string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            throw new ArgumentException("Game root is required.", nameof(gameRoot));
        if (!Path.IsPathRooted(gameRoot))
            throw new ArgumentException("Game root must be absolute.", nameof(gameRoot));

        string fullPath = Path.GetFullPath(gameRoot.Trim());
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException(fullPath);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsGameCodeKey(string key)
    {
        return key.Equals("コード", StringComparison.OrdinalIgnoreCase)
            || key.Equals("GAMECODE", StringComparison.OrdinalIgnoreCase)
            || key.Equals("GAME_CODE", StringComparison.OrdinalIgnoreCase)
            || key.Equals("CODE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTitleKey(string key)
    {
        return key.Equals("タイトル", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ゲームタイトル", StringComparison.OrdinalIgnoreCase)
            || key.Equals("TITLE", StringComparison.OrdinalIgnoreCase)
            || key.Equals("SCRIPT_TITLE", StringComparison.OrdinalIgnoreCase);
    }

    private static string Unquote(string value)
    {
        string result = value.Trim();
        if (result.Length >= 2 && result[0] == '"' && result[^1] == '"')
            return result[1..^1];
        return result;
    }

    private static bool ContainsToken(string text, params string[] tokens)
    {
        return tokens.All(token => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void SetMatch(
        List<GameCompatibilityAnchorEvidence> anchors,
        string anchorId,
        bool matched)
    {
        if (!matched)
            return;
        int index = anchors.FindIndex(anchor => anchor.AnchorId == anchorId);
        if (index >= 0)
            anchors[index] = new GameCompatibilityAnchorEvidence(anchorId, true);
    }
}

public static class GameCompatibilityDetector
{
    public static GameCompatibilityResolution Detect(string gameRoot)
    {
        var evidence = GameContentProbe.Probe(gameRoot);
        return BuiltInGameCompatibilityResolver.Resolve(evidence);
    }

    public static bool TryDetectProfile(string gameRoot, out string profileId, out GameCompatibilityResolution resolution)
    {
        try
        {
            resolution = Detect(gameRoot);
            if (resolution.CanAutoSelect)
            {
                profileId = resolution.ProfileId;
                return true;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DirectoryNotFoundException or ArgumentException)
        {
            resolution = new GameCompatibilityResolution(
                GameCompatibilityResolutionStatus.Invalid,
                GameCompatibilityConfidence.Unknown,
                null,
                BuiltInGameCompatibilityResolver.V24ProfileId,
                "builtin.game-identity.v1",
                "probe-failed");
        }

        profileId = string.Empty;
        return false;
    }
}
