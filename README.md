# Analytics

Internal workspace for the Instagram analytics product. The public product
name is intentionally not encoded in namespaces or package names so it can be
changed later.

Architecture decisions are indexed in
[`docs/architecture/README.md`](docs/architecture/README.md). Environment,
branding, locale, time, and secret-provider conventions are documented in
[`docs/configuration.md`](docs/configuration.md).
The cookie-based account contract is documented in
[`docs/api/authentication.md`](docs/api/authentication.md).
The tenant ownership contract for connected Instagram accounts is documented in
[`docs/api/instagram-accounts.md`](docs/api/instagram-accounts.md).
The browser, transport, rate-limit, and tenant authorization baseline is in
[`docs/security.md`](docs/security.md).
Meta app settings and the local User Secrets workflow are documented in
[`docs/instagram-configuration.md`](docs/instagram-configuration.md).
Encrypted connected-account credentials and key-ring operations are documented
in [`docs/instagram-credential-storage.md`](docs/instagram-credential-storage.md).
The one-time-state Instagram authorization-code flow is documented in
[`docs/api/instagram-oauth.md`](docs/api/instagram-oauth.md).

## Repository structure

```text
apps/web/                              Next.js frontend
src/backend/Analytics.Api/             ASP.NET Core Web API
tests/Analytics.Api.UnitTests/         Backend unit tests
tests/Analytics.Api.IntegrationTests/  Backend integration tests
```

## Prerequisites

- .NET SDK 9.0.300 or a newer 9.0 patch
- Node.js 20.9 or newer
- pnpm 11.19.0
- SQL Server LocalDB (installed with Visual Studio) or another SQL Server

## Install dependencies

```powershell
dotnet restore Analytics.sln
dotnet tool restore
pnpm install
```

Create or update the local database from the committed migrations:

```powershell
pnpm db:update
```

The committed development connection uses Windows authentication and does not
contain credentials. Override `ConnectionStrings__ApplicationDatabase` outside
Git for staging and production.

## Run locally

Start the API and web app in separate terminals:

```powershell
pnpm dev:api
pnpm dev:web
```

- API root: `http://localhost:5157/`
- API liveness: `http://localhost:5157/health`
- API readiness (including database): `http://localhost:5157/health/ready`
- Web app: `http://localhost:3000/`

The API development ports are defined in
`src/backend/Analytics.Api/Properties/launchSettings.json`.

## Build and test

Build every application from the repository root:

```powershell
pnpm build
```

Run all backend and frontend tests:

```powershell
pnpm test
```

Run lint, tests, and all production builds:

```powershell
pnpm verify
```

## Continuous integration

GitHub Actions runs the same `pnpm verify` quality gate for every pull request
and every push to `main`. A failing lint, test, or build command fails the job.
The workflow uses only credential-free LocalDB and public package feeds, so it
does not require repository secrets.

To reproduce the CI setup and checks locally:

```powershell
pnpm install --frozen-lockfile
dotnet restore Analytics.sln --locked-mode
dotnet tool restore
pnpm verify
```

No application secret or Instagram token belongs in this repository. Local
credentials are supplied through development secret providers; staging and
production use protected deployment variables or a managed secret store. Copy
`apps/web/.env.example` to an ignored local environment file only for public,
non-secret frontend configuration.
