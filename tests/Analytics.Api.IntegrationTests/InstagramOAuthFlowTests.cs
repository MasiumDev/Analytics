using System.Net;
using System.Net.Http.Json;
using Analytics.Api.Data;
using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts.Domain;
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
        await WithMigratedApplication(async (application, client, oauthClient, _, _, _) =>
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
            Assert.Equal("connected_brand", connection.Username);
            Assert.Equal("Business", connection.ProfessionalAccountType);
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
                credential.EncryptedAccessToken!,
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
        await WithMigratedApplication(async (_application, client, oauthClient, _, time, _) =>
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

    [Fact]
    public async Task Discovery_ValidatesProfessionalAccountScopesAndTenantOwnership()
    {
        await WithMigratedApplication(async (
            application,
            client,
            oauthClient,
            discoveryClient,
            _,
            _) =>
        {
            await RegisterAsync(client, "first-owner@example.com");

            var (_, firstState) = await StartConnectionAsync(client);
            var firstConnect = await client.GetAsync(
                $"/api/integrations/instagram/callback?code=first&state={Uri.EscapeDataString(firstState)}");
            Assert.Equal(HttpStatusCode.OK, firstConnect.StatusCode);

            var (_, reconnectState) = await StartConnectionAsync(client);
            var reconnect = await client.GetAsync(
                $"/api/integrations/instagram/callback?code=reconnect&state={Uri.EscapeDataString(reconnectState)}");
            Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);

            Guid originalOwnerId;
            await using (var verificationScope = application.Services.CreateAsyncScope())
            {
                var database = verificationScope.ServiceProvider
                    .GetRequiredService<AnalyticsDbContext>();
                var account = await database.InstagramAccounts
                    .AsNoTracking()
                    .SingleAsync();
                originalOwnerId = account.OwnerUserId;
                Assert.Equal("connected_brand", account.Username);
                Assert.Equal(
                    InstagramProfessionalAccountType.Business,
                    account.ProfessionalAccountType);
                Assert.Equal(
                    InstagramConnectionStatus.Connected,
                    account.ConnectionStatus);
                Assert.Single(await database.InstagramCredentials.ToListAsync());
            }

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.PostWithCsrfAsync("/api/auth/logout")).StatusCode);
            await RegisterAsync(client, "second-owner@example.com");

            var (_, takeoverState) = await StartConnectionAsync(client);
            var takeover = await client.GetAsync(
                $"/api/integrations/instagram/callback?code=takeover&state={Uri.EscapeDataString(takeoverState)}");
            Assert.Equal(HttpStatusCode.Conflict, takeover.StatusCode);

            oauthClient.InstagramUserId = "17841400000000001";
            discoveryClient.Account = new InstagramDiscoveredAccount(
                oauthClient.InstagramUserId,
                "personal_profile",
                "Personal Profile",
                ProfessionalAccountType: null,
                GrantedScopes:
                ["instagram_business_basic", "instagram_business_manage_insights"]);
            var (_, personalState) = await StartConnectionAsync(client);
            var personal = await client.GetAsync(
                $"/api/integrations/instagram/callback?code=personal&state={Uri.EscapeDataString(personalState)}");
            var personalBody = await personal.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, personal.StatusCode);
            Assert.Contains("Business or Creator", personalBody, StringComparison.Ordinal);

            oauthClient.InstagramUserId = "17841400000000002";
            discoveryClient.Account = new InstagramDiscoveredAccount(
                oauthClient.InstagramUserId,
                "missing_scope",
                "Missing Scope",
                InstagramProfessionalAccountType.Creator,
                ["instagram_business_basic"]);
            var (_, missingScopeState) = await StartConnectionAsync(client);
            var missingScope = await client.GetAsync(
                $"/api/integrations/instagram/callback?code=missing-scope&state={Uri.EscapeDataString(missingScopeState)}");
            var missingScopeBody = await missingScope.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, missingScope.StatusCode);
            Assert.Contains(
                "instagram_business_manage_insights",
                missingScopeBody,
                StringComparison.Ordinal);

            await using var finalScope = application.Services.CreateAsyncScope();
            var finalDatabase = finalScope.ServiceProvider
                .GetRequiredService<AnalyticsDbContext>();
            var persistedAccount = await finalDatabase.InstagramAccounts
                .AsNoTracking()
                .SingleAsync();
            Assert.Equal(originalOwnerId, persistedAccount.OwnerUserId);
            Assert.Equal("17841400000000000", persistedAccount.InstagramUserId);
            Assert.Single(await finalDatabase.InstagramCredentials.ToListAsync());
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
            StubInstagramAccountDiscoveryClient,
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
        var discoveryClient = new StubInstagramAccountDiscoveryClient();

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
                        services.RemoveAll<IInstagramAccountDiscoveryClient>();
                        services.RemoveAll<TimeProvider>();
                        services.AddSingleton<IInstagramOAuthClient>(oauthClient);
                        services.AddSingleton<IInstagramAccountDiscoveryClient>(discoveryClient);
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
            await test(
                application,
                client,
                oauthClient,
                discoveryClient,
                time,
                connectionString);
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

        public string InstagramUserId { get; set; } = "17841400000000000";

        public Task<InstagramOAuthToken> ExchangeCodeAsync(
            string authorizationCode,
            CancellationToken cancellationToken)
        {
            ExchangedCodes.Add(authorizationCode);
            var issuedAt = time.GetUtcNow();
            return Task.FromResult(new InstagramOAuthToken(
                TestAccessToken,
                InstagramUserId,
                ["instagram_business_basic", "instagram_business_manage_insights"],
                issuedAt,
                issuedAt.AddHours(1)));
        }
    }

    private sealed class StubInstagramAccountDiscoveryClient
        : IInstagramAccountDiscoveryClient
    {
        public InstagramDiscoveredAccount Account { get; set; } = new(
            "17841400000000000",
            "connected_brand",
            "Connected Brand",
            InstagramProfessionalAccountType.Business,
            ["instagram_business_basic", "instagram_business_manage_insights"]);

        public Task<InstagramDiscoveredAccount> DiscoverAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            Assert.Equal(TestAccessToken, accessToken);
            return Task.FromResult(Account);
        }
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
