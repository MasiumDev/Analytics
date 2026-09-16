using Analytics.Api.Data;
using Analytics.Api.InstagramIntegration.Application;
using Analytics.Api.InstagramIntegration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Api.InstagramIntegration.Infrastructure;

public sealed class EfInstagramOAuthStateRepository(AnalyticsDbContext database)
    : IInstagramOAuthStateRepository
{
    public Task AddAsync(
        InstagramOAuthState oauthState,
        CancellationToken cancellationToken) =>
        database.InstagramOAuthStates.AddAsync(oauthState, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        database.SaveChangesAsync(cancellationToken);

    public async Task<bool> TryConsumeAsync(
        string stateHash,
        Guid ownerUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var oauthState = await database.InstagramOAuthStates.SingleOrDefaultAsync(
            item => item.StateHash == stateHash && item.OwnerUserId == ownerUserId,
            cancellationToken);
        if (oauthState is null || !oauthState.TryConsume(now))
        {
            return false;
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
