using Analytics.Api.InstagramAccounts.Domain;

namespace Analytics.Api.InstagramMedia.Domain;

public sealed class AccountCurrentStats
{
    private AccountCurrentStats()
    {
    }

    public AccountCurrentStats(
        Guid instagramAccountId,
        long? followersCount,
        long? followsCount,
        long? mediaCount,
        DateTimeOffset capturedAtUtc)
    {
        if (instagramAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "An Instagram account is required.",
                nameof(instagramAccountId));
        }

        InstagramAccountId = instagramAccountId;
        Update(followersCount, followsCount, mediaCount, capturedAtUtc);
    }

    public Guid InstagramAccountId { get; private init; }

    public long? FollowersCount { get; private set; }

    public long? FollowsCount { get; private set; }

    public long? MediaCount { get; private set; }

    public DateTimeOffset CapturedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public InstagramAccount InstagramAccount { get; private init; } = null!;

    public void Update(
        long? followersCount,
        long? followsCount,
        long? mediaCount,
        DateTimeOffset capturedAtUtc)
    {
        FollowersCount = NonNegative(followersCount, nameof(followersCount));
        FollowsCount = NonNegative(followsCount, nameof(followsCount));
        MediaCount = NonNegative(mediaCount, nameof(mediaCount));
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
