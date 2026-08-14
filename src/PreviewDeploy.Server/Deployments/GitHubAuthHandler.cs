using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using PreviewDeploy.Server.Options;

namespace PreviewDeploy.Server.Deployments;

public sealed class GitHubAuthHandler(IOptions<GitHubOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);
        return base.SendAsync(request, cancellationToken);
    }
}
