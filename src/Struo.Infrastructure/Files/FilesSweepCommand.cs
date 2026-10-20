using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Files;
using Struo.Domain.Auditing;
using Struo.Infrastructure.Migrations.Commands;

namespace Struo.Infrastructure.Files;

/// <summary>
/// <c>files:sweep</c>: finds blobs in the byte store that no <c>files</c> row references (trashed rows
/// count as references) and, with <c>--apply</c>, deletes them. Blobs younger than the grace period are
/// never touched, so an upload still between its blob save and its row insert is safe.
/// </summary>
internal static class FilesSweepCommand
{
    public const string Name = "files:sweep";
    private const int DefaultMinAgeHours = 24;
    private const int LookupBatchSize = 500;

    private sealed record Options(bool Apply, int MinAgeHours);

    private sealed class Tally
    {
        public int Orphans, Younger, Deleted, Failed;
    }

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args, IServiceProvider services, TextWriter stdout, TextWriter stderr,
        TimeProvider clock, CancellationToken ct)
    {
        if (!TryParse(args, out var options, out var usageError))
        {
            await stderr.WriteLineAsync($"{Name}: {usageError}");
            return MigrationCli.Usage;
        }

        var tally = new Tally();
        try
        {
            using var scope = services.CreateScope();
            var storage = services.GetRequiredService<IFileStorage>();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var cutoff = clock.GetUtcNow().UtcDateTime.AddHours(-options.MinAgeHours);

            List<string> batch = [];
            await foreach (var blob in storage.ListAsync(ct))
            {
                if (!StorageKey.IsStorageKey(blob.Key)) continue;
                if (blob.LastModifiedUtc > cutoff) { tally.Younger++; continue; }
                batch.Add(blob.Key);
                if (batch.Count < LookupBatchSize) continue;
                await SweepBatchAsync(batch, options, storage, db, stdout, stderr, tally, ct);
                batch.Clear();
            }
            if (batch.Count > 0) await SweepBatchAsync(batch, options, storage, db, stdout, stderr, tally, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await stderr.WriteLineAsync($"{Name} failed: {Innermost(ex).Message.ReplaceLineEndings(" ")}");
            return MigrationCli.Failure;
        }

        await WriteSummaryAsync(options, tally, stdout);
        return tally.Failed > 0 ? MigrationCli.Failure : MigrationCli.Success;
    }

    private static async Task SweepBatchAsync(
        List<string> keys, Options options, IFileStorage storage, ISqlSugarClient db,
        TextWriter stdout, TextWriter stderr, Tally tally, CancellationToken ct)
    {
        var referenced = (await db.Queryable<File>()
            .ClearFilter<ISoftDeletable>()
            .In(f => f.StorageKey, keys.ToArray())
            .Select(f => f.StorageKey)
            .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        foreach (var key in keys.Where(k => !referenced.Contains(k)))
        {
            tally.Orphans++;
            await stdout.WriteLineAsync($"orphan {key}");
            if (!options.Apply) continue;
            try
            {
                await storage.DeleteAsync(key, ct);
                tally.Deleted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                tally.Failed++;
                await stderr.WriteLineAsync($"{Name}: deleting {key} failed: {ex.Message.ReplaceLineEndings(" ")}");
            }
        }
    }

    private static async Task WriteSummaryAsync(Options options, Tally tally, TextWriter stdout)
    {
        var found = $"{tally.Orphans} orphan blob(s) found, {tally.Younger} younger than {options.MinAgeHours} hour(s) skipped.";
        await stdout.WriteLineAsync(options.Apply
            ? $"{found} {tally.Deleted} deleted, {tally.Failed} failed."
            : $"{found} Dry run: nothing was deleted; pass --apply to delete.");
    }

    private static bool TryParse(IReadOnlyList<string> args, out Options options, out string error)
    {
        options = new Options(false, DefaultMinAgeHours);
        error = "";
        var (apply, minAge) = (false, DefaultMinAgeHours);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--apply") { apply = true; continue; }

            string? value = null;
            if (arg == "--min-age-hours") value = i + 1 < args.Count ? args[++i] : null;
            else if (arg.StartsWith("--min-age-hours=", StringComparison.Ordinal)) value = arg["--min-age-hours=".Length..];
            else { error = $"Unexpected argument '{arg}'. Usage: files:sweep [--apply] [--min-age-hours N]."; return false; }

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out minAge))
            {
                error = "--min-age-hours needs a whole number of hours, 0 or greater.";
                return false;
            }
        }
        options = new Options(apply, minAge);
        return true;
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex;
    }
}
