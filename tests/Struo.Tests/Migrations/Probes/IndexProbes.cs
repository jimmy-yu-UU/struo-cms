using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Migrations.Probes.IndexCatalog
{
    /// <summary>Columns are declared id, a, b; the unique index lists them b, a.</summary>
    [Migration(50, "index catalog probe")]
    public sealed class IndexCatalogProbe : StruoMigration
    {
        public override void Up()
        {
            var table = FrameworkTable("idxprobe");
            Create.Table(table)
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("a").AsString(50).NotNullable()
                .WithColumn("b").AsString(50).NotNullable();
            Create.Index(table + "_ux_ba").OnTable(table)
                .OnColumn("b").Ascending()
                .OnColumn("a").Ascending()
                .WithOptions().Unique();
            Create.Index(table + "_ix_a").OnTable(table).OnColumn("a").Ascending();
        }

        public override void Down() { }
    }
}
