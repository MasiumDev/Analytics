# Instagram live API spike

This document records the reproducible development-account probe used to set
the MVP synchronization policy. It is an empirical snapshot, not a permanent
Meta API guarantee. Re-run the probe after an API-version, permission, or
account-type change.

## Safe reproduction

Configure `Instagram:DevelopmentAccessToken` through the local User Secrets
workflow in [`../instagram-configuration.md`](../instagram-configuration.md),
connect the VPN when Meta is not directly reachable, and run from the
repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/instagram-spike.ps1
```

The probe sends the credential only in the HTTPS `Authorization` header. Its
output contains no access token, account ID, media ID, username, metric value,
caption, or provider response body. It reports only response classifications,
timing, usage signals, content types, and bucket timestamps.

## Observation

The successful run was made on 2026-09-18 at 13:17 UTC with an Instagram
Creator development account and the Instagram Login Graph host. Profile and
media discovery both returned HTTP 200. The first discovery page contained at
least one Reel but no non-Reel media, so non-Reel metric support remains
unverified by this sample.

### Reel media

| Classification | Metrics | Observed response |
| --- | --- | --- |
| Available with a value | `views`, `reach`, `likes`, `comments`, `saved`, `shares`, `total_interactions`, `ig_reels_avg_watch_time`, `ig_reels_video_view_total_time` | HTTP 200 |
| Unsupported for the sampled Reel | `clips_replays_count`, `plays`, `skip_rate` | HTTP 400, provider code 100 |

The media detail resource also returned `timestamp`, `like_count`, and
`comments_count`. A second read of every available Reel metric after 30 seconds
returned an identical payload. Media insight responses did not expose a source
bucket timestamp, so this run proves short-interval stability but cannot
measure the provider's ingestion lag.

### Account metrics

| Classification | Metrics | Observed response |
| --- | --- | --- |
| Available with a value | `reach`, `follower_count` | HTTP 200 with a named series |
| Empty for this account and interval | `views`, `profile_views`, `website_clicks`, `accounts_engaged`, `total_interactions` | HTTP 200 with an empty `data` array |

An empty successful response is deliberately not classified as unsupported.
It means that no series was available for the sampled account and interval;
the application must preserve the distinction between missing data and a
numeric zero. The available daily account series reported a latest bucket end
of 2026-09-18 07:00 UTC during the 13:17 UTC run. This timestamp identifies the
provider bucket; it does not by itself prove a six-hour ingestion delay.

### Rate-limit signals and latency

Meta returned an `x-app-usage` header, but its decoded shape did not contain the
standard `call_count`, `total_cputime`, and `total_time` fields expected by the
probe. The probe records the header as present with an unrecognized schema and
does not emit unknown property names because they may contain identifiers.
`x-business-use-case-usage` and `Retry-After` were absent. No 429 response was
induced because deliberately exhausting a live development quota would be
unsafe and would not represent production traffic. Successful request latency
in the sample was approximately 0.3-1.2 seconds.

The runtime client must continue to capture these headers on every response.
Header absence is treated as unknown capacity, never as unlimited capacity.
HTTP 429 and `Retry-After` remain authoritative when supplied.

## MVP request budget and cadence

Use a conservative per-account scheduler until production traffic provides
real usage-header data:

| Work | Cadence | Budget rule |
| --- | --- | --- |
| Profile and media discovery | Every 15 minutes | One profile read plus cursor-bounded media reads; skip unchanged media during persistence. |
| Media younger than 48 hours | Every hour | Read the supported metric set in one comma-separated insights request per media item when the API contract allows it. |
| Media aged 2-7 days | Every 6 hours | Stop early when the current-stat projection is unchanged. |
| Media older than 7 days | Daily | Refresh only retained/reportable media. |
| Account daily metrics | Every 6 hours | Store empty as unavailable, not zero; retain the provider bucket timestamp. |

Apply up to 20% random jitter to scheduled start times so tenants do not align
on clock boundaries. Permit only one active synchronization per connected
account. A run stops scheduling additional pages or media when any reported
usage percentage reaches 80%. At 90%, defer nonessential work for at least one
hour. On HTTP 429, honor `Retry-After`; when it is absent, use the client's
bounded backoff and postpone that account's remaining work.

The scheduler must calculate the projected calls before starting a batch and
must not start work it cannot finish below the 80% threshold. Until populated
usage headers are observed, cap a normal run at 100 provider requests per
connected account and carry remaining cursor work to the next run.

## Follow-up evidence

Before broad beta, repeat this probe with:

- at least one image or carousel post;
- a Business account in addition to the sampled Creator account;
- an account with available values for the currently empty account metrics;
- production-like multi-tenant traffic so populated usage headers and the
  effective quota can be recorded.

These follow-ups may expand the allowlist. They must not silently convert empty
responses into zero or re-enable metrics that returned provider code 100.
