using System.Text;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 第一方包 installer 的纯逻辑门禁（执行设计 §6.1/§6.2）：全新安装、同字节跳过、
/// 内置升级、用户改过保留、记录缺失保留、损坏载荷/非法 packId/权限失败聚合。
/// </summary>
public class CompatPackBundleInstallerTests
{
    static byte[] DllPayload(string marker, byte fill = 0x90)
    {
        var bytes = new byte[256];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        byte[] tail = Encoding.UTF8.GetBytes(marker);
        Array.Copy(tail, 0, bytes, bytes.Length - tail.Length, tail.Length);
        for (int i = 2; i < bytes.Length - tail.Length; i++)
            bytes[i] = fill;
        return bytes;
    }

    static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "gemuera-bundle-installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch { /* 测试清理失败不影响断言。 */ }
    }

    [Fact]
    public void FreshInstall_WritesDllAndStateRecord()
    {
        string root = NewRoot();
        try
        {
            byte[] payload = DllPayload("v1");
            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", payload) }, root);

            Assert.True(result.Succeeded, string.Join("; ", result.Errors));
            Assert.Equal(1, result.Installed);
            Assert.Equal(0, result.Failed);
            Assert.Equal(new[] { "pack.test.demo" }, result.InstalledPackIds);
            string target = Path.Combine(root, "pack.test.demo", "pack.test.demo.dll");
            Assert.Equal(payload, File.ReadAllBytes(target));
            string state = File.ReadAllText(Path.Combine(root, CompatPackBundleInstaller.StateFileName));
            Assert.Contains("pack.test.demo", state);
            Assert.Contains(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant(),
                state);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void SameBytes_IsSkippedWithoutRewrite()
    {
        string root = NewRoot();
        try
        {
            byte[] payload = DllPayload("same");
            var payloads = new[] { new CompatPackBundlePayload("pack.test.demo", payload) };
            CompatPackBundleInstaller.InstallFromPayloads(payloads, root);
            var result = CompatPackBundleInstaller.InstallFromPayloads(payloads, root);

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, result.Installed);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void BuiltinUpgrade_OverwritesPreviouslyInstalledBytes()
    {
        string root = NewRoot();
        try
        {
            CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", DllPayload("v1")) }, root);
            byte[] upgraded = DllPayload("v2", fill: 0x91);
            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", upgraded) }, root);

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Installed);
            Assert.Equal(0, result.KeptUser);
            Assert.Equal(upgraded, File.ReadAllBytes(Path.Combine(root, "pack.test.demo", "pack.test.demo.dll")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void UserModifiedTarget_IsKeptNotOverwritten()
    {
        string root = NewRoot();
        try
        {
            CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", DllPayload("v1")) }, root);
            string target = Path.Combine(root, "pack.test.demo", "pack.test.demo.dll");
            byte[] userBytes = DllPayload("user", fill: 0x42);
            File.WriteAllBytes(target, userBytes);

            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", DllPayload("v2", fill: 0x93)) }, root);

            Assert.True(result.Succeeded);
            Assert.Equal(0, result.Installed);
            Assert.Equal(1, result.KeptUser);
            Assert.Equal(userBytes, File.ReadAllBytes(target));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void MissingStateRecord_WithExistingTarget_KeepsUserVersion()
    {
        string root = NewRoot();
        try
        {
            string packDirectory = Path.Combine(root, "pack.test.demo");
            Directory.CreateDirectory(packDirectory);
            string target = Path.Combine(packDirectory, "pack.test.demo.dll");
            byte[] existing = DllPayload("old-install");
            File.WriteAllBytes(target, existing);

            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", DllPayload("v2", fill: 0x94)) }, root);

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.KeptUser);
            Assert.Equal(existing, File.ReadAllBytes(target));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void CorruptPayload_IsRejectedWithoutWritingTarget()
    {
        string root = NewRoot();
        try
        {
            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", new byte[] { 1, 2, 3 }) }, root);

            Assert.False(result.Succeeded);
            Assert.Equal(1, result.Failed);
            Assert.Contains(result.Errors, error => error.Contains("损坏"));
            Assert.False(File.Exists(Path.Combine(root, "pack.test.demo", "pack.test.demo.dll")));
        }
        finally { Cleanup(root); }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData("Bad/Id")]
    [InlineData("")]
    public void InvalidPackId_IsRejected(string packId)
    {
        string root = NewRoot();
        try
        {
            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload(packId, DllPayload("x")) }, root);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, error => error.Contains("packId"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void UnusableTargetRoot_FailsWithoutThrowing()
    {
        string root = NewRoot();
        try
        {
            // 安装根被一个同名文件占据：创建目录必然失败，结果必须聚合为 Failed 而不是抛异常。
            string blocked = Path.Combine(root, "blocked-root");
            File.WriteAllText(blocked, "not a directory");
            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", DllPayload("x")) }, blocked);

            Assert.False(result.Succeeded);
            Assert.Equal(1, result.Failed);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void CorruptStateFile_IsTreatedAsMissingAndKeepsExistingTarget()
    {
        string root = NewRoot();
        try
        {
            string packDirectory = Path.Combine(root, "pack.test.demo");
            Directory.CreateDirectory(packDirectory);
            string target = Path.Combine(packDirectory, "pack.test.demo.dll");
            byte[] existing = DllPayload("existing");
            File.WriteAllBytes(target, existing);
            File.WriteAllText(Path.Combine(root, CompatPackBundleInstaller.StateFileName), "{not json");

            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", DllPayload("v2", fill: 0x95)) }, root);

            Assert.Equal(1, result.KeptUser);
            Assert.Contains(result.Errors, error => error.Contains("状态记录损坏"));
            Assert.Equal(existing, File.ReadAllBytes(target));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void InstallFromDirectory_EnumeratesOnePackPerDirectory()
    {
        string root = NewRoot();
        try
        {
            string source = Path.Combine(root, "staging");
            Directory.CreateDirectory(Path.Combine(source, "pack.test.alpha"));
            Directory.CreateDirectory(Path.Combine(source, "pack.test.beta"));
            File.WriteAllBytes(
                Path.Combine(source, "pack.test.alpha", "pack.test.alpha.dll"), DllPayload("alpha"));
            // beta 缺主 DLL：必须报错而不是静默跳过。

            var result = CompatPackBundleInstaller.InstallFromDirectory(source, Path.Combine(root, "target"));

            Assert.False(result.Succeeded);
            Assert.Equal(1, result.Installed);
            Assert.Contains(result.Errors, error => error.Contains("pack.test.beta"));
            Assert.True(File.Exists(Path.Combine(root, "target", "pack.test.alpha", "pack.test.alpha.dll")));
        }
        finally { Cleanup(root); }
    }
}
