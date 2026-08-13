using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PreviewDeploy.Server.Data;

namespace PreviewDeploy.Server.Tests;

public sealed class SchemaTests
{
    [Fact]
    public async Task MigrationsCreateExpectedTables()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var tables = await QueryTableNames(db);
        Assert.Contains("apps", tables);
        Assert.Contains("deploy_events", tables);
        Assert.Contains("deployments", tables);
    }

    [Fact]
    public async Task AppCanBeStoredAndLoaded()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();

        db.Apps.Add(new App
        {
            Owner = "filipkauffeldt",
            Repo = "example-app",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var loaded = await db.Apps.SingleAsync();
        Assert.Equal("filipkauffeldt", loaded.Owner);
        Assert.Equal("example-app", loaded.Repo);
    }

    private static async Task<List<string>> QueryTableNames(PreviewDeployDbContext db)
    {
        var tables = new List<string>();
        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }
}
