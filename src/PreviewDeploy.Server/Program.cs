using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using PreviewDeploy.Server.Auth;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Deployments;
using PreviewDeploy.Server.Endpoints;
using PreviewDeploy.Server.Options;
using PreviewDeploy.Server.Routing;
using PreviewDeploy.Server.Seeding;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

var databaseOptions = new DatabaseOptions();
builder.Configuration.GetSection(DatabaseOptions.SectionName).Bind(databaseOptions);
var dataDirFromEnv = Environment.GetEnvironmentVariable("DATA_DIR");
if (!string.IsNullOrWhiteSpace(dataDirFromEnv))
{
    databaseOptions.DataDirectory = dataDirFromEnv;
}
Directory.CreateDirectory(databaseOptions.DataDirectory);

builder.Services.AddDbContext<PreviewDeployDbContext>(options =>
    options.UseSqlite(databaseOptions.ResolveConnectionString()));

var certsOptions = new CertsOptions();
builder.Configuration.GetSection(CertsOptions.SectionName).Bind(certsOptions);
ConfigureHttps(builder.WebHost, certsOptions);

builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection(GitHubOptions.SectionName));
builder.Services.Configure<RoutingOptions>(builder.Configuration.GetSection(RoutingOptions.SectionName));
builder.Services.Configure<DockerOptions>(builder.Configuration.GetSection(DockerOptions.SectionName));
builder.Services.AddScoped<SeedAppService>();
builder.Services.AddScoped<AppTokenValidator>();
builder.Services.AddSingleton(Channel.CreateUnbounded<DeployEventRequest>(new UnboundedChannelOptions
{
    SingleReader = true,
}));
builder.Services.AddSingleton<IGitCloner, GitCloner>();
builder.Services.AddSingleton<IContainerRuntime, DockerContainerRuntime>();
builder.Services.AddHttpClient<IGitHubCommentClient, GitHubCommentClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("preview-deploy");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
}).AddHttpMessageHandler<GitHubAuthHandler>();
builder.Services.AddTransient<GitHubAuthHandler>();
builder.Services.AddSingleton<PreviewProxyConfigProvider>();
builder.Services.AddSingleton<IProxyConfigProvider>(
    sp => sp.GetRequiredService<PreviewProxyConfigProvider>());
builder.Services.AddSingleton<IPreviewRoutingConfig>(
    sp => sp.GetRequiredService<PreviewProxyConfigProvider>());
builder.Services.AddReverseProxy();
builder.Services.AddHostedService<DeployEventProcessor>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PreviewDeployDbContext>(
        "sqlite",
        tags: ["database"]);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
    db.Database.Migrate();
    await scope.ServiceProvider.GetRequiredService<SeedAppService>().RunAsync(CancellationToken.None);
}

var dockerOptions = new DockerOptions();
builder.Configuration.GetSection(DockerOptions.SectionName).Bind(dockerOptions);
if (dockerOptions.AttachNetworkOnStartup)
{
    try
    {
        var runtime = app.Services.GetRequiredService<IContainerRuntime>();
        await runtime.AttachToNetworkAsync(dockerOptions.SelfContainerName, CancellationToken.None);
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup")
            .LogWarning(ex, "Failed to attach {Container} to {Network}; preview routes will be unreachable",
                dockerOptions.SelfContainerName, dockerOptions.NetworkName);
    }
}

app.MapHealthChecks("/health");
app.MapDeployEvents();
app.MapReverseProxy();

app.Run();

static void ConfigureHttps(ConfigureWebHostBuilder webHost, CertsOptions certsOptions)
{
    var previewCertPath = Path.Combine(certsOptions.Directory, certsOptions.PreviewCertificateFile);
    var previewKeyPath = Path.Combine(certsOptions.Directory, certsOptions.PreviewKeyFile);
    if (!File.Exists(previewCertPath) || !File.Exists(previewKeyPath))
    {
        return;
    }

    var apiCertPath = Path.Combine(certsOptions.Directory, certsOptions.ApiCertificateFile);
    var apiKeyPath = Path.Combine(certsOptions.Directory, certsOptions.ApiKeyFile);
    var hasApiCertificate = File.Exists(apiCertPath) && File.Exists(apiKeyPath);

    webHost.ConfigureKestrel(serverOptions =>
        serverOptions.ListenAnyIP(443, listenOptions =>
            listenOptions.UseHttps(httpsOptions =>
                httpsOptions.ServerCertificateSelector = (_, hostName) =>
                {
                    if (hasApiCertificate && hostName is not null &&
                        !hostName.StartsWith("pr-", StringComparison.Ordinal))
                    {
                        return CertificateLoader.Load(apiCertPath, apiKeyPath);
                    }

                    return CertificateLoader.Load(previewCertPath, previewKeyPath);
                })));
}

public partial class Program;
