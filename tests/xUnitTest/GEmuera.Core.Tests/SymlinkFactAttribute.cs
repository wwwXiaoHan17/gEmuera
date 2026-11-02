using System;
using System.IO;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 环境能力门控的 <c>[Fact]</c>：discovery 阶段探测本机能否创建文件符号链接，不能则把
/// <see cref="FactAttribute.Skip"/> 置为原因——xUnit 报告 <c>skipped</c>，而不是让测试体
/// <c>return</c> 被记成 passed。**跳过 ≠ 通过**：被跳过的安全路径没有被验证，gate 必须能从
/// 计数上看出来。
/// 为什么不用 <c>Assert.Skip</c>：xunit 2.9.2 的 <c>Xunit.Sdk.SkipException.ForSkip</c> 文档
/// 明确写明动态跳过只支持 xUnit.net v3（需要 runner 的运行时基础设施）；本项目 runner 是 v2，
/// 因此按 brief 的兜底方案用自定义 FactAttribute 在 discovery 期判能力。
/// </summary>
sealed class SymlinkFactAttribute : FactAttribute
{
    public SymlinkFactAttribute()
    {
        if (!SymlinkCapability.IsAvailable(out string reason))
            Skip = "当前环境不能创建符号链接（" + reason + "）；跳过 ≠ 通过，该安全路径未被验证。";
    }
}

/// <summary>探测一次并缓存：临时目录内创建 target 文件 + 符号链接，成功即具备能力。</summary>
static class SymlinkCapability
{
    static readonly Lazy<(bool Available, string Reason)> probe = new(Probe);

    public static bool IsAvailable(out string reason)
    {
        (bool available, string text) = probe.Value;
        reason = text;
        return available;
    }

    static (bool Available, string Reason) Probe()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "compat-pack-symlink-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, "target.dll");
            File.WriteAllBytes(target, new byte[] { 0x4d, 0x5a });
            File.CreateSymbolicLink(Path.Combine(directory, "link.dll"), target);
            return (true, string.Empty);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException or PlatformNotSupportedException or NotSupportedException)
        {
            return (false, exception.GetType().Name + ": " + exception.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // 探测目录清理失败不影响能力判定（系统临时目录）。
            }
        }
    }
}
