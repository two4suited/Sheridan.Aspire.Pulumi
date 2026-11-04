namespace Sheridan.Aspire.Pulumi.Provisioning;

/// <summary>Fluent construction of a Pulumi YAML resource definition (type, properties, options).</summary>
public sealed class PulumiResourceBuilder(string type)
{
    private readonly Dictionary<string, object?> _properties = [];
    private readonly Dictionary<string, object?> _options = [];
    private readonly List<object?> _dependsOn = [];

    /// <summary>Sets a property; null values are omitted.</summary>
    public PulumiResourceBuilder Property(string name, object? value)
    {
        if (value is not null) _properties[name] = value;
        return this;
    }

    /// <summary>Sets a resource option (protect, parent, provider, ...); null values are omitted.</summary>
    public PulumiResourceBuilder Option(string name, object? value)
    {
        if (value is not null) _options[name] = value;
        return this;
    }

    /// <summary>Adds an explicit dependency on another resource by its program name.</summary>
    public PulumiResourceBuilder DependsOn(string resourceName)
    {
        _dependsOn.Add(PulumiExpr.Ref(resourceName));
        return this;
    }

    /// <summary>Places the resource in a resource group (by program name of the group), inheriting its location unless given.</summary>
    public PulumiResourceBuilder InResourceGroup(string resourceGroupName, string? location = null)
    {
        Property("resourceGroupName", PulumiExpr.Ref(resourceGroupName, "name"));
        return Property("location", location ?? PulumiExpr.Ref(resourceGroupName, "location"));
    }

    public PulumiResourceBuilder WithTags(IDictionary<string, string>? tags) =>
        tags is { Count: > 0 } ? Property("tags", tags.ToDictionary(kv => kv.Key, kv => (object?)kv.Value)) : this;

    public Dictionary<string, object?> Build()
    {
        var def = new Dictionary<string, object?> { ["type"] = type };
        if (_properties.Count > 0) def["properties"] = new Dictionary<string, object?>(_properties);
        var options = new Dictionary<string, object?>(_options);
        if (_dependsOn.Count > 0) options["dependsOn"] = new List<object?>(_dependsOn);
        if (options.Count > 0) def["options"] = options;
        return def;
    }

    /// <summary>Adds the built resource to the program under <paramref name="name"/>.</summary>
    public void AddTo(PulumiProgram program, string name) => program.Resources[name] = Build();
}
