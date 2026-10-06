using FluentMigrator;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Persistence;

namespace Struo.Tests.Migrations.Probes.Types
{
    [Migration(50, "shaped columns")]
    public sealed class ShapedColumns : StruoMigration
    {
        public override void Up() =>
            Create.Table(FrameworkTable("typeprobe"))
                .WithColumn("id").AsInt32().PrimaryKey()
                .WithColumn("at").AsShape(ColumnShape.TimestampWithTimeZone, Db).Nullable()
                .WithColumn("body").AsLongText(Db).Nullable()
                .WithColumn("data").AsJson(Db).Nullable();
        public override void Down() { }
    }
}

namespace Struo.Tests.Migrations.Probes.Sidecar
{
    [Migration(60, "sidecar with translation unique")]
    public sealed class Sidecar : StruoMigration
    {
        public override void Up()
        {
            Create.Table("probe_translations")
                .WithColumn("id").AsInt32().PrimaryKey()
                .WithColumn("probeid").AsInt32()
                .WithColumn("locale").AsString(35);
            CreateTranslationUniqueIndex("probe_translations", "probe_translations", "probeid", "locale");
        }
        public override void Down() { }
    }
}
