using System.Net;
using System.Text.Json;

namespace PreviewDeploy.Server.Deployments;

public interface IGitHubPullRequestClient
{
    /// <summary>Returns true when the PR is closed or merged, false when still open.</summary>
    Task<bool> IsClosedAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken);
}

public sealed class GitHubPullRequestClient(HttpClient httpClient) : IGitHubPullRequestClient
{
    public async Task<bool> IsClosedAsync(
        string owner, string repo, int prNumber, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{owner}/{repo}/pulls/{prNumber}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"GitHub pull request request to {request.RequestUri} failed with {response.StatusCode}: {error}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var pullRequest = await JsonSerializer.DeserializeAsync<PullRequest>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        return pullRequest?.State is not null &&
               pullRequest.State.Equals("closed", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record PullRequest(string? State);
}
