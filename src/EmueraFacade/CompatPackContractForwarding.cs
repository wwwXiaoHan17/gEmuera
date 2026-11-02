using System.Runtime.CompilerServices;

// CompatPack 契约程序集拆分（2026-10-03，docs/plans/2026-10-03-compatpack-contract-assembly-split-adr.md）：
// 全部契约类型已迁至独立契约程序集 Emuera.CompatPack（src/EmueraCompatPack）。本程序集
// （AssemblyName=Emuera, 1.824.0.0）只承载上游插件 ABI（MinorShift.Emuera.Runtime.Utils.PluginSystem）。
// 下列 TypeForwardedTo 让拆分前编译的包（AssemblyRef=Emuera）在运行期把契约类型绑定链到
// Emuera.CompatPack 的同一实例——数据包与 v1 入口类包的兼容窗口为无限期（CLR 元数据机制，
// 无弃用时钟）。类型命名空间 Emuera.Compatibility.Packs 保持不变。

[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.ICompatPack))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.ICompatPackContribution))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.ISurfaceContribution))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.IInstructionSurfaceRegistry))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.IFunctionSurfaceRegistry))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.ICapabilityContribution))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.CompatVersion))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.CompatPackManifest))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.CompatPackDependency))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.CompatPackOverride))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.CompatPackSurface))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.CompatPackGameIdentity))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.IInstructionVariantContribution))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.InstructionVariantBinding))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.ICompatInstructionFactory))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.IPolicyContribution))]
[assembly: TypeForwardedTo(typeof(Emuera.Compatibility.Packs.EnginePolicyBinding))]
