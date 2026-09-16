using Analytics.Api.Identity;
using Analytics.Api.InstagramCredentials.Domain;

namespace Analytics.Api.InstagramAccounts.Domain;

public sealed class InstagramAccount
{
    private InstagramAccount()
    {
    }

    public InstagramAccount(
        Guid ownerUserId,
        string instagramUserId,
        string username,
        string? displayName = null)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("An owner is required.", nameof(ownerUserId));
        }

        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        InstagramUserId = RequiredValue(instagramUserId, nameof(instagramUserId));
        UpdateProfile(username, displayName);
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private init; }

    public Guid OwnerUserId { get; private init; }

    public string InstagramUserId { get; private init; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    public string? DisplayName { get; private set; }

    public InstagramProfessionalAccountType? ProfessionalAccountType { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public ApplicationUser Owner { get; private init; } = null!;

    public InstagramCredential? Credential { get; private set; }

    public void UpdateProfile(string username, string? displayName)
    {
        Username = RequiredValue(username, nameof(username));
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void UpdateProfessionalProfile(
        string username,
        string? displayName,
        InstagramProfessionalAccountType accountType)
    {
        if (accountType is not (
                InstagramProfessionalAccountType.Business
                or InstagramProfessionalAccountType.Creator))
        {
            throw new ArgumentException(
                "A Business or Creator account type is required.",
                nameof(accountType));
        }

        UpdateProfile(username, displayName);
        ProfessionalAccountType = accountType;
    }

    private static string RequiredValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        return value.Trim();
    }
}

public enum InstagramProfessionalAccountType
{
    Business,
    Creator,
}
