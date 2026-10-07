using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Infrastructure.Migrations.Schema;

namespace Struo.Infrastructure.Migrations.Commands;

/// <summary>Writes a migration source file; it reads entity metadata only and never touches the database.</summary>
internal static class MakeMigrationCommand
{
    internal const string UsageText = "Usage: make:migration <Name> [--entity <Type>] --output <dir> [--namespace <Ns>]";

    private static readonly string[] Options = ["--entity", "--output", "--namespace"];

    private sealed record Parsed(string Name, string? Entity, string Output, string? Namespace);

    private sealed record Failed(string Message);

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args, IServiceProvider services,
        TextWriter stdout, TextWriter stderr, TimeProvider clock)
    {
        var (parsed, error) = Parse(args);
        if (parsed is null) return await UsageError(stderr, error!.Message);

        if (!Directory.Exists(parsed.Output))
            return await UsageError(stderr, $"Output directory '{parsed.Output}' does not exist.");

        using var scope = services.CreateScope();
        ExpectedTable? table = null;
        if (parsed.Entity is not null)
        {
            var (type, lookupError) = ResolveEntity(scope.ServiceProvider, parsed.Entity);
            if (type is null) return await UsageError(stderr, lookupError!);
            try { table = EntitySchemaReader.Read(scope.ServiceProvider.GetRequiredService<ISqlSugarClient>(), type); }
            catch (OptionsValidationException ex)
            {
                await stderr.WriteLineAsync($"make:migration failed: {ex.Message}");
                return MigrationCli.Failure;
            }
        }

        var version = NextFreeVersion(parsed.Output, clock.GetUtcNow().UtcDateTime);
        var spec = new MigrationSpec(parsed.Name, version, parsed.Namespace, table);

        string source;
        try { source = MigrationGenerator.Generate(spec); }
        catch (NotSupportedException ex)
        {
            await stderr.WriteLineAsync(ex.Message);
            return MigrationCli.Failure;
        }

        var path = Path.Combine(parsed.Output, MigrationGenerator.FileName(spec));
        var failure = await WriteNewFileAsync(path, source);
        if (failure is not null)
        {
            await stderr.WriteLineAsync(failure);
            return MigrationCli.Failure;
        }

        await stdout.WriteLineAsync($"Created {path}");
        return MigrationCli.Success;
    }

    private const string VersionFormat = "yyyyMMddHHmm";

    /// <summary>The UTC minute stamp, moved forward minute by minute until no <c>{version}_*.cs</c> file exists in the directory.</summary>
    private static long NextFreeVersion(string directory, DateTime utcNow)
    {
        var stamp = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, utcNow.Minute, 0, DateTimeKind.Utc);
        while (true)
        {
            var version = stamp.ToString(VersionFormat, CultureInfo.InvariantCulture);
            if (!Directory.EnumerateFiles(directory, $"{version}_*.cs").Any())
                return long.Parse(version, CultureInfo.InvariantCulture);
            stamp = stamp.AddMinutes(1);
        }
    }

    /// <summary>Creates the file without overwriting; returns the one-line failure message, or null on success.</summary>
    private static async Task<string?> WriteNewFileAsync(string path, string source)
    {
        FileStream stream;
        try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write); }
        catch (IOException) when (File.Exists(path)) { return $"File already exists: {path}"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not write {path}: {ex.Message}";
        }

        try
        {
            await using (stream) await stream.WriteAsync(new UTF8Encoding(false).GetBytes(source));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(path);
            return $"Could not write {path}: {ex.Message}";
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* nothing more can be done */ }
    }

    private static async Task<int> UsageError(TextWriter stderr, string message)
    {
        await stderr.WriteLineAsync(message);
        await stderr.WriteLineAsync(UsageText);
        return MigrationCli.Usage;
    }

    private static (Parsed? Parsed, Failed? Error) Parse(IReadOnlyList<string> args)
    {
        string? name = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (name is not null) return (null, new Failed($"Unexpected argument '{arg}'."));
                name = arg;
                continue;
            }

            var (option, value, consumedNext, optionError) = ReadOption(args, i);
            if (optionError is not null) return (null, optionError);
            if (!values.TryAdd(option, value!)) return (null, new Failed($"Option '{option}' was given more than once."));
            if (consumedNext) i++;
        }

        return Validate(name, values);
    }

    private static (string Option, string? Value, bool ConsumedNext, Failed? Error) ReadOption(IReadOnlyList<string> args, int index)
    {
        var parts = args[index].Split('=', 2);
        var option = parts[0];
        if (!Options.Contains(option)) return (option, null, false, new Failed($"Unknown option '{option}'."));
        if (parts.Length == 2 && parts[1].Length > 0) return (option, parts[1], false, null);

        var hasNext = parts.Length == 1 && index + 1 < args.Count
            && !args[index + 1].StartsWith("--", StringComparison.Ordinal);
        return hasNext
            ? (option, args[index + 1], true, null)
            : (option, null, false, new Failed($"Option '{option}' needs a value."));
    }

    private static (Parsed? Parsed, Failed? Error) Validate(string? name, IReadOnlyDictionary<string, string> values)
    {
        if (name is null) return (null, new Failed("Missing migration name."));
        if (!MigrationGenerator.IsValidClassName(name)) return (null, new Failed($"'{name}' is not a valid migration class name."));
        if (!values.TryGetValue("--output", out var output)) return (null, new Failed("Missing required option '--output'."));
        values.TryGetValue("--namespace", out var ns);
        if (ns is not null && !MigrationGenerator.IsValidNamespace(ns)) return (null, new Failed($"'{ns}' is not a valid namespace."));
        values.TryGetValue("--entity", out var entity);
        return (new Parsed(name, entity, output, ns), null);
    }

    private static (Type? Type, string? Error) ResolveEntity(IServiceProvider services, string entity)
    {
        var matches = services.GetRequiredService<IEntityTypeCollector>().CollectForInitTables()
            .Where(t => t.Name == entity || t.FullName == entity)
            .Distinct()
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
        return matches.Count switch
        {
            0 => (null, $"No entity named '{entity}'."),
            1 => (matches[0], null),
            _ => (null, $"'{entity}' is ambiguous; use one of: {string.Join(", ", matches.Select(t => t.FullName))}.")
        };
    }
}
