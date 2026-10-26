using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.AppContainers;
using AspirePulumiPackages.Pulumi.Azure.AppService;
using AspirePulumiPackages.Pulumi.Azure.Base;

namespace AspirePulumiPackages.Pulumi.Azure.FrontDoor;

/// <summary>
/// An Azure Front Door (Standard_AzureFrontDoor) profile provisioned by Pulumi (azure-native), mirroring
/// Aspire.Hosting.Azure.FrontDoor's Bicep: a global <c>cdn:Profile</c> and, per origin added with WithOrigin, an
/// <c>AFDEndpoint</c>, <c>AFDOriginGroup</c> (health probe + load balancing 4/3/50), <c>AFDOrigin</c> and a <c>/*</c> <c>Route</c>
/// (HttpsOnly forwarding, HTTPS redirect, linked to the default domain). Origins must be deployed to a compute
/// environment in the same Pulumi stack.
/// </summary>
public sealed class AzureFrontDoorResource(string name) : PulumiAzureResource(name)
{
    internal List<IResourceWithEndpoints> Origins { get; } = [];

    internal static string OriginKey(string originName) => PulumiAzureProgramExtensions.ToPulumiId(originName);
    internal string EndpointId(string originName) => $"{Id}{OriginKey(originName)}Endpoint";
    internal string OriginGroupId(string originName) => $"{Id}{OriginKey(originName)}OriginGroup";
    internal string OriginId(string originName) => $"{Id}{OriginKey(originName)}Origin";
    internal string RouteId(string originName) => $"{Id}{OriginKey(originName)}Route";

    /// <summary>The endpoint URL (<c>https://*.azurefd.net</c>) serving the named origin (its Aspire resource name).</summary>
    public PulumiOutputReference GetEndpointUrl(string originResourceName) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."),
            $"{OriginKey(originResourceName)}EndpointUrl");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        program.Resources[Id] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cdn:Profile",
            ["properties"] = new Dictionary<string, object?>
            {
                ["profileName"] = NameFor(AzureNameKind.FrontDoorProfile),
                ["resourceGroupName"] = rg,
                ["location"] = "Global",
                ["sku"] = new Dictionary<string, object?> { ["name"] = "Standard_AzureFrontDoor" },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        };

        foreach (var origin in Origins)
        {
            var (host, probePath, probeProtocol) = ResolveOrigin(origin);

            var profileName = $"${{{Id}.name}}";
            program.Resources[EndpointId(origin.Name)] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cdn:AFDEndpoint",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["profileName"] = profileName,
                    ["endpointName"] = NameForChild(AzureNameKind.FrontDoorEndpoint, origin.Name),
                    ["location"] = "Global",
                },
            };
            program.Resources[OriginGroupId(origin.Name)] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cdn:AFDOriginGroup",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["profileName"] = profileName,
                    ["originGroupName"] = AzureNaming.ComposeScoped(AzureNameKind.FrontDoorOriginGroup, origin.Name),
                    ["healthProbeSettings"] = new Dictionary<string, object?> { ["probePath"] = probePath, ["probeProtocol"] = probeProtocol },
                    ["loadBalancingSettings"] = new Dictionary<string, object?>
                    {
                        ["sampleSize"] = 4,
                        ["successfulSamplesRequired"] = 3,
                        ["additionalLatencyInMilliseconds"] = 50,
                    },
                },
            };
            program.Resources[OriginId(origin.Name)] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cdn:AFDOrigin",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["profileName"] = profileName,
                    ["originGroupName"] = $"${{{OriginGroupId(origin.Name)}.name}}",
                    ["originName"] = AzureNaming.ComposeScoped(AzureNameKind.FrontDoorOrigin, origin.Name),
                    ["hostName"] = host,
                    ["originHostHeader"] = host,
                },
            };
            program.Resources[RouteId(origin.Name)] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cdn:Route",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["profileName"] = profileName,
                    ["endpointName"] = $"${{{EndpointId(origin.Name)}.name}}",
                    ["routeName"] = AzureNaming.ComposeScoped(AzureNameKind.FrontDoorRoute, origin.Name),
                    ["originGroup"] = new Dictionary<string, object?> { ["id"] = $"${{{OriginGroupId(origin.Name)}.id}}" },
                    ["patternsToMatch"] = new List<object?> { "/*" },
                    ["forwardingProtocol"] = "HttpsOnly",
                    ["linkToDefaultDomain"] = "Enabled",
                    ["httpsRedirect"] = "Enabled",
                },
                ["options"] = new Dictionary<string, object?> { ["dependsOn"] = new List<object?> { $"${{{OriginId(origin.Name)}}}" } },
            };
            program.Outputs[$"{Id}{OriginKey(origin.Name)}EndpointUrl"] = $"https://${{{EndpointId(origin.Name)}.hostName}}";
        }

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>Resolves the origin host (a Pulumi expression), health probe path and protocol, as the official integration does.</summary>
    private static (string Host, string ProbePath, string ProbeProtocol) ResolveOrigin(IResourceWithEndpoints origin)
    {
        if (!origin.GetEndpoints().Any(e => e.EndpointAnnotation.UriScheme is "http" or "https" && e.EndpointAnnotation.IsExternal))
            throw new InvalidOperationException(
                $"Resource '{origin.Name}' does not have an external HTTP or HTTPS endpoint. Azure Front Door requires an origin to expose an external HTTP or HTTPS endpoint. Call .WithExternalHttpEndpoints() on the resource before adding it as an origin.");

        var env = origin.GetComputeEnvironment()
            ?? origin.Annotations.OfType<DeploymentTargetAnnotation>().Select(a => a.ComputeEnvironment).FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Resource '{origin.Name}' does not have a compute environment. Ensure a compute environment (Azure Container Apps or App Service) is configured in the application model.");

        var key = PulumiAzureProgramExtensions.ToPulumiId(origin.Name);
        var host = env switch
        {
            AzureContainerAppEnvironmentResource => $"${{{key}App.configuration.ingress.fqdn}}",
            AzureAppServiceEnvironmentResource => $"${{{key}Site.defaultHostName}}",
            _ => throw new NotSupportedException(
                $"Compute environment '{env.Name}' ({env.GetType().Name}) is not supported as a Front Door origin host; use Azure Container Apps or App Service."),
        };

        var probe = origin.Annotations.OfType<EndpointProbeAnnotation>().OrderBy(p => p.Type == ProbeType.Liveness ? 0 : 1).FirstOrDefault();
        return probe is null ? (host, "/", "Https") : (host, probe.Path, probe.EndpointReference.Scheme == "http" ? "Http" : "Https");
    }
}
