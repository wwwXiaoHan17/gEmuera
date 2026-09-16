using System;
using System.Collections.Generic;

namespace Emuera.Compatibility.Packs
{
	/// <summary>
	/// 一个兼容包程序集的入口契约。加载器在包程序集内按类型名发现唯一的 ICompatPack 实现类；
	/// 程序集必须同时内嵌 <c>compatpack.manifest.json</c>（见 <see cref="CompatPackManifest"/>），
	/// 缺清单的程序集不是包（fail-closed）。
	/// 设计文档：docs/designs/compat-pack-interface.md §4。
	/// </summary>
	public interface ICompatPack
	{
		/// <summary>解析自内嵌资源的清单。实现类应返回加载器传入/读取的同一实例。</summary>
		CompatPackManifest Manifest { get; }

		/// <summary>本包全部贡献；贡献 id 在包内必须唯一（加载器校验）。</summary>
		IReadOnlyList<ICompatPackContribution> Contributions { get; }
	}

	/// <summary>包贡献的公共基面。贡献 id 在单个包内唯一。</summary>
	public interface ICompatPackContribution
	{
		string ContributionId { get; }
	}

	/// <summary>
	/// 表面贡献（纯数据）：向会话表面注册/隐藏指令与表达式函数。名单数据能走 manifest 的
	/// 优先走 manifest（大多数包只需要 manifest）；本接口只为运行期动态生成的名单保留。
	/// 注册表接口由宿主桥接到 Core 的 InstructionRegistryBuilder/FunctionRegistryBuilder。
	/// </summary>
	public interface ISurfaceContribution : ICompatPackContribution
	{
		void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions);
	}

	/// <summary>指令表面注册表桥（宿主实现；指令名由宿主按 Trim+Upper 规范化）。</summary>
	public interface IInstructionSurfaceRegistry
	{
		void RegisterInstruction(string name);
		void HideInstruction(string name);
	}

	/// <summary>表达式函数表面注册表桥（宿主实现；函数名跟随引擎注册表 comparer 语义）。</summary>
	public interface IFunctionSurfaceRegistry
	{
		void RegisterFunction(string name, string returnType);
		void HideFunction(string name);
	}

	/// <summary>
	/// 能力声明贡献：capability id 进会话账本。实现（算法）由引擎能力实现库按 id 解析；
	/// 未知 id 由加载器拒载（fail-closed），本接口不携带代码。
	/// </summary>
	public interface ICapabilityContribution : ICompatPackContribution
	{
		IReadOnlyList<string> CapabilityIds { get; }
	}
}
