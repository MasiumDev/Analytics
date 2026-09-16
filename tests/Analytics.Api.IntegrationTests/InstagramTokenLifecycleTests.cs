using System.Net;
using System.Net.Http.Json;
using Analytics.Api.Data;
using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts.Application;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramCredentials.Domain;
using Analytics.Api.InstagramCredentials.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Analytics.Api.IntegrationTests;

public sealed class InstagramTokenLifecycleTests
{
    private const string Password = "StrongPass123";
    private const string AccessToken = "token-lifecycle-integration-secret";

    [Fact]
    public async Task ExpiredCredential_RequiresReconnectAndStopsDependentJobs()
    {
        await WithApplication(async (application, client, lifecycleClient, jobs) =>
        {
            var accountId = await CreateConnectedAccountAsync(
                application,
                client,
                "expired-token@example.com",
                DateTimeOffset.UtcNow.AddMinutes(-1));

            var response = await client.GetAsync(
                $"/api/instagram-accounts/{accountId}/connection");
            var health = await response.Content.ReadFromJsonAsync<InstagramConnectionHealth>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(health);
            Assert.Equal("ReconnectRequired", health.ConnectionStatus);
            Assert.Equal("Expired", health.CredentialStatus);
            Assert.Empty(lifecycleClient.ValidatedTokens);
            Assert.Contains(accountId, jobs.StoppedAccountIds);
        });
    }

    [Fact]
    public async Task Disconnect_RevokesLocalCredentialStopsJobsAndPreventsFutureAccess()
    {
        await WithApplication(async (application, client, lifecycleClient, jobs) =>
        {
            var accountId = await CreateConnectedAccountAsync(
                application,
                client,
                "disconnect-token@example.com",
                DateTimeOffset.UtcNow.AddHours(1));
            lifecycleClient.UserId = "17841400000000000";

            var validated = await client.GetAsync(
                $"/api/instagram-accounts/{accountId}/connection");
            Assert.Equal(HttpStatusCode.OK, validated.StatusCode);
            Assert.Single(lifecycleClient.ValidatedTokens);

            var disconnected = await client.PostWithCsrfAsync(
                $"/api/instagram-accounts/{accountId}/disconnect");
            var responseBody = await disconnected.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, disconnected.StatusCode);
            Assert.DoesNotContain(AccessToken, responseBody, StringComparison.Ordinal);
            Assert.Single(lifecycleClient.RevokedTokens);
            Assert.Contains(accountId, jobs.StoppedAccountIds);

            await using (var scope = application.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                var account = await database.InstagramAccounts
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == accountId);
                var credential = await database.InstagramCredentials
                    .AsNoTracking()
                    .SingleAsync(item => item.InstagramAccountId == accountId);

                Assert.Equal(InstagramConnectionStatus.Disconnected, account.ConnectionStatus);
                Assert.Equal(InstagramCredentialStatus.Revoked, credential.Status);
                Assert.Null(credential.EncryptedAccessToken);
            }

            var validationCount = lifecycleClient.ValidatedTokens.Count;
            var afterDisconnect = await client.GetAsync(
                $"/api/instagram-accounts/{accountId}/connection");
            var health = await afterDisconnect.Content
                .ReadFromJsonAsync<InstagramConnectionHealth>();
            Assert.Equal(HttpStatusCode.OK, afterDisconnect.StatusCode);
            Assert.Equal("Disconnected", health!.ConnectionStatus);
            Assert.Equal(validationCount, lifecycleClient.ValidatedTokens.Count);
        });
    }

    [Fact]
    public async Task ProviderRevocation_RequiresReconnectWithoutExposingToken()
    {
        await WithApplication(async (application, client, lifecycleClient, jobs) =>
        {
            var accountId = await CreateConnectedAccountAsync(
                application,
                client,
                "revoked-token@example.com",
                DateTimeOffset.UtcNow.AddHours(1));
            lifecycleClient.Status = InstagramProviderTokenStatus.Revoked;

            var response = await client.GetAsync(
                $"/api/instagram-accounts/{accountId}/connection");
            var body = await response.Content.ReadAsStringAsync();
            var health = await response.Content.ReadFromJsonAsync<InstagramConnectionHealth>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("ReconnectRequired", health!.ConnectionStatus);
            Assert.Equal("Revoked", health.CredentialStatus);
            Assert.DoesNotContain(AccessToken, body, StringComparison.Ordinal);
            Assert.Contains(accountId, jobs.StoppedAccountIds);
        });
    }

    private static async Task<Guid> CreateConnectedAccountAsync(
        WebApplicationFactory<Program> application,
        HttpClient client,
        string email,
        DateTimeOffset expiresAtUtc)
    {
        var registration = await client.PostAsJsonWithCsrfAsync(
            "/api/auth/register",
            new RegisterRequest(email, Password));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        await using var scope = application.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var database = services.GetRequiredService<AnalyticsDbContext>();
        var ownerId = await database.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync();
        var accountService = services.GetRequiredService<IInstagramAccountService>();
        var created = await accountService.CreateAsync(
            ownerId,
            "17841400000000000",
            "lifecycle_brand",
            "Lifecycle Brand",
            CancellationToken.None);
        var account = created.Account!;
        await accountService.UpdateProfessionalProfileAsync(
            ownerId,
            account.Id,
            "lifecycle_brand",
            "Lifecycle Brand",
            InstagramProfessionalAccountType.Business,
            CancellationToken.None);
        var credentialService = services.GetRequiredService<IInstagramCredentialService>();
        await credentialService.StoreOrReplaceAsync(
            ownerId,
            account.Id,
            AccessToken,
            ["instagram_business_basic", "instagram_business_manage_insights"],
            DateTimeOffset.UtcNow.AddMinutes(-10),
            expiresAtUtc,
            CancellationToken.None);
        await accountService.UpdateConnectionStatusAsync(
            ownerId,
            account.Id,
            InstagramConnectionStatus.Connected,
            CancellationToken.None);
        return account.Id;
    }

    private static async Task WithApplication(
        Func<
            WebApplicationFactory<Program>,
            HttpClient,
            StubLifecycleClient,
            RecordingJobController,
            Task> test)
    {
        var databaseName = $"Analytics_TokenLifecycle_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        var keyRingPath = Path.Combine(
            Path.GetTempPath(),
            $"Analytics_TokenLifecycleKeys_{Guid.NewGuid():N}");
        var databaseOptions = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var lifecycleClient = new StubLifecycleClient();
        var jobs = new RecordingJobController();

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
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<IInstagramTokenLifecycleClient>();
                        services.RemoveAll<IInstagramDependentJobController>();
                        services.AddSingleton<IInstagramTokenLifecycleClient>(lifecycleClient);
                        services.AddSingleton<IInstagramDependentJobController>(jobs);
                    });
                });

            await using (var scope = application.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                await database.Database.MigrateAsync();
            }

            using var client = application.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://api.example.com"),
            });
            await test(application, client, lifecycleClient, jobs);
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

    private sealed class StubLifecycleClient : IInstagramTokenLifecycleClient
    {
        public InstagramProviderTokenStatus Status { get; set; } =
            InstagramProviderTokenStatus.Active;

        public string UserId { get; set; } = "17841400000000000";

        public List<string> ValidatedTokens { get; } = [];

        public List<string> RevokedTokens { get; } = [];

        public Task<InstagramTokenInspection> ValidateAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            ValidatedTokens.Add(accessToken);
            return Task.FromResult(new InstagramTokenInspection(Status, UserId));
        }

        public Task<bool> RevokeAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            RevokedTokens.Add(accessToken);
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingJobController : IInstagramDependentJobController
    {
        public List<Guid> StoppedAccountIds { get; } = [];

        public Task StopAsync(
            Guid instagramAccountId,
            CancellationToken cancellationToken)
        {
            StoppedAccountIds.Add(instagramAccountId);
            return Task.CompletedTask;
        }
    }
}
