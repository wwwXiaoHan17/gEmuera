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

    // —— 用户手装的过期第一方包：宿主注入判定后允许替换（2026-11-24 自愈）——————
    // 默认（不注入判定）行为不变：记录缺失/与记录不符的目标一律 KeptUser。

    /// <summary>用户手装包的目标路径；顺带建好一包一目录（用户是用文件管理器放进现有/新建目录的）。</summary>
    static string StaleTargetPath(string root)
    {
        string directory = Path.Combine(root, "pack.test.demo");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "pack.test.demo.dll");
    }

    [Fact]
    public void WithoutReplacer_UserPlacedTarget_IsStillKept()
    {
        string root = NewRoot();
        try
        {
            byte[] existing = DllPayload("user-stale");
            File.WriteAllBytes(StaleTargetPath(root), existing);

            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", DllPayload("fresh")) }, root, null, null);

            Assert.Equal(1, result.KeptUser);
            Assert.Equal(0, result.ReplacedStale);
            Assert.Equal(existing, File.ReadAllBytes(StaleTargetPath(root)));
            // 包目录里只有目标本身，没有留下备份或临时文件。
            Assert.Equal(new[] { StaleTargetPath(root) }, Directory.GetFiles(Path.GetDirectoryName(StaleTargetPath(root))!));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void StaleUserPlacedTarget_WhenReplacerApproves_IsReplacedAndBackedUp()
    {
        string root = NewRoot();
        try
        {
            byte[] existing = DllPayload("user-stale");
            File.WriteAllBytes(StaleTargetPath(root), existing);
            byte[] payload = DllPayload("fresh", fill: 0x77);
            var asked = new List<string>();

            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", payload) }, root, null,
                (pack, targetPath) =>
                {
                    asked.Add(pack.PackId + "|" + targetPath);
                    return true;
                });

            Assert.True(result.Succeeded, string.Join("; ", result.Errors));
            // 判定只对「记录缺失/不符」的目标发起，并拿到它要覆盖的确切路径。
            Assert.Equal(new[] { "pack.test.demo|" + StaleTargetPath(root) }, asked);
            Assert.Equal(0, result.KeptUser);
            Assert.Equal(0, result.Installed);
            Assert.Equal(1, result.ReplacedStale);
            Assert.Equal(new[] { "pack.test.demo" }, result.ReplacedStalePackIds);
            Assert.Equal(payload, File.ReadAllBytes(StaleTargetPath(root)));
            // 用户的旧文件留在同目录的隐藏备份里，操作可逆。
            string backup = Path.Combine(
                Path.GetDirectoryName(StaleTargetPath(root))!, ".pack.test.demo.dll" + CompatPackStalePackPolicy.StaleBackupSuffix);
            Assert.Equal(existing, File.ReadAllBytes(backup));
            // 状态记录写入新字节：此后按内置升级语义跟进。
            string state = File.ReadAllText(Path.Combine(root, CompatPackBundleInstaller.StateFileName));
            Assert.Contains(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant(), state);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void UserPlacedTarget_WhenReplacerDeclines_IsKeptWithoutBackup()
    {
        string root = NewRoot();
        try
        {
            byte[] existing = DllPayload("user-modified-but-usable");
            File.WriteAllBytes(StaleTargetPath(root), existing);

            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", DllPayload("fresh", fill: 0x78)) }, root, null,
                (_, _) => false);

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.KeptUser);
            Assert.Equal(0, result.ReplacedStale);
            Assert.Equal(existing, File.ReadAllBytes(StaleTargetPath(root)));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(StaleTargetPath(root))!));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void ReplacerThrows_TargetIsKeptAndFailureAggregated()
    {
        string root = NewRoot();
        try
        {
            byte[] existing = DllPayload("user-stale");
            File.WriteAllBytes(StaleTargetPath(root), existing);

            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", DllPayload("fresh", fill: 0x79)) }, root, null,
                (_, _) => throw new IOException("disk on fire"));

            Assert.False(result.Succeeded);
            Assert.Equal(1, result.KeptUser);
            Assert.Equal(0, result.ReplacedStale);
            Assert.Contains(result.Errors, error => error.Contains("pack.test.demo") && error.Contains("disk on fire"));
            Assert.Equal(existing, File.ReadAllBytes(StaleTargetPath(root)));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void StaleBackupCannotBeWritten_TargetIsNotReplaced()
    {
        string root = NewRoot();
        try
        {
            byte[] existing = DllPayload("user-stale");
            File.WriteAllBytes(StaleTargetPath(root), existing);
            // 备份文件名被一个目录占住：备份必然失败，此时绝不能先删用户的旧文件。
            string blockedBackup = Path.Combine(
                Path.GetDirectoryName(StaleTargetPath(root))!, ".pack.test.demo.dll" + CompatPackStalePackPolicy.StaleBackupSuffix);
            Directory.CreateDirectory(blockedBackup);

            var result = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", DllPayload("fresh", fill: 0x7A)) }, root, null,
                (_, _) => true);

            Assert.False(result.Succeeded);
            Assert.Equal(1, result.KeptUser);
            Assert.Equal(0, result.ReplacedStale);
            Assert.Equal(existing, File.ReadAllBytes(StaleTargetPath(root)));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Replacer_IsNeverAskedForTargetsTheInstallerOwns()
    {
        string root = NewRoot();
        try
        {
            var payloads = new[] { new CompatPackBundlePayload("pack.test.demo", DllPayload("v1")) };
            CompatPackBundleInstaller.InstallFromPayloads(payloads, root);
            var asked = 0;

            // 目标字节 == 内置字节（跳过）与 目标 == 上次内置写入（内置升级）都不该征求判定。
            var skipped = CompatPackBundleInstaller.InstallFromPayloads(payloads, root, null, (_, _) => { asked++; return false; });
            byte[] upgraded = DllPayload("v2", fill: 0x91);
            var upgrade = CompatPackBundleInstaller.InstallFromPayloads(
                new[] { new CompatPackBundlePayload("pack.test.demo", upgraded) }, root, null, (_, _) => { asked++; return false; });

            Assert.Equal(1, skipped.Skipped);
            Assert.Equal(1, upgrade.Installed);
            Assert.Equal(0, upgrade.ReplacedStale);
            Assert.Equal(0, asked);
        }
        finally { Cleanup(root); }
    }
}
