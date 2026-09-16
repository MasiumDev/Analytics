namespace Analytics.Api.InstagramMedia.Domain;

public sealed class MediaCurrentStats
{
    private MediaCurrentStats()
    {
    }

    public MediaCurrentStats(
        Guid instagramMediaId,
        long? likeCount,
        long? commentsCount,
        long? savesCount,
        long? sharesCount,
        long? reachCount,
        long? playsCount,
        DateTimeOffset capturedAtUtc)
    {
        if (instagramMediaId == Guid.Empty)
        {
            throw new ArgumentException("A media record is required.", nameof(instagramMediaId));
        }

        InstagramMediaId = instagramMediaId;
        Update(
            likeCount,
            commentsCount,
            savesCount,
            sharesCount,
            reachCount,
            playsCount,
            capturedAtUtc);
    }

    public Guid InstagramMediaId { get; private init; }

    public long? LikeCount { get; private set; }

    public long? CommentsCount { get; private set; }

    public long? SavesCount { get; private set; }

    public long? SharesCount { get; private set; }

    public long? ReachCount { get; private set; }

    public long? PlaysCount { get; private set; }

    public DateTimeOffset CapturedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public InstagramMedia InstagramMedia { get; private init; } = null!;

    public void Update(
        long? likeCount,
        long? commentsCount,
        long? savesCount,
        long? sharesCount,
        long? reachCount,
        long? playsCount,
        DateTimeOffset capturedAtUtc)
    {
        LikeCount = NonNegative(likeCount, nameof(likeCount));
        CommentsCount = NonNegative(commentsCount, nameof(commentsCount));
        SavesCount = NonNegative(savesCount, nameof(savesCount));
        SharesCount = NonNegative(sharesCount, nameof(sharesCount));
        ReachCount = NonNegative(reachCount, nameof(reachCount));
        PlaysCount = NonNegative(playsCount, nameof(playsCount));
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
    }

    private static long? NonNegative(long? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Metric values cannot be negative.");
        }

        return value;
    }
}
