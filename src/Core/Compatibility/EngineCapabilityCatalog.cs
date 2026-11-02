using System.Collections.ObjectModel;
using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// Engine capability 的接线状态。Wired = 有真实运行期消费者（policy 属性/方法由该
/// capability 驱动）；DiagnosticOnly = 只进入 profile/包账本与诊断，不激活行为，
/// 因而不允许包把请求它当成"已实现"（fail-at-load，设计 §6.3）。
/// </summary>
public enum CapabilityStatus
{
    Wired,
    DiagnosticOnly,
}

/// <summary>
/// 单个 engine capability 的显式注册项。Requires/Implies/ConflictsWith 均按 id 引用
/// <see cref="EngineCapabilityCatalog.All"/> 中的其它条目；ConsumerContractId 指真实消费
/// 契约（legacy policy 属性、行为端口或模块账本声明），不允许空字符串。
/// </summary>
public sealed record CapabilityDescriptor(
    string CapabilityId,
    CapabilityStatus Status,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Implies,
    IReadOnlyList<string> ConflictsWith,
    string ConsumerContractId,
    CompatVersion IntroducedInEngineApi);

/// <summary>
/// capability 闭合解析结果：OrderedCapabilityIds 按请求顺序保留账本（profile 解析时
/// DiagnosticOnly 也在此列，以便既有计划哈希/表面保持不变），闭包展开只入账一次。
/// </summary>
public sealed class CapabilityResolution
{
    private readonly ReadOnlyCollection<string> orderedCapabilityIds;

    internal CapabilityResolution(IEnumerable<string> orderedCapabilityIds)
    {
        this.orderedCapabilityIds = new ReadOnlyCollection<string>(orderedCapabilityIds.ToArray());
    }

    public IReadOnlyList<string> OrderedCapabilityIds => orderedCapabilityIds;
}

/// <summary>
/// 引擎 capability 注册表（设计 §6.3）。取代"六 profile RequiredCapabilityIds 求并集"的
/// 隐式词汇表：每个 id 显式声明接线状态、真实消费者与版本轴。
/// 解析规则：未知 id / Requires 缺失或非 Wired / ConflictsWith 同时在场 → 失败；
/// Implies 自动加入；重复声明只入账一次。pack 解析额外要求所有请求项为 Wired；
/// profile 解析保留 DiagnosticOnly 在账本中，但不展开其依赖、不参与冲突检测。
/// </summary>
public static class EngineCapabilityCatalog
{
    const string SnakeConsumerPrefix = "ISnakeCompatibilityPolicy.";
    const string EraFlConsumerPrefix = "IEraFlCompatibilityPolicy.";
    const string MegatenConsumerPrefix = "IMegatenCompatibilityPolicy.";

    // 所有既有 capability 的 engineApi 引入轴均为 1.0（Task 1 的双轴起点）。
    static readonly CompatVersion EngineApiV1 = new(1, 0);
    static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();
    static readonly IReadOnlyDictionary<string, CapabilityDescriptor> Catalog = BuildCatalog();

    /// <summary>引擎已收录的全部 capability（id → 描述符，Ordinal 键）。</summary>
    public static IReadOnlyDictionary<string, CapabilityDescriptor> All => Catalog;

    /// <summary>pack 解析：未知/DiagnosticOnly/Requires 不满足/冲突均失败，成功后返回 Wired 闭包。</summary>
    public static bool TryResolveForPack(
        IEnumerable<string> requested,
        out CapabilityResolution resolution,
        out IReadOnlyList<string> errors)
        => TryResolve(All, requested, requireWired: true, out resolution, out errors);

    /// <summary>
    /// 内置 profile 解析：DiagnosticOnly 保留为账本（不激活），未知/Requires 不满足/冲突失败。
    /// </summary>
    public static bool TryResolveForProfile(
        IEnumerable<string> requested,
        out CapabilityResolution resolution,
        out IReadOnlyList<string> errors)
        => TryResolve(All, requested, requireWired: false, out resolution, out errors);

    // 测试/规则层注入的目录重载（InternalsVisibleTo: GEmuera.Core.Tests）：
    // 当前 engine catalog 没有天然冲突对，R4 用合成目录覆盖 resolver 算法；
    // CompatPackRules 用传入的 ValidationContext.Capabilities 保持校验源可注入。
    internal static bool TryResolveForPack(
        IReadOnlyDictionary<string, CapabilityDescriptor> catalog,
        IEnumerable<string> requested,
        out CapabilityResolution resolution,
        out IReadOnlyList<string> errors)
        => TryResolve(catalog, requested, requireWired: true, out resolution, out errors);

    internal static bool TryResolveForProfile(
        IReadOnlyDictionary<string, CapabilityDescriptor> catalog,
        IEnumerable<string> requested,
        out CapabilityResolution resolution,
        out IReadOnlyList<string> errors)
        => TryResolve(catalog, requested, requireWired: false, out resolution, out errors);

    static bool TryResolve(
        IReadOnlyDictionary<string, CapabilityDescriptor> catalog,
        IEnumerable<string> requested,
        bool requireWired,
        out CapabilityResolution resolution,
        out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var collected = new List<string>();
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);

        if (requested is null)
        {
            collected.Add("capability 请求列表为 null。");
            resolution = new CapabilityResolution(Array.Empty<string>());
            errors = collected;
            return false;
        }

        var pending = new Queue<string>();
        foreach (string? capabilityId in requested)
        {
            if (string.IsNullOrWhiteSpace(capabilityId))
            {
                collected.Add("capability id 不能为空。");
                continue;
            }
            pending.Enqueue(capabilityId);
        }

        while (pending.Count > 0)
        {
            string capabilityId = pending.Dequeue();
            if (!seen.Add(capabilityId))
                continue; // 重复声明/环只展开一次
            if (!catalog.TryGetValue(capabilityId, out CapabilityDescriptor? descriptor))
            {
                collected.Add("unknown capability id: " + capabilityId);
                continue;
            }
            if (requireWired && descriptor.Status != CapabilityStatus.Wired)
            {
                collected.Add("capability " + capabilityId
                    + " 为 DiagnosticOnly（无真实引擎行为消费者），不能进入包的激活集合。");
                continue;
            }

            ordered.Add(capabilityId); // profile 模式：DiagnosticOnly 保留在账本
            if (descriptor.Status != CapabilityStatus.Wired)
                continue; // 不展开诊断项依赖，也不让它参与冲突

            active.Add(capabilityId);
            foreach (string requiredId in descriptor.Requires)
            {
                if (!catalog.TryGetValue(requiredId, out CapabilityDescriptor? required)
                    || required.Status != CapabilityStatus.Wired)
                {
                    collected.Add("capability " + capabilityId + " 依赖的 " + requiredId
                        + " 不存在或不是 Wired。");
                    continue;
                }
                pending.Enqueue(requiredId);
            }
            foreach (string impliedId in descriptor.Implies)
                pending.Enqueue(impliedId);
        }

        var reportedConflicts = new HashSet<string>(StringComparer.Ordinal);
        foreach (string capabilityId in ordered)
        {
            if (!active.Contains(capabilityId)
                || !catalog.TryGetValue(capabilityId, out CapabilityDescriptor? descriptor))
            {
                continue;
            }
            foreach (string conflictId in descriptor.ConflictsWith)
            {
                if (!active.Contains(conflictId))
                    continue;
                string pairKey = string.CompareOrdinal(capabilityId, conflictId) <= 0
                    ? capabilityId + "\u0000" + conflictId
                    : conflictId + "\u0000" + capabilityId;
                if (reportedConflicts.Add(pairKey))
                    collected.Add("capability conflict: " + capabilityId + " conflicts with " + conflictId + ".");
            }
        }

        resolution = new CapabilityResolution(ordered);
        errors = collected;
        return collected.Count == 0;
    }

    static IReadOnlyDictionary<string, CapabilityDescriptor> BuildCatalog()
    {
        var catalog = new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal);

        // snake：PolicyPropertyByCapabilityId 是 id → ISnakeCompatibilityPolicy.<Property>
        // 的唯一映射（SurfaceSmoke 反射把关穷尽性），逐项据此生成 Wired 描述符。
        foreach (KeyValuePair<string, string> mapping in SnakeCompatibilityCapabilities.PolicyPropertyByCapabilityId)
        {
            Add(catalog, Wired(
                mapping.Key,
                SnakeConsumerPrefix + mapping.Value));
        }

        // eraFL 表面 capability：markup.div-v2 / markup.image-dual-src / resource.dynamic-sprite
        // 只有模块账本声明，没有 IEraFlCompatibilityPolicy 属性级消费者（DiagnosticOnly）。
        Add(catalog, Diagnostic(
            EraFlCompatibilityModule.MarkupDivCapability,
            "EraFlCompatibilityModule.RequiredCapabilityIds"));
        Add(catalog, Diagnostic(
            EraFlCompatibilityModule.MarkupDualImageCapability,
            "EraFlCompatibilityModule.RequiredCapabilityIds"));
        Add(catalog, Diagnostic(
            EraFlCompatibilityModule.DynamicSpriteCapability,
            "EraFlCompatibilityModule.RequiredCapabilityIds"));

        // display.dynamic-map-transaction / input.pointer-button 有真实消费者（R13）：
        // LegacyEraFlCompatibilityPolicy 用 HasDynamicMap/HasPointerButton 门控 GMap 三方法
        // 与 TaskStartRoomLookupFunction/GMapQuestType 两属性、指针两方法
        //（scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs）。社区包声明后确实
        // 改变运行期行为，必须是 Wired，不能被当成诊断账本拒载。
        Add(catalog, Wired(
            EraFlCompatibilityModule.DynamicMapCapability,
            "LegacyEraFlCompatibilityPolicy.HasDynamicMap"));
        Add(catalog, Wired(
            EraFlCompatibilityModule.PointerButtonCapability,
            "LegacyEraFlCompatibilityPolicy.HasPointerButton"));

        // eraFL 行为 capability：IEraFlCompatibilityPolicy 的属性/方法由 id 驱动。
        Add(catalog, Wired(
            EraFlCompatibilityModule.InputOmittedDefaultArgumentBehavior,
            EraFlConsumerPrefix + "IsOmittedDefaultArgument"));
        Add(catalog, Wired(
            EraFlCompatibilityModule.PointerBlankStringBehavior,
            EraFlConsumerPrefix + "ShouldSubmitBlankPointerStringInput"));
        Add(catalog, Wired(
            EraFlCompatibilityModule.DisplayExtendedHistoryBehavior,
            EraFlConsumerPrefix + "UsesExtendedDisplayHistory"));
        Add(catalog, Wired(
            EraFlCompatibilityModule.FloatLiteralsBehavior,
            EraFlConsumerPrefix + "AllowsFloatLiterals"));
        Add(catalog, Wired(
            EraFlCompatibilityModule.OutKeywordBehavior,
            EraFlConsumerPrefix + "AllowsOutKeyword"));
        Add(catalog, Wired(
            EraFlCompatibilityModule.SafeArithmeticGuardBehavior,
            EraFlConsumerPrefix + "UsesSafeArithmeticGuard"));
        Add(catalog, Wired(
            EraFlCompatibilityModule.ExtendedHtmlAttributesBehavior,
            EraFlConsumerPrefix + "AllowsExtendedHtmlAttributes"));
        Add(catalog, Wired(
            EraFlCompatibilityModule.DataTableXmlLeadingBomBehavior,
            EraFlConsumerPrefix + "StripsLeadingBomFromDataTableXml"));

        // 仅出现在 DefaultBehaviorPorts 的 behavior id：设计 §H1 明确 Ports 是装饰性账本；
        // 这些 id 没有被任何 capability-gated policy 成员读取，按 DiagnosticOnly 记录。
        Add(catalog, Diagnostic(
            EraFlCompatibilityModule.PointerBlankIntegerBehavior,
            "EraFlCompatibilityModule.DefaultPorts"));
        Add(catalog, Diagnostic(
            EraFlCompatibilityModule.MarkupUnknownAngleTextBehavior,
            "EraFlCompatibilityModule.DefaultPorts"));
        Add(catalog, Diagnostic(
            EraFlCompatibilityModule.DisplayDynamicScopeBehavior,
            "EraFlCompatibilityModule.DefaultPorts"));
        Add(catalog, Diagnostic(
            EraFlCompatibilityModule.ResourceDynamicSpriteBehavior,
            "EraFlCompatibilityModule.DefaultPorts"));

        // eraMegaten：四个行为有真实 IMegatenCompatibilityPolicy 属性消费者（Wired）。
        Add(catalog, Wired(
            MegatenCompatibilityModule.LabelLookupCaseBehavior,
            MegatenConsumerPrefix + "UsesVariableCaseForFunctionLabelLookup"));
        Add(catalog, Wired(
            MegatenCompatibilityModule.RefOutNameBehavior,
            MegatenConsumerPrefix + "AllowsOutAsVariableNameAfterRefKeyword"));
        Add(catalog, Wired(
            MegatenCompatibilityModule.PrivateSystemShadowBehavior,
            MegatenConsumerPrefix + "AllowsPrivateSystemVariableShadowing"));
        Add(catalog, Wired(
            MegatenCompatibilityModule.HtmlRgbZeroAlphaBehavior,
            MegatenConsumerPrefix + "TreatZeroAlphaRgbHexAsOpaque"));

        // ui.standard-color-name.v1 只有 policy 属性、没有运行期消费者（Lead 裁决 R17）：
        // 2026-10-20 按 v24 基线语义移除了 Color.FromName 的 megaten 门控
        //（governance/evolution-log/2026-10-20-standard-color-name-fallback.md），
        // scripts/uEmuera/Drawing.cs 现在无条件查标准颜色名表，UsesStandardColorNameTable
        // 全仓无读取点。该 id 仍保留在 megaten profile 账本（DiagnosticOnly 保留账本），
        // 但包不能声明它（声明也不会改变行为，属静默 no-op）。
        Add(catalog, Diagnostic(
            MegatenCompatibilityModule.StandardColorNameBehavior,
            MegatenConsumerPrefix + "UsesStandardColorNameTable (no runtime consumer since 2026-10-20)"));

        // eraBlue：外部插件加载是宿主能力声明，legacy policy 当前无消费者。
        Add(catalog, Diagnostic(
            EraBlueCompatibilityModule.ExternalPluginCapability,
            "EraBlueCompatibilityModule.RequiredCapabilityIds"));

        return new ReadOnlyDictionary<string, CapabilityDescriptor>(catalog);
    }

    static CapabilityDescriptor Wired(string capabilityId, string consumerContractId) => new(
        capabilityId,
        CapabilityStatus.Wired,
        NoDependencies,
        NoDependencies,
        NoDependencies,
        consumerContractId,
        EngineApiV1);

    static CapabilityDescriptor Diagnostic(string capabilityId, string consumerContractId) => new(
        capabilityId,
        CapabilityStatus.DiagnosticOnly,
        NoDependencies,
        NoDependencies,
        NoDependencies,
        consumerContractId,
        EngineApiV1);

    /// <summary>
    /// 测试注入入口：用与生产目录相同的 <see cref="Add"/> 合并逻辑构造目录，
    /// 钉住"同一 id 被多个模块登记"时的消费者合并与不一致 fail-fast 语义。
    /// </summary>
    internal static IReadOnlyDictionary<string, CapabilityDescriptor> ComposeCatalog(
        IEnumerable<CapabilityDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var catalog = new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal);
        foreach (CapabilityDescriptor descriptor in descriptors)
            Add(catalog, descriptor);
        return new ReadOnlyDictionary<string, CapabilityDescriptor>(catalog);
    }

    // 同一 id 可能被多个模块声明（declare.out-keyword.v1 由 snake 与 eraFL 共用）：
    // 只保留一条描述符，消费者契约按 Ordinal 合并，避免静默丢掉任一真实消费者。
    // 状态相同还不够——Requires/Implies/ConflictsWith 的任何不一致都是声明漂移，
    // 构建期（静态初始化）即 fail-fast，而不是让后登记的依赖被静默丢弃。
    static void Add(Dictionary<string, CapabilityDescriptor> catalog, CapabilityDescriptor descriptor)
    {
        if (!catalog.TryGetValue(descriptor.CapabilityId, out CapabilityDescriptor? existing))
        {
            catalog.Add(descriptor.CapabilityId, descriptor);
            return;
        }
        if (existing.Status != descriptor.Status)
        {
            throw new InvalidOperationException(
                "capability '" + descriptor.CapabilityId + "' 在 engine catalog 中出现不一致状态。");
        }
        if (!SequenceEqualOrdinal(existing.Requires, descriptor.Requires)
            || !SequenceEqualOrdinal(existing.Implies, descriptor.Implies)
            || !SequenceEqualOrdinal(existing.ConflictsWith, descriptor.ConflictsWith))
        {
            throw new InvalidOperationException(
                "capability '" + descriptor.CapabilityId
                + "' 在 engine catalog 中重复登记，但 Requires/Implies/ConflictsWith 声明不一致。");
        }
        string merged = MergeConsumers(existing.ConsumerContractId, descriptor.ConsumerContractId);
        if (!string.Equals(merged, existing.ConsumerContractId, StringComparison.Ordinal))
            catalog[descriptor.CapabilityId] = existing with { ConsumerContractId = merged };
    }

    static bool SequenceEqualOrdinal(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    static string MergeConsumers(string left, string right)
    {
        var consumers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string part in left.Split(" | ", StringSplitOptions.RemoveEmptyEntries))
            consumers.Add(part);
        foreach (string part in right.Split(" | ", StringSplitOptions.RemoveEmptyEntries))
            consumers.Add(part);
        return string.Join(" | ", consumers);
    }
}
