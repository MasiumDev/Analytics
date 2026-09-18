using Analytics.Api.InstagramMedia.Domain;

namespace Analytics.Api.InstagramMedia.Application;

public interface IInstagramMediaImportRepository
{
    Task<MediaImportCheckpoint?> PrepareAsync(
        Guid instagramAccountId,
        DateTimeOffset nowUtc,
        bool retryOnly,
        CancellationToken cancellationToken);

    Task<MediaImportCheckpoint?> FindAsync(
        Guid instagramAccountId,
        CancellationToken cancellationToken);

    Task<MediaImportCheckpoint> PersistPageAsync(
        Guid instagramAccountId,
        IReadOnlyList<ImportedInstagramMedia> media,
        int fetchedCount,
        int failedCount,
        string? afterCursor,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MediaImportCheckpoint> CompleteAsync(
        Guid instagramAccountId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MediaImportCheckpoint> FailAsync(
        Guid instagramAccountId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

public sealed record ImportedInstagramMedia(
    string InstagramMediaId,
    InstagramMediaType MediaType,
    string Permalink,
    string? Caption,
    DateTimeOffset PublishedAtUtc);
