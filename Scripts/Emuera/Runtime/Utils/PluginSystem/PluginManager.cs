using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.Runtime.Utils.PluginSystem
{
	public sealed class PluginManager
	{
		public static PluginManager GetInstance()
		{
			if (instance == null)
				instance = new PluginManager();
			return instance;
		}

		private PluginManager()
		{
			AssemblyLoadContext.Default.Resolving += resolveExternalEmueraAssembly;
			AppDomain.CurrentDomain.AssemblyResolve += resolveExternalEmueraAssembly;
		}

		static PluginManager instance;
		readonly Dictionary<string, IPluginMethod> methods = new Dictionary<string, IPluginMethod>();
		string currentPluginDir;
		Process process;
		ProcessState processState;
		ExpressionMediator expressionMediator;

		public void LoadPlugins()
		{
			ClearMethods();
			string pluginDir = GetPluginDirectory();
			if (!Directory.Exists(pluginDir))
				return;

			bool pluginsAware = File.Exists(Path.Combine(Program.ExeDir ?? "", "pluginsAware.txt"));
			currentPluginDir = pluginDir;
			foreach (string pluginPath in Directory.GetFiles(pluginDir, "*.dll"))
			{
				if (!pluginsAware)
					throw new ExeEE("This game comes prepackaged with plugins. Create pluginsAware.txt in the game root if you trust these plugins.");
				try
				{
					Assembly dll = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(pluginPath));
					Type manifestType = dll.GetTypes().FirstOrDefault(type => type.Name == "PluginManifest");
					if (manifestType == null)
						continue;
					object manifest = Activator.CreateInstance(manifestType);
					addManifestMethods(manifest);
				}
				catch (Exception ex)
				{
					global::GenericUtils.Warn("Plugin load failed: " + Path.GetFileName(pluginPath) + " - " + ex.Message);
				}
			}
		}

		public IPluginMethod GetMethod(string name)
		{
			return methods[getKey(name)];
		}

		public bool HasMethod(string name)
		{
			return methods.ContainsKey(getKey(name));
		}

		internal void SetParent(Process process, ProcessState processState, ExpressionMediator expressionMediator)
		{
			this.process = process;
			this.processState = processState;
			this.expressionMediator = expressionMediator;
		}

		public void Print(string text)
		{
			expressionMediator.Console.Print(text);
		}

		public void PrintError(string text)
		{
			expressionMediator.Console.PrintError(text);
		}

		public void PrintC(string text, bool aligmentRight = false)
		{
			expressionMediator.Console.PrintC(text, aligmentRight);
		}

		public void PrintPlain(string text)
		{
			expressionMediator.Console.PrintPlain(text);
		}

		public void PrintSingleLine(string text)
		{
			expressionMediator.Console.PrintSingleLine(text);
		}

		public void PrintSystemLine(string text)
		{
			expressionMediator.Console.PrintSystemLine(text);
		}

		public void PrintTemporaryLine(string text)
		{
			expressionMediator.Console.PrintTemporaryLine(text);
		}

		public void PrintButton(string text, long id)
		{
			expressionMediator.Console.PrintButton(text, id);
		}

		public void PrintButtonC(string text, long id, bool aligmentRight = false)
		{
			expressionMediator.Console.PrintButtonC(text, id, aligmentRight);
		}

		public void PrintHtml(string htmlText, bool toBuffer = false)
		{
			expressionMediator.Console.PrintHtml(htmlText, toBuffer);
		}

		public void PrintNewLine()
		{
			expressionMediator.Console.NewLine();
		}

		public void FlushConsole(bool force = false)
		{
			expressionMediator.Console.PrintFlush(force);
		}

		public void ClearDisplay()
		{
			expressionMediator.Console.ClearDisplay();
		}

		public void WaitInput(bool oneInput = true, int timelimit = -1)
		{
			InputRequest request = new InputRequest
			{
				OneInput = oneInput,
				Timelimit = timelimit
			};
			expressionMediator.Console.WaitInput(request);
		}

		public void ReadAnyKey()
		{
			expressionMediator.Console.ReadAnyKey();
		}

		public void Await(int time)
		{
			expressionMediator.Console.Await(time);
		}

		public long GetIntVar(string name, int index = 0)
		{
			VariableToken token = expressionMediator.VEvaluator.VariableData.GetVarTokenDic()[name];
			return token.GetIntValue(expressionMediator, new long[] { index });
		}

		public string GetStrVar(string name, int index = 0)
		{
			VariableToken token = expressionMediator.VEvaluator.VariableData.GetVarTokenDic()[name];
			return token.GetStrValue(expressionMediator, new long[] { index });
		}

		public void SetIntVar(string name, long value, int index = 0)
		{
			VariableToken token = expressionMediator.VEvaluator.VariableData.GetVarTokenDic()[name];
			token.SetValue(value, new long[] { index });
		}

		public void SetStrVar(string name, string value, int index = 0)
		{
			VariableToken token = expressionMediator.VEvaluator.VariableData.GetVarTokenDic()[name];
			token.SetValue(value, new long[] { index });
		}

		void ClearMethods()
		{
			methods.Clear();
		}

		void AddMethod(IPluginMethod method)
		{
			methods[getKey(method.Name)] = method;
		}

		void addManifestMethods(object manifest)
		{
			if (manifest is PluginManifestAbstract typedManifest)
			{
				foreach (IPluginMethod method in typedManifest.GetPluginMethods())
					AddMethod(method);
				return;
			}

			MethodInfo getMethods = manifest.GetType().GetMethod("GetPluginMethods", BindingFlags.Public | BindingFlags.Instance);
			if (getMethods == null)
				return;
			object methodList = getMethods.Invoke(manifest, null);
			if (methodList is System.Collections.IEnumerable enumerable)
			{
				foreach (object method in enumerable)
				{
					IPluginMethod adapted = method as IPluginMethod ?? new ReflectionPluginMethod(method);
					AddMethod(adapted);
				}
			}
		}

		string getKey(string name)
		{
			if (Config.ICFunction)
				return name.ToUpper(CultureInfo.InvariantCulture);
			return name;
		}

		string GetPluginDirectory()
		{
			string root = Program.ExeDir ?? "";
			string upper = Path.Combine(root, "Plugins");
			if (Directory.Exists(upper))
				return upper;
			string lower = Path.Combine(root, "plugins");
			if (Directory.Exists(lower))
				return lower;
			return upper;
		}

		Assembly resolveExternalEmueraAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
		{
			return resolveExternalEmueraAssembly(assemblyName);
		}

		Assembly resolveExternalEmueraAssembly(object sender, ResolveEventArgs args)
		{
			return resolveExternalEmueraAssembly(new AssemblyName(args.Name));
		}

		Assembly resolveExternalEmueraAssembly(AssemblyName assemblyName)
		{
			if (assemblyName.Name == "Emuera" || assemblyName.Name == "emuera" || assemblyName.Name == "gemuera-c#")
				return Assembly.GetExecutingAssembly();
			if (!string.IsNullOrEmpty(currentPluginDir))
			{
				string candidate = Path.Combine(currentPluginDir, assemblyName.Name + ".dll");
				if (File.Exists(candidate))
					return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(candidate));
			}
			return null;
		}

		sealed class ReflectionPluginMethod : IPluginMethod
		{
			public ReflectionPluginMethod(object instance)
			{
				this.instance = instance;
			}

			readonly object instance;

			public string Name
			{
				get { return readStringProperty("Name"); }
			}

			public string Description
			{
				get { return readStringProperty("Description"); }
			}

			public void Execute(PluginMethodParameter[] args)
			{
				MethodInfo execute = instance.GetType().GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance);
				if (execute == null)
					throw new CodeEE("Plugin method " + Name + " has no Execute method");
				execute.Invoke(instance, new object[] { args });
			}

			string readStringProperty(string name)
			{
				PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
				return property == null ? "" : (property.GetValue(instance) as string ?? "");
			}
		}
	}
}
