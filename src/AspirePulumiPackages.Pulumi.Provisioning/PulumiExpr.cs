namespace AspirePulumiPackages.Pulumi.Provisioning;

/// <summary>
/// Builders for Pulumi YAML expressions. This is the Pulumi counterpart of the BicepValue&lt;T&gt; factories in
/// Aspire.Hosting.Azure.Provisioning: every method returns a plain value (string / dictionary / list) that can be
/// dropped straight into <see cref="PulumiProgram"/> sections (Resources, Variables, Outputs, Configuration).
/// Evaluation happens in the Pulumi engine, so Output&lt;T&gt; composition is expressed as "${...}" interpolation
/// and "fn::" built-in functions rather than evaluated in .NET.
/// </summary>
public static class PulumiExpr
{
    /// <summary>Reference to a resource, variable or config key, optionally with a property path: "${name.prop[0].sub}".</summary>
    public static string Ref(string name, string? path = null) =>
        string.IsNullOrEmpty(path) ? $"${{{name}}}" : $"${{{name}.{path}}}";

    /// <summary>Built-in pseudo variables: project, stack, cwd, organization.</summary>
    public static string Pulumi(string name) => Ref($"pulumi.{name}");

    /// <summary>Reference to an element of a list output, optionally followed by a property path.</summary>
    public static string Index(string name, int index, string? path = null) =>
        path is null ? $"${{{name}[{index}]}}" : $"${{{name}[{index}].{path}}}";

    /// <summary>Escapes any "${" in a literal so Pulumi does not interpolate it.</summary>
    public static string Literal(string value) => value.Replace("${", "$${");

    /// <summary>String interpolation mixing literals and references; parts are used verbatim (use <see cref="Ref"/> for references).</summary>
    public static string Interpolate(params object?[] parts) =>
        string.Concat(parts.Select(p => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>fn::join - joins values with a delimiter.</summary>
    public static Dictionary<string, object?> Join(string delimiter, params object?[] values) =>
        Fn("fn::join", new List<object?> { delimiter, new List<object?>(values) });

    /// <summary>fn::select - picks an element by index from a list.</summary>
    public static Dictionary<string, object?> Select(int index, object? list) =>
        Fn("fn::select", new List<object?> { index, list });

    /// <summary>fn::split - splits a string on a delimiter.</summary>
    public static Dictionary<string, object?> Split(string delimiter, object? value) =>
        Fn("fn::split", new List<object?> { delimiter, value });

    /// <summary>fn::toJSON.</summary>
    public static Dictionary<string, object?> ToJson(object? value) => Fn("fn::toJSON", value);

    /// <summary>fn::toBase64.</summary>
    public static Dictionary<string, object?> ToBase64(object? value) => Fn("fn::toBase64", value);

    /// <summary>fn::fromBase64.</summary>
    public static Dictionary<string, object?> FromBase64(object? value) => Fn("fn::fromBase64", value);

    /// <summary>fn::secret - marks a value as secret (Pulumi's equivalent of Bicep @secure()).</summary>
    public static Dictionary<string, object?> Secret(object? value) => Fn("fn::secret", value);

    /// <summary>fn::invoke - calls a provider function, optionally selecting one property of the result via <paramref name="returns"/>.</summary>
    public static Dictionary<string, object?> Invoke(string token, Dictionary<string, object?>? arguments = null, string? returns = null)
    {
        var body = new Dictionary<string, object?> { ["function"] = token };
        if (arguments is { Count: > 0 }) body["arguments"] = arguments;
        if (returns is not null) body["return"] = returns;
        return Fn("fn::invoke", body);
    }

    /// <summary>
    /// Conditional (Bicep ternary). Pulumi YAML has no ternary, so conditions must be resolved in .NET at
    /// generation time and only the chosen value emitted.
    /// </summary>
    public static T When<T>(bool condition, T whenTrue, T whenFalse) => condition ? whenTrue : whenFalse;

    /// <summary>A single-key "fn::" call.</summary>
    public static Dictionary<string, object?> Fn(string name, object? argument) => new() { [name] = argument };
}
