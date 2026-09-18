namespace Analytics.Api.InstagramMedia.Application;

public interface IInstagramMediaImportService
{
    Task<InstagramMediaImportResult?> ImportAsync(
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

public enum InstagramMediaImportResultStatus
{
    Completed,
    ReconnectRequired,
    RetryLater,
    ProviderFailure,
}
