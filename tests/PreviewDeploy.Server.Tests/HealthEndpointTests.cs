using System.Net;
using Shouldly;

namespace PreviewDeploy.Server.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_ReturnsOk_WhenDatabaseIsReachable()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Health_ReturnsOk_WhenStartedWithDataDirEnvVar()
    {
        var tempDir = Directory.CreateTempSubdirectory("preview-deploy-env-").FullName;
        Environment.SetEnvironmentVariable("DATA_DIR", tempDir);
        try
        {
            using var factory = new TestAppFactory(dataDirectory: "/proc", fileName: "preview-deploy.db");
            var client = factory.CreateClient();

            var response = await client.GetAsync("/health");

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DATA_DIR", null);
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
