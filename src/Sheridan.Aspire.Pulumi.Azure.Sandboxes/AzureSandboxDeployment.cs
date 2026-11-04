using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sheridan.Aspire.Pulumi.Azure.Sandboxes;

/// <summary>
/// The data-plane half of a sandbox group: for each compute resource that targets it, creates a disk image from the image pushed to
/// the group's registry, a sandbox from that disk image (tier, environment, identity, egress policy), its lifecycle policy and its
/// ports, and removes the previous generation. Port of <c>AzureSandboxContainerDeployment</c> in Aspire.Hosting.Azure.Sandboxes, minus
/// its local deployment-state file and cross-process lease (ownership is tracked with sandbox/disk image labels only).
/// </summary>
internal static class AzureSandboxDeployment
{
    private const int DefaultContainerPort = 8080;
    private const int DiskImageReadyTimeoutSeconds = 600;

    internal readonly record struct SandboxEndpoint(string Name, int TargetPort, bool IsExternal, string Protocol, bool? Anonymous);

    private sealed record ImageMetadata(IReadOnlyList<string> Entrypoint, IReadOnlyList<string> Command, string? WorkingDirectory);

    private sealed record Resolved(string Value, IReadOnlySet<string> EgressHosts);

    private static readonly HttpClient s_http = new();

    private static readonly IReadOnlySet<string> s_outboundHttpConnectionStringKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AccountEndpoint", "Address", "BlobEndpoint", "Data Source", "DataLakeEndpoint", "Endpoint", "EndpointAIInference", "FileEndpoint",
        "IngestionEndpoint", "LiveEndpoint", "QueueEndpoint", "ServiceUri", "TableEndpoint", "Uri", "Url",
    };

    // ---- pipeline wiring ------------------------------------------------------------------------------------------------

    internal static IEnumerable<PipelineStep> CreateDeploySteps(AzureSandboxGroupResource group, AzureSandboxContainerResource target)
    {
        yield return new PipelineStep
        {
            Name = $"sandbox-deploy-{target.TargetResource.Name}",
            Description = $"Deploys compute resource '{target.TargetResource.Name}' to the sandbox group '{group.Name}'",
            Action = ctx => DeployAsync(ctx, group, target),
            DependsOnSteps = [WellKnownPipelineSteps.DeployPrereq],
            RequiredBySteps = [WellKnownPipelineSteps.Deploy],
            Tags = [WellKnownPipelineTags.DeployCompute],
            Resource = target,
        };
    }

    /// <summary>Orders a sandbox after the sandboxes whose endpoints it references (their URLs only exist once they are deployed).</summary>
    internal static async Task ConfigureReferenceOrderingAsync(
        PipelineConfigurationContext context, AzureSandboxGroupResource group, AzureSandboxContainerResource target, List<PipelineStep> deploySteps)
    {
        var references = await FindReferencedResourcesAsync(context, target.TargetResource);
        foreach (var other in references)
        {
            if (other.GetDeploymentTargetAnnotation(group)?.DeploymentTarget is not AzureSandboxContainerResource otherTarget || otherTarget == target) continue;
            foreach (var producer in context.GetSteps(otherTarget, WellKnownPipelineTags.DeployCompute))
            foreach (var consumer in deploySteps)
            {
                if (WouldCreateCycle(context.Steps, consumer, producer))
                    throw new InvalidOperationException(
                        $"Azure sandbox resources '{target.TargetResource.Name}' and '{other.Name}' have a circular deployment dependency.");
                consumer.DependsOn(producer);
            }
        }
    }

    private static async Task<IReadOnlyCollection<IResource>> FindReferencedResourcesAsync(PipelineConfigurationContext context, IResource resource)
    {
        var found = new HashSet<IResource>();
        try
        {
            var executionContext = context.Services.GetRequiredService<DistributedApplicationExecutionContext>();
            var envVars = new Dictionary<string, object>();
            var envContext = new EnvironmentCallbackContext(executionContext, resource, envVars, CancellationToken.None);
            foreach (var a in resource.Annotations.OfType<EnvironmentCallbackAnnotation>()) await a.Callback(envContext);
            foreach (var value in envVars.Values) CollectReferencedResources(value, found);
        }
        catch (Exception)
        {
            // Callbacks that need runtime values can't be evaluated this early; their references (if any) resolve at deploy time or fail there.
        }
        return found;
    }

    private static void CollectReferencedResources(object? value, HashSet<IResource> found)
    {
        switch (value)
        {
            case EndpointReference e: found.Add(e.Resource); break;
            case EndpointReferenceExpression ex: found.Add(ex.Endpoint.Resource); break;
            case ConnectionStringReference c: found.Add(c.Resource); break;
            case ReferenceExpression re:
                foreach (var p in re.ValueProviders) CollectReferencedResources(p, found);
                break;
            case IResource r: found.Add(r); break;
        }
    }

    private static bool WouldCreateCycle(IReadOnlyList<PipelineStep> steps, PipelineStep consumer, PipelineStep producer)
    {
        if (consumer.Name == producer.Name) return true;
        var byName = steps.ToDictionary(s => s.Name, StringComparer.Ordinal);
        var stack = new Stack<string>(producer.DependsOnSteps);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (stack.TryPop(out var name))
        {
            if (!seen.Add(name)) continue;
            if (name == consumer.Name) return true;
            if (byName.TryGetValue(name, out var s)) foreach (var d in s.DependsOnSteps) stack.Push(d);
        }
        return false;
    }

    internal static void ValidateSandboxCompatibility(IResource resource)
    {
        if (resource.TryGetContainerMounts(out var mounts) && mounts.Any())
            throw new NotSupportedException($"Resource '{resource.Name}' configures container mounts, but Azure sandbox volume provisioning is not supported by the sandbox preview.");
    }

    // ---- scope & client -------------------------------------------------------------------------------------------------

    internal static async Task<AzureDevComputeResourceScope> CreateScopeAsync(AzureSandboxGroupResource group, CancellationToken ct)
    {
        string Required(string? v, string what) =>
            string.IsNullOrWhiteSpace(v) ? throw new InvalidOperationException($"Sandbox group '{group.Name}' is missing the '{what}' stack output. Ensure the Pulumi stack deployed successfully.") : v;

        var subscription = Required(await group.SubscriptionIdOutput.GetValueAsync(ct), "SubscriptionId");
        var resourceGroup = Required(await group.ResourceGroupOutput.GetValueAsync(ct), "ResourceGroup");
        var name = Required(await group.NameOutputReference.GetValueAsync(ct), "Name");
        var location = Required(await group.LocationOutputReference.GetValueAsync(ct), "Location");
        // ARM may report the display name ("East US"); the data plane host uses the short name ("eastus").
        return new AzureDevComputeResourceScope(subscription, resourceGroup, name, location.Replace(" ", "").ToLowerInvariant());
    }

    private static AzureDevComputeClient CreateClient(PipelineStepContext ctx) =>
        new(s_http, new DefaultAzureCredential(), ctx.Logger);

    // ---- deploy ---------------------------------------------------------------------------------------------------------

    private static async Task DeployAsync(PipelineStepContext ctx, AzureSandboxGroupResource group, AzureSandboxContainerResource target)
    {
        var resource = target.TargetResource;
        var endpoints = ResolveSandboxEndpoints(target);
        var scope = await CreateScopeAsync(group, ctx.CancellationToken);
        var client = CreateClient(ctx);
        var ownerId = CreateOwnerId(group, scope);
        var deployId = Guid.NewGuid().ToString("N");
        string diskImageId = "", sandboxId = "";
        var addedPorts = new List<int>();
        var committed = false;

        try
        {
            var imageReference = await ResolveContainerImageAsync(ctx, target);
            var runtime = await ctx.Services.GetRequiredService<IContainerRuntimeResolver>().ResolveAsync(ctx.CancellationToken);
            var metadata = await ResolveImageMetadataAsync(ctx, runtime, resource, imageReference);
            var diskImageReference = await ResolveDigestReferenceAsync(runtime, imageReference, ctx.CancellationToken);

            // Disk image
            await using (var diskTask = await ctx.ReportingStep.CreateTaskAsync($"Creating sandbox disk image for {resource.Name}", ctx.CancellationToken))
            {
                var pullClientId = await ResolvePullIdentityClientIdAsync(group, imageReference, ctx.CancellationToken);
                var diskImage = await client.CreateDiskImageAsync(scope, new AzureDevComputeCreateDiskImageRequest
                {
                    Name = CreateResourceName(resource.Name, deployId),
                    Labels = CreateLabels(target, group, ownerId, deployId),
                    Source = new AzureDevComputeDiskImageSource { ImageUrl = diskImageReference, ManagedIdentityClientId = pullClientId },
                }, ctx.CancellationToken);
                diskImageId = diskImage.Id;
                await WaitForDiskImageReadyAsync(ctx, client, scope, diskImage);
                await diskTask.CompleteAsync($"Created sandbox disk image {diskImageId}", CompletionState.Completed, ctx.CancellationToken);
            }

            // Sandbox
            var env = new Dictionary<string, string>(StringComparer.Ordinal);
            var resolvedEnv = await ResolveEnvironmentVariablesAsync(ctx, group, resource);
            foreach (var (k, v) in resolvedEnv.Values) env[k] = v;
            var identityAnnotation = resource.Annotations.OfType<AzureSandboxIdentityAnnotation>().LastOrDefault();
            List<AzureDevComputeIdentitySetting>? identitySettings = null;
            if (identityAnnotation is not null)
            {
                var identity = identityAnnotation.Identity;
                var identityId = await identity.IdentityId.GetValueAsync(ctx.CancellationToken)
                    ?? throw new InvalidOperationException($"Identity '{identity.Name}' has no 'Id' stack output.");
                identitySettings = [new AzureDevComputeIdentitySetting { Identity = identityId, Lifecycle = "All" }];
                if (!env.ContainsKey("AZURE_CLIENT_ID"))
                    env["AZURE_CLIENT_ID"] = await identity.ClientId.GetValueAsync(ctx.CancellationToken)
                        ?? throw new InvalidOperationException($"Identity '{identity.Name}' has no 'ClientId' stack output.");
            }

            var egress = CreateEgressPolicy(resolvedEnv.EgressHosts.Concat(metadata.EgressHosts));
            await using (var createTask = await ctx.ReportingStep.CreateTaskAsync($"Creating sandbox for {resource.Name}", ctx.CancellationToken))
            {
                var sandbox = await client.CreateSandboxAsync(scope, new AzureDevComputeSandboxRequest
                {
                    Labels = CreateLabels(target, group, ownerId, deployId),
                    Environment = env.Count > 0 ? env : null,
                    IdentitySettings = identitySettings,
                    SkipEgressProxy = false,
                    EgressPolicy = egress,
                    Entrypoint = metadata.Metadata.Entrypoint.Count > 0 ? metadata.Metadata.Entrypoint.ToList() : null,
                    Cmd = metadata.Metadata.Command.Count > 0 ? metadata.Metadata.Command.ToList() : null,
                    WorkingDirectory = metadata.Metadata.WorkingDirectory,
                    SourcesRef = new AzureDevComputeSandboxSource { DiskImage = new AzureDevComputeSandboxDiskImageSource { Id = diskImageId, IsPublic = false } },
                    Resources = CreateSandboxResources(resource),
                }, ctx.CancellationToken);
                sandboxId = sandbox.Id;
                await createTask.CompleteAsync($"Created sandbox {sandboxId}", CompletionState.Completed, ctx.CancellationToken);
            }

            if (CreateLifecyclePolicy(resource) is { } lifecycle)
            {
                await using var lifecycleTask = await ctx.ReportingStep.CreateTaskAsync($"Configuring lifecycle for {resource.Name}", ctx.CancellationToken);
                await client.SetLifecycleAsync(scope, sandboxId, lifecycle, ctx.CancellationToken);
                await lifecycleTask.CompleteAsync("Lifecycle policy configured", CompletionState.Completed, ctx.CancellationToken);
            }

            var ports = new List<SandboxPortInfo>();
            foreach (var endpoint in endpoints)
            {
                await using var portTask = await ctx.ReportingStep.CreateTaskAsync($"Exposing sandbox port {endpoint.TargetPort}", ctx.CancellationToken);
                var added = await client.AddPortAsync(scope, sandboxId, new AzureDevComputeAddPortRequest
                {
                    Name = endpoint.Name,
                    Port = endpoint.TargetPort,
                    Auth = endpoint.IsExternal ? CreatePortAuthConfig(endpoint.Anonymous == true) : null,
                    Protocol = endpoint.Protocol,
                }, ctx.CancellationToken);
                addedPorts.Add(endpoint.TargetPort);
                var port = added.FirstOrDefault(p => p.Port == endpoint.TargetPort)
                    ?? throw new InvalidOperationException($"The port add response did not contain port '{endpoint.TargetPort}' for sandbox '{sandboxId}'.");
                var url = port.Url?.ToString() ?? throw new InvalidOperationException($"Port '{endpoint.TargetPort}' of sandbox '{sandboxId}' has no public URL.");
                ports.Add(new SandboxPortInfo(endpoint.Name, endpoint.TargetPort, url));
                await portTask.CompleteAsync(new MarkdownString($"Public URL: [{url}]({url})"), CompletionState.Completed, ctx.CancellationToken);
            }

            committed = true;
            group.DeployedPorts[resource.Name] = ports;

            // Remove the previous generation (and anything left behind by failed deploys) only once the new one is up.
            try
            {
                await DeleteLabeledAsync(ctx, client, scope, $"aspire-owner={ownerId},aspire-resource={target.Name}", deployId, throwOnError: true);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                ctx.Logger.LogWarning(e, "Best-effort pruning of previous sandbox generations of '{Resource}' failed; the new deployment remains active.", resource.Name);
            }

            ctx.Summary.Add(resource.Name, new MarkdownString(ports.Count > 0 ? $"[{ports[0].Url}]({ports[0].Url})" : $"Sandbox `{sandboxId}`"));
        }
        catch
        {
            if (!committed)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                try
                {
                    if (sandboxId.Length > 0) await DeleteSandboxAsync(ctx, client, scope, sandboxId, addedPorts, throwOnError: false, cleanup.Token);
                    if (diskImageId.Length > 0) await client.DeleteDiskImageAsync(scope, diskImageId, cleanup.Token);
                    // A create whose response was lost may still have produced a resource; sweep this deploy's labels.
                    await DeleteLabeledAsync(ctx, client, scope, $"aspire-owner={ownerId},aspire-resource={target.Name},aspire-deploy={deployId}", keepDeployId: null, throwOnError: false, cleanup.Token);
                }
                catch (Exception e)
                {
                    ctx.Logger.LogWarning(e, "Failed to reconcile Azure sandbox resources after deployment '{DeployId}' failed.", deployId);
                }
            }
            throw;
        }
    }

    // ---- destroy --------------------------------------------------------------------------------------------------------

    internal static async Task DestroyAsync(AzureSandboxGroupResource group, PipelineStepContext ctx)
    {
        AzureDevComputeResourceScope scope;
        try
        {
            scope = await CreateScopeAsync(group, ctx.CancellationToken);
        }
        catch (Exception e) when (e is InvalidOperationException)
        {
            ctx.Logger.LogWarning(e, "The sandbox group's stack outputs are unavailable, so its sandboxes could not be cleaned up before the stack is destroyed.");
            await ctx.ReportingStep.CompleteAsync("No sandbox group was found to clean up.", CompletionState.CompletedWithWarning, ctx.CancellationToken);
            return;
        }

        var ownerId = CreateOwnerId(group, scope);
        await DeleteLabeledAsync(ctx, CreateClient(ctx), scope, $"aspire-owner={ownerId}", keepDeployId: null, throwOnError: true, ctx.CancellationToken);
        await ctx.ReportingStep.CompleteAsync($"Deleted the sandboxes and disk images deployed to '{group.Name}'.", CompletionState.Completed, ctx.CancellationToken);
    }

    private static Task DeleteLabeledAsync(PipelineStepContext ctx, AzureDevComputeClient client, AzureDevComputeResourceScope scope, string selector,
        string? keepDeployId, bool throwOnError, CancellationToken ct = default) =>
        DeleteLabeledCoreAsync(ctx, client, scope, selector, keepDeployId, throwOnError, ct == default ? ctx.CancellationToken : ct);

    private static async Task DeleteLabeledCoreAsync(PipelineStepContext ctx, AzureDevComputeClient client, AzureDevComputeResourceScope scope, string selector,
        string? keepDeployId, bool throwOnError, CancellationToken ct)
    {
        var failures = new List<Exception>();
        bool Stale(Dictionary<string, string>? labels) => keepDeployId is null || labels is null || !labels.TryGetValue("aspire-deploy", out var d) || d != keepDeployId;

        foreach (var sandbox in (await client.ListSandboxesAsync(scope, selector, ct)).Where(s => Stale(s.Labels)))
        {
            try { await DeleteSandboxAsync(ctx, client, scope, sandbox.Id, sandbox.Ports.Select(p => p.Port), throwOnError: true, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { failures.Add(e); }
        }
        foreach (var image in (await client.ListDiskImagesAsync(scope, selector, ct)).Where(i => Stale(i.Labels)))
        {
            try { await client.DeleteDiskImageAsync(scope, image.Id, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { failures.Add(e); }
        }

        if (failures.Count == 0) return;
        if (throwOnError) throw failures.Count == 1 ? failures[0] : new AggregateException("Failed to delete sandbox resources.", failures);
        foreach (var f in failures) ctx.Logger.LogWarning(f, "Failed to delete a sandbox resource.");
    }

    private static async Task DeleteSandboxAsync(PipelineStepContext ctx, AzureDevComputeClient client, AzureDevComputeResourceScope scope, string sandboxId,
        IEnumerable<int> ports, bool throwOnError, CancellationToken ct)
    {
        foreach (var port in ports.Distinct())
        {
            try { await client.RemovePortAsync(scope, sandboxId, new AzureDevComputeRemovePortRequest { Port = port }, ct); }
            catch (Exception e) when (!throwOnError && e is not OperationCanceledException)
            {
                ctx.Logger.LogWarning(e, "Failed to remove sandbox port {Port} from sandbox '{SandboxId}'.", port, sandboxId);
            }
        }
        try { await client.DeleteSandboxAsync(scope, sandboxId, ct); }
        catch (Exception e) when (!throwOnError && e is not OperationCanceledException)
        {
            ctx.Logger.LogWarning(e, "Failed to delete sandbox '{SandboxId}'.", sandboxId);
        }
    }

    // ---- images ---------------------------------------------------------------------------------------------------------

    private static async Task<string> ResolveContainerImageAsync(PipelineStepContext ctx, AzureSandboxContainerResource target)
    {
        var resource = target.TargetResource;
        if (resource.RequiresImageBuildAndPush())
        {
            IValueProvider reference = new ContainerImageReference(resource);
            return await reference.GetValueAsync(new ValueProviderContext { ExecutionContext = ctx.ExecutionContext, Caller = resource }, ctx.CancellationToken)
                ?? throw new InvalidOperationException($"Could not resolve the pushed container image for resource '{resource.Name}'.");
        }
        if (resource.TryGetContainerImageName(out var image)) return image;
        throw new NotSupportedException(
            $"Resource '{resource.Name}' cannot be deployed to Azure sandbox group '{target.Parent.Name}' because it does not produce or reference a container image.");
    }

    /// <summary>Pins the image to its immutable linux/amd64 digest so the disk image is built from exactly what was pushed.</summary>
    private static async Task<string> ResolveDigestReferenceAsync(IContainerRuntime runtime, string imageReference, CancellationToken ct)
    {
        var result = await runtime.InspectImageManifestAsync(imageReference, ct);
        if (result.Status == ContainerImageInspectionStatus.Unsupported)
            throw new NotSupportedException($"Container runtime '{runtime.Name}' does not support image manifest inspection, which is required for Azure sandbox deployment.");
        if (result.Status == ContainerImageInspectionStatus.Failed)
            throw new InvalidOperationException(result.ErrorMessage ?? $"Container runtime failed to inspect image manifest '{imageReference}'.");
        if (!result.TryGetManifest("linux", "amd64", out var manifest))
            throw new InvalidOperationException($"Container image '{imageReference}' does not contain a linux/amd64 manifest with an immutable digest.");

        var at = imageReference.IndexOf('@');
        if (at >= 0) return imageReference[..at] + "@" + manifest.Digest;
        var slash = imageReference.LastIndexOf('/');
        var colon = imageReference.LastIndexOf(':');
        return (colon > slash ? imageReference[..colon] : imageReference) + "@" + manifest.Digest;
    }

    private sealed record ResolvedImageMetadata(ImageMetadata Metadata, IReadOnlySet<string> EgressHosts);

    private static async Task<ResolvedImageMetadata> ResolveImageMetadataAsync(PipelineStepContext ctx, IContainerRuntime runtime, IResource resource, string imageReference)
    {
        // Command-line arguments modeled on the resource (WithArgs, WithEntrypoint) override the image's own.
        var args = new List<object>();
        foreach (var a in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
            await a.Callback(new CommandLineArgsCallbackContext(args, resource, ctx.CancellationToken) { ExecutionContext = ctx.ExecutionContext, Logger = ctx.Logger });

        var resolvedArgs = new List<string>();
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            var r = await ResolveValueAsync(ctx, null, resource, arg);
            resolvedArgs.Add(r.Value);
            hosts.UnionWith(r.EgressHosts);
        }
        IReadOnlyList<string>? entrypoint = resource is ContainerResource { Entrypoint: { Length: > 0 } ep } ? [ep] : null;
        IReadOnlyList<string>? command = resolvedArgs.Count == 0 ? null : resolvedArgs;

        // A container built from a Dockerfile declares its own entrypoint/command/working directory in the image config.
        IReadOnlyList<string> imageEntrypoint = [], imageCommand = [];
        string? workingDirectory = null;
        if (resource is ContainerResource && resource.RequiresImageBuildAndPush())
        {
            var inspected = await runtime.InspectImageConfigAsync(imageReference, ctx.CancellationToken);
            if (inspected.Status == ContainerImageInspectionStatus.Unsupported)
                throw new NotSupportedException($"Container runtime '{runtime.Name}' does not support image configuration inspection, which is required for Azure sandbox deployment.");
            if (inspected.Status == ContainerImageInspectionStatus.Failed)
                throw new InvalidOperationException(inspected.ErrorMessage ?? $"Container runtime failed to inspect image configuration '{imageReference}'.");
            if (!inspected.TryGetConfig(out var config))
                throw new InvalidOperationException($"Container runtime did not return image configuration for '{imageReference}'.");
            imageEntrypoint = config.Entrypoint;
            imageCommand = config.Command;
            workingDirectory = config.WorkingDirectory;
        }

        return new ResolvedImageMetadata(new ImageMetadata(entrypoint ?? imageEntrypoint, command ?? imageCommand, workingDirectory), hosts);
    }

    private static async Task<string?> ResolvePullIdentityClientIdAsync(AzureSandboxGroupResource group, string imageReference, CancellationToken ct)
    {
        // Only images from the group's own registry are pulled with the group's image-pull identity.
        var endpoint = await group.RegistryLoginServerOutput.GetValueAsync(ct);
        if (string.IsNullOrWhiteSpace(endpoint) || !imageReference.StartsWith(endpoint + "/", StringComparison.OrdinalIgnoreCase)) return null;
        return await group.ImagePullIdentityClientId.GetValueAsync(ct);
    }

    private static async Task WaitForDiskImageReadyAsync(PipelineStepContext ctx, AzureDevComputeClient client, AzureDevComputeResourceScope scope, AzureDevComputeDiskImage diskImage)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(DiskImageReadyTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = diskImage.Status?.State ?? "";
            if (state.Equals("Ready", StringComparison.OrdinalIgnoreCase)) return;
            if (state.Contains("fail", StringComparison.OrdinalIgnoreCase) || state.Contains("error", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Sandbox disk image '{diskImage.Id}' failed to become ready (terminal state: '{state}'). Service-provided error details were redacted.");
            await Task.Delay(TimeSpan.FromSeconds(2), ctx.CancellationToken);
            diskImage = await client.GetDiskImageAsync(scope, diskImage.Id, ctx.CancellationToken);
        }
        throw new TimeoutException($"Sandbox disk image '{diskImage.Id}' was not ready after {DiskImageReadyTimeoutSeconds} seconds (last state: '{diskImage.Status?.State}').");
    }

    // ---- sandbox shape --------------------------------------------------------------------------------------------------

    private static AzureSandboxOptions? GetOptions(IResource resource) =>
        resource.Annotations.OfType<AzureSandboxContainerOptionsAnnotation>().LastOrDefault()?.Options;

    internal static AzureDevComputeSandboxResources CreateSandboxResources(IResource resource) =>
        (GetOptions(resource)?.Tier ?? AzureSandboxTier.Medium) switch
        {
            AzureSandboxTier.ExtraSmall => new() { Cpu = "250m", Memory = "512Mi", Disk = "5120Mi" },
            AzureSandboxTier.Small => new() { Cpu = "500m", Memory = "1024Mi", Disk = "10240Mi" },
            AzureSandboxTier.Medium => new() { Cpu = "1000m", Memory = "2048Mi", Disk = "20480Mi" },
            AzureSandboxTier.Large => new() { Cpu = "2000m", Memory = "4096Mi", Disk = "40960Mi" },
            AzureSandboxTier.ExtraLarge => new() { Cpu = "4000m", Memory = "8192Mi", Disk = "81920Mi" },
            var tier => throw new InvalidOperationException($"Unsupported Azure sandbox tier '{tier}'."),
        };

    internal static AzureDevComputeSandboxLifecyclePolicy? CreateLifecyclePolicy(IResource resource)
    {
        var options = GetOptions(resource);
        var suspend = options?.AutoSuspendEnabled.HasValue == true;
        var delete = options?.AutoDeleteEnabled.HasValue == true;
        if (!suspend && !delete) return null;
        return new AzureDevComputeSandboxLifecyclePolicy
        {
            AutoSuspendPolicy = suspend
                ? new AzureDevComputeSandboxAutoSuspendPolicy
                {
                    Enabled = options!.AutoSuspendEnabled!.Value,
                    Interval = options.AutoSuspendInterval is { } i ? checked((int)ToSeconds(i, "AutoSuspendInterval")) : null,
                    Mode = options.AutoSuspendMode?.ToString(),
                }
                : null,
            AutoDeletePolicy = delete
                ? new AzureDevComputeSandboxAutoDeletePolicy
                {
                    Enabled = options!.AutoDeleteEnabled!.Value,
                    DeleteIntervalInSeconds = options.AutoDeleteInterval is { } d ? ToSeconds(d, "AutoDeleteInterval") : null,
                    Trigger = options.AutoDeleteTrigger?.ToString(),
                }
                : null,
        };
    }

    private static long ToSeconds(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero || value.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new InvalidOperationException($"{name} must be a non-negative whole-second duration.");
        return value.Ticks / TimeSpan.TicksPerSecond;
    }

    internal static AzureDevComputePortAuthConfig CreatePortAuthConfig(bool anonymous) =>
        anonymous
            ? new AzureDevComputePortAuthConfig { Anonymous = true }
            : new AzureDevComputePortAuthConfig { EntraId = new AzureDevComputePortEntraIdAuthConfig { Enabled = true, ObjectIds = [], TenantIds = [] } };

    /// <summary>Egress is denied by default; only the hosts the sandbox's configuration refers to are allowed.</summary>
    internal static AzureDevComputeSandboxEgressPolicy CreateEgressPolicy(IEnumerable<string> allowedHosts) => new()
    {
        DefaultAction = "Deny",
        TrafficInspection = "Full",
        HostRules = allowedHosts
            .Where(h => Uri.CheckHostName(h) != UriHostNameType.Unknown && IsOutboundHost(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(h => new AzureDevComputeSandboxEgressHostRule { Action = "Allow", Pattern = h })
            .ToList(),
    };

    // ---- endpoints ------------------------------------------------------------------------------------------------------

    internal static IReadOnlyList<SandboxEndpoint> ResolveSandboxEndpoints(AzureSandboxContainerResource target)
    {
        var resource = target.TargetResource;
        var overrides = GetOptions(resource)?.Endpoints?.ToDictionary(e => e.Name!, StringComparer.OrdinalIgnoreCase);
        var unmatched = overrides is null ? null : new HashSet<string>(overrides.Keys, StringComparer.OrdinalIgnoreCase);
        var resolved = resource.ResolveEndpoints();
        var byPort = new Dictionary<int, SandboxEndpoint>();

        foreach (var re in resolved)
        {
            if (!re.Endpoint.IsExternal) continue;

            // Sandbox ingress terminates TLS and forwards plaintext HTTP, so an https-only .NET endpoint can't be served.
            if (resource is IDotnetProgramResource && string.Equals(re.Endpoint.UriScheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !resolved.Any(c => string.Equals(c.Endpoint.UriScheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && c.TargetPort.Value == re.TargetPort.Value))
            {
                throw new NotSupportedException(
                    $"Endpoint '{re.Endpoint.Name}' on project resource '{resource.Name}' is exposed through Azure sandbox ingress, which terminates TLS and forwards plaintext HTTP. " +
                    "Add an HTTP endpoint that shares this endpoint's target port.");
            }

            var port = ResolveSandboxTargetPort(resource, re)
                ?? throw new InvalidOperationException($"Endpoint '{re.Endpoint.Name}' on resource '{resource.Name}' does not have a target port. Configure a target port before deploying it to an Azure sandbox.");
            var protocol = re.Endpoint.Transport switch
            {
                "http" => "Http",
                "http2" => "Http2",
                _ => throw new NotSupportedException(
                    $"Endpoint '{re.Endpoint.Name}' on resource '{resource.Name}' uses transport '{re.Endpoint.Transport}'. Azure sandbox ports currently support only HTTP and HTTP/2 endpoints."),
            };

            AzureSandboxEndpointOptions? option = null;
            overrides?.TryGetValue(re.Endpoint.Name, out option);
            unmatched?.Remove(re.Endpoint.Name);
            var candidate = new SandboxEndpoint(re.Endpoint.Name, port, true, protocol, option?.Anonymous);

            if (byPort.TryGetValue(port, out var existing))
            {
                if (existing.Protocol != candidate.Protocol)
                    throw new NotSupportedException($"Endpoint '{re.Endpoint.Name}' on resource '{resource.Name}' shares target port {port} with endpoint '{existing.Name}' but uses a different transport. Azure sandbox ports support a single HTTP protocol per target port.");
                if (existing.Anonymous.HasValue && candidate.Anonymous.HasValue && existing.Anonymous != candidate.Anonymous)
                    throw new NotSupportedException($"Endpoint '{re.Endpoint.Name}' on resource '{resource.Name}' shares target port {port} with endpoint '{existing.Name}' but configures a different anonymous-access policy. Azure sandbox ports support a single access policy per target port.");
                byPort[port] = existing with { Anonymous = existing.Anonymous ?? candidate.Anonymous };
            }
            else byPort.Add(port, candidate);
        }

        if (unmatched is { Count: > 0 })
            throw new InvalidOperationException($"Resource '{resource.Name}' has Azure sandbox endpoint options for endpoint(s) that are not exposed by EndpointAnnotation: {string.Join(", ", unmatched)}.");

        // Ports are authenticated with Entra ID unless an endpoint explicitly opts in to anonymous access.
        return byPort.Values.OrderBy(e => e.TargetPort).Select(e => e with { Anonymous = e.Anonymous == true }).ToList();
    }

    internal static int? ResolveEndpointTargetPort(AzureSandboxContainerResource target, string endpointName)
    {
        foreach (var re in target.TargetResource.ResolveEndpoints())
            if (re.Endpoint.Name == endpointName) return ResolveSandboxTargetPort(target.TargetResource, re);
        return null;
    }

    private static int? ResolveSandboxTargetPort(IResource resource, ResolvedEndpoint endpoint)
    {
        if (endpoint.TargetPort.Value is { } port) return port;
        // .NET projects listen on 8080 inside the container image when no target port was configured.
        return resource is IDotnetProgramResource && endpoint.Endpoint.Transport is "http" or "http2" ? DefaultContainerPort : null;
    }

    // ---- environment ----------------------------------------------------------------------------------------------------

    private sealed record ResolvedEnvironment(IReadOnlyDictionary<string, string> Values, IReadOnlySet<string> EgressHosts);

    private static async Task<ResolvedEnvironment> ResolveEnvironmentVariablesAsync(PipelineStepContext ctx, AzureSandboxGroupResource group, IResource resource)
    {
        var raw = new Dictionary<string, object>();
        var callbackContext = new EnvironmentCallbackContext(ctx.ExecutionContext, resource, raw, ctx.CancellationToken) { Logger = ctx.Logger };
        foreach (var a in resource.Annotations.OfType<EnvironmentCallbackAnnotation>()) await a.Callback(callbackContext);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw)
        {
            var r = await ResolveValueAsync(ctx, group, resource, value);
            values[key] = r.Value;
            hosts.UnionWith(r.EgressHosts);
        }
        return new ResolvedEnvironment(values, hosts);
    }

    private static async Task<Resolved> ResolveValueAsync(PipelineStepContext ctx, AzureSandboxGroupResource? group, IResource resource, object? value)
    {
        var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var context = new ValueProviderContext { ExecutionContext = ctx.ExecutionContext, Caller = resource };

        switch (value)
        {
            case null:
                return new Resolved("", none);
            case string s:
                return new Resolved(s, none);
            case ConnectionStringReference csr:
            {
                var text = await ((IValueProvider)csr).GetValueAsync(context, ctx.CancellationToken) ?? "";
                return new Resolved(text, new HashSet<string>(GetOutboundHttpHosts(text), StringComparer.OrdinalIgnoreCase));
            }
            case IResourceWithConnectionString withConnectionString:
                return await ResolveValueAsync(ctx, group, resource, withConnectionString.ConnectionStringExpression);
            case EndpointReference endpoint:
                return await ResolveEndpointAsync(ctx, group, resource, endpoint.Property(EndpointProperty.Url), context);
            case EndpointReferenceExpression endpointExpression:
                return await ResolveEndpointAsync(ctx, group, resource, endpointExpression, context);
            case ReferenceExpression expression:
                return await ResolveReferenceExpressionAsync(ctx, group, resource, expression);
            case IValueProvider provider:
                return new Resolved(await provider.GetValueAsync(context, ctx.CancellationToken) ?? "", none);
            default:
                return new Resolved(value.ToString() ?? "", none);
        }
    }

    private static async Task<Resolved> ResolveEndpointAsync(PipelineStepContext ctx, AzureSandboxGroupResource? group, IResource resource,
        EndpointReferenceExpression expression, ValueProviderContext context)
    {
        // Endpoints of other sandboxes in the same group resolve to their deployed URLs.
        IValueProvider provider = group is not null && expression.Endpoint.Resource.GetDeploymentTargetAnnotation(group)?.DeploymentTarget is AzureSandboxContainerResource other
            ? new AzureSandboxEndpointValueProvider(group, other, expression)
            : expression;
        var text = await provider.GetValueAsync(context, ctx.CancellationToken) ?? "";
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        switch (expression.Property)
        {
            case EndpointProperty.Url:
                hosts.UnionWith(GetOutboundHttpHosts(text));
                break;
            case EndpointProperty.Host or EndpointProperty.IPV4Host:
                if (Uri.CheckHostName(text) != UriHostNameType.Unknown && IsOutboundHost(text)) hosts.Add(text);
                break;
            case EndpointProperty.HostAndPort:
                if (Uri.TryCreate("http://" + text, UriKind.Absolute, out var uri) && IsOutboundHost(uri.IdnHost)) hosts.Add(uri.IdnHost);
                break;
        }
        return new Resolved(text, hosts);
    }

    private static async Task<Resolved> ResolveReferenceExpressionAsync(PipelineStepContext ctx, AzureSandboxGroupResource? group, IResource resource, ReferenceExpression expression)
    {
        if (expression.IsConditional)
        {
            var condition = await ResolveValueAsync(ctx, group, resource, expression.Condition);
            var branch = string.Equals(condition.Value, expression.MatchValue, StringComparison.OrdinalIgnoreCase) ? expression.WhenTrue : expression.WhenFalse;
            return branch is null ? new Resolved("", new HashSet<string>()) : await ResolveReferenceExpressionAsync(ctx, group, resource, branch);
        }

        var arguments = new object?[expression.ValueProviders.Count];
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < arguments.Length; i++)
        {
            var r = await ResolveValueAsync(ctx, group, resource, expression.ValueProviders[i]);
            hosts.UnionWith(r.EgressHosts);
            arguments[i] = expression.StringFormats[i] is { } format
                ? format.Equals("uri", StringComparison.OrdinalIgnoreCase)
                    ? Uri.EscapeDataString(r.Value)
                    : throw new NotSupportedException($"The format '{format}' is not supported. Supported formats are 'uri' (encodes a URI).")
                : r.Value;
        }
        return new Resolved(string.Format(CultureInfo.InvariantCulture, expression.Format, arguments), hosts);
    }

    private static IEnumerable<string> GetOutboundHttpHosts(string value)
    {
        if (TryGetOutboundHttpHost(value, out var host)) return [host];
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = value };
            return builder.Keys.Cast<string>()
                .Where(s_outboundHttpConnectionStringKeys.Contains)
                .Select(k => Convert.ToString(builder[k], CultureInfo.InvariantCulture))
                .Select(candidate => TryGetOutboundHttpHost(candidate, out var h) ? h : null)
                .OfType<string>()
                .ToList();
        }
        catch (ArgumentException)
        {
            return [];
        }
    }

    private static bool TryGetOutboundHttpHost(string? value, out string host)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && IsOutboundHost(uri.IdnHost))
        {
            host = uri.IdnHost;
            return true;
        }
        host = "";
        return false;
    }

    private static bool IsOutboundHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host is "*" or "+" or "::" or "[::]") return false;
        return !IPAddress.TryParse(host, out var address) || !(address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any));
    }

    // ---- naming & labels ------------------------------------------------------------------------------------------------

    /// <summary>Stable identity of "this stack's deployments into this sandbox group", used to find (and only touch) what Aspire created.</summary>
    private static string CreateOwnerId(AzureSandboxGroupResource group, AzureDevComputeResourceScope scope)
    {
        var text = string.Join("\n", group.ResolvedStack.QualifiedStack.ToLowerInvariant(), scope.SubscriptionId.ToLowerInvariant(),
            scope.ResourceGroupName.ToLowerInvariant(), scope.SandboxGroupName.ToLowerInvariant(), group.Name.ToLowerInvariant());
        return "aspire-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 8));
    }

    private static Dictionary<string, string> CreateLabels(AzureSandboxContainerResource target, AzureSandboxGroupResource group, string ownerId, string deployId) => new(StringComparer.Ordinal)
    {
        ["aspire-owner"] = ownerId,
        ["aspire-resource"] = target.Name,
        ["aspire-source"] = target.TargetResource.Name,
        ["aspire-deploy"] = deployId,
    };

    private static string CreateResourceName(string resourceName, string deployId)
    {
        var text = new string(resourceName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (text.Length == 0) text = "app";
        if (text.Length > 32) text = text[..32].Trim('-');
        return text + "-" + deployId[..8];
    }
}
