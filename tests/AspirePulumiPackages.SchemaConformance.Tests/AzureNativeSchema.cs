using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AspirePulumiPackages.SchemaConformance.Tests;

/// <summary>
/// A trimmed azure-native schema: only the resources/functions the repo uses plus the types they reference.
/// The full schema (about 500 MB) is produced by <c>pulumi package get-schema azure-native</c> into a temp directory
/// (or read from AZURE_NATIVE_SCHEMA_PATH) and reduced once; the trimmed snapshot is cached in the temp directory
/// and never committed. Delete the temp directory "aspire-pulumi-schema" to refresh after a provider upgrade.
/// </summary>
public sealed class AzureNativeSchema
{
    private readonly JsonElement _resources, _types, _functions;

    private AzureNativeSchema(JsonDocument doc)
    {
        var root = doc.RootElement;
        _resources = root.GetProperty("resources");
        _types = root.GetProperty("types");
        _functions = root.GetProperty("functions");
    }

    public static AzureNativeSchema Load(IEnumerable<string> neededTokens)
    {
        var tokens = neededTokens.Where(t => t.StartsWith("azure-native:", StringComparison.Ordinal)).Distinct().Order().ToList();
        var cacheDir = Path.Combine(Path.GetTempPath(), "aspire-pulumi-schema");
        Directory.CreateDirectory(cacheDir);

        var fullPath = Environment.GetEnvironmentVariable("AZURE_NATIVE_SCHEMA_PATH");
        if (string.IsNullOrEmpty(fullPath)) fullPath = Path.Combine(cacheDir, "azure-native.full.json");

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', tokens)))).Substring(0, 16);
        var trimmedPath = Path.Combine(cacheDir, $"azure-native.trimmed.{key}.json");
        if (!File.Exists(trimmedPath))
        {
            if (!File.Exists(fullPath)) DownloadFullSchema(fullPath);
            Trim(fullPath, tokens, trimmedPath);
        }
        return new AzureNativeSchema(JsonDocument.Parse(File.ReadAllBytes(trimmedPath)));
    }

    private static void DownloadFullSchema(string path)
    {
        var tmp = path + ".tmp";
        var psi = new ProcessStartInfo("pulumi", "package get-schema azure-native")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the pulumi CLI.");
        var err = p.StandardError.ReadToEndAsync();
        using (var f = File.Create(tmp)) p.StandardOutput.BaseStream.CopyTo(f);
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"'pulumi package get-schema azure-native' failed: {err.Result}");
        File.Move(tmp, path, overwrite: true);
    }

    private static void Trim(string fullPath, List<string> tokens, string outPath)
    {
        using var full = JsonDocument.Parse(File.ReadAllBytes(fullPath));
        var root = full.RootElement;
        var resources = root.GetProperty("resources");
        var types = root.GetProperty("types");
        var functions = root.GetProperty("functions");

        var keepResources = new HashSet<string>();
        var keepFunctions = new HashSet<string>();
        var keepTypes = new HashSet<string>();
        var queue = new Queue<JsonElement>();

        foreach (var t in tokens)
        {
            if (resources.TryGetProperty(t, out var r)) { keepResources.Add(t); queue.Enqueue(r); }
            else if (functions.TryGetProperty(t, out var f)) { keepFunctions.Add(t); queue.Enqueue(f); }
        }
        while (queue.Count > 0)
        {
            foreach (var reference in Refs(queue.Dequeue()))
            {
                const string prefix = "#/types/";
                if (!reference.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var tok = Uri.UnescapeDataString(reference[prefix.Length..]);
                if (keepTypes.Add(tok) && types.TryGetProperty(tok, out var t)) queue.Enqueue(t);
            }
        }

        var tmp = outPath + ".tmp";
        using (var fs = File.Create(tmp))
        using (var w = new Utf8JsonWriter(fs))
        {
            w.WriteStartObject();
            Section(w, "resources", resources, keepResources);
            Section(w, "types", types, keepTypes);
            Section(w, "functions", functions, keepFunctions);
            w.WriteEndObject();
        }
        File.Move(tmp, outPath, overwrite: true);
    }

    private static void Section(Utf8JsonWriter w, string name, JsonElement source, HashSet<string> keep)
    {
        w.WriteStartObject(name);
        foreach (var k in keep.Order())
            if (source.TryGetProperty(k, out var v)) { w.WritePropertyName(k); v.WriteTo(w); }
        w.WriteEndObject();
    }

    private static IEnumerable<string> Refs(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "$ref" && p.Value.ValueKind == JsonValueKind.String) yield return p.Value.GetString()!;
                    else foreach (var r in Refs(p.Value)) yield return r;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    foreach (var r in Refs(item)) yield return r;
                break;
        }
    }

    public bool HasResource(string token) => _resources.TryGetProperty(token, out _);
    public bool HasFunction(string token) => _functions.TryGetProperty(token, out _);

    /// <summary>The input property bag of a resource.</summary>
    public JsonElement? ResourceInputs(string token) =>
        _resources.TryGetProperty(token, out var r) && r.TryGetProperty("inputProperties", out var p) ? p : null;

    /// <summary>The output property bag of a resource (id and urn are implicit).</summary>
    public JsonElement? ResourceOutputs(string token) =>
        _resources.TryGetProperty(token, out var r) && r.TryGetProperty("properties", out var p) ? p : null;

    public JsonElement? FunctionInputs(string token) =>
        _functions.TryGetProperty(token, out var f) && f.TryGetProperty("inputs", out var i) && i.TryGetProperty("properties", out var p) ? p : null;

    public JsonElement? FunctionOutputs(string token) =>
        _functions.TryGetProperty(token, out var f) && f.TryGetProperty("outputs", out var o) && o.TryGetProperty("properties", out var p) ? p : null;

    /// <summary>Resolves a "#/types/..." reference to its property bag, or null when it is not an object type.</summary>
    public JsonElement? TypeProperties(string reference)
    {
        const string prefix = "#/types/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var tok = Uri.UnescapeDataString(reference[prefix.Length..]);
        return _types.TryGetProperty(tok, out var t) && t.TryGetProperty("properties", out var p) ? p : null;
    }
}
