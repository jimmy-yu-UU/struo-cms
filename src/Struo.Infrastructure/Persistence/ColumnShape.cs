namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Dialect-neutral description of the column shape a property needs, when SqlSugar's default
/// C#-type mapping is not what this framework wants. <see cref="ColumnTypeMap"/> resolves each
/// shape to the concrete type literal for the configured backend, inside
/// <c>SqlSugarClientFactory</c>'s <c>EntityService</c> hook.
///
/// <para>
/// Declaring intent this way instead of writing <c>[SugarColumn(ColumnDataType = "timestamptz")]</c>
/// is what keeps CodeFirst table creation working on every supported backend: a vendor type literal
/// is emitted verbatim into the DDL and fails wherever that type name does not exist.
/// </para>
/// </summary>
public enum ColumnShape
{
    /// <summary>
    /// Unbounded text. SqlSugar's default <c>varchar(255)</c> mapping for <see cref="string"/>
    /// overflows for prose, sanitized HTML, and serialized JSON (PostgreSQL 22001).
    /// </summary>
    LongText,

    /// <summary>
    /// Timestamp that carries a time-zone/UTC-instant semantic. Used by framework tables added
    /// after the convention was adopted; the older baseline <c>AuditableEntity</c> tables
    /// deliberately keep SqlSugar's default (zone-less) mapping.
    /// </summary>
    TimestampWithTimeZone,
}

/// <summary>
/// Marks a property with the dialect-neutral <see cref="ColumnShape"/> it needs. Takes precedence
/// over the convention-based widening in <c>SqlSugarClientFactory</c>'s <c>EntityService</c> hook.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnShapeAttribute(ColumnShape shape) : Attribute
{
    public ColumnShape Shape { get; } = shape;
}
