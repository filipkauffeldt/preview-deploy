using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Deployments;
using PreviewDeploy.Server.Endpoints;
using Shouldly;

namespace PreviewDeploy.Server.Tests;

public sealed class DeployFlowTests : IAsyncLifetime
{
    private const string AppName = "demo";
    private const string AppToken = "top-secret";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private readonly IContainerRuntime _containers = Substitute.For<IContainerRuntime>();
    private readonly IGitCloner _cloner = Substitute.For<IGitCloner>();
    private readonly IGitHubCommentClient _comments = Substitute.For<IGitHubCommentClient>();
    private readonly List<string> _commentBodies = [];
    private TestAppFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _containers.DeployAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(("pr-12-demo", 3000));

        _cloner.CloneAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var directory = Path.Combine(Path.GetTempPath(), $"fake-clone-{Guid.NewGuid():N}");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch\n");
                return directory;
            });

        _comments.UpsertAsync(
            Arg.Any<App>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                _commentBodies.Add(ci.ArgAt<string>(2));
                return Task.CompletedTask;
            });

        _factory = new TestAppFactory(
            configureServices: services =>
            {
                services.RemoveAll<IContainerRuntime>();
                services.AddSingleton(_containers);
                services.RemoveAll<IGitCloner>();
                services.AddSingleton(_cloner);
                services.RemoveAll<IGitHubCommentClient>();
                services.AddSingleton(_comments);
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
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var deployment = await EventuallyAsync(
            () => GetDeployment(),
            d => d is not null && d.Status == DeploymentStatus.Running,
            "deployment to run");

        deployment.ShouldNotBeNull();
        deployment.Sha.ShouldBe(Sha);
        deployment.Status.ShouldBe("running");
        deployment.ImageTag.ShouldBe("demo:pr12-0123456");
        deployment.Port.ShouldBe(3000);
        deployment.Url.ShouldBe("https://pr-12-demo.test.ts.net");

        await _cloner.Received(1).CloneAsync(
            "acme", "widgets", 12, Sha, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await _containers.Received(1).DeployAsync(
            AppName, 12, Sha, "demo:pr12-0123456", Arg.Any<string>(), 8080, Arg.Any<CancellationToken>());

        var comments = await WaitForCommentsAsync(2);
        comments[0].ShouldContain("Deploying preview for PR #12 (sha 0123456)");
        comments[1].ShouldContain("Preview ready at https://pr-12-demo.test.ts.net");
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

        deployment.ShouldNotBeNull();
        deployment.Sha.ShouldBe(newSha);
        await _containers.Received(2).DeployAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _cloner.Received(2).CloneAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _cloner.Received(1).CloneAsync(
            "acme", "widgets", 12, newSha, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await CountDeploymentsAsync()).ShouldBe(1);

        var comments = await WaitForCommentsAsync(4);
        comments[^1].ShouldContain("Preview ready at https://pr-12-demo.test.ts.net (sha fffffff)");
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

        deployment.ShouldNotBeNull();
        deployment.Status.ShouldBe("stopped");
        await _containers.Received(1).StopAndRemoveAsync("pr-12-demo", Arg.Any<CancellationToken>());
        await _containers.Received(1).RemoveImageAsync("demo:pr12-0123456", Arg.Any<CancellationToken>());

        var comments = await WaitForCommentsAsync(3);
        comments[^1].ShouldContain("Preview deployment removed for PR #12");
    }

    [Fact]
    public async Task DeployEvent_WithBuildFailure_MarksDeploymentFailed()
    {
        _containers.DeployAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("build failed"));
        var response = await PostEventAsync("create");
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var deployment = await EventuallyAsync(
            () => GetDeployment(),
            d => d is not null && d.Status == DeploymentStatus.Failed,
            "deployment to fail");

        deployment.ShouldNotBeNull();
        deployment.Status.ShouldBe("failed");
        deployment.Url.ShouldBeNull();

        var comments = await WaitForCommentsAsync(2);
        comments[^1].ShouldContain("Preview deployment failed: build failed");
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

    private async Task<List<string>> WaitForCommentsAsync(int count) =>
        await EventuallyAsync(
            () => Task.FromResult(_commentBodies.ToList()),
            comments => comments.Count == count,
            $"{count} comments to be posted");

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