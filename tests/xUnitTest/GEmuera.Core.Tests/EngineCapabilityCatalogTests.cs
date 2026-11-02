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
    public void ResolveForPack_RequiresAndImplies_AreExpandedOnce()
    {
        Assert.True(EngineCapabilityCatalog.TryResolveForPack(
            new[] { "math.times-clamp.v1" }, out var resolution, out var errors));
        Assert.Contains("math.times-clamp.v1", resolution.OrderedCapabilityIds);
        Assert.Empty(errors);
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
        foreach (string profileId in new[] { "v24pure", "v18", "snake", "erafl", "erablue", "megaten" })
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
        // 表面账本（DiagnosticOnly 也在内）不得因闭合解析过滤而收缩：
        // erafl 13 = 5 表面 + 8 policy 行为；erablue 2；megaten 6（含共享启动容错）。
        CompatibilityPlan erafl = BuiltInDialectCatalog.CreateLegacySessionPlan("erafl");
        Assert.Equal(13, erafl.CapabilityIds.Count);
        Assert.Contains(EraFlCompatibilityModule.MarkupDivCapability, erafl.CapabilityIds);
        Assert.Contains(EraFlCompatibilityModule.DynamicMapCapability, erafl.CapabilityIds);
        Assert.Contains(EraFlCompatibilityModule.InputOmittedDefaultArgumentBehavior, erafl.CapabilityIds);

        Assert.Equal(2, BuiltInDialectCatalog.CreateLegacySessionPlan("erablue").CapabilityIds.Count);
        Assert.Equal(6, BuiltInDialectCatalog.CreateLegacySessionPlan("megaten").CapabilityIds.Count);
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
    public void Catalog_EraFlSurfaceCapabilities_AreDiagnosticOnly()
    {
        // 5 个表面 capability 没有 IEraFlCompatibilityPolicy 属性级消费者：
        // 其中 markup.div-v2 / markup.image-dual-src / resource.dynamic-sprite 零消费；
        // display.dynamic-map-transaction / input.pointer-button 虽被 legacy policy 私有字段
        // 读取以放行 GMap/指针算法，但并非独立 policy 属性（brief 口径：无真实 policy 属性）。
        string[] surfaceCapabilityIds =
        {
            EraFlCompatibilityModule.MarkupDivCapability,
            EraFlCompatibilityModule.MarkupDualImageCapability,
            EraFlCompatibilityModule.DynamicMapCapability,
            EraFlCompatibilityModule.PointerButtonCapability,
            EraFlCompatibilityModule.DynamicSpriteCapability,
        };

        foreach (string capabilityId in surfaceCapabilityIds)
        {
            Assert.True(EngineCapabilityCatalog.All.TryGetValue(capabilityId, out CapabilityDescriptor? descriptor), capabilityId);
            Assert.Equal(CapabilityStatus.DiagnosticOnly, descriptor!.Status);
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
        string[] capabilityIds =
        {
            MegatenCompatibilityModule.LabelLookupCaseBehavior,
            MegatenCompatibilityModule.RefOutNameBehavior,
            MegatenCompatibilityModule.PrivateSystemShadowBehavior,
            MegatenCompatibilityModule.StandardColorNameBehavior,
            MegatenCompatibilityModule.HtmlRgbZeroAlphaBehavior,
        };

        foreach (string capabilityId in capabilityIds)
        {
            Assert.True(EngineCapabilityCatalog.All.TryGetValue(capabilityId, out CapabilityDescriptor? descriptor), capabilityId);
            Assert.Equal(CapabilityStatus.Wired, descriptor!.Status);
            Assert.Contains("IMegatenCompatibilityPolicy.", descriptor.ConsumerContractId, StringComparison.Ordinal);
        }
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
}
