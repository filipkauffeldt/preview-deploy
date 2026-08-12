using PreviewDeploy.Server.Data;

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

        Assert.Equal("Data Source=/app/data/preview-deploy.db", options.ResolveConnectionString());
    }

    [Fact]
    public void Defaults_UseCurrentDirectoryAndPreviewDeployDb()
    {
        var options = new DatabaseOptions();

        Assert.Equal(".", options.DataDirectory);
        Assert.Equal("preview-deploy.db", options.FileName);
    }
}
