namespace Sheridan.Aspire.Pulumi.Tests;

/// <summary>Creates stub project files for <c>AddDotnetProject</c>, which (unlike <c>AddProject&lt;T&gt;</c>) reads the project at the path it is given.</summary>
internal static class TestProjects
{
    public static string Create(string fileName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "sheridan-tests-projects", Path.GetFileNameWithoutExtension(fileName));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>");
        return path;
    }
}
