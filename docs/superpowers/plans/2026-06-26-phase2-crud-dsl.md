# StruoCMS Phase 2 (Generic CRUD + Single-Table Query DSL) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Metadata-driven generic REST CRUD + a single-table query DSL (filter/sort/fields/pagination/search) over any `[CmsCollection]` entity, with whitelist validation and DoS caps, entirely through SqlSugar (zero vendor SQL).

**Architecture:** A normalized DSL AST in Domain; parser + validator + `ItemService` + ports in Application; a SqlSugar repository that translates the AST to dynamic `ConditionalModel`s and runs queries by runtime `Type` (via `MakeGenericMethod`) in Infrastructure; an `ItemsController` in Api. Rows project to camelCase dictionaries wrapped in `{data, meta}`.

**Tech Stack:** .NET 10, ASP.NET Core Controllers, SqlSugarCore (dynamic `IConditionalModel` API), System.Text.Json, xUnit + AwesomeAssertions, SQLite (tests) / PostgreSQL (runtime).

## Global Constraints

- **Dependency rule (§2):** Domain → nothing; Application → Domain; Infrastructure → Application+Domain; Api → Application+Infrastructure; samples → Domain; only Api references samples.
- **Domain purity (§2):** `Struo.Domain` has zero external package references. AST + `QueryException` are plain C# (BCL only).
- **Zero vendor SQL (§15):** all DB access via SqlSugar ORM / `IConditionalModel`; no raw SQL strings, no DB-specific functions. Operates on SQLite (tests) and PostgreSQL.
- **No per-request attribute reflection (§3):** metadata + entity descriptors built once at startup. The only per-request reflection is the generic `MakeGenericMethod` dispatch.
- **camelCase JSON (§1):** field names in transport + responses are camelCase; the registry maps camelCase field name → C# property → DB column.
- **Whitelist validation (§7.7):** every field path is validated against the collection metadata; unknown field or dotted/relation path → HTTP 400. Dotted paths are deferred to Phase 3.
- **DoS caps:** `MaxLimit`=100, `DefaultLimit`=25, `MaxFilterConditions`=50 (config-bound).
- **Packages (§15):** add via `dotnet add package` (latest, CPM); never hardcode versions.
- **Build `TreatWarningsAsErrors=true` → 0 warnings. TDD:** failing test first.
- **Tests use `using AwesomeAssertions;`** (not FluentAssertions).
- **SqlSugar API note:** exact member names (`ConditionalType` values, `ConditionalCollections`, `ToPageList`, `InSingle`, `Deleteable().In`, `EntityMaintenance.GetDbColumnName`) must be verified against the installed SqlSugarCore at implementation time; the intent in each step governs — adjust member names to match the package and keep tests/impl in agreement.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Struo.Domain/Query/QueryOperator.cs` | Comparison operator enum |
| `src/Struo.Domain/Query/LogicalOperator.cs` | And/Or enum |
| `src/Struo.Domain/Query/FilterNode.cs` | AST: abstract + LogicalFilter + ComparisonFilter |
| `src/Struo.Domain/Query/SortField.cs` | Sort spec record |
| `src/Struo.Domain/Query/QueryModel.cs` | Normalized query record |
| `src/Struo.Domain/Query/QueryException.cs` | Parse/validation domain error (→400) + CollectionNotFoundException (→404) |
| `src/Struo.Application/Configuration/StruoQueryOptions.cs` | DoS caps |
| `src/Struo.Application/Metadata/IEntityRegistry.cs` | collection → entity descriptor port |
| `src/Struo.Application/Security/IPermissionService.cs` | Permission seam |
| `src/Struo.Application/Query/IItemRepository.cs` | Data port (query + CRUD) |
| `src/Struo.Application/Query/QueryParser.cs` | JSON + bracket → QueryModel |
| `src/Struo.Application/Query/QueryValidator.cs` | Whitelist + caps |
| `src/Struo.Application/Query/ItemService.cs` | Orchestration + projection + envelope |
| `src/Struo.Infrastructure/Metadata/EntityRegistry.cs` | descriptor impl (built at scan) |
| `src/Struo.Infrastructure/Security/AllowAllPermissionService.cs` | Allow-all impl |
| `src/Struo.Infrastructure/Query/ConditionalModelTranslator.cs` | AST → SqlSugar conditionals |
| `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` | repository + generic QueryRunner |
| `src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs` (modify) | also build/register EntityRegistry |
| `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs` | `AddStruoData` (repo, permission, options, ItemService) |
| `src/Struo.Api/Controllers/ItemsController.cs` | `/api/items/{collection}` endpoints |
| `src/Struo.Api/Program.cs` (modify) | wire `AddStruoData` + exception mapping |
| `tests/Struo.Tests/Query/QueryParserTests.cs` | parser unit tests |
| `tests/Struo.Tests/Query/QueryValidatorTests.cs` | validator unit tests |
| `tests/Struo.Tests/Query/ConditionalModelTranslatorTests.cs` | translator unit tests |
| `tests/Struo.Tests/Query/EntityRegistryTests.cs` | registry unit tests |
| `tests/Struo.Tests/Query/SqlSugarItemRepositoryTests.cs` | repository integration (SQLite) |
| `tests/Struo.Tests/Query/ItemServiceTests.cs` | service projection test |
| `tests/Struo.Tests/Api/ItemsEndpointTests.cs` | HTTP integration |

---

### Task 1: Domain query AST

**Files:** all under `src/Struo.Domain/Query/`.

**Interfaces:**
- Consumes: nothing.
- Produces: `QueryOperator`, `LogicalOperator`, `FilterNode`/`LogicalFilter`/`ComparisonFilter`, `SortField`, `QueryModel`, `QueryException` in namespace `Struo.Domain.Query`.

- [ ] **Step 1: Create the enums and AST**

```csharp
// src/Struo.Domain/Query/QueryOperator.cs
namespace Struo.Domain.Query;

public enum QueryOperator
{
    Eq, Neq, In, Nin, Lt, Lte, Gt, Gte, Null, NNull, Contains, StartsWith, EndsWith
}
```

```csharp
// src/Struo.Domain/Query/LogicalOperator.cs
namespace Struo.Domain.Query;

public enum LogicalOperator { And, Or }
```

```csharp
// src/Struo.Domain/Query/FilterNode.cs
namespace Struo.Domain.Query;

public abstract record FilterNode;

public sealed record LogicalFilter(LogicalOperator Op, IReadOnlyList<FilterNode> Children) : FilterNode;

public sealed record ComparisonFilter(string FieldPath, QueryOperator Op, object? Value) : FilterNode;
```

```csharp
// src/Struo.Domain/Query/SortField.cs
namespace Struo.Domain.Query;

public sealed record SortField(string Field, bool Descending);
```

```csharp
// src/Struo.Domain/Query/QueryModel.cs
namespace Struo.Domain.Query;

public sealed record QueryModel(
    IReadOnlyList<string>? Fields,
    FilterNode? Filter,
    IReadOnlyList<SortField> Sort,
    int Limit,
    int Offset,
    string? Search);
```

```csharp
// src/Struo.Domain/Query/QueryException.cs
namespace Struo.Domain.Query;

/// <summary>Invalid query/parse input. Surfaced as HTTP 400.</summary>
public sealed class QueryException : Exception
{
    public QueryException(string message) : base(message) { }
}

/// <summary>Unknown collection name. Surfaced as HTTP 404.</summary>
public sealed class CollectionNotFoundException(string collection)
    : Exception($"Unknown collection '{collection}'.")
{
    public string Collection { get; } = collection;
}
```

- [ ] **Step 2: Build Domain**

Run: `dotnet build src/Struo.Domain`
Expected: 0 warnings/errors. Confirm `Struo.Domain.csproj` still has no `<PackageReference>`.

- [ ] **Step 3: Commit**

```bash
git add src/Struo.Domain/Query
git commit -m "feat: add Domain query AST (operators, filter nodes, QueryModel)"
```

---

### Task 2: Query options, entity registry port + impl, registry wiring

**Files:**
- Create: `src/Struo.Application/Configuration/StruoQueryOptions.cs`, `src/Struo.Application/Metadata/IEntityRegistry.cs`, `src/Struo.Infrastructure/Metadata/EntityRegistry.cs`
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs`, `src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs`
- Test: `tests/Struo.Tests/Query/EntityRegistryTests.cs`

**Interfaces:**
- Consumes: `MetadataScanner` types, `Article`/`Tag`.
- Produces:
  - `sealed class StruoQueryOptions { const string SectionName="Query"; int MaxLimit=100; int DefaultLimit=25; int MaxFilterConditions=50; }`
  - `sealed record EntityDescriptor(Type EntityType, IReadOnlyDictionary<string,string> FieldToProperty, string IdProperty)` and `interface IEntityRegistry { EntityDescriptor? Get(string collection); }` in `Struo.Application.Metadata`.
  - `EntityRegistry : IEntityRegistry` (case-insensitive).
  - `MetadataScanner.ScanDescriptors(IEnumerable<Type>)` and `AddStruoMetadata` also registers `IEntityRegistry` singleton.

- [ ] **Step 1: Create options + port**

```csharp
// src/Struo.Application/Configuration/StruoQueryOptions.cs
namespace Struo.Application.Configuration;

public sealed class StruoQueryOptions
{
    public const string SectionName = "Query";
    public int MaxLimit { get; set; } = 100;
    public int DefaultLimit { get; set; } = 25;
    public int MaxFilterConditions { get; set; } = 50;
}
```

```csharp
// src/Struo.Application/Metadata/IEntityRegistry.cs
namespace Struo.Application.Metadata;

public sealed record EntityDescriptor(
    Type EntityType,
    IReadOnlyDictionary<string, string> FieldToProperty,  // camelCase field name -> CLR property name
    string IdProperty);

public interface IEntityRegistry
{
    EntityDescriptor? Get(string collection);  // case-insensitive
}
```

- [ ] **Step 2: Write the failing registry test**

```csharp
// tests/Struo.Tests/Query/EntityRegistryTests.cs
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Metadata;
using Struo.Infrastructure.DependencyInjection;
using Struo.Sample.Blog;
using Xunit;

namespace Struo.Tests.Query;

public class EntityRegistryTests
{
    private static IEntityRegistry Build()
    {
        var services = new ServiceCollection();
        services.AddStruoMetadata(typeof(Article).Assembly);
        return services.BuildServiceProvider().GetRequiredService<IEntityRegistry>();
    }

    [Fact]
    public void Resolves_entity_type_and_id_for_collection()
    {
        var d = Build().Get("article");
        d.Should().NotBeNull();
        d!.EntityType.Should().Be(typeof(Article));
        d.IdProperty.Should().Be("Id");
    }

    [Fact]
    public void Maps_camel_field_names_to_clr_properties()
    {
        var d = Build().Get("article")!;
        d.FieldToProperty["title"].Should().Be("Title");
        d.FieldToProperty["seoTitle"].Should().Be("SeoTitle");
        d.FieldToProperty["createdAt"].Should().Be("CreatedAt");
    }

    [Fact]
    public void Get_is_case_insensitive_and_null_for_unknown()
    {
        var reg = Build();
        reg.Get("ARTICLE").Should().NotBeNull();
        reg.Get("nope").Should().BeNull();
    }
}
```

- [ ] **Step 3: Run — verify fail**

Run: `dotnet test --filter FullyQualifiedName~EntityRegistryTests`
Expected: FAIL — `IEntityRegistry`/`EntityRegistry` not registered.

- [ ] **Step 4: Implement `ScanDescriptors`, `EntityRegistry`, and registration**

Append to `src/Struo.Infrastructure/Metadata/MetadataScanner.cs` (add `using Struo.Application.Metadata;` and `using SqlSugar;`):

```csharp
public static IReadOnlyDictionary<string, EntityDescriptor> ScanDescriptors(IEnumerable<Type> types)
{
    var map = new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase);
    foreach (var type in types)
    {
        if (type.GetCustomAttribute<CmsCollectionAttribute>() is null) continue;

        var collection = Camel(type.Name);
        var fieldToProp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var idProperty = "Id";
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            fieldToProp[Camel(prop.Name)] = prop.Name;
            if (prop.GetCustomAttribute<SugarColumn>() is { IsPrimaryKey: true })
                idProperty = prop.Name;
        }
        map[collection] = new EntityDescriptor(type, fieldToProp, idProperty);
    }
    return map;
}
```

```csharp
// src/Struo.Infrastructure/Metadata/EntityRegistry.cs
using Struo.Application.Metadata;

namespace Struo.Infrastructure.Metadata;

public sealed class EntityRegistry : IEntityRegistry
{
    private readonly IReadOnlyDictionary<string, EntityDescriptor> _byCollection;

    public EntityRegistry(IReadOnlyDictionary<string, EntityDescriptor> descriptors) =>
        _byCollection = descriptors;

    public EntityDescriptor? Get(string collection) =>
        _byCollection.TryGetValue(collection, out var d) ? d : null;
}
```

In `MetadataServiceCollectionExtensions.AddStruoMetadata`, after registering `CachedMetadataProvider`, add (with `using Struo.Application.Metadata;`):

```csharp
        var descriptors = MetadataScanner.ScanDescriptors(assemblies.SelectMany(a => a.GetTypes()));
        services.AddSingleton<IEntityRegistry>(new EntityRegistry(descriptors));
```

- [ ] **Step 5: Run — verify pass**

Run: `dotnet test --filter FullyQualifiedName~EntityRegistryTests`
Expected: PASS (3). Then `dotnet test` → all green.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Application/Configuration/StruoQueryOptions.cs src/Struo.Application/Metadata/IEntityRegistry.cs src/Struo.Infrastructure/Metadata/EntityRegistry.cs src/Struo.Infrastructure/Metadata/MetadataScanner.cs src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs tests/Struo.Tests/Query/EntityRegistryTests.cs
git commit -m "feat: add StruoQueryOptions and entity registry (type/field/PK resolution)"
```

---

### Task 3: QueryParser (JSON envelope + bracket query-string)

**Files:**
- Create: `src/Struo.Application/Query/QueryParser.cs`
- Test: `tests/Struo.Tests/Query/QueryParserTests.cs`

**Interfaces:**
- Consumes: Domain AST.
- Produces:
  - `static QueryModel QueryParser.ParseEnvelope(JsonElement envelope)`
  - `static QueryModel QueryParser.ParseQueryString(IReadOnlyDictionary<string,string?> query)`
  - Both yield an **unvalidated** `QueryModel`. Operators are `_`-prefixed → `QueryOperator`. Throws `QueryException` for unknown operators or malformed input.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Struo.Tests/Query/QueryParserTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class QueryParserTests
{
    [Fact]
    public void Envelope_parses_filter_sort_fields_paging()
    {
        var json = JsonDocument.Parse("""
        {
          "fields": ["id","title"],
          "filter": { "status": { "_eq": "published" } },
          "sort": ["-createdAt","title"],
          "limit": 10, "offset": 5, "search": "hello"
        }
        """).RootElement;

        var q = QueryParser.ParseEnvelope(json);

        q.Fields.Should().BeEquivalentTo(["id", "title"]);
        q.Limit.Should().Be(10);
        q.Offset.Should().Be(5);
        q.Search.Should().Be("hello");
        q.Sort.Should().ContainInOrder(new SortField("createdAt", true), new SortField("title", false));
        var cmp = q.Filter.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("status");
        cmp.Op.Should().Be(QueryOperator.Eq);
    }

    [Fact]
    public void Envelope_parses_and_or_logical_groups()
    {
        var json = JsonDocument.Parse("""
        { "filter": { "_and": [ { "status": { "_eq": "published" } }, { "title": { "_contains": "x" } } ] } }
        """).RootElement;

        var q = QueryParser.ParseEnvelope(json);
        var logical = q.Filter.Should().BeOfType<LogicalFilter>().Subject;
        logical.Op.Should().Be(LogicalOperator.And);
        logical.Children.Should().HaveCount(2);
    }

    [Fact]
    public void QueryString_parses_bracket_filter_and_sort()
    {
        var qs = new Dictionary<string, string?>
        {
            ["filter[status][_eq]"] = "published",
            ["sort"] = "-createdAt,title",
            ["fields"] = "id,title",
            ["limit"] = "10", ["offset"] = "5", ["search"] = "hello"
        };

        var q = QueryParser.ParseQueryString(qs);

        q.Fields.Should().BeEquivalentTo(["id", "title"]);
        q.Sort.Should().ContainInOrder(new SortField("createdAt", true), new SortField("title", false));
        var cmp = q.Filter.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("status");
        cmp.Op.Should().Be(QueryOperator.Eq);
        cmp.Value.Should().Be("published");
    }

    [Fact]
    public void Unknown_operator_throws()
    {
        var json = JsonDocument.Parse("""{ "filter": { "status": { "_bogus": "x" } } }""").RootElement;
        var act = () => QueryParser.ParseEnvelope(json);
        act.Should().Throw<QueryException>();
    }
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test --filter FullyQualifiedName~QueryParserTests`
Expected: FAIL — `QueryParser` missing.

- [ ] **Step 3: Implement the parser**

```csharp
// src/Struo.Application/Query/QueryParser.cs
using System.Text.Json;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public static class QueryParser
{
    private static readonly IReadOnlyDictionary<string, QueryOperator> Operators =
        new Dictionary<string, QueryOperator>(StringComparer.Ordinal)
        {
            ["_eq"] = QueryOperator.Eq, ["_neq"] = QueryOperator.Neq,
            ["_in"] = QueryOperator.In, ["_nin"] = QueryOperator.Nin,
            ["_lt"] = QueryOperator.Lt, ["_lte"] = QueryOperator.Lte,
            ["_gt"] = QueryOperator.Gt, ["_gte"] = QueryOperator.Gte,
            ["_null"] = QueryOperator.Null, ["_nnull"] = QueryOperator.NNull,
            ["_contains"] = QueryOperator.Contains,
            ["_starts_with"] = QueryOperator.StartsWith,
            ["_ends_with"] = QueryOperator.EndsWith,
        };

    public static QueryModel ParseEnvelope(JsonElement env)
    {
        IReadOnlyList<string>? fields = null;
        if (env.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array)
            fields = f.EnumerateArray().Select(x => x.GetString() ?? "").ToList();

        FilterNode? filter = null;
        if (env.TryGetProperty("filter", out var fl) && fl.ValueKind == JsonValueKind.Object)
            filter = ParseFilter(fl);

        var sort = new List<SortField>();
        if (env.TryGetProperty("sort", out var s) && s.ValueKind == JsonValueKind.Array)
            foreach (var item in s.EnumerateArray())
                sort.Add(ParseSortToken(item.GetString() ?? ""));

        var limit = env.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li) ? li : 0;
        var offset = env.TryGetProperty("offset", out var o) && o.TryGetInt32(out var oi) ? oi : 0;
        var search = env.TryGetProperty("search", out var se) ? se.GetString() : null;

        return new QueryModel(fields, filter, sort, limit, offset, search);
    }

    private static FilterNode ParseFilter(JsonElement obj)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.NameEquals("_and") || prop.NameEquals("_or"))
            {
                var op = prop.NameEquals("_and") ? LogicalOperator.And : LogicalOperator.Or;
                if (prop.Value.ValueKind != JsonValueKind.Array)
                    throw new QueryException($"'{prop.Name}' must be an array.");
                var children = prop.Value.EnumerateArray().Select(ParseFilter).ToList();
                return new LogicalFilter(op, children);
            }

            var field = prop.Name;
            if (prop.Value.ValueKind != JsonValueKind.Object)
                throw new QueryException($"Filter for field '{field}' must be an object of operators.");
            var inner = prop.Value.EnumerateObject().FirstOrDefault();
            if (inner.Value.ValueKind == JsonValueKind.Undefined)
                throw new QueryException($"Filter for field '{field}' has no operator.");
            if (!Operators.TryGetValue(inner.Name, out var qop))
                throw new QueryException($"Unknown operator '{inner.Name}'.");
            return new ComparisonFilter(field, qop, ReadValue(inner.Value));
        }
        throw new QueryException("Empty filter object.");
    }

    private static object? ReadValue(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.TryGetInt64(out var n) ? n : v.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => v.EnumerateArray().Select(ReadValue).ToList(),
        _ => v.GetRawText()
    };

    private static SortField ParseSortToken(string token)
    {
        token = token.Trim();
        return token.StartsWith('-')
            ? new SortField(token[1..], true)
            : new SortField(token, false);
    }

    public static QueryModel ParseQueryString(IReadOnlyDictionary<string, string?> query)
    {
        IReadOnlyList<string>? fields = query.TryGetValue("fields", out var fv) && !string.IsNullOrWhiteSpace(fv)
            ? fv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;

        var sort = new List<SortField>();
        if (query.TryGetValue("sort", out var sv) && !string.IsNullOrWhiteSpace(sv))
            foreach (var t in sv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                sort.Add(ParseSortToken(t));

        var conditions = new List<FilterNode>();
        foreach (var (key, val) in query)
        {
            if (!key.StartsWith("filter[", StringComparison.Ordinal)) continue;
            var inner = key["filter[".Length..].TrimEnd(']');
            var parts = inner.Split("][", StringSplitOptions.None);
            if (parts.Length != 2) throw new QueryException($"Malformed filter key '{key}'.");
            var field = parts[0];
            var opToken = parts[1];
            if (!Operators.TryGetValue(opToken, out var qop))
                throw new QueryException($"Unknown operator '{opToken}'.");
            conditions.Add(new ComparisonFilter(field, qop, val));
        }

        FilterNode? filter = conditions.Count switch
        {
            0 => null,
            1 => conditions[0],
            _ => new LogicalFilter(LogicalOperator.And, conditions)
        };

        var limit = query.TryGetValue("limit", out var lv) && int.TryParse(lv, out var li) ? li : 0;
        var offset = query.TryGetValue("offset", out var ov) && int.TryParse(ov, out var oi) ? oi : 0;
        var search = query.TryGetValue("search", out var se) ? se : null;

        return new QueryModel(fields, filter, sort, limit, offset, search);
    }
}
```

- [ ] **Step 4: Run — verify pass**

Run: `dotnet test --filter FullyQualifiedName~QueryParserTests`
Expected: PASS (4).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/QueryParser.cs tests/Struo.Tests/Query/QueryParserTests.cs
git commit -m "feat: add query DSL parser (JSON envelope + bracket query-string)"
```

---

### Task 4: QueryValidator (whitelist, dotted-path reject, caps, search)

**Files:**
- Create: `src/Struo.Application/Security/IPermissionService.cs`, `src/Struo.Application/Query/QueryValidator.cs`
- Test: `tests/Struo.Tests/Query/QueryValidatorTests.cs`

**Interfaces:**
- Consumes: `QueryModel`, `CollectionMetadata`, `FieldMetadata` (Phase 1), `StruoQueryOptions`, AST.
- Produces:
  - `interface IPermissionService { bool CanRead(string c); bool CanWrite(string c); bool CanDelete(string c); IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all); }`
  - `static QueryModel QueryValidator.Validate(QueryModel q, CollectionMetadata meta, StruoQueryOptions opts)` (returns normalized model; throws `QueryException`) and `static IReadOnlyList<string> QueryValidator.SearchableFields(CollectionMetadata meta)`.

- [ ] **Step 1: Create the permission port**

```csharp
// src/Struo.Application/Security/IPermissionService.cs
namespace Struo.Application.Security;

public interface IPermissionService
{
    bool CanRead(string collection);
    bool CanWrite(string collection);
    bool CanDelete(string collection);
    IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames);
}
```

- [ ] **Step 2: Write the failing validator tests**

```csharp
// tests/Struo.Tests/Query/QueryValidatorTests.cs
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class QueryValidatorTests
{
    private static CollectionMetadata Meta() => new()
    {
        Name = "article", Label = "Article",
        FieldGroups = [],
        Fields =
        [
            new FieldMetadata { Name = "title", Label = "Title", Interface = FieldInterface.Text, Searchable = true },
            new FieldMetadata { Name = "status", Label = "Status", Interface = FieldInterface.Select },
            new FieldMetadata { Name = "createdAt", Label = "CreatedAt", Interface = FieldInterface.DateTime, IsSystem = true, ReadOnly = true },
        ]
    };

    private static readonly StruoQueryOptions Opts = new();

    [Fact]
    public void Unknown_filter_field_throws()
    {
        var q = new QueryModel(null, new ComparisonFilter("nope", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts);
        act.Should().Throw<QueryException>().WithMessage("*nope*");
    }

    [Fact]
    public void Dotted_relation_path_throws()
    {
        var q = new QueryModel(null, new ComparisonFilter("category.name", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts);
        act.Should().Throw<QueryException>().WithMessage("*relation*");
    }

    [Fact]
    public void Unknown_sort_or_field_throws()
    {
        var q1 = new QueryModel(null, null, [new SortField("ghost", false)], 0, 0, null);
        var a1 = () => QueryValidator.Validate(q1, Meta(), Opts);
        a1.Should().Throw<QueryException>();

        var q2 = new QueryModel(["ghost"], null, [], 0, 0, null);
        var a2 = () => QueryValidator.Validate(q2, Meta(), Opts);
        a2.Should().Throw<QueryException>();
    }

    [Fact]
    public void Limit_is_clamped_and_defaulted()
    {
        var zero = QueryValidator.Validate(new QueryModel(null, null, [], 0, 0, null), Meta(), Opts);
        zero.Limit.Should().Be(Opts.DefaultLimit);

        var over = QueryValidator.Validate(new QueryModel(null, null, [], 9999, 0, null), Meta(), Opts);
        over.Limit.Should().Be(Opts.MaxLimit);
    }

    [Fact]
    public void Too_many_conditions_throws()
    {
        var many = Enumerable.Range(0, Opts.MaxFilterConditions + 1)
            .Select(_ => (FilterNode)new ComparisonFilter("title", QueryOperator.Eq, "x")).ToList();
        var q = new QueryModel(null, new LogicalFilter(LogicalOperator.And, many), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts);
        act.Should().Throw<QueryException>().WithMessage("*conditions*");
    }
}
```

- [ ] **Step 3: Run — verify fail**

Run: `dotnet test --filter FullyQualifiedName~QueryValidatorTests`
Expected: FAIL — `QueryValidator` missing.

- [ ] **Step 4: Implement the validator**

```csharp
// src/Struo.Application/Query/QueryValidator.cs
using Struo.Application.Configuration;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public static class QueryValidator
{
    public static QueryModel Validate(QueryModel q, CollectionMetadata meta, StruoQueryOptions opts)
    {
        var known = meta.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        void CheckField(string path)
        {
            if (path.Contains('.'))
                throw new QueryException($"Relation path '{path}' is not supported until Phase 3.");
            if (!known.Contains(path))
                throw new QueryException($"Unknown field '{path}' on collection '{meta.Name}'.");
        }

        var conditionCount = 0;
        void Walk(FilterNode? node)
        {
            switch (node)
            {
                case null: return;
                case ComparisonFilter c:
                    conditionCount++;
                    if (conditionCount > opts.MaxFilterConditions)
                        throw new QueryException($"Too many filter conditions (max {opts.MaxFilterConditions}).");
                    CheckField(c.FieldPath);
                    break;
                case LogicalFilter l:
                    foreach (var child in l.Children) Walk(child);
                    break;
            }
        }

        Walk(q.Filter);

        foreach (var s in q.Sort) CheckField(s.Field);
        if (q.Fields is not null) foreach (var f in q.Fields) CheckField(f);

        var limit = q.Limit <= 0 ? opts.DefaultLimit : Math.Min(q.Limit, opts.MaxLimit);
        var offset = Math.Max(0, q.Offset);

        return q with { Limit = limit, Offset = offset };
    }

    public static IReadOnlyList<string> SearchableFields(CollectionMetadata meta) =>
        meta.Fields.Where(f => f.Searchable).Select(f => f.Name).ToList();
}
```

- [ ] **Step 5: Run — verify pass**

Run: `dotnet test --filter FullyQualifiedName~QueryValidatorTests`
Expected: PASS (5).

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Application/Security/IPermissionService.cs src/Struo.Application/Query/QueryValidator.cs tests/Struo.Tests/Query/QueryValidatorTests.cs
git commit -m "feat: add query validator (whitelist, dotted-path reject, DoS caps) and permission port"
```

---

### Task 5: ConditionalModelTranslator (AST → SqlSugar conditionals)

**Files:**
- Create: `src/Struo.Infrastructure/Query/ConditionalModelTranslator.cs`
- Test: `tests/Struo.Tests/Query/ConditionalModelTranslatorTests.cs`

**Interfaces:**
- Consumes: AST, `EntityDescriptor`, `ISqlSugarClient` (for `EntityMaintenance.GetDbColumnName`).
- Produces: `static List<IConditionalModel> ConditionalModelTranslator.Translate(FilterNode? filter, string? search, IReadOnlyList<string> searchableFields, EntityDescriptor descriptor, ISqlSugarClient db)`.

> **Verify against installed SqlSugarCore:** `IConditionalModel`, `ConditionalModel { FieldName, ConditionalType, FieldValue }`, `ConditionalCollections { ConditionalList: List<KeyValuePair<WhereType, ConditionalModel>> }`, the `ConditionalType` members, and `EntityMaintenance.GetDbColumnName(propertyName, entityType)` arg order — adjust names to the package; keep tests + impl in agreement.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Struo.Tests/Query/ConditionalModelTranslatorTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Sample.Blog;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class ConditionalModelTranslatorTests
{
    private static (ISqlSugarClient db, EntityDescriptor d, SqliteTestDatabase file) Setup()
    {
        var file = new SqliteTestDatabase();
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = file.ConnectionString },
            new TestCurrentUserAccessor("system"));
        db.CodeFirst.InitTables<Article>();
        var fieldToProp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["title"] = "Title", ["status"] = "Status" };
        var d = new EntityDescriptor(typeof(Article), fieldToProp, "Id");
        return (db, d, file);
    }

    [Fact]
    public void Translates_single_equality()
    {
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("status", QueryOperator.Eq, "published"), null, [], d, db);

            list.Should().ContainSingle();
            var cm = (ConditionalModel)list[0];
            cm.FieldName.Should().Be("Status");
            cm.ConditionalType.Should().Be(ConditionalType.Equal);
            cm.FieldValue.Should().Be("published");
        }
    }

    [Fact]
    public void Translates_contains_to_like()
    {
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("title", QueryOperator.Contains, "x"), null, [], d, db);
            ((ConditionalModel)list[0]).ConditionalType.Should().Be(ConditionalType.Like);
        }
    }

    [Fact]
    public void Search_builds_or_group_over_searchable_fields()
    {
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(null, "hello", ["title"], d, db);
            list.Should().ContainSingle();
            list[0].Should().BeOfType<ConditionalCollections>();
        }
    }
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test --filter FullyQualifiedName~ConditionalModelTranslatorTests`
Expected: FAIL — translator missing.

- [ ] **Step 3: Implement the translator**

```csharp
// src/Struo.Infrastructure/Query/ConditionalModelTranslator.cs
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

public static class ConditionalModelTranslator
{
    public static List<IConditionalModel> Translate(
        FilterNode? filter, string? search, IReadOnlyList<string> searchableFields,
        EntityDescriptor descriptor, ISqlSugarClient db)
    {
        var models = new List<IConditionalModel>();
        if (filter is not null) models.Add(ToModel(filter, descriptor, db));

        if (!string.IsNullOrWhiteSpace(search) && searchableFields.Count > 0)
        {
            var or = new ConditionalCollections
            {
                ConditionalList = searchableFields
                    .Select(f => new KeyValuePair<WhereType, ConditionalModel>(
                        WhereType.Or,
                        new ConditionalModel
                        {
                            FieldName = Column(descriptor, db, f),
                            ConditionalType = ConditionalType.Like,
                            FieldValue = search
                        }))
                    .ToList()
            };
            models.Add(or);
        }

        return models;
    }

    private static IConditionalModel ToModel(FilterNode node, EntityDescriptor d, ISqlSugarClient db)
    {
        switch (node)
        {
            case ComparisonFilter c:
                return new ConditionalModel
                {
                    FieldName = Column(d, db, c.FieldPath),
                    ConditionalType = MapOperator(c.Op),
                    FieldValue = ToFieldValue(c.Value)
                };
            case LogicalFilter l:
                var wt = l.Op == LogicalOperator.And ? WhereType.And : WhereType.Or;
                return new ConditionalCollections
                {
                    ConditionalList = l.Children
                        .Select(ch => new KeyValuePair<WhereType, ConditionalModel>(wt, (ConditionalModel)ToModel(ch, d, db)))
                        .ToList()
                };
            default:
                throw new InvalidOperationException("Unknown filter node.");
        }
    }

    private static string Column(EntityDescriptor d, ISqlSugarClient db, string field)
    {
        var property = d.FieldToProperty.TryGetValue(field, out var p) ? p : field;
        return db.EntityMaintenance.GetDbColumnName(property, d.EntityType);
    }

    private static string? ToFieldValue(object? value) => value switch
    {
        null => null,
        IEnumerable<object?> list => string.Join(",", list.Select(v => v?.ToString())),
        _ => value.ToString()
    };

    private static ConditionalType MapOperator(QueryOperator op) => op switch
    {
        QueryOperator.Eq => ConditionalType.Equal,
        QueryOperator.Neq => ConditionalType.NoEqual,
        QueryOperator.Contains => ConditionalType.Like,
        QueryOperator.StartsWith => ConditionalType.LikeRight,
        QueryOperator.EndsWith => ConditionalType.LikeLeft,
        QueryOperator.In => ConditionalType.In,
        QueryOperator.Nin => ConditionalType.NotIn,
        QueryOperator.Lt => ConditionalType.LessThan,
        QueryOperator.Lte => ConditionalType.LessThanOrEqual,
        QueryOperator.Gt => ConditionalType.GreaterThan,
        QueryOperator.Gte => ConditionalType.GreaterThanOrEqual,
        QueryOperator.Null => ConditionalType.IsNullOrEmpty,
        QueryOperator.NNull => ConditionalType.IsNot,
        _ => throw new InvalidOperationException($"Unmapped operator {op}.")
    };
}
```

> `ConditionalType` member names (`LikeRight`/`LikeLeft`, `NoEqual`, `IsNullOrEmpty`, `IsNot`) and `WhereType` must match the installed SqlSugarCore — verify and adjust. Confirm which of `LikeLeft`/`LikeRight` yields `value%` vs `%value` and map `_starts_with`/`_ends_with` accordingly. `In`/`Nin` value formatting (comma-joined string vs typed list) must match SqlSugar — verify with a focused test.

- [ ] **Step 4: Run — verify pass**

Run: `dotnet test --filter FullyQualifiedName~ConditionalModelTranslatorTests`
Expected: PASS (3).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Query/ConditionalModelTranslator.cs tests/Struo.Tests/Query/ConditionalModelTranslatorTests.cs
git commit -m "feat: add AST-to-SqlSugar conditional-model translator"
```

---

### Task 6: SqlSugarItemRepository (query + CRUD by runtime type)

**Files:**
- Create: `src/Struo.Application/Query/IItemRepository.cs`, `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs`, `src/Struo.Infrastructure/Security/AllowAllPermissionService.cs`
- Test: `tests/Struo.Tests/Query/SqlSugarItemRepositoryTests.cs`

**Interfaces:**
- Consumes: `QueryModel`, `EntityDescriptor`/`IEntityRegistry`, `ConditionalModelTranslator`, `ISqlSugarClient`.
- Produces:
  - `sealed record QueryResult(IReadOnlyList<object> Rows, int Total)` + `interface IItemRepository` (signatures below).
  - `SqlSugarItemRepository : IItemRepository`; `AllowAllPermissionService : IPermissionService`.

- [ ] **Step 1: Create the data port + allow-all permission**

```csharp
// src/Struo.Application/Query/IItemRepository.cs
using Struo.Domain.Query;

namespace Struo.Application.Query;

public sealed record QueryResult(IReadOnlyList<object> Rows, int Total);

public interface IItemRepository
{
    Task<QueryResult> QueryAsync(string collection, QueryModel query, IReadOnlyList<string> searchableFields, CancellationToken ct = default);
    Task<object?> GetByIdAsync(string collection, string id, CancellationToken ct = default);
    Task<object> CreateAsync(string collection, object entity, CancellationToken ct = default);
    Task<object?> UpdateAsync(string collection, string id, object entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default);
}
```

```csharp
// src/Struo.Infrastructure/Security/AllowAllPermissionService.cs
using Struo.Application.Security;

namespace Struo.Infrastructure.Security;

public sealed class AllowAllPermissionService : IPermissionService
{
    public bool CanRead(string collection) => true;
    public bool CanWrite(string collection) => true;
    public bool CanDelete(string collection) => true;
    public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
        allFieldNames.ToList();
}
```

- [ ] **Step 2: Write the failing repository integration tests**

```csharp
// tests/Struo.Tests/Query/SqlSugarItemRepositoryTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class SqlSugarItemRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly IItemRepository _repo;

    public SqlSugarItemRepositoryTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor("tester"));
        _db.CodeFirst.InitTables<Article>();
        var descriptors = MetadataScanner.ScanDescriptors([typeof(Article)]);
        _repo = new SqlSugarItemRepository(_db, new EntityRegistry(descriptors));
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Create_then_get_returns_entity_with_audit()
    {
        var created = (Article)await _repo.CreateAsync("article", new Article { Title = "Hello", Status = "draft" });
        created.Id.Should().BeGreaterThan(0);

        var fetched = (Article?)await _repo.GetByIdAsync("article", created.Id.ToString());
        fetched.Should().NotBeNull();
        fetched!.Title.Should().Be("Hello");
        fetched.CreatedBy.Should().Be("tester");
    }

    [Fact]
    public async Task Query_filters_and_paginates()
    {
        for (var i = 0; i < 5; i++)
            await _repo.CreateAsync("article", new Article { Title = $"T{i}", Status = i % 2 == 0 ? "published" : "draft" });

        var q = new QueryModel(null,
            new ComparisonFilter("status", QueryOperator.Eq, "published"),
            [new SortField("title", false)], 2, 0, null);

        var result = await _repo.QueryAsync("article", q, []);
        result.Total.Should().Be(3);
        result.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task Update_changes_fields_and_delete_removes()
    {
        var created = (Article)await _repo.CreateAsync("article", new Article { Title = "Old", Status = "draft" });
        var updated = (Article?)await _repo.UpdateAsync("article", created.Id.ToString(),
            new Article { Title = "New", Status = "published" });
        updated!.Title.Should().Be("New");

        (await _repo.DeleteAsync("article", created.Id.ToString())).Should().BeTrue();
        (await _repo.GetByIdAsync("article", created.Id.ToString())).Should().BeNull();
    }
}
```

- [ ] **Step 3: Run — verify fail**

Run: `dotnet test --filter FullyQualifiedName~SqlSugarItemRepositoryTests`
Expected: FAIL — `SqlSugarItemRepository` missing.

- [ ] **Step 4: Implement the repository (generic-by-type via reflection)**

```csharp
// src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs
using System.Reflection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

public sealed class SqlSugarItemRepository(ISqlSugarClient db, IEntityRegistry registry) : IItemRepository
{
    public Task<QueryResult> QueryAsync(string collection, QueryModel query,
        IReadOnlyList<string> searchableFields, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var conditionals = ConditionalModelTranslator.Translate(query.Filter, query.Search, searchableFields, d, db);
        var orderBy = BuildOrderBy(query.Sort, d);
        var method = typeof(SqlSugarItemRepository)
            .GetMethod(nameof(RunQuery), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(d.EntityType);
        var result = (QueryResult)method.Invoke(this, [conditionals, orderBy, query.Limit, query.Offset])!;
        return Task.FromResult(result);
    }

    private QueryResult RunQuery<T>(List<IConditionalModel> conditionals, string? orderBy, int limit, int offset) where T : class, new()
    {
        var pageNumber = (offset / Math.Max(1, limit)) + 1;
        var total = 0;
        var queryable = db.Queryable<T>().Where(conditionals);
        if (!string.IsNullOrWhiteSpace(orderBy)) queryable = queryable.OrderBy(orderBy);
        var rows = queryable.ToPageList(pageNumber, limit, ref total);
        return new QueryResult(rows.Cast<object>().ToList(), total);
    }

    public Task<object?> GetByIdAsync(string collection, string id, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var method = typeof(SqlSugarItemRepository)
            .GetMethod(nameof(GetByIdGeneric), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(d.EntityType);
        return Task.FromResult((object?)method.Invoke(this, [ConvertId(id, d)]));
    }

    private object? GetByIdGeneric<T>(object id) where T : class, new() => db.Queryable<T>().InSingle(id);

    public Task<object> CreateAsync(string collection, object entity, CancellationToken ct = default)
    {
        var created = db.Insertable(entity).ExecuteReturnEntity();
        return Task.FromResult(created);
    }

    public async Task<object?> UpdateAsync(string collection, string id, object entity, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var existing = await GetByIdAsync(collection, id, ct);
        if (existing is null) return null;
        d.EntityType.GetProperty(d.IdProperty)!.SetValue(entity, ConvertId(id, d));
        db.Updateable(entity).ExecuteCommand();
        return await GetByIdAsync(collection, id, ct);
    }

    public async Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var existing = await GetByIdAsync(collection, id, ct);
        if (existing is null) return false;
        var method = typeof(SqlSugarItemRepository)
            .GetMethod(nameof(DeleteGeneric), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(d.EntityType);
        method.Invoke(this, [ConvertId(id, d)]);
        return true;
    }

    private void DeleteGeneric<T>(object id) where T : class, new() => db.Deleteable<T>().In(id).ExecuteCommand();

    private EntityDescriptor Descriptor(string collection) =>
        registry.Get(collection) ?? throw new InvalidOperationException($"Unknown collection '{collection}'.");

    private static object ConvertId(string id, EntityDescriptor d)
    {
        var idType = d.EntityType.GetProperty(d.IdProperty)!.PropertyType;
        return Convert.ChangeType(id, Nullable.GetUnderlyingType(idType) ?? idType);
    }

    private string? BuildOrderBy(IReadOnlyList<SortField> sort, EntityDescriptor d)
    {
        if (sort.Count == 0) return null;
        var parts = sort.Select(s =>
        {
            var prop = d.FieldToProperty.TryGetValue(s.Field, out var p) ? p : s.Field;
            var col = db.EntityMaintenance.GetDbColumnName(prop, d.EntityType);
            return $"{col} {(s.Descending ? "DESC" : "ASC")}";
        });
        return string.Join(", ", parts);
    }
}
```

> Verify `ExecuteReturnEntity`, `InSingle(object)`, `Deleteable<T>().In(object)`, `ToPageList(int,int,ref int)`, and `Queryable<T>().Where(List<IConditionalModel>)` against the installed SqlSugarCore; adjust to the package while preserving behavior. The order-by string uses validated column names only (no user-supplied SQL).

- [ ] **Step 5: Run — verify pass**

Run: `dotnet test --filter FullyQualifiedName~SqlSugarItemRepositoryTests`
Expected: PASS (3). Then `dotnet test` → all green.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Application/Query/IItemRepository.cs src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs src/Struo.Infrastructure/Security/AllowAllPermissionService.cs tests/Struo.Tests/Query/SqlSugarItemRepositoryTests.cs
git commit -m "feat: add SqlSugar item repository (dynamic query + CRUD by runtime type)"
```

---

### Task 7: ItemService (orchestration, projection, envelope)

**Files:**
- Create: `src/Struo.Application/Query/ItemService.cs`
- Test: `tests/Struo.Tests/Query/ItemServiceTests.cs`

**Interfaces:**
- Consumes: `IItemRepository`, `IMetadataProvider`, `IEntityRegistry`, `IPermissionService`, `StruoQueryOptions`, `QueryParser`, `QueryValidator`, `CollectionNotFoundException`.
- Produces:
  - `sealed record PagedResult(IReadOnlyList<IReadOnlyDictionary<string,object?>> Data, int Total, int Limit, int Offset)`
  - `ItemService` with `QueryAsync(string, QueryModel, CancellationToken)`, `GetAsync(string, string, CancellationToken)`, `CreateAsync(string, JsonElement, CancellationToken)`, `UpdateAsync(string, string, JsonElement, CancellationToken)`, `DeleteAsync(string, string, CancellationToken)`. Throws `QueryException` (400), `CollectionNotFoundException` (404); returns null for not-found id.

- [ ] **Step 1: Create the service**

```csharp
// src/Struo.Application/Query/ItemService.cs
using System.Text.Json;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public sealed record PagedResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Data, int Total, int Limit, int Offset);

public sealed class ItemService(
    IItemRepository repository,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IPermissionService permissions,
    StruoQueryOptions options)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult> QueryAsync(string collection, QueryModel raw, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new QueryException("Read not permitted.");
        var validated = QueryValidator.Validate(raw, meta, options);
        var searchable = QueryValidator.SearchableFields(meta);
        var result = await repository.QueryAsync(collection, validated, searchable, ct);
        var rows = result.Rows.Select(r => Project(r, meta, validated.Fields)).ToList();
        return new PagedResult(rows, result.Total, validated.Limit, validated.Offset);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        var entity = await repository.GetByIdAsync(collection, id, ct);
        return entity is null ? null : Project(entity, meta, null);
    }

    public async Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new QueryException("Write not permitted.");
        var entity = Deserialize(collection, body, meta);
        var created = await repository.CreateAsync(collection, entity, ct);
        return Project(created, meta, null);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new QueryException("Write not permitted.");
        var entity = Deserialize(collection, body, meta);
        var updated = await repository.UpdateAsync(collection, id, entity, ct);
        return updated is null ? null : Project(updated, meta, null);
    }

    public Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        _ = Meta(collection);
        if (!permissions.CanDelete(collection)) throw new QueryException("Delete not permitted.");
        return repository.DeleteAsync(collection, id, ct);
    }

    private CollectionMetadata Meta(string collection) =>
        metadata.GetCollection(collection) ?? throw new CollectionNotFoundException(collection);

    private object Deserialize(string collection, JsonElement body, CollectionMetadata meta)
    {
        var d = registry.Get(collection) ?? throw new CollectionNotFoundException(collection);
        var entity = body.Deserialize(d.EntityType, JsonOpts)
                     ?? throw new QueryException("Request body could not be parsed.");

        // strip system/read-only fields (audit AOP / identity own them); only nullable props can be nulled
        foreach (var field in meta.Fields.Where(f => f.IsSystem || f.ReadOnly))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;
            var canBeNull = !pi.PropertyType.IsValueType || Nullable.GetUnderlyingType(pi.PropertyType) is not null;
            if (canBeNull) pi.SetValue(entity, null);
        }

        // required validation
        foreach (var field in meta.Fields.Where(f => f.Required))
        {
            var pi = d.FieldToProperty.TryGetValue(field.Name, out var prop) ? d.EntityType.GetProperty(prop) : null;
            var value = pi?.GetValue(entity);
            if (value is null || (value is string s && string.IsNullOrWhiteSpace(s)))
                throw new QueryException($"Field '{field.Name}' is required.");
        }
        return entity;
    }

    private IReadOnlyDictionary<string, object?> Project(object entity, CollectionMetadata meta, IReadOnlyList<string>? fields)
    {
        var d = registry.Get(meta.Name)!;
        var dict = new Dictionary<string, object?>();

        const string idKey = "id";
        dict[idKey] = d.EntityType.GetProperty(d.IdProperty)?.GetValue(entity);

        var wanted = fields is { Count: > 0 } ? fields.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        var readable = permissions.ReadableFields(meta.Name, meta.Fields.Select(f => f.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var field in meta.Fields)
        {
            if (field.Hidden) continue;
            if (field.Name == idKey) continue;
            if (wanted is not null && !wanted.Contains(field.Name)) continue;
            if (!readable.Contains(field.Name)) continue;
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            dict[field.Name] = d.EntityType.GetProperty(prop)?.GetValue(entity);
        }
        return dict;
    }
}
```

> System/read-only stripping only nulls nullable/reference properties; a non-nullable value type (e.g. `DateTime CreatedAt`) keeps its default, and the audit AOP overwrites it on insert/update. The Task 8 integration test asserts the created row's `createdBy` equals the current user (not client-supplied), confirming this.

- [ ] **Step 2: Write a projection test**

```csharp
// tests/Struo.Tests/Query/ItemServiceTests.cs
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using SqlSugar;
using Xunit;

namespace Struo.Tests.Query;

public class ItemServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor("tester"));
        db.CodeFirst.InitTables<Article>();

        var provider = new CachedMetadataProvider(MetadataScanner.Scan(typeof(Article).Assembly));
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors([typeof(Article), typeof(Tag)]));
        var repo = new SqlSugarItemRepository(db, registry);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(), new StruoQueryOptions());
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Create_projects_id_audit_and_camelCase_fields()
    {
        using var body = System.Text.Json.JsonDocument.Parse("""{"title":"Hello","status":"draft"}""");
        var dict = await _svc.CreateAsync("article", body.RootElement);

        dict.Should().ContainKey("id");
        dict["title"].Should().Be("Hello");
        dict.Should().ContainKey("createdAt");
        dict.Should().ContainKey("seoTitle");
    }
}
```

- [ ] **Step 3: Run — verify fail then pass**

Run: `dotnet test --filter FullyQualifiedName~ItemServiceTests`
Expected: FAIL (service missing) → PASS (1). Then `dotnet test` → all green.

- [ ] **Step 4: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/ItemServiceTests.cs
git commit -m "feat: add ItemService (validation, CRUD orchestration, metadata projection)"
```

---

### Task 8: ItemsController + DI wiring + HTTP integration

**Files:**
- Create: `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs`, `src/Struo.Api/Controllers/ItemsController.cs`
- Modify: `src/Struo.Api/Program.cs`
- Test: `tests/Struo.Tests/Api/ItemsEndpointTests.cs`

**Interfaces:**
- Consumes: `ItemService`, `QueryParser`, `QueryException`/`CollectionNotFoundException`, repository/permission/options.
- Produces: `AddStruoData(IConfiguration)`; the `/api/items/...` endpoints.

- [ ] **Step 1: Create the DI extension**

```csharp
// src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Application.Security;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Security;

namespace Struo.Infrastructure.DependencyInjection;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddStruoData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StruoQueryOptions>(configuration.GetSection(StruoQueryOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<StruoQueryOptions>>().Value);
        services.AddSingleton<IPermissionService, AllowAllPermissionService>();
        services.AddScoped<IItemRepository, SqlSugarItemRepository>();
        services.AddScoped<ItemService>();
        return services;
    }
}
```

- [ ] **Step 2: Write the failing HTTP integration tests**

```csharp
// tests/Struo.Tests/Api/ItemsEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class ItemsEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task Crud_round_trip_with_envelope_and_audit()
    {
        var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/items/article", new { title = "Hello", status = "draft" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdData = Root(await create.Content.ReadAsStringAsync()).GetProperty("data");
        var id = createdData.GetProperty("id").GetInt64();
        createdData.GetProperty("createdBy").GetString().Should().Be("system");

        var get = await client.GetAsync($"/api/items/article/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var put = await client.PutAsJsonAsync($"/api/items/article/{id}", new { title = "Updated", status = "published" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await put.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("title").GetString().Should().Be("Updated");

        (await client.DeleteAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_filters_sorts_paginates_with_meta()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < 4; i++)
            await client.PostAsJsonAsync("/api/items/article", new { title = $"Post{i}", status = i % 2 == 0 ? "published" : "draft" });

        var resp = await client.GetAsync("/api/items/article?filter[status][_eq]=published&sort=-title&limit=1&offset=0&fields=id,title");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.GetProperty("meta").GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        var first = root.GetProperty("data")[0];
        first.TryGetProperty("status", out _).Should().BeFalse();
        first.TryGetProperty("title", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_field_returns_400()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/items/article?filter[ghost][_eq]=x");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_collection_returns_404()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/api/items/nope")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 3: Run — verify fail**

Run: `dotnet test --filter FullyQualifiedName~ItemsEndpointTests`
Expected: FAIL — no `/api/items` routes.

- [ ] **Step 4: Create the controller**

```csharp
// src/Struo.Api/Controllers/ItemsController.cs
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Struo.Application.Query;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/items/{collection}")]
public sealed class ItemsController(ItemService items) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string collection, CancellationToken ct)
    {
        var qs = Request.Query.ToDictionary(k => k.Key, v => (string?)v.Value.ToString());
        var raw = QueryParser.ParseQueryString(qs);
        var result = await items.QueryAsync(collection, raw, ct);
        return Ok(new { data = result.Data, meta = new { total = result.Total, limit = result.Limit, offset = result.Offset } });
    }

    [HttpPost("query")]
    public async Task<IActionResult> Query(string collection, [FromBody] JsonElement body, CancellationToken ct)
    {
        var raw = QueryParser.ParseEnvelope(body);
        var result = await items.QueryAsync(collection, raw, ct);
        return Ok(new { data = result.Data, meta = new { total = result.Total, limit = result.Limit, offset = result.Offset } });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string collection, string id, CancellationToken ct)
    {
        var item = await items.GetAsync(collection, id, ct);
        return item is null ? NotFound() : Ok(new { data = item });
    }

    [HttpPost]
    public async Task<IActionResult> Create(string collection, [FromBody] JsonElement body, CancellationToken ct)
    {
        var created = await items.CreateAsync(collection, body, ct);
        var id = created.TryGetValue("id", out var idValue) ? idValue : null;
        return Created($"/api/items/{collection}/{id}", new { data = created });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string collection, string id, [FromBody] JsonElement body, CancellationToken ct)
    {
        var updated = await items.UpdateAsync(collection, id, body, ct);
        return updated is null ? NotFound() : Ok(new { data = updated });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string collection, string id, CancellationToken ct)
    {
        var ok = await items.DeleteAsync(collection, id, ct);
        return ok ? NoContent() : NotFound();
    }
}
```

- [ ] **Step 5: Wire DI + exception mapping in Program.cs**

After `builder.Services.AddStruoMetadata(...)`, add:

```csharp
    builder.Services.AddStruoData(builder.Configuration);
```

Add exception-mapping middleware before `app.MapControllers()` (and after `app.UseSerilogRequestLogging()`):

```csharp
    app.Use(async (context, next) =>
    {
        try { await next(); }
        catch (Struo.Domain.Query.CollectionNotFoundException ex)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = new { message = ex.Message } });
        }
        catch (Struo.Domain.Query.QueryException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = new { message = ex.Message } });
        }
    });
```

`using Struo.Infrastructure.DependencyInjection;` is already present (it is the namespace of `AddStruoMetadata`/`AddStruoInfrastructure`).

- [ ] **Step 6: Run — verify pass**

Run: `dotnet test --filter FullyQualifiedName~ItemsEndpointTests`
Expected: PASS (4).

- [ ] **Step 7: Full suite**

Run: `dotnet test`
Expected: PASS (all — Phases 0+1+2).

- [ ] **Step 8: Commit**

```bash
git add src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs src/Struo.Api/Controllers/ItemsController.cs src/Struo.Api/Program.cs tests/Struo.Tests/Api/ItemsEndpointTests.cs
git commit -m "feat: expose /api/items CRUD + query endpoints with {data,meta} envelope"
```

---

## Self-Review

**1. Spec coverage** (design → tasks):

| Spec item | Task |
|---|---|
| DSL AST + operators (§7.3/7.5) | Task 1 |
| Entity registry (type/field/PK) | Task 2 |
| StruoQueryOptions (DoS caps) | Task 2, 4 |
| Parser (JSON + bracket) (§7.2) | Task 3 |
| Validation: whitelist, dotted-reject, caps, search (§7.7) | Task 4 |
| Permission seam (allow-all) (§13.3) | Task 4, 6 |
| AST→SqlSugar conditionals, zero vendor SQL (§15) | Task 5 |
| Repository: query + CRUD by type | Task 6 |
| ItemService: orchestration, projection, envelope | Task 7 |
| Endpoints (CRUD + /query), {data,meta}, 400/404 | Task 8 |
| camelCase + projection (hidden excluded, id always) | Task 7 |
| Hard delete | Task 6, 8 |

No gaps.

**2. Placeholder scan:** No "TBD"/"implement later"; every code step has full code. SqlSugar member-name verification notes are explicit per §15 (install-latest) and caught by TDD.

**3. Type consistency:** `QueryModel`/`FilterNode`/`SortField`/`QueryOperator`/`LogicalOperator` (Task 1); `EntityDescriptor`(EntityType/FieldToProperty/IdProperty), `IEntityRegistry.Get`, `MetadataScanner.ScanDescriptors` (Task 2); `QueryParser.ParseEnvelope/ParseQueryString` (Task 3); `QueryValidator.Validate/SearchableFields`, `IPermissionService` (Task 4); `ConditionalModelTranslator.Translate` (Task 5); `IItemRepository`/`QueryResult`/`SqlSugarItemRepository`/`AllowAllPermissionService` (Task 6); `ItemService`/`PagedResult`/`CollectionNotFoundException` (Task 1+7); `AddStruoData`/`ItemsController` (Task 8) — consistent across tasks.

**Known verify-against-package points (flagged inline):** SqlSugar `ConditionalType`/`WhereType`/`ConditionalCollections` members, `ToPageList(ref total)`, `InSingle`, `Deleteable().In`, `ExecuteReturnEntity`, `EntityMaintenance.GetDbColumnName` arg order. TDD catches mismatches at first run.
