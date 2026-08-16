using System.Net;
using System.Text.Json;
using NSubstitute;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Deployments;
using Shouldly;

namespace PreviewDeploy.Server.Tests;

public sealed class GitHubCommentClientTests
{
    private const string Marker = "<!-- preview-deploy -->";

    [Fact]
    public async Task Upsert_CreatesCommentWithMarker_WhenNoStickyCommentExists()
    {
        var handler = CreateHandler("[]");
        var client = CreateClient(handler);

        await client.UpsertAsync(CreateApp(), 12, "Deploying preview for PR #12...", CancellationToken.None);

        var post = SentRequest(handler, HttpMethod.Post);
        post.RequestUri!.AbsolutePath.ShouldBe("/repos/acme/widgets/issues/12/comments");
        var body = JsonSerializer.Deserialize<JsonElement>(await post.Content!.ReadAsStringAsync());
        body.GetProperty("body").GetString().ShouldBe($"Deploying preview for PR #12...\n\n{Marker}");
        SentRequests(handler).ShouldNotContain(r => r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingStickyComment_WhenFound()
    {
        var handler = CreateHandler("""[{"id": 42, "body": "old status\n\n<!-- preview-deploy -->"}]""");
        var client = CreateClient(handler);

        await client.UpsertAsync(CreateApp(), 12, "Preview ready at https://pr-12-demo.test.ts.net", CancellationToken.None);

        var patch = SentRequest(handler, HttpMethod.Patch);
        patch.RequestUri!.AbsolutePath.ShouldBe("/repos/acme/widgets/issues/comments/42");
        var body = JsonSerializer.Deserialize<JsonElement>(await patch.Content!.ReadAsStringAsync());
        body.GetProperty("body").GetString()
            .ShouldBe($"Preview ready at https://pr-12-demo.test.ts.net\n\n{Marker}");
        SentRequests(handler).ShouldNotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Upsert_IgnoresOtherCommentsOnTheIssue()
    {
        var handler = CreateHandler(
            """[{"id": 1, "body": "nice work"}, {"id": 2, "body": "second look"}, {"id": 3, "body": "the sticky one\n\n<!-- preview-deploy -->"}]""");
        var client = CreateClient(handler);

        await client.UpsertAsync(CreateApp(), 12, "new status", CancellationToken.None);

        var patch = SentRequest(handler, HttpMethod.Patch);
        patch.RequestUri!.AbsolutePath.ShouldBe("/repos/acme/widgets/issues/comments/3");
    }

    private static MockHttpMessageHandler CreateHandler(string commentsJson)
    {
        var handler = Substitute.For<MockHttpMessageHandler>();
        handler.MockSend(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(commentsJson),
            });
        return handler;
    }

    private static GitHubCommentClient CreateClient(MockHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

    private static List<HttpRequestMessage> SentRequests(MockHttpMessageHandler handler) =>
        handler.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .Cast<HttpRequestMessage>()
            .ToList();

    private static HttpRequestMessage SentRequest(MockHttpMessageHandler handler, HttpMethod method)
    {
        var request = SentRequests(handler).Single(r => r.Method == method);
        request.ShouldNotBeNull();
        return request;
    }

    private static App CreateApp() => new()
    {
        Id = 1,
        Name = "demo",
        Owner = "acme",
        Repo = "widgets",
        TokenHash = "x",
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };
}