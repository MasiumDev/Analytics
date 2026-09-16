namespace Analytics.Api.InstagramCredentials.Application;

public interface IInstagramCredentialService
{
    Task<InstagramCredentialMetadata?> StoreOrReplaceAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        string accessToken,
        IEnumerable<string> grantedScopes,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken);
}

public sealed record InstagramCredentialMetadata(
    Guid InstagramAccountId,
    string[] GrantedScopes,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? LastRefreshedAtUtc,
    string Status);
