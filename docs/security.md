# Application security baseline

The API applies defense in depth before any external OAuth integration is
introduced.

## Browser request protection

- Authentication uses an HttpOnly, Secure cookie in non-development
  environments.
- Every state-changing endpoint requires a matching antiforgery cookie and
  `X-CSRF-TOKEN` request header.
- Authentication mutations use a fixed-window limit of 10 requests per client
  per minute. Authenticated Instagram account mutations use 30 requests per
  user per minute.
- Five failed password attempts lock an account for 15 minutes independently
  of the network rate limit.

## Transport and response headers

Non-development environments redirect HTTP to HTTPS with status `308` and
enable HSTS. API responses include these headers:

- `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'`
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`
- `Permissions-Policy: camera=(), microphone=(), geolocation=()`

TLS is terminated either by ASP.NET Core or by a trusted deployment proxy. A
proxy deployment must preserve the original scheme and client address through
platform-managed forwarding; the application does not trust arbitrary public
forwarding headers.

## Tenant authorization

Tenant routes require an authenticated principal with a stable Identity user
ID. The API derives the owner from that principal, and repository queries repeat
the owner predicate. Cross-tenant reads and updates return `404` to avoid
confirming that another tenant's resource exists. Integration tests exercise
anonymous, positive-owner, and negative cross-owner paths against SQL Server.
