using System.Security.Cryptography;
using System.Text;

namespace AspirePulumiPackages.Pulumi.Azure.Base;

/// <summary>The Azure resource types the naming layer knows rules for. See docs/naming.md for the rule table.</summary>
public enum AzureNameKind
{
    ResourceGroup,
    UserAssignedIdentity,
    KeyVault,
    KeyVaultSecret,
    StorageAccount,
    ContainerRegistry,
    CosmosDbAccount,
    SqlServer,
    PostgresFlexibleServer,
    RedisCache,
    ServiceBusNamespace,
    EventHubsNamespace,
    AppConfigurationStore,
    CognitiveServicesAccount,
    CognitiveServicesSubdomain,
    KubernetesCluster,
    ContainerApp,
    ContainerAppsEnvironment,
    LogAnalyticsWorkspace,
    ApplicationInsights,
    AppServicePlan,
    WebApp,
    SearchService,
    SignalRService,
    WebPubSubService,
    KustoCluster,
    VirtualNetwork,
    FrontDoorProfile,
    ContainerAppJob,
    SandboxGroup,
    NetworkSecurityGroup,
    NatGateway,
    PublicIpAddress,
    Subnet,
    PrivateEndpoint,
    PrivateDnsVirtualNetworkLink,
    NetworkSecurityPerimeter,
    FrontDoorEndpoint,
    FrontDoorOriginGroup,
    FrontDoorOrigin,
    FrontDoorRoute,
    AksDnsPrefix,
    FederatedIdentityCredential,
    NetworkSecurityRule,
    RedisEnterpriseCluster,
    DeploymentScript,
}

/// <summary>Where an Azure resource name must be unique.</summary>
public enum AzureNameScope { Global, Region, Subscription, ResourceGroup, Parent }

/// <summary>
/// The naming constraints of one Azure resource type. <see cref="Abbreviation"/> is only used as a deterministic
/// prefix when a composed name would start with a digit but the type requires a leading letter.
/// </summary>
public sealed record AzureNamingRule(
    AzureNameKind Kind,
    string ResourceType,
    int Min,
    int Max,
    AzureNameScope Scope,
    string Abbreviation,
    bool AllowHyphen = true,
    bool AllowUnderscore = false,
    bool AllowPeriod = false,
    bool LowercaseOnly = false,
    bool MustStartWithLetter = false,
    bool MustStartAlphanumeric = true,
    bool MustEndAlphanumeric = true,
    bool NoConsecutiveHyphens = false,
    string? Note = null)
{
    /// <summary>Human readable summary of the allowed characters, used in error messages and docs.</summary>
    public string CharacterSummary
    {
        get
        {
            var parts = new List<string> { LowercaseOnly ? "lowercase letters" : "letters", "digits" };
            if (AllowHyphen) parts.Add("hyphens");
            if (AllowUnderscore) parts.Add("underscores");
            if (AllowPeriod) parts.Add("periods");
            return string.Join(", ", parts);
        }
    }
}

/// <summary>
/// Builds Azure resource names from the app (Pulumi project) name, the Aspire resource name, the stack name and an
/// optional suffix, conforming to each resource type's rules. Names are deterministic: when the composed name is too
/// long (or sanitizing it loses information) the segments are trimmed and a short hash of the full name is appended.
/// </summary>
public static class AzureNaming
{
    /// <summary>Length of the hex hash appended when a name is lossy.</summary>
    public const int HashLength = 6;

    private static AzureNamingRule R(AzureNameKind kind, string type, int min, int max, AzureNameScope scope, string abbr,
        bool hyphen = true, bool underscore = false, bool period = false, bool lower = false, bool letterStart = false,
        bool alnumStart = true, bool alnumEnd = true, bool noDoubleHyphen = false, string? note = null) =>
        new(kind, type, min, max, scope, abbr, hyphen, underscore, period, lower, letterStart, alnumStart, alnumEnd, noDoubleHyphen, note);

    /// <summary>The rule table (checked against Microsoft's "Naming rules and restrictions for Azure resources").</summary>
    public static IReadOnlyDictionary<AzureNameKind, AzureNamingRule> Rules { get; } = new[]
    {
        R(AzureNameKind.ResourceGroup, "Microsoft.Resources/resourceGroups", 1, 90, AzureNameScope.Subscription, "rg", underscore: true, period: true, alnumEnd: false, note: "Generated names never end with a period; parentheses are legal but never generated."),
        R(AzureNameKind.UserAssignedIdentity, "Microsoft.ManagedIdentity/userAssignedIdentities", 3, 128, AzureNameScope.ResourceGroup, "id", underscore: true),
        R(AzureNameKind.KeyVault, "Microsoft.KeyVault/vaults", 3, 24, AzureNameScope.Global, "kv", letterStart: true, noDoubleHyphen: true, note: "Soft-deleted vaults keep their name reserved until purged."),
        R(AzureNameKind.KeyVaultSecret, "Microsoft.KeyVault/vaults/secrets", 1, 127, AzureNameScope.Parent, "s", alnumStart: false, alnumEnd: false),
        R(AzureNameKind.StorageAccount, "Microsoft.Storage/storageAccounts", 3, 24, AzureNameScope.Global, "st", hyphen: false, lower: true),
        R(AzureNameKind.ContainerRegistry, "Microsoft.ContainerRegistry/registries", 5, 50, AzureNameScope.Global, "cr", hyphen: false),
        R(AzureNameKind.CosmosDbAccount, "Microsoft.DocumentDB/databaseAccounts", 3, 44, AzureNameScope.Global, "cosmos", lower: true, alnumEnd: false),
        R(AzureNameKind.SqlServer, "Microsoft.Sql/servers", 1, 63, AzureNameScope.Global, "sql", lower: true),
        R(AzureNameKind.PostgresFlexibleServer, "Microsoft.DBforPostgreSQL/flexibleServers", 3, 63, AzureNameScope.Global, "pg", lower: true),
        R(AzureNameKind.RedisCache, "Microsoft.Cache/redis", 1, 63, AzureNameScope.Global, "redis", noDoubleHyphen: true),
        R(AzureNameKind.ServiceBusNamespace, "Microsoft.ServiceBus/namespaces", 6, 50, AzureNameScope.Global, "sb", letterStart: true),
        R(AzureNameKind.EventHubsNamespace, "Microsoft.EventHub/namespaces", 6, 50, AzureNameScope.Global, "evh", letterStart: true),
        R(AzureNameKind.AppConfigurationStore, "Microsoft.AppConfiguration/configurationStores", 5, 50, AzureNameScope.Global, "appcs", noDoubleHyphen: true, note: "Microsoft allows up to two consecutive hyphens; generated names use at most one and overrides are held to the stricter rule."),
        R(AzureNameKind.CognitiveServicesAccount, "Microsoft.CognitiveServices/accounts", 2, 64, AzureNameScope.ResourceGroup, "cog"),
        R(AzureNameKind.CognitiveServicesSubdomain, "Microsoft.CognitiveServices/accounts (customSubDomainName)", 2, 64, AzureNameScope.Global, "cog", note: "The custom sub-domain becomes <name>.cognitiveservices.azure.com and is globally unique."),
        R(AzureNameKind.KubernetesCluster, "Microsoft.ContainerService/managedClusters", 1, 63, AzureNameScope.ResourceGroup, "aks", underscore: true),
        R(AzureNameKind.ContainerApp, "Microsoft.App/containerApps", 2, 32, AzureNameScope.ResourceGroup, "ca", lower: true, letterStart: true, noDoubleHyphen: true, note: "Consecutive hyphens are also rejected (generated FQDN)."),
        R(AzureNameKind.ContainerAppsEnvironment, "Microsoft.App/managedEnvironments", 2, 60, AzureNameScope.ResourceGroup, "cae", lower: true, letterStart: true, noDoubleHyphen: true, note: "Not on Microsoft's rules page; a conservative rule (same shape as container apps)."),
        R(AzureNameKind.LogAnalyticsWorkspace, "Microsoft.OperationalInsights/workspaces", 4, 63, AzureNameScope.ResourceGroup, "log"),
        R(AzureNameKind.ApplicationInsights, "Microsoft.Insights/components", 1, 260, AzureNameScope.ResourceGroup, "appi", underscore: true, period: true),
        R(AzureNameKind.AppServicePlan, "Microsoft.Web/serverfarms", 1, 60, AzureNameScope.ResourceGroup, "asp"),
        R(AzureNameKind.WebApp, "Microsoft.Web/sites", 2, 60, AzureNameScope.Global, "app", note: "Global per default host name (<name>.azurewebsites.net). Function apps use the same rules; the host id only uses the first 32 characters."),
        R(AzureNameKind.SearchService, "Microsoft.Search/searchServices", 2, 60, AzureNameScope.Global, "srch", lower: true, noDoubleHyphen: true, note: "Not on Microsoft's rules page; taken from the Azure AI Search service validation."),
        R(AzureNameKind.SignalRService, "Microsoft.SignalRService/signalR", 3, 63, AzureNameScope.Global, "sigr", letterStart: true),
        R(AzureNameKind.WebPubSubService, "Microsoft.SignalRService/webPubSub", 3, 63, AzureNameScope.Global, "wps", letterStart: true),
        R(AzureNameKind.KustoCluster, "Microsoft.Kusto/clusters", 4, 22, AzureNameScope.Region, "adx", hyphen: false, lower: true, letterStart: true),
        R(AzureNameKind.VirtualNetwork, "Microsoft.Network/virtualNetworks", 2, 64, AzureNameScope.ResourceGroup, "vnet", underscore: true, period: true),
        R(AzureNameKind.FrontDoorProfile, "Microsoft.Cdn/profiles", 1, 260, AzureNameScope.ResourceGroup, "afd"),
        R(AzureNameKind.ContainerAppJob, "Microsoft.App/jobs", 2, 32, AzureNameScope.ResourceGroup, "job", lower: true, letterStart: true, noDoubleHyphen: true, note: "Not on Microsoft's rules page; same conservative rule as container apps."),
        R(AzureNameKind.SandboxGroup, "Microsoft.App/sandboxGroups", 1, 63, AzureNameScope.ResourceGroup, "sbx", lower: true, letterStart: true, noDoubleHyphen: true, note: "Preview type, not on Microsoft's rules page; a conservative DNS-label rule."),
        R(AzureNameKind.NetworkSecurityGroup, "Microsoft.Network/networkSecurityGroups", 1, 80, AzureNameScope.ResourceGroup, "nsg", underscore: true, period: true),
        R(AzureNameKind.NatGateway, "Microsoft.Network/natGateways", 1, 80, AzureNameScope.ResourceGroup, "ng", underscore: true, period: true, note: "Not on Microsoft's rules page; the common Microsoft.Network 1-80 rule."),
        R(AzureNameKind.PublicIpAddress, "Microsoft.Network/publicIPAddresses", 1, 80, AzureNameScope.ResourceGroup, "pip", underscore: true, period: true),
        R(AzureNameKind.Subnet, "Microsoft.Network/virtualNetworks/subnets", 1, 80, AzureNameScope.Parent, "snet", underscore: true, period: true, note: "Unique within the virtual network."),
        R(AzureNameKind.PrivateEndpoint, "Microsoft.Network/privateEndpoints", 2, 64, AzureNameScope.ResourceGroup, "pe", underscore: true, period: true),
        R(AzureNameKind.PrivateDnsVirtualNetworkLink, "Microsoft.Network/privateDnsZones/virtualNetworkLinks", 1, 80, AzureNameScope.Parent, "link", underscore: true, period: true),
        R(AzureNameKind.NetworkSecurityPerimeter, "Microsoft.Network/networkSecurityPerimeters", 1, 80, AzureNameScope.ResourceGroup, "nsp", underscore: true, period: true, note: "Not on Microsoft's rules page; the common Microsoft.Network 1-80 rule."),
        R(AzureNameKind.FrontDoorEndpoint, "Microsoft.Cdn/profiles/afdEndpoints", 1, 50, AzureNameScope.Global, "fde", note: "Becomes part of the <name>-<hash>.z01.azurefd.net host name."),
        R(AzureNameKind.FrontDoorOriginGroup, "Microsoft.Cdn/profiles/originGroups", 1, 50, AzureNameScope.Parent, "og"),
        R(AzureNameKind.FrontDoorOrigin, "Microsoft.Cdn/profiles/originGroups/origins", 1, 50, AzureNameScope.Parent, "origin"),
        R(AzureNameKind.AksDnsPrefix, "Microsoft.ContainerService/managedClusters (dnsPrefix)", 1, 54, AzureNameScope.Global, "aks", note: "The DNS prefix becomes part of the cluster FQDN (<prefix>-<hash>.hcp.<region>.azmk8s.io); AKS limits it to 54 characters."),
        R(AzureNameKind.FederatedIdentityCredential, "Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials", 3, 120, AzureNameScope.Parent, "fic", underscore: true),
        R(AzureNameKind.NetworkSecurityRule, "Microsoft.Network/networkSecurityGroups/securityRules", 1, 80, AzureNameScope.Parent, "rule", underscore: true, period: true),
        R(AzureNameKind.RedisEnterpriseCluster, "Microsoft.Cache/redisEnterprise", 1, 60, AzureNameScope.Global, "redis", noDoubleHyphen: true, note: "Azure Managed Redis; the name is part of the <name>.<region>.redis.azure.net host name."),
        R(AzureNameKind.DeploymentScript, "Microsoft.Resources/deploymentScripts", 1, 90, AzureNameScope.ResourceGroup, "ds", underscore: true, period: true, alnumEnd: false, note: "Generated names never end with a period."),
        R(AzureNameKind.FrontDoorRoute, "Microsoft.Cdn/profiles/afdEndpoints/routes", 1, 50, AzureNameScope.Parent, "route"),
    }.ToDictionary(r => r.Kind);

    public static AzureNamingRule RuleFor(AzureNameKind kind) => Rules[kind];

    /// <summary>
    /// Composes a conforming name: <c>{project}-{resource}[-{suffix}][-{stack}]</c> (separators dropped for types that forbid
    /// hyphens). Always lowercase. Trimmed with a hash only when the full name does not fit or sanitizing was lossy.
    /// </summary>
    public static string Compose(AzureNameKind kind, string? projectName, string resourceName, string? stackName, string? suffix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        var rule = RuleFor(kind);
        var raw = new[] { projectName, resourceName, suffix, stackName };

        var lossy = false;
        // Priority: 0 = trimmed first (project, resource), 1 = trimmed only when still too long (suffix, stack), 2 = the start prefix (never trimmed).
        var segments = new List<(string Text, int Priority)>();
        for (var i = 0; i < raw.Length; i++)
        {
            var s = Sanitize(raw[i], rule, ref lossy);
            if (s.Length > 0) segments.Add((s, i is 0 or 1 ? 0 : 1));
        }
        if (segments.Count == 0) throw new ArgumentException("Nothing is left of the name segments after sanitizing.", nameof(resourceName));

        if (rule.MustStartWithLetter && !char.IsAsciiLetter(segments[0].Text[0]))
            segments.Insert(0, (rule.Abbreviation, 2));

        var sep = rule.AllowHyphen ? "-" : "";
        var full = string.Join(sep, segments.Select(s => s.Text));
        string name;
        if (!lossy && full.Length <= rule.Max)
        {
            name = full;
            if (name.Length < rule.Min) name += StableHash(raw, kind)[..(rule.Min - name.Length)];
        }
        else
        {
            var hash = StableHash(raw, kind);
            var trimmed = Trim(segments, rule.Max - HashLength - sep.Length, sep.Length);
            name = string.Join(sep, trimmed) + sep + hash;
            if (name.Length > rule.Max) name = name[..(rule.Max - HashLength)].TrimEnd('-') + hash; // pathological tiny budgets
        }
        return name;
    }

    /// <summary>
    /// Composes a name from the Aspire resource name alone, for resources whose names are only unique within their parent
    /// (container apps, subnets, ...). Unchanged (lowercased) when it already conforms, otherwise shortened with a hash.
    /// </summary>
    public static string ComposeScoped(AzureNameKind kind, string resourceName) => Compose(kind, null, resourceName, null);

    /// <summary>Hex hash (lowercase) of the full, un-shortened composed name; stable across runs and platforms.</summary>
    public static string StableHash(IEnumerable<string?> segments, AzureNameKind kind)
    {
        var text = string.Join("/", segments.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!.ToLowerInvariant())) + "|" + kind;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..HashLength];
    }

    /// <summary>Checks an explicit name against the rule for <paramref name="kind"/>; returns the violations (empty when valid).</summary>
    public static IReadOnlyList<string> Validate(AzureNameKind kind, string? name)
    {
        var rule = RuleFor(kind);
        var errors = new List<string>();
        if (string.IsNullOrEmpty(name)) { errors.Add("the name is empty"); return errors; }

        if (name.Length < rule.Min || name.Length > rule.Max)
            errors.Add($"length {name.Length} is outside {rule.Min}-{rule.Max}");

        var bad = name.Where(c => !IsAllowed(c, rule)).Distinct().ToArray();
        if (bad.Length > 0)
            errors.Add($"invalid character(s) '{new string(bad)}' (allowed: {rule.CharacterSummary})");

        if (rule.LowercaseOnly && name.Any(char.IsAsciiLetterUpper)) errors.Add("must be lowercase");
        if (rule.MustStartWithLetter && !char.IsAsciiLetter(name[0])) errors.Add("must start with a letter");
        else if (rule.MustStartAlphanumeric && !char.IsAsciiLetterOrDigit(name[0])) errors.Add("must start with a letter or digit");
        if (rule.MustEndAlphanumeric && !char.IsAsciiLetterOrDigit(name[^1])) errors.Add("must end with a letter or digit");
        if (kind == AzureNameKind.ResourceGroup && name[^1] == '.') errors.Add("must not end with a period");
        if (rule.NoConsecutiveHyphens && name.Contains("--", StringComparison.Ordinal)) errors.Add("must not contain consecutive hyphens");
        return errors;
    }

    /// <summary>Returns <paramref name="name"/> when valid; otherwise throws an <see cref="ArgumentException"/> naming the resource type and every violation.</summary>
    public static string ValidateOverride(AzureNameKind kind, string? name, string? paramName = null)
    {
        var errors = Validate(kind, name);
        if (errors.Count == 0) return name!;
        var rule = RuleFor(kind);
        throw new ArgumentException(
            $"'{name}' is not a valid {rule.ResourceType} name: {string.Join("; ", errors)}. " +
            $"Rule: {rule.Min}-{rule.Max} characters, {rule.CharacterSummary}, scope {rule.Scope}.", paramName ?? "name");
    }

    private static bool IsAllowed(char c, AzureNamingRule rule) =>
        char.IsAsciiLetterOrDigit(c) || (c == '-' && rule.AllowHyphen) || (c == '_' && rule.AllowUnderscore) || (c == '.' && rule.AllowPeriod);

    // Lowercases, maps separators (- _ . space) to '-' (or drops them where hyphens are forbidden), drops everything else
    // (lossy), collapses runs of hyphens and trims them off the ends.
    private static string Sanitize(string? value, AzureNamingRule rule, ref bool lossy)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var sb = new StringBuilder();
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (c is '-' or '_' or '.' or ' ')
            {
                if (rule.AllowHyphen && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            else lossy = true;
        }
        return sb.ToString().Trim('-');
    }

    // Shortens the longest trimmable segment one character at a time until the joined length fits the budget:
    // project/resource first, then suffix/stack only if still too long.
    private static List<string> Trim(List<(string Text, int Priority)> segments, int budget, int sepLength)
    {
        var parts = segments.ToList();
        int Total() => parts.Sum(p => p.Text.Length) + sepLength * Math.Max(0, parts.Count - 1);
        void Shorten(int i) => parts[i] = (parts[i].Text[..^1].TrimEnd('-'), parts[i].Priority);

        foreach (var (maxPriority, floor) in new[] { (0, 3), (1, 3), (1, 2), (1, 1) })
        {
            while (Total() > budget)
            {
                var idx = -1;
                for (var i = 0; i < parts.Count; i++)
                    if (parts[i].Priority <= maxPriority && parts[i].Text.Length > floor
                        && (idx < 0 || parts[i].Text.Length > parts[idx].Text.Length))
                        idx = i;
                if (idx < 0) break;
                Shorten(idx);
            }
        }
        return parts.Where(p => p.Text.Length > 0).Select(p => p.Text).ToList();
    }
}
