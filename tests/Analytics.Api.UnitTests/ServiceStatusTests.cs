namespace Analytics.Api.UnitTests;

public sealed class ServiceStatusTests
{
    [Fact]
    public void Constructor_PreservesServiceAndStatus()
    {
        var status = new ServiceStatus("Analytics.Api", "ready");

        Assert.Equal("Analytics.Api", status.Service);
        Assert.Equal("ready", status.Status);
    }
}
