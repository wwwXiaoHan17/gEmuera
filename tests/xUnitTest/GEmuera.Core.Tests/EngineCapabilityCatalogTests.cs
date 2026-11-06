using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 引擎 capability 注册表与闭合解析直测（设计 §6.3）。catalog 状态按 scripts/ 中
/// 真实 policy 消费者逐项钉住：Wired = 有 capability 驱动的运行期消费者；
/// DiagnosticOnly = 仅作为 profile/包账本声明（无运行期行为）。
/// </summary>
public class EngineCapabilityCatalogTests
{
    static readonly CompatVersion EngineApiV1 = new(1, 0);

    static CapabilityDescriptor Descriptor(
        string capabilityId,
        CapabilityStatus status = CapabilityStatus.Wired,
        string[]? requires = null,
        string[]? implies = null,
        string[]? conflictsWith = null,
        string consumerContractId = "test.consumer")
        => new(
            capabilityId,
            status,
            requires ?? Array.Empty<string>(),
            implies ?? Array.Empty<string>(),
            conflictsWith ?? Array.Empty<string>(),
            consumerContractId,
            EngineApiV1);

    static IReadOnlyDictionary<string, CapabilityDescriptor> Catalog(params CapabilityDescriptor[] descriptors)
        => descriptors.ToDictionary(descriptor => descriptor.CapabilityId, StringComparer.Ordinal);

    [Fact]
    public void ResolveForPack_UnknownCapability_Fails()
    {
        Assert.False(EngineCapabilityCatalog.TryResolveForPack(
            new[] { "does.not.exist.v1" }, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveForPack_DiagnosticOnly_Fails()
    {
        Assert.False(EngineCapabilityCatalog.TryResolveForPack(
            new[] { "markup.div-v2.v1" }, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("markup.div-v2.v1"));
        Assert.Contains(errors, e => e.Contains("DiagnosticOnly"));
    }

    [Fact]
    public void ResolveForPack_KnownWiredCapability_Succeeds()
    {
        // 真实 catalog 路径的冒烟；"Requires/Implies 只展开一次"的语义由下方注入式
        // 合成目录用例钉住（真实 catalog 当前没有任何依赖边）。
        Assert.True(EngineCapabilityCatalog.TryResolveForPack(
            new[] { "math.times-clamp.v1" }, out var resolution, out var errors));
        Assert.Equal(new[] { "math.times-clamp.v1" }, resolution.OrderedCapabilityIds);
        Assert.Empty(errors);
    }

    [Fact]
    public void ResolveForPack_RequiresDiagnosticOnly_Fails()
    {
        // Requires 指向 DiagnosticOnly（无真实行为消费者）不能算依赖满足：
        // 否则包会凭一个诊断账本 id 拿到激活。
        var catalog = Catalog(
            Descriptor("root.v1", requires: new[] { "diag.only.v1" }),
            Descriptor("diag.only.v1", CapabilityStatus.DiagnosticOnly));

        Assert.False(EngineCapabilityCatalog.TryResolveForPack(
            catalog, new[] { "root.v1" }, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("diag.only.v1") && e.Contains("不是 Wired"));
    }

    [Fact]
    public void ResolveForPack_RequiresCycle_AccountsEachCapabilityOnce()
    {
        // Requires 环不得无限展开：每个 id 只入账一次（BFS 发现序）。
        var catalog = Catalog(
            Descriptor("cycle.a.v1", requires: new[] { "cycle.b.v1" }),
            Descriptor("cycle.b.v1", requires: new[] { "cycle.a.v1" }));

        Assert.True(EngineCapabilityCatalog.TryResolveForPack(
            catalog, new[] { "cycle.a.v1" }, out var resolution, out var errors), string.Join("; ", errors));
        Assert.Equal(new[] { "cycle.a.v1", "cycle.b.v1" }, resolution.OrderedCapabilityIds);
    }

    [Fact]
    public void ResolveForPack_NullRequest_Fails()
    {
        Assert.False(EngineCapabilityCatalog.TryResolveForPack(
            null!, out var resolution, out var errors));
        Assert.Empty(resolution.OrderedCapabilityIds);
        Assert.Contains(errors, e => e.Contains("null"));
    }

    [Fact]
    public void ResolveForPack_ConflictingCapabilities_Fails()
    {
        // 当前 engine catalog 无天然冲突对（snake 19 / erafl 8 / megaten 5 项的真实消费者
        // 彼此独立，没有任何"同时激活会矛盾"的语义证据），因此本用例只覆盖 resolver 算法：
        // 注入一对显式互斥的合成 Wired capability，而不是给真实 capability 编造冲突。
        var catalog = Catalog(
            Descriptor("a.conflict.v1", conflictsWith: new[] { "b.conflict.v1" }),
            Descriptor("b.conflict.v1", conflictsWith: new[] { "a.conflict.v1" }));

        Assert.False(EngineCapabilityCatalog.TryResolveForPack(
            catalog, new[] { "a.conflict.v1", "b.conflict.v1" }, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("conflict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveForPack_RequiresAndImplies_ExpandInDeterministicOrder()
    {
        var catalog = Catalog(
            Descriptor("root.v1", requires: new[] { "required.v1" }, implies: new[] { "implied.v1" }),
            Descriptor("required.v1"),
            Descriptor("implied.v1"));

        // 重复声明同一 id 只入账一次；Requires/Implies 闭包各展开一次。
        Assert.True(EngineCapabilityCatalog.TryResolveForPack(
            catalog, new[] { "root.v1", "root.v1" }, out var resolution, out var errors));
        Assert.Equal(new[] { "root.v1", "required.v1", "implied.v1" }, resolution.OrderedCapabilityIds);
        Assert.Empty(errors);
    }

    [Fact]
    public void ResolveForPack_MissingRequirement_Fails()
    {
        var catalog = Catalog(Descriptor("root.v1", requires: new[] { "missing.v1" }));

        Assert.False(EngineCapabilityCatalog.TryResolveForPack(
            catalog, new[] { "root.v1" }, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("missing.v1"));
    }

    [Fact]
    public void ResolveForProfile_KeepsDiagnosticOnlyInLedger()
    {
        // 内置 profile 的 DiagnosticOnly 表面能力必须保留在 OrderedCapabilityIds 账本里，
        // 否则 erafl 计划的 capability 集合（以及计划哈希）会漂移；它们只是不参与激活。
        Assert.True(EngineCapabilityCatalog.TryResolveForProfile(
            new[] { "markup.div-v2.v1", "math.times-clamp.v1" }, out var resolution, out var errors));
        Assert.Contains("markup.div-v2.v1", resolution.OrderedCapabilityIds);
        Assert.Contains("math.times-clamp.v1", resolution.OrderedCapabilityIds);
        Assert.Empty(errors);
    }

    [Fact]
    public void ResolveForProfile_UnknownCapability_Fails()
    {
        Assert.False(EngineCapabilityCatalog.TryResolveForProfile(
            new[] { "does.not.exist.v1" }, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveForProfile_BuiltInProfiles_PreserveTheirLedgerExactly()
    {
        // 内置 profile 的 plan capability 集合 = RequiredCapabilityIds 原样（DiagnosticOnly
        // 也在账本内）；这是既有 plan hash / 回归面不变的前提。
        CompatibilityProfileCatalog profiles = BuiltInDialectCatalog.CreateLegacyProfileCatalog();
        foreach (string profileId in new[] { "v24pure" })
        {
            CompatibilityProfileDefinition profile = profiles.Resolve(profileId);
            Assert.True(EngineCapabilityCatalog.TryResolveForProfile(
                profile.RequiredCapabilityIds, out CapabilityResolution resolution, out var errors),
                profileId + ": " + string.Join("; ", errors));
            Assert.Equal(
                profile.RequiredCapabilityIds.Order(StringComparer.Ordinal),
                resolution.OrderedCapabilityIds.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void BuiltInProfiles_PlanCapabilityLedgerIsUnchanged()
    {
        // erafl/erablue/megaten/v18 已退役为第一方包（P-D 步 1-4）；其 capability 契约
        // （Wired 子集 + DiagnosticOnly 不可入包）由 FirstPartyPackEquivalenceTests 以
        // 真实包 DLL 装配链验证。本测试只保留仍内置 profile 的账本断言。
        // snake 19 项 capability 契约由 FirstPartyPackEquivalenceTests 以 pack DLL 装配链验证
        //（P-D 步 5 后内置 snake profile 不再存在）。
        Assert.Empty(BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure").CapabilityIds);
    }

    [Fact]
    public void Catalog_EveryDescriptor_IsWellFormed()
    {
        // 19 snake + 17 eraFL（declare.out-keyword.v1 与 snake 共享，只算一条）+ 5 megaten + 1 erablue = 41。
        Assert.Equal(41, EngineCapabilityCatalog.All.Count);
        foreach (KeyValuePair<string, CapabilityDescriptor> entry in EngineCapabilityCatalog.All)
        {
            Assert.Equal(entry.Key, entry.Value.CapabilityId);
            Assert.False(string.IsNullOrWhiteSpace(entry.Value.ConsumerContractId), entry.Key);
            Assert.Equal(EngineApiV1, entry.Value.IntroducedInEngineApi);
        }

        // 最终分类账：32 Wired / 9 DiagnosticOnly / 41 unique。
        // DiagnosticOnly 9 = eraFL 3 零消费者表面 + 4 端口账本 + megaten 标准色名 + erablue 插件。
        Assert.Equal(32, EngineCapabilityCatalog.All.Values.Count(d => d.Status == CapabilityStatus.Wired));
        Assert.Equal(9, EngineCapabilityCatalog.All.Values.Count(d => d.Status == CapabilityStatus.DiagnosticOnly));
        Assert.Equal(41, EngineCapabilityCatalog.All.Values
            .Select(d => d.CapabilityId)
            .Distinct(StringComparer.Ordinal)
            .Count());
    }

    [Fact]
    public void Catalog_SnakeCapabilities_AreWiredWithPolicyConsumers()
    {
        foreach (KeyValuePair<string, string> mapping in SnakeCompatibilityCapabilities.PolicyPropertyByCapabilityId)
        {
            Assert.True(EngineCapabilityCatalog.All.TryGetValue(mapping.Key, out CapabilityDescriptor? descriptor), mapping.Key);
            Assert.Equal(CapabilityStatus.Wired, descriptor!.Status);
            Assert.Contains("ISnakeCompatibilityPolicy." + mapping.Value, descriptor.ConsumerContractId, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Catalog_EraFlRequiredCapabilities_AreRegistered()
    {
        foreach (string capabilityId in EraFlCompatibilityModule.RequiredCapabilityIds)
            Assert.True(EngineCapabilityCatalog.All.ContainsKey(capabilityId), capabilityId);
    }

    [Fact]
    public void Catalog_EraFlSurfaceCapabilities_ZeroConsumerAreDiagnosticOnly()
    {
        // 3 个零消费者表面 capability 没有 IEraFlCompatibilityPolicy 属性级消费者，
        // 也没有 capability-gated 运行期行为：标 DiagnosticOnly（包不能声明）。
        string[] surfaceCapabilityIds =
        {
            EraFlCompatibilityModule.MarkupDivCapability,
            EraFlCompatibilityModule.MarkupDualImageCapability,
            EraFlCompatibilityModule.DynamicSpriteCapability,
        };

        foreach (string capabilityId in surfaceCapabilityIds)
        {
            Assert.True(EngineCapabilityCatalog.All.TryGetValue(capabilityId, out CapabilityDescriptor? descriptor), capabilityId);
            Assert.Equal(CapabilityStatus.DiagnosticOnly, descriptor!.Status);
        }
    }

    [Fact]
    public void Catalog_EraFlGMapAndPointerButton_AreWiredWithPolicyConsumers()
    {
        // R13：display.dynamic-map-transaction / input.pointer-button 有真实消费者——
        // LegacyEraFlCompatibilityPolicy 的 HasDynamicMap/HasPointerButton 门控 GMap 三方法与
        // TaskStartRoomLookupFunction/GMapQuestType 两属性、指针两方法。它们必须是 Wired，
        // 否则社区包声明这些能力会被当成诊断账本拒载（真实行为永远无法被包激活）。
        var consumers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EraFlCompatibilityModule.DynamicMapCapability] = "LegacyEraFlCompatibilityPolicy.HasDynamicMap",
            [EraFlCompatibilityModule.PointerButtonCapability] = "LegacyEraFlCompatibilityPolicy.HasPointerButton",
        };

        foreach (KeyValuePair<string, string> entry in consumers)
        {
            Assert.True(EngineCapabilityCatalog.All.TryGetValue(entry.Key, out CapabilityDescriptor? descriptor), entry.Key);
            Assert.Equal(CapabilityStatus.Wired, descriptor!.Status);
            Assert.Equal(entry.Value, descriptor.ConsumerContractId);
        }
    }

    [Fact]
    public void Catalog_EraFlPolicyBehaviors_AreWired()
    {
        var consumers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EraFlCompatibilityModule.InputOmittedDefaultArgumentBehavior] = "IEraFlCompatibilityPolicy.IsOmittedDefaultArgument",
            [EraFlCompatibilityModule.PointerBlankStringBehavior] = "IEraFlCompatibilityPolicy.ShouldSubmitBlankPointerStringInput",
            [EraFlCompatibilityModule.DisplayExtendedHistoryBehavior] = "IEraFlCompatibilityPolicy.UsesExtendedDisplayHistory",
            [EraFlCompatibilityModule.FloatLiteralsBehavior] = "IEraFlCompatibilityPolicy.AllowsFloatLiterals",
            [EraFlCompatibilityModule.OutKeywordBehavior] = "IEraFlCompatibilityPolicy.AllowsOutKeyword",
            [EraFlCompatibilityModule.SafeArithmeticGuardBehavior] = "IEraFlCompatibilityPolicy.UsesSafeArithmeticGuard",
            [EraFlCompatibilityModule.ExtendedHtmlAttributesBehavior] = "IEraFlCompatibilityPolicy.AllowsExtendedHtmlAttributes",
            [EraFlCompatibilityModule.DataTableXmlLeadingBomBehavior] = "IEraFlCompatibilityPolicy.StripsLeadingBomFromDataTableXml",
        };

        foreach (KeyValuePair<string, string> entry in consumers)
        {
            Assert.True(EngineCapabilityCatalog.All.TryGetValue(entry.Key, out CapabilityDescriptor? descriptor), entry.Key);
            Assert.Equal(CapabilityStatus.Wired, descriptor!.Status);
            Assert.Contains(entry.Value, descriptor.ConsumerContractId, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Catalog_EraFlPortOnlyBehaviors_AreDiagnosticOnly()
    {
        // 这四个 behavior 只出现在 EraFlCompatibilityModule.DefaultBehaviorPorts（设计文档
        // docs/designs/compat-pack-interface.md §H1 明确 Ports/BehaviorPortSnapshot 是装饰性
        // 账本、无运行期消费）；grep scripts/ 无任何 capability 驱动的 policy 属性读取。
        // input.pointer-blank-integer 的实际算法由 input.pointer-button.v1 间接门控
        //（LegacyEraFlCompatibilityPolicy.NormalizePointerIntegerSubmission），本 id 无人读取。
        string[] portOnlyBehaviorIds =
        {
            EraFlCompatibilityModule.PointerBlankIntegerBehavior,
            EraFlCompatibilityModule.MarkupUnknownAngleTextBehavior,
            EraFlCompatibilityModule.DisplayDynamicScopeBehavior,
            EraFlCompatibilityModule.ResourceDynamicSpriteBehavior,
        };

        foreach (string capabilityId in portOnlyBehaviorIds)
        {
            Assert.True(EngineCapabilityCatalog.All.TryGetValue(capabilityId, out CapabilityDescriptor? descriptor), capabilityId);
            Assert.Equal(CapabilityStatus.DiagnosticOnly, descriptor!.Status);
        }
    }

    [Fact]
    public void Catalog_MegatenCapabilities_AreWired()
    {
        // megaten Wired = 4：标准色名不在内（R17，见下方专属用例）。
        string[] capabilityIds =
        {
            MegatenCompatibilityModule.LabelLookupCaseBehavior,
            MegatenCompatibilityModule.RefOutNameBehavior,
            MegatenCompatibilityModule.PrivateSystemShadowBehavior,
            MegatenCompatibilityModule.HtmlRgbZeroAlphaBehavior,
        };

        foreach (string capabilityId in capabilityIds)
        {
            Assert.True(EngineCapabilityCatalog.All.TryGetValue(capabilityId, out CapabilityDescriptor? descriptor), capabilityId);
            Assert.Equal(CapabilityStatus.Wired, descriptor!.Status);
            Assert.Contains("IMegatenCompatibilityPolicy.", descriptor.ConsumerContractId, StringComparison.Ordinal);
        }

        Assert.Equal(4, EngineCapabilityCatalog.All.Values.Count(
            descriptor => descriptor.ConsumerContractId.Contains("IMegatenCompatibilityPolicy.", StringComparison.Ordinal)
                && descriptor.Status == CapabilityStatus.Wired));
    }

    [Fact]
    public void Catalog_MegatenStandardColorName_IsDiagnosticOnly()
    {
        // R17（Lead 裁决）：2026-10-20 按 v24 基线语义移除了 Color.FromName 的 megaten 门控
        //（governance/evolution-log/2026-10-20-standard-color-name-fallback.md）。
        // scripts/uEmuera/Drawing.cs 无条件查标准颜色名表，UsesStandardColorNameTable 无
        // 运行期读取点；该 id 只是 megaten profile 账本（DiagnosticOnly 保留账本），
        // 包不能声明它（声明也不会改变行为）。
        Assert.True(EngineCapabilityCatalog.All.TryGetValue(
            MegatenCompatibilityModule.StandardColorNameBehavior, out CapabilityDescriptor? descriptor));
        Assert.Equal(CapabilityStatus.DiagnosticOnly, descriptor!.Status);
        Assert.Contains("UsesStandardColorNameTable", descriptor.ConsumerContractId, StringComparison.Ordinal);
        Assert.Contains("no runtime consumer", descriptor.ConsumerContractId, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_EraBlueExternalPlugin_IsDiagnosticOnly()
    {
        Assert.True(EngineCapabilityCatalog.All.TryGetValue(
            EraBlueCompatibilityModule.ExternalPluginCapability, out CapabilityDescriptor? descriptor));
        Assert.Equal(CapabilityStatus.DiagnosticOnly, descriptor!.Status);
    }

    [Fact]
    public void Catalog_DeclareOutKeyword_RecordsBothConsumers()
    {
        // 同一 id 被 snake 与 eraFL 同时使用：catalog 只能有一条，消费者必须如实记录两个。
        Assert.True(EngineCapabilityCatalog.All.TryGetValue(
            SnakeCompatibilityCapabilities.OutKeyword, out CapabilityDescriptor? descriptor));
        Assert.Equal(CapabilityStatus.Wired, descriptor!.Status);
        Assert.Contains("ISnakeCompatibilityPolicy.AllowsOutKeyword", descriptor.ConsumerContractId, StringComparison.Ordinal);
        Assert.Contains("IEraFlCompatibilityPolicy.AllowsOutKeyword", descriptor.ConsumerContractId, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredWiredCapabilityIds_PinnedCounts()
    {
        // IsEnabled 只由 catalog 的 Wired 子集推断，计数漂移会直接改变会话激活语义：
        // snake 19/19 全 Wired；eraFL 13 项账本里 3 个零消费者表面项是 DiagnosticOnly，
        // Wired = 10（8 policy 行为 + display.dynamic-map-transaction + input.pointer-button）。
        Assert.Equal(19, SnakeCompatibilityCapabilities.RequiredWiredCapabilityIds.Count);
        Assert.Equal(10, EraFlCompatibilityModule.RequiredWiredCapabilityIds.Count);

        Assert.Contains(EraFlCompatibilityModule.DynamicMapCapability, EraFlCompatibilityModule.RequiredWiredCapabilityIds);
        Assert.Contains(EraFlCompatibilityModule.PointerButtonCapability, EraFlCompatibilityModule.RequiredWiredCapabilityIds);
        Assert.DoesNotContain(EraFlCompatibilityModule.MarkupDivCapability, EraFlCompatibilityModule.RequiredWiredCapabilityIds);
        Assert.DoesNotContain(EraFlCompatibilityModule.MarkupDualImageCapability, EraFlCompatibilityModule.RequiredWiredCapabilityIds);
        Assert.DoesNotContain(EraFlCompatibilityModule.DynamicSpriteCapability, EraFlCompatibilityModule.RequiredWiredCapabilityIds);
    }

    [Fact]
    public void RequiredCapabilityIds_AreAllRegisteredInCatalog()
    {
        // 模块账本里的每个 id 都必须有 catalog 登记（缺失会让 Wired 过滤静默跳过）。
        foreach (string capabilityId in SnakeCompatibilityCapabilities.RequiredCapabilityIds)
            Assert.True(EngineCapabilityCatalog.All.ContainsKey(capabilityId), "snake: " + capabilityId);
        foreach (string capabilityId in EraFlCompatibilityModule.RequiredCapabilityIds)
            Assert.True(EngineCapabilityCatalog.All.ContainsKey(capabilityId), "erafl: " + capabilityId);
    }

    [Fact]
    public void ComposeCatalog_DuplicateIdSameStatus_MergesConsumers()
    {
        var composed = EngineCapabilityCatalog.ComposeCatalog(new[]
        {
            Descriptor("shared.v1", consumerContractId: "ISnakeCompatibilityPolicy.Shared"),
            Descriptor("shared.v1", consumerContractId: "IEraFlCompatibilityPolicy.Shared"),
        });

        Assert.Single(composed);
        Assert.Equal(
            "IEraFlCompatibilityPolicy.Shared | ISnakeCompatibilityPolicy.Shared",
            composed["shared.v1"].ConsumerContractId);
    }

    [Fact]
    public void ComposeCatalog_DuplicateIdConflictingStatus_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => EngineCapabilityCatalog.ComposeCatalog(new[]
        {
            Descriptor("shared.v1", CapabilityStatus.Wired),
            Descriptor("shared.v1", CapabilityStatus.DiagnosticOnly),
        }));
    }

    [Fact]
    public void ComposeCatalog_DuplicateIdConflictingDependencies_Throws()
    {
        // 状态相同还不够：Requires/Implies/ConflictsWith 任一不一致都是声明漂移，
        // 构建期必须 fail-fast，不能静默保留先登记者的依赖（后登记依赖会被吞掉）。
        Assert.Throws<InvalidOperationException>(() => EngineCapabilityCatalog.ComposeCatalog(new[]
        {
            Descriptor("shared.v1", requires: new[] { "dep.a.v1" }),
            Descriptor("shared.v1", requires: new[] { "dep.b.v1" }),
        }));
        Assert.Throws<InvalidOperationException>(() => EngineCapabilityCatalog.ComposeCatalog(new[]
        {
            Descriptor("shared.v1", implies: new[] { "implied.v1" }),
            Descriptor("shared.v1"),
        }));
        Assert.Throws<InvalidOperationException>(() => EngineCapabilityCatalog.ComposeCatalog(new[]
        {
            Descriptor("shared.v1", conflictsWith: new[] { "other.v1" }),
            Descriptor("shared.v1"),
        }));
    }
}
