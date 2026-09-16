namespace Analytics.Api.InstagramCredentials.Application;

public interface IInstagramTokenLifecycleService
{
    Task<InstagramConnectionHealth?> ValidateAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken);

    Task<InstagramDisconnectResult?> DisconnectAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken);
}

public sealed record InstagramConnectionHealth(
    Guid InstagramAccountId,
    string ConnectionStatus,
    string? CredentialStatus,
    DateTimeOffset? ExpiresAtUtc);

public sealed record InstagramDisconnectResult(
    InstagramConnectionHealth Connection,
    bool ProviderAccessRevoked);
