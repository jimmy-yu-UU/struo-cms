using System.Reflection;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Settings;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Covers <see cref="ColumnTypeMap"/>, the single place in <c>src/</c> a vendor column-type literal
/// may appear, and the <see cref="ColumnShapeAttribute"/> markers that replaced the hardcoded
/// PostgreSQL-only literals previously on MediaFolder/SiteSettings/Revision. See
/// <c>SqlSugarClientFactory</c>'s EntityService hook, which translates the marker into a
/// per-dialect literal via this map.
/// </summary>
public class ColumnTypeMapTests
{
    [Fact]
    public void LongText_maps_to_the_expected_literal_per_backend()
    {
        ColumnTypeMap.For(ColumnShape.LongText, DbType.PostgreSQL).Should().Be("text");
        // MySQL TEXT caps at 65,535 bytes, which is not unbounded — LONGTEXT is the correct choice
        // for a shape whose whole point is "this content can be arbitrarily large" (see
        // Revision.Snapshot / RichText bodies).
        ColumnTypeMap.For(ColumnShape.LongText, DbType.MySql).Should().Be("longtext");
        ColumnTypeMap.For(ColumnShape.LongText, DbType.Sqlite).Should().Be("text");
        ColumnTypeMap.For(ColumnShape.LongText, DbType.SqlServer).Should().Be("nvarchar(max)");
        ColumnTypeMap.For(ColumnShape.LongText, DbType.Oracle).Should().Be("clob");
    }

    [Fact]
    public void TimestampWithTimeZone_maps_to_the_expected_literal_per_backend()
    {
        ColumnTypeMap.For(ColumnShape.TimestampWithTimeZone, DbType.PostgreSQL).Should().Be("timestamptz");
        ColumnTypeMap.For(ColumnShape.TimestampWithTimeZone, DbType.MySql).Should().Be("datetime(6)");
        ColumnTypeMap.For(ColumnShape.TimestampWithTimeZone, DbType.SqlServer).Should().Be("datetimeoffset");
        ColumnTypeMap.For(ColumnShape.TimestampWithTimeZone, DbType.Oracle).Should().Be("timestamp with time zone");
        // SQLite does not validate declared type names (it derives a storage affinity from substring
        // matches), so this literal is accepted as-is — preserving exactly the column type the
        // existing test suite has always created.
        ColumnTypeMap.For(ColumnShape.TimestampWithTimeZone, DbType.Sqlite).Should().Be("timestamptz");
    }

    public static IEnumerable<object[]> TimestampWithTimeZoneProperties()
    {
        yield return [typeof(MediaFolder), nameof(MediaFolder.CreatedAt)];
        yield return [typeof(MediaFolder), nameof(MediaFolder.UpdatedAt)];
        yield return [typeof(SiteSettings), nameof(SiteSettings.UpdatedAt)];
        yield return [typeof(UserSession), nameof(UserSession.CreatedAt)];
        yield return [typeof(UserSession), nameof(UserSession.ExpiresAt)];
    }

    [Theory]
    [MemberData(nameof(TimestampWithTimeZoneProperties))]
    public void Entity_properties_carry_the_TimestampWithTimeZone_shape_and_no_literal_ColumnDataType(
        Type entityType, string propertyName)
    {
        var property = entityType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!;

        var shape = property.GetCustomAttribute<ColumnShapeAttribute>();
        shape.Should().NotBeNull();
        shape!.Shape.Should().Be(ColumnShape.TimestampWithTimeZone);

        // Assign to a local first: `sugarColumn?.ColumnDataType.Should()...` would short-circuit the
        // ENTIRE chain (including the assertion call) to null whenever sugarColumn is null — which is
        // exactly the expected case here — silently skipping the assertion instead of running it.
        var columnDataType = property.GetCustomAttribute<SugarColumn>()?.ColumnDataType;
        columnDataType.Should().BeNullOrEmpty();
    }

    public static IEnumerable<object[]> LongTextProperties()
    {
        yield return [typeof(SiteSettings), nameof(SiteSettings.BrandName)];
        yield return [typeof(Revision), nameof(Revision.Snapshot)];
    }

    [Theory]
    [MemberData(nameof(LongTextProperties))]
    public void Entity_properties_carry_the_LongText_shape_and_no_literal_ColumnDataType(
        Type entityType, string propertyName)
    {
        var property = entityType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!;

        var shape = property.GetCustomAttribute<ColumnShapeAttribute>();
        shape.Should().NotBeNull();
        shape!.Shape.Should().Be(ColumnShape.LongText);

        var columnDataType = property.GetCustomAttribute<SugarColumn>()?.ColumnDataType;
        columnDataType.Should().BeNullOrEmpty();
    }

    [Fact]
    public void InitTables_builds_the_framework_tables_with_the_shaped_columns_declared_correctly_on_Sqlite()
    {
        var db = new SqliteTestDatabase();
        using (db)
        {
            var client = SqlSugarClientFactory.Create(
                new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
                new TestCurrentUserAccessor(Guid.Empty));

            client.CodeFirst.InitTables<MediaFolder>();
            client.CodeFirst.InitTables<SiteSettings>();
            client.CodeFirst.InitTables<Revision>();

            // This pins the actual wiring end-to-end: marker attribute -> EntityService hook ->
            // ColumnTypeMap -> declared DDL type. SQLite records the declared type name verbatim
            // (it only derives a storage *affinity* from it, it does not normalize/discard it), so
            // this fails if the hook's [ColumnShape] branch is ever removed or bypassed — unlike a
            // bare column-name-presence check, which would stay green even if every shaped column
            // silently reverted to SqlSugar's default varchar(255)/timestamp mapping.
            var mediaFolderColumns = client.DbMaintenance.GetColumnInfosByTableName("media_folders", false);
            var mediaFolderNames = mediaFolderColumns.Select(c => c.DbColumnName).ToList();
            mediaFolderNames.Should().Contain("Id");
            mediaFolderNames.Should().Contain("Name");
            var createdAt = mediaFolderColumns.Single(c => c.DbColumnName.Equals("CreatedAt", StringComparison.OrdinalIgnoreCase));
            var mediaUpdatedAt = mediaFolderColumns.Single(c => c.DbColumnName.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase));
            createdAt.DataType.Should().ContainEquivalentOf("timestamptz");
            mediaUpdatedAt.DataType.Should().ContainEquivalentOf("timestamptz");

            var siteSettingsColumns = client.DbMaintenance.GetColumnInfosByTableName("site_settings", false);
            var siteSettingsNames = siteSettingsColumns.Select(c => c.DbColumnName).ToList();
            siteSettingsNames.Should().Contain("Id");
            var brandName = siteSettingsColumns.Single(c => c.DbColumnName.Equals("BrandName", StringComparison.OrdinalIgnoreCase));
            var siteUpdatedAt = siteSettingsColumns.Single(c => c.DbColumnName.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase));
            brandName.DataType.Should().ContainEquivalentOf("text");
            siteUpdatedAt.DataType.Should().ContainEquivalentOf("timestamptz");

            var revisionColumns = client.DbMaintenance.GetColumnInfosByTableName("revisions", false);
            var revisionNames = revisionColumns.Select(c => c.DbColumnName).ToList();
            revisionNames.Should().Contain("Id");
            var snapshot = revisionColumns.Single(c => c.DbColumnName.Equals("Snapshot", StringComparison.OrdinalIgnoreCase));
            snapshot.DataType.Should().ContainEquivalentOf("text");
        }
    }

    [SugarTable("column_shape_nullable_test_entity")]
    private sealed class ColumnShapeNullableTestEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [ColumnShape(ColumnShape.TimestampWithTimeZone)]
        public DateTime? DeletedAt { get; set; }

        [ColumnShape(ColumnShape.LongText)]
        public string? Notes { get; set; }
    }

    /// <summary>
    /// A property carrying an explicit [ColumnShape] on a nullable type must get BOTH the shaped
    /// type AND nullability inference — the hook must not early-return out of the shape branch
    /// before nullability has been decided. Pins the fix for the precedence bug where the shape
    /// branch's early `return` used to bypass the nullable-value-type/NRT-string checks entirely,
    /// silently mapping a nullable shaped property to a NOT NULL column.
    /// </summary>
    [Fact]
    public void ColumnShape_on_a_nullable_property_gets_the_shaped_type_and_stays_nullable()
    {
        var db = new SqliteTestDatabase();
        using (db)
        {
            var client = SqlSugarClientFactory.Create(
                new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
                new TestCurrentUserAccessor(Guid.Empty));

            client.CodeFirst.InitTables<ColumnShapeNullableTestEntity>();

            var columns = client.DbMaintenance.GetColumnInfosByTableName("column_shape_nullable_test_entity", false);
            var deletedAt = columns.Single(c => c.DbColumnName.Equals("DeletedAt", StringComparison.OrdinalIgnoreCase));
            var notes = columns.Single(c => c.DbColumnName.Equals("Notes", StringComparison.OrdinalIgnoreCase));
            deletedAt.DataType.Should().ContainEquivalentOf("timestamptz");
            notes.DataType.Should().ContainEquivalentOf("text");

            // Explicit assertion of the fact "the column is nullable" itself. The insert-null below is
            // a behavioral guard (the DB genuinely accepts null) — a different layer, both stay.
            deletedAt.IsNullable.Should().BeTrue();
            notes.IsNullable.Should().BeTrue();

            // A NOT NULL column would reject this insert of explicit nulls. Succeeding proves
            // nullability inference actually ran for these two shaped properties.
            var row = new ColumnShapeNullableTestEntity { DeletedAt = null, Notes = null };
            client.Insertable(row).ExecuteCommand();

            var read = client.Queryable<ColumnShapeNullableTestEntity>().First();
            read.DeletedAt.Should().BeNull();
            read.Notes.Should().BeNull();
        }
    }

    [SugarTable("column_shape_precedence_test_entity")]
    private sealed class ColumnShapePrecedenceTestEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        // 同一個屬性上兩種相衝的宣告：dialect-neutral 的 shape 與明寫的 vendor 字面值。
        [ColumnShape(ColumnShape.LongText)]
        [SugarColumn(ColumnDataType = "varchar(7)")]
        public string? Body { get; set; }
    }

    /// <summary>
    /// [ColumnShape] 蓋掉屬性上明寫的 [SugarColumn(ColumnDataType = ...)]。這是 fork 最可能誤解的
    /// 一點（「我明寫的應該贏」），而它會靜默地得到相反結果。優先序來自 EntityService hook 中
    /// [ColumnShape] 分支的早退（return）；本測試把它從實作細節升格為受檢規格，否則哪天有人
    /// 重排 hook 分支順序，fork 的 DDL 會無聲改變。
    /// </summary>
    [Fact]
    public void ColumnShape_wins_over_an_explicitly_declared_ColumnDataType()
    {
        var db = new SqliteTestDatabase();
        using (db)
        {
            var client = SqlSugarClientFactory.Create(
                new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
                new TestCurrentUserAccessor(Guid.Empty));

            client.CodeFirst.InitTables<ColumnShapePrecedenceTestEntity>();

            var body = client.DbMaintenance
                .GetColumnInfosByTableName("column_shape_precedence_test_entity", false)
                .Single(c => c.DbColumnName.Equals("Body", StringComparison.OrdinalIgnoreCase));

            // SQLite 逐字記錄宣告型別（只從中推導 storage affinity），所以這條斷言在 SQLite 上有效。
            body.DataType.Should().ContainEquivalentOf("text");
            body.DataType.Should().NotContainEquivalentOf("varchar");
        }
    }

    [SugarTable("column_shape_json_conflict_test_entity")]
    private sealed class ColumnShapeJsonConflictTestEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        // 同一個屬性上兩種互斥的宣告：dialect-neutral 的 shape，與一個會對映成 JSON 欄位的
        // [CmsField] 介面。這個組合永遠不是正確的宣告，所以 hook 必須拒絕而非靜默解析。
        [ColumnShape(ColumnShape.LongText)]
        [CmsField(Label = "Tags", Interface = FieldInterface.Tags)]
        public List<string> Tags { get; set; } = [];
    }

    /// <summary>
    /// 走到 shape 分支的早退之前，hook 必須拒絕「[ColumnShape] + JSON-column [CmsField]」。
    /// 兩條路算出的 DataType 完全相同（都是 LongText），所以這個組合唯一的效果就是丟掉
    /// <c>IsJson</c>；而少了 IsJson，SqlSugar 根本不會序列化那個 List&lt;&gt;，CodeFirst 也會讓長度
    /// 保持未設定——在 PostgreSQL 上就是 varchar(1)，任何真實值寫入都會 22001 失敗。
    /// 也就是說這不是「順序不巧」，而是一個沒有正確用途的宣告。
    /// </summary>
    [Fact]
    public void ColumnShape_combined_with_a_JSON_column_CmsField_is_refused()
    {
        var db = new SqliteTestDatabase();
        using (db)
        {
            var client = SqlSugarClientFactory.Create(
                new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
                new TestCurrentUserAccessor(Guid.Empty));

            var act = () => client.CodeFirst.InitTables<ColumnShapeJsonConflictTestEntity>();

            // SqlSugar 在自己的 pipeline 內反射 entity，可能把 hook 丟出的例外包一層，所以沿
            // inner-exception 鏈找型別，而不是對最外層型別硬斷言——用共用的
            // ExceptionChainSearch.FindInner，與 OptionsValidationTests 那邊用的是同一個
            // helper，理由相同。
            var thrown = act.Should().Throw<Exception>().Which;
            var guard = ExceptionChainSearch.FindInner<InvalidOperationException>(thrown);
            guard.Should().NotBeNull("hook 必須拒絕 [ColumnShape] 與 JSON-column [CmsField] 併用");
            guard!.Message.Should().Contain(nameof(ColumnShapeJsonConflictTestEntity.Tags),
                "訊息必須指名違規的 property，否則讀者無從下手");
            guard.Message.Should().Contain("Remove [ColumnShape]",
                "訊息必須直接給出修法");
        }
    }

    [SugarTable("column_shape_content_field_test_entity")]
    private sealed class ColumnShapeContentFieldTestEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        // 合法的組合：content-bearing 介面（非 JSON 欄位）。兩條路都算出 LongText，shape 先贏，
        // 結果一致，沒有任何東西被丟掉。
        [ColumnShape(ColumnShape.LongText)]
        [CmsField(Label = "Body", Interface = FieldInterface.RichText)]
        public string Body { get; set; } = "";
    }

    /// <summary>
    /// 負向控制。守衛必須窄到只擋 JSON-column 介面：shape 與 content-bearing 介面
    /// （RichText/Textarea/Markdown/Code/Json）併用是合法的，若守衛寫成「任何 [CmsField]」，
    /// 這條會紅。沒有這條測試，上面那條會允許一個過寬的實作通過。
    /// </summary>
    [Fact]
    public void ColumnShape_combined_with_a_content_bearing_CmsField_is_still_allowed()
    {
        var db = new SqliteTestDatabase();
        using (db)
        {
            var client = SqlSugarClientFactory.Create(
                new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
                new TestCurrentUserAccessor(Guid.Empty));

            client.CodeFirst.InitTables<ColumnShapeContentFieldTestEntity>();

            var body = client.DbMaintenance
                .GetColumnInfosByTableName("column_shape_content_field_test_entity", false)
                .Single(c => c.DbColumnName.Equals("Body", StringComparison.OrdinalIgnoreCase));

            body.DataType.Should().ContainEquivalentOf("text");
        }
    }

    [SugarTable("bare_isjson_probe")]
    private sealed class BareIsJsonProbe
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
        [SugarColumn(IsJson = true)] public List<string> Tags { get; set; } = [];
        [SugarColumn(IsJson = true, ColumnDataType = "clob")] public List<string> Pinned { get; set; } = [];
    }

    [Fact]
    public void Bare_IsJson_without_CmsField_or_shape_is_widened_to_LongText_on_Sqlite()
    {
        using var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        client.CodeFirst.InitTables<BareIsJsonProbe>();

        var columns = client.DbMaintenance.GetColumnInfosByTableName("bare_isjson_probe", false);
        columns.Single(c => c.DbColumnName.Equals("Tags", StringComparison.OrdinalIgnoreCase))
            .DataType.Should().ContainEquivalentOf(ColumnTypeMap.For(ColumnShape.LongText, DbType.Sqlite));

        client.Insertable(new BareIsJsonProbe { Tags = ["alpha", "beta"] }).ExecuteCommand();
        client.Queryable<BareIsJsonProbe>().First()!.Tags.Should().Equal("alpha", "beta");
    }

    [Fact]
    public void Explicit_ColumnDataType_on_an_IsJson_property_is_respected()
    {
        using var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        client.CodeFirst.InitTables<BareIsJsonProbe>();

        var columns = client.DbMaintenance.GetColumnInfosByTableName("bare_isjson_probe", false);
        columns.Single(c => c.DbColumnName.Equals("Pinned", StringComparison.OrdinalIgnoreCase))
            .DataType.Should().ContainEquivalentOf("clob", "a fork's own literal must win over the convention");
    }
}
