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
        ColumnTypeMap.For(ColumnShape.LongText, DbType.MySql).Should().Be("text");
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

        var sugarColumn = property.GetCustomAttribute<SugarColumn>();
        string.IsNullOrEmpty(sugarColumn?.ColumnDataType).Should().BeTrue();
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

        var sugarColumn = property.GetCustomAttribute<SugarColumn>();
        string.IsNullOrEmpty(sugarColumn?.ColumnDataType).Should().BeTrue();
    }

    [Fact]
    public void InitTables_builds_the_framework_tables_with_all_columns_on_Sqlite()
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

            var mediaFolderColumns = client.DbMaintenance.GetColumnInfosByTableName("media_folders", false);
            var mediaFolderNames = mediaFolderColumns.Select(c => c.DbColumnName).ToList();
            mediaFolderNames.Should().Contain("Id");
            mediaFolderNames.Should().Contain("CreatedAt");
            mediaFolderNames.Should().Contain("UpdatedAt");
            mediaFolderNames.Should().Contain("Name");

            var siteSettingsColumns = client.DbMaintenance.GetColumnInfosByTableName("site_settings", false);
            var siteSettingsNames = siteSettingsColumns.Select(c => c.DbColumnName).ToList();
            siteSettingsNames.Should().Contain("Id");
            siteSettingsNames.Should().Contain("BrandName");
            siteSettingsNames.Should().Contain("UpdatedAt");

            var revisionColumns = client.DbMaintenance.GetColumnInfosByTableName("revisions", false);
            var revisionNames = revisionColumns.Select(c => c.DbColumnName).ToList();
            revisionNames.Should().Contain("Id");
            revisionNames.Should().Contain("Snapshot");
        }
    }
}
