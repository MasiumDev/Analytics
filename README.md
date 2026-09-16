# Analytics

Internal workspace for the Instagram analytics product. The public product
name is intentionally not encoded in namespaces or package names so it can be
changed later.

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

## Install dependencies

```powershell
dotnet restore Analytics.sln
pnpm install
```

## Run locally

Start the API and web app in separate terminals:

```powershell
pnpm dev:api
pnpm dev:web
```

- API root: `http://localhost:5157/`
- API health: `http://localhost:5157/health`
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

No application secret or Instagram token belongs in this repository. Local
credentials will be configured through development secret providers in a later
issue.
