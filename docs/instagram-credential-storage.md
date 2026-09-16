# Instagram credential storage

Connected Instagram access tokens are server-side credentials. They never
appear in an API contract, frontend bundle, log message, or plaintext database
column.

## Protection boundary

`IInstagramTokenProtector` is the application boundary for token encryption.
The local infrastructure implementation uses ASP.NET Core Data Protection with
an application discriminator and a versioned purpose string. Only ciphertext is
accepted by the `InstagramCredential` entity. The credential service checks
account ownership before it protects and stores or replaces a token.

The database stores:

- encrypted access-token ciphertext;
- normalized granted scopes;
- issued, expiry, last-refresh, and revoke timestamps;
- active/expired/revoked/invalid status;
- SQL Server `rowversion` for optimistic concurrency.

One credential row belongs to one Instagram account. The foreign key is unique,
and deleting the account also deletes its credential row. Response metadata is
defined separately and contains no plaintext or encrypted token property.

## Key persistence

On a developer workstation, ASP.NET Core Data Protection uses its standard
user-profile key ring, outside SQL Server and outside this repository. To use a
dedicated persistent directory, set:

```text
DataProtection__KeyRingPath=C:\protected\analytics-keys
```

The directory must not be inside the repository and must be readable only by
the application identity. Production must point this setting at a durable,
encrypted deployment volume or replace the provider with a managed key service.
The key ring must be backed up and shared by all replicas; losing it makes stored
credentials undecryptable. Encryption keys are never stored in the application
database.

## Rotation and failure behavior

Data Protection rotates keys while retaining old keys for decryption. The
versioned purpose isolates Instagram access tokens from session and antiforgery
payloads even when they share the same provider. Unprotect returns a failure for
malformed ciphertext or an unrelated key ring and never falls back to treating
the stored value as plaintext.

When an owner disconnects, the provider revoke is best-effort, but local
revocation is unconditional: the encrypted token value is cleared, the
credential becomes `Revoked`, and future dependent work is stopped. Expiry or
provider revocation similarly changes the account to `ReconnectRequired`.
Reconnect replaces the existing credential row with newly protected ciphertext
instead of creating a duplicate.
