# Phase 6a — Authentication Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add authentication (identity + credentials + revocable cookie session + per-user bearer token) so the system knows who the caller is and stamps real audit identities, without changing the generic engine.

**Architecture:** `User` is a first-class CMS collection (Infrastructure). Credentials verify via Argon2id (PHC-encoded). ASP.NET Core cookie authentication stores tickets server-side in a Redis-backed `ITicketStore` (revocable); a second bearer scheme authenticates a permanent per-user token (SHA-256 stored). Web-coupled pieces live in **Api**; Infrastructure stays web-free. Authorization (RBAC) is deferred to 6b — `IPermissionService` stays allow-all.

**Tech Stack:** .NET 10 / C# latest, SqlSugarCore, ASP.NET Core Controllers + Cookie/Authentication, `Isopoh.Cryptography.Argon2`, `Microsoft.Extensions.Caching.StackExchangeRedis` (`IDistributedCache`), xUnit + AwesomeAssertions, SQLite + in-memory distributed cache for tests.

**Spec:** `docs/superpowers/specs/2026-06-30-phase6a-auth-core-design.md`

## Global Constraints

- Target `net10.0`, `Nullable` enable, `LangVersion` latest, **warnings-as-errors** (build must be clean).
- All DB access via SqlSugar ORM; **zero vendor SQL**. `InitTables` dev-only.
- Packages added via `dotnet add package` at **latest**; versions centralized in `Directory.Packages.props` (never hardcode a version in a `.csproj`).
- Dependency rule (§2): Domain → nothing; Application → Domain; Infrastructure → Application + Domain (+ external pkgs, **no ASP.NET**); Api → Application + Infrastructure. Persistence attributes (`[SugarColumn]`) only on Infrastructure/sample entities; Domain stays package-free.
- Outbound JSON = camelCase. Error envelope = `{ "error": { "message": "..." } }`; success envelope = `{ "data": ... }`.
- Metadata scanned once at startup (no per-request reflection). `AddStruoMetadata` already auto-includes the Infrastructure assembly, so a new `[CmsCollection]` there is scanned automatically.
- TDD: failing test first; commit after each green step.
- Sensitive fields (`Password`, `AccessToken`) must never appear in any projection nor be client-writable.
- Tests are hermetic: SQLite temp DB + `AddDistributedMemoryCache` (no real Redis). Live PG + Redis is a separate manual verification gate (Task 13).

---

### Task 1: `IPasswordHasher` port + Argon2id implementation

**Files:**
- Modify: `Directory.Packages.props` (add `Isopoh.Cryptography.Argon2`)
- Modify: `src/Struo.Infrastructure/Struo.Infrastructure.csproj` (reference the package)
- Create: `src/Struo.Application/Security/IPasswordHasher.cs`
- Create: `src/Struo.Infrastructure/Identity/Argon2idPasswordHasher.cs`
- Test: `tests/Struo.Tests/Identity/PasswordHasherTests.cs`

**Interfaces:**
- Produces: `IPasswordHasher.Hash(string password) → string` (PHC-encoded), `IPasswordHasher.Verify(string encoded, string password) → bool`.

- [ ] **Step 1: Add the package**

Run from repo root:
```bash
dotnet add src/Struo.Infrastructure package Isopoh.Cryptography.Argon2
```
Expected: a `<PackageVersion Include="Isopoh.Cryptography.Argon2" Version="..." />` line is added to `Directory.Packages.props` and a versionless `<PackageReference Include="Isopoh.Cryptography.Argon2" />` to the csproj. (If the SDK adds the version to the csproj instead, move it to `Directory.Packages.props` and leave the reference versionless.)

- [ ] **Step 2: Define the port**

`src/Struo.Application/Security/IPasswordHasher.cs`:
```csharp
namespace Struo.Application.Security;

/// <summary>Hashes and verifies passwords. The encoded string is self-contained
/// (algorithm, parameters, and salt embedded) — no separate salt storage.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string encoded, string password);
}
```

- [ ] **Step 3: Write the failing test**

`tests/Struo.Tests/Identity/PasswordHasherTests.cs`:
```csharp
using AwesomeAssertions;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Xunit;

namespace Struo.Tests.Identity;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new Argon2idPasswordHasher();

    [Fact]
    public void Hash_is_not_the_plaintext_and_differs_each_call()
    {
        var a = _hasher.Hash("correct horse");
        var b = _hasher.Hash("correct horse");
        a.Should().NotBe("correct horse");
        a.Should().NotBe(b); // random salt per call
    }

    [Fact]
    public void Verify_true_for_correct_password_false_otherwise()
    {
        var encoded = _hasher.Hash("s3cret!");
        _hasher.Verify(encoded, "s3cret!").Should().BeTrue();
        _hasher.Verify(encoded, "wrong").Should().BeFalse();
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PasswordHasherTests"`
Expected: FAIL — `Argon2idPasswordHasher` does not exist (compile error).

- [ ] **Step 5: Implement the hasher**

`src/Struo.Infrastructure/Identity/Argon2idPasswordHasher.cs`:
```csharp
using Isopoh.Cryptography.Argon2;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

/// <summary>Argon2id hasher producing a self-contained PHC-encoded string
/// (salt + parameters embedded). No separate salt column is needed.</summary>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => Argon2.Hash(password);

    public bool Verify(string encoded, string password) =>
        Argon2.Verify(encoded, password);
}
```
(If the resolved Isopoh API differs, the equivalent is `Argon2.Hash(string)` / `Argon2.Verify(string, string)`; adjust call shape but keep the `IPasswordHasher` contract.)

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PasswordHasherTests"`
Expected: PASS (both facts).

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props src/Struo.Infrastructure/Struo.Infrastructure.csproj src/Struo.Application/Security/IPasswordHasher.cs src/Struo.Infrastructure/Identity/Argon2idPasswordHasher.cs tests/Struo.Tests/Identity/PasswordHasherTests.cs
git commit -m "feat: IPasswordHasher port + Argon2id (PHC) implementation"
```

---

### Task 2: `User` collection entity + InitTables wiring

**Files:**
- Create: `src/Struo.Infrastructure/Identity/User.cs`
- Modify: `src/Struo.Api/Program.cs` (add `typeof(User)` to the dev `InitTables` list)
- Test: `tests/Struo.Tests/Identity/UserCollectionTests.cs`

**Interfaces:**
- Produces: entity `Struo.Infrastructure.Identity.User` with `Guid Id`, `string Email`, `string Password`, `string? Name`, `bool IsActive`, `string? AccessToken`. Collection route name `user`.

- [ ] **Step 1: Write the failing test (schema hides secrets)**

`tests/Struo.Tests/Identity/UserCollectionTests.cs`:
```csharp
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class UserCollectionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task User_schema_does_not_expose_password_or_accessToken()
    {
        var c = _factory.CreateClient();
        var resp = await c.GetAsync("/api/schema/user");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fields = Root(await resp.Content.ReadAsStringAsync()).GetProperty("fields");
        var names = fields.EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        names.Should().Contain("email");
        names.Should().NotContain("password");     // Hidden
        names.Should().NotContain("accessToken");   // Hidden
    }
}
```
(`Hidden` fields are dropped from the schema field list and from `ItemService.Project`; this asserts the schema view. A read-path test is added in Task 11 once auth gates the `user` collection.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UserCollectionTests"`
Expected: FAIL — `user` collection unknown → 404 (entity not created yet).

- [ ] **Step 3: Create the `User` entity**

`src/Struo.Infrastructure/Identity/User.cs`:
```csharp
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>
/// Framework-owned identity. Collection route is <c>user</c>. Credentials never leave the
/// server: <see cref="Password"/> stores the Argon2id PHC hash and <see cref="AccessToken"/>
/// stores the SHA-256 of the issued bearer token — both Hidden+ReadOnly so the generic CRUD
/// path can neither project nor accept them. PK is a <see cref="Guid"/> (UUIDv7), assigned by
/// the create flow.
/// </summary>
[SugarTable("users")]
[CmsCollection("User", Group = "System", DefaultDisplayField = nameof(Email))]
public sealed class User : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_users_email"])]
    [CmsField(Label = "Email", Interface = FieldInterface.Email, Required = true, Searchable = true, Sort = 1)]
    public string Email { get; set; } = "";

    [CmsField(Label = "Password", Interface = FieldInterface.Password, Hidden = true, ReadOnly = true, Sort = 2)]
    public string Password { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Name", Interface = FieldInterface.Text, Sort = 3)]
    public string? Name { get; set; }

    [CmsField(Label = "Active", Interface = FieldInterface.Boolean, Sort = 4)]
    public bool IsActive { get; set; } = true;

    [SugarColumn(IsNullable = true, UniqueGroupNameList = ["uq_users_accesstoken"])]
    [CmsField(Label = "Access Token", Interface = FieldInterface.Text, Hidden = true, ReadOnly = true, Sort = 5)]
    public string? AccessToken { get; set; }
}
```
(If `UniqueGroupNameList` is unavailable on the resolved SqlSugar version, fall back to a plain column; uniqueness is also enforced in the service layer by Task 9's duplicate check.)

- [ ] **Step 4: Register `User` for dev table creation**

In `src/Struo.Api/Program.cs`, add `using Struo.Infrastructure.Identity;` and `typeof(User)` to the `InitTables` list:
```csharp
DatabaseInitializer.InitializeDevelopmentSchema(db, app.Environment,
    typeof(Article), typeof(ArticleTranslation), typeof(Category),
    typeof(Struo.Infrastructure.Localization.Language),
    typeof(Struo.Infrastructure.Files.File), typeof(Struo.Infrastructure.Files.FileTranslation),
    typeof(User));
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UserCollectionTests"`
Expected: PASS — schema returns `user` with `email`, without `password`/`accessToken`.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Infrastructure/Identity/User.cs src/Struo.Api/Program.cs tests/Struo.Tests/Identity/UserCollectionTests.cs
git commit -m "feat: User as a CMS collection (password/accessToken hidden)"
```

---

### Task 3: `IUserCredentialStore` + SqlSugar implementation

**Files:**
- Create: `src/Struo.Application/Security/IUserCredentialStore.cs`
- Create: `src/Struo.Infrastructure/Identity/SqlSugarUserCredentialStore.cs`
- Modify: `src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` (register hasher + store)
- Test: `tests/Struo.Tests/Identity/UserCredentialStoreTests.cs`

**Interfaces:**
- Consumes: `ISqlSugarClient`, `IPasswordHasher` (Task 1), `User` (Task 2).
- Produces:
  - `record UserCredential(Guid Id, string PasswordEncoded, bool IsActive);`
  - `IUserCredentialStore.FindByEmailAsync(string email, CancellationToken) → Task<UserCredential?>`
  - `IUserCredentialStore.FindByAccessTokenAsync(string tokenHash, CancellationToken) → Task<UserCredential?>`

- [ ] **Step 1: Define the port**

`src/Struo.Application/Security/IUserCredentialStore.cs`:
```csharp
namespace Struo.Application.Security;

/// <summary>Credential lookups that deliberately bypass the generic projection so password
/// hashes never traverse the read path.</summary>
public sealed record UserCredential(Guid Id, string PasswordEncoded, bool IsActive);

public interface IUserCredentialStore
{
    Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing test**

`tests/Struo.Tests/Identity/UserCredentialStoreTests.cs`:
```csharp
using AwesomeAssertions;
using SqlSugar;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

public class UserCredentialStoreTests
{
    private static ISqlSugarClient NewDb(SqliteTestDatabase db)
    {
        var client = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = db.ConnectionString, DbType = DbType.Sqlite, IsAutoCloseConnection = true
        });
        client.CodeFirst.InitTables(typeof(User));
        return client;
    }

    [Fact]
    public async Task FindByEmail_returns_credential_or_null()
    {
        using var db = new SqliteTestDatabase();
        var client = NewDb(db);
        var hasher = new Argon2idPasswordHasher();
        await client.Insertable(new User
        {
            Id = Guid.CreateVersion7(), Email = "a@b.com",
            Password = hasher.Hash("pw"), IsActive = true
        }).ExecuteCommandAsync();

        var store = new SqlSugarUserCredentialStore(client);
        var found = await store.FindByEmailAsync("A@B.COM"); // case-insensitive
        found.Should().NotBeNull();
        found!.IsActive.Should().BeTrue();
        hasher.Verify(found.PasswordEncoded, "pw").Should().BeTrue();

        (await store.FindByEmailAsync("missing@b.com")).Should().BeNull();
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UserCredentialStoreTests"`
Expected: FAIL — `SqlSugarUserCredentialStore` does not exist.

- [ ] **Step 4: Implement the store**

`src/Struo.Infrastructure/Identity/SqlSugarUserCredentialStore.cs`:
```csharp
using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

public sealed class SqlSugarUserCredentialStore(ISqlSugarClient db) : IUserCredentialStore
{
    public async Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var lowered = email.ToLowerInvariant();
        var u = await db.Queryable<User>()
            .Where(x => x.Email.ToLower() == lowered)
            .FirstAsync(ct);
        return u is null ? null : new UserCredential(u.Id, u.Password, u.IsActive);
    }

    public async Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        var u = await db.Queryable<User>()
            .Where(x => x.AccessToken == tokenHash)
            .FirstAsync(ct);
        return u is null ? null : new UserCredential(u.Id, u.Password, u.IsActive);
    }
}
```

- [ ] **Step 5: Register hasher + store in DI**

In `src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`, inside `AddStruoInfrastructure`, after the `ICurrentUserAccessor` registration add:
```csharp
services.AddSingleton<Struo.Application.Security.IPasswordHasher, Identity.Argon2idPasswordHasher>();
services.AddScoped<Struo.Application.Security.IUserCredentialStore, Identity.SqlSugarUserCredentialStore>();
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UserCredentialStoreTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Application/Security/IUserCredentialStore.cs src/Struo.Infrastructure/Identity/SqlSugarUserCredentialStore.cs src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs tests/Struo.Tests/Identity/UserCredentialStoreTests.cs
git commit -m "feat: IUserCredentialStore + SqlSugar implementation (email/token lookup)"
```

---

### Task 4: `IAuthService` (credential verification)

**Files:**
- Create: `src/Struo.Application/Security/IAuthService.cs`
- Create: `src/Struo.Application/Security/AuthService.cs`
- Modify: `src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` (register `IAuthService`)
- Test: `tests/Struo.Tests/Identity/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `IUserCredentialStore` (Task 3), `IPasswordHasher` (Task 1).
- Produces:
  - `enum AuthFailure { InvalidCredentials, Inactive }`
  - `record AuthResult(Guid? UserId, AuthFailure? Failure)` with `bool Succeeded => UserId is not null;` and `static Ok(Guid)/Fail(AuthFailure)`.
  - `IAuthService.AuthenticateAsync(string email, string password, CancellationToken) → Task<AuthResult>`

- [ ] **Step 1: Define port + result types**

`src/Struo.Application/Security/IAuthService.cs`:
```csharp
namespace Struo.Application.Security;

public enum AuthFailure { InvalidCredentials, Inactive }

public sealed record AuthResult(Guid? UserId, AuthFailure? Failure)
{
    public bool Succeeded => UserId is not null;
    public static AuthResult Ok(Guid id) => new(id, null);
    public static AuthResult Fail(AuthFailure f) => new(null, f);
}

public interface IAuthService
{
    Task<AuthResult> AuthenticateAsync(string email, string password, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing test**

`tests/Struo.Tests/Identity/AuthServiceTests.cs`:
```csharp
using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class AuthServiceTests
{
    private sealed class FakeStore(UserCredential? byEmail) : IUserCredentialStore
    {
        public Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult(byEmail);
        public Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<UserCredential?>(null);
    }
    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string p) => "enc:" + p;
        public bool Verify(string encoded, string p) => encoded == "enc:" + p;
    }

    [Fact]
    public async Task Valid_credentials_succeed()
    {
        var id = Guid.CreateVersion7();
        var svc = new AuthService(new FakeStore(new UserCredential(id, "enc:pw", true)), new PlainHasher());
        var r = await svc.AuthenticateAsync("a@b.com", "pw");
        r.Succeeded.Should().BeTrue();
        r.UserId.Should().Be(id);
    }

    [Fact]
    public async Task Wrong_password_fails_invalid()
    {
        var svc = new AuthService(new FakeStore(new UserCredential(Guid.CreateVersion7(), "enc:pw", true)), new PlainHasher());
        (await svc.AuthenticateAsync("a@b.com", "nope")).Failure.Should().Be(AuthFailure.InvalidCredentials);
    }

    [Fact]
    public async Task Unknown_email_fails_invalid()
    {
        var svc = new AuthService(new FakeStore(null), new PlainHasher());
        (await svc.AuthenticateAsync("x@b.com", "pw")).Failure.Should().Be(AuthFailure.InvalidCredentials);
    }

    [Fact]
    public async Task Inactive_user_fails_inactive()
    {
        var svc = new AuthService(new FakeStore(new UserCredential(Guid.CreateVersion7(), "enc:pw", false)), new PlainHasher());
        (await svc.AuthenticateAsync("a@b.com", "pw")).Failure.Should().Be(AuthFailure.Inactive);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AuthServiceTests"`
Expected: FAIL — `AuthService` does not exist.

- [ ] **Step 4: Implement `AuthService`**

`src/Struo.Application/Security/AuthService.cs`:
```csharp
namespace Struo.Application.Security;

public sealed class AuthService(IUserCredentialStore store, IPasswordHasher hasher) : IAuthService
{
    public async Task<AuthResult> AuthenticateAsync(string email, string password, CancellationToken ct = default)
    {
        var cred = await store.FindByEmailAsync(email, ct);
        if (cred is null || !hasher.Verify(cred.PasswordEncoded, password))
            return AuthResult.Fail(AuthFailure.InvalidCredentials);
        if (!cred.IsActive)
            return AuthResult.Fail(AuthFailure.Inactive);
        return AuthResult.Ok(cred.Id);
    }
}
```

- [ ] **Step 5: Register `IAuthService`**

In `AddStruoInfrastructure`, add:
```csharp
services.AddScoped<Struo.Application.Security.IAuthService, Struo.Application.Security.AuthService>();
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~AuthServiceTests"`
Expected: PASS (4 facts).

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Application/Security/IAuthService.cs src/Struo.Application/Security/AuthService.cs src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs tests/Struo.Tests/Identity/AuthServiceTests.cs
git commit -m "feat: IAuthService credential verification (invalid/inactive results)"
```

---

### Task 5: Access-token generation + hashing helper

**Files:**
- Create: `src/Struo.Application/Security/AccessTokenHasher.cs`
- Test: `tests/Struo.Tests/Identity/AccessTokenHasherTests.cs`

**Interfaces:**
- Produces: `static AccessTokenHasher.Generate() → (string token, string hash)`, `static AccessTokenHasher.Hash(string token) → string`. Token is base64url-encoded 32-byte (256-bit) random; hash is uppercase hex SHA-256.

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Identity/AccessTokenHasherTests.cs`:
```csharp
using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class AccessTokenHasherTests
{
    [Fact]
    public void Generate_returns_token_and_matching_hash()
    {
        var (token, hash) = AccessTokenHasher.Generate();
        token.Should().NotBeNullOrWhiteSpace();
        token.Length.Should().BeGreaterThan(32);            // 256-bit base64url
        AccessTokenHasher.Hash(token).Should().Be(hash);    // deterministic
    }

    [Fact]
    public void Different_tokens_each_call()
    {
        AccessTokenHasher.Generate().token.Should().NotBe(AccessTokenHasher.Generate().token);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AccessTokenHasherTests"`
Expected: FAIL — `AccessTokenHasher` does not exist.

- [ ] **Step 3: Implement the helper**

`src/Struo.Application/Security/AccessTokenHasher.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;

namespace Struo.Application.Security;

/// <summary>Generates high-entropy permanent access tokens and hashes them for storage.
/// Only the hash is persisted; the plaintext token is shown once at generation.</summary>
public static class AccessTokenHasher
{
    public static (string token, string hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32); // 256-bit
        var token = Base64UrlEncode(bytes);
        return (token, Hash(token));
    }

    public static string Hash(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(digest); // uppercase hex
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~AccessTokenHasherTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Security/AccessTokenHasher.cs tests/Struo.Tests/Identity/AccessTokenHasherTests.cs
git commit -m "feat: access-token generation + SHA-256 hashing helper"
```

---

### Task 6: Redis-backed `ITicketStore` (Api)

**Files:**
- Modify: `Directory.Packages.props` (add `Microsoft.Extensions.Caching.StackExchangeRedis`)
- Modify: `src/Struo.Api/Struo.Api.csproj` (reference the package)
- Create: `src/Struo.Api/Auth/DistributedCacheTicketStore.cs`
- Test: `tests/Struo.Tests/Identity/DistributedCacheTicketStoreTests.cs`

**Interfaces:**
- Consumes: `IDistributedCache`.
- Produces: `DistributedCacheTicketStore : ITicketStore` — `StoreAsync`, `RenewAsync`, `RetrieveAsync`, `RemoveAsync`.

- [ ] **Step 1: Add the package**

```bash
dotnet add src/Struo.Api package Microsoft.Extensions.Caching.StackExchangeRedis
```
Expected: version centralized in `Directory.Packages.props`, versionless reference in the Api csproj.

- [ ] **Step 2: Write the failing test**

`tests/Struo.Tests/Identity/DistributedCacheTicketStoreTests.cs`:
```csharp
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Struo.Api.Auth;
using Xunit;

namespace Struo.Tests.Identity;

public class DistributedCacheTicketStoreTests
{
    private static AuthenticationTicket Ticket()
    {
        var identity = new ClaimsIdentity("Cookies");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()));
        return new AuthenticationTicket(new ClaimsPrincipal(identity), "Cookies");
    }

    private static IDistributedCache MemoryCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public async Task Store_then_retrieve_round_trips_then_remove_clears()
    {
        var store = new DistributedCacheTicketStore(MemoryCache());
        var key = await store.StoreAsync(Ticket());

        var retrieved = await store.RetrieveAsync(key);
        retrieved.Should().NotBeNull();
        retrieved!.Principal.FindFirst(ClaimTypes.NameIdentifier).Should().NotBeNull();

        await store.RemoveAsync(key);
        (await store.RetrieveAsync(key)).Should().BeNull();
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DistributedCacheTicketStoreTests"`
Expected: FAIL — `DistributedCacheTicketStore` does not exist.

- [ ] **Step 4: Implement the ticket store**

`src/Struo.Api/Auth/DistributedCacheTicketStore.cs`:
```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;

namespace Struo.Api.Auth;

/// <summary>Server-side session store for cookie auth, backed by IDistributedCache (Redis in
/// real environments, in-memory in tests). Enables immediate revocation (logout / logout-all).</summary>
public sealed class DistributedCacheTicketStore(IDistributedCache cache) : ITicketStore
{
    private const string Prefix = "auth-ticket:";
    private static readonly DistributedCacheEntryOptions Expiry = new()
    {
        SlidingExpiration = TimeSpan.FromHours(8)
    };

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Prefix + Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var bytes = TicketSerializer.Default.Serialize(ticket);
        return cache.SetAsync(key, bytes, Expiry);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var bytes = await cache.GetAsync(key);
        return bytes is null ? null : TicketSerializer.Default.Deserialize(bytes);
    }

    public Task RemoveAsync(string key) => cache.RemoveAsync(key);
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~DistributedCacheTicketStoreTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props src/Struo.Api/Struo.Api.csproj src/Struo.Api/Auth/DistributedCacheTicketStore.cs tests/Struo.Tests/Identity/DistributedCacheTicketStoreTests.cs
git commit -m "feat: Redis-backed ITicketStore over IDistributedCache (revocable sessions)"
```

---

### Task 7: Cookie auth wiring + `HttpContextCurrentUserAccessor` + AuthController (login/logout/me)

**Files:**
- Create: `src/Struo.Api/Auth/AuthSchemes.cs`
- Create: `src/Struo.Api/Auth/HttpContextCurrentUserAccessor.cs`
- Create: `src/Struo.Api/Auth/AuthWiring.cs`
- Create: `src/Struo.Api/Controllers/AuthController.cs`
- Modify: `src/Struo.Api/Program.cs` (call auth wiring; `UseAuthentication/UseAuthorization`)
- Test: `tests/Struo.Tests/Identity/LoginFlowTests.cs`

**Interfaces:**
- Consumes: `IAuthService` (Task 4), `DistributedCacheTicketStore` (Task 6).
- Produces: `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`; `AuthSchemes.Cookie="Cookies"`, `AuthSchemes.Bearer="Bearer"`, `AuthSchemes.CookieOrBearer="Cookies,Bearer"`; real `ICurrentUserAccessor`.

> NOTE: the bearer scheme registration line is added in Task 8 (its handler does not exist yet). In this task register only the cookie scheme.

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Identity/LoginFlowTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class LoginFlowTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedUser(string email, string password, bool active = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        if (await db.Queryable<User>().Where(u => u.Email == email).AnyAsync()) return;
        await db.Insertable(new User
        {
            Id = Guid.CreateVersion7(), Email = email, Password = hasher.Hash(password), IsActive = active
        }).ExecuteCommandAsync();
    }

    [Fact]
    public async Task Login_valid_sets_cookie_and_me_returns_user()
    {
        await SeedUser("login@b.com", "pw12345678");
        var c = _factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/auth/login", new { email = "login@b.com", password = "pw12345678" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.Contains("Set-Cookie").Should().BeTrue();

        var me = await c.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_wrong_password_returns_401()
    {
        await SeedUser("login2@b.com", "pw12345678");
        var c = _factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/auth/login", new { email = "login2@b.com", password = "WRONG" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_when_anonymous_returns_401()
    {
        (await _factory.CreateClient().GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LoginFlowTests"`
Expected: FAIL — `/api/auth/login` not found / compile errors.

- [ ] **Step 3: Scheme constants + real accessor**

`src/Struo.Api/Auth/AuthSchemes.cs`:
```csharp
namespace Struo.Api.Auth;

public static class AuthSchemes
{
    public const string Cookie = "Cookies";
    public const string Bearer = "Bearer";
    public const string CookieOrBearer = "Cookies,Bearer";
}
```

`src/Struo.Api/Auth/HttpContextCurrentUserAccessor.cs`:
```csharp
using System.Security.Claims;
using Struo.Application.Abstractions;

namespace Struo.Api.Auth;

public sealed class HttpContextCurrentUserAccessor(IHttpContextAccessor accessor) : ICurrentUserAccessor
{
    public Guid? GetCurrentUserId()
    {
        var id = accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var g) ? g : null;
    }
}
```

- [ ] **Step 4: Auth wiring extension (cookie scheme only for now)**

`src/Struo.Api/Auth/AuthWiring.cs`:
```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Struo.Application.Abstractions;

namespace Struo.Api.Auth;

public static class AuthWiring
{
    public static IServiceCollection AddStruoAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<ICurrentUserAccessor, HttpContextCurrentUserAccessor>());

        // Session store backend: real Redis when configured, in-memory otherwise (tests/dev fallback).
        var redis = config.GetValue<string>("Redis:ConnectionString");
        if (!string.IsNullOrWhiteSpace(redis))
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);
        else
            services.AddDistributedMemoryCache();

        services.AddSingleton<DistributedCacheTicketStore>();

        services.AddAuthentication(AuthSchemes.Cookie)
            .AddCookie(AuthSchemes.Cookie, options =>
            {
                options.Cookie.Name = "struo.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                // API, not MVC views: return 401/403 instead of redirecting.
                options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            });
        // NOTE (Task 8): append
        //   .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>(AuthSchemes.Bearer, _ => { });

        services.AddAuthorization();
        return services;
    }
}
```

- [ ] **Step 5: AuthController**

`src/Struo.Api/Controllers/AuthController.cs`:
```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Struo.Api.Auth;
using Struo.Application.Security;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    public sealed record LoginRequest(string Email, string Password);

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken ct)
    {
        var result = await auth.AuthenticateAsync(body.Email, body.Password, ct);
        if (!result.Succeeded)
            return Unauthorized(new { error = new { message = "Invalid credentials." } });

        var identity = new ClaimsIdentity(AuthSchemes.Cookie);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, result.UserId!.Value.ToString()));
        await HttpContext.SignInAsync(AuthSchemes.Cookie, new ClaimsPrincipal(identity));
        return Ok(new { data = new { id = result.UserId } });
    }

    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSchemes.Cookie);
        return NoContent();
    }

    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    [HttpGet("me")]
    public IActionResult Me() =>
        Ok(new { data = new { id = User.FindFirstValue(ClaimTypes.NameIdentifier) } });
}
```

- [ ] **Step 6: Wire into Program.cs**

In `src/Struo.Api/Program.cs`: add `using Struo.Api.Auth;`. After `builder.Services.AddStruoFiles(...)` add:
```csharp
builder.Services.AddStruoAuth(builder.Configuration);
builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(AuthSchemes.Cookie)
    .PostConfigure<DistributedCacheTicketStore>((options, store) => options.SessionStore = store);
```
After `app.UseSerilogRequestLogging();` and before `app.MapControllers();` add:
```csharp
app.UseAuthentication();
app.UseAuthorization();
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~LoginFlowTests"`
Expected: PASS (login 200 + Set-Cookie, me 200 with cookie, wrong pw 401, anonymous me 401).

- [ ] **Step 8: Run the full suite (no enforcement yet → existing tests still green)**

Run: `dotnet test`
Expected: PASS — content endpoints remain anonymous until Task 11.

- [ ] **Step 9: Commit**

```bash
git add src/Struo.Api/Auth/ src/Struo.Api/Controllers/AuthController.cs src/Struo.Api/Program.cs tests/Struo.Tests/Identity/LoginFlowTests.cs
git commit -m "feat: cookie auth + HttpContext accessor + login/logout/me endpoints"
```

---

### Task 8: Bearer token authentication handler + access-token endpoints

> Depends on the `ApiFactory.CreateAuthenticatedClientAsync` helper (Task 11 Step 4). **Recommended:** implement Task 11 Step 4 (the `ApiFactory` helper + bootstrap config) before this task so the integration test below can authenticate. Everything else in Task 11 can follow.

**Files:**
- Create: `src/Struo.Api/Auth/BearerTokenAuthenticationHandler.cs`
- Modify: `src/Struo.Api/Auth/AuthWiring.cs` (append the bearer scheme — see Task 7 Step 4 note)
- Create: `src/Struo.Api/Controllers/UsersController.cs` (access-token actions; create/password added in Task 9)
- Test: `tests/Struo.Tests/Identity/BearerTokenTests.cs`

**Interfaces:**
- Consumes: `IUserCredentialStore.FindByAccessTokenAsync` (Task 3), `AccessTokenHasher` (Task 5), `ISqlSugarClient`, `User` (Task 2), `ApiFactory.CreateAuthenticatedClientAsync` (Task 11).
- Produces: bearer scheme `"Bearer"`; `POST /api/users/{id}/access-token` (returns plaintext once), `DELETE /api/users/{id}/access-token` (revoke).

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Identity/BearerTokenTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class BearerTokenTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<Guid> SeedUser(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var id = Guid.CreateVersion7();
        await db.Insertable(new User { Id = id, Email = email, Password = hasher.Hash("pw12345678"), IsActive = true })
            .ExecuteCommandAsync();
        return id;
    }

    [Fact]
    public async Task Generated_token_authenticates_then_revoke_rejects()
    {
        var id = await SeedUser($"bearer-{Guid.NewGuid():N}@b.com");
        var admin = await _factory.CreateAuthenticatedClientAsync();

        var gen = await admin.PostAsync($"/api/users/{id}/access-token", null);
        gen.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = Root(await gen.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("token").GetString()!;

        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await c.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.DeleteAsync($"/api/users/{id}/access-token")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Bad_token_is_unauthorized()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
        (await c.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~BearerTokenTests"`
Expected: FAIL — handler/endpoints missing.

- [ ] **Step 3: Implement the bearer handler**

`src/Struo.Api/Auth/BearerTokenAuthenticationHandler.cs`:
```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Struo.Application.Security;

namespace Struo.Api.Auth;

public sealed class BearerTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IUserCredentialStore store)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0) return AuthenticateResult.NoResult();

        var cred = await store.FindByAccessTokenAsync(AccessTokenHasher.Hash(token));
        if (cred is null || !cred.IsActive)
            return AuthenticateResult.Fail("Invalid token.");

        var identity = new ClaimsIdentity(AuthSchemes.Bearer);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, cred.Id.ToString()));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), AuthSchemes.Bearer);
        return AuthenticateResult.Success(ticket);
    }
}
```

- [ ] **Step 4: Register the bearer scheme**

In `src/Struo.Api/Auth/AuthWiring.cs`, append to the `AddAuthentication(...).AddCookie(...)` chain:
```csharp
.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>(
    AuthSchemes.Bearer, _ => { });
```

- [ ] **Step 5: Access-token endpoints**

`src/Struo.Api/Controllers/UsersController.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class UsersController(ISqlSugarClient db, IPasswordHasher hasher, IUserCredentialStore store) : ControllerBase
{
    [HttpPost("{id:guid}/access-token")]
    public async Task<IActionResult> GenerateToken(Guid id, CancellationToken ct)
    {
        var (token, hash) = AccessTokenHasher.Generate();
        var updated = await db.Updateable<User>()
            .SetColumns(u => u.AccessToken == hash).Where(u => u.Id == id).ExecuteCommandAsync(ct);
        if (updated == 0) return NotFound();
        return Ok(new { data = new { token } }); // shown once
    }

    [HttpDelete("{id:guid}/access-token")]
    public async Task<IActionResult> RevokeToken(Guid id, CancellationToken ct)
    {
        var updated = await db.Updateable<User>()
            .SetColumns(u => u.AccessToken == null).Where(u => u.Id == id).ExecuteCommandAsync(ct);
        return updated == 0 ? NotFound() : NoContent();
    }
}
```
(`hasher`/`store` are injected here so Task 9 can add create/password actions without touching the constructor.)

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~BearerTokenTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Api/Auth/BearerTokenAuthenticationHandler.cs src/Struo.Api/Auth/AuthWiring.cs src/Struo.Api/Controllers/UsersController.cs tests/Struo.Tests/Identity/BearerTokenTests.cs
git commit -m "feat: bearer token auth handler + access-token generate/revoke endpoints"
```

---

### Task 9: User provisioning — create + change password

**Files:**
- Modify: `src/Struo.Api/Controllers/UsersController.cs` (add create + password endpoints)
- Test: `tests/Struo.Tests/Identity/UserProvisioningTests.cs`

**Interfaces:**
- Consumes: `IPasswordHasher` (Task 1), `IUserCredentialStore` (Task 3), `ISqlSugarClient`.
- Produces: `POST /api/users` (create), `PUT /api/users/{id}/password` (change).

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Identity/UserProvisioningTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class UserProvisioningTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task Create_user_hashes_password_and_never_returns_it()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var email = $"new-{Guid.NewGuid():N}@b.com";
        var resp = await admin.PostAsJsonAsync("/api/users", new { email, password = "pw12345678", name = "New" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotContain("pw12345678");

        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "pw12345678" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Duplicate_email_returns_409()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var email = $"dup-{Guid.NewGuid():N}@b.com";
        await admin.PostAsJsonAsync("/api/users", new { email, password = "pw12345678" });
        var second = await admin.PostAsJsonAsync("/api/users", new { email, password = "pw12345678" });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Weak_password_returns_400()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var resp = await admin.PostAsJsonAsync("/api/users", new { email = $"weak-{Guid.NewGuid():N}@b.com", password = "short" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UserProvisioningTests"`
Expected: FAIL — `POST /api/users` not found.

- [ ] **Step 3: Implement create + change-password**

Add to `UsersController`:
```csharp
public sealed record CreateUserRequest(string Email, string Password, string? Name);
public sealed record ChangePasswordRequest(string NewPassword, string? CurrentPassword);

private const int MinPasswordLength = 8;

[HttpPost]
public async Task<IActionResult> Create([FromBody] CreateUserRequest body, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(body.Email))
        return BadRequest(new { error = new { message = "Email is required." } });
    if (body.Password is null || body.Password.Length < MinPasswordLength)
        return BadRequest(new { error = new { message = $"Password must be at least {MinPasswordLength} characters." } });
    if (await store.FindByEmailAsync(body.Email, ct) is not null)
        return Conflict(new { error = new { message = "Email already in use." } });

    var id = Guid.CreateVersion7();
    await db.Insertable(new User
    {
        Id = id, Email = body.Email, Password = hasher.Hash(body.Password), Name = body.Name, IsActive = true
    }).ExecuteCommandAsync(ct);
    return Created($"/api/items/user/{id}", new { data = new { id, email = body.Email, name = body.Name } });
}

[HttpPut("{id:guid}/password")]
public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordRequest body, CancellationToken ct)
{
    if (body.NewPassword is null || body.NewPassword.Length < MinPasswordLength)
        return BadRequest(new { error = new { message = $"Password must be at least {MinPasswordLength} characters." } });

    var updated = await db.Updateable<User>()
        .SetColumns(u => u.Password == hasher.Hash(body.NewPassword))
        .Where(u => u.Id == id).ExecuteCommandAsync(ct);
    return updated == 0 ? NotFound() : NoContent();
}
```
(Self-service `CurrentPassword` re-verification is enforced in 6b alongside RBAC ownership rules; only an admin-authenticated caller reaches this endpoint in 6a.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UserProvisioningTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Controllers/UsersController.cs tests/Struo.Tests/Identity/UserProvisioningTests.cs
git commit -m "feat: user provisioning endpoints (create + change password)"
```

---

### Task 10: Dev bootstrap admin seeder

**Files:**
- Create: `src/Struo.Infrastructure/Identity/AdminUserSeeder.cs`
- Modify: `src/Struo.Api/Program.cs` (invoke seeder in the dev block after `LanguageSeeder`)
- Modify: `src/Struo.Api/appsettings.json` (placeholder `Auth:BootstrapAdmin` section)
- Test: `tests/Struo.Tests/Identity/AdminUserSeederTests.cs`

**Interfaces:**
- Consumes: `ISqlSugarClient`, `IPasswordHasher` (Task 1), `User` (Task 2).
- Produces: `AdminUserSeeder.SeedAsync(ISqlSugarClient db, IPasswordHasher hasher, string? email, string? password) → Task`.

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Identity/AdminUserSeederTests.cs`:
```csharp
using AwesomeAssertions;
using SqlSugar;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

public class AdminUserSeederTests
{
    private static ISqlSugarClient NewDb(SqliteTestDatabase db)
    {
        var c = new SqlSugarClient(new ConnectionConfig
        { ConnectionString = db.ConnectionString, DbType = DbType.Sqlite, IsAutoCloseConnection = true });
        c.CodeFirst.InitTables(typeof(User));
        return c;
    }

    [Fact]
    public async Task Seeds_admin_when_no_users_and_is_idempotent()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);
        var hasher = new Argon2idPasswordHasher();

        await AdminUserSeeder.SeedAsync(db, hasher, "admin@struo.local", "admin12345678");
        (await db.Queryable<User>().CountAsync()).Should().Be(1);

        await AdminUserSeeder.SeedAsync(db, hasher, "admin@struo.local", "admin12345678");
        (await db.Queryable<User>().CountAsync()).Should().Be(1); // no duplicate
    }

    [Fact]
    public async Task Does_nothing_when_config_missing()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);
        await AdminUserSeeder.SeedAsync(db, new Argon2idPasswordHasher(), null, null);
        (await db.Queryable<User>().CountAsync()).Should().Be(0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AdminUserSeederTests"`
Expected: FAIL — `AdminUserSeeder` does not exist.

- [ ] **Step 3: Implement the seeder**

`src/Struo.Infrastructure/Identity/AdminUserSeeder.cs`:
```csharp
using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

/// <summary>Dev-only: seeds an initial admin from config when the user table is empty.</summary>
public static class AdminUserSeeder
{
    public static async Task SeedAsync(ISqlSugarClient db, IPasswordHasher hasher, string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        if (await db.Queryable<User>().AnyAsync()) return;
        await db.Insertable(new User
        {
            Id = Guid.CreateVersion7(), Email = email, Password = hasher.Hash(password),
            Name = "Administrator", IsActive = true
        }).ExecuteCommandAsync();
    }
}
```

- [ ] **Step 4: Invoke in Program.cs dev block + config placeholder**

In `src/Struo.Api/Program.cs`, inside `if (app.Environment.IsDevelopment())`, after `await LanguageSeeder.SeedAsync(db);` add:
```csharp
var hasher = scope.ServiceProvider.GetRequiredService<Struo.Application.Security.IPasswordHasher>();
await Struo.Infrastructure.Identity.AdminUserSeeder.SeedAsync(db, hasher,
    builder.Configuration["Auth:BootstrapAdmin:Email"],
    builder.Configuration["Auth:BootstrapAdmin:Password"]);
```
In `src/Struo.Api/appsettings.json` add (placeholder; real password via user-secrets / env / `appsettings.Development.json`):
```json
"Auth": { "BootstrapAdmin": { "Email": "", "Password": "" } }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~AdminUserSeederTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Infrastructure/Identity/AdminUserSeeder.cs src/Struo.Api/Program.cs src/Struo.Api/appsettings.json tests/Struo.Tests/Identity/AdminUserSeederTests.cs
git commit -m "feat: dev bootstrap admin seeder (config-driven, idempotent)"
```

---

### Task 11: Enforce auth on mutations + protected-collection guard + migrate test suite

**Files:**
- Create: `src/Struo.Api/Auth/ProtectedCollections.cs`
- Modify: `src/Struo.Api/Controllers/ItemsController.cs` (authorize mutations; protected-collection read guard)
- Modify: `src/Struo.Api/Controllers/FilesController.cs` (authorize Upload + Delete)
- Modify: `tests/Struo.Tests/Support/ApiFactory.cs` (bootstrap admin config + `CreateAuthenticatedClientAsync` + `AdminUserId`)
- Modify: existing mutating tests (Step 5) to use the authenticated client.
- Test: `tests/Struo.Tests/Identity/AuthEnforcementTests.cs`

**Interfaces:**
- Consumes: cookie/bearer schemes (Tasks 7-8), bootstrap seeder (Task 10).
- Produces: `ApiFactory.CreateAuthenticatedClientAsync() → Task<HttpClient>`, `ApiFactory.AdminUserId → Guid`, `ProtectedCollections.Set` (contains `"user"`).

> **Implement Step 4 (the `ApiFactory` helper) early** — Tasks 8 and 9 tests depend on it. The rest of this task (enforcement + migration) follows once the auth mechanism exists.

- [ ] **Step 1: Write the failing enforcement test**

`tests/Struo.Tests/Identity/AuthEnforcementTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class AuthEnforcementTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task Anonymous_write_is_401()
    {
        var c = _factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "X" } } });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_write_stamps_real_user()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var create = await admin.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "Y" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        Root(await create.Content.ReadAsStringAsync()).GetProperty("data")
            .GetProperty("createdBy").GetString().Should().Be(_factory.AdminUserId.ToString());
    }

    [Fact]
    public async Task Anonymous_content_read_allowed_but_user_read_requires_auth()
    {
        var c = _factory.CreateClient();
        (await c.GetAsync("/api/items/article")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetAsync("/api/items/user")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AuthEnforcementTests"`
Expected: FAIL — anonymous write returns 201; helper/property missing.

- [ ] **Step 3: Protected-collection set + controller enforcement**

`src/Struo.Api/Auth/ProtectedCollections.cs`:
```csharp
namespace Struo.Api.Auth;

/// <summary>System collections that require authentication for ALL access (read included),
/// even before RBAC (6b). 6b's per-collection permission model later subsumes this set.</summary>
public static class ProtectedCollections
{
    public static readonly HashSet<string> Set = new(StringComparer.OrdinalIgnoreCase) { "user" };
}
```

In `src/Struo.Api/Controllers/ItemsController.cs`:
- Add `using Microsoft.AspNetCore.Authorization;` and `using Struo.Api.Auth;`.
- Add `[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]` to `Create`, `Update`, `Delete`.
- Add a guard helper and call it as the first line of `List`, `Query`, and `Get`:
```csharp
private IActionResult? GuardProtected(string collection) =>
    ProtectedCollections.Set.Contains(collection) && User.Identity?.IsAuthenticated != true
        ? Unauthorized(new { error = new { message = "Authentication required." } })
        : null;
```
e.g. in `List`:
```csharp
[HttpGet]
public async Task<IActionResult> List(string collection, CancellationToken ct)
{
    if (GuardProtected(collection) is { } denied) return denied;
    // ... existing body unchanged ...
}
```

In `src/Struo.Api/Controllers/FilesController.cs`: add the two `using` lines and `[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]` on `Upload` and `Delete` (leave `Get`/`Download` anonymous — published files are public).

- [ ] **Step 4: Test auth helper in `ApiFactory`**

In `tests/Struo.Tests/Support/ApiFactory.cs`:
- Add to the in-memory config dictionary:
```csharp
["Auth:BootstrapAdmin:Email"] = AdminEmail,
["Auth:BootstrapAdmin:Password"] = AdminPassword,
```
- Add constants, property, and helper (add `using System.Net.Http.Json;`):
```csharp
public const string AdminEmail = "it-admin@struo.local";
public const string AdminPassword = "it-admin-pw-123456";
public Guid AdminUserId { get; private set; }

public async Task<HttpClient> CreateAuthenticatedClientAsync()
{
    var client = CreateClient();
    var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
    resp.EnsureSuccessStatusCode();
    var id = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
        .RootElement.GetProperty("data").GetProperty("id").GetString();
    AdminUserId = Guid.Parse(id!);
    return client; // WebApplicationFactory's default client retains the session cookie
}
```

- [ ] **Step 5: Migrate existing mutating tests**

Switch each test that POSTs/PUTs/DELETEs from `CreateClient()` to `await _factory.CreateAuthenticatedClientAsync()`, and fix audit assertions from `Guid.Empty.ToString()` to `_factory.AdminUserId.ToString()`. Files:
- `tests/Struo.Tests/Api/ItemsEndpointTests.cs` — all mutating tests; update the `createdBy` assertion in `Crud_round_trip_with_envelope_and_audit`.
- `tests/Struo.Tests/Api/ArticleIdentityTests.cs`
- `tests/Struo.Tests/Files/FileUploadTests.cs`, `FileDownloadTests.cs`, `FileCollectionTests.cs`, `FileReferenceTests.cs`, `FileTranslationTests.cs`
- `tests/Struo.Tests/Localization/LanguageCollectionTests.cs`, `TranslationReadWriteTests.cs`, `TranslationCreateRequiredTests.cs`, `TranslationQueryTests.cs`, `LocaleSecurityTests.cs`, `TranslationMultibyteBufferTests.cs`
- `tests/Struo.Tests/Query/RelationWriteTests.cs`

Replace, at each mutating call site:
```csharp
var client = _factory.CreateClient();
```
with
```csharp
var client = await _factory.CreateAuthenticatedClientAsync();
```
Pure read-only tests may stay on `CreateClient()`. Practical loop: run `dotnet test`, fix each test that now returns 401, one file at a time.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: PASS — `AuthEnforcementTests` green; migrated tests authenticate; read-only/login paths still work.

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Api/Auth/ProtectedCollections.cs src/Struo.Api/Controllers/ItemsController.cs src/Struo.Api/Controllers/FilesController.cs tests/
git commit -m "feat: enforce auth on mutations + protected user collection; migrate test suite to authenticated client"
```

---

### Task 12: Redis readiness health check

**Files:**
- Create: `src/Struo.Infrastructure/Health/CacheReadinessCheck.cs`
- Modify: `src/Struo.Api/Program.cs` (register under tag `ready`)
- Test: `tests/Struo.Tests/Health/CacheReadinessCheckTests.cs`

**Interfaces:**
- Consumes: `IDistributedCache`.
- Produces: `CacheReadinessCheck : IHealthCheck` — round-trip set/get → Healthy; exception → Unhealthy.

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Health/CacheReadinessCheckTests.cs`:
```csharp
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Struo.Infrastructure.Health;
using Xunit;

namespace Struo.Tests.Health;

public class CacheReadinessCheckTests
{
    [Fact]
    public async Task Healthy_when_cache_round_trips()
    {
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var check = new CacheReadinessCheck(cache);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CacheReadinessCheckTests"`
Expected: FAIL — `CacheReadinessCheck` does not exist.

- [ ] **Step 3: Implement the check**

`src/Struo.Infrastructure/Health/CacheReadinessCheck.cs`:
```csharp
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Struo.Infrastructure.Health;

public sealed class CacheReadinessCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            const string key = "health:ping";
            await cache.SetStringAsync(key, "1", new DistributedCacheEntryOptions
            { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5) }, ct);
            var v = await cache.GetStringAsync(key, ct);
            return v == "1" ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("cache round-trip mismatch");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("cache unavailable", ex);
        }
    }
}
```
(Depends only on `IDistributedCache` + health abstractions — no ASP.NET; stays in Infrastructure beside `DbReadinessCheck`. Add a `Microsoft.Extensions.Caching.Abstractions` reference to Infrastructure if it is not already present transitively.)

- [ ] **Step 4: Register under the `ready` tag**

In `src/Struo.Api/Program.cs`:
```csharp
builder.Services.AddHealthChecks()
    .AddCheck<DbReadinessCheck>("database", tags: ["ready"])
    .AddCheck<Struo.Infrastructure.Health.CacheReadinessCheck>("cache", tags: ["ready"]);
```

- [ ] **Step 5: Run test + full suite**

Run: `dotnet test --filter "FullyQualifiedName~CacheReadinessCheckTests"` then `dotnet test`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Infrastructure/Health/CacheReadinessCheck.cs src/Struo.Api/Program.cs tests/Struo.Tests/Health/CacheReadinessCheckTests.cs
git commit -m "feat: cache (Redis) readiness health check under /health/ready"
```

---

### Task 13: Live Postgres + Redis verification gate

**Files:**
- Modify: `docs/guide/` (append a Phase 6a auth section — setup/run, bootstrap admin, login/token usage)
- No production code; this is the evidence gate from spec §10. **No unit tests — capture evidence in the PR / commit.**

- [ ] **Step 1: Run against real Postgres + Redis**

Provide `Database:DbType=PostgreSQL` + a real connection string, `Redis:ConnectionString`, and `Auth:BootstrapAdmin:Email/Password` via user-secrets / `appsettings.Development.json` (gitignored) / env. Start: `dotnet run --project src/Struo.Api`.

- [ ] **Step 2: Verify schema on Postgres**

Confirm the `users` table has columns `email` (unique), `password`, `name`, `isactive`, `accesstoken` (unique, nullable), plus audit columns. Capture `information_schema.columns`.

- [ ] **Step 3: Verify login + audit round-trip**

`POST /api/auth/login` (bootstrap admin) → 200 + cookie. Create an article with the cookie → `createdBy` equals the admin id. Anonymous write → 401. Anonymous `GET /api/items/user` → 401; `GET /api/items/article` → 200.

- [ ] **Step 4: Verify Redis session + revocation**

Confirm a key under `auth-ticket:` in Redis after login. `POST /api/auth/logout` → key removed, cookie no longer authenticates. Restart the app → a pre-logout session still authenticates (server-side store survives restart).

- [ ] **Step 5: Verify bearer token**

Generate a token (`POST /api/users/{id}/access-token`), call a protected endpoint with `Authorization: Bearer <token>` → authorized; revoke (`DELETE`) → 401.

- [ ] **Step 6: Document + commit**

Update `docs/guide/` with the auth setup/usage section. Commit:
```bash
git add docs/guide/
git commit -m "docs: Phase 6a auth setup/usage guide + live PG/Redis verification evidence"
```

---

## Notes on ordering & dependencies

- Tasks 1→5 are independent building blocks (hasher, entity, store, auth service, token helper).
- Task 6 (ticket store) + Task 7 (cookie wiring + login) establish the session; Task 8 (bearer) and Tasks 9-10 build on them.
- **Task 11 turns on enforcement and migrates the existing suite.** Tasks 8 & 9 tests use `CreateAuthenticatedClientAsync` (Task 11 Step 4) — implement that helper right after Task 7, then proceed 8→9→10→11. Run the full `dotnet test` green before each commit from Task 11 onward.
- Build must stay clean (warnings-as-errors) at every commit.
