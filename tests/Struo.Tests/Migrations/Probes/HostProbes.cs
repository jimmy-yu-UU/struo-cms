using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Migrations.Probes.Upgrade.V1
{
    [Migration(1, "create alpha")]
    public sealed class CreateAlpha : StruoMigration
    {
        public override void Up() => Create.Table("probe_alpha").WithColumn("id").AsInt32().PrimaryKey();
        public override void Down() => Delete.Table("probe_alpha");
    }

    [Migration(3, "create gamma")]
    public sealed class CreateGamma : StruoMigration
    {
        public override void Up() => Create.Table("probe_gamma").WithColumn("id").AsInt32().PrimaryKey();
        public override void Down() => Delete.Table("probe_gamma");
    }
}

namespace Struo.Tests.Migrations.Probes.Upgrade.V2
{
    [Migration(2, "create beta (older version, shipped later)")]
    public sealed class CreateBeta : StruoMigration
    {
        public override void Up() => Create.Table("probe_beta").WithColumn("id").AsInt32().PrimaryKey();
        public override void Down() => Delete.Table("probe_beta");
    }
}

namespace Struo.Tests.Migrations.Probes.Failing
{
    [Migration(10, "create a")]
    public sealed class CreateA : StruoMigration
    {
        public override void Up() => Create.Table("probe_fail_a").WithColumn("id").AsInt32();
        public override void Down() { }
    }

    [Migration(11, "create a again (fails)")]
    public sealed class CreateAAgain : StruoMigration
    {
        public override void Up() => Create.Table("probe_fail_a").WithColumn("id").AsInt32();
        public override void Down() { }
    }

    [Migration(12, "create c (must not run)")]
    public sealed class CreateC : StruoMigration
    {
        public override void Up() => Create.Table("probe_fail_c").WithColumn("id").AsInt32();
        public override void Down() { }
    }
}

namespace Struo.Tests.Migrations.Probes.Duplicate
{
    [Migration(20, "dup one")]
    public sealed class DupOne : StruoMigration
    {
        public override void Up() => Create.Table("probe_dup_one").WithColumn("id").AsInt32();
        public override void Down() { }
    }

    [Migration(20, "dup two")]
    public sealed class DupTwo : StruoMigration
    {
        public override void Up() => Create.Table("probe_dup_two").WithColumn("id").AsInt32();
        public override void Down() { }
    }
}

namespace Struo.Tests.Migrations.Probes.Context
{
    [Migration(30, "uses framework prefix")]
    public sealed class UsesPrefix : StruoMigration
    {
        public override void Up() => Create.Table(FrameworkTable("ctxprobe")).WithColumn("id").AsInt32();
        public override void Down() { }
    }
}
