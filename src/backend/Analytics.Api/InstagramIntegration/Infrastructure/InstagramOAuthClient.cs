using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Analytics.Api.InstagramIntegration.Application;
using Microsoft.Extensions.Options;

namespace Analytics.Api.InstagramIntegration.Infrastructure;

public sealed class InstagramOAuthClient(
    HttpClient httpClient,
    IOptions<InstagramIntegrationOptions> options,
    TimeProvider timeProvider) : IInstagramOAuthClient
{
    public async Task<InstagramOAuthToken> ExchangeCodeAsync(
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            options.Value.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.Value.AppId!,
                ["client_secret"] = options.Value.AppSecret!,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = options.Value.OAuthRedirectUri!,
                ["code"] = authorizationCode,
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InstagramOAuthException(
                "Instagram token exchange could not reach the provider.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InstagramOAuthException(
                "Instagram token exchange timed out.");
        }

        using (response)
        {
            return await ParseResponseAsync(response, cancellationToken);
        }
    }

    private InstagramOAuthToken ParseResponse(
        JsonElement root)
    {
        var accessToken = RequiredString(root, "access_token");
        var userId = RequiredIdentifier(root, "user_id");
        var issuedAt = timeProvider.GetUtcNow();
        DateTimeOffset? expiresAt = TryGetSeconds(root, "expires_in") is { } expiresIn
            ? issuedAt.AddSeconds(expiresIn)
            : null;
        var scopes = ReadScopes(root);

        return new InstagramOAuthToken(
            accessToken,
            userId,
            scopes.Length > 0
                ? scopes
                : InstagramOAuthFlowService.RequestedScopes,
            issuedAt,
            expiresAt);
    }

    private async Task<InstagramOAuthToken> ParseResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InstagramOAuthException(
                $"Instagram token exchange failed with status {(int)response.StatusCode}.");
        }

        try
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                responseStream,
                cancellationToken: cancellationToken);
            return ParseResponse(document.RootElement);
        }
        catch (JsonException)
        {
            throw new InstagramOAuthException(
                "Instagram token exchange returned an invalid response.");
        }
    }

    private static string RequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InstagramOAuthException(
                $"Instagram token response omitted {propertyName}.");
        }

        return property.GetString()!;
    }

    private static string RequiredIdentifier(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            throw new InstagramOAuthException(
                $"Instagram token response omitted {propertyName}.");
        }

        var value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InstagramOAuthException(
                $"Instagram token response contained an invalid {propertyName}.");
        }

        return value;
    }

    private static double? TryGetSeconds(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(
                property.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var number) => number,
            _ => null,
        };
    }

    private static string[] ReadScopes(JsonElement root)
    {
        if (!root.TryGetProperty("permissions", out var permissions))
        {
            return [];
        }

        if (permissions.ValueKind == JsonValueKind.Array)
        {
            return permissions
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();
        }

        return permissions.ValueKind == JsonValueKind.String
            ? permissions.GetString()!
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }
}
