using System.Text.Json.Serialization;
using Analytics.Api.InstagramAccounts.Application;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramCredentials.Domain;
using Analytics.Api.InstagramIntegration.Application;

namespace Analytics.Api.InstagramMedia.Application;

public sealed class InstagramMediaStatsSyncService(
    IInstagramAccountService accountService,
    IInstagramCredentialRepository credentialRepository,
    IInstagramTokenProtector tokenProtector,
    IInstagramApiClient apiClient,
    IInstagramMediaCurrentStatsRepository statsRepository,
    IInstagramDependentJobController jobController,
    TimeProvider timeProvider) : IInstagramMediaStatsSyncService
{
    public async Task<InstagramMediaStatsSyncResult?> SynchronizeAsync(
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

        var receivedAt = timeProvider.GetUtcNow();
        var credential = await credentialRepository.FindByAccountAsync(
            instagramAccountId,
            cancellationToken);
        string? accessToken = null;
        var tokenAvailable = credential is not null
            && credential.Status == InstagramCredentialStatus.Active
            && tokenProtector.TryUnprotect(
                credential.EncryptedAccessToken ?? string.Empty,
                out accessToken);
        if (account.ConnectionStatus == InstagramConnectionStatus.Disconnected
            || credential is null
            || credential.Status != InstagramCredentialStatus.Active
            || credential.ExpiresAtUtc is { } expiresAt && expiresAt <= receivedAt
            || !tokenAvailable)
        {
            if (credential?.ExpiresAtUtc is { } expiry && expiry <= receivedAt)
            {
                credential.MarkExpired();
                await credentialRepository.SaveChangesAsync(cancellationToken);
            }
            else if (credential is not null
                && credential.Status == InstagramCredentialStatus.Active
                && !tokenAvailable)
            {
                credential.MarkInvalid();
                await credentialRepository.SaveChangesAsync(cancellationToken);
            }

            return await ReconnectAsync(
                ownerUserId,
                instagramAccountId,
                0,
                0,
                receivedAt,
                cancellationToken);
        }

        var media = await statsRepository.ListMediaAsync(
            instagramAccountId,
            cancellationToken);
        var updated = 0;
        var failed = 0;
        foreach (var item in media)
        {
            MediaStatsPayload payload;
            try
            {
                var response = await apiClient.GetAsync<MediaStatsPayload>(
                    item.InstagramMediaId,
                    accessToken!,
                    new Dictionary<string, string?>
                    {
                        ["fields"] =
                            "id,like_count,comments_count,saved,shares,reach,plays,timestamp",
                    },
                    cancellationToken);
                payload = response.Data;
            }
            catch (InstagramApiException exception) when (exception.Kind is
                InstagramApiErrorKind.Unauthorized or InstagramApiErrorKind.Forbidden)
            {
                credential.Revoke(receivedAt);
                await credentialRepository.SaveChangesAsync(cancellationToken);
                return await ReconnectAsync(
                    ownerUserId,
                    instagramAccountId,
                    media.Count,
                    failed + 1,
                    receivedAt,
                    cancellationToken);
            }
            catch (InstagramApiException)
            {
                failed++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(payload.Id)
                || !payload.Id.Equals(item.InstagramMediaId, StringComparison.Ordinal)
                || HasNegativeMetric(payload))
            {
                failed++;
                continue;
            }

            await statsRepository.UpsertAsync(
                item.Id,
                payload.LikeCount,
                payload.CommentsCount,
                payload.SavesCount,
                payload.SharesCount,
                payload.ReachCount,
                payload.PlaysCount,
                payload.SourceTimestampUtc,
                receivedAt,
                cancellationToken);
            updated++;
        }

        var status = failed == 0
            ? InstagramMediaStatsSyncStatus.Succeeded
            : updated == 0
                ? InstagramMediaStatsSyncStatus.Failed
                : InstagramMediaStatsSyncStatus.Partial;
        return new InstagramMediaStatsSyncResult(
            status,
            media.Count,
            updated,
            failed,
            receivedAt);
    }

    private async Task<InstagramMediaStatsSyncResult> ReconnectAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        int total,
        int failed,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        await accountService.UpdateConnectionStatusAsync(
            ownerUserId,
            instagramAccountId,
            InstagramConnectionStatus.ReconnectRequired,
            cancellationToken);
        await jobController.StopAsync(instagramAccountId, cancellationToken);
        return new InstagramMediaStatsSyncResult(
            InstagramMediaStatsSyncStatus.ReconnectRequired,
            total,
            0,
            failed,
            receivedAt);
    }

    private static bool HasNegativeMetric(MediaStatsPayload payload) =>
        payload.LikeCount < 0
        || payload.CommentsCount < 0
        || payload.SavesCount < 0
        || payload.SharesCount < 0
        || payload.ReachCount < 0
        || payload.PlaysCount < 0;

    private sealed record MediaStatsPayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("like_count")] long? LikeCount,
        [property: JsonPropertyName("comments_count")] long? CommentsCount,
        [property: JsonPropertyName("saved")] long? SavesCount,
        [property: JsonPropertyName("shares")] long? SharesCount,
        [property: JsonPropertyName("reach")] long? ReachCount,
        [property: JsonPropertyName("plays")] long? PlaysCount,
        [property: JsonPropertyName("timestamp")] DateTimeOffset? SourceTimestampUtc);
}
