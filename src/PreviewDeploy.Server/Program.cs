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

app.MapHealthChecks("/health");
app.MapDeployEvents();
app.MapReverseProxy();

app.Run();

public partial class Program;
