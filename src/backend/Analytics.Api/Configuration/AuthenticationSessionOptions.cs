namespace Analytics.Api.Configuration;

public sealed class AuthenticationSessionOptions
{
    public const string SectionName = "Authentication";

    public string CookieName { get; init; } = "analytics.session";

    public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(8);

    public bool SlidingExpiration { get; init; } = true;
}
