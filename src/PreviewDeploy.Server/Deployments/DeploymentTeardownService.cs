using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Routing;

namespace PreviewDeploy.Server.Deployments;

public sealed class DeploymentTeardownService(
    PreviewDeployDbContext db,
    IContainerRuntime containers,
    IGitHubCommentClient comments,
    IPreviewRoutingConfig routingConfig,
    ILogger<DeploymentTeardownService> logger)
{
    public async Task TeardownAsync(App app, Deployment deployment, CancellationToken cancellationToken)
    {
        await containers.StopAndRemoveAsync(
            DeploymentNames.ContainerName(app.Name, deployment.PrNumber), cancellationToken);

        if (deployment.ImageTag is { Length: > 0 })
        {
            try
            {
                await containers.RemoveImageAsync(deployment.ImageTag, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to remove preview image {ImageTag} for app {App} PR {Pr}",
                    deployment.ImageTag, app.Name, deployment.PrNumber);
            }
        }

        deployment.Status = DeploymentStatus.Stopped;
        deployment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        routingConfig.NotifyChanged();

        try
        {
            await comments.UpsertAsync(app, deployment.PrNumber,
                $"Preview deployment removed for PR #{deployment.PrNumber}", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to post teardown comment for app {App} PR {Pr}",
                app.Name, deployment.PrNumber);
        }

        logger.LogInformation("Tore down preview for app {App} PR {Pr}", app.Name, deployment.PrNumber);
    }
}
