using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Migrations.Probes.Lock
{
    [Migration(40, "slow create")]
    public sealed class SlowCreate : StruoMigration
    {
        public override void Up()
        {
            Thread.Sleep(1500);
            Create.Table(FrameworkTable("lockprobe")).WithColumn("id").AsInt32();
        }
        public override void Down() { }
    }
}
