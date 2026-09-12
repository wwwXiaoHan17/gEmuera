using System.Collections.Generic;

namespace MinorShift.Emuera.Runtime.Utils.PluginSystem
{
	public abstract class PluginManifestAbstract
	{
		public abstract string PluginName { get; }
		public abstract string PluginDescription { get; }
		public abstract string PluginVersion { get; }
		public abstract string PluginAuthor { get; }

		public List<IPluginMethod> GetPluginMethods()
		{
			return methods;
		}

		protected List<IPluginMethod> methods = new List<IPluginMethod>();
	}
}
