using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PreviewDeploy.Server.Auth;
using PreviewDeploy.Server.Data;
using PreviewDeploy.Server.Endpoints;
using Shouldly;

namespace PreviewDeploy.Server.Tests;

public sealed class DeployEventsTests : IAsyncLifetime
{
    private const string AppName = "demo";
    private const string AppToken = "top-secret";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private TestAppFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestAppFactory(
            configureHost: builder => builder
                .UseSetting("Seed:Name", AppName)
                .UseSetting("Seed:Owner", "acme")
                .UseSetting("Seed:Repo", "widgets")
                .UseSetting("Seed:Token", AppToken)
                .UseSetting("Routing:BaseHost", "test.ts.net"));

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        await db.Apps.Where(a => a.Name == AppName)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.Port, 8080));
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Post_WithoutToken_ReturnsUnauthorized()
    {
        var response = await PostEventAsync(CreateRequest("create"), token: null);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_WithWrongToken_ReturnsUnauthorized()
    {
        var response = await PostEventAsync(CreateRequest("create"), token: "wrong-token");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_WithUnknownApp_ReturnsUnauthorized()
    {
        var response = await PostEventAsync(CreateRequest("create") with { App = "ghost" }, token: AppToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_WithInvalidAction_ReturnsBadRequest()
    {
        var response = await PostEventAsync(CreateRequest("explode"), token: AppToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("create", 0, "0123456789abcdef0123456789abcdef01234567")]
    [InlineData("create", 12, "not-a-sha")]
    [InlineData("create", 12, "")]
    public async Task Post_WithMalformedRequest_ReturnsBadRequest(string action, int pr, string sha)
    {
        var response = await PostEventAsync(new DeployEventRequest(AppName, pr, sha, action), token: AppToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_TeardownWithoutSha_IsAccepted()
    {
        var response = await PostEventAsync(new DeployEventRequest(AppName, 12, "", "teardown"), token: AppToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Post_WithValidToken_AcceptsAndRecordsEvent()
    {
        var response = await PostEventAsync(CreateRequest("create"), token: AppToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<PreviewDeployDbContext>();
        var @event = await db.DeployEvents.SingleAsync();
        (await db.Apps.SingleAsync(a => a.Id == @event.AppId)).Name.ShouldBe(AppName);
        @event.PrNumber.ShouldBe(12);
        @event.Sha.ShouldBe(Sha);
        @event.Action.ShouldBe("create");
    }

    private async Task<HttpResponseMessage> PostEventAsync(DeployEventRequest request, string? token)
    {
        var client = _factory.CreateClient();
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/deploy-events")
        {
            Content = JsonContent.Create(request),
        };
        if (token is not null)
        {
            httpRequest.Headers.Authorization = new("Bearer", token);
        }

        return await client.SendAsync(httpRequest);
    }

    private static DeployEventRequest CreateRequest(string action) =>
        new(AppName, 12, Sha, action);
}
