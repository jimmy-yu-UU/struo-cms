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
/// <c>AppliedAt</c> 依本 repo 對「baseline 之後新增的框架表」溫度欄位的慣例，標記為時區感知
/// （<see cref="ColumnShape.TimestampWithTimeZone"/>），而非寫死的 vendor 型別字面，因此在五個後端皆可
/// 建表成功——見 <see cref="ColumnTypeMap"/>。此舉讓「新建立」的追蹤表與既有 PostgreSQL 部署上
/// （由舊版 runner 建出的）<c>timestamptz</c> 欄位一致。既有的追蹤表本身<b>不會</b>因為這個標記而被改
/// 動：<c>MigrationRunner.EnsureTrackingTable</c> 只在表不存在時才建立，已存在的表永遠原樣沿用。
/// </para>
/// </summary>
[SugarTable("schema_migrations")]
public sealed class SchemaMigration
{
    // Length 191：MySQL 舊版 InnoDB 的索引前綴上限為 767 bytes，utf8mb4 下 191*4 = 764 仍可作為主鍵。
    // 已知且刻意的分歧：新建 PostgreSQL 部署因此得到 varchar(191)，既有部署(舊 runner 手寫 DDL)則是
    // text——與 AppliedAt 不同，這個欄位當時未被納入「新舊 PostgreSQL 不應分歧」的考量，事後判斷此分歧
    // 無害(單純檔名字串，兩種型別讀寫結果相同)，因此不比照 AppliedAt 加上 ColumnShape 去追平，維持原樣。
    [SugarColumn(ColumnName = "filename", IsPrimaryKey = true, Length = 191)]
    public string Filename { get; set; } = "";

    [SugarColumn(ColumnName = "appliedat")]
    [ColumnShape(ColumnShape.TimestampWithTimeZone)]
    public DateTime AppliedAt { get; set; }
}
