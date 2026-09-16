namespace Analytics.Api.InstagramIntegration;

public sealed class InstagramIntegrationOptions
{
    public const string SectionName = "Instagram";

    public bool Enabled { get; init; }

    public string? AppId { get; init; }

    public string? AppSecret { get; init; }

    public string? OAuthRedirectUri { get; init; }

    public string AuthorizationEndpoint { get; init; } =
        "https://www.instagram.com/oauth/authorize";

    public string TokenEndpoint { get; init; } =
        "https://api.instagram.com/oauth/access_token";

    public string GraphApiBaseUri { get; init; } = "https://graph.instagram.com";

    public TimeSpan StateLifetime { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan ApiRequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int ApiMaxAttempts { get; init; } = 3;

    public TimeSpan ApiRetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    public int ApiMaxPageCount { get; init; } = 100;

    public string? DevelopmentAccessToken { get; init; }
}
