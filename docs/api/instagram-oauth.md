# Instagram OAuth connection flow

The connection flow uses Instagram Login with an authorization code and the
read-only scopes required by the analytics MVP:

- `instagram_business_basic`
- `instagram_business_manage_insights`

Both endpoints require the authenticated application session cookie.

## Start connection

`GET /api/integrations/instagram/connect` creates a cryptographically random
32-byte state value, stores only its SHA-256 hash with a ten-minute expiry and
the current owner ID, then returns a `302` redirect to Instagram. The redirect
contains the app ID, callback URI, scopes, response type, and raw one-time state.
It never contains the app secret or an access token.

## Callback

Instagram redirects the browser to:

```text
GET /api/integrations/instagram/callback?code=...&state=...
```

The API hashes the supplied state and atomically consumes the matching row for
the authenticated owner. Missing, changed, expired, cross-owner, or previously
consumed state returns `400` before any provider request. A user denial also
consumes the valid state and returns safe Problem Details without reflecting the
provider's description.

For a valid callback, the API posts the code, app ID, app secret, grant type,
and exact redirect URI directly to Instagram's token endpoint. That form and
the returned token stay server-side. The current implementation creates a
provisional owned account keyed by the stable Instagram user ID and encrypts the
credential immediately. Profile discovery and Professional-account validation
replace the provisional profile in the next integration stage.

The success response includes only account ID, Instagram user ID, provisional
username, granted scopes, expiry, and credential status. It contains neither
plaintext nor encrypted token material.

## Safe failures

- `400`: invalid/replayed/expired state, denial, or missing code.
- `409`: the Instagram user ID is already owned by another application user.
- `502`: Instagram rejected or returned an invalid code-exchange response.
- `503`: Instagram integration is disabled for the environment.

The code exchange is behind `IInstagramOAuthClient`, so automated tests use a
deterministic server-side double and never call Meta or require a real token.
