# Tenant identities and sessions

The replacement service supports local owner pairing and tenant password login over its optional HTTPS listener. Both produce opaque one-hour revocable sessions. The listener remains restricted to `127.0.0.1`; this capability does not enable hosted deployment. Hosted ingress, credential exchange/signatures, scoped RBAC and isolated CLI execution remain open requirements.

The seeded local owner is protected and has no default password. Read its single-use pairing code from the protected application home. Ordinary user management cannot rename, disable, reset or remove this owner. No default password or external identity auto-provisioning is introduced.

Tenant administrators manage identities in their own tenant through the dashboard Users page, Ctrl+U in the TUI, console `users` commands, or `/v1.0/api/users`. System administrators can additionally grant and modify the system administrator flag, while remaining bound to the current tenant for these routes. Normal users can read their own metadata; enumerating identities requires an administrator. Disable accounts to preserve historical ownership and accounting. There is no user deletion endpoint.

An email address is normalized to lowercase and unique within its tenant. Names are display metadata. Compatibility domain roles remain `viewer`, `operator` and `admin`: viewers read owned conversation data, while operators/admins can modify it. The separate system and tenant administrator flags grant administrative access and domain mutation bypass; role names alone never confer system administration. Resource ownership still scopes domain reads. Full stored role/permission assignment and explicit-deny resolution remain unfinished.

User updates replace metadata and require `expectedRevision`. A stale revision returns `409 revision_conflict`. Protected identities return `403 protected_user`. Password replacement and disablement revoke all current sessions in the same transaction as the identity mutation and its security accounting. Other role/flag changes are read from the current user record on every authentication. Live event streams recheck identity state and terminate after revocation. Already accepted background execution remains subject to the separate worker authorization gate still tracked in P2.

The dashboard and terminal accept a masked password and compute its SHA-256 equivalent before exchanging it. JSON callers supply exactly 64 hexadecimal characters in `passwordSha256`. This equivalent is itself a password: possession permits login. The database stores the equivalent as required by the current authentication model, so database snapshots and recovery archives remain sensitive even when local encryption keys are omitted. Prefer encrypted recovery archives when storing identity data outside the protected home. Neither user metadata nor diagnostic capture returns the stored password equivalent.

Leave the password field blank in an editing form to preserve it. Creating a user without a password leaves password login unavailable. A new password invalidates existing sessions. Browser and TUI entry requires twelve through 1024 characters; the low-level SHA-256 exchange cannot infer the original password's length or strength.

Password exchange requires HTTPS and explicit `tenantId`, `email` and `passwordSha256`, with no existing session or unrelated authentication headers. Tenant hints must agree. Invalid, inactive or missing identities return the same `401 invalid_login`. The listener allows twenty exchange attempts per minute across the service using a monotonic window; it returns `429 login_rate_limit` afterward. This service-wide local limit does not claim distributed hosted rate limiting. Failed login, session issuance, identity mutations, session invalidation and HTTP denial/rate-limit accounting do not depend on request-history capture.

Console usage:

```sh
# Read sensitive JSON from a protected file or stdin, never shell history.
dotnet run --project src/ArgueBot.Cli -c Release -- users create --file .run/user.json
dotnet run --project src/ArgueBot.Cli -c Release -- users list example.invalid
dotnet run --project src/ArgueBot.Cli -c Release -- users show USER_ID
dotnet run --project src/ArgueBot.Cli -c Release -- users update USER_ID --file .run/update-user.json
dotnet run --project src/ArgueBot.Cli -c Release -- auth login --file .run/login.json
```

The explicit login command returns token JSON on stdout. Protect that output and supply its token through `ARGUEBOT_TOKEN` or the console's `--token-env NAME`. The TUI also accepts `ARGUEBOT_TOKEN`. Login never replaces the cached local owner's session; subsequent commands without an explicit token retain the existing local pairing behavior.

Database schema 10 adds email, password-equivalent and revision columns plus the tenant email uniqueness index. Earlier users retain their IDs, roles and flags and receive no password. Historical snapshot inventories omit these columns until schema 10 and upgrade them before reuse. Product version 0.1.0 and attachment protocol 2 remain unchanged.

Enable loopback HTTPS by provisioning `keys/listener.pfx` in the selected application home, then saving `TlsEnabled: true` in service settings and restarting. If the PFX is password protected, set `TlsPasswordEnvironmentVariable` to the name of a service environment variable containing its password. The certificate needs a private key, current validity and an IP subject alternative name for `127.0.0.1`. No certificate or root trust is installed automatically. Browsers need a certificate they trust through the operator's trust process. HTTPS disables the HTTP development-origin allowance and accepts only the HTTPS listener origin.

Native console/TUI attachment uses platform certificate validation or an exact local certificate pin published in the owner-protected service descriptor. The pin is bound to that home's path identity and exact loopback endpoint. It cannot waive name mismatch, expiry, a different port or a different home. The HMAC home proof is still verified before cached credentials are sent. Generic remote clients retain platform certificate validation. HTTP remains the default for protected local pairing; both server and native login helper refuse password exchange over HTTP.

Recovery bundles omit TLS private keys by default. Explicit encrypted secret-inclusive bundles may include `keys/listener.pfx`; the PFX password remains an environment reference. Restoring a default bundle with HTTPS enabled requires provisioning its certificate before startup. The recovered service stays stopped. TLS settings do not enable remote listening or establish hosted runner isolation.

The loader uses ephemeral key import where supported. macOS uses the platform default key set because .NET rejects ephemeral PFX import there; certificate objects are disposed with the service. This follows the [key-storage flag contract](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509keystorageflags) and is not a claim that macOS key import remains entirely in memory.

Pinned native connections independently check the certificate IP SAN with managed hostname matching, with wildcard and common-name fallback disabled. This preserves actual name validation on .NET 8/macOS, whose platform callback reported a name mismatch for the fixture's matching IP SAN. A different IP SAN remains rejected even with an exact certificate pin. Both runtime targets exercise these cases.
