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
    [SugarColumn(ColumnName = "filename", IsPrimaryKey = true, Length = 191)]
    public string Filename { get; set; } = "";

    [SugarColumn(ColumnName = "appliedat")]
    [ColumnShape(ColumnShape.TimestampWithTimeZone)]
    public DateTime AppliedAt { get; set; }
}
