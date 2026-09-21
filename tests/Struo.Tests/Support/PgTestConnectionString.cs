using System.Text.RegularExpressions;

namespace Struo.Tests.Support;

/// <summary>
/// Connection-string shaping for the opt-in PostgreSQL suite (<c>PostgresIntegrationTests</c>): disables
/// Npgsql connection pooling so each test gets its own physical connection instead of reusing one from
/// the process-wide pool.
///
/// Measured: the abort this guards against tracks the xUnit/VSTest test host, not product code — the
/// Production probe against real PostgreSQL with pooling on saw 0 aborts.
/// Ruled out: a cold start, a server-side cause, and a branch-specific cause.
/// Unknown: which actor aborts the socket; it is inferred from the Windows error code, not proven.
/// If undone: the abort returns, and the suite's red test is not a stable, reproducible failure.
///
/// The escape hatch is below: set <c>Pooling=true</c> yourself in the connection string.
///
/// Full write-up: docs/ai/decisions/pg-test-connection-pooling.md
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
