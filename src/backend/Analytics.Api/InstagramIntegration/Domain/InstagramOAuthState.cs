using Analytics.Api.Identity;

namespace Analytics.Api.InstagramIntegration.Domain;

public sealed class InstagramOAuthState
{
    private InstagramOAuthState()
    {
    }

    internal InstagramOAuthState(
        Guid ownerUserId,
        string stateHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        StateHash = stateHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private init; }

    public Guid OwnerUserId { get; private init; }

    public string StateHash { get; private init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset ExpiresAtUtc { get; private init; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public ApplicationUser Owner { get; private init; } = null!;

    internal bool TryConsume(DateTimeOffset now)
    {
        if (ConsumedAtUtc is not null || ExpiresAtUtc <= now)
        {
            return false;
        }

        ConsumedAtUtc = now;
        return true;
    }
}
