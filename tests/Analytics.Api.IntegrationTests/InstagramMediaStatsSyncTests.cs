using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Analytics.Api.Data;
using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts.Application;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramCredentials.Infrastructure;
using Analytics.Api.InstagramIntegration.Application;
using Analytics.Api.InstagramMedia.Application;
using Analytics.Api.InstagramMedia.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using InstagramMediaEntity = Analytics.Api.InstagramMedia.Domain.InstagramMedia;

namespace Analytics.Api.IntegrationTests;

public sealed class InstagramMediaStatsSyncTests
{
    private const string Password = "StrongPass123";
    private const string AccessToken = "media-stats-test-token";

    [Fact]
    public async Task Sync_StoresSourceAndReceivedTimesHandlesPartialFailureAndIsTenantSafe()
    {
        var databaseName = $"Analytics_MediaStats_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        var keyRingPath = Path.Combine(
            Path.GetTempPath(),
            $"Analytics_MediaStatsKeys_{Guid.NewGuid():N}");
        var databaseOptions = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var apiClient = new MediaStatsInstagramApiClient();
        var time = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 9, 18, 8, 30, 0, TimeSpan.Zero));

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
                        services.RemoveAll<TimeProvider>();
                        services.AddSingleton<IInstagramApiClient>(apiClient);
                        services.AddSingleton<TimeProvider>(time);
                    });
                });

            await using (var migrationScope = application.Services.CreateAsyncScope())
            {
                var database = migrationScope.ServiceProvider
                    .GetRequiredService<AnalyticsDbContext>();
                await database.Database.MigrateAsync();
            }

            using var client = application.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://api.example.com"),
            });
            var accountId = await RegisterAndSeedAsync(
                application,
                client,
                "media-stats-owner@example.com");

            apiClient.FailedMediaIds.Add("media-2");
            var partialResponse = await client.PostWithCsrfAsync(
                $"/api/instagram-accounts/{accountId}/sync-media-stats");
            Assert.Equal(HttpStatusCode.MultiStatus, partialResponse.StatusCode);
            var partial = await partialResponse.Content
                .ReadFromJsonAsync<InstagramMediaStatsSyncResult>();
            Assert.Equal(InstagramMediaStatsSyncStatus.Partial, partial!.Status);
            Assert.Equal(2, partial.Total);
            Assert.Equal(1, partial.Updated);
            Assert.Equal(1, partial.Failed);

            await using (var partialScope = application.Services.CreateAsyncScope())
            {
                var database = partialScope.ServiceProvider
                    .GetRequiredService<AnalyticsDbContext>();
                var stats = await database.MediaCurrentStats
                    .AsNoTracking()
                    .SingleAsync();
                Assert.Equal(10, stats.LikeCount);
                Assert.Equal(apiClient.SourceTimestampUtc, stats.SourceTimestampUtc);
                Assert.Equal(time.GetUtcNow(), stats.ReceivedAtUtc);
            }

            apiClient.FailedMediaIds.Clear();
            time.Advance(TimeSpan.FromMinutes(10));
            var successResponse = await client.PostWithCsrfAsync(
                $"/api/instagram-accounts/{accountId}/sync-media-stats");
            Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);
            var succeeded = await successResponse.Content
                .ReadFromJsonAsync<InstagramMediaStatsSyncResult>();
            Assert.Equal(InstagramMediaStatsSyncStatus.Succeeded, succeeded!.Status);
            Assert.Equal(2, succeeded.Updated);

            await using (var successScope = application.Services.CreateAsyncScope())
            {
                var database = successScope.ServiceProvider
                    .GetRequiredService<AnalyticsDbContext>();
                var stats = await database.MediaCurrentStats
                    .AsNoTracking()
                    .OrderBy(item => item.InstagramMediaId)
                    .ToListAsync();
                Assert.Equal(2, stats.Count);
                Assert.All(stats, item => Assert.Equal(time.GetUtcNow(), item.ReceivedAtUtc));
                Assert.All(stats, item =>
                    Assert.Equal(apiClient.SourceTimestampUtc, item.SourceTimestampUtc));
            }

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.PostWithCsrfAsync("/api/auth/logout")).StatusCode);
            await RegisterAsync(client, "different-media-owner@example.com");
            var callsBeforeDenied = apiClient.RequestCount;
            var denied = await client.PostWithCsrfAsync(
                $"/api/instagram-accounts/{accountId}/sync-media-stats");
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.Equal(callsBeforeDenied, apiClient.RequestCount);
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
        var accounts = services.GetRequiredService<IInstagramAccountService>();
        var created = await accounts.CreateAsync(
            ownerId,
            "17841400000000000",
            "media_stats_owner",
            "Media Stats Owner",
            CancellationToken.None);
        var accountId = created.Account!.Id;
        await accounts.UpdateProfessionalProfileAsync(
            ownerId,
            accountId,
            created.Account.Username,
            created.Account.DisplayName,
            InstagramProfessionalAccountType.Business,
            CancellationToken.None);
        var credentials = services.GetRequiredService<IInstagramCredentialService>();
        await credentials.StoreOrReplaceAsync(
            ownerId,
            accountId,
            AccessToken,
            ["instagram_business_basic", "instagram_business_manage_insights"],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30),
            CancellationToken.None);
        await accounts.UpdateConnectionStatusAsync(
            ownerId,
            accountId,
            InstagramConnectionStatus.Connected,
            CancellationToken.None);
        database.InstagramMedia.AddRange(
            new InstagramMediaEntity(
                accountId,
                "media-1",
                InstagramMediaType.Image,
                "https://www.instagram.com/p/media-1/",
                null,
                DateTimeOffset.UtcNow.AddDays(-2)),
            new InstagramMediaEntity(
                accountId,
                "media-2",
                InstagramMediaType.Reel,
                "https://www.instagram.com/reel/media-2/",
                null,
                DateTimeOffset.UtcNow.AddDays(-1)));
        await database.SaveChangesAsync();
        return accountId;
    }

    private static async Task RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonWithCsrfAsync(
            "/api/auth/register",
            new RegisterRequest(email, Password));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private sealed class MediaStatsInstagramApiClient : IInstagramApiClient
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        public DateTimeOffset SourceTimestampUtc { get; } =
            new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);

        public HashSet<string> FailedMediaIds { get; } = [];

        public int RequestCount { get; private set; }

        public Task<InstagramApiResponse<T>> GetAsync<T>(
            string relativePath,
            string accessToken,
            IReadOnlyDictionary<string, string?>? query,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal(AccessToken, accessToken);
            Assert.Contains("like_count", query!["fields"]!, StringComparison.Ordinal);
            if (FailedMediaIds.Contains(relativePath))
            {
                throw new InstagramApiException(
                    InstagramApiErrorKind.Transient,
                    "Safe media stats test failure.");
            }

            var json = $$"""
                {
                  "id": "{{relativePath}}",
                  "like_count": 10,
                  "comments_count": 2,
                  "saved": 3,
                  "shares": 4,
                  "reach": 100,
                  "plays": 120,
                  "timestamp": "{{SourceTimestampUtc:O}}"
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
}
