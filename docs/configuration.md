# Configuration and secret management

Application configuration is environment-aware, brand-neutral, and safe to
review in source control. Only non-secret defaults and examples are committed.

## Backend provider hierarchy

ASP.NET Core reads values in its standard precedence order; later providers
override earlier ones:

1. `appsettings.json` for safe shared defaults.
2. `appsettings.{Environment}.json` for safe environment defaults.
3. ASP.NET Core User Secrets for local server-side secrets.
4. Deployment environment variables, using `__` for nested keys.
5. Command-line configuration for controlled operational overrides.

Staging and production secrets must come from deployment environment variables
or a managed secret provider. They are not placed in `appsettings` files.

Example safe overrides:

```text
Branding__ProductName=Example Product
Localization__DefaultLocale=fa-IR
Localization__DisplayTimeZone=Asia/Tehran
```

## Frontend provider hierarchy

Next.js reads process environment variables and ignored `.env*` files. The
committed `apps/web/.env.example` contains public, non-secret examples only.
Values prefixed with `NEXT_PUBLIC_` are embedded into browser assets at build
time and must never contain credentials.

Branding builds should set the public product variables before `next build`.
Server-only frontend secrets, if introduced later, must omit the
`NEXT_PUBLIC_` prefix and be read only from Server Components or Route Handlers.

## Environment matrix

| Environment | Safe configuration | Secret provider | Notes |
| --- | --- | --- | --- |
| Local | `appsettings.json`, development settings, ignored `.env.local` | ASP.NET Core User Secrets | No real token in files or shell history. |
| Staging | Versioned safe defaults plus deployment variables | Managed secret store or protected deployment variables | Uses separate Meta redirect URIs and credentials. |
| Production | Versioned safe defaults plus deployment variables | Managed secret store with rotation | Least-privilege access and audited changes. |

## Time and locale

- Persist and exchange instants as UTC with an explicit offset.
- Use `DateTimeOffset` for instants in backend contracts and persistence.
- Use `fa-IR` and RTL as the MVP defaults.
- Convert to `Asia/Tehran` only at the presentation boundary.
- Do not persist localized date strings or assume a fixed Iran UTC offset.

## Brand changes

`Analytics` remains an internal workspace name. To change the public display
name, configure `Branding:ProductName` for the API and the corresponding
`NEXT_PUBLIC_PRODUCT_*` values for the web build. Domain models, namespaces,
database identifiers, and API error codes do not change.
