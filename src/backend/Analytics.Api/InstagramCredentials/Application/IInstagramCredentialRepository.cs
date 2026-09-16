using Analytics.Api.InstagramCredentials.Domain;

namespace Analytics.Api.InstagramCredentials.Application;

public interface IInstagramCredentialRepository
{
    Task<InstagramCredential?> FindByAccountAsync(
        Guid instagramAccountId,
        CancellationToken cancellationToken);

    Task AddAsync(
        InstagramCredential credential,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
