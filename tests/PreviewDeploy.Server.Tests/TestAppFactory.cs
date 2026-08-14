using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace PreviewDeploy.Server.Tests;

public sealed class TestAppFactory(
    Action<IServiceCollection>? configureServices = null,
    string? dataDirectory = null,
    string? fileName = null,
    Action<IWebHostBuilder>? configureHost = null)
    : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } =
        dataDirectory ?? Directory.CreateTempSubdirectory("preview-deploy-tests-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:DataDirectory", DataDirectory);
        if (fileName is not null)
        {
            builder.UseSetting("Database:FileName", fileName);
        }

        configureHost?.Invoke(builder);
        builder.ConfigureServices(services => configureServices?.Invoke(services));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && dataDirectory is null && Directory.Exists(DataDirectory))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(DataDirectory, recursive: true);
        }
    }

    public IServiceScope CreateDbScope() => Services.CreateScope();
}
