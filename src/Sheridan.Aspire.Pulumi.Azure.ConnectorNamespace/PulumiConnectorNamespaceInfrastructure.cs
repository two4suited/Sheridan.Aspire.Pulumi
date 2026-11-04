using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.ConnectorNamespace;

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

    /// <summary>The ARM template's <c>resources</c> list; the first entry is the <c>Microsoft.Web/connectorGateways</c> gateway (absent for an existing gateway).</summary>
    public IList<object?> TemplateResources =>
        (IList<object?>)(Deployment.Get("properties.template.resources") ?? throw new InvalidOperationException("The ARM template has no resources."));

    /// <summary>The gateway (<c>Microsoft.Web/connectorGateways</c>) template resource as an editable map. Throws for gateways declared with PublishAsExisting.</summary>
    public IDictionary<string, object?> Gateway => owner.ExistingGatewayName is null
        ? (IDictionary<string, object?>)TemplateResources[0]!
        : throw new InvalidOperationException($"'{owner.Name}' uses an existing gateway; the template does not contain a gateway resource.");
}
