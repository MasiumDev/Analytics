# Historical Instagram media import

`POST /api/instagram-accounts/{id}/import-media` imports every accessible media
page for an account owned by the authenticated user. The server reads the
encrypted credential, sends it only as a Bearer header, and asks the typed
Instagram client for batches of at most 50 items.

Each provider page is validated and committed in its own database transaction.
Media is upserted by `(InstagramAccountId, InstagramMediaId)`, so rerunning an
import updates existing rows and creates no duplicates. Image, video, carousel,
and Reel payloads are normalized into the catalog model. Invalid individual
items are skipped and included in the `failed` count.

One `MediaImportCheckpoint` row per account stores the next cursor, status,
page count, and cumulative fetched/created/updated/failed totals. The cursor and
page changes commit atomically. If cancellation or a transient provider failure
interrupts the run, the next request resumes from the last committed cursor.
After a completed import, a new request intentionally starts from the first page
to discover updates while remaining idempotent.

The persisted lifecycle is `Queued`, `Running`, `Succeeded`, `Partial`, or
`Failed`. `GET /api/instagram-accounts/{id}/media-import` exposes the current
status, cursor, counters, and timestamps. `POST
/api/instagram-accounts/{id}/media-import/retry` accepts only `Partial` or
`Failed` checkpoints; other states return `409` without calling Instagram.

The result reports `fetched`, `created`, `updated`, `failed`, `pagesProcessed`,
and whether a checkpoint exists. Credential failures return an actionable
reconnect response. Rate limits, transient errors, repeated/oversized cursors,
and the configured maximum page count stop the bounded run without an infinite
loop. Ownership is verified before reading a credential or calling Instagram.

`POST /api/instagram-accounts/{id}/sync-media-stats` refreshes the current
likes, comments, saves, shares, reach, and plays for every imported media item.
Each upsert records the provider's optional source timestamp and the server's
UTC receipt timestamp. Individual provider failures produce a `207 Partial`
result while successful items remain committed; a complete provider failure
returns `502`. Credential rejection moves the account to reconnect-required and
stops dependent jobs. All status, retry, and statistics endpoints return `404`
for an account owned by another tenant before making any provider request.
