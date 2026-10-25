using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi;

/// <summary>A value that is read from a Pulumi stack output once the stack has been (partially) deployed.</summary>
public sealed class PulumiOutputReference(PulumiStackResource stack, string outputName) : IValueProvider, IManifestExpressionProvider
{
    public string ValueExpression => $"{{{stack.Name}.outputs.{outputName}}}";

    public async ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
    {
        var dir = stack.ProjectDirectory
            ?? throw new InvalidOperationException($"Pulumi project for '{stack.Name}' has not been generated yet.");
        return await PulumiCli.CaptureAsync(dir, ["stack", "output", outputName, "--stack", stack.QualifiedStack], cancellationToken);
    }

    public ValueTask<string?> GetValueAsync(ValueProviderContext context, CancellationToken cancellationToken = default) =>
        GetValueAsync(cancellationToken);
}
