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
/// Marks a property with the dialect-neutral <see cref="ColumnShape"/> it needs.
///
/// <para>
/// Precedence: this attribute wins over BOTH the convention-based widening in
/// <c>SqlSugarClientFactory</c>'s <c>EntityService</c> hook AND an explicit
/// <c>[SugarColumn(ColumnDataType = "...")]</c> on the same property — the hook resolves the shape
/// and returns before either is considered. A fork that wants its own vendor literal to win must
/// omit <c>[ColumnShape]</c> on that property. Pinned by
/// <c>ColumnTypeMapTests.ColumnShape_wins_over_an_explicitly_declared_ColumnDataType</c>.
/// </para>
/// <para>
/// Combining this attribute with a JSON-column <c>[CmsField]</c> interface (the multi-value selects,
/// <c>KeyValue</c>, <c>Files</c>, <c>Repeater</c>) is <b>refused</b>: the hook throws an
/// <see cref="InvalidOperationException"/> naming the property and the offending interface, either at
/// startup — if CodeFirst still has to create that entity's table — or on the first operation that
/// reflects the entity, on an existing database where the table is already present. It used to be
/// resolved silently, and the result was always broken — the early <c>return</c> here outranks the
/// hook's later JSON branch, which sets both <c>column.IsJson = true</c> and a widened
/// <c>DataType</c>, so the property kept the <c>DataType</c> and lost <c>IsJson</c>. Both branches
/// resolve a JSON-column interface to
/// <see cref="ColumnShape.LongText"/>, so with the shape declared as that same value — the only
/// sensible choice here — losing <c>IsJson</c> was the combination's only effect, and without it
/// SqlSugar never serializes the collection and CodeFirst leaves the length unset —
/// <c>varchar(1)</c> on PostgreSQL, where every write of a real value fails with 22001. Pinned by
/// <c>ColumnTypeMapTests.ColumnShape_combined_with_a_JSON_column_CmsField_is_refused</c>.
/// </para>
/// <para>
/// A <b>content-bearing</b> <c>[CmsField]</c> interface (<c>RichText</c>, <c>Textarea</c>,
/// <c>Markdown</c>, <c>Code</c>, <c>Json</c>) is a different case and stays legal: both paths resolve
/// to <see cref="ColumnShape.LongText"/>, the shape simply wins first, and nothing is lost. Pinned by
/// <c>ColumnTypeMapTests.ColumnShape_combined_with_a_content_bearing_CmsField_is_still_allowed</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnShapeAttribute(ColumnShape shape) : Attribute
{
    public ColumnShape Shape { get; } = shape;
}
