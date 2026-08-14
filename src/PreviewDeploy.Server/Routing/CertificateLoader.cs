using System.Security.Cryptography.X509Certificates;

namespace PreviewDeploy.Server.Routing;

public static class CertificateLoader
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (DateTime LastWriteTimeUtc, X509Certificate2 Certificate)> Cache = new();

    public static X509Certificate2 Load(string certificatePath, string keyPath)
    {
        lock (Gate)
        {
            var lastWriteTime = File.GetLastWriteTimeUtc(certificatePath);
            if (Cache.TryGetValue(certificatePath, out var cached) && cached.LastWriteTimeUtc == lastWriteTime)
            {
                return cached.Certificate;
            }

            var certificate = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
            Cache[certificatePath] = (lastWriteTime, certificate);
            return certificate;
        }
    }
}
