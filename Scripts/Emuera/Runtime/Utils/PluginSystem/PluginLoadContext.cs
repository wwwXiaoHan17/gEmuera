using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace MinorShift.Emuera.Runtime.Utils.PluginSystem
{
	/// <summary>
	/// 每插件独立的加载上下文（Phase C 插件真兼容）。
	/// 外部插件按真 Emuera 程序集（Emuera, Version=1.824.0.0）与更高版本框架（实测 net8/net10 混编）编译。
	/// .NET 程序集绑定按简单名匹配（2026-09-04 复现实验：返回名字不匹配的程序集会被运行时以
	/// 0x80131509 拒绝；名字匹配、版本不同则可绑定），因此：
	///   1. Emuera/emuera → Emuera 契约程序集（src/EmueraFacade，与插件共享同一类型标识）；
	///   2. netstandard/System.*/Microsoft.* → 宿主已加载的同名框架程序集（不在回调里发起绑定，
	///      只返回已加载实例；net10 引用由此回落到宿主 net8/net9 运行时）；
	///   3. 其余 → 插件目录探测 &lt;name&gt;.dll；否则返回 null 落回运行时默认解析。
	/// </summary>
	internal sealed class PluginLoadContext : AssemblyLoadContext
	{
		static readonly Assembly contractAssembly = typeof(PluginMethodParameter).Assembly;

		readonly string pluginDirectory;

		public PluginLoadContext(string pluginPath)
		{
			pluginDirectory = Path.GetDirectoryName(Path.GetFullPath(pluginPath));
		}

		protected override Assembly Load(AssemblyName assemblyName)
		{
			if (assemblyName.Name == "Emuera" || assemblyName.Name == "emuera")
				return contractAssembly;
			if (assemblyName.Name == "gemuera-c#")
				return Assembly.GetExecutingAssembly();
			string name = assemblyName.Name ?? "";
			if (name == "netstandard" || name.StartsWith("System.", StringComparison.Ordinal)
				|| name.StartsWith("Microsoft.", StringComparison.Ordinal))
			{
				foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
				{
					if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
						return loaded;
				}
				return null;
			}
			if (pluginDirectory != null)
			{
				string candidate = Path.Combine(pluginDirectory, assemblyName.Name + ".dll");
				if (File.Exists(candidate))
					return LoadFromAssemblyPath(candidate);
			}
			return null;
		}
	}
}
