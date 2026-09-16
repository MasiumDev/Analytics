using Analytics.Api.InstagramAccounts.Domain;

namespace Analytics.Api.InstagramIntegration.Application;

public interface IInstagramAccountDiscoveryClient
{
    Task<InstagramDiscoveredAccount> DiscoverAsync(
        string accessToken,
        CancellationToken cancellationToken);
}

public sealed record InstagramDiscoveredAccount(
    string InstagramUserId,
    string Username,
    string? DisplayName,
    InstagramProfessionalAccountType? ProfessionalAccountType,
    string[] GrantedScopes);

public sealed class InstagramDiscoveryException(string message) : Exception(message);
