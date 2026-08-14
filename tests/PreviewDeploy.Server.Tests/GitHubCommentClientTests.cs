using System.Net;
using System.Text.Json;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Deployments;

namespace PreviewDeploy.Server.Tests;

public sealed class GitHubCommentClientTests
{
    private const string Marker = "<!-- preview-deploy -->";

    [Fact]
    public async Task Upsert_CreatesCommentWithMarker_WhenNoStickyCommentExists()
    {
        var recorder = new StubHandler
        {
            Responder = request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]"),
            },
        };
        var client = CreateClient(recorder);

        await client.UpsertAsync(CreateApp(), 12, "Deploying preview for PR #12...", CancellationToken.None);

        var post = recorder.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("/repos/acme/widgets/issues/12/comments", post.RequestUri!.AbsolutePath);
        var body = JsonSerializer.Deserialize<JsonElement>(await post.Content!.ReadAsStringAsync());
        Assert.Equal($"Deploying preview for PR #12...\n\n{Marker}", body.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Upsert_UpdatesExistingStickyComment_WhenFound()
    {
        var recorder = new StubHandler
        {
            Responder = request => request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """[{"id": 42, "body": "old status\n\n<!-- preview-deploy -->"}]"""),
                }
                : new HttpResponseMessage(HttpStatusCode.OK),
        };
        var client = CreateClient(recorder);

        await client.UpsertAsync(CreateApp(), 12, "Preview ready at https://pr-12-demo.test.ts.net", CancellationToken.None);

        var patch = recorder.Requests.Single(r => r.Method == HttpMethod.Patch);
        Assert.Equal("/repos/acme/widgets/issues/comments/42", patch.RequestUri!.AbsolutePath);
        var body = JsonSerializer.Deserialize<JsonElement>(await patch.Content!.ReadAsStringAsync());
        Assert.Equal(
            "Preview ready at https://pr-12-demo.test.ts.net\n\n<!-- preview-deploy -->",
            body.GetProperty("body").GetString());
        Assert.DoesNotContain(recorder.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Upsert_IgnoresOtherCommentsOnTheIssue()
    {
        var recorder = new StubHandler
        {
            Responder = request => request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """[{"id": 1, "body": "nice work"}, {"id": 2, "body": "second look"}, {"id": 3, "body": "the sticky one\n\n<!-- preview-deploy -->"}]"""),
                }
                : new HttpResponseMessage(HttpStatusCode.OK),
        };
        var client = CreateClient(recorder);

        await client.UpsertAsync(CreateApp(), 12, "new status", CancellationToken.None);

        var patch = recorder.Requests.Single(r => r.Method == HttpMethod.Patch);
        Assert.Equal("/repos/acme/widgets/issues/comments/3", patch.RequestUri!.AbsolutePath);
    }

    private static GitHubCommentClient CreateClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

    private static App CreateApp() => new()
    {
        Id = 1,
        Name = "demo",
        Owner = "acme",
        Repo = "widgets",
        TokenHash = "x",
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

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
