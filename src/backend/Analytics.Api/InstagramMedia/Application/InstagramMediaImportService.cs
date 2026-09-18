using System.Text.Json.Serialization;
using Analytics.Api.InstagramAccounts.Application;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramCredentials.Domain;
using Analytics.Api.InstagramIntegration;
using Analytics.Api.InstagramIntegration.Application;
using Analytics.Api.InstagramMedia.Domain;
using Microsoft.Extensions.Options;

namespace Analytics.Api.InstagramMedia.Application;

public sealed class InstagramMediaImportService(
    IInstagramAccountService accountService,
    IInstagramCredentialRepository credentialRepository,
    IInstagramTokenProtector tokenProtector,
    IInstagramApiClient apiClient,
    IInstagramMediaImportRepository repository,
    IInstagramDependentJobController jobController,
    IOptions<InstagramIntegrationOptions> options,
    TimeProvider timeProvider) : IInstagramMediaImportService
{
    private const int PageSize = 50;

    public async Task<InstagramMediaImportResult?> ImportAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken) =>
        await ImportInternalAsync(
            ownerUserId,
            instagramAccountId,
            retryOnly: false,
            cancellationToken);

    public async Task<InstagramMediaImportResult?> RetryAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken) =>
        await ImportInternalAsync(
            ownerUserId,
            instagramAccountId,
            retryOnly: true,
            cancellationToken);

    public async Task<InstagramMediaImportStatusResult?> GetStatusAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        CancellationToken cancellationToken)
    {
        if (await accountService.GetAsync(
                ownerUserId,
                instagramAccountId,
                cancellationToken) is null)
        {
            return null;
        }

        var checkpoint = await repository.FindAsync(
            instagramAccountId,
            cancellationToken);
        return checkpoint is null ? null : Status(checkpoint);
    }

    private async Task<InstagramMediaImportResult?> ImportInternalAsync(
        Guid ownerUserId,
        Guid instagramAccountId,
        bool retryOnly,
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
        var now = timeProvider.GetUtcNow();
        if (account.ConnectionStatus == InstagramConnectionStatus.Disconnected
            || credential is null
            || credential.Status != InstagramCredentialStatus.Active)
        {
            return await RequireReconnectAsync(
                ownerUserId,
                instagramAccountId,
                cancellationToken);
        }

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

        var checkpoint = await repository.PrepareAsync(
            instagramAccountId,
            now,
            retryOnly,
            cancellationToken);
        if (checkpoint is null)
        {
            return new InstagramMediaImportResult(
                InstagramMediaImportResultStatus.RetryNotAllowed,
                0,
                0,
                0,
                0,
                0,
                HasCheckpoint: false);
        }
        var afterCursor = checkpoint.AfterCursor;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(afterCursor))
        {
            seenCursors.Add(afterCursor);
        }

        for (var pageNumber = 1; pageNumber <= options.Value.ApiMaxPageCount; pageNumber++)
        {
            InstagramApiPage<InstagramMediaPayload> page;
            try
            {
                page = await apiClient.GetPageAsync<InstagramMediaPayload>(
                    "me/media",
                    accessToken!,
                    Query(afterCursor),
                    cancellationToken);
            }
            catch (InstagramApiException exception) when (exception.Kind is
                InstagramApiErrorKind.Unauthorized or InstagramApiErrorKind.Forbidden)
            {
                credential.Revoke(timeProvider.GetUtcNow());
                await credentialRepository.SaveChangesAsync(cancellationToken);
                await repository.FailAsync(
                    instagramAccountId,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return await RequireReconnectAsync(
                    ownerUserId,
                    instagramAccountId,
                    cancellationToken);
            }
            catch (InstagramApiException exception) when (exception.Kind is
                InstagramApiErrorKind.RateLimited or InstagramApiErrorKind.Transient)
            {
                checkpoint = await repository.FailAsync(
                    instagramAccountId,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return Result(InstagramMediaImportResultStatus.RetryLater, checkpoint);
            }
            catch (InstagramApiException)
            {
                checkpoint = await repository.FailAsync(
                    instagramAccountId,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return Result(InstagramMediaImportResultStatus.ProviderFailure, checkpoint);
            }

            var imported = new Dictionary<string, ImportedInstagramMedia>(StringComparer.Ordinal);
            var failed = 0;
            foreach (var payload in page.Data)
            {
                if (TryMap(payload, out var item))
                {
                    imported[item!.InstagramMediaId] = item;
                }
                else
                {
                    failed++;
                }
            }

            if (!string.IsNullOrWhiteSpace(page.AfterCursor)
                && (page.AfterCursor.Length > 1024
                    || !seenCursors.Add(page.AfterCursor)))
            {
                checkpoint = await repository.FailAsync(
                    instagramAccountId,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return Result(
                    InstagramMediaImportResultStatus.ProviderFailure,
                    checkpoint);
            }

            checkpoint = await repository.PersistPageAsync(
                instagramAccountId,
                imported.Values.ToArray(),
                page.Data.Count,
                failed,
                page.AfterCursor,
                timeProvider.GetUtcNow(),
                cancellationToken);
            afterCursor = page.AfterCursor;

            if (string.IsNullOrWhiteSpace(afterCursor))
            {
                checkpoint = await repository.CompleteAsync(
                    instagramAccountId,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return Result(InstagramMediaImportResultStatus.Completed, checkpoint);
            }
        }

        checkpoint = await repository.FailAsync(
            instagramAccountId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return Result(InstagramMediaImportResultStatus.RetryLater, checkpoint);
    }

    private async Task<InstagramMediaImportResult> RequireReconnectAsync(
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
        return new InstagramMediaImportResult(
            InstagramMediaImportResultStatus.ReconnectRequired,
            0,
            0,
            0,
            0,
            0,
            HasCheckpoint: false);
    }

    private static Dictionary<string, string?> Query(string? afterCursor)
    {
        var query = new Dictionary<string, string?>
        {
            ["fields"] = "id,caption,media_type,media_product_type,permalink,timestamp",
            ["limit"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(afterCursor))
        {
            query["after"] = afterCursor;
        }

        return query;
    }

    private static bool TryMap(
        InstagramMediaPayload payload,
        out ImportedInstagramMedia? media)
    {
        media = null;
        var mediaType = ParseMediaType(payload.MediaType, payload.MediaProductType);
        if (string.IsNullOrWhiteSpace(payload.Id)
            || payload.Id.Length > 64
            || mediaType is null
            || string.IsNullOrWhiteSpace(payload.Permalink)
            || payload.Permalink.Length > 2048
            || payload.Caption is { Length: > 2200 }
            || !Uri.TryCreate(payload.Permalink, UriKind.Absolute, out var permalink)
            || permalink.Scheme != Uri.UriSchemeHttps
            || payload.Timestamp is null)
        {
            return false;
        }

        media = new ImportedInstagramMedia(
            payload.Id,
            mediaType.Value,
            payload.Permalink,
            payload.Caption,
            payload.Timestamp.Value.ToUniversalTime());
        return true;
    }

    private static InstagramMediaType? ParseMediaType(
        string? mediaType,
        string? mediaProductType)
    {
        if (mediaProductType?.Equals("REELS", StringComparison.OrdinalIgnoreCase) is true)
        {
            return InstagramMediaType.Reel;
        }

        return mediaType?.ToUpperInvariant() switch
        {
            "IMAGE" => InstagramMediaType.Image,
            "VIDEO" => InstagramMediaType.Video,
            "CAROUSEL_ALBUM" => InstagramMediaType.CarouselAlbum,
            _ => null,
        };
    }

    private static InstagramMediaImportResult Result(
        InstagramMediaImportResultStatus status,
        MediaImportCheckpoint checkpoint) =>
        new(
            status,
            checkpoint.FetchedCount,
            checkpoint.CreatedCount,
            checkpoint.UpdatedCount,
            checkpoint.FailedCount,
            checkpoint.PagesProcessed,
            HasCheckpoint: true);

    private static InstagramMediaImportStatusResult Status(
        MediaImportCheckpoint checkpoint) =>
        new(
            checkpoint.InstagramAccountId,
            checkpoint.Status.ToString(),
            checkpoint.AfterCursor,
            checkpoint.FetchedCount,
            checkpoint.CreatedCount,
            checkpoint.UpdatedCount,
            checkpoint.FailedCount,
            checkpoint.PagesProcessed,
            checkpoint.StartedAtUtc,
            checkpoint.UpdatedAtUtc,
            checkpoint.CompletedAtUtc);

    private sealed record InstagramMediaPayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("caption")] string? Caption,
        [property: JsonPropertyName("media_type")] string? MediaType,
        [property: JsonPropertyName("media_product_type")] string? MediaProductType,
        [property: JsonPropertyName("permalink")] string? Permalink,
        [property: JsonPropertyName("timestamp")] DateTimeOffset? Timestamp);
}
