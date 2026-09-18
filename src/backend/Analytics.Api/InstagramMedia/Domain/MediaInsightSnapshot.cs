namespace Analytics.Api.InstagramMedia.Domain;

public sealed class MediaInsightSnapshot
{
    private MediaInsightSnapshot()
    {
    }

    public MediaInsightSnapshot(
        Guid instagramMediaId,
        MediaInsightMetrics metrics,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset? sourceTimestampUtc)
    {
        if (instagramMediaId == Guid.Empty)
        {
            throw new ArgumentException(
                "A media record is required.",
                nameof(instagramMediaId));
        }

        ArgumentNullException.ThrowIfNull(metrics);

        Id = Guid.NewGuid();
        InstagramMediaId = instagramMediaId;
        ViewsCount = NonNegative(metrics.ViewsCount, nameof(metrics.ViewsCount));
        ReachCount = NonNegative(metrics.ReachCount, nameof(metrics.ReachCount));
        LikesCount = NonNegative(metrics.LikesCount, nameof(metrics.LikesCount));
        CommentsCount = NonNegative(metrics.CommentsCount, nameof(metrics.CommentsCount));
        SavesCount = NonNegative(metrics.SavesCount, nameof(metrics.SavesCount));
        SharesCount = NonNegative(metrics.SharesCount, nameof(metrics.SharesCount));
        TotalInteractionsCount = NonNegative(
            metrics.TotalInteractionsCount,
            nameof(metrics.TotalInteractionsCount));
        AverageWatchTimeMilliseconds = NonNegative(
            metrics.AverageWatchTimeMilliseconds,
            nameof(metrics.AverageWatchTimeMilliseconds));
        TotalWatchTimeMilliseconds = NonNegative(
            metrics.TotalWatchTimeMilliseconds,
            nameof(metrics.TotalWatchTimeMilliseconds));
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
        SourceTimestampUtc = sourceTimestampUtc?.ToUniversalTime();
    }

    public Guid Id { get; private init; }

    public Guid InstagramMediaId { get; private init; }

    public long? ViewsCount { get; private init; }

    public long? ReachCount { get; private init; }

    public long? LikesCount { get; private init; }

    public long? CommentsCount { get; private init; }

    public long? SavesCount { get; private init; }

    public long? SharesCount { get; private init; }

    public long? TotalInteractionsCount { get; private init; }

    public long? AverageWatchTimeMilliseconds { get; private init; }

    public long? TotalWatchTimeMilliseconds { get; private init; }

    public DateTimeOffset CapturedAtUtc { get; private init; }

    public DateTimeOffset? SourceTimestampUtc { get; private init; }

    public InstagramMedia InstagramMedia { get; private init; } = null!;

    private static long? NonNegative(long? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Metric values cannot be negative.");
        }

        return value;
    }
}

public sealed record MediaInsightMetrics(
    long? ViewsCount,
    long? ReachCount,
    long? LikesCount,
    long? CommentsCount,
    long? SavesCount,
    long? SharesCount,
    long? TotalInteractionsCount,
    long? AverageWatchTimeMilliseconds,
    long? TotalWatchTimeMilliseconds);
