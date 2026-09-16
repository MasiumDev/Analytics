using Analytics.Api.InstagramAccounts.Domain;

namespace Analytics.Api.InstagramMedia.Domain;

public sealed class InstagramMedia
{
    private InstagramMedia()
    {
    }

    public InstagramMedia(
        Guid instagramAccountId,
        string instagramMediaId,
        InstagramMediaType mediaType,
        string permalink,
        string? caption,
        DateTimeOffset publishedAtUtc)
    {
        if (instagramAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "An Instagram account is required.",
                nameof(instagramAccountId));
        }

        Id = Guid.NewGuid();
        InstagramAccountId = instagramAccountId;
        InstagramMediaId = RequiredValue(instagramMediaId, nameof(instagramMediaId), 64);
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdateMetadata(mediaType, permalink, caption, publishedAtUtc);
    }

    public Guid Id { get; private init; }

    public Guid InstagramAccountId { get; private init; }

    public string InstagramMediaId { get; private init; } = string.Empty;

    public InstagramMediaType MediaType { get; private set; }

    public string Permalink { get; private set; } = string.Empty;

    public string? Caption { get; private set; }

    public DateTimeOffset PublishedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public InstagramAccount InstagramAccount { get; private init; } = null!;

    public MediaCurrentStats? CurrentStats { get; private set; }

    public void UpdateMetadata(
        InstagramMediaType mediaType,
        string permalink,
        string? caption,
        DateTimeOffset publishedAtUtc)
    {
        if (!Enum.IsDefined(mediaType))
        {
            throw new ArgumentOutOfRangeException(nameof(mediaType));
        }

        MediaType = mediaType;
        Permalink = RequiredHttpsUri(permalink, nameof(permalink));
        Caption = OptionalValue(caption, nameof(caption), 2200);
        PublishedAtUtc = publishedAtUtc.ToUniversalTime();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string RequiredValue(
        string value,
        string parameterName,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
        {
            throw new ArgumentException(
                $"A value with at most {maxLength} characters is required.",
                parameterName);
        }

        return value.Trim();
    }

    private static string? OptionalValue(
        string? value,
        string parameterName,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maxLength} characters.",
                parameterName);
        }

        return trimmed;
    }

    private static string RequiredHttpsUri(string value, string parameterName)
    {
        var normalized = RequiredValue(value, parameterName, 2048);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "An absolute HTTPS permalink is required.",
                parameterName);
        }

        return normalized;
    }
}

public enum InstagramMediaType
{
    Image,
    Video,
    CarouselAlbum,
    Reel,
}
