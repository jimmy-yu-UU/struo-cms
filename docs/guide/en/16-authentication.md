# 16. Authentication and SSO

A caller proves who it is with a password login, a session cookie, a bearer token, or a login
through an external identity provider. Whether a caller should use the cookie or a bearer token,
and when the CSRF header is required, is covered in
[Chapter 12: REST API Conventions](12-rest-conventions.md).

This chapter covers the mechanics behind each credential — how it's issued, where it lives, when
it expires, and how it's revoked. What a caller can do once authenticated is in
[Chapter 17: Roles and Permissions](17-roles-and-permissions.md).

## The first administrator

The moment the `users` table is created and still empty, the first administrator account is
created along with it: active, its name fixed at `Administrator`, its email and password taken
from `Auth:BootstrapAdmin`. This happens exactly once — changing the setting and restarting
does nothing to an existing database.

This default account is the one deliberate bypass of the password policy: its configured
password is hashed and written directly, skipping the length check the next section covers,
so the short default password in the settings file still works for that first login. The
transcripts log in with that same default password. A production environment should change it
immediately after the first login.

The same seed data also creates the `admin` and `public` roles, and adds the first administrator
to `admin` — the detail is left to
[Chapter 17: Roles and Permissions](17-roles-and-permissions.md).

## Passwords

There's exactly one password-hashing implementation: Argon2id, with parameters fixed in code —
time cost 3, memory cost 65536 (64 MiB), parallelism 1, hybrid addressing, a 32-byte output —
producing a self-contained PHC-encoded string. Salt and parameters are embedded in the hash
itself. The `user` collection carries no separate salt column.

Login verifies the submitted password against the stored hash, and success issues the cookie in
the same request — there's no separate "exchange a password for a token" step. When no such
account exists, or the account has no local password at all (covered in the OIDC section below),
the server still runs one full Argon2id verification against a fake hash, so failure always takes
the same time — the caller can't infer from response speed whether that email exists.

Login exposes only two kinds of failure to the caller: a password that verifies but an account
that's deactivated returns `401` `ACCOUNT_INACTIVE`; every other case — a wrong password, an
account that doesn't exist — returns the same `401` `UNAUTHORIZED` `Invalid credentials.` — the two
are deliberately indistinguishable. Success returns `200` with `{ id }` and no other fields.

`POST /api/users` creates a user, and only a super-admin can call it: success returns `201` with a
body of `{ id, email, name }`, and `Location` points at `/api/items/user/{id}` — the generic item
route, since there's no dedicated `GET /api/users/{id}`. An email already in use returns `409`
`CONFLICT` `Email already in use.`. The password's minimum and maximum length, and the configuration
keys, are in the `Auth` section of [Chapter 4: Configuration Reference](04-configuration.md).

Changing a password goes through `PUT /api/users/{id}/password`: the caller is either a
super-admin, or the account owner supplying `currentPassword` — the owner's own change always
verifies the current password, a super-admin's change never does.

A wrong current password returns `400` `INVALID_CURRENT_PASSWORD`; a non-admin trying to change
someone else's password returns `403` `Admin role required.`; a new password that violates the
policy returns `400` with a field-level message.

When the owner calls for an account that only ever logged in through OIDC and never set a local
password, the request is stopped before the current-password check even runs: `400`
`NO_LOCAL_PASSWORD`. A super-admin can still set a local password on such an account. Across the
whole identity surface, this is the only endpoint that lets a non-super-admin write.

The admin SPA surfaces this endpoint in two places: an ordinary user changes their own password
from the account menu in the app shell, and a super-admin sees a reset action on the user form —
which most callers never reach, since an ordinary role isn't usually granted read on `user`.

This endpoint carries its own rate limit, partitioned by the user id of the caller making the
change — not the target user's, and not the caller's IP: a super-admin resetting several users'
passwords in a row spends its own quota, and never locks out the accounts it's changing. The
configuration keys are in the `RateLimiting` section of
[Chapter 4: Configuration Reference](04-configuration.md).

## Login and the session cookie

A request that carries no `Authorization: Bearer …` header always goes through the cookie: named
`struo.session`, `HttpOnly`, `SameSite` defaulting to `Lax`. In production the `Secure` policy is
fixed to `Always`; every other environment follows the request itself.

As soon as any CORS origin is configured at all, both the `Secure` policy and `SameSite` switch to
`Always`/`None`, because a browser only accepts `SameSite=None` under `Secure` — meaning a
cross-origin deployment has to run HTTPS on both sides.

The cookie itself is only an opaque key. The actual session state behind it lives in a distributed
cache: setting `Redis:ConnectionString` uses Redis, and leaving it unset falls back to an
in-process cache — the practical difference is that restarting the process loses every session,
and replicas don't share state. Redis survives both.

Expiry is the same 8-hour sliding window: the cookie itself, the cache entry, and the index row
covered below all share this one constant, and the three can't drift apart. The configuration key
is in the `Redis` section of [Chapter 4: Configuration Reference](04-configuration.md).

Every cookie-authenticated request re-checks that the account still exists and is active.
The moment either isn't true, the request is denied and the caller is logged out on the spot — the
session behind that key is deleted from the cache too, not just this one request refused.

This check, along with the ordinary `401` `Authentication required.`/`403` `Forbidden.` denial
when sign-in is required but no credential is present, is written out by the cookie handler before
the MVC action itself ever runs. The action needs no handling of its own.

The default authentication scheme isn't actually the cookie itself, but a forwarding scheme: a
request carrying `Authorization: Bearer …` forwards to bearer authentication; anything else
forwards to the cookie — which is why even reads, `/graphql`, and file reads, none of which
declare any scheme, still recognize a bearer caller.

On these scheme-less endpoints, a broken or revoked token always degrades to anonymous rather than
falling back to the cookie, even when the request carries both at once. A write endpoint that
requires sign-in validates and merges both schemes instead — see
[Chapter 12: REST API Conventions](12-rest-conventions.md).

If a caller logs in again while it's still carrying a valid session cookie, that doesn't open a
second session: the cookie handler sees that this request already carries a session key, and
renews that same key in place under the new login's identity, rather than storing a separate one.

The cookie the caller holds — whether the one already sitting in the browser or the one this
response sets again — now authenticates as the new user. The old session isn't revoked, only
overwritten.

That renewal also re-attributes the index row: the user id recorded there becomes the new user's,
not the previous one's. The next section covers how user-level revocation (a password change, an
account deletion) relies on that same index, so a session renewed in place is now revoked by the
new user's password change or deletion, never by the previous user's. Log out before switching
accounts, rather than letting the same cookie jar answer for two accounts at once.

## Logout and revocation

Session state lives server-side, which is what makes revocation immediate: logging
out consists entirely of signing the cookie scheme out, deleting that key's session from the
cache, and clearing the browser-side cookie.

It revokes only the cookie side: a bearer token the same account also holds is completely
unaffected — a request authenticated only by bearer still gets `204` from this same endpoint, and
its token stays alive. A token can only be revoked through the next section's revocation endpoint,
or by issuing a new one that overwrites the old hash.

`user_sessions` is a key-to-user index table, and the reason it exists is simple: the distributed
cache itself has no scan or enumerate operation, so without this index there's no way to answer
"which sessions does this user currently have alive." It isn't a collection you can browse or edit
through the item API, and a row is hard-deleted, never soft-deleted; a stale index row is only
cleaned up the next time that user logs in, not by any scheduled sweep of the whole table.

A successful password write immediately reads this index and revokes every session the target
user currently has. A failure in that revocation step doesn't roll back the password change — it's
reported with its own error code, `SESSION_REVOCATION_FAILED`, because the caller needs to know
some sessions may still be alive. Deleting a user row (soft-delete or purge) triggers the same
revocation at the service layer, so GraphQL's delete mutation is covered too.

## Bearer tokens

A bearer token is a separately issued, long-lived credential for a given user, and only a
super-admin can issue one, through `POST /api/users/{id}/access-token`. It appears in the response
only once, at issue time, and only its hash is kept afterward. The raw value is 256 bits of random
data, base64url-encoded with padding stripped; what's stored is its uppercase hexadecimal SHA-256
digest.

A token never expires on its own — it stays valid until it's revoked
(`DELETE /api/users/{id}/access-token`), or until a new one is issued and overwrites it.
Overwriting means replacing that same hash column, and the old token stops working immediately.

Every request that authenticates with bearer updates its last-used timestamp, throttled to at most
one write per token per minute, so a busy integration doesn't turn every call into a write. When
the account itself is deactivated, authentication fails regardless of whether the token is
otherwise still valid, and this flag is re-checked on every request.

Bearer counts on every endpoint, including the read endpoints that declare no scheme — it
isn't only write actions that recognize it; a bearer caller's permissions are its own role's
grants, not anonymous. A bearer call needs no CSRF header; the full selection rule and CSRF detail
are in [Chapter 12: REST API Conventions](12-rest-conventions.md).

Issue a token for `editor@example.com`, read once with it, revoke it, then read again:

```text
$ POST /api/users/01a0a2e2-2751-74e9-9f41-0913880cba31/access-token
{"success":true,"data":{"token":"Lg9y_o7xrCFg2nGIQzeOPPluSndpdNQ-Hr9YtfNW7Pc"}}
HTTP_STATUS:200

$ GET /api/items/article?limit=1  (Authorization: Bearer <token>, no cookie)
{"success":true,"data":[{"id":"01a08f92-4137-72e2-afeb-a0451167539c","version":4,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:21.975394","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:21.975508","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Draft piece","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":3,"limit":1,"offset":0}}
HTTP_STATUS:200

$ DELETE /api/users/01a0a2e2-2751-74e9-9f41-0913880cba31/access-token

HTTP_STATUS:204

$ GET /api/items/article?limit=1  (Authorization: Bearer <token>, no cookie)
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Read not permitted."}}
HTTP_STATUS:401
```

This transcript carries no session cookie at all, so the last step shows only "revoked token →
anonymous → 401": the broken bearer header degrades this request to anonymous, and this
environment's `article` collection doesn't grant anonymous read — the authorization rule is in
[Chapter 17: Roles and Permissions](17-roles-and-permissions.md).

## Login rate limiting

Every anonymous login attempt, whatever the outcome, runs one full Argon2id verification; left
unchecked, brute-forcing passwords doubles as a CPU-exhaustion attack. Two independent layers of
defense guard against it, and neither can substitute for the other.

Per-account throttling (`RateLimiting:LoginAccount`) is on by default, partitioning by the account
in the request body (keyed on a hash of the email, never the plain text), checked before the
password is verified — a request it blocks costs no Argon2id computation at all. This
layer is a fixed window, not a sliding one: the window starts counting once, and further failures
during it never push the end time back; once the window passes, the next failure starts a new one.

A missing account, a wrong password, and a deactivated account all count alike as one failure, and
a blocked request always returns the same `429`, so the throttle itself can't be turned into a
tool for guessing whether an account exists; a successful login clears that account's count.

Per-caller-IP throttling (`RateLimiting:Login`) is the opposite: off by default, and it protects
only the login endpoint — logout, `me`, and the OIDC challenge are all unaffected by it.
Why it's off by default, and when it's appropriate to turn on, is in the `RateLimiting` section of
[Chapter 4: Configuration Reference](04-configuration.md).

Both layers return `429` when they block, with the envelope code always `TOO_MANY_REQUESTS` and
`Retry-After` always present, so the caller can't tell the two apart. The transcript below sends
ten failed logins in a row against the same never-used email, and the eleventh trips the
throttle — the response shown is one more request sent after the throttle engaged:

```text
$ POST /api/auth/login (-i, the attempt after the first 429)
body:
{"email":"$TW","password":"nope"}
HTTP/1.1 429 Too Many Requests
Content-Type: application/json; charset=utf-8
Date: Tue, 15 Sep 2026 02:25:50 GMT
Server: Kestrel
Retry-After: 897
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"success":false,"error":{"code":"TOO_MANY_REQUESTS","message":"Too many login attempts. Please try again later."}}
HTTP_STATUS:429
```

The `$TW` in the transcript is an email that's never been used. Only this one account is blocked:
within the same run, a correct login for another account still goes through immediately. The
partition key is the account, not the caller's IP.

## OIDC external login

When `Oidc:Enabled` is `false` (the default) or `Oidc:Authority` is left blank, the external-login
scheme is never registered, and `GET /api/auth/login/oidc` returns `404`. Which keys are required
to enable it, and what happens when one is missing, is in the `Oidc` section of
[Chapter 4: Configuration Reference](04-configuration.md).

An anonymous `GET /api/config` exposes an `oidcEnabled` field, so the login page knows whether to
offer the external-login button before the user types anything.

External login is the authorization code flow with PKCE. The provider's own token is never
stored, claim names use the short form, and a separate call to the userinfo endpoint fills in the
rest. The callback path is `Oidc:CallbackPath`, defaulting to `/signin-oidc`, and that's the
redirect URL to register with the provider.

The requested scopes are `Oidc:Scopes`, defaulting to `openid`, `email`, `profile` — leaving it
blank and giving an empty array both fall back to that same default, never to no scope at all. The
claims it reads:

- `email` — when missing, falls back to the longer email claim type, then to `preferred_username`;
- `name`, with the same longer-type fallback;
- `iss`, `tid`, `email_verified`.

A validated external identity is never used to log in directly: it's first resolved, matched or
created on the spot into a local user, then exchanged for a local cookie identity carrying only
that user's id — what lands in session state is the exact same mechanism a password login uses.
Resolution checks the following in order, and failing any one of them fails the login outright:

- the tenant matches (`Oidc:AllowedTenantId`), case-insensitively; the default value is a
  placeholder that matches no real tenant, blocking every external login until it's replaced;
- the email is verified (`Oidc:RequireEmailVerified`, not required by default) — a missing or
  unparseable claim counts as unverified either way;
- the identity carries a usable email at all; without one the login is rejected outright, whatever
  else is configured, since this is the only key this flow can match against a local account;
- the email's domain is on the allowlist (`Oidc:AllowedEmailDomains`, an empty list by default
  meaning unrestricted);
- the matched local account is still active — a deactivated account is never reactivated just
  because an external identity passed validation.

Matching is by email equality, case-insensitive. When nothing matches, a new account is created on
the spot — that's what "just-in-time provisioning" means, with no administrator having to open an
account beforehand.

A JIT-provisioned account is active, role-less, and its password field is empty; having no role
means it has nothing beyond the `public` floor — external login resolves identity, not
authorization. Roles and that floor are covered in
[Chapter 17: Roles and Permissions](17-roles-and-permissions.md). It has no local password either,
so the password-change endpoint returns the same `400` `NO_LOCAL_PASSWORD`.

Matching on email equality alone is a deliberately accepted risk: without at least one extra check
layered on top, anyone whose email matches in any identity provider could in theory take over an
existing password account. How to configure those guards for a production environment is in the
`Oidc` section of [Chapter 4: Configuration Reference](04-configuration.md).

A failed external login returns `401` with `{ "error": { "message": "External login failed." } }`
(`External login denied.` when the provider itself reports that the user declined) — this shape is
hand-written, not the standard envelope every other endpoint shares. `GET /api/auth/login/oidc`
accepts a `returnUrl`, sanitized to an in-site path before it's used in the challenge; leaving it
out falls back to `Oidc:ReturnUrlDefault`.

## What's next

That's authentication covered; what a caller can do once authenticated is in
[Chapter 17: Roles and Permissions](17-roles-and-permissions.md).
