namespace PreviewDeploy.Server.Tests;

/// <summary>
/// NSubstitute cannot intercept the protected <see cref="HttpMessageHandler.SendAsync"/>,
/// so tests substitute <see cref="MockSend"/> instead.
/// </summary>
public abstract class MockHttpMessageHandler : HttpMessageHandler
{
    protected sealed override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        MockSend(request, cancellationToken);

    public abstract Task<HttpResponseMessage> MockSend(
        HttpRequestMessage request, CancellationToken cancellationToken);
}