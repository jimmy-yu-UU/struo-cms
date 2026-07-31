using System.Reflection;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
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

            // A NOT NULL column would reject this insert of explicit nulls. Succeeding proves
            // nullability inference actually ran for these two shaped properties.
            var row = new ColumnShapeNullableTestEntity { DeletedAt = null, Notes = null };
            client.Insertable(row).ExecuteCommand();

            var read = client.Queryable<ColumnShapeNullableTestEntity>().First();
            read.DeletedAt.Should().BeNull();
            read.Notes.Should().BeNull();
        }
    }
}
