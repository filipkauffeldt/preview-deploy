using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Deployments;
using PreviewDeploy.Server.Endpoints;

namespace PreviewDeploy.Server.Tests;

public sealed class DeployFlowTests : IAsyncLifetime
{
    private const string AppName = "demo";
    private const string AppToken = "top-secret";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private readonly FakeContainerRuntime _containers = new() { Port = 3000 };
    private readonly FakeGitCloner _cloner = new();
    private readonly FakeGitHubCommentClient _comments = new();
    private TestAppFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestAppFactory(
            configureServices: services =>
            {
                services.RemoveAll<IContainerRuntime>();
                services.AddSingleton<IContainerRuntime>(_containers);
                services.RemoveAll<IGitCloner>();
                services.AddSingleton<IGitCloner>(_cloner);
                services.RemoveAll<IGitHubCommentClient>();
                services.AddSingleton<IGitHubCommentClient>(_comments);
            },
            configureHost: builder => builder
                .UseSetting("Seed:Name", AppName)
                .UseSetting("Seed:Owner", "acme")
                .UseSetting("Seed:Repo", "widgets")
                .UseSetting("Seed:Token", AppToken)
                .UseSetting("Routing:BaseHost", "test.ts.net"));

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        await db.Apps.Where(a => a.Name == AppName)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.Port, 8080));
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DeployEvent_ClonesBuildsAndRunsContainer_AndRecordsStatus()
    {
        var response = await PostEventAsync("create");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var deployment = await EventuallyAsync(
            () => GetDeployment(),
            d => d is not null && d.Status == DeploymentStatus.Running,
            "deployment to run");

        Assert.NotNull(deployment);
        Assert.Equal(Sha, deployment.Sha);
        Assert.Equal("running", deployment.Status);
        Assert.Equal("demo:pr12-0123456", deployment.ImageTag);
        Assert.Equal(3000, deployment.Port);
        Assert.Equal("https://pr-12-demo.test.ts.net", deployment.Url);

        var clone = Assert.Single(_cloner.Clones);
        Assert.Equal("acme", clone.Owner);
        Assert.Equal("widgets", clone.Repo);
        Assert.Equal(12, clone.Pr);
        Assert.Equal(Sha, clone.Sha);

        var deployed = Assert.Single(_containers.Deployed);
        Assert.Equal(AppName, deployed.AppName);
        Assert.Equal(12, deployed.Pr);
        Assert.Equal(Sha, deployed.Sha);
        Assert.Equal("demo:pr12-0123456", deployed.ImageTag);
        Assert.False(Directory.Exists(deployed.ContextDirectory), "clone directory should be cleaned up after the build");

        Assert.Equal(2, _comments.Upserts.Count);
        Assert.Contains("Deploying preview for PR #12 (sha 0123456)", _comments.Upserts[0].Body);
        Assert.Contains("Preview ready at https://pr-12-demo.test.ts.net", _comments.Upserts[1].Body);
    }

    [Fact]
    public async Task CreateEvent_WithNewSha_Redeploys()
    {
        await PostEventAsync("create");
        await EventuallyAsync(
            () => GetDeployment(),
            d => d is not null && d.Status == DeploymentStatus.Running,
            "first deployment to run");

        var newSha = "ffffffffffffffffffffffffffffffffffffffff";
        await PostEventAsync("create", sha: newSha);

        var deployment = await EventuallyAsync(
            () => GetDeployment(),
            d => d is not null && d.Status == DeploymentStatus.Running && d.Sha == newSha,
            "redeployment with new sha");

        Assert.NotNull(deployment);
        Assert.Equal(newSha, deployment.Sha);
        Assert.Equal(2, _containers.Deployed.Count);
        Assert.Equal(2, _cloner.Clones.Count);
        Assert.Equal(newSha, _cloner.Clones[1].Sha);
        Assert.Equal(1, await CountDeploymentsAsync());
        Assert.Equal(4, _comments.Upserts.Count);
        Assert.Contains("Preview ready at https://pr-12-demo.test.ts.net (sha fffffff)", _comments.Upserts[^1].Body);
    }

    [Fact]
    public async Task TeardownEvent_StopsContainer_RemovesImage_AndMarksDeploymentStopped()
    {
        await PostEventAsync("create");
        await EventuallyAsync(
            () => GetDeployment(),
            d => d is not null && d.Status == DeploymentStatus.Running,
            "deployment to run");

        await PostEventAsync("teardown");

        var deployment = await EventuallyAsync(
            () => GetDeployment(),
            d => d is not null && d.Status == DeploymentStatus.Stopped,
            "deployment to stop");

        Assert.NotNull(deployment);
        Assert.Equal("stopped", deployment.Status);
        Assert.Equal("pr-12-demo", Assert.Single(_containers.Stopped));
        Assert.Equal("demo:pr12-0123456", Assert.Single(_containers.RemovedImages));
        Assert.Contains("Preview deployment removed for PR #12", _comments.Upserts[^1].Body);
    }

    [Fact]
    public async Task DeployEvent_WithBuildFailure_MarksDeploymentFailed()
    {
        _containers.FailDeploy = true;
        var response = await PostEventAsync("create");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var deployment = await EventuallyAsync(
            () => GetDeployment(),
            d => d is not null && d.Status == DeploymentStatus.Failed,
            "deployment to fail");

        Assert.NotNull(deployment);
        Assert.Equal("failed", deployment.Status);
        Assert.Null(deployment.Url);
        Assert.Contains("Preview deployment failed: build failed", _comments.Upserts[^1].Body);
    }
    private async Task<Deployment?> GetDeployment()
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        return await db.Deployments.SingleOrDefaultAsync(d => d.PrNumber == 12);
    }

    private async Task<int> CountDeploymentsAsync()
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        return await db.Deployments.CountAsync();
    }

    private static async Task<T> EventuallyAsync<T>(
        Func<Task<T>> get, Func<T, bool> isDone, string what, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        T? value;
        do
        {
            value = await get();
            if (isDone(value))
            {
                return value;
            }

            await Task.Delay(50);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Timed out waiting for {what}");
    }

    private async Task<HttpResponseMessage> PostEventAsync(string action, string? sha = null)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/deploy-events")
        {
            Content = JsonContent.Create(new DeployEventRequest(AppName, 12, sha ?? Sha, action)),
        };
        request.Headers.Authorization = new("Bearer", AppToken);
        return await client.SendAsync(request);
    }
}
