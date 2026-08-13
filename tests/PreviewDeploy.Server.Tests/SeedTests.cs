using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PreviewDeploy.Server.Auth;
using PreviewDeploy.Server.Data;

namespace PreviewDeploy.Server.Tests;

public sealed class SeedTests
{
    [Fact]
    public async Task Startup_WithSeedOptions_CreatesAppWithHashedToken()
    {
        using var factory = new TestAppFactory(
            configureHost: builder => builder
                .UseSetting("Seed:Name", "demo")
                .UseSetting("Seed:Owner", "acme")
                .UseSetting("Seed:Repo", "widgets")
                .UseSetting("Seed:Token", "top-secret"));
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();

        var app = await db.Apps.SingleAsync(a => a.Name == "demo");
        Assert.Equal("acme", app.Owner);
        Assert.Equal("widgets", app.Repo);
        Assert.Equal(AppToken.Hash("top-secret"), app.TokenHash);
        Assert.NotEqual("top-secret", app.TokenHash);
    }

    [Fact]
    public async Task Startup_WithSeedOptions_UpdatesExistingApp()
    {
        using var factory = new TestAppFactory(
            configureHost: builder => builder
                .UseSetting("Seed:Name", "demo")
                .UseSetting("Seed:Owner", "acme")
                .UseSetting("Seed:Repo", "widgets")
                .UseSetting("Seed:Token", "first-token"));
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();

        var app = await db.Apps.SingleAsync();
        Assert.Equal(AppToken.Hash("first-token"), app.TokenHash);

        await db.Apps.Where(a => a.Id == app.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.Repo, "gadgets")
                .SetProperty(a => a.TokenHash, AppToken.Hash("second-token")));

        using var factory2 = new TestAppFactory(
            dataDirectory: factory.DataDirectory,
            configureHost: builder => builder
                .UseSetting("Seed:Name", "demo")
                .UseSetting("Seed:Owner", "acme")
                .UseSetting("Seed:Repo", "widgets")
                .UseSetting("Seed:Token", "first-token"));

        using var scope2 = factory2.CreateDbScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        var reloaded = await db2.Apps.SingleAsync();
        Assert.Equal("widgets", reloaded.Repo);
        Assert.Equal(AppToken.Hash("first-token"), reloaded.TokenHash);
    }
}
