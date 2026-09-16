using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Analytics.Api.Data;
using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts.Application;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramCredentials.Domain;
using Analytics.Api.InstagramCredentials.Infrastructure;
using Analytics.Api.InstagramIntegration.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Analytics.Api.IntegrationTests;

public sealed class InstagramAccountProfileSyncTests
{
    private const string Password = "StrongPass123";
    private const string AccessToken = "profile-sync-test-token";

    [Fact]
    public async Task RepeatedSync_UpdatesOneOwnedProfileAndCurrentStatsRow()
    {
        await WithApplication(async (application, client, apiClient, time, _) =>
        {
            var accountId = await RegisterAndSeedAsync(
                application,
                client,
                "profile-owner@example.com");

            var first = await client.PostWithCsrfAsync(
                $"/api/instagram-accounts/{accountId}/sync-profile");
            var firstBody = await first.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.DoesNotContain(AccessToken, firstBody, StringComparison.Ordinal);

            apiClient.Username = "updated_profile";
            apiClient.FollowersCount = 1250;
            time.Advance(TimeSpan.FromMinutes(5));
            var second = await client.PostWithCsrfAsync(
                $"/api/instagram-accounts/{accountId}/sync-profile");
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            await using (var scope = application.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                var account = await database.InstagramAccounts
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == accountId);
                var stats = await database.AccountCurrentStats
                    .AsNoTracking()
                    .SingleAsync(item => item.InstagramAccountId == accountId);

                Assert.Equal("updated_profile", account.Username);
                Assert.Equal("Synced Profile", account.DisplayName);
                Assert.Equal(InstagramProfessionalAccountType.Business, account.ProfessionalAccountType);
                Assert.Equal(InstagramConnectionStatus.Connected, account.ConnectionStatus);
                Assert.Equal(time.GetUtcNow(), account.LastSyncedAtUtc);
                Assert.Equal(1250, stats.FollowersCount);
                Assert.Equal(time.GetUtcNow(), stats.CapturedAtUtc);
                Assert.Single(await database.AccountCurrentStats.ToListAsync());
            }

            Assert.Equal(2, apiClient.RequestCount);
            Assert.All(apiClient.AccessTokens, token => Assert.Equal(AccessToken, token));

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.PostWithCsrfAsync("/api/auth/logout")).StatusCode);
            await RegisterAsync(client, "different-owner@example.com");
            var beforeDenied = apiClient.RequestCount;
            var denied = await client.PostWithCsrfAsync(
                $"/api/instagram-accounts/{accountId}/sync-profile");
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.Equal(beforeDenied, apiClient.RequestCount);
        });
    }

    [Fact]
    public async Task CredentialFailure_RequiresReconnectRevokesTokenAndStopsJobs()
    {
        await WithApplication(async (application, client, apiClient, _, jobs) =>
        {
            var accountId = await RegisterAndSeedAsync(
                application,
                client,
                "profile-failure@example.com");
            apiClient.ErrorKind = InstagramApiErrorKind.Unauthorized;

            var response = await client.PostWithCsrfAsync(
                $"/api/instagram-accounts/{accountId}/sync-profile");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("reconnect", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(AccessToken, body, StringComparison.Ordinal);
            Assert.Contains(accountId, jobs.StoppedAccountIds);

            await using var scope = application.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            var account = await database.InstagramAccounts
                .AsNoTracking()
                .SingleAsync(item => item.Id == accountId);
            var credential = await database.InstagramCredentials
                .AsNoTracking()
                .SingleAsync(item => item.InstagramAccountId == accountId);
            Assert.Equal(InstagramConnectionStatus.ReconnectRequired, account.ConnectionStatus);
            Assert.Equal(InstagramCredentialStatus.Revoked, credential.Status);
            Assert.Null(credential.EncryptedAccessToken);
        });
    }

    private static async Task<Guid> RegisterAndSeedAsync(
        WebApplicationFactory<Program> application,
        HttpClient client,
        string email)
    {
        await RegisterAsync(client, email);
        await using var scope = application.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var database = services.GetRequiredService<AnalyticsDbContext>();
        var ownerId = await database.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync();
        var accountService = services.GetRequiredService<IInstagramAccountService>();
        var created = await accountService.CreateAsync(
            ownerId,
            "17841400000000000",
            "original_profile",
            "Original Profile",
            CancellationToken.None);
        var account = created.Account!;
        await accountService.UpdateProfessionalProfileAsync(
            ownerId,
            account.Id,
            account.Username,
            account.DisplayName,
            InstagramProfessionalAccountType.Business,
            CancellationToken.None);
        var credentialService = services.GetRequiredService<IInstagramCredentialService>();
        await credentialService.StoreOrReplaceAsync(
            ownerId,
            account.Id,
            AccessToken,
            ["instagram_business_basic", "instagram_business_manage_insights"],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30),
            CancellationToken.None);
        await accountService.UpdateConnectionStatusAsync(
            ownerId,
            account.Id,
            InstagramConnectionStatus.Connected,
            CancellationToken.None);
        return account.Id;
    }

    private static async Task RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonWithCsrfAsync(
            "/api/auth/register",
            new RegisterRequest(email, Password));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task WithApplication(
        Func<
            WebApplicationFactory<Program>,
            HttpClient,
            StubInstagramApiClient,
            AdjustableTimeProvider,
            RecordingJobController,
            Task> test)
    {
        var databaseName = $"Analytics_ProfileSync_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        var keyRingPath = Path.Combine(
            Path.GetTempPath(),
            $"Analytics_ProfileSyncKeys_{Guid.NewGuid():N}");
        var databaseOptions = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var apiClient = new StubInstagramApiClient();
        var time = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero));
        var jobs = new RecordingJobController();

        try
        {
            await using var application = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting(
                        $"ConnectionStrings:{DatabaseServiceCollectionExtensions.ConnectionStringName}",
                        connectionString);
                    builder.UseSetting(
                        InstagramCredentialProtectionExtensions.KeyRingPathConfigurationKey,
                        keyRingPath);
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<IInstagramApiClient>();
                        services.RemoveAll<IInstagramDependentJobController>();
                        services.RemoveAll<TimeProvider>();
                        services.AddSingleton<IInstagramApiClient>(apiClient);
                        services.AddSingleton<IInstagramDependentJobController>(jobs);
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
            await test(application, client, apiClient, time, jobs);
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

    private sealed class StubInstagramApiClient : IInstagramApiClient
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        public string Username { get; set; } = "synced_profile";

        public long FollowersCount { get; set; } = 1200;

        public InstagramApiErrorKind? ErrorKind { get; set; }

        public int RequestCount { get; private set; }

        public List<string> AccessTokens { get; } = [];

        public Task<InstagramApiResponse<T>> GetAsync<T>(
            string relativePath,
            string accessToken,
            IReadOnlyDictionary<string, string?>? query,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            AccessTokens.Add(accessToken);
            if (ErrorKind is { } errorKind)
            {
                throw new InstagramApiException(errorKind, "Safe test provider failure.");
            }

            Assert.Equal("me", relativePath);
            Assert.Contains("followers_count", query!["fields"]!, StringComparison.Ordinal);
            var json = $$"""
                {
                  "user_id": "17841400000000000",
                  "username": "{{Username}}",
                  "name": "Synced Profile",
                  "account_type": "BUSINESS",
                  "followers_count": {{FollowersCount}},
                  "follows_count": 140,
                  "media_count": 52
                }
                """;
            var data = JsonSerializer.Deserialize<T>(json, JsonOptions)!;
            return Task.FromResult(new InstagramApiResponse<T>(
                data,
                new InstagramUsageMetadata(null, null, null)));
        }

        public Task<InstagramApiPage<T>> GetPageAsync<T>(
            string relativePath,
            string accessToken,
            IReadOnlyDictionary<string, string?>? query,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InstagramApiCollection<T>> GetAllPagesAsync<T>(
            string relativePath,
            string accessToken,
            IReadOnlyDictionary<string, string?>? query,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class RecordingJobController : IInstagramDependentJobController
    {
        public List<Guid> StoppedAccountIds { get; } = [];

        public Task StopAsync(
            Guid instagramAccountId,
            CancellationToken cancellationToken)
        {
            StoppedAccountIds.Add(instagramAccountId);
            return Task.CompletedTask;
        }
    }
}
