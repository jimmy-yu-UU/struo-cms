// tests/Struo.Tests/Support/ApiFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using System.Net.Http.Json;

namespace Struo.Tests.Support;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteTestDatabase _db = new();

    public string FilesRoot { get; } =
        Path.Combine(Path.GetTempPath(), "struo-files-it-" + Guid.NewGuid().ToString("N"));

    // Isolates the image-variant cache from the repo's src/Struo.Api/App_Data default (same
    // rationale as FilesRoot above) — otherwise the IT suite would write real variant files under
    // the checked-out working tree.
    public string ImageCacheRoot { get; } =
        Path.Combine(Path.GetTempPath(), "struo-image-cache-it-" + Guid.NewGuid().ToString("N"));

    public const string AdminEmail = "it-admin@struo.local";
    public const string AdminPassword = "it-admin-pw-123456";
    public Guid AdminUserId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DbType"] = "Sqlite",
                ["Database:ConnectionString"] = _db.ConnectionString,
                // Struo:ContentAssemblies cannot be set here - it is read before Build(). See Support/ContentAssemblyEnvBootstrap.cs.
                ["Struo:Files:Backend"] = "local",
                ["Struo:Files:Local:RootPath"] = FilesRoot,
                ["Struo:Files:ImageTransform:CachePath"] = ImageCacheRoot,
                ["Rbac:PublicReadCollections:0"] = "article",
                ["Rbac:PublicReadCollections:1"] = "category",
                ["Rbac:PublicReadCollections:2"] = "file",
                ["Rbac:PublicReadCollections:3"] = "language",
                // Pin OIDC off by default so the IT suite is deterministic regardless of a developer's
                // local, gitignored appsettings.Development.json (which may carry real tenant config for
                // manual OIDC testing). Tests that need it on layer an override via WithWebHostBuilder.
                // The same content-root file is also a live input to Struo:ContentAssemblies (now empty
                // in the shipped appsettings.json): a developer who uncomments a sample entry there gets
                // a suite that behaves differently from CI, since that key can't be overridden here (see
                // ContentAssemblyEnvBootstrap.cs).
                ["Oidc:Enabled"] = "false",
                // The whole suite shares this ApiFactory instance (and its client-IP partition,
                // since TestServer has a fixed connection IP) across ~55 test classes that each log in
                // one or more test users via CreateAuthenticatedClientAsync/CreateEditorClientAsync/
                // CreateRolelessClientAsync. A production-sized PermitLimit (5/60s) would make the
                // suite itself trip the 429 the limiter exists to produce. Kept generously high here;
                // AuthLoginRateLimitTests exercises the real limiter behavior on its own isolated
                // derived host via WithWebHostBuilder with a small, dedicated PermitLimit.
                ["RateLimiting:Login:PermitLimit"] = "100000",
                ["RateLimiting:Login:WindowSeconds"] = "60"
                // RateLimiting:Password is deliberately left at its production default (5/60s) here,
                // unlike RateLimiting:Login above: it partitions by authenticated user id, and every
                // call site except the shared admin logs in a freshly-seeded user via
                // CreateEditorClientAsync/CreateRolelessClientAsync, so each gets its own untouched
                // bucket — raising the default would mask exactly the per-user-partition bug
                // PasswordChangeRateLimitTests exists to catch. The ONE exception is the shared admin
                // from CreateAuthenticatedClientAsync: every admin-authenticated password change in
                // this collection spends down that single bucket. As of this writing
                // UserCredentialWriteAuditTests uses 2 of the 5 permits (well inside the 60s test
                // run), so there is headroom, but it is not unlimited. A future author adding more
                // admin-authenticated password changes to this collection should either (a) use a
                // fresh CreateEditorClientAsync user instead of the shared admin when the test doesn't
                // specifically need admin privileges, or (b) raise RateLimiting:Password:PermitLimit
                // on an isolated derived host via WithWebHostBuilder the way
                // PasswordChangeRateLimitTests/AuthLoginRateLimitTests already do — not here, since
                // that would blunt the very partition-key test this limiter needs.
            }));
    }

    /// <summary>
    /// Idempotently ensures the shared IT admin user/role exist in the DB, WITHOUT creating a client
    /// or logging in. Split out of <see cref="CreateAuthenticatedClientAsync"/> (mirroring the
    /// <see cref="SeedEditorAsync"/>/<see cref="CreateEditorClientAsync"/> split) so a caller that only
    /// needs the admin row to exist — e.g. to then log in against a DIFFERENT (derived) host using the
    /// public <see cref="AdminEmail"/>/<see cref="AdminPassword"/> constants — doesn't have to pay for
    /// a login round-trip against this host whose resulting client it would only discard.
    /// </summary>
    public async Task EnsureAdminSeededAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var existing = await db.Queryable<User>().Where(u => u.Email == AdminEmail).FirstAsync();
        if (existing is null)
        {
            var id = Guid.CreateVersion7();
            await db.Insertable(new User
            {
                Id = id, Email = AdminEmail, Password = hasher.Hash(AdminPassword),
                Name = "IT Admin", IsActive = true
            }).ExecuteCommandAsync();
            AdminUserId = id;
        }
        else
        {
            AdminUserId = existing.Id;
        }

        var adminRole = await db.Queryable<Role>().Where(r => r.Name == "admin").FirstAsync();
        if (adminRole is null)
        {
            adminRole = new Role { Id = Guid.CreateVersion7(), Name = "admin", IsSuperAdmin = true };
            await db.Insertable(adminRole).ExecuteCommandAsync();
        }
        var linked = await db.Queryable<UserRole>()
            .Where(ur => ur.UserId == AdminUserId && ur.RoleId == adminRole.Id).AnyAsync();
        if (!linked)
            await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = AdminUserId, RoleId = adminRole.Id })
                .ExecuteCommandAsync();
    }

    /// <summary>
    /// Returns an HttpClient carrying a valid session cookie for a seeded admin user.
    /// The admin is seeded directly via the DB (independent of the bootstrap seeder and the
    /// /api/users endpoint) and is idempotent across calls within the shared collection fixture.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        await EnsureAdminSeededAsync();

        var client = CreateClient();
        // The SPA sends the CSRF header on every cookie-authenticated mutation; mirror that here so
        // these cookie-based clients aren't rejected by CsrfProtectionMiddleware.
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        resp.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>
    /// Seeds a non-super role with the given read/write grants, a fresh editor user, and the
    /// user-role link, and returns the credentials + user id — WITHOUT logging in. Split out of
    /// <see cref="CreateEditorClientAsync"/> so a derived host (e.g. via <c>WithWebHostBuilder</c>)
    /// can seed through this base factory (both hosts share the one <see cref="SqliteTestDatabase"/>)
    /// and then log the same user in against its OWN <c>HttpClient</c>/cookie jar.
    /// </summary>
    public async Task<(string email, string password, Guid userId)> SeedEditorAsync(
        string[] readCollections, string[] writeCollections)
    {
        Guid userId;
        const string password = "editor-pw-123";
        string email;
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            userId = Guid.CreateVersion7();
            email = $"editor-{userId:N}@struo.test";
            await db.Insertable(new User
            {
                Id = userId, Email = email, Password = hasher.Hash(password),
                Name = "Editor", IsActive = true
            }).ExecuteCommandAsync();
            var role = new Role { Id = Guid.CreateVersion7(), Name = $"editor-{userId:N}" };
            await db.Insertable(role).ExecuteCommandAsync();
            await db.Insertable(new UserRole
            {
                Id = Guid.CreateVersion7(), UserId = userId, RoleId = role.Id
            }).ExecuteCommandAsync();
            foreach (var c in readCollections.Union(writeCollections).Distinct())
                await db.Insertable(new Permission
                {
                    Id = Guid.CreateVersion7(), RoleId = role.Id, Collection = c,
                    CanRead = readCollections.Contains(c), CanWrite = writeCollections.Contains(c)
                }).ExecuteCommandAsync();
        }
        return (email, password, userId);
    }

    /// <summary>
    /// Seeds a non-super role with the given read/write grants, a fresh editor user,
    /// the user-role link, and returns a logged-in client + the user id.
    /// </summary>
    public async Task<(HttpClient client, Guid userId)> CreateEditorClientAsync(
        string[] readCollections, string[] writeCollections)
    {
        var (email, password, userId) = await SeedEditorAsync(readCollections, writeCollections);
        var client = CreateClient();
        // The SPA sends the CSRF header on every cookie-authenticated mutation; mirror that here so
        // these cookie-based clients aren't rejected by CsrfProtectionMiddleware.
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        return (client, userId);
    }

    /// <summary>
    /// Seeds a fresh user with NO role and a password, logs them in, and returns the client + id.
    /// Used to verify the RBAC "public floor" (role-less authenticated user gets public permissions).
    /// </summary>
    public async Task<(HttpClient client, Guid userId)> CreateRolelessClientAsync()
    {
        Guid userId;
        const string password = "roleless-pw-123";
        string email;
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            userId = Guid.CreateVersion7();
            email = $"roleless-{userId:N}@struo.test";
            await db.Insertable(new User
            {
                Id = userId, Email = email, Password = hasher.Hash(password),
                Name = "Roleless", IsActive = true
            }).ExecuteCommandAsync();
        }
        var client = CreateClient();
        // The SPA sends the CSRF header on every cookie-authenticated mutation; mirror that here so
        // these cookie-based clients aren't rejected by CsrfProtectionMiddleware.
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        return (client, userId);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _db.Dispose();
            if (Directory.Exists(FilesRoot)) Directory.Delete(FilesRoot, recursive: true);
        }
    }
}
