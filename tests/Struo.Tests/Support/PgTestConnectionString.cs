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
/// has been aborted because of either a thread exit or an application request"). Diagnosed 2026-08-04;
/// the evidence is recorded in AGENTS.md's Verification section. In short:
///   * the failing test passes 5/5 when it is the only test in the process, so its own logic is fine;
///   * it usually fails only once some other test in the suite has run before it, whichever one that
///     is — but one run instead went red on the FIRST test executed, with the pool still empty, so a
///     preceding test is not required and reuse within a single test can produce it too;
///   * a warm-up connection opened first does not help, so it is not a cold-start effect;
///   * PostgreSQL logs no error at all for these runs (log_min_messages=warning would show one) and
///     the server is nowhere near max_connections, so the abort is raised locally, not by the server;
///   * with pooling off the whole suite is green 5/5, with pooling on it went red in 8 of 9 runs.
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
/// threading model, load shape or Windows build. AGENTS.md's Verification section carries the full
/// numbers.
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
