using System.Text;
using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 真实第一方包字节夹具（供预检 / 释放策略 / 改绑判定三类测试共用）：
/// 新包直接取 snake 包程序集；过期包用「等长改写内嵌清单里的 baseSurfaceHash」制造——
/// 清单是未压缩资源，64 位十六进制换成 64 个 0 不破坏 PE 布局，等于一份按旧表面快照发布的同名包。
/// </summary>
internal static class FirstPartyPackBytes
{
    public const string SnakePackId = "pack.gemuera.snake";

    /// <summary>永远不等于任何真实基线表面哈希的合法取值（64 位小写十六进制）。</summary>
    public static readonly string StaleHash = new('0', 64);

    static readonly Lazy<byte[]> FreshSnakeBytes = new(
        () => File.ReadAllBytes(typeof(PackGemueraSnake.PackGemueraSnakeMarker).Assembly.Location));

    static readonly Lazy<byte[]> StaleSnakeBytes = new(
        () => ReplaceAll(FreshSnakeBytes.Value, DeclaredSnakeHash(), StaleHash));

    /// <summary>与 FirstPartyPackEquivalenceTests 同口径的加载校验上下文。</summary>
    public static CompatPackValidationContext Context() => new(
        currentContractAbi: new CompatVersion(1, 1),
        currentEngineApi: new CompatVersion(1, 0),
        capabilities: EngineCapabilityCatalog.All,
        knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        baselineInstructions: new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        baselineFunctions: new HashSet<string>(
            LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal),
        reservedModuleIds: new HashSet<string>(StringComparer.Ordinal) { "game.snake" },
        knownBuiltinVariantInstructions: new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["builtin:snake"] = new HashSet<string>(StringComparer.Ordinal) { "FOR", "SETBGIMAGE" },
        },
        methodProjectedInstructionNames: new HashSet<string>(
            LegacyDialectInventories.MethodProjectedInstructionNames, StringComparer.Ordinal));

    public static CompatPackEngineHeader EngineHeader() => CompatPackEngineHeader.FromContext(Context());

    public static byte[] FreshSnake() => (byte[])FreshSnakeBytes.Value.Clone();

    /// <summary>清单按旧表面快照发布的同名 snake 包（新引擎必然拒载）。</summary>
    public static byte[] StaleSnake() => (byte[])StaleSnakeBytes.Value.Clone();

    public static string DeclaredSnakeHash()
    {
        Assert.True(CompatPackManifest.TryLoadFromAssembly(
            typeof(PackGemueraSnake.PackGemueraSnakeMarker).Assembly, out var manifest, out var errors),
            string.Join("; ", errors));
        return manifest!.BaseSurfaceHash!;
    }

    /// <summary>改一个与清单无关的字节（PE 头的链接时间戳），得到字节不同但清单完全相同的包。</summary>
    public static byte[] WithDifferentTimestamp(byte[] packBytes)
    {
        byte[] result = (byte[])packBytes.Clone();
        int peOffset = BitConverter.ToInt32(result, 0x3C);
        Assert.Equal((byte)'P', result[peOffset]);
        Assert.Equal((byte)'E', result[peOffset + 1]);
        // COFF 头：Machine(2) NumberOfSections(2) TimeDateStamp(4)——翻转时间戳的最高位字节。
        result[peOffset + 8 + 3] ^= 0x5A;
        return result;
    }

    static byte[] ReplaceAll(byte[] source, string from, string to)
    {
        byte[] needle = Encoding.ASCII.GetBytes(from);
        byte[] replacement = Encoding.ASCII.GetBytes(to);
        Assert.Equal(needle.Length, replacement.Length);
        byte[] result = (byte[])source.Clone();
        int replaced = 0;
        for (int index = 0; index <= result.Length - needle.Length; index++)
        {
            if (result[index] != needle[0])
                continue;
            bool matches = true;
            for (int offset = 1; offset < needle.Length; offset++)
            {
                if (result[index + offset] != needle[offset])
                {
                    matches = false;
                    break;
                }
            }
            if (!matches)
                continue;
            Array.Copy(replacement, 0, result, index, replacement.Length);
            replaced++;
            index += needle.Length - 1;
        }
        Assert.True(replaced > 0, "程序集字节里找不到待替换文本：" + from);
        return result;
    }
}
