# Instagram media catalog schema

The catalog is rooted at the tenant-owned `InstagramAccount`. `InstagramMedia`
belongs to exactly one account, and its provider ID is unique inside that
account boundary. The same test or provider ID cannot be duplicated for one
account, while independent accounts are not coupled by a global database key.
Deleting an account cascades to its media and current statistics.

The catalog supports Business and Creator accounts and the MVP media variants:
image, video, carousel album, and reel. Each media row keeps its provider ID,
HTTPS permalink, optional caption, provider publication time, local creation
and update times, and a SQL Server rowversion. All date-time values are stored
as `DateTimeOffset` after normalization to UTC.

`AccountCurrentStats` is a one-to-one current projection for follower, follows,
and media counts. `MediaCurrentStats` is a one-to-one current projection for
likes, comments, saves, shares, reach, and plays. Metrics are nullable because
Meta does not return every metric for every account/media type, and non-null
values cannot be negative. Account statistics include capture time. Media
statistics preserve both the provider's optional source timestamp and the
server receipt timestamp. Both projections include rowversion for safe
refreshes. Historical, append-only snapshots are intentionally separate from
these current-value tables.

`MediaInsightSnapshots` stores the observed media metrics validated by the
live API spike: views, reach, likes, comments, saves, shares, total
interactions, average watch time, and total watch time.
`AccountInsightSnapshots` stores views, reach, follower count, profile views,
website clicks, accounts engaged, and total interactions. Every metric remains
nullable so an unavailable series is never rewritten as a numeric zero.

Each snapshot has an immutable surrogate ID, a server `CapturedAtUtc`, and an
optional provider `SourceTimestampUtc`. The parent ID plus capture timestamp is
a unique descending index: retrying the same logical sample is idempotent and
the main newest-first time-series query uses the same index. A separate capture
time index supports future retention batches. EF rejects updates after insert,
while account ownership cascades and explicit retention deletes remain
possible. Domain construction normalizes timestamps to UTC and both domain and
database constraints reject negative metric values.

The entity and table names describe the Instagram platform domain only; they do
not contain the repository name or any candidate public product brand.
