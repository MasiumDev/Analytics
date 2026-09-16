using System.Text.Json.Serialization;

namespace Analytics.Api.InstagramIntegration.Application;

public interface IInstagramOAuthClient
{
    Task<InstagramOAuthToken> ExchangeCodeAsync(
        string authorizationCode,
        CancellationToken cancellationToken);
}

public sealed record InstagramOAuthToken(
    [property: JsonIgnore] string AccessToken,
    string InstagramUserId,
    string[] GrantedScopes,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

public sealed class InstagramOAuthException(string message) : Exception(message);
