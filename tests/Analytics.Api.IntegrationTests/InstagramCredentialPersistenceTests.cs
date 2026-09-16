using System.Text.Json;
using Analytics.Api.Data;
using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramCredentials.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Analytics.Api.IntegrationTests;

public sealed class InstagramCredentialPersistenceTests
{
    [Fact]
    public async Task CredentialService_StoresOnlyCiphertextAndReturnsOnlyMetadata()
    {
        var databaseName = $"Analytics_Credential_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        var keyRingPath = Path.Combine(
            Path.GetTempPath(),
            $"Analytics_CredentialKeys_{Guid.NewGuid():N}");
        var databaseOptions = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        const string accessToken = "integration-test-access-credential";

        try
        {
            await using var application = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting(
                        $"ConnectionStrings:{DatabaseServiceCollectionExtensions.ConnectionStringName}",
                        connectionString);
                    builder.UseSetting(
                        InstagramCredentialProtectionExtensions.KeyRingPathConfigurationKey,
                        keyRingPath);
                });

            await using var scope = application.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var database = services.GetRequiredService<AnalyticsDbContext>();
            await database.Database.MigrateAsync();

            var owner = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "credential-owner@example.com",
                Email = "credential-owner@example.com",
            };
            var otherOwner = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "other-owner@example.com",
                Email = "other-owner@example.com",
            };
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await userManager.CreateAsync(owner)).Succeeded);
            Assert.True((await userManager.CreateAsync(otherOwner)).Succeeded);

            var account = new InstagramAccount(
                owner.Id,
                "instagram-credential-test",
                "credential_test");
            database.InstagramAccounts.Add(account);
            await database.SaveChangesAsync();

            var credentialService = services.GetRequiredService<IInstagramCredentialService>();
            var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var expiresAt = issuedAt.AddDays(60);
            var metadata = await credentialService.StoreOrReplaceAsync(
                owner.Id,
                account.Id,
                accessToken,
                ["instagram_business_basic", "instagram_business_manage_insights"],
                issuedAt,
                expiresAt,
                CancellationToken.None);

            var persisted = await database.InstagramCredentials
                .AsNoTracking()
                .SingleAsync(item => item.InstagramAccountId == account.Id);
            var protector = services.GetRequiredService<IInstagramTokenProtector>();

            Assert.NotNull(metadata);
            Assert.NotEqual(accessToken, persisted.EncryptedAccessToken);
            Assert.DoesNotContain(
                accessToken,
                persisted.EncryptedAccessToken!,
                StringComparison.Ordinal);
            Assert.True(protector.TryUnprotect(
                persisted.EncryptedAccessToken!,
                out var decrypted));
            Assert.Equal(accessToken, decrypted);
            Assert.NotEmpty(persisted.RowVersion);

            var responseJson = JsonSerializer.Serialize(metadata);
            Assert.DoesNotContain(accessToken, responseJson, StringComparison.Ordinal);
            Assert.DoesNotContain(
                persisted.EncryptedAccessToken!,
                responseJson,
                StringComparison.Ordinal);

            var denied = await credentialService.StoreOrReplaceAsync(
                otherOwner.Id,
                account.Id,
                "other-test-access-credential",
                [],
                issuedAt,
                expiresAt,
                CancellationToken.None);
            Assert.Null(denied);
            Assert.Single(await database.InstagramCredentials.ToListAsync());
        }
        finally
        {
            await using var database = new AnalyticsDbContext(databaseOptions);
            await database.Database.EnsureDeletedAsync();
            if (Directory.Exists(keyRingPath))
            {
                Directory.Delete(keyRingPath, recursive: true);
            }
        }
    }
}
