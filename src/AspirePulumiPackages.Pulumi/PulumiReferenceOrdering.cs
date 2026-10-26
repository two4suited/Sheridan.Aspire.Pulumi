using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;

namespace AspirePulumiPackages.Pulumi;

/// <summary>
/// Turns Aspire's resource relationships into pipeline ordering between Pulumi stacks. A resource hosted in stack S that
/// consumes another resource (WithReference, an endpoint/connection-string/<see cref="PulumiOutputReference"/> in its
/// environment, or an explicit WaitFor) implicitly waits for the stack hosting that resource: S's provision step is made
/// to depend on the other stack's deploy step, so the outputs S reads exist (and S's program, regenerated after its own
/// provision pass, resolves them). Within one stack the provision-then-deploy split already provides the ordering.
/// </summary>
internal static class PulumiReferenceOrdering
{
    public static void Configure(PipelineConfigurationContext context, PulumiStackResource stack)
    {
        var stacks = context.Model.Resources.OfType<PulumiStackResource>().ToList();
        if (stacks.Count < 2) return;

        var dependencies = new HashSet<PulumiStackResource>();
        var executionContext = context.Services.GetService<DistributedApplicationExecutionContext>();

        foreach (var consumer in context.Model.Resources)
        {
            if (consumer is PulumiStackResource || HostStack(consumer, stacks) != stack) continue;

            var references = new HashSet<IResource>();
            var outputStacks = new HashSet<PulumiStackResource>();

            foreach (var wait in consumer.Annotations.OfType<WaitAnnotation>()) references.Add(wait.Resource);

            if (executionContext is not null)
            {
                try
                {
                    var envVars = new Dictionary<string, object>();
                    var envContext = new EnvironmentCallbackContext(executionContext, consumer, envVars, CancellationToken.None);
                    foreach (var a in consumer.Annotations.OfType<EnvironmentCallbackAnnotation>())
                        a.Callback(envContext).GetAwaiter().GetResult();
                    foreach (var value in envVars.Values) Collect(value, references, outputStacks);
                }
                catch (Exception)
                {
                    // Callbacks that need runtime values can't be evaluated this early; relationships expressed
                    // through WaitFor/WithReference annotations are still honoured below.
                }
            }

            foreach (var reference in references)
                if (reference != consumer && HostStack(reference, stacks) is { } other) dependencies.Add(other);
            foreach (var other in outputStacks) dependencies.Add(other);
        }

        dependencies.Remove(stack);
        foreach (var dependency in dependencies)
        {
            var provisionSteps = context.GetSteps(stack, WellKnownPipelineTags.ProvisionInfrastructure).ToList();
            foreach (var producer in context.GetSteps(dependency, WellKnownPipelineTags.DeployCompute))
            foreach (var consumerStep in provisionSteps)
            {
                if (WouldCreateCycle(context.Steps, consumerStep, producer))
                    throw new InvalidOperationException(
                        $"Pulumi stacks '{stack.Name}' and '{dependency.Name}' reference each other's resources, which creates a circular deployment dependency.");
                if (!consumerStep.DependsOnSteps.Contains(producer.Name)) consumerStep.DependsOn(producer);
            }
        }
    }

    /// <summary>The Pulumi stack a resource is provisioned or hosted in, or null when it isn't Pulumi-managed (or the stack is ambiguous).</summary>
    internal static PulumiStackResource? HostStack(IResource resource, IReadOnlyCollection<PulumiStackResource> stacks)
    {
        PulumiStackResource? Single() => stacks.Count == 1 ? stacks.First() : null;

        for (IResource? current = resource; current is not null; current = (current as IResourceWithParent)?.Parent)
        {
            if (current is IPulumiProgramContributor contributor) return contributor.Stack ?? Single();
            if (current.Annotations.OfType<DeploymentTargetAnnotation>().FirstOrDefault()?.ComputeEnvironment is IPulumiProgramContributor env)
                return env.Stack ?? Single();
        }
        return null;
    }

    private static void Collect(object? value, HashSet<IResource> found, HashSet<PulumiStackResource> outputStacks)
    {
        switch (value)
        {
            case PulumiOutputReference o: outputStacks.Add(o.Stack); break;
            case EndpointReference e: found.Add(e.Resource); break;
            case EndpointReferenceExpression ex: found.Add(ex.Endpoint.Resource); break;
            case ConnectionStringReference c: found.Add(c.Resource); break;
            case ReferenceExpression re:
                foreach (var p in re.ValueProviders) Collect(p, found, outputStacks);
                break;
            case IResource r: found.Add(r); break;
        }
    }

    private static bool WouldCreateCycle(IReadOnlyList<PipelineStep> steps, PipelineStep consumer, PipelineStep producer)
    {
        if (consumer.Name == producer.Name) return true;
        var byName = steps.ToDictionary(s => s.Name, StringComparer.Ordinal);
        var pending = new Stack<string>(producer.DependsOnSteps);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var name))
        {
            if (!seen.Add(name)) continue;
            if (name == consumer.Name) return true;
            if (byName.TryGetValue(name, out var s)) foreach (var d in s.DependsOnSteps) pending.Push(d);
        }
        return false;
    }
}
