using Microsoft.EntityFrameworkCore;

namespace Analytics.Api.Data;

public static class DatabaseServiceCollectionExtensions
{
    public const string ConnectionStringName = "ApplicationDatabase";

    public static IServiceCollection AddApplicationDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is required.");
        }

        services.AddDbContext<AnalyticsDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlServer => sqlServer
                    .MigrationsAssembly(typeof(AnalyticsDbContext).Assembly.FullName)
                    .EnableRetryOnFailure()));

        return services;
    }
}
