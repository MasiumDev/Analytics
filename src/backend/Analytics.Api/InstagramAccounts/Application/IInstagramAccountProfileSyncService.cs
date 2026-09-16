namespace Analytics.Api.InstagramAccounts.Application;

public interface IInstagramAccountProfileSyncService
{
    Task<InstagramProfileSyncResult?> SynchronizeAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken);
}

public sealed record InstagramProfileSyncResult(
    InstagramProfileSyncStatus Status,
    InstagramProfileSyncMetadata? Account = null);

public sealed record InstagramProfileSyncMetadata(
    Guid InstagramAccountId,
    string InstagramUserId,
    string Username,
    string? DisplayName,
    string ProfessionalAccountType,
    long? FollowersCount,
    long? FollowsCount,
    long? MediaCount,
    DateTimeOffset SyncedAtUtc);

public enum InstagramProfileSyncStatus
{
    Synced,
    ReconnectRequired,
    RetryLater,
    ProviderFailure,
}
