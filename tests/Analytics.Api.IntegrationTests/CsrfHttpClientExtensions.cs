using System.Net.Http.Json;
using Analytics.Api.Security;

namespace Analytics.Api.IntegrationTests;

internal static class CsrfHttpClientExtensions
{
    public static Task<HttpResponseMessage> PostAsJsonWithCsrfAsync<TValue>(
        this HttpClient client,
        string requestUri,
        TValue value) =>
        SendWithCsrfAsync(client, HttpMethod.Post, requestUri, JsonContent.Create(value));

    public static Task<HttpResponseMessage> PutAsJsonWithCsrfAsync<TValue>(
        this HttpClient client,
        string requestUri,
        TValue value) =>
        SendWithCsrfAsync(client, HttpMethod.Put, requestUri, JsonContent.Create(value));

    public static Task<HttpResponseMessage> PostWithCsrfAsync(
        this HttpClient client,
        string requestUri) =>
        SendWithCsrfAsync(client, HttpMethod.Post, requestUri, content: null);

    private static async Task<HttpResponseMessage> SendWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        HttpContent? content)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryTokenResponse>(
            "/api/auth/csrf");
        ArgumentNullException.ThrowIfNull(token);

        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation(token.HeaderName, token.RequestToken);
        return await client.SendAsync(request);
    }
}
