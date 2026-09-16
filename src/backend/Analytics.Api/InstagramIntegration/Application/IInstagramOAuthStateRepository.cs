using Analytics.Api.InstagramIntegration.Domain;

namespace Analytics.Api.InstagramIntegration.Application;

public interface IInstagramOAuthStateRepository
{
    Task AddAsync(InstagramOAuthState oauthState, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<bool> TryConsumeAsync(
        string stateHash,
        Guid ownerUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
