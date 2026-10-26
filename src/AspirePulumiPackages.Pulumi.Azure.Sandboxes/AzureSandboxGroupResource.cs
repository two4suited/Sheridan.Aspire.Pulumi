using System.Collections.Concurrent;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.ContainerRegistry;
using Microsoft.Extensions.Logging;

namespace AspirePulumiPackages.Pulumi.Azure.Sandboxes;

/// <summary>
/// An Azure Container Apps sandbox group provisioned by Pulumi, the counterpart of <c>AzureSandboxGroupResource</c> in
/// Aspire.Hosting.Azure.Sandboxes. Pulumi provisions the ARM half: the sandbox group (<c>Microsoft.App/sandboxGroups</c>), the
/// container registry (<c>{name}-acr</c>, Basic) it pulls images from, a dedicated image-pull user-assigned identity
/// (<c>{name}-mi</c>) with <c>AcrPull</c> on the registry, and the <c>Container Apps SandboxGroup Data Owner</c> role for the
/// deploying principal. Like the official resource it is a compute environment: compute resources that target it are built,
/// pushed to the registry and then deployed (disk image, sandbox, lifecycle policy, ports) through the Azure Dev Compute data
/// plane by a pipeline step that runs after the stack has deployed (the data plane is not an ARM API, so Pulumi cannot manage it).
/// </summary>
#pragma warning disable ASPIRECOMPUTE002
public sealed class AzureSandboxGroupResource : PulumiAzureResource, IComputeEnvironmentResource, IContainerRegistry
{
    // https://learn.microsoft.com/azure/role-based-access-control/built-in-roles#container-apps-sandboxgroup-data-owner
    internal const string SandboxGroupDataOwnerRoleId = "c24cf47c-5077-412d-a19c-45202126392c";
    internal const string AcrLoginTag = "acr-login";

    // azure-native 3.x only models the group as Microsoft.ContainerInstance/sandboxGroups (a type that does not exist in that
    // resource provider, see the README). The real type is Microsoft.App/sandboxGroups, created here through azure-native's
    // generic ARM resource, with the same api-version as the official Bicep (override with ConfigureInfrastructure: Set("apiVersion", ...)).
    internal const string GroupResourceType = "azure-native:resources:Resource";
    internal const string GroupApiVersion = "2026-02-01-preview";

    internal bool SystemAssigned { get; set; }
    internal List<PulumiAzureUserAssignedIdentityResource> WorkloadIdentities { get; } = [];

    /// <summary>A registry to use instead of the group's own <c>{name}-acr</c> (see <c>WithAzureContainerRegistry</c>).</summary>
    public AzureContainerRegistryResource? Registry { get; internal set; }

    /// <summary>An image-pull identity to use instead of the group's own <c>{name}-mi</c> (see <c>WithAcrPullIdentity</c>).</summary>
    public PulumiAzureUserAssignedIdentityResource? PullIdentity { get; internal set; }

    /// <summary>Name of an existing sandbox group (set by PublishAsExisting); when set, no group is created.</summary>
    public string? ExistingGroupName { get; internal set; }

    internal bool IsExisting => ExistingGroupName is not null;
    internal string RegistryId => Registry?.Id ?? $"{Id}Acr";
    internal string PullIdentityId => PullIdentity?.Id ?? $"{Id}Mi";
    internal bool OwnsRegistry => Registry is null;
    internal bool OwnsPullIdentity => PullIdentity is null;

    /// <summary>Pulumi identifier of the group resource (created) or of the lookup variable (existing); both expose name/id/location.</summary>
    internal string GroupRef => IsExisting ? $"{Id}Existing" : Id;

    /// <summary>The name of the group's ARM resource.</summary>
    internal string GroupName => ExistingGroupName ?? NameFor(AzureNameKind.SandboxGroup);

    /// <summary>Public URLs of the sandboxes deployed in this run, keyed by compute resource name; filled by the deploy steps so that later sandboxes can reference them.</summary>
    internal ConcurrentDictionary<string, IReadOnlyList<SandboxPortInfo>> DeployedPorts { get; } = new(StringComparer.Ordinal);

    private PulumiStackResource? _resolvedStack;

    public AzureSandboxGroupResource(string name) : base(name)
    {
        Annotations.Add(new PipelineStepAnnotation(async factoryContext =>
        {
            var steps = new List<PipelineStep>
            {
                new()
                {
                    Name = $"sandbox-prepare-{Name}",
                    Description = $"Targets compute resources at the sandbox group '{Name}'",
                    Action = PrepareDeploymentTargetsAsync,
                    DependsOnSteps = [WellKnownPipelineSteps.ValidateComputeEnvironments],
                    RequiredBySteps = [WellKnownPipelineSteps.BeforeStart],
                },
                new()
                {
                    Name = $"acr-login-{Name}",
                    Description = $"Log the container runtime in to the registry of '{Name}'",
                    Tags = [AcrLoginTag],
                    Action = LoginToRegistryAsync,
                },
                new()
                {
                    Name = $"sandbox-dashboard-{Name}",
                    Description = $"Adds the sandbox dashboard for '{Name}' to the deployment summary",
                    Action = AddDashboardToSummaryAsync,
                    Tags = ["print-summary"],
                },
                new()
                {
                    Name = $"sandbox-destroy-{Name}",
                    Description = $"Deletes the sandboxes and disk images Aspire deployed to '{Name}'",
                    Action = ctx => AzureSandboxDeployment.DestroyAsync(this, ctx),
                    DependsOnSteps = [WellKnownPipelineSteps.DestroyPrereq],
                    RequiredBySteps = [WellKnownPipelineSteps.Destroy],
                },
            };

            foreach (var computeResource in factoryContext.PipelineContext.Model.GetComputeResources())
            {
                if (computeResource.GetDeploymentTargetAnnotation(this)?.DeploymentTarget is not AzureSandboxContainerResource target) continue;
                foreach (var step in AzureSandboxDeployment.CreateDeploySteps(this, target))
                {
                    step.Resource ??= target;
                    steps.Add(step);
                }
            }

            return (IEnumerable<PipelineStep>)steps;
        }));

        Annotations.Add(new PipelineConfigurationAnnotation(async context =>
        {
            var stack = Stack ?? context.Model.Resources.OfType<PulumiStackResource>().SingleOrDefault();
            if (stack is null) return;

            var provision = context.GetSteps(stack, WellKnownPipelineTags.ProvisionInfrastructure);
            var deploy = context.GetSteps(stack, WellKnownPipelineTags.DeployCompute);
            var login = context.GetSteps(this, AcrLoginTag);
            login.DependsOn(provision);

            // The dashboard URL needs the stack's outputs (subscription, resource group, group name).
            var dashboard = context.GetSteps(this).Where(s => s.Name == $"sandbox-dashboard-{Name}").ToList();
            dashboard.DependsOn(deploy);
            dashboard.RequiredBy(WellKnownPipelineSteps.Deploy);

            // The group deletion would fail (or orphan data) while sandboxes exist, so remove them before the stack is destroyed.
            var publishStack = context.GetSteps(stack).Where(s => s.Name == $"pulumi-publish-{stack.Name}").ToList();
            var destroyStack = context.GetSteps(stack).Where(s => s.Name == $"pulumi-destroy-{stack.Name}").ToList();
            var destroySandboxes = context.GetSteps(this).Where(s => s.Name == $"sandbox-destroy-{Name}").ToList();
            destroySandboxes.DependsOn(publishStack);
            destroyStack.DependsOn(destroySandboxes);

            foreach (var r in context.Model.GetComputeResources().Where(IsTargeted))
            {
                var target = (AzureSandboxContainerResource)r.GetDeploymentTargetAnnotation(this)!.DeploymentTarget!;
                var push = context.GetSteps(r, WellKnownPipelineTags.PushContainerImage);
                push.DependsOn(login);

                // Sandboxes are created through the data plane once the image is in the registry and the stack (including the
                // deployer's data-owner role assignment) is deployed.
                var deploySandbox = context.GetSteps(target, WellKnownPipelineTags.DeployCompute).ToList();
                deploySandbox.DependsOn(push);
                deploySandbox.DependsOn(login);
                deploySandbox.DependsOn(deploy);
                await AzureSandboxDeployment.ConfigureReferenceOrderingAsync(context, this, target, deploySandbox);
            }

            foreach (var r in context.Model.GetBuildResources())
            {
                context.GetSteps(r, WellKnownPipelineTags.BuildCompute)
                    .RequiredBy(WellKnownPipelineSteps.Deploy)
                    .DependsOn(WellKnownPipelineSteps.DeployPrereq);
            }
        }));
    }

    internal bool IsTargeted(IResource r) =>
        r.Annotations.OfType<DeploymentTargetAnnotation>().Any(a => a.ComputeEnvironment == this);

    internal PulumiStackResource ResolvedStack => _resolvedStack
        ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack().");

    private PulumiOutputReference Out(string suffix) => Output(ResolvedStack, suffix);

    /// <summary>The sandbox group name.</summary>
    public PulumiOutputReference NameOutputReference => Out("Name");
    /// <summary>The sandbox group resource id.</summary>
    public PulumiOutputReference IdOutputReference => Out("Id");
    /// <summary>The region of the sandbox group.</summary>
    public PulumiOutputReference LocationOutputReference => Out("Location");
    /// <summary>The client id of the identity Azure Dev Compute uses to pull images.</summary>
    public PulumiOutputReference ImagePullIdentityClientId => Out("ImagePullIdentityClientId");
    internal PulumiOutputReference SubscriptionIdOutput => Out("SubscriptionId");
    internal PulumiOutputReference ResourceGroupOutput => Out("ResourceGroup");
    internal PulumiOutputReference RegistryLoginServerOutput => Out("RegistryLoginServer");

    ReferenceExpression IContainerRegistry.Name => ReferenceExpression.Create($"{Out("RegistryName")}");
    ReferenceExpression IContainerRegistry.Endpoint => ReferenceExpression.Create($"{Out("RegistryLoginServer")}");
    ReferenceExpression? IContainerRegistry.Repository => null;

    /// <summary>The URL of the sandbox group's dashboard (requires the subscription id, so it is composed from outputs).</summary>
    public static string GetDashboardUrl(string subscriptionId, string resourceGroupName, string sandboxGroupName) =>
        $"https://sandboxes.azure.com/sandbox-groups/{Uri.EscapeDataString(subscriptionId)}/{Uri.EscapeDataString(resourceGroupName)}/{Uri.EscapeDataString(sandboxGroupName)}";

    public ReferenceExpression GetHostAddressExpression(EndpointReference endpointReference) =>
        GetEndpointPropertyExpression(endpointReference.Property(EndpointProperty.Host));

    public ReferenceExpression GetEndpointPropertyExpression(EndpointReferenceExpression endpointReferenceExpression)
    {
        var endpoint = endpointReferenceExpression.Endpoint;
        if (endpoint.Resource.GetDeploymentTargetAnnotation(this)?.DeploymentTarget is not AzureSandboxContainerResource target)
            throw new InvalidOperationException($"Resource '{endpoint.Resource.Name}' is not deployed to Azure sandbox group '{Name}'.");
        return ReferenceExpression.Create($"{new AzureSandboxEndpointValueProvider(this, target, endpointReferenceExpression)}");
    }

    private async Task LoginToRegistryAsync(PipelineStepContext ctx)
    {
        var registry = await Out("RegistryName").GetValueAsync(ctx.CancellationToken);
        var subscription = await Out("SubscriptionId").GetValueAsync(ctx.CancellationToken);

        var psi = new System.Diagnostics.ProcessStartInfo("az") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "acr", "login", "--name", registry!, "--subscription", subscription! }) psi.ArgumentList.Add(a);
        using var process = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the az CLI. Is it installed?");
        var output = await process.StandardOutput.ReadToEndAsync(ctx.CancellationToken) + await process.StandardError.ReadToEndAsync(ctx.CancellationToken);
        await process.WaitForExitAsync(ctx.CancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"'az acr login' failed (run 'az login' first): {output}");
        ctx.Logger.LogInformation("Logged in to registry {Registry}", registry);
    }

    private async Task AddDashboardToSummaryAsync(PipelineStepContext ctx)
    {
        var scope = await AzureSandboxDeployment.CreateScopeAsync(this, ctx.CancellationToken);
        var url = GetDashboardUrl(scope.SubscriptionId, scope.ResourceGroupName, scope.SandboxGroupName);
        ctx.Summary.Add($"{Name} sandbox dashboard", new MarkdownString($"[{url}]({url})"));
        await ctx.ReportingStep.CompleteAsync(new MarkdownString($"Sandbox dashboard available at [{url}]({url})"), CompletionState.Completed, ctx.CancellationToken);
    }

    private Task PrepareDeploymentTargetsAsync(PipelineStepContext ctx)
    {
        if (!ctx.ExecutionContext.IsPublishMode) return Task.CompletedTask;

        _resolvedStack = ResolveStack(ctx);
        RequireSameStack(Registry, _resolvedStack, "container registry");
        RequireSameStack(PullIdentity, _resolvedStack, "image-pull identity");
        foreach (var w in WorkloadIdentities) RequireSameStack(w, _resolvedStack, "workload identity");

        if (PullIdentity is not null && WorkloadIdentities.Contains(PullIdentity))
            throw new InvalidOperationException(
                $"Sandbox group '{Name}' uses identity '{PullIdentity.Name}' for both image pulls and workloads. " +
                "Use a dedicated image-pull identity so its AcrPull permission is not exposed to sandbox workloads.");
        if (IsExisting && PullIdentity is null)
            throw new InvalidOperationException(
                $"Existing Azure sandbox group '{Name}' requires a user-assigned ACR pull identity. Call 'WithAcrPullIdentity' with an identity " +
                "that is already attached to the sandbox group and has AcrPull on the configured registry.");

        var environments = ctx.Model.Resources.OfType<IComputeEnvironmentResource>().ToList();
        var onlyEnvironment = environments.Count == 1 && environments[0] == this;

        foreach (var r in ctx.Model.GetComputeResources())
        {
            var env = r.GetComputeEnvironment();
            if ((env is null && !onlyEnvironment) || (env is not null && env != this) || r.GetDeploymentTargetAnnotation(this) is not null) continue;

            if (r.Annotations.OfType<AzureSandboxIdentityAnnotation>().LastOrDefault() is { } identityAnnotation)
            {
                var identity = identityAnnotation.Identity;
                if (IsExisting)
                    throw new InvalidOperationException(
                        $"Compute resource '{r.Name}' uses managed identity '{identity.Name}', but workload identities are not supported when publishing to existing Azure sandbox group '{Name}'.");
                if (identity == PullIdentity)
                    throw new InvalidOperationException(
                        $"Azure sandbox group '{Name}' uses identity '{identity.Name}' for both image pulls and workload '{r.Name}'. " +
                        "Use a dedicated image-pull identity so its AcrPull permission is not exposed to sandbox workloads.");
                RequireSameStack(identity, _resolvedStack, "workload identity");
                AddWorkloadIdentity(identity);
            }

            AzureSandboxDeployment.ValidateSandboxCompatibility(r);

#pragma warning disable ASPIREPIPELINES003
            r.Annotations.Add(new ContainerBuildOptionsCallbackAnnotation(buildOptions =>
            {
                buildOptions.Destination = ContainerImageDestination.Registry;
                buildOptions.OutputPath = null;
                buildOptions.ImageFormat = ContainerImageFormat.Docker;
                // Sandboxes run linux/amd64 images only.
                buildOptions.TargetPlatform = ContainerTargetPlatform.LinuxAmd64;
            }));
            var stack = _resolvedStack;
            r.Annotations.Add(new ContainerImagePushOptionsCallbackAnnotation(c =>
            {
                c.Options.RemoteImageName = r.Name.ToLowerInvariant();
                c.Options.RemoteImageTag = stack.DeployTag;
            }));
#pragma warning restore ASPIREPIPELINES003

            var target = new AzureSandboxContainerResource($"{r.Name}-sandbox-container", r, this);
            target.Annotations.Add(ManifestPublishingCallbackAnnotation.Ignore);
            r.Annotations.Add(new DeploymentTargetAnnotation(target)
            {
                ContainerRegistry = this,
                ComputeEnvironment = this,
            });
        }

        return Task.CompletedTask;
    }

    internal void AddWorkloadIdentity(PulumiAzureUserAssignedIdentityResource identity)
    {
        if (!WorkloadIdentities.Contains(identity)) WorkloadIdentities.Add(identity);
    }

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        var stack = _resolvedStack ?? ResolveStack(context);
        _resolvedStack ??= stack;
        RequireSameStack(Registry, stack, "container registry");
        RequireSameStack(PullIdentity, stack, "image-pull identity");
        foreach (var w in WorkloadIdentities) RequireSameStack(w, stack, "workload identity");
        if (PullIdentity is not null && WorkloadIdentities.Contains(PullIdentity))
            throw new InvalidOperationException(
                $"Sandbox group '{Name}' uses identity '{PullIdentity.Name}' for both image pulls and workloads. " +
                "Use a dedicated image-pull identity so its AcrPull permission is not exposed to sandbox workloads.");
        if (IsExisting && PullIdentity is null)
            throw new InvalidOperationException(
                $"Existing Azure sandbox group '{Name}' requires a user-assigned ACR pull identity. Call 'WithAcrPullIdentity' with an identity " +
                "that is already attached to the sandbox group and has AcrPull on the configured registry.");

        program.AddClientConfig();

        // Registry: the group's own '{name}-acr' (Basic) unless one was supplied.
        if (OwnsRegistry)
        {
            program.AddInfraResource(RegistryId, "azure-native:containerregistry:Registry", new()
            {
                ["type"] = "azure-native:containerregistry:Registry",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["registryName"] = NameFor(AzureNameKind.ContainerRegistry, "acr"),
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["sku"] = new Dictionary<string, object?> { ["name"] = "Basic" },
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = $"{Name}-acr" },
                },
            });
        }

        // Image-pull identity: the group's own '{name}-mi' unless one was supplied.
        if (OwnsPullIdentity)
        {
            program.AddInfraResource(PullIdentityId, "azure-native:managedidentity:UserAssignedIdentity", new()
            {
                ["type"] = "azure-native:managedidentity:UserAssignedIdentity",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceName"] = NameFor(AzureNameKind.UserAssignedIdentity, "mi"),
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = $"{Name}-mi" },
                },
            });
            // For a caller-supplied identity the caller remains responsible for AcrPull (as upstream).
            program.AddRoleAssignment($"{Id}AcrPull", $"${{{PullIdentityId}.principalId}}", AzureRoleDefinitions.AcrPull,
                $"${{{RegistryId}.id}}");
        }

        if (IsExisting)
        {
            program.Variables[GroupRef] = new Dictionary<string, object?>
            {
                ["fn::invoke"] = new Dictionary<string, object?>
                {
                    ["function"] = "azure-native:resources:getResource",
                    ["arguments"] = new Dictionary<string, object?>
                    {
                        ["resourceGroupName"] = rg,
                        ["resourceProviderNamespace"] = "Microsoft.App",
                        ["resourceType"] = "sandboxGroups",
                        ["resourceName"] = ExistingGroupName,
                        ["parentResourcePath"] = "",
                        ["apiVersion"] = GroupApiVersion,
                    },
                },
            };
        }
        else
        {
            var userAssigned = new List<object?> { $"${{{PullIdentityId}.id}}" };
            userAssigned.AddRange(WorkloadIdentities.Select(w => (object?)$"${{{w.Id}.id}}"));

            program.AddInfraResource(Id, GroupResourceType, new Dictionary<string, object?>
            {
                ["type"] = GroupResourceType,
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["resourceProviderNamespace"] = "Microsoft.App",
                    ["resourceType"] = "sandboxGroups",
                    ["resourceName"] = GroupName,
                    ["parentResourcePath"] = "",
                    ["apiVersion"] = GroupApiVersion,
                    ["location"] = location,
                    ["identity"] = new Dictionary<string, object?>
                    {
                        ["type"] = SystemAssigned ? "SystemAssigned, UserAssigned" : "UserAssigned",
                        ["userAssignedIdentities"] = userAssigned,
                    },
                    ["properties"] = new Dictionary<string, object?>(),
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
                },
            });

            // Sandboxes, disk images and ports are created through the data plane after the group exists, using the deploying principal.
            program.AddRoleAssignment($"{Id}DeploymentPrincipalDataOwner", PulumiAzureProgramExtensions.CurrentPrincipalObjectId,
                SandboxGroupDataOwnerRoleId, $"${{{Id}.id}}", "User");
        }

        var groupId = IsExisting
            ? $"/subscriptions/{PulumiAzureProgramExtensions.SubscriptionId}/resourceGroups/{rg}/providers/Microsoft.App/sandboxGroups/{ExistingGroupName}"
            : $"${{{Id}.id}}";
        program.Outputs[$"{Id}Id"] = groupId;
        program.Outputs[$"{Id}Name"] = $"${{{GroupRef}.name}}";
        program.Outputs[$"{Id}Location"] = $"${{{GroupRef}.location}}";
        program.Outputs[$"{Id}ResourceGroup"] = rg;
        program.Outputs[$"{Id}SubscriptionId"] = PulumiAzureProgramExtensions.SubscriptionId;
        program.Outputs[$"{Id}ImagePullIdentityClientId"] = $"${{{PullIdentityId}.clientId}}";
        program.Outputs[$"{Id}RegistryLoginServer"] = $"${{{RegistryId}.loginServer}}";
        program.Outputs[$"{Id}RegistryName"] = $"${{{RegistryId}.name}}";

        // The registry must exist before images are pushed, so tell the pipeline to create the infrastructure first.
        if (context.Model.GetComputeResources().Any(IsTargeted)) program.HasApps = true;

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    private static void RequireSameStack(PulumiAzureResource? other, PulumiStackResource stack, string what)
    {
        if (other?.Stack is { } s && s != stack)
            throw new InvalidOperationException(
                $"The {what} '{other.Name}' is in Pulumi stack '{s.Name}', but the sandbox group is in '{stack.Name}'; they must share a stack.");
    }
}

/// <summary>A port exposed on a deployed sandbox.</summary>
internal sealed record SandboxPortInfo(string Name, int Port, string Url);
