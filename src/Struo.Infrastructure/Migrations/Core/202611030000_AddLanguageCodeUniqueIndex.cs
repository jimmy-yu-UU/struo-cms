using FluentMigrator;

namespace Struo.Infrastructure.Migrations.Core;

/// <summary>Adds a unique index on the language <c>code</c>; case-insensitive uniqueness stays in the application.</summary>
[Migration(Version, "AddLanguageCodeUniqueIndex")]
public sealed class AddLanguageCodeUniqueIndex : StruoMigration
{
    public const long Version = 202611030000;

    private const string Languages = "languages";

    private string IndexName => $"ux_{FrameworkTable(Languages)}_code";

    public override void Up() =>
        Create.Index(IndexName).OnTable(FrameworkTable(Languages))
            .OnColumn("code").Ascending()
            .WithOptions().Unique();

    public override void Down() =>
        Delete.Index(IndexName).OnTable(FrameworkTable(Languages));
}
