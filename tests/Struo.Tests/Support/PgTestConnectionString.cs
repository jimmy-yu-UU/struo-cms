using System.Text.RegularExpressions;

namespace Struo.Tests.Support;

/// <summary>
/// Connection-string shaping for the opt-in PostgreSQL suite (<c>PostgresIntegrationTests</c>).
///
/// Why this exists: that suite builds a fresh <c>ISqlSugarClient</c> per test and disposes it, but
/// Npgsql's connection pool is process-wide and outlives every one of those clients. Reuse of a pooled
/// physical connection across a connection-close boundary — whether that boundary falls between two
/// tests or between two commands inside one test — is what makes one test in the suite go red with
/// <c>NpgsqlException: Exception while reading from stream</c> → <c>IOException</c> →
/// <c>SocketException</c> carrying the Windows <c>WSA_OPERATION_ABORTED</c> text ("the I/O operation
/// has been aborted because of either a thread exit or an application request"). Diagnosed 2026-08-04.
/// This class doc is the full write-up; AGENTS.md's Verification section carries only the summary. The
/// evidence:
///   * the failing test passes 5/5 when it is the only test in the process, so its own logic is fine;
///   * it usually fails only once some other test in the suite has run before it, whichever one that
///     is — but one run instead went red on the FIRST test executed, with the pool still empty, so a
///     preceding test is not required and reuse within a single test can produce it too;
///   * a warm-up connection opened first does not help, so it is not a cold-start effect;
///   * PostgreSQL logs no error at all for these runs (log_min_messages=warning would show one) and
///     the server is nowhere near max_connections, so the abort is raised locally, not by the server;
///   * with pooling off the whole suite is green 5/5, with pooling on it went red in 8 of 9 runs;
///   * it is not specific to any one branch — the same abort, same stack trace, reproduced at the
///     merge-base 74c5dcc in 5 of 6 runs — and which test goes red is not stable, so if this suite
///     turns red on you, check whether pooling was re-enabled before assuming you caused it.
/// What that last comparison earns is that pooled reuse across a close boundary is NECESSARY for the
/// failure — which is exactly what this class removes. It does not explain why only one test goes red;
/// in particular the test executed immediately after the first one passes, and the mechanism does not
/// account for that. Taking the pool out is therefore test isolation aimed at the one variable proven
/// to control the failure — the same category as giving each test its own database. It is NOT a retry,
/// a swallowed exception, or a relaxed assertion, and it costs the suite nothing it was built to
/// check: every test still runs real DDL and DML against a real PostgreSQL, so the type-mapping,
/// column-semantics and concurrency divergences this suite exists to catch are all still exercised.
///
/// Scoped to the harness, and measured 2026-08-05 — because the abort surfaced inside product code
/// (<c>SqlSugarItemRepository.CreateGenericAsync</c>) on a pooled connection, which is production's own
/// configuration, "harness-only" had to be measured rather than assumed. It now has measurement behind
/// it, though not proof: the same repository code aborted 4 times in 46 runs of a minimal xUnit project
/// (the positive control) but 0 times in 150 runs of a plain console process across five threading
/// models, and <c>Struo.Api</c> booted as Production against real PostgreSQL with pooling on served
/// 22,023 item-API requests with 0 aborts. So in everything measured here, the abort tracks the
/// xUnit/VSTest test host, not the product's use of a pooled connection. That is a non-observation, NOT
/// a proof of safety — the mechanism is still unknown, so it does not rule the abort out for a different
/// threading model, load shape or Windows build.
///
/// What amplifies the bare host's ~9% into this suite's every-run failure is A SECOND NPGSQL POOL in the
/// process. <c>PostgresIntegrationTests</c>' repo/raw-client builders each call
/// <c>DbMaintenance.CreateDatabase()</c> to self-provision the disposable database, which opens a
/// connection whose string differs only in <c>Database=</c> — a separate pool. Adding that one call to
/// the bare probe took it from 2 of 15 to 15 of 15. It is the second pool that does it, not the extra
/// connection and not the swallowed error: an extra pooled open/close on the SAME database went 0 of 12
/// and a swallowed server-side error on the same database 1 of 12, while a plain connection to the
/// maintenance database that throws nothing went 11 of 12. Production cannot reach that state — it binds
/// one connection string, never calls <c>DbMaintenance.CreateDatabase()</c>, and never constructs an
/// <c>NpgsqlConnection</c> directly, so it has exactly one pool. That removes the AMPLIFIER from
/// production, not the phenomenon: the single-pool bare xUnit host still failed ~10% of runs.
///
/// One residual unknown, and it stays one by decision: WHAT aborts the socket is inferred from the
/// Windows error code — a thread-exit I/O cancellation, xUnit's worker threads the plausible source —
/// and is unproven. The 2026-08-05 probe weakens it slightly rather than settling it: three separate
/// renderings of "a thread that exits" outside xUnit stayed green, though those are a reconstruction of
/// xUnit's threading, not xUnit's own.
///
/// Disabling pooling here still removes the repo's only local reproduction of the abort. The way back to
/// one is the escape hatch below: set <c>Pooling=true</c> yourself in the connection string.
/// </summary>
internal static class PgTestConnectionString
{
    // Whole-key match: `Pooling` as a key at the start of the string or after a `;`, up to its `=`.
    // Substring matches (e.g. an "Application Name=Pooling-probe" value) must not count as the key.
    private static readonly Regex PoolingKey = new(
        @"(^|;)\s*Pooling\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns <paramref name="connectionString"/> with Npgsql pooling disabled. An empty/whitespace
    /// input (PostgreSQL not configured, so the suite skips) and a connection string that already
    /// states <c>Pooling</c> explicitly are both returned unchanged — an explicit caller choice wins,
    /// so re-investigating the abort is a matter of setting <c>Pooling=true</c> yourself.
    /// </summary>
    public static string DisablePooling(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;
        if (PoolingKey.IsMatch(connectionString)) return connectionString;

        var separator = connectionString.TrimEnd().EndsWith(';') ? "" : ";";
        return $"{connectionString.TrimEnd()}{separator}Pooling=false";
    }
}
