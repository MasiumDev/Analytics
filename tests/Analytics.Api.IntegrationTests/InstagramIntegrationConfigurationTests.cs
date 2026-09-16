using Analytics.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Analytics.Api.IntegrationTests;

public sealed class InstagramIntegrationConfigurationTests
{
    [Fact]
    public async Task EnabledIntegrationWithoutRequiredSettings_FailsFastSafely()
    {
        await using var application = CreateProductionApplication(new Dictionary<string, string?>
        {
            ["Instagram:Enabled"] = "true",
            ["Instagram:AppId"] = "configured-app-id",
            ["Instagram:OAuthRedirectUri"] =
                "https://api.example.com/api/integrations/instagram/callback",
        });

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = application.CreateClient();
            await client.GetAsync("/");
        });

        Assert.Contains("Instagram:AppSecret", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("configured-app-id", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevelopmentAccessTokenInProduction_FailsFastWithoutLeakingValue()
    {
        const string developmentCredential = "temporary-development-credential";
        await using var application = CreateProductionApplication(new Dictionary<string, string?>
        {
            ["Instagram:DevelopmentAccessToken"] = developmentCredential,
        });

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = application.CreateClient();
            await client.GetAsync("/");
        });

        Assert.Contains("permitted only", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(developmentCredential, exception.ToString(), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateProductionApplication(
        IReadOnlyDictionary<string, string?> settings)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(
                $"ConnectionStrings:{DatabaseServiceCollectionExtensions.ConnectionStringName}",
                $"Server=(localdb)\\MSSQLLocalDB;Database=Analytics_Config_{Guid.NewGuid():N};" +
                "Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True");

            foreach (var setting in settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }
}
