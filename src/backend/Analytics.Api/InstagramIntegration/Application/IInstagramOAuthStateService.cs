namespace Analytics.Api.InstagramIntegration.Application;

public interface IInstagramOAuthStateService
{
    Task<string> CreateAsync(Guid ownerUserId, CancellationToken cancellationToken);

    Task<bool> TryConsumeAsync(
        Guid ownerUserId,
        string? state,
        CancellationToken cancellationToken);
}
