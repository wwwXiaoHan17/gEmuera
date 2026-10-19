using System;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.Modern.Script.Functions;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Function
{
	internal static partial class FunctionMethodCreator
	{
		private sealed class SqlConnectionOpenMethod : FunctionMethod
		{
			public SqlConnectionOpenMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ConnectionOpen(arguments[0].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlConnectMethod : FunctionMethod
		{
			public SqlConnectMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				return CheckSqlArgs(name, arguments, 1, 2, EraType.String, EraType.String);
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					string connectionString = arguments.Length > 1 && arguments[1] != null ? arguments[1].GetStrValue(exm) : "Data Source=:memory:";
					return ModernSqlManager.Connect(arguments[0].GetStrValue(exm), connectionString, false);
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlDisconnectMethod : FunctionMethod
		{
			public SqlDisconnectMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.Disconnect(arguments[0].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlExecuteNonQueryMethod : FunctionMethod
		{
			public SqlExecuteNonQueryMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ExecuteNonQuery(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlExecuteReaderMethod : FunctionMethod
		{
			public SqlExecuteReaderMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ExecuteReader(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlReaderReadMethod : FunctionMethod
		{
			public SqlReaderReadMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ReaderRead(arguments[0].GetIntValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlReaderGetIntMethod : FunctionMethod
		{
			public SqlReaderGetIntMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ReaderGetLong(arguments[0].GetIntValue(exm), (int)arguments[1].GetIntValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlReaderGetFloatMethod : FunctionMethod
		{
			public SqlReaderGetFloatMethod()
			{
				ReturnType = EraType.Float;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return new SingleTerm(ModernSqlManager.ReaderGetFloat(arguments[0].GetIntValue(exm), (int)arguments[1].GetIntValue(exm)));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlReaderIsNullMethod : FunctionMethod
		{
			public SqlReaderIsNullMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ReaderIsNull(arguments[0].GetIntValue(exm), (int)arguments[1].GetIntValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlReaderCloseMethod : FunctionMethod
		{
			public SqlReaderCloseMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ReaderClose(arguments[0].GetIntValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlPExecuteNonQueryMethod : FunctionMethod
		{
			public SqlPExecuteNonQueryMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				return CheckSqlArgs(name, arguments, 2, int.MaxValue, EraType.String, EraType.String);
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ExecuteNonQuery(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), ReadSqlParameters(exm, arguments, 2));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlPExecuteReaderMethod : FunctionMethod
		{
			public SqlPExecuteReaderMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				return CheckSqlArgs(name, arguments, 2, int.MaxValue, EraType.String, EraType.String);
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ExecuteReader(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), ReadSqlParameters(exm, arguments, 2));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlExecuteScalarLongMethod : FunctionMethod
		{
			public SqlExecuteScalarLongMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ExecuteScalarLong(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlExecuteScalarFloatMethod : FunctionMethod
		{
			public SqlExecuteScalarFloatMethod()
			{
				ReturnType = EraType.Float;
				argumentTypeArray = new EraType[] { EraType.String, EraType.String };
				CanRestructure = false;
			}
			public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return new SingleTerm(ModernSqlManager.ExecuteScalarFloat(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm)));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlPExecuteScalarLongMethod : FunctionMethod
		{
			public SqlPExecuteScalarLongMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				return CheckSqlArgs(name, arguments, 2, int.MaxValue, EraType.String, EraType.String);
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ExecuteScalarLong(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), ReadSqlParameters(exm, arguments, 2));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlPExecuteScalarFloatMethod : FunctionMethod
		{
			public SqlPExecuteScalarFloatMethod()
			{
				ReturnType = EraType.Float;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				return CheckSqlArgs(name, arguments, 2, int.MaxValue, EraType.String, EraType.String);
			}
			public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return new SingleTerm(ModernSqlManager.ExecuteScalarFloat(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), ReadSqlParameters(exm, arguments, 2)));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlEscapeMethod : FunctionMethod
		{
			public SqlEscapeMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = true;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.Escape(arguments[0].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlExecuteScalarStringMethod : FunctionMethod
		{
			public SqlExecuteScalarStringMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = new EraType[] { EraType.String, EraType.String };
				CanRestructure = false;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ExecuteScalarString(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlPExecuteScalarStringMethod : FunctionMethod
		{
			public SqlPExecuteScalarStringMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				return CheckSqlArgs(name, arguments, 2, int.MaxValue, EraType.String, EraType.String);
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ExecuteScalarString(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), ReadSqlParameters(exm, arguments, 2));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlReaderGetStringMethod : FunctionMethod
		{
			public SqlReaderGetStringMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ReaderGetString(arguments[0].GetIntValue(exm), (int)arguments[1].GetIntValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlImportMapXmlMethod : FunctionMethod
		{
			public SqlImportMapXmlMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ImportMapXml(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), arguments[2].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlImportDtXmlMethod : FunctionMethod
		{
			public SqlImportDtXmlMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.String, EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ImportDtXml(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), arguments[2].GetStrValue(exm), arguments[3].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlImportXmlCustomMethod : FunctionMethod
		{
			public SqlImportXmlCustomMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.String, EraType.String, EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ImportXmlCustom(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), arguments[2].GetStrValue(exm), arguments[3].GetStrValue(exm), arguments[4].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlExportMapXmlMethod : FunctionMethod
		{
			public SqlExportMapXmlMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ExportMapXml(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), arguments[2].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}

		private sealed class SqlExportDtXmlMethod : FunctionMethod
		{
			public SqlExportDtXmlMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.String, EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				try
				{
					return ModernSqlManager.ExportDtXml(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), arguments[2].GetStrValue(exm), arguments[3].GetStrValue(exm));
				}
				catch (CodeEE)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new CodeEE(Name + ": " + ex.Message);
				}
			}
		}
	}
}
