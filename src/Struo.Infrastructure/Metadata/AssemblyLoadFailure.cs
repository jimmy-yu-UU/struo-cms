using System.Reflection;
using Struo.Domain.Metadata;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// Builds the <see cref="MetadataException"/> for a scanned assembly (content, framework or host) whose
/// types do not load. A type that cannot load is never skipped: its collection would vanish from the API
/// without a trace.
/// </summary>
internal static class AssemblyLoadFailure
{
    private const int MaxListed = 5;

    /// <summary>
    /// <see cref="Assembly.GetTypes"/> failed. The loader reports the failed types as null entries and
    /// names a type only when its error is a <see cref="TypeLoadException"/>, so the message gives the
    /// count, the names the loader supplied, and the distinct loader errors.
    /// </summary>
    internal static MetadataException ForAssembly(Assembly assembly, ReflectionTypeLoadException failure)
    {
        var failed = failure.Types.Count(t => t is null);
        var errors = failure.LoaderExceptions.OfType<Exception>().ToList();
        var names = errors.OfType<TypeLoadException>()
            .Select(e => e.TypeName)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
        var named = names.Count > 0 ? $" ({Capped(names, ", ")})" : string.Empty;
        return new MetadataException(
            $"Assembly '{assembly.GetName().Name}' has {failed} of {failure.Types.Length} types that cannot be " +
            $"loaded{named}. Loader errors: {Summarize(errors)}",
            failure);
    }

    /// <summary>A collection type that loads but whose members or attributes cannot be read.</summary>
    internal static MetadataException ForType(Type type, Exception failure) =>
        new($"Collection type '{type.FullName}' in assembly '{type.Assembly.GetName().Name}' could not be inspected: " +
            $"{Summarize([failure])}",
            failure);

    /// <summary>
    /// Runs <paramref name="inspect"/> on <paramref name="type"/>, turning a load error into a
    /// <see cref="MetadataException"/>.
    /// </summary>
    internal static T Inspect<T>(Type type, Func<T> inspect)
    {
        try
        {
            return inspect();
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            throw ForType(type, ex);
        }
    }

    // MissingMemberException is the base of MissingMethodException and MissingFieldException.
    private static bool IsLoadFailure(Exception ex) =>
        ex is FileNotFoundException or FileLoadException or BadImageFormatException
            or TypeLoadException or ReflectionTypeLoadException
            or MissingMemberException or CustomAttributeFormatException;

    // Distinct messages in order of first appearance, each with its count, capped like the type names.
    private static string Summarize(List<Exception> errors)
    {
        if (errors.Count == 0) return "none reported";

        var groups = errors.GroupBy(e => e.Message.Trim());
        return Capped(groups.Select(g => g.Count() > 1 ? $"{g.Key} (x{g.Count()})" : g.Key), "; ");
    }

    // At most MaxListed items joined by the separator, then how many more there are.
    private static string Capped(IEnumerable<string> items, string separator)
    {
        var all = items.ToList();
        var listed = string.Join(separator, all.Take(MaxListed));
        return all.Count > MaxListed ? $"{listed}{separator}and {all.Count - MaxListed} more" : listed;
    }
}
