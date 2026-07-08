# Phase 8b.1 — GraphQL Mutations (backbone) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a strongly-typed GraphQL write surface (`createX` / `updateX` / `deleteX` per discovered `[CmsCollection]`, scalar own-fields + M2O foreign keys + optimistic `version`) layered on the Phase 8 read-only schema, delegating to the existing `ItemService` write path.

**Architecture:** A new mutation `ObjectTypeExtension` (mirroring the existing `BuildQueryExtension`) registers three root mutation fields per collection. Per collection, two `InputObjectType`s (`XCreateInput` / `XUpdateInput`) are generated from metadata. Resolvers convert the typed input dict to a `JsonElement` and call new write methods on the Api-owned `IGraphQlDataSource` seam, which delegate to `ItemService.CreateAsync/UpdateAsync/DeleteAsync`. Create/update re-read the row via `GetAsync` (using the mutation's selection set) so the returned node behaves exactly like a query result. Errors flow through the existing `StruoErrorFilter`; RBAC/CSRF/transactions/concurrency are inherited unchanged.

**Tech Stack:** .NET 10, C# latest, HotChocolate 16.4.0 (`HotChocolate.AspNetCore`, already installed), xUnit + AwesomeAssertions, SQLite for automated tests / PostgreSQL for the live gate.

## Global Constraints

- **No new packages.** HotChocolate is already installed; add nothing. (§2, §17.5 — no hand-authored version strings.)
- **Domain / Application / Infrastructure are untouched.** All new code lives in `src/Struo.Api/GraphQl/`. No interface added to Application; the Api-owned `IGraphQlDataSource` is the only seam. (Dependency rule §2.)
- **No backend write-logic change.** `ItemService.CreateAsync/UpdateAsync/DeleteAsync` already handle deserialization, validation, RBAC, sanitization, relation/M2M/translation sync, transactions, and optimistic concurrency. This slice only builds the typed GraphQL input surface and converts it to the `JsonElement` those methods already accept.
- **Outbound JSON = camelCase.** GraphQL field/input names reuse the projection's camelCase field names (`SchemaTypeMapper`).
- **TDD:** failing test first, then minimal implementation (§17.2).
- **Verification baseline:** backend `dotnet build -warnaserror` clean (0 warnings) + `dotnet test` all green — currently **449**; this slice adds tests on top. Frontend untouched (237, not exercised here).
- **Scope fence:** scalar own-fields + M2O FK + `version` only. M2M, File/Image/Files, MultiSelect/CheckboxGroup/Tags, Json, KeyValue, Repeater, and typed `translations` input are **deferred to 8b.2** and must NOT appear in the generated input types. Advanced read querying (cross-relation / deep filtering / multi-level nesting) is out of Phase 8b entirely (8c).
- **`PermissionDeniedException` → `FORBIDDEN`.** The shipped `StruoErrorFilter` maps it to `FORBIDDEN` unconditionally (no `UNAUTHENTICATED` split). Match the shipped behaviour in tests.

---

## File Structure

**New files (all under `src/Struo.Api/GraphQl/`):**
- `MutationResolvers.cs` — `create`/`update`/`delete` field configs + resolvers (mirrors `CollectionResolvers.cs`).
- `MutationInputMapper.cs` — typed input dict → `JsonElement` bridge (pure).

**Modified files:**
- `SchemaTypeMapper.cs` — add mutation naming helpers + the writable-scalar input SDL mapping.
- `CollectionSchemaBuilder.cs` — add `BuildCreateInput` / `BuildUpdateInput`, emit them from `Build`.
- `CollectionResolvers.cs` — make `SelectionRelations` `internal` so mutation resolvers reuse it.
- `StruoTypeModule.cs` — add `BuildMutationExtension`, emit it from `CreateTypesAsync`.
- `GraphQlDataSource.cs` — add `CreateAsync`/`UpdateAsync`/`DeleteAsync` to `IGraphQlDataSource` + `ItemServiceGraphQlDataSource`.
- `GraphQlServiceCollectionExtensions.cs` — add the `Mutation` root anchor (`AddMutationType`).

**Test files (under `tests/Struo.Tests/GraphQl/`):**
- `FakeGraphQlDataSource.cs` — add `OnCreate`/`OnUpdate`/`OnDelete` delegates + call logs (modified).
- `GraphQlSchemaTests.cs` — add `AddMutationType` anchor to the executor builder; add mutation schema-shape assertions (modified).
- `GraphQlExecutionTests.cs` — add `AddMutationType` anchor to both executor builders (modified).
- `MutationInputMapperTests.cs` — pure mapper unit tests (new).
- `GraphQlMutationSchemaTests.cs` — mutation SDL shape (new).
- `GraphQlMutationExecutionTests.cs` — create/update/delete execution + error codes (new).

**Live gate (doc):**
- `docs/superpowers/plans/2026-07-08-phase8b-graphql-mutations.md` (this file) records the live-gate script in Task 8; evidence appended to `docs/ROADMAP.md`.

---

## Task 1: `SchemaTypeMapper` mutation naming + writable-scalar input mapping

Pure additions to the single source of truth for metadata→SDL. No HotChocolate/DB dependency.

**Files:**
- Modify: `src/Struo.Api/GraphQl/SchemaTypeMapper.cs`
- Test: `tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs` (add a pure `[Fact]` in a new small test class file `tests/Struo.Tests/GraphQl/SchemaTypeMapperMutationTests.cs`)

**Interfaces:**
- Produces:
  - `SchemaTypeMapper.CreateFieldName(string) : string` (e.g. `"createArticle"`)
  - `SchemaTypeMapper.UpdateFieldName(string) : string` (`"updateArticle"`)
  - `SchemaTypeMapper.DeleteFieldName(string) : string` (`"deleteArticle"`)
  - `SchemaTypeMapper.CreateInputName(string) : string` (`"ArticleCreateInput"`)
  - `SchemaTypeMapper.UpdateInputName(string) : string` (`"ArticleUpdateInput"`)
  - `SchemaTypeMapper.WritableScalarInputSdl(FieldInterface, Type?) : string?` — SDL for an included writable scalar interface, else `null`.

- [ ] **Step 1: Write the failing test**

Create `tests/Struo.Tests/GraphQl/SchemaTypeMapperMutationTests.cs`:

```csharp
// tests/Struo.Tests/GraphQl/SchemaTypeMapperMutationTests.cs
using AwesomeAssertions;
using Struo.Api.GraphQl;
using Struo.Domain.Metadata.Enums;
using Xunit;

namespace Struo.Tests.GraphQl;

public class SchemaTypeMapperMutationTests
{
    [Fact]
    public void Mutation_field_and_input_names_follow_convention()
    {
        SchemaTypeMapper.CreateFieldName("article").Should().Be("createArticle");
        SchemaTypeMapper.UpdateFieldName("article").Should().Be("updateArticle");
        SchemaTypeMapper.DeleteFieldName("article").Should().Be("deleteArticle");
        SchemaTypeMapper.CreateInputName("article").Should().Be("ArticleCreateInput");
        SchemaTypeMapper.UpdateInputName("article").Should().Be("ArticleUpdateInput");
    }

    [Theory]
    [InlineData(FieldInterface.Text, "String")]
    [InlineData(FieldInterface.Select, "String")]
    [InlineData(FieldInterface.Boolean, "Boolean")]
    [InlineData(FieldInterface.Date, "Date")]
    [InlineData(FieldInterface.DateTime, "DateTime")]
    [InlineData(FieldInterface.Uuid, "ID")]
    public void Writable_scalar_interfaces_map_to_sdl(FieldInterface iface, string expected)
    {
        SchemaTypeMapper.WritableScalarInputSdl(iface, null).Should().Be(expected);
    }

    [Fact]
    public void Number_maps_by_clr_type()
    {
        SchemaTypeMapper.WritableScalarInputSdl(FieldInterface.Number, typeof(int)).Should().Be("Int");
        SchemaTypeMapper.WritableScalarInputSdl(FieldInterface.Number, typeof(long)).Should().Be("Long");
        SchemaTypeMapper.WritableScalarInputSdl(FieldInterface.Number, typeof(decimal)).Should().Be("Float");
    }

    [Theory]
    [InlineData(FieldInterface.MultiSelect)]
    [InlineData(FieldInterface.CheckboxGroup)]
    [InlineData(FieldInterface.Tags)]
    [InlineData(FieldInterface.Json)]
    [InlineData(FieldInterface.KeyValue)]
    [InlineData(FieldInterface.File)]
    [InlineData(FieldInterface.Image)]
    [InlineData(FieldInterface.Files)]
    [InlineData(FieldInterface.Repeater)]
    [InlineData(FieldInterface.Hidden)]
    [InlineData(FieldInterface.Divider)]
    [InlineData(FieldInterface.Password)]
    public void Deferred_and_excluded_interfaces_are_not_writable_scalars(FieldInterface iface)
    {
        SchemaTypeMapper.WritableScalarInputSdl(iface, null).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~SchemaTypeMapperMutationTests`
Expected: FAIL — `CreateFieldName` / `WritableScalarInputSdl` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

In `src/Struo.Api/GraphQl/SchemaTypeMapper.cs`, add these members (place the naming helpers next to `RepeaterItemTypeName`, and `WritableScalarInputSdl` next to `ScalarSdl`; `NumberSdl` is already `private` in this class and is reused):

```csharp
    public static string CreateFieldName(string collection) => "create" + Pascal(collection);
    public static string UpdateFieldName(string collection) => "update" + Pascal(collection);
    public static string DeleteFieldName(string collection) => "delete" + Pascal(collection);
    public static string CreateInputName(string collection) => Pascal(collection) + "CreateInput";
    public static string UpdateInputName(string collection) => Pascal(collection) + "UpdateInput";

    /// <summary>
    /// SDL for a writable scalar own-field in a create/update input, or <c>null</c> for interfaces
    /// deferred to Phase 8b.2 (MultiSelect/CheckboxGroup/Tags/Json/KeyValue/File/Image/Files/Repeater)
    /// or excluded entirely (Hidden/Divider/Password). This is the Phase 8b.1 writable subset — a
    /// deliberately narrower set than the read-side <see cref="ScalarSdl"/>.
    /// </summary>
    public static string? WritableScalarInputSdl(FieldInterface iface, Type? clrType) => iface switch
    {
        FieldInterface.Text or FieldInterface.Textarea or FieldInterface.RichText or FieldInterface.Markdown
            or FieldInterface.Code or FieldInterface.Slug or FieldInterface.Email or FieldInterface.Url
            or FieldInterface.Color or FieldInterface.Phone or FieldInterface.Select or FieldInterface.Radio
            or FieldInterface.Time => "String",
        FieldInterface.Number or FieldInterface.Slider or FieldInterface.Rating => NumberSdl(clrType),
        FieldInterface.Boolean or FieldInterface.Checkbox => "Boolean",
        FieldInterface.Date => "Date",
        FieldInterface.DateTime => "DateTime",
        FieldInterface.Uuid => "ID",
        _ => null,
    };
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~SchemaTypeMapperMutationTests`
Expected: PASS (all facts green).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl/SchemaTypeMapper.cs tests/Struo.Tests/GraphQl/SchemaTypeMapperMutationTests.cs
git commit -m "feat(graphql): mutation naming + writable-scalar input SDL mapping"
```

---

## Task 2: `MutationInputMapper` — typed input dict → `JsonElement`

Pure bridge from the HotChocolate input dictionary to the `JsonElement` `ItemService` accepts.

**Files:**
- Create: `src/Struo.Api/GraphQl/MutationInputMapper.cs`
- Test: `tests/Struo.Tests/GraphQl/MutationInputMapperTests.cs`

**Interfaces:**
- Produces: `MutationInputMapper.ToJsonElement(IReadOnlyDictionary<string, object?>?) : JsonElement`
  - Serializes with `JsonSerializerDefaults.Web`. A `null` input → an empty JSON object (`{}`). Only keys present in the dict appear in the element (this is what makes partial-update work).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/GraphQl/MutationInputMapperTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Api.GraphQl;
using Xunit;

namespace Struo.Tests.GraphQl;

public class MutationInputMapperTests
{
    [Fact]
    public void Null_input_produces_empty_object()
    {
        var el = MutationInputMapper.ToJsonElement(null);
        el.ValueKind.Should().Be(JsonValueKind.Object);
        el.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void Only_present_keys_appear()
    {
        var el = MutationInputMapper.ToJsonElement(new Dictionary<string, object?>
        {
            ["status"] = "published",
            ["categoryId"] = "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        });

        el.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["status", "categoryId"]);
        el.GetProperty("status").GetString().Should().Be("published");
        el.GetProperty("categoryId").GetString().Should().Be("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    }

    [Fact]
    public void Long_and_bool_serialize_as_json_primitives()
    {
        var el = MutationInputMapper.ToJsonElement(new Dictionary<string, object?>
        {
            ["version"] = 7L,
            ["active"] = true,
        });

        el.GetProperty("version").GetInt64().Should().Be(7);
        el.GetProperty("active").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Explicit_null_value_is_preserved_as_json_null()
    {
        var el = MutationInputMapper.ToJsonElement(new Dictionary<string, object?> { ["categoryId"] = null });
        el.TryGetProperty("categoryId", out var v).Should().BeTrue();
        v.ValueKind.Should().Be(JsonValueKind.Null);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~MutationInputMapperTests`
Expected: FAIL — `MutationInputMapper` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Struo.Api/GraphQl/MutationInputMapper.cs
using System.Text.Json;

namespace Struo.Api.GraphQl;

/// <summary>
/// Converts the HotChocolate typed-input dictionary (keys = camelCase input field names, values =
/// CLR scalars produced by the scalar bindings) into the <see cref="JsonElement"/> that
/// <c>ItemService.CreateAsync</c>/<c>UpdateAsync</c> already accept. Uses the same web JSON options
/// as ItemService so number/date/string shapes match the REST write path. Because the dictionary
/// contains ONLY the keys the client supplied, the resulting element carries exactly the sent fields
/// — which is what drives ItemService's partial merge-update.
/// </summary>
public static class MutationInputMapper
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    public static JsonElement ToJsonElement(IReadOnlyDictionary<string, object?>? input)
        => JsonSerializer.SerializeToElement(
            input ?? new Dictionary<string, object?>(), Opts);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~MutationInputMapperTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl/MutationInputMapper.cs tests/Struo.Tests/GraphQl/MutationInputMapperTests.cs
git commit -m "feat(graphql): typed-input dict -> JsonElement bridge (MutationInputMapper)"
```

---

## Task 3: Delete mutation end-to-end (proves the mutation-extension plumbing)

The simplest operation: no input type. Adds `DeleteAsync` to the data-source seam, the mutation type extension, the `Mutation` root anchor, and the delete resolver. Updates existing test executors to declare the `Mutation` root (otherwise the new extension has no base type to extend and every GraphQL executor test fails).

**Files:**
- Modify: `src/Struo.Api/GraphQl/GraphQlDataSource.cs` (add `DeleteAsync`)
- Modify: `src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs` (add `AddMutationType` anchor)
- Create: `src/Struo.Api/GraphQl/MutationResolvers.cs` (delete field + resolver)
- Modify: `src/Struo.Api/GraphQl/StruoTypeModule.cs` (add `BuildMutationExtension`, emit it)
- Modify: `tests/Struo.Tests/GraphQl/FakeGraphQlDataSource.cs` (add `OnDelete` + `DeleteAsync`)
- Modify: `tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs` (add `AddMutationType` anchor to `BuildSdlAsync`)
- Modify: `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs` (add `AddMutationType` anchor to both executor builders)
- Create: `tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs` (delete tests; extended in Tasks 4-5)

**Interfaces:**
- Consumes: `IGraphQlDataSource` (Task 0 existing).
- Produces:
  - `IGraphQlDataSource.DeleteAsync(string collection, string id, CancellationToken ct) : Task<bool>`
  - `MutationResolvers.DeleteField(string collection) : ObjectFieldConfiguration`
  - `StruoTypeModule.BuildMutationExtension(IEnumerable<string>) : ObjectTypeExtension` (private; emits `Mutation`)
  - The `Mutation` root type with `_service: String` anchor + `deleteX(id: ID!): Boolean` per collection.

- [ ] **Step 1: Write the failing test**

Create `tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs`:

```csharp
// tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.GraphQl;

public class GraphQlMutationExecutionTests
{
    // Mirrors GraphQlExecutionTests.ExecutorAsync but adds the Mutation root anchor and registers
    // the error filter (mutations must surface domain-exception codes).
    private static async Task<IRequestExecutor> ExecutorAsync(FakeGraphQlDataSource ds)
    {
        var services = new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddScoped<IGraphQlDataSource>(_ => ds)
            .AddSingleton(new StruoQueryOptions())
            .AddSingleton<StruoTypeModule>()
            .AddLogging();
        services.AddErrorFilter<StruoErrorFilter>();

        return await services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddMutationType(d => d.Name("Mutation").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            .BuildRequestExecutorAsync();
    }

    private static JsonElement ParseData(IExecutionResult result)
    {
        var json = result.ToJson();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("errors", out var errors))
            throw new InvalidOperationException($"GraphQL errors: {errors}\n{json}");
        return doc.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Delete_returns_true_when_found()
    {
        string? seenCollection = null, seenId = null;
        var ds = new FakeGraphQlDataSource
        {
            OnDelete = (c, id) => { seenCollection = c; seenId = id; return true; }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { deleteArticle(id: \"7\") }");
        var data = ParseData(result);

        data.GetProperty("deleteArticle").GetBoolean().Should().BeTrue();
        seenCollection.Should().Be("article");
        seenId.Should().Be("7");
    }

    [Fact]
    public async Task Delete_returns_false_when_not_found()
    {
        var ds = new FakeGraphQlDataSource { OnDelete = (_, _) => false };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { deleteArticle(id: \"missing\") }");
        var data = ParseData(result);

        data.GetProperty("deleteArticle").GetBoolean().Should().BeFalse();
    }
}
```

Also add the `OnDelete` delegate to the fake — in `tests/Struo.Tests/GraphQl/FakeGraphQlDataSource.cs` add:

```csharp
    public List<string> DeletedCollections { get; } = [];
    public Func<string, string, bool> OnDelete { get; set; } = (_, _) => false;

    public Task<bool> DeleteAsync(string collection, string id, CancellationToken ct)
    {
        DeletedCollections.Add(collection);
        return Task.FromResult(OnDelete(collection, id));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~GraphQlMutationExecutionTests`
Expected: FAIL — `IGraphQlDataSource` has no `DeleteAsync` (the fake won't compile), and `deleteArticle`/`Mutation` do not exist.

- [ ] **Step 3: Write minimal implementation**

(a) `src/Struo.Api/GraphQl/GraphQlDataSource.cs` — add to the interface and the impl:

```csharp
public interface IGraphQlDataSource
{
    Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, CancellationToken ct);
    Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct);
    Task<bool> DeleteAsync(string collection, string id, CancellationToken ct);
}

public sealed class ItemServiceGraphQlDataSource(ItemService items) : IGraphQlDataSource
{
    public Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, CancellationToken ct)
        => items.QueryAsync(collection, query, locale, ct);

    public Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct)
        => items.GetAsync(collection, id, deep, locale, ct);

    public Task<bool> DeleteAsync(string collection, string id, CancellationToken ct)
        => items.DeleteAsync(collection, id, ct);
}
```

(b) Create `src/Struo.Api/GraphQl/MutationResolvers.cs`:

```csharp
// src/Struo.Api/GraphQl/MutationResolvers.cs
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace Struo.Api.GraphQl;

/// <summary>
/// Root mutation field configs + resolvers, one create/update/delete per collection. Mirrors
/// <see cref="CollectionResolvers"/> (queries). Resolvers convert the typed input to a JsonElement
/// and delegate to <see cref="IGraphQlDataSource"/> (which wraps ItemService); create/update re-read
/// the row so the returned node matches a query result.
/// </summary>
internal static class MutationResolvers
{
    internal static ObjectFieldConfiguration DeleteField(string collection)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.DeleteFieldName(collection), null,
            TypeReference.Parse("Boolean"),
            resolver: ctx => ResolveDelete(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        return config;
    }

    private static async ValueTask<object?> ResolveDelete(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        return await ctx.Service<IGraphQlDataSource>().DeleteAsync(collection, id, ctx.RequestAborted);
    }
}
```

(c) `src/Struo.Api/GraphQl/StruoTypeModule.cs` — emit a mutation extension. Add to `CreateTypesAsync`, right after the query extension line:

```csharp
        types.Add(BuildQueryExtension(collections.Select(c => c.Name)));
        types.Add(BuildMutationExtension(collections.Select(c => c.Name)));
```

and add the method next to `BuildQueryExtension`:

```csharp
    private ObjectTypeExtension BuildMutationExtension(IEnumerable<string> collectionNames)
    {
        var config = new ObjectTypeConfiguration("Mutation");
        foreach (var name in collectionNames)
            config.Fields.Add(MutationResolvers.DeleteField(name));
        return ObjectTypeExtension.CreateUnsafe(config);
    }
```

(d) `src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs` — declare the `Mutation` root, right after `.AddQueryType(...)`:

```csharp
            .AddMutationType(d => d
                .Name("Mutation")
                // Anchor field so the Mutation root is valid even before the type module adds
                // collection mutations (GraphQL requires each root type to have >=1 field). Also
                // serves as the empty-schema guard when zero collections are discovered.
                .Field("_service").Type<StringType>().Resolve(_ => "StruoCMS GraphQL mutations"))
```

(e) `tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs` — add the anchor to `BuildSdlAsync`'s builder chain, right after `.AddQueryType(...)`:

```csharp
            .AddMutationType(d => d.Name("Mutation").Field("_service").Type<StringType>().Resolve(_ => "x"))
```

(f) `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs` — add the same `.AddMutationType(...)` line after `.AddQueryType(...)` in **both** `ExecutorAsync` and the inline builder inside `PermissionDenied_surfaces_as_FORBIDDEN_code`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~GraphQl`
Expected: PASS — the new delete tests pass and all pre-existing GraphQL schema/execution tests still pass (they now declare the Mutation root).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl tests/Struo.Tests/GraphQl
git commit -m "feat(graphql): delete mutation + Mutation root plumbing (type extension, anchor, data-source seam)"
```

---

## Task 4: Create mutation (`XCreateInput` + `createX` + re-read)

**Files:**
- Modify: `src/Struo.Api/GraphQl/GraphQlDataSource.cs` (add `CreateAsync`)
- Modify: `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs` (add `BuildCreateInput`, emit from `Build`)
- Modify: `src/Struo.Api/GraphQl/CollectionResolvers.cs` (make `SelectionRelations` `internal`)
- Modify: `src/Struo.Api/GraphQl/MutationResolvers.cs` (add `CreateField` + `ResolveCreate`)
- Modify: `src/Struo.Api/GraphQl/StruoTypeModule.cs` (add `CreateField` to the mutation extension)
- Modify: `tests/Struo.Tests/GraphQl/FakeGraphQlDataSource.cs` (add `OnCreate` + `CreateAsync`)
- Create: `tests/Struo.Tests/GraphQl/GraphQlMutationSchemaTests.cs` (create-input SDL shape)
- Modify: `tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs` (create execution + error tests)

**Interfaces:**
- Consumes: `MutationInputMapper.ToJsonElement` (Task 2); `SchemaTypeMapper.CreateInputName`/`CreateFieldName`/`WritableScalarInputSdl` (Task 1); `CollectionResolvers.SelectionRelations(IResolverContext, string, bool)` (made internal here); `GraphQlQueryBuilder.BuildQuery` (existing).
- Produces:
  - `IGraphQlDataSource.CreateAsync(string collection, JsonElement body, CancellationToken ct) : Task<IReadOnlyDictionary<string, object?>>`
  - `CollectionSchemaBuilder.BuildCreateInput(CollectionMetadata) : InputObjectType` (private; emitted from `Build`)
  - `MutationResolvers.CreateField(string collection, IMetadataProvider metadata) : ObjectFieldConfiguration`
  - The `ArticleCreateInput` type (writable scalar own-fields + M2O FK, all nullable) + `createArticle(input: ArticleCreateInput!, locale: String): Article`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Struo.Tests/GraphQl/GraphQlMutationSchemaTests.cs`:

```csharp
// tests/Struo.Tests/GraphQl/GraphQlMutationSchemaTests.cs
using AwesomeAssertions;
using HotChocolate.Serialization;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Struo.Application.Metadata;
using Xunit;

namespace Struo.Tests.GraphQl;

public class GraphQlMutationSchemaTests
{
    private static async Task<string> BuildSdlAsync()
    {
        var services = new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddScoped<IGraphQlDataSource, FakeGraphQlDataSource>()
            .AddSingleton<StruoTypeModule>();

        var executor = await services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddMutationType(d => d.Name("Mutation").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            .BuildRequestExecutorAsync();

        return SchemaFormatter.FormatAsString(executor.Schema);
    }

    [Fact]
    public async Task Mutation_has_create_field_per_collection()
    {
        var sdl = await BuildSdlAsync();
        sdl.Should().Contain("createArticle(");
        sdl.Should().Contain("createCategory(");
        sdl.Should().Contain("input ArticleCreateInput");
    }

    [Fact]
    public async Task Create_input_has_writable_scalars_and_m2o_fk_only()
    {
        var sdl = await BuildSdlAsync();
        var block = InputBlock(sdl, "ArticleCreateInput");

        block.Should().Contain("status: String");        // Select -> String
        block.Should().Contain("publishedAt: DateTime");  // DateTime
        block.Should().Contain("categoryId: ID");         // M2O FK

        // Deferred kinds must NOT appear in the 8b.1 create input:
        block.Should().NotContain("regions");   // MultiSelect
        block.Should().NotContain("keywords");  // Tags
        block.Should().NotContain("attributes");// Json
        block.Should().NotContain("gallery");   // Files
        block.Should().NotContain("faqs");      // Repeater
        block.Should().NotContain("heroImageId"); // Image
        // Title is translatable and not on the entity's FieldToProperty -> excluded here (8b.2).
        block.Should().NotContain("version");   // create carries no concurrency token
    }

    // Returns the SDL text of a single `input X { ... }` block.
    private static string InputBlock(string sdl, string typeName)
    {
        var start = sdl.IndexOf("input " + typeName, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"schema should declare input {typeName}");
        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return sdl[open..close];
    }
}
```

Append create tests to `GraphQlMutationExecutionTests`:

```csharp
    [Fact]
    public async Task Create_returns_node_with_selected_fields_and_reread_relation()
    {
        JsonElement? capturedBody = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (c, body) => { capturedBody = body.Clone(); return new Dictionary<string, object?> { ["id"] = "42" }; },
            // Re-read (GetAsync) returns the fully-projected node incl. the M2O relation.
            OnGet = (_, id, _, _) => new Dictionary<string, object?>
            {
                ["id"] = id,
                ["status"] = "published",
                ["category"] = new Dictionary<string, object?> { ["id"] = "9", ["name"] = "News" },
            },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { status: \"published\", categoryId: \"9\" }) { id status category { name } } }");
        var data = ParseData(result);

        var node = data.GetProperty("createArticle");
        node.GetProperty("id").GetString().Should().Be("42");
        node.GetProperty("status").GetString().Should().Be("published");
        node.GetProperty("category").GetProperty("name").GetString().Should().Be("News");

        // The input reached the service as a JSON body carrying exactly the sent keys.
        capturedBody!.Value.GetProperty("status").GetString().Should().Be("published");
        capturedBody.Value.GetProperty("categoryId").GetString().Should().Be("9");
    }

    [Fact]
    public async Task Create_validation_error_maps_to_BAD_USER_INPUT()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, _) => throw new QueryException("Field 'status' is required.")
        };

        var json = (await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { }) { id } }")).ToJson();

        json.Should().Contain("BAD_USER_INPUT");
        json.Should().Contain("required");
    }

    [Fact]
    public async Task Create_permission_denied_maps_to_FORBIDDEN()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, _) => throw new PermissionDeniedException("Write not permitted.")
        };

        var json = (await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { status: \"x\" }) { id } }")).ToJson();

        json.Should().Contain("FORBIDDEN");
    }
```

Add the `OnCreate` delegate to `FakeGraphQlDataSource`:

```csharp
    public List<string> CreatedCollections { get; } = [];
    public Func<string, System.Text.Json.JsonElement, IReadOnlyDictionary<string, object?>> OnCreate { get; set; } =
        (_, _) => new Dictionary<string, object?> { ["id"] = "1" };

    public Task<IReadOnlyDictionary<string, object?>> CreateAsync(
        string collection, System.Text.Json.JsonElement body, CancellationToken ct)
    {
        CreatedCollections.Add(collection);
        return Task.FromResult(OnCreate(collection, body));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutation"`
Expected: FAIL — `CreateAsync` missing on the interface (fake won't compile); `createArticle` / `ArticleCreateInput` absent.

- [ ] **Step 3: Write minimal implementation**

(a) `GraphQlDataSource.cs` — add to interface + impl:

```csharp
    Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct);
```
```csharp
    public Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct)
        => items.CreateAsync(collection, body, ct);
```
Add `using System.Text.Json;` at the top of `GraphQlDataSource.cs`.

(b) `CollectionSchemaBuilder.cs` — emit the create input from `Build`, and add the builder. In `Build`:

```csharp
        types.Add(BuildFilterInput(meta));                         // ArticleFilterInput
        types.Add(BuildCreateInput(meta));                         // ArticleCreateInput
        return types;
```

Add the method (reuses the existing private `ClrType`; scalar-writable + M2O FK, all nullable):

```csharp
    private InputObjectType BuildCreateInput(CollectionMetadata meta)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.CreateInputName(meta.Name), null, typeof(IReadOnlyDictionary<string, object?>));
        AddWritableFields(config, meta);
        return InputObjectType.CreateUnsafe(config);
    }

    // Shared by create/update inputs: writable scalar own-fields + M2O foreign keys, all nullable.
    // Deferred kinds resolve to a null SDL from WritableScalarInputSdl and are skipped.
    private void AddWritableFields(InputObjectTypeConfiguration config, CollectionMetadata meta)
    {
        var desc = registry.Get(meta.Name);
        foreach (var f in meta.Fields)
        {
            if (f.Hidden || f.ReadOnly || f.IsSystem) continue;
            var sdl = SchemaTypeMapper.WritableScalarInputSdl(f.Interface, ClrType(desc, f.Name));
            if (sdl is null) continue;
            config.Fields.Add(new InputFieldConfiguration(f.Name, null, TypeReference.Parse(sdl)));
        }
        foreach (var rel in meta.Relations)
            if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
                config.Fields.Add(new InputFieldConfiguration(fk, null, TypeReference.Parse("ID")));
    }
```

(c) `CollectionResolvers.cs` — change the `SelectionRelations` signature from `private static` to `internal static` (no body change):

```csharp
    internal static IReadOnlyList<string> SelectionRelations(IResolverContext ctx, string collection, bool elementIsDirect)
```

(d) `MutationResolvers.cs` — add the create field + resolver (add `using Struo.Application.Metadata;`):

```csharp
    internal static ObjectFieldConfiguration CreateField(string collection, IMetadataProvider metadata)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.CreateFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
            resolver: ctx => ResolveCreate(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration(
            "input", null, TypeReference.Parse(SchemaTypeMapper.CreateInputName(collection) + "!")));
        config.Arguments.Add(new ArgumentConfiguration("locale", null, TypeReference.Parse("String")));
        return config;
    }

    private static async ValueTask<object?> ResolveCreate(IResolverContext ctx, string collection)
    {
        var input = ctx.ArgumentValue<IReadOnlyDictionary<string, object?>?>("input");
        var locale = ctx.ArgumentValue<string?>("locale");
        var body = MutationInputMapper.ToJsonElement(input);

        var source = ctx.Service<IGraphQlDataSource>();
        var created = await source.CreateAsync(collection, body, ctx.RequestAborted);

        // Re-read so the returned node matches a query result (relations/translations resolvable).
        var id = created.GetValueOrDefault("id")?.ToString();
        if (id is null) return created;
        var relations = CollectionResolvers.SelectionRelations(ctx, collection, elementIsDirect: true);
        var deep = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, relations).Deep;
        return await source.GetAsync(collection, id, deep, locale, ctx.RequestAborted);
    }
```

(e) `StruoTypeModule.BuildMutationExtension` — add the create field (pass `metadata`):

```csharp
        foreach (var name in collectionNames)
        {
            config.Fields.Add(MutationResolvers.CreateField(name, metadata));
            config.Fields.Add(MutationResolvers.DeleteField(name));
        }
```

(`metadata` is the constructor field already on `StruoTypeModule`.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutation"`
Expected: PASS (schema + create execution + error-code tests green).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl tests/Struo.Tests/GraphQl
git commit -m "feat(graphql): create mutation (typed XCreateInput + re-read + error mapping)"
```

---

## Task 5: Update mutation (`XUpdateInput` with `version` + partial merge + CONFLICT)

**Files:**
- Modify: `src/Struo.Api/GraphQl/GraphQlDataSource.cs` (add `UpdateAsync`)
- Modify: `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs` (add `BuildUpdateInput`, emit from `Build`)
- Modify: `src/Struo.Api/GraphQl/MutationResolvers.cs` (add `UpdateField` + `ResolveUpdate`)
- Modify: `src/Struo.Api/GraphQl/StruoTypeModule.cs` (add `UpdateField` to the mutation extension)
- Modify: `tests/Struo.Tests/GraphQl/FakeGraphQlDataSource.cs` (add `OnUpdate` + `UpdateAsync`)
- Modify: `tests/Struo.Tests/GraphQl/GraphQlMutationSchemaTests.cs` (update-input SDL shape)
- Modify: `tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs` (update execution + CONFLICT)

**Interfaces:**
- Produces:
  - `IGraphQlDataSource.UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct) : Task<IReadOnlyDictionary<string, object?>?>`
  - `CollectionSchemaBuilder.BuildUpdateInput(CollectionMetadata) : InputObjectType`
  - `MutationResolvers.UpdateField(string collection, IMetadataProvider metadata) : ObjectFieldConfiguration`
  - `ArticleUpdateInput` (same fields as create + `version: Long`) + `updateArticle(id: ID!, input: ArticleUpdateInput!, locale: String): Article`.

- [ ] **Step 1: Write the failing tests**

Add to `GraphQlMutationSchemaTests`:

```csharp
    [Fact]
    public async Task Update_input_matches_create_plus_version()
    {
        var sdl = await BuildSdlAsync();
        sdl.Should().Contain("updateArticle(");
        var block = InputBlock(sdl, "ArticleUpdateInput");
        block.Should().Contain("status: String");
        block.Should().Contain("categoryId: ID");
        block.Should().Contain("version: Long"); // optimistic-concurrency token, update-only
    }
```

Add to `GraphQlMutationExecutionTests`:

```csharp
    [Fact]
    public async Task Update_sends_only_provided_keys_and_returns_reread_node()
    {
        JsonElement? capturedBody = null;
        string? capturedId = null;
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, id, body) => { capturedId = id; capturedBody = body.Clone(); return new Dictionary<string, object?> { ["id"] = id }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id, ["status"] = "archived" },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"5\", input: { status: \"archived\", version: 3 }) { id status } }");
        var data = ParseData(result);

        capturedId.Should().Be("5");
        // Partial merge: only the sent keys are present in the body.
        capturedBody!.Value.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["status", "version"]);
        capturedBody.Value.GetProperty("version").GetInt64().Should().Be(3);

        data.GetProperty("updateArticle").GetProperty("status").GetString().Should().Be("archived");
    }

    [Fact]
    public async Task Update_unknown_id_returns_null()
    {
        var ds = new FakeGraphQlDataSource { OnUpdate = (_, _, _) => null };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"nope\", input: { status: \"x\" }) { id } }");
        var data = ParseData(result);

        data.GetProperty("updateArticle").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Update_version_conflict_maps_to_CONFLICT()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, _, _) => throw new ConcurrencyConflictException("Row was modified by another writer.")
        };

        var json = (await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"5\", input: { status: \"x\", version: 1 }) { id } }")).ToJson();

        json.Should().Contain("CONFLICT");
    }
```

Add the `OnUpdate` delegate to `FakeGraphQlDataSource`:

```csharp
    public Func<string, string, System.Text.Json.JsonElement, IReadOnlyDictionary<string, object?>?> OnUpdate { get; set; } =
        (_, _, _) => null;

    public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        string collection, string id, System.Text.Json.JsonElement body, CancellationToken ct)
        => Task.FromResult(OnUpdate(collection, id, body));
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutation"`
Expected: FAIL — `UpdateAsync` missing; `updateArticle` / `ArticleUpdateInput` absent.

- [ ] **Step 3: Write minimal implementation**

(a) `GraphQlDataSource.cs` — interface + impl:

```csharp
    Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct);
```
```csharp
    public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct)
        => items.UpdateAsync(collection, id, body, ct);
```

(b) `CollectionSchemaBuilder.cs` — emit the update input from `Build`, add the builder:

```csharp
        types.Add(BuildCreateInput(meta));                         // ArticleCreateInput
        types.Add(BuildUpdateInput(meta));                         // ArticleUpdateInput
        return types;
```
```csharp
    private InputObjectType BuildUpdateInput(CollectionMetadata meta)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.UpdateInputName(meta.Name), null, typeof(IReadOnlyDictionary<string, object?>));
        // Optimistic-concurrency token (update-only). Absent -> no protection (backward compatible).
        config.Fields.Add(new InputFieldConfiguration("version", null, TypeReference.Parse("Long")));
        AddWritableFields(config, meta);
        return InputObjectType.CreateUnsafe(config);
    }
```

(c) `MutationResolvers.cs` — add the update field + resolver:

```csharp
    internal static ObjectFieldConfiguration UpdateField(string collection, IMetadataProvider metadata)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.UpdateFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
            resolver: ctx => ResolveUpdate(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        config.Arguments.Add(new ArgumentConfiguration(
            "input", null, TypeReference.Parse(SchemaTypeMapper.UpdateInputName(collection) + "!")));
        config.Arguments.Add(new ArgumentConfiguration("locale", null, TypeReference.Parse("String")));
        return config;
    }

    private static async ValueTask<object?> ResolveUpdate(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var input = ctx.ArgumentValue<IReadOnlyDictionary<string, object?>?>("input");
        var locale = ctx.ArgumentValue<string?>("locale");
        var body = MutationInputMapper.ToJsonElement(input);

        var source = ctx.Service<IGraphQlDataSource>();
        var updated = await source.UpdateAsync(collection, id, body, ctx.RequestAborted);
        if (updated is null) return null; // unknown id -> null (REST 404 parity)

        var relations = CollectionResolvers.SelectionRelations(ctx, collection, elementIsDirect: true);
        var deep = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, relations).Deep;
        return await source.GetAsync(collection, id, deep, locale, ctx.RequestAborted);
    }
```

(d) `StruoTypeModule.BuildMutationExtension` — add the update field:

```csharp
        foreach (var name in collectionNames)
        {
            config.Fields.Add(MutationResolvers.CreateField(name, metadata));
            config.Fields.Add(MutationResolvers.UpdateField(name, metadata));
            config.Fields.Add(MutationResolvers.DeleteField(name));
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutation"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl tests/Struo.Tests/GraphQl
git commit -m "feat(graphql): update mutation (XUpdateInput + version + partial merge + CONFLICT)"
```

---

## Task 6: Full-suite verification + build gate

No new production code — this is the whole-branch green gate before the live gate. If anything fails, fix in the task that owns the code (do not patch here).

**Files:** none (verification only). If the real-endpoint smoke test (`GraphQlEndpointSmokeTests` / `GraphQlEndpointTests`, which use `AddStruoGraphQl`) needs a trivial assertion for the mutation root, add it here.

- [ ] **Step 1: Build with warnings-as-errors**

Run: `dotnet build -warnaserror`
Expected: clean, 0 warnings, 0 errors.

- [ ] **Step 2: Run the full backend suite**

Run: `dotnet test tests/Struo.Tests`
Expected: all green — **449 (baseline) + the new mutation tests** (Task 1: 3 facts/theories; Task 2: 4; Task 3: 2; Task 4: 2 schema + 3 execution; Task 5: 1 schema + 3 execution). No pre-existing GraphQL test regressed (they now declare the Mutation root).

- [ ] **Step 3: Add a mutation-root smoke assertion (only if not already covered)**

If `GraphQlEndpointSmokeTests` does not touch the mutation root, add a `[Fact]` asserting the introspected schema exposes `createArticle` via the real `AddStruoGraphQl` wiring (dev environment), e.g. execute `{ __schema { mutationType { fields { name } } } }` against the test host and assert it contains `createArticle`. (Skip if the smoke suite already prints/introspects the schema.)

- [ ] **Step 4: Commit (if Step 3 added a test)**

```bash
git add tests/Struo.Tests/GraphQl
git commit -m "test(graphql): mutation root exposed via real endpoint wiring"
```

---

## Task 7: Docs — ROADMAP + memory continuity

**Files:**
- Modify: `docs/ROADMAP.md` (add a Phase 8b.1 status row + phases-table entry; mark 8b.2 / 8c as the remaining follow-ups)

- [ ] **Step 1: Update ROADMAP**

Add a status bullet summarising 8b.1 (backbone mutations: typed `create`/`update`/`delete` per collection, scalar + M2O FK + `version`, re-read return, error/RBAC/CSRF reuse, `dotnet test` count) and a phases-table row. State explicitly that **8b.2** (structured/multi-value/i18n inputs) and **8c** (advanced read querying) remain.

- [ ] **Step 2: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs: Phase 8b.1 GraphQL mutations (backbone) done (ROADMAP)"
```

---

## Task 8: Live gate (real Postgres)

Per the project rule (SQLite-green ≠ Postgres-correct), verify against live Postgres before declaring done. Run the API against the live DB (see `frontend/README.md` / prior live-gate notes for the dev-API-on-live-PG recipe), authenticate as the bootstrap super-admin, and exercise mutations via `POST /graphql`. Non-ASCII payloads must be sent UTF-8 (PowerShell `Invoke-RestMethod` / a UTF-8 body file — the console here is Big5). Cookie auth requires the `X-Struo-CSRF` header; a Bearer token is simpler for scripting.

- [ ] **Step 1: create** — `mutation { createArticle(input: { status: "published", categoryId: "<real-category-id>" }) { id status category { name } } }` → returns `id` + nested `category` (proves re-read + M2O). Record the id.
- [ ] **Step 2: create with CJK** (a non-translatable text field on a suitable collection, e.g. `createCategory(input: { name: "分類一" }) { id name }`) → round-trips code-point-exact (`分類一` = U+5206 U+985E U+4E00).
- [ ] **Step 3: update partial-merge** — `updateArticle(id: "<id>", input: { status: "draft" }) { status }` → `draft`, other columns unchanged (re-query to confirm).
- [ ] **Step 4: version conflict** — read the current `version`; `updateArticle(id, input: { status: "x", version: <stale> })` → error `code: CONFLICT`.
- [ ] **Step 5: validation** — `createArticle(input: { })` (omit a required field) → error `code: BAD_USER_INPUT`.
- [ ] **Step 6: permission** — as an anonymous / non-writer caller, any mutation → error `code: FORBIDDEN`. `updateUser`/`createUser` as a non-super-admin → `FORBIDDEN` (AdminOnly guard).
- [ ] **Step 7: delete** — `deleteArticle(id: "<id>")` → `true`; re-query `article(id)` → `null`.
- [ ] **Step 8: record evidence** in `docs/ROADMAP.md` (per the live-gate convention) and note any backend fix commits the gate surfaces.

---

## Self-Review

**1. Spec coverage:**
- Spec §1.1 input typing (typed per-collection) → Tasks 4/5 (`BuildCreateInput`/`BuildUpdateInput`). ✓
- Spec §1.1 bare-node + error filter → Tasks 4/5 resolvers return the node; error codes via existing `StruoErrorFilter` (Tasks 4/5 tests). ✓
- Spec §2 `IGraphQlDataSource` write methods + mutation type extension + empty-schema guard → Tasks 3/4/5 + the `_service` anchor (Task 3). ✓
- Spec §3 included/excluded fields, all-nullable, partial-update, signatures → Tasks 4/5 (`AddWritableFields`, `WritableScalarInputSdl`, nullable inputs, `id` as arg, `version` update-only). ✓
- Spec §4 input→JsonElement + re-read + delete → Task 2 (`MutationInputMapper`), Tasks 4/5 (re-read via `SelectionRelations`+`GetAsync`), Task 3 (delete bool). ✓
- Spec §5 version/CONFLICT, RBAC, error table → Task 5 (CONFLICT), Tasks 4/5 (FORBIDDEN/BAD_USER_INPUT); RBAC inherited from `ItemService` (live gate Task 8 steps 5-6). ✓
- Spec §6 CSRF zero-code → no task needed; documented; live gate uses the header (Task 8). ✓
- Spec §7 three test layers → schema-gen (Tasks 1/4/5), execution (Tasks 3/4/5), live gate (Task 8). ✓

**2. Placeholder scan:** No "TBD"/"add error handling"/"similar to Task N". Every code step shows full code. Task 6 Step 3 and Task 7 Step 1 are conditional/prose but concrete (exact introspection query / exact content to add). ✓

**3. Type consistency:**
- `SelectionRelations(IResolverContext, string, bool)` — defined in `CollectionResolvers`, made `internal` in Task 4, called with `elementIsDirect: true` in Tasks 4/5. ✓
- `IGraphQlDataSource` methods: `DeleteAsync(string,string,CT):Task<bool>` (Task 3), `CreateAsync(string,JsonElement,CT):Task<IReadOnlyDictionary<string,object?>>` (Task 4), `UpdateAsync(string,string,JsonElement,CT):Task<IReadOnlyDictionary<string,object?>?>` (Task 5) — the fake and impl signatures match exactly across tasks. ✓
- `MutationResolvers.CreateField/UpdateField` take `(string, IMetadataProvider)`; `DeleteField` takes `(string)` — matched at the `StruoTypeModule` call site (Tasks 3/4/5). ✓
- `SchemaTypeMapper.WritableScalarInputSdl` / `CreateInputName` / `UpdateInputName` used in `CollectionSchemaBuilder` and `MutationResolvers` exactly as defined in Task 1. ✓
- `MutationInputMapper.ToJsonElement` return `JsonElement` consumed by the resolvers (Tasks 4/5) and asserted in Task 2. ✓
