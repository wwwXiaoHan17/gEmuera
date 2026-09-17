using System.Security.Cryptography;
using System.Text;
using Emuera.Compatibility.Packs;
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
    /// <summary>折叠包集合进基线计划。任一跨包冲突（同名注册、变体选择同键异值）即整体失败
    /// （fail-closed，与加载器同纪律）。包清单的 saveProfileId v1 不参与组装——存档 profile
    /// 由基线会话决定，包级覆盖留宿主接线增量裁定。</summary>
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

        // 回放每个包的表面贡献，收集 register/hide（贡献确定性，校验期已回放过一次）。
        // 二轮回放与校验期同纪律：包作者代码（getter / Apply）抛异常一律转为组装错误条目
        // （含包 id + 贡献 id + 异常消息）并 fail-closed 返回 false——本类契约与加载器的
        // "任何输入都不允许异常逃逸"对齐，不依赖宿主 ConfigureForLaunch 的顶层 catch。
        // 非幂等包可能在校验期首轮回放通过、组装期第二轮抛异常，此处是最后一道防线。
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

            packModules.Add(new DialectModuleSnapshot(pack.Manifest.PackId, pack.Manifest.PackVersion, pack.Manifest.TargetEngineApi));
            packHashLines.Add((pack.Manifest.PackId, pack.PackSha256));

            foreach (string capability in pack.Manifest.Capabilities)
                capabilities.Add(capability);
            foreach (ICapabilityContribution contribution in pack.Capabilities)
            {
                if (contribution is null)
                {
                    collected.Add("包 " + pack.Manifest.PackId + " 的能力贡献列表含 null 元素。");
                    continue;
                }
                try
                {
                    if (contribution.CapabilityIds is null)
                        continue; // 规则校验已拒载此类包；此处防御性跳过
                    foreach (string capability in contribution.CapabilityIds)
                        capabilities.Add(capability);
                }
                catch (Exception exception)
                {
                    collected.Add("包 " + pack.Manifest.PackId + " 能力贡献 '" + DescribeContribution(contribution)
                        + "' CapabilityIds 回放抛出异常：" + exception.Message);
                }
            }
            foreach (IPolicyContribution contribution in pack.Policies)
            {
                if (contribution is null)
                {
                    collected.Add("包 " + pack.Manifest.PackId + " 的策略贡献列表含 null 元素。");
                    continue;
                }
                try
                {
                    if (contribution.Policies is null)
                        continue;
                    foreach (EnginePolicyBinding binding in contribution.Policies)
                        capabilities.Add(binding.CapabilityId);
                }
                catch (Exception exception)
                {
                    collected.Add("包 " + pack.Manifest.PackId + " 策略贡献 '" + DescribeContribution(contribution)
                        + "' Policies 回放抛出异常：" + exception.Message);
                }
            }

            var recorder = new AssemblySurfaceRecorder(collected, pack.Manifest.PackId);
            foreach (ISurfaceContribution contribution in pack.Surface)
            {
                if (contribution is null)
                {
                    collected.Add("包 " + pack.Manifest.PackId + " 的表面贡献列表含 null 元素。");
                    continue;
                }
                try
                {
                    contribution.Apply(recorder.Instructions, recorder.Functions);
                }
                catch (Exception exception)
                {
                    collected.Add("包 " + pack.Manifest.PackId + " 表面贡献 '" + DescribeContribution(contribution)
                        + "' Apply 回放抛出异常：" + exception.Message);
                }
            }

            foreach (var registered in recorder.RegisteredInstructions)
            {
                if (addedInstructions.TryGetValue(registered.Key, out var owner))
                    collected.Add("指令 " + registered.Key + " 被多个包注册：" + owner.PackId + " 与 " + pack.Manifest.PackId + "。");
                else
                    addedInstructions[registered.Key] = (pack.Manifest.PackId, registered.Key);
            }
            foreach (var entry in recorder.RegisteredFunctions)
            {
                if (addedFunctions.TryGetValue(entry.Key, out var owner))
                    collected.Add("函数 " + entry.Key + " 被多个包注册：" + owner.PackId + " 与 " + pack.Manifest.PackId + "。");
                else
                    addedFunctions[entry.Key] = (pack.Manifest.PackId, entry.Key, entry.Value);
            }
            foreach (string name in recorder.HiddenInstructions)
                removedInstructions.Add(name);
            foreach (string name in recorder.HiddenFunctions)
                removedFunctions.Add(name);
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
            // 函数名镜像声明进指令面：引擎把可见的表达式函数投影为 METHOD 指令进入解析器
            // 指令注册表（语句语法，FunctionIdentifier.GetInstructionNameDic），而会话校验
            //（LegacyCompatibilityPlanConsumption）要求注册表里每个名字都被计划的**指令侧**
            // 声明——内置方言的生成清单即双侧声明（如 snake 的 SQL_CONNECT 同时在指令/函数
            // 清单内）。包只声明函数侧时引擎投影仍会发生，故此处无条件镜像补齐指令侧声明；
            // 计划多出的声明被既有语义容忍（VARI/VARS 条件可见性先例），不与包的指令注册
            // 混淆（ContainsKey 保留已有指令描述符的变体语义），也不进差量哈希（派生数据）。
            // 同名缝隙语义（hide 指令 X + register 函数 X）：镜像会把刚被 Remove 的 X 复活回
            // 指令面——这是引擎真实行为的忠实反映（语句/函数双形态共享键，函数可见即投影
            // 同名 METHOD 指令；内置先例 v24 的 BITMAP_CACHE_ENABLE：隐藏 snake 语句形态 +
            // METHOD 投影函数形态并存），非规则漂移，不拒载。
            if (!instructions.ContainsKey(entry.Name))
                instructions[entry.Name] = new InstructionDescriptor(entry.Name, "compatpack", entry.PackId, VmCompletionMode.CoreImmediate);
        }
        if (collected.Count > 0)
        {
            errors = collected;
            return false;
        }

        var modules = baseline.Dialect.Modules.ToList();
        // 模块快照按 packId 固化排序：组装产出的计划数据与包传入顺序无关（与哈希同纪律）。
        modules.AddRange(packModules.OrderBy(module => module.ModuleId, StringComparer.Ordinal));

        var variantSelections = CollectVariantSelections(packs, collected);
        if (collected.Count > 0)
        {
            errors = collected;
            return false;
        }

        string dialectHash = ComputeDialectDeltaHash(baseline, packHashLines, addedInstructions, addedFunctions, removedInstructions, removedFunctions, variantSelections);
        var dialect = new DialectPlan(modules, baseline.Dialect.Ports, instructions, functions, dialectHash);
        string canonicalHash = ComputePlanEnvelopeHash(baseline, dialectHash, capabilities);
        assembled = new CompatibilityPlan(baseline.ProfileId, dialect, capabilities, baseline.SaveProfileId, canonicalHash);
        errors = collected;
        return true;
    }

    /// <summary>
    /// 错误文案兜底：贡献 id 本身也是包作者代码，取 id 抛异常时降级为占位描述——
    /// 保证组装失败路径（构建错误条目时）不会再抛，fail-closed 止步于"返回 false + 错误清单"。
    /// </summary>
    static string DescribeContribution(ICompatPackContribution contribution)
    {
        try
        {
            return contribution.ContributionId ?? "";
        }
        catch (Exception exception)
        {
            return "<贡献 id 访问抛异常：" + exception.Message + ">";
        }
    }

    static IReadOnlyDictionary<string, string> CollectVariantSelections(IReadOnlyList<CompatPackHandle> packs, List<string> errors)
    {
        // 同键同值幂等放行；同键不同值是跨包冲突，收集错误整体拒载——不做静默仲裁
        // （fail-closed；否则包排列顺序会影响组装哈希，破坏「同启用集同哈希」）。
        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CompatPackHandle pack in packs)
        {
            foreach (KeyValuePair<string, string> selection in pack.Manifest.VariantSelections)
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

    static string ComputePlanEnvelopeHash(CompatibilityPlan baseline, string dialectHash, IEnumerable<string> capabilities)
    {
        var canonical = new StringBuilder()
            .Append("profile=").Append(baseline.ProfileId).Append('\n')
            .Append("dialect=").Append(dialectHash).Append('\n')
            .Append("save=").Append(baseline.SaveProfileId ?? string.Empty).Append('\n');
        foreach (string capability in capabilities)
            canonical.Append("capability=").Append(capability).Append('\n');
        return Hash(canonical.ToString());
    }

    static string Hash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>组装期表面回放录制器：与校验期录制同语义，但把注册项归到包 id 名下。</summary>
    sealed class AssemblySurfaceRecorder
    {
        readonly List<string> errors;
        readonly string packId;

        public AssemblySurfaceRecorder(List<string> errors, string packId)
        {
            this.errors = errors;
            this.packId = packId;
        }

        public IInstructionSurfaceRegistry Instructions => new InstructionRecorder(this);
        public IFunctionSurfaceRegistry Functions => new FunctionRecorder(this);

        public Dictionary<string, string> RegisteredInstructions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> RegisteredFunctions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> HiddenInstructions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> HiddenFunctions { get; } = new(StringComparer.Ordinal);

        string Normalize(string name)
        {
            string normalized = (name ?? "").Trim().ToUpperInvariant();
            if (normalized.Length == 0)
                errors.Add("包 " + packId + " 表面贡献存在空名。");
            return normalized;
        }

        sealed class InstructionRecorder : IInstructionSurfaceRegistry
        {
            readonly AssemblySurfaceRecorder owner;
            public InstructionRecorder(AssemblySurfaceRecorder owner) => this.owner = owner;

            public void RegisterInstruction(string name)
            {
                string normalized = owner.Normalize(name);
                if (normalized.Length > 0)
                    owner.RegisteredInstructions[normalized] = owner.packId;
            }

            public void HideInstruction(string name)
            {
                string normalized = owner.Normalize(name);
                if (normalized.Length > 0)
                    owner.HiddenInstructions.Add(normalized);
            }
        }

        sealed class FunctionRecorder : IFunctionSurfaceRegistry
        {
            readonly AssemblySurfaceRecorder owner;
            public FunctionRecorder(AssemblySurfaceRecorder owner) => this.owner = owner;

            public void RegisterFunction(string name, string returnType)
            {
                string normalized = owner.Normalize(name);
                if (normalized.Length > 0)
                    owner.RegisteredFunctions[normalized] = string.IsNullOrWhiteSpace(returnType) ? "Unknown" : returnType;
            }

            public void HideFunction(string name)
            {
                string normalized = owner.Normalize(name);
                if (normalized.Length > 0)
                    owner.HiddenFunctions.Add(normalized);
            }
        }
    }
}
