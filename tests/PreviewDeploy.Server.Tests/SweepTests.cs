using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Deployments;
using Shouldly;

namespace PreviewDeploy.Server.Tests;

public sealed class SweepTests : IAsyncLifetime
{
    private const string AppName = "demo";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private readonly IContainerRuntime _containers = Substitute.For<IContainerRuntime>();
    private readonly IGitHubCommentClient _comments = Substitute.For<IGitHubCommentClient>();
    private readonly IGitHubPullRequestClient _pullRequests = Substitute.For<IGitHubPullRequestClient>();
    private readonly List<string> _commentBodies = [];
    private TestAppFactory _factory = null!;
    private SweepService _sweep = null!;

    public async Task InitializeAsync()
    {
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
                services.RemoveAll<IGitHubCommentClient>();
                services.AddSingleton(_comments);
                services.RemoveAll<IGitHubPullRequestClient>();
                services.AddSingleton(_pullRequests);

                var sweepHosted = services
                    .Where(d => d.ServiceType == typeof(IHostedService) &&
                                d.ImplementationType == typeof(SweepService))
                    .ToList();
                foreach (var descriptor in sweepHosted)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton<SweepService>();
            });

        _sweep = _factory.Services.GetRequiredService<SweepService>();
        await Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Sweep_TearsDownRunningDeployment_WhenPrIsClosed()
    {
        var app = await CreateAppAsync();
        await CreateDeploymentAsync(app, pr: 12);
        _pullRequests.IsClosedAsync("acme", "widgets", 12, Arg.Any<CancellationToken>()).Returns(true);

        await _sweep.RunOnceAsync(CancellationToken.None);

        (await GetDeploymentAsync(12)).Status.ShouldBe(DeploymentStatus.Stopped);
        await _containers.Received(1).StopAndRemoveAsync("pr-12-demo", Arg.Any<CancellationToken>());
        await _containers.Received(1).RemoveImageAsync("demo:pr12-0123456", Arg.Any<CancellationToken>());
        await _containers.Received(1).PruneImagesAsync(Arg.Any<CancellationToken>());
        _commentBodies.ShouldHaveSingleItem().ShouldContain("Preview deployment removed for PR #12");
    }

    [Fact]
    public async Task Sweep_LeavesRunningDeployment_WhenPrIsOpenAndFresh()
    {
        var app = await CreateAppAsync();
        await CreateDeploymentAsync(app, pr: 12);
        _pullRequests.IsClosedAsync("acme", "widgets", 12, Arg.Any<CancellationToken>()).Returns(false);

        await _sweep.RunOnceAsync(CancellationToken.None);

        (await GetDeploymentAsync(12)).Status.ShouldBe(DeploymentStatus.Running);
        await _containers.DidNotReceive().StopAndRemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _containers.DidNotReceive().RemoveImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _commentBodies.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sweep_TearsDownRunningDeployment_WhenTtlExpiredEvenIfPrIsOpen()
    {
        var app = await CreateAppAsync(ttlDays: 1);
        await CreateDeploymentAsync(app, pr: 12,
            updatedAtUtc: DateTimeOffset.UtcNow.AddDays(-3));

        await _sweep.RunOnceAsync(CancellationToken.None);

        (await GetDeploymentAsync(12)).Status.ShouldBe(DeploymentStatus.Stopped);
        await _containers.Received(1).StopAndRemoveAsync("pr-12-demo", Arg.Any<CancellationToken>());
        await _containers.Received(1).RemoveImageAsync("demo:pr12-0123456", Arg.Any<CancellationToken>());
        await _pullRequests.DidNotReceive().IsClosedAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        _commentBodies.ShouldHaveSingleItem().ShouldContain("Preview deployment removed for PR #12");
    }

    [Fact]
    public async Task Sweep_LeavesRunningDeployment_WhenGitHubCheckFails()
    {
        var app = await CreateAppAsync();
        await CreateDeploymentAsync(app, pr: 12);
        _pullRequests.IsClosedAsync("acme", "widgets", 12, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("github down"));

        await _sweep.RunOnceAsync(CancellationToken.None);

        (await GetDeploymentAsync(12)).Status.ShouldBe(DeploymentStatus.Running);
        await _containers.DidNotReceive().StopAndRemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _containers.DidNotReceive().RemoveImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _pullRequests.Received(1).IsClosedAsync(
            "acme", "widgets", 12, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_IgnoresDeploymentsThatAreNotRunning()
    {
        var app = await CreateAppAsync();
        await CreateDeploymentAsync(app, pr: 12, status: DeploymentStatus.Building);
        await CreateDeploymentAsync(app, pr: 13, status: DeploymentStatus.Failed);
        await CreateDeploymentAsync(app, pr: 14, status: DeploymentStatus.Stopped);

        await _sweep.RunOnceAsync(CancellationToken.None);

        await _containers.DidNotReceive().StopAndRemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _containers.DidNotReceive().RemoveImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _pullRequests.DidNotReceive().IsClosedAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        (await GetDeploymentAsync(12)).Status.ShouldBe(DeploymentStatus.Building);
        (await GetDeploymentAsync(13)).Status.ShouldBe(DeploymentStatus.Failed);
        (await GetDeploymentAsync(14)).Status.ShouldBe(DeploymentStatus.Stopped);
    }

    private async Task<App> CreateAppAsync(int ttlDays = 14)
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        var app = new App
        {
            Name = AppName,
            Owner = "acme",
            Repo = "widgets",
            TokenHash = "x",
            TtlDays = ttlDays,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Apps.Add(app);
        await db.SaveChangesAsync();
        return app;
    }

    private async Task CreateDeploymentAsync(
        App app,
        int pr,
        string status = DeploymentStatus.Running,
        DateTimeOffset? updatedAtUtc = null)
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        db.Deployments.Add(new Deployment
        {
            AppId = app.Id,
            PrNumber = pr,
            Sha = Sha,
            Status = status,
            ImageTag = $"{app.Name}:pr{pr}-0123456",
            Port = 3000,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = updatedAtUtc ?? DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Deployment> GetDeploymentAsync(int pr)
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        return await db.Deployments.SingleAsync(d => d.PrNumber == pr);
    }
}