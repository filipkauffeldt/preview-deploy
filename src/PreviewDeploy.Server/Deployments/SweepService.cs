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

            var ttlExpired = deployment.UpdatedAtUtc <
                DateTimeOffset.UtcNow - TimeSpan.FromDays(Math.Max(app.TtlDays, 0));
            if (ttlExpired)
            {
                logger.LogInformation("Sweep: deployment for app {App} PR {Pr} exceeded TTL ({TtlDays} days)",
                    app.Name, deployment.PrNumber, app.TtlDays);
                await teardown.TeardownAsync(app, deployment, cancellationToken);
                continue;
            }

            try
            {
                if (!await pullRequests.IsClosedAsync(app.Owner, app.Repo, deployment.PrNumber, cancellationToken))
                {
                    continue;
                }

                logger.LogInformation("Sweep: PR {Pr} for app {App} is closed; tearing down",
                    deployment.PrNumber, app.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sweep: could not check PR {Pr} for app {App}; leaving deployment alone",
                    deployment.PrNumber, app.Name);
                continue;
            }

            await teardown.TeardownAsync(app, deployment, cancellationToken);
        }

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
