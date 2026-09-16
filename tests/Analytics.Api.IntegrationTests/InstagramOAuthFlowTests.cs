using System.Net;
using System.Net.Http.Json;
using Analytics.Api.Data;
using Analytics.Api.Identity;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramCredentials.Infrastructure;
using Analytics.Api.InstagramIntegration.Application;
using Analytics.Api.InstagramIntegration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Analytics.Api.IntegrationTests;

public sealed class InstagramOAuthFlowTests
{
    private const string Password = "StrongPass123";
    private const string TestAccessToken = "oauth-integration-access-credential";
    private const string TestAppSecret = "oauth-integration-app-secret";

    [Fact]
    public async Task SuccessfulCallback_StoresEncryptedCredentialWithoutBrowserSecrets()
    {
        await WithMigratedApplication(async (application, client, oauthClient, _, _) =>
        {
            await RegisterAsync(client, "oauth-success@example.com");

            var (connectResponse, state) = await StartConnectionAsync(client);
            var redirect = connectResponse.Headers.Location!;
            var redirectQuery = QueryHelpers.ParseQuery(redirect.Query);

            Assert.Equal("www.instagram.com", redirect.Host);
            Assert.Equal("code", redirectQuery["response_type"]);
            Assert.Equal("test-instagram-app", redirectQuery["client_id"]);
            Assert.Equal(
                "instagram_business_basic,instagram_business_manage_insights",
                redirectQuery["scope"]);
            Assert.DoesNotContain(TestAppSecret, redirect.AbsoluteUri, StringComparison.Ordinal);
            Assert.DoesNotContain(TestAccessToken, redirect.AbsoluteUri, StringComparison.Ordinal);

            var callbackResponse = await client.GetAsync(
                $"/api/integrations/instagram/callback?code=valid-code&state={Uri.EscapeDataString(state)}");
            var responseBody = await callbackResponse.Content.ReadAsStringAsync();
            var connection = await callbackResponse.Content
                .ReadFromJsonAsync<InstagramConnectionMetadata>();

            Assert.Equal(HttpStatusCode.OK, callbackResponse.StatusCode);
            Assert.NotNull(connection);
            Assert.Equal("17841400000000000", connection.InstagramUserId);
            Assert.Equal("17841400000000000", connection.Username);
            Assert.Equal("valid-code", Assert.Single(oauthClient.ExchangedCodes));
            Assert.DoesNotContain(TestAccessToken, responseBody, StringComparison.Ordinal);
            Assert.DoesNotContain(TestAppSecret, responseBody, StringComparison.Ordinal);

            await using var verificationScope = application.Services.CreateAsyncScope();
            var database = verificationScope.ServiceProvider
                .GetRequiredService<AnalyticsDbContext>();
            var credential = await database.InstagramCredentials
                .AsNoTracking()
                .SingleAsync();
            var persistedState = await database.InstagramOAuthStates
                .AsNoTracking()
                .SingleAsync();
            var protector = verificationScope.ServiceProvider
                .GetRequiredService<IInstagramTokenProtector>();

            Assert.NotEqual(TestAccessToken, credential.EncryptedAccessToken);
            Assert.True(protector.TryUnprotect(
                credential.EncryptedAccessToken,
                out var decrypted));
            Assert.Equal(TestAccessToken, decrypted);
            Assert.NotEqual(state, persistedState.StateHash);
            Assert.NotNull(persistedState.ConsumedAtUtc);

            var replayResponse = await client.GetAsync(
                $"/api/integrations/instagram/callback?code=replay-code&state={Uri.EscapeDataString(state)}");
            Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);
            Assert.Single(oauthClient.ExchangedCodes);
        });
    }

    [Fact]
    public async Task Callback_RejectsTamperingDenialExpiryAndMissingCode()
    {
        await WithMigratedApplication(async (_application, client, oauthClient, time, _) =>
        {
            await RegisterAsync(client, "oauth-errors@example.com");

            var (_, denialState) = await StartConnectionAsync(client);
            var tampered = await client.GetAsync(
                $"/api/integrations/instagram/callback?code=ignored&state={denialState}tampered");
            Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);

            var denied = await client.GetAsync(
                "/api/integrations/instagram/callback" +
                $"?error=access_denied&error_description=private-provider-detail&state={Uri.EscapeDataString(denialState)}");
            var deniedBody = await denied.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.DoesNotContain("private-provider-detail", deniedBody, StringComparison.Ordinal);

            var replay = await client.GetAsync(
                $"/api/integrations/instagram/callback?code=ignored&state={Uri.EscapeDataString(denialState)}");
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

            var (_, expiredState) = await StartConnectionAsync(client);
            time.Advance(TimeSpan.FromMinutes(11));
            var expired = await client.GetAsync(
                $"/api/integrations/instagram/callback?code=ignored&state={Uri.EscapeDataString(expiredState)}");
            Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);

            time.Advance(TimeSpan.FromMinutes(-11));
            var (_, missingCodeState) = await StartConnectionAsync(client);
            var missingCode = await client.GetAsync(
                $"/api/integrations/instagram/callback?state={Uri.EscapeDataString(missingCodeState)}");
            Assert.Equal(HttpStatusCode.BadRequest, missingCode.StatusCode);
            Assert.Empty(oauthClient.ExchangedCodes);
        });
    }

    private static async Task<(HttpResponseMessage Response, string State)>
        StartConnectionAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/integrations/instagram/connect");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var query = QueryHelpers.ParseQuery(response.Headers.Location.Query);
        var state = query["state"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(state));
        return (response, state);
    }

    private static async Task RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonWithCsrfAsync(
            "/api/auth/register",
            new RegisterRequest(email, Password));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task WithMigratedApplication(
        Func<
            WebApplicationFactory<Program>,
            HttpClient,
            StubInstagramOAuthClient,
            AdjustableTimeProvider,
            string,
            Task> test)
    {
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database=Analytics_OAuth_{Guid.NewGuid():N};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        var keyRingPath = Path.Combine(
            Path.GetTempPath(),
            $"Analytics_OAuthKeys_{Guid.NewGuid():N}");
        var databaseOptions = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var time = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero));
        var oauthClient = new StubInstagramOAuthClient(time);

        try
        {
            await using var application = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting(
                        $"ConnectionStrings:{DatabaseServiceCollectionExtensions.ConnectionStringName}",
                        connectionString);
                    builder.UseSetting("Instagram:Enabled", "true");
                    builder.UseSetting("Instagram:AppId", "test-instagram-app");
                    builder.UseSetting("Instagram:AppSecret", TestAppSecret);
                    builder.UseSetting(
                        "Instagram:OAuthRedirectUri",
                        "https://api.example.com/api/integrations/instagram/callback");
                    builder.UseSetting(
                        InstagramCredentialProtectionExtensions.KeyRingPathConfigurationKey,
                        keyRingPath);
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<IInstagramOAuthClient>();
                        services.RemoveAll<TimeProvider>();
                        services.AddSingleton<IInstagramOAuthClient>(oauthClient);
                        services.AddSingleton<TimeProvider>(time);
                    });
                });

            await using (var scope = application.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                await database.Database.MigrateAsync();
            }

            using var client = application.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://api.example.com"),
            });
            await test(application, client, oauthClient, time, connectionString);
        }
        finally
        {
            await using var database = new AnalyticsDbContext(databaseOptions);
            await database.Database.EnsureDeletedAsync();
            if (Directory.Exists(keyRingPath))
            {
                Directory.Delete(keyRingPath, recursive: true);
            }
        }
    }

    private sealed class StubInstagramOAuthClient(AdjustableTimeProvider time)
        : IInstagramOAuthClient
    {
        public List<string> ExchangedCodes { get; } = [];

        public Task<InstagramOAuthToken> ExchangeCodeAsync(
            string authorizationCode,
            CancellationToken cancellationToken)
        {
            ExchangedCodes.Add(authorizationCode);
            var issuedAt = time.GetUtcNow();
            return Task.FromResult(new InstagramOAuthToken(
                TestAccessToken,
                "17841400000000000",
                ["instagram_business_basic", "instagram_business_manage_insights"],
                issuedAt,
                issuedAt.AddHours(1)));
        }
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
