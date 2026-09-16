namespace Analytics.Api.Configuration;

public static class ApplicationConfigurationExtensions
{
    public static IServiceCollection AddApplicationConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<BrandingOptions>()
            .Bind(configuration.GetSection(BrandingOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ProductName),
                $"{BrandingOptions.SectionName}:ProductName is required.")
            .ValidateOnStart();

        services
            .AddOptions<LocalizationOptions>()
            .Bind(configuration.GetSection(LocalizationOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DefaultLocale),
                $"{LocalizationOptions.SectionName}:DefaultLocale is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DisplayTimeZone),
                $"{LocalizationOptions.SectionName}:DisplayTimeZone is required.")
            .ValidateOnStart();

        return services;
    }
}
