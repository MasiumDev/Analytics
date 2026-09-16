using Analytics.Api.InstagramAccounts.Application;
using Analytics.Api.InstagramCredentials.Application;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Analytics.Api.InstagramIntegration.Application;

public sealed class InstagramOAuthFlowService(
    IInstagramOAuthStateService stateService,
    IInstagramOAuthClient oauthClient,
    IInstagramAccountDiscoveryClient discoveryClient,
    IInstagramAccountService accountService,
    IInstagramCredentialService credentialService,
    IOptions<InstagramIntegrationOptions> options) : IInstagramOAuthFlowService
{
    public static readonly string[] RequestedScopes =
    [
        "instagram_business_basic",
        "instagram_business_manage_insights",
    ];

    public async Task<Uri?> CreateAuthorizationUriAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return null;
        }

        var state = await stateService.CreateAsync(ownerUserId, cancellationToken);
        var authorizationUri = QueryHelpers.AddQueryString(
            options.Value.AuthorizationEndpoint,
            new Dictionary<string, string?>
            {
                ["client_id"] = options.Value.AppId,
                ["redirect_uri"] = options.Value.OAuthRedirectUri,
                ["response_type"] = "code",
                ["scope"] = string.Join(',', RequestedScopes),
                ["state"] = state,
            });

        return new Uri(authorizationUri);
    }

    public async Task<InstagramOAuthCompletionResult> CompleteAsync(
        Guid ownerUserId,
        string? state,
        string? authorizationCode,
        string? error,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return new(InstagramOAuthCompletionStatus.Disabled);
        }

        if (!await stateService.TryConsumeAsync(
                ownerUserId,
                state,
                cancellationToken))
        {
            return new(InstagramOAuthCompletionStatus.InvalidState);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            return new(InstagramOAuthCompletionStatus.Denied);
        }

        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            return new(InstagramOAuthCompletionStatus.InvalidCallback);
        }

        InstagramOAuthToken oauthToken;
        try
        {
            oauthToken = await oauthClient.ExchangeCodeAsync(
                authorizationCode,
                cancellationToken);
        }
        catch (InstagramOAuthException)
        {
            return new(InstagramOAuthCompletionStatus.ProviderFailure);
        }

        InstagramDiscoveredAccount discoveredAccount;
        try
        {
            discoveredAccount = await discoveryClient.DiscoverAsync(
                oauthToken.AccessToken,
                cancellationToken);
        }
        catch (InstagramDiscoveryException)
        {
            return new(InstagramOAuthCompletionStatus.ProviderFailure);
        }

        if (!discoveredAccount.InstagramUserId.Equals(
                oauthToken.InstagramUserId,
                StringComparison.Ordinal))
        {
            return new(InstagramOAuthCompletionStatus.ProviderFailure);
        }

        if (discoveredAccount.ProfessionalAccountType is null)
        {
            return new(InstagramOAuthCompletionStatus.UnsupportedAccount);
        }

        var missingScopes = RequestedScopes
            .Except(discoveredAccount.GrantedScopes, StringComparer.Ordinal)
            .ToArray();
        if (missingScopes.Length > 0)
        {
            return new(
                InstagramOAuthCompletionStatus.MissingScopes,
                MissingScopes: missingScopes);
        }

        var account = await accountService.FindByInstagramUserIdAsync(
            ownerUserId,
            discoveredAccount.InstagramUserId,
            cancellationToken);
        if (account is null)
        {
            var created = await accountService.CreateAsync(
                ownerUserId,
                discoveredAccount.InstagramUserId,
                discoveredAccount.Username,
                discoveredAccount.DisplayName,
                cancellationToken: cancellationToken);
            if (created.AlreadyConnected)
            {
                return new(InstagramOAuthCompletionStatus.AccountAlreadyOwned);
            }

            account = created.Account!;
        }

        account = await accountService.UpdateProfessionalProfileAsync(
            ownerUserId,
            account.Id,
            discoveredAccount.Username,
            discoveredAccount.DisplayName,
            discoveredAccount.ProfessionalAccountType.Value,
            cancellationToken);
        if (account is null)
        {
            return new(InstagramOAuthCompletionStatus.AccountAlreadyOwned);
        }

        var credential = await credentialService.StoreOrReplaceAsync(
            ownerUserId,
            account.Id,
            oauthToken.AccessToken,
            discoveredAccount.GrantedScopes,
            oauthToken.IssuedAtUtc,
            oauthToken.ExpiresAtUtc,
            cancellationToken);
        if (credential is null)
        {
            return new(InstagramOAuthCompletionStatus.AccountAlreadyOwned);
        }

        return new(
            InstagramOAuthCompletionStatus.Connected,
            new InstagramConnectionMetadata(
                account.Id,
                account.InstagramUserId,
                account.Username,
                account.ProfessionalAccountType!.Value.ToString(),
                credential.GrantedScopes,
                credential.ExpiresAtUtc,
                credential.Status));
    }
}
