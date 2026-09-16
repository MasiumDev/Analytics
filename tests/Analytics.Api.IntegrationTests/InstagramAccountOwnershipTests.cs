using System.Net;
using System.Net.Http.Json;
using Analytics.Api.Data;
using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Analytics.Api.IntegrationTests;

public sealed class InstagramAccountOwnershipTests
{
    private const string Password = "StrongPass123";

    [Fact]
    public async Task AuthenticatedUsers_CanOnlyReadAndChangeTheirOwnAccounts()
    {
        await WithMigratedApplication(async (application, connectionString) =>
        {
            using var anonymous = CreateClient(application);
            using var alice = CreateClient(application);
            using var bob = CreateClient(application);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await anonymous.GetAsync("/api/instagram-accounts")).StatusCode);

            await RegisterAsync(alice, "alice@example.com");
            await RegisterAsync(bob, "bob@example.com");

            var aliceFirst = await CreateAccountAsync(
                alice,
                "instagram-alice-1",
                "alice_one");
            await CreateAccountAsync(alice, "instagram-alice-2", "alice_two");
            var bobAccount = await CreateAccountAsync(
                bob,
                "instagram-bob-1",
                "bob_one");

            var aliceAccounts = await alice.GetFromJsonAsync<InstagramAccountResponse[]>(
                "/api/instagram-accounts");
            var bobAccounts = await bob.GetFromJsonAsync<InstagramAccountResponse[]>(
                "/api/instagram-accounts");

            Assert.Equal(2, aliceAccounts?.Length);
            Assert.Single(bobAccounts!);
            Assert.DoesNotContain(aliceAccounts!, account => account.Id == bobAccount.Id);

            Assert.Equal(
                HttpStatusCode.NotFound,
                (await bob.GetAsync($"/api/instagram-accounts/{aliceFirst.Id}")).StatusCode);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await bob.PutAsJsonWithCsrfAsync(
                    $"/api/instagram-accounts/{aliceFirst.Id}",
                    new InstagramAccountProfileRequest("stolen_name"))).StatusCode);

            var updateResponse = await alice.PutAsJsonWithCsrfAsync(
                $"/api/instagram-accounts/{aliceFirst.Id}",
                new InstagramAccountProfileRequest("alice_updated", "Alice Brand"));
            var updated = await updateResponse.Content
                .ReadFromJsonAsync<InstagramAccountResponse>();

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            Assert.Equal("alice_updated", updated?.Username);

            await using var verificationDatabase = new AnalyticsDbContext(
                new DbContextOptionsBuilder<AnalyticsDbContext>()
                    .UseSqlServer(connectionString)
                    .Options);
            var persisted = await verificationDatabase.InstagramAccounts
                .AsNoTracking()
                .SingleAsync(account => account.Id == aliceFirst.Id);

            Assert.Equal("alice_updated", persisted.Username);
            Assert.NotEqual(bobAccount.Id, persisted.Id);
        });
    }

    [Fact]
    public async Task InstagramUserId_IsGloballyUniqueAcrossOwners()
    {
        await WithMigratedApplication(async (application, _connectionString) =>
        {
            using var firstOwner = CreateClient(application);
            using var secondOwner = CreateClient(application);

            await RegisterAsync(firstOwner, "first@example.com");
            await RegisterAsync(secondOwner, "second@example.com");
            await CreateAccountAsync(firstOwner, "same-instagram-id", "first_handle");

            var duplicate = await secondOwner.PostAsJsonWithCsrfAsync(
                "/api/instagram-accounts",
                new InstagramAccountRequest("same-instagram-id", "other_handle"));

            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal(
                "application/problem+json",
                duplicate.Content.Headers.ContentType?.MediaType);
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private static async Task RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonWithCsrfAsync(
            "/api/auth/register",
            new RegisterRequest(email, Password));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<InstagramAccountResponse> CreateAccountAsync(
        HttpClient client,
        string instagramUserId,
        string username)
    {
        var response = await client.PostAsJsonWithCsrfAsync(
            "/api/instagram-accounts",
            new InstagramAccountRequest(instagramUserId, username));
        var account = await response.Content.ReadFromJsonAsync<InstagramAccountResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<InstagramAccountResponse>(account);
    }

    private static async Task WithMigratedApplication(
        Func<WebApplicationFactory<Program>, string, Task> test)
    {
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database=Analytics_Ownership_{Guid.NewGuid():N};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        var databaseOptions = new DbContextOptionsBuilder<AnalyticsDbContext>()
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

            await using (var scope = application.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                await database.Database.MigrateAsync();
            }

            await test(application, connectionString);
        }
        finally
        {
            await using var database = new AnalyticsDbContext(databaseOptions);
            await database.Database.EnsureDeletedAsync();
        }
    }
}
