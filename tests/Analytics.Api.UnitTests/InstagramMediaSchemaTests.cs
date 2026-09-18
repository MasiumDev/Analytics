using Analytics.Api.Data;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramMedia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using InstagramMediaEntity = Analytics.Api.InstagramMedia.Domain.InstagramMedia;

namespace Analytics.Api.UnitTests;

public sealed class InstagramMediaSchemaTests
{
    [Fact]
    public void DomainModels_CoverProfessionalAccountsAndMvpMediaTypes()
    {
        Assert.Equal(
            ["Business", "Creator"],
            Enum.GetNames<InstagramProfessionalAccountType>());
        Assert.Equal(
            ["Image", "Video", "CarouselAlbum", "Reel"],
            Enum.GetNames<InstagramMediaType>());
    }

    [Fact]
    public void DomainModels_NormalizeTimestampsToUtcAndRejectNegativeMetrics()
    {
        var accountId = Guid.NewGuid();
        var published = new DateTimeOffset(2026, 9, 17, 8, 30, 0, TimeSpan.FromHours(3.5));
        var media = new InstagramMediaEntity(
            accountId,
            "media-1",
            InstagramMediaType.Reel,
            "https://www.instagram.com/reel/example/",
            " caption ",
            published);
        var mediaStats = new MediaCurrentStats(
            media.Id,
            10,
            2,
            3,
            4,
            100,
            80,
            published,
            published);
        var accountStats = new AccountCurrentStats(accountId, 1000, 120, 40, published);

        Assert.Equal(TimeSpan.Zero, media.PublishedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, media.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, media.UpdatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, mediaStats.SourceTimestampUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, mediaStats.ReceivedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, accountStats.CapturedAtUtc.Offset);
        Assert.Equal("caption", media.Caption);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            mediaStats.Update(-1, null, null, null, null, null, published, published));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            accountStats.Update(-1, null, null, published));
    }

    [Fact]
    public void InsightSnapshots_PreserveNullAndZeroNormalizeUtcAndRejectNegativeMetrics()
    {
        var parentId = Guid.NewGuid();
        var capturedAt = new DateTimeOffset(
            2026,
            9,
            18,
            18,
            30,
            0,
            TimeSpan.FromHours(3.5));
        var sourceTimestamp = capturedAt.AddHours(-2);
        var mediaSnapshot = new MediaInsightSnapshot(
            parentId,
            new MediaInsightMetrics(
                ViewsCount: 0,
                ReachCount: null,
                LikesCount: 10,
                CommentsCount: 2,
                SavesCount: 3,
                SharesCount: 4,
                TotalInteractionsCount: 19,
                AverageWatchTimeMilliseconds: 1500,
                TotalWatchTimeMilliseconds: 9000),
            capturedAt,
            sourceTimestamp);
        var accountSnapshot = new AccountInsightSnapshot(
            parentId,
            new AccountInsightMetrics(
                ViewsCount: null,
                ReachCount: 0,
                FollowerCount: 1000,
                ProfileViewsCount: null,
                WebsiteClicksCount: null,
                AccountsEngagedCount: null,
                TotalInteractionsCount: null),
            capturedAt,
            sourceTimestamp);

        Assert.Equal(0, mediaSnapshot.ViewsCount);
        Assert.Null(mediaSnapshot.ReachCount);
        Assert.Null(accountSnapshot.ViewsCount);
        Assert.Equal(0, accountSnapshot.ReachCount);
        Assert.Equal(TimeSpan.Zero, mediaSnapshot.CapturedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, mediaSnapshot.SourceTimestampUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, accountSnapshot.CapturedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, accountSnapshot.SourceTimestampUtc!.Value.Offset);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaInsightSnapshot(
            parentId,
            new MediaInsightMetrics(-1, null, null, null, null, null, null, null, null),
            capturedAt,
            null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AccountInsightSnapshot(
            parentId,
            new AccountInsightMetrics(null, -1, null, null, null, null, null),
            capturedAt,
            null));
    }

    [Fact]
    public void EfModel_DefinesTenantBoundaryIndexesRelationshipsAndNeutralNames()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelOnly;Trusted_Connection=True")
            .Options;
        using var database = new AnalyticsDbContext(options);
        var model = database.GetService<IDesignTimeModel>().Model;
        var media = model.FindEntityType(typeof(InstagramMediaEntity))!;
        var mediaStats = model.FindEntityType(typeof(MediaCurrentStats))!;
        var accountStats = model.FindEntityType(typeof(AccountCurrentStats))!;
        var mediaSnapshots = model.FindEntityType(typeof(MediaInsightSnapshot))!;
        var accountSnapshots = model.FindEntityType(typeof(AccountInsightSnapshot))!;

        var externalIdIndex = media.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(InstagramMediaEntity.InstagramAccountId), nameof(InstagramMediaEntity.InstagramMediaId)]));
        Assert.True(externalIdIndex.IsUnique);
        Assert.Equal("InstagramMedia", media.GetTableName());
        Assert.Equal("MediaCurrentStats", mediaStats.GetTableName());
        Assert.Equal("AccountCurrentStats", accountStats.GetTableName());
        Assert.Equal("MediaInsightSnapshots", mediaSnapshots.GetTableName());
        Assert.Equal("AccountInsightSnapshots", accountSnapshots.GetTableName());
        AssertSnapshotModel(
            mediaSnapshots,
            nameof(MediaInsightSnapshot.InstagramMediaId));
        AssertSnapshotModel(
            accountSnapshots,
            nameof(AccountInsightSnapshot.InstagramAccountId));
        Assert.True(mediaSnapshots.FindProperty(nameof(MediaInsightSnapshot.ViewsCount))!.IsNullable);
        Assert.True(accountSnapshots.FindProperty(nameof(AccountInsightSnapshot.ViewsCount))!.IsNullable);
        Assert.All(
            media.GetForeignKeys()
                .Concat(mediaStats.GetForeignKeys())
                .Concat(accountStats.GetForeignKeys())
                .Concat(mediaSnapshots.GetForeignKeys())
                .Concat(accountSnapshots.GetForeignKeys()),
            foreignKey => Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior));

        var forbiddenBrandFragments = new[]
        {
            "Analytics",
            "Metrico",
            "MetricLens",
            "MetricaLens",
            "MetricTrack",
            "SocialTrack",
        };
        var schemaNames = new[]
        {
            media.ClrType.Name,
            media.GetTableName()!,
            mediaStats.ClrType.Name,
            mediaStats.GetTableName()!,
            accountStats.ClrType.Name,
            accountStats.GetTableName()!,
            mediaSnapshots.ClrType.Name,
            mediaSnapshots.GetTableName()!,
            accountSnapshots.ClrType.Name,
            accountSnapshots.GetTableName()!,
        };
        Assert.All(
            schemaNames,
            name => Assert.All(
                forbiddenBrandFragments,
                brand => Assert.DoesNotContain(brand, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssertSnapshotModel(
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType,
        string parentPropertyName)
    {
        var timeSeriesIndex = entityType.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [parentPropertyName, "CapturedAtUtc"]));
        Assert.True(timeSeriesIndex.IsUnique);
        Assert.Equal([false, true], timeSeriesIndex.IsDescending);
        Assert.Contains(
            entityType.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual(["CapturedAtUtc"]));
        Assert.All(
            entityType.GetProperties(),
            property => Assert.Equal(
                PropertySaveBehavior.Throw,
                property.GetAfterSaveBehavior()));
    }
}
