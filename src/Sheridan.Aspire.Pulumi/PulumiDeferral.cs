using Microsoft.Extensions.Logging;

namespace Sheridan.Aspire.Pulumi;

public static class PulumiDeferral
{
    /// <summary>
    /// Logs that an environment variable could not be resolved while generating the program and was left out. Before the
    /// stack's provision pass has run (the first generation of a deploy, or <c>aspire publish</c>, which has no real
    /// stack) that is expected and logged at debug level; the deploy pass regenerates the program once the
    /// infrastructure exists, and anything still unresolved then is worth a warning.
    /// </summary>
    public static void LogDeferredEnvironmentVariable(
        this ILogger logger, PulumiStackResource? stack, string key, string resource, Exception error)
    {
        var level = stack?.InfrastructureProvisioned == true ? LogLevel.Warning : LogLevel.Debug;
        logger.Log(level, "Deferring environment variable {Key} for {Resource}: {Message}", key, resource, error.Message);
    }
}
