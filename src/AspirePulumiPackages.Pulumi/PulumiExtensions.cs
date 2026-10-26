using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AspirePulumiPackages.Pulumi;

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
            .WithPipelineStepFactory(ctx => CreateSteps(resource));
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
    /// Only orders the two stacks' provision/deploy steps; each stack's Pulumi project is still generated
    /// independently during the publish step, so a value read from <paramref name="dependency"/>'s outputs
    /// (e.g. via <see cref="PulumiOutputReference"/>) still needs a prior successful deploy to resolve.
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
        resource.InfraTargets.AddRange(program.InfraTargets);

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

        if (!resource.HasApps) return;

        // Images can only be pushed once the registry exists, so create the infrastructure first.
        var args = new List<string> { "up", "--yes", "--skip-preview", "--non-interactive" };
        foreach (var (type, name) in resource.InfraTargets)
            args.AddRange(["--target", $"urn:pulumi:{resource.StackName}::{resource.ProjectName}::{type}::{name}"]);
        await PulumiCli.RunOrThrowAsync(dir, args, log, ct);
    }

    private static Task DeployAsync(PipelineStepContext ctx, PulumiStackResource resource) =>
        PulumiCli.RunOrThrowAsync(ProjectDirectory(ctx, resource), ["up", "--yes", "--skip-preview", "--non-interactive"], ctx.Logger, ctx.CancellationToken);

    private static async Task DestroyAsync(PipelineStepContext ctx, PulumiStackResource resource)
    {
        var dir = ProjectDirectory(ctx, resource);
        await PulumiCli.RunOrThrowAsync(dir, ["stack", "select", resource.QualifiedStack], ctx.Logger, ctx.CancellationToken);
        await PulumiCli.RunOrThrowAsync(dir, ["destroy", "--yes", "--non-interactive"], ctx.Logger, ctx.CancellationToken);
    }
}
