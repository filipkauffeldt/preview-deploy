using PreviewDeploy.Server.Data;
using Shouldly;

namespace PreviewDeploy.Server.Tests;

public sealed class DatabaseOptionsTests
{
    [Fact]
    public void ResolveConnectionString_PointsAtDataDirectoryAndFileName()
    {
        var options = new DatabaseOptions
        {
            DataDirectory = "/app/data",
            FileName = "preview-deploy.db",
        };

        options.ResolveConnectionString().ShouldBe("Data Source=/app/data/preview-deploy.db");
    }

    [Fact]
    public void Defaults_UseCurrentDirectoryAndPreviewDeployDb()
    {
        var options = new DatabaseOptions();

        options.DataDirectory.ShouldBe(".");
        options.FileName.ShouldBe("preview-deploy.db");
    }
}
