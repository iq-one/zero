using System.Reflection;
using System.Text;

namespace IQOne.Zero.ApiSurface.Tests;

/// <summary>
/// Renders an assembly's public surface as deterministic text.
/// </summary>
/// <remarks>
/// The rendered form is what gets reviewed. A change to it appears in the diff of the
/// approved file, so removing a member or altering a signature cannot reach a release
/// unnoticed — which is the whole point of promising semantic versioning.
/// </remarks>
internal static class ApiSurface
{
    public static string Render(Assembly assembly)
    {
        var builder = new StringBuilder();

        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            builder.AppendLine(Declaration(type));

            foreach (var member in Members(type)) builder.Append("    ").AppendLine(member);

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    private static string Declaration(Type type)
    {
        var kind = type.IsInterface ? "interface"
            : type.IsEnum ? "enum"
            : type.IsValueType ? "struct"
            : type.IsAbstract && type.IsSealed ? "static class"
            : type.IsAbstract ? "abstract class"
            : type.IsSealed ? "sealed class"
            : "class";

        var bases = new List<string>();

        if (type is { IsClass: true, BaseType: not null } && type.BaseType != typeof(object))
            bases.Add(Name(type.BaseType));

        // Only interfaces declared here; inherited ones would make the file churn on a
        // change to a base type that did not alter this type's own contract.
        bases.AddRange(Declared(type).Select(Name).OrderBy(n => n, StringComparer.Ordinal));

        var suffix = bases.Count > 0 ? " : " + string.Join(", ", bases) : string.Empty;

        return $"{kind} {Name(type)}{suffix}";
    }

    private static IEnumerable<Type> Declared(Type type)
    {
        var inherited = new HashSet<Type>(
            (type.BaseType?.GetInterfaces() ?? []).Concat(type.GetInterfaces().SelectMany(i => i.GetInterfaces())));

        return type.GetInterfaces().Where(i => i.IsPublic && !inherited.Contains(i));
    }

    private static IEnumerable<string> Members(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly;

        var rendered = new List<string>();

        foreach (var member in type.GetMembers(flags))
        {
            var text = member switch
            {
                FieldInfo f when Visible(f) => $"{Modifier(f)}{Name(f.FieldType)} {f.Name}",
                PropertyInfo p when Visible(p) => Property(p),
                MethodInfo m when Visible(m) && !IsAccessor(m) => Method(m),
                ConstructorInfo c when Visible(c) => Constructor(type, c),
                _ => null
            };

            if (text is not null) rendered.Add(text);
        }

        return rendered.OrderBy(m => m, StringComparer.Ordinal);
    }

    private static bool IsAccessor(MethodInfo method) => method.IsSpecialName;

    private static bool Visible(FieldInfo f) => f.IsPublic || f.IsFamily;

    private static bool Visible(MethodBase m) => m.IsPublic || m.IsFamily;

    private static bool Visible(PropertyInfo p)
        => (p.GetMethod is { } g && Visible(g)) || (p.SetMethod is { } s && Visible(s));

    private static string Property(PropertyInfo property)
    {
        var accessors = new List<string>();

        if (property.GetMethod is { } get && Visible(get)) accessors.Add("get");
        if (property.SetMethod is { } set && Visible(set)) accessors.Add(set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit") ? "init" : "set");

        var owner = property.GetMethod ?? property.SetMethod!;

        return $"{Modifier(owner)}{Name(property.PropertyType)} {property.Name} {{ {string.Join("; ", accessors)}; }}";
    }

    private static string Method(MethodInfo method)
        => $"{Modifier(method)}{Name(method.ReturnType)} {method.Name}{Generics(method)}({Parameters(method)})";

    private static string Constructor(Type type, ConstructorInfo constructor)
        => $"{Modifier(constructor)}.ctor {Bare(type)}({Parameters(constructor)})";

    private static string Modifier(MemberInfo member) => member switch
    {
        FieldInfo { IsFamily: true } => "protected ",
        MethodBase { IsFamily: true } => "protected ",
        FieldInfo { IsStatic: true } => "static ",
        MethodBase { IsStatic: true } => "static ",
        _ => string.Empty
    };

    private static string Generics(MethodInfo method)
        => method.IsGenericMethodDefinition
            ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">"
            : string.Empty;

    private static string Parameters(MethodBase method)
        => string.Join(", ", method.GetParameters().Select(p =>
        {
            var prefix = p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : string.Empty;
            var optional = p.IsOptional ? " = default" : string.Empty;

            return $"{prefix}{Name(p.ParameterType)} {p.Name}{optional}";
        }));

    private static string Bare(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`');

        return tick < 0 ? name : name[..tick];
    }

    private static string Name(Type type)
    {
        if (type.IsByRef) return Name(type.GetElementType()!);
        if (type.IsArray) return Name(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter) return type.Name;

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) return Name(underlying) + "?";

        if (!type.IsGenericType) return Alias(type);

        var arguments = string.Join(", ", type.GetGenericArguments().Select(Name));

        return $"{Namespaced(type)}<{arguments}>";
    }

    private static string Namespaced(Type type)
        => type.Namespace is null || type.Namespace.StartsWith("System", StringComparison.Ordinal)
            ? Bare(type)
            : $"{type.Namespace}.{Bare(type)}";

    private static string Alias(Type type) => type.FullName switch
    {
        "System.Void" => "void",
        "System.Boolean" => "bool",
        "System.Int32" => "int",
        "System.String" => "string",
        "System.Object" => "object",
        "System.Type" => "Type",
        _ => Namespaced(type)
    };
}
