using Analytics.Api.Data;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramCredentials.Domain;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Api.InstagramCredentials.Infrastructure;

public sealed class EfInstagramCredentialRepository(AnalyticsDbContext database)
    : IInstagramCredentialRepository
{
    public Task<InstagramCredential?> FindByAccountAsync(
        Guid instagramAccountId,
        CancellationToken cancellationToken) =>
        database.InstagramCredentials.SingleOrDefaultAsync(
            credential => credential.InstagramAccountId == instagramAccountId,
            cancellationToken);

    public Task AddAsync(
        InstagramCredential credential,
        CancellationToken cancellationToken) =>
        database.InstagramCredentials.AddAsync(credential, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        database.SaveChangesAsync(cancellationToken);
}
