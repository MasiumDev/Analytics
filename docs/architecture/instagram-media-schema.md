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

The entity and table names describe the Instagram platform domain only; they do
not contain the repository name or any candidate public product brand.
