namespace Analytics.Api.InstagramIntegration;

public sealed class InstagramIntegrationOptions
{
    public const string SectionName = "Instagram";

    public bool Enabled { get; init; }

    public string? AppId { get; init; }

    public string? AppSecret { get; init; }

    public string? OAuthRedirectUri { get; init; }

    public string GraphApiBaseUri { get; init; } = "https://graph.facebook.com";

    public string? DevelopmentAccessToken { get; init; }
}
