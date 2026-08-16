using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PreviewDeploy.Server.Auth;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Options;

namespace PreviewDeploy.Server.Seeding;

public sealed class SeedAppService(PreviewDeployDbContext db, IOptions<SeedOptions> options)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;
        if (!seed.IsConfigured)
        {
            return;
        }

        var app = await db.Apps.SingleOrDefaultAsync(a => a.Name == seed.Name, cancellationToken);
        if (app is null)
        {
            app = new App
            {
                Name = seed.Name,
                Owner = seed.Owner,
                Repo = seed.Repo,
                TokenHash = AppToken.Hash(seed.Token),
                TtlDays = seed.TtlDays,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Apps.Add(app);
        }
        else
        {
            app.Owner = seed.Owner;
            app.Repo = seed.Repo;
            app.TokenHash = AppToken.Hash(seed.Token);
            app.TtlDays = seed.TtlDays;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
