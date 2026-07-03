# Phase 7f — TipTap rich text + server-side HTML sanitization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the RichText `Textarea` fallback with a TipTap WYSIWYG editor (basic formatting + inline images from the media library) and add write-path HTML sanitization so stored RichText cannot carry XSS.

**Architecture:** Backend gains an `IHtmlSanitizer` port (Application) implemented by `GanssHtmlSanitizer` (Infrastructure, Ganss.Xss), applied inside `ItemService` on both the parent-entity and translation-sidecar write paths; blank RichText coerces to `null`. Frontend gains a `RichTextInput.vue` (TipTap) wired through the existing `FieldInput` dispatcher with an unchanged string field contract; inline images reuse the Phase 7e `MediaGrid` and store a base-independent relative `src` plus `data-file-id`.

**Tech Stack:** .NET 10 / C# (xUnit + AwesomeAssertions, SQLite test DB), Ganss.Xss; Vue 3 + TypeScript + PrimeVue, TipTap (`@tiptap/vue-3`, `@tiptap/starter-kit`, `@tiptap/extension-link`, `@tiptap/extension-image`), Vitest + `@vue/test-utils`.

## Global Constraints

- **Dependency rule (§2):** Domain → nothing · Application → Domain · Infrastructure → Application+Domain · Api → Application+Infrastructure. The sanitizer port lives in Application; the Ganss.Xss implementation lives in Infrastructure. No external package may be referenced from Application/Domain.
- **Package versions (§17.5):** NEVER hand-author versions. Install via `dotnet add package Ganss.Xss` (NuGet) and `pnpm add <pkg>` (frontend). NuGet versions are centralized in `Directory.Packages.props`.
- **RichText is server-side sanitized (§8).** Sanitize on write (store-clean).
- **Outbound JSON = camelCase.** Field names in payloads/tests are camelCase (`body`, `title`, `heroImageId`).
- **No `console.log` in frontend production code.** Use no logging in components.
- **All DB access via SqlSugar ORM; zero vendor SQL.**
- **Live-gate is user-driven** (real Postgres + Redis + MinIO). Automated gates (backend `dotnet test`, frontend `pnpm test` + `pnpm build`) must be green before handing the live-gate recipe to the user.

---

### Task 1: `IHtmlSanitizer` port + `GanssHtmlSanitizer` implementation + DI

**Files:**
- Create: `src/Struo.Application/Security/IHtmlSanitizer.cs`
- Create: `src/Struo.Infrastructure/Security/GanssHtmlSanitizer.cs`
- Modify: `src/Struo.Infrastructure/Struo.Infrastructure.csproj` (add `<PackageReference Include="Ganss.Xss" />`)
- Modify: `Directory.Packages.props` (the `<PackageVersion Include="Ganss.Xss" .../>` line the tool writes)
- Modify: `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs`
- Test: `tests/Struo.Tests/Security/GanssHtmlSanitizerTests.cs`

**Interfaces:**
- Produces: `Struo.Application.Security.IHtmlSanitizer` with `string Sanitize(string html)`.
- Produces: `Struo.Infrastructure.Security.GanssHtmlSanitizer : IHtmlSanitizer` (registered as a singleton; configured once in its constructor and never mutated afterward, which is the thread-safe usage for Ganss.Xss).

- [ ] **Step 1: Add the Ganss.Xss package**

Run (from repo root):
```bash
dotnet add src/Struo.Infrastructure/Struo.Infrastructure.csproj package Ganss.Xss
```
Expected: the tool adds a `PackageReference` to `Struo.Infrastructure.csproj` and, because central management is on, a `PackageVersion` entry to `Directory.Packages.props`. Do not edit the version by hand.

- [ ] **Step 2: Define the port**

Create `src/Struo.Application/Security/IHtmlSanitizer.cs`:
```csharp
namespace Struo.Application.Security;

/// <summary>
/// Sanitizes untrusted HTML (rich-text field values) before persistence, removing
/// scripts, event handlers, and unsafe URLs while preserving an allowlist of
/// formatting tags. Implementations MUST be safe to call concurrently.
/// </summary>
public interface IHtmlSanitizer
{
    /// <summary>Returns a cleaned copy of <paramref name="html"/> containing only allowlisted markup.</summary>
    string Sanitize(string html);
}
```

- [ ] **Step 3: Write the failing test**

Create `tests/Struo.Tests/Security/GanssHtmlSanitizerTests.cs`:
```csharp
using AwesomeAssertions;
using Struo.Infrastructure.Security;
using Xunit;

namespace Struo.Tests.Security;

public class GanssHtmlSanitizerTests
{
    private readonly GanssHtmlSanitizer _s = new();

    [Fact]
    public void Keeps_allowlisted_formatting_tags()
    {
        var html = "<h2>Title</h2><p><strong>bold</strong> <em>i</em> <s>x</s></p>"
                 + "<ul><li>a</li></ul><ol><li>b</li></ol><blockquote>q</blockquote>"
                 + "<pre><code>code</code></pre><hr>";
        var clean = _s.Sanitize(html);
        clean.Should().Contain("<h2>").And.Contain("<strong>").And.Contain("<em>")
             .And.Contain("<s>").And.Contain("<ul>").And.Contain("<ol>")
             .And.Contain("<blockquote>").And.Contain("<pre>").And.Contain("<code>").And.Contain("<hr");
    }

    [Fact]
    public void Strips_script_and_event_handlers()
    {
        var clean = _s.Sanitize("<p onclick=\"steal()\">hi</p><script>alert(1)</script>");
        clean.Should().NotContain("script").And.NotContain("onclick");
        clean.Should().Contain("hi");
    }

    [Fact]
    public void Strips_iframe_and_inline_style()
    {
        var clean = _s.Sanitize("<iframe src=\"http://evil\"></iframe><p style=\"color:red\">t</p>");
        clean.Should().NotContain("iframe").And.NotContain("style=");
        clean.Should().Contain("t");
    }

    [Theory]
    [InlineData("<a href=\"http://ok\">l</a>", true)]
    [InlineData("<a href=\"https://ok\">l</a>", true)]
    [InlineData("<a href=\"mailto:a@b.c\">l</a>", true)]
    [InlineData("<a href=\"javascript:alert(1)\">l</a>", false)]
    public void Enforces_anchor_scheme_allowlist(string html, bool keepsHref)
    {
        var clean = _s.Sanitize(html);
        if (keepsHref) clean.Should().Contain("href=");
        else clean.Should().NotContain("javascript");
    }

    [Fact]
    public void Keeps_relative_image_with_data_file_id_but_drops_unsafe_src()
    {
        var ok = _s.Sanitize("<img src=\"/api/files/abc/content\" data-file-id=\"abc\" alt=\"x\">");
        ok.Should().Contain("src=\"/api/files/abc/content\"").And.Contain("data-file-id=\"abc\"").And.Contain("alt=\"x\"");

        var bad = _s.Sanitize("<img src=\"javascript:alert(1)\"><img src=\"data:text/html;base64,PHN2Zz4=\">");
        bad.Should().NotContain("javascript").And.NotContain("data:");
    }

    [Fact]
    public void Adds_rel_noopener_to_anchors()
    {
        var clean = _s.Sanitize("<a href=\"https://ok\">l</a>");
        clean.Should().Contain("rel=").And.Contain("noopener");
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GanssHtmlSanitizerTests"`
Expected: FAIL — `GanssHtmlSanitizer` does not exist (compile error).

- [ ] **Step 5: Implement `GanssHtmlSanitizer`**

Create `src/Struo.Infrastructure/Security/GanssHtmlSanitizer.cs`:
```csharp
using Ganss.Xss;
using Struo.Application.Security;

namespace Struo.Infrastructure.Security;

/// <summary>
/// Ganss.Xss-backed <see cref="IHtmlSanitizer"/>. The allowlist is configured once in the
/// constructor (tags/attributes/schemes) and never mutated afterward, so a single instance is
/// safe to share across requests. The allowlist mirrors the TipTap editor output (Phase 7f):
/// basic formatting + anchors (http/https/mailto) + relative-src images carrying data-file-id.
/// </summary>
public sealed class GanssHtmlSanitizer : IHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public GanssHtmlSanitizer()
    {
        _sanitizer = new HtmlSanitizer();

        _sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 { "p", "h2", "h3", "strong", "em", "s", "ul", "ol", "li",
                   "blockquote", "pre", "code", "hr", "br", "a", "img" })
            _sanitizer.AllowedTags.Add(tag);

        _sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "src", "alt", "rel" })
            _sanitizer.AllowedAttributes.Add(attr);

        // data-file-id: allow data-* attributes (inert; carry no script surface).
        _sanitizer.AllowDataAttributes = true;

        _sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" })
            _sanitizer.AllowedSchemes.Add(scheme);

        // Drop inline styles and CSS entirely.
        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedAtRules.Clear();

        // Harden every surviving anchor.
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement a)
            {
                a.SetAttribute("rel", "noopener noreferrer");
                a.RemoveAttribute("target");
            }
        };
    }

    public string Sanitize(string html) => _sanitizer.Sanitize(html);
}
```
Note: `AngleSharp` types are transitively available via Ganss.Xss. If the `IHtmlAnchorElement` namespace resolves differently for the installed version, use `e.Node is AngleSharp.Dom.IElement el && el.TagName == "A"` and `el.SetAttribute(...)` instead — verify against the restored package.

- [ ] **Step 6: Run the sanitizer test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GanssHtmlSanitizerTests"`
Expected: PASS (all facts/theories).

- [ ] **Step 7: Register the service in DI**

Modify `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs` — add the using and one registration line inside `AddStruoData`, before `services.AddScoped<ItemService>();`:
```csharp
// (top, with the other usings)
using Struo.Infrastructure.Security;
```
```csharp
// (inside AddStruoData, before AddScoped<ItemService>())
services.AddSingleton<IHtmlSanitizer, GanssHtmlSanitizer>();
```

- [ ] **Step 8: Build to confirm wiring compiles**

Run: `dotnet build src/Struo.Infrastructure`
Expected: build succeeds (warnings-as-errors clean).

- [ ] **Step 9: Commit**

```bash
git add src/Struo.Application/Security/IHtmlSanitizer.cs \
        src/Struo.Infrastructure/Security/GanssHtmlSanitizer.cs \
        src/Struo.Infrastructure/Struo.Infrastructure.csproj \
        Directory.Packages.props \
        src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs \
        tests/Struo.Tests/Security/GanssHtmlSanitizerTests.cs
git commit -m "feat(security): add IHtmlSanitizer port + Ganss.Xss impl with RichText allowlist"
```

---

### Task 2: Apply sanitization in `ItemService` (both write paths) + blank→null + required-empty

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs`
- Modify: `tests/Struo.Tests/Query/ItemServiceTests.cs:51` (constructor call)
- Modify: `tests/Struo.Tests/Query/ItemServicePermissionTests.cs:78` (constructor call)
- Modify: `tests/Struo.Tests/Query/DeleteRestrictWithGuidPkTests.cs:143` (constructor call)
- Test: `tests/Struo.Tests/Query/ItemServiceRichTextSanitizationTests.cs`

**Interfaces:**
- Consumes: `Struo.Application.Security.IHtmlSanitizer` (Task 1). The sample `ArticleTranslation.Body` (`Interface = RichText`, translatable) is the primary path under test.
- Produces: `ItemService` constructor gains a **final** parameter `IHtmlSanitizer sanitizer`. Every `new ItemService(...)` call site must pass it.

- [ ] **Step 1: Write the failing test**

Create `tests/Struo.Tests/Query/ItemServiceRichTextSanitizationTests.cs`:
```csharp
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class ItemServiceRichTextSanitizationTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceRichTextSanitizationTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<Article>();
        db.CodeFirst.InitTables<ArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Tag>();
        db.CodeFirst.InitTables<ArticleTag>();
        db.CodeFirst.InitTables<Category>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category),
            ["tag"] = typeof(Tag), ["file"] = typeof(Struo.Infrastructure.Files.File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph);
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer());
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Create_strips_script_from_translatable_richtext_body()
    {
        using var body = System.Text.Json.JsonDocument.Parse(
            """{"status":"draft","translations":{"en":{"title":"T","body":"<p>ok</p><script>alert(1)</script><p onclick=\"x()\">y</p>"}}}""");
        var created = await _svc.CreateAsync("article", body.RootElement);
        var id = created["id"]!.ToString()!;

        var reloaded = await _svc.GetAsync("article", id, locale: "en");
        var translations = (IReadOnlyDictionary<string, object?>)reloaded!["translations"]!;
        var en = (IReadOnlyDictionary<string, object?>)translations["en"]!;
        var stored = en["body"] as string;
        stored.Should().NotBeNull();
        stored!.Should().Contain("ok").And.NotContain("script").And.NotContain("onclick");
    }

    [Fact]
    public async Task Create_coerces_blank_richtext_to_null()
    {
        using var body = System.Text.Json.JsonDocument.Parse(
            """{"status":"draft","translations":{"en":{"title":"T","body":"<p></p>"}}}""");
        var created = await _svc.CreateAsync("article", body.RootElement);
        var id = created["id"]!.ToString()!;

        var reloaded = await _svc.GetAsync("article", id, locale: "en");
        var translations = (IReadOnlyDictionary<string, object?>)reloaded!["translations"]!;
        var en = (IReadOnlyDictionary<string, object?>)translations["en"]!;
        en["body"].Should().BeNull();
    }
}
```
(Body is optional on the sample, so a blank body is legal; Title remains the required translatable field, so blank-required behaviour is enforced by the existing required-field loop once RichText sanitizes to null.)

- [ ] **Step 2: Update the three existing constructor call sites**

Each must pass a sanitizer as the final argument. Use `new GanssHtmlSanitizer()` and add `using Struo.Infrastructure.Security;` if not already present.

`tests/Struo.Tests/Query/ItemServiceTests.cs:51` — change:
```csharp
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions());
```
to:
```csharp
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer());
```
(`Struo.Infrastructure.Security` is already imported in this file.)

`tests/Struo.Tests/Query/ItemServicePermissionTests.cs:78` — append `, new GanssHtmlSanitizer()` as the final argument of the `return new ItemService(...)` call, and add `using Struo.Infrastructure.Security;` to the usings if missing.

`tests/Struo.Tests/Query/DeleteRestrictWithGuidPkTests.cs:143` — append `, new GanssHtmlSanitizer()` as the final argument of the `new ItemService(...)` call, and add `using Struo.Infrastructure.Security;` to the usings if missing.

- [ ] **Step 3: Run the new test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceRichTextSanitizationTests"`
Expected: FAIL — `ItemService` has no 11-arg constructor (compile error).

- [ ] **Step 4: Add the constructor parameter and a shared sanitize helper**

Modify `src/Struo.Application/Query/ItemService.cs`. Add `IHtmlSanitizer sanitizer` as the final primary-constructor parameter:
```csharp
public sealed class ItemService(
    IItemRepository repository,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IPermissionService permissions,
    IRelationshipGraph graph,
    IRelationExpander expander,
    IM2MDescriptorSource m2mSource,
    IRelationFilterResolver relationFilter,
    ILanguageProvider languages,
    StruoQueryOptions options,
    IHtmlSanitizer sanitizer)
{
```
Add the `using` at the top if not present (the file already has `using Struo.Application.Security;`). Add these private helpers (place near `JsonValue`):
```csharp
/// <summary>
/// Sanitizes a RichText field value: null stays null; otherwise the HTML is run through the
/// sanitizer and, if the cleaned result is visually blank (no text and no void media), coerced
/// to null so blank editor documents (<c>&lt;p&gt;&lt;/p&gt;</c>) do not create dirty rows and
/// so a required RichText field treats blank as missing.
/// </summary>
private string? SanitizeRichText(string? raw)
{
    if (raw is null) return null;
    var clean = sanitizer.Sanitize(raw);
    return IsBlankHtml(clean) ? null : clean;
}

private static bool IsBlankHtml(string html)
{
    if (string.IsNullOrWhiteSpace(html)) return true;
    // Void/media content counts as non-blank.
    if (html.Contains("<img", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("<hr", StringComparison.OrdinalIgnoreCase)) return false;
    // Strip tags and non-breaking spaces; blank if nothing meaningful remains.
    var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", string.Empty)
        .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);
    return string.IsNullOrWhiteSpace(text);
}

private static bool IsRichTextField(CollectionMetadata meta, string fieldName) =>
    meta.Fields.Any(f =>
        string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase) &&
        f.Interface == FieldInterface.RichText);
```

- [ ] **Step 5: Apply sanitization on the translatable path (before the required check)**

In `SyncTranslationsAsync`, the per-field loop currently does `fieldValues[field.Name] = JsonValue(field.Value);`. Replace that loop body so RichText values are sanitized before the required-field validation that follows it:
```csharp
            var fieldValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in localeProp.Value.EnumerateObject())
            {
                if (!allowed.Contains(field.Name))
                    throw new QueryException(
                        $"Field '{field.Name}' is not a translatable field of '{meta.Name}'.");
                var value = JsonValue(field.Value);
                if (value is string s && IsRichTextField(meta, field.Name))
                    value = SanitizeRichText(s);
                fieldValues[field.Name] = value;
            }
```
(The existing required-field loop treats `null` and whitespace strings as missing, so a blank body sanitized to `null` is correctly rejected when the field is required.)

- [ ] **Step 6: Apply sanitization on the non-translatable path (before the required check)**

In `Deserialize`, after the system/read-only stripping loop and **before** the `f.Required && !f.Translatable` validation loop, insert:
```csharp
        // Sanitize non-translatable RichText field values on the entity before required validation,
        // so stored HTML is XSS-clean and a blank editor document is treated as missing.
        foreach (var field in meta.Fields.Where(f => f.Interface == FieldInterface.RichText && !f.Translatable))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true } || pi.PropertyType != typeof(string)) continue;
            if (pi.GetValue(entity) is string raw)
                pi.SetValue(entity, SanitizeRichText(raw));
        }
```

- [ ] **Step 7: Run the sanitization tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceRichTextSanitizationTests"`
Expected: PASS (both facts).

- [ ] **Step 8: Run the full backend suite (no regressions)**

Run: `dotnet test tests/Struo.Tests`
Expected: PASS — all tests green (prior 272 + the new sanitizer/sanitization tests). Confirm 0 failed / 0 skipped.

- [ ] **Step 9: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs \
        tests/Struo.Tests/Query/ItemServiceRichTextSanitizationTests.cs \
        tests/Struo.Tests/Query/ItemServiceTests.cs \
        tests/Struo.Tests/Query/ItemServicePermissionTests.cs \
        tests/Struo.Tests/Query/DeleteRestrictWithGuidPkTests.cs
git commit -m "feat(items): sanitize RichText on write (both paths) + blank->null"
```

---

### Task 3: Inline-image URL helpers (base-independent storage ⇄ display)

**Files:**
- Create: `frontend/src/lib/richTextImages.ts`
- Test: `frontend/src/lib/richTextImages.test.ts`

**Interfaces:**
- Produces:
  - `fileContentPath(id: string): string` → `"/api/files/{id}/content"` (the stored, base-independent form).
  - `fileContentDisplayUrl(id: string): string` → `` `${API_BASE}/files/{id}/content` `` (absolute-for-preview; `API_BASE = import.meta.env.VITE_API_BASE_URL || '/api'`).
  - `absolutizeImageSrc(html: string): string` → rewrites every `<img data-file-id>`'s `src` to the display URL (for loading into the editor).
  - `relativizeImageSrc(html: string): string` → rewrites every `<img data-file-id>`'s `src` back to `fileContentPath(id)` (for emitting/storing).
- Consumed by: Task 5 (`RichTextInput.vue`).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/richTextImages.test.ts`:
```typescript
import { describe, it, expect } from 'vitest'
import {
  fileContentPath, fileContentDisplayUrl, absolutizeImageSrc, relativizeImageSrc,
} from './richTextImages'

describe('richTextImages', () => {
  it('builds the stored relative path', () => {
    expect(fileContentPath('abc')).toBe('/api/files/abc/content')
  })

  it('builds a display url from the api base', () => {
    // default base is '/api' in tests
    expect(fileContentDisplayUrl('abc')).toBe('/api/files/abc/content')
  })

  it('absolutizes img src from data-file-id', () => {
    const html = '<p>x</p><img src="/api/files/abc/content" data-file-id="abc" alt="a">'
    const out = absolutizeImageSrc(html)
    expect(out).toContain('data-file-id="abc"')
    expect(out).toContain(`src="${fileContentDisplayUrl('abc')}"`)
  })

  it('relativizes img src back to the stored path', () => {
    const html = `<img src="${fileContentDisplayUrl('abc')}" data-file-id="abc" alt="a">`
    const out = relativizeImageSrc(html)
    expect(out).toContain('src="/api/files/abc/content"')
  })

  it('leaves images without data-file-id untouched', () => {
    const html = '<img src="http://x/y.png" alt="a">'
    expect(relativizeImageSrc(html)).toContain('src="http://x/y.png"')
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `frontend/`): `pnpm test richTextImages`
Expected: FAIL — module `./richTextImages` not found.

- [ ] **Step 3: Implement the helpers**

Create `frontend/src/lib/richTextImages.ts`:
```typescript
const API_BASE = import.meta.env.VITE_API_BASE_URL || '/api'

/** Stored, base-independent content path for a file id. */
export function fileContentPath(id: string): string {
  return `/api/files/${id}/content`
}

/** Absolute (or app-relative) URL used to preview a file in the editor. */
export function fileContentDisplayUrl(id: string): string {
  return `${API_BASE}/files/${id}/content`
}

function rewriteImgSrc(html: string, srcFor: (id: string) => string): string {
  if (!html) return html
  const doc = new DOMParser().parseFromString(html, 'text/html')
  doc.querySelectorAll('img[data-file-id]').forEach((img) => {
    const id = img.getAttribute('data-file-id')
    if (id) img.setAttribute('src', srcFor(id))
  })
  return doc.body.innerHTML
}

/** For loading stored HTML into the editor: point each managed img at its display URL. */
export function absolutizeImageSrc(html: string): string {
  return rewriteImgSrc(html, fileContentDisplayUrl)
}

/** For emitting/storing: point each managed img back at its base-independent path. */
export function relativizeImageSrc(html: string): string {
  return rewriteImgSrc(html, fileContentPath)
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `frontend/`): `pnpm test richTextImages`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/richTextImages.ts frontend/src/lib/richTextImages.test.ts
git commit -m "feat(frontend): base-independent rich-text image url helpers"
```

---

### Task 4: `RichTextInput.vue` core editor + wire into `FieldInput`

**Files:**
- Modify: `frontend/package.json` (via `pnpm add`)
- Create: `frontend/src/components/fields/RichTextInput.vue`
- Create: `frontend/src/components/fields/RichTextInput.test.ts`
- Modify: `frontend/src/components/fields/FieldInput.vue`
- Modify: `frontend/src/components/fields/FieldInput.test.ts`

**Interfaces:**
- Produces: `RichTextInput.vue` — props `{ modelValue: string; disabled?: boolean }`, emits `update:modelValue` (HTML string). Exposes (via `defineExpose`) `{ editor }` now and `{ editor, insertImage }` after Task 5.
- Consumes: TipTap packages.

- [ ] **Step 1: Install TipTap packages**

Run (from `frontend/`):
```bash
pnpm add @tiptap/vue-3 @tiptap/starter-kit @tiptap/extension-link @tiptap/extension-image
```
Expected: `package.json` gains the four deps at pnpm-resolved versions; `pnpm-lock.yaml` updates.

- [ ] **Step 2: Write the failing component test**

Create `frontend/src/components/fields/RichTextInput.test.ts`:
```typescript
import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import RichTextInput from './RichTextInput.vue'

describe('RichTextInput', () => {
  it('renders initial HTML content', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>hello</p>' } })
    await flushPromises()
    expect(w.get('.rich-text__content').html()).toContain('hello')
  })

  it('emits update:modelValue as HTML when content changes', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' } })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { commands: { setContent: (h: string) => void } } }
    vm.editor.commands.setContent('<p>b</p>')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    expect(emitted).toBeTruthy()
    expect(String(emitted!.at(-1)![0])).toContain('b')
  })

  it('is not editable when disabled', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>', disabled: true } })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { isEditable: boolean } }
    expect(vm.editor.isEditable).toBe(false)
  })

  it('toggles bold via the toolbar', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' } })
    await flushPromises()
    const btn = w.get('[data-cmd="bold"]')
    await btn.trigger('click')
    const vm = w.vm as unknown as { editor: { isActive: (n: string) => boolean } }
    expect(vm.editor.isActive('bold')).toBe(true)
  })
})
```

- [ ] **Step 3: Run the test to verify it fails**

Run (from `frontend/`): `pnpm test RichTextInput`
Expected: FAIL — component `./RichTextInput.vue` does not exist.

- [ ] **Step 4: Implement `RichTextInput.vue` (no images yet)**

Create `frontend/src/components/fields/RichTextInput.vue`:
```vue
<script setup lang="ts">
import { watch, onBeforeUnmount } from 'vue'
import { useEditor, EditorContent } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'

defineOptions({ name: 'RichTextInput' })

const props = defineProps<{ modelValue: string; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const editor = useEditor({
  content: props.modelValue || '',
  editable: !props.disabled,
  extensions: [
    StarterKit.configure({ heading: { levels: [2, 3] } }),
    Link.configure({ openOnClick: false, protocols: ['http', 'https', 'mailto'], autolink: false }),
    Image.configure({ inline: false }),
  ],
  onUpdate: ({ editor }) => emit('update:modelValue', editor.getHTML()),
})

// Keep the editor in sync with external model changes without clobbering the cursor.
watch(() => props.modelValue, (val) => {
  const current = editor.value?.getHTML()
  if (editor.value && val !== current) editor.value.commands.setContent(val || '', false)
})
watch(() => props.disabled, (d) => editor.value?.setEditable(!d))

onBeforeUnmount(() => editor.value?.destroy())

type Level = 2 | 3

function setLink(): void {
  if (!editor.value) return
  const prev = editor.value.getAttributes('link').href as string | undefined
  const url = window.prompt('Link URL', prev ?? 'https://')
  if (url === null) return
  if (url === '') { editor.value.chain().focus().unsetLink().run(); return }
  editor.value.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
}

defineExpose({ editor })
</script>

<template>
  <div class="rich-text">
    <div v-if="editor" class="rich-text__toolbar">
      <button type="button" data-cmd="bold" :class="{ active: editor.isActive('bold') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleBold().run()"><b>B</b></button>
      <button type="button" data-cmd="italic" :class="{ active: editor.isActive('italic') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleItalic().run()"><i>I</i></button>
      <button type="button" data-cmd="strike" :class="{ active: editor.isActive('strike') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleStrike().run()"><s>S</s></button>
      <button v-for="lvl in ([2, 3] as Level[])" :key="lvl" type="button" :data-cmd="`h${lvl}`"
        :class="{ active: editor.isActive('heading', { level: lvl }) }" :disabled="disabled"
        @click="editor!.chain().focus().toggleHeading({ level: lvl }).run()">H{{ lvl }}</button>
      <button type="button" data-cmd="bulletList" :class="{ active: editor.isActive('bulletList') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleBulletList().run()">• List</button>
      <button type="button" data-cmd="orderedList" :class="{ active: editor.isActive('orderedList') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleOrderedList().run()">1. List</button>
      <button type="button" data-cmd="blockquote" :class="{ active: editor.isActive('blockquote') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleBlockquote().run()">❝</button>
      <button type="button" data-cmd="codeBlock" :class="{ active: editor.isActive('codeBlock') }"
        :disabled="disabled" @click="editor!.chain().focus().toggleCodeBlock().run()">{ }</button>
      <button type="button" data-cmd="link" :class="{ active: editor.isActive('link') }"
        :disabled="disabled" @click="setLink">🔗</button>
      <button type="button" data-cmd="hr" :disabled="disabled"
        @click="editor!.chain().focus().setHorizontalRule().run()">―</button>
      <button type="button" data-cmd="undo" :disabled="disabled"
        @click="editor!.chain().focus().undo().run()">↶</button>
      <button type="button" data-cmd="redo" :disabled="disabled"
        @click="editor!.chain().focus().redo().run()">↷</button>
    </div>
    <EditorContent class="rich-text__content" :editor="editor" />
  </div>
</template>

<style scoped>
.rich-text { border: 1px solid var(--surface-border, #d0d0d0); border-radius: 6px; }
.rich-text__toolbar { display: flex; flex-wrap: wrap; gap: 4px; padding: 6px; border-bottom: 1px solid var(--surface-border, #d0d0d0); }
.rich-text__toolbar button { min-width: 30px; padding: 2px 6px; cursor: pointer; background: transparent; border: 1px solid transparent; border-radius: 4px; }
.rich-text__toolbar button.active { background: var(--primary-color, #6366f1); color: #fff; }
.rich-text__toolbar button:disabled { opacity: 0.5; cursor: not-allowed; }
.rich-text__content { padding: 10px; min-height: 8rem; }
.rich-text__content :deep(.ProseMirror) { outline: none; min-height: 6rem; }
</style>
```

- [ ] **Step 5: Run the component test to verify it passes**

Run (from `frontend/`): `pnpm test RichTextInput`
Expected: PASS (4 tests). The tests exercise editor commands/state (not layout), which jsdom supports.

- [ ] **Step 6: Wire `RichTextInput` into `FieldInput`**

Modify `frontend/src/components/fields/FieldInput.vue`:
- Add the import near the other field imports:
```typescript
import RichTextInput from './RichTextInput.vue'
```
- Split the combined textarea/richtext branch. Replace:
```html
  <Textarea v-else-if="kind === 'textarea' || kind === 'richtext'" :model-value="(modelValue as string)"
    :disabled="isDisabled" :rows="6" @update:model-value="update" />
```
with:
```html
  <Textarea v-else-if="kind === 'textarea'" :model-value="(modelValue as string)"
    :disabled="isDisabled" :rows="6" @update:model-value="update" />

  <RichTextInput v-else-if="kind === 'richtext'" :model-value="((modelValue as string) ?? '')"
    :disabled="isDisabled" @update:model-value="(v: string) => update(v)" />
```

- [ ] **Step 7: Update the `FieldInput` fallback test**

Modify `frontend/src/components/fields/FieldInput.test.ts` — the `renders Textarea for richText (fallback)` test now must assert `RichTextInput` is used instead. Replace that test with:
```typescript
  it('renders RichTextInput for richText', () => {
    const w = mount(FieldInput, {
      props: { field: field({ interface: 'richText' }), modelValue: '' },
      global: { stubs: { ...stubs, RichTextInput: { template: '<div class="stub-richtext" />' } } },
    })
    expect(w.find('.stub-richtext').exists()).toBe(true)
    expect(w.find('.stub-textarea').exists()).toBe(false)
  })
```

- [ ] **Step 8: Run the field tests to verify they pass**

Run (from `frontend/`): `pnpm test FieldInput RichTextInput`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add frontend/package.json frontend/pnpm-lock.yaml \
        frontend/src/components/fields/RichTextInput.vue \
        frontend/src/components/fields/RichTextInput.test.ts \
        frontend/src/components/fields/FieldInput.vue \
        frontend/src/components/fields/FieldInput.test.ts
git commit -m "feat(frontend): TipTap RichTextInput editor + wire into FieldInput"
```

---

### Task 5: Inline images via the media library

**Files:**
- Modify: `frontend/src/components/fields/RichTextInput.vue`
- Modify: `frontend/src/components/fields/RichTextInput.test.ts`

**Interfaces:**
- Consumes: `richTextImages` helpers (Task 3), `MediaGrid.vue` + `itemsApi.list('file', …)` (Phase 7e), `useLanguageStore`.
- Produces: an "insert image" toolbar button that opens a `MediaGrid` dialog; selecting a file inserts `<img src="/api/files/{id}/content" data-file-id="{id}" alt="">`. Editor load absolutizes managed img src for preview; emitted HTML is relativized before `update:modelValue`. Exposes `insertImage(id, alt?)`.

- [ ] **Step 1: Write the failing test**

Add to `frontend/src/components/fields/RichTextInput.test.ts` (add the import at the top of the file and the test inside the `describe`):
```typescript
import { fileContentPath } from '../../lib/richTextImages'
```
```typescript
  it('inserts a managed image with relative src + data-file-id', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p>a</p>' },
      global: { stubs: { Dialog: true, Button: true, MediaGrid: true } },
    })
    await flushPromises()
    const vm = w.vm as unknown as { insertImage: (id: string, alt?: string) => void }
    vm.insertImage('abc', 'cat')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    const html = String(emitted!.at(-1)![0])
    expect(html).toContain('data-file-id="abc"')
    expect(html).toContain(`src="${fileContentPath('abc')}"`)
  })
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `frontend/`): `pnpm test RichTextInput`
Expected: FAIL — `insertImage` is not exposed.

- [ ] **Step 3: Add image insertion + dialog to `RichTextInput.vue`**

In `<script setup>`, add imports and state (after the existing imports):
```typescript
import { ref } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import MediaGrid from '../media/MediaGrid.vue'
import type { FileRow } from '../media/FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import { fileContentPath, fileContentDisplayUrl, absolutizeImageSrc, relativizeImageSrc } from '../../lib/richTextImages'

const langStore = useLanguageStore()
const imageDialogOpen = ref(false)
const files = ref<FileRow[]>([])
const imageSearch = ref('')
const imageError = ref('')

async function loadImages(): Promise<void> {
  imageError.value = ''
  try {
    const res = await itemsApi.list('file', {
      page: 0, rows: 50, search: imageSearch.value || undefined,
      locale: langStore.defaultCode || undefined,
    })
    files.value = res.data as unknown as FileRow[]
  } catch (e) {
    imageError.value = e instanceof Error ? e.message : 'Failed to load files.'
  }
}

async function openImageDialog(): Promise<void> {
  imageDialogOpen.value = true
  await loadImages()
}

// Re-derive data-file-id from managed src, then relativize before emitting.
function withFileIds(html: string): string {
  if (!html) return html
  const doc = new DOMParser().parseFromString(html, 'text/html')
  doc.querySelectorAll('img').forEach((img) => {
    if (img.getAttribute('data-file-id')) return
    const m = (img.getAttribute('src') || '').match(/\/files\/([^/]+)\/content/)
    if (m) img.setAttribute('data-file-id', m[1])
  })
  return doc.body.innerHTML
}

function emitNormalized(): void {
  const html = editor.value?.getHTML() ?? ''
  emit('update:modelValue', relativizeImageSrc(withFileIds(html)))
}

function insertImage(id: string, alt = ''): void {
  if (!editor.value) return
  editor.value.chain().focus().setImage({ src: fileContentDisplayUrl(id), alt }).run()
  emitNormalized()
}

function onImageSelected(id: string): void {
  insertImage(id)
  imageDialogOpen.value = false
}
```
Change the editor definition so it loads absolutized content and emits normalized HTML:
```typescript
const editor = useEditor({
  content: absolutizeImageSrc(props.modelValue || ''),
  editable: !props.disabled,
  extensions: [
    StarterKit.configure({ heading: { levels: [2, 3] } }),
    Link.configure({ openOnClick: false, protocols: ['http', 'https', 'mailto'], autolink: false }),
    Image.configure({ inline: false }),
  ],
  onUpdate: () => emitNormalized(),
})
```
Update the external-model watcher to compare/relativize correctly and absolutize incoming HTML:
```typescript
watch(() => props.modelValue, (val) => {
  const current = relativizeImageSrc(withFileIds(editor.value?.getHTML() ?? ''))
  if (editor.value && (val || '') !== current) {
    editor.value.commands.setContent(absolutizeImageSrc(val || ''), false)
  }
})
```
Add the toolbar button (after the `hr` button):
```html
      <button type="button" data-cmd="image" :disabled="disabled" @click="openImageDialog">🖼️</button>
```
Add the dialog (after `<EditorContent .../>`, still inside the root `.rich-text` div):
```html
    <Dialog v-model:visible="imageDialogOpen" modal header="Insert image" :style="{ width: '60rem' }">
      <p v-if="imageError" class="error" role="alert">{{ imageError }}</p>
      <InputText v-model="imageSearch" placeholder="Search files…" class="rich-text__search" @update:model-value="loadImages" />
      <MediaGrid :files="files" selectable @select="onImageSelected" />
    </Dialog>
```
Update the expose:
```typescript
defineExpose({ editor, insertImage })
```
Add a style for the search box (append to `<style scoped>`):
```css
.rich-text__search { display: block; margin: 8px 0 12px; width: 100%; }
```

- [ ] **Step 4: Run the image test to verify it passes**

Run (from `frontend/`): `pnpm test RichTextInput`
Expected: PASS (all RichTextInput tests including image insertion).

- [ ] **Step 5: Run the full frontend suite + build**

Run (from `frontend/`): `pnpm test && pnpm build`
Expected: all unit/component tests pass; `pnpm build` (vue-tsc + vite) succeeds with no type errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/fields/RichTextInput.vue \
        frontend/src/components/fields/RichTextInput.test.ts
git commit -m "feat(frontend): insert media-library images into RichTextInput (relative src + data-file-id)"
```

---

### Task 6: Docs update + regression gate + live-gate recipe

**Files:**
- Modify: `docs/ROADMAP.md`

**Interfaces:**
- Consumes: everything above. No code interfaces.

- [ ] **Step 1: Full backend + frontend regression gate**

Run:
```bash
dotnet build && dotnet test tests/Struo.Tests
```
Expected: build clean (warnings-as-errors); all tests pass, 0 failed / 0 skipped. Record the count.
Run (from `frontend/`):
```bash
pnpm test && pnpm build
```
Expected: all pass; build succeeds. Record the count.

- [ ] **Step 2: Update the ROADMAP**

Modify `docs/ROADMAP.md`:
- In "Status at a glance", change the "Next up" line to point to 7g (advanced rich text) and add a Phase 7f summary line: TipTap editor (basic formatting + inline images) + `IHtmlSanitizer`/`GanssHtmlSanitizer` write-path sanitization; note automated gates green and **live-gate user-driven / pending**.
- In the Phases table, change the `7f+` row to a `7f` row marked "⬜ code-complete, live-gate pending" with links to the new spec (`superpowers/specs/2026-07-03-phase7f-richtext-tiptap-design.md`) and this plan (`superpowers/plans/2026-07-03-phase7f-richtext-tiptap.md`); add a fresh `7g+` row for the deferred advanced rich text + multi-value + structured + multi-file items.
- Update the "Verification baseline" with the recorded backend/frontend test counts from Step 1.

- [ ] **Step 3: Commit the docs**

```bash
git add docs/ROADMAP.md
git commit -m "docs: Phase 7f code-complete (TipTap + HTML sanitization); automated gates green, live-gate pending"
```

- [ ] **Step 4: Hand the live-gate recipe to the user**

The live-gate is user-driven (real Postgres + Redis + MinIO). Present this recipe for the user to run:
1. Start the API against live PG + Redis + MinIO and the frontend dev server (per `frontend/README.md`).
2. Log in as the bootstrap super-admin.
3. **Stored-XSS gate (API):** `POST /api/items/article` with `translations.en.body` = `"<p>ok</p><script>alert(1)</script><p onclick=\"x()\">y</p>"`; then `GET /api/items/article/{id}?locale=en` and confirm the stored body contains `ok` but no `script`/`onclick`.
4. **Image round-trip (UI):** in the article form, open the RichText editor → insert image → pick a media-library file; save; reload; confirm the stored body has `<img src="/api/files/{id}/content" data-file-id="{id}">` and the image previews (content endpoint 302).
5. **i18n round-trip:** author `en` + `zh-TW` bodies; save; reload; confirm both round-trip with correct UTF-8.
6. Report results; if the live gate surfaces a Postgres-only issue (per the "SQLite-green ≠ Postgres-correct" pattern), fix and re-run before marking 7f live-verified.

---

## Self-Review

**Spec coverage:**
- §3.1 backend sanitizer (port + Ganss impl + allowlist + DI) → Task 1. ✔
- §3.1 application at both write points + empty→null + required-empty → Task 2. ✔
- §3.2 TipTap editor (StarterKit levels [2,3], toolbar, model contract, disabled) → Task 4. ✔
- §3.3 inline images (MediaGrid select, relative src + data-file-id, preview absolutize) → Tasks 3 + 5. ✔
- §3.4 no new read-only render surface → respected (only the FieldInput form branch changes). ✔
- §5 security (scheme restriction, stored-XSS on write) → Tasks 1, 2, live-gate Task 6. ✔
- §6 tests + live-gate → Tasks 1–6. ✔
- §7 out of scope (advanced formatting, multi-value, etc.) → not implemented; ROADMAP 7g row (Task 6). ✔

**Placeholder scan:** no TBD/TODO; all code shown; commands have expected output. ✔

**Type consistency:** `IHtmlSanitizer.Sanitize(string): string` consistent across Tasks 1–2; `ItemService` 11th ctor param `IHtmlSanitizer sanitizer` consistent across Task 2 and all three test call sites; helpers `SanitizeRichText`/`IsBlankHtml`/`IsRichTextField` used only where defined; frontend helpers `fileContentPath`/`fileContentDisplayUrl`/`absolutizeImageSrc`/`relativizeImageSrc` consistent across Tasks 3 & 5; `RichTextInput` exposes `{ editor }` (Task 4) then `{ editor, insertImage }` (Task 5) as consumed by tests. ✔
