using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
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

    private const int MaxOrphanPercent = 50;
    private const int MaxMinAgeHours = 87_600; // ten years

    private enum ScanMode { Count, Report, Delete }

    private sealed record Options(bool Apply, bool Force, int MinAgeHours);

    private sealed class Tally
    {
        public int Eligible, Orphans, Younger, Deleted, Failed;

        public int Percent => Eligible == 0 ? 0 : (int)Math.Round(Orphans * 100.0 / Eligible);

        // Compared as integers so exactly half is below the threshold.
        public bool ExceedsThreshold => (long)Orphans * 100 > (long)Eligible * MaxOrphanPercent;
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

        try
        {
            await stdout.WriteLineAsync(SweepTargets.Describe(
                services.GetRequiredService<FileStorageOptions>(),
                services.GetRequiredService<IOptions<DatabaseOptions>>().Value));

            var scan = new Scan(
                services.GetRequiredService<IFileStorage>(),
                services.GetRequiredService<ISqlSugarClient>(),
                clock.GetUtcNow().UtcDateTime.AddHours(-options.MinAgeHours));

            if (options.Apply && !options.Force)
            {
                var census = await scan.RunAsync(ScanMode.Count, stdout, stderr, ct);
                if (census.ExceedsThreshold)
                {
                    await stderr.WriteLineAsync(
                        $"{Name}: refusing to delete: {census.Orphans} of {census.Eligible} eligible blobs " +
                        $"({census.Percent}%) are orphans, more than {MaxOrphanPercent}%. This often means the " +
                        "configuration points at the wrong storage or database. Check the targets above, " +
                        "then rerun with --force to delete anyway.");
                    return MigrationCli.Refused;
                }
            }

            // The count pass above and this pass list the store separately. The cutoff is fixed, so a blob
            // uploaded in between is younger than it and untouched; only a row purged in between can add an
            // orphan, which is acceptable without re-checking the ratio here.
            var tally = await scan.RunAsync(options.Apply ? ScanMode.Delete : ScanMode.Report, stdout, stderr, ct);
            await WriteSummaryAsync(options, tally, stdout);
            return tally.Failed > 0 ? MigrationCli.Failure : MigrationCli.Success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await stderr.WriteLineAsync($"{Name} failed: {Innermost(ex).Message.ReplaceLineEndings(" ")}");
            return MigrationCli.Failure;
        }
    }

    private sealed class Scan(IFileStorage storage, ISqlSugarClient db, DateTime cutoff)
    {
        public async Task<Tally> RunAsync(ScanMode mode, TextWriter stdout, TextWriter stderr, CancellationToken ct)
        {
            var tally = new Tally();
            List<string> batch = [];
            await foreach (var blob in storage.ListAsync(ct))
            {
                if (!StorageKey.IsStorageKey(blob.Key)) continue;
                if (blob.LastModifiedUtc > cutoff) { tally.Younger++; continue; }
                tally.Eligible++;
                batch.Add(blob.Key);
                if (batch.Count < LookupBatchSize) continue;
                await SweepBatchAsync(batch, mode, stdout, stderr, tally, ct);
                batch.Clear();
            }
            if (batch.Count > 0) await SweepBatchAsync(batch, mode, stdout, stderr, tally, ct);
            return tally;
        }

        private async Task SweepBatchAsync(
            List<string> keys, ScanMode mode, TextWriter stdout, TextWriter stderr, Tally tally, CancellationToken ct)
        {
            var referenced = (await db.Queryable<File>()
                .ClearFilter<ISoftDeletable>()
                .In(f => f.StorageKey, keys.ToArray())
                .Select(f => f.StorageKey)
                .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

            foreach (var key in keys.Where(k => !referenced.Contains(k)))
            {
                tally.Orphans++;
                if (mode == ScanMode.Count) continue;
                await stdout.WriteLineAsync($"orphan {key}");
                if (mode == ScanMode.Report) continue;
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
    }

    private static async Task WriteSummaryAsync(Options options, Tally tally, TextWriter stdout)
    {
        var found = $"{tally.Orphans} of {tally.Eligible} eligible blobs ({tally.Percent}%) are orphans; " +
                    $"{tally.Younger} younger than {options.MinAgeHours} hour(s) skipped.";
        if (options.Apply)
        {
            await stdout.WriteLineAsync($"{found} {tally.Deleted} deleted, {tally.Failed} failed.");
            return;
        }
        await stdout.WriteLineAsync($"{found} Dry run: nothing was deleted; pass --apply to delete.");
        if (tally.ExceedsThreshold)
            await stdout.WriteLineAsync(
                $"More than {MaxOrphanPercent}% are orphans, so --apply would need --force. " +
                "Check the targets above first.");
    }

    private static bool TryParse(IReadOnlyList<string> args, out Options options, out string error)
    {
        options = new Options(false, false, DefaultMinAgeHours);
        error = "";
        var (apply, force, minAge) = (false, false, DefaultMinAgeHours);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--apply") { apply = true; continue; }
            if (arg == "--force") { force = true; continue; }

            string? value = null;
            if (arg == "--min-age-hours") value = i + 1 < args.Count ? args[++i] : null;
            else if (arg.StartsWith("--min-age-hours=", StringComparison.Ordinal)) value = arg["--min-age-hours=".Length..];
            else { error = $"Unexpected argument '{arg}'. Usage: files:sweep [--apply [--force]] [--min-age-hours N]."; return false; }

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out minAge) || minAge > MaxMinAgeHours)
            {
                error = $"--min-age-hours needs a whole number of hours from 0 to {MaxMinAgeHours}.";
                return false;
            }
        }
        options = new Options(apply, force, minAge);
        return true;
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex;
    }
}
