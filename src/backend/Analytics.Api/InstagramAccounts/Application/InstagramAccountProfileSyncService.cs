using System.Text.Json.Serialization;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramCredentials.Domain;
using Analytics.Api.InstagramIntegration.Application;
using Analytics.Api.InstagramMedia.Application;
using Analytics.Api.InstagramMedia.Domain;

namespace Analytics.Api.InstagramAccounts.Application;

public sealed class InstagramAccountProfileSyncService(
    IInstagramAccountService accountService,
    IInstagramCredentialRepository credentialRepository,
    IInstagramTokenProtector tokenProtector,
    IInstagramApiClient apiClient,
    IAccountCurrentStatsRepository statsRepository,
    IInstagramDependentJobController jobController,
    TimeProvider timeProvider) : IInstagramAccountProfileSyncService
{
    public async Task<InstagramProfileSyncResult?> SynchronizeAsync(
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
        if (account.ConnectionStatus == InstagramConnectionStatus.Disconnected
            || credential is null
            || credential.Status != InstagramCredentialStatus.Active)
        {
            return await RequireReconnectAsync(
                ownerUserId,
                instagramAccountId,
                cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        if (credential.ExpiresAtUtc is { } expiresAt && expiresAt <= now)
        {
            credential.MarkExpired();
            await credentialRepository.SaveChangesAsync(cancellationToken);
            return await RequireReconnectAsync(
                ownerUserId,
                instagramAccountId,
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
                instagramAccountId,
                cancellationToken);
        }

        InstagramAccountProfile profile;
        try
        {
            var response = await apiClient.GetAsync<InstagramAccountProfile>(
                "me",
                accessToken!,
                new Dictionary<string, string?>
                {
                    ["fields"] =
                        "user_id,username,name,account_type,followers_count,follows_count,media_count",
                },
                cancellationToken);
            profile = response.Data;
        }
        catch (InstagramApiException exception) when (exception.Kind is
            InstagramApiErrorKind.Unauthorized or InstagramApiErrorKind.Forbidden)
        {
            credential.Revoke(now);
            await credentialRepository.SaveChangesAsync(cancellationToken);
            return await RequireReconnectAsync(
                ownerUserId,
                instagramAccountId,
                cancellationToken);
        }
        catch (InstagramApiException exception) when (exception.Kind is
            InstagramApiErrorKind.RateLimited or InstagramApiErrorKind.Transient)
        {
            return new InstagramProfileSyncResult(InstagramProfileSyncStatus.RetryLater);
        }
        catch (InstagramApiException)
        {
            return new InstagramProfileSyncResult(InstagramProfileSyncStatus.ProviderFailure);
        }

        var accountType = ParseAccountType(profile.AccountType);
        if (accountType is null
            || string.IsNullOrWhiteSpace(profile.UserId)
            || !profile.UserId.Equals(account.InstagramUserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(profile.Username)
            || HasNegativeMetric(profile))
        {
            return new InstagramProfileSyncResult(InstagramProfileSyncStatus.ProviderFailure);
        }

        account = await accountService.SynchronizeProfileAsync(
            ownerUserId,
            instagramAccountId,
            profile.Username,
            profile.Name,
            accountType.Value,
            now,
            cancellationToken);
        if (account is null)
        {
            return null;
        }

        var stats = await statsRepository.FindAsync(
            instagramAccountId,
            cancellationToken);
        if (stats is null)
        {
            stats = new AccountCurrentStats(
                instagramAccountId,
                profile.FollowersCount,
                profile.FollowsCount,
                profile.MediaCount,
                now);
            await statsRepository.AddAsync(stats, cancellationToken);
        }
        else
        {
            stats.Update(
                profile.FollowersCount,
                profile.FollowsCount,
                profile.MediaCount,
                now);
        }

        await statsRepository.SaveChangesAsync(cancellationToken);
        return new InstagramProfileSyncResult(
            InstagramProfileSyncStatus.Synced,
            new InstagramProfileSyncMetadata(
                account.Id,
                account.InstagramUserId,
                account.Username,
                account.DisplayName,
                account.ProfessionalAccountType!.Value.ToString(),
                stats.FollowersCount,
                stats.FollowsCount,
                stats.MediaCount,
                stats.CapturedAtUtc));
    }

    private async Task<InstagramProfileSyncResult> RequireReconnectAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken)
    {
        await accountService.UpdateConnectionStatusAsync(
            ownerUserId,
            instagramAccountId,
            InstagramConnectionStatus.ReconnectRequired,
            cancellationToken);
        await jobController.StopAsync(instagramAccountId, cancellationToken);
        return new InstagramProfileSyncResult(
            InstagramProfileSyncStatus.ReconnectRequired);
    }

    private static InstagramProfessionalAccountType? ParseAccountType(string? accountType) =>
        accountType?.ToUpperInvariant() switch
        {
            "BUSINESS" => InstagramProfessionalAccountType.Business,
            "CREATOR" or "MEDIA_CREATOR" => InstagramProfessionalAccountType.Creator,
            _ => null,
        };

    private static bool HasNegativeMetric(InstagramAccountProfile profile) =>
        profile.FollowersCount < 0
        || profile.FollowsCount < 0
        || profile.MediaCount < 0;

    private sealed record InstagramAccountProfile(
        [property: JsonPropertyName("user_id")] string UserId,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("account_type")] string? AccountType,
        [property: JsonPropertyName("followers_count")] long? FollowersCount,
        [property: JsonPropertyName("follows_count")] long? FollowsCount,
        [property: JsonPropertyName("media_count")] long? MediaCount);
}
