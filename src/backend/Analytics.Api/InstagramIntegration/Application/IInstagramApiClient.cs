using System.Net;

namespace Analytics.Api.InstagramIntegration.Application;

public interface IInstagramApiClient
{
    Task<InstagramApiResponse<T>> GetAsync<T>(
        string relativePath,
        string accessToken,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken cancellationToken);

    Task<InstagramApiPage<T>> GetPageAsync<T>(
        string relativePath,
        string accessToken,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken cancellationToken);

    Task<InstagramApiCollection<T>> GetAllPagesAsync<T>(
        string relativePath,
        string accessToken,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken cancellationToken);
}

public sealed record InstagramApiResponse<T>(
    T Data,
    InstagramUsageMetadata Usage);

public sealed record InstagramApiPage<T>(
    IReadOnlyList<T> Data,
    string? AfterCursor,
    InstagramUsageMetadata Usage);

public sealed record InstagramApiCollection<T>(
    IReadOnlyList<T> Data,
    IReadOnlyList<InstagramUsageMetadata> UsageByPage);

public sealed record InstagramUsageMetadata(
    string? AppUsage,
    string? BusinessUseCaseUsage,
    TimeSpan? RetryAfter);

public enum InstagramApiErrorKind
{
    Unauthorized,
    Forbidden,
    RateLimited,
    Transient,
    InvalidResponse,
    Permanent,
}

public sealed class InstagramApiException(
    InstagramApiErrorKind kind,
    string message,
    HttpStatusCode? statusCode = null,
    TimeSpan? retryAfter = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public InstagramApiErrorKind Kind { get; } = kind;

    public HttpStatusCode? StatusCode { get; } = statusCode;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
