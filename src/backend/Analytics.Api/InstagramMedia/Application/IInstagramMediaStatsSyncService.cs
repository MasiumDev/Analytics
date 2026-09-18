namespace Analytics.Api.InstagramMedia.Application;

public interface IInstagramMediaStatsSyncService
{
    Task<InstagramMediaStatsSyncResult?> SynchronizeAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken);
}

public sealed record InstagramMediaStatsSyncResult(
    InstagramMediaStatsSyncStatus Status,
    int Total,
    int Updated,
    int Failed,
    DateTimeOffset ReceivedAtUtc);

public enum InstagramMediaStatsSyncStatus
{
    Succeeded,
    Partial,
    Failed,
    ReconnectRequired,
}
