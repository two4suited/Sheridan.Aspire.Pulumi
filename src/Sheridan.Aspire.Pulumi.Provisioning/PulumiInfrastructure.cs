using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Provisioning;

/// <summary>
/// Pulumi counterpart of Azure.Provisioning's <c>AzureResourceInfrastructure</c>: the object handed to a
/// <c>ConfigureInfrastructure</c> callback. It gives typed-ish access to the resources a contributor added to the
/// program, so callers can set any provider property before the YAML is emitted.
/// </summary>
/// <remarks>
/// The pipeline emits Pulumi YAML, so azure-native's generated .NET args classes cannot be used directly; properties are
/// addressed by their azure-native schema names (dotted paths, e.g. <c>properties.sku.name</c>). Values may be literals,
/// <see cref="PulumiExpr"/> expressions or <c>${resource.output}</c> references.
/// </remarks>
public class PulumiInfrastructure(PulumiProgram program)
{
    public PulumiProgram Program { get; } = program;

    /// <summary>All resources currently in the program.</summary>
    public IEnumerable<PulumiResourceNode> Resources =>
        Program.Resources.Where(kv => kv.Value is Dictionary<string, object?>).Select(kv => new PulumiResourceNode(kv.Key, (Dictionary<string, object?>)kv.Value!));

    /// <summary>Looks up a resource by its program id (the identifier lookup of the upstream overlay).</summary>
    public PulumiResourceNode GetResource(string id) =>
        TryGetResource(id) ?? throw new KeyNotFoundException($"No resource '{id}' in the Pulumi program.");

    public PulumiResourceNode? TryGetResource(string id) =>
        Program.Resources.TryGetValue(id, out var v) && v is Dictionary<string, object?> d ? new PulumiResourceNode(id, d) : null;

    /// <summary>All resources of an azure-native type token, e.g. <c>azure-native:keyvault:Vault</c>.</summary>
    public IEnumerable<PulumiResourceNode> GetResources(string type) => Resources.Where(r => r.Type == type);

    /// <summary>The single resource of a type; throws if there are none or several.</summary>
    public PulumiResourceNode GetSingleResource(string type) => GetResources(type).Single();

    /// <summary>Adds a new resource (e.g. a lock or diagnostic setting) and returns it for further configuration.</summary>
    public PulumiResourceNode AddResource(string id, string type)
    {
        var def = new Dictionary<string, object?> { ["type"] = type };
        Program.Resources[id] = def;
        return new PulumiResourceNode(id, def);
    }

    /// <summary>Adds a stack output (BicepOutput equivalent).</summary>
    public void AddOutput(string name, object? value) => Program.Outputs[name] = value;
}

/// <summary>Editable handle over one resource definition in the program (its azure-native type, properties and options).</summary>
public sealed class PulumiResourceNode
{
    private readonly Dictionary<string, object?> _def;

    internal PulumiResourceNode(string id, Dictionary<string, object?> def) { Id = id; _def = def; }

    public string Id { get; }

    public string Type => (string)_def["type"]!;

    /// <summary>A <c>${id.path}</c> reference to this resource or one of its outputs.</summary>
    public string Ref(string? path = null) => PulumiExpr.Ref(Id, path);

    /// <summary>Sets a property by dotted schema path (creating intermediate maps); returns this for chaining. Null removes it.</summary>
    public PulumiResourceNode Set(string path, object? value)
    {
        if (value is null) return Remove(path);
        var map = Walk(Section("properties"), path, out var leaf, create: true)!;
        map[leaf] = value;
        return this;
    }

    public object? Get(string path)
    {
        var map = Walk(_def.TryGetValue("properties", out var p) ? p as Dictionary<string, object?> : null, path, out var leaf, create: false);
        return map is not null && map.TryGetValue(leaf, out var v) ? v : null;
    }

    public PulumiResourceNode Remove(string path)
    {
        Walk(_def.TryGetValue("properties", out var p) ? p as Dictionary<string, object?> : null, path, out var leaf, create: false)?.Remove(leaf);
        return this;
    }

    /// <summary>Sets a resource option (protect, parent, ignoreChanges, ...).</summary>
    public PulumiResourceNode SetOption(string name, object? value)
    {
        if (value is null) Section("options").Remove(name); else Section("options")[name] = value;
        return this;
    }

    public PulumiResourceNode DependsOn(string resourceId)
    {
        var opts = Section("options");
        if (!opts.TryGetValue("dependsOn", out var l) || l is not List<object?> list) opts["dependsOn"] = list = [];
        list.Add(PulumiExpr.Ref(resourceId));
        return this;
    }

    private Dictionary<string, object?> Section(string name)
    {
        if (!_def.TryGetValue(name, out var v) || v is not Dictionary<string, object?> d) _def[name] = d = [];
        return d;
    }

    private static Dictionary<string, object?>? Walk(Dictionary<string, object?>? root, string path, out string leaf, bool create)
    {
        var parts = path.Split('.');
        leaf = parts[^1];
        var cur = root;
        foreach (var part in parts[..^1])
        {
            if (cur is null) return null;
            if (!cur.TryGetValue(part, out var next) || next is not Dictionary<string, object?> nd)
            {
                if (!create) return null;
                cur[part] = nd = [];
            }
            cur = nd;
        }
        return cur;
    }
}

/// <summary>Stores <c>ConfigureInfrastructure</c> callbacks on an Aspire resource.</summary>
public sealed class PulumiInfrastructureCallbackAnnotation(Action<PulumiInfrastructure> callback) : IResourceAnnotation
{
    public Action<PulumiInfrastructure> Callback { get; } = callback;
}

public static class PulumiInfrastructureExtensions
{
    /// <summary>
    /// Registers a callback run (in registration order) after the resource has added its resources to the program and before
    /// the YAML is emitted. Overlay packages wrap this with a resource-specific, typed-roots <c>ConfigureInfrastructure</c>.
    /// </summary>
    public static IResourceBuilder<T> ConfigureInfrastructure<T>(this IResourceBuilder<T> builder, Action<PulumiInfrastructure> callback)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(callback);
        return builder.WithAnnotation(new PulumiInfrastructureCallbackAnnotation(callback));
    }

    /// <summary>Called by a contributor at the end of its <c>ContributeAsync</c> to run the registered callbacks.</summary>
    public static void ApplyInfrastructureCallbacks(this IResource resource, PulumiProgram program)
    {
        var infra = new PulumiInfrastructure(program);
        foreach (var a in resource.Annotations.OfType<PulumiInfrastructureCallbackAnnotation>()) a.Callback(infra);
    }
}
