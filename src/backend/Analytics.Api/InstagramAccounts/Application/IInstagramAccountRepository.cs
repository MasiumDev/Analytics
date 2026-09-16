using Analytics.Api.InstagramAccounts.Domain;

namespace Analytics.Api.InstagramAccounts.Application;

public interface IInstagramAccountRepository
{
    Task<IReadOnlyList<InstagramAccount>> ListOwnedAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken);

    Task<InstagramAccount?> FindOwnedAsync(
        Guid ownerUserId,
        Guid accountId,
        CancellationToken cancellationToken);

    Task<bool> IsConnectedAsync(
        string instagramUserId,
        CancellationToken cancellationToken);

    Task AddAsync(
        InstagramAccount account,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
