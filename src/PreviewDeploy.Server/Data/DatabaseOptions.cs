namespace PreviewDeploy.Server.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string DataDirectory { get; set; } = ".";
    public string FileName { get; set; } = "preview-deploy.db";

    public string ResolveConnectionString()
    {
        var path = Path.Combine(DataDirectory, FileName);
        return $"Data Source={path}";
    }
}
