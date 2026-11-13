using AwesomeAssertions;
using SqlSugar;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

[SugarTable("test_nullability_probes")]
internal sealed class NullabilityProbe
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    public string PlainString { get; set; } = "";
    public string? PlainNullableString { get; set; }
    public Guid PlainGuid { get; set; }
    public Guid? PlainNullableGuid { get; set; }

    [SugarColumn(IsNullable = false)] public string? NullableStringForcedNotNull { get; set; }
    [SugarColumn(IsNullable = false)] public Guid? NullableGuidForcedNotNull { get; set; }
    [SugarColumn(IsNullable = true)] public string NonNullableStringForcedNull { get; set; } = "";
}

/// <summary>
/// Which nullability the factory gives a column: the entity's column info (what the ORM builds the table and
/// the purge clean-up reads) and the column the table really got.
/// </summary>
public sealed class ColumnNullabilityTests : IDisposable
{
    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;

    public ColumnNullabilityTests()
    {
        _db = SqlSugarClientFactory.Create(_database.Options(), new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<NullabilityProbe>();
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    // A nullable property gives a nullable column even when [SugarColumn(IsNullable = false)] says
    // otherwise: the factory sets it after the attribute is read. [SugarColumn(IsNullable = true)] makes a
    // non-nullable string nullable.
    [Theory]
    [InlineData(nameof(NullabilityProbe.PlainString), false)]
    [InlineData(nameof(NullabilityProbe.PlainNullableString), true)]
    [InlineData(nameof(NullabilityProbe.PlainGuid), false)]
    [InlineData(nameof(NullabilityProbe.PlainNullableGuid), true)]
    [InlineData(nameof(NullabilityProbe.NullableStringForcedNotNull), true)]
    [InlineData(nameof(NullabilityProbe.NullableGuidForcedNotNull), true)]
    [InlineData(nameof(NullabilityProbe.NonNullableStringForcedNull), true)]
    public void The_entity_column_info_and_the_created_column_agree(string property, bool nullable)
    {
        var info = _db.EntityMaintenance.GetEntityInfo<NullabilityProbe>().Columns
            .Single(c => c.PropertyName == property);
        var created = _db.DbMaintenance.GetColumnInfosByTableName("test_nullability_probes", false)
            .Single(c => string.Equals(c.DbColumnName, info.DbColumnName, StringComparison.OrdinalIgnoreCase));

        info.IsNullable.Should().Be(nullable);
        created.IsNullable.Should().Be(nullable);
    }
}
