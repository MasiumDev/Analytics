# ADR 0001 Technology stack and deployment shape

- Status: Accepted
- Date: 2026-09-16

## Context

The MVP needs a browser application, a server-side integration boundary for
Instagram credentials, relational persistence, and reliable background work.
The first release is operated as one product, while the design must still
support many application users and connected Instagram accounts.

## Decision

- Use Next.js with TypeScript for the web application.
- Use ASP.NET Core Web API for HTTP endpoints and application composition.
- Use SQL Server with EF Core for application data.
- Use Hangfire with SQL Server storage for scheduled and background work.
- Keep the repository as a pnpm/.NET monorepo with one documented verification
  command.
- Start as a modular monolith. Web, API, worker, and database may be deployed
  independently when operational needs justify it, but they remain one product
  and one source repository for the MVP.

## Consequences

This minimizes distributed-system overhead while leaving clear seams for a
separate worker or additional clients. Technology-specific code must remain at
the outer boundaries described in ADR 0002.
