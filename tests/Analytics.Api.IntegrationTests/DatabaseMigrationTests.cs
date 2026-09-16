using System.Net;
using Analytics.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Api.IntegrationTests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task BaselineMigration_CreatesDatabase_AndReadinessIsHealthy()
    {
        var databaseName = $"Analytics_Integration_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            await using (var database = new AnalyticsDbContext(options))
            {
                await database.Database.MigrateAsync();

                Assert.True(await database.Database.CanConnectAsync());
                Assert.Contains(
                    await database.Database.GetAppliedMigrationsAsync(),
                    migration => migration.EndsWith(
                        "_InitialPersistenceBaseline",
                        StringComparison.Ordinal));
            }

            await using var application = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting(
                        $"ConnectionStrings:{DatabaseServiceCollectionExtensions.ConnectionStringName}",
                        connectionString);
                });
            using var client = application.CreateClient();

            var response = await client.GetAsync("/health/ready");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await using var database = new AnalyticsDbContext(options);
            await database.Database.EnsureDeletedAsync();
        }
    }
}
