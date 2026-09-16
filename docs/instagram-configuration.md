# Instagram configuration and local secrets

Instagram integration is disabled by default so the rest of the application
can run before Meta credentials are available. Enabling it activates fail-fast
validation for all required settings.

## Local development

The API project has an ASP.NET Core User Secrets ID. Run these commands from the
repository root, replacing placeholders only in your local terminal:

```powershell
dotnet user-secrets set "Instagram:Enabled" "true" --project src/backend/Analytics.Api/Analytics.Api.csproj
dotnet user-secrets set "Instagram:AppId" "<meta-app-id>" --project src/backend/Analytics.Api/Analytics.Api.csproj
dotnet user-secrets set "Instagram:AppSecret" "<meta-app-secret>" --project src/backend/Analytics.Api/Analytics.Api.csproj
dotnet user-secrets set "Instagram:OAuthRedirectUri" "https://localhost:7261/api/integrations/instagram/callback" --project src/backend/Analytics.Api/Analytics.Api.csproj
```

For a short-lived development spike only, the developer access token can also
be supplied through User Secrets:

```powershell
dotnet user-secrets set "Instagram:DevelopmentAccessToken" "<short-lived-development-token>" --project src/backend/Analytics.Api/Analytics.Api.csproj
```

Remove it as soon as the spike is complete:

```powershell
dotnet user-secrets remove "Instagram:DevelopmentAccessToken" --project src/backend/Analytics.Api/Analytics.Api.csproj
```

User Secrets are outside the repository and are not committed, but they are a
development convenience rather than an encrypted production vault. Do not paste
real values into issues, chat, source files, `appsettings`, test data, or commit
messages. If terminal history is retained on the machine, enter real values in
Visual Studio's **Manage User Secrets** editor instead of putting them directly
on a command line.

## Configuration contract

| Key | Required | Secret | Notes |
| --- | --- | --- | --- |
| `Instagram:Enabled` | Always | No | Defaults to `false`. |
| `Instagram:AppId` | When enabled | Treat as private | Meta application identifier. |
| `Instagram:AppSecret` | When enabled | Yes | Server-side only. |
| `Instagram:OAuthRedirectUri` | When enabled | No | Absolute HTTPS callback URI registered in Meta. |
| `Instagram:AuthorizationEndpoint` | Always | No | Defaults to Instagram's HTTPS authorization endpoint. |
| `Instagram:TokenEndpoint` | Always | No | Defaults to Instagram's server-side code-exchange endpoint. |
| `Instagram:GraphApiBaseUri` | Always | No | Safe default is committed. |
| `Instagram:StateLifetime` | Always | No | Defaults to ten minutes; maximum one hour. |
| `Instagram:ApiRequestTimeout` | Always | No | Per-attempt API timeout; defaults to 30 seconds. |
| `Instagram:ApiMaxAttempts` | Always | No | Bounded transient retry attempts; defaults to 3. |
| `Instagram:ApiRetryBaseDelay` | Always | No | Linear retry delay base; defaults to 200 ms. |
| `Instagram:ApiMaxPageCount` | Always | No | Pagination safety limit; defaults to 100. |
| `Instagram:DevelopmentAccessToken` | Never | Yes | Optional Development-only spike credential. |

The development access token is configuration-only. No application service,
entity, migration, or database column persists it. Startup rejects this setting
outside the Development environment.

The current connection workflow intentionally does not bootstrap the database
from this setting. Create or reconnect accounts through the OAuth endpoints; no
manual SQL is required. Automated lifecycle and provider-contract tests use
in-process test doubles and never require a real Instagram token.

Startup logs only boolean diagnostics such as `AppSecretConfigured=True`.
Credential values are never interpolated into logs or validation messages.
Production values must come from protected deployment variables or a managed
secret store using keys such as `Instagram__AppSecret`.
