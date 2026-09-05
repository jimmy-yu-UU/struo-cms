namespace Struo.Application.Query.Write;

/// <summary>
/// One element of an M2M write: which target row to link and, optionally, which junction payload
/// properties to set. Keys of <see cref="Payload"/> are CLR property names on the junction type.
/// A null <see cref="Payload"/> is a bare id: membership only, existing payload left untouched.
/// </summary>
public sealed record JunctionLink(object TargetId, IReadOnlyDictionary<string, object?>? Payload)
{
    public static JunctionLink Bare(object targetId) => new(targetId, null);
}
