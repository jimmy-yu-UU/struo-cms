# 16. Authentication and SSO

A caller proves who it is with a password login, a session cookie, a bearer token, or a login
through an external identity provider. Whether a caller should use the cookie or a bearer token,
and when the CSRF header is required, is covered in
[Chapter 12: REST API Conventions](12-rest-conventions.md).

This chapter covers the mechanics behind each credential — how it's issued, where it lives, when
it expires, and how it's revoked. What a caller can do once authenticated is in
[Chapter 17: Roles and Permissions](17-roles-and-permissions.md).

## The first administrator

The moment the `struo_users` table is created and still empty, the first administrator account is
created along with it: active, its name fixed at `Administrator`, its email and password taken
from `Auth:BootstrapAdmin`; an empty password is replaced by a generated one. This happens exactly
once — changing the setting and restarting does nothing to an existing database.

The template ships no bootstrap password. When `Auth:BootstrapAdmin:Password` is empty, the seed
generates a random password of at least 24 characters and logs it once, as a Warning, in the run
that creates the account. A password you configure goes through the password policy the next
section covers, and `admin` is refused in any letter case; while a weak value is configured,
`migrate`, `migrate:preview` and a `Database:MigrateOnStartup` start refuse with an error that
names `Auth__BootstrapAdmin__Password`. The value only seeds the account, so it can be removed
afterwards. The transcripts log in with a placeholder for that password. Change it after the first
login.

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
route, since there's no dedicated `GET /api/users/{id}`. The generic `POST /api/items/user` refuses
to create a user (`400`), because only this endpoint hashes the password and applies the password
policy. An email already in use returns `409` `CONFLICT` `Email already in use.`. The password's
minimum and maximum length, and the configuration keys, are in the `Auth` section of
[Chapter 4: Configuration Reference](04-configuration.md).

The admin UI calls this endpoint from the "New" button on the user list, shown to super-admins only.
The dialog asks for an email, an optional display name, and a password typed twice; it takes no
roles, so assign them on the new user's page, which opens once the user is created.

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
`struo.session` unless `Auth:Cookie:Name` says otherwise, `HttpOnly`, `SameSite` defaulting to `Lax`.
The `Secure` policy follows `Auth:Cookie:RequireSecure`; left unset, it is `Always` in every
environment except Development, which follows the request itself.

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
account deletion) relies on that same index, so a session renewed in place is revoked by the
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

`struo_user_sessions` is a key-to-user index table, and the reason it exists is simple: the distributed
cache itself has no scan or enumerate operation, so without this index there's no way to answer
"which sessions does this user currently have alive." It isn't a collection you can browse or edit
through the item API, and a row is hard-deleted, never soft-deleted; a stale index row is only
cleaned up the next time that user logs in, not by any scheduled sweep of the whole table.

A successful password write immediately reads this index and revokes every session the target
user currently has. A failure in that revocation step doesn't roll back the password change — it's
reported with its own error code, `SESSION_REVOCATION_FAILED`, because the caller needs to know
some sessions may still be alive. Deleting a user row (soft-delete or purge) triggers the same
revocation at the service layer, so GraphQL's delete mutation is covered too.

The admin's **Log out** always ends on the login page, even with unsaved edits in a form. If the
server cannot confirm the sign-out, the login page says so; a `401` means no session was left to end
and counts as success. When a request is rejected with `401` while you are signed in, the admin drops
any unsaved edits, goes to the login page with a "session expired" notice and returns to the page you
were on after you sign in again.

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

Each user has at most one token. Changing a password revokes that user's sessions but never the
token, so a password change does not cut off an integration that authenticates with a token; if a
token may have leaked, revoke it or issue a new one instead. The last-used time lives in the
database alone: the API and the admin UI do not show it, so finding stale tokens means reading it
from the users data directly.

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
{"success":true,"data":[{"id":"01a08f92-4137-72e2-afeb-a0451167539c","version":4,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:21.975394Z","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:21.975508Z","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Draft piece","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":3,"limit":1,"offset":0}}
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
unchecked, brute-forcing passwords doubles as a CPU-exhaustion attack. Three independent layers of
defense guard against it, and none can substitute for the others.

Per-account throttling (`RateLimiting:LoginAccount`) is on by default, partitioning by the account
in the request body (keyed on a hash of the email, never the plain text). The attempt is counted
before the password is verified, so parallel guesses cannot all slip past the limit, and a request
it blocks costs no Argon2id computation at all. This layer is a fixed window, not a sliding one:
the window starts counting once, and further attempts during it never push the end time back; once
the window passes, the next attempt starts a new one.

A missing account, a wrong password, and a deactivated account all count alike as one attempt, and
a blocked request always returns the same `429`, so the throttle itself can't be turned into a
tool for guessing whether an account exists; a successful login clears that account's count.

The count is shared by every replica when `Redis:ConnectionString` is set, and a login fails with a
5xx error while Redis is unreachable. Without Redis each replica counts on its own.

Counting every attempt has a cost: whoever knows an account's email can use up its limit and keep
that account locked out of password sign-in until the window ends. This is a deliberate trade-off;
SSO sign-in is unaffected.

A process-wide concurrency cap (`RateLimiting:LoginConcurrency`) bounds how many logins verify a
password at once: 4 run, 32 more wait, and the rest get the same `429` with `Retry-After`. It
protects CPU and memory from a flood of parallel attempts whatever their source.

Per-caller-IP throttling (`RateLimiting:Login`) is the opposite: off by default, and it protects
only the login endpoint — logout, `me`, and the OIDC challenge are all unaffected by it.
Why it's off by default, and when it's appropriate to turn on, is in the `RateLimiting` section of
[Chapter 4: Configuration Reference](04-configuration.md).

Every layer returns `429` when it blocks, with the envelope code always `TOO_MANY_REQUESTS` and
`Retry-After` always present, so the caller can't tell them apart. The transcript below sends
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

Each external identity provider is one entry under `Oidc:Providers:{id}`. A disabled provider
registers no authentication scheme. `GET /api/auth/login/oidc/{provider}` returns `404` for an
unknown or disabled provider, so with no enabled provider it returns `404` for every id. Which keys are required to enable a provider, and what happens when one is
missing, is in the `Oidc` section of [Chapter 4: Configuration Reference](04-configuration.md).
The single-provider keys directly under `Oidc` (such as `Oidc:Authority`) are ignored; the startup log
names the ones found in a warning.

An anonymous `GET /api/config` returns `oidcProviders`, an array of `{ id, displayName, icon }` in
button order (`Order` ascending, then id). The login page draws one full-width button per entry,
labelled "Sign in with" plus `displayName`, before the user types anything. An empty array means no SSO buttons.
The response never carries a secret.

External login is the authorization code flow with PKCE. The provider's own token is never
stored, claim names use the short form, and a separate call to the userinfo endpoint fills in the
rest. Each provider has its own callback path: the shipped `microsoft` entry sets `/signin-oidc`, the
shipped `google` entry `/signin-oidc-google`, and an entry without `CallbackPath` uses `/signin-oidc-{id}`.
Each provider also has two sign-out paths derived from its id: `/signout-callback-oidc-{id}` and `/signout-oidc-{id}`.
No two enabled providers may share a path.

Sign-in always shows the provider's account picker by default (`Prompt` = `select_account`);
chapter 4 lists the other values and how to send no prompt.

### Button icons

`Icon` is a token, not a URL. The admin UI maps it to an inline SVG mark in
`frontend/src/lib/ssoIcons.ts`. The built-in tokens are `microsoft` and `google`; matching ignores
case. An empty or unknown token shows a generic key icon.

To add a brand mark in a fork, add an entry to the `ICONS` table in that file, keyed by a lowercase
name, and set the name as the provider's `Icon`. Then rebuild the admin. `Icon` never takes a URL; each
mark is an SVG component in that table.

### Setting up Microsoft Entra

Use the single-tenant authority, `https://login.microsoftonline.com/<tenant-id>/v2.0`, and put the
same tenant id in `AllowedTenantId`. In the app registration, add a Web redirect URI of the admin's
origin plus the callback path: with the shipped `microsoft` entry, `https://cms.example.com/signin-oidc`.
If you change `CallbackPath` or use another id without one, register that path instead, such as
`https://cms.example.com/signin-oidc-<id>`.

### Setting up Google

In the Google Cloud console, create an OAuth client of type Web application and add
`https://cms.example.com/signin-oidc-google` as an authorized redirect URI. Use
`https://accounts.google.com` as the `Authority`, the client's id as `ClientId`, and its secret as
`ClientSecret` through an environment variable or user-secrets. Google has no tenant, so leave
`AllowedTenantId` empty. The shipped entry has `RequireEmailVerified` on; keep it.

An enabled Google provider must also set `AllowedEmailDomains` to your company's domain, or the host
fails to start. Without it, any Google account in the world could sign in and get a new account with no
roles. The shipped entry is disabled and has an empty list, so enabling it takes that one extra step.

### Any other OpenID Connect provider

Add an entry with any id of lowercase letters, digits and hyphens, the provider's `Authority`, the client id and secret,
and a `DisplayName`. Register `https://cms.example.com/signin-oidc-<id>` as the redirect URI. Accounts
are linked by email, so if the provider can issue tokens with an unverified email, turn on
`RequireEmailVerified` or pin `AllowedEmailDomains`. Otherwise a person who controls a matching
unverified address can take over an existing account.

### Redirect URIs

The redirect URI to register with the provider is the origin users open the admin at plus the
provider's callback path: `https://cms.example.com/signin-oidc-google` for the shipped `google` entry, or
`http://localhost:8080/signin-oidc-google` for the Compose stack. A plain `http` redirect URI works
only on `localhost`: Entra rejects other `http` redirect URIs, and the OIDC correlation cookies are
`Secure`, so any other origin needs `https`. Register the sign-out callback
`/signout-callback-oidc-{id}` the same way if you use it.

For development under `pnpm dev`, register `http://localhost:5173/signin-oidc` for Entra and
`http://localhost:5173/signin-oidc-google` for Google, and `http://localhost:5173/signout-callback-oidc-{id}`
if you use the sign-out callback. The Vite dev
server forwards these paths to the API and keeps the browser's `Host`, so the API builds that redirect
URI from the host you opened the dev server with. Open `http://localhost:5173`, not
`http://127.0.0.1:5173`, to match the registered URI.

The admin image's nginx proxies `/signin-oidc-{id}`, `/signout-callback-oidc-{id}` and
`/signout-oidc-{id}` to the API alongside `/api/`, so the provider's redirect lands on the same origin
the sign-in started from. The Vite dev server forwards the same paths. A custom `CallbackPath` under
`/api/`, such as `/api/signin-oidc-acme`, is covered by the `/api/` rule with no nginx change. Any
other custom path needs its own `location` block in `frontend/nginx/default.conf.template`, or a
matching rule in your own proxy.

The API builds the redirect URI from the scheme and host of the request it receives. nginx passes
the client's `Host` header through with its port, so `http://localhost:<port>` works as is. A
public `https` origin needs the API to see `https` as well: when TLS ends at a proxy in front of
the admin container, make that proxy set `X-Forwarded-Proto: https`, overwriting rather than
appending, because the image's nginx passes the incoming value to the API unchanged (and uses its
own scheme only when none arrives). Then enable `ForwardedHeaders` on the API with the nginx
container's own address, or a dedicated network, as the known proxy; do not trust a whole shared
Docker network range or publish the API port (Chapter 20), or the API ignores the header or can be
made to trust a forged one.

The requested scopes are the provider's `Scopes`, defaulting to `openid`, `email`, `profile` — leaving it
blank and giving an empty array both fall back to that same default, never to no scope at all. The
claims it reads:

- `email` — when missing, falls back to the longer email claim type, then to `preferred_username`;
- `name`, with the same longer-type fallback;
- `iss`, `tid`, `email_verified`.

A validated external identity never logs a user in directly: it's first resolved, matched or
created on the spot into a local user, then exchanged for a local cookie identity carrying only
that user's id — what lands in session state is the exact same mechanism a password login uses.
Resolution checks the following in order, and failing any one of them fails the login outright:

- the tenant matches the provider's `AllowedTenantId`, case-insensitively; a blank value skips this
  check, and the shipped `microsoft` entry's placeholder matches no real tenant, blocking every
  external login through it until it's replaced;
- the email is verified (the provider's `RequireEmailVerified`, not required by default) — a missing or
  unparseable claim counts as unverified either way;
- the identity carries a usable email at all; without one the login is rejected outright, whatever
  else is configured, since this is the only key this flow can match against a local account;
- the email's domain is on the allowlist (the provider's `AllowedEmailDomains`, an empty list by
  default meaning unrestricted);
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

Two combinations are refused at startup. The first: when a provider is enabled and its `Authority` has a path segment
of `common`, `organizations`, `consumers` or the consumers tenant GUID (an Entra multi-tenant endpoint, where anyone can create
a tenant and present any email), the host fails to start unless that provider's `AllowedTenantId` or
`AllowedEmailDomains` is set. The error names both keys. Blank values don't count as set.
The second is a Google `Authority` (`https://accounts.google.com`, in any case, with or without a trailing
slash): the provider needs a non-empty `AllowedEmailDomains`, because Google has no tenant to pin, and the
error names that key. A single-tenant authority, or a disabled provider, is never affected.

A failed external login redirects the browser to the admin login page, `/login?error=<code>`, which
shows a localized message and removes the parameter from the address. The code is one of a fixed
set and never carries exception text:

| Code | Cause |
|---|---|
| `external_login_failed` | The provider or the callback failed |
| `external_login_denied` | The user declined at the provider |
| `external_login_NoEmail` | The identity carries no usable email |
| `external_login_EmailNotVerified` | The email is unverified and `RequireEmailVerified` is on |
| `external_login_DomainNotAllowed` | The email's domain is not in `AllowedEmailDomains` |
| `external_login_TenantNotAllowed` | The tenant is not the provider's `AllowedTenantId` |
| `external_login_Inactive` | The matched local account is deactivated |

The login page shows the generic message for any other value. `GET /api/auth/login/oidc/{provider}` accepts a
`returnUrl`, sanitized to an in-site path before it's used in the challenge; leaving it out falls
back to `Oidc:ReturnUrlDefault`.

## What's next

That's authentication covered; what a caller can do once authenticated is in
[Chapter 17: Roles and Permissions](17-roles-and-permissions.md).
