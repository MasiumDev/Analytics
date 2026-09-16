# ADR 0003 Configuration branding time and API errors

- Status: Accepted
- Date: 2026-09-16

## Context

The product name is intentionally undecided. The MVP is Persian-first, stores
historical measurements, runs in multiple environments, and must never place
Instagram credentials in source control or browser bundles.

## Decision

### Configuration and secrets

Safe defaults live in versioned configuration. Environment-specific operational
values and every secret come from providers outside source control. Local
development uses ASP.NET Core User Secrets for server secrets and ignored local
environment files for non-committed frontend values. Staging and production use
deployment environment variables or a managed secret store.

No secret may use a `NEXT_PUBLIC_` name because Next.js exposes those values to
the browser. Startup validates required typed server options. The detailed
provider order and environment matrix are documented in `docs/configuration.md`.

### Branding and localization

`Branding:ProductName` and the corresponding public frontend configuration own
the display name. The neutral fallback is `Product`. Logo and localized display
settings are outer-layer configuration; Domain code cannot depend on them.

The initial locale is `fa-IR`, text direction is RTL, and the display time zone
is the IANA identifier `Asia/Tehran`. Persisted instants and API timestamps use
UTC. The frontend converts UTC instants for display and must retain the original
instant when formatting or charting data.

### API errors

HTTP errors use `application/problem+json` Problem Details. Generic responses
contain `type`, `title`, `status`, `instance` when relevant, and `traceId`.
Known application errors add a stable, non-localized `code`; validation errors
add an `errors` object. Human-readable text can be localized, but clients must
branch on status and code, never on translated text. Production responses do
not expose stack traces, credentials, upstream response bodies, or tenant data.

## Consequences

The product can be renamed without changing namespaces, domain entities, or
database schema. Time-series comparisons remain unambiguous across clients.
Operators can correlate safe error responses with logs without exposing secret
material.
