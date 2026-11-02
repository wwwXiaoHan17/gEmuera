using System.Globalization;
using System.Reflection;
using System.Text;
using Emuera.Compatibility.Packs;
using Xunit;

namespace EmueraFacade.Tests;

/// <summary>
/// CompatPack 契约程序集（Emuera.CompatPack，v1.0）的 public API 冻结基线（T8）。
/// 渲染规范文本：assembly 身份 + 全部 public type/基类/接口/record 位置参数/字段/构造器/
/// 属性/事件/方法。排序一律 Ordinal，文本只用 LF，不含路径/时间/哈希等环境噪声，
/// 因此任何 public 面删除、签名或 record 位置参数变化都会让本用例变红。
/// 基线文件由 csproj 复制到测试输出目录；首次运行（文件缺失）会把 actual 写到输出目录
/// 并失败，提示人工核对后提交为 CompatPackPublicApi.v1.0.txt。
/// </summary>
public class CompatPackPublicApiTests
{
    private const string BaselineFileName = "CompatPackPublicApi.v1.0.txt";

    [Fact]
    public void CompatPackPublicApi_MatchesV1Baseline()
    {
        string actual = CompatPackPublicApiSnapshot.Render(typeof(ICompatPack).Assembly);
        string baselinePath = Path.Combine(AppContext.BaseDirectory, BaselineFileName);
        if (!File.Exists(baselinePath))
        {
            string actualPath = Path.Combine(AppContext.BaseDirectory, "CompatPackPublicApi.v1.0.actual.txt");
            File.WriteAllText(actualPath, actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail("v1.0 API 基线尚未冻结：actual 已写入 " + actualPath
                + "；核对后提交为 " + BaselineFileName + "。");
        }

        // 仓库检出的换行可能被 git autocrlf 改写；canonical 文本固定 LF。
        string expected = File.ReadAllText(baselinePath).Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
    }
}

/// <summary>
/// 规范 API 文本渲染器（只读反射，无副作用）。成员按 kind 分组、组内 Ordinal 排序；
/// 组合类型用简单名 + 泛型参数渲染（不写程序集限定名，避免把契约程序集版本钉进成员签名）。
/// record 判定用编译器生成的 <c>PrintMembers</c>；record 位置参数取"参数名逐一匹配 public
/// 属性名"的最大构造器（兼容 record class/record struct）。
/// </summary>
internal static class CompatPackPublicApiSnapshot
{
    public static string Render(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var builder = new StringBuilder();
        AssemblyName name = assembly.GetName();
        byte[]? token = name.GetPublicKeyToken();
        Append(builder, "# gemuera CompatPack public API baseline v1.0");
        Append(builder, "assembly=" + name.Name
            + "|version=" + name.Version
            + "|culture=" + (string.IsNullOrEmpty(name.CultureName) ? "neutral" : name.CultureName)
            + "|publicKeyToken=" + (token is { Length: > 0 } ? Convert.ToHexString(token).ToLowerInvariant() : "null"));

        Type[] publicTypes = assembly.GetExportedTypes()
            .Where(type => type.IsPublic || type.IsNestedPublic)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        Append(builder, "public-types=" + publicTypes.Length);
        foreach (Type type in publicTypes)
            RenderType(builder, type);
        return builder.ToString();
    }

    private static void RenderType(StringBuilder builder, Type type)
    {
        Append(builder, "type=" + TypeName(type)
            + "|" + DescribeTypeKind(type)
            + "|base=" + (type.BaseType is null ? "<none>" : TypeName(type.BaseType))
            + "|interfaces=" + FormatInterfaces(type));

        if (TryGetRecordPositionalParameters(type, out ParameterInfo[]? positional))
            Append(builder, "  positional=" + FormatParameters(positional));

        foreach (FieldInfo field in type
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(DescribeField, StringComparer.Ordinal))
        {
            Append(builder, "  field=" + DescribeField(field));
        }

        foreach (ConstructorInfo constructor in type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(DescribeConstructor, StringComparer.Ordinal))
        {
            Append(builder, "  ctor=" + DescribeConstructor(constructor));
        }

        foreach (PropertyInfo property in type
            .GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(DescribeProperty, StringComparer.Ordinal))
        {
            Append(builder, "  property=" + DescribeProperty(property));
        }

        foreach (EventInfo @event in type
            .GetEvents(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(DescribeEvent, StringComparer.Ordinal))
        {
            Append(builder, "  event=" + DescribeEvent(@event));
        }

        foreach (MethodInfo method in type
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !IsAccessor(method))
            .OrderBy(DescribeMethod, StringComparer.Ordinal))
        {
            Append(builder, "  method=" + DescribeMethod(method));
        }
    }

    private static bool IsAccessor(MethodInfo method)
        => method.IsSpecialName
            && (method.Name.StartsWith("get_", StringComparison.Ordinal)
                || method.Name.StartsWith("set_", StringComparison.Ordinal)
                || method.Name.StartsWith("add_", StringComparison.Ordinal)
                || method.Name.StartsWith("remove_", StringComparison.Ordinal));

    private static string DescribeTypeKind(Type type)
    {
        if (type.IsInterface)
            return "public interface";

        bool isRecord = IsRecord(type);
        string kind = type.IsValueType
            ? (isRecord ? "record struct" : "struct")
            : (isRecord ? "record class" : "class");
        if (!type.IsValueType && type.IsAbstract && type.IsSealed)
            return "public static " + kind;
        if (type.IsAbstract)
            kind = "abstract " + kind;
        if (type.IsSealed && !type.IsValueType)
            kind = "sealed " + kind;
        if (type.IsValueType && IsReadOnly(type))
            kind = "readonly " + kind;
        return "public " + kind;
    }

    private static bool IsRecord(Type type)
    {
        MethodInfo? printMembers = type.GetMethod(
            "PrintMembers", BindingFlags.Instance | BindingFlags.NonPublic);
        return printMembers is not null && printMembers.ReturnType == typeof(bool);
    }

    private static bool IsReadOnly(Type type)
        => type.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");

    private static bool TryGetRecordPositionalParameters(Type type, out ParameterInfo[] positional)
    {
        positional = Array.Empty<ParameterInfo>();
        if (!IsRecord(type))
            return false;

        HashSet<string> propertyNames = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        ConstructorInfo? primary = type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(constructor => constructor.GetParameters().Length > 0)
            .Where(constructor => constructor.GetParameters().All(parameter =>
                parameter.Name is not null && propertyNames.Contains(parameter.Name)))
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .FirstOrDefault();
        if (primary is null)
            return false;
        positional = primary.GetParameters();
        return true;
    }

    private static string FormatInterfaces(Type type)
    {
        string[] names = type.GetInterfaces()
            .Select(TypeName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        return names.Length == 0 ? "<none>" : string.Join(", ", names);
    }

    private static string DescribeField(FieldInfo field)
    {
        string modifier = field.IsLiteral ? "const " : field.IsStatic ? "static " : "";
        string value = field.IsLiteral ? " = " + FormatLiteralValue(field.GetRawConstantValue()) : "";
        return modifier + TypeName(field.FieldType) + " " + field.Name + value;
    }

    private static string FormatLiteralValue(object? value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        bool boolean => boolean ? "true" : "false",
        char character => "'" + character + "'",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string DescribeConstructor(ConstructorInfo constructor)
        => ".ctor(" + FormatParameters(constructor.GetParameters()) + ")";

    private static string DescribeProperty(PropertyInfo property)
    {
        string modifier = property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true
            ? "static "
            : "";
        var accessors = new List<string>(2);
        if (property.GetMethod is not null && property.GetMethod.IsPublic)
            accessors.Add("get");
        if (property.SetMethod is not null && property.SetMethod.IsPublic)
            accessors.Add("set");
        string indexer = property.GetIndexParameters() is { Length: > 0 } indexParameters
            ? "[" + FormatParameters(indexParameters) + "]"
            : "";
        return modifier + TypeName(property.PropertyType) + " " + property.Name + indexer
            + " {" + string.Join(",", accessors) + "}";
    }

    private static string DescribeEvent(EventInfo @event)
    {
        string modifier = @event.AddMethod?.IsStatic == true ? "static " : "";
        return modifier + TypeName(@event.EventHandlerType ?? typeof(void)) + " " + @event.Name;
    }

    private static string DescribeMethod(MethodInfo method)
    {
        string modifier = method.IsStatic ? "static " : "";
        if (method.IsAbstract && !method.DeclaringType!.IsInterface)
            modifier += "abstract ";
        string genericArguments = method.IsGenericMethodDefinition
            ? "<" + string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name)) + ">"
            : "";
        return modifier + TypeName(method.ReturnType) + " " + method.Name + genericArguments
            + "(" + FormatParameters(method.GetParameters()) + ")";
    }

    private static string FormatParameters(IReadOnlyList<ParameterInfo> parameters)
    {
        var parts = new List<string>(parameters.Count);
        foreach (ParameterInfo parameter in parameters)
        {
            Type parameterType = parameter.ParameterType;
            string direction = "";
            if (parameterType.IsByRef)
            {
                direction = parameter.IsOut ? "out " : "ref ";
                parameterType = parameterType.GetElementType()!;
            }
            parts.Add(direction + TypeName(parameterType) + " " + (parameter.Name ?? "arg" + parts.Count));
        }
        return string.Join(", ", parts);
    }

    private static string TypeName(Type type)
    {
        if (type.IsByRef)
            return TypeName(type.GetElementType()!);
        if (type.IsArray)
            return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericParameter)
            return type.Name;
        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            string name = (string.IsNullOrEmpty(definition.Namespace) ? "" : definition.Namespace + ".")
                + StripArity(definition.Name);
            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
        }
        return type.FullName ?? type.Name;
    }

    private static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name.Substring(0, tick);
    }

    private static void Append(StringBuilder builder, string line)
        => builder.Append(line).Append('\n');
}
