using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Routing;

namespace PreviewDeploy.Server.Tests;

public sealed class PreviewProxyConfigProviderTests : IAsyncLifetime
{
    private TestAppFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestAppFactory(
            configureHost: builder => builder
                .UseSetting("Routing:BaseHost", "test.ts.net"));

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        db.Apps.Add(new App
        {
            Name = "demo",
            Owner = "acme",
            Repo = "widgets",
            TokenHash = "x",
            Port = 8080,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetConfig_AfterChange_ContainsRouteForRunningDeployment()
    {
        await InsertDeploymentAsync(12, DeploymentStatus.Running, port: 3000, sha: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var provider = GetProvider();
        provider.NotifyChanged();
        var config = provider.GetConfig();

        var route = Assert.Single(config.Routes);
        Assert.Equal("preview-1-pr12", route.RouteId);
        Assert.Equal("pr-12-demo.test.ts.net", Assert.Single(route.Match.Hosts!));

        var cluster = Assert.Single(config.Clusters);
        Assert.Equal("preview-1-pr12", cluster.ClusterId);
        Assert.Equal("http://pr-12-demo:3000", Assert.Single(cluster.Destinations.Values).Address);
    }

    [Fact]
    public async Task GetConfig_OmitsNonRunningDeployments()
    {
        await InsertDeploymentAsync(12, DeploymentStatus.Failed, port: 3000, sha: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var provider = GetProvider();
        provider.NotifyChanged();
        var config = provider.GetConfig();

        Assert.Empty(config.Routes);
        Assert.Empty(config.Clusters);
    }

    [Fact]
    public async Task NotifyChanged_SwapsConfig_AndSignalsChange()
    {
        await InsertDeploymentAsync(12, DeploymentStatus.Running, port: 3000, sha: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var provider = GetProvider();
        provider.NotifyChanged();

        var initial = provider.GetConfig();
        Assert.Single(initial.Routes);

        await InsertDeploymentAsync(13, DeploymentStatus.Running, port: 4000, sha: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        provider.NotifyChanged();

        var updated = provider.GetConfig();
        Assert.Equal(2, updated.Routes.Count);
        Assert.Single(updated.Routes, r => r.Match.Hosts!.Single() == "pr-13-demo.test.ts.net");
        Assert.Single(updated.Clusters, c => c.Destinations.Values.Single().Address == "http://pr-13-demo:4000");
        Assert.NotSame(initial, updated);
        Assert.True(initial.ChangeToken.ActiveChangeCallbacks);
    }

    [Fact]
    public async Task NotifyChanged_AfterTeardown_RemovesRoute()
    {
        await InsertDeploymentAsync(12, DeploymentStatus.Running, port: 3000, sha: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var provider = GetProvider();
        provider.NotifyChanged();
        Assert.Single(provider.GetConfig().Routes);

        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
            await db.Deployments
                .Where(d => d.PrNumber == 12)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DeploymentStatus.Stopped));
        }

        provider.NotifyChanged();

        Assert.Empty(provider.GetConfig().Routes);
    }

    [Fact]
    public async Task GetConfig_LoadsExistingRunningDeployments_AfterRestart()
    {
        var restartDirectory = Directory.CreateTempSubdirectory("preview-deploy-restart-").FullName;
        try
        {
            using (var first = new TestAppFactory(
                dataDirectory: restartDirectory,
                configureHost: builder => builder.UseSetting("Routing:BaseHost", "test.ts.net")))
            {
                using var scope = first.CreateDbScope();
                var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
                var app = new App
                {
                    Name = "demo",
                    Owner = "acme",
                    Repo = "widgets",
                    TokenHash = "x",
                    Port = 8080,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                };
                db.Apps.Add(app);
                db.Deployments.Add(new Deployment
                {
                    App = app,
                    PrNumber = 12,
                    Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    Status = DeploymentStatus.Running,
                    Url = "https://pr-12-demo.test.ts.net",
                    Port = 3000,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            using (var restarted = new TestAppFactory(
                dataDirectory: restartDirectory,
                configureHost: builder => builder.UseSetting("Routing:BaseHost", "test.ts.net")))
            {
                var config = restarted.Services
                    .GetRequiredService<PreviewProxyConfigProvider>()
                    .GetConfig();

                Assert.Equal("pr-12-demo.test.ts.net", Assert.Single(config.Routes).Match.Hosts!.Single());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(restartDirectory, recursive: true);
        }
    }

    private PreviewProxyConfigProvider GetProvider() =>
        _factory.Services.GetRequiredService<PreviewProxyConfigProvider>();

    private async Task InsertDeploymentAsync(int pr, string status, int port, string sha)
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        var app = await db.Apps.SingleAsync(a => a.Name == "demo");
        db.Deployments.Add(new Deployment
        {
            AppId = app.Id,
            PrNumber = pr,
            Sha = sha,
            Status = status,
            Url = $"https://pr-{pr}-demo.test.ts.net",
            Port = port,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
