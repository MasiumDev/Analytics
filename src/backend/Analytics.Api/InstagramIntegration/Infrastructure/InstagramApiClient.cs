using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Analytics.Api.InstagramIntegration.Application;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Analytics.Api.InstagramIntegration.Infrastructure;

public sealed class InstagramApiClient(
    HttpClient httpClient,
    IOptions<InstagramIntegrationOptions> options) : IInstagramApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<InstagramApiResponse<T>> GetAsync<T>(
        string relativePath,
        string accessToken,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken cancellationToken)
    {
        using var response = await SendGetAsync(
            relativePath,
            accessToken,
            query,
            cancellationToken);
        var usage = ReadUsage(response);
        var data = await DeserializeAsync<T>(response, cancellationToken);
        return new InstagramApiResponse<T>(data, usage);
    }

    public async Task<InstagramApiPage<T>> GetPageAsync<T>(
        string relativePath,
        string accessToken,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken cancellationToken)
    {
        using var response = await SendGetAsync(
            relativePath,
            accessToken,
            query,
            cancellationToken);
        var usage = ReadUsage(response);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                throw InvalidResponse();
            }

            var items = data.Deserialize<List<T>>(SerializerOptions);
            if (items is null)
            {
                throw InvalidResponse();
            }

            return new InstagramApiPage<T>(
                items,
                ReadAfterCursor(document.RootElement),
                usage);
        }
        catch (JsonException exception)
        {
            throw InvalidResponse(exception);
        }
    }

    public async Task<InstagramApiCollection<T>> GetAllPagesAsync<T>(
        string relativePath,
        string accessToken,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken cancellationToken)
    {
        var allItems = new List<T>();
        var usageByPage = new List<InstagramUsageMetadata>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var pageQuery = query is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(query, StringComparer.Ordinal);

        for (var pageNumber = 1; pageNumber <= options.Value.ApiMaxPageCount; pageNumber++)
        {
            var page = await GetPageAsync<T>(
                relativePath,
                accessToken,
                pageQuery,
                cancellationToken);
            allItems.AddRange(page.Data);
            usageByPage.Add(page.Usage);

            if (string.IsNullOrWhiteSpace(page.AfterCursor))
            {
                return new InstagramApiCollection<T>(allItems, usageByPage);
            }

            if (!seenCursors.Add(page.AfterCursor))
            {
                throw new InstagramApiException(
                    InstagramApiErrorKind.InvalidResponse,
                    "Instagram pagination returned a repeated cursor.");
            }

            pageQuery["after"] = page.AfterCursor;
        }

        throw new InstagramApiException(
            InstagramApiErrorKind.InvalidResponse,
            "Instagram pagination exceeded the configured page limit.");
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        string relativePath,
        string accessToken,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("An access token is required.", nameof(accessToken));
        }

        var endpoint = ComposeEndpoint(relativePath, query);
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= options.Value.ApiMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Value.ApiRequestTimeout);

            try
            {
                var response = await httpClient.SendAsync(request, timeout.Token);
                if (!IsTransient(response.StatusCode)
                    || attempt == options.Value.ApiMaxAttempts)
                {
                    try
                    {
                        EnsureSuccess(response);
                    }
                    catch
                    {
                        response.Dispose();
                        throw;
                    }

                    return response;
                }

                lastFailure = new InstagramApiException(
                    InstagramApiErrorKind.Transient,
                    "Instagram returned a temporary failure.",
                    response.StatusCode);
                response.Dispose();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception)
            {
                lastFailure = new InstagramApiException(
                    InstagramApiErrorKind.Transient,
                    "Instagram request timed out.",
                    innerException: exception);
            }
            catch (HttpRequestException exception)
            {
                lastFailure = new InstagramApiException(
                    InstagramApiErrorKind.Transient,
                    "Instagram could not be reached.",
                    innerException: exception);
            }

            if (attempt < options.Value.ApiMaxAttempts)
            {
                var delay = TimeSpan.FromTicks(
                    options.Value.ApiRetryBaseDelay.Ticks * attempt);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw lastFailure ?? new InstagramApiException(
            InstagramApiErrorKind.Transient,
            "Instagram request failed temporarily.");
    }

    private string ComposeEndpoint(
        string relativePath,
        IReadOnlyDictionary<string, string?>? query)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Uri.TryCreate(relativePath, UriKind.Absolute, out _)
            || relativePath.Contains('?')
            || relativePath.Contains('#'))
        {
            throw new ArgumentException(
                "A relative Instagram API path is required.",
                nameof(relativePath));
        }

        if (query?.Keys.Any(key => key.Equals(
                "access_token",
                StringComparison.OrdinalIgnoreCase)) is true)
        {
            throw new ArgumentException(
                "Access tokens must be supplied through the server-side credential parameter.",
                nameof(query));
        }

        var endpoint =
            $"{options.Value.GraphApiBaseUri.TrimEnd('/')}/{relativePath.TrimStart('/')}";
        return query is null || query.Count == 0
            ? endpoint
            : QueryHelpers.AddQueryString(endpoint, query);
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var value = await JsonSerializer.DeserializeAsync<T>(
                stream,
                SerializerOptions,
                cancellationToken);
            return value is null ? throw InvalidResponse() : value;
        }
        catch (JsonException exception)
        {
            throw InvalidResponse(exception);
        }
    }

    private static string? ReadAfterCursor(JsonElement root)
    {
        if (!root.TryGetProperty("paging", out var paging)
            || paging.ValueKind != JsonValueKind.Object
            || !paging.TryGetProperty("cursors", out var cursors)
            || cursors.ValueKind != JsonValueKind.Object
            || !cursors.TryGetProperty("after", out var after)
            || after.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(after.GetString()) ? null : after.GetString();
    }

    private static InstagramUsageMetadata ReadUsage(HttpResponseMessage response) => new(
        Header(response, "x-app-usage"),
        Header(response, "x-business-use-case-usage"),
        response.Headers.RetryAfter?.Delta);

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? string.Join(',', values)
            : null;

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || (int)statusCode >= 500;

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => InstagramApiErrorKind.Unauthorized,
            HttpStatusCode.Forbidden => InstagramApiErrorKind.Forbidden,
            HttpStatusCode.TooManyRequests => InstagramApiErrorKind.RateLimited,
            HttpStatusCode.RequestTimeout => InstagramApiErrorKind.Transient,
            _ when (int)response.StatusCode >= 500 => InstagramApiErrorKind.Transient,
            _ => InstagramApiErrorKind.Permanent,
        };
        throw new InstagramApiException(
            kind,
            $"Instagram API request failed with status {(int)response.StatusCode}.",
            response.StatusCode,
            response.Headers.RetryAfter?.Delta);
    }

    private static InstagramApiException InvalidResponse(Exception? innerException = null) =>
        new(
            InstagramApiErrorKind.InvalidResponse,
            "Instagram API returned an invalid response.",
            innerException: innerException);
}
