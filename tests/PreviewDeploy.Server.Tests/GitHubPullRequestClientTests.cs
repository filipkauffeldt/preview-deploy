using System.Net;
using PreviewDeploy.Server.Deployments;

namespace PreviewDeploy.Server.Tests;

public sealed class GitHubPullRequestClientTests
{
    [Fact]
    public async Task IsClosed_ReturnsTrue_WhenPullRequestStateIsClosed()
    {
        var recorder = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"state": "closed"}"""),
            },
        };
        var client = CreateClient(recorder);

        var closed = await client.IsClosedAsync("acme", "widgets", 12, CancellationToken.None);

        Assert.True(closed);
        var request = Assert.Single(recorder.Requests);
        Assert.Equal("/repos/acme/widgets/pulls/12", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task IsClosed_ReturnsFalse_WhenPullRequestIsOpen()
    {
        var recorder = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"state": "open"}"""),
            },
        };
        var client = CreateClient(recorder);

        var closed = await client.IsClosedAsync("acme", "widgets", 12, CancellationToken.None);

        Assert.False(closed);
    }

    [Fact]
    public async Task IsClosed_Throws_WhenGitHubReturnsAnError()
    {
        var recorder = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        };
        var client = CreateClient(recorder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.IsClosedAsync("acme", "widgets", 12, CancellationToken.None));
    }

    private static GitHubPullRequestClient CreateClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; init; } = _ =>
            new HttpResponseMessage(HttpStatusCode.OK);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Responder(request));
        }
    }
}