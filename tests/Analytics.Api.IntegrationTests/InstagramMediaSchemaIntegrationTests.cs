using Analytics.Api.Data;
using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramMedia.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InstagramMediaEntity = Analytics.Api.InstagramMedia.Domain.InstagramMedia;

namespace Analytics.Api.IntegrationTests;

public sealed class InstagramMediaSchemaIntegrationTests
{
    [Fact]
    public async Task Migration_EnforcesAccountScopedExternalIdsAndCascadeOwnership()
    {
        var databaseName = $"Analytics_MediaSchema_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            await using var application = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting(
                        $"ConnectionStrings:{DatabaseServiceCollectionExtensions.ConnectionStringName}",
                        connectionString);
                });

            Guid firstAccountId;
            Guid firstMediaId;
            await using (var scope = application.Services.CreateAsyncScope())
            {
                var services = scope.ServiceProvider;
                var database = services.GetRequiredService<AnalyticsDbContext>();
                await database.Database.MigrateAsync();
                var users = services.GetRequiredService<UserManager<ApplicationUser>>();
                var owner = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = "media-schema@example.com",
                    Email = "media-schema@example.com",
                };
                Assert.True((await users.CreateAsync(owner)).Succeeded);

                var firstAccount = new InstagramAccount(
                    owner.Id,
                    "account-1",
                    "first_account");
                var secondAccount = new InstagramAccount(
                    owner.Id,
                    "account-2",
                    "second_account");
                database.InstagramAccounts.AddRange(firstAccount, secondAccount);
                await database.SaveChangesAsync();

                var capturedAt = new DateTimeOffset(
                    2026,
                    9,
                    17,
                    12,
                    0,
                    0,
                    TimeSpan.FromHours(3.5));
                var firstMedia = Media(firstAccount.Id, "shared-external-id", capturedAt);
                var secondMedia = Media(secondAccount.Id, "shared-external-id", capturedAt);
                database.InstagramMedia.AddRange(firstMedia, secondMedia);
                database.MediaCurrentStats.Add(new MediaCurrentStats(
                    firstMedia.Id,
                    10,
                    2,
                    3,
                    4,
                    100,
                    80,
                    capturedAt));
                database.AccountCurrentStats.Add(new AccountCurrentStats(
                    firstAccount.Id,
                    1000,
                    120,
                    40,
                    capturedAt));
                await database.SaveChangesAsync();

                firstAccountId = firstAccount.Id;
                firstMediaId = firstMedia.Id;
                var persisted = await database.InstagramMedia
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == firstMedia.Id);
                Assert.Equal(TimeSpan.Zero, persisted.PublishedAtUtc.Offset);
            }

            await using (var duplicateScope = application.Services.CreateAsyncScope())
            {
                var database = duplicateScope.ServiceProvider
                    .GetRequiredService<AnalyticsDbContext>();
                database.InstagramMedia.Add(Media(
                    firstAccountId,
                    "shared-external-id",
                    DateTimeOffset.UtcNow));
                await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
            }

            await using (var cascadeScope = application.Services.CreateAsyncScope())
            {
                var database = cascadeScope.ServiceProvider
                    .GetRequiredService<AnalyticsDbContext>();
                var account = await database.InstagramAccounts
                    .SingleAsync(item => item.Id == firstAccountId);
                database.InstagramAccounts.Remove(account);
                await database.SaveChangesAsync();

                Assert.False(await database.InstagramMedia.AnyAsync(
                    item => item.Id == firstMediaId));
                Assert.False(await database.MediaCurrentStats.AnyAsync(
                    item => item.InstagramMediaId == firstMediaId));
                Assert.False(await database.AccountCurrentStats.AnyAsync(
                    item => item.InstagramAccountId == firstAccountId));
            }
        }
        finally
        {
            await using var database = new AnalyticsDbContext(options);
            await database.Database.EnsureDeletedAsync();
        }
    }

    private static InstagramMediaEntity Media(
        Guid accountId,
        string externalId,
        DateTimeOffset publishedAt) =>
        new(
            accountId,
            externalId,
            InstagramMediaType.Image,
            $"https://www.instagram.com/p/{Guid.NewGuid():N}/",
            "Schema test",
            publishedAt);
}
