namespace Analytics.Api.Configuration;

public sealed class WebClientOptions
{
    public const string SectionName = "WebClient";

    public string[] AllowedOrigins { get; init; } = [];
}
