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
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Analytics.Api.IntegrationTests;

public sealed class InstagramMediaImportTests
{
    private const string AccessToken = "media-import-test-token";

    [Fact]
    public async Task Import_ResumesFromCheckpointImportsAllPagesAndRemainsIdempotent()
    {
        var databaseName = $"Analytics_MediaImport_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        var keyRingPath = Path.Combine(
            Path.GetTempPath(),
            $"Analytics_MediaImportKeys_{Guid.NewGuid():N}");
        var databaseOptions = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var apiClient = new PagedInstagramApiClient();

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
                        services.AddSingleton<IInstagramApiClient>(apiClient);
                    });
                });

            Guid ownerId;
            Guid otherOwnerId;
            Guid accountId;
            await using (var seedScope = application.Services.CreateAsyncScope())
            {
                var services = seedScope.ServiceProvider;
                var database = services.GetRequiredService<AnalyticsDbContext>();
                await database.Database.MigrateAsync();
                var users = services.GetRequiredService<UserManager<ApplicationUser>>();
                var owner = User("import-owner@example.com");
                var otherOwner = User("other-import-owner@example.com");
                Assert.True((await users.CreateAsync(owner)).Succeeded);
                Assert.True((await users.CreateAsync(otherOwner)).Succeeded);
                ownerId = owner.Id;
                otherOwnerId = otherOwner.Id;

                var accounts = services.GetRequiredService<IInstagramAccountService>();
                var created = await accounts.CreateAsync(
                    ownerId,
                    "17841400000000000",
                    "import_owner",
                    "Import Owner",
                    CancellationToken.None);
                accountId = created.Account!.Id;
                await accounts.UpdateProfessionalProfileAsync(
                    ownerId,
                    accountId,
                    "import_owner",
                    "Import Owner",
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
            }

            using (var cancellation = new CancellationTokenSource())
            {
                apiClient.CancelOnRequest = 2;
                apiClient.Cancellation = cancellation;
                await using var interruptedScope = application.Services.CreateAsyncScope();
                var importer = interruptedScope.ServiceProvider
                    .GetRequiredService<IInstagramMediaImportService>();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    importer.ImportAsync(ownerId, accountId, cancellation.Token));
            }

            await using (var checkpointScope = application.Services.CreateAsyncScope())
            {
                var database = checkpointScope.ServiceProvider
                    .GetRequiredService<AnalyticsDbContext>();
                var checkpoint = await database.MediaImportCheckpoints
                    .AsNoTracking()
                    .SingleAsync();
                Assert.Equal("cursor-1", checkpoint.AfterCursor);
                Assert.Equal(1, checkpoint.PagesProcessed);
                Assert.Equal(2, checkpoint.CreatedCount);
                Assert.Equal(2, await database.InstagramMedia.CountAsync());
            }

            apiClient.CancelOnRequest = null;
            apiClient.Cancellation = null;
            apiClient.ResetRequestCounter();
            apiClient.RequestedAfterCursors.Clear();
            InstagramMediaImportResult resumed;
            await using (var resumeScope = application.Services.CreateAsyncScope())
            {
                var importer = resumeScope.ServiceProvider
                    .GetRequiredService<IInstagramMediaImportService>();
                resumed = (await importer.ImportAsync(
                    ownerId,
                    accountId,
                    CancellationToken.None))!;
            }

            Assert.Equal(InstagramMediaImportResultStatus.Completed, resumed.Status);
            Assert.Equal(5, resumed.Fetched);
            Assert.Equal(4, resumed.Created);
            Assert.Equal(0, resumed.Updated);
            Assert.Equal(1, resumed.Failed);
            Assert.Equal(3, resumed.PagesProcessed);
            Assert.Equal("cursor-1", apiClient.RequestedAfterCursors[0]);

            apiClient.ResetRequestCounter();
            apiClient.RequestedAfterCursors.Clear();
            InstagramMediaImportResult repeated;
            await using (var repeatedScope = application.Services.CreateAsyncScope())
            {
                var importer = repeatedScope.ServiceProvider
                    .GetRequiredService<IInstagramMediaImportService>();
                repeated = (await importer.ImportAsync(
                    ownerId,
                    accountId,
                    CancellationToken.None))!;
            }

            Assert.Equal(InstagramMediaImportResultStatus.Completed, repeated.Status);
            Assert.Equal(5, repeated.Fetched);
            Assert.Equal(0, repeated.Created);
            Assert.Equal(4, repeated.Updated);
            Assert.Equal(1, repeated.Failed);

            var callsBeforeDenied = apiClient.TotalRequestCount;
            await using (var deniedScope = application.Services.CreateAsyncScope())
            {
                var importer = deniedScope.ServiceProvider
                    .GetRequiredService<IInstagramMediaImportService>();
                Assert.Null(await importer.ImportAsync(
                    otherOwnerId,
                    accountId,
                    CancellationToken.None));
            }
            Assert.Equal(callsBeforeDenied, apiClient.TotalRequestCount);

            await using (var verificationScope = application.Services.CreateAsyncScope())
            {
                var database = verificationScope.ServiceProvider
                    .GetRequiredService<AnalyticsDbContext>();
                Assert.Equal(4, await database.InstagramMedia.CountAsync());
                Assert.Equal(
                    4,
                    await database.InstagramMedia
                        .Select(item => item.InstagramMediaId)
                        .Distinct()
                        .CountAsync());
            }
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

    private static ApplicationUser User(string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        Email = email,
    };

    private sealed class PagedInstagramApiClient : IInstagramApiClient
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        public int? CancelOnRequest { get; set; }

        public CancellationTokenSource? Cancellation { get; set; }

        public int RequestCount { get; private set; }

        public int TotalRequestCount { get; private set; }

        public List<string?> RequestedAfterCursors { get; } = [];

        public Task<InstagramApiPage<T>> GetPageAsync<T>(
            string relativePath,
            string accessToken,
            IReadOnlyDictionary<string, string?>? query,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            TotalRequestCount++;
            if (CancelOnRequest == RequestCount)
            {
                Cancellation!.Cancel();
                return Task.FromCanceled<InstagramApiPage<T>>(Cancellation.Token);
            }

            Assert.Equal("me/media", relativePath);
            Assert.Equal(AccessToken, accessToken);
            var after = query!.GetValueOrDefault("after");
            RequestedAfterCursors.Add(after);
            var (jsonItems, next) = after switch
            {
                null => (
                    new[] { Payload("media-1", "IMAGE"), Payload("media-2", "VIDEO") },
                    "cursor-1"),
                "cursor-1" => (
                    new[] { Payload("media-3", "CAROUSEL_ALBUM"), "{\"id\":\"invalid\"}" },
                    "cursor-2"),
                "cursor-2" => (
                    new[] { Payload("media-4", "VIDEO", "REELS") },
                    null),
                _ => throw new InvalidOperationException("Unexpected cursor."),
            };
            var data = jsonItems
                .Select(json => JsonSerializer.Deserialize<T>(json, JsonOptions)!)
                .ToArray();
            return Task.FromResult(new InstagramApiPage<T>(
                data,
                next,
                new InstagramUsageMetadata(null, null, null)));
        }

        public Task<InstagramApiResponse<T>> GetAsync<T>(
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

        public void ResetRequestCounter() => RequestCount = 0;

        private static string Payload(
            string id,
            string mediaType,
            string? productType = null) => $$"""
            {
              "id": "{{id}}",
              "caption": "Imported {{id}}",
              "media_type": "{{mediaType}}",
              "media_product_type": {{(productType is null ? "null" : $"\"{productType}\"")}},
              "permalink": "https://www.instagram.com/p/{{id}}/",
              "timestamp": "2026-09-17T00:00:00Z"
            }
            """;
    }
}
