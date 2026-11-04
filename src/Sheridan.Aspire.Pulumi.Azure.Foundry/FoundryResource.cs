using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.Network;

namespace Sheridan.Aspire.Pulumi.Azure.Foundry;

/// <summary>
/// A Microsoft Foundry account provisioned by Pulumi (azure-native), mirroring Aspire.Hosting.Foundry's Bicep: a
/// <c>Microsoft.CognitiveServices/accounts</c> of kind AIServices (S0, system-assigned identity, local auth disabled, public
/// network access enabled unless a private endpoint targets it, project management allowed), an Agents capability host
/// (with <c>enablePublicHostingEnvironment</c>), the model deployments (created one after another) and the projects added to
/// the builder. Compute environments hosting an app that references the account, one of its deployments or one of its projects
/// are granted Cognitive Services User and Cognitive Services OpenAI User by default (override with WithRoleAssignments).
/// Resources a project connects to (Application Insights, Key Vault, Storage, Search, Cosmos DB, Container Registry, another
/// account) must be provisioned in the same Pulumi stack, because the project's connections reference them directly.
/// </summary>
public sealed class FoundryResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString, IPulumiPrivateEndpointTarget
{
    internal static readonly FoundryRole[] DefaultRoles = [FoundryRole.CognitiveServicesUser, FoundryRole.CognitiveServicesOpenAIUser];

    /// <summary>Account SKU (default S0, as in the official integration).</summary>
    public string Sku { get; set; } = "S0";

    /// <summary>
    /// Custom sub-domain of the account endpoint (globally unique, 2-64 letters/digits/hyphens). When null it is composed as
    /// <c>{app}-{resource}-{stack}</c> (shortened with a stable hash when too long; see docs/naming.md). Can also be pinned with WithAzureName.
    /// </summary>
    public string? CustomSubDomainName { get; set; }

    /// <summary>Name of an existing account (set by PublishAsExisting); nothing is created for the account itself, but deployments, projects and role assignments are still applied to it.</summary>
    public string? ExistingAccountName { get; internal set; }

    /// <summary>The Foundry Local endpoint when running locally (see <c>RunAsFoundryLocal</c>); null when the account runs in Azure.</summary>
    public Uri? EmulatorServiceUri { get; internal set; }

    /// <summary>True when the resource runs against a Foundry Local endpoint instead of Azure.</summary>
    public bool IsEmulator => EmulatorServiceUri is not null;

    /// <summary>The (dummy) API key Foundry Local expects.</summary>
    public string? ApiKey { get; internal set; }

    public List<FoundryDeploymentResource> Deployments { get; } = [];
    public List<FoundryProjectResource> Projects { get; } = [];

    /// <summary>Pulumi identifier to reference the account (<c>${x.name}</c>, <c>${x.id}</c>): the resource, or the lookup variable for an existing account.</summary>
    internal string AccountRef => ExistingAccountName is null ? Id : $"{Id}Existing";

    // IPulumiPrivateEndpointTarget: group 'account', registered in the three zones the official integration uses.
    string IPulumiAzureResourceWithId.IdExpression => $"${{{AccountRef}.id}}";
    string IPulumiPrivateEndpointTarget.PrivateLinkGroupId => "account";
    string IPulumiPrivateEndpointTarget.PrivateDnsZoneName => "privatelink.cognitiveservices.azure.com";
    IReadOnlyList<string> IPulumiPrivateEndpointTarget.AdditionalPrivateDnsZoneNames =>
        ["privatelink.services.ai.azure.com", "privatelink.openai.azure.com"];

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The account endpoint, e.g. <c>https://myfoundry.cognitiveservices.azure.com/</c>.</summary>
    public PulumiOutputReference Endpoint => OutputRef("Endpoint");

    /// <summary>The AI Foundry API endpoint, e.g. <c>https://myfoundry.services.ai.azure.com/</c>.</summary>
    public PulumiOutputReference AIFoundryApiEndpoint => OutputRef("AiFoundryApiEndpoint");
    public PulumiOutputReference NameOutput => OutputRef("Name");

    /// <summary>The connection string, <c>Endpoint=&lt;endpoint&gt;;EndpointAIInference=&lt;AI Foundry API endpoint&gt;models</c> (<c>Endpoint=&lt;local&gt;;Key=&lt;key&gt;</c> for Foundry Local).</summary>
    public ReferenceExpression ConnectionStringExpression => IsEmulator
        ? ReferenceExpression.Create($"Endpoint={EmulatorServiceUri!.ToString()};Key={ApiKey}")
        : ReferenceExpression.Create($"Endpoint={Endpoint};EndpointAIInference={AIFoundryApiEndpoint}models");

    /// <summary>The service endpoint URI expression (the AI Foundry API endpoint, or the Foundry Local endpoint).</summary>
    public ReferenceExpression UriExpression => IsEmulator
        ? ReferenceExpression.Create($"{EmulatorServiceUri!.ToString()}")
        : ReferenceExpression.Create($"{AIFoundryApiEndpoint}");

    /// <summary>The custom sub-domain: the explicit <see cref="CustomSubDomainName"/> (validated) or one composed from the app, resource and stack names.</summary>
    private string SubDomain() => CustomSubDomainName is { } custom
        ? AzureNaming.ValidateOverride(AzureNameKind.CognitiveServicesSubdomain, custom, nameof(CustomSubDomainName))
        : NameFor(AzureNameKind.CognitiveServicesSubdomain);

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        program.AddClientConfig();

        if (IsEmulator)
            throw new InvalidOperationException($"'{Name}' runs against Foundry Local (RunAsFoundryLocal) and has nothing to provision; it is only available outside publish mode.");

        // A private endpoint targeting the account turns public network access off, as the official integration does.
        var hasPrivateEndpoint = context.Model.Resources.OfType<PulumiPrivateEndpointResource>().Any(e => ReferenceEquals(e.Target, this));

        var acct = AccountRef;
        if (ExistingAccountName is { } existing)
        {
            program.Variables[acct] = new Dictionary<string, object?>
            {
                ["fn::invoke"] = new Dictionary<string, object?>
                {
                    ["function"] = "azure-native:cognitiveservices:getAccount",
                    ["arguments"] = new Dictionary<string, object?> { ["resourceGroupName"] = rg, ["accountName"] = existing },
                },
            };
        }
        else
        {
            // accountName and the custom sub-domain are composed from the app, resource and stack names (see docs/naming.md),
            // replacing the provider's random suffix / Bicep's uniqueString.
            // An infra target: role assignments are created in the targeted provision pass and depend on the account.
            program.AddInfraResource(Id, "azure-native:cognitiveservices:Account", new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cognitiveservices:Account",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["accountName"] = NameFor(AzureNameKind.CognitiveServicesAccount),
                    ["location"] = location,
                    ["kind"] = "AIServices",
                    ["identity"] = new Dictionary<string, object?> { ["type"] = "SystemAssigned" },
                    ["sku"] = new Dictionary<string, object?> { ["name"] = Sku },
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["customSubDomainName"] = SubDomain(),
                        ["publicNetworkAccess"] = hasPrivateEndpoint ? "Disabled" : "Enabled",
                        ["disableLocalAuth"] = true,
                        ["allowProjectManagement"] = true,
                    },
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
                },
            });
        }

        // The default CapabilityHost type of azure-native does not expose enablePublicHostingEnvironment (the Bicep sets it, API
        // 2025-10-01-preview); the GA v20251201 version does, so that version of the resource is used.
        program.Resources[$"{Id}Caphost"] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cognitiveservices/v20251201:AccountCapabilityHost",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["accountName"] = $"${{{acct}.name}}",
                ["capabilityHostName"] = "foundry-caphost",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["capabilityHostKind"] = "Agents",
                    ["enablePublicHostingEnvironment"] = true,
                },
            },
        };

        string? previous = null;
        foreach (var d in Deployments)
        {
            var id = $"{Id}{d.Id}";
            var def = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cognitiveservices:Deployment",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["accountName"] = $"${{{acct}.name}}",
                    ["deploymentName"] = d.DeploymentName,
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["model"] = new Dictionary<string, object?>
                        {
                            ["format"] = d.Format,
                            ["name"] = d.ModelName,
                            ["version"] = d.ModelVersion,
                        },
                    },
                    ["sku"] = new Dictionary<string, object?> { ["name"] = d.SkuName, ["capacity"] = d.SkuCapacity },
                },
            };
            // Azure rejects concurrent deployment writes on one account, so chain them.
            if (previous is not null)
                def["options"] = new Dictionary<string, object?> { ["dependsOn"] = new List<object?> { $"${{{previous}}}" } };
            program.Resources[id] = def;
            previous = id;
        }

        foreach (var p in Projects) AddProject(program, p, rg, location);

        program.Outputs[$"{Id}AiFoundryApiEndpoint"] = $"${{{acct}.properties.endpoints[\"AI Foundry API\"]}}";
        program.Outputs[$"{Id}Endpoint"] = $"${{{acct}.properties.endpoint}}";
        program.Outputs[$"{Id}Name"] = $"${{{acct}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{acct}.id}}";

        AddRoleAssignments(program, context);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A project (system-assigned identity) with an Application Insights component (a new one, or the one given to WithAppInsights)
    /// the project identity may publish metrics to, connected to the project through an ApiKey connection (category AppInsights),
    /// plus its other connections and capability host, as the official Bicep does.
    /// </summary>
    private void AddProject(PulumiProgram program, FoundryProjectResource p, string rg, string location)
    {
        var acct = AccountRef;
        var id = $"{Id}{p.Id}";
        program.Resources[id] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cognitiveservices:Project",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["accountName"] = $"${{{acct}.name}}",
                ["projectName"] = p.Name,
                ["location"] = location,
                ["identity"] = new Dictionary<string, object?> { ["type"] = "SystemAssigned" },
                ["properties"] = new Dictionary<string, object?> { ["displayName"] = p.Name },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = p.Name },
            },
        };

        var principal = $"${{{id}.identity.principalId}}";
        var ctx = new FoundryConnectionContext(program, this, p, id, rg, location);

        // The Key Vault connection (WithKeyVault) is created first; every other project connection depends on it.
        string? kvConnId = null;
        if (p.KeyVaultConnection is { } kvConn) kvConnId = EmitConnection(ctx, kvConn, null);

        string aiId, aiLocation, aiConnectionString;
        if (p.AppInsights is { } existingAi)
        {
            aiId = existingAi.Id;
            aiLocation = $"${{{aiId}.location}}";
            aiConnectionString = $"${{{aiId}.connectionString}}";
        }
        else
        {
            aiId = $"{id}Ai";
            aiLocation = $"${{{aiId}.location}}";
            aiConnectionString = $"${{{aiId}.connectionString}}";
            program.Resources[aiId] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:applicationinsights:Component",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["resourceName"] = NameFor(AzureNameKind.ApplicationInsights, $"{p.Name}-appi"),
                    ["location"] = location,
                    ["kind"] = "web",
                    ["applicationType"] = "web",
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = p.Name },
                },
            };
        }

        // Monitoring Metrics Publisher for the project identity on the Application Insights component.
        program.AddRoleAssignment(aiId.StartsWith(id, StringComparison.Ordinal) ? $"{aiId}MonitoringMetricsPublisher" : $"{id}{aiId}MonitoringMetricsPublisher", principal,
            "3913510d-42f4-4e42-8a64-420c390055eb", $"${{{aiId}.id}}");

        var aiConn = $"{id}AiConn";
        program.Resources[aiConn] = WithDependsOn(new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cognitiveservices:ProjectConnection",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["accountName"] = $"${{{acct}.name}}",
                ["projectName"] = $"${{{id}.name}}",
                ["connectionName"] = $"{p.Name}-ai-conn",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["authType"] = "ApiKey",
                    ["category"] = "AppInsights",
                    ["isSharedToAll"] = false,
                    ["target"] = $"${{{aiId}.id}}",
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["ApiType"] = "Azure",
                        ["ResourceId"] = $"${{{aiId}.id}}",
                        ["location"] = aiLocation,
                    },
                    ["credentials"] = new Dictionary<string, object?> { ["key"] = aiConnectionString },
                },
            },
        }, kvConnId);

        foreach (var conn in p.Connections.Where(c => c != p.KeyVaultConnection)) EmitConnection(ctx, conn, kvConnId);

        if (p.CapabilityHost is { } host) AddProjectCapabilityHost(ctx, host, kvConnId);

        program.Outputs[$"{id}Id"] = $"${{{id}.id}}";
        program.Outputs[$"{id}Name"] = $"${{{acct}.name}}/{p.Name}";
        program.Outputs[$"{id}Endpoint"] = $"${{{id}.properties.endpoints[\"AI Foundry API\"]}}";
        program.Outputs[$"{id}PrincipalId"] = principal;
        program.Outputs[$"{id}AppInsightsConnectionString"] = aiConnectionString;
    }

    private static Dictionary<string, object?> WithDependsOn(Dictionary<string, object?> def, params string?[] dependsOn)
    {
        var list = dependsOn.Where(d => d is not null).Select(d => (object?)$"${{{d}}}").ToList();
        if (list.Count > 0) def["options"] = new Dictionary<string, object?> { ["dependsOn"] = list };
        return def;
    }

    /// <summary>Adds a project (or, for Bing grounding, account) connection and its role assignments; returns its Pulumi identifier.</summary>
    private string EmitConnection(FoundryConnectionContext ctx, FoundryProjectConnectionResource conn, string? kvConnId)
    {
        var acct = AccountRef;
        var connId = $"{ctx.ProjectId}{conn.Id}";
        var props = new Dictionary<string, object?>
        {
            ["resourceGroupName"] = ctx.ResourceGroup,
            ["accountName"] = $"${{{acct}.name}}",
            ["connectionName"] = conn.ConnectionName,
            ["properties"] = conn.Configure(ctx),
        };
        if (!conn.AccountLevel) props["projectName"] = $"${{{ctx.ProjectId}.name}}";

        // Role assignments for the connected resource are created first (they only need the project identity), no dependency on the connection.
        conn.AddRoleAssignments?.Invoke(ctx);

        ctx.Program.Resources[connId] = WithDependsOn(new Dictionary<string, object?>
        {
            ["type"] = conn.AccountLevel ? "azure-native:cognitiveservices:AccountConnection" : "azure-native:cognitiveservices:ProjectConnection",
            ["properties"] = props,
        }, ReferenceEquals(conn, ctx.Project.KeyVaultConnection) ? null : kvConnId);

        ctx.Program.Outputs[$"{connId}Name"] = $"${{{connId}.name}}";
        ctx.Program.Outputs[$"{connId}Id"] = $"${{{connId}.id}}";
        return connId;
    }

    /// <summary>
    /// The project capability host: connections to Storage (file storage), Cosmos DB (thread storage), Search (vector store) and,
    /// optionally, another account for Azure OpenAI, each with the role assignments the official Bicep creates for the project identity.
    /// </summary>
    private void AddProjectCapabilityHost(FoundryConnectionContext ctx, FoundryCapabilityHostConfiguration host, string? kvConnId)
    {
        host.Validate(ctx.Project.Name);
        var acct = AccountRef;
        var program = ctx.Program;
        var dependsOn = new List<string?> { kvConnId };

        string HostConnection(string suffix, FoundryResourceTargets.Target target)
        {
            var connId = $"{ctx.ProjectId}{suffix}Conn";
            program.Resources[connId] = WithDependsOn(new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cognitiveservices:ProjectConnection",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = ctx.ResourceGroup,
                    ["accountName"] = $"${{{acct}.name}}",
                    ["projectName"] = $"${{{ctx.ProjectId}.name}}",
                    ["connectionName"] = $"${{{ctx.ProjectId}.name}}-${{{target.Ref}.name}}",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["authType"] = "AAD",
                        ["category"] = target.Category,
                        ["target"] = target.TargetValue,
                        ["metadata"] = FoundryConnectionContext.Metadata(target.Ref),
                    },
                },
            }, kvConnId);
            dependsOn.Add(connId);
            return connId;
        }

        var storage = FoundryResourceTargets.Storage(host.Storage!);
        var storageConn = HostConnection("Storage", storage);
        dependsOn.Add(ctx.Role($"{storage.Ref}BlobDataContributor", AzureRoleDefinitions.StorageBlobDataContributor, $"${{{storage.Ref}.id}}"));

        var cosmos = FoundryResourceTargets.Cosmos(host.CosmosDB!);
        var cosmosConn = HostConnection("Cosmos", cosmos);
        dependsOn.Add(ctx.CosmosDataContributor(host.CosmosDB!));

        var search = FoundryResourceTargets.Search(host.Search!);
        var searchConn = HostConnection("Search", search);
        dependsOn.Add(ctx.Role($"{search.Ref}SearchServiceContributor", FoundryRoleIds.SearchServiceContributor, $"${{{search.Ref}.id}}"));
        dependsOn.Add(ctx.Role($"{search.Ref}SearchIndexDataContributor", FoundryRoleIds.SearchIndexDataContributor, $"${{{search.Ref}.id}}"));

        var properties = new Dictionary<string, object?>
        {
            ["storageConnections"] = new List<object?> { $"${{{storageConn}.name}}" },
            ["threadStorageConnections"] = new List<object?> { $"${{{cosmosConn}.name}}" },
            ["vectorStoreConnections"] = new List<object?> { $"${{{searchConn}.name}}" },
        };

        if (host.AzureOpenAI is { } openAi)
        {
            var aoai = FoundryResourceTargets.AzureOpenAI(openAi);
            var aoaiConn = HostConnection("Aoai", aoai);
            dependsOn.Add(ctx.Role($"{aoai.Ref}OpenAIUser", FoundryRoles.RoleDefinitionId(FoundryRole.CognitiveServicesOpenAIUser), $"${{{aoai.Ref}.id}}"));
            properties["aiServicesConnections"] = new List<object?> { $"${{{aoaiConn}.name}}" };
        }

        // The official Bicep also sets capabilityHostKind/enablePublicHostingEnvironment on the project capability host; azure-native's
        // ProjectCapabilityHostProperties (every API version) only exposes the connection lists, so they are omitted here.
        program.Resources[$"{ctx.ProjectId}{FoundryProjectResource.CapabilityHostIdSuffix}"] = WithDependsOn(new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cognitiveservices/v20251201:ProjectCapabilityHost",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = ctx.ResourceGroup,
                ["accountName"] = $"${{{acct}.name}}",
                ["projectName"] = $"${{{ctx.ProjectId}.name}}",
                ["capabilityHostName"] = $"${{{ctx.ProjectId}.name}}-ch",
                ["properties"] = properties,
            },
        }, dependsOn.Where(d => d is not null).Distinct().ToArray());
    }

    private bool IsReferencedBy(IResource consumer)
    {
        var referenced = consumer.Annotations.OfType<ResourceRelationshipAnnotation>().Select(a => a.Resource).ToList();
        return referenced.Contains(this) || Deployments.Any(referenced.Contains) || Projects.Any(referenced.Contains);
    }

    /// <summary>Grants each compute environment hosting a referencing app its roles on the account.</summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx)
    {
        var grants = new Dictionary<IPulumiManagedIdentityResource, HashSet<FoundryRole>>();
        foreach (var consumer in ctx.Model.GetComputeResources().Where(IsReferencedBy))
        {
            var custom = consumer.Annotations.OfType<FoundryRoleAnnotation>().Where(a => a.Target == this).ToList();
            IEnumerable<FoundryRole> roles = custom.Count > 0 ? custom.SelectMany(a => a.Roles) : DefaultRoles;
            foreach (var env in consumer.Annotations.OfType<DeploymentTargetAnnotation>()
                         .Select(a => a.ComputeEnvironment).OfType<IPulumiManagedIdentityResource>())
            {
                if (!grants.TryGetValue(env, out var set)) grants[env] = set = [];
                set.UnionWith(roles);
            }
        }

        foreach (var (env, roles) in grants)
        {
            var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
            foreach (var role in roles)
                program.AddRoleAssignment($"{Id}{envId}{role}", env.PrincipalIdExpression,
                    FoundryRoles.RoleDefinitionId(role), $"${{{AccountRef}.id}}");
        }
    }
}

/// <summary>A model deployment of a <see cref="FoundryResource"/> (connection string = account connection string + <c>;Deployment=</c>).</summary>
public sealed class FoundryDeploymentResource : Resource, IResourceWithConnectionString, IResourceWithParent<FoundryResource>
{
    private string _deploymentName;
    private string _modelName;
    private string _modelVersion;
    private string _format;
    private string _skuName = "GlobalStandard";

    public FoundryDeploymentResource(string name, string modelName, string modelVersion, string format, FoundryResource parent) : base(name)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        ArgumentException.ThrowIfNullOrEmpty(modelVersion);
        ArgumentException.ThrowIfNullOrEmpty(format);
        ArgumentNullException.ThrowIfNull(parent);
        _deploymentName = name;
        _modelName = modelName;
        _modelVersion = modelVersion;
        _format = format;
        Parent = parent;
    }

    public FoundryResource Parent { get; }

    /// <summary>The deployment name in Azure (defaults to the resource name).</summary>
    public string DeploymentName { get => _deploymentName; set { ArgumentException.ThrowIfNullOrEmpty(value); _deploymentName = value; } }
    public string ModelName { get => _modelName; set { ArgumentException.ThrowIfNullOrEmpty(value); _modelName = value; } }
    public string ModelVersion { get => _modelVersion; set { ArgumentException.ThrowIfNullOrEmpty(value); _modelVersion = value; } }
    /// <summary>The model format / publisher, e.g. <c>OpenAI</c>.</summary>
    public string Format { get => _format; set { ArgumentException.ThrowIfNullOrEmpty(value); _format = value; } }
    /// <summary>Deployment SKU (default GlobalStandard).</summary>
    public string SkuName { get => _skuName; set { ArgumentException.ThrowIfNullOrEmpty(value); _skuName = value; } }
    /// <summary>Deployment SKU capacity (default 1).</summary>
    public int SkuCapacity { get; set; } = 1;

    /// <summary>The model id Foundry Local serves the deployment as (defaults to the model name).</summary>
    public string? LocalModelId { get; set; }

    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    public ReferenceExpression ConnectionStringExpression => Parent.IsEmulator
        ? ReferenceExpression.Create($"{Parent.ConnectionStringExpression};Model={LocalModelId ?? ModelName}")
        : ReferenceExpression.Create($"{Parent.ConnectionStringExpression};Deployment={DeploymentName}");
}

/// <summary>Roles the annotated resource's identity should get on an account (replaces the default).</summary>
internal sealed class FoundryRoleAnnotation(FoundryResource target, FoundryRole[] roles) : IResourceAnnotation
{
    public FoundryResource Target { get; } = target;
    public FoundryRole[] Roles { get; } = roles;
}
