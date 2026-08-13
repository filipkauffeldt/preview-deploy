namespace PreviewDeploy.Server.Options;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string Token { get; set; } = "";
}

public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    public string BaseHost { get; set; } = "preview-server.ts.net";
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string Name { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = "";
    public string Token { get; set; } = "";

    public bool IsConfigured =>
        Name is not "" && Owner is not "" && Repo is not "" && Token is not "";
}

public sealed class DockerOptions
{
    public const string SectionName = "Docker";

    public string SocketPath { get; set; } = "/var/run/docker.sock";
    public string NetworkName { get; set; } = "preview-net";
    public string WorkDirectory { get; set; } = "/tmp/preview-deploy";
    public bool AttachNetworkOnStartup { get; set; }
    public string SelfContainerName { get; set; } = "preview-deploy-server";
}

public sealed class CertsOptions
{
    public const string SectionName = "Certs";

    public string Directory { get; set; } = "/certs";
    public string ApiCertificateFile { get; set; } = "api.crt";
    public string ApiKeyFile { get; set; } = "api.key";
    public string PreviewCertificateFile { get; set; } = "preview.crt";
    public string PreviewKeyFile { get; set; } = "preview.key";
}
