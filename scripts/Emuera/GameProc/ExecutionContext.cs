using System;
using System.Collections.Generic;
using MinorShift.Emuera.GameData;

namespace MinorShift.Emuera.GameProc
{
	internal sealed class ExecutionContext
	{
		public FunctionLabelLine Function { get; }

		// 按函数实际声明的 LocalLength/ArgLength 延迟分配：首次访问才 new，
		// 未使用到的变量类型不分配数组。每个 ExecutionContext 属于一次调用，
		// 递归/嵌套调用各自持有独立数组，保持与参考实现一致的隔离语义。
		long[] localIntegers;
		string[] localStrings;
		double[] localFloats;
		long[] argIntegers;
		string[] argStrings;
		double[] argFloats;

		public long[] LocalIntegers
		{
			get { return localIntegers ?? (localIntegers = new long[localIntLen]); }
		}
		public string[] LocalStrings
		{
			get { return localStrings ?? (localStrings = new string[localStrLen]); }
		}
		public double[] LocalFloats
		{
			get { return localFloats ?? (localFloats = new double[localFloatLen]); }
		}
		public long[] ArgIntegers
		{
			get { return argIntegers ?? (argIntegers = new long[argIntLen]); }
			set { argIntegers = value; }
		}
		public string[] ArgStrings
		{
			get { return argStrings ?? (argStrings = new string[argStrLen]); }
			set { argStrings = value; }
		}
		public double[] ArgFloats
		{
			get { return argFloats ?? (argFloats = new double[argFloatLen]); }
			set { argFloats = value; }
		}

		// 本帧六个局部变量数组的有效长度。
		readonly int localIntLen, localStrLen, localFloatLen, argIntLen, argStrLen, argFloatLen;

		ExecutionContext parent;
		readonly List<ExecutionContext> children = new List<ExecutionContext>();

		// 各变量类型的默认尺寸由 IdentifierDictionary（其内 VariableLocal.size 为 readonly）
		// 在装载完成后固定，按实例缓存一次，避免每帧执行 6 次字典查找。
		static IdentifierDictionary cachedDefaultDict;
		static int[] cachedDefaultSizes;

		public ExecutionContext(FunctionLabelLine func, ExecutionContext parent)
		{
			Function = func;
			this.parent = parent;
			parent?.children.Add(this);

			var idDict = GlobalStatic.IdentifierDictionary;
			if (idDict != null)
			{
				int[] defs = GetDefaultSizes(idDict);
				localIntLen = ResolveLen(func.LocalLength, defs[0], false);
				localStrLen = ResolveLen(func.LocalsLength, defs[1], false);
				localFloatLen = ResolveLen(func.LocalFloatLength, defs[2], false);
				argIntLen = ResolveLen(func.ArgLength, defs[3], true);
				argStrLen = ResolveLen(func.ArgsLength, defs[4], true);
				argFloatLen = ResolveLen(func.ArgFloatLength, defs[5], true);
			}
			else
			{
				// IdentifierDictionary 未就绪时的回退语义与原先一致：
				// 整数/字符串按 1000/100，浮点按声明长度（可能为 0 → 空数组）。
				int localLen = func.LocalLength <= 0 ? 1000 : func.LocalLength;
				int localsLen = func.LocalsLength <= 0 ? 100 : func.LocalsLength;
				int argLen = func.ArgLength <= 0 ? 1000 : Math.Max(func.ArgLength, 1000);
				int argsLen = func.ArgsLength <= 0 ? 100 : Math.Max(func.ArgsLength, 100);
				localIntLen = Math.Max(localLen, 0);
				localStrLen = Math.Max(localsLen, 0);
				localFloatLen = Math.Max(func.LocalFloatLength, 0);
				argIntLen = Math.Max(argLen, 0);
				argStrLen = Math.Max(argsLen, 0);
				argFloatLen = Math.Max(func.ArgFloatLength, 0);
			}
		}

		static int[] GetDefaultSizes(IdentifierDictionary idDict)
		{
			int[] defs = cachedDefaultSizes;
			if (defs != null && ReferenceEquals(cachedDefaultDict, idDict))
				return defs;
			defs = new int[6];
			defs[0] = idDict.getLocalDefaultSize("LOCAL");
			defs[1] = idDict.getLocalDefaultSize("LOCALS");
			defs[2] = idDict.getLocalDefaultSize("LOCALF");
			defs[3] = idDict.getLocalDefaultSize("ARG");
			defs[4] = idDict.getLocalDefaultSize("ARGS");
			defs[5] = idDict.getLocalDefaultSize("ARGF");
			cachedDefaultSizes = defs;
			cachedDefaultDict = idDict;
			return defs;
		}

		// 尺寸规则与 v24/Snake 的 VariableLocal.GetNewLocalVariableToken 完全一致：
		// LOCAL 系 = 声明长度（未声明取默认）；ARG 系 = max(声明长度, 默认)，
		// 即 ARG 向上钳制到默认尺寸（v24/Snake 同样钳制，不能去掉，否则 ARG:5 声明下访问 ARG:999 会越界）。
		static int ResolveLen(int declared, int defaultSize, bool clampToDefault)
		{
			if (declared <= 0)
				return Math.Max(defaultSize, 0);
			if (clampToDefault && declared < defaultSize)
				return Math.Max(defaultSize, 0);
			return Math.Max(declared, 0);
		}

		public ExecutionContext Parent
		{
			get { return parent; }
		}

		public void Dispose()
		{
			parent?.children.Remove(this);
			foreach (ExecutionContext child in children)
				child.parent = null;
			children.Clear();
		}
	}
}
