using Analytics.Api.Data;
using Analytics.Api.InstagramMedia.Application;
using Analytics.Api.InstagramMedia.Domain;
using Microsoft.EntityFrameworkCore;
using InstagramMediaEntity = Analytics.Api.InstagramMedia.Domain.InstagramMedia;

namespace Analytics.Api.InstagramMedia.Infrastructure;

public sealed class EfInstagramMediaCurrentStatsRepository(AnalyticsDbContext database)
    : IInstagramMediaCurrentStatsRepository
{
    public async Task<IReadOnlyList<InstagramMediaEntity>> ListMediaAsync(
        Guid instagramAccountId,
        CancellationToken cancellationToken) =>
        await database.InstagramMedia
            .AsNoTracking()
            .Where(media => media.InstagramAccountId == instagramAccountId)
            .OrderBy(media => media.PublishedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task UpsertAsync(
        Guid instagramMediaId,
        long? likeCount,
        long? commentsCount,
        long? savesCount,
        long? sharesCount,
        long? reachCount,
        long? playsCount,
        DateTimeOffset? sourceTimestampUtc,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken)
    {
        var stats = await database.MediaCurrentStats.SingleOrDefaultAsync(
            item => item.InstagramMediaId == instagramMediaId,
            cancellationToken);
        if (stats is null)
        {
            stats = new MediaCurrentStats(
                instagramMediaId,
                likeCount,
                commentsCount,
                savesCount,
                sharesCount,
                reachCount,
                playsCount,
                sourceTimestampUtc,
                receivedAtUtc);
            database.MediaCurrentStats.Add(stats);
        }
        else
        {
            stats.Update(
                likeCount,
                commentsCount,
                savesCount,
                sharesCount,
                reachCount,
                playsCount,
                sourceTimestampUtc,
                receivedAtUtc);
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
