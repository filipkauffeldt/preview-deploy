namespace PreviewDeploy.Server.Deployments;

public static class DeploymentNames
{
    public static string ContainerName(string appName, int prNumber) => $"pr-{prNumber}-{appName}";

    public static string Subdomain(string appName, int prNumber) => ContainerName(appName, prNumber);
}
