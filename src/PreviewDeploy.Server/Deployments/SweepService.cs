using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Options;

namespace PreviewDeploy.Server.Deployments;

public sealed class SweepService(
    IServiceScopeFactory scopeFactory,
    IContainerRuntime containers,
    IGitHubPullRequestClient pullRequests,
    IOptions<SweepOptions> options,
    ILogger<SweepService> logger)
    : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        var teardown = scope.ServiceProvider.GetRequiredService<DeploymentTeardownService>();

        var deployments = await db.Deployments
            .Include(d => d.App)
            .Where(d => d.Status == DeploymentStatus.Running)
            .ToListAsync(cancellationToken);

        foreach (var deployment in deployments)
        {
            var app = deployment.App;
            var reason = await TeardownReasonAsync(app, deployment, cancellationToken);
            if (reason is null)
            {
                continue;
            }

            logger.LogInformation("Sweep: tearing down app {App} PR {Pr}: {Reason}",
                app.Name, deployment.PrNumber, reason);
            await teardown.TeardownAsync(app, deployment, cancellationToken);
        }

        await TryPruneImagesAsync(cancellationToken);
    }

    private async Task<string?> TeardownReasonAsync(
        App app, Deployment deployment, CancellationToken cancellationToken)
    {
        if (TtlExpired(app, deployment))
        {
            return $"TTL exceeded ({app.TtlDays} days)";
        }

        try
        {
            return await pullRequests.IsClosedAsync(app.Owner, app.Repo, deployment.PrNumber, cancellationToken)
                ? "PR is closed or merged"
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sweep: could not check PR {Pr} for app {App}; leaving deployment alone",
                deployment.PrNumber, app.Name);
            return null;
        }
    }

    private static bool TtlExpired(App app, Deployment deployment) =>
        deployment.UpdatedAtUtc < DateTimeOffset.UtcNow - TimeSpan.FromDays(Math.Max(app.TtlDays, 0));

    private async Task TryPruneImagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await containers.PruneImagesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sweep: image pruning failed");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);
        while (true)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sweep run failed");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
