using System.Security.Cryptography;
using System.Text;
using GEmuera.Core.Runtime;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 组装段（设计文档 §5.4 / DOD-2「组装」项）：把已加载并通过校验的包集合折叠进会话计划。
/// T6 起本类不再自行合并包差量：先调用 <see cref="CompatPackCompositionResolver"/> 完成
/// 依赖闭包/conflicts/override 仲裁/双形态闭包，再只读其 <see cref="CompatPackCompositionResult.OrderedPacks"/>
/// 与 <see cref="CompatPackCompositionResult.SymbolStates"/> 生成 plan。因此：
///   - 表面 = 基线 ±（resolver 最终 symbol 状态）；capability = 基线 ∪ resolver 组合级闭包；
///   - 模块闭包 = 基线 + 每个有序包一个合成快照；
///   - 哈希 = 基线方言哈希 + resolver 的 CompositionHash + 有序包身份/最终表面（同包内容必同
///     哈希，与输入排列无关）；
///   - 组装期绝不执行包贡献对象（ValidatedDelta 已在规则层冻结一次）。
/// 空包集原样返回基线实例（禁用任何包的会话 = 纯 v24，逐字节等价的不变量入口）。
/// 引擎侧投影（handler 变体接线）属 DOD-3 宿主接线，本类只产出计划数据。
/// </summary>
public static class CompatPackPlanAssembler
{
    /// <summary>
    /// 组合解析并折叠进基线计划。任一跨包问题（依赖/冲突/override/双形态/无效果包/变体或
    /// save profile 冲突）即整体失败（fail-closed，与加载器同纪律）。saveProfileId 采用
    /// "包声明覆盖基线、跨包必须一致"的确定性语义；无包声明时保持基线 save profile。
    /// </summary>
    /// <param name="baseline">会话基线计划（v1 固定 v24pure）。</param>
    /// <param name="packs">本次显式启用的包句柄（顺序任意；resolver 负责确定性排序）。</param>
    /// <param name="context">
    /// 宿主校验上下文（与加载期同一份事实源）：resolver 用它做 capability 闭包、v24 基线
    /// 保护与 METHOD 双形态闭包。选择显式传参而非内部自建 catalog：宿主在加载前已构造
    /// 该上下文（含 handler/变体/基线表面等真实引擎事实），内部重建会与加载期输入漂移。
    /// 契约：<see cref="CompatPackValidationContext.BaselineProfileId"/> 必须等于
    /// <see cref="CompatibilityPlan.ProfileId"/>（由同一会话基线构造），否则拒载。
    /// </param>
    public static bool TryAssemble(
        CompatibilityPlan baseline,
        IReadOnlyList<CompatPackHandle> packs,
        CompatPackValidationContext context,
        out CompatibilityPlan assembled,
        out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(context);
        assembled = null!;
        var collected = new List<string>();

        if (packs.Count == 0)
        {
            // 空集 = 未启用任何包：会话计划就是基线实例本身（同哈希同引用）。
            assembled = baseline;
            errors = collected;
            return true;
        }

        // 组装上下文必须与会话基线同源：profile 身份漂移（如 v18 基线 + v24pure 上下文）
        // 会让 resolver 用错误的基线事实裁决，属接线错误，fail-closed。空包集已在上方
        // 原样返回基线（不消费 context），因此不受该约束影响。
        if (!string.Equals(baseline.ProfileId, context.BaselineProfileId, StringComparison.Ordinal))
        {
            collected.Add("组装上下文与基线 profile 不一致：基线 " + baseline.ProfileId
                + "，上下文 " + context.BaselineProfileId
                + "（context 必须由同一会话基线构造）。");
            errors = collected;
            return false;
        }

        // resolver 先行：依赖/冲突/override/capability/双形态全部在这里定稿；组装器只读结果。
        if (!CompatPackCompositionResolver.TryResolve(
                packs, context, out CompatPackCompositionResult composition, out IReadOnlyList<string> compositionErrors))
        {
            errors = compositionErrors;
            return false;
        }

        var instructions = new Dictionary<string, InstructionDescriptor>(
            baseline.Dialect.Instructions, StringComparer.Ordinal);
        var functions = new Dictionary<string, FunctionDescriptor>(
            baseline.Dialect.Functions, StringComparer.Ordinal);
        var variantSelections = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (KeyValuePair<CompatSymbolKey, CompatSymbolState> entry in composition.SymbolStates)
        {
            CompatSymbolKey key = entry.Key;
            CompatSymbolState state = entry.Value;
            switch (key.Kind)
            {
                case CompatSymbolKind.Instruction:
                    if (state.Kind == CompatSymbolStateKind.Hidden)
                    {
                        // 良性 no-op：名字在 v24 清单内、但当前 profile 的基线表面本就缺席
                        //（如 v18 会话隐藏 v24 后增指令 SETBGIMAGE）——该隐藏在此会话无事可做，
                        // 跳过而非拒载。真正未知的名字已在规则校验段挡下（hide ⊆ v24 基线）。
                        instructions.Remove(key.Name);
                    }
                    else if (state.Kind == CompatSymbolStateKind.Present)
                    {
                        if (string.IsNullOrWhiteSpace(state.OwnerPackId))
                        {
                            collected.Add("组合结果的指令 Present 状态缺少归属包：" + key.Name + "。");
                            break;
                        }
                        instructions[key.Name] = new InstructionDescriptor(
                            key.Name, "compatpack", state.OwnerPackId!, VmCompletionMode.CoreImmediate);
                    }
                    break;
                case CompatSymbolKind.Function:
                    if (state.Kind == CompatSymbolStateKind.Hidden)
                    {
                        functions.Remove(key.Name);
                    }
                    else if (state.Kind == CompatSymbolStateKind.Present)
                    {
                        if (string.IsNullOrWhiteSpace(state.OwnerPackId))
                        {
                            collected.Add("组合结果的函数 Present 状态缺少归属包：" + key.Name + "。");
                            break;
                        }
                        functions[key.Name] = new FunctionDescriptor(
                            key.Name,
                            "compatpack",
                            state.OwnerPackId!,
                            string.IsNullOrWhiteSpace(state.ReturnType) ? "Unknown" : state.ReturnType);
                    }
                    break;
                case CompatSymbolKind.Variant:
                    if (state.Kind == CompatSymbolStateKind.VariantSelection
                        && !string.IsNullOrWhiteSpace(state.BuiltinVariant))
                    {
                        variantSelections[key.Name] = state.BuiltinVariant!;
                    }
                    break;
            }
        }

        // 模块快照按 resolver 拓扑序取包身份，再按 packId 固化排序：plan 数据与输入排列无关。
        var packModules = composition.OrderedPacks
            .Select(handle => new DialectModuleSnapshot(
                handle.Manifest.PackId,
                handle.Manifest.PackVersion,
                handle.Manifest.EngineApi.Major))
            .ToList();
        var modules = baseline.Dialect.Modules.ToList();
        modules.AddRange(packModules.OrderBy(module => module.ModuleId, StringComparer.Ordinal));
        string[] packModuleIds = packModules
            .Select(module => module.ModuleId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string? saveProfileId = CollectSaveProfileId(baseline, composition.OrderedPacks, collected);
        if (collected.Count > 0)
        {
            errors = collected;
            return false;
        }

        var capabilities = new SortedSet<string>(baseline.CapabilityIds, StringComparer.Ordinal);
        capabilities.UnionWith(composition.CapabilityIds);

        string dialectHash = ComputeDialectDeltaHash(baseline, composition);
        var dialect = new DialectPlan(
            modules,
            baseline.Dialect.Ports,
            instructions,
            functions,
            dialectHash,
            packModuleIds: packModuleIds,
            variantSelections: variantSelections);
        string canonicalHash = ComputePlanEnvelopeHash(baseline, dialectHash, capabilities, saveProfileId);
        assembled = new CompatibilityPlan(baseline.ProfileId, dialect, capabilities, saveProfileId, canonicalHash);
        errors = collected;
        return true;
    }

    static string? CollectSaveProfileId(
        CompatibilityPlan baseline,
        IReadOnlyList<CompatPackHandle> packs,
        List<string> errors)
    {
        string? declared = null;
        string? declaringPack = null;
        foreach (CompatPackHandle pack in packs)
        {
            string? candidate = pack.Manifest.SaveProfileId;
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            if (declared is null)
            {
                declared = candidate;
                declaringPack = pack.Manifest.PackId;
                continue;
            }
            if (!string.Equals(declared, candidate, StringComparison.Ordinal))
            {
                errors.Add("包 " + declaringPack + " 与 " + pack.Manifest.PackId
                    + " 的 saveProfileId 冲突：" + declared + " 与 " + candidate + "。");
            }
        }
        return declared ?? baseline.SaveProfileId;
    }

    /// <summary>
    /// 方言层差量哈希：基线方言哈希 + resolver CompositionHash +（包 id|包字节哈希）有序对 +
    /// 最终表面差量 + 变体选择。CompositionHash 已覆盖有序包身份/capability 闭包/最终符号
    /// 状态，这里再按旧格式展开表面行，保持诊断可读与既有哈希分层（dialect hash → plan hash）。
    /// 全部行以规范排序/拓扑序输出——包输入排列不影响本值。
    /// </summary>
    static string ComputeDialectDeltaHash(CompatibilityPlan baseline, CompatPackCompositionResult composition)
    {
        var canonical = new StringBuilder()
            .Append("base=").Append(baseline.Dialect.CanonicalHash).Append('\n')
            .Append("composition=").Append(composition.CompositionHash).Append('\n');
        foreach (CompatPackHandle handle in composition.OrderedPacks)
        {
            canonical.Append("pack=").Append(handle.Manifest.PackId)
                .Append('|').Append(handle.PackSha256).Append('\n');
        }
        foreach (KeyValuePair<CompatSymbolKey, CompatSymbolState> entry in composition.SymbolStates)
        {
            CompatSymbolKey key = entry.Key;
            CompatSymbolState state = entry.Value;
            switch (key.Kind)
            {
                case CompatSymbolKind.Instruction:
                    if (state.Kind == CompatSymbolStateKind.Present)
                        canonical.Append("+instruction=").Append(key.Name).Append('|')
                            .Append(state.OwnerPackId).Append('\n');
                    else if (state.Kind == CompatSymbolStateKind.Hidden)
                        canonical.Append("-instruction=").Append(key.Name).Append('\n');
                    break;
                case CompatSymbolKind.Function:
                    if (state.Kind == CompatSymbolStateKind.Present)
                        canonical.Append("+function=").Append(key.Name).Append('|')
                            .Append(state.OwnerPackId).Append('|')
                            .Append(string.IsNullOrWhiteSpace(state.ReturnType) ? "Unknown" : state.ReturnType)
                            .Append('\n');
                    else if (state.Kind == CompatSymbolStateKind.Hidden)
                        canonical.Append("-function=").Append(key.Name).Append('\n');
                    break;
                case CompatSymbolKind.Variant:
                    if (state.Kind == CompatSymbolStateKind.VariantSelection)
                        canonical.Append("variant=").Append(key.Name).Append('|')
                            .Append(state.BuiltinVariant).Append('\n');
                    break;
            }
        }
        return Hash(canonical.ToString());
    }

    static string ComputePlanEnvelopeHash(
        CompatibilityPlan baseline,
        string dialectHash,
        IEnumerable<string> capabilities,
        string? saveProfileId)
    {
        var canonical = new StringBuilder()
            .Append("profile=").Append(baseline.ProfileId).Append('\n')
            .Append("dialect=").Append(dialectHash).Append('\n')
            .Append("save=").Append(saveProfileId ?? string.Empty).Append('\n');
        foreach (string capability in capabilities)
            canonical.Append("capability=").Append(capability).Append('\n');
        return Hash(canonical.ToString());
    }

    static string Hash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
