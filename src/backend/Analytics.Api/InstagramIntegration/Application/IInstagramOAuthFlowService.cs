namespace Analytics.Api.InstagramIntegration.Application;

public interface IInstagramOAuthFlowService
{
    Task<Uri?> CreateAuthorizationUriAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken);

    Task<InstagramOAuthCompletionResult> CompleteAsync(
        Guid ownerUserId,
        string? state,
        string? authorizationCode,
        string? error,
        CancellationToken cancellationToken);
}

public sealed record InstagramOAuthCompletionResult(
    InstagramOAuthCompletionStatus Status,
    InstagramConnectionMetadata? Connection = null);

public sealed record InstagramConnectionMetadata(
    Guid InstagramAccountId,
    string InstagramUserId,
    string Username,
    string[] GrantedScopes,
    DateTimeOffset? ExpiresAtUtc,
    string CredentialStatus);

public enum InstagramOAuthCompletionStatus
{
    Connected,
    Disabled,
    InvalidState,
    Denied,
    InvalidCallback,
    AccountAlreadyOwned,
    ProviderFailure,
}
