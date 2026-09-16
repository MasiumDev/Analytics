using Analytics.Api.InstagramIntegration;

namespace Analytics.Api.UnitTests;

public sealed class InstagramConfigurationDiagnosticsTests
{
    [Fact]
    public void Diagnostics_ExposeOnlyConfigurationPresence()
    {
        var options = new InstagramIntegrationOptions
        {
            Enabled = true,
            AppId = "private-app-id",
            AppSecret = "private-app-secret",
            OAuthRedirectUri = "https://api.example.com/callback",
            DevelopmentAccessToken = "private-development-token",
        };

        var diagnostics = InstagramConfigurationDiagnostics.FromOptions(options);
        var rendered = diagnostics.ToString();

        Assert.True(diagnostics.Enabled);
        Assert.True(diagnostics.AppIdConfigured);
        Assert.True(diagnostics.AppSecretConfigured);
        Assert.True(diagnostics.RedirectUriConfigured);
        Assert.True(diagnostics.DevelopmentAccessTokenConfigured);
        Assert.DoesNotContain(options.AppId, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(options.AppSecret, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(options.OAuthRedirectUri, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(options.DevelopmentAccessToken, rendered, StringComparison.Ordinal);
    }
}
