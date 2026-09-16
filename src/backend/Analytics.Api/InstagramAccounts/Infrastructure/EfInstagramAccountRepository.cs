using Analytics.Api.Data;
using Analytics.Api.InstagramAccounts.Application;
using Analytics.Api.InstagramAccounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Api.InstagramAccounts.Infrastructure;

public sealed class EfInstagramAccountRepository(AnalyticsDbContext database)
    : IInstagramAccountRepository
{
    public async Task<IReadOnlyList<InstagramAccount>> ListOwnedAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken) =>
        await database.InstagramAccounts
            .AsNoTracking()
            .Where(account => account.OwnerUserId == ownerUserId)
            .OrderBy(account => account.Username)
            .ToListAsync(cancellationToken);

    public Task<InstagramAccount?> FindOwnedAsync(
        Guid ownerUserId,
        Guid accountId,
        CancellationToken cancellationToken) =>
        database.InstagramAccounts.SingleOrDefaultAsync(
            account => account.Id == accountId && account.OwnerUserId == ownerUserId,
            cancellationToken);

    public Task<bool> IsConnectedAsync(
        string instagramUserId,
        CancellationToken cancellationToken) =>
        database.InstagramAccounts.AnyAsync(
            account => account.InstagramUserId == instagramUserId,
            cancellationToken);

    public Task AddAsync(
        InstagramAccount account,
        CancellationToken cancellationToken) =>
        database.InstagramAccounts.AddAsync(account, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        database.SaveChangesAsync(cancellationToken);
}
