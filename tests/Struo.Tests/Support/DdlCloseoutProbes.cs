using SqlSugar;

namespace Struo.Tests.Support;

/// <summary>
/// Probe for the "bare IsJson column" widening hook in SqlSugarClientFactory: a property whose
/// only annotation is <c>[SugarColumn(IsJson = true)]</c> — no <c>[CmsField]</c>, no
/// <c>ColumnDataType</c> — must land as PostgreSQL <c>text</c>, not the default <c>varchar(1)</c>.
/// See PostgresIntegrationTests.Bare_IsJson_column_is_text_on_postgres_and_round_trips.
/// </summary>
[SugarTable("ddl_closeout_isjson_probe")]
public sealed class DdlCloseoutIsJsonProbe
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    [SugarColumn(IsJson = true)] public List<string> Tags { get; set; } = [];
}
