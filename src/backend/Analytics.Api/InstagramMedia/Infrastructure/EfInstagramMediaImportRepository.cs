using Analytics.Api.Data;
using Analytics.Api.InstagramMedia.Application;
using Analytics.Api.InstagramMedia.Domain;
using Microsoft.EntityFrameworkCore;
using InstagramMediaEntity = Analytics.Api.InstagramMedia.Domain.InstagramMedia;

namespace Analytics.Api.InstagramMedia.Infrastructure;

public sealed class EfInstagramMediaImportRepository(AnalyticsDbContext database)
    : IInstagramMediaImportRepository
{
    public async Task<MediaImportCheckpoint> PrepareAsync(
        Guid instagramAccountId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var checkpoint = await database.MediaImportCheckpoints.SingleOrDefaultAsync(
            item => item.InstagramAccountId == instagramAccountId,
            cancellationToken);
        if (checkpoint is null)
        {
            checkpoint = new MediaImportCheckpoint(instagramAccountId, nowUtc);
            database.MediaImportCheckpoints.Add(checkpoint);
        }
        else
        {
            checkpoint.Resume(nowUtc);
        }

        await database.SaveChangesAsync(cancellationToken);
        return checkpoint;
    }

    public async Task<MediaImportCheckpoint> PersistPageAsync(
        Guid instagramAccountId,
        IReadOnlyList<ImportedInstagramMedia> media,
        int fetchedCount,
        int failedCount,
        string? afterCursor,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var strategy = database.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken);
            var checkpoint = await RequiredCheckpointAsync(
                instagramAccountId,
                cancellationToken);
            var externalIds = media.Select(item => item.InstagramMediaId).ToArray();
            var existing = await database.InstagramMedia
                .Where(item => item.InstagramAccountId == instagramAccountId
                    && externalIds.Contains(item.InstagramMediaId))
                .ToDictionaryAsync(
                    item => item.InstagramMediaId,
                    StringComparer.Ordinal,
                    cancellationToken);
            var created = 0;
            var updated = 0;

            foreach (var item in media)
            {
                if (existing.TryGetValue(item.InstagramMediaId, out var persisted))
                {
                    persisted.UpdateMetadata(
                        item.MediaType,
                        item.Permalink,
                        item.Caption,
                        item.PublishedAtUtc);
                    updated++;
                }
                else
                {
                    database.InstagramMedia.Add(new InstagramMediaEntity(
                        instagramAccountId,
                        item.InstagramMediaId,
                        item.MediaType,
                        item.Permalink,
                        item.Caption,
                        item.PublishedAtUtc));
                    created++;
                }
            }

            checkpoint.RecordPage(
                afterCursor,
                fetchedCount,
                created,
                updated,
                failedCount,
                nowUtc);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return checkpoint;
        });
    }

    public async Task<MediaImportCheckpoint> CompleteAsync(
        Guid instagramAccountId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var checkpoint = await RequiredCheckpointAsync(
            instagramAccountId,
            cancellationToken);
        checkpoint.Complete(nowUtc);
        await database.SaveChangesAsync(cancellationToken);
        return checkpoint;
    }

    public async Task<MediaImportCheckpoint> FailAsync(
        Guid instagramAccountId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var checkpoint = await RequiredCheckpointAsync(
            instagramAccountId,
            cancellationToken);
        checkpoint.Fail(nowUtc);
        await database.SaveChangesAsync(cancellationToken);
        return checkpoint;
    }

    private async Task<MediaImportCheckpoint> RequiredCheckpointAsync(
        Guid instagramAccountId,
        CancellationToken cancellationToken) =>
        await database.MediaImportCheckpoints.SingleAsync(
            item => item.InstagramAccountId == instagramAccountId,
            cancellationToken);
}
