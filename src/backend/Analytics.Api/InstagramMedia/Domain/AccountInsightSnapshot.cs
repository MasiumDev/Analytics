using Analytics.Api.InstagramAccounts.Domain;

namespace Analytics.Api.InstagramMedia.Domain;

public sealed class AccountInsightSnapshot
{
    private AccountInsightSnapshot()
    {
    }

    public AccountInsightSnapshot(
        Guid instagramAccountId,
        AccountInsightMetrics metrics,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset? sourceTimestampUtc)
    {
        if (instagramAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "An Instagram account is required.",
                nameof(instagramAccountId));
        }

        ArgumentNullException.ThrowIfNull(metrics);

        Id = Guid.NewGuid();
        InstagramAccountId = instagramAccountId;
        ViewsCount = NonNegative(metrics.ViewsCount, nameof(metrics.ViewsCount));
        ReachCount = NonNegative(metrics.ReachCount, nameof(metrics.ReachCount));
        FollowerCount = NonNegative(metrics.FollowerCount, nameof(metrics.FollowerCount));
        ProfileViewsCount = NonNegative(
            metrics.ProfileViewsCount,
            nameof(metrics.ProfileViewsCount));
        WebsiteClicksCount = NonNegative(
            metrics.WebsiteClicksCount,
            nameof(metrics.WebsiteClicksCount));
        AccountsEngagedCount = NonNegative(
            metrics.AccountsEngagedCount,
            nameof(metrics.AccountsEngagedCount));
        TotalInteractionsCount = NonNegative(
            metrics.TotalInteractionsCount,
            nameof(metrics.TotalInteractionsCount));
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
        SourceTimestampUtc = sourceTimestampUtc?.ToUniversalTime();
    }

    public Guid Id { get; private init; }

    public Guid InstagramAccountId { get; private init; }

    public long? ViewsCount { get; private init; }

    public long? ReachCount { get; private init; }

    public long? FollowerCount { get; private init; }

    public long? ProfileViewsCount { get; private init; }

    public long? WebsiteClicksCount { get; private init; }

    public long? AccountsEngagedCount { get; private init; }

    public long? TotalInteractionsCount { get; private init; }

    public DateTimeOffset CapturedAtUtc { get; private init; }

    public DateTimeOffset? SourceTimestampUtc { get; private init; }

    public InstagramAccount InstagramAccount { get; private init; } = null!;

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

public sealed record AccountInsightMetrics(
    long? ViewsCount,
    long? ReachCount,
    long? FollowerCount,
    long? ProfileViewsCount,
    long? WebsiteClicksCount,
    long? AccountsEngagedCount,
    long? TotalInteractionsCount);
