using Microsoft.Extensions.Options;
using Analytics.Api.InstagramIntegration.Application;
using Analytics.Api.InstagramIntegration.Infrastructure;

namespace Analytics.Api.InstagramIntegration;

public static class InstagramIntegrationConfigurationExtensions
{
    public static IServiceCollection AddInstagramIntegrationConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<InstagramIntegrationOptions>()
            .Bind(configuration.GetSection(InstagramIntegrationOptions.SectionName))
            .Validate(
                options => !options.Enabled || HasRequiredCredentials(options),
                "Instagram:AppId, Instagram:AppSecret, and Instagram:OAuthRedirectUri are required when Instagram integration is enabled.")
            .Validate(
                options => !options.Enabled || IsAbsoluteHttpsUri(options.OAuthRedirectUri),
                "Instagram:OAuthRedirectUri must be an absolute HTTPS URI when Instagram integration is enabled.")
            .Validate(
                options => IsAbsoluteHttpsUri(options.AuthorizationEndpoint),
                "Instagram:AuthorizationEndpoint must be an absolute HTTPS URI.")
            .Validate(
                options => IsAbsoluteHttpsUri(options.TokenEndpoint),
                "Instagram:TokenEndpoint must be an absolute HTTPS URI.")
            .Validate(
                options => IsAbsoluteHttpsUri(options.GraphApiBaseUri),
                "Instagram:GraphApiBaseUri must be an absolute HTTPS URI.")
            .Validate(
                options => options.StateLifetime > TimeSpan.Zero
                    && options.StateLifetime <= TimeSpan.FromHours(1),
                "Instagram:StateLifetime must be greater than zero and no longer than one hour.")
            .Validate(
                options => environment.IsDevelopment()
                    || string.IsNullOrWhiteSpace(options.DevelopmentAccessToken),
                "Instagram:DevelopmentAccessToken is permitted only in the Development environment.")
            .ValidateOnStart();

        services.AddSingleton(serviceProvider =>
            InstagramConfigurationDiagnostics.FromOptions(
                serviceProvider
                    .GetRequiredService<IOptions<InstagramIntegrationOptions>>()
                    .Value));
        services.AddSingleton(TimeProvider.System);
        services
            .AddHttpClient<IInstagramOAuthClient, InstagramOAuthClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
            });
        services.AddScoped<IInstagramOAuthFlowService, InstagramOAuthFlowService>();

        return services;
    }

    private static bool HasRequiredCredentials(InstagramIntegrationOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.AppId)
            && !string.IsNullOrWhiteSpace(options.AppSecret)
            && !string.IsNullOrWhiteSpace(options.OAuthRedirectUri);
    }

    private static bool IsAbsoluteHttpsUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }
}

public sealed record InstagramConfigurationDiagnostics(
    bool Enabled,
    bool AppIdConfigured,
    bool AppSecretConfigured,
    bool RedirectUriConfigured,
    bool DevelopmentAccessTokenConfigured)
{
    public static InstagramConfigurationDiagnostics FromOptions(
        InstagramIntegrationOptions options) =>
        new(
            options.Enabled,
            !string.IsNullOrWhiteSpace(options.AppId),
            !string.IsNullOrWhiteSpace(options.AppSecret),
            !string.IsNullOrWhiteSpace(options.OAuthRedirectUri),
            !string.IsNullOrWhiteSpace(options.DevelopmentAccessToken));
}
