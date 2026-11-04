using System.Globalization;
using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Azure.Sandboxes;

/// <summary>
/// Resolves a property of an endpoint of a compute resource that is deployed to a sandbox group. The public URL only exists once
/// the producing sandbox has been deployed, so the value resolves from the group's deployed-port registry, which the deploy
/// step fills; referencing sandboxes are ordered after the sandboxes they reference.
/// </summary>
internal sealed class AzureSandboxEndpointValueProvider(
    AzureSandboxGroupResource group, AzureSandboxContainerResource target, EndpointReferenceExpression expression) : IValueProvider, IManifestExpressionProvider
{
    public string ValueExpression =>
        $"{{{target.Name}.endpoints.{expression.Endpoint.EndpointName}.{expression.Property.ToString().ToLowerInvariant()}}}";

    public ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
    {
        var targetPort = AzureSandboxDeployment.ResolveEndpointTargetPort(target, expression.Endpoint.EndpointName);

        // These do not depend on the deployed URL (sandbox ingress always terminates TLS on 443).
        switch (expression.Property)
        {
            case EndpointProperty.TargetPort when targetPort is { } port:
                return new(port.ToString(CultureInfo.InvariantCulture));
            case EndpointProperty.Scheme:
                return new(Uri.UriSchemeHttps);
            case EndpointProperty.Port:
                return new("443");
            case EndpointProperty.TlsEnabled:
                return new(bool.TrueString);
        }

        if (!group.DeployedPorts.TryGetValue(target.TargetResource.Name, out var ports)
            || ports.FirstOrDefault(p => p.Name == expression.Endpoint.EndpointName || p.Port == targetPort) is not { } info)
        {
            throw new InvalidOperationException(
                $"Azure sandbox endpoint '{expression.Endpoint.EndpointName}' on resource '{target.TargetResource.Name}' does not have a deployed URL yet. " +
                "Runtime sandbox URLs cannot be used as first-pass provisioning values; deploy the producing sandbox before resolving this reference.");
        }

        var uri = new Uri(info.Url);
        return new(expression.Property switch
        {
            EndpointProperty.Url => info.Url,
            EndpointProperty.Host or EndpointProperty.IPV4Host => uri.Host,
            EndpointProperty.HostAndPort => uri.IsDefaultPort ? uri.Host : uri.Authority,
            _ => throw new InvalidOperationException($"The property '{expression.Property}' is not supported for the sandbox endpoint '{expression.Endpoint.EndpointName}'."),
        });
    }

    public ValueTask<string?> GetValueAsync(ValueProviderContext context, CancellationToken cancellationToken = default) =>
        GetValueAsync(cancellationToken);
}
