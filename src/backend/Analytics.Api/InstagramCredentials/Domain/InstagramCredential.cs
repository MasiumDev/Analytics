using System.Text.Json.Serialization;
using Analytics.Api.InstagramAccounts.Domain;

namespace Analytics.Api.InstagramCredentials.Domain;

public sealed class InstagramCredential
{
    private InstagramCredential()
    {
    }

    internal InstagramCredential(
        Guid instagramAccountId,
        string encryptedAccessToken,
        string grantedScopes,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        Id = Guid.NewGuid();
        InstagramAccountId = instagramAccountId;
        Replace(encryptedAccessToken, grantedScopes, issuedAtUtc, expiresAtUtc);
    }

    public Guid Id { get; private init; }

    public Guid InstagramAccountId { get; private init; }

    [JsonIgnore]
    public string? EncryptedAccessToken { get; private set; }

    public string GrantedScopes { get; private set; } = string.Empty;

    public DateTimeOffset IssuedAtUtc { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public DateTimeOffset? LastRefreshedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public InstagramCredentialStatus Status { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public InstagramAccount InstagramAccount { get; private init; } = null!;

    internal void Replace(
        string encryptedAccessToken,
        string grantedScopes,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(encryptedAccessToken))
        {
            throw new ArgumentException(
                "An encrypted access token is required.",
                nameof(encryptedAccessToken));
        }

        EncryptedAccessToken = encryptedAccessToken;
        GrantedScopes = grantedScopes;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        LastRefreshedAtUtc = DateTimeOffset.UtcNow;
        RevokedAtUtc = null;
        Status = InstagramCredentialStatus.Active;
    }

    public void Revoke(DateTimeOffset revokedAtUtc)
    {
        EncryptedAccessToken = null;
        RevokedAtUtc = revokedAtUtc;
        Status = InstagramCredentialStatus.Revoked;
    }

    public void MarkExpired()
    {
        Status = InstagramCredentialStatus.Expired;
    }

    public void MarkInvalid()
    {
        Status = InstagramCredentialStatus.Invalid;
    }
}

public enum InstagramCredentialStatus
{
    Active,
    Expired,
    Revoked,
    Invalid,
}
