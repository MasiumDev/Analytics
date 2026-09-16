using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Analytics.Api.Data;
using Analytics.Api.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Analytics.Api.IntegrationTests;

public sealed class AuthenticationEndpointTests
{
    private const string Email = "account@example.com";
    private const string Password = "StrongPass123";

    [Fact]
    public async Task AccountLifecycle_RegisterSessionLogoutAndLogin_Succeeds()
    {
        await WithMigratedApplication(async (_application, client) =>
        {
            var anonymousSession = await client
                .GetFromJsonAsync<AuthenticationStateResponse>("/api/auth/session");

            Assert.NotNull(anonymousSession);
            Assert.False(anonymousSession.IsAuthenticated);
            Assert.Null(anonymousSession.User);

            var registerResponse = await client.PostAsJsonAsync(
                "/api/auth/register",
                new RegisterRequest(Email, Password));
            var registeredSession = await registerResponse.Content
                .ReadFromJsonAsync<AuthenticationStateResponse>();

            Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
            Assert.NotNull(registeredSession);
            Assert.True(registeredSession.IsAuthenticated);
            Assert.Equal(Email, registeredSession.User?.Email);

            var currentSession = await client
                .GetFromJsonAsync<AuthenticationStateResponse>("/api/auth/session");
            Assert.True(currentSession?.IsAuthenticated);

            var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);
            Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

            var loggedOutSession = await client
                .GetFromJsonAsync<AuthenticationStateResponse>("/api/auth/session");
            Assert.NotNull(loggedOutSession);
            Assert.False(loggedOutSession.IsAuthenticated);

            var failedLogin = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(Email, "WrongPass123"));
            Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);
            Assert.Equal(
                "application/problem+json",
                failedLogin.Content.Headers.ContentType?.MediaType);

            var loginResponse = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(Email, Password));
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

            var restoredSession = await client
                .GetFromJsonAsync<AuthenticationStateResponse>("/api/auth/session");
            Assert.True(restoredSession?.IsAuthenticated);
            Assert.Equal(Email, restoredSession?.User?.Email);
        });
    }

    [Fact]
    public async Task RegistrationValidationAndDuplicateAccount_ReturnProblemDetails()
    {
        await WithMigratedApplication(async (_application, client) =>
        {
            var invalidResponse = await client.PostAsJsonAsync(
                "/api/auth/register",
                new RegisterRequest("not-an-email", string.Empty));
            var invalidProblem = await invalidResponse.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
            Assert.Equal(
                "application/problem+json",
                invalidResponse.Content.Headers.ContentType?.MediaType);
            Assert.True(invalidProblem.TryGetProperty("errors", out var errors));
            Assert.Equal(JsonValueKind.Object, errors.ValueKind);

            var firstResponse = await client.PostAsJsonAsync(
                "/api/auth/register",
                new RegisterRequest(Email, Password));
            var duplicateResponse = await client.PostAsJsonAsync(
                "/api/auth/register",
                new RegisterRequest(Email, Password));

            Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
            Assert.Equal(
                "application/problem+json",
                duplicateResponse.Content.Headers.ContentType?.MediaType);
        });
    }

    [Fact]
    public async Task RepeatedFailedLogin_LocksAccount()
    {
        await WithMigratedApplication(async (_application, client) =>
        {
            var registerResponse = await client.PostAsJsonAsync(
                "/api/auth/register",
                new RegisterRequest(Email, Password));
            Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

            await client.PostAsync("/api/auth/logout", content: null);

            HttpResponseMessage? lastFailure = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                lastFailure?.Dispose();
                lastFailure = await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new LoginRequest(Email, "WrongPass123"));
            }

            using (lastFailure)
            {
                Assert.NotNull(lastFailure);
                Assert.Equal(HttpStatusCode.TooManyRequests, lastFailure.StatusCode);
            }

            var correctPasswordWhileLocked = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(Email, Password));
            Assert.Equal(HttpStatusCode.TooManyRequests, correctPasswordWhileLocked.StatusCode);
        });
    }

    private static async Task WithMigratedApplication(
        Func<WebApplicationFactory<Program>, HttpClient, Task> test)
    {
        var connectionString = CreateConnectionString();
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

            using var client = application.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });

            await test(application, client);
        }
        finally
        {
            await using var database = new AnalyticsDbContext(databaseOptions);
            await database.Database.EnsureDeletedAsync();
        }
    }

    private static string CreateConnectionString()
    {
        return
            $"Server=(localdb)\\MSSQLLocalDB;Database=Analytics_AuthApi_{Guid.NewGuid():N};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
    }
}
