namespace MinorShift.Emuera.Runtime.Utils.PluginSystem
{
	public class PluginMethodParameter
	{
		public PluginMethodParameter(string initialValue)
		{
			isString = true;
			strValue = initialValue;
		}

		public PluginMethodParameter(long initialValue)
		{
			isString = false;
			intValue = initialValue;
		}

		public PluginMethodParameter(double initialValue)
		{
			isString = false;
			isFloat = true;
			floatValue = initialValue;
		}

		public bool isString;
		public bool isFloat;
		public string strValue;
		public long intValue;
		public double floatValue;
	}
}
