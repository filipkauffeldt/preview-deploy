using System.Security.Cryptography;
using System.Text;

namespace PreviewDeploy.Server.Auth;

public static class AppToken
{
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
