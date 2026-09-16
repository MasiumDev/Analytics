using Analytics.Api.Data;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramMedia.Domain;
using Microsoft.EntityFrameworkCore;
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
            published);
        var accountStats = new AccountCurrentStats(accountId, 1000, 120, 40, published);

        Assert.Equal(TimeSpan.Zero, media.PublishedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, media.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, media.UpdatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, mediaStats.CapturedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, accountStats.CapturedAtUtc.Offset);
        Assert.Equal("caption", media.Caption);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            mediaStats.Update(-1, null, null, null, null, null, published));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            accountStats.Update(-1, null, null, published));
    }

    [Fact]
    public void EfModel_DefinesTenantBoundaryIndexesRelationshipsAndNeutralNames()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelOnly;Trusted_Connection=True")
            .Options;
        using var database = new AnalyticsDbContext(options);
        var model = database.Model;
        var media = model.FindEntityType(typeof(InstagramMediaEntity))!;
        var mediaStats = model.FindEntityType(typeof(MediaCurrentStats))!;
        var accountStats = model.FindEntityType(typeof(AccountCurrentStats))!;

        var externalIdIndex = media.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(InstagramMediaEntity.InstagramAccountId), nameof(InstagramMediaEntity.InstagramMediaId)]));
        Assert.True(externalIdIndex.IsUnique);
        Assert.Equal("InstagramMedia", media.GetTableName());
        Assert.Equal("MediaCurrentStats", mediaStats.GetTableName());
        Assert.Equal("AccountCurrentStats", accountStats.GetTableName());
        Assert.All(
            media.GetForeignKeys().Concat(mediaStats.GetForeignKeys()).Concat(accountStats.GetForeignKeys()),
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
        };
        Assert.All(
            schemaNames,
            name => Assert.All(
                forbiddenBrandFragments,
                brand => Assert.DoesNotContain(brand, name, StringComparison.OrdinalIgnoreCase)));
    }
}
