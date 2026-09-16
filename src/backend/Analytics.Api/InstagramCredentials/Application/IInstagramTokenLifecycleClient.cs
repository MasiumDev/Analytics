namespace Analytics.Api.InstagramCredentials.Application;

public interface IInstagramTokenLifecycleClient
{
    Task<InstagramTokenInspection> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<bool> RevokeAsync(
        string accessToken,
        CancellationToken cancellationToken);
}

public sealed record InstagramTokenInspection(
    InstagramProviderTokenStatus Status,
    string? InstagramUserId = null);

public enum InstagramProviderTokenStatus
{
    Active,
    Revoked,
    Invalid,
}

public sealed class InstagramTokenLifecycleException(string message) : Exception(message);
