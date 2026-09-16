using Analytics.Api.InstagramCredentials.Application;
using Microsoft.AspNetCore.DataProtection;

namespace Analytics.Api.InstagramCredentials.Infrastructure;

public static class InstagramCredentialProtectionExtensions
{
    public const string KeyRingPathConfigurationKey = "DataProtection:KeyRingPath";

    public static IServiceCollection AddInstagramCredentialProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var configuredPath = configuration[KeyRingPathConfigurationKey];
        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName("Analytics.InstagramCredentials");

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            dataProtection.PersistKeysToFileSystem(
                new DirectoryInfo(Path.GetFullPath(configuredPath)));
        }

        services.AddSingleton<IInstagramTokenProtector, DataProtectionInstagramTokenProtector>();

        return services;
    }
}
