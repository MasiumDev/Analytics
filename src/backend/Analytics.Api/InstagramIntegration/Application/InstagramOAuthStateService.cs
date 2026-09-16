using System.Security.Cryptography;
using System.Text;
using Analytics.Api.InstagramIntegration.Domain;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Analytics.Api.InstagramIntegration.Application;

public sealed class InstagramOAuthStateService(
    IInstagramOAuthStateRepository repository,
    IOptions<InstagramIntegrationOptions> options,
    TimeProvider timeProvider) : IInstagramOAuthStateService
{
    public async Task<string> CreateAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = timeProvider.GetUtcNow();
        var oauthState = new InstagramOAuthState(
            ownerUserId,
            Hash(state),
            now,
            now.Add(options.Value.StateLifetime));

        await repository.AddAsync(oauthState, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return state;
    }

    public Task<bool> TryConsumeAsync(
        Guid ownerUserId,
        string? state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state) || state.Length > 128)
        {
            return Task.FromResult(false);
        }

        return repository.TryConsumeAsync(
            Hash(state),
            ownerUserId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static string Hash(string state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
}
