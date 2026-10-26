using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace AspirePulumiPackages.SchemaConformance.Tests;

/// <summary>
/// Validates a generated Pulumi YAML program against the azure-native schema: resource and function tokens,
/// property keys at every depth (against inputProperties / nested types) and <c>${res.prop}</c> references
/// (against the referenced resource's output properties). Non azure-native providers (azuread, ...) are not checked.
/// </summary>
public sealed partial class PulumiYamlValidator(AzureNativeSchema schema)
{
    private const string Provider = "azure-native:";

    private readonly List<string> _errors = [];
    private Dictionary<string, object?> _resources = [];
    private Dictionary<string, object?> _variables = [];
    private HashSet<string> _config = [];

    public static IEnumerable<string> TokensIn(string yaml)
    {
        var tokens = new HashSet<string>();
        Collect(Parse(yaml), tokens);
        return tokens;
    }

    private static void Collect(object? node, HashSet<string> tokens)
    {
        switch (node)
        {
            case Dictionary<string, object?> map:
                foreach (var (k, v) in map)
                {
                    if ((k == "type" || k == "function") && v is string s && s.StartsWith(Provider, StringComparison.Ordinal)) tokens.Add(s);
                    Collect(v, tokens);
                }
                break;
            case List<object?> list:
                foreach (var v in list) Collect(v, tokens);
                break;
        }
    }

    public IReadOnlyList<string> Validate(string yaml, string label)
    {
        _errors.Clear();
        var root = Parse(yaml) as Dictionary<string, object?> ?? [];
        _resources = AsMap(root.GetValueOrDefault("resources"));
        _variables = AsMap(root.GetValueOrDefault("variables"));
        _config = [.. AsMap(root.GetValueOrDefault("config")).Keys];

        foreach (var (name, def) in _resources)
        {
            var d = AsMap(def);
            if (d.GetValueOrDefault("type") is not string type) { Error(label, name, "resource has no type"); continue; }
            if (!type.StartsWith(Provider, StringComparison.Ordinal)) continue;
            if (!schema.HasResource(type)) { Error(label, name, $"unknown resource token '{type}'"); continue; }
            CheckBag(label, $"{name}", schema.ResourceInputs(type), d.GetValueOrDefault("properties"), $"properties of '{type}'");
        }

        foreach (var (name, def) in _variables)
        {
            if (AsMap(def).GetValueOrDefault("fn::invoke") is not Dictionary<string, object?> inv) continue;
            if (inv.GetValueOrDefault("function") is not string fn || !fn.StartsWith(Provider, StringComparison.Ordinal)) continue;
            if (!schema.HasFunction(fn)) { Error(label, name, $"unknown function token '{fn}'"); continue; }
            CheckBag(label, name, schema.FunctionInputs(fn), inv.GetValueOrDefault("arguments"), $"arguments of '{fn}'");
        }

        CheckReferences(label, root);
        return [.. _errors];
    }

    private void Error(string label, string where, string message) => _errors.Add($"[{label}] {where}: {message}");

    // ---- property keys ----

    private void CheckBag(string label, string where, JsonElement? bag, object? value, string what)
    {
        if (bag is null || value is not Dictionary<string, object?> map) return;
        if (IsExpression(map)) return;
        foreach (var (key, v) in map)
        {
            if (!bag.Value.TryGetProperty(key, out var propSchema))
            {
                Error(label, where, $"'{key}' is not a property of {what}");
                continue;
            }
            CheckValue(label, $"{where}.{key}", propSchema, v);
        }
    }

    private void CheckValue(string label, string where, JsonElement propSchema, object? value)
    {
        switch (value)
        {
            case Dictionary<string, object?> map when !IsExpression(map):
                if (propSchema.TryGetProperty("oneOf", out var oneOf) && oneOf.ValueKind == JsonValueKind.Array)
                {
                    // Union: accept when any alternative with a property bag fits; otherwise report the first alternative's errors.
                    var attempts = oneOf.EnumerateArray().Select(alt => (alt, errs: Probe(label, where, alt, map))).ToList();
                    if (attempts.Count == 0 || attempts.Any(a => a.errs.Count == 0)) return;
                    _errors.AddRange(attempts.First(a => ObjectBag(a.alt) is not null).errs);
                    return;
                }
                if (ObjectBag(propSchema) is { } bag)
                {
                    var typeName = propSchema.TryGetProperty("$ref", out var r) ? r.GetString() : "object";
                    CheckBag(label, where, bag, map, $"type '{typeName}'");
                }
                else if (propSchema.TryGetProperty("additionalProperties", out var ap))
                {
                    foreach (var (k, v) in map) CheckValue(label, $"{where}.{k}", ap, v);
                }
                break;
            case List<object?> list when propSchema.TryGetProperty("items", out var items):
                for (var i = 0; i < list.Count; i++) CheckValue(label, $"{where}[{i}]", items, list[i]);
                break;
        }
    }

    private List<string> Probe(string label, string where, JsonElement alt, Dictionary<string, object?> map)
    {
        var inner = new PulumiYamlValidator(schema);
        if (ObjectBag(alt) is { } bag) inner.CheckBag(label, where, bag, map, "union member");
        return inner._errors;
    }

    private JsonElement? ObjectBag(JsonElement propSchema) =>
        propSchema.TryGetProperty("$ref", out var r) && r.GetString() is { } reference ? schema.TypeProperties(reference) : null;

    private static bool IsExpression(Dictionary<string, object?> map) => map.Keys.Any(k => k.StartsWith("fn::", StringComparison.Ordinal));

    // ---- ${res.prop} references ----

    private void CheckReferences(string label, object? node, string where = "")
    {
        switch (node)
        {
            case string s:
                foreach (Match m in RefPattern().Matches(s)) CheckReference(label, where, m.Groups[1].Value);
                break;
            case Dictionary<string, object?> map:
                foreach (var (k, v) in map) CheckReferences(label, v, where.Length == 0 ? k : $"{where}.{k}");
                break;
            case List<object?> list:
                for (var i = 0; i < list.Count; i++) CheckReferences(label, list[i], $"{where}[{i}]");
                break;
        }
    }

    private void CheckReference(string label, string where, string expr)
    {
        var segments = SplitPath(expr);
        var head = segments[0];
        if (head == "pulumi" || head.Contains(':') || _config.Contains(head)) return;

        JsonElement? bag;
        string describe;
        if (_resources.TryGetValue(head, out var def))
        {
            var type = AsMap(def).GetValueOrDefault("type") as string;
            if (type is null || !type.StartsWith(Provider, StringComparison.Ordinal) || !schema.HasResource(type)) return;
            if (segments.Count > 1 && segments[1] is "id" or "urn") return;
            bag = schema.ResourceOutputs(type);
            describe = $"outputs of '{type}'";
        }
        else if (_variables.TryGetValue(head, out var v))
        {
            if (AsMap(v).GetValueOrDefault("fn::invoke") is not Dictionary<string, object?> inv
                || inv.GetValueOrDefault("function") is not string fn
                || !schema.HasFunction(fn)) return;
            bag = schema.FunctionOutputs(fn);
            describe = $"outputs of '{fn}'";
        }
        else
        {
            Error(label, where, $"reference '${{{expr}}}' names nothing in config, variables or resources");
            return;
        }

        for (var i = 1; i < segments.Count && bag is not null; i++)
        {
            if (!bag.Value.TryGetProperty(segments[i], out var prop))
            {
                Error(label, where, $"reference '${{{expr}}}': '{segments[i]}' is not an output property of {describe}");
                return;
            }
            // Descend into nested object types; stop at anything not statically typed (arrays are indexed by the next segment).
            var cur = prop;
            while (cur.TryGetProperty("items", out var items)) cur = items;
            while (i + 1 < segments.Count && segments[i + 1] == "[]") i++;
            bag = ObjectBag(cur);
            describe = $"type of '{segments[i]}'";
        }
    }

    /// <summary>Splits "a.b[0].c" into [a, b, [], c]; the [] marker means "an array index here" and is skipped by the walker.</summary>
    private static List<string> SplitPath(string expr)
    {
        var result = new List<string>();
        foreach (var part in expr.Split('.'))
        {
            var bracket = part.IndexOf('[');
            if (bracket < 0) { result.Add(part); continue; }
            result.Add(part[..bracket]);
            result.Add("[]");
        }
        return result.Where(s => s.Length > 0).ToList();
    }

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex RefPattern();

    // ---- yaml ----

    private static object? Parse(string yaml) => Normalize(new DeserializerBuilder().Build().Deserialize<object?>(yaml));

    private static object? Normalize(object? o) => o switch
    {
        Dictionary<object, object> d => d.ToDictionary(kv => Convert.ToString(kv.Key)!, kv => Normalize(kv.Value)),
        List<object> l => l.Select(Normalize).ToList(),
        _ => o,
    };

    private static Dictionary<string, object?> AsMap(object? o) => o as Dictionary<string, object?> ?? [];
}
