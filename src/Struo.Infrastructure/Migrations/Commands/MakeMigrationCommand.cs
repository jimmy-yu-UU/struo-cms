using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
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
            table = EntitySchemaReader.Read(scope.ServiceProvider.GetRequiredService<ISqlSugarClient>(), type);
        }

        var version = long.Parse(
            clock.GetUtcNow().UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
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

            var parts = arg.Split('=', 2);
            if (!Options.Contains(parts[0])) return (null, new Failed($"Unknown option '{parts[0]}'."));
            string value;
            if (parts.Length == 2 && parts[1].Length > 0) value = parts[1];
            else if (parts.Length == 1 && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) value = args[++i];
            else return (null, new Failed($"Option '{parts[0]}' needs a value."));
            if (!values.TryAdd(parts[0], value)) return (null, new Failed($"Option '{parts[0]}' was given more than once."));
        }

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
