using Analytics.Api.InstagramAccounts.Domain;

namespace Analytics.Api.InstagramAccounts.Application;

public interface IInstagramAccountService
{
    Task<IReadOnlyList<InstagramAccount>> ListAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken);

    Task<InstagramAccount?> GetAsync(
        Guid ownerUserId,
        Guid accountId,
        CancellationToken cancellationToken);

    Task<InstagramAccount?> FindByInstagramUserIdAsync(
        Guid ownerUserId,
        string instagramUserId,
        CancellationToken cancellationToken);

    Task<CreateInstagramAccountResult> CreateAsync(
        Guid ownerUserId,
        string instagramUserId,
        string username,
        string? displayName,
        CancellationToken cancellationToken);

    Task<InstagramAccount?> UpdateAsync(
        Guid ownerUserId,
        Guid accountId,
        string username,
        string? displayName,
        CancellationToken cancellationToken);

    Task<InstagramAccount?> UpdateProfessionalProfileAsync(
        Guid ownerUserId,
        Guid accountId,
        string username,
        string? displayName,
        InstagramProfessionalAccountType accountType,
        CancellationToken cancellationToken);

    Task<InstagramAccount?> UpdateConnectionStatusAsync(
        Guid ownerUserId,
        Guid accountId,
        InstagramConnectionStatus status,
        CancellationToken cancellationToken);

    Task<InstagramAccount?> SynchronizeProfileAsync(
        Guid ownerUserId,
        Guid accountId,
        string username,
        string? displayName,
        InstagramProfessionalAccountType accountType,
        DateTimeOffset syncedAtUtc,
        CancellationToken cancellationToken);
}

public sealed record CreateInstagramAccountResult(
    InstagramAccount? Account,
    bool AlreadyConnected);
