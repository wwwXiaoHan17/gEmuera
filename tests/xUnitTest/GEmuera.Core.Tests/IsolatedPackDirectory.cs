using System;
using System.IO;

namespace GEmuera.Core.Tests;

/// <summary>
/// 隔离的临时包目录（T5）：加载器把包目录顶层全部 <c>*.dll</c> 读成依赖字节闭包并纳入
/// <c>PackSha256</c>，共享 bin 输出目录（xunit/Core/其它夹具程序集）会让"包"语义失真
///（依赖闭包变成整个测试输出、失败路径也会被无关程序集干扰）。测试统一把被测包程序集
///（及显式声明的依赖）复制进只含这些文件的临时目录再加载，用完确定性删除。
/// </summary>
sealed class IsolatedPackDirectory : IDisposable
{
    public IsolatedPackDirectory(string sourceAssemblyPath, params string[] additionalDependencyPaths)
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(), "compat-pack-isolated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        MainAssemblyPath = CopyInto(sourceAssemblyPath);
        foreach (string dependency in additionalDependencyPaths)
            CopyInto(dependency);
    }

    /// <summary>临时包目录的绝对路径。</summary>
    public string DirectoryPath { get; }

    /// <summary>复制进临时目录后的主程序集路径（传给 <c>CompatPackLoader.TryLoad</c>）。</summary>
    public string MainAssemblyPath { get; }

    /// <summary>在临时包目录内写入一个依赖 DLL（文件名字面量，内容由调用方给出）。</summary>
    public string WriteAssembly(string fileName, byte[] bytes)
    {
        string path = Path.Combine(DirectoryPath, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>把包程序集字节替换进已存在的临时目录文件（同一路径，模拟重发布）。</summary>
    public void WriteMainAssembly(byte[] bytes) => File.WriteAllBytes(MainAssemblyPath, bytes);

    string CopyInto(string path)
    {
        string destination = Path.Combine(DirectoryPath, Path.GetFileName(path));
        File.Copy(path, destination, overwrite: true);
        return destination;
    }

    public void Dispose()
    {
        // 包 ALC（isCollectible）的程序集来自内存流，不映射磁盘文件；但入口实例/反射缓存
        // 仍由 GC 驱动释放。先收集再有限重试删除，避免测试遗留临时目录。
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                    Directory.Delete(DirectoryPath, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                if (attempt == 4)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                Thread.Sleep(100);
            }
        }
    }
}
