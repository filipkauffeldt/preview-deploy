using System.Formats.Tar;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PreviewDeploy.Server.Options;

namespace PreviewDeploy.Server.Deployments;

public interface IContainerRuntime
{
    Task<(string Name, int Port)> DeployAsync(
        string appName,
        int prNumber,
        string sha,
        string imageTag,
        string contextDirectory,
        int fallbackPort,
        CancellationToken cancellationToken);

    Task StopAndRemoveAsync(string containerName, CancellationToken cancellationToken);

    Task AttachToNetworkAsync(string containerName, CancellationToken cancellationToken);
}

public sealed class DockerContainerRuntime : IContainerRuntime
{
    private readonly DockerClient _client;
    private readonly DockerOptions _options;
    private readonly ILogger<DockerContainerRuntime> _logger;

    public DockerContainerRuntime(IOptions<DockerOptions> options, ILogger<DockerContainerRuntime> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new DockerClientConfiguration(new Uri($"unix://{_options.SocketPath}")).CreateClient();
    }

    public async Task<(string Name, int Port)> DeployAsync(
        string appName,
        int prNumber,
        string sha,
        string imageTag,
        string contextDirectory,
        int fallbackPort,
        CancellationToken cancellationToken)
    {
        var containerName = DeploymentNames.ContainerName(appName, prNumber);

        await BuildImageAsync(imageTag, contextDirectory, cancellationToken);
        var port = await ResolvePortAsync(imageTag, fallbackPort, cancellationToken);

        await TryStopAndRemoveAsync(containerName, cancellationToken);

        var created = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = containerName,
            Image = imageTag,
            Labels = new Dictionary<string, string>
            {
                ["app"] = appName,
                ["pr"] = prNumber.ToString(),
                ["sha"] = sha,
            },
            NetworkingConfig = new NetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, EndpointSettings>
                {
                    [_options.NetworkName] = new EndpointSettings(),
                },
            },
        }, cancellationToken);
        await _client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);

        _logger.LogInformation("Started preview container {ContainerName} ({ImageTag}) on {Network}",
            containerName, imageTag, _options.NetworkName);

        return (containerName, port);
    }

    public async Task StopAndRemoveAsync(string containerName, CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(containerName, cancellationToken);
        if (container is null)
        {
            return;
        }

        await _client.Containers.StopContainerAsync(
            container.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 10 }, cancellationToken);
        await _client.Containers.RemoveContainerAsync(
            container.ID, new ContainerRemoveParameters { Force = true }, cancellationToken);

        _logger.LogInformation("Stopped and removed preview container {ContainerName}", containerName);
    }

    public async Task AttachToNetworkAsync(string containerName, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Networks.ConnectNetworkAsync(
                _options.NetworkName,
                new NetworkConnectParameters { Container = containerName },
                cancellationToken);
            _logger.LogInformation("Attached {ContainerName} to network {Network}", containerName, _options.NetworkName);
        }
        catch (DockerApiException ex) when (
            ex.Message.Contains("already exists in network", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "{ContainerName} is already attached to network {Network}", containerName, _options.NetworkName);
        }
    }

    private async Task BuildImageAsync(string imageTag, string contextDirectory, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Building image {ImageTag} from {ContextDirectory}", imageTag, contextDirectory);
        using var context = CreateBuildContextTar(contextDirectory);
        var progress = new Progress<JSONMessage>(message =>
        {
            if (message.ErrorMessage is not null)
            {
                _logger.LogWarning("Docker build for {ImageTag}: {Error}", imageTag, message.ErrorMessage);
            }
        });
        await _client.Images.BuildImageFromDockerfileAsync(
            new ImageBuildParameters { Tags = [imageTag], Remove = true },
            context,
            authConfigs: null,
            headers: null,
            progress,
            cancellationToken);
    }

    private static Stream CreateBuildContextTar(string directory)
    {
        var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/');
                var entry = new UstarTarEntry(TarEntryType.RegularFile, relativePath)
                {
                    DataStream = File.OpenRead(file),
                };
                writer.WriteEntry(entry);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private async Task<int> ResolvePortAsync(string imageTag, int fallbackPort, CancellationToken cancellationToken)
    {
        var image = await _client.Images.InspectImageAsync(imageTag, cancellationToken);
        var exposedPorts = image.Config?.ExposedPorts?.Keys
            .Select(TryParsePort)
            .Where(port => port > 0)
            .ToList() ?? [];

        return exposedPorts.Count == 1 ? exposedPorts[0] : fallbackPort;
    }

    private static int TryParsePort(string exposedPort)
    {
        var slash = exposedPort.IndexOf('/');
        var portPart = slash >= 0 ? exposedPort[..slash] : exposedPort;
        return int.TryParse(portPart, out var port) ? port : 0;
    }

    private async Task TryStopAndRemoveAsync(string containerName, CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(containerName, cancellationToken);
        if (container is null)
        {
            return;
        }

        await _client.Containers.StopContainerAsync(
            container.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 10 }, cancellationToken);
        await _client.Containers.RemoveContainerAsync(
            container.ID, new ContainerRemoveParameters { Force = true }, cancellationToken);
    }

    private async Task<ContainerListResponse?> FindContainerAsync(string containerName, CancellationToken cancellationToken)
    {
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true }, cancellationToken);
        return containers.FirstOrDefault(
            container => container.Names.Contains("/" + containerName, StringComparer.Ordinal));
    }
}
