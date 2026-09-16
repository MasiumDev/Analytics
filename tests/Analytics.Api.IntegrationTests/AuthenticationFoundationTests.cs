using System.Net;
using Analytics.Api.Configuration;
using Analytics.Api.Data;
using Analytics.Api.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Analytics.Api.IntegrationTests;

public sealed class AuthenticationFoundationTests
{
    private const string AllowedOrigin = "https://app.example.com";

    [Fact]
    public async Task AnonymousProtectedRequest_ReturnsUnauthorizedWithoutRedirect()
    {
        await using var application = CreateProductionApplication(CreateConnectionString());
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetAsync("/api/auth/validate");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ValidIdentityUser_ReceivesSecureCookie_AndAccessesProtectedApi()
    {
        var connectionString = CreateConnectionString();
        var databaseOptions = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            await using var application = CreateProductionApplication(connectionString);

            await using (var migrationScope = application.Services.CreateAsyncScope())
            {
                var database = migrationScope.ServiceProvider
                    .GetRequiredService<AnalyticsDbContext>();
                await database.Database.MigrateAsync();
            }

            string setCookie;
            await using (var identityScope = application.Services.CreateAsyncScope())
            {
                var services = identityScope.ServiceProvider;
                var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
                var claimsFactory = services
                    .GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
                var cookieOptions = services
                    .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                    .Get(IdentityConstants.ApplicationScheme);
                var user = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = "identity-test@example.com",
                    Email = "identity-test@example.com",
                    EmailConfirmed = true,
                };

                var createResult = await userManager.CreateAsync(user);
                Assert.True(
                    createResult.Succeeded,
                    string.Join(", ", createResult.Errors.Select(error => error.Description)));

                var principal = await claimsFactory.CreateAsync(user);
                var context = new DefaultHttpContext
                {
                    RequestServices = services,
                };
                context.Request.Scheme = Uri.UriSchemeHttps;

                await context.SignInAsync(
                    IdentityConstants.ApplicationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        AllowRefresh = true,
                        IsPersistent = true,
                    });

                setCookie = context.Response.Headers.SetCookie.ToString();

                Assert.Equal("analytics.session", cookieOptions.Cookie.Name);
                Assert.True(cookieOptions.Cookie.HttpOnly);
                Assert.Equal(SameSiteMode.Lax, cookieOptions.Cookie.SameSite);
                Assert.Equal(CookieSecurePolicy.Always, cookieOptions.Cookie.SecurePolicy);
                Assert.True(cookieOptions.SlidingExpiration);
                Assert.Equal(TimeSpan.FromHours(8), cookieOptions.ExpireTimeSpan);
            }

            Assert.Contains("HttpOnly", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Secure", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SameSite=Lax", setCookie, StringComparison.OrdinalIgnoreCase);

            using var client = application.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = false,
            });
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/validate");
            request.Headers.TryAddWithoutValidation("Cookie", setCookie.Split(';')[0]);

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        finally
        {
            await using var database = new AnalyticsDbContext(databaseOptions);
            await database.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ConfiguredWebOrigin_ReceivesCredentialedCorsHeaders()
    {
        await using var application = CreateProductionApplication(CreateConnectionString());
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/validate");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            AllowedOrigin,
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(
            "true",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
    }

    private static WebApplicationFactory<Program> CreateProductionApplication(
        string connectionString)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(
                $"ConnectionStrings:{DatabaseServiceCollectionExtensions.ConnectionStringName}",
                connectionString);
            builder.UseSetting(
                $"{WebClientOptions.SectionName}:AllowedOrigins:0",
                AllowedOrigin);
        });
    }

    private static string CreateConnectionString()
    {
        return
            $"Server=(localdb)\\MSSQLLocalDB;Database=Analytics_Identity_{Guid.NewGuid():N};" +
            "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
    }
}
