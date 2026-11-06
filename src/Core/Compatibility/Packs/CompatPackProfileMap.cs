#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 生产映射 assets/text/pack-profile-map.json 的一条记录（执行设计 §4.5 / P-D spec §2）：
/// launcher 层 profile id 对应第一方 packId 与安装根相对路径；条目存在即表示该 profile
/// 已由兼容包供给（引擎侧不再注册内置 profile）。
/// </summary>
public sealed class CompatPackProfileMapEntry
{
    internal CompatPackProfileMapEntry(
        string profileId,
        string packId,
        string relativePath,
        string relativePathAndroid,
        string requiredPackVersion)
    {
        ProfileId = profileId;
        PackId = packId;
        RelativePath = relativePath;
        RelativePathAndroid = relativePathAndroid;
        RequiredPackVersion = requiredPackVersion;
    }

    public string ProfileId { get; }
    public string PackId { get; }
    public string RelativePath { get; }
    public string RelativePathAndroid { get; }
    public string RequiredPackVersion { get; }
}

/// <summary>
/// 生产 profile→第一方兼容包映射的纯逻辑（launcher 层概念；引擎永远只见 v24pure + 包路径）。
/// 解析 fail-closed：未知字段 / 重复 profileId / 非法 id / 绝对路径或路径穿越一律整体拒绝。
/// </summary>
public sealed class CompatPackProfileMap
{
    public const string SupportedSchemaVersion = "1.0.0";

    readonly IReadOnlyDictionary<string, CompatPackProfileMapEntry> entries;

    CompatPackProfileMap(IReadOnlyDictionary<string, CompatPackProfileMapEntry> entries)
    {
        this.entries = entries;
    }

    public IReadOnlyDictionary<string, CompatPackProfileMapEntry> Entries => entries;

    public static bool TryParse(
        string? json,
        out CompatPackProfileMap? map,
        out IReadOnlyList<string> errors)
    {
        var collected = new List<string>();
        map = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            collected.Add("profile 映射内容为空。");
            errors = collected;
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json.TrimStart('\uFEFF'), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        }
        catch (JsonException exception)
        {
            collected.Add("profile 映射不是合法 JSON：" + exception.Message);
            errors = collected;
            return false;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                collected.Add("profile 映射根必须是 JSON 对象。");
                errors = collected;
                return false;
            }

            string? schemaVersion = null;
            bool hasProfiles = false;
            var parsedEntries = new Dictionary<string, CompatPackProfileMapEntry>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "schemaVersion":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            collected.Add("字段 'schemaVersion' 不是字符串。");
                            break;
                        }
                        schemaVersion = property.Value.GetString();
                        break;
                    case "description":
                        if (property.Value.ValueKind != JsonValueKind.String)
                            collected.Add("字段 'description' 不是字符串。");
                        break;
                    case "profiles":
                        hasProfiles = true;
                        ReadProfiles(property.Value, parsedEntries, collected);
                        break;
                    default:
                        collected.Add("profile 映射含未知字段：'" + property.Name + "'。");
                        break;
                }
            }

            if (string.IsNullOrEmpty(schemaVersion))
                collected.Add("字段 'schemaVersion' 缺失。");
            else if (!IsSupportedSchemaVersion(schemaVersion))
                collected.Add("字段 'schemaVersion' 版本不受支持（launcher 只认识 1.x）：" + schemaVersion);
            if (!hasProfiles)
                collected.Add("字段 'profiles' 缺失。");

            errors = collected;
            if (collected.Count > 0)
                return false;

            map = new CompatPackProfileMap(
                new ReadOnlyDictionary<string, CompatPackProfileMapEntry>(parsedEntries));
            return true;
        }
    }

    /// <summary>按 launcher profile id 查条目（Trim + Ordinal；launcher id 均为小写常量）。</summary>
    public bool TryGetEntry(string? launcherProfileId, out CompatPackProfileMapEntry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(launcherProfileId))
            return false;
        string key = launcherProfileId.Trim();
        if (entries.TryGetValue(key, out entry!))
            return true;
        // launcher.cfg/CLI 历史入口大小写语义：显式常量小写，但旧配置可能是混合大小写。
        foreach (KeyValuePair<string, CompatPackProfileMapEntry> pair in entries)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                entry = pair.Value;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// launcher profile id → 引擎 profile id：命中映射（= 该 profile 已由包供给）→ v24pure；
    /// 未命中原样返回（未退役 profile 继续走内置链路，行为零变化）。
    /// </summary>
    public string ResolveEngineProfileId(string? launcherProfileId)
    {
        string original = (launcherProfileId ?? string.Empty).Trim();
        if (original.Length == 0)
            return "v24pure";
        return TryGetEntry(original, out _) ? "v24pure" : original;
    }

    /// <summary>
    /// 宿主 save-profile 映射（v1 包通道不能声明 saveProfileId；engine plan.SaveProfileId
    /// 退役后为 null，宿主必须按 launcher profile 保留存档语义）。未知/基线 id 沿用
    /// 历史默认 gemuera.v24。
    /// </summary>
    public static string LauncherProfileSaveProfileId(string? launcherProfileId)
    {
        string normalized = (launcherProfileId ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "snake" => "gemuera.snake",
            "erafl" => "gemuera.erafl",
            "megaten" => "gemuera.megaten",
            _ => "gemuera.v24",
        };
    }

    static void ReadProfiles(
        JsonElement profilesElement,
        Dictionary<string, CompatPackProfileMapEntry> entries,
        List<string> collected)
    {
        if (profilesElement.ValueKind != JsonValueKind.Array)
        {
            collected.Add("字段 'profiles' 必须是数组。");
            return;
        }

        foreach (JsonElement item in profilesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                collected.Add("字段 'profiles' 含非对象元素。");
                continue;
            }

            string? profileId = null;
            string? packId = null;
            string? relativePath = null;
            string? relativePathAndroid = null;
            string? requiredPackVersion = null;
            bool hasProfileId = false;
            bool hasPackId = false;
            bool hasRelativePath = false;
            bool hasRequiredPackVersion = false;
            foreach (JsonProperty member in item.EnumerateObject())
            {
                switch (member.Name)
                {
                    case "profileId":
                        hasProfileId = true;
                        profileId = ReadIdentifier(member.Value, "profiles[].profileId", collected);
                        break;
                    case "packId":
                        hasPackId = true;
                        packId = ReadIdentifier(member.Value, "profiles[].packId", collected);
                        break;
                    case "relativePath":
                        hasRelativePath = true;
                        relativePath = ReadRelativePath(member.Value, "profiles[].relativePath", collected);
                        break;
                    case "relativePathAndroid":
                        relativePathAndroid = ReadRelativePath(member.Value, "profiles[].relativePathAndroid", collected);
                        break;
                    case "requiredPackVersion":
                        hasRequiredPackVersion = true;
                        requiredPackVersion = ReadSemver(member.Value, "profiles[].requiredPackVersion", collected);
                        break;
                    case "note":
                        if (member.Value.ValueKind != JsonValueKind.String)
                            collected.Add("字段 'profiles[].note' 不是字符串。");
                        break;
                    default:
                        collected.Add("字段 'profiles[]' 含未知成员：'" + member.Name + "'。");
                        break;
                }
            }

            if (!hasProfileId)
                collected.Add("字段 'profiles[]' 缺少 profileId。");
            if (!hasPackId)
                collected.Add("字段 'profiles[]' 缺少 packId。");
            if (!hasRelativePath)
                collected.Add("字段 'profiles[]' 缺少 relativePath。");
            if (!hasRequiredPackVersion)
                collected.Add("字段 'profiles[]' 缺少 requiredPackVersion。");
            if (profileId is null || packId is null || relativePath is null || requiredPackVersion is null)
                continue;
            if (!entries.TryAdd(profileId, new CompatPackProfileMapEntry(
                    profileId,
                    packId,
                    relativePath,
                    relativePathAndroid ?? relativePath,
                    requiredPackVersion)))
            {
                collected.Add("字段 'profiles' 的 profileId 重复：" + profileId);
            }
        }
    }

    static string? ReadIdentifier(JsonElement value, string field, List<string> collected)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            collected.Add("字段 '" + field + "' 不是字符串。");
            return null;
        }
        string text = (value.GetString() ?? string.Empty).Trim();
        if (!IsLowerIdentifier(text))
        {
            collected.Add("字段 '" + field + "' 形式不合法（小写字母数字、点、连字符）：" + text);
            return null;
        }
        return text;
    }

    static string? ReadRelativePath(JsonElement value, string field, List<string> collected)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            collected.Add("字段 '" + field + "' 不是字符串。");
            return null;
        }
        string text = (value.GetString() ?? string.Empty).Trim().Replace('\\', '/');
        if (text.Length == 0)
        {
            collected.Add("字段 '" + field + "' 为空。");
            return null;
        }
        if (text.StartsWith('/'))
        {
            collected.Add("字段 '" + field + "' 必须是相对安装根的路径：" + text);
            return null;
        }
        string[] segments = text.Split('/');
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                collected.Add("字段 '" + field + "' 含空段/路径穿越（. 或 ..）：" + text);
                return null;
            }
        }
        return text;
    }

    static string? ReadSemver(JsonElement value, string field, List<string> collected)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            collected.Add("字段 '" + field + "' 不是字符串。");
            return null;
        }
        string text = (value.GetString() ?? string.Empty).Trim();
        string[] parts = text.Split('.');
        bool valid = parts.Length == 3;
        if (valid)
        {
            foreach (string part in parts)
            {
                if (part.Length == 0)
                {
                    valid = false;
                    break;
                }
                foreach (char c in part)
                {
                    if (c < '0' || c > '9')
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid)
                    break;
            }
        }
        if (!valid)
        {
            collected.Add("字段 '" + field + "' 形式不合法（x.y.z）：" + text);
            return null;
        }
        return text;
    }

    static bool IsSupportedSchemaVersion(string text)
    {
        int dot = text.IndexOf('.');
        if (dot <= 0)
            return false;
        return text.AsSpan(0, dot).SequenceEqual("1");
    }

    static bool IsLowerIdentifier(string text)
    {
        if (text.Length == 0)
            return false;
        char first = text[0];
        bool firstOk = (first >= 'a' && first <= 'z') || (first >= '0' && first <= '9');
        if (!firstOk)
            return false;
        foreach (char c in text)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-';
            if (!ok)
                return false;
        }
        return true;
    }
}
