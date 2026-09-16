using System.Net;
using System.Text;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramCredentials.Infrastructure;
using Analytics.Api.InstagramIntegration;
using Microsoft.Extensions.Options;

namespace Analytics.Api.UnitTests;

public sealed class InstagramTokenLifecycleClientTests
{
    [Fact]
    public async Task ValidateAndRevoke_UseBearerHeaderWithoutPuttingTokenInUri()
    {
        const string accessToken = "lifecycle-server-only-credential";
        var requests = new List<(HttpMethod Method, Uri Uri, string? Authorization)>();
        var handler = new LifecycleHandler(request =>
        {
            requests.Add((
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString()));
            return request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, """{"user_id":"17841400000000000"}""")
                : Json(HttpStatusCode.OK, """{"success":true}""");
        });
        using var httpClient = new HttpClient(handler);
        var client = new InstagramTokenLifecycleClient(
            httpClient,
            Options.Create(new InstagramIntegrationOptions()));

        var inspection = await client.ValidateAsync(accessToken, CancellationToken.None);
        var revoked = await client.RevokeAsync(accessToken, CancellationToken.None);

        Assert.Equal(InstagramProviderTokenStatus.Active, inspection.Status);
        Assert.Equal("17841400000000000", inspection.InstagramUserId);
        Assert.True(revoked);
        Assert.Equal([HttpMethod.Get, HttpMethod.Delete], requests.Select(item => item.Method));
        Assert.All(
            requests,
            request =>
            {
                Assert.Equal($"Bearer {accessToken}", request.Authorization);
                Assert.DoesNotContain(
                    accessToken,
                    request.Uri.AbsoluteUri,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task ProviderErrors_ReturnSafeStatusOrSafeException()
    {
        const string accessToken = "lifecycle-private-credential";
        const string providerDetail = "private-provider-error-detail";
        using var revokedHttpClient = new HttpClient(new LifecycleHandler(_ =>
            Json(HttpStatusCode.Unauthorized, providerDetail)));
        var revokedClient = new InstagramTokenLifecycleClient(
            revokedHttpClient,
            Options.Create(new InstagramIntegrationOptions()));

        var revoked = await revokedClient.ValidateAsync(
            accessToken,
            CancellationToken.None);

        Assert.Equal(InstagramProviderTokenStatus.Revoked, revoked.Status);

        using var failedHttpClient = new HttpClient(new LifecycleHandler(_ =>
            Json(HttpStatusCode.BadGateway, providerDetail)));
        var failedClient = new InstagramTokenLifecycleClient(
            failedHttpClient,
            Options.Create(new InstagramIntegrationOptions()));
        var exception = await Assert.ThrowsAsync<InstagramTokenLifecycleException>(() =>
            failedClient.ValidateAsync(accessToken, CancellationToken.None));

        Assert.DoesNotContain(providerDetail, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, exception.ToString(), StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class LifecycleHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
