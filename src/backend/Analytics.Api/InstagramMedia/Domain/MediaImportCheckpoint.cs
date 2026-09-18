using Analytics.Api.InstagramAccounts.Domain;

namespace Analytics.Api.InstagramMedia.Domain;

public sealed class MediaImportCheckpoint
{
    private MediaImportCheckpoint()
    {
    }

    public MediaImportCheckpoint(Guid instagramAccountId, DateTimeOffset startedAtUtc)
    {
        if (instagramAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "An Instagram account is required.",
                nameof(instagramAccountId));
        }

        InstagramAccountId = instagramAccountId;
        Restart(startedAtUtc);
    }

    public Guid InstagramAccountId { get; private init; }

    public string? AfterCursor { get; private set; }

    public InstagramMediaImportStatus Status { get; private set; }

    public int PagesProcessed { get; private set; }

    public int FetchedCount { get; private set; }

    public int CreatedCount { get; private set; }

    public int UpdatedCount { get; private set; }

    public int FailedCount { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public InstagramAccount InstagramAccount { get; private init; } = null!;

    public void Resume(DateTimeOffset nowUtc)
    {
        if (Status == InstagramMediaImportStatus.Completed)
        {
            Restart(nowUtc);
            return;
        }

        Status = InstagramMediaImportStatus.Running;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    public void RecordPage(
        string? afterCursor,
        int fetched,
        int created,
        int updated,
        int failed,
        DateTimeOffset nowUtc)
    {
        AfterCursor = string.IsNullOrWhiteSpace(afterCursor) ? null : afterCursor;
        PagesProcessed++;
        FetchedCount += NonNegative(fetched, nameof(fetched));
        CreatedCount += NonNegative(created, nameof(created));
        UpdatedCount += NonNegative(updated, nameof(updated));
        FailedCount += NonNegative(failed, nameof(failed));
        Status = InstagramMediaImportStatus.Running;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    public void Complete(DateTimeOffset nowUtc)
    {
        AfterCursor = null;
        Status = InstagramMediaImportStatus.Completed;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
        CompletedAtUtc = UpdatedAtUtc;
    }

    public void Fail(DateTimeOffset nowUtc)
    {
        Status = InstagramMediaImportStatus.Failed;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    private void Restart(DateTimeOffset nowUtc)
    {
        AfterCursor = null;
        Status = InstagramMediaImportStatus.Running;
        PagesProcessed = 0;
        FetchedCount = 0;
        CreatedCount = 0;
        UpdatedCount = 0;
        FailedCount = 0;
        StartedAtUtc = nowUtc.ToUniversalTime();
        UpdatedAtUtc = StartedAtUtc;
        CompletedAtUtc = null;
    }

    private static int NonNegative(int value, string parameterName) =>
        value < 0
            ? throw new ArgumentOutOfRangeException(parameterName)
            : value;
}

public enum InstagramMediaImportStatus
{
    Running,
    Completed,
    Failed,
}
