using System.Security.Cryptography;
using System.Text;
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

        var candidateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bearerToken)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidateHash),
            Encoding.UTF8.GetBytes(app.TokenHash))
            ? app
            : null;
    }
}
