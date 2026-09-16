using System.Net;
using System.Text;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramIntegration;
using Analytics.Api.InstagramIntegration.Application;
using Analytics.Api.InstagramIntegration.Infrastructure;
using Microsoft.Extensions.Options;

namespace Analytics.Api.UnitTests;

public sealed class InstagramAccountDiscoveryClientTests
{
    [Fact]
    public async Task Discover_UsesBearerHeaderAndReturnsProfessionalProfileWithGrantedScopes()
    {
        const string accessToken = "discovery-server-only-credential";
        var requestedUris = new List<Uri>();
        var authorizationValues = new List<string>();
        var handler = new DiscoveryHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);
            authorizationValues.Add(request.Headers.Authorization?.ToString() ?? string.Empty);

            var json = request.RequestUri!.AbsolutePath.EndsWith(
                "/permissions",
                StringComparison.Ordinal)
                ? """
                  {
                    "data": [
                      { "permission": "instagram_business_basic", "status": "granted" },
                      { "permission": "instagram_business_manage_insights", "status": "granted" },
                      { "permission": "ignored_scope", "status": "declined" }
                    ]
                  }
                  """
                : """
                  {
                    "user_id": "17841400000000000",
                    "username": "discovered_brand",
                    "name": "Discovered Brand",
                    "account_type": "BUSINESS"
                  }
                  """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });
        using var httpClient = new HttpClient(handler);
        var client = new InstagramAccountDiscoveryClient(
            httpClient,
            Options.Create(new InstagramIntegrationOptions()));

        var result = await client.DiscoverAsync(accessToken, CancellationToken.None);

        Assert.Equal("17841400000000000", result.InstagramUserId);
        Assert.Equal("discovered_brand", result.Username);
        Assert.Equal("Discovered Brand", result.DisplayName);
        Assert.Equal(InstagramProfessionalAccountType.Business, result.ProfessionalAccountType);
        Assert.Equal(2, result.GrantedScopes.Length);
        Assert.All(authorizationValues, value => Assert.Equal($"Bearer {accessToken}", value));
        Assert.All(
            requestedUris,
            uri => Assert.DoesNotContain(accessToken, uri.AbsoluteUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProviderFailure_DoesNotReflectProviderBodyOrToken()
    {
        const string accessToken = "discovery-server-only-credential";
        const string providerDetail = "provider-private-discovery-detail";
        var handler = new DiscoveryHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(providerDetail),
        });
        using var httpClient = new HttpClient(handler);
        var client = new InstagramAccountDiscoveryClient(
            httpClient,
            Options.Create(new InstagramIntegrationOptions()));

        var exception = await Assert.ThrowsAsync<InstagramDiscoveryException>(() =>
            client.DiscoverAsync(accessToken, CancellationToken.None));

        Assert.DoesNotContain(providerDetail, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, exception.ToString(), StringComparison.Ordinal);
    }

    private sealed class DiscoveryHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
