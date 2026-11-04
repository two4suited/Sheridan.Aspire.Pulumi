using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Azure;

/// <summary>
/// Lets an integration adjust the <c>azure-native:app:ContainerApp</c> definition (the Pulumi YAML resource node with
/// <c>type</c>, <c>properties</c> and <c>options</c>) that <see cref="AzureContainerAppEnvironmentResource"/> generates for a
/// compute resource, e.g. Azure Functions setting <c>kind: functionapp</c>.
/// </summary>
public sealed class PulumiContainerAppCustomizationAnnotation(Action<Dictionary<string, object?>> configure) : IResourceAnnotation
{
    public Action<Dictionary<string, object?>> Configure { get; } = configure;
}
