# `LoginConcurrency` is one process-wide partition

## Decision

`RateLimiting:LoginConcurrency` caps how many `POST /api/auth/login` requests verify a password at the
same time in one process. `CreateLoginConcurrencyLimiter` in
`src/Struo.Api/Hosting/StruoCmsRateLimiting.cs` gives the `login` endpoint a single concurrency
partition named `login-concurrency`; every other request gets no limiter there. The defaults are
`PermitLimit` 4 and `QueueLimit` 32 (`src/Struo.Application/Configuration/LoginConcurrencyOptions.cs`).

The partition is not split by email or by client IP. Throttling that has to tell callers apart belongs
to the layers in front of the API (a reverse proxy or WAF rate limit) and to the per-account and
per-IP limiters, which are separate settings.

## Why

The cap protects the CPU and memory that every login spends on Argon2id, and those are shared by the
whole machine. Its job is to shed load when logins pile up, whoever sends them.

- Partitioning by email lets an attacker use a different email in each request and stay under every
  partition's cap while the machine is still saturated.
- Partitioning by client IP does not stop a distributed flood. Behind a reverse proxy without
  `ForwardedHeaders` configured, the API sees the proxy's address for every request, so all clients
  share one partition anyway, which is the global behaviour with an extra layer of keys.
- A global partition under flood also rejects legitimate logins. That is the price of keeping the
  machine responsive, and it is paid until the proxy sheds the flood.

## Evidence

Second-round review (the Grok recheck, 2026-11-02, report item R2-04): with 80 simultaneous login
requests from one attacker, 44 returned `429`; during the same period a victim's logins returned `429`
in 6 of 6 attempts. The review measured this on its own run. In this repository
`Global_concurrency_cap` (`tests/Struo.Tests/Api/LoginConcurrencyLimitTests.cs`) shows that a second
login gets `429` while the one permit is taken, even with a different password; no test reproduces the flood.

The configuration comment and defaults are in `src/Struo.Api/appsettings.json` under
`RateLimiting:LoginConcurrency`. The per-account throttle `RateLimiting:LoginAccount` and the per-IP
limiter `RateLimiting:Login` are described in the same file.

## Unknowns

The review's remedy options (a partition by email or IP, or a higher cap) were not tried here, so how a
partitioned cap would behave under the same flood is reasoned, not measured.

## Referenced from

- `src/Struo.Api/Hosting/StruoCmsRateLimiting.cs`
