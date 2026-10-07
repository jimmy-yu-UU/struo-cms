namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>A column an entity declares. <c>Name</c> is lower-case; <c>Length</c> is set for <see cref="ColumnCategory.String"/> only.</summary>
public sealed record ExpectedColumn(
    string Name,
    ColumnCategory Category,
    bool IsNullable,
    int? Length,
    bool IsPrimaryKey,
    bool IsIdentity,
    bool IsJson);

/// <summary>An index an entity declares; columns are lower-case, in declaration order.</summary>
public sealed record ExpectedIndex(string Name, IReadOnlyList<string> Columns, bool IsUnique);

/// <summary>
/// The table an entity declares. <c>LogicalName</c> is the <c>[SugarTable]</c> name before the framework prefix;
/// <c>TranslationLogicalTable</c> is set for translation sidecars.
/// </summary>
public sealed record ExpectedTable(
    Type EntityType,
    string PhysicalName,
    string LogicalName,
    bool IsFrameworkTable,
    IReadOnlyList<ExpectedColumn> Columns,
    IReadOnlyList<ExpectedIndex> Indexes,
    string? TranslationLogicalTable);
