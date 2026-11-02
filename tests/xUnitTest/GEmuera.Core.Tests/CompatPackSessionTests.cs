using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 会话持有 pack set 与确定性 Unload（docs/designs/compat-pack-interface.md §5.4，
/// 计划 E2-R2）：成功加载的句柄集合由会话绑定对象独占，Dispose 幂等且对每个租约
/// 恰好调用一次 Unload——把包程序集的存活从 GC 猜测改为确定性边界。
/// T8 起另加真实句柄路径：公开构造器（CompatPackHandle 而非计数租约）的会话必须
/// 接管句柄所有权，Dispose 后幂等、真实 ALC 的确定性回收由
/// <see cref="CompatPackAlcReclamationTests"/> 做 GC 级验证（本类只钉 Dispose 语义）。
/// </summary>
[Collection("CompatPack")]
public class CompatPackSessionTests
{
    private sealed class CountingLease : ICompatPackLease
    {
        public int UnloadCount { get; private set; }

        public void Unload() => UnloadCount++;
    }

    [Fact]
    public void Dispose_UnloadsEachLeaseOnceIdempotently()
    {
        CompatibilityPlan plan = BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");
        var leases = new[] { new CountingLease(), new CountingLease(), new CountingLease() };

        using var session = new CompatPackSession(plan, leases);

        Assert.False(session.IsDisposed);
        session.Dispose();
        Assert.True(session.IsDisposed);
        session.Dispose();
        Assert.All(leases, lease => Assert.Equal(1, lease.UnloadCount));
    }

    [Fact]
    public void Dispose_WithRealHandle_TransfersOwnershipAndIsIdempotent()
    {
        // 真实句柄路径（非计数租约）：公开构造器接管 CompatPackHandle 的所有权。
        // 这里不重复 GC 级验收（CompatPackAlcReclamationTests 负责），只钉：
        //   - 会话 Handles 暴露同一句柄；
        //   - Dispose 只改变会话状态，句柄本身幂等 Unload 不抛（双回收兜底）；
        //   - 重复 Dispose 不重复触达句柄。
        using var directory = new IsolatedPackDirectory(
            typeof(HelloCompatPackFixture.HelloCompatPack).Assembly.Location);
        Assert.True(
            CompatPackLoader.TryLoad(
                directory.MainAssemblyPath, CompatPackCompositionTestData.Context(), out var handle, out var errors),
            string.Join("; ", errors));
        try
        {
            Assert.NotNull(handle);
            Assert.True(handle!.LoadContext.IsCollectible);
            CompatibilityPlan plan = BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");
            var session = new CompatPackSession(plan, new[] { handle });

            Assert.Same(handle, Assert.Single(session.Handles));
            Assert.False(session.IsDisposed);

            session.Dispose();
            Assert.True(session.IsDisposed);
            session.Dispose(); // Interlocked 幂等：no-op
            handle.Unload();   // 会话已回收；句柄幂等 Unload 不抛（双回收兜底）
        }
        finally
        {
            handle?.Unload();
        }
    }
}
