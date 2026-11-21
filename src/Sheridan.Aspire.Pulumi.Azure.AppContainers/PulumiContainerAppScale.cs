using System.Globalization;
using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Azure.AppContainers;

/// <summary>
/// Scale settings for a container app (azure-native <c>ContainerApp</c> <c>template.scale</c>). Pulumi counterpart of the
/// <c>app.Template.Scale</c> customization in Aspire's <c>PublishAsAzureContainerApp</c>.
/// </summary>
public sealed class PulumiContainerAppScale
{
    internal List<Dictionary<string, object?>> Rules { get; } = [];

    /// <summary>Minimum replica count (0 allows scale to zero). Defaults to 1.</summary>
    public int MinReplicas { get; set; } = 1;

    /// <summary>Maximum replica count. Defaults to 1, or 10 (the Azure default) when scale rules are added.</summary>
    public int? MaxReplicas { get; set; }

    /// <summary>Seconds to wait before scaling back down to the minimum. Unset keeps the Azure default (300).</summary>
    public int? CooldownPeriod { get; set; }

    /// <summary>Seconds between scale-rule polls. Unset keeps the Azure default (30).</summary>
    public int? PollingInterval { get; set; }

    /// <summary>Scales on the number of concurrent HTTP requests per replica.</summary>
    public PulumiContainerAppScale AddHttpRule(string name, int concurrentRequests) =>
        concurrentRequests < 1 ? throw new ArgumentOutOfRangeException(nameof(concurrentRequests)) : Add(name, "http", new Dictionary<string, object?> { ["metadata"] = new Dictionary<string, object?> { ["concurrentRequests"] = Str(concurrentRequests) } });

    /// <summary>Scales on average CPU utilization (percent) per replica.</summary>
    public PulumiContainerAppScale AddCpuRule(string name, int utilizationPercentage) => AddResourceRule(name, "cpu", utilizationPercentage);

    /// <summary>Scales on average memory utilization (percent) per replica.</summary>
    public PulumiContainerAppScale AddMemoryRule(string name, int utilizationPercentage) => AddResourceRule(name, "memory", utilizationPercentage);

    /// <summary>Scales on a KEDA scaler of the given <paramref name="type"/> (e.g. <c>azure-servicebus</c>, <c>azure-queue</c>).</summary>
    /// <param name="name">Rule name.</param>
    /// <param name="type">KEDA scaler type.</param>
    /// <param name="metadata">Scaler metadata.</param>
    /// <param name="useEnvironmentIdentity">Authenticate the scaler with the environment's shared user-assigned identity (needs the AcrPull/identity setup, i.e. not <c>WithoutAcrPullRoleAssignment</c>); the identity must hold the data-plane role the scaler needs.</param>
    public PulumiContainerAppScale AddCustomRule(string name, string type, IReadOnlyDictionary<string, string> metadata, bool useEnvironmentIdentity = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(metadata);
        var custom = new Dictionary<string, object?> { ["type"] = type, ["metadata"] = metadata.ToDictionary(kv => kv.Key, kv => (object?)kv.Value) };
        if (useEnvironmentIdentity) custom["identity"] = IdentityToken;
        return Add(name, "custom", custom);
    }

    private PulumiContainerAppScale AddResourceRule(string name, string type, int utilization) =>
        utilization is < 1 or > 100 ? throw new ArgumentOutOfRangeException(nameof(utilization)) : Add(name, "custom", new Dictionary<string, object?>
        {
            ["type"] = type,
            ["metadata"] = new Dictionary<string, object?> { ["type"] = "Utilization", ["value"] = Str(utilization) },
        });

    private PulumiContainerAppScale Add(string name, string kind, Dictionary<string, object?> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Rules.Any(r => (string?)r["name"] == name)) throw new ArgumentException($"A scale rule named '{name}' already exists.", nameof(name));
        Rules.Add(new Dictionary<string, object?> { ["name"] = name, [kind] = body });
        return this;
    }

    internal const string IdentityToken = "<environment-identity>";

    private static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);

    internal Dictionary<string, object?> ToProgram(string? identityRef)
    {
        var max = MaxReplicas ?? (Rules.Count > 0 ? Math.Max(10, MinReplicas) : Math.Max(1, MinReplicas));
        if (MinReplicas < 0) throw new InvalidOperationException("MinReplicas must be >= 0.");
        if (max < 1 || max > 1000 || max < MinReplicas) throw new InvalidOperationException("MaxReplicas must be between 1 and 1000 and >= MinReplicas.");
        if (CooldownPeriod < 0 || PollingInterval < 0) throw new InvalidOperationException("CooldownPeriod and PollingInterval must be >= 0.");
        var scale = new Dictionary<string, object?> { ["minReplicas"] = MinReplicas, ["maxReplicas"] = max };
        if (CooldownPeriod is { } c) scale["cooldownPeriod"] = c;
        if (PollingInterval is { } p) scale["pollingInterval"] = p;
        if (Rules.Count > 0)
        {
            foreach (var rule in Rules)
            foreach (var body in rule.Values.OfType<Dictionary<string, object?>>())
            {
                if (!body.TryGetValue("identity", out var id) || !Equals(id, IdentityToken)) continue;
                if (identityRef is null) throw new InvalidOperationException($"Scale rule '{rule["name"]}' uses the environment identity, which is unavailable with WithoutAcrPullRoleAssignment.");
                body["identity"] = identityRef;
            }
            scale["rules"] = Rules.Cast<object?>().ToList();
        }
        return scale;
    }
}

/// <summary>Holds the scale settings for a compute resource deployed as a container app (see <c>PublishAsPulumiContainerApp</c>).</summary>
public sealed class PulumiContainerAppScaleAnnotation(PulumiContainerAppScale scale) : IResourceAnnotation
{
    public PulumiContainerAppScale Scale { get; } = scale;
}
