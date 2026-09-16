using Analytics.Api.Data;
using Analytics.Api.InstagramMedia.Application;
using Analytics.Api.InstagramMedia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Api.InstagramMedia.Infrastructure;

public sealed class EfAccountCurrentStatsRepository(AnalyticsDbContext database)
    : IAccountCurrentStatsRepository
{
    public Task<AccountCurrentStats?> FindAsync(
        Guid instagramAccountId,
        CancellationToken cancellationToken) =>
        database.AccountCurrentStats.SingleOrDefaultAsync(
            stats => stats.InstagramAccountId == instagramAccountId,
            cancellationToken);

    public Task AddAsync(
        AccountCurrentStats stats,
        CancellationToken cancellationToken) =>
        database.AccountCurrentStats.AddAsync(stats, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        database.SaveChangesAsync(cancellationToken);
}
