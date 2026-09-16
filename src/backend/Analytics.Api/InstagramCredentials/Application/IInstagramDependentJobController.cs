namespace Analytics.Api.InstagramCredentials.Application;

public interface IInstagramDependentJobController
{
    Task StopAsync(Guid instagramAccountId, CancellationToken cancellationToken);
}

public sealed class NoOpInstagramDependentJobController : IInstagramDependentJobController
{
    public Task StopAsync(
        Guid instagramAccountId,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
