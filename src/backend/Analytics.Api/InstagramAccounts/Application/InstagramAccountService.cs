using Analytics.Api.InstagramAccounts.Domain;

namespace Analytics.Api.InstagramAccounts.Application;

public sealed class InstagramAccountService(IInstagramAccountRepository repository)
    : IInstagramAccountService
{
    public Task<IReadOnlyList<InstagramAccount>> ListAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken) =>
        repository.ListOwnedAsync(ownerUserId, cancellationToken);

    public Task<InstagramAccount?> GetAsync(
        Guid ownerUserId,
        Guid accountId,
        CancellationToken cancellationToken) =>
        repository.FindOwnedAsync(ownerUserId, accountId, cancellationToken);

    public async Task<CreateInstagramAccountResult> CreateAsync(
        Guid ownerUserId,
        string instagramUserId,
        string username,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var normalizedInstagramUserId = instagramUserId.Trim();
        if (await repository.IsConnectedAsync(normalizedInstagramUserId, cancellationToken))
        {
            return new CreateInstagramAccountResult(null, AlreadyConnected: true);
        }

        var account = new InstagramAccount(
            ownerUserId,
            normalizedInstagramUserId,
            username,
            displayName);
        await repository.AddAsync(account, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return new CreateInstagramAccountResult(account, AlreadyConnected: false);
    }

    public async Task<InstagramAccount?> UpdateAsync(
        Guid ownerUserId,
        Guid accountId,
        string username,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var account = await repository.FindOwnedAsync(
            ownerUserId,
            accountId,
            cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.UpdateProfile(username, displayName);
        await repository.SaveChangesAsync(cancellationToken);
        return account;
    }
}
