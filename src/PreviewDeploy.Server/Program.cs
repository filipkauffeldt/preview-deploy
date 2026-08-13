using Microsoft.EntityFrameworkCore;
using PreviewDeploy.Server.Data;

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

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PreviewDeployDbContext>(
        "sqlite",
        tags: ["database"]);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
    db.Database.Migrate();
}

app.MapHealthChecks("/health");

app.Run();

public partial class Program;
