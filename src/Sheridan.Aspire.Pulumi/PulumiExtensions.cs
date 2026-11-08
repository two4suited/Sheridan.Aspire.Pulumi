using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sheridan.Aspire.Pulumi;

public static class PulumiExtensions
{
    public static IResourceBuilder<PulumiStackResource> AddPulumiStack(
        this IDistributedApplicationBuilder builder,
        string name,
        string? stackName = null)
    {
        stackName = builder.Configuration["Pulumi:Stack"]
            ?? stackName
            ?? builder.Environment.EnvironmentName.ToLowerInvariant();
        var resource = new PulumiStackResource(name, stackName);

        var esc = builder.Configuration["Pulumi:Esc"]?
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (esc is { Length: > 0 })
        {
            resource.EscEnvironments.AddRange(esc);
            resource.EscFromConfiguration = true;
        }

        return builder.AddResource(resource)
            .ExcludeFromManifest()
            .HideInRunMode()
            .WithPipelineStepFactory(ctx => CreateSteps(resource))
            .WithPipelineConfiguration(context => PulumiReferenceOrdering.Configure(context, resource));
    }

    /// <summary>
    /// Hides a deploy-only resource (one with no local counterpart) from the dashboard and <c>aspire describe</c> when running
    /// locally. The resource stays in the model, so <c>aspire publish</c> / <c>aspire deploy</c> are unaffected.
    /// </summary>
    public static IResourceBuilder<T> HideInRunMode<T>(this IResourceBuilder<T> builder) where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.ApplicationBuilder.ExecutionContext.IsRunMode) return builder;
        return builder.WithInitialState(new CustomResourceSnapshot
        {
            ResourceType = builder.Resource.GetType().Name,
            Properties = [],
            IsHidden = true,
        });
    }

    public static IResourceBuilder<PulumiStackResource> WithOrganization(
        this IResourceBuilder<PulumiStackResource> builder, string organization)
    {
        builder.Resource.Organization = organization;
        return builder;
    }

    /// <summary>Sets the Pulumi project name, overriding the Aspire resource name. Lets several stacks (each with a distinct stack name) share one Pulumi project.</summary>
    public static IResourceBuilder<PulumiStackResource> WithProjectName(
        this IResourceBuilder<PulumiStackResource> builder, string projectName)
    {
        builder.Resource.ProjectNameOverride = projectName;
        return builder;
    }

    public static IResourceBuilder<PulumiStackResource> WithEscEnvironment(
        this IResourceBuilder<PulumiStackResource> builder, params string[] environments)
    {
        if (!builder.Resource.EscFromConfiguration)
            builder.Resource.EscEnvironments.AddRange(environments);
        return builder;
    }

    /// <summary>
    /// Deploys <paramref name="dependency"/> to completion before this stack's infrastructure is provisioned.
    /// Only orders the two stacks' provision/deploy steps. This is implied automatically whenever a resource in this
    /// stack references (WithReference/WaitFor/<see cref="PulumiOutputReference"/>) a resource in <paramref name="dependency"/>;
    /// call it explicitly for dependencies Aspire can't see. This stack's program is regenerated after its own provision
    /// pass (i.e. after <paramref name="dependency"/> has deployed), so values read from the dependency's outputs resolve
    /// on the first deploy.
    /// </summary>
    public static IResourceBuilder<PulumiStackResource> DependsOnStack(
        this IResourceBuilder<PulumiStackResource> builder, IResourceBuilder<PulumiStackResource> dependency)
    {
        var dependencyResource = dependency.Resource;
        return builder.WithPipelineConfiguration(context =>
        {
            var dependencyDeploy = context.GetSteps(dependencyResource, WellKnownPipelineTags.DeployCompute);
            var provision = context.GetSteps(builder.Resource, WellKnownPipelineTags.ProvisionInfrastructure);
            provision.DependsOn(dependencyDeploy);
        });
    }

    private static IEnumerable<PipelineStep> CreateSteps(PulumiStackResource resource)
    {
        var publish = new PipelineStep
        {
            Name = $"pulumi-publish-{resource.Name}",
            Description = $"Generate the Pulumi project for '{resource.Name}'",
            Resource = resource,
            Action = ctx => WriteProjectAsync(ctx, resource),
        };
        publish.RequiredBy(WellKnownPipelineSteps.Publish);
        publish.DependsOn(WellKnownPipelineSteps.PublishPrereq);

        var provision = new PipelineStep
        {
            Name = $"pulumi-provision-{resource.Name}",
            Description = $"Log in, ensure the ESC environment and stack exist, and create the infrastructure images are pushed to for '{resource.Name}'",
            Resource = resource,
            Tags = [WellKnownPipelineTags.ProvisionInfrastructure],
            Action = ctx => ProvisionAsync(ctx, resource),
        };
        provision.DependsOn(publish);
        provision.DependsOn(WellKnownPipelineSteps.DeployPrereq);

        var deploy = new PipelineStep
        {
            Name = $"pulumi-deploy-{resource.Name}",
            Description = $"Run 'pulumi up' for '{resource.Name}'",
            Resource = resource,
            Tags = [WellKnownPipelineTags.DeployCompute],
            Action = ctx => DeployAsync(ctx, resource),
        };
        deploy.DependsOn(provision);
        deploy.RequiredBy(WellKnownPipelineSteps.Deploy);

        var destroy = new PipelineStep
        {
            Name = $"pulumi-destroy-{resource.Name}",
            Description = $"Run 'pulumi destroy' for '{resource.Name}'",
            Resource = resource,
            Action = ctx => DestroyAsync(ctx, resource),
        };
        destroy.DependsOn(publish);
        destroy.DependsOn(WellKnownPipelineSteps.DestroyPrereq);
        destroy.RequiredBy(WellKnownPipelineSteps.Destroy);

        return [publish, provision, deploy, destroy];
    }

    private static string ProjectDirectory(PipelineStepContext ctx, PulumiStackResource resource) =>
        Path.Combine(ctx.Services.GetRequiredService<IPipelineOutputService>().GetOutputDirectory(resource), "pulumi");

    private static async Task WriteProjectAsync(PipelineStepContext ctx, PulumiStackResource resource)
    {
        var dir = ProjectDirectory(ctx, resource);
        Directory.CreateDirectory(dir);

        // Set before contributing: contributors resolve stack outputs (PulumiOutputReference) from this directory.
        resource.ProjectDirectory = dir;

        var program = new PulumiProgram();
        foreach (var c in ctx.Model.Resources.OfType<IPulumiProgramContributor>().Where(c => c.Stack is null || c.Stack == resource))
            await c.ContributeAsync(program, ctx);

        resource.HasApps = program.HasApps;
        resource.InfraTargets.Clear();
        resource.InfraTargets.AddRange(program.GetInfrastructureTargets());

        await File.WriteAllTextAsync(Path.Combine(dir, "Pulumi.yaml"),
            program.ToYaml(resource.ProjectName, $"Generated by Aspire for '{resource.Name}'"), ctx.CancellationToken);

        var imports = string.Concat(resource.StackEscReferences.Select(e => $"  - {e}\n"));
        await File.WriteAllTextAsync(Path.Combine(dir, $"Pulumi.{resource.StackName}.yaml"),
            "environment:\n" + imports, ctx.CancellationToken);

        ctx.Logger.LogInformation("Wrote Pulumi project to {Directory}", dir);
    }

    private static async Task ProvisionAsync(PipelineStepContext ctx, PulumiStackResource resource)
    {
        var dir = ProjectDirectory(ctx, resource);
        var ct = ctx.CancellationToken;
        var log = ctx.Logger;

        if (await PulumiCli.RunAsync(dir, ["whoami"], log, ct) != 0)
            throw new InvalidOperationException("Not logged in to a Pulumi backend. Run 'pulumi login' (or set PULUMI_ACCESS_TOKEN) and retry.");

        if (resource.UsesDefaultEscEnvironment
            && await PulumiCli.RunAsync(dir, ["env", "get", resource.QualifiedDefaultEscEnvironment], log, ct) != 0)
        {
            log.LogInformation("Creating ESC environment {Environment}", resource.QualifiedDefaultEscEnvironment);
            await PulumiCli.RunOrThrowAsync(dir, ["env", "init", resource.QualifiedDefaultEscEnvironment, "--file", "-"], log, ct,
                stdin: "values:\n  pulumiConfig: {}\n");
        }

        await PulumiCli.RunOrThrowAsync(dir, ["stack", "select", resource.QualifiedStack, "--create"], log, ct);

        if (!resource.HasApps)
        {
            resource.InfrastructureProvisioned = true;
            return;
        }

        // Provision pass: create all infrastructure (everything but the apps) so that images can be pushed (the registry
        // exists) and so that every stack output the apps consume exists when the deploy pass regenerates the program.
        var args = new List<string> { "up", "--yes", "--skip-preview", "--non-interactive" };
        foreach (var (type, name) in resource.InfraTargets)
            args.AddRange(["--target", $"urn:pulumi:{resource.StackName}::{resource.ProjectName}::{type}::{name}"]);
        if (resource.InfraTargets.Count > 0)
            await PulumiCli.RunOrThrowAsync(dir, args, log, ct);
        resource.InfrastructureProvisioned = true;
    }

    private static async Task DeployAsync(PipelineStepContext ctx, PulumiStackResource resource)
    {
        // The provision pass has created all the infrastructure (and any stack this one depends on has been deployed), so
        // values read from stack outputs (e.g. a new storage account's endpoint) that were unavailable when the program was
        // first generated resolve now.
        if (resource.HasApps) await WriteProjectAsync(ctx, resource);
        await PulumiCli.RunOrThrowAsync(ProjectDirectory(ctx, resource), ["up", "--yes", "--skip-preview", "--non-interactive"], ctx.Logger, ctx.CancellationToken);
    }

    private static async Task DestroyAsync(PipelineStepContext ctx, PulumiStackResource resource)
    {
        var dir = ProjectDirectory(ctx, resource);
        await PulumiCli.RunOrThrowAsync(dir, ["stack", "select", resource.QualifiedStack], ctx.Logger, ctx.CancellationToken);
        await PulumiCli.RunOrThrowAsync(dir, ["destroy", "--yes", "--non-interactive"], ctx.Logger, ctx.CancellationToken);
    }
}
