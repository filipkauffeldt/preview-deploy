using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PreviewDeploy.Server.Data;

namespace PreviewDeploy.Server.Auth;

public sealed class AppTokenValidator(PreviewDeployDbContext db)
{
    public async Task<App?> ValidateAsync(string? bearerToken, string appName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(bearerToken))
        {
            return null;
        }

        var app = await db.Apps.SingleOrDefaultAsync(a => a.Name == appName, cancellationToken);
        if (app is null || app.TokenHash.Length == 0)
        {
            return null;
        }

        var candidate = Convert.FromHexString(AppToken.Hash(bearerToken));
        var stored = Convert.FromHexString(app.TokenHash);
        return CryptographicOperations.FixedTimeEquals(candidate, stored)
            ? app
            : null;
    }
}
