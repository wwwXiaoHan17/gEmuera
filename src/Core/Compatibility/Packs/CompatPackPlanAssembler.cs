using System.Security.Cryptography;
using System.Text;
using GEmuera.Core.Runtime;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 组装段（设计文档 §5.4 / DOD-2「组装」项）：把已加载并通过校验的包集合折叠进会话计划。
/// 表面 = 基线 ±（包 register/hide）；账本 = 基线 ∪ 包 capability；模块闭包 = 基线 + 每包
/// 一个合成快照。哈希把每个包的 PackSha256 与折叠差量一并计入——同包内容必同哈希，
/// 与包在清单中的排列顺序无关（诊断可复现，回归可比对）。
/// 空包集原样返回基线实例（禁用任何包的会话 = 纯 v24，逐字节等价的不变量入口）。
/// 引擎侧投影（handler 变体接线）属 DOD-3 宿主接线，本类只产出计划数据。
/// </summary>
public static class CompatPackPlanAssembler
{
    /// <summary>折叠包集合进基线计划。任一跨包冲突（同名注册、变体选择同键异值、save profile
    /// 同键异值）即整体失败（fail-closed，与加载器同纪律）。saveProfileId 采用"包声明覆盖
    /// 基线、跨包必须一致"的确定性语义；无包声明时保持基线 save profile。</summary>
    public static bool TryAssemble(
        CompatibilityPlan baseline,
        IReadOnlyList<CompatPackHandle> packs,
        out CompatibilityPlan assembled,
        out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(packs);
        assembled = null!;
        var collected = new List<string>();

        if (packs.Count == 0)
        {
            // 空集 = 未启用任何包：会话计划就是基线实例本身（同哈希同引用）。
            assembled = baseline;
            errors = collected;
            return true;
        }

        // T5：表面差量、能力账本与变体选择全部读校验期冻结的 ValidatedPackDelta——贡献对象
        // 只在规则层回放一次（ValidatePackEntry），组装期不再执行包作者代码。跨包冲突
        //（同名注册、变体选择同键异值、save profile 同键异值）仍在本类按 fail-closed 汇总。
        var addedInstructions = new Dictionary<string, (string PackId, string Name)>(StringComparer.Ordinal);
        var addedFunctions = new Dictionary<string, (string PackId, string Name, string ReturnType)>(StringComparer.Ordinal);
        var removedInstructions = new HashSet<string>(StringComparer.Ordinal);
        var removedFunctions = new HashSet<string>(StringComparer.Ordinal);
        var capabilities = new SortedSet<string>(baseline.CapabilityIds, StringComparer.Ordinal);
        var packModules = new List<DialectModuleSnapshot>();
        var packHashLines = new List<(string PackId, string PackSha256)>();

        foreach (CompatPackHandle pack in packs)
        {
            if (pack is null)
            {
                collected.Add("包句柄列表含 null 元素。");
                continue;
            }

            // T5：组装器只读校验期冻结的 ValidatedPackDelta，不再回放包作者的贡献对象
            //（设计 §6.6：校验一次、组装只读冻结 delta）。能力账本同样取 closure 展开结果。
            ValidatedPackDelta delta = pack.ValidatedDelta;
            // 合成模块快照的 ModuleApiVersion 取包的 engineApi major：双轴模型下 engineApi 取代
            // 旧的 targetEngineApi（DialectModuleSnapshot 只有 int 字段；完整双轴版本由 manifest 承载）。
            packModules.Add(new DialectModuleSnapshot(delta.PackId, delta.PackVersion, pack.Manifest.EngineApi.Major));
            packHashLines.Add((delta.PackId, delta.PackSha256));

            foreach (string capability in delta.CapabilityIds)
                capabilities.Add(capability);

            foreach (CompatSymbolState state in delta.SurfaceStates)
            {
                switch (state.Key.Kind)
                {
                    case CompatSymbolKind.Instruction:
                        if (state.Kind == CompatSymbolStateKind.Present)
                        {
                            if (addedInstructions.TryGetValue(state.Key.Name, out var instructionOwner))
                                collected.Add("指令 " + state.Key.Name + " 被多个包注册："
                                    + instructionOwner.PackId + " 与 " + delta.PackId + "。");
                            else
                                addedInstructions[state.Key.Name] = (delta.PackId, state.Key.Name);
                        }
                        else if (state.Kind == CompatSymbolStateKind.Hidden)
                        {
                            removedInstructions.Add(state.Key.Name);
                        }
                        break;
                    case CompatSymbolKind.Function:
                        if (state.Kind == CompatSymbolStateKind.Present)
                        {
                            if (addedFunctions.TryGetValue(state.Key.Name, out var functionOwner))
                                collected.Add("函数 " + state.Key.Name + " 被多个包注册："
                                    + functionOwner.PackId + " 与 " + delta.PackId + "。");
                            else
                                addedFunctions[state.Key.Name] = (
                                    delta.PackId,
                                    state.Key.Name,
                                    string.IsNullOrWhiteSpace(state.ReturnType) ? "Unknown" : state.ReturnType);
                        }
                        else if (state.Kind == CompatSymbolStateKind.Hidden)
                        {
                            removedFunctions.Add(state.Key.Name);
                        }
                        break;
                    case CompatSymbolKind.Variant:
                        // 变体选择从 delta.VariantSelections 合并（CollectVariantSelections）；
                        // VariantSelection 状态仅为冻结载体保留，不在此二次登记。
                        break;
                }
            }
        }

        if (collected.Count > 0)
        {
            errors = collected;
            return false;
        }

        // 折叠表面。单包校验已保证 hide⊆基线、register∉基线，因此 hide 与 register 的
        // 名字不可能相交（基线二分传递覆盖）；基线缺席的隐藏名按良性 no-op 跳过（见下）。
        var instructions = new Dictionary<string, InstructionDescriptor>(baseline.Dialect.Instructions, StringComparer.Ordinal);
        var functions = new Dictionary<string, FunctionDescriptor>(baseline.Dialect.Functions, StringComparer.Ordinal);
        foreach (string hidden in removedInstructions)
        {
            // 良性 no-op：名字在 v24 清单内、但当前 profile 的基线表面本就缺席
            //（如 v18 会话隐藏 v24 后增指令 SETBGIMAGE）——该隐藏在此会话无事可做，
            // 跳过而非拒载。真正未知的名字（∉任何基线）已在规则校验段挡下（hide⊆v24 基线）。
            instructions.Remove(hidden);
        }
        foreach (string hidden in removedFunctions)
        {
            // 同上：函数侧对称的良性 no-op（隐藏差量仍进哈希，包意图可复现）。
            functions.Remove(hidden);
        }
        foreach (var entry in addedInstructions.Values)
        {
            instructions[entry.Name] = new InstructionDescriptor(entry.Name, "compatpack", entry.PackId, VmCompletionMode.CoreImmediate);
        }
        foreach (var entry in addedFunctions.Values)
        {
            functions[entry.Name] = new FunctionDescriptor(entry.Name, "compatpack", entry.PackId, entry.ReturnType);
            // 函数侧到指令侧的 METHOD 投影镜像不在这里推导：规范化状态已在校验期按
            // MethodProjectedInstructionNames 产出同名 Present 指令（引擎把可见表达式函数
            // 投影为 METHOD 指令进入解析器注册表，因此指令侧声明必须与运行时投影同义）。
            // 这样组装器的输入是"校验通过的状态"，而不是二次解释原始 add/hide 名单。
        }
        if (collected.Count > 0)
        {
            errors = collected;
            return false;
        }

        var modules = baseline.Dialect.Modules.ToList();
        // 模块快照按 packId 固化排序：组装产出的计划数据与包传入顺序无关（与哈希同纪律）。
        modules.AddRange(packModules.OrderBy(module => module.ModuleId, StringComparer.Ordinal));
        string[] packModuleIds = packModules
            .Select(module => module.ModuleId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var variantSelections = CollectVariantSelections(packs, collected);
        string? saveProfileId = CollectSaveProfileId(baseline, packs, collected);
        if (collected.Count > 0)
        {
            errors = collected;
            return false;
        }

        string dialectHash = ComputeDialectDeltaHash(baseline, packHashLines, addedInstructions, addedFunctions, removedInstructions, removedFunctions, variantSelections);
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

    static IReadOnlyDictionary<string, string> CollectVariantSelections(
        IReadOnlyList<CompatPackHandle> packs,
        List<string> errors)
    {
        // 同键同值幂等放行；同键不同值是跨包冲突，收集错误整体拒载——不做静默仲裁
        // （fail-closed；否则包排列顺序会影响组装哈希，破坏「同启用集同哈希」）。
        // T5：变体选择读冻结 delta（manifest 副本已随校验冻结，组装期不再回看清单）。
        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CompatPackHandle pack in packs)
        {
            foreach (KeyValuePair<string, string> selection in pack.ValidatedDelta.VariantSelections)
            {
                if (selections.TryGetValue(selection.Key, out string? existing))
                {
                    if (!string.Equals(existing, selection.Value, StringComparison.Ordinal))
                        errors.Add("指令 " + selection.Key + " 的变体选择跨包冲突：" + existing + " 与 " + selection.Value + "。");
                    continue;
                }
                selections[selection.Key] = selection.Value;
            }
        }
        return selections;
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
    /// 方言层差量哈希：基线方言哈希 +（包 id|包字节哈希）有序对 + 表面差量 + 变体选择。
    /// 与 CompatibilityPlanBuilder 的哈希格式分层同构（dialect hash → plan hash），
    /// 但把包字节哈希作为一等输入——同包内容必同哈希，与包排列顺序无关。
    /// </summary>
    static string ComputeDialectDeltaHash(
        CompatibilityPlan baseline,
        List<(string PackId, string PackSha256)> packHashLines,
        Dictionary<string, (string PackId, string Name)> addedInstructions,
        Dictionary<string, (string PackId, string Name, string ReturnType)> addedFunctions,
        HashSet<string> removedInstructions,
        HashSet<string> removedFunctions,
        IReadOnlyDictionary<string, string> variantSelections)
    {
        var canonical = new StringBuilder()
            .Append("base=").Append(baseline.Dialect.CanonicalHash).Append('\n');
        foreach (var line in packHashLines.OrderBy(line => line.PackId, StringComparer.Ordinal))
            canonical.Append("pack=").Append(line.PackId).Append('|').Append(line.PackSha256).Append('\n');
        foreach (var name in addedInstructions.Keys.Order(StringComparer.Ordinal))
            canonical.Append("+instruction=").Append(name).Append('|').Append(addedInstructions[name].PackId).Append('\n');
        foreach (var name in removedInstructions.Order(StringComparer.Ordinal))
            canonical.Append("-instruction=").Append(name).Append('\n');
        foreach (var name in addedFunctions.Keys.Order(StringComparer.Ordinal))
            canonical.Append("+function=").Append(name).Append('|').Append(addedFunctions[name].PackId)
                .Append('|').Append(addedFunctions[name].ReturnType).Append('\n');
        foreach (var name in removedFunctions.Order(StringComparer.Ordinal))
            canonical.Append("-function=").Append(name).Append('\n');
        foreach (var pair in variantSelections.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            canonical.Append("variant=").Append(pair.Key).Append('|').Append(pair.Value).Append('\n');
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
