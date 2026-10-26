namespace AspirePulumiPackages.SchemaConformance.Tests;

internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.GetFiles(dir.FullName, "*.slnx").Length > 0) return dir.FullName;
        throw new InvalidOperationException("Could not locate the repository root (*.slnx).");
    }
}
