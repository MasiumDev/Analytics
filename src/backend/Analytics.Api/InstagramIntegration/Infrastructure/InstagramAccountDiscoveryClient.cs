using System.Net.Http.Headers;
using System.Text.Json;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramIntegration.Application;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Analytics.Api.InstagramIntegration.Infrastructure;

public sealed class InstagramAccountDiscoveryClient(
    HttpClient httpClient,
    IOptions<InstagramIntegrationOptions> options) : IInstagramAccountDiscoveryClient
{
    public async Task<InstagramDiscoveredAccount> DiscoverAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("An access token is required.", nameof(accessToken));
        }

        using var profileDocument = await GetJsonAsync(
            "me",
            new Dictionary<string, string?>
            {
                ["fields"] = "user_id,username,name,account_type",
            },
            accessToken,
            cancellationToken);
        using var permissionsDocument = await GetJsonAsync(
            "me/permissions",
            query: null,
            accessToken,
            cancellationToken);

        var profile = profileDocument.RootElement;
        return new InstagramDiscoveredAccount(
            RequiredIdentifier(profile, "user_id", fallbackPropertyName: "id"),
            RequiredString(profile, "username"),
            OptionalString(profile, "name"),
            ParseAccountType(OptionalString(profile, "account_type")),
            ReadGrantedScopes(permissionsDocument.RootElement));
    }

    private async Task<JsonDocument> GetJsonAsync(
        string relativePath,
        IReadOnlyDictionary<string, string?>? query,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{options.Value.GraphApiBaseUri.TrimEnd('/')}/{relativePath}";
        var requestUri = query is null
            ? endpoint
            : QueryHelpers.AddQueryString(endpoint, query);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InstagramDiscoveryException(
                "Instagram account discovery could not reach the provider.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InstagramDiscoveryException(
                "Instagram account discovery timed out.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InstagramDiscoveryException(
                    $"Instagram account discovery failed with status {(int)response.StatusCode}.");
            }

            try
            {
                await using var responseStream = await response.Content.ReadAsStreamAsync(
                    cancellationToken);
                return await JsonDocument.ParseAsync(
                    responseStream,
                    cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                throw new InstagramDiscoveryException(
                    "Instagram account discovery returned an invalid response.");
            }
        }
    }

    private static string RequiredIdentifier(
        JsonElement root,
        string propertyName,
        string? fallbackPropertyName = null)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            && (fallbackPropertyName is null
                || !root.TryGetProperty(fallbackPropertyName, out property)))
        {
            throw new InstagramDiscoveryException(
                $"Instagram profile omitted {propertyName}.");
        }

        var value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InstagramDiscoveryException(
                $"Instagram profile contained an invalid {propertyName}.");
        }

        return value;
    }

    private static string RequiredString(JsonElement root, string propertyName)
    {
        var value = OptionalString(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InstagramDiscoveryException(
                $"Instagram profile omitted {propertyName}.");
        }

        return value;
    }

    private static string? OptionalString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static InstagramProfessionalAccountType? ParseAccountType(string? accountType)
    {
        return accountType?.ToUpperInvariant() switch
        {
            "BUSINESS" => InstagramProfessionalAccountType.Business,
            "CREATOR" or "MEDIA_CREATOR" => InstagramProfessionalAccountType.Creator,
            _ => null,
        };
    }

    private static string[] ReadGrantedScopes(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            throw new InstagramDiscoveryException(
                "Instagram permissions response omitted data.");
        }

        return data
            .EnumerateArray()
            .Where(item => OptionalString(item, "status")
                ?.Equals("granted", StringComparison.OrdinalIgnoreCase) is true)
            .Select(item => OptionalString(item, "permission"))
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
