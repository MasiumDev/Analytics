namespace Analytics.Api.InstagramMedia.Application;

public interface IInstagramMediaImportService
{
    Task<InstagramMediaImportResult?> ImportAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken);

    Task<InstagramMediaImportResult?> RetryAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken);

    Task<InstagramMediaImportStatusResult?> GetStatusAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken);
}

public sealed record InstagramMediaImportResult(
    InstagramMediaImportResultStatus Status,
    int Fetched,
    int Created,
    int Updated,
    int Failed,
    int PagesProcessed,
    bool HasCheckpoint);

public sealed record InstagramMediaImportStatusResult(
    Guid InstagramAccountId,
    string Status,
    string? AfterCursor,
    int Fetched,
    int Created,
    int Updated,
    int Failed,
    int PagesProcessed,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public enum InstagramMediaImportResultStatus
{
    Completed,
    ReconnectRequired,
    RetryLater,
    ProviderFailure,
    RetryNotAllowed,
}
