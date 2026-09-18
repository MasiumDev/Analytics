using Analytics.Api.InstagramMedia.Domain;

namespace Analytics.Api.InstagramMedia.Application;

public interface IInstagramMediaCurrentStatsRepository
{
    Task<IReadOnlyList<InstagramMedia.Domain.InstagramMedia>> ListMediaAsync(
        Guid instagramAccountId,
        CancellationToken cancellationToken);

    Task UpsertAsync(
        Guid instagramMediaId,
        long? likeCount,
        long? commentsCount,
        long? savesCount,
        long? sharesCount,
        long? reachCount,
        long? playsCount,
        DateTimeOffset? sourceTimestampUtc,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken);
}
