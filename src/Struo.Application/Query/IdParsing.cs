using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Parses a string id to a PK CLR type. Special-cases <see cref="Guid"/> (not
/// <see cref="IConvertible"/>, so <see cref="Convert.ChangeType(object, Type)"/> throws for it).
/// Application-layer twin of Infrastructure's IdCoercion, which the dependency rule keeps out of
/// this layer (Application must not reference Infrastructure).
/// </summary>
public static class IdParsing
{
    public static object ParseTo(string id, Type targetType)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try
        {
            return t == typeof(Guid) ? Guid.Parse(id) : Convert.ChangeType(id, t);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new QueryException($"Invalid id '{id}' for type '{t.Name}'.");
        }
    }
}
