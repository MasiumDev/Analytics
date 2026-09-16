using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramIntegration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Analytics.Api.InstagramCredentials.Infrastructure;

public sealed class InstagramTokenLifecycleClient(
    HttpClient httpClient,
    IOptions<InstagramIntegrationOptions> options) : IInstagramTokenLifecycleClient
{
    public async Task<InstagramTokenInspection> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var endpoint = QueryHelpers.AddQueryString(
            $"{options.Value.GraphApiBaseUri.TrimEnd('/')}/me",
            "fields",
            "user_id");
        using var request = CreateRequest(HttpMethod.Get, endpoint, accessToken);
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new InstagramTokenInspection(InstagramProviderTokenStatus.Revoked);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InstagramTokenLifecycleException(
                $"Instagram token validation failed with status {(int)response.StatusCode}.");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!TryReadIdentifier(root, "user_id", out var userId)
                && !TryReadIdentifier(root, "id", out userId))
            {
                return new InstagramTokenInspection(InstagramProviderTokenStatus.Invalid);
            }

            return new InstagramTokenInspection(
                InstagramProviderTokenStatus.Active,
                userId);
        }
        catch (JsonException)
        {
            return new InstagramTokenInspection(InstagramProviderTokenStatus.Invalid);
        }
    }

    public async Task<bool> RevokeAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{options.Value.GraphApiBaseUri.TrimEnd('/')}/me/permissions";
        using var request = CreateRequest(HttpMethod.Delete, endpoint, accessToken);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                || response.StatusCode is HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InstagramTokenLifecycleException(
                "Instagram token validation could not reach the provider.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InstagramTokenLifecycleException(
                "Instagram token validation timed out.");
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string endpoint,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static bool TryReadIdentifier(
        JsonElement root,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
        return !string.IsNullOrWhiteSpace(value);
    }
}
