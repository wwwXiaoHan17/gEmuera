using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Function
{
    internal static partial class FunctionMethodCreator
    {
        #region XML functions

        private sealed class XmlDocumentMethod : FunctionMethod
        {
            readonly bool create;
            public XmlDocumentMethod(bool create)
            {
                this.create = create;
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                int expected = create ? 2 : 1;
                if (arguments.Length != expected)
                    return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
                if (!IsXmlDocumentKeyArgument(arguments[0]))
                    return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 1);
                if (create && (arguments[1] == null || !arguments[1].IsString))
                    return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 2);
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string key = GetXmlDocumentKey(arguments[0], exm);
                var documents = RuntimeDataStore.XmlDocuments;
                if (create)
                {
                    if (documents.ContainsKey(key))
                        return 0;
                    documents.Add(key, ParseXml(arguments[1].GetStrValue(exm), Name));
                    return 1;
                }
                if (!documents.ContainsKey(key))
                    return 0;
                return 1;
            }
        }

        private sealed class XmlReleaseMethod : FunctionMethod
        {
            public XmlReleaseMethod()
            {
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                return CheckSingleXmlDocumentKeyArgument(name, arguments);
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string key = GetXmlDocumentKey(arguments[0], exm);
                var documents = RuntimeDataStore.XmlDocuments;
                if (documents.ContainsKey(key))
                    documents.Remove(key);
                return 1;
            }
        }

        private sealed class XmlToStrMethod : FunctionMethod
        {
            public XmlToStrMethod()
            {
                ReturnType = typeof(string);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                return CheckSingleXmlDocumentKeyArgument(name, arguments);
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return TryGetXmlDocument(GetXmlDocumentKey(arguments[0], exm), out var document)
                    ? document.OuterXml
                    : "";
            }
        }

        private sealed class XmlGetMethod : FunctionMethod
        {
            readonly bool byName;
            public XmlGetMethod(bool byName)
            {
                this.byName = byName;
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
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[1] == null || !arguments[1].IsString)
                    return name + "関数の2番目の引数が文字列ではありません";
                if (arguments.Length >= 3 && arguments[2] != null && !arguments[2].IsInteger && !(arguments[2] is VariableTerm))
                    return name + "関数の3番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryLoadXmlSource(arguments[0], byName, exm, out var document, out _))
                    return -1;
                var nodes = SelectXmlNodes(document, arguments[1].GetStrValue(exm), Name);
                long outputStyle = arguments.Length == 4 ? arguments[3].GetIntValue(exm) : 0;
                if (arguments.Length >= 3)
                {
                    var values = new List<string>(nodes.Count);
                    for (int i = 0; i < nodes.Count; i++)
                        values.Add(ReadXmlNode(nodes[i], outputStyle));
                    if (arguments[2].IsInteger)
                    {
                        if (arguments[2].GetIntValue(exm) != 0)
                            WriteStringResults(exm, null, values.ToArray());
                    }
                    else
                        WriteStringResults(exm, arguments[2] as VariableTerm, values.ToArray());
                }
                return nodes.Count;
            }
        }

        private sealed class XmlSetMethod : FunctionMethod
        {
            readonly bool byName;
            public XmlSetMethod(bool byName)
            {
                this.byName = byName;
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
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[1] == null || !arguments[1].IsString)
                    return name + "関数の2番目の引数が文字列ではありません";
                if (arguments[2] == null || !arguments[2].IsString)
                    return name + "関数の3番目の引数が文字列ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryLoadXmlSource(arguments[0], byName, exm, out var document, out bool saveToSource))
                    return -1;
                var nodes = SelectXmlNodes(document, arguments[1].GetStrValue(exm), Name);
                bool setAllNodes = arguments.Length >= 4 && arguments[3].GetIntValue(exm) != 0;
                long style = arguments.Length == 5 ? arguments[4].GetIntValue(exm) : 0;
                string value = arguments[2].GetStrValue(exm) ?? "";
                if (nodes.Count == 1)
                    SetXmlNode(nodes[0], value, style);
                else if (nodes.Count > 1 && setAllNodes)
                {
                    for (int i = 0; i < nodes.Count; i++)
                        SetXmlNode(nodes[i], value, style);
                }
                SaveXmlSourceIfNeeded(arguments[0], exm, document, saveToSource);
                return nodes.Count;
            }
        }

        private sealed class XmlAddNodeMethod : FunctionMethod
        {
            readonly bool isAttribute;
            readonly bool byName;
            public XmlAddNodeMethod(bool isAttribute, bool byName)
            {
                this.isAttribute = isAttribute;
                this.byName = byName;
                ReturnType = typeof(Int64);
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 3)
                    return name + "関数には少なくとも3つの引数が必要です";
                if (isAttribute && arguments.Length > 6)
                    return name + "関数の引数が多すぎます";
                if (!isAttribute && arguments.Length > 5)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[1] == null || !arguments[1].IsString)
                    return name + "関数の2番目の引数が文字列ではありません";
                if (arguments[2] == null || !arguments[2].IsString)
                    return name + "関数の3番目の引数が文字列ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryLoadXmlSource(arguments[0], byName, exm, out var document, out bool saveToSource))
                    return -1;
                int methodPosition = isAttribute ? 5 : 4;
                int setAllPosition = isAttribute ? 6 : 5;
                int method = arguments.Length >= methodPosition ? NormalizeXmlInsertMethod(arguments[methodPosition - 1].GetIntValue(exm)) : 0;
                bool setAllNodes = arguments.Length == setAllPosition && arguments[setAllPosition - 1].GetIntValue(exm) != 0;
                var nodes = SelectXmlNodes(document, arguments[1].GetStrValue(exm), Name);
                if (nodes.Count == 0)
                    return 0;

                Func<XmlNode> createChild;
                if (!isAttribute)
                {
                    var sourceDocument = ParseXml(arguments[2].GetStrValue(exm), Name);
                    var sourceNode = sourceDocument.DocumentElement;
                    createChild = () => document.ImportNode(sourceNode, true);
                }
                else
                {
                    string attributeName = arguments[2].GetStrValue(exm) ?? "";
                    string attributeValue = arguments.Length >= 4 ? arguments[3].GetStrValue(exm) ?? "" : "";
                    createChild = () =>
                    {
                        var attribute = document.CreateAttribute(attributeName);
                        attribute.Value = attributeValue;
                        return attribute;
                    };
                }

                if (nodes.Count == 1)
                {
                    if (!InsertXmlNode(nodes[0], createChild(), method, isAttribute) && method > 0)
                        return 0;
                }
                else if (setAllNodes)
                {
                    for (int i = 0; i < nodes.Count; i++)
                        InsertXmlNode(nodes[i], createChild(), method, isAttribute);
                }
                SaveXmlSourceIfNeeded(arguments[0], exm, document, saveToSource);
                return nodes.Count;
            }
        }

        private sealed class XmlRemoveNodeMethod : FunctionMethod
        {
            readonly bool isAttribute;
            readonly bool byName;
            public XmlRemoveNodeMethod(bool isAttribute, bool byName)
            {
                this.isAttribute = isAttribute;
                this.byName = byName;
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
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[1] == null || !arguments[1].IsString)
                    return name + "関数の2番目の引数が文字列ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (!TryLoadXmlSource(arguments[0], byName, exm, out var document, out bool saveToSource))
                    return -1;
                var nodes = SelectXmlNodes(document, arguments[1].GetStrValue(exm), Name);
                bool setAllNodes = arguments.Length == 3 && arguments[2].GetIntValue(exm) != 0;
                if (nodes.Count == 1)
                {
                    if (!RemoveXmlNode(nodes[0], isAttribute))
                        return 0;
                }
                else if (nodes.Count > 1 && setAllNodes)
                {
                    for (int i = 0; i < nodes.Count; i++)
                        RemoveXmlNode(nodes[i], isAttribute);
                }
                SaveXmlSourceIfNeeded(arguments[0], exm, document, saveToSource);
                return nodes.Count;
            }
        }

        private sealed class XmlReplaceMethod : FunctionMethod
        {
            readonly bool byName;
            public XmlReplaceMethod(bool byName)
            {
                this.byName = byName;
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
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                var newDocument = ParseXml(arguments.Length > 2 ? arguments[2].GetStrValue(exm) : arguments[1].GetStrValue(exm), Name);
                if (arguments.Length == 2)
                {
                    string key = GetXmlDocumentKey(arguments[0], exm);
                    if (!RuntimeDataStore.XmlDocuments.ContainsKey(key))
                        return -1;
                    RuntimeDataStore.XmlDocuments[key] = newDocument;
                    return 1;
                }

                if (!TryLoadXmlSource(arguments[0], byName, exm, out var document, out bool saveToSource))
                    return -1;
                var nodes = SelectXmlNodes(document, arguments[1].GetStrValue(exm), Name);
                bool setAllNodes = arguments.Length >= 4 && arguments[3].GetIntValue(exm) != 0;
                if (nodes.Count == 1)
                {
                    if (!ReplaceXmlNode(nodes[0], document.ImportNode(newDocument.DocumentElement, true)))
                        return 0;
                }
                else if (nodes.Count > 1 && setAllNodes)
                {
                    for (int i = 0; i < nodes.Count; i++)
                        ReplaceXmlNode(nodes[i], document.ImportNode(newDocument.DocumentElement, true));
                }
                SaveXmlSourceIfNeeded(arguments[0], exm, document, saveToSource);
                return nodes.Count;
            }
        }

        static string CheckSingleXmlDocumentKeyArgument(string name, IOperandTerm[] arguments)
        {
            if (arguments.Length != 1)
                return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
            if (!IsXmlDocumentKeyArgument(arguments[0]))
                return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 1);
            return null;
        }

        static bool IsXmlDocumentKeyArgument(IOperandTerm term)
        {
            return term != null && (term.IsString || term.IsInteger);
        }

        static string GetXmlDocumentKey(IOperandTerm term, ExpressionMediator exm)
        {
            return term.IsString ? term.GetStrValue(exm) ?? "" : term.GetIntValue(exm).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        static bool TryGetXmlDocument(string key, out XmlDocument document)
        {
            return RuntimeDataStore.XmlDocuments.TryGetValue(key ?? "", out document);
        }

        static bool TryGetStoredXmlDocument(IOperandTerm source, bool byName, ExpressionMediator exm, out string key, out XmlDocument document)
        {
            key = null;
            document = null;
            if (!source.IsInteger && !(byName && source.IsString))
                return false;
            key = GetXmlDocumentKey(source, exm);
            return TryGetXmlDocument(key, out document);
        }

        static bool TryLoadXmlSource(IOperandTerm source, bool byName, ExpressionMediator exm, out XmlDocument document, out bool saveToSource)
        {
            saveToSource = false;
            if (TryGetStoredXmlDocument(source, byName, exm, out _, out document))
                return true;
            if (source.IsInteger || byName)
            {
                document = null;
                return false;
            }
            document = ParseXml(source.GetStrValue(exm), "XML");
            saveToSource = true;
            return true;
        }

        static XmlDocument ParseXml(string xml, string functionName)
        {
            var document = new XmlDocument();
            try
            {
                document.LoadXml(xml ?? "");
                return document;
            }
            catch (XmlException e)
            {
                throw new CodeEE(functionName + " received invalid XML: " + e.Message);
            }
        }

        static XmlNodeList SelectXmlNodes(XmlDocument document, string path, string functionName)
        {
            try
            {
                return document.SelectNodes(path ?? "");
            }
            catch (XPathException e)
            {
                throw new CodeEE(functionName + " received invalid XPath: " + e.Message);
            }
        }

        static string ReadXmlNode(XmlNode node, long style)
        {
            return style switch
            {
                1 => node.InnerText,
                2 => node.InnerXml,
                3 => node.OuterXml,
                4 => node.Name,
                _ => node.Value ?? "",
            };
        }

        static void SetXmlNode(XmlNode node, string value, long style)
        {
            switch (style)
            {
                case 1:
                    node.InnerText = value;
                    break;
                case 2:
                    node.InnerXml = value;
                    break;
                default:
                    node.Value = value;
                    break;
            }
        }

        static int NormalizeXmlInsertMethod(long method)
        {
            return method < 0 || method > 2 ? 0 : (int)method;
        }

        static bool InsertXmlNode(XmlNode targetNode, XmlNode newChild, int method, bool isAttribute)
        {
            if (!isAttribute)
            {
                switch (method)
                {
                    case 0:
                        targetNode.AppendChild(newChild);
                        return true;
                    case 1:
                        if (targetNode.ParentNode == null)
                            return false;
                        targetNode.ParentNode.InsertBefore(newChild, targetNode);
                        return true;
                    case 2:
                        if (targetNode.ParentNode == null)
                            return false;
                        targetNode.ParentNode.InsertAfter(newChild, targetNode);
                        return true;
                    default:
                        return false;
                }
            }

            if (newChild is not XmlAttribute attribute)
                return false;
            if (method > 0 && targetNode is not XmlAttribute)
                return false;
            switch (method)
            {
                case 0:
                    if (targetNode is not XmlElement element)
                        return false;
                    element.Attributes.Append(attribute);
                    return true;
                case 1:
                    if (targetNode is not XmlAttribute before || before.OwnerElement == null)
                        return false;
                    before.OwnerElement.Attributes.InsertBefore(attribute, before);
                    return true;
                case 2:
                    if (targetNode is not XmlAttribute after || after.OwnerElement == null)
                        return false;
                    after.OwnerElement.Attributes.InsertAfter(attribute, after);
                    return true;
                default:
                    return false;
            }
        }

        static bool RemoveXmlNode(XmlNode node, bool isAttribute)
        {
            if (isAttribute)
            {
                if (node is not XmlAttribute attribute || attribute.OwnerElement == null)
                    return false;
                attribute.OwnerElement.Attributes.Remove(attribute);
                return true;
            }
            if (node.ParentNode == null)
                return false;
            node.ParentNode.RemoveChild(node);
            return true;
        }

        static bool ReplaceXmlNode(XmlNode node, XmlNode newNode)
        {
            if (node.ParentNode == null)
                return false;
            node.ParentNode.ReplaceChild(newNode, node);
            return true;
        }

        static void SaveXmlSourceIfNeeded(IOperandTerm source, ExpressionMediator exm, XmlDocument document, bool saveToSource)
        {
            if (!saveToSource)
                return;
            if (source is VariableTerm term && term.Identifier.IsString)
                term.Identifier.SetValue(document.OuterXml, new long[0]);
        }

        #endregion
    }
}
