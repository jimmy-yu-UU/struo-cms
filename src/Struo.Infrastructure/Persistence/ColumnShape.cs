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
    /// Timestamp that carries a time-zone/UTC-instant semantic. Declared explicitly on the
    /// properties that need it.
    /// </summary>
    TimestampWithTimeZone,

    /// <summary>
    /// Zone-less timestamp with microseconds on MySQL and MariaDB. Every <see cref="DateTime"/>
    /// property without an explicit shape or <c>ColumnDataType</c> gets it there, so two writes within
    /// one second stay ordered; their plain <c>datetime</c> keeps whole seconds. Other backends keep
    /// SqlSugar's default <see cref="DateTime"/> mapping, also when the shape is declared explicitly.
    /// </summary>
    Timestamp,
}

/// <summary>
/// Marks a property with the dialect-neutral <see cref="ColumnShape"/> it needs.
///
/// <para>
/// On a <c>[CmsField]</c> property the metadata scan rejects the attribute together with an explicit
/// <c>[SugarColumn(ColumnDataType = ...)]</c> and together with a JSON-column interface (the
/// multi-value selects, <c>KeyValue</c>, <c>Files</c>, <c>Repeater</c>), at startup in every
/// environment. For an entity the scan does not see, the column convention refuses the JSON-interface
/// pairing with an <see cref="InvalidOperationException"/> on first use.
/// </para>
/// <para>
/// A content-bearing <c>[CmsField]</c> interface (<c>RichText</c>, <c>Textarea</c>, <c>Markdown</c>,
/// <c>Code</c>, <c>Json</c>) may carry the attribute: both resolve to <see cref="ColumnShape.LongText"/>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnShapeAttribute(ColumnShape shape) : Attribute
{
    public ColumnShape Shape { get; } = shape;
}
