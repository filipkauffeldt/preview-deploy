using System.Text.Json;
using System.Text.Json.Serialization;
using PreviewDeploy.Server.Data;

namespace PreviewDeploy.Server.Deployments;

public interface IGitHubCommentClient
{
    Task UpsertAsync(App app, int prNumber, string body, CancellationToken cancellationToken);
}

public sealed class GitHubCommentClient(HttpClient httpClient) : IGitHubCommentClient
{
    private const string Marker = "<!-- preview-deploy -->";

    public async Task UpsertAsync(App app, int prNumber, string body, CancellationToken cancellationToken)
    {
        var commentsUrl = $"repos/{app.Owner}/{app.Repo}/issues/{prNumber}/comments";
        var existing = await FindExistingCommentAsync(commentsUrl, cancellationToken);

        var request = existing is null
            ? new HttpRequestMessage(HttpMethod.Post, commentsUrl)
            : new HttpRequestMessage(HttpMethod.Patch, $"repos/{app.Owner}/{app.Repo}/issues/comments/{existing.Id}");

        request.Content = JsonContent.Create(new CommentBody($"{body}\n\n{Marker}"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"GitHub comment request to {request.RequestUri} failed with {response.StatusCode}: {error}");
        }
    }

    private async Task<Comment?> FindExistingCommentAsync(string commentsUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{commentsUrl}?per_page=100");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"GitHub comment list request to {request.RequestUri} failed with {response.StatusCode}: {error}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var comments = await JsonSerializer.DeserializeAsync<List<Comment>>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        return comments?.FirstOrDefault(comment => comment.Body?.Contains(Marker, StringComparison.Ordinal) == true);
    }

    private sealed record Comment(long Id, string? Body);

    private sealed record CommentBody([property: JsonPropertyName("body")] string Body);
}
