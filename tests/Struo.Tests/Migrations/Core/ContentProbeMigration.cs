using FluentMigrator;
using Struo.Infrastructure.Migrations;

// Lives under the core namespace so a host scanning [core assembly, test assembly] with the core filter
// sees it as a content-assembly migration next to the core ones.
namespace Struo.Infrastructure.Migrations.Core.ContentProbe
{
    [Migration(202610090000, "content probe")]
    public sealed class CreateContentProbe : StruoMigration
    {
        public override void Up() => Create.Table("content_probe").WithColumn("id").AsInt32().PrimaryKey();
        public override void Down() => Delete.Table("content_probe");
    }
}
