using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 真实 ALC 回收（T8，docs/designs/compat-pack-interface.md §5.4 / 计划 E2-R2）：
/// 不再用 CountingLease 代替真实资源——每轮用真实 <see cref="CompatPackLoader"/> 加载
/// HelloCompatPackFixture（独立可回收 ALC），用 <see cref="CompatPackSession"/> 接管句柄，
/// 记录 ALC 与入口实例的 WeakReference，Dispose 后清除测试侧强引用（装载逻辑放在
/// NoInlining 的独立方法里、只返回 WeakReference），再连续 GC 三轮并断言两者都不可达。
/// 重复三轮（每轮新加载）证明回收不是一次性的 GC 巧合；最后检查 loader/session 静态面
/// 无残留（BytesReaderOverrideForTest 必须为 null，静态字段不得持有 ALC/租约/句柄），
/// 以及 AppDomain 中不再存在来自包 ALC 的（Location 为空的）夹具程序集。
/// 说明：引擎侧 Program.m1CompatPackSession 属 Scripts/（Core 测试不可达），其清零点由
/// governance 静态守卫 tools/compat-pack/Test-CompatPackLifecycle.ps1 检查
/// （DisposeCompatPackSession 必须先置 null 再 session.Dispose()）；本类的静态残留
/// 检查落在 session/loader/handle/load-context 这四个 Core 侧类型上。
/// </summary>
[Collection("CompatPack")]
public class CompatPackAlcReclamationTests
{
    const int Rounds = 3;
    const string FixtureAssemblyName = "HelloCompatPackFixture";

    static string PackPath => typeof(HelloCompatPackFixture.HelloCompatPack).Assembly.Location;

    [Fact]
    public void SessionDispose_UnloadsRealAlcAndAllowsCollection()
    {
        using var directory = new IsolatedPackDirectory(PackPath);
        for (int round = 1; round <= Rounds; round++)
        {
            (WeakReference alc, WeakReference pack) =
                LoadSessionDisposeAndDetach(directory.MainAssemblyPath);
            ForceCollect();
            Assert.False(
                alc.IsAlive,
                "第 " + round + " 轮：session.Dispose() 后包 ALC 仍可达（真实卸载未发生或仍有强引用）。");
            Assert.False(
                pack.IsAlive,
                "第 " + round + " 轮：session.Dispose() 后包入口实例仍可达（句柄/会话静态残留？）。");
        }

        AssertNoStaticResidue();
        AssertNoLivePackContext(directory.MainAssemblyPath);
    }

    /// <summary>
    /// 单独方法持有 handle/session 强引用：返回后栈帧销毁，只留 WeakReference。
    /// NoInlining 防止 JIT 把局部变量生命周期延长到调用方。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static (WeakReference Alc, WeakReference Pack) LoadSessionDisposeAndDetach(string packPath)
    {
        Assert.True(
            CompatPackLoader.TryLoad(packPath, CompatPackCompositionTestData.Context(), out var handle, out var errors),
            string.Join("; ", errors));
        // 成功路径只允许 session.Dispose() 释放租约：若 finally 也兜底 Unload，即使
        // session.Dispose() 完全不卸载，WeakReference 仍会死，核心断言就失去区分力。
        // 因此 finally 只在创建/Dispose 失败（sessionDisposed == false）时清理。
        bool sessionDisposed = false;
        try
        {
            Assert.Equal("test.hello-pack", handle!.Manifest.PackId);
            // WeakReference 只能证明“ALC 最终不可达”：可回收 ALC 在无人引用时也可能被
            // GC 顺手回收，无法区分 session 是否真的调用了 Unload。Unloading 事件只由
            // AssemblyLoadContext.Unload() 触发，是“session 主动卸载”的直接证据
            // （T8 review Important #1 的 RED 判据）。
            var unloadObserved = new StrongBox<bool>(false);
            handle.LoadContext.Unloading += _ => unloadObserved.Value = true;
            var plan = BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");
            var session = new CompatPackSession(plan, new[] { handle });
            var alcReference = new WeakReference(handle.LoadContext);
            var packReference = new WeakReference(handle.Pack);
            Assert.Same(handle, Assert.Single(session.Handles));
            Assert.False(session.IsDisposed);
            session.Dispose();
            // 必须在 session.Dispose() 返回后立即断言：finally 的兜底（失败路径）不得参与。
            Assert.True(
                unloadObserved.Value,
                "session.Dispose() 未触发 handle.LoadContext.Unloading：会话没有主动卸载包 ALC。");
            sessionDisposed = true;
            Assert.True(session.IsDisposed);
            session.Dispose(); // Interlocked 幂等：第二次不得再次 Unload。
            return (alcReference, packReference);
        }
        finally
        {
            // 仅异常/失败路径兜底：断言失败时确定性回收半加载 ALC，避免污染后续用例。
            // 成功路径不执行——否则“session 拥有并卸载 ALC”的断言会被 finally 的
            // 显式 Unload 掩盖（T8 review Important #1）。
            if (!sessionDisposed)
                handle?.Unload();
        }
    }

    static void ForceCollect()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        GC.Collect();
    }

    static void AssertNoStaticResidue()
    {
        Assert.Null(CompatPackLoader.BytesReaderOverrideForTest);

        Type[] holders =
        {
            typeof(CompatPackLoader),
            typeof(CompatPackSession),
            typeof(CompatPackHandle),
            typeof(CompatPackLoadContext),
        };
        foreach (Type holder in holders)
        {
            foreach (FieldInfo field in holder.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string name = holder.Name + "." + field.Name;
                Assert.False(typeof(AssemblyLoadContext).IsAssignableFrom(field.FieldType),
                    "静态字段持有 AssemblyLoadContext：" + name);
                Assert.False(typeof(ICompatPackLease).IsAssignableFrom(field.FieldType),
                    "静态字段持有包租约：" + name);
                Assert.False(typeof(CompatPackHandle).IsAssignableFrom(field.FieldType),
                    "静态字段持有句柄：" + name);
            }
        }

        // 包 ALC 程序集来自内存流：Location 为空。Default ALC 里的同名夹具（测试工程
        // ProjectReference 的静态引用）Location 非空，不在本断言范围内。
        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly => string.Equals(assembly.GetName().Name, FixtureAssemblyName, StringComparison.Ordinal)
                && string.IsNullOrEmpty(assembly.Location));
    }

    static void AssertNoLivePackContext(string packPath)
    {
        string expectedName = "CompatPack:" + Path.GetFileNameWithoutExtension(packPath);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!AssemblyLoadContext.All.Any(context =>
                    string.Equals(context.Name, expectedName, StringComparison.Ordinal)))
            {
                return;
            }
            ForceCollect();
            Thread.Sleep(50);
        }
        Assert.DoesNotContain(
            AssemblyLoadContext.All,
            context => string.Equals(context.Name, expectedName, StringComparison.Ordinal));
    }
}
