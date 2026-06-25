// tests/Struo.Tests/Support/ApiFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Struo.Tests.Support;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteTestDatabase _db = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DbType"] = "Sqlite",
                ["Database:ConnectionString"] = _db.ConnectionString
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _db.Dispose();
    }
}
