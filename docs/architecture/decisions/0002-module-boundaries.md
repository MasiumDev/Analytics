# ADR 0002 Module boundaries and dependency direction

- Status: Accepted
- Date: 2026-09-16

## Context

The public brand is not finalized, Instagram integration has strict credential
requirements, and monitoring jobs must not become an alternative business-logic
entry point. Boundaries are needed before the domain grows.

## Decision

The backend follows these logical boundaries, even while some boundaries share
one project during the early MVP:

1. Domain contains product concepts and invariants. It has no dependency on
   ASP.NET Core, EF Core, Hangfire, Instagram SDKs, configuration, or branding.
2. Application contains use cases and ports. It depends on Domain only.
3. Infrastructure implements persistence, Instagram API, encryption, clock,
   and job-scheduling ports. It may depend on Application and Domain.
4. API owns HTTP contracts, authentication composition, configuration binding,
   and dependency injection. It invokes Application use cases.
5. Background jobs are thin adapters that invoke Application use cases; they do
   not contain separate business rules.
6. The Next.js application communicates through documented API contracts. It
   never connects directly to SQL Server or exposes Instagram credentials.

Dependencies point inward. Cross-tenant ownership checks belong in Application
and Domain rules and cannot be bypassed by API or job adapters.

`Analytics` is an internal repository, solution, package, and namespace label.
It must not appear as a product rule, persisted tenant value, or required public
brand. Public naming enters only through configuration at an outer boundary.

## Consequences

The MVP can begin with few projects without losing the intended dependency
direction. New code is grouped by product capability inside the appropriate
boundary. A project split is justified by enforcement or deployment needs, not
by folder aesthetics.
