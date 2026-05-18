using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Function
{
    internal static partial class FunctionMethodCreator
    {
        #region DT functions

        private sealed class DtCreateMethod : FunctionMethod
        {
            public DtCreateMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string key = arguments[0].GetStrValue(exm) ?? "";
                var tables = RuntimeDataStore.DataTables;
                if (tables.ContainsKey(key))
                    return 0;
                tables[key] = CreateDataTable(key);
                return 1;
            }
        }

        private sealed class DtExistMethod : FunctionMethod
        {
            public DtExistMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string key = arguments[0].GetStrValue(exm) ?? "";
                return RuntimeDataStore.DataTables.ContainsKey(key) ? 1 : 0;
            }
        }

        private sealed class DtReleaseMethod : FunctionMethod
        {
            public DtReleaseMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string key = arguments[0].GetStrValue(exm) ?? "";
                var tables = RuntimeDataStore.DataTables;
                if (tables.ContainsKey(key))
                    tables.Remove(key);
                return 1;
            }
        }

        private sealed class DtClearMethod : FunctionMethod
        {
            public DtClearMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string key = arguments[0].GetStrValue(exm) ?? "";
                var tables = RuntimeDataStore.DataTables;
                if (!tables.ContainsKey(key))
                    return -1;
                tables[key].Clear();
                return 1;
            }
        }

        private sealed class DtNoCaseMethod : FunctionMethod
        {
            public DtNoCaseMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string), typeof(Int64) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string key = arguments[0].GetStrValue(exm) ?? "";
                var tables = RuntimeDataStore.DataTables;
                if (!tables.ContainsKey(key))
                    return -1;
                tables[key].CaseSensitive = arguments[1].GetIntValue(exm) == 0;
                return 1;
            }
        }

        private sealed class DtColumnAddMethod : FunctionMethod
        {
            public DtColumnAddMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments.Length > 4)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments[1] == null || !arguments[1].IsString)
                    return name + "関数の2番目の引数が文字列ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return -1;
                string columnName = arguments[1].GetStrValue(exm) ?? "";
                if (table.Columns.Contains(columnName))
                    return 0;
                Type type = typeof(string);
                if (arguments.Length >= 3)
                {
                    if (arguments[2].IsString)
                        type = DataTableNameToType(arguments[2].GetStrValue(exm));
                    else
                        type = DataTableIntToType(arguments[2].GetIntValue(exm));
                }
                if (type == null)
                    throw new CodeEE(Name + " received an unsupported DataTable column type.");
                var column = table.Columns.Add(columnName, type);
                column.AllowDBNull = arguments.Length != 4 || arguments[3].GetIntValue(exm) != 0;
                return 1;
            }
        }

        private sealed class DtColumnExistMethod : FunctionMethod
        {
            public DtColumnExistMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string), typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return -1;
                string columnName = arguments[1].GetStrValue(exm) ?? "";
                return table.Columns.Contains(columnName) ? DataTableTypeToInt(table.Columns[columnName].DataType) : 0;
            }
        }

        private sealed class DtColumnRemoveMethod : FunctionMethod
        {
            public DtColumnRemoveMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string), typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return -1;
                string columnName = arguments[1].GetStrValue(exm) ?? "";
                if (!table.Columns.Contains(columnName) || string.Equals(columnName, "id", StringComparison.OrdinalIgnoreCase))
                    return 0;
                table.Columns.Remove(columnName);
                return 1;
            }
        }

        private sealed class DtColumnLengthMethod : FunctionMethod
        {
            public DtColumnLengthMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return -1;
                return table.Columns.Count;
            }
        }

        private sealed class DtColumnNamesMethod : FunctionMethod
        {
            public DtColumnNamesMethod()
            {
                ReturnType = typeof(string);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 2)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments.Length == 2 && (arguments[1] == null || !(arguments[1] is VariableTerm) || !((VariableTerm)arguments[1]).Identifier.IsString || !((VariableTerm)arguments[1]).Identifier.IsArray1D))
                    return name + "関数の2番目の引数は文字列型1次元配列変数である必要があります";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return "";
                var names = table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToArray();
                if (arguments.Length == 2)
                    WriteStringResults(exm, arguments[1] as VariableTerm, names);
                else
                    WriteStringResults(exm, null, names);
                SetIntegerResult(exm, 0, names.Length);
                return names.Length > 0 ? names[0] : "";
            }
        }

        private sealed class DtRowAddMethod : FunctionMethod
        {
            public DtRowAddMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return -1;
                var row = table.NewRow();
                row["id"] = RuntimeDataStore.NextDataTableRowId++;
                long changed = SetDataTableRowValues(row, table, exm, arguments, 1);
                table.Rows.Add(row);
                return Convert.ToInt64(row["id"], CultureInfo.InvariantCulture);
            }
        }

        private sealed class DtRowSetMethod : FunctionMethod
        {
            public DtRowSetMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments[1] == null || !arguments[1].IsInteger)
                    return name + "関数の2番目の引数が整数ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return -1;
                var row = table.Rows.Find(arguments[1].GetIntValue(exm));
                if (row == null)
                    return -2;
                return SetDataTableRowValues(row, table, exm, arguments, 2);
            }
        }

        private sealed class DtRowRemoveMethod : FunctionMethod
        {
            public DtRowRemoveMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments.Length > 3)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments[1] == null || !arguments[1].IsInteger)
                    return name + "関数の2番目の引数が整数ではありません";
                if (arguments.Length == 3 && (arguments[2] == null || !(arguments[2] is VariableTerm) || !((VariableTerm)arguments[2]).Identifier.IsInteger || !((VariableTerm)arguments[2]).Identifier.IsArray1D))
                    return name + "関数の3番目の引数は整数型1次元配列変数である必要があります";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return -1;
                var rows = new System.Collections.Generic.List<DataRow>();
                if (arguments.Length == 2)
                {
                    var row = table.Rows.Find(arguments[1].GetIntValue(exm));
                    if (row != null)
                        rows.Add(row);
                }
                else
                {
                    var ids = ReadIntegerArray(arguments[2] as VariableTerm, exm, arguments[1].GetIntValue(exm));
                    foreach (long id in ids)
                    {
                        var row = table.Rows.Find(id);
                        if (row != null)
                            rows.Add(row);
                    }
                }
                foreach (var row in rows)
                    table.Rows.Remove(row);
                return rows.Count;
            }
        }

        private sealed class DtRowLengthMethod : FunctionMethod
        {
            public DtRowLengthMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return -1;
                return table.Rows.Count;
            }
        }

        private sealed class DtCellGetMethod : FunctionMethod
        {
            public DtCellGetMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 3)
                    return name + "関数には少なくとも3つの引数が必要です";
                if (arguments.Length > 4)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments[1] == null || !arguments[1].IsInteger)
                    return name + "関数の2番目の引数が整数ではありません";
                if (arguments[2] == null || !arguments[2].IsString)
                    return name + "関数の3番目の引数が文字列ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTableCell(exm, arguments, out _, out _, out var value))
                    return 0;
                return value == DBNull.Value ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }

        private sealed class DtCellGetfMethod : FunctionMethod
        {
            public DtCellGetfMethod()
            {
                ReturnType = typeof(double);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 3)
                    return name + "関数には少なくとも3つの引数が必要です";
                if (arguments.Length > 4)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments[1] == null || !arguments[1].IsInteger)
                    return name + "関数の2番目の引数が整数ではありません";
                if (arguments[2] == null || !arguments[2].IsString)
                    return name + "関数の3番目の引数が文字列ではありません";
                return null;
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTableCell(exm, arguments, out _, out _, out var value) || value == DBNull.Value)
                    return new SingleTerm(0.0d);
                return new SingleTerm(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            }
        }

        private sealed class DtCellGetsMethod : FunctionMethod
        {
            public DtCellGetsMethod()
            {
                ReturnType = typeof(string);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 3)
                    return name + "関数には少なくとも3つの引数が必要です";
                if (arguments.Length > 4)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments[1] == null || !arguments[1].IsInteger)
                    return name + "関数の2番目の引数が整数ではありません";
                if (arguments[2] == null || !arguments[2].IsString)
                    return name + "関数の3番目の引数が文字列ではありません";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTableCell(exm, arguments, out _, out _, out var value) || value == DBNull.Value)
                    return "";
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
        }

        private sealed class DtCellIsNullMethod : FunctionMethod
        {
            public DtCellIsNullMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 3)
                    return name + "関数には少なくとも3つの引数が必要です";
                if (arguments.Length > 4)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments[1] == null || !arguments[1].IsInteger)
                    return name + "関数の2番目の引数が整数ではありません";
                if (arguments[2] == null || !arguments[2].IsString)
                    return name + "関数の3番目の引数が文字列ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTableCell(exm, arguments, out _, out _, out var value))
                    return -2;
                return value == DBNull.Value ? 1 : 0;
            }
        }

        private sealed class DtCellSetMethod : FunctionMethod
        {
            public DtCellSetMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 3)
                    return name + "関数には少なくとも3つの引数が必要です";
                if (arguments.Length > 5)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments[1] == null || !arguments[1].IsInteger)
                    return name + "関数の2番目の引数が整数ではありません";
                if (arguments[2] == null || !arguments[2].IsString)
                    return name + "関数の3番目の引数が文字列ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return -1;
                bool asId = arguments.Length == 5 && arguments[4].GetIntValue(exm) != 0;
                long index = arguments[1].GetIntValue(exm);
                string columnName = arguments[2].GetStrValue(exm) ?? "";
                if (string.Equals(columnName, "id", StringComparison.OrdinalIgnoreCase))
                    return 0;
                var row = GetDataTableRow(table, index, asId);
                if (row == null || !table.Columns.Contains(columnName))
                    return -3;
                if (arguments.Length < 4)
                {
                    row[columnName] = DBNull.Value;
                    return 1;
                }
                try
                {
                    SetDataTableValue(row, table.Columns[columnName], arguments[3], exm);
                    return 1;
                }
                catch
                {
                    return -2;
                }
            }
        }

        private sealed class DtCellSetfMethod : FunctionMethod
        {
            public DtCellSetfMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 4)
                    return name + "関数には少なくとも4つの引数が必要です";
                if (arguments.Length > 5)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments[1] == null || !arguments[1].IsInteger)
                    return name + "関数の2番目の引数が整数ではありません";
                if (arguments[2] == null || !arguments[2].IsString)
                    return name + "関数の3番目の引数が文字列ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return -1;
                bool asId = arguments.Length == 5 && arguments[4].GetIntValue(exm) != 0;
                long index = arguments[1].GetIntValue(exm);
                string columnName = arguments[2].GetStrValue(exm) ?? "";
                if (string.Equals(columnName, "id", StringComparison.OrdinalIgnoreCase))
                    return 0;
                var row = GetDataTableRow(table, index, asId);
                if (row == null || !table.Columns.Contains(columnName))
                    return -3;
                try
                {
                    row[columnName] = arguments[3].GetFloatValue(exm);
                    return 1;
                }
                catch
                {
                    return -2;
                }
            }
        }

        private sealed class DtSelectMethod : FunctionMethod
        {
            public DtSelectMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 4)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments.Length == 4 && (arguments[3] == null || !(arguments[3] is VariableTerm) || !((VariableTerm)arguments[3]).Identifier.IsInteger || !((VariableTerm)arguments[3]).Identifier.IsArray1D))
                    return name + "関数の4番目の引数は整数型1次元配列変数である必要があります";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return -1;
                string filter = arguments.Length > 1 && arguments[1] != null ? arguments[1].GetStrValue(exm) : null;
                string sort = arguments.Length > 2 && arguments[2] != null ? arguments[2].GetStrValue(exm) : null;
                DataRow[] rows = !string.IsNullOrEmpty(sort)
                    ? table.Select(filter ?? "", sort)
                    : !string.IsNullOrEmpty(filter)
                        ? table.Select(filter)
                        : table.Select();
                var ids = rows.Select(row => Convert.ToInt64(row["id"], CultureInfo.InvariantCulture)).ToArray();
                if (arguments.Length == 4)
                {
                    WriteIntegerResults(exm, arguments[3] as VariableTerm, ids);
                }
                else
                {
                    SetIntegerResult(exm, 0, ids.Length);
                    for (int i = 0; i < ids.Length; i++)
                        SetIntegerResult(exm, i + 1, ids[i]);
                }
                return ids.Length;
            }
        }

        private sealed class DtToXmlMethod : FunctionMethod
        {
            public DtToXmlMethod()
            {
                ReturnType = typeof(string);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 2)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments.Length == 2 && (arguments[1] == null || !(arguments[1] is VariableTerm) || !((VariableTerm)arguments[1]).Identifier.IsString || !((VariableTerm)arguments[1]).Identifier.IsArray1D))
                    return name + "関数の2番目の引数は文字列型1次元配列変数である必要があります";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out var table))
                    return "";
                var schemaBuilder = new StringBuilder();
                using (var writer = new StringWriter(schemaBuilder, CultureInfo.InvariantCulture))
                    table.WriteXmlSchema(writer);
                if (arguments.Length == 2)
                    WriteStringResults(exm, arguments[1] as VariableTerm, new[] { schemaBuilder.ToString() });
                else
                    WriteStringResults(exm, null, new[] { "", schemaBuilder.ToString() });

                var dataBuilder = new StringBuilder();
                using (var writer = new StringWriter(dataBuilder, CultureInfo.InvariantCulture))
                    table.WriteXml(writer);
                return dataBuilder.ToString();
            }
        }

        private sealed class DtFromXmlMethod : FunctionMethod
        {
            public DtFromXmlMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string), typeof(string), typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string key = arguments[0].GetStrValue(exm) ?? "";
                try
                {
                    var table = new DataTable(key);
                    using (var reader = new StringReader(arguments[1].GetStrValue(exm) ?? ""))
                        table.ReadXmlSchema(reader);
                    using (var reader = new StringReader(arguments[2].GetStrValue(exm) ?? ""))
                        table.ReadXml(reader);
                    RuntimeDataStore.DataTables[key] = table;
                    if (table.PrimaryKey == null || table.PrimaryKey.Length == 0)
                    {
                        if (table.Columns.Contains("id"))
                            table.PrimaryKey = new[] { table.Columns["id"] };
                    }
                    return 1;
                }
                catch
                {
                    return 0;
                }
            }
        }

        static bool TryGetDataTable(string key, out DataTable table)
        {
            return RuntimeDataStore.DataTables.TryGetValue(key ?? "", out table);
        }

        static DataTable CreateDataTable(string key)
        {
            var table = new DataTable(key)
            {
                CaseSensitive = true
            };
            var idColumn = table.Columns.Add("id", typeof(long));
            idColumn.AllowDBNull = false;
            idColumn.Unique = true;
            table.PrimaryKey = new[] { idColumn };
            return table;
        }

        static DataRow GetDataTableRow(DataTable table, long index, bool asId)
        {
            if (asId)
                return table.Rows.Find(index);
            return index >= 0 && index < table.Rows.Count ? table.Rows[(int)index] : null;
        }

        static bool TryGetDataTableCell(ExpressionMediator exm, IOperandTerm[] arguments, out DataTable table, out DataRow row, out object value)
        {
            table = null;
            row = null;
            value = null;
            if (!TryGetDataTable(arguments[0].GetStrValue(exm) ?? "", out table))
                return false;
            bool asId = arguments.Length == 4 && arguments[3].GetIntValue(exm) != 0;
            row = GetDataTableRow(table, arguments[1].GetIntValue(exm), asId);
            string columnName = arguments[2].GetStrValue(exm) ?? "";
            if (row == null || !table.Columns.Contains(columnName))
                return false;
            value = row[columnName];
            return true;
        }

        static long SetDataTableRowValues(DataRow row, DataTable table, ExpressionMediator exm, IOperandTerm[] arguments, int offset)
        {
            if (arguments.Length == offset)
                return 0;
            if (arguments.Length == offset + 3 && arguments[offset] is VariableTerm namesTerm && namesTerm.Identifier.IsString && namesTerm.Identifier.IsArray1D)
            {
                long requested = arguments[offset + 2].GetIntValue(exm);
                if (requested <= 0)
                    return 0;
                var names = ReadStringArray(namesTerm, exm, requested);
                long count = 0;
                if (arguments[offset + 1] is VariableTerm valuesTerm && valuesTerm.Identifier.IsArray1D)
                {
                    if (valuesTerm.Identifier.IsString)
                    {
                        var values = ReadStringArray(valuesTerm, exm, names.Length);
                        for (int i = 0; i < Math.Min(names.Length, values.Length); i++)
                        {
                            SetDataTableStringValue(row, table.Columns[names[i]], values[i]);
                            count++;
                        }
                        return count;
                    }
                    if (valuesTerm.Identifier.IsInteger)
                    {
                        var values = ReadIntegerArray(valuesTerm, exm, names.Length);
                        for (int i = 0; i < Math.Min(names.Length, values.Length); i++)
                        {
                            SetDataTableIntegerValue(row, table.Columns[names[i]], values[i]);
                            count++;
                        }
                        return count;
                    }
                }
            }

            if (((arguments.Length - offset) % 2) != 0)
                throw new CodeEE("DT_ROW_ADD/DT_ROW_SET need column/value pairs.");
            long changed = 0;
            for (int i = offset; i < arguments.Length; i += 2)
            {
                string columnName = arguments[i].GetStrValue(exm) ?? "";
                if (!table.Columns.Contains(columnName))
                    throw new CodeEE(columnName + " is not a DataTable column.");
                SetDataTableValue(row, table.Columns[columnName], arguments[i + 1], exm);
                changed++;
            }
            return changed;
        }

        static void SetDataTableValue(DataRow row, DataColumn column, IOperandTerm value, ExpressionMediator exm)
        {
            if (string.Equals(column.ColumnName, "id", StringComparison.OrdinalIgnoreCase))
                throw new CodeEE("DataTable id column is read-only.");
            if (value == null)
            {
                row[column] = DBNull.Value;
                return;
            }
            if (column.DataType == typeof(string))
                row[column] = value.GetStrValue(exm) ?? "";
            else if (column.DataType == typeof(double))
                row[column] = value.GetFloatValue(exm);
            else
                row[column] = ConvertDataTableInteger(value.GetIntValue(exm), column.DataType);
        }

        static void SetDataTableStringValue(DataRow row, DataColumn column, string value)
        {
            if (column == null)
                throw new CodeEE("DataTable column does not exist.");
            if (column.DataType != typeof(string))
                throw new CodeEE(column.ColumnName + " is not a string column.");
            row[column] = value ?? "";
        }

        static void SetDataTableIntegerValue(DataRow row, DataColumn column, long value)
        {
            if (column == null)
                throw new CodeEE("DataTable column does not exist.");
            if (column.DataType == typeof(string))
                throw new CodeEE(column.ColumnName + " is not an integer column.");
            row[column] = ConvertDataTableInteger(value, column.DataType);
        }

        static long[] ReadIntegerArray(VariableTerm term, ExpressionMediator exm, long maxCount)
        {
            if (term == null || !term.Identifier.IsInteger || !term.Identifier.IsArray1D)
                return Array.Empty<long>();
            int count = Math.Min((int)term.Identifier.GetLength(), ClampCount(maxCount));
            var values = new long[count];
            for (int i = 0; i < count; i++)
                values[i] = term.Identifier.GetIntValue(exm, new long[] { i });
            return values;
        }

        static string[] ReadStringArray(VariableTerm term, ExpressionMediator exm, long maxCount)
        {
            if (term == null || !term.Identifier.IsString || !term.Identifier.IsArray1D)
                return Array.Empty<string>();
            int count = Math.Min((int)term.Identifier.GetLength(), ClampCount(maxCount));
            var values = new string[count];
            for (int i = 0; i < count; i++)
                values[i] = term.Identifier.GetStrValue(exm, new long[] { i }) ?? "";
            return values;
        }

        static int ClampCount(long count)
        {
            if (count <= 0)
                return 0;
            return count > int.MaxValue ? int.MaxValue : (int)count;
        }

        static long DataTableTypeToInt(Type type)
        {
            if (type == typeof(sbyte))
                return 1;
            if (type == typeof(short))
                return 2;
            if (type == typeof(int))
                return 3;
            if (type == typeof(long))
                return 4;
            if (type == typeof(string))
                return 5;
            if (type == typeof(double))
                return 6;
            return long.MaxValue;
        }

        static Type DataTableIntToType(long value)
        {
            return value switch
            {
                1 => typeof(sbyte),
                2 => typeof(short),
                3 => typeof(int),
                4 => typeof(long),
                5 => typeof(string),
                6 => typeof(double),
                _ => null,
            };
        }

        static Type DataTableNameToType(string name)
        {
            return (name ?? "").ToLowerInvariant() switch
            {
                "int8" => typeof(sbyte),
                "int16" => typeof(short),
                "int32" => typeof(int),
                "int64" => typeof(long),
                "string" => typeof(string),
                "float" => typeof(double),
                "double" => typeof(double),
                _ => null,
            };
        }

        static object ConvertDataTableInteger(long value, Type type)
        {
            if (type == typeof(sbyte))
                return (sbyte)Math.Min(Math.Max(value, sbyte.MinValue), sbyte.MaxValue);
            if (type == typeof(short))
                return (short)Math.Min(Math.Max(value, short.MinValue), short.MaxValue);
            if (type == typeof(int))
                return (int)Math.Min(Math.Max(value, int.MinValue), int.MaxValue);
            if (type == typeof(double))
                return (double)value;
            return value;
        }

        static void WriteIntegerResults(ExpressionMediator exm, VariableTerm destination, long[] values)
        {
            long[] target;
            if (destination != null && destination.Identifier.IsInteger && destination.Identifier.IsArray1D)
            {
                try
                {
                    long len = destination.Identifier.GetLength();
                    int count = (int)Math.Min(values.Length, len);
                    for (int i = 0; i < count; i++)
                        destination.Identifier.SetValue(values[i], new long[] { i });
                    return;
                }
                catch { }
            }
            target = exm.VEvaluator.RESULT_ARRAY;
            int max = Math.Min(values.Length, target.Length);
            for (int i = 0; i < max; i++)
                target[i] = values[i];
        }

        #endregion
    }
}
