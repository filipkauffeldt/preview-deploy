using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Deployments;

namespace PreviewDeploy.Server.Tests;

public sealed class SweepTests : IAsyncLifetime
{
    private const string AppName = "demo";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private readonly FakeContainerRuntime _containers = new();
    private readonly FakeGitHubCommentClient _comments = new();
    private readonly FakeGitHubPullRequestClient _pullRequests = new();
    private TestAppFactory _factory = null!;
    private SweepService _sweep = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestAppFactory(
            configureServices: services =>
            {
                services.RemoveAll<IContainerRuntime>();
                services.AddSingleton<IContainerRuntime>(_containers);
                services.RemoveAll<IGitHubCommentClient>();
                services.AddSingleton<IGitHubCommentClient>(_comments);
                services.RemoveAll<IGitHubPullRequestClient>();
                services.AddSingleton<IGitHubPullRequestClient>(_pullRequests);

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
        _pullRequests.Closed[12] = true;

        await _sweep.RunOnceAsync(CancellationToken.None);

        var deployment = await GetDeploymentAsync(12);
        Assert.Equal(DeploymentStatus.Stopped, deployment.Status);
        Assert.Equal("pr-12-demo", Assert.Single(_containers.Stopped));
        Assert.Equal("demo:pr12-0123456", Assert.Single(_containers.RemovedImages));
        Assert.True(_containers.PruneCalls > 0, "sweep should prune dangling images");
        Assert.Contains(_comments.Upserts,
            u => u.Pr == 12 && u.Body.Contains("Preview deployment removed for PR #12"));
    }

    [Fact]
    public async Task Sweep_LeavesRunningDeployment_WhenPrIsOpenAndFresh()
    {
        var app = await CreateAppAsync();
        await CreateDeploymentAsync(app, pr: 12);

        await _sweep.RunOnceAsync(CancellationToken.None);

        var deployment = await GetDeploymentAsync(12);
        Assert.Equal(DeploymentStatus.Running, deployment.Status);
        Assert.Empty(_containers.Stopped);
        Assert.Empty(_containers.RemovedImages);
        Assert.Empty(_comments.Upserts);
    }

    [Fact]
    public async Task Sweep_TearsDownRunningDeployment_WhenTtlExpiredEvenIfPrIsOpen()
    {
        var app = await CreateAppAsync(ttlDays: 1);
        await CreateDeploymentAsync(app, pr: 12,
            updatedAtUtc: DateTimeOffset.UtcNow.AddDays(-3));

        await _sweep.RunOnceAsync(CancellationToken.None);

        var deployment = await GetDeploymentAsync(12);
        Assert.Equal(DeploymentStatus.Stopped, deployment.Status);
        Assert.Equal("pr-12-demo", Assert.Single(_containers.Stopped));
        Assert.Equal("demo:pr12-0123456", Assert.Single(_containers.RemovedImages));
        Assert.Contains(_comments.Upserts,
            u => u.Pr == 12 && u.Body.Contains("Preview deployment removed for PR #12"));
    }

    [Fact]
    public async Task Sweep_LeavesRunningDeployment_WhenGitHubCheckFails()
    {
        var app = await CreateAppAsync();
        await CreateDeploymentAsync(app, pr: 12);
        _pullRequests.FailChecks = true;

        await _sweep.RunOnceAsync(CancellationToken.None);

        var deployment = await GetDeploymentAsync(12);
        Assert.Equal(DeploymentStatus.Running, deployment.Status);
        Assert.Empty(_containers.Stopped);
        Assert.Empty(_containers.RemovedImages);
        Assert.NotEmpty(_pullRequests.Checked);
    }

    [Fact]
    public async Task Sweep_IgnoresDeploymentsThatAreNotRunning()
    {
        var app = await CreateAppAsync();
        await CreateDeploymentAsync(app, pr: 12, status: DeploymentStatus.Building);
        await CreateDeploymentAsync(app, pr: 13, status: DeploymentStatus.Failed);
        await CreateDeploymentAsync(app, pr: 14, status: DeploymentStatus.Stopped);
        _pullRequests.Closed[12] = true;
        _pullRequests.Closed[13] = true;
        _pullRequests.Closed[14] = true;

        await _sweep.RunOnceAsync(CancellationToken.None);

        Assert.Empty(_containers.Stopped);
        Assert.Empty(_containers.RemovedImages);
        Assert.Empty(_pullRequests.Checked);
        Assert.Equal(DeploymentStatus.Building, (await GetDeploymentAsync(12)).Status);
        Assert.Equal(DeploymentStatus.Failed, (await GetDeploymentAsync(13)).Status);
        Assert.Equal(DeploymentStatus.Stopped, (await GetDeploymentAsync(14)).Status);
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