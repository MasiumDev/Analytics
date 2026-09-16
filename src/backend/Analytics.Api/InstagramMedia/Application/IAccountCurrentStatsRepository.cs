using Analytics.Api.InstagramMedia.Domain;

namespace Analytics.Api.InstagramMedia.Application;

public interface IAccountCurrentStatsRepository
{
    Task<AccountCurrentStats?> FindAsync(
        Guid instagramAccountId,
        CancellationToken cancellationToken);

    Task AddAsync(
        AccountCurrentStats stats,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
