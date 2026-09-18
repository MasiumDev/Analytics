# Instagram account ownership API

Every Instagram account belongs to exactly one authenticated application user.
The same user may own multiple Instagram accounts, while an Instagram user ID
can only be connected once across the application.

All endpoints below require the secure application session cookie. `POST` and
`PUT` also require the CSRF cookie/header pair described in the authentication
contract and are rate limited per authenticated user.

- `GET /api/instagram-accounts` lists only the current user's accounts.
- `GET /api/instagram-accounts/{id}` returns an owned account.
- `POST /api/instagram-accounts` connects an account to the current user.
- `PUT /api/instagram-accounts/{id}` updates an owned account profile.
- `GET /api/instagram-accounts/{id}/connection` validates the stored token and
  returns redacted connection health.
- `POST /api/instagram-accounts/{id}/disconnect` revokes provider access when
  possible, clears the local ciphertext, and stops dependent jobs.
- `POST /api/instagram-accounts/{id}/sync-profile` refreshes owned profile
  metadata and current account counters.
- `POST /api/instagram-accounts/{id}/import-media` runs or resumes the bounded,
  idempotent historical media import.
- `GET /api/instagram-accounts/{id}/media-import` returns the persisted import
  lifecycle, cursor, counters, and timestamps.
- `POST /api/instagram-accounts/{id}/media-import/retry` retries a partial or
  failed import.
- `POST /api/instagram-accounts/{id}/sync-media-stats` refreshes current metrics
  for imported media and reports complete or partial results.

The API derives ownership from the authenticated session; clients never submit
an owner user ID. Requests for an account owned by another user return `404` so
the API does not disclose whether that account exists. Ownership is checked a
second time in the application service's repository boundary by including the
owner user ID in every account lookup.

Create requests use this shape:

```json
{
  "instagramUserId": "17841400000000000",
  "username": "brand_handle",
  "displayName": "Brand Name"
}
```

Profile updates accept `username` and optional `displayName`. The owner and the
Instagram user ID are immutable. A duplicate Instagram user ID returns RFC 9457
Problem Details with status `409`.

OAuth discovery also records `professionalAccountType` as `Business` or
`Creator`. This value remains null only for manually created or not-yet-
discovered records.

## Connection lifecycle

Successful OAuth and reconnects set the account to `Connected`. An expired,
revoked, invalid, undecryptable, or identity-mismatched credential changes it
to `ReconnectRequired` and stops dependent jobs. Disconnect always removes the
locally usable token and sets `Disconnected`, even if Instagram is temporarily
unavailable while provider revocation is attempted. A disconnected account is
not revalidated until the owner completes OAuth again.

Provider validation and revocation send the token only in the HTTPS Bearer
header. URLs, API responses, errors, and logs contain no token material.
Requests for another user's account return `404` for both lifecycle endpoints.

## Profile synchronization

Profile sync reads the account identity, username, display name, professional
account type, follower/follows counts, and media count through the typed
server-side Instagram client. It verifies that the provider user ID still
matches the owned account, then updates the account and its one-to-one
`AccountCurrentStats` projection. Repeating the same sync updates those rows and
never creates duplicates. Successful responses contain `lastSyncedAtUtc` and no
credential material.

An expired, missing, revoked, rejected, or undecryptable credential returns
`409` with an actionable reconnect message, changes the connection state to
`ReconnectRequired`, and stops dependent jobs. Temporary provider or rate-limit
failures return `503` without changing a healthy connection; invalid provider
profiles return `502`. Tenant ownership is checked before any provider request
or database write.
