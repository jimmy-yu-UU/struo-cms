// tests/Struo.Tests/Support/ApiFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
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
                ["Struo:Files:Backend"] = "local",
                ["Struo:Files:Local:RootPath"] = FilesRoot
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
        }

        var client = CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        resp.EnsureSuccessStatusCode();
        return client;
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
