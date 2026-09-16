using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 兼容包语义校验的纯规则集（fail-closed，错误全量收集）。公开为静态方法便于直接单测：
/// 加载器在程序集发现/清单解析之后调用；测试可用手工构造的清单与贡献直接驱动。
/// 对应 docs/designs/compat-pack-interface.md §5.3 三原则中的（1）未知 capability 拒载、
/// （3）targetEngineApi 不匹配拒载，以及 Core 数据可支撑的（2）表面对账子集；
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

        // 原则（3）：引擎包 API 主版本不匹配拒载（当前为整数版本，精确匹配即主版本匹配）。
        if (manifest.TargetEngineApi != context.EngineModuleApiVersion)
        {
            collected.Add("targetEngineApi=" + manifest.TargetEngineApi + " 与引擎包 API 版本 "
                + context.EngineModuleApiVersion + " 不匹配，包需按新 API 重编译后分发。");
        }

        // 原则（1）：manifest 声明的 capability 必须在引擎词汇表内。
        foreach (string capability in manifest.Capabilities)
        {
            if (!context.KnownCapabilityIds.Contains(capability))
                collected.Add("清单声明未知 capability id：" + capability);
        }

        // 贡献 id 包内唯一。
        var contributionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ICompatPackContribution contribution in contributions)
        {
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
            switch (contribution)
            {
                case ICapabilityContribution capability:
                    foreach (string id in capability.CapabilityIds)
                    {
                        if (!context.KnownCapabilityIds.Contains(id))
                            collected.Add("贡献 '" + contribution.ContributionId + "' 声明未知 capability id：" + id);
                    }
                    break;
                case IPolicyContribution policy:
                    foreach (EnginePolicyBinding binding in policy.Policies)
                    {
                        if (!context.KnownCapabilityIds.Contains(binding.CapabilityId))
                            collected.Add("策略贡献 '" + contribution.ContributionId + "' 绑定未知 capability id：" + binding.CapabilityId);
                    }
                    break;
                case IInstructionVariantContribution variant:
                    foreach (InstructionVariantBinding binding in variant.Bindings)
                    {
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

        // 变体选择对账：清单 variantSelections 的 builtin 值必须在内置变体名录；
        // 非 builtin 值 v1 不支持（自带变体经 IInstructionVariantContribution 绑定隐式生效）；
        // 选择键若同时被变体贡献绑定 → 冲突拒载。
        foreach (KeyValuePair<string, string> selection in manifest.VariantSelections)
        {
            if (!selection.Value.StartsWith("builtin:", StringComparison.Ordinal))
            {
                collected.Add("变体选择 " + selection.Key + " 的值不是 builtin:*（v1 自带变体经贡献绑定隐式生效）：" + selection.Value);
                continue;
            }
            if (!context.KnownBuiltinVariantNames.Contains(selection.Value))
                collected.Add("变体选择 " + selection.Key + " 引用未知内置变体：" + selection.Value);
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
        }
        foreach (string registered in recorder.RegisteredFunctions)
        {
            if (context.BaselineFunctions.Contains(registered))
                collected.Add("注册函数与 v24 基线同名：" + registered);
        }

        errors = collected;
        return collected.Count == 0;
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
