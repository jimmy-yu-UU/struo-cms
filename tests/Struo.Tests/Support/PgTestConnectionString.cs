using System.Text.RegularExpressions;

namespace Struo.Tests.Support;

/// <summary>
/// Connection-string shaping for the opt-in PostgreSQL suite (<c>PostgresIntegrationTests</c>).
///
/// Why this exists: that suite builds a fresh <c>ISqlSugarClient</c> per test and disposes it, but
/// Npgsql's connection pool is process-wide and outlives every one of those clients. Reusing a pooled
/// physical connection across test boundaries is what makes exactly one test in the suite go red with
/// <c>NpgsqlException: Exception while reading from stream</c> → <c>IOException</c> →
/// <c>SocketException</c> carrying the Windows <c>WSA_OPERATION_ABORTED</c> text ("the I/O operation
/// has been aborted because of either a thread exit or an application request"). Diagnosed 2026-08-04;
/// the evidence is recorded in AGENTS.md's Verification section. In short:
///   * the failing test passes 5/5 when it is the only test in the process, so its own logic is fine;
///   * it fails whenever ANY other test in the suite runs before it, whichever one that is;
///   * a warm-up connection opened first does not help, so it is not a cold-start effect;
///   * PostgreSQL logs no error at all for these runs (log_min_messages=warning would show one) and
///     the server is nowhere near max_connections, so the abort is raised locally, not by the server;
///   * with pooling off the whole suite is green 5/5, with pooling on it went red in 8 of 9 runs.
/// Taking the pool out is therefore test isolation aimed at the one variable that was proven to
/// control the failure — the same category as giving each test its own database. It is NOT a retry,
/// a swallowed exception, or a relaxed assertion, and it costs the suite nothing it was built to
/// check: every test still runs real DDL and DML against a real PostgreSQL, so the type-mapping,
/// column-semantics and concurrency divergences this suite exists to catch are all still exercised.
///
/// This is scoped to the test harness only. It is NOT a statement that StruoCMS needs pooling
/// disabled in production — the application pools normally, and nothing here suggests otherwise.
/// </summary>
public static class PgTestConnectionString
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
