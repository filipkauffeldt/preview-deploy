using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Deployments;
using PreviewDeploy.Server.Options;
using Yarp.ReverseProxy.Configuration;

namespace PreviewDeploy.Server.Routing;

public interface IPreviewRoutingConfig
{
    void NotifyChanged();
}

public sealed class PreviewProxyConfigProvider(
    IServiceScopeFactory scopeFactory,
    IOptions<RoutingOptions> routingOptions,
    ILogger<PreviewProxyConfigProvider> logger)
    : IProxyConfigProvider, IPreviewRoutingConfig
{
    private readonly object _gate = new();
    private volatile PreviewProxyConfig _config = new([], [], loaded: false);

    public IProxyConfig GetConfig()
    {
        if (_config.Loaded)
        {
            return _config;
        }

        lock (_gate)
        {
            if (!_config.Loaded)
            {
                _config = LoadConfig();
            }
        }

        return _config;
    }

    public void NotifyChanged()
    {
        lock (_gate)
        {
            _config = LoadConfig();
        }

        _config.SignalChange();
    }

    private PreviewProxyConfig LoadConfig()
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
            var deployments = db.Deployments
                .Include(deployment => deployment.App)
                .Where(deployment => deployment.Status == DeploymentStatus.Running)
                .ToList();

            foreach (var deployment in deployments)
            {
                var id = $"preview-{deployment.AppId}-pr{deployment.PrNumber}";
                var subdomain = DeploymentNames.Subdomain(deployment.App.Name, deployment.PrNumber);
                var host = $"{subdomain}.{routingOptions.Value.BaseHost}";
                var containerName = DeploymentNames.ContainerName(deployment.App.Name, deployment.PrNumber);

                routes.Add(new RouteConfig
                {
                    RouteId = id,
                    ClusterId = id,
                    Match = new RouteMatch { Hosts = [host] },
                });
                clusters.Add(new ClusterConfig
                {
                    ClusterId = id,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        [id] = new() { Address = $"http://{containerName}:{deployment.Port}" },
                    },
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load preview routes from the database; no preview routes active");
        }

        return new PreviewProxyConfig(routes, clusters, loaded: true);
    }
}

public sealed class PreviewProxyConfig(
    IReadOnlyList<RouteConfig> routes,
    IReadOnlyList<ClusterConfig> clusters,
    bool loaded)
    : IProxyConfig
{
    private readonly CancellationTokenSource _changeTokenSource = new();

    public IReadOnlyList<RouteConfig> Routes => routes;
    public IReadOnlyList<ClusterConfig> Clusters => clusters;
    public bool Loaded => loaded;
    public IChangeToken ChangeToken => new CancellationChangeToken(_changeTokenSource.Token);

    public void SignalChange() => _changeTokenSource.Cancel();
}
