using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PreviewDeploy.Server.Tests;

public sealed class TestAppFactory(string? dataDirectory = null, string? fileName = null)
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
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && dataDirectory is null && Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }

    public IServiceScope CreateDbScope() => Services.CreateScope();
}
