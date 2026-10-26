using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi;

/// <summary>A value that is read from a Pulumi stack output once the stack has been (partially) deployed.</summary>
public sealed class PulumiOutputReference(PulumiStackResource stack, string outputName) : IValueProvider, IManifestExpressionProvider
{
    /// <summary>The stack whose outputs this reads.</summary>
    public PulumiStackResource Stack => stack;

    public string ValueExpression => $"{{{stack.Name}.outputs.{outputName}}}";

    public async ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
    {
        var dir = stack.ProjectDirectory
            ?? throw new InvalidOperationException($"Pulumi project for '{stack.Name}' has not been generated yet.");
        try
        {
            var value = await PulumiCli.CaptureAsync(dir, ["stack", "output", outputName, "--stack", stack.QualifiedStack], cancellationToken);
            // A targeted 'pulumi up' records outputs of resources it did not create as null: "not available yet", not a value.
            if (value == "null") throw new PulumiOutputNotAvailableException(stack.Name, outputName);
            return value;
        }
        catch (InvalidOperationException e) when (e.Message.Contains("does not have output property", StringComparison.Ordinal))
        {
            throw new PulumiOutputNotAvailableException(stack.Name, outputName);
        }
    }

    public ValueTask<string?> GetValueAsync(ValueProviderContext context, CancellationToken cancellationToken = default) =>
        GetValueAsync(cancellationToken);
}

/// <summary>The stack has no such output yet, which is expected until the infrastructure behind it has been provisioned.</summary>
public sealed class PulumiOutputNotAvailableException(string stackName, string outputName)
    : InvalidOperationException($"output '{outputName}' of stack '{stackName}' isn't available yet (the stack hasn't created the resource behind it)")
{
}
