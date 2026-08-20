using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonitovoPDF.Tests;

/// <summary>
/// Pins the library's public API against a checked-in baseline.
/// </summary>
/// <remarks>
/// <para>
/// Once the package is published its public surface is a contract, and the cheapest way to break
/// it is by accident — renaming a parameter, tightening a type, dropping a default. This renders
/// the surface as text and compares it to <c>PublicApi.approved.txt</c>, so any change to it has
/// to be made deliberately and shows up in review as a diff.
/// </para>
/// <para>
/// When a change is intended, run the tests, then replace the approved file with the received one
/// the failure names. Reviewing that diff is the point of the exercise.
/// </para>
/// </remarks>
public class PublicApiTests
{
    private const string ApprovedFileName = "PublicApi.approved.txt";
    private const string ReceivedFileName = "PublicApi.received.txt";

    [Fact]
    public void PublicApi_MatchesTheApprovedBaseline()
    {
        var actual = Describe(typeof(MonitovoPdf).Assembly);

        var directory = SourceDirectory();
        var approvedPath = Path.Combine(directory, ApprovedFileName);
        var receivedPath = Path.Combine(directory, ReceivedFileName);

        var approved = File.Exists(approvedPath)
            ? Normalise(File.ReadAllText(approvedPath))
            : string.Empty;

        if (approved == actual)
        {
            // Leave no stale received file behind once the surfaces agree again.
            if (File.Exists(receivedPath))
                File.Delete(receivedPath);

            return;
        }

        File.WriteAllText(receivedPath, actual);

        Assert.Fail(
            $"The public API no longer matches {ApprovedFileName}.\n"
            + $"If the change is intended, replace it with {ReceivedFileName}:\n"
            + $"  copy \"{receivedPath}\" \"{approvedPath}\"\n"
            + "Review the diff first — anything removed or retyped is a breaking change for consumers.");
    }

    [Fact]
    public void OnlyTheIntendedTypesArePublic()
    {
        // A second, blunter guard: the internals stay internal even if the baseline is regenerated
        // carelessly. Adding a type here should be a deliberate act.
        string[] expected =
        [
            "MonitovoPDF.BarcodeType",
            "MonitovoPDF.BarcodeTypes",
            "MonitovoPDF.FillBuilder",
            "MonitovoPDF.MonitovoPdf",
            "MonitovoPDF.RenderingOptions",
            "MonitovoPDF.TemplateRenderException",
        ];

        var actual = typeof(MonitovoPdf).Assembly.GetExportedTypes()
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    private static string SourceDirectory([CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;

    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n").TrimEnd() + "\n";

    /// <summary>Renders every exported type and member as stable, sorted text.</summary>
    private static string Describe(Assembly assembly)
    {
        var builder = new StringBuilder();

        var byNamespace = assembly.GetExportedTypes()
            .GroupBy(type => type.Namespace ?? string.Empty)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in byNamespace)
        {
            builder.Append("namespace ").Append(group.Key).AppendLine();
            builder.AppendLine("{");

            foreach (var type in group.OrderBy(type => type.FullName, StringComparer.Ordinal))
                AppendType(builder, type);

            builder.AppendLine("}");
        }

        return Normalise(builder.ToString());
    }

    private static void AppendType(StringBuilder builder, Type type)
    {
        builder.Append("    ").Append(Declaration(type)).AppendLine();
        builder.AppendLine("    {");

        foreach (var member in Members(type))
            builder.Append("        ").Append(member).AppendLine();

        builder.AppendLine("    }");
    }

    private static string Declaration(Type type)
    {
        if (type.IsEnum)
            return $"public enum {type.Name}";

        var modifiers = type switch
        {
            { IsInterface: true } => "interface",
            { IsValueType: true } => "struct",
            { IsAbstract: true, IsSealed: true } => "static class",
            { IsAbstract: true } => "abstract class",
            { IsSealed: true } => "sealed class",
            _ => "class"
        };

        var bases = new List<string>();
        if (type.BaseType is not null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType))
            bases.Add(Name(type.BaseType));

        bases.AddRange(type.GetInterfaces().Select(Name).Order(StringComparer.Ordinal));

        var suffix = bases.Count > 0 ? " : " + string.Join(", ", bases) : "";
        return $"public {modifiers} {type.Name}{suffix}";
    }

    private static IEnumerable<string> Members(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var lines = new List<string>();

        if (type.IsEnum)
        {
            foreach (var value in Enum.GetValues(type).Cast<object>())
                lines.Add($"{value} = {Convert.ToInt64(value)},");

            return lines.Order(StringComparer.Ordinal);
        }

        foreach (var field in type.GetFields(flags).Where(NotCompilerGenerated))
        {
            var modifiers = field.IsLiteral ? "const " : field.IsStatic ? "static " : "";
            lines.Add($"public {modifiers}{Name(field.FieldType)} {field.Name};");
        }

        foreach (var property in type.GetProperties(flags).Where(NotCompilerGenerated))
        {
            var accessors = new List<string>();
            if (property.GetMethod is { IsPublic: true }) accessors.Add("get;");
            if (property.SetMethod is { IsPublic: true })
                accessors.Add(IsInitOnly(property.SetMethod) ? "init;" : "set;");

            var modifier = property.GetMethod?.IsStatic == true ? "static " : "";
            lines.Add($"public {modifier}{Name(property.PropertyType)} {property.Name} {{ {string.Join(" ", accessors)} }}");
        }

        foreach (var constructor in type.GetConstructors(flags).Where(NotCompilerGenerated))
            lines.Add($"public {type.Name}({Parameters(constructor)});");

        foreach (var method in type.GetMethods(flags).Where(NotCompilerGenerated).Where(m => !m.IsSpecialName))
        {
            var modifier = method.IsStatic ? "static " : "";
            lines.Add($"public {modifier}{Name(method.ReturnType)} {method.Name}({Parameters(method)});");
        }

        return lines.Order(StringComparer.Ordinal);
    }

    private static bool NotCompilerGenerated(MemberInfo member) =>
        !member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(Parameter));

    private static string Parameter(ParameterInfo parameter)
    {
        var direction = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : "";
        var text = $"{direction}{Name(parameter.ParameterType)} {parameter.Name}";

        if (!parameter.HasDefaultValue)
            return text;

        var value = parameter.DefaultValue switch
        {
            null => "null",
            string s => $"\"{s}\"",
            bool b => b ? "true" : "false",
            var other => Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture) ?? "null"
        };

        return $"{text} = {value}";
    }

    /// <summary>Formats a type name the way it would be written in source.</summary>
    private static string Name(Type type)
    {
        if (type.IsByRef)
            return Name(type.GetElementType()!);

        if (type.IsArray)
            return Name(type.GetElementType()!) + "[]";

        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Name(underlying) + "?";

        if (type.IsGenericType)
        {
            var name = type.Name[..type.Name.IndexOf('`')];
            var arguments = string.Join(", ", type.GetGenericArguments().Select(Name));
            return $"{Namespace(type)}{name}<{arguments}>";
        }

        return Aliases.TryGetValue(type, out var alias) ? alias : $"{Namespace(type)}{type.Name}";
    }

    private static string Namespace(Type type) =>
        type.Namespace is null or "System" or "MonitovoPDF" ? "" : type.Namespace + ".";

    private static readonly Dictionary<Type, string> Aliases = new()
    {
        [typeof(void)] = "void",
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(double)] = "double",
        [typeof(string)] = "string",
        [typeof(object)] = "object",
    };
}
