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
                ["Struo:ContentAssemblies:0"] = "Struo.Sample.Blog",
                ["Struo:Files:Backend"] = "local",
                ["Struo:Files:Local:RootPath"] = FilesRoot,
                ["Rbac:PublicReadCollections:0"] = "article",
                ["Rbac:PublicReadCollections:1"] = "category",
                ["Rbac:PublicReadCollections:2"] = "file",
                ["Rbac:PublicReadCollections:3"] = "language",
                // Pin OIDC off by default so the IT suite is deterministic regardless of a developer's
                // local, gitignored appsettings.Development.json (which may carry real tenant config for
                // manual OIDC testing). Tests that need it on layer an override via WithWebHostBuilder.
                ["Oidc:Enabled"] = "false"
            }));
    }

    /// <summary>
    /// Returns an HttpClient carrying a valid session cookie for a seeded admin user.
    /// The admin is seeded directly via the DB (independent of the bootstrap seeder and the
    /// /api/users endpoint) and is idempotent across calls within the shared collection fixture.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        using (var scope = Services.CreateScope())
        {
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

        var client = CreateClient();
        // The SPA sends the CSRF header on every cookie-authenticated mutation; mirror that here so
        // these cookie-based clients aren't rejected by CsrfProtectionMiddleware. (M1)
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        resp.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>
    /// Seeds a non-super role with the given read/write grants, a fresh editor user,
    /// the user-role link, and returns a logged-in client + the user id.
    /// </summary>
    public async Task<(HttpClient client, Guid userId)> CreateEditorClientAsync(
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
        var client = CreateClient();
        // The SPA sends the CSRF header on every cookie-authenticated mutation; mirror that here so
        // these cookie-based clients aren't rejected by CsrfProtectionMiddleware. (M1)
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
        // these cookie-based clients aren't rejected by CsrfProtectionMiddleware. (M1)
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
