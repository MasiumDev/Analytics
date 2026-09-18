using Analytics.Api.Data;
using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramMedia.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InstagramMediaEntity = Analytics.Api.InstagramMedia.Domain.InstagramMedia;

namespace Analytics.Api.IntegrationTests;

public sealed class InstagramInsightSnapshotSchemaTests
{
    [Fact]
    public async Task Snapshots_AreAppendOnlyDeduplicatedNullableUtcAndCascadeOwned()
    {
        var databaseName = $"Analytics_InsightSnapshots_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            await using var application = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting(
                        $"ConnectionStrings:{DatabaseServiceCollectionExtensions.ConnectionStringName}",
                        connectionString);
                });

            var capturedAt = new DateTimeOffset(
                2026,
                9,
                18,
                18,
                30,
                0,
                TimeSpan.FromHours(3.5));
            var laterCapture = capturedAt.AddHours(1);
            Guid accountId;
            Guid mediaId;
            Guid firstMediaSnapshotId;

            await using (var scope = application.Services.CreateAsyncScope())
            {
                var services = scope.ServiceProvider;
                var database = services.GetRequiredService<AnalyticsDbContext>();
                await database.Database.MigrateAsync();
                var users = services.GetRequiredService<UserManager<ApplicationUser>>();
                var owner = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = "insight-snapshots@example.com",
                    Email = "insight-snapshots@example.com",
                };
                Assert.True((await users.CreateAsync(owner)).Succeeded);

                var account = new InstagramAccount(owner.Id, "account-1", "snapshot_account");
                database.InstagramAccounts.Add(account);
                var media = new InstagramMediaEntity(
                    account.Id,
                    "media-1",
                    InstagramMediaType.Reel,
                    "https://www.instagram.com/reel/snapshot/",
                    "Snapshot test",
                    capturedAt);
                database.InstagramMedia.Add(media);

                var firstMediaSnapshot = new MediaInsightSnapshot(
                    media.Id,
                    MediaMetrics(views: 0, reach: null),
                    capturedAt,
                    sourceTimestampUtc: null);
                database.MediaInsightSnapshots.Add(firstMediaSnapshot);
                database.AccountInsightSnapshots.Add(new AccountInsightSnapshot(
                    account.Id,
                    AccountMetrics(views: null, reach: 0),
                    capturedAt,
                    capturedAt.AddHours(-2)));
                await database.SaveChangesAsync();

                accountId = account.Id;
                mediaId = media.Id;
                firstMediaSnapshotId = firstMediaSnapshot.Id;
            }

            await using (var appendScope = application.Services.CreateAsyncScope())
            {
                var database = appendScope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                database.MediaInsightSnapshots.Add(new MediaInsightSnapshot(
                    mediaId,
                    MediaMetrics(views: 25, reach: 20),
                    laterCapture,
                    sourceTimestampUtc: null));
                database.AccountInsightSnapshots.Add(new AccountInsightSnapshot(
                    accountId,
                    AccountMetrics(views: 50, reach: 40),
                    laterCapture,
                    laterCapture.AddHours(-2)));
                await database.SaveChangesAsync();

                var first = await database.MediaInsightSnapshots
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == firstMediaSnapshotId);
                Assert.Equal(0, first.ViewsCount);
                Assert.Null(first.ReachCount);
                Assert.Equal(TimeSpan.Zero, first.CapturedAtUtc.Offset);
                Assert.Equal(2, await database.MediaInsightSnapshots.CountAsync());
                Assert.Equal(2, await database.AccountInsightSnapshots.CountAsync());
            }

            await using (var mutationScope = application.Services.CreateAsyncScope())
            {
                var database = mutationScope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                var first = await database.MediaInsightSnapshots
                    .SingleAsync(item => item.Id == firstMediaSnapshotId);
                database.Entry(first).Property(item => item.ViewsCount).CurrentValue = 999;
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => database.SaveChangesAsync());
            }

            await using (var duplicateScope = application.Services.CreateAsyncScope())
            {
                var database = duplicateScope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                database.MediaInsightSnapshots.Add(new MediaInsightSnapshot(
                    mediaId,
                    MediaMetrics(views: 999, reach: 999),
                    capturedAt,
                    sourceTimestampUtc: null));
                await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
            }

            await using (var cascadeScope = application.Services.CreateAsyncScope())
            {
                var database = cascadeScope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                var account = await database.InstagramAccounts
                    .SingleAsync(item => item.Id == accountId);
                database.InstagramAccounts.Remove(account);
                await database.SaveChangesAsync();

                Assert.False(await database.MediaInsightSnapshots.AnyAsync(
                    item => item.InstagramMediaId == mediaId));
                Assert.False(await database.AccountInsightSnapshots.AnyAsync(
                    item => item.InstagramAccountId == accountId));
            }
        }
        finally
        {
            await using var database = new AnalyticsDbContext(options);
            await database.Database.EnsureDeletedAsync();
        }
    }

    private static MediaInsightMetrics MediaMetrics(long? views, long? reach) =>
        new(
            views,
            reach,
            LikesCount: null,
            CommentsCount: null,
            SavesCount: null,
            SharesCount: null,
            TotalInteractionsCount: null,
            AverageWatchTimeMilliseconds: null,
            TotalWatchTimeMilliseconds: null);

    private static AccountInsightMetrics AccountMetrics(long? views, long? reach) =>
        new(
            views,
            reach,
            FollowerCount: null,
            ProfileViewsCount: null,
            WebsiteClicksCount: null,
            AccountsEngagedCount: null,
            TotalInteractionsCount: null);
}
