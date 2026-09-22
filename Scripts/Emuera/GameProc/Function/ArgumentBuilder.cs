using System;
using System.Collections.Generic;
//using System.Drawing;
//using Microsoft.VisualBasic;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameData.Function;
using uEmuera.Drawing;
using uEmuera.VisualBasic;

namespace MinorShift.Emuera.GameProc.Function
{
	internal abstract class ArgumentBuilder
	{
		protected void assignwarn(string mes, InstructionLine line, int level, bool isBackComp)
		{
			bool isError = level >= 2;
			if (isError)
			{
				line.IsError = true;
				line.ErrMes = mes;
			}
			ParserMediator.Warn(mes, line, level, isError, isBackComp);
		}
		protected void warn(string mes, InstructionLine line, int level, bool isBackComp)
		{
			mes = line.Function.Name + "命令:" + mes;
			bool isError = level >= 2;
			if (isError)
			{
				line.IsError = true;
				line.ErrMes = mes;
			}
			ParserMediator.Warn(mes, line, level, isError, isBackComp);
		}
		/// <summary>
		/// 引数の型と数。EraType.Voidで任意の型（あるいは個別にチェックするべき引数）。
		/// </summary>
		protected EraType[] argumentTypeArray;//
		protected bool[] nullableArgumentArray;
		/// <summary>
		/// 最低限必要な引数の数。設定しないと全て省略不可。
		/// </summary>
		protected int minArg = -1;
		/// <summary>
		/// 引数の数に制限なし。
		/// </summary>
		protected bool argAny = false;
		protected bool checkArgumentType(InstructionLine line, ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (arguments == null)
			{
				warn("引数がありません", line, 2, false);
				return false;
			}
			if ( arguments.Length < minArg || 
				((arguments.Length < argumentTypeArray.Length) && (minArg < 0)) )
			{
				warn("引数が足りません", line, 2, false);
				return false;
			}
			int length = arguments.Length;
			if ((arguments.Length > argumentTypeArray.Length)&&(!argAny))
			{
				warn("引数が多すぎます", line, 1, false);
				length = argumentTypeArray.Length;
			}
			for (int i = 0; i < length; i++)
			{
				EraType allowType;
				if (argAny && i >= argumentTypeArray.Length)
					allowType = argumentTypeArray[argumentTypeArray.Length - 1];
				else
					allowType = argumentTypeArray[i];
				if (!argAny && allowType == EraType.Void)
					continue;
				if (arguments[i] == null)
				{
					if (allowType == EraType.Void)
						continue;
					warn("第" + (i + 1).ToString() + "引数を認識できません", line, 2, false);
					return false;
				}
				if ((allowType != EraType.Void) && (allowType != arguments[i].GetEraType()))
				{
					warn("第" + (i + 1).ToString() + "引数の型が正しくありません", line, 2, false);
					return false;
				}
			}
			length = arguments.Length;
			for (int i = 0; i < length; i++)
			{
				if (arguments[i] == null)
					continue;
				arguments[i] = arguments[i].Restructure(exm);
			}
			return true;
		}

		protected VariableTerm getChangeableVariable(IOperandTerm[] terms, int i, InstructionLine line)
		{
            if (!(terms[i - 1] is VariableTerm varTerm))
            {
                warn("第" + i + "引数に変数以外を指定することはできません", line, 2, false);
                return null;
            }
            else if (varTerm.Identifier.IsConst)
            {
                warn("第" + i + "引数に変更できない変数を指定することはできません", line, 2, false);
                return null;
            }
            return varTerm;
		}

		protected WordCollection popWords(InstructionLine line)
		{
			StringStream st = line.PopArgumentPrimitive();
			return LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
		}

		protected IOperandTerm[] popTerms(InstructionLine line)
		{
			StringStream st = line.PopArgumentPrimitive();
			WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
			return ExpressionParser.ReduceArguments(wc, ArgsEndWith.EoL, false);
		}
		public abstract Argument CreateArgument(InstructionLine line, ExpressionMediator exm);
	}


	internal static partial class ArgumentParser
	{
		readonly static Dictionary<FunctionArgType, ArgumentBuilder> argb = new Dictionary<FunctionArgType, ArgumentBuilder>();
		
		public static Dictionary<FunctionArgType, ArgumentBuilder> GetArgumentBuilderDictionary()
		{
			return argb;
		}
		public static ArgumentBuilder GetArgumentBuilder(FunctionArgType key)
		{
			return argb[key];
		}
		internal static ArgumentBuilder CreateForNextArgumentBuilder(bool allowsOmittedStart)
		{
			return new SP_FOR_NEXT_ArgumentBuilder(allowsOmittedStart);
		}
		readonly static Dictionary<string, ArgumentBuilder> nargb = new Dictionary<string, ArgumentBuilder>();

		/// <summary>
		/// 一般的な引数作成器の呼び出し。数式と文字列式のいずれかのみを引数とし、特殊なチェックが必要ないもの
		/// </summary>
		/// <param name="argstr">大文字のIとSで"IIS"で(int, int, string )のように引数の数と順序を指定する。</param>
		/// <param name="minArg">引数の最低数。これ以降は省略可能</param>
		/// <returns></returns>
		public static ArgumentBuilder GetNormalArgumentBuilder(string argstr, int minArg)
		{
			if (minArg < 0)
				minArg = argstr.Length;
			string key = argstr + minArg.ToString();
            ArgumentBuilder argbuilder = null;
			if (nargb.TryGetValue(key, out argbuilder))
				return argbuilder;
			EraType[] types = new EraType[argstr.Length];
			for (int i = 0; i < argstr.Length; i++)
			{
				if (argstr[i] == 'I')
					types[i] = EraType.Integer;
				else if (argstr[i] == 'S')
					types[i] = EraType.String;
				else
					throw new ExeEE("異常な指定");
			}
            argbuilder = new Expressions_ArgumentBuilder(types, minArg);
			nargb.Add(key, argbuilder);
			return argbuilder;
		}
		static ArgumentParser()
		{
			argb[FunctionArgType.METHOD] = new METHOD_ArgumentBuilder();
			argb[FunctionArgType.VOID] = new VOID_ArgumentBuilder();
			argb[FunctionArgType.INT_EXPRESSION] = new INT_EXPRESSION_ArgumentBuilder(false);
			argb[FunctionArgType.INT_EXPRESSION_NULLABLE] = new INT_EXPRESSION_ArgumentBuilder(true);
			argb[FunctionArgType.STR_EXPRESSION] = new STR_EXPRESSION_ArgumentBuilder(false);
            argb[FunctionArgType.STR_EXPRESSION_NULLABLE] = new STR_EXPRESSION_ArgumentBuilder(true);
            argb[FunctionArgType.STR] = new STR_ArgumentBuilder(false);
			argb[FunctionArgType.STR_NULLABLE] = new STR_ArgumentBuilder(true);
			argb[FunctionArgType.FORM_STR] = new FORM_STR_ArgumentBuilder(false);
			argb[FunctionArgType.FORM_STR_NULLABLE] = new FORM_STR_ArgumentBuilder(true);
			argb[FunctionArgType.SP_PRINTV] = new SP_PRINTV_ArgumentBuilder();
			argb[FunctionArgType.SP_TIMES] = new SP_TIMES_ArgumentBuilder();
			argb[FunctionArgType.SP_BAR] = new SP_BAR_ArgumentBuilder();
			argb[FunctionArgType.SP_SET] = new SP_SET_ArgumentBuilder();
			argb[FunctionArgType.SP_SETS] = new SP_SET_ArgumentBuilder();
			argb[FunctionArgType.SP_SWAP] = new SP_SWAP_ArgumentBuilder(false);
			argb[FunctionArgType.SP_VAR] = new SP_VAR_ArgumentBuilder();
			argb[FunctionArgType.SP_SAVEDATA] = new SP_SAVEDATA_ArgumentBuilder();
            argb[FunctionArgType.SP_TINPUT] = new SP_TINPUT_ArgumentBuilder();
            argb[FunctionArgType.SP_TINPUTS] = new SP_TINPUTS_ArgumentBuilder();
			argb[FunctionArgType.SP_SORTCHARA] = new SP_SORTCHARA_ArgumentBuilder();
			argb[FunctionArgType.SP_CALL] = new SP_CALL_ArgumentBuilder(false, false);
			argb[FunctionArgType.SP_CALLF] = new SP_CALL_ArgumentBuilder(true, false);
			argb[FunctionArgType.SP_CALLFORM] = new SP_CALL_ArgumentBuilder(false, true);
			argb[FunctionArgType.SP_CALLFORMF] = new SP_CALL_ArgumentBuilder(true, true);
			argb[FunctionArgType.SP_FOR_NEXT] = new SP_FOR_NEXT_ArgumentBuilder(false);
			argb[FunctionArgType.SP_POWER] = new SP_POWER_ArgumentBuilder();
			argb[FunctionArgType.SP_SWAPVAR] = new SP_SWAPVAR_ArgumentBuilder();
			argb[FunctionArgType.EXPRESSION] = new EXPRESSION_ArgumentBuilder(false);
			argb[FunctionArgType.EXPRESSION_NULLABLE] = new EXPRESSION_ArgumentBuilder(true);
			argb[FunctionArgType.CASE] = new CASE_ArgumentBuilder();
			argb[FunctionArgType.VAR_INT] = new VAR_INT_ArgumentBuilder();
            argb[FunctionArgType.VAR_STR] = new VAR_STR_ArgumentBuilder();
			argb[FunctionArgType.BIT_ARG] = new BIT_ARG_ArgumentBuilder();
			argb[FunctionArgType.SP_VAR_SET] = new SP_VAR_SET_ArgumentBuilder();
			argb[FunctionArgType.SP_BUTTON] = new SP_BUTTON_ArgumentBuilder();
			argb[FunctionArgType.SP_COLOR] = new SP_COLOR_ArgumentBuilder();
			argb[FunctionArgType.SP_COLOR_ALPHA] = new SP_COLOR_ALPHA_ArgumentBuilder();
			argb[FunctionArgType.SP_SPLIT] = new SP_SPLIT_ArgumentBuilder();
			argb[FunctionArgType.SP_GETINT] = new SP_GETINT_ArgumentBuilder();
			argb[FunctionArgType.SP_CVAR_SET] = new SP_CVAR_SET_ArgumentBuilder();
			argb[FunctionArgType.SP_CONTROL_ARRAY] = new SP_CONTROL_ARRAY_ArgumentBuilder();
			argb[FunctionArgType.SP_SHIFT_ARRAY] = new SP_SHIFT_ARRAY_ArgumentBuilder();
            argb[FunctionArgType.SP_SORTARRAY] = new SP_SORT_ARRAY_ArgumentBuilder();
			argb[FunctionArgType.INT_ANY] = new INT_ANY_ArgumentBuilder();
			argb[FunctionArgType.FORM_STR_ANY] = new FORM_STR_ANY_ArgumentBuilder();
            argb[FunctionArgType.SP_COPYCHARA] = new SP_SWAP_ArgumentBuilder(true);
            argb[FunctionArgType.SP_INPUT] = new SP_INPUT_ArgumentBuilder();
			argb[FunctionArgType.SP_INPUTS] = new SP_INPUTS_ArgumentBuilder();
            argb[FunctionArgType.SP_COPY_ARRAY] = new SP_COPY_ARRAY_Arguments();
			argb[FunctionArgType.SP_SAVEVAR] = new SP_SAVEVAR_ArgumentBuilder();
			argb[FunctionArgType.SP_SAVECHARA] = new SP_SAVECHARA_ArgumentBuilder();
			argb[FunctionArgType.SP_REF] = new SP_REF_ArgumentBuilder(false);
			argb[FunctionArgType.SP_REFBYNAME] = new SP_REF_ArgumentBuilder(true);
			argb[FunctionArgType.SP_SETBGIMAGE] = new SP_SETBGIMAGE_ArgumentBuilder();
			argb[FunctionArgType.SP_SETIMAGELAYERL] = new SP_SETIMAGELAYERL_ArgumentBuilder();
			argb[FunctionArgType.SP_HTMLSPLIT] = new SP_HTMLSPLIT_ArgumentBuilder();
			argb[FunctionArgType.SP_DT_COLUMN_OPTIONS] = new SP_DT_COLUMN_OPTIONS_ArgumentBuilder();
			argb[FunctionArgType.SP_PRINT_IMG] = new SP_PRINT_IMG_ArgumentBuilder();
			argb[FunctionArgType.SP_PRINT_RECT] = new SP_PRINT_SHAPE_ArgumentBuilder(4);
			argb[FunctionArgType.SP_PRINT_SPACE] = new SP_PRINT_SHAPE_ArgumentBuilder(1);
			
        }

		private sealed class SP_PRINT_IMG_ArgumentBuilder : ArgumentBuilder
		{
			public SP_PRINT_IMG_ArgumentBuilder()
			{
				argumentTypeArray = null;
				minArg = 1;
			}

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				WordCollection words = popWords(line);
				if (words.EOL)
				{
					warn("第1引数を省略することはできません", line, 2, false);
					return null;
				}

				IOperandTerm name = ExpressionParser.ReduceExpressionTerm(words, TermEndWith.Comma);
				if (name == null)
				{
					warn("第1引数を省略することはできません", line, 2, false);
					return null;
				}
				if (Config.NeedReduceArgumentOnLoad)
					name = name.Restructure(exm);
				words.ShiftNext();

				IOperandTerm buttonName = null;
				IOperandTerm mappingName = null;
				var parameters = new List<MixedIntegerExprTerm>();
				int argumentIndex = 2;
				while (!words.EOL)
				{
					if (parameters.Count == 3)
					{
						warn("引数が多すぎます", line, 2, false);
						return null;
					}

					IOperandTerm term = ExpressionParser.ReduceExpressionTerm(words, TermEndWith.Comma | TermEndWith.KeyWordPx);
					if (term == null)
					{
						warn("第" + argumentIndex.ToString() + "引数を省略することはできません", line, 2, false);
						return null;
					}
					if (Config.NeedReduceArgumentOnLoad)
						term = term.Restructure(exm);

					if (term.GetEraType() == EraType.String)
					{
						if (parameters.Count > 0 || argumentIndex > 3)
						{
							warn("第" + argumentIndex.ToString() + "引数の型が正しくありません", line, 2, false);
							return null;
						}
						if (argumentIndex == 2)
							buttonName = term;
						else
							mappingName = term;
					}
					else
					{
						parameters.Add(new MixedIntegerExprTerm
						{
							Num = term,
							IsPx = words.Current.Type != '\0' && words.Current.Type != ',',
						});
					}

					if (words.Current.Type != '\0' && words.Current.Type != ',')
						words.ShiftNext();
					words.ShiftNext();
					argumentIndex++;
				}

				return new SpPrintImgArgument(
					name,
					buttonName,
					mappingName,
					parameters.Count > 0 ? parameters.ToArray() : null);
			}
		}

		private sealed class SP_PRINT_SHAPE_ArgumentBuilder : ArgumentBuilder
		{
			private readonly int maxArguments;

			public SP_PRINT_SHAPE_ArgumentBuilder(int maxArguments)
			{
				this.maxArguments = maxArguments;
				argumentTypeArray = new EraType[]
				{
					EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer
				};
				minArg = 1;
			}

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				WordCollection words = popWords(line);
				var parameters = new List<MixedIntegerExprTerm>();
				while (!words.EOL)
				{
					IOperandTerm term = ExpressionParser.ReduceExpressionTerm(words, TermEndWith.Comma | TermEndWith.KeyWordPx);
					if (term == null)
					{
						warn("引数を認識できません", line, 2, false);
						return null;
					}
					if (Config.NeedReduceArgumentOnLoad)
						term = term.Restructure(exm);
					parameters.Add(new MixedIntegerExprTerm
					{
						Num = term,
						IsPx = words.Current.Type != '\0' && words.Current.Type != ',',
					});
					if (words.Current.Type != '\0' && words.Current.Type != ',')
						words.ShiftNext();
					words.ShiftNext();
				}

				// 参考两段式校验：超 maxArg 仅警告放行；个数非 1 且非 4 才是错误
				if (parameters.Count > maxArguments)
				{
					warn("引数が多すぎます", line, 1, false);
				}
				if (parameters.Count != 1 && parameters.Count != 4)
				{
					warn("引数の数が正しくありません", line, 2, false);
					return null;
				}
				return new SpPrintShapeArgument(parameters.ToArray());
			}
		}
			
		private sealed class SP_DT_COLUMN_OPTIONS_ArgumentBuilder : ArgumentBuilder
		{
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				WordCollection wc = popWords(line);
				IOperandTerm dataTable = ReduceRequiredStringTerm(wc, line, exm, 1);
				if (dataTable == null)
					return null;
				IOperandTerm column = ReduceRequiredStringTerm(wc, line, exm, 2);
				if (column == null)
					return null;

				List<SpDtColumnOptionsArgument.OptionType> options = new List<SpDtColumnOptionsArgument.OptionType>();
				List<IOperandTerm> values = new List<IOperandTerm>();
				int argIndex = 3;
				while (!wc.EOL)
				{
					string keyword = wc.Current.ToString().ToLowerInvariant();
					wc.ShiftNext();
					wc.ShiftNext();
					if (wc.EOL)
					{
						warn("引数が足りません", line, 2, false);
						return null;
					}

					IOperandTerm value;
					switch (keyword)
					{
						case "default":
							options.Add(SpDtColumnOptionsArgument.OptionType.Default);
							value = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.Comma);
							wc.ShiftNext();
							break;
						default:
							warn("解釈できないオプションです", line, 2, false);
							return null;
					}

					if (value == null)
					{
						warn("第" + argIndex.ToString() + "引数を省略することはできません", line, 2, false);
						return null;
					}
					values.Add(value.Restructure(exm));
					argIndex += 2;
				}

				if (options.Count == 0)
				{
					warn("引数が足りません", line, 2, false);
					return null;
				}
				return new SpDtColumnOptionsArgument(dataTable, column, options.ToArray(), values.ToArray());
			}

			private IOperandTerm ReduceRequiredStringTerm(WordCollection wc, InstructionLine line, ExpressionMediator exm, int index)
			{
				if (wc.EOL)
				{
					warn("第" + index.ToString() + "引数を省略することはできません", line, 2, false);
					return null;
				}
				IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.Comma);
				if (term == null)
				{
					warn("第" + index.ToString() + "引数を認識できません", line, 2, false);
					return null;
				}
				if (!term.IsString)
				{
					warn("第" + index.ToString() + "引数の型が正しくありません", line, 2, false);
					return null;
				}
				wc.ShiftNext();
				return term.Restructure(exm);
			}
		}

		private sealed class SP_PRINTV_ArgumentBuilder : ArgumentBuilder
		{
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
				WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AnalyzePrintV);
				IOperandTerm[] args = ExpressionParser.ReduceArguments(wc, ArgsEndWith.EoL, false);
				for(int i = 0; i< args.Length;i++)
				{
					if(args[i] == null)
						{warn("引数を省略することはできません", line, 2, false); return null;}
					else
						args[i] = args[i].Restructure(exm);
				}
				return new SpPrintVArgument(args);
			}
		}

        private sealed class SP_TIMES_ArgumentBuilder : ArgumentBuilder
		{
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
				WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.Comma, LexAnalyzeFlag.None);
				st.ShiftNext();
				if (st.EOS)
					{warn("引数が足りません", line, 2, false); return null;}
				WordCollection wc2 = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
				IOperandTerm multiplier = ExpressionParser.ReduceExpressionTerm(wc2, TermEndWith.EoL);
				if (multiplier == null)
				{ warn("書式が間違っています", line, 2, false); return null; }
				if (multiplier.IsString)
				{ warn("第２引数を文字列式にすることはできません", line, 2, false); return null; }
				multiplier = multiplier.Restructure(exm);
				IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
				if (term == null)
				{ warn("書式が間違っています", line, 2, false); return null; }
				VariableTerm varTerm = term.Restructure(exm) as VariableTerm;
				if (varTerm == null)
				{ warn("第１引数に変数以外を指定することはできません", line, 2, false); return null; }
				else if (varTerm.IsString)
				{ warn("第１引数を文字列変数にすることはできません", line, 2, false); return null; }
				else if (varTerm.Identifier.IsConst)
				{ warn("第１引数に変更できない変数を指定することはできません", line, 2, false); return null; }
				return new SpTimesArgument(varTerm, multiplier);
			}
		}
		
        private sealed class FORM_STR_ANY_ArgumentBuilder : ArgumentBuilder
		{
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				Argument ret = null;
				StringStream st = line.PopArgumentPrimitive();
				List<IOperandTerm> termList = new List<IOperandTerm>();
				LexicalAnalyzer.SkipHalfSpace(st);
				if (st.EOS)
				{
					if (line.FunctionCode == FunctionCode.RETURNFORM)
					{
						termList.Add(new SingleTerm("0"));
						ret = new ExpressionArrayArgument(termList);
						ret.IsConst = true;
						ret.ConstInt = 0;
						return ret;
					}
					warn("引数が設定されていません", line, 2, false);
					return null;
				}
				while (true)
				{
					StrFormWord sfwt = LexicalAnalyzer.AnalyseFormattedString(st, FormStrEndWith.Comma, false);
					IOperandTerm term = ExpressionParser.ToStrFormTerm(sfwt);
					term = term.Restructure(exm);
					termList.Add(term);
					st.ShiftNext();
					if (st.EOS)
						break;
					LexicalAnalyzer.SkipHalfSpace(st);
					if (st.EOS)
					{
					    warn("\',\'の後ろに引数がありません。", line, 1, false);
					    break;
					}
				}
				return new ExpressionArrayArgument(termList);
			}
		}
	
		private sealed class VOID_ArgumentBuilder : ArgumentBuilder
		{
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
				LexicalAnalyzer.SkipWhiteSpace(st);
				if (!st.EOS)
					warn("引数は不要です", line, 1, false);
				return new VoidArgument();
			}
		}

		private sealed class STR_ArgumentBuilder : ArgumentBuilder
		{
			public STR_ArgumentBuilder(bool nullable)
			{
				this.nullable = nullable;
			}

            readonly bool nullable;
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
					string rowStr;
				if (st.EOS)
				{
					if (!nullable)
					{
						warn("引数が設定されていません", line, 2, false);
						return null;
					}
					rowStr = "";
					//1756 処理変更のために完全に見分けが付かなくなってしまった
					//if (line.FunctionCode == FunctionCode.PRINTL)
					//	warn("PRINTLの後ろに空白がありません(eramaker：\'PRINTL\'を表示)", line, 0, true);
				}
				else
					rowStr = st.Substring();
                if (line.FunctionCode == FunctionCode.SETCOLORBYNAME || line.FunctionCode == FunctionCode.SETBGCOLORBYNAME)
				{
                    Color c = Color.FromName(rowStr);
					if (c.A == 0)
					{
						if (rowStr.Equals("transparent", StringComparison.OrdinalIgnoreCase))
							throw new CodeEE("無色透明(Transparent)は色として指定できません");
						throw new CodeEE("指定された色名\"" + rowStr + "\"は無効な色名です");
					}

                }
                Argument ret = new ExpressionArgument(new SingleTerm(rowStr))
                {
                    ConstStr = rowStr,
                    IsConst = true
                };
                return ret;
			}
		}

		private sealed class FORM_STR_ArgumentBuilder : ArgumentBuilder
		{
			public FORM_STR_ArgumentBuilder(bool nullable)
			{
				this.nullable = nullable;
			}

            readonly bool nullable;

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
				Argument ret;
				if (st.EOS)
				{
					if(!nullable)
					{
						warn("引数が設定されていません", line, 2, false);
						return null;
					}
                    //if (line.FunctionCode == FunctionCode.PRINTFORML)
                    //	warn("PRINTFORMLの後ろに空白がありません(eramaker：\'PRINTFORML\'を表示)", line, 0, true);
                    ret = new ExpressionArgument(new SingleTerm(""))
                    {
                        ConstStr = "",
                        IsConst = true
                    };
                    return ret;
				}
				StrFormWord sfwt = LexicalAnalyzer.AnalyseFormattedString(st, FormStrEndWith.EoL, false);
				IOperandTerm term = ExpressionParser.ToStrFormTerm(sfwt);
				term = term.Restructure(exm);
				ret = new ExpressionArgument(term);
				if(term is SingleTerm)
				{
					ret.ConstStr = term.GetStrValue(exm);
					ret.IsConst = true;
				}
				return ret;
			}
		}

		private sealed class SP_VAR_ArgumentBuilder : ArgumentBuilder
		{
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
                IdentifierWord iw = LexicalAnalyzer.ReadSingleIdentifierWord(st);
                if (iw == null)
                { warn("第１引数を読み取ることができません", line, 2, false); return null; }
				string idStr = iw.Code;
				VariableToken id = GlobalStatic.IdentifierDictionary.GetVariableToken(idStr, null, true);
				if (id == null)
				{ warn("第１引数に変数以外を指定することはできません", line, 2, false); return null; }
				else if ((!id.IsArray1D && !id.IsArray2D && !id.IsArray3D) || (id.Code == VariableCode.RAND))
				{ warn("第１引数に配列でない変数を指定することはできません", line, 2, false); return null; }
				LexicalAnalyzer.SkipWhiteSpace(st);
				if (!st.EOS)
				{
					warn("引数の後に余分な文字があります", line, 1, false);
				}
				return new SpVarsizeArgument(id);
			}
		}

		private sealed class SP_SORTCHARA_ArgumentBuilder : ArgumentBuilder
		{
			
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				VariableTerm varTerm = new VariableTerm(GlobalStatic.VariableData.GetSystemVariableToken("NO"), new IOperandTerm[] { new SingleTerm(0) });
				SortOrder order = SortOrder.ASCENDING;
				WordCollection wc = popWords(line);
				IdentifierWord id = wc.Current as IdentifierWord;
				if (wc.EOL)
				{
					return new SpSortcharaArgument(varTerm, order);
				}
				if ((id != null) && (id.Code.Equals("FORWARD", Config.SCVariable)
					|| (id.Code.Equals("BACK", Config.SCVariable))))
				{
					if (id.Code.Equals("BACK", Config.SCVariable))
						order = SortOrder.DESENDING;
					wc.ShiftNext();
					if (!wc.EOL)
						warn("引数が多すぎます", line, 1, false);
				}
				else
				{
					IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.Comma);
					if (term == null)
					{ warn("書式が間違っています", line, 2, false); return null; }
					varTerm = term.Restructure(exm) as VariableTerm;
					if (varTerm == null)
					{ warn("第１引数に変数以外を指定することはできません", line, 2, false); return null; }
					else if (!varTerm.Identifier.IsCharacterData)
					{ warn("第１引数はキャラクタ変数でなければなりません", line, 2, false); return null; }
					wc.ShiftNext();
					if (!wc.EOL)
					{
						id = wc.Current as IdentifierWord;
						if ((id != null) && (id.Code.Equals("FORWARD", Config.SCVariable)
							|| (id.Code.Equals("BACK", Config.SCVariable))))
						{
							if (id.Code.Equals("BACK", Config.SCVariable))
								order = SortOrder.DESENDING;
							wc.ShiftNext();
							if (!wc.EOL)
								warn("引数が多すぎます", line, 1, false);
						}
						else
						{ warn("書式が間違っています", line, 2, false); return null; }
					}
				}
				return new SpSortcharaArgument(varTerm, order);
			}
		}

        private sealed class SP_SORT_ARRAY_ArgumentBuilder : ArgumentBuilder
        {
            public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
            {
                SortOrder order = SortOrder.ASCENDING;
                WordCollection wc = popWords(line);
                IOperandTerm term3 = new SingleTerm(0);
                IOperandTerm term4 = null;

                if (wc.EOL)
                {
                    warn("書式が間違っています", line, 2, false); return null;
                }

                VariableTerm varTerm;
                IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.Comma);
                if (term == null)
                { warn("書式が間違っています", line, 2, false); return null; }
                varTerm = term.Restructure(exm) as VariableTerm;
                if (varTerm == null)
                { warn("第１引数に変数以外を指定することはできません", line, 2, false); return null; }
                else if (varTerm.Identifier.IsConst)
				{ warn("第１引数が変更できない変数です", line, 2, false); return null; }
                if (!varTerm.Identifier.IsArray1D)
                { warn("第１引数に１次元配列もしくは配列型キャラクタ変数以外を指定することはできません", line, 2, false); return null; }

                wc.ShiftNext();
                IdentifierWord id = wc.Current as IdentifierWord;

                if ((id != null) && (id.Code.Equals("FORWARD", Config.SCVariable) || (id.Code.Equals("BACK", Config.SCVariable))))
                {
                    if (id.Code.Equals("BACK", Config.SCVariable))
                        order = SortOrder.DESENDING;
                    wc.ShiftNext();
                }
                else if (id != null)
                { warn("第２引数にソート方法指定子（FORWARD or BACK）以外が指定されています", line, 2, false); return null; }

                if (id != null)
                {
                    wc.ShiftNext();
                    if (!wc.EOL)
                    {
                        term3 = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.Comma);
                        if (term3 == null)
                        { warn("第３引数が解釈出来ません", line, 2, false); return null; }
                        if (!term3.IsInteger)
                        { warn("第３引数が数値ではありません", line, 2, false); return null; }
                        wc.ShiftNext();
                        if (!wc.EOL)
                        {
                            term4 = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.Comma);
                            if (term4 == null)
                            { warn("第４引数が解釈出来ません", line, 2, false); return null; }
                            if (!term4.IsInteger)
                            { warn("第４引数が数値ではありません", line, 2, false); return null; }
                            wc.ShiftNext();
                            if (!wc.EOL)
                                warn("引数が多すぎます", line, 1, false);
                        }
                    }
                }
                return new SpArraySortArgument(varTerm, order, term3, term4);
            }
        }

		private sealed class SP_CALL_ArgumentBuilder : ArgumentBuilder
		{
			public SP_CALL_ArgumentBuilder(bool callf, bool form)
			{
				this.form = form;
				this.callf = callf;
			}

            readonly bool form;
            readonly bool callf;
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
				IOperandTerm funcname;
				if (form)
				{
					StrFormWord sfw = LexicalAnalyzer.AnalyseFormattedString(st, FormStrEndWith.LeftParenthesis_Bracket_Comma_Semicolon, true);
					funcname = ExpressionParser.ToStrFormTerm(sfw);
					funcname = funcname.Restructure(exm);
				}
				else
				{
					string str = LexicalAnalyzer.ReadString(st, StrEndWith.LeftParenthesis_Bracket_Comma_Semicolon);
					str = str.Trim(new char[] { ' ', '\t' });
					funcname = new SingleTerm(str);
				}
				char cur = st.Current;
				WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
				wc.ShiftNext();

				IOperandTerm[] subNames = null;
				IOperandTerm[] args = null;
                if (cur == '[')
                {
                    subNames = ExpressionParser.ReduceArguments(wc, ArgsEndWith.RightBracket, false);
                    if (!wc.EOL)
                    {
                        if (wc.Current.Type != '(')
                        wc.ShiftNext();
                        args = ExpressionParser.ReduceArguments(wc, ArgsEndWith.RightParenthesis, false);
                    }
                }
				if ((cur == '(') || (cur == ','))
				{
					if (cur == '(')
						args = ExpressionParser.ReduceArguments(wc, ArgsEndWith.RightParenthesis, false);
					else
						args = ExpressionParser.ReduceArguments(wc, ArgsEndWith.EoL, false);
					if (!wc.EOL)
					{ warn("書式が間違っています", line, 2, false); return null; }
				}
				if (subNames == null)
					subNames = new IOperandTerm[0];
				if (args == null)
					args = new IOperandTerm[0];
				for(int i = 0; i < subNames.Length; i++)
					if (subNames != null)
						subNames[i] = subNames[i].Restructure(exm);
				for(int i = 0; i < args.Length; i++)
					if (args[i] != null)
						args[i] = args[i].Restructure(exm);
				Argument ret;
				if(callf)
					ret = new SpCallFArgment(funcname, subNames, args);
				else
					ret = new SpCallArgment(funcname, subNames, args);
                if (funcname is SingleTerm)
                {
                    ret.IsConst = true;
                    ret.ConstStr = funcname.GetStrValue(null);
                    if (ret.ConstStr == "")
                    {
                        warn("関数名が指定されていません", line, 2, false);
                        return null;
                    }
                }
				return ret;
			}
		}

		private sealed class CASE_ArgumentBuilder : ArgumentBuilder
		{
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				WordCollection wc = popWords(line);
				CaseExpression[] args = ExpressionParser.ReduceCaseExpressions(wc);
				if ((!wc.EOL) || (args.Length == 0))
				{ warn("書式が間違っています", line, 2, false); return null; }
				for(int i = 0; i < args.Length; i++)
					args[i].Reduce(exm);
				return new CaseArgument(args);
			}
		}
		
		private sealed class SP_SET_ArgumentBuilder : ArgumentBuilder
		{
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm) 
			{
				WordCollection destWc = line.PopAssignmentDestStr();
				IOperandTerm[] destTerms = ExpressionParser.ReduceArguments(destWc, ArgsEndWith.EoL, false);
				SpSetArgument ret;
				if ((destTerms.Length == 0) || (destTerms[0] == null))
				{ assignwarn("代入文の左辺の読み取りに失敗しました", line, 2, false); return null; }
				if (destTerms.Length != 1)
					{assignwarn("代入文の左辺に余分な','があります", line, 2, false); return null;}
                if (!(destTerms[0] is VariableTerm varTerm))
                {//
                    assignwarn("代入文の左辺に変数以外を指定することはできません", line, 2, false);
                    return null;
                }
                else if (varTerm.Identifier.IsConst)
                {
                    assignwarn("代入文の左辺に変更できない変数を指定することはできません", line, 2, false);
                    return null;
                }
                varTerm.Restructure(exm);
				StringStream st = line.PopArgumentPrimitive();
                if (st == null)
                    st = new StringStream("");
                OperatorCode op = line.AssignOperator;
				IOperandTerm src;
				if(varTerm.IsInteger || varTerm.IsFloat)
				{
					if (op == OperatorCode.AssignmentStr)
						{ assignwarn("数値型の代入に演算子"+OperatorManager.ToOperatorString(op) + "は使用できません", line, 2, false); return null; }
					if((op == OperatorCode.Increment)||(op == OperatorCode.Decrement))
					{
						LexicalAnalyzer.SkipWhiteSpace(st);
						if (!st.EOS)
						{
							if (op == OperatorCode.Increment)
								{assignwarn("インクリメント行でインクリメント以外の処理が定義されています", line, 2, false);return null;}
							else
								{assignwarn("デクリメント行でデクリメント以外の処理が定義されています", line, 2, false);return null;}
						}
						ret = new SpSetArgument(varTerm, null)
						{
							IsConst = true,
							AddConst = true
                        };
						if (varTerm.IsFloat)
							ret.ConstFloat = op == OperatorCode.Increment ? 1.0 : -1.0;
						else
							ret.ConstInt = op == OperatorCode.Increment ? 1 : -1;
						return ret;
					}
					WordCollection srcWc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
					IOperandTerm[] srcTerms = ExpressionParser.ReduceArguments(srcWc, ArgsEndWith.EoL, false);
					
					if ((srcTerms.Length == 0) || (srcTerms[0] == null))
						{assignwarn("代入文の右辺の読み取りに失敗しました", line, 2, false); return null;}
					if (srcTerms.Length != 1)
					{
						if(op != OperatorCode.Assignment)
						{assignwarn("複合代入演算では右辺に複数の値を含めることはできません", line, 2, false); return null;}
						bool allConst = true;
						if (varTerm.IsFloat)
						{
							double[] constValues = new double[srcTerms.Length];
							for (int i = 0; i < srcTerms.Length; i++)
							{
								if (srcTerms[i] == null)
								{ assignwarn("代入式の右辺の値は省略できません", line, 2, false); return null; }
								if (srcTerms[i].IsString)
								{ assignwarn("数値型変数に文字列は代入できません", line, 2, false); return null; }
								srcTerms[i] = srcTerms[i].Restructure(exm);
								if (allConst && (srcTerms[i] is SingleTerm))
									constValues[i] = srcTerms[i].GetFloatValue(null);
								else
									allConst = false;
							}
							SpSetArrayArgument arrayarg = new SpSetArrayArgument(varTerm, srcTerms, constValues)
							{
								IsConst = allConst
							};
							return arrayarg;
						}
						else
						{
							Int64[] constValues = new Int64[srcTerms.Length];
							for (int i = 0; i < srcTerms.Length; i++)
							{
								if (srcTerms[i] == null)
								{ assignwarn("代入式の右辺の値は省略できません", line, 2, false); return null; }
								if (srcTerms[i].IsString)
								{ assignwarn("数値型変数に文字列は代入できません", line, 2, false); return null; }
								srcTerms[i] = srcTerms[i].Restructure(exm);
								if (allConst && (srcTerms[i] is SingleTerm))
									constValues[i] = srcTerms[i].GetIntValue(null);
								else
									allConst = false;
							}
							SpSetArrayArgument arrayarg = new SpSetArrayArgument(varTerm, srcTerms, constValues)
							{
								IsConst = allConst
							};
							return arrayarg;
						}
					}
					if(srcTerms[0].IsString)
						{assignwarn("数値型変数に文字列は代入できません", line, 2, false); return null;}
					src = srcTerms[0].Restructure(exm);
					if(op == OperatorCode.Assignment)
					{
						ret = new SpSetArgument(varTerm, src);
						if(src is SingleTerm)
						{
							ret.IsConst = true;
							ret.AddConst = false;
							if (varTerm.IsFloat)
								ret.ConstFloat = src.GetFloatValue(null);
							else
								ret.ConstInt = src.GetIntValue(null);
						}
						return ret;
					}
					if((op == OperatorCode.Plus)||(op == OperatorCode.Minus))
					{
						if(src is SingleTerm)
						{
                            ret = new SpSetArgument(varTerm, null)
                            {
                                IsConst = true,
								AddConst = true
                            };
							if (varTerm.IsFloat)
								ret.ConstFloat = op == OperatorCode.Plus ? src.GetFloatValue(null) : -src.GetFloatValue(null);
							else
								ret.ConstInt = op == OperatorCode.Plus ? src.GetIntValue(null) : -src.GetIntValue(null);
							return ret;
						}
					}
					src = OperatorMethodManager.ReduceBinaryTerm(op,varTerm, src);
					return new SpSetArgument(varTerm, src);
					
				}
				else
				{
					if (op == OperatorCode.Assignment)
					{
						if (Config.SystemIgnoreStringSet)
						{ assignwarn("文字列代入は禁止されています（'=を用いるかコンフィグオプションを変えてください)", line, 2, false); return null; }
						LexicalAnalyzer.SkipHalfSpace(st);//文字列の代入なら半角スペースだけを読み飛ばす
						//eramakerは代入文では妙なTrim()をする。半端にしか再現できないがとりあえずtrim = true
						StrFormWord sfwt = LexicalAnalyzer.AnalyseFormattedString(st, FormStrEndWith.EoL, true);
						IOperandTerm term = ExpressionParser.ToStrFormTerm(sfwt);
						src = term.Restructure(exm);
						ret = new SpSetArgument(varTerm, src);
						if (src is SingleTerm)
						{
							ret.IsConst = true;
							ret.AddConst = false;
							ret.ConstStr = src.GetStrValue(null);
						}
						return ret;
					}
					else if ((op == OperatorCode.Mult)||(op == OperatorCode.Plus)||(op == OperatorCode.AssignmentStr))
					{
						WordCollection srcWc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
						IOperandTerm[] srcTerms = ExpressionParser.ReduceArguments(srcWc, ArgsEndWith.EoL, false);
						
						if ((srcTerms.Length == 0) || (srcTerms[0] == null))
							{assignwarn("代入文の右辺の読み取りに失敗しました", line, 2, false); return null;}
						if (op == OperatorCode.AssignmentStr)
						{
							if (srcTerms.Length == 1)
							{
								if (!srcTerms[0].IsString)
								{ assignwarn("文字列変数に数値型は代入できません", line, 2, false); return null; }
								src = srcTerms[0].Restructure(exm);
								ret = new SpSetArgument(varTerm, src);
								if (src is SingleTerm)
								{
									ret.IsConst = true;
									ret.AddConst = false;
									ret.ConstStr = src.GetStrValue(null);
								}
								return ret;
							}
							bool allConst = true;
							string[] constValues = new string[srcTerms.Length];
							for (int i = 0; i < srcTerms.Length; i++)
							{
								if (srcTerms[i] == null)
								{ assignwarn("代入式の右辺の値は省略できません", line, 2, false); return null; }
								if (!srcTerms[i].IsString)
								{ assignwarn("文字列変数に数値型は代入できません", line, 2, false); return null; }
								srcTerms[i] = srcTerms[i].Restructure(exm);
								if (allConst && (srcTerms[i] is SingleTerm))
									constValues[i] = srcTerms[i].GetStrValue(null);
								else
									allConst = false;
							}
                            SpSetArrayArgument arrayarg = new SpSetArrayArgument(varTerm, srcTerms, constValues)
                            {
                                IsConst = allConst
                            };
                            return arrayarg;
						}
						if (srcTerms.Length != 1)
						{ assignwarn("代入文の右辺に余分な','があります", line, 2, false); return null; }
							
						src = srcTerms[0].Restructure(exm);
						src = OperatorMethodManager.ReduceBinaryTerm(op, varTerm, src);
						return new SpSetArgument(varTerm, src);
					}
					assignwarn("代入式に使用できない演算子が使われました", line, 2, false);
					return null;
				}
			}
		}
				
		private sealed class METHOD_ArgumentBuilder : ArgumentBuilder
		{
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] args = popTerms(line);
				string errmes = line.Function.Method.CheckArgumentType(line.Function.Name, args);
				if (errmes != null)
					throw new CodeEE(errmes);
				IOperandTerm mTerm = new FunctionMethodTerm(line.Function.Method, args);
				return new MethodArgument(mTerm.Restructure(exm));
			}
		}

        private sealed class SP_INPUTS_ArgumentBuilder : ArgumentBuilder
        {
            public SP_INPUTS_ArgumentBuilder()
            {
                argumentTypeArray = new EraType[] { EraType.String };
                //if (nullable)妥協
                minArg = 0;
            }
            public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
            {
                StringStream st = line.PopArgumentPrimitive();
                // eraFL 的 "INPUTS ,1" 同时表示省略默认字符串并启用鼠标扩展结果。
                // 该标记必须跟随等待请求进入提交链，不能只丢弃第二参数，否则技能按钮值无法写入 RESULTS:1。
                if (!st.EOS && Program.Compatibility.EraFl.IsOmittedDefaultArgument(st.Current))
                {
                    st.ShiftNext();
                    bool enablePointerInputMetadata = Program.Compatibility.EraFl
                        .IsPointerInputMetadataOption(st.Substring());
                    if (!enablePointerInputMetadata)
                        warn("eraFLのINPUTS省略引数にはマウス拡張オプション1を指定してください", line, 1, false);
                    return new SpInputsArgument(null, null, null, enablePointerInputMetadata);
                }
                // 参考（EM_私家版_INPUT系機能拡張）：form 串以逗号结束，其后至多 2 个整数参数（Mouse/CanSkip）
                if (st.EOS)
                    return new SpInputsArgument(null, null, null);
                StrFormWord sfwt = LexicalAnalyzer.AnalyseFormattedString(st, FormStrEndWith.Comma, false);
                IOperandTerm term = ExpressionParser.ToStrFormTerm(sfwt);
                term = term.Restructure(exm);
                if (st.EOS)
                    return new SpInputsArgument(term, null, null);
                st.ShiftNext();
                WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
                IOperandTerm[] terms = ExpressionParser.ReduceArguments(wc, ArgsEndWith.EoL, false);
                if (!st.EOS || terms.Length > 1)
                {
                    warn("引数が多すぎます", line, 1, false);
                }
                if (terms.Length > 0)
                {
                    if (terms[0] == null || !terms[0].IsInteger)
                    {
                        warn("第2引数は整数型ではないため、無視されます", line, 1, false);
                        return new SpInputsArgument(term, null, null);
                    }
                    if (terms.Length == 1)
                        return new SpInputsArgument(term, terms[0], null);
                    return new SpInputsArgument(term, terms[0], terms[1]);
                }
                // 参考侧此路径（逗号后无实参）返回 null 交由上游报错
                return null;
            }
        }
        

		/// <summary>
		/// 一般型。数式と文字列式の組み合わせのみを引数とし、特殊なチェックが必要ないもの
		/// </summary>
		private sealed class Expressions_ArgumentBuilder : ArgumentBuilder
		{
			public Expressions_ArgumentBuilder(EraType[] types, int minArgs = -1)
			{
				argumentTypeArray = types;
				this.minArg = minArgs;
			}

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				return new ExpressionsArgument(argumentTypeArray, terms);
			}
		}
	}
}
