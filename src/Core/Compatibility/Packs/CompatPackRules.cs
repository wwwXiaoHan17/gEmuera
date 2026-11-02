using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 兼容包语义校验的纯规则集（fail-closed，错误全量收集）。公开为静态方法便于直接单测：
/// 加载器在程序集发现/清单解析之后调用；测试可用手工构造的清单与贡献直接驱动。
/// 对应 docs/designs/compat-pack-interface.md §5.3 三原则中的（1）未知 capability 拒载、
/// （3）contractAbi/engineApi 双轴版本不匹配拒载，以及 Core 数据可支撑的（2）表面对账子集；
/// 引擎注册表级对账（add 名单须有真实 handler）在 DOD-3 宿主接线叠加。
/// </summary>
public static class CompatPackRules
{
    /// <summary>清单与贡献的完整语义校验。返回 false 时 errors 非空，调用方应拒载。</summary>
    public static bool Validate(
        CompatPackManifest manifest,
        IReadOnlyList<ICompatPackContribution> contributions,
        CompatPackValidationContext context,
        out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(context);
        var collected = new List<string>();

        // 保留名：packId 撞内置方言模块 id 即拒载（否则组装后引擎闭包校验会以未捕获
        // 异常炸掉会话绑定，违反降级不变量——必须在加载/校验段挡下）。
        if (context.ReservedModuleIds.Contains(manifest.PackId))
            collected.Add("packId 与内置方言模块保留名冲突：" + manifest.PackId);

        // v1 契约面硬约束：包只允许叠加 v24pure。即便宿主会话基线恰好同名，非 v24pure 的
        // 声明也必须拒载——v1 没有承诺任何非 v24pure 基线的叠加语义。
        if (!string.Equals(manifest.BaseProfileId, "v24pure", StringComparison.Ordinal))
        {
            collected.Add("包 " + manifest.PackId + " 声明 baseProfileId=" + manifest.BaseProfileId
                + "，但 v1 只允许 v24pure（其他基线需 v2 契约面支持后开放）。");
        }

        // 包声明的是相对哪个基线 profile 的差量；若与实际会话基线不一致，加载期拒载，
        // 避免在 v18/snake 等基线上静默错叠。
        if (!string.Equals(manifest.BaseProfileId, context.BaselineProfileId, StringComparison.Ordinal))
        {
            collected.Add("包声明 baseProfileId=" + manifest.BaseProfileId
                + " 与当前会话基线 " + context.BaselineProfileId + " 不一致（v1 包只允许叠加 v24pure 基线）。");
        }

        // baseSurfaceHash 精确对账（E2-R6）：manifest 声明非空 baseSurfaceHash 时必须等于
        // 当前 v24 基线表面快照（<see cref="CompatPackValidationContext.BaselineSurfaceHash"/>，
        // 生成清单的规范化 SHA256）——包声称"基于某表面"而引擎表面已漂移时，其 add/hide 差量
        // 前提即失效，静默错叠比拒载更危险。不声明（null，可选字段）不触发本规则。
        if (manifest.BaseSurfaceHash is not null
            && !string.Equals(manifest.BaseSurfaceHash, context.BaselineSurfaceHash, StringComparison.Ordinal))
        {
            collected.Add("包 " + manifest.PackId + " 的 baseSurfaceHash 与当前 v24 表面快照不一致（声明 "
                + manifest.BaseSurfaceHash + "，当前基线 " + context.BaselineSurfaceHash
                + "；包需按新表面快照重发布，或由维护方再生生成清单后更新声明）。");
        }

        // 原则（3）双轴版本比较：两轴都是 major 相等且包 minor <= 引擎 minor（缺 API 就是缺 API，
        // 不能因"引擎更新向下兼容"放宽）。两轴独立判定、独立文案——包作者必须能看出是
        // contract（公开契约面）还是 engine（引擎行为/能力契约）不满足。
        if (!manifest.ContractAbi.CanRunOn(context.CurrentContractAbi))
        {
            collected.Add("包 " + manifest.PackId + " 的 contractAbi=" + manifest.ContractAbi
                + " 不能在引擎 contractAbi=" + context.CurrentContractAbi
                + " 上运行（contract 轴要求 major 相等且包 minor <= 引擎 minor），包需按新契约面重编译后分发。");
        }
        if (!manifest.EngineApi.CanRunOn(context.CurrentEngineApi))
        {
            collected.Add("包 " + manifest.PackId + " 的 engineApi=" + manifest.EngineApi
                + " 不能在引擎 engineApi=" + context.CurrentEngineApi
                + " 上运行（engine 轴要求 major 相等且包 minor <= 引擎 minor），包需按新引擎能力重编译后分发。");
        }

        // saveProfileId v1 未接线：legacy 存档路径尚不消费 CompatibilityPlan.SaveProfileId，
        // 声明后只会进 plan 元数据/哈希而不改变真实存档格式。按死契约纪律显式拒载，
        // 避免"看起来生效"的静默误导；v2 宿主接线后删除本条即可恢复。
        if (manifest.SaveProfileId is not null)
        {
            collected.Add("包 " + manifest.PackId + " 声明 saveProfileId=" + manifest.SaveProfileId
                + "，但 v1 legacy 存档路径尚不消费 plan.SaveProfileId（v2 预留）；携带即拒载。");
        }

        // 原则（1）：manifest 声明的 capability 必须在引擎 catalog 内且为 Wired；
        // 已知 Wired 集合稍后统一做 Requires/Implies/ConflictsWith 闭包。
        var requestedCapabilities = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string capability in manifest.Capabilities)
            ValidateCapability(context, capability, "清单", requestedCapabilities, collected);

        // 贡献 id 包内唯一。贡献列表与各成员集合都来自包作者代码，null 一律收集错误而非抛出
        // （fail-closed：加载器的任何输入都不允许异常逃逸）。
        var contributionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ICompatPackContribution contribution in contributions)
        {
            if (contribution is null)
            {
                collected.Add("贡献列表含 null 元素。");
                continue;
            }
            string id = contribution.ContributionId ?? "";
            if (id.Trim().Length == 0)
            {
                collected.Add("存在空贡献 id。");
                continue;
            }
            if (!contributionIds.Add(id))
                collected.Add("贡献 id 重复：" + id);
        }

        var variantBindings = new Dictionary<string, string>(StringComparer.Ordinal); // 规范化指令 → 贡献 id（定位用）
        foreach (ICompatPackContribution contribution in contributions)
        {
            if (contribution is null)
                continue;
            switch (contribution)
            {
                case ICapabilityContribution capability:
                    if (capability.CapabilityIds is null)
                    {
                        collected.Add("贡献 '" + contribution.ContributionId + "' 的 CapabilityIds 为 null。");
                        break;
                    }
                    foreach (string id in capability.CapabilityIds)
                    {
                        ValidateCapability(
                            context,
                            id,
                            "贡献 '" + contribution.ContributionId + "'",
                            requestedCapabilities,
                            collected);
                    }
                    break;
                case IPolicyContribution policy:
                    // v1 fail-closed 拒载（用户裁定 2026-09-17，评审 P2-6）：策略贡献的宿主
                    // 消费链未接线，按文档写策略贡献只会得到静默 no-op——加载期明确拒绝并
                    // 指引 manifest 通道；v2 接线后移除此条恢复放行。下方结构校验照常执行
                    //（错误全量收集，便于包作者一次看全问题）。
                    collected.Add("包 " + manifest.PackId + " 策略贡献 '" + contribution.ContributionId
                        + "' v1 未接线（v2 预留）：策略贡献宿主尚不消费，请仅使用 manifest 的"
                        + " variantSelections/builtin 变体与 capabilities 声明。");
                    if (policy.Policies is null)
                    {
                        collected.Add("策略贡献 '" + contribution.ContributionId + "' 的 Policies 为 null。");
                        break;
                    }
                    foreach (EnginePolicyBinding binding in policy.Policies)
                    {
                        if (binding is null)
                        {
                            collected.Add("策略贡献 '" + contribution.ContributionId + "' 含 null 绑定。");
                            continue;
                        }
                        ValidateCapability(
                            context,
                            binding.CapabilityId,
                            "策略贡献 '" + contribution.ContributionId + "'",
                            requestedCapabilities,
                            collected);
                    }
                    break;
                case IInstructionVariantContribution variant:
                    // v1 fail-closed 拒载（用户裁定 2026-09-17，评审 P2-6）：自带变体工厂的
                    // 宿主接线桥未实现（宿主只消费 manifest 的 builtin:* 变体选择），按文档写
                    // 自定义变体会静默 no-op——加载期明确拒绝并指引 manifest 通道；v2 接线后
                    // 移除此条恢复放行。结构校验照常执行（错误全量收集）。
                    collected.Add("包 " + manifest.PackId + " 变体贡献 '" + contribution.ContributionId
                        + "' v1 未接线（v2 预留）：自带变体工厂宿主尚不消费，请仅使用 manifest 的"
                        + " variantSelections/builtin 变体。");
                    if (variant.Bindings is null)
                    {
                        collected.Add("变体贡献 '" + contribution.ContributionId + "' 的 Bindings 为 null。");
                        break;
                    }
                    foreach (InstructionVariantBinding binding in variant.Bindings)
                    {
                        if (binding is null)
                        {
                            collected.Add("变体贡献 '" + contribution.ContributionId + "' 含 null 绑定。");
                            continue;
                        }
                        string instruction = (binding.InstructionName ?? "").Trim().ToUpperInvariant();
                        if (instruction.Length == 0)
                        {
                            collected.Add("变体贡献 '" + contribution.ContributionId + "' 存在空指令名绑定。");
                            continue;
                        }
                        if (binding.Factory is null)
                        {
                            collected.Add("变体贡献 '" + contribution.ContributionId + "' 的 " + instruction + " 绑定工厂为空。");
                            continue;
                        }
                        if (!variantBindings.TryAdd(instruction, contribution.ContributionId ?? ""))
                            collected.Add("指令 " + instruction + " 被多个变体贡献重复绑定。");
                    }
                    break;
                case ISurfaceContribution:
                    // 表面对账在下方录制回放中做。
                    break;
            }
        }

        // capability 闭包（设计 §6.3）：Requires 递归存在且 Wired、Implies 自动加入、
        // ConflictsWith 任一出现即拒载。只对上方已验证为已知 Wired 的集合解析——未知/
        // DiagnosticOnly 已逐项收集，避免重复噪音；闭包失败同样 fail-closed。
        if (!EngineCapabilityCatalog.TryResolveForPack(
                context.Capabilities,
                requestedCapabilities,
                out _,
                out IReadOnlyList<string> closureErrors))
        {
            collected.AddRange(closureErrors);
        }

        // 变体选择对账：清单 variantSelections 的 builtin 值必须在内置变体名录；
        // 非 builtin 值 v1 不支持（自带变体贡献 v1 未接线、携带即拒载，v2 预留）；
        // 选择键若同时被变体贡献绑定 → 冲突拒载。
        foreach (KeyValuePair<string, string> selection in manifest.VariantSelections)
        {
            if (!selection.Value.StartsWith("builtin:", StringComparison.Ordinal))
            {
                collected.Add("变体选择 " + selection.Key + " 的值不是 builtin:*（v1 仅支持内置变体；自带变体贡献 v1 未接线、携带即拒载，v2 预留）：" + selection.Value);
                continue;
            }
            if (!context.KnownBuiltinVariantNames.Contains(selection.Value))
            {
                collected.Add("变体选择 " + selection.Key + " 引用未知内置变体：" + selection.Value);
                continue;
            }
            // 组合级校验（宿主传入组合表时）：内置变体只对注册过 handler 变体的指令有效。
            if (context.KnownBuiltinVariantInstructions.Count > 0
                && (!context.KnownBuiltinVariantInstructions.TryGetValue(selection.Value, out var supported)
                    || !supported.Contains(selection.Key)))
            {
                collected.Add("内置变体 " + selection.Value + " 未注册指令 " + selection.Key + " 的 handler 变体。");
            }
            if (variantBindings.ContainsKey(selection.Key))
                collected.Add("指令 " + selection.Key + " 同时出现在清单变体选择与变体贡献绑定中。");
        }

        // 表面贡献对账：录制回放 ISurfaceContribution.Apply。
        var recorder = new RecordingSurfaceRegistry(collected);
        foreach (ICompatPackContribution contribution in contributions)
        {
            if (contribution is not ISurfaceContribution surface)
                continue;
            try
            {
                surface.Apply(recorder.Instructions, recorder.Functions);
            }
            catch (Exception exception)
            {
                collected.Add("表面贡献 '" + contribution.ContributionId + "' Apply 抛出异常：" + exception.Message);
            }
        }

        // hide 必须落在 v24 基线内（隐藏基线没有的名字没有意义，多为拼写错误）；
        // register 不得与 v24 基线同名（改既有指令语义必须走变体通道）。
        foreach (string hidden in recorder.HiddenInstructions)
        {
            if (!context.BaselineInstructions.Contains(hidden))
                collected.Add("隐藏指令不在 v24 基线内：" + hidden);
        }
        foreach (string hidden in recorder.HiddenFunctions)
        {
            if (!context.BaselineFunctions.Contains(hidden))
                collected.Add("隐藏函数不在 v24 基线内：" + hidden);
        }
        foreach (string registered in recorder.RegisteredInstructions)
        {
            if (context.BaselineInstructions.Contains(registered))
                collected.Add("注册指令与 v24 基线同名（应走变体选择通道）：" + registered);
            // 引擎 handler 对账（宿主传入全集时启用）：注册名必须有真实 handler，否则
            // 名字会穿过全部校验进入 plan 哈希、却在会话注册表投影时静默无 handler 可绑
            // （"未知指令"运行期报错），违背 fail-at-load。
            else if (context.KnownInstructionHandlers.Count > 0
                && !context.KnownInstructionHandlers.Contains(registered))
                collected.Add("包 " + manifest.PackId + " 注册指令 " + registered
                    + " 无引擎 handler（指令名拼错或引擎未收录；内置模块可声明的名字均在全集内）。");
        }
        foreach (string registered in recorder.RegisteredFunctions)
        {
            if (context.BaselineFunctions.Contains(registered))
                collected.Add("注册函数与 v24 基线同名：" + registered);
            else if (context.KnownFunctionHandlers.Count > 0
                && !context.KnownFunctionHandlers.Contains(registered))
                collected.Add("包 " + manifest.PackId + " 注册函数 " + registered
                    + " 无引擎 handler（函数名拼错或引擎未收录；内置模块可声明的名字均在全集内）。");
            else if (context.KnownFunctionReturnTypes.Count > 0
                && !context.KnownFunctionReturnTypes.ContainsKey(registered))
                collected.Add("包 " + manifest.PackId + " 注册函数 " + registered
                    + " 缺少引擎返回类型元数据（manifest-only 表面需要宿主词汇表包含该函数）。");
        }

        // 交叉对账：表面动作 × 变体声明。同一指令的 handler 来源必须无歧义——
        // 变体绑定的指令不能同时被隐藏（隐藏后无从触发变体）或被表面注册（注册即自带 handler 来源）；
        // 清单变体选择的指令必须在 v24 基线内（内置变体只存在于基线名）且不能同时被隐藏。
        foreach (KeyValuePair<string, string> binding in variantBindings)
        {
            if (recorder.HiddenInstructions.Contains(binding.Key))
                collected.Add("指令 " + binding.Key + " 同时被隐藏与变体绑定。");
            if (recorder.RegisteredInstructions.Contains(binding.Key))
                collected.Add("指令 " + binding.Key + " 同时被表面注册与变体绑定（handler 来源歧义）。");
        }
        foreach (KeyValuePair<string, string> selection in manifest.VariantSelections)
        {
            if (!context.BaselineInstructions.Contains(selection.Key))
                collected.Add("变体选择的指令不在 v24 基线内：" + selection.Key);
            if (recorder.HiddenInstructions.Contains(selection.Key))
                collected.Add("指令 " + selection.Key + " 同时被隐藏与清单变体选择。");
        }

        errors = collected;
        return collected.Count == 0;
    }

    /// <summary>
    /// 单个 capability 声明的词汇/接线校验：必须存在于 engine catalog 且为 Wired，
    /// 未知或 DiagnosticOnly 一律拒载（fail-at-load，避免包声明"看起来生效"的 no-op）。
    /// 已知 Wired 项收集进 <paramref name="requestedCapabilities"/> 供闭包解析。
    /// </summary>
    static void ValidateCapability(
        CompatPackValidationContext context,
        string? capabilityId,
        string owner,
        ISet<string> requestedCapabilities,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(capabilityId))
        {
            errors.Add(owner + " 声明空 capability id。");
            return;
        }
        if (!context.Capabilities.TryGetValue(capabilityId, out CapabilityDescriptor? descriptor))
        {
            errors.Add(owner + " 声明未知 capability id：" + capabilityId);
            return;
        }
        if (descriptor.Status != CapabilityStatus.Wired)
        {
            errors.Add(owner + " 声明的 capability " + capabilityId
                + " 为 DiagnosticOnly（无真实引擎行为消费者），不能进入包的激活集合。");
            return;
        }
        requestedCapabilities.Add(capabilityId);
    }

    /// <summary>录制型注册表桥：校验期 dry-run 捕获表面贡献的全部调用。</summary>
    sealed class RecordingSurfaceRegistry
    {
        readonly List<string> errors;

        public RecordingSurfaceRegistry(List<string> errors)
        {
            this.errors = errors;
        }

        public IInstructionSurfaceRegistry Instructions => new InstructionRegistry(this);
        public IFunctionSurfaceRegistry Functions => new FunctionRegistry(this);

        public HashSet<string> RegisteredInstructions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> HiddenInstructions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> RegisteredFunctions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> HiddenFunctions { get; } = new(StringComparer.Ordinal);

        string Normalize(string name, string kind)
        {
            // 规范化语义契约（与引擎侧对齐的依据）：引擎 IsFunctionVisible 按 Trim 原样
            // （Ordinal、大小写敏感）比对，但引擎函数注册表（FunctionMethodCreator.methodList）
            // 的键全部为「Trim().ToUpperInvariant() 不变」的形态——纯大写 ASCII 或无大小写
            // CJK（陥落状態/陷落状态，见生成清单 LegacyDialectInventories 的函数名集）。
            // 因此本处的 Trim+ToUpper 规范化对真实注册表逐名等价（差异被全大写现状掩盖）；
            // 该假设由 CompatPackRulesTests 依生成清单钉住——未来注册表引入非大写函数名时
            // 该测试先红，须重新审视此规范化而非引擎侧语义。
            string normalized = (name ?? "").Trim().ToUpperInvariant();
            if (normalized.Length == 0)
                errors.Add("表面贡献存在空" + kind + "名。");
            return normalized;
        }

        sealed class InstructionRegistry : IInstructionSurfaceRegistry
        {
            readonly RecordingSurfaceRegistry owner;
            public InstructionRegistry(RecordingSurfaceRegistry owner) => this.owner = owner;

            public void RegisterInstruction(string name)
            {
                string normalized = owner.Normalize(name, "指令");
                if (normalized.Length > 0 && !owner.RegisteredInstructions.Add(normalized))
                    owner.errors.Add("重复注册指令：" + normalized);
            }

            public void HideInstruction(string name)
            {
                string normalized = owner.Normalize(name, "指令");
                if (normalized.Length > 0 && !owner.HiddenInstructions.Add(normalized))
                    owner.errors.Add("重复隐藏指令：" + normalized);
            }
        }

        sealed class FunctionRegistry : IFunctionSurfaceRegistry
        {
            readonly RecordingSurfaceRegistry owner;
            public FunctionRegistry(RecordingSurfaceRegistry owner) => this.owner = owner;

            public void RegisterFunction(string name, string returnType)
            {
                string normalized = owner.Normalize(name, "函数");
                if (normalized.Length > 0)
                {
                    if (!owner.RegisteredFunctions.Add(normalized))
                        owner.errors.Add("重复注册函数：" + normalized);
                    if (string.IsNullOrWhiteSpace(returnType))
                        owner.errors.Add("注册函数 " + normalized + " 缺少返回类型。");
                }
            }

            public void HideFunction(string name)
            {
                string normalized = owner.Normalize(name, "函数");
                if (normalized.Length > 0 && !owner.HiddenFunctions.Add(normalized))
                    owner.errors.Add("重复隐藏函数：" + normalized);
            }
        }
    }
}
