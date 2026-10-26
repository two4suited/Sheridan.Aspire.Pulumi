using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.ConnectorNamespace;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Connector Namespace. Because azure-native has no
/// <c>connectorGateways</c> resource, the namespace is one <c>azure-native:resources:Deployment</c> whose ARM template holds
/// the gateway and its children; <see cref="TemplateResources"/> exposes those template resources (plain maps using
/// ARM property names, e.g. <c>type</c>, <c>name</c>, <c>properties</c>) for editing or appending.
/// </summary>
public sealed class PulumiConnectorNamespaceInfrastructure(PulumiInfrastructure inner, AzureConnectorNamespaceResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:resources:Deployment</c> resource (e.g. <c>Set("properties.mode", "Complete")</c>).</summary>
    public PulumiResourceNode Deployment => GetResource(owner.Id);

    /// <summary>The ARM template's <c>resources</c> list; the first entry is the <c>Microsoft.Web/connectorGateways</c> gateway.</summary>
    public IList<object?> TemplateResources =>
        (IList<object?>)(Deployment.Get("properties.template.resources") ?? throw new InvalidOperationException("The ARM template has no resources."));

    /// <summary>The gateway (<c>Microsoft.Web/connectorGateways</c>) template resource as an editable map.</summary>
    public IDictionary<string, object?> Gateway => (IDictionary<string, object?>)TemplateResources[0]!;
}
