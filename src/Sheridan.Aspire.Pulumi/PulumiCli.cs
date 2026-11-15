using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Sheridan.Aspire.Pulumi;

internal static class PulumiCli
{
    public static async Task<int> RunAsync(
        string workingDirectory,
        IEnumerable<string> args,
        ILogger logger,
        CancellationToken cancellationToken,
        string? stdin = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo("pulumi")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PULUMI_SKIP_UPDATE_CHECK"] = "true";
        if (env is not null) foreach (var (k, v) in env) psi.Environment[k] = v;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the pulumi CLI. Is it installed and on PATH?");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("{Line}", e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("{Line}", e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    public static async Task RunOrThrowAsync(
        string workingDirectory,
        IEnumerable<string> args,
        ILogger logger,
        CancellationToken cancellationToken,
        string? stdin = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var list = args.ToList();
        var code = await RunAsync(workingDirectory, list, logger, cancellationToken, stdin, env);
        if (code != 0)
            throw new InvalidOperationException($"'pulumi {string.Join(' ', list)}' failed with exit code {code}.");
    }

    public static async Task<string> CaptureAsync(string workingDirectory, IEnumerable<string> args, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo("pulumi")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PULUMI_SKIP_UPDATE_CHECK"] = "true";
        if (env is not null) foreach (var (k, v) in env) psi.Environment[k] = v;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the pulumi CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"'pulumi {string.Join(' ', args)}' failed: {await stderr}");
        return (await stdout).Trim();
    }
}
