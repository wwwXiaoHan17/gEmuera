using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

public class CompatPackProfileMapTests
{
    const string ValidJson = """
    {
      "schemaVersion": "1.0.0",
      "profiles": [
        {
          "profileId": "v18",
          "packId": "pack.gemuera.v18",
          "relativePath": "compat_packs/pack.gemuera.v18/pack.gemuera.v18.dll",
          "relativePathAndroid": "packs/pack.gemuera.v18/pack.gemuera.v18.dll",
          "requiredPackVersion": "1.0.0",
          "note": "first-party"
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ValidMap_ExposesEntry()
    {
        Assert.True(CompatPackProfileMap.TryParse(ValidJson, out CompatPackProfileMap? map, out var errors), string.Join("; ", errors));
        Assert.Empty(errors);
        Assert.NotNull(map);
        Assert.True(map!.TryGetEntry("v18", out CompatPackProfileMapEntry entry));
        Assert.Equal("pack.gemuera.v18", entry.PackId);
        Assert.Equal("packs/pack.gemuera.v18/pack.gemuera.v18.dll", entry.RelativePathAndroid);
    }

    [Fact]
    public void ResolveEngineProfileId_MappedProfile_TranslatesToV24Pure()
    {
        Assert.True(CompatPackProfileMap.TryParse(ValidJson, out CompatPackProfileMap? map, out _));
        Assert.Equal("v24pure", map!.ResolveEngineProfileId("v18"));
        Assert.Equal("v24pure", map.ResolveEngineProfileId("V18"));
        Assert.Equal("snake", map.ResolveEngineProfileId("snake"));
        Assert.Equal("v24pure", map.ResolveEngineProfileId(null));
    }

    [Fact]
    public void Parse_DuplicateProfileId_Rejects()
    {
        const string json = """
        { "schemaVersion": "1.0.0", "profiles": [
          { "profileId": "v18", "packId": "pack.gemuera.v18", "relativePath": "compat_packs/v18/v18.dll", "requiredPackVersion": "1.0.0" },
          { "profileId": "v18", "packId": "pack.gemuera.v18", "relativePath": "compat_packs/v18/v18.dll", "requiredPackVersion": "1.0.0" }
        ] }
        """;
        Assert.False(CompatPackProfileMap.TryParse(json, out _, out var errors));
        Assert.Contains(errors, error => error.Contains("重复", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_MissingRequiredField_Rejects()
    {
        const string json = """
        { "schemaVersion": "1.0.0", "profiles": [
          { "profileId": "v18", "relativePath": "compat_packs/v18/v18.dll", "requiredPackVersion": "1.0.0" }
        ] }
        """;
        Assert.False(CompatPackProfileMap.TryParse(json, out _, out var errors));
        Assert.Contains(errors, error => error.Contains("packId", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_UnknownField_Rejects()
    {
        const string json = """
        { "schemaVersion": "1.0.0", "profiles": [
          { "profileId": "v18", "packId": "pack.gemuera.v18", "relativePath": "compat_packs/v18/v18.dll", "requiredPackVersion": "1.0.0", "typo": true }
        ] }
        """;
        Assert.False(CompatPackProfileMap.TryParse(json, out _, out var errors));
        Assert.Contains(errors, error => error.Contains("未知", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_WrongSchemaMajor_Rejects()
    {
        Assert.False(CompatPackProfileMap.TryParse("{\"schemaVersion\":\"2.0.0\",\"profiles\":[]}", out _, out var errors));
        Assert.Contains(errors, error => error.Contains("schemaVersion", StringComparison.Ordinal));
    }

    [Fact]
    public void LauncherProfileSaveProfileId_PreservesHostSaveProfiles()
    {
        Assert.Equal("gemuera.v24", CompatPackProfileMap.LauncherProfileSaveProfileId("v24pure"));
        Assert.Equal("gemuera.v24", CompatPackProfileMap.LauncherProfileSaveProfileId("v18"));
        Assert.Equal("gemuera.v24", CompatPackProfileMap.LauncherProfileSaveProfileId("erablue"));
        Assert.Equal("gemuera.snake", CompatPackProfileMap.LauncherProfileSaveProfileId("snake"));
        Assert.Equal("gemuera.erafl", CompatPackProfileMap.LauncherProfileSaveProfileId("erafl"));
        Assert.Equal("gemuera.megaten", CompatPackProfileMap.LauncherProfileSaveProfileId("megaten"));
    }
    [Fact]
    public void ProductionMap_EntriesMatchOnePackPerDirectoryContract()
    {
        string repoRoot = FindRepoRoot();
        string mapPath = Path.Combine(repoRoot, "assets", "text", "pack-profile-map.json");
        Assert.True(File.Exists(mapPath), "生产映射缺失：" + mapPath);
        Assert.True(
            CompatPackProfileMap.TryParse(File.ReadAllText(mapPath), out CompatPackProfileMap? map, out var errors),
            string.Join("; ", errors));
        foreach (string profileId in new[] { "v18", "megaten", "erablue", "erafl", "snake" })
        {
            Assert.True(map!.TryGetEntry(profileId, out CompatPackProfileMapEntry entry), "缺少映射条目：" + profileId);
            AssertPathShape(entry, entry.RelativePath, "compat_packs");
            AssertPathShape(entry, entry.RelativePathAndroid, "packs");
        }
    }

    static void AssertPathShape(CompatPackProfileMapEntry entry, string path, string root)
    {
        string[] segments = path.Split('/');
        Assert.Equal(3, segments.Length);
        Assert.Equal(root, segments[0]);
        Assert.Equal(entry.PackId, segments[1]);
        Assert.Equal(entry.PackId + ".dll", segments[2]);
    }

    static string FindRepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "gemuera-c#.csproj")))
                return directory;
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("找不到仓库根（gemuera-c#.csproj）。");
    }
}
