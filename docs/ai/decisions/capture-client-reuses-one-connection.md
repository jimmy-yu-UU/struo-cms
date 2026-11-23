# The capture client reuses one connection

## Decision

The HTTP client of the manual's capture tool (`docs/scripts/capture/lib/client.mjs`) keeps one
keep-alive agent per client, so a run travels over one long-lived connection. A request that fails
with `ECONNRESET` on a reused socket is sent once more when its method is GET, HEAD or OPTIONS. Any
other method fails the step, and the failure is reported with the step's capture id.

## Why

- A new connection per request produced intermittent `ECONNRESET` errors on the development machine.
  The run cannot tell such a reset from a host that is down, and a capture that stops half-way
  leaves a database that holds partial data.
- A server can close an idle kept-alive socket just as the next request goes out. A reset on a
  reused socket does not show that the server never processed the request, so only reads are
  repeated. A repeated create or delete would shift the ids and totals the manual prints.

## Evidence

Measured on 2026-11-23 on one Windows 11 development machine with Node 24: 100 sequential GET
requests to a local Node `http` server. With a new connection for each request, 17 failed (15
`ECONNRESET`, 2 timeouts). Over one kept-alive connection, 0 failed.

On the same machine a DELETE request with a body, sent to a loopback test server, was answered with
an HTML block page that the test server did not produce. Something on the machine inspects loopback
HTTP traffic.

## Unknowns

The cause of the resets is not identified. The measurement comes from one machine; a CI runner or a
Linux host may never reset a new connection, and then the keep-alive agent changes nothing.

## Referenced from

- `docs/scripts/capture/lib/client.mjs`
- `docs/ai/task-playbooks.md` (Playbook 6)
