using System.Net;
using System.Text;
using Analytics.Api.InstagramIntegration;
using Analytics.Api.InstagramIntegration.Application;
using Analytics.Api.InstagramIntegration.Infrastructure;
using Microsoft.Extensions.Options;

namespace Analytics.Api.UnitTests;

public sealed class InstagramApiClientTests
{
    [Fact]
    public async Task GetAllPages_UsesBearerHeaderAndWalksUniqueCursors()
    {
        const string accessToken = "api-client-server-only-token";
        var requests = new List<HttpRequestSnapshot>();
        var handler = new RecordingHandler(request =>
        {
            var query = request.RequestUri!.Query;
            requests.Add(new HttpRequestSnapshot(
                request.RequestUri,
                request.Headers.Authorization?.ToString()));
            var response = query.Contains("after=cursor-1", StringComparison.Ordinal)
                ? Json("""{"data":[{"id":"2"}],"paging":{"cursors":{}}}""")
                : Json("""{"data":[{"id":"1"}],"paging":{"cursors":{"after":"cursor-1"}}}""");
            response.Headers.TryAddWithoutValidation("x-app-usage", "{\"call_count\":12}");
            return response;
        });
        var client = CreateClient(handler);

        var result = await client.GetAllPagesAsync<ApiItem>(
            "me/media",
            accessToken,
            new Dictionary<string, string?> { ["fields"] = "id" },
            CancellationToken.None);

        Assert.Equal(["1", "2"], result.Data.Select(item => item.Id));
        Assert.Equal(2, result.UsageByPage.Count);
        Assert.Equal("{\"call_count\":12}", result.UsageByPage[0].AppUsage);
        Assert.Equal(2, requests.Count);
        Assert.DoesNotContain("after=", requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.Contains("after=cursor-1", requests[1].Uri.Query, StringComparison.Ordinal);
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
    public async Task GetAllPages_RejectsRepeatedCursorInsteadOfLooping()
    {
        var callCount = 0;
        var handler = new RecordingHandler(_ =>
        {
            callCount++;
            return Json(
                """{"data":[{"id":"1"}],"paging":{"cursors":{"after":"same"}}}""");
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<InstagramApiException>(() =>
            client.GetAllPagesAsync<ApiItem>(
                "me/media",
                "repeated-cursor-test-token",
                query: null,
                CancellationToken.None));

        Assert.Equal(InstagramApiErrorKind.InvalidResponse, exception.Kind);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task TransientFailures_AreRetriedAndCanRecover()
    {
        var callCount = 0;
        var handler = new RecordingHandler(_ =>
        {
            callCount++;
            return callCount < 3
                ? Json("{}", HttpStatusCode.ServiceUnavailable)
                : Json("""{"id":"recovered"}""");
        });
        var client = CreateClient(handler);

        var response = await client.GetAsync<ApiItem>(
            "me",
            "retry-test-token",
            query: null,
            CancellationToken.None);

        Assert.Equal("recovered", response.Data.Id);
        Assert.Equal(3, callCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, InstagramApiErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, InstagramApiErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, InstagramApiErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, InstagramApiErrorKind.Transient)]
    public async Task ProviderFailures_AreNormalizedWithoutSensitiveDetails(
        HttpStatusCode statusCode,
        InstagramApiErrorKind expectedKind)
    {
        const string accessToken = "normalized-error-private-token";
        const string providerDetail = "private-provider-response-detail";
        var client = CreateClient(new RecordingHandler(_ =>
        {
            var response = Json(providerDetail, statusCode);
            response.Headers.RetryAfter =
                new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(20));
            return response;
        }));

        var exception = await Assert.ThrowsAsync<InstagramApiException>(() =>
            client.GetAsync<ApiItem>(
                "me",
                accessToken,
                query: null,
                CancellationToken.None));

        Assert.Equal(expectedKind, exception.Kind);
        Assert.Equal(statusCode, exception.StatusCode);
        Assert.DoesNotContain(providerDetail, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, exception.ToString(), StringComparison.Ordinal);
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            Assert.Equal(TimeSpan.FromSeconds(20), exception.RetryAfter);
        }
    }

    [Fact]
    public async Task CallerCancellation_IsNotConvertedToProviderFailure()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return Json("{}");
        });
        var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetAsync<ApiItem>(
                "me",
                "cancellation-test-token",
                query: null,
                cancellation.Token));
    }

    [Fact]
    public async Task PerAttemptTimeout_IsRetriedAndNormalizedAsTransient()
    {
        var callCount = 0;
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            callCount++;
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return Json("{}");
        });
        var client = CreateClient(
            handler,
            maxAttempts: 2,
            requestTimeout: TimeSpan.FromMilliseconds(10));

        var exception = await Assert.ThrowsAsync<InstagramApiException>(() =>
            client.GetAsync<ApiItem>(
                "me",
                "timeout-test-token",
                query: null,
                CancellationToken.None));

        Assert.Equal(InstagramApiErrorKind.Transient, exception.Kind);
        Assert.Equal(2, callCount);
        Assert.DoesNotContain(
            "timeout-test-token",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccessTokenQueryParameter_IsRejectedBeforeSending()
    {
        var callCount = 0;
        var client = CreateClient(new RecordingHandler(_ =>
        {
            callCount++;
            return Json("{}");
        }));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetAsync<ApiItem>(
                "me",
                "header-only-token",
                new Dictionary<string, string?>
                {
                    ["access_token"] = "query-token",
                },
                CancellationToken.None));

        Assert.Equal(0, callCount);
    }

    private static InstagramApiClient CreateClient(
        HttpMessageHandler handler,
        int maxAttempts = 3,
        TimeSpan? requestTimeout = null)
    {
        var options = Options.Create(new InstagramIntegrationOptions
        {
            ApiMaxAttempts = maxAttempts,
            ApiRetryBaseDelay = TimeSpan.Zero,
            ApiRequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(2),
            ApiMaxPageCount = 10,
        });
        return new InstagramApiClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage Json(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed record ApiItem(string Id);

    private sealed record HttpRequestSnapshot(Uri Uri, string? Authorization);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> _responseFactory;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
            : this((request, _) => Task.FromResult(responseFactory(request)))
        {
        }

        public RecordingHandler(
            Func<
                HttpRequestMessage,
                CancellationToken,
                Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _responseFactory(request, cancellationToken);
    }
}
