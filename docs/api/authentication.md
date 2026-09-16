# Authentication API contract

The MVP uses an HttpOnly Identity cookie. The API never returns a password,
credential, access token, or refresh token. Browser clients send requests with
credentials enabled.

## Endpoints

| Method | Path | Success | Purpose |
| --- | --- | --- | --- |
| `POST` | `/api/auth/register` | `201` | Create an account and start a session. |
| `POST` | `/api/auth/login` | `200` | Verify credentials and start a session. |
| `POST` | `/api/auth/logout` | `204` | Invalidate the current session cookie. |
| `GET` | `/api/auth/session` | `200` | Return the current authenticated/anonymous state. |
| `GET` | `/api/auth/validate` | `204` | Verify that the request has a valid session; otherwise `401`. |
| `GET` | `/api/auth/csrf` | `200` | Issue a CSRF cookie and request token. |

## CSRF flow

Before every state-changing request, the browser client calls
`GET /api/auth/csrf` with credentials enabled. The response contains a
`requestToken` and `headerName`. The client sends that token in the named header
on the immediately following `POST` or `PUT`; the HttpOnly CSRF cookie is sent
automatically. Fetch a fresh token after login, registration, or logout because
the authenticated identity changed.

Requests without a matching cookie and header token return RFC 9457 Problem
Details with status `400`. Authentication mutations are also limited to ten
requests per client per minute and return `429` after the limit is exhausted.

Register request:

```json
{
  "email": "user@example.com",
  "password": "StrongPass123"
}
```

Login adds an optional `rememberMe` boolean. Successful register and login
responses, and the session endpoint, use this shape:

```json
{
  "isAuthenticated": true,
  "user": {
    "id": "2a7d0617-4c7d-45e4-a047-dcd21b79ae49",
    "email": "user@example.com"
  }
}
```

Anonymous session state is explicit:

```json
{
  "isAuthenticated": false,
  "user": null
}
```

Validation failures use `application/problem+json` with an `errors` object.
Invalid login responses are intentionally generic and never disclose whether
an email address exists. Five consecutive failed attempts temporarily lock the
account for 15 minutes.
