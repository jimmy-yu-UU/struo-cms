using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Which tables carry <c>Database:TablePrefix</c>, and how the prefixed name is formed. The set is the
/// 11 entities in <see cref="FrameworkEntityTypes.All"/> plus <see cref="SchemaMigration"/>, which
/// <c>MigrationRunner</c> creates itself and therefore keeps out of that list. Fork and sample
/// entities are never prefixed — that is what lets a fork tell framework tables from its own.
/// </summary>
internal static class TableNaming
{
    private static readonly HashSet<Type> FrameworkTables =
        [.. FrameworkEntityTypes.All, typeof(SchemaMigration)];

    public static bool IsFrameworkTable(Type type) => FrameworkTables.Contains(type);

    /// <summary>Pure: SqlSugar re-runs the naming hook from the attribute value on every CodeFirst pass,
    /// so this must never read the current name back and prepend again.</summary>
    public static string Apply(string prefix, string attributeTableName) => prefix + attributeTableName;
}
