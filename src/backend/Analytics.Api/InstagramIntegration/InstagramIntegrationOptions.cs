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

    public string? DevelopmentAccessToken { get; init; }
}
