using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Analytics.Api.IntegrationTests;

public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Root_ReturnsReadyServiceStatus()
    {
        var response = await _client.GetAsync("/");
        var status = await response.Content.ReadFromJsonAsync<ServiceStatus>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(status);
        Assert.Equal("Analytics.Api", status.Service);
        Assert.Equal("ready", status.Status);
        Assert.Equal("Product", status.ProductName);
    }

    [Fact]
    public async Task Root_UsesConfiguredProductName()
    {
        await using var application = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Branding:ProductName", "Test Brand");
        });
        using var client = application.CreateClient();

        var status = await client.GetFromJsonAsync<ServiceStatus>("/");

        Assert.NotNull(status);
        Assert.Equal("Test Brand", status.ProductName);
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetailsWithTraceId()
    {
        var response = await _client.GetAsync("/missing");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(problem.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }
}
