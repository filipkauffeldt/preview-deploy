using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Endpoints;
using PreviewDeploy.Server.Options;

namespace PreviewDeploy.Server.Deployments;

public sealed class DeployEventProcessor(
    Channel<DeployEventRequest> queue,
    IServiceScopeFactory scopeFactory,
    IContainerRuntime containers,
    IGitCloner cloner,
    IOptions<GitHubOptions> gitHubOptions,
    IOptions<RoutingOptions> routingOptions,
    IOptions<DockerOptions> dockerOptions,
    ILogger<DeployEventProcessor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await HandleAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Deploy event for app {App} PR {Pr} failed", request.App, request.Pr);
            }
        }
    }

    private async Task HandleAsync(DeployEventRequest request, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        var app = await db.Apps.SingleOrDefaultAsync(a => a.Name == request.App, cancellationToken);
        if (app is null)
        {
            logger.LogWarning("Ignoring deploy event for unknown app {App}", request.App);
            return;
        }

        var deployment = await db.Deployments
            .SingleOrDefaultAsync(d => d.AppId == app.Id && d.PrNumber == request.Pr, cancellationToken);
        if (deployment is null)
        {
            deployment = new Deployment
            {
                AppId = app.Id,
                PrNumber = request.Pr,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Deployments.Add(deployment);
        }

        if (request.Action == DeployEventsEndpoint.TeardownAction)
        {
            await TeardownAsync(db, app, deployment, cancellationToken);
            return;
        }

        deployment.Status = DeploymentStatus.Building;
        deployment.Sha = request.Sha;
        deployment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var cloneDirectory = await cloner.CloneAsync(
            app.Owner,
            app.Repo,
            request.Pr,
            request.Sha,
            gitHubOptions.Value.Token,
            dockerOptions.Value.WorkDirectory,
            cancellationToken);

        try
        {
            var imageTag = $"{app.Name}:pr{request.Pr}-{request.Sha[..Math.Min(request.Sha.Length, 7)]}";
            var (_, port) = await containers.DeployAsync(
                app.Name,
                request.Pr,
                request.Sha,
                imageTag,
                cloneDirectory,
                app.Port,
                cancellationToken);

            deployment.Status = DeploymentStatus.Running;
            deployment.Port = port;
            deployment.Url = BuildPreviewUrl(app.Name, request.Pr);
            deployment.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Preview for app {App} PR {Pr} is running at {Url} (sha {Sha})",
                app.Name, request.Pr, deployment.Url, request.Sha);
        }
        catch (Exception ex)
        {
            deployment.Status = DeploymentStatus.Failed;
            deployment.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogError(ex, "Deploying app {App} PR {Pr} (sha {Sha}) failed", app.Name, request.Pr, request.Sha);
        }
        finally
        {
            TryDeleteDirectory(cloneDirectory);
        }
    }

    private async Task TeardownAsync(
        PreviewDeployDbContext db,
        App app,
        Deployment deployment,
        CancellationToken cancellationToken)
    {
        await containers.StopAndRemoveAsync(
            DeploymentNames.ContainerName(app.Name, deployment.PrNumber), cancellationToken);

        deployment.Status = DeploymentStatus.Stopped;
        deployment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Tore down preview for app {App} PR {Pr}", app.Name, deployment.PrNumber);
    }

    private string BuildPreviewUrl(string appName, int prNumber) =>
        $"https://{DeploymentNames.Subdomain(appName, prNumber)}.{routingOptions.Value.BaseHost}";

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception)
        {
        }
    }
}
