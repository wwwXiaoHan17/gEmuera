using System;
using System.Collections.Generic;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameProc.Function;

namespace MinorShift.Emuera.GameProc
{
	internal sealed class SelectCaseJumpTable
	{
		private readonly Dictionary<long, InstructionLine> intTable;
		private readonly Dictionary<string, InstructionLine> strTable;
		private readonly Dictionary<double, InstructionLine> floatTable;
		private readonly InstructionLine caseElseLine;
		private readonly LogicalLine endSelectLine;

		private SelectCaseJumpTable(EraType selectType, InstructionLine caseElseLine, LogicalLine endSelectLine)
		{
			this.caseElseLine = caseElseLine;
			this.endSelectLine = endSelectLine;
			if (selectType == EraType.Integer)
				intTable = new Dictionary<long, InstructionLine>();
			else if (selectType == EraType.Float)
				floatTable = new Dictionary<double, InstructionLine>();
			else
				strTable = new Dictionary<string, InstructionLine>();
		}

		public static SelectCaseJumpTable TryBuild(InstructionLine selectLine, EraType selectType)
		{
			if (selectLine == null || selectLine.IfCaseList == null)
				return null;
			if (selectType != EraType.Integer && selectType != EraType.String && selectType != EraType.Float)
				return null;

			InstructionLine caseElseLine = null;
			foreach (InstructionLine caseLine in selectLine.IfCaseList)
			{
				if (caseLine.FunctionCode == FunctionCode.CASEELSE)
				{
					caseElseLine = caseLine;
					break;
				}
			}

			SelectCaseJumpTable table = new SelectCaseJumpTable(selectType, caseElseLine, selectLine.JumpTo);
			foreach (InstructionLine caseLine in selectLine.IfCaseList)
			{
				if (caseLine.IsError)
					return null;
				if (caseLine.FunctionCode == FunctionCode.CASEELSE)
					break;

				CaseArgument caseArg = caseLine.Argument as CaseArgument;
				if (caseArg == null || caseArg.CaseExps == null)
					return null;

				foreach (CaseExpression caseExp in caseArg.CaseExps)
				{
					// 跳转表只处理普通常量 CASE；范围、比较和运行期表达式仍走原顺序扫描，避免改变脚本语义。
					// snake 参考实现：非常量表达式先 Restructure，能折叠为 SingleTerm 才收进跳转表。
					if (caseExp.CaseType != CaseExpressionType.Normal || caseExp.LeftTerm == null)
						return null;
					IOperandTerm term = caseExp.LeftTerm;
					if (!(term is SingleTerm))
					{
						try
						{
							IOperandTerm restructured = term.Restructure(null);
							if (restructured is SingleTerm st)
								term = st;
							else
								return null;
						}
						catch
						{
							return null;
						}
					}
					if (term.GetEraType() != selectType)
						return null;

					if (selectType == EraType.Integer)
					{
						long value = term.GetIntValue(null);
						if (table.intTable.ContainsKey(value))
						{
							// 重复 CASE 值：与 snake 一致给警告并跳过，不建立跳转表（避免歧义）。
							ParserMediator.Warn("CASEの値(" + value + ")が重複しています", caseLine, 1, false, false);
							continue;
						}
						table.intTable.Add(value, caseLine);
					}
					else
					if (selectType == EraType.Float)
					{
						double value = term.GetFloatValue(null);
						if (table.floatTable.ContainsKey(value))
						{
							ParserMediator.Warn("CASEの値(" + value + ")が重複しています", caseLine, 1, false, false);
							continue;
						}
						table.floatTable.Add(value, caseLine);
					}
					else
					{
						string value = term.GetStrValue(null);
						if (table.strTable.ContainsKey(value))
						{
							ParserMediator.Warn("CASEの値(" + value + ")が重複しています", caseLine, 1, false, false);
							continue;
						}
						table.strTable.Add(value, caseLine);
					}
				}
			}

			return table;
		}

		public LogicalLine Lookup(long value)
		{
			InstructionLine line;
			if (intTable != null && intTable.TryGetValue(value, out line))
				return line;
			return caseElseLine ?? endSelectLine;
		}

		public LogicalLine Lookup(double value)
		{
			InstructionLine line;
			if (floatTable != null && floatTable.TryGetValue(value, out line))
				return line;
			return caseElseLine ?? endSelectLine;
		}

		public LogicalLine Lookup(string value)
		{
			InstructionLine line;
			if (strTable != null && strTable.TryGetValue(value, out line))
				return line;
			return caseElseLine ?? endSelectLine;
		}
	}
}
