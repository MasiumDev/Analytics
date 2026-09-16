using System.Net;
using System.Text;
using System.Text.Json;
using Analytics.Api.InstagramIntegration;
using Analytics.Api.InstagramIntegration.Application;
using Analytics.Api.InstagramIntegration.Infrastructure;
using Microsoft.Extensions.Options;

namespace Analytics.Api.UnitTests;

public sealed class InstagramOAuthClientTests
{
    [Fact]
    public async Task ExchangeCode_PostsServerSideFormAndParsesTokenWithoutSerializingIt()
    {
        string? requestBody = null;
        HttpMethod? requestMethod = null;
        Uri? requestUri = null;
        var handler = new RecordingHandler(async request =>
        {
            requestMethod = request.Method;
            requestUri = request.RequestUri;
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "access_token": "server-only-access-credential",
                      "user_id": 17841400000000000,
                      "permissions": [
                        "instagram_business_basic",
                        "instagram_business_manage_insights"
                      ],
                      "expires_in": 3600
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        using var httpClient = new HttpClient(handler);
        var now = new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
        var client = new InstagramOAuthClient(
            httpClient,
            Options.Create(CreateOptions()),
            new FixedTimeProvider(now));

        var result = await client.ExchangeCodeAsync("one-time-code", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, requestMethod);
        Assert.Equal(new Uri("https://api.instagram.com/oauth/access_token"), requestUri);
        Assert.Contains("client_secret=server-only-app-secret", requestBody, StringComparison.Ordinal);
        Assert.Contains("code=one-time-code", requestBody, StringComparison.Ordinal);
        Assert.Equal("server-only-access-credential", result.AccessToken);
        Assert.Equal("17841400000000000", result.InstagramUserId);
        Assert.Equal(now.AddHours(1), result.ExpiresAtUtc);
        Assert.DoesNotContain(
            result.AccessToken,
            JsonSerializer.Serialize(result),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderFailure_DoesNotIncludeProviderBodyInException()
    {
        const string providerDetail = "provider-private-error-detail";
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(providerDetail),
            }));
        using var httpClient = new HttpClient(handler);
        var client = new InstagramOAuthClient(
            httpClient,
            Options.Create(CreateOptions()),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        var exception = await Assert.ThrowsAsync<InstagramOAuthException>(() =>
            client.ExchangeCodeAsync("rejected-code", CancellationToken.None));

        Assert.DoesNotContain(providerDetail, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-app-secret", exception.ToString(), StringComparison.Ordinal);
    }

    private static InstagramIntegrationOptions CreateOptions() => new()
    {
        Enabled = true,
        AppId = "test-app-id",
        AppSecret = "server-only-app-secret",
        OAuthRedirectUri = "https://api.example.com/api/integrations/instagram/callback",
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
