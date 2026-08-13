using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Deployments;

namespace PreviewDeploy.Server.Tests;

public sealed class FakeContainerRuntime : IContainerRuntime
{
    public List<(string AppName, int Pr, string Sha, string ImageTag, string ContextDirectory)> Deployed { get; } = [];
    public List<string> Stopped { get; } = [];
    public int Port { get; init; } = 3000;
    public bool FailDeploy { get; set; }

    public Task<(string Name, int Port)> DeployAsync(
        string appName,
        int prNumber,
        string sha,
        string imageTag,
        string contextDirectory,
        int fallbackPort,
        CancellationToken cancellationToken)
    {
        if (FailDeploy)
        {
            throw new InvalidOperationException("build failed");
        }

        Deployed.Add((appName, prNumber, sha, imageTag, contextDirectory));
        return Task.FromResult((DeploymentNames.ContainerName(appName, prNumber), Port));
    }

    public Task StopAndRemoveAsync(string containerName, CancellationToken cancellationToken)
    {
        Stopped.Add(containerName);
        return Task.CompletedTask;
    }
}

public sealed class FakeGitCloner : IGitCloner
{
    public List<(string Owner, string Repo, int Pr, string Sha)> Clones { get; } = [];

    public Task<string> CloneAsync(
        string owner,
        string repo,
        int prNumber,
        string sha,
        string token,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        Clones.Add((owner, repo, prNumber, sha));
        var directory = Path.Combine(workDirectory, $"fake-clone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch\n");
        return Task.FromResult(directory);
    }
}

public sealed class FakeGitHubCommentClient : IGitHubCommentClient
{
    public List<(App App, int Pr, string Body)> Upserts { get; } = [];

    public Task UpsertAsync(App app, int prNumber, string body, CancellationToken cancellationToken)
    {
        Upserts.Add((app, prNumber, body));
        return Task.CompletedTask;
    }
}
