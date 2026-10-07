using FluentMigrator;

namespace Struo.Infrastructure.Migrations.Core;

/// <summary>Seeds the languages, the <c>admin</c> and <c>public</c> roles, the bootstrap admin and the public read grants.</summary>
[Migration(Version, "SeedCoreData")]
public sealed class SeedCoreData : StruoMigration
{
    public const long Version = 202610080001;

    public override void Up()
    {
        var seed = Seed ?? throw new InvalidOperationException(
            "SeedCoreData needs seed data: pass CoreSeedData through MigrationHostOptions.Seed.");
        var now = DateTime.UtcNow;
        var adminRoleId = Guid.CreateVersion7();
        var publicRoleId = Guid.CreateVersion7();

        foreach (var language in seed.Languages) InsertLanguage(language, now);
        InsertRole(adminRoleId, "admin", isSuperAdmin: true, "Full access", now);
        InsertRole(publicRoleId, "public", isSuperAdmin: false, "Anonymous callers", now);
        if (seed.HasAdmin) InsertAdmin(seed, adminRoleId, now);
        foreach (var collection in seed.PublicReadCollections) InsertPublicRead(publicRoleId, collection, now);
    }

    public override void Down() =>
        throw new NotSupportedException("SeedCoreData is forward-only.");

    private void InsertLanguage(SeedLanguage language, DateTime now) =>
        Insert.IntoTable(FrameworkTable("languages")).Row(new
        {
            code = language.Code, name = language.Name, isdefault = language.IsDefault, enabled = true,
            sort = language.Sort, createdat = now, updatedat = now,
        });

    private void InsertRole(Guid id, string name, bool isSuperAdmin, string description, DateTime now) =>
        Insert.IntoTable(FrameworkTable("roles")).Row(new
        {
            id, name, issuperadmin = isSuperAdmin, description, createdat = now, updatedat = now, version = 0L,
        });

    private void InsertAdmin(CoreSeedData seed, Guid adminRoleId, DateTime now)
    {
        var userId = Guid.CreateVersion7();
        Insert.IntoTable(FrameworkTable("users")).Row(new
        {
            id = userId, email = seed.AdminEmail, password = seed.AdminPasswordHash, name = "Administrator",
            isactive = true, createdat = now, updatedat = now, version = 0L,
        });
        Insert.IntoTable(FrameworkTable("user_roles")).Row(new
        {
            id = Guid.CreateVersion7(), userid = userId, roleid = adminRoleId,
            createdat = now, updatedat = now, version = 0L,
        });
    }

    private void InsertPublicRead(Guid publicRoleId, string collection, DateTime now) =>
        Insert.IntoTable(FrameworkTable("permissions")).Row(new
        {
            id = Guid.CreateVersion7(), roleid = publicRoleId, collection, canread = true, canwrite = false,
            candelete = false, createdat = now, updatedat = now, version = 0L,
        });
}
