using System.Net;
using NSubstitute;
using PreviewDeploy.Server.Deployments;
using Shouldly;

namespace PreviewDeploy.Server.Tests;

public sealed class GitHubPullRequestClientTests
{
    [Fact]
    public async Task IsClosed_ReturnsTrue_WhenPullRequestStateIsClosed()
    {
        var handler = CreateHandler("""{"state": "closed"}""");
        var client = CreateClient(handler);

        var closed = await client.IsClosedAsync("acme", "widgets", 12, CancellationToken.None);

        closed.ShouldBeTrue();
        var request = SentRequest(handler);
        request.RequestUri!.AbsolutePath.ShouldBe("/repos/acme/widgets/pulls/12");
    }

    [Fact]
    public async Task IsClosed_ReturnsFalse_WhenPullRequestIsOpen()
    {
        var handler = CreateHandler("""{"state": "open"}""");
        var client = CreateClient(handler);

        var closed = await client.IsClosedAsync("acme", "widgets", 12, CancellationToken.None);

        closed.ShouldBeFalse();
    }

    [Fact]
    public async Task IsClosed_Throws_WhenGitHubReturnsAnError()
    {
        var handler = Substitute.For<MockHttpMessageHandler>();
        handler.MockSend(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        await Should.ThrowAsync<InvalidOperationException>(
            () => client.IsClosedAsync("acme", "widgets", 12, CancellationToken.None));
    }

    private static MockHttpMessageHandler CreateHandler(string stateJson)
    {
        var handler = Substitute.For<MockHttpMessageHandler>();
        handler.MockSend(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(stateJson),
            });
        return handler;
    }

    private static GitHubPullRequestClient CreateClient(MockHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

    private static HttpRequestMessage SentRequest(MockHttpMessageHandler handler)
    {
        var request = handler.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .Cast<HttpRequestMessage>()
            .ShouldHaveSingleItem();
        return request;
    }
}