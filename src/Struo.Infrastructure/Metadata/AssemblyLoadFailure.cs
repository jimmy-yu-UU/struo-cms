using System.Reflection;
using Struo.Domain.Metadata;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// Builds the <see cref="MetadataException"/> for a content assembly whose types do not load. A type
/// that cannot load is never skipped: its collection would vanish from the API without a trace.
/// </summary>
internal static class AssemblyLoadFailure
{
    private const int MaxListedErrors = 5;

    /// <summary>
    /// <see cref="Assembly.GetTypes"/> failed. The loader reports the failed types as null entries and
    /// names a type only when its error is a <see cref="TypeLoadException"/>, so the message gives the
    /// count, the names the loader supplied, and the distinct loader errors.
    /// </summary>
    internal static MetadataException ForAssembly(Assembly assembly, ReflectionTypeLoadException failure)
    {
        var failed = failure.Types.Count(t => t is null);
        var errors = failure.LoaderExceptions.OfType<Exception>().ToList();
        var names = errors.OfType<TypeLoadException>().Select(e => e.TypeName).Where(n => n.Length > 0).Distinct().ToList();
        var named = names.Count > 0 ? $" ({string.Join(", ", names)})" : string.Empty;
        return new MetadataException(
            $"Content assembly '{assembly.GetName().Name}' has {failed} of {failure.Types.Length} types that cannot be " +
            $"loaded{named}. Loader errors: {Summarize(errors)}",
            failure);
    }

    /// <summary>A collection type that loads but whose members or attributes cannot be read.</summary>
    internal static MetadataException ForType(Type type, Exception failure) =>
        new($"Collection type '{type.FullName}' in assembly '{type.Assembly.GetName().Name}' could not be inspected: " +
            $"{Summarize([failure])}",
            failure);

    /// <summary>Runs <paramref name="inspect"/> on <paramref name="type"/>, turning a load error into a <see cref="MetadataException"/>.</summary>
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

    private static bool IsLoadFailure(Exception ex) =>
        ex is FileNotFoundException or FileLoadException or BadImageFormatException
            or TypeLoadException or ReflectionTypeLoadException;

    // Distinct messages in order of first appearance, each with its count; at most MaxListedErrors of
    // them, then how many more there are.
    private static string Summarize(IReadOnlyCollection<Exception> errors)
    {
        if (errors.Count == 0) return "none reported";

        var groups = errors.GroupBy(e => e.Message.Trim()).ToList();
        var listed = groups.Take(MaxListedErrors)
            .Select(g => g.Count() > 1 ? $"{g.Key} (x{g.Count()})" : g.Key);
        var more = groups.Count - MaxListedErrors;
        return string.Join("; ", listed) + (more > 0 ? $"; and {more} more" : string.Empty);
    }
}
