# Phase 5 — Files / Media Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add file/media management — a framework-owned `File` collection (UUID PK) with metadata, a multipart upload endpoint, status-gated download/info endpoints, a pluggable `IFileStorage` backend (local disk + S3/MinIO), file references via the Phase-3a relation machinery, and translatable `title`/`alt`.

**Architecture:** Storage is abstracted behind `IFileStorage` (one global backend, selected by config). `FileAsset` is a `Guid`-keyed framework collection scanned like `Language`; `FileTranslation` is its Phase-4 `[CmsTranslations]` sidecar holding `title`/`alt`. The `Guid` PK generalizes the id pipeline (M2M/translation FK coercion + create-time id assignment). Other collections link to files by reusing Phase-3a one-to-one FK (single) and ordered M2M (multiple). Upload streams bytes to storage, extracts image dimensions, and inserts a `draft` row; download enforces `status == published` and either streams (local) or 302-redirects to a presigned URL (S3).

**Tech Stack:** .NET 10, C#, SqlSugarCore, ASP.NET Core controllers, xUnit + AwesomeAssertions, SQLite (tests) / PostgreSQL (dev), MinIO (S3 tests). New packages: `AWSSDK.S3`, `SixLabors.ImageSharp`.

## Global Constraints

- All DB access via SqlSugar ORM; **zero vendor SQL** (§17.4). InitTables dev-only.
- Dependency rule (§2): Domain → nothing; Application → Domain; Infrastructure → Application+Domain; Api → Application+Infrastructure. **Framework code never references `samples/*`.** Domain stays free of external packages.
- Metadata scanned at startup and cached; no per-request reflection beyond existing patterns (§17.6).
- Query DSL never leaks ORM internals; field/relation paths whitelist-validated (§17.7).
- Outbound JSON = camelCase. Packages via `dotnet add package` (latest, centralized in `Directory.Packages.props`).
- Secrets (S3 credentials) via configuration/environment, never hardcoded.
- **Storage:** a single configured backend at a time; no per-row backend column.
- **Access:** only `published` files are externally downloadable; `draft`/`archived` → 404. No auth yet (single route group).
- **i18n:** `title`/`alt` live ONLY on the `FileTranslation` sidecar; read returns all locales under `translations` (`?locale=` filters); write uses the embedded `translations` envelope (Phase-4 semantics).
- Tests: unit in `tests/Struo.Tests/...`; integration via `[Collection("ApiIntegration")]` + `ApiFactory`, asserting over HTTP. Full suite: `dotnet test` (baseline today: 130 passing).
- The SQLite integration fixture gets its schema from `Program.cs`'s Development `InitTables` block (the `ApiFactory` runs under `UseEnvironment("Development")`); add new tables there.

---

### Task 1: Guid PK pipeline generalization

**Goal:** Make the id pipeline PK-type-aware so a `Guid`-keyed collection works end-to-end: M2M/translation FK coercion handles `Guid`, and create assigns `Guid.NewGuid()` for an unset `Guid` PK. All changes additive — the `long` path is unchanged and the existing 130 tests stay green.

**Files:**
- Create: `src/Struo.Infrastructure/Query/IdCoercion.cs`
- Modify: `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` (use `IdCoercion` at the junction FK set `:333-334`, the translation FK set `:518`, and add the create-time Guid assignment in `CreateAsync` `:188-193`)
- Modify: `src/Struo.Application/Query/ItemService.cs` (M2M target-id parse `:384` — emit `string` for non-numeric JSON so Guid target ids survive)
- Test: `tests/Struo.Tests/Query/IdCoercionTests.cs` (new)

**Interfaces:**
- Produces: `public static class IdCoercion { public static object? Coerce(object? value, Type targetType); }` — returns `value` unchanged when already assignable; parses `string`/`Guid` → `Guid` when `targetType` (unwrapped of `Nullable<>`) is `Guid`; otherwise `Convert.ChangeType`, returning the raw value on a failed conversion.

- [ ] **Step 1: Write the failing test** — `tests/Struo.Tests/Query/IdCoercionTests.cs`:

```csharp
using AwesomeAssertions;
using Struo.Infrastructure.Query;
using Xunit;

namespace Struo.Tests.Query;

public class IdCoercionTests
{
    [Fact]
    public void Coerce_string_to_guid()
    {
        var g = Guid.NewGuid();
        IdCoercion.Coerce(g.ToString(), typeof(Guid)).Should().Be(g);
    }

    [Fact]
    public void Coerce_guid_to_guid_is_identity()
    {
        var g = Guid.NewGuid();
        IdCoercion.Coerce(g, typeof(Guid)).Should().Be(g);
    }

    [Fact]
    public void Coerce_string_to_nullable_guid()
    {
        var g = Guid.NewGuid();
        IdCoercion.Coerce(g.ToString(), typeof(Guid?)).Should().Be(g);
    }

    [Fact]
    public void Coerce_long_path_unchanged()
    {
        IdCoercion.Coerce("42", typeof(long)).Should().Be(42L);
        IdCoercion.Coerce(7, typeof(int)).Should().Be(7);
    }

    [Fact]
    public void Coerce_null_returns_null() =>
        IdCoercion.Coerce(null, typeof(Guid)).Should().BeNull();
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test --filter "FullyQualifiedName~IdCoercionTests"`. Expected: FAIL (`IdCoercion` missing).

- [ ] **Step 3: Implement `IdCoercion`** — `src/Struo.Infrastructure/Query/IdCoercion.cs`:

```csharp
namespace Struo.Infrastructure.Query;

/// <summary>
/// PK/FK-type-aware value coercion. Handles <see cref="Guid"/> (not <see cref="IConvertible"/>,
/// so <see cref="Convert.ChangeType(object, Type)"/> throws for it) in addition to the
/// IConvertible types used by the existing long-keyed collections.
/// </summary>
public static class IdCoercion
{
    public static object? Coerce(object? value, Type targetType)
    {
        if (value is null) return null;
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsInstanceOfType(value)) return value;
        if (underlying == typeof(Guid))
            return value is Guid g ? g : Guid.Parse(value.ToString()!);
        try { return Convert.ChangeType(value, underlying); }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        { return value; }
    }
}
```

- [ ] **Step 4: Wire `IdCoercion` into `SqlSugarItemRepository`.**

4a. In `SyncM2MGenericAsync` (`:333-334`), replace the two `Convert.ChangeType` calls:
```csharp
parentProp.SetValue(row, IdCoercion.Coerce(parentId,    parentProp.PropertyType));
targetProp.SetValue(row, IdCoercion.Coerce(targetIds[i], targetProp.PropertyType));
```
(Leave the `sortProp` line using `Convert.ChangeType(i, ...)` — it's always an `int`.)

4b. In `SyncTranslationsGenericAsync` (`:518`), replace:
```csharp
fkProp.SetValue(row, IdCoercion.Coerce(parentId, fkProp.PropertyType));
```

4c. Replace the body of the existing private `CoerceValue` (`:572`) to delegate, so translation field values also get Guid-awareness for free:
```csharp
private static object? CoerceValue(object? raw, Type targetType) => IdCoercion.Coerce(raw, targetType);
```

4d. In `CreateAsync` (`:188`), assign a `Guid` PK before dispatch:
```csharp
public async Task<object> CreateAsync(string collection, object entity, CancellationToken ct = default)
{
    var d = Descriptor(collection);
    var pk = d.EntityType.GetProperty(d.IdProperty)!;
    if (pk.PropertyType == typeof(Guid) && pk.GetValue(entity) is Guid cur && cur == Guid.Empty)
        pk.SetValue(entity, Guid.NewGuid());
    var method = CreateGenericAsyncDef.MakeGenericMethod(d.EntityType);
    return await (Task<object>)method.Invoke(this, [entity])!;
}
```

- [ ] **Step 5: Make Guid M2M target ids survive parsing** in `ItemService.SyncM2MAsync` (`:383-385`). Replace the `Select`:
```csharp
var targetIds = idsElem.EnumerateArray()
    .Select(e => e.ValueKind == JsonValueKind.Number
        ? (object)e.GetInt64()
        : (object)(e.GetString() ?? string.Empty))
    .ToList();
```
(The repository's `IdCoercion` then converts the string form to `Guid` when the junction's target FK is `Guid`. `QueryWhereInAsync` validation already joins values via `ToString()`, so string-form guids work there.)

- [ ] **Step 6: Run the new test + full suite** — `dotnet test --filter "FullyQualifiedName~IdCoercionTests"` (Expected: PASS 5/5), then `dotnet test` (Expected: 130 still green — long path unchanged).

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Infrastructure/Query/IdCoercion.cs src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/IdCoercionTests.cs
git commit -m "feat: PK-type-aware id coercion (Guid) for FK sync + create-time Guid assignment"
```

---

### Task 2: `IFileStorage` abstraction + `LocalFileStorage` + options + DI

**Goal:** The storage seam and the local-disk implementation, plus strongly-typed `FileStorageOptions` validated at startup, registered via `AddStruoFiles`.

**Files:**
- Create: `src/Struo.Application/Files/IFileStorage.cs`
- Create: `src/Struo.Application/Files/FileStorageOptions.cs`
- Create: `src/Struo.Infrastructure/Files/LocalFileStorage.cs`
- Create: `src/Struo.Infrastructure/Files/StorageKey.cs` (key generator)
- Create: `src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs`
- Test: `tests/Struo.Tests/Files/LocalFileStorageTests.cs` (new)
- Test: `tests/Struo.Tests/Files/FileStorageOptionsTests.cs` (new)

**Interfaces:**
- Produces:
  - `public interface IFileStorage { Task SaveAsync(string key, Stream content, CancellationToken ct = default); Task<Stream> OpenReadAsync(string key, CancellationToken ct = default); Task DeleteAsync(string key, CancellationToken ct = default); bool SupportsPresignedUrls { get; } Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default); }`
  - `FileStorageOptions` with nested `LocalOptions { string RootPath }` and `S3Options { string? Endpoint; string? Bucket; string? AccessKey; string? SecretKey; string Region; bool ForcePathStyle; int PresignTtlSeconds; }`, plus `void Validate()`.
  - `public static IServiceCollection AddStruoFiles(this IServiceCollection services, IConfiguration config);`
  - `public static class StorageKey { public static string Create(string originalFileName); }` → `"{yyyy}/{MM}/{guid:N}{ext}"`.

- [ ] **Step 1: Write the failing tests.**

`tests/Struo.Tests/Files/LocalFileStorageTests.cs`:
```csharp
using System.Text;
using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "struo-files-" + Guid.NewGuid().ToString("N"));

    private LocalFileStorage NewStorage() =>
        new(new FileStorageOptions { Local = new FileStorageOptions.LocalOptions { RootPath = _root } });

    [Fact]
    public async Task Save_then_read_round_trips_bytes()
    {
        var storage = NewStorage();
        var key = "2026/06/" + Guid.NewGuid().ToString("N") + ".txt";
        await storage.SaveAsync(key, new MemoryStream(Encoding.UTF8.GetBytes("hello")));

        await using var read = await storage.OpenReadAsync(key);
        using var sr = new StreamReader(read);
        (await sr.ReadToEndAsync()).Should().Be("hello");
    }

    [Fact]
    public async Task Delete_removes_the_file()
    {
        var storage = NewStorage();
        var key = "a/b/" + Guid.NewGuid().ToString("N");
        await storage.SaveAsync(key, new MemoryStream([1, 2, 3]));
        await storage.DeleteAsync(key);
        var act = async () => await storage.OpenReadAsync(key);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public void Local_does_not_support_presigned_urls() =>
        NewStorage().SupportsPresignedUrls.Should().BeFalse();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
```

`tests/Struo.Tests/Files/FileStorageOptionsTests.cs`:
```csharp
using AwesomeAssertions;
using Struo.Application.Files;
using Xunit;

namespace Struo.Tests.Files;

public class FileStorageOptionsTests
{
    [Fact]
    public void Local_backend_requires_root_path()
    {
        var o = new FileStorageOptions { Backend = "local", Local = new() { RootPath = "" } };
        o.Invoking(x => x.Validate()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void S3_backend_requires_bucket_and_credentials()
    {
        var o = new FileStorageOptions { Backend = "s3", S3 = new() { Endpoint = "http://x", Bucket = null } };
        o.Invoking(x => x.Validate()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Unknown_backend_throws()
    {
        new FileStorageOptions { Backend = "azure" }
            .Invoking(x => x.Validate()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Valid_local_passes() =>
        new FileStorageOptions { Backend = "local", Local = new() { RootPath = "App_Data/uploads" } }
            .Invoking(x => x.Validate()).Should().NotThrow();
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test --filter "FullyQualifiedName~Files.LocalFileStorageTests|FullyQualifiedName~Files.FileStorageOptionsTests"`. Expected: FAIL (types missing).

- [ ] **Step 3: Implement the Application types.**

`src/Struo.Application/Files/IFileStorage.cs`:
```csharp
namespace Struo.Application.Files;

/// <summary>Pluggable byte storage for file assets. Exactly one implementation is registered.</summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>True when the backend can mint short-lived direct-download URLs (e.g. S3).</summary>
    bool SupportsPresignedUrls { get; }

    /// <summary>Returns a presigned URL, or null when the backend streams through the API (local).</summary>
    Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
```

`src/Struo.Application/Files/FileStorageOptions.cs`:
```csharp
namespace Struo.Application.Files;

public sealed class FileStorageOptions
{
    public const string SectionName = "Struo:Files";

    public string Backend { get; set; } = "local";          // "local" | "s3"
    public long MaxUploadBytes { get; set; } = 26_214_400;   // 25 MB
    public string[] AllowedContentTypes { get; set; } = [];  // empty = allow all
    public LocalOptions Local { get; set; } = new();
    public S3Options S3 { get; set; } = new();

    public sealed class LocalOptions { public string RootPath { get; set; } = "App_Data/uploads"; }

    public sealed class S3Options
    {
        public string? Endpoint { get; set; }
        public string? Bucket { get; set; }
        public string? AccessKey { get; set; }
        public string? SecretKey { get; set; }
        public string Region { get; set; } = "us-east-1";
        public bool ForcePathStyle { get; set; } = true;
        public int PresignTtlSeconds { get; set; } = 300;
    }

    public void Validate()
    {
        switch (Backend?.ToLowerInvariant())
        {
            case "local":
                if (string.IsNullOrWhiteSpace(Local.RootPath))
                    throw new InvalidOperationException("Struo:Files:Local:RootPath is required when Backend=local.");
                break;
            case "s3":
                if (string.IsNullOrWhiteSpace(S3.Endpoint) || string.IsNullOrWhiteSpace(S3.Bucket)
                    || string.IsNullOrWhiteSpace(S3.AccessKey) || string.IsNullOrWhiteSpace(S3.SecretKey))
                    throw new InvalidOperationException(
                        "Struo:Files:S3 Endpoint, Bucket, AccessKey and SecretKey are required when Backend=s3.");
                break;
            default:
                throw new InvalidOperationException($"Unknown Struo:Files:Backend '{Backend}' (expected 'local' or 's3').");
        }
    }
}
```

- [ ] **Step 4: Implement `StorageKey` + `LocalFileStorage`.**

`src/Struo.Infrastructure/Files/StorageKey.cs`:
```csharp
namespace Struo.Infrastructure.Files;

public static class StorageKey
{
    /// <summary>Builds a collision- and traversal-safe key: <c>{yyyy}/{MM}/{guid:N}{ext}</c>.</summary>
    public static string Create(string originalFileName)
    {
        var ext = Path.GetExtension(originalFileName); // includes leading dot, may be empty
        var now = DateTime.UtcNow;
        return $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{ext}";
    }
}
```

`src/Struo.Infrastructure/Files/LocalFileStorage.cs`:
```csharp
using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

public sealed class LocalFileStorage(FileStorageOptions options) : IFileStorage
{
    private string Root => options.Local.RootPath;

    private string FullPath(string key)
    {
        // Reject traversal: the resolved path must stay under Root.
        var root = Path.GetFullPath(Root);
        var full = Path.GetFullPath(Path.Combine(root, key));
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException($"Storage key '{key}' escapes the storage root.");
        return full;
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken ct = default)
    {
        var path = FullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream>(new FileStream(FullPath(key), FileMode.Open, FileAccess.Read, FileShare.Read));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = FullPath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public bool SupportsPresignedUrls => false;
    public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
```

- [ ] **Step 5: Implement `AddStruoFiles`** — `src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs`. For Task 2, register only `IFileStorage` + `FileStorageOptions`; the commented lines are uncommented by Tasks 3/5/9 as their types appear:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Files;
using Struo.Infrastructure.Files;

namespace Struo.Infrastructure.DependencyInjection;

public static class FileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddStruoFiles(this IServiceCollection services, IConfiguration config)
    {
        var options = new FileStorageOptions();
        config.GetSection(FileStorageOptions.SectionName).Bind(options);
        options.Validate(); // fail-fast at startup
        services.AddSingleton(options);

        // Task 9 swaps in S3FileStorage when Backend=s3:
        // if (string.Equals(options.Backend, "s3", StringComparison.OrdinalIgnoreCase))
        //     services.AddSingleton<IFileStorage, S3FileStorage>();
        // else
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        // services.AddSingleton<IImageDimensionReader, ImageSharpDimensionReader>(); // Task 3
        // services.AddScoped<FileService>();                                          // Task 5
        return services;
    }
}
```

- [ ] **Step 6: Run the new tests** — `dotnet test --filter "FullyQualifiedName~Files.LocalFileStorageTests|FullyQualifiedName~Files.FileStorageOptionsTests"`. Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Application/Files src/Struo.Infrastructure/Files/LocalFileStorage.cs src/Struo.Infrastructure/Files/StorageKey.cs src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs tests/Struo.Tests/Files
git commit -m "feat: IFileStorage abstraction + LocalFileStorage + validated FileStorageOptions"
```

---

### Task 3: Image dimension extraction (ImageSharp)

**Goal:** Read image width/height cheaply (header-only) at upload, behind an Application interface so ImageSharp stays in Infrastructure.

**Files:**
- Create: `src/Struo.Application/Files/IImageDimensionReader.cs`
- Create: `src/Struo.Infrastructure/Files/ImageSharpDimensionReader.cs`
- Modify: `Directory.Packages.props` (add `SixLabors.ImageSharp`) + `src/Struo.Infrastructure/Struo.Infrastructure.csproj` (PackageReference)
- Modify: `src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs` (uncomment the `IImageDimensionReader` registration)
- Test: `tests/Struo.Tests/Files/ImageSharpDimensionReaderTests.cs` (new)

**Interfaces:**
- Produces: `public interface IImageDimensionReader { (int Width, int Height)? TryRead(Stream seekable, string contentType); }`. Returns `null` for non-images / unreadable content; resets the stream position to 0 before returning.

- [ ] **Step 1: Add the package** — `dotnet add src/Struo.Infrastructure package SixLabors.ImageSharp`. Verify it lands in `Directory.Packages.props` as a `PackageVersion` and in the csproj as a versionless `PackageReference` (CPM).

- [ ] **Step 2: Write the failing test** — `tests/Struo.Tests/Files/ImageSharpDimensionReaderTests.cs`:

```csharp
using AwesomeAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class ImageSharpDimensionReaderTests
{
    private static Stream PngOf(int w, int h)
    {
        var ms = new MemoryStream();
        using (var img = new Image<Rgba32>(w, h)) img.SaveAsPng(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Reads_png_dimensions()
    {
        var reader = new ImageSharpDimensionReader();
        reader.TryRead(PngOf(12, 7), "image/png").Should().Be((12, 7));
    }

    [Fact]
    public void Returns_null_for_non_image()
    {
        var reader = new ImageSharpDimensionReader();
        var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not an image"));
        reader.TryRead(bytes, "text/plain").Should().BeNull();
    }
}
```

- [ ] **Step 3: Run to verify it fails** — `dotnet test --filter "FullyQualifiedName~ImageSharpDimensionReaderTests"`. Expected: FAIL.

- [ ] **Step 4: Implement.**

`src/Struo.Application/Files/IImageDimensionReader.cs`:
```csharp
namespace Struo.Application.Files;

public interface IImageDimensionReader
{
    /// <summary>Reads (width, height) from a seekable stream, or null if not a readable image.
    /// Resets the stream position to 0 before returning.</summary>
    (int Width, int Height)? TryRead(Stream seekable, string contentType);
}
```

`src/Struo.Infrastructure/Files/ImageSharpDimensionReader.cs`:
```csharp
using SixLabors.ImageSharp;
using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

public sealed class ImageSharpDimensionReader : IImageDimensionReader
{
    public (int Width, int Height)? TryRead(Stream seekable, string contentType)
    {
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            if (seekable.CanSeek) seekable.Position = 0;
            var info = Image.Identify(seekable); // header-only, no full decode
            return info is null ? null : (info.Width, info.Height);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            return null;
        }
        finally
        {
            if (seekable.CanSeek) seekable.Position = 0;
        }
    }
}
```

- [ ] **Step 5: Uncomment the `IImageDimensionReader` registration** in `FileStorageServiceCollectionExtensions`:
```csharp
services.AddSingleton<IImageDimensionReader, ImageSharpDimensionReader>();
```

- [ ] **Step 6: Run the new test** — `dotnet test --filter "FullyQualifiedName~ImageSharpDimensionReaderTests"`. Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props src/Struo.Infrastructure/Struo.Infrastructure.csproj src/Struo.Application/Files/IImageDimensionReader.cs src/Struo.Infrastructure/Files/ImageSharpDimensionReader.cs src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs tests/Struo.Tests/Files/ImageSharpDimensionReaderTests.cs
git commit -m "feat: image dimension reader (ImageSharp Image.Identify)"
```

---

### Task 4: `FileAsset` + `FileTranslation` collection (Guid PK) + registration + InitTables

**Goal:** The framework `File` collection (Guid PK) with its translatable `title`/`alt` sidecar, scanned into metadata and table-initialized, exposed via `/api/schema/file` and `/api/items/file`.

**Files:**
- Create: `src/Struo.Infrastructure/Files/FileAsset.cs`
- Create: `src/Struo.Infrastructure/Files/FileTranslation.cs`
- Modify: `src/Struo.Api/Program.cs` (add `AddStruoFiles`; add `typeof(FileAsset)`, `typeof(FileTranslation)` to dev `InitTables`)
- Modify: `tests/Struo.Tests/Support/ApiFactory.cs` (add `Struo:Files` config + temp root)
- Test: `tests/Struo.Tests/Files/FileCollectionTests.cs` (new)

**Interfaces:**
- Consumes: the framework-assembly metadata scan (already includes `Struo.Infrastructure` since Phase 4); the `[CmsTranslations]` scanner (Phase 4, FK convention `{ParentTypeName}Id`); `[CmsCollection]`/`[CmsField]`/`IAuditable`.
- Produces: collection `file` with fields `fileName, contentType, size, width, height, status` + translatable `title, alt`; `FileAsset.Id` is `Guid`; sidecar FK is `FileAssetId`.

- [ ] **Step 1: Write the failing test** — `tests/Struo.Tests/Files/FileCollectionTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class FileCollectionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task File_schema_lists_metadata_and_translatable_fields()
    {
        var c = _factory.CreateClient();
        var resp = await c.GetAsync("/api/schema/file");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadAsStringAsync();
        var fields = Root(json).GetProperty("fields");
        var names = fields.EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        names.Should().Contain(new[] { "fileName", "contentType", "size", "status", "title", "alt" });
        fields.EnumerateArray().Should().Contain(f =>
            f.GetProperty("name").GetString() == "title" && f.GetProperty("translatable").GetBoolean());
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test --filter "FullyQualifiedName~FileCollectionTests"`. Expected: FAIL (no `file` collection).

- [ ] **Step 3: Implement the entities.**

`src/Struo.Infrastructure/Files/FileAsset.cs`:
```csharp
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Files;

[SugarTable("files")]
[CmsCollection("File", Group = "System", DefaultDisplayField = nameof(FileName))]
public sealed class FileAsset : IAuditable
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }   // app-assigned (Task 1 create-flow)

    public string StorageKey { get; set; } = "";                     // internal: no [CmsField]

    [CmsField(Label = "File Name", Interface = FieldInterface.Text, Searchable = true, ReadOnly = true, Sort = 1)]
    public string FileName { get; set; } = "";
    [CmsField(Label = "Content Type", Interface = FieldInterface.Text, ReadOnly = true, Sort = 2)]
    public string ContentType { get; set; } = "";
    [CmsField(Label = "Size", Interface = FieldInterface.Number, ReadOnly = true, Sort = 3)]
    public long Size { get; set; }
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Width", Interface = FieldInterface.Number, ReadOnly = true, Sort = 4)]
    public int? Width { get; set; }
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Height", Interface = FieldInterface.Number, ReadOnly = true, Sort = 5)]
    public int? Height { get; set; }
    [CmsField(Label = "Status", Interface = FieldInterface.Select, Sort = 6)]
    [CmsOptions("draft:Draft", "published:Published", "archived:Archived")]
    public string Status { get; set; } = "draft";

    [CmsTranslations(typeof(FileTranslation))]
    [SugarColumn(IsIgnore = true)]
    public List<FileTranslation> Translations { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? UpdatedBy { get; set; }
}
```

`src/Struo.Infrastructure/Files/FileTranslation.cs`:
```csharp
using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Files;

public sealed class FileTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }

    // The [CmsTranslations] scanner derives the FK from the PARENT CLR TYPE NAME: FileAsset -> "FileAssetId"
    // (MetadataScanner.ScanTranslations: type.Name + "Id"). The property MUST be named FileAssetId.
    [SugarColumn(ColumnName = "file_id")] public Guid FileAssetId { get; set; }

    public string Locale { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Title", Interface = FieldInterface.Text, Searchable = true, Sort = 1)]
    public string? Title { get; set; }
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Alt", Interface = FieldInterface.Text, Sort = 2)]
    public string? Alt { get; set; }
}
```
> Sets `[SugarTable("file_translations")]`? — add it: put `[SugarTable("file_translations")]` on the class (omitted above for brevity; include it). The `ColumnName = "file_id"` keeps the DB column readable while the CLR property satisfies the scanner's `{ParentTypeName}Id` convention.

- [ ] **Step 4: Register + init tables.**

4a. In `src/Struo.Api/Program.cs`, after `builder.Services.AddStruoData(builder.Configuration);` add:
```csharp
builder.Services.AddStruoFiles(builder.Configuration);
```

4b. Extend the Development `InitTables` list:
```csharp
DatabaseInitializer.InitializeDevelopmentSchema(db, app.Environment,
    typeof(Article), typeof(ArticleTranslation), typeof(Tag), typeof(Author), typeof(Category), typeof(ArticleTag),
    typeof(Struo.Infrastructure.Localization.Language),
    typeof(Struo.Infrastructure.Files.FileAsset), typeof(Struo.Infrastructure.Files.FileTranslation));
```

4c. Add `Struo:Files` config + an isolated temp root to `tests/Struo.Tests/Support/ApiFactory.cs`:
```csharp
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteTestDatabase _db = new();
    public string FilesRoot { get; } =
        Path.Combine(Path.GetTempPath(), "struo-files-it-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DbType"] = "Sqlite",
                ["Database:ConnectionString"] = _db.ConnectionString,
                ["Struo:Files:Backend"] = "local",
                ["Struo:Files:Local:RootPath"] = FilesRoot,
            }));
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
```

- [ ] **Step 5: Run the new test** — `dotnet test --filter "FullyQualifiedName~FileCollectionTests"`. Expected: PASS.
> If the schema DTO does not currently emit a `translatable` property, this assertion fails — add `Translatable` to the schema field projection (the metadata `FieldMetadata.Translatable` already exists from Phase 4; surface it in `SchemaService`/the schema DTO).

- [ ] **Step 6: Run full suite** — `dotnet test`. Expected: green.

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Infrastructure/Files/FileAsset.cs src/Struo.Infrastructure/Files/FileTranslation.cs src/Struo.Api/Program.cs tests/Struo.Tests/Support/ApiFactory.cs tests/Struo.Tests/Files/FileCollectionTests.cs
git commit -m "feat: File collection (Guid PK) + FileTranslation sidecar; register + init tables"
```

---

### Task 5: `FileService` + multipart upload endpoint (`POST /api/files`)

**Goal:** Upload streams bytes to storage, extracts image dimensions, inserts a `draft` `FileAsset`, and returns its metadata.

**Files:**
- Create: `src/Struo.Infrastructure/Files/FileService.cs` (in Infrastructure — it references `FileAsset`, which Application may not)
- Create: `src/Struo.Api/Controllers/FilesController.cs`
- Modify: `src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs` (uncomment `FileService` registration)
- Test: `tests/Struo.Tests/Files/FileUploadTests.cs` (new)
- Test: `tests/Struo.Tests/Files/FileServiceValidationTests.cs` (new)

**Interfaces:**
- Consumes: `IFileStorage`, `IImageDimensionReader`, `FileStorageOptions`, `ISqlSugarClient`.
- Produces:
  - `FileService.UploadAsync(Stream content, string fileName, string contentType, long length, CancellationToken) : Task<FileAsset>` — validates size/type, stores, extracts dims, inserts (`Id = Guid.NewGuid()`, `Status = "draft"`). Throws `QueryException` (→ 400) on validation failure.
  - `FileService.GetAsync(Guid) : Task<FileAsset?>`; `FileService.DeleteAsync(Guid) : Task<bool>` (used in Task 6).
  - `FilesController.Upload` → `POST /api/files` (multipart, part `file`) → `201` with `{ data: { id, fileName, contentType, size, width, height, status } }`.

- [ ] **Step 1: Write the failing tests** — `tests/Struo.Tests/Files/FileUploadTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class FileUploadTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static MultipartFormDataContent Multipart(byte[] bytes, string name, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    [Fact]
    public async Task Upload_returns_201_with_metadata_and_draft_status()
    {
        var c = _factory.CreateClient();
        var resp = await c.PostAsync("/api/files", Multipart(Encoding.UTF8.GetBytes("hello world"), "note.txt", "text/plain"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        data.GetProperty("fileName").GetString().Should().Be("note.txt");
        data.GetProperty("contentType").GetString().Should().Be("text/plain");
        data.GetProperty("size").GetInt64().Should().Be(11);
        data.GetProperty("status").GetString().Should().Be("draft");
        Guid.TryParse(data.GetProperty("id").GetString(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task Upload_multibyte_filename_round_trips()
    {
        var c = _factory.CreateClient();
        var resp = await c.PostAsync("/api/files", Multipart([1, 2, 3], "報告.bin", "application/octet-stream"));
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        data.GetProperty("fileName").GetString().Should().Be("報告.bin");
    }

    [Fact]
    public async Task Upload_image_populates_width_height()
    {
        var c = _factory.CreateClient();
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var resp = await c.PostAsync("/api/files", Multipart(png, "px.png", "image/png"));
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        data.GetProperty("width").GetInt32().Should().Be(1);
        data.GetProperty("height").GetInt32().Should().Be(1);
    }
}
```

`tests/Struo.Tests/Files/FileServiceValidationTests.cs`:
```csharp
using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Domain.Query;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class FileServiceValidationTests
{
    private sealed class NoopStorage : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public bool SupportsPresignedUrls => false;
        public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }
    private sealed class NoopImages : IImageDimensionReader
    {
        public (int Width, int Height)? TryRead(Stream seekable, string contentType) => null;
    }

    [Fact]
    public async Task Upload_over_cap_throws()
    {
        var opts = new FileStorageOptions { MaxUploadBytes = 4 };
        var svc = new FileService(null!, new NoopStorage(), new NoopImages(), opts);
        var act = async () => await svc.UploadAsync(new MemoryStream(new byte[10]), "x.bin", "application/octet-stream", 10);
        await act.Should().ThrowAsync<QueryException>();
    }

    [Fact]
    public async Task Upload_disallowed_type_throws()
    {
        var opts = new FileStorageOptions { AllowedContentTypes = ["image/png"] };
        var svc = new FileService(null!, new NoopStorage(), new NoopImages(), opts);
        var act = async () => await svc.UploadAsync(new MemoryStream([1]), "x.txt", "text/plain", 1);
        await act.Should().ThrowAsync<QueryException>();
    }
}
```
> The size/type checks run before any DB or storage call, so the `null!` `ISqlSugarClient` is never dereferenced in these tests.

- [ ] **Step 2: Run to verify they fail** — `dotnet test --filter "FullyQualifiedName~FileUploadTests|FullyQualifiedName~FileServiceValidationTests"`. Expected: FAIL.

- [ ] **Step 3: Implement `FileService`** — `src/Struo.Infrastructure/Files/FileService.cs`:

```csharp
using SqlSugar;
using Struo.Application.Files;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Files;

public sealed class FileService(
    ISqlSugarClient db,
    IFileStorage storage,
    IImageDimensionReader images,
    FileStorageOptions options)
{
    public async Task<FileAsset> UploadAsync(
        Stream content, string fileName, string contentType, long length, CancellationToken ct = default)
    {
        if (length <= 0) throw new QueryException("Empty file.");
        if (length > options.MaxUploadBytes)
            throw new QueryException($"File exceeds the maximum size of {options.MaxUploadBytes} bytes.");
        if (options.AllowedContentTypes.Length > 0 &&
            !options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new QueryException($"Content type '{contentType}' is not allowed.");

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var dims = images.TryRead(buffer, contentType);
        buffer.Position = 0;

        var key = StorageKey.Create(fileName);
        await storage.SaveAsync(key, buffer, ct);

        var entity = new FileAsset
        {
            Id = Guid.NewGuid(),
            StorageKey = key,
            FileName = fileName,
            ContentType = contentType,
            Size = length,
            Width = dims?.Width,
            Height = dims?.Height,
            Status = "draft",
        };
        return (await db.Insertable(entity).ExecuteReturnEntityAsync())!;
    }

    public Task<FileAsset?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Queryable<FileAsset>().InSingleAsync(id);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.Queryable<FileAsset>().InSingleAsync(id);
        if (row is null) return false;
        var fkCol = db.EntityMaintenance.GetDbColumnName(nameof(FileTranslation.FileAssetId), typeof(FileTranslation));
        try
        {
            await db.Ado.BeginTranAsync();
            await db.Deleteable<FileTranslation>()
                .Where(new List<IConditionalModel>
                {
                    new ConditionalModel { FieldName = fkCol, ConditionalType = ConditionalType.In, FieldValue = id.ToString() }
                }).ExecuteCommandAsync(ct);
            await db.Deleteable<FileAsset>().In(id).ExecuteCommandAsync(ct);
            await db.Ado.CommitTranAsync();
        }
        catch { await db.Ado.RollbackTranAsync(); throw; }

        try { await storage.DeleteAsync(row.StorageKey, ct); } catch { /* best-effort */ }
        return true;
    }
}
```

- [ ] **Step 4: Uncomment the `FileService` registration** in `FileStorageServiceCollectionExtensions`:
```csharp
services.AddScoped<FileService>();
```

- [ ] **Step 5: Implement `FilesController`** — `src/Struo.Api/Controllers/FilesController.cs`:
```csharp
using Microsoft.AspNetCore.Mvc;
using Struo.Infrastructure.Files;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/files")]
public sealed class FilesController(FileService files) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        if (!Request.HasFormContentType)
            return BadRequest(new { error = new { message = "Expected multipart/form-data." } });
        var form = await Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null) return BadRequest(new { error = new { message = "Missing 'file' part." } });

        await using var stream = file.OpenReadStream();
        var created = await files.UploadAsync(stream, file.FileName, file.ContentType, file.Length, ct);
        return StatusCode(StatusCodes.Status201Created, new
        {
            data = new { id = created.Id, fileName = created.FileName, contentType = created.ContentType,
                         size = created.Size, width = created.Width, height = created.Height, status = created.Status }
        });
    }
}
```
> `QueryException` thrown by `UploadAsync` maps to 400 via the existing `Program.cs` middleware.

- [ ] **Step 6: Run the new tests + full suite** — `dotnet test --filter "FullyQualifiedName~FileUploadTests|FullyQualifiedName~FileServiceValidationTests"` (PASS), then `dotnet test` (green).

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Infrastructure/Files/FileService.cs src/Struo.Api/Controllers/FilesController.cs src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs tests/Struo.Tests/Files
git commit -m "feat: file upload (POST /api/files) — store bytes, extract dims, draft FileAsset"
```

---

### Task 6: Download + info endpoints + status gate (+ delete)

**Goal:** `GET /api/files/{id}` (info) and `GET /api/files/{id}/content` (download) gated to `published`; local streams, S3 will 302 to a presigned URL. `DELETE /api/files/{id}` removes row, translations, and bytes.

**Files:**
- Modify: `src/Struo.Api/Controllers/FilesController.cs` (add `Get`, `Download`, `Delete`; inject `IFileStorage` + `FileStorageOptions`)
- Test: `tests/Struo.Tests/Files/FileDownloadTests.cs` (new)

**Interfaces:**
- Consumes: `FileService.GetAsync(Guid)`, `FileService.DeleteAsync(Guid)`, `IFileStorage.OpenReadAsync`/`GetPresignedUrlAsync`; the existing `/api/items/file/{id}` PUT for `status`.
- Produces: `GET /api/files/{id}` → 200 metadata when `published`, else 404; `GET /api/files/{id}/content` → 200 stream / 302 presigned when `published`, else 404; `DELETE /api/files/{id}` → 204.

- [ ] **Step 1: Write the failing test** — `tests/Struo.Tests/Files/FileDownloadTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class FileDownloadTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> UploadDraft(System.Net.Http.HttpClient c, string body = "data")
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var mp = new MultipartFormDataContent { { content, "file", "f.txt" } };
        var resp = await c.PostAsync("/api/files", mp);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task Publish(System.Net.Http.HttpClient c, string id) =>
        await c.PutAsJsonAsync($"/api/items/file/{id}", new
        {
            status = "published",
            translations = new Dictionary<string, object> { ["en"] = new { title = "T", alt = (string?)null } }
        });

    [Fact]
    public async Task Draft_download_is_404()
    {
        var c = _factory.CreateClient();
        var id = await UploadDraft(c);
        (await c.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Published_download_streams_bytes()
    {
        var c = _factory.CreateClient();
        var id = await UploadDraft(c, "payload");
        await Publish(c, id);
        var resp = await c.GetAsync($"/api/files/{id}/content");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Be("payload");
    }

    [Fact]
    public async Task Published_info_returns_metadata()
    {
        var c = _factory.CreateClient();
        var id = await UploadDraft(c);
        await Publish(c, id);
        var resp = await c.GetAsync($"/api/files/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("status").GetString()
            .Should().Be("published");
    }

    [Fact]
    public async Task Delete_removes_file_then_download_404()
    {
        var c = _factory.CreateClient();
        var id = await UploadDraft(c);
        await Publish(c, id);
        (await c.DeleteAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test --filter "FullyQualifiedName~FileDownloadTests"`. Expected: FAIL.

- [ ] **Step 3: Add the endpoints + update the constructor** in `FilesController`:
```csharp
using Struo.Application.Files;   // add
// ...
public sealed class FilesController(FileService files, IFileStorage storage, FileStorageOptions options) : ControllerBase
{
    // (existing Upload) ...

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await files.GetAsync(id, ct);
        if (row is null || row.Status != "published") return NotFound();
        return Ok(new { data = new { id = row.Id, fileName = row.FileName, contentType = row.ContentType,
                                     size = row.Size, width = row.Width, height = row.Height, status = row.Status } });
    }

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var row = await files.GetAsync(id, ct);
        if (row is null || row.Status != "published") return NotFound();

        var presigned = await storage.GetPresignedUrlAsync(
            row.StorageKey, TimeSpan.FromSeconds(options.S3.PresignTtlSeconds), ct);
        if (presigned is not null) return Redirect(presigned);   // 302 (S3/MinIO)

        var stream = await storage.OpenReadAsync(row.StorageKey, ct);
        return File(stream, row.ContentType, fileDownloadName: row.FileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await files.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
```

- [ ] **Step 4: Run the new tests + full suite** — `dotnet test --filter "FullyQualifiedName~FileDownloadTests"` (PASS), then `dotnet test` (green).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Controllers/FilesController.cs tests/Struo.Tests/Files/FileDownloadTests.cs
git commit -m "feat: file info/download endpoints with status gate + delete (bytes+translations)"
```

---

### Task 7: File translations (title/alt) round-trip

**Goal:** Verify `title`/`alt` set via the Phase-4 `translations` envelope on `/api/items/file` persist to `FileTranslation` (Guid FK via Task 1) and overlay on read (all-locales + `?locale=`). No new production code expected — fix any gap this exposes.

**Files:**
- Test: `tests/Struo.Tests/Files/FileTranslationTests.cs` (new)
- Modify (only if the test exposes a gap): `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` / `src/Struo.Application/Query/ItemService.cs`

**Interfaces:**
- Consumes: Phase-4 write-sync/read-overlay; Task-1 Guid coercion in `SyncTranslationsGenericAsync`.

- [ ] **Step 1: Write the test** — `tests/Struo.Tests/Files/FileTranslationTests.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class FileTranslationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> Upload(System.Net.Http.HttpClient c)
    {
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var mp = new MultipartFormDataContent { { content, "file", "a.bin" } };
        var resp = await c.PostAsync("/api/files", mp);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Title_alt_translations_round_trip_all_locales()
    {
        var c = _factory.CreateClient();
        var id = await Upload(c);
        await c.PutAsJsonAsync($"/api/items/file/{id}", new
        {
            status = "published",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "Hello", alt = "An image" },
                ["zh-TW"] = new { title = "你好", alt = "圖片" }
            }
        });

        var data = Root(await (await c.GetAsync($"/api/items/file/{id}")).Content.ReadAsStringAsync()).GetProperty("data");
        var tr = data.GetProperty("translations");
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("Hello");
        tr.GetProperty("zh-TW").GetProperty("alt").GetString().Should().Be("圖片");
        data.TryGetProperty("title", out _).Should().BeFalse(); // title not top-level
    }

    [Fact]
    public async Task Single_locale_filter_on_file_translations()
    {
        var c = _factory.CreateClient();
        var id = await Upload(c);
        await c.PutAsJsonAsync($"/api/items/file/{id}", new
        {
            status = "published",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "EnOnly", alt = (string?)null },
                ["zh-TW"] = new { title = "只中", alt = (string?)null }
            }
        });
        var tr = Root(await (await c.GetAsync($"/api/items/file/{id}?locale=zh-TW")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("translations");
        tr.TryGetProperty("zh-TW", out _).Should().BeTrue();
        tr.TryGetProperty("en", out _).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run** — `dotnet test --filter "FullyQualifiedName~FileTranslationTests"`. Expected: PASS (Task 1 made the Guid translation-FK coercion work). If it fails on a Guid coercion path, fix the offending `Convert.ChangeType` to use `IdCoercion.Coerce` and re-run.

> NOTE: a `file` row is created by upload; here `PUT /api/items/file/{id}` only sets `status` + `translations`. System fields stay immutable (they are `ReadOnly` and stripped by `ItemService.Deserialize`). Do NOT create files via `POST /api/items/file` (no bytes).

- [ ] **Step 3: Run full suite** — `dotnet test`. Expected: green.

- [ ] **Step 4: Commit**

```bash
git add tests/Struo.Tests/Files/FileTranslationTests.cs
git commit -m "test: file title/alt translation round-trip (Guid sidecar FK)"
```

---

### Task 8: Reference wiring — single-image relation + ordered gallery M2M + RelationInterface

**Goal:** Demonstrate file references via Phase-3a relations on the blog sample: convert `SeoOgImageId` into a single-image relation to `File`, and add an ordered `Gallery` M2M. Add `RelationInterface` UI-hint values.

**Files:**
- Modify: `src/Struo.Domain/Metadata/Enums/RelationInterface.cs` (add `FilePicker, ImagePicker, FilesPicker`)
- Modify: `src/Struo.Domain/Seo/ISeoMeta.cs` (`SeoOgImageId`: `long?` → `Guid?`)
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs` (`BuildSeoFields`: `seoOgImageId` interface `Number` → `Hidden`)
- Modify: `samples/Struo.Sample.Blog/Article.cs` (type change `SeoOgImageId` → `Guid?`; add `SeoOgImage` nav + `Gallery` M2M nav)
- Create: `samples/Struo.Sample.Blog/ArticleFile.cs` (gallery junction)
- Modify: `src/Struo.Api/Program.cs` (`InitTables` += `typeof(ArticleFile)`)
- Test: `tests/Struo.Tests/Files/FileReferenceTests.cs` (new)

**Interfaces:**
- Consumes: Phase-3a relation expander; `FileAsset` (Guid); Task-1 Guid M2M coercion.
- Produces: `article` rows can carry gallery references that expand `FileAsset` metadata; `seoOgImageId` accepts a `Guid`.

- [ ] **Step 1: Write the failing test** — `tests/Struo.Tests/Files/FileReferenceTests.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class FileReferenceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> UploadFile(System.Net.Http.HttpClient c)
    {
        var content = new ByteArrayContent([9, 9, 9]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var mp = new MultipartFormDataContent { { content, "file", "img.bin" } };
        var resp = await c.PostAsync("/api/files", mp);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task<long> NewAuthor(System.Net.Http.HttpClient c) =>
        Root(await (await c.PostAsJsonAsync("/api/items/author", new { name = "RefA" })).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

    [Fact]
    public async Task Gallery_m2m_with_guid_file_ids_round_trips()
    {
        var c = _factory.CreateClient();
        var f1 = await UploadFile(c);
        var f2 = await UploadFile(c);
        var author = await NewAuthor(c);

        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft", authorId = author,
            translations = new Dictionary<string, object> { ["en"] = new { title = "A", body = (string?)null } },
            gallery = new[] { f1, f2 }
        });
        var id = Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

        var data = Root(await (await c.GetAsync($"/api/items/article/{id}?deep=gallery")).Content.ReadAsStringAsync())
            .GetProperty("data");
        data.GetProperty("gallery").EnumerateArray().Select(g => g.GetProperty("id").GetString())
            .Should().BeEquivalentTo(new[] { f1, f2 });
    }
}
```
> Verify the deep-expansion query-string form (`?deep=gallery`) against an existing Phase-3a test (e.g. `DeepExpansionTests`) and match its exact syntax; adjust if the project uses a different deep-spec format.

- [ ] **Step 2: Run to verify it fails** — `dotnet test --filter "FullyQualifiedName~FileReferenceTests"`. Expected: FAIL.

- [ ] **Step 3: Add `RelationInterface` values** — `src/Struo.Domain/Metadata/Enums/RelationInterface.cs`:
```csharp
public enum RelationInterface { Dropdown, TagSelect, TreeSelect, RelatedList, FilePicker, ImagePicker, FilesPicker }
```

- [ ] **Step 4: Change `ISeoMeta.SeoOgImageId`** — `src/Struo.Domain/Seo/ISeoMeta.cs`:
```csharp
    Guid? SeoOgImageId { get; set; }   // FK to the File collection (Phase 5)
```
And update `Article.SeoOgImageId` to `Guid?` (`samples/Struo.Sample.Blog/Article.cs:34`).

- [ ] **Step 5: Update `BuildSeoFields`** — `src/Struo.Infrastructure/Metadata/MetadataScanner.cs:251-255`:
```csharp
        yield return new FieldMetadata
        {
            Name = "seoOgImageId", Label = "OG Image", Interface = FieldInterface.Hidden,
            Hidden = true, Group = "SEO", Sort = 902
        };
```

- [ ] **Step 6: Wire the sample relations.**

`samples/Struo.Sample.Blog/ArticleFile.cs`:
```csharp
using SqlSugar;

namespace Struo.Sample.Blog;

[SugarTable("article_files")]
public sealed class ArticleFile
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public long ArticleId { get; set; }
    public Guid FileId { get; set; }
    public int SortOrder { get; set; }
}
```

In `samples/Struo.Sample.Blog/Article.cs`, add after the existing relations:
```csharp
    // single-image relation behind the SEO OG image FK
    [Navigate(NavigateType.OneToOne, nameof(SeoOgImageId))]
    [CmsRelation(Interface = RelationInterface.ImagePicker, OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public Struo.Infrastructure.Files.FileAsset? SeoOgImage { get; set; }

    // ordered many-to-many gallery
    [Navigate(typeof(ArticleFile), nameof(ArticleFile.ArticleId), nameof(ArticleFile.FileId))]
    [CmsRelation(Interface = RelationInterface.FilesPicker, SortField = nameof(ArticleFile.SortOrder))]
    [SugarColumn(IsIgnore = true)]
    public List<Struo.Infrastructure.Files.FileAsset> Gallery { get; set; } = [];
```
> DEPENDENCY CHECK: `samples/Struo.Sample.Blog` must reference `Struo.Infrastructure` to use `FileAsset`. Inspect `samples/Struo.Sample.Blog/Struo.Sample.Blog.csproj`. The Global Constraint forbids Infrastructure→samples; samples→Infrastructure is allowed. If the sample only references `Struo.Domain`, add a `ProjectReference` to `Struo.Infrastructure` (intentional, and acceptable for a sample). If a project-reference addition feels wrong to the reviewer, STOP and surface it before proceeding.

- [ ] **Step 7: Init the junction table** — `src/Struo.Api/Program.cs` `InitTables`, add `typeof(ArticleFile)`.

- [ ] **Step 8: Run the new test + full suite** — `dotnet test --filter "FullyQualifiedName~FileReferenceTests"` (PASS), then `dotnet test` (green). If any existing test sets `seoOgImageId` to a number, update it to a `Guid`/string form.

- [ ] **Step 9: Commit**

```bash
git add src/Struo.Domain/Metadata/Enums/RelationInterface.cs src/Struo.Domain/Seo/ISeoMeta.cs src/Struo.Infrastructure/Metadata/MetadataScanner.cs samples/Struo.Sample.Blog/Article.cs samples/Struo.Sample.Blog/ArticleFile.cs src/Struo.Api/Program.cs tests/Struo.Tests/Files/FileReferenceTests.cs
git commit -m "feat: file references via Phase-3a relations (SEO image + gallery M2M); RelationInterface pickers"
```

---

### Task 9: `S3FileStorage` (MinIO) + env-gated integration tests

**Goal:** S3-compatible backend (AWS SDK against MinIO), with presigned-URL download. Integration tests run only when MinIO connection info is provided via environment, so the default SQLite suite stays green.

**Files:**
- Modify: `Directory.Packages.props` + `src/Struo.Infrastructure/Struo.Infrastructure.csproj` (add `AWSSDK.S3`)
- Create: `src/Struo.Infrastructure/Files/S3FileStorage.cs`
- Modify: `src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs` (uncomment the s3 branch)
- Test: `tests/Struo.Tests/Files/S3FileStorageTests.cs` (new, env-gated)

**Interfaces:**
- Produces: `S3FileStorage : IFileStorage` with `SupportsPresignedUrls = true`; `GetPresignedUrlAsync` returns a time-limited GET URL.

- [ ] **Step 1: Add the package** — `dotnet add src/Struo.Infrastructure package AWSSDK.S3`. Verify CPM placement.

- [ ] **Step 2: Write the env-gated test** — `tests/Struo.Tests/Files/S3FileStorageTests.cs`:

```csharp
using System.Text;
using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class S3FileStorageTests
{
    private static FileStorageOptions? FromEnv()
    {
        var ep = Environment.GetEnvironmentVariable("STRUO_S3_ENDPOINT");
        var bucket = Environment.GetEnvironmentVariable("STRUO_S3_BUCKET");
        var access = Environment.GetEnvironmentVariable("STRUO_S3_ACCESS");
        var secret = Environment.GetEnvironmentVariable("STRUO_S3_SECRET");
        if (string.IsNullOrEmpty(ep) || string.IsNullOrEmpty(bucket) ||
            string.IsNullOrEmpty(access) || string.IsNullOrEmpty(secret)) return null;
        return new FileStorageOptions
        {
            Backend = "s3",
            S3 = new() { Endpoint = ep, Bucket = bucket, AccessKey = access, SecretKey = secret, ForcePathStyle = true }
        };
    }

    [Fact]
    public async Task Save_read_delete_and_presign()
    {
        var opts = FromEnv();
        if (opts is null) return; // skip when MinIO env not configured
        var s = new S3FileStorage(opts);
        var key = "it/" + Guid.NewGuid().ToString("N") + ".txt";

        await s.SaveAsync(key, new MemoryStream(Encoding.UTF8.GetBytes("s3-hello")));
        await using (var read = await s.OpenReadAsync(key))
        using (var sr = new StreamReader(read))
            (await sr.ReadToEndAsync()).Should().Be("s3-hello");

        s.SupportsPresignedUrls.Should().BeTrue();
        (await s.GetPresignedUrlAsync(key, TimeSpan.FromMinutes(5)))!.Should().Contain(key);

        await s.DeleteAsync(key);
    }
}
```

- [ ] **Step 3: Implement `S3FileStorage`** — `src/Struo.Infrastructure/Files/S3FileStorage.cs`:

```csharp
using Amazon.S3;
using Amazon.S3.Model;
using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;

    public S3FileStorage(FileStorageOptions options)
    {
        var s3 = options.S3;
        _bucket = s3.Bucket!;
        _client = new AmazonS3Client(
            s3.AccessKey, s3.SecretKey,
            new AmazonS3Config
            {
                ServiceURL = s3.Endpoint,
                ForcePathStyle = s3.ForcePathStyle,
                AuthenticationRegion = s3.Region,
            });
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken ct = default) =>
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket, Key = key, InputStream = content, AutoCloseStream = false
        }, ct);

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var resp = await _client.GetObjectAsync(_bucket, key, ct);
        return resp.ResponseStream;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await _client.DeleteObjectAsync(_bucket, key, ct);

    public bool SupportsPresignedUrls => true;

    public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<string?>(_client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket, Key = key, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.Add(ttl)
        }));

    public void Dispose() => _client.Dispose();
}
```

- [ ] **Step 4: Uncomment the s3 branch** in `FileStorageServiceCollectionExtensions`:
```csharp
if (string.Equals(options.Backend, "s3", StringComparison.OrdinalIgnoreCase))
    services.AddSingleton<IFileStorage, S3FileStorage>();
else
    services.AddSingleton<IFileStorage, LocalFileStorage>();
```

- [ ] **Step 5: Build + run** — `dotnet test --filter "FullyQualifiedName~S3FileStorageTests"` (PASS — early-returns without env). Then `dotnet test` (full suite green).

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props src/Struo.Infrastructure/Struo.Infrastructure.csproj src/Struo.Infrastructure/Files/S3FileStorage.cs src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs tests/Struo.Tests/Files/S3FileStorageTests.cs
git commit -m "feat: S3FileStorage (MinIO via ForcePathStyle) with presigned download"
```

---

### Task 10: Live MinIO + PostgreSQL verification gate

**Goal:** Exercise files end-to-end against real PostgreSQL + MinIO (§13). SQLite green is necessary but not sufficient — Postgres strict typing (uuid) and real S3 semantics catch what SQLite/local hide.

**Files:** none (verification). Optionally update `docs/guide/01-getting-started.md`.

- [ ] **Step 1: SQLite suite green** — `dotnet test`. Record the pass count.

- [ ] **Step 2: Start MinIO** (e.g. `docker run -p 9000:9000 -p 9001:9001 minio/minio server /data`), create the bucket, and run the S3 test with env set:
  `STRUO_S3_ENDPOINT=http://localhost:9000 STRUO_S3_BUCKET=struo STRUO_S3_ACCESS=… STRUO_S3_SECRET=… dotnet test --filter "FullyQualifiedName~S3FileStorageTests"`. Expected: PASS (real round-trip + presign).

- [ ] **Step 3: Start the API against dev PostgreSQL** with `Struo:Files:Backend=s3` (MinIO). Confirm clean start: `FileStorageOptions.Validate()` passes; `InitTables` creates `files` (uuid PK), `file_translations`, `article_files`. **If the DB has stale Blog tables, drop the affected ones** (notably `articles.seo_og_image_id` changes type long→uuid) so `InitTables` recreates them.

- [ ] **Step 4: Exercise over HTTP** (use PowerShell `Invoke-RestMethod` / a UTF-8 file for multibyte payloads — the Git Bash console here is Big5, per the project encoding lesson):
  - `POST /api/files` (multipart) → 201, `id` is a uuid, `status=draft`.
  - `GET /api/files/{id}/content` → 404 (draft).
  - `PUT /api/items/file/{id}` `{ "status":"published", "translations": { "en": {"title":"…","alt":"…"}, "zh-TW": {...} } }` → 200.
  - `GET /api/files/{id}` → 200 published + `translations` both locales.
  - `GET /api/files/{id}/content` → **302** to a MinIO presigned URL; following it downloads the bytes.
  - archive (`status=archived`) then `GET /api/files/{id}` → 404.
  - Multibyte filename upload → `fileName` round-trips correctly.
  - Create an `article` with `gallery: [uuid, uuid]` → `GET …?deep=gallery` expands `FileAsset` rows (uuid join works on Postgres).
  - `DELETE /api/files/{id}` → 204; object removed from MinIO; `file_translations` rows gone.

- [ ] **Step 5: Record results** in the PR/plan. Closes the §13 gate.

- [ ] **Step 6: Commit** (only if docs changed)

```bash
git add docs/guide/01-getting-started.md
git commit -m "docs: document files/media (upload, download, references) in the guide"
```

---

## Self-Review (controller, before execution)

- **Spec coverage:** `IFileStorage` + Local (T2) ✓; S3/MinIO + presign (T9) ✓; `FileStorageOptions` validation (T2) ✓; `FileAsset` Guid PK + metadata (T4) ✓; translatable title/alt sidecar (T4 model, T7 round-trip) ✓; image dimensions (T3) ✓; PK-type generalization (T1) ✓; upload (T5) ✓; download/info + status gate `published`-only / draft+archived 404 (T6) ✓; delete bytes+translations (T6) ✓; references via Phase-3a single + ordered M2M + RelationInterface (T8) ✓; `SeoOgImageId` placeholder conversion (T8) ✓; errors 400/404/500 + startup fail-fast (T2/T5/T6) ✓; live MinIO+PG gate (T10) ✓.
- **Sequencing:** T1 (Guid pipeline, suite stays green) → T2/T3 (storage + dims) → T4 (collection + sidecar + tables) → T5 (upload) → T6 (download/gate/delete) → T7 (translation proof) → T8 (references + sample) → T9 (S3) → T10 (live gate). Each task ends green and independently reviewable.
- **Type consistency:** `IFileStorage` signatures identical across T2/T6/T9; `FileService.UploadAsync/GetAsync/DeleteAsync(Guid)` consistent T5→T6; `FileTranslation.FileAssetId` matches the scanner convention `{ParentTypeName}Id` (T4); `IdCoercion.Coerce(object?, Type)` used uniformly (T1) at junction FK, translation FK, and create-flow.
- **Known verify-against-codebase points (flagged inline):** schema DTO exposes `translatable` (T4/T5); `samples/Struo.Sample.Blog` references `Struo.Infrastructure` for `FileAsset` (T8); deep-expansion query-string syntax `?deep=gallery` matches Phase-3a tests (T8); `FileService` lives in Infrastructure (not Application) so it may reference `FileAsset` (T5); existing tests that set `seoOgImageId` updated for the `Guid?` type (T8); `[SugarTable("file_translations")]` present on `FileTranslation` (T4).
