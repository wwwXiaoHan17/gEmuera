using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 一次成功的包组合解析结果（T6）：依赖闭包验证、capability 组合闭包、显式 override 仲裁
/// 与双形态闭包之后的最终快照。组装器只读本对象，不再自行合并包差量或执行包贡献。
/// 全部集合按规范顺序冻结：<see cref="OrderedPacks"/> 为拓扑序（依赖先于依赖者，同层
/// packId Ordinal），<see cref="CapabilityIds"/> 为 Ordinal 排序去重，<see cref="SymbolStates"/>
/// 按 (Kind, Name) 规范排序。同一启用集合（与输入排列无关）必得同一 <see cref="CompositionHash"/>。
/// </summary>
public sealed class CompatPackCompositionResult
{
    readonly ReadOnlyCollection<CompatPackHandle> orderedPacks;
    readonly ReadOnlyCollection<string> capabilityIds;
    readonly ReadOnlyDictionary<CompatSymbolKey, CompatSymbolState> symbolStates;

    internal CompatPackCompositionResult(
        IEnumerable<CompatPackHandle> orderedPacks,
        IEnumerable<string> capabilityIds,
        IEnumerable<CompatSymbolState> symbolStates,
        string compositionHash)
    {
        ArgumentNullException.ThrowIfNull(orderedPacks);
        ArgumentNullException.ThrowIfNull(capabilityIds);
        ArgumentNullException.ThrowIfNull(symbolStates);
        this.orderedPacks = new ReadOnlyCollection<CompatPackHandle>(orderedPacks.ToArray());
        this.capabilityIds = new ReadOnlyCollection<string>(
            capabilityIds.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
        var states = new SortedDictionary<CompatSymbolKey, CompatSymbolState>(CompatSymbolKeyComparer.Instance);
        foreach (CompatSymbolState state in symbolStates)
            states[state.Key] = state;
        this.symbolStates = new ReadOnlyDictionary<CompatSymbolKey, CompatSymbolState>(states);
        CompositionHash = compositionHash ?? throw new ArgumentNullException(nameof(compositionHash));
    }

    /// <summary>最终组合的包集合（拓扑序：依赖先于依赖者；同层按 packId Ordinal）。</summary>
    public IReadOnlyList<CompatPackHandle> OrderedPacks => orderedPacks;

    /// <summary>组合级 capability 闭包（pack 差量的 per-pack closure 再闭合一次），Ordinal 排序。</summary>
    public IReadOnlyList<string> CapabilityIds => capabilityIds;

    /// <summary>合并/override 仲裁/双形态闭包后的最终符号状态，按 (Kind, Name) 规范排序。</summary>
    public IReadOnlyDictionary<CompatSymbolKey, CompatSymbolState> SymbolStates => symbolStates;

    /// <summary>
    /// 组合语义哈希（规范文本 SHA256，格式见 <see cref="CompatPackCompositionResolver"/>）：
    /// 有序 packId/version/PackSha256、capability 闭包与最终 symbol 状态全部入账。
    /// 输入顺序不影响本值（拓扑序与规范排序先行）。
    /// </summary>
    public string CompositionHash { get; }
}

/// <summary>CompatSymbolKey 的规范排序（Kind 枚举序，再 Name Ordinal）。</summary>
internal sealed class CompatSymbolKeyComparer : IComparer<CompatSymbolKey>
{
    public static CompatSymbolKeyComparer Instance { get; } = new();

    public int Compare(CompatSymbolKey left, CompatSymbolKey right)
    {
        int kind = left.Kind.CompareTo(right.Kind);
        return kind != 0 ? kind : string.CompareOrdinal(left.Name, right.Name);
    }
}

/// <summary>
/// 包组合解析器（设计 §6.2）：把"已加载并通过单包校验"的句柄集合折叠成确定性组合结果。
/// 解析顺序（任一失败整体拒载，错误全量收集）：
///   1. packId → handle（重复 packId 拒载；null 句柄拒载）；
///   2. requires 闭包：缺失依赖、版本范围不满足、依赖环均拒载，错误携带依赖链
///      （<c>community.a -&gt; community.b -&gt; community.c</c>）；
///   3. capability 闭包：并集各包冻结的 CapabilityIds 后经 engine capability catalog 再闭合；
///   4. 拓扑排序：依赖先于依赖者，同层按 packId Ordinal；
///   5. 合并 symbol states：同一最终状态可合并，不同最终状态必须有显式 override；
///   6. 显式 override/priority：只有声明 override 的包可覆盖；多个 override 取 priority 唯一
///      最高者，同 priority 拒载；override 目标必须是本次组合真实存在的符号，且不得覆盖
///      v24 基线 instruction/function handler。priority 只在真实冲突需要仲裁时参与：多包
///      最终状态一致（都 Hidden / 变体同值）按同态合并，owner 取 packId Ordinal 最小者；
///   7. 冲突检测：manifest conflicts 声明的包同时启用即拒载（priority 不能覆盖冲突）；
///   8. 无效果包：无任何 symbol/capability/variant 贡献的包不得进入最终组合（bundle 不在 v1）；
///   9. 跨 kind 闭包：最终 Hidden 的指令（含 METHOD 投影的函数侧隐藏）不得与同名变体选择
///      共存——隐藏后变体运行期静默无效果，且跨 kind 没有 override 仲裁通道，只能整组拒载；
///  10. 双形态闭包：METHOD 投影名在 instruction/function 两侧的最终状态必须一致
///      （见 <see cref="ValidateDualFormClosure"/> 的拒载理由）；
///  11. CompositionHash：有序 packId/version/PackSha256 + capability 闭包 + 最终 symbol 状态。
/// 本类不执行任何包贡献对象（只读 ValidatedDelta 与 manifest 声明）。
/// </summary>
public static class CompatPackCompositionResolver
{
    /// <summary>CompositionHash 的规范文本版本标记（格式变更必须升版本）。</summary>
    const string HashDomain = "gemuera.compatpack.composition.v1";

    const char NewLine = (char)10;
    const char Backslash = (char)92;
    const char Pipe = (char)124;
    const char CarriageReturn = (char)13;

    /// <summary>解析包组合。失败时 <paramref name="result"/> 为 null，errors 非空。</summary>
    public static bool TryResolve(
        IReadOnlyList<CompatPackHandle> handles,
        CompatPackValidationContext context,
        out CompatPackCompositionResult result,
        out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(handles);
        ArgumentNullException.ThrowIfNull(context);
        result = null!;
        var collected = new List<string>();

        // 1. packId → handle（重复/空句柄拒载；组合图按 manifest packId 建索引）。
        var byId = new Dictionary<string, CompatPackHandle>(StringComparer.Ordinal);
        var packs = new List<CompatPackHandle>();
        foreach (CompatPackHandle? handle in handles)
        {
            if (handle is null)
            {
                collected.Add("包句柄列表含 null 元素。");
                continue;
            }
            CompatPackManifest manifest = handle.Manifest;
            ValidatedPackDelta delta = handle.ValidatedDelta;
            if (manifest is null || delta is null)
            {
                collected.Add("包句柄缺少清单或冻结差量（内部不变量破坏）。");
                continue;
            }
            if (!string.Equals(delta.PackId, manifest.PackId, StringComparison.Ordinal))
            {
                collected.Add("包 " + manifest.PackId + " 的冻结差量 packId 与清单不一致："
                    + delta.PackId + "。");
            }
            // 依赖版本匹配读 delta.PackVersion，CompositionHash/pack 行读 manifest.PackVersion：
            // 两者漂移会让"被校验的版本"与"入账的版本"不同，必须在组合入口拒载。
            if (!string.Equals(delta.PackVersion, manifest.PackVersion, StringComparison.Ordinal))
            {
                collected.Add("包 " + manifest.PackId + " 的冻结差量 packVersion 与清单不一致："
                    + delta.PackVersion + " 与 " + manifest.PackVersion + "。");
            }
            if (!byId.TryAdd(handle.PackId, handle))
            {
                collected.Add("packId 重复：" + handle.PackId + "（同一包不能在一次组合中启用两次）。");
                continue;
            }
            packs.Add(handle);
        }

        // 诊断顺序与输入排列无关（错误文案顺序也确定，便于回归比对）。
        packs.Sort((left, right) => string.CompareOrdinal(left.Manifest.PackId, right.Manifest.PackId));

        // 8. 无效果包：没有任何 symbol/capability/variant 贡献（只有 requires 不算贡献）。
        foreach (CompatPackHandle handle in packs)
        {
            if (!HasEffect(handle.ValidatedDelta))
            {
                collected.Add("包 " + handle.Manifest.PackId
                    + " 无任何有效 symbol/capability/variant 贡献"
                    + "（无效果包不得进入最终组合；bundle 不在 v1 范围）。");
            }
        }

        // 5/6 的输入：每包符号状态规范化（owner 以句柄 packId 为准）与包内自相矛盾检测。
        var claimsBySymbol = new Dictionary<CompatSymbolKey, List<Claim>>();
        foreach (CompatPackHandle handle in packs)
            CollectClaims(handle, context, claimsBySymbol, collected);

        // 2. requires 闭包（缺失/版本不满足/环），错误携带依赖链。
        Dictionary<string, IReadOnlyList<string>> dependencies =
            BuildRequirements(packs, byId, collected, out bool hasCycle);

        // 3. capability 组合闭包（per-pack closure 的并集再闭合一次）。
        IReadOnlyList<string> capabilityIds = ResolveCapabilities(packs, context, collected);

        // 7. 声明冲突（任意版本；priority 不参与）。
        ValidateDeclaredConflicts(packs, byId, collected);

        // 6. override 声明合法性（目标存在、声明方参与、基线保护、variant 目标）。
        Dictionary<CompatSymbolKey, List<OverrideClaim>> validOverrides =
            ValidateOverrides(packs, claimsBySymbol, context, collected);

        // 5/6. 合并最终状态并仲裁冲突。
        Dictionary<CompatSymbolKey, CompatSymbolState> finalStates =
            MergeSymbolStates(claimsBySymbol, validOverrides, collected);

        // 9. 跨 kind 闭包（隐藏的指令 × 同名变体选择，跨包/跨 kind 无 override 通道）。
        ValidateHideVariantCrossKind(finalStates, claimsBySymbol, context, collected);

        // 10. 双形态闭包（METHOD 投影名两侧同态；override 破坏镜像即拒载，见方法注释）。
        // contractAbi 1.1：addInstructionCollisions 声明"保留基线指令形态、只隐藏函数形态"的
        // 同名碰撞态，闭包对集合内名字接受 function=Hidden + instruction=基线 Present。
        var instructionCollisions = new HashSet<string>(StringComparer.Ordinal);
        foreach (CompatPackHandle handle in packs)
        {
            foreach (string collision in handle.Manifest.Surface.AddInstructionCollisions)
                instructionCollisions.Add(collision);
        }
        ValidateDualFormClosure(finalStates, context, instructionCollisions, collected);

        if (collected.Count > 0)
        {
            errors = collected;
            return false;
        }

        // 4. 拓扑排序：依赖先于依赖者，同层按 packId Ordinal（深度 = 最长依赖路径）。
        List<CompatPackHandle> orderedPacks = OrderPacks(packs, dependencies, hasCycle);

        // 11. CompositionHash（有序 pack 身份 + capability 闭包 + 最终符号状态）。
        string compositionHash = ComputeCompositionHash(orderedPacks, capabilityIds, finalStates);
        result = new CompatPackCompositionResult(orderedPacks, capabilityIds, finalStates.Values, compositionHash);
        errors = collected;
        return true;
    }

    static bool HasEffect(ValidatedPackDelta delta)
        => delta.SurfaceStates.Count > 0
            || delta.CapabilityIds.Count > 0
            || delta.VariantSelections.Count > 0;

    static void CollectClaims(
        CompatPackHandle handle,
        CompatPackValidationContext context,
        Dictionary<CompatSymbolKey, List<Claim>> claimsBySymbol,
        List<string> errors)
    {
        string packId = handle.Manifest.PackId;
        var stateKinds = new Dictionary<CompatSymbolKey, CompatSymbolStateKind>();
        var seenStates = new HashSet<string>(StringComparer.Ordinal);
        foreach (CompatSymbolState state in handle.ValidatedDelta.SurfaceStates)
        {
            if (state is null)
            {
                errors.Add("包 " + packId + " 的冻结差量含 null 符号状态。");
                continue;
            }

            CompatSymbolKey key = state.Key;
            if (key.Kind == CompatSymbolKind.Variant)
            {
                if (state.Kind != CompatSymbolStateKind.VariantSelection)
                {
                    errors.Add("包 " + packId + " 的变体符号 " + key.ToContractString()
                        + " 状态必须是变体选择。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(state.BuiltinVariant))
                {
                    errors.Add("包 " + packId + " 的变体选择 " + key.ToContractString() + " 缺少变体名。");
                    continue;
                }
                if (!context.BaselineInstructions.Contains(key.Name))
                {
                    errors.Add("包 " + packId + " 的变体选择的指令不在 v24 基线内："
                        + key.ToContractString() + "。");
                    continue;
                }
            }
            else
            {
                if (state.Kind == CompatSymbolStateKind.VariantSelection)
                {
                    errors.Add("包 " + packId + " 的 " + key.ToContractString()
                        + " 使用了 VariantSelection 状态（kind 不匹配）。");
                    continue;
                }
                // 改既有语义必须走变体通道：Present 不得覆盖 v24 基线 handler。
                if (state.Kind == CompatSymbolStateKind.Present)
                {
                    // contractAbi 1.1：addInstructionCollisions 允许包为"保留基线指令形态"声明
                    // Present（同名碰撞态）；其余 Present 仍不得覆盖 v24 基线 handler。
                    bool collisionInstruction = key.Kind == CompatSymbolKind.Instruction
                        && handle.Manifest.Surface.AddInstructionCollisions.Any(collision =>
                            string.Equals(collision, key.Name, StringComparison.Ordinal));
                    if (key.Kind == CompatSymbolKind.Instruction
                        && context.BaselineInstructions.Contains(key.Name)
                        && !collisionInstruction)
                    {
                        errors.Add("包 " + packId + " 注册指令与 v24 基线同名：" + key.Name
                            + "（改既有语义必须走变体选择通道）。");
                        continue;
                    }
                    if (key.Kind == CompatSymbolKind.Function && context.BaselineFunctions.Contains(key.Name))
                    {
                        errors.Add("包 " + packId + " 注册函数与 v24 基线同名：" + key.Name
                            + "（改既有语义必须走变体选择通道）。");
                        continue;
                    }
                }
            }

            if (stateKinds.TryGetValue(key, out CompatSymbolStateKind existingKind)
                && existingKind != state.Kind)
            {
                errors.Add("包 " + packId + " 对 " + key.ToContractString() + " 同时声明了 "
                    + existingKind + " 与 " + state.Kind + "（同一包内状态自相矛盾）。");
                continue;
            }
            stateKinds[key] = state.Kind;

            string fingerprint = key.ToContractString() + Pipe + state.Kind
                + Pipe + (state.ReturnType ?? "") + Pipe + (state.BuiltinVariant ?? "");
            if (!seenStates.Add(fingerprint))
                continue;

            if (!claimsBySymbol.TryGetValue(key, out List<Claim>? claims))
            {
                claims = new List<Claim>();
                claimsBySymbol[key] = claims;
            }
            // owner 以句柄 packId 为归属事实源（差量 owner 只作诊断/校验期记录）。
            claims.Add(new Claim(handle, state with { OwnerPackId = packId }));
        }

        // 变体选择的第二事实源：ValidatedPackDelta.VariantSelections 是清单副本，规范差量同时
        // 带 VariantSelection 状态。手工/旧差量可能只有字典——这里补声明为 claim（与规则层
        // 产出的双份数据同语义），并校验二者一致，避免"字典有选择、组装却丢失"。
        foreach (KeyValuePair<string, string> selection in handle.ValidatedDelta.VariantSelections)
        {
            var key = new CompatSymbolKey(CompatSymbolKind.Variant, selection.Key);
            if (!context.BaselineInstructions.Contains(key.Name))
            {
                errors.Add("包 " + packId + " 的变体选择的指令不在 v24 基线内："
                    + key.ToContractString() + "。");
                continue;
            }
            if (string.IsNullOrWhiteSpace(selection.Value))
            {
                errors.Add("包 " + packId + " 的变体选择 " + key.ToContractString() + " 缺少变体名。");
                continue;
            }

            string? existingValue = claimsBySymbol.TryGetValue(key, out List<Claim>? existingClaims)
                ? existingClaims
                    .Where(claim => string.Equals(claim.PackId, packId, StringComparison.Ordinal))
                    .Select(claim => claim.State.BuiltinVariant)
                    .FirstOrDefault(value => value is not null)
                : null;
            if (existingValue is not null)
            {
                if (!string.Equals(existingValue, selection.Value, StringComparison.Ordinal))
                {
                    errors.Add("包 " + packId + " 的变体选择 " + key.ToContractString()
                        + " 与冻结状态不一致：" + existingValue + " 与 " + selection.Value + "。");
                }
                continue;
            }

            if (stateKinds.TryGetValue(key, out CompatSymbolStateKind existingKind)
                && existingKind != CompatSymbolStateKind.VariantSelection)
            {
                errors.Add("包 " + packId + " 对 " + key.ToContractString() + " 同时声明了 "
                    + existingKind + " 与变体选择（同一包内状态自相矛盾）。");
                continue;
            }
            stateKinds[key] = CompatSymbolStateKind.VariantSelection;
            if (!claimsBySymbol.TryGetValue(key, out List<Claim>? variantClaims))
            {
                variantClaims = new List<Claim>();
                claimsBySymbol[key] = variantClaims;
            }
            variantClaims.Add(new Claim(handle, new CompatSymbolState(
                key, CompatSymbolStateKind.VariantSelection, packId, null, selection.Value)));
        }
    }

    static Dictionary<string, IReadOnlyList<string>> BuildRequirements(
        IReadOnlyList<CompatPackHandle> packs,
        IReadOnlyDictionary<string, CompatPackHandle> byId,
        List<string> errors,
        out bool hasCycle)
    {
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var states = new Dictionary<string, VisitState>(StringComparer.Ordinal);
        foreach (CompatPackHandle handle in packs)
            states[handle.Manifest.PackId] = VisitState.Unvisited;

        bool cycleDetected = false;

        void Visit(string packId, List<string> path)
        {
            states[packId] = VisitState.Visiting;
            path.Add(packId);
            var dependencies = new List<string>();
            foreach (CompatPackDependency dependency in byId[packId].Manifest.RequiredPacks)
            {
                string dependencyId = dependency.PackId;
                if (string.Equals(dependencyId, packId, StringComparison.Ordinal))
                {
                    errors.Add("包依赖环：" + FormatCycle(path, dependencyId) + "（包不能依赖自身）。");
                    cycleDetected = true;
                    continue;
                }
                if (!byId.TryGetValue(dependencyId, out CompatPackHandle? dependencyHandle))
                {
                    errors.Add("包依赖缺失：" + FormatChain(path, dependencyId)
                        + "（要求版本范围 " + dependency.VersionRange + "）。");
                    continue;
                }
                if (!SemanticVersionRange.IsValid(dependency.VersionRange))
                {
                    errors.Add("包 " + packId + " 依赖 " + dependencyId + " 的版本范围不合法："
                        + dependency.VersionRange + "。");
                    continue;
                }
                string actualVersion = dependencyHandle.ValidatedDelta.PackVersion;
                if (!SemanticVersionRange.Contains(dependency.VersionRange, actualVersion))
                {
                    errors.Add("包依赖版本不满足：" + FormatChain(path, dependencyId)
                        + "（要求 " + dependency.VersionRange + "，实际 " + actualVersion + "）。");
                }
                dependencies.Add(dependencyId);
                switch (states[dependencyId])
                {
                    case VisitState.Visiting:
                        errors.Add("包依赖环：" + FormatCycle(path, dependencyId) + "。");
                        cycleDetected = true;
                        break;
                    case VisitState.Unvisited:
                        Visit(dependencyId, path);
                        break;
                }
            }
            edges[packId] = dependencies;
            path.RemoveAt(path.Count - 1);
            states[packId] = VisitState.Visited;
        }

        foreach (string packId in states.Keys.Order(StringComparer.Ordinal).ToArray())
        {
            if (states[packId] == VisitState.Unvisited)
                Visit(packId, new List<string>());
        }

        hasCycle = cycleDetected;
        return edges.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
    }

    static string FormatChain(List<string> path, string next)
    {
        var builder = new StringBuilder();
        foreach (string packId in path)
            builder.Append(packId).Append(" -> ");
        builder.Append(next);
        return builder.ToString();
    }

    static string FormatCycle(List<string> path, string repeated)
    {
        int start = path.LastIndexOf(repeated);
        var builder = new StringBuilder();
        for (int index = start < 0 ? 0 : start; index < path.Count; index++)
            builder.Append(path[index]).Append(" -> ");
        builder.Append(repeated);
        return builder.ToString();
    }

    static IReadOnlyList<string> ResolveCapabilities(
        IReadOnlyList<CompatPackHandle> packs,
        CompatPackValidationContext context,
        List<string> errors)
    {
        var requested = new SortedSet<string>(StringComparer.Ordinal);
        foreach (CompatPackHandle handle in packs)
        {
            foreach (string capabilityId in handle.ValidatedDelta.CapabilityIds)
            {
                if (!string.IsNullOrWhiteSpace(capabilityId))
                    requested.Add(capabilityId);
            }
        }

        if (!EngineCapabilityCatalog.TryResolveForPack(
                context.Capabilities, requested, out CapabilityResolution resolution, out IReadOnlyList<string> closureErrors))
        {
            errors.AddRange(closureErrors);
            return requested.ToArray();
        }

        return resolution.OrderedCapabilityIds
            .Where(capabilityId => !string.IsNullOrWhiteSpace(capabilityId))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    static void ValidateDeclaredConflicts(
        IReadOnlyList<CompatPackHandle> packs,
        IReadOnlyDictionary<string, CompatPackHandle> byId,
        List<string> errors)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (CompatPackHandle handle in packs)
        {
            string packId = handle.Manifest.PackId;
            foreach (string conflictingPackId in handle.Manifest.ConflictingPacks)
            {
                if (!byId.ContainsKey(conflictingPackId))
                    continue;
                string pairKey = string.CompareOrdinal(packId, conflictingPackId) <= 0
                    ? packId + "\u0000" + conflictingPackId
                    : conflictingPackId + "\u0000" + packId;
                if (!reported.Add(pairKey))
                    continue;
                errors.Add("包冲突：包 " + packId + " 声明与 " + conflictingPackId
                    + " 冲突，但两者同时启用（conflicts 永远拒载，priority 不能覆盖）。");
            }
        }
    }

    static Dictionary<CompatSymbolKey, List<OverrideClaim>> ValidateOverrides(
        IReadOnlyList<CompatPackHandle> packs,
        IReadOnlyDictionary<CompatSymbolKey, List<Claim>> claimsBySymbol,
        CompatPackValidationContext context,
        List<string> errors)
    {
        var valid = new Dictionary<CompatSymbolKey, List<OverrideClaim>>();
        foreach (CompatPackHandle handle in packs)
        {
            string packId = handle.Manifest.PackId;
            foreach (CompatPackOverride entry in handle.Manifest.Overrides)
            {
                if (!CompatSymbolKey.TryParse(entry.Symbol, out CompatSymbolKey key))
                {
                    errors.Add("包 " + packId + " 的 override symbol 不是合法契约串：" + entry.Symbol + "。");
                    continue;
                }
                // 基线保护先于目标存在性：即使当前组合没有别的包声明该符号，覆盖 v24 基线
                // handler 也是硬错误（改既有语义必须走变体通道），错误文案不应误导成"目标不存在"。
                if (key.Kind == CompatSymbolKind.Instruction && context.BaselineInstructions.Contains(key.Name))
                {
                    errors.Add("包 " + packId + " 的 override 不能覆盖 v24 基线指令 handler："
                        + key.ToContractString() + "。");
                    continue;
                }
                if (key.Kind == CompatSymbolKind.Function && context.BaselineFunctions.Contains(key.Name))
                {
                    errors.Add("包 " + packId + " 的 override 不能覆盖 v24 基线函数 handler："
                        + key.ToContractString() + "。");
                    continue;
                }
                if (key.Kind == CompatSymbolKind.Variant && !context.BaselineInstructions.Contains(key.Name))
                {
                    errors.Add("包 " + packId + " 的 variant override 目标指令不在 v24 基线内："
                        + key.ToContractString() + "。");
                    continue;
                }
                if (!claimsBySymbol.TryGetValue(key, out List<Claim>? claims) || claims.Count == 0)
                {
                    errors.Add("包 " + packId + " 的 override 目标不存在：" + key.ToContractString()
                        + "（本次组合没有任何包声明该符号）。");
                    continue;
                }
                if (!claims.Exists(claim => string.Equals(claim.PackId, packId, StringComparison.Ordinal)))
                {
                    errors.Add("包 " + packId + " 的 override 声明方未贡献目标符号："
                        + key.ToContractString() + "（override 只能仲裁自己参与的符号）。");
                    continue;
                }
                if (!valid.TryGetValue(key, out List<OverrideClaim>? entries))
                {
                    entries = new List<OverrideClaim>();
                    valid[key] = entries;
                }
                entries.Add(new OverrideClaim(packId, entry.Priority));
            }
        }
        return valid;
    }

    static Dictionary<CompatSymbolKey, CompatSymbolState> MergeSymbolStates(
        IReadOnlyDictionary<CompatSymbolKey, List<Claim>> claimsBySymbol,
        IReadOnlyDictionary<CompatSymbolKey, List<OverrideClaim>> validOverrides,
        List<string> errors)
    {
        var finalStates = new Dictionary<CompatSymbolKey, CompatSymbolState>();
        foreach (KeyValuePair<CompatSymbolKey, List<Claim>> pair in claimsBySymbol
            .OrderBy(entry => entry.Key.Kind)
            .ThenBy(entry => entry.Key.Name, StringComparer.Ordinal))
        {
            CompatSymbolKey key = pair.Key;
            List<Claim> claims = pair.Value;
            if (key.Kind == CompatSymbolKind.Variant)
            {
                MergeVariantState(key, claims, validOverrides, finalStates, errors);
                continue;
            }

            List<Claim> presentClaims = claims
                .Where(claim => claim.State.Kind == CompatSymbolStateKind.Present)
                .ToList();
            List<Claim> hiddenClaims = claims
                .Where(claim => claim.State.Kind == CompatSymbolStateKind.Hidden)
                .ToList();
            string[] presentOwners = presentClaims
                .Select(claim => claim.PackId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            // 同一包内函数返回类型不一致：即使只有单一 owner 也无法确定 plan 元数据。
            if (key.Kind == CompatSymbolKind.Function && presentOwners.Length == 1)
            {
                string[] returnTypes = presentClaims
                    .Select(claim => claim.State.ReturnType ?? "")
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                if (returnTypes.Length > 1)
                {
                    errors.Add("函数 " + key.Name + " 在同一包内声明了不同返回类型："
                        + string.Join("、", returnTypes) + "。");
                    continue;
                }
            }

            bool conflicted = presentOwners.Length > 1
                || (presentClaims.Count > 0 && hiddenClaims.Count > 0);
            if (!conflicted)
            {
                if (presentClaims.Count > 0)
                {
                    finalStates[key] = presentClaims[0].State;
                }
                else
                {
                    finalStates[key] = hiddenClaims
                        .OrderBy(claim => claim.PackId, StringComparer.Ordinal)
                        .First().State;
                }
                continue;
            }

            string kindLabel = key.Kind == CompatSymbolKind.Instruction ? "指令" : "函数";
            if (presentOwners.Length > 1)
            {
                string message = kindLabel + " " + key.Name + " 被多个包注册："
                    + string.Join(" 与 ", presentOwners) + "（需要显式 override 声明唯一最高 priority）。";
                if (!TryResolveConflict(key, claims, validOverrides, finalStates, errors, message))
                    continue;
                continue;
            }

            string mixedMessage = "符号 " + key.ToContractString() + " 被多个包以不同最终状态声明（Present 与 Hidden："
                + string.Join("、", claims.Select(claim => claim.PackId).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)) + "），需要显式 override 声明唯一最高 priority。";
            TryResolveConflict(key, claims, validOverrides, finalStates, errors, mixedMessage);
        }
        return finalStates;
    }

    static void MergeVariantState(
        CompatSymbolKey key,
        List<Claim> claims,
        IReadOnlyDictionary<CompatSymbolKey, List<OverrideClaim>> validOverrides,
        Dictionary<CompatSymbolKey, CompatSymbolState> finalStates,
        List<string> errors)
    {
        string[] variants = claims
            .Select(claim => claim.State.BuiltinVariant ?? "")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (variants.Length == 1)
        {
            finalStates[key] = claims
                .OrderBy(claim => claim.PackId, StringComparer.Ordinal)
                .First().State;
            return;
        }

        string message = "指令 " + key.Name + " 的变体选择跨包冲突："
            + string.Join(" 与 ", variants) + "（需要显式 override 声明唯一最高 priority）。";
        TryResolveConflict(key, claims, validOverrides, finalStates, errors, message);
    }

    /// <summary>
    /// 跨 kind 闭包（T6 fix I-2）：变体选择以指令 handler 为消费目标，指令被隐藏后该选择
    /// 在运行期静默零效果（plan 有选择、运行期无对象）。单包层 <see cref="CompatPackRules"/>
    /// 已对同一组合 fail-closed；组合层对跨包组合同样拒载——跨 kind 没有 override 仲裁通道，
    /// 唯一正确结果是整组拒绝。函数侧沿用规则层语义：METHOD 投影名的函数隐藏镜像为指令
    /// 不可见（双形态闭包），因此以 effective hidden instruction 集合判断。
    /// </summary>
    static void ValidateHideVariantCrossKind(
        IReadOnlyDictionary<CompatSymbolKey, CompatSymbolState> finalStates,
        IReadOnlyDictionary<CompatSymbolKey, List<Claim>> claimsBySymbol,
        CompatPackValidationContext context,
        List<string> errors)
    {
        var hiddenInstructions = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<CompatSymbolKey, CompatSymbolState> entry in finalStates)
        {
            if (entry.Value.Kind != CompatSymbolStateKind.Hidden)
                continue;
            if (entry.Key.Kind == CompatSymbolKind.Instruction
                || (entry.Key.Kind == CompatSymbolKind.Function
                    && context.MethodProjectedInstructionNames.Contains(entry.Key.Name)))
            {
                hiddenInstructions.Add(entry.Key.Name);
            }
        }
        if (hiddenInstructions.Count == 0)
            return;

        foreach (KeyValuePair<CompatSymbolKey, CompatSymbolState> entry in finalStates
            .Where(pair => pair.Key.Kind == CompatSymbolKind.Variant)
            .OrderBy(pair => pair.Key.Name, StringComparer.Ordinal))
        {
            string name = entry.Key.Name;
            if (!hiddenInstructions.Contains(name))
                continue;

            var hideOwners = new SortedSet<string>(StringComparer.Ordinal);
            AddOwners(claimsBySymbol,
                new CompatSymbolKey(CompatSymbolKind.Instruction, name), CompatSymbolStateKind.Hidden, hideOwners);
            if (context.MethodProjectedInstructionNames.Contains(name))
            {
                AddOwners(claimsBySymbol,
                    new CompatSymbolKey(CompatSymbolKind.Function, name), CompatSymbolStateKind.Hidden, hideOwners);
            }
            var selectionOwners = new SortedSet<string>(StringComparer.Ordinal);
            AddOwners(claimsBySymbol, entry.Key, CompatSymbolStateKind.VariantSelection, selectionOwners);

            errors.Add("组合跨 kind 冲突：指令 " + name
                + " 被包 " + string.Join("、", hideOwners) + " 隐藏，但同一组合仍声明变体选择 "
                + entry.Key.ToContractString() + " = " + entry.Value.BuiltinVariant
                + "（包 " + string.Join("、", selectionOwners)
                + "）；hide 与 variant 选择不可共存（隐藏后变体在运行期静默无效果），整组拒载。");
        }
    }

    static void AddOwners(
        IReadOnlyDictionary<CompatSymbolKey, List<Claim>> claimsBySymbol,
        CompatSymbolKey key,
        CompatSymbolStateKind stateKind,
        ISet<string> owners)
    {
        if (!claimsBySymbol.TryGetValue(key, out List<Claim>? claims))
            return;
        foreach (Claim claim in claims)
        {
            if (claim.State.Kind == stateKind)
                owners.Add(claim.PackId);
        }
    }

    static bool TryResolveConflict(
        CompatSymbolKey key,
        List<Claim> claims,
        IReadOnlyDictionary<CompatSymbolKey, List<OverrideClaim>> validOverrides,
        Dictionary<CompatSymbolKey, CompatSymbolState> finalStates,
        List<string> errors,
        string conflictMessage)
    {
        // priority 只在"确实需要仲裁"时参与（评审 M-1 的文档边界）：同最终状态的多包声明
        // 在 MergeSymbolStates 已按同态合并（owner 取 packId Ordinal 最小），不比较 priority；
        // 只有 Present 多 owner / Present×Hidden / 变体异值这些真实冲突才检查最高 priority
        // 唯一性，并列即拒载。两种口径都确定（合并 owner 仍进 CompositionHash）。
        if (!validOverrides.TryGetValue(key, out List<OverrideClaim>? overrides) || overrides.Count == 0)
        {
            errors.Add(conflictMessage);
            return false;
        }

        int highestPriority = overrides.Max(entry => entry.Priority);
        List<OverrideClaim> top = overrides
            .Where(entry => entry.Priority == highestPriority)
            .ToList();
        if (top.Count > 1)
        {
            errors.Add("符号 " + key.ToContractString() + " 的 override priority 并列最高（"
                + highestPriority + "）："
                + string.Join("、", top.Select(entry => entry.PackId).Order(StringComparer.Ordinal))
                + "；同 priority 拒载。");
            return false;
        }

        string winnerPackId = top[0].PackId;
        List<Claim> winnerClaims = claims
            .Where(claim => string.Equals(claim.PackId, winnerPackId, StringComparison.Ordinal))
            .ToList();
        if (winnerClaims.Count != 1)
        {
            errors.Add("符号 " + key.ToContractString() + " 的 override 胜出包 " + winnerPackId
                + " 必须恰好声明一个最终状态（实际 " + winnerClaims.Count + " 个）。");
            return false;
        }
        finalStates[key] = winnerClaims[0].State;
        return true;
    }

    /// <summary>
    /// 双形态闭包（设计 §6.7 / T4）：引擎把可见表达式函数投影为 METHOD 指令，二者共享
    /// 注册表键空间。单包差量已在规则层补镜像，但跨包 override 可能只仲裁一侧、导致
    /// plan 指令面与运行期注册表不一致。这里选择**拒载**而非静默重闭合：任何确定性重闭合
    /// 都会替包作者改写另一侧的显式声明（例如把另一包的 hide 静默翻成 present），来源
    /// 归属与意图不再可复现；fail-closed 让包作者显式声明两侧 override。两侧同态且 Present
    /// 归属同一包才接受。
    /// </summary>
    static void ValidateDualFormClosure(
        IReadOnlyDictionary<CompatSymbolKey, CompatSymbolState> finalStates,
        CompatPackValidationContext context,
        IReadOnlySet<string> instructionCollisions,
        List<string> errors)
    {
        foreach (string name in context.MethodProjectedInstructionNames.Order(StringComparer.Ordinal))
        {
            var instructionKey = new CompatSymbolKey(CompatSymbolKind.Instruction, name);
            var functionKey = new CompatSymbolKey(CompatSymbolKind.Function, name);
            bool hasInstruction = finalStates.TryGetValue(instructionKey, out CompatSymbolState? instruction);
            bool hasFunction = finalStates.TryGetValue(functionKey, out CompatSymbolState? function);
            if (!hasInstruction && !hasFunction)
                continue;

            string symbolName = instructionKey.ToContractString();
            if (instructionCollisions.Contains(name))
            {
                // 碰撞态（contractAbi 1.1）：函数侧必须 Hidden；指令侧允许缺省（沿用 v24 基线
                // Present）或显式 Present；任何其他组合都破坏 fail-closed 语义。
                if (!hasFunction || function!.Kind != CompatSymbolStateKind.Hidden)
                    errors.Add("碰撞态 METHOD 投影名 " + symbolName
                        + " 的 function 最终状态必须是 Hidden（addInstructionCollisions 契约）。");
                if (hasInstruction && instruction!.Kind != CompatSymbolStateKind.Present)
                    errors.Add("碰撞态 METHOD 投影名 " + symbolName
                        + " 的 instruction 最终状态只能是 Present 或沿用基线（实际 " + instruction.Kind + "）。");
                if (!context.BaselineInstructions.Contains(name))
                    errors.Add("碰撞态 METHOD 投影名 " + symbolName + " 不在 v24 基线指令面内。");
                continue;
            }
            if (hasInstruction != hasFunction)
            {
                errors.Add("双形态闭包不一致：METHOD 投影名 " + symbolName
                    + " 只在 " + (hasInstruction ? "instruction" : "function")
                    + " 侧存在最终状态（缺少镜像 " + (hasInstruction ? "function" : "instruction")
                    + " 状态）；包差量或 override 破坏了 T4 双形态闭包，按 fail-closed 拒载。");
                continue;
            }

            if (instruction!.Kind != function!.Kind)
            {
                errors.Add("双形态闭包不一致：METHOD 投影名 " + symbolName + " 的 instruction 状态为 "
                    + instruction.Kind + "、function 状态为 " + function.Kind
                    + "（两侧必须同态，否则 plan 与运行期注册表不一致）。");
                continue;
            }

            if (instruction.Kind == CompatSymbolStateKind.Present
                && !string.Equals(instruction.OwnerPackId, function.OwnerPackId, StringComparison.Ordinal))
            {
                errors.Add("双形态闭包不一致：METHOD 投影名 " + symbolName
                    + " 的 instruction/function 归属包不同（" + instruction.OwnerPackId
                    + " 与 " + function.OwnerPackId + "）；同一符号的两形态必须同属一个包。");
            }
        }
    }

    static List<CompatPackHandle> OrderPacks(
        IReadOnlyList<CompatPackHandle> packs,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies,
        bool hasCycle)
    {
        var byId = packs.ToDictionary(handle => handle.Manifest.PackId, StringComparer.Ordinal);
        if (hasCycle)
        {
            // 防御：正常路径在错误收集后不会走到这里；仍给出确定性顺序（不下溢/不抛）。
            return packs.OrderBy(handle => handle.Manifest.PackId, StringComparer.Ordinal).ToList();
        }

        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        int Depth(string packId)
        {
            if (depths.TryGetValue(packId, out int cached))
                return cached;
            if (!visiting.Add(packId))
                return 0; // 环已在 BuildRequirements 拒载；此处只做防御，避免无限递归。
            int depth = 0;
            if (dependencies.TryGetValue(packId, out IReadOnlyList<string>? required))
            {
                foreach (string dependencyId in required)
                {
                    int candidate = Depth(dependencyId) + 1;
                    if (candidate > depth)
                        depth = candidate;
                }
            }
            visiting.Remove(packId);
            depths[packId] = depth;
            return depth;
        }

        return packs
            .Select(handle => handle.Manifest.PackId)
            .OrderBy(Depth)
            .ThenBy(packId => packId, StringComparer.Ordinal)
            .Select(packId => byId[packId])
            .ToList();
    }

    static string ComputeCompositionHash(
        IReadOnlyList<CompatPackHandle> orderedPacks,
        IReadOnlyList<string> capabilityIds,
        IReadOnlyDictionary<CompatSymbolKey, CompatSymbolState> symbolStates)
    {
        var canonical = new StringBuilder();
        canonical.Append(HashDomain).Append(NewLine);
        foreach (CompatPackHandle handle in orderedPacks)
        {
            canonical.Append("pack=").Append(Escape(handle.PackId))
                .Append(Pipe).Append(Escape(handle.PackVersion))
                .Append(Pipe).Append(Escape(handle.PackSha256))
                .Append(NewLine);
        }
        foreach (string capabilityId in capabilityIds.Order(StringComparer.Ordinal))
            canonical.Append("capability=").Append(Escape(capabilityId)).Append(NewLine);
        foreach (CompatSymbolState state in symbolStates.Values
            .OrderBy(value => value.Key.Kind)
            .ThenBy(value => value.Key.Name, StringComparer.Ordinal))
        {
            canonical.Append("symbol=").Append(Escape(state.Key.ToContractString()))
                .Append(Pipe).Append(state.Kind.ToString())
                .Append(Pipe).Append(Field(state.OwnerPackId))
                .Append(Pipe).Append(Field(state.ReturnType))
                .Append(Pipe).Append(Field(state.BuiltinVariant))
                .Append(NewLine);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    static string Field(string? value) => value is null ? "n" : "v" + Escape(value);

    static string Escape(string value)
    {
        if (value.Length == 0)
            return string.Empty;
        var escaped = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case Backslash:
                    escaped.Append(Backslash).Append(Backslash);
                    break;
                case NewLine:
                    escaped.Append(Backslash).Append('n');
                    break;
                case CarriageReturn:
                    escaped.Append(Backslash).Append('r');
                    break;
                case Pipe:
                    escaped.Append(Backslash).Append(Pipe);
                    break;
                default:
                    escaped.Append(c);
                    break;
            }
        }
        return escaped.ToString();
    }

    enum VisitState
    {
        Unvisited,
        Visiting,
        Visited,
    }

    sealed class Claim
    {
        public Claim(CompatPackHandle handle, CompatSymbolState state)
        {
            Handle = handle;
            State = state;
        }

        public CompatPackHandle Handle { get; }
        public CompatSymbolState State { get; }
        public string PackId => Handle.Manifest.PackId;
    }

    sealed record OverrideClaim(string PackId, int Priority);
}
