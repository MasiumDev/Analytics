# Instagram account ownership API

Every Instagram account belongs to exactly one authenticated application user.
The same user may own multiple Instagram accounts, while an Instagram user ID
can only be connected once across the application.

All endpoints below require the secure application session cookie:

- `GET /api/instagram-accounts` lists only the current user's accounts.
- `GET /api/instagram-accounts/{id}` returns an owned account.
- `POST /api/instagram-accounts` connects an account to the current user.
- `PUT /api/instagram-accounts/{id}` updates an owned account profile.

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
