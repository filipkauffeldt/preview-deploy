namespace PreviewDeploy.Server.Data;

public sealed class App
{
    public int Id { get; set; }
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<DeployEvent> Events { get; } = [];
    public List<Deployment> Deployments { get; } = [];
}

public sealed class DeployEvent
{
    public int Id { get; set; }
    public int AppId { get; set; }
    public App App { get; set; } = null!;
    public int PrNumber { get; set; }
    public string Sha { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed class Deployment
{
    public int Id { get; set; }
    public int AppId { get; set; }
    public App App { get; set; } = null!;
    public int PrNumber { get; set; }
    public string Sha { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Url { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
