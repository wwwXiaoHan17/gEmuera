using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Function
{
    internal static partial class FunctionMethodCreator
    {
        #region MAP functions

        private sealed class MapCreateMethod : FunctionMethod
        {
            public MapCreateMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string mapName = arguments[0].GetStrValue(exm) ?? "";
                var maps = RuntimeDataStore.Maps;
                if (maps.ContainsKey(mapName))
                    return 0;
                maps[mapName] = new Dictionary<string, string>();
                return 1;
            }
        }

        private sealed class MapExistMethod : FunctionMethod
        {
            public MapExistMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string mapName = arguments[0].GetStrValue(exm) ?? "";
                return RuntimeDataStore.Maps.ContainsKey(mapName) ? 1 : 0;
            }
        }

        private sealed class MapReleaseMethod : FunctionMethod
        {
            public MapReleaseMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string mapName = arguments[0].GetStrValue(exm) ?? "";
                var maps = RuntimeDataStore.Maps;
                if (maps.ContainsKey(mapName))
                    maps.Remove(mapName);
                return 1;
            }
        }

        private sealed class MapSetMethod : FunctionMethod
        {
            public MapSetMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string), typeof(string), typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return -1;
                string key = arguments[1].GetStrValue(exm) ?? "";
                string value = arguments[2].GetStrValue(exm) ?? "";
                map[key] = value;
                return 1;
            }
        }

        private sealed class MapHasMethod : FunctionMethod
        {
            public MapHasMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string), typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return -1;
                string key = arguments[1].GetStrValue(exm) ?? "";
                return map.ContainsKey(key) ? 1 : 0;
            }
        }

        private sealed class MapRemoveMethod : FunctionMethod
        {
            public MapRemoveMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string), typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return -1;
                string key = arguments[1].GetStrValue(exm) ?? "";
                map.Remove(key);
                return 1;
            }
        }

        private sealed class MapClearMethod : FunctionMethod
        {
            public MapClearMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return -1;
                map.Clear();
                return 1;
            }
        }

        private sealed class MapSizeMethod : FunctionMethod
        {
            public MapSizeMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return -1;
                return map.Count;
            }
        }

        private sealed class MapGetMethod : FunctionMethod
        {
            public MapGetMethod()
            {
                ReturnType = typeof(string);
                argumentTypeArray = new Type[] { typeof(string), typeof(string) };
                CanRestructure = false;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return "";
                string key = arguments[1].GetStrValue(exm) ?? "";
                return map.TryGetValue(key, out var value) ? value ?? "" : "";
            }
        }

        private sealed class MapGetKeysMethod : FunctionMethod
        {
            public MapGetKeysMethod()
            {
                ReturnType = typeof(string);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 3)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments.Length >= 2 && arguments[1] != null && !arguments[1].IsInteger)
                    return name + "関数の2番目の引数が整数ではありません";
                if (arguments.Length == 3 && (arguments[1] == null || !(arguments[1] is VariableTerm) || !((VariableTerm)arguments[1]).Identifier.IsString || !((VariableTerm)arguments[1]).Identifier.IsArray1D))
                    return name + "関数の2番目の引数は文字列型1次元配列変数である必要があります";
                if (arguments.Length == 3 && (arguments[2] == null || !arguments[2].IsInteger))
                    return name + "関数の3番目の引数が整数ではありません";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return "";
                var values = map.Keys.ToArray();
                if (arguments.Length == 1)
                    return string.Join(",", values);
                if (arguments.Length == 2)
                {
                    if (arguments[1].GetIntValue(exm) == 0)
                        return "";
                    WriteStringResults(exm, null, values);
                    SetIntegerResult(exm, 0, values.Length);
                    return values.Length > 0 ? values[0] : "";
                }
                if (arguments[2].GetIntValue(exm) == 0)
                    return "";
                WriteStringResults(exm, arguments[1] as VariableTerm, values);
                SetIntegerResult(exm, 0, values.Length);
                return "";
            }
        }

        private sealed class MapValuesMethod : FunctionMethod
        {
            public MapValuesMethod()
            {
                ReturnType = typeof(string);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 3)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments.Length >= 2 && arguments[1] != null && !arguments[1].IsInteger)
                    return name + "関数の2番目の引数が整数ではありません";
                if (arguments.Length == 3 && (arguments[1] == null || !(arguments[1] is VariableTerm) || !((VariableTerm)arguments[1]).Identifier.IsString || !((VariableTerm)arguments[1]).Identifier.IsArray1D))
                    return name + "関数の2番目の引数は文字列型1次元配列変数である必要があります";
                if (arguments.Length == 3 && (arguments[2] == null || !arguments[2].IsInteger))
                    return name + "関数の3番目の引数が整数ではありません";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return "";
                var values = map.Values.ToArray();
                if (arguments.Length == 1)
                    return string.Join(",", values);
                if (arguments.Length == 2)
                {
                    if (arguments[1].GetIntValue(exm) == 0)
                        return "";
                    WriteStringResults(exm, null, values);
                    SetIntegerResult(exm, 0, values.Length);
                    return values.Length > 0 ? values[0] : "";
                }
                if (arguments[2].GetIntValue(exm) == 0)
                    return "";
                WriteStringResults(exm, arguments[1] as VariableTerm, values);
                SetIntegerResult(exm, 0, values.Length);
                return "";
            }
        }

        private sealed class MapToStringMethod : FunctionMethod
        {
            public MapToStringMethod()
            {
                ReturnType = typeof(string);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 3)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null || !arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return "";
                string entrySeparator = arguments.Length > 1 ? arguments[1].GetStrValue(exm) ?? "" : ",";
                string keyValueSeparator = arguments.Length > 2 ? arguments[2].GetStrValue(exm) ?? "" : "=";
                return string.Join(entrySeparator, map.Select(pair => pair.Key + keyValueSeparator + pair.Value));
            }
        }

        private sealed class MapFromStringMethod : FunctionMethod
        {
            public MapFromStringMethod()
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
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return 0;
                string data = arguments[1].GetStrValue(exm) ?? "";
                if (data.Length == 0)
                    return 0;
                string entrySeparator = arguments.Length > 2 ? arguments[2].GetStrValue(exm) ?? "" : ",";
                string keyValueSeparator = arguments.Length > 3 ? arguments[3].GetStrValue(exm) ?? "" : "=";
                if (entrySeparator.Length == 0 || keyValueSeparator.Length == 0)
                    return 0;

                int count = 0;
                var entries = data.Split(new[] { entrySeparator }, StringSplitOptions.None);
                foreach (string entry in entries)
                {
                    if (entry.Length == 0)
                        continue;
                    int index = entry.IndexOf(keyValueSeparator, StringComparison.Ordinal);
                    if (index < 0)
                        continue;
                    map[entry.Substring(0, index)] = entry.Substring(index + keyValueSeparator.Length);
                    count++;
                }
                return count;
            }
        }

        private sealed class MapToXmlMethod : FunctionMethod
        {
            public MapToXmlMethod()
            {
                ReturnType = typeof(string);
                argumentTypeArray = new Type[] { typeof(string) };
                CanRestructure = false;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return "";
                var builder = new StringBuilder();
                builder.Append("<map>");
                foreach (var pair in map)
                    builder.Append("<p><k>").Append(pair.Key).Append("</k><v>").Append(pair.Value).Append("</v></p>");
                builder.Append("</map>");
                return builder.ToString();
            }
        }

        private sealed class MapFromXmlMethod : FunctionMethod
        {
            public MapFromXmlMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string), typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return 0;
                var document = new XmlDocument();
                string xml = arguments[1].GetStrValue(exm) ?? "";
                try
                {
                    document.LoadXml(xml);
                }
                catch (XmlException e)
                {
                    throw new CodeEE("MAP_FROMXML received invalid XML: " + e.Message);
                }

                var nodes = document.SelectNodes("/map/p");
                if (nodes == null)
                    return 1;
                foreach (XmlNode node in nodes)
                {
                    var key = node.SelectSingleNode("./k");
                    var value = node.SelectSingleNode("./v");
                    if (key == null || value == null)
                        continue;
                    map[key.InnerText] = value.InnerXml;
                }
                return 1;
            }
        }

        private sealed class MapMergeMethod : FunctionMethod
        {
            public MapMergeMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string), typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var destination))
                    return 0;
                if (!TryGetMap(arguments[1].GetStrValue(exm) ?? "", out var source))
                    return 0;
                foreach (var pair in source)
                    destination[pair.Key] = pair.Value;
                return 1;
            }
        }

        private sealed class MapRemoveIfMethod : FunctionMethod
        {
            public MapRemoveIfMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = new Type[] { typeof(string), typeof(string), typeof(string) };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return 0;
                string matchValue = arguments[1].GetStrValue(exm) ?? "";
                string mode = arguments[2].GetStrValue(exm) ?? "";
                var toRemove = map.Where(pair => MapPredicate(pair, matchValue, mode)).Select(pair => pair.Key).ToArray();
                if (toRemove.Length == 0 && !IsKnownMapPredicateMode(mode))
                    return -1;
                for (int i = 0; i < toRemove.Length; i++)
                    map.Remove(toRemove[i]);
                return toRemove.Length;
            }
        }

        private sealed class MapFindKeyMethod : FunctionMethod
        {
            public MapFindKeyMethod()
            {
                ReturnType = typeof(string);
                argumentTypeArray = new Type[] { typeof(string), typeof(string), typeof(string) };
                CanRestructure = false;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryGetMap(arguments[0].GetStrValue(exm) ?? "", out var map))
                    return "";
                string matchValue = arguments[1].GetStrValue(exm) ?? "";
                string mode = arguments[2].GetStrValue(exm) ?? "";
                if (!IsKnownMapPredicateMode(mode))
                {
                    SetIntegerResult(exm, 0, 0);
                    return "";
                }
                var keys = map.Where(pair => MapPredicate(pair, matchValue, mode)).Select(pair => pair.Key).ToArray();
                SetIntegerResult(exm, 0, keys.Length);
                return string.Join(",", keys);
            }
        }

        static bool TryGetMap(string name, out Dictionary<string, string> map)
        {
            return RuntimeDataStore.Maps.TryGetValue(name ?? "", out map);
        }

        static bool IsKnownMapPredicateMode(string mode)
        {
            return mode == "KEY_CONTAINS"
                || mode == "KEY_PREFIX"
                || mode == "KEY_SUFFIX"
                || mode == "VAL_CONTAINS"
                || mode == "VAL_EQ"
                || mode == "VAL_NE";
        }

        static bool MapPredicate(KeyValuePair<string, string> pair, string matchValue, string mode)
        {
            return mode switch
            {
                "KEY_CONTAINS" => pair.Key.Contains(matchValue),
                "KEY_PREFIX" => pair.Key.StartsWith(matchValue),
                "KEY_SUFFIX" => pair.Key.EndsWith(matchValue),
                "VAL_CONTAINS" => (pair.Value ?? "").Contains(matchValue),
                "VAL_EQ" => pair.Value == matchValue,
                "VAL_NE" => pair.Value != matchValue,
                _ => false,
            };
        }

        static void WriteStringResults(ExpressionMediator exm, VariableTerm destination, string[] values)
        {
            string[] target;
            if (destination != null && destination.Identifier.IsString && destination.Identifier.IsArray1D)
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
            target = exm.VEvaluator.RESULTS_ARRAY;
            int max = Math.Min(values.Length, target.Length);
            for (int i = 0; i < max; i++)
                target[i] = values[i];
        }

        static void SetIntegerResult(ExpressionMediator exm, int index, long value)
        {
            var resultArray = exm.VEvaluator.RESULT_ARRAY;
            if (index >= 0 && index < resultArray.Length)
                resultArray[index] = value;
        }

        #endregion
    }
}
