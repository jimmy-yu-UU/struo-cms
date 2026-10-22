# 25. After Forking

This chapter is a checklist for after you fork StruoCMS. A few things carry the template author's
name, secrets store or reporting channel, and a fork should swap them for its own. Each item says
what to do, why, and how. They don't depend on each other, so do the ones that apply to you.

## 1. LICENSE: keep the MIT notice, add your own copyright line

The `LICENSE` at the repository root is MIT. MIT requires every copy to carry the original copyright
notice and the license text, so don't remove the existing line.

Add your own line below the existing `Copyright (c) …`, for example `Copyright (c) <year> Your Name`.
The code your fork adds is yours; the original parts stay with the original author.

## 2. `UserSecretsId`: regenerate it

`src/Struo.Api/Struo.Api.csproj` has a fixed `UserSecretsId`. Every clone carrying that ID on one
machine reads and writes the same user-secrets store, so two projects overwrite each other's
connection strings and OIDC secrets.

Delete the `<UserSecretsId>` element from the csproj, then run:

```bash
dotnet user-secrets init --project src/Struo.Api
```

The command writes a fresh GUID. Secrets already stored under the old ID don't follow; set them again.

## 3. SonarQube Cloud: set your own variables and secret

The `sonar-backend` and `sonar-frontend` jobs in `.github/workflows/ci.yml` read the organization and
project keys from repository variables: `SONAR_ORGANIZATION`, `SONAR_BACKEND_PROJECT_KEY` and
`SONAR_FRONTEND_PROJECT_KEY`. The token is the secret `SONAR_TOKEN`. Set all of them under Settings →
Secrets and variables → Actions.

Both jobs are skipped while `SONAR_ORGANIZATION` is unset, so a fork that doesn't use Sonar has
nothing to do. Once you set it, set the other three too, or the jobs fail on the missing values.
Pull requests from forks and from Dependabot can't read the secret, so the jobs are always skipped
for them.

## 4. `samples/Struo.Sample.Blog`: delete it

`samples/Struo.Sample.Blog` is a learning aid, not part of your project. Delete it once you've read
it. It reaches beyond its own directory, because the test project uses its collections as a fixture,
so follow the checklist rather than just removing the folder. The steps are in "Removing the sample
completely" in [Chapter 23: Sample Walkthrough](23-sample-walkthrough.md).

## 5. `SECURITY.md`: replace the reporting channel

The `SECURITY.md` at the repository root describes how to report a vulnerability in this template:
privately, through GitHub's "Report a vulnerability" on this repository. Your fork is a different
repository, so that text is wrong for your users.

Rewrite it for your own channel: turn on private vulnerability reporting under Settings → Code
security in your fork, and set the supported versions and the response time to what you can keep.

## 6. Admin-UI default language

The admin UI defaults to `en`. To change it, set `VITE_DEFAULT_UI_LOCALE` at build time: a dev server
reads `frontend/.env`, and an image build reads a `frontend/.env.production` you add and commit
yourself. A language a user picked in the switcher always wins. How to register a new language, and
the details of this variable, are in "UI language" in
[Chapter 19: Admin Customization](19-admin-customization.md).

## 7. A production domain under a shared parent domain: consider the `__Host-` cookie prefix

The login cookie is named `struo.session`. If your production site is a subdomain of a shared parent
domain (say `app.example.com`, with other subdomains under the same parent controlled by someone
else), those sibling subdomains can set a cookie of the same name for the whole parent domain,
overwriting or planting your session cookie.

The `__Host-` prefix makes the browser refuse such cookies: a cookie with this prefix must be
`Secure`, have `Path=/` and carry no `Domain`, so a sibling subdomain can't write it. This project's
cookie is already `Path=/` with no `Domain`; only the name is missing.

The name is the constant `SessionCookieName` in `src/Struo.Api/Auth/AuthSchemes.cs`, with no
configuration key. Change it to `__Host-struo.session`; the CSRF and session-rotation middleware read
the same constant. The literal `struo.session` shown in
[Chapter 13](13-rest-endpoints.md) and [Chapter 16](16-authentication.md) changes with it. Three
things to know first:

- The prefix requires `Secure`, and only the browser-facing origin has to be HTTPS. Outside
  Development the cookie is `Secure` whatever scheme the API itself sees, so nginx can still talk to
  the API over plain http. `RequireSecure=false` means `SameAsRequest`: the cookie loses `Secure`
  only when the API itself sees plain http, as on a host with no TLS in front, and the browser then
  rejects the prefixed cookie.
- The constant applies in Development too, and there `Auth:Cookie:RequireSecure` resolves to
  `SameAsRequest`: over `http://localhost` the cookie carries no `Secure`, the browser rejects it, and
  local login and the e2e suite fail. Set `Auth__Cookie__RequireSecure=true` for local runs and the
  e2e host. The cookie then carries `Secure`, and Chrome and Firefox accept a `Secure` cookie from
  `http://localhost`. A browser that doesn't (Safari) needs HTTPS locally.
- Renaming the cookie signs everyone out once.

## 8. Images or the API on another origin: `CSP_IMG_EXTRA` and `CSP_CONNECT_EXTRA`

The `Content-Security-Policy` the admin image sends allows only its own origin. When images come from
somewhere else (a CDN, a bucket's presigned URL, a `Branding:LogoUrl` on another host), the browser
blocks them. It also blocks the SPA when it calls an API on another origin directly.

Add the origins with the environment variables `CSP_IMG_EXTRA` (appended to `img-src`) and
`CSP_CONNECT_EXTRA` (appended to `connect-src`) when the container starts. The nginx template is
rendered at that point, so no rebuild is needed. The value is space-separated origins such as
`https://cdn.example.com`. The full description is in "The admin SPA image" in
[Chapter 20: Deployment](20-deployment.md).

## 9. A reverse proxy in front of the admin nginx: `ForwardedHeaders`

The API doesn't trust `X-Forwarded-For`, `X-Forwarded-Proto` or `X-Forwarded-Host` by default.
Without that, the client address the API sees is its direct caller's, which breaks the login IP rate
limit if you enable it, and OIDC builds an `http` redirect URI when TLS ends at a proxy.

Set `ForwardedHeaders__Enabled=true` and list the API's own proxy in
`ForwardedHeaders__KnownProxies__0` or `ForwardedHeaders__KnownNetworks__0`. With the admin image in
front of the API, that proxy is the admin nginx container: the API sees that container as its
client, so list the container's address (or a dedicated network's range), not your outer proxy. The
outer proxy adds a second hop that the API doesn't honour. The details, and the headers the outer
proxy has to set, are in "Behind a reverse proxy, enable `ForwardedHeaders`" in
[Chapter 20: Deployment](20-deployment.md).

## 10. Single sign-on providers: add yours and register the redirect URIs

The template ships two disabled entries, `microsoft` and `google`, under `Oidc:Providers` in
`src/Struo.Api/appsettings.json`. Keep the ones you use, delete the rest, and add one entry for each
other OpenID Connect provider your users sign in with. Each entry needs its own id, `DisplayName`,
`Authority`, `ClientId` and `ClientSecret`; set the secret through an environment variable such as
`Oidc__Providers__<id>__ClientSecret`, never in a committed file.

A provider's button shows the glyph named by its `Icon`. The built-in tokens are `microsoft` and
`google`. For another brand, add an SVG entry to the `ICONS` table in `frontend/src/lib/ssoIcons.ts`,
set its name as the entry's `Icon`, and rebuild the admin. Without a matching entry the button shows a
generic key icon.

Register the redirect URI at each provider's console: your production origin plus the provider's
callback path: its `CallbackPath`, or `/signin-oidc-<id>` when the entry has none. Do the same for a
staging origin, and register `http://localhost:5173` plus the same path for development. A custom `CallbackPath` outside
`/api/` also needs a `location` block in `frontend/nginx/default.conf.template`, and an entry in
`frontend/vite.proxy.ts` for the dev server. The setup steps per
provider are in [Chapter 16: Authentication and SSO](16-authentication.md).
