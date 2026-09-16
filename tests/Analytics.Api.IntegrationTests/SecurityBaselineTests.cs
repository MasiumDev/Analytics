using System.Net;
using System.Net.Http.Json;
using Analytics.Api.Data;
using Analytics.Api.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Analytics.Api.IntegrationTests;

public sealed class SecurityBaselineTests
{
    [Fact]
    public async Task StateChangingRequestWithoutCsrfToken_IsRejected()
    {
        await using var application = CreateProductionApplication();
        using var client = CreateHttpsClient(application);

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("csrf@example.com", "StrongPass123"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticationEndpoint_RejectsRequestsBeyondRateLimit()
    {
        await using var application = CreateProductionApplication();
        using var client = CreateHttpsClient(application);

        for (var requestNumber = 0; requestNumber < 10; requestNumber++)
        {
            using var accepted = await client.PostAsJsonWithCsrfAsync(
                "/api/auth/login",
                new LoginRequest("not-an-email", string.Empty));
            Assert.Equal(HttpStatusCode.BadRequest, accepted.StatusCode);
        }

        var rejected = await client.PostAsJsonWithCsrfAsync(
            "/api/auth/login",
            new LoginRequest("not-an-email", string.Empty));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task ProductionResponses_UseSecurityHeadersAndHttpsRedirect()
    {
        await using var application = CreateProductionApplication();
        using var httpsClient = CreateHttpsClient(application);

        var secureResponse = await httpsClient.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, secureResponse.StatusCode);
        Assert.Equal("nosniff", HeaderValue(secureResponse, "X-Content-Type-Options"));
        Assert.Equal("DENY", HeaderValue(secureResponse, "X-Frame-Options"));
        Assert.Equal("no-referrer", HeaderValue(secureResponse, "Referrer-Policy"));
        Assert.Equal(
            "camera=(), microphone=(), geolocation=()",
            HeaderValue(secureResponse, "Permissions-Policy"));
        Assert.Contains(
            "default-src 'none'",
            HeaderValue(secureResponse, "Content-Security-Policy"),
            StringComparison.Ordinal);
        Assert.True(secureResponse.Headers.Contains("Strict-Transport-Security"));

        using var httpClient = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://api.example.com"),
        });
        var redirectResponse = await httpClient.GetAsync("/");

        Assert.Equal(HttpStatusCode.PermanentRedirect, redirectResponse.StatusCode);
        Assert.Equal(new Uri("https://api.example.com/"), redirectResponse.Headers.Location);
    }

    private static string HeaderValue(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues(name));

    private static HttpClient CreateHttpsClient(WebApplicationFactory<Program> application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://api.example.com"),
        });

    private static WebApplicationFactory<Program> CreateProductionApplication()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(
                $"ConnectionStrings:{DatabaseServiceCollectionExtensions.ConnectionStringName}",
                $"Server=(localdb)\\MSSQLLocalDB;Database=Analytics_Security_{Guid.NewGuid():N};" +
                "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True");
        });
    }
}
