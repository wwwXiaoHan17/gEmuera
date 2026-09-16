using System;
using Emuera.Compatibility.Packs;
using Xunit;

namespace EmueraFacade.Tests;

/// <summary>
/// 程序集内嵌清单的资源发现路径：规范资源名 Ordinal 精确匹配；
/// 缺清单的程序集不是包（fail-closed），错误文案指名资源与程序集。
/// </summary>
public class CompatPackAssemblyDiscoveryTests
{
    [Fact]
    public void LoadFromTestAssembly_WithEmbeddedManifest_Succeeds()
    {
        // 测试工程以 LogicalName=compatpack.manifest.json 内嵌合法样例（见 csproj）。
        Assert.True(
            CompatPackManifest.TryLoadFromAssembly(typeof(CompatPackManifestTests).Assembly, out var manifest, out var errors),
            string.Join("; ", errors));

        Assert.Equal("test.hello-pack", manifest!.PackId);
        Assert.Equal("gemuera.hello", manifest.SaveProfileId);
        Assert.Equal("builtin:snake", manifest.VariantSelections["SETBGIMAGE"]);
    }

    [Fact]
    public void LoadFromAssembly_WithoutManifest_FailsWithResourceName()
    {
        Assert.False(CompatPackManifest.TryLoadFromAssembly(typeof(object).Assembly, out _, out var errors));
        Assert.Contains(errors, e => e.Contains(CompatPackManifest.ManifestResourceName));
    }

    [Fact]
    public void LoadFromNullAssembly_Fails()
    {
        Assert.False(CompatPackManifest.TryLoadFromAssembly(null!, out _, out var errors));
        Assert.NotEmpty(errors);
    }
}
