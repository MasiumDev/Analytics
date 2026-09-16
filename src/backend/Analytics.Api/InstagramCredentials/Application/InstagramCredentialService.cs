using Analytics.Api.InstagramAccounts.Application;
using Analytics.Api.InstagramCredentials.Domain;

namespace Analytics.Api.InstagramCredentials.Application;

public sealed class InstagramCredentialService(
    IInstagramAccountRepository accountRepository,
    IInstagramCredentialRepository credentialRepository,
    IInstagramTokenProtector tokenProtector) : IInstagramCredentialService
{
    public async Task<InstagramCredentialMetadata?> StoreOrReplaceAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        string accessToken,
        IEnumerable<string> grantedScopes,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var account = await accountRepository.FindOwnedAsync(
            ownerUserId,
            instagramAccountId,
            cancellationToken);
        if (account is null)
        {
            return null;
        }

        var normalizedScopes = grantedScopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var encryptedAccessToken = tokenProtector.Protect(accessToken);
        var credential = await credentialRepository.FindByAccountAsync(
            instagramAccountId,
            cancellationToken);

        if (credential is null)
        {
            credential = new InstagramCredential(
                instagramAccountId,
                encryptedAccessToken,
                string.Join(' ', normalizedScopes),
                issuedAtUtc,
                expiresAtUtc);
            await credentialRepository.AddAsync(credential, cancellationToken);
        }
        else
        {
            credential.Replace(
                encryptedAccessToken,
                string.Join(' ', normalizedScopes),
                issuedAtUtc,
                expiresAtUtc);
        }

        await credentialRepository.SaveChangesAsync(cancellationToken);
        return new InstagramCredentialMetadata(
            credential.InstagramAccountId,
            normalizedScopes,
            credential.IssuedAtUtc,
            credential.ExpiresAtUtc,
            credential.LastRefreshedAtUtc,
            credential.Status.ToString());
    }
}
