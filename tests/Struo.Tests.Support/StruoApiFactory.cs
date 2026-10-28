// tests/Struo.Tests.Support/StruoApiFactory.cs
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

/// <summary>
/// Host factory for API integration tests: an isolated test database, file roots and the rate-limit,
/// OIDC and migration settings a deterministic suite needs. A test project derives from it and names
/// the collections that are publicly readable through <see cref="PublicReadCollections"/>.
/// </summary>
public class StruoApiFactory : WebApplicationFactory<Program>
{
    private readonly ITestDatabase _db = TestBackend.CreateDatabase("api");

    public string FilesRoot { get; } =
        Path.Combine(Path.GetTempPath(), "struo-files-it-" + Guid.NewGuid().ToString("N"));

    // Isolates the image-variant cache from the API project's App_Data default (same
    // rationale as FilesRoot above) — otherwise the IT suite would write real variant files under
    // the checked-out working tree.
    public string ImageCacheRoot { get; } =
        Path.Combine(Path.GetTempPath(), "struo-image-cache-it-" + Guid.NewGuid().ToString("N"));

    public const string AdminEmail = "it-admin@struo.local";
    public const string AdminPassword = "it-admin-pw-123456";
    public Guid AdminUserId { get; private set; }

    /// <summary>Collections readable without a role, in addition to the core <c>file</c> and <c>language</c>.</summary>
    protected virtual IReadOnlyList<string> PublicReadCollections => [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(HostIsolation.ExternalTargets(FilesRoot, ImageCacheRoot)));
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(PublicReadSettings().Concat(new Dictionary<string, string?>
            {
                ["Database:DbType"] = _db.DbType.ToString(),
                ["Database:ConnectionString"] = _db.ConnectionString,
                // The host builds its schema and seed data from the migrations; the startup gate then
                // checks the result against the entity model (Development).
                ["Database:MigrateOnStartup"] = "true",
                // Struo:ContentAssemblies is read before Build(); see TestHostEnvironment.Pin.
                ["Struo:Files:Backend"] = "local",
                ["Struo:Files:Local:RootPath"] = FilesRoot,
                ["Struo:Files:ImageTransform:CachePath"] = ImageCacheRoot,
                // OIDC is pinned off so a developer's gitignored appsettings.Development.json cannot change
                // the suite. A test that needs a provider layers an override via WithWebHostBuilder.
                ["Oidc:Providers:microsoft:Enabled"] = "false",
                ["Oidc:Providers:google:Enabled"] = "false",
                // Hosts are shared across many test classes behind one client IP, so the login limiters are
                // raised far above the production defaults. A test of the limiter itself runs on its own
                // derived host with a small PermitLimit.
                ["RateLimiting:Login:Enabled"] = "true",
                ["RateLimiting:Login:PermitLimit"] = "100000",
                ["RateLimiting:Login:WindowSeconds"] = "60",
                // The per-account throttle is raised for the same reason: shared admin and editor
                // accounts log in repeatedly across unrelated classes.
                ["RateLimiting:LoginAccount:PermitLimit"] = "100000",
                ["RateLimiting:LoginAccount:WindowSeconds"] = "60"
                // RateLimiting:Password keeps its production default (5/60s). It partitions by user id and
                // each editor or roleless client is a fresh user. The shared admin has one bucket, so use
                // another user, or an isolated derived host, for many admin password changes.
            }).ToDictionary()));
    }

    private IEnumerable<KeyValuePair<string, string?>> PublicReadSettings() =>
        PublicReadCollections.Concat(["file", "language"])
            .Select((name, index) => new KeyValuePair<string, string?>($"Rbac:PublicReadCollections:{index}", name));

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
        await LoginAsync(client, new { email = AdminEmail, password = AdminPassword });
        return client;
    }

    /// <summary>
    /// Seeds a non-super role with the given read/write grants, a fresh editor user, and the
    /// user-role link, and returns the credentials + user id — WITHOUT logging in. Split out of
    /// <see cref="CreateEditorClientAsync"/> so a derived host (e.g. via <c>WithWebHostBuilder</c>)
    /// can seed through this base factory (both hosts share the one test database)
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
        await LoginAsync(client, new { email, password });
        return (client, userId);
    }

    /// <summary>
    /// Seeds a fresh user with NO role and a password, logs them in, and returns the client + id.
    /// For verifying the RBAC "public floor" (role-less authenticated user gets public permissions).
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
        await LoginAsync(client, new { email, password });
        return (client, userId);
    }

    // The SPA sends the CSRF header on every cookie-authenticated mutation; mirror that so these
    // clients are not rejected by CsrfProtectionMiddleware.
    private static async Task LoginAsync(HttpClient client, object credentials)
    {
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", credentials);
        resp.EnsureSuccessStatusCode();
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
