# Instagram API client foundation

`IInstagramApiClient` is the server-side boundary for authenticated Instagram
Graph API reads. Callers provide a relative path, optional non-secret query
parameters, and a token obtained from the encrypted credential store. The
client accepts no absolute URL and sends the token only in the HTTPS
`Authorization: Bearer` header.

The typed client supports single-object reads, one-page reads, and bounded
cursor pagination. Pagination rebuilds every request from the configured Graph
API base URI and the `paging.cursors.after` value; it never follows a provider
`next` URL. Repeated cursors and the configured maximum page count terminate
with an `InvalidResponse` domain error instead of looping indefinitely.

## Resilience and errors

Each request has a bounded timeout and honors caller cancellation. Network
failures, timeouts, HTTP 408, and HTTP 5xx responses use a small bounded retry
policy. HTTP 401, 403, and 429 map respectively to `Unauthorized`, `Forbidden`,
and `RateLimited`; other failures are normalized to safe domain errors.
Provider response bodies, access tokens, authorization headers, and request
URLs are not included in exceptions or application logs.

The client captures `x-app-usage`, `x-business-use-case-usage`, and
`Retry-After` metadata for scheduling and observability. Automated contract
tests use fake HTTP handlers, so no Meta account or real token is required.

## Configuration

| Key | Default | Purpose |
| --- | --- | --- |
| `Instagram:ApiRequestTimeout` | 30 seconds | Per-attempt timeout. |
| `Instagram:ApiMaxAttempts` | 3 | Total attempts for transient failures. |
| `Instagram:ApiRetryBaseDelay` | 200 ms | Linear delay base between attempts. |
| `Instagram:ApiMaxPageCount` | 100 | Hard upper bound for one pagination walk. |
