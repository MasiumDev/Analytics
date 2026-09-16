using Analytics.Api.InstagramAccounts.Application;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramCredentials.Domain;

namespace Analytics.Api.InstagramCredentials.Application;

public sealed class InstagramTokenLifecycleService(
    IInstagramAccountService accountService,
    IInstagramCredentialRepository credentialRepository,
    IInstagramTokenProtector tokenProtector,
    IInstagramTokenLifecycleClient lifecycleClient,
    IInstagramDependentJobController jobController,
    TimeProvider timeProvider) : IInstagramTokenLifecycleService
{
    public async Task<InstagramConnectionHealth?> ValidateAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken)
    {
        var account = await accountService.GetAsync(
            ownerUserId,
            instagramAccountId,
            cancellationToken);
        if (account is null)
        {
            return null;
        }

        if (account.ConnectionStatus == InstagramConnectionStatus.Disconnected)
        {
            return ToHealth(account, credential: null);
        }

        var credential = await credentialRepository.FindByAccountAsync(
            instagramAccountId,
            cancellationToken);
        if (credential is null)
        {
            return await RequireReconnectAsync(
                ownerUserId,
                account,
                credential,
                cancellationToken);
        }

        if (credential.Status == InstagramCredentialStatus.Revoked
            || credential.Status == InstagramCredentialStatus.Invalid)
        {
            return await RequireReconnectAsync(
                ownerUserId,
                account,
                credential,
                cancellationToken);
        }

        if (credential.ExpiresAtUtc is { } expiresAt
            && expiresAt <= timeProvider.GetUtcNow())
        {
            credential.MarkExpired();
            await credentialRepository.SaveChangesAsync(cancellationToken);
            return await RequireReconnectAsync(
                ownerUserId,
                account,
                credential,
                cancellationToken);
        }

        if (!tokenProtector.TryUnprotect(
                credential.EncryptedAccessToken ?? string.Empty,
                out var accessToken))
        {
            credential.MarkInvalid();
            await credentialRepository.SaveChangesAsync(cancellationToken);
            return await RequireReconnectAsync(
                ownerUserId,
                account,
                credential,
                cancellationToken);
        }

        var inspection = await lifecycleClient.ValidateAsync(
            accessToken!,
            cancellationToken);
        if (inspection.Status == InstagramProviderTokenStatus.Active
            && inspection.InstagramUserId?.Equals(
                account.InstagramUserId,
                StringComparison.Ordinal) is true)
        {
            account = await accountService.UpdateConnectionStatusAsync(
                ownerUserId,
                account.Id,
                InstagramConnectionStatus.Connected,
                cancellationToken) ?? account;
            return ToHealth(account, credential);
        }

        if (inspection.Status == InstagramProviderTokenStatus.Revoked)
        {
            credential.Revoke(timeProvider.GetUtcNow());
        }
        else
        {
            credential.MarkInvalid();
        }

        await credentialRepository.SaveChangesAsync(cancellationToken);
        return await RequireReconnectAsync(
            ownerUserId,
            account,
            credential,
            cancellationToken);
    }

    public async Task<InstagramDisconnectResult?> DisconnectAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken)
    {
        var account = await accountService.GetAsync(
            ownerUserId,
            instagramAccountId,
            cancellationToken);
        if (account is null)
        {
            return null;
        }

        var credential = await credentialRepository.FindByAccountAsync(
            instagramAccountId,
            cancellationToken);
        var providerAccessRevoked = false;
        if (credential is not null)
        {
            if (tokenProtector.TryUnprotect(
                    credential.EncryptedAccessToken ?? string.Empty,
                    out var accessToken))
            {
                providerAccessRevoked = await lifecycleClient.RevokeAsync(
                    accessToken!,
                    cancellationToken);
            }

            credential.Revoke(timeProvider.GetUtcNow());
            await credentialRepository.SaveChangesAsync(cancellationToken);
        }

        account = await accountService.UpdateConnectionStatusAsync(
            ownerUserId,
            account.Id,
            InstagramConnectionStatus.Disconnected,
            cancellationToken) ?? account;
        await jobController.StopAsync(instagramAccountId, cancellationToken);

        return new InstagramDisconnectResult(
            ToHealth(account, credential),
            providerAccessRevoked);
    }

    private async Task<InstagramConnectionHealth> RequireReconnectAsync(
        Guid ownerUserId,
        InstagramAccount account,
        InstagramCredential? credential,
        CancellationToken cancellationToken)
    {
        account = await accountService.UpdateConnectionStatusAsync(
            ownerUserId,
            account.Id,
            InstagramConnectionStatus.ReconnectRequired,
            cancellationToken) ?? account;
        await jobController.StopAsync(account.Id, cancellationToken);
        return ToHealth(account, credential);
    }

    private static InstagramConnectionHealth ToHealth(
        InstagramAccount account,
        InstagramCredential? credential) =>
        new(
            account.Id,
            account.ConnectionStatus.ToString(),
            credential?.Status.ToString(),
            credential?.ExpiresAtUtc);
}
