using System.Reflection;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Core index parity: every [SugarIndex] declared on a core FrameworkEntityTypes entity must be emitted
/// by CodeFirst (InitTables). CodeFirst is the only table/index creator, in every environment and on
/// every backend — there is no separate hand-maintained baseline SQL file to check parity against —
/// asserting against InitTables directly is the whole check. Declared indexes are discovered
/// by reflecting [SugarIndex] off FrameworkEntityTypes.All rather than hardcoded, so a new core entity's
/// index is covered automatically. Sample (Blog) index parity is the sample's own concern and is not
/// asserted here.
/// (FileTranslation's (fileid, locale) lookup is served by the composite UNIQUE index, so
/// FileTranslation contributes no separate mapped plain btree here.)
/// </summary>
public sealed class IndexParityTests
{
    private static (SqliteTestDatabase, ISqlSugarClient) NewClient()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        return (db, client);
    }

    // Derived, not hardcoded: every core entity carrying at least one [SugarIndex] attribute, together
    // with the index names it declares. A new core entity that adds [SugarIndex] is picked up here with
    // no test edit required.
    private static readonly IReadOnlyList<Type> CoreIndexedEntities = FrameworkEntityTypes.All
        .Where(t => t.GetCustomAttributes<SugarIndexAttribute>().Any())
        .ToArray();

    public static TheoryData<Type, string> CoreDeclaredIndexes()
    {
        var data = new TheoryData<Type, string>();
        foreach (var type in CoreIndexedEntities)
            foreach (var attr in type.GetCustomAttributes<SugarIndexAttribute>())
                data.Add(type, attr.IndexName);
        return data;
    }

    private static List<(string Name, bool Unique)> Indexes(ISqlSugarClient client) =>
        client.Ado.SqlQuery<dynamic>("SELECT name, sql FROM sqlite_master WHERE type='index' AND sql IS NOT NULL")
            .Select(r => ((string)r.name, ((string)r.sql).Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)))
            .ToList();

    [Theory]
    [MemberData(nameof(CoreDeclaredIndexes))]
    public void InitTables_emits_each_declared_core_index_under_the_resolved_table_name(Type type, string declaredName)
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(type);
            var expected = declaredName.Replace("{table}", client.EntityMaintenance.GetTableName(type));
            var declaredUnique = type.GetCustomAttributes<SugarIndexAttribute>()
                .Single(a => a.IndexName == declaredName).IsUnique;

            var emitted = Indexes(client);
            emitted.Select(i => i.Name).Should().Contain(expected);
            emitted.Single(i => i.Name == expected).Unique.Should().Be(declaredUnique);
        }
    }

    [Fact]
    public void InitTables_is_idempotent_for_core_indexes()
    {
        var (db, client) = NewClient();
        using (db)
        {
            var types = CoreIndexedEntities.ToArray();

            client.CodeFirst.InitTables(types);
            var firstPass = Indexes(client);

            var act = () => client.CodeFirst.InitTables(types);
            act.Should().NotThrow();
            var secondPass = Indexes(client);

            // A regression here (e.g. {table} left unresolved on the second pass) would surface as either
            // a duplicate-name error from SqlSugar's "does the index exist" check or a second, differently
            // named index — either way the two passes' index sets would stop matching.
            secondPass.Should().BeEquivalentTo(firstPass,
                "a second InitTables pass must neither duplicate nor recreate any core index");

            foreach (var (type, attr) in CoreIndexedEntities
                         .SelectMany(t => t.GetCustomAttributes<SugarIndexAttribute>().Select(a => (t, a))))
            {
                var expected = attr.IndexName.Replace("{table}", client.EntityMaintenance.GetTableName(type));
                secondPass.Select(i => i.Name).Should().Contain(expected);
            }
        }
    }

    [Fact]
    public void Every_declared_core_index_name_carries_the_table_placeholder()
    {
        foreach (var type in CoreIndexedEntities)
            foreach (var attr in type.GetCustomAttributes<SugarIndexAttribute>())
                attr.IndexName.Should().Contain("{table}", $"{type.Name}: '{attr.IndexName}' must follow the prefixed table name");
    }

    [Fact]
    public void No_core_entity_declares_a_unique_group_by_column_attribute()
    {
        foreach (var type in FrameworkEntityTypes.All.Append(typeof(SchemaMigration)))
            foreach (var prop in type.GetProperties())
            {
                var col = prop.GetCustomAttribute<SugarColumn>();
                (col?.UniqueGroupNameList ?? []).Should().BeEmpty(
                    $"{type.Name}.{prop.Name}: unique constraints are declared with [SugarIndex(IsUnique)] so their names follow the table");
            }
    }
}
