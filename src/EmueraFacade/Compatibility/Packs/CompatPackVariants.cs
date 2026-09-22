using System;
using System.Collections.Generic;

namespace Emuera.Compatibility.Packs
{
	/// <summary>
	/// 变体贡献（代码）：同名指令在不同方言沿用名字但更换文法/handler。取代引擎侧 closed enum
	/// LegacyInstructionVariant 的开放注册——内置变体经 manifest 的 <c>variantSelections</c>
	/// 以 <c>builtin:*</c> 名字选择；包自带的新变体经本贡献提供工厂。
	/// v1 未接线（v2 接线预留）：宿主尚不消费本贡献，携带它的包在加载期被拒载（fail-closed，
	/// 不会静默 no-op）；v1 的变体选择只能走 manifest 的 variantSelections/builtin 变体。
	/// </summary>
	public interface IInstructionVariantContribution : ICompatPackContribution
	{
		IReadOnlyList<InstructionVariantBinding> Bindings { get; }
	}

	/// <summary>一条变体绑定：指令名 → 变体工厂。指令名由宿主按 Trim+Upper 规范化。</summary>
	public sealed record InstructionVariantBinding(string InstructionName, ICompatInstructionFactory Factory);

	/// <summary>
	/// 变体指令工厂。契约程序集不见引擎内部类型（AbstractInstruction），因此返回 object，
	/// 由宿主桥收窄；收窄失败 = 拒载并报告包作者（fail-closed），不做静默回退。
	/// </summary>
	public interface ICompatInstructionFactory
	{
		object CreateInstruction();
	}

	/// <summary>
	/// 策略贡献（窄逃生舱）：仅当 capability id 在引擎能力实现库中无内置实现时使用
	/// （社区新 quirk）。策略窄接口集合随加载器增量逐个提炼（设计文档 §4 留白处），
	/// 定稿前本接口只建立绑定骨架。
	/// v1 未接线（v2 接线预留）：宿主尚不消费策略贡献，携带它的包在加载期被拒载
	/// （fail-closed，不会静默 no-op）；v1 的能力声明只能走 manifest 的 capabilities。
	/// </summary>
	public interface IPolicyContribution : ICompatPackContribution
	{
		IReadOnlyList<EnginePolicyBinding> Policies { get; }
	}

	/// <summary>策略绑定：capability id → 包提供的实现标记（窄接口定稿时扩展实现载体）。</summary>
	public sealed record EnginePolicyBinding(string CapabilityId);
}
