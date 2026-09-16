using Analytics.Api.Configuration;

namespace Analytics.Api.UnitTests;

public sealed class ConfigurationDefaultsTests
{
    [Fact]
    public void BrandingDefaults_DoNotEncodeTheRepositoryName()
    {
        var options = new BrandingOptions();

        Assert.Equal("Product", options.ProductName);
        Assert.DoesNotContain("Analytics", options.ProductName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalizationDefaults_ArePersianFirstAndUseIranTime()
    {
        var options = new LocalizationOptions();

        Assert.Equal("fa-IR", options.DefaultLocale);
        Assert.Equal("Asia/Tehran", options.DisplayTimeZone);
    }
}
