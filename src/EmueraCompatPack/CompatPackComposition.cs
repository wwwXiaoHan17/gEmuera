#nullable enable
using System;

namespace Emuera.Compatibility.Packs
{
	/// <summary>
	/// 清单声明的包依赖（requires.packs[]）：运行期组合解析必须按 <see cref="VersionRange"/>
	/// 校验依赖包的实际 packVersion。版本范围语法与引擎 <c>SemanticVersionRange</c> 等价：
	/// 精确 SemVer（x.y.z）或区间 [min,max) / (min,max] / [min,max] / (min,max)。
	/// 结构校验（非空、语法、重复 packId、禁止自依赖）在 <see cref="CompatPackManifest"/>/
	/// 规则层完成；依赖闭包、缺失、版本不满足与环检测在 Core 组合解析器完成。
	/// </summary>
	public sealed record CompatPackDependency(string PackId, string VersionRange);

	/// <summary>
	/// 清单声明的显式覆盖（overrides[]）：仅当同一符号被多个包以不同最终状态声明时生效，
	/// 且只有显式声明本覆盖的包可以胜出；多个覆盖取 priority 唯一最高者，同 priority 拒载。
	/// <see cref="Symbol"/> 为契约字符串（instruction:/function:/variant: 前缀 + 规范化名字）；
	/// 覆盖不能改写 v24 基线 handler，也不能绕过 conflicts 声明（冲突永远拒载）。
	/// </summary>
	public sealed record CompatPackOverride(string Symbol, int Priority);
}
