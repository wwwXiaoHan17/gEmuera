namespace MinorShift.Emuera.Runtime.Utils.PluginSystem
{
	public interface IPluginMethod
	{
		string Name { get; }
		string Description { get; }
		void Execute(PluginMethodParameter[] args);
	}
}
