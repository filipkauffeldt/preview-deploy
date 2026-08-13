using System.Diagnostics;

namespace PreviewDeploy.Server.Deployments;

public interface IGitCloner
{
    Task<string> CloneAsync(
        string owner,
        string repo,
        int prNumber,
        string sha,
        string token,
        string workDirectory,
        CancellationToken cancellationToken);
}

public sealed class GitCloner : IGitCloner
{
    public async Task<string> CloneAsync(
        string owner,
        string repo,
        int prNumber,
        string sha,
        string token,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(
            workDirectory,
            $"{repo}-pr{prNumber}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(directory);

        var remoteUrl = $"https://x-access-token:{token}@github.com/{owner}/{repo}.git";
        await RunAsync(directory, ["init", "-q"], cancellationToken);
        await RunAsync(directory, ["remote", "add", "origin", remoteUrl], cancellationToken);
        await RunAsync(
            directory,
            ["fetch", "-q", "--depth", "1", "origin", $"refs/pull/{prNumber}/head"],
            cancellationToken);
        await RunAsync(directory, ["checkout", "-q", sha], cancellationToken);

        return directory;
    }

    private static async Task RunAsync(string workingDirectory, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the git process");
        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {standardError.Trim()}");
        }
    }
}
