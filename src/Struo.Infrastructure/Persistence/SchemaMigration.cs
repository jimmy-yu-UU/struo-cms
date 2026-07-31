using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// <see cref="MigrationRunner"/> 的追蹤表。以 CodeFirst 建立，取代原先寫死 <c>text</c> /
/// <c>timestamptz</c> 的 PostgreSQL 專用 DDL，使 runner 可在任何後端運作。
///
/// <para>
/// 欄位名以 <c>ColumnName</c> 明確指定為小寫：既有 PostgreSQL 部署的追蹤表是由舊版 runner 以未加引號的
/// DDL 建立的（PostgreSQL 會摺疊為小寫），明確指定可保證新舊兩種表都能被同一個 entity 正確讀寫。
/// </para>
///
/// <para>
/// <c>AppliedAt</c> 刻意<b>不</b>指定 <c>ColumnDataType</c>。本 repo 對新增溫度欄位的慣例是
/// <c>timestamptz</c>，但該字面型別在 MySQL / SqlServer / Oracle 並不存在；此處以可攜性優先，交由
/// SqlSugar 依各 dialect 映射 <see cref="DateTime"/>。
/// </para>
/// </summary>
[SugarTable("schema_migrations")]
public sealed class SchemaMigration
{
    // Length 191：MySQL 舊版 InnoDB 的索引前綴上限為 767 bytes，utf8mb4 下 191*4 = 764 仍可作為主鍵。
    [SugarColumn(ColumnName = "filename", IsPrimaryKey = true, Length = 191)]
    public string Filename { get; set; } = "";

    [SugarColumn(ColumnName = "appliedat")]
    public DateTime AppliedAt { get; set; }
}
