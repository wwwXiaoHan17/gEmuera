using System;
using System.Collections.Generic;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 企业级说明：最小 TOML 解析器，只负责文本 -> 键值对，不感知日志类别或 Godot 路径。
    /// 不引入外部 NuGet TOML 包，避免网络受限环境恢复失败和 APK 体积膨胀。
    /// 支持范围：空行、# 注释、section（含嵌套如 [logging.categories]）、bool/int/string、行尾注释。
    /// 暂不支持：数组、多行字符串、日期、浮点数、inline table。
    /// 解析失败时返回已解析部分，不抛异常到 Godot 主循环。
    /// </summary>
    public static class RuntimeTomlParser
    {
        public readonly struct ParseResult
        {
            public readonly Dictionary<string, Dictionary<string, string>> Sections;
            public readonly List<ParseError> Errors;
            public readonly bool HasErrors;

            public ParseResult(Dictionary<string, Dictionary<string, string>> sections, List<ParseError> errors)
            {
                Sections = sections ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                Errors = errors ?? new List<ParseError>();
                HasErrors = Errors.Count > 0;
            }
        }

        public readonly struct ParseError
        {
            public readonly int LineNumber;
            public readonly string RawLine;
            public readonly string Key;
            public readonly string Reason;

            public ParseError(int lineNumber, string rawLine, string key, string reason)
            {
                LineNumber = lineNumber;
                RawLine = rawLine ?? "";
                Key = key ?? "";
                Reason = reason ?? "";
            }

            public override string ToString()
            {
                return $"line={LineNumber} key={Key} raw={EscapeForLog(RawLine)} reason={Reason}";
            }
        }

        public static ParseResult Parse(string text)
        {
            var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<ParseError>();
            string currentSection = "";
            sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(text))
                return new ParseResult(sections, errors);

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string rawLine = lines[i];
                int lineNumber = i + 1;
                string line = rawLine.Trim();

                if (string.IsNullOrEmpty(line))
                    continue;
                if (line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                // Section
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    if (!sections.ContainsKey(currentSection))
                        sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                int eqIndex = line.IndexOf('=', StringComparison.Ordinal);
                if (eqIndex < 0)
                {
                    errors.Add(new ParseError(lineNumber, rawLine, "", "missing_equals"));
                    continue;
                }

                string key = line.Substring(0, eqIndex).Trim();
                string valuePart = line.Substring(eqIndex + 1).Trim();

                int commentIndex = FindUnquotedHash(valuePart);
                if (commentIndex >= 0)
                    valuePart = valuePart.Substring(0, commentIndex).TrimEnd();

                if (string.IsNullOrEmpty(key))
                {
                    errors.Add(new ParseError(lineNumber, rawLine, "", "empty_key"));
                    continue;
                }

                string parsedValue = TryParseValue(valuePart, out string parseError);
                if (parseError != null)
                {
                    errors.Add(new ParseError(lineNumber, rawLine, key, parseError));
                    continue;
                }

                sections[currentSection][key] = parsedValue;
            }

            return new ParseResult(sections, errors);
        }

        static int FindUnquotedHash(string value)
        {
            bool inString = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }
                if (!inString && c == '#')
                    return i;
            }
            return -1;
        }

        static string TryParseValue(string raw, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(raw))
                return "";

            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                return "true";
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                return "false";

            if (raw.StartsWith("\"", StringComparison.Ordinal) && raw.EndsWith("\"", StringComparison.Ordinal))
            {
                if (raw.Length < 2)
                {
                    error = "unclosed_string";
                    return null;
                }
                return raw.Substring(1, raw.Length - 2);
            }

            bool isInt = true;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (i == 0 && c == '-')
                    continue;
                if (c < '0' || c > '9')
                {
                    isInt = false;
                    break;
                }
            }
            if (isInt && raw.Length > 0 && !(raw.Length == 1 && raw[0] == '-'))
                return raw;

            return raw;
        }

        static string EscapeForLog(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "";
            return raw.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        }
    }
}
