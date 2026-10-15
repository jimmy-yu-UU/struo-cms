using FluentMigrator;

// Outside the core namespace so only a host filtered to Struo.Infrastructure.Migrations runs it after the
// seed; it throws only for the trigger email, so any other host that scans the test assembly ignores it.
namespace Struo.Infrastructure.Migrations.FailAfterSeedProbe
{
    [Migration(202610090001, "fail after seed probe")]
    public sealed class FailAfterSeedProbe : StruoMigration
    {
        public const string TriggerEmail = "fail-after-seed@example.com";
        public const string Message = "probe failure after seed";

        public override void Up()
        {
            if (Seed?.AdminEmail == TriggerEmail) throw new InvalidOperationException(Message);
        }

        public override void Down() { }
    }
}
