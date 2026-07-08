# Phase 8 — GraphQL (read-only) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose every discovered `[CmsCollection]` as a strongly-typed, introspectable, **read-only** GraphQL API at `/graphql`, reusing the existing metadata + query DSL + `ItemService` read pipeline + RBAC.

**Architecture:** A new `Struo.Api/GraphQl/` layer only. A HotChocolate `ITypeModule` reads the startup-cached `IMetadataProvider` and emits one object type + one input filter type + two root query fields (`x(id)` and pluralised `xs(...)`) per collection. Resolvers delegate through an **Api-owned port** `IGraphQlDataSource` (adapter over the concrete `ItemService`) so Application/Infrastructure stay untouched and untestable-sealed-class problems are avoided. Relations expand **one level** via the existing `deep` mechanism (`DeepSpec` built from the GraphQL selection set); File/Image/Files resolve to `File` nodes via a batched `BatchDataLoader`.

**Tech Stack:** .NET 10, C# latest, HotChocolate v15 (`HotChocolate.AspNetCore`), xUnit (existing `tests/Struo.Tests`), SQLite for automated tests, real PostgreSQL for the live gate.

## Global Constraints

- **Read-only.** No mutations. No create/update/delete resolvers. Writes stay on REST.
- **Dependency rule (§2).** New code lives **only** in `src/Struo.Api/GraphQl/` (plus `Program.cs` wiring + tests). Domain / Application / Infrastructure files are **not modified** and gain **no new packages**.
- **Single-level relations.** Relations expand one level from the queried collection; a relation-of-a-relation resolves `null`/empty. File/Image/Files resolution is exempt (any depth).
- **Package versions never hand-authored (§17.5).** Install via `dotnet add package HotChocolate.AspNetCore` (latest 15.1.x); the version lands in `Directory.Packages.props` from the tool, not typed from memory.
- **camelCase everywhere.** GraphQL field names reuse the projection's camelCase keys; enum values (none generated this phase) would be camelCase to match the REST JSON config.
- **HotChocolate v15 API surface** (verified against 15.1.x docs): dynamic types are built from `ObjectTypeDefinition` / `ObjectFieldDefinition` / `InputObjectTypeDefinition` / `InputFieldDefinition` / `ArgumentDefinition` (namespace `HotChocolate.Types.Descriptors.Definitions`) + `TypeReference.Parse("SDL")` (namespace `HotChocolate.Types.Descriptors`), realised via `ObjectType.CreateUnsafe(def)` / `ObjectTypeExtension.CreateUnsafe(def)` / `InputObjectType.CreateUnsafe(def)`, emitted from an `ITypeModule`. Resolver context: `ctx.ArgumentValue<T>(name)`, `ctx.Service<T>()`, `ctx.Parent<T>()`, `ctx.RequestAborted`, `ctx.GetSelections(IObjectType, ISelection?)`, `ctx.Selection.Field.Type.NamedType()`, `ctx.BatchDataLoader<TKey,TValue>(fetch).LoadAsync(key, ct)`. If a name does not resolve against the installed package, use the build-error-resolver agent to reconcile — do **not** invent alternatives.
- **DI scope.** GraphQL query resolvers must share one request scope so the per-request `ICurrentPermissions` snapshot and scoped `ItemService` are consistent: set `DefaultQueryDependencyInjectionScope = DependencyInjectionScope.Request`.
- **Number scalar by CLR type:** `int`/`short`/`byte` → `Int`; `long` → `Long`; `decimal`/`double`/`float` → `Float`. `version` → `Long`. `id` → `ID`.
- **Excluded field interfaces** (never in schema): `Hidden`, `Divider`, `Password`.
- **Verification baseline to preserve:** backend `dotnet build -warnaserror` clean; `dotnet test` stays green (378 + new tests). Frontend untouched (237).

---

## File Structure

**Create (all under `src/Struo.Api/GraphQl/`):**
- `GraphQlDataSource.cs` — the Api-owned port `IGraphQlDataSource` + `ItemServiceGraphQlDataSource` adapter (thin pass-through to `ItemService.QueryAsync`/`GetAsync`).
- `SchemaTypeMapper.cs` — pure functions: collection→GraphQL type name, collection→plural query field name, `FieldMetadata`(+CLR property type)→SDL type string, relation→SDL type string, file companion field naming, excluded-interface set.
- `FilterInputTranslator.cs` — pure: a submitted filter dictionary → `FilterNode` tree; and the list of `(fieldName, operatorInputTypeName)` a collection's filter input needs.
- `GraphQlQueryBuilder.cs` — pure: resolver args (`limit/offset/sort/search/filter`) → `QueryModel`; selection set → `DeepSpec` (which relations requested).
- `SharedFilterTypes.cs` — builds the reusable per-scalar operator input types (`StringFilter`, `IntFilter`, `FloatFilter`, `DateTimeFilter`, `BooleanFilter`, `IdFilter`) as `InputObjectType` definitions.
- `CollectionSchemaBuilder.cs` — builds, for one `CollectionMetadata`: the object type (scalar + relation + file + repeater-nested fields), the `XList` wrapper type, the `XFilterInput` type, and the two root Query fields (with resolvers). Depends on `SchemaTypeMapper`, `GraphQlQueryBuilder`, `FilterInputTranslator`.
- `StruoTypeModule.cs` — `ITypeModule`; iterates `IMetadataProvider.GetCollections()`, calls `CollectionSchemaBuilder` per collection, plus the shared filter types + the `Query` extension, returns all `ITypeSystemMember`s.
- `StruoErrorFilter.cs` — `IErrorFilter` mapping domain exceptions → GraphQL error `code`.
- `GraphQlServiceCollectionExtensions.cs` — `AddStruoGraphQl(this IServiceCollection)` + `MapStruoGraphQl(this WebApplication)` (or endpoint options helper).

**Modify:**
- `src/Struo.Api/Program.cs` — call `AddStruoGraphQl()` after `AddStruoData(...)`; map the endpoint after `MapControllers()`.
- `Directory.Packages.props` — the `HotChocolate.AspNetCore` version pin (written by `dotnet add package`).

**Test (all under `tests/Struo.Tests/`, mirror the existing folder convention — e.g. `GraphQl/`):**
- `GraphQl/SchemaTypeMapperTests.cs`, `GraphQl/FilterInputTranslatorTests.cs`, `GraphQl/GraphQlQueryBuilderTests.cs`, `GraphQl/StruoErrorFilterTests.cs` — pure unit tests.
- `GraphQl/GraphQlSchemaTests.cs` — build schema from a fake metadata provider, assert SDL shape + exhaustiveness.
- `GraphQl/GraphQlExecutionTests.cs` — execute queries against the executor with a fake `IGraphQlDataSource`.
- `GraphQl/FakeGraphQlDataSource.cs`, `GraphQl/FakeMetadataFixtures.cs` — test doubles (canned dicts + hand-built `CollectionMetadata` + a fake `IEntityRegistry`).

---

## Task 1: Package, server skeleton, endpoint, error filter

Installs HotChocolate, stands up an empty-but-valid schema at `/graphql`, and adds the exception→error-code filter. Deliverable: the endpoint responds to `{ _service }` and the error filter maps each domain exception to its `code`.

**Files:**
- Modify: `Directory.Packages.props` (version pin via CLI), `src/Struo.Api/Struo.Api.csproj` (PackageReference), `src/Struo.Api/Program.cs`
- Create: `src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs`, `src/Struo.Api/GraphQl/StruoErrorFilter.cs`
- Test: `tests/Struo.Tests/GraphQl/StruoErrorFilterTests.cs`

**Interfaces:**
- Produces: `GraphQlServiceCollectionExtensions.AddStruoGraphQl(IServiceCollection, IWebHostEnvironment)` and `MapStruoGraphQl(WebApplication)`; `StruoErrorFilter : IErrorFilter`.

- [ ] **Step 1: Install the package (CLI writes the version — do not hand-author).**

Run:
```bash
cd /d/dotnet/struo-cms
dotnet add src/Struo.Api/Struo.Api.csproj package HotChocolate.AspNetCore
```
Expected: restore succeeds; `Directory.Packages.props` gains a `<PackageVersion Include="HotChocolate.AspNetCore" Version="15.1.*" />` line (exact patch from the tool). If central package management complains, move the version to `Directory.Packages.props` and leave a versionless `<PackageReference>` in the csproj (match the repo's existing pattern).

- [ ] **Step 2: Write the failing error-filter test.**

Create `tests/Struo.Tests/GraphQl/StruoErrorFilterTests.cs`:
```csharp
using HotChocolate;
using Struo.Api.GraphQl;
using Struo.Application.Query;      // QueryException, CollectionNotFoundException, RelationConflictException, ConcurrencyConflictException
using Struo.Application.Security;   // PermissionDeniedException
using Xunit;

namespace Struo.Tests.GraphQl;

public class StruoErrorFilterTests
{
    private static IError Wrap(Exception ex) =>
        ErrorBuilder.New().SetMessage("original").SetException(ex).Build();

    private readonly StruoErrorFilter _filter = new(NullLoggerFactory.Instance.CreateLogger<StruoErrorFilter>());

    [Fact]
    public void QueryException_maps_to_BAD_USER_INPUT()
    {
        var e = _filter.OnError(Wrap(new QueryException("bad")));
        Assert.Equal("BAD_USER_INPUT", e.Code);
        Assert.Equal("bad", e.Message);
    }

    [Fact]
    public void CollectionNotFound_maps_to_NOT_FOUND()
        => Assert.Equal("NOT_FOUND", _filter.OnError(Wrap(new CollectionNotFoundException("x"))).Code);

    [Fact]
    public void PermissionDenied_maps_to_FORBIDDEN()
        => Assert.Equal("FORBIDDEN", _filter.OnError(Wrap(new PermissionDeniedException("no"))).Code);

    [Fact]
    public void Conflict_maps_to_CONFLICT()
        => Assert.Equal("CONFLICT", _filter.OnError(Wrap(new RelationConflictException("c"))).Code);

    [Fact]
    public void Unknown_exception_is_masked_as_INTERNAL_SERVER_ERROR()
    {
        var e = _filter.OnError(Wrap(new InvalidOperationException("secret detail")));
        Assert.Equal("INTERNAL_SERVER_ERROR", e.Code);
        Assert.DoesNotContain("secret", e.Message);
    }

    [Fact]
    public void Null_exception_error_is_returned_unchanged()
    {
        var validationError = ErrorBuilder.New().SetMessage("parse error").Build();
        Assert.Same(validationError, _filter.OnError(validationError));
    }
}
```
Add `using Microsoft.Extensions.Logging.Abstractions;` for `NullLoggerFactory`.

> Note: confirm the exact exception type names by grepping `src/Struo.Application` (they were seen as `QueryException`, `CollectionNotFoundException`, `RelationConflictException`, `ConcurrencyConflictException`, `PermissionDeniedException`). Use `ConcurrencyConflictException` in place of `RelationConflictException` in one test if you prefer; both must map to `CONFLICT`.

- [ ] **Step 3: Run the test to verify it fails.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~StruoErrorFilterTests`
Expected: FAIL — `StruoErrorFilter` does not exist (compile error).

- [ ] **Step 4: Implement `StruoErrorFilter`.**

Create `src/Struo.Api/GraphQl/StruoErrorFilter.cs`:
```csharp
using HotChocolate;
using Microsoft.Extensions.Logging;
using Struo.Application.Query;
using Struo.Application.Security;

namespace Struo.Api.GraphQl;

/// <summary>
/// Maps StruoCMS domain exceptions to GraphQL errors carrying a stable <c>code</c> extension,
/// mirroring the REST exception→HTTP middleware. Unmapped exceptions are masked (no internal
/// detail leaked) and logged server-side.
/// </summary>
public sealed class StruoErrorFilter(ILogger<StruoErrorFilter> logger) : IErrorFilter
{
    public IError OnError(IError error)
    {
        switch (error.Exception)
        {
            case null:
                return error; // validation/parse errors — leave as-is
            case QueryException:
                return error.WithCode("BAD_USER_INPUT");
            case CollectionNotFoundException:
                return error.WithCode("NOT_FOUND");
            case PermissionDeniedException:
                return error.WithCode("FORBIDDEN");
            case RelationConflictException:
            case ConcurrencyConflictException:
                return error.WithCode("CONFLICT");
            default:
                logger.LogError(error.Exception, "Unhandled GraphQL resolver exception");
                return error
                    .WithMessage("An internal error occurred.")
                    .WithCode("INTERNAL_SERVER_ERROR")
                    .RemoveException();
        }
    }
}
```
> `error.WithCode(x)` sets both the `code` extension and `IError.Code`. If `RemoveException()` is unavailable in the installed version, use `.WithException(null!)` — the build-error-resolver will confirm.

- [ ] **Step 5: Create the DI + endpoint extension with a valid empty schema.**

Create `src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs`:
```csharp
using HotChocolate.AspNetCore;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Struo.Api.GraphQl;

public static class GraphQlServiceCollectionExtensions
{
    public static IServiceCollection AddStruoGraphQl(this IServiceCollection services, IHostEnvironment env)
    {
        services
            .AddGraphQLServer()
            .AddQueryType(d => d
                .Name("Query")
                // Anchor field so the schema is always valid even before the type module adds
                // collection fields (GraphQL requires Query to have >=1 field).
                .Field("_service").Type<StringType>().Resolve(_ => "StruoCMS GraphQL"))
            .AddErrorFilter<StruoErrorFilter>()
            .AddType<LongType>()
            .AddType<DateTimeType>()
            .AddType<DateType>()
            .AddType<UuidType>()
            .AddType<AnyType>()            // JSON scalar (SDL name "Any")
            .AddJsonTypeConverter()        // lets resolvers return dictionaries/JsonElement for Any
            .AddMaxExecutionDepthRule(12, skipIntrospectionFields: true)
            .AllowIntrospection(env.IsDevelopment())
            .ModifyOptions(o => o.DefaultQueryDependencyInjectionScope =
                HotChocolate.Execution.Options.DependencyInjectionScope.Request);

        return services;
    }

    public static void MapStruoGraphQl(this WebApplication app)
        => app.MapGraphQL("/graphql")
              .WithOptions(new GraphQLServerOptions
              {
                  Tool = { Enable = app.Environment.IsDevelopment() } // Nitro IDE dev-only
              });
}
```
> If `DependencyInjectionScope` lives in a different namespace in the installed package, let build-error-resolver fix the `using`. `AddJsonTypeConverter` may be an extension in `HotChocolate` root namespace.

- [ ] **Step 6: Wire into `Program.cs`.**

In `src/Struo.Api/Program.cs`, after the `AddStruoData(config)` line, add:
```csharp
builder.Services.AddStruoGraphQl(builder.Environment);
```
And after `app.MapControllers();`, add:
```csharp
app.MapStruoGraphQl();
```
Add `using Struo.Api.GraphQl;` at the top if not already covered by an implicit using.

- [ ] **Step 7: Run tests + build to verify green.**

Run:
```bash
dotnet build -warnaserror
dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~StruoErrorFilterTests
```
Expected: build clean; error-filter tests PASS.

- [ ] **Step 8: Commit.**

```bash
git add src/Struo.Api/GraphQl/ src/Struo.Api/Program.cs src/Struo.Api/Struo.Api.csproj Directory.Packages.props tests/Struo.Tests/GraphQl/StruoErrorFilterTests.cs
git commit -m "feat(graphql): server skeleton, /graphql endpoint, domain error filter"
```

---

## Task 2: `SchemaTypeMapper` (pure type mapping)

Pure, DB-free functions that decide GraphQL type names and SDL type strings. Testable without HotChocolate.

**Files:**
- Create: `src/Struo.Api/GraphQl/SchemaTypeMapper.cs`
- Test: `tests/Struo.Tests/GraphQl/SchemaTypeMapperTests.cs`

**Interfaces:**
- Produces:
  - `SchemaTypeMapper.TypeName(string collection)` → PascalCase (`"article"`→`"Article"`).
  - `SchemaTypeMapper.ListFieldName(string collection)` → plural camelCase (`"article"`→`"articles"`, `"category"`→`"categories"`).
  - `SchemaTypeMapper.SingleFieldName(string collection)` → camelCase singular (`"article"`→`"article"`).
  - `SchemaTypeMapper.RepeaterItemTypeName(string collection, string field)` → `"ArticleFaqsItem"`.
  - `SchemaTypeMapper.ScalarSdl(FieldInterface iface, Type? clrType)` → nullable SDL string (e.g. `"String"`, `"Int"`, `"Long"`, `"Float"`, `"Boolean"`, `"DateTime"`, `"Date"`, `"[String!]"`, `"Any"`, `"ID"`, `"[ID!]"`); returns `null` for excluded interfaces (`Hidden`/`Divider`/`Password`) and for `Repeater`/`Tags` (handled by the builder as named types).
  - `SchemaTypeMapper.IsExcluded(FieldInterface iface)` → bool.
  - `SchemaTypeMapper.Excluded` → the excluded set.

- [ ] **Step 1: Write the failing tests.**

Create `tests/Struo.Tests/GraphQl/SchemaTypeMapperTests.cs`:
```csharp
using Struo.Api.GraphQl;
using Struo.Domain.Metadata.Enums;
using Xunit;

namespace Struo.Tests.GraphQl;

public class SchemaTypeMapperTests
{
    [Theory]
    [InlineData("article", "Article")]
    [InlineData("file", "File")]
    [InlineData("userRole", "UserRole")]
    public void TypeName_is_pascal_case(string c, string expected)
        => Assert.Equal(expected, SchemaTypeMapper.TypeName(c));

    [Theory]
    [InlineData("article", "articles")]
    [InlineData("category", "categories")]
    [InlineData("box", "boxes")]
    [InlineData("dish", "dishes")]
    public void ListFieldName_pluralises(string c, string expected)
        => Assert.Equal(expected, SchemaTypeMapper.ListFieldName(c));

    [Theory]
    [InlineData(FieldInterface.Text, typeof(string), "String")]
    [InlineData(FieldInterface.RichText, typeof(string), "String")]
    [InlineData(FieldInterface.Number, typeof(int), "Int")]
    [InlineData(FieldInterface.Number, typeof(long), "Long")]
    [InlineData(FieldInterface.Number, typeof(decimal), "Float")]
    [InlineData(FieldInterface.Boolean, typeof(bool), "Boolean")]
    [InlineData(FieldInterface.DateTime, typeof(System.DateTime), "DateTime")]
    [InlineData(FieldInterface.Date, typeof(System.DateTime), "Date")]
    [InlineData(FieldInterface.Select, typeof(string), "String")]
    [InlineData(FieldInterface.MultiSelect, typeof(object), "[String!]")]
    [InlineData(FieldInterface.CheckboxGroup, typeof(object), "[String!]")]
    [InlineData(FieldInterface.Json, typeof(string), "Any")]
    [InlineData(FieldInterface.KeyValue, typeof(object), "Any")]
    [InlineData(FieldInterface.Image, typeof(System.Guid), "ID")]
    [InlineData(FieldInterface.File, typeof(System.Guid), "ID")]
    [InlineData(FieldInterface.Files, typeof(object), "[ID!]")]
    public void ScalarSdl_maps_interface_and_clr_type(FieldInterface iface, System.Type clr, string expected)
        => Assert.Equal(expected, SchemaTypeMapper.ScalarSdl(iface, clr));

    [Theory]
    [InlineData(FieldInterface.Hidden)]
    [InlineData(FieldInterface.Divider)]
    [InlineData(FieldInterface.Password)]
    public void Excluded_interfaces_return_null(FieldInterface iface)
    {
        Assert.True(SchemaTypeMapper.IsExcluded(iface));
        Assert.Null(SchemaTypeMapper.ScalarSdl(iface, typeof(string)));
    }

    [Theory]
    [InlineData(FieldInterface.Tags)]
    [InlineData(FieldInterface.Repeater)]
    public void Named_type_interfaces_return_null_scalar(FieldInterface iface)
        => Assert.Null(SchemaTypeMapper.ScalarSdl(iface, typeof(object)));
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~SchemaTypeMapperTests`
Expected: FAIL — `SchemaTypeMapper` not defined.

- [ ] **Step 3: Implement `SchemaTypeMapper`.**

Create `src/Struo.Api/GraphQl/SchemaTypeMapper.cs`:
```csharp
using System.Globalization;
using Struo.Domain.Metadata.Enums;

namespace Struo.Api.GraphQl;

/// <summary>
/// Pure mapping from CMS metadata to GraphQL SDL type strings and schema names.
/// No HotChocolate or DB dependency — this is the single source of truth for how a
/// FieldInterface (and, for numbers, its CLR type) becomes a GraphQL type.
/// </summary>
public static class SchemaTypeMapper
{
    public static readonly IReadOnlySet<FieldInterface> Excluded =
        new HashSet<FieldInterface> { FieldInterface.Hidden, FieldInterface.Divider, FieldInterface.Password };

    public static bool IsExcluded(FieldInterface iface) => Excluded.Contains(iface);

    public static string TypeName(string collection) => Pascal(collection);
    public static string SingleFieldName(string collection) => Camel(collection);
    public static string ListFieldName(string collection) => Pluralise(Camel(collection));
    public static string RepeaterItemTypeName(string collection, string field) => Pascal(collection) + Pascal(field) + "Item";

    /// <summary>
    /// Returns the nullable SDL type string for a scalar/list field, or <c>null</c> when the
    /// interface is excluded (Hidden/Divider/Password) or is a named-type interface
    /// (Tags/Repeater) that the schema builder handles separately.
    /// </summary>
    public static string? ScalarSdl(FieldInterface iface, Type? clrType) => iface switch
    {
        FieldInterface.Hidden or FieldInterface.Divider or FieldInterface.Password => null,
        FieldInterface.Tags or FieldInterface.Repeater => null,

        FieldInterface.Text or FieldInterface.Textarea or FieldInterface.RichText or FieldInterface.Markdown
            or FieldInterface.Code or FieldInterface.Slug or FieldInterface.Email or FieldInterface.Url
            or FieldInterface.Color or FieldInterface.Phone or FieldInterface.Select or FieldInterface.Radio => "String",

        FieldInterface.Number or FieldInterface.Slider or FieldInterface.Rating => NumberSdl(clrType),

        FieldInterface.Boolean or FieldInterface.Checkbox => "Boolean",
        FieldInterface.Date => "Date",
        FieldInterface.Time => "String",
        FieldInterface.DateTime => "DateTime",

        FieldInterface.MultiSelect or FieldInterface.CheckboxGroup => "[String!]",
        FieldInterface.Json or FieldInterface.KeyValue => "Any",

        FieldInterface.File or FieldInterface.Image => "ID",
        FieldInterface.Files => "[ID!]",
        FieldInterface.Uuid => "ID",
        _ => throw new NotSupportedException($"No GraphQL mapping for field interface '{iface}'.")
    };

    private static string NumberSdl(Type? clrType)
    {
        var t = clrType is null ? null : Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (t == typeof(int) || t == typeof(short) || t == typeof(byte)) return "Int";
        if (t == typeof(long)) return "Long";
        return "Float"; // decimal/double/float/unknown
    }

    private static string Pascal(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];
    }

    private static string Camel(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToLower(s[0], CultureInfo.InvariantCulture) + s[1..];
    }

    // Simple, predictable English pluralisation (documented so hosts can rely on it).
    private static string Pluralise(string s)
    {
        if (s.EndsWith("y", StringComparison.Ordinal) && s.Length > 1 && !"aeiou".Contains(s[^2]))
            return s[..^1] + "ies";
        if (s.EndsWith("s", StringComparison.Ordinal) || s.EndsWith("x", StringComparison.Ordinal) ||
            s.EndsWith("z", StringComparison.Ordinal) || s.EndsWith("ch", StringComparison.Ordinal) ||
            s.EndsWith("sh", StringComparison.Ordinal))
            return s + "es";
        return s + "s";
    }
}
```
> The `_ => throw` arm gives the exhaustiveness fail-fast: a future `FieldInterface` value with no mapping throws at schema-build time.

- [ ] **Step 4: Run to verify pass.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~SchemaTypeMapperTests`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/Struo.Api/GraphQl/SchemaTypeMapper.cs tests/Struo.Tests/GraphQl/SchemaTypeMapperTests.cs
git commit -m "feat(graphql): SchemaTypeMapper — FieldInterface→SDL type mapping"
```

---

## Task 3: `FilterInputTranslator` (filter dict → FilterNode)

Pure translation from a submitted GraphQL filter input (read back as a nested dictionary) into the existing `FilterNode` tree, plus the metadata that says which operator input a field uses.

**Files:**
- Create: `src/Struo.Api/GraphQl/FilterInputTranslator.cs`
- Test: `tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs`

**Interfaces:**
- Consumes: `Struo.Domain.Query.FilterNode`/`LogicalFilter`/`ComparisonFilter`/`LogicalOperator`/`QueryOperator`.
- Produces:
  - `FilterInputTranslator.Translate(IReadOnlyDictionary<string,object?>? filter)` → `FilterNode?` (null when filter null/empty).
  - `FilterInputTranslator.OperatorInputTypeName(FieldInterface iface, Type? clrType)` → one of `"StringFilter"`,`"IntFilter"`,`"FloatFilter"`,`"DateTimeFilter"`,`"BooleanFilter"`,`"IdFilter"`.
  - `FilterInputTranslator.OperatorTokens` — the map used by both the input-type builder and the translator: token (`"eq"`,`"neq"`,`"in"`,`"nin"`,`"lt"`,`"lte"`,`"gt"`,`"gte"`,`"contains"`,`"startsWith"`,`"endsWith"`,`"isNull"`) → `QueryOperator`.

> **Design of the submitted shape.** A filter input reads back (because its `RuntimeType` is a dictionary) as `{ "and": [ ... ], "or": [ ... ], "<field>": { "<opToken>": value } }`. `and`/`or` values are `IReadOnlyList<object?>` of nested filter dicts. Field values are nested dicts of opToken→value. `isNull: true` → `Null`; `isNull: false` → `NNull`.

- [ ] **Step 1: Write the failing tests.**

Create `tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs`:
```csharp
using Struo.Api.GraphQl;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.GraphQl;

public class FilterInputTranslatorTests
{
    [Fact]
    public void Null_or_empty_filter_returns_null()
    {
        Assert.Null(FilterInputTranslator.Translate(null));
        Assert.Null(FilterInputTranslator.Translate(new Dictionary<string, object?>()));
    }

    [Fact]
    public void Single_field_eq_becomes_comparison()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["status"] = new Dictionary<string, object?> { ["eq"] = "published" }
        });
        var cmp = Assert.IsType<ComparisonFilter>(f);
        Assert.Equal("status", cmp.FieldPath);
        Assert.Equal(QueryOperator.Eq, cmp.Op);
        Assert.Equal("published", cmp.Value);
    }

    [Fact]
    public void Multiple_fields_are_anded()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["status"] = new Dictionary<string, object?> { ["eq"] = "published" },
            ["title"]  = new Dictionary<string, object?> { ["contains"] = "hello" }
        });
        var logical = Assert.IsType<LogicalFilter>(f);
        Assert.Equal(LogicalOperator.And, logical.Op);
        Assert.Equal(2, logical.Children.Count);
    }

    [Fact]
    public void Explicit_or_group_is_honoured()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["or"] = new List<object?>
            {
                new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "a" } },
                new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "b" } },
            }
        });
        var logical = Assert.IsType<LogicalFilter>(f);
        Assert.Equal(LogicalOperator.Or, logical.Op);
        Assert.Equal(2, logical.Children.Count);
    }

    [Fact]
    public void IsNull_true_and_false_map_to_Null_and_NNull()
    {
        var t = (ComparisonFilter)FilterInputTranslator.Translate(new Dictionary<string, object?>
            { ["publishedAt"] = new Dictionary<string, object?> { ["isNull"] = true } })!;
        Assert.Equal(QueryOperator.Null, t.Op);

        var fl = (ComparisonFilter)FilterInputTranslator.Translate(new Dictionary<string, object?>
            { ["publishedAt"] = new Dictionary<string, object?> { ["isNull"] = false } })!;
        Assert.Equal(QueryOperator.NNull, fl.Op);
    }

    [Theory]
    [InlineData(FieldInterface.Text, typeof(string), "StringFilter")]
    [InlineData(FieldInterface.Number, typeof(int), "IntFilter")]
    [InlineData(FieldInterface.Number, typeof(decimal), "FloatFilter")]
    [InlineData(FieldInterface.DateTime, typeof(System.DateTime), "DateTimeFilter")]
    [InlineData(FieldInterface.Boolean, typeof(bool), "BooleanFilter")]
    public void OperatorInputTypeName_by_interface(FieldInterface iface, System.Type clr, string expected)
        => Assert.Equal(expected, FilterInputTranslator.OperatorInputTypeName(iface, clr));
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~FilterInputTranslatorTests`
Expected: FAIL — type not defined.

- [ ] **Step 3: Implement `FilterInputTranslator`.**

Create `src/Struo.Api/GraphQl/FilterInputTranslator.cs`:
```csharp
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Translates a submitted GraphQL filter input (read back as a nested dictionary because the
/// input type's RuntimeType is a dictionary) into the existing <see cref="FilterNode"/> tree,
/// so the whole query then flows through the existing QueryValidator + repository unchanged.
/// </summary>
public static class FilterInputTranslator
{
    public static readonly IReadOnlyDictionary<string, QueryOperator> OperatorTokens =
        new Dictionary<string, QueryOperator>(StringComparer.Ordinal)
        {
            ["eq"] = QueryOperator.Eq, ["neq"] = QueryOperator.Neq,
            ["in"] = QueryOperator.In, ["nin"] = QueryOperator.Nin,
            ["lt"] = QueryOperator.Lt, ["lte"] = QueryOperator.Lte,
            ["gt"] = QueryOperator.Gt, ["gte"] = QueryOperator.Gte,
            ["contains"] = QueryOperator.Contains,
            ["startsWith"] = QueryOperator.StartsWith,
            ["endsWith"] = QueryOperator.EndsWith,
        };

    public static string OperatorInputTypeName(FieldInterface iface, Type? clrType) => iface switch
    {
        FieldInterface.Number or FieldInterface.Slider or FieldInterface.Rating => NumberFilter(clrType),
        FieldInterface.Boolean or FieldInterface.Checkbox => "BooleanFilter",
        FieldInterface.DateTime or FieldInterface.Date => "DateTimeFilter",
        FieldInterface.File or FieldInterface.Image or FieldInterface.Uuid => "IdFilter",
        _ => "StringFilter"
    };

    private static string NumberFilter(Type? clrType)
    {
        var t = clrType is null ? null : Nullable.GetUnderlyingType(clrType) ?? clrType;
        return (t == typeof(int) || t == typeof(short) || t == typeof(byte) || t == typeof(long))
            ? "IntFilter" : "FloatFilter";
    }

    public static FilterNode? Translate(IReadOnlyDictionary<string, object?>? filter)
    {
        if (filter is null || filter.Count == 0) return null;
        var children = new List<FilterNode>();

        foreach (var (key, value) in filter)
        {
            if (value is null) continue;
            if (string.Equals(key, "and", StringComparison.Ordinal))
                AddGroup(children, value, LogicalOperator.And);
            else if (string.Equals(key, "or", StringComparison.Ordinal))
                AddGroup(children, value, LogicalOperator.Or);
            else
                AddField(children, key, value);
        }

        if (children.Count == 0) return null;
        return children.Count == 1 ? children[0] : new LogicalFilter(LogicalOperator.And, children);
    }

    private static void AddGroup(List<FilterNode> into, object value, LogicalOperator op)
    {
        if (value is not System.Collections.IEnumerable list) return;
        var group = new List<FilterNode>();
        foreach (var item in list)
            if (AsDict(item) is { } d && Translate(d) is { } node) group.Add(node);
        if (group.Count > 0) into.Add(new LogicalFilter(op, group));
    }

    private static void AddField(List<FilterNode> into, string field, object value)
    {
        if (AsDict(value) is not { } ops) return;
        foreach (var (token, opValue) in ops)
        {
            if (string.Equals(token, "isNull", StringComparison.Ordinal))
            {
                var isNull = opValue is true;
                into.Add(new ComparisonFilter(field, isNull ? QueryOperator.Null : QueryOperator.NNull, null));
            }
            else if (OperatorTokens.TryGetValue(token, out var qop))
            {
                into.Add(new ComparisonFilter(field, qop, opValue));
            }
        }
    }

    private static IReadOnlyDictionary<string, object?>? AsDict(object? o) =>
        o as IReadOnlyDictionary<string, object?>
        ?? (o as IDictionary<string, object?>) is { } d
            ? new Dictionary<string, object?>(d)
            : null;
}
```
> Confirm `LogicalOperator`/`ComparisonFilter`/`LogicalFilter` member names by opening `src/Struo.Domain/Query/FilterNode.cs` (seen as `LogicalFilter(LogicalOperator Op, IReadOnlyList<FilterNode> Children)` and `ComparisonFilter(string FieldPath, QueryOperator Op, object? Value)`).

- [ ] **Step 4: Run to verify pass.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~FilterInputTranslatorTests`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/Struo.Api/GraphQl/FilterInputTranslator.cs tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs
git commit -m "feat(graphql): FilterInputTranslator — GraphQL filter input → FilterNode"
```

---

## Task 4: `GraphQlQueryBuilder` (args → QueryModel, sort tokens)

Pure assembly of a `QueryModel` from resolver arguments. The selection→`DeepSpec` extraction lives here too but its function takes the already-extracted relation-name list (so it stays pure/testable); the resolver supplies that list from `ctx.GetSelections` in Task 6/7.

**Files:**
- Create: `src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs`
- Test: `tests/Struo.Tests/GraphQl/GraphQlQueryBuilderTests.cs`

**Interfaces:**
- Produces:
  - `GraphQlQueryBuilder.BuildQuery(IReadOnlyDictionary<string,object?>? filter, IReadOnlyList<string>? sort, int? limit, int? offset, string? search, IReadOnlyList<string> requestedRelations)` → `QueryModel` (with `Deep` set when `requestedRelations` non-empty).
  - `GraphQlQueryBuilder.ParseSort(IReadOnlyList<string>? sort)` → `IReadOnlyList<SortField>` (`"-x"`→descending).
- Consumes: `Struo.Domain.Query.QueryModel`/`SortField`/`DeepSpec`/`DeepRelationSpec`.

- [ ] **Step 1: Write the failing tests.**

Create `tests/Struo.Tests/GraphQl/GraphQlQueryBuilderTests.cs`:
```csharp
using Struo.Api.GraphQl;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.GraphQl;

public class GraphQlQueryBuilderTests
{
    [Fact]
    public void ParseSort_handles_asc_and_desc_tokens()
    {
        var s = GraphQlQueryBuilder.ParseSort(new[] { "title", "-publishedAt" });
        Assert.Equal(2, s.Count);
        Assert.Equal("title", s[0].Field); Assert.False(s[0].Descending);
        Assert.Equal("publishedAt", s[1].Field); Assert.True(s[1].Descending);
    }

    [Fact]
    public void BuildQuery_maps_limit_offset_search_and_filter()
    {
        var q = GraphQlQueryBuilder.BuildQuery(
            filter: new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "x" } },
            sort: new[] { "-id" }, limit: 5, offset: 10, search: "term",
            requestedRelations: System.Array.Empty<string>());
        Assert.Equal(5, q.Limit);
        Assert.Equal(10, q.Offset);
        Assert.Equal("term", q.Search);
        Assert.NotNull(q.Filter);
        Assert.Null(q.Deep);
    }

    [Fact]
    public void BuildQuery_sets_Deep_only_when_relations_requested()
    {
        var q = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, new[] { "category", "tags" });
        Assert.NotNull(q.Deep);
        Assert.True(q.Deep!.Relations.ContainsKey("category"));
        Assert.True(q.Deep.Relations.ContainsKey("tags"));
    }

    [Fact]
    public void BuildQuery_defaults_limit_and_offset_to_zero_when_absent()
    {
        // 0 = "let the validator clamp to DefaultLimit" (QueryValidator owns clamping).
        var q = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, System.Array.Empty<string>());
        Assert.Equal(0, q.Limit);
        Assert.Equal(0, q.Offset);
    }
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~GraphQlQueryBuilderTests`
Expected: FAIL — type not defined.

- [ ] **Step 3: Implement `GraphQlQueryBuilder`.**

Create `src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs`:
```csharp
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Assembles the existing <see cref="QueryModel"/> from GraphQL resolver arguments and the set of
/// requested relation names. Limit/offset default to 0 so the existing QueryValidator performs its
/// DefaultLimit/MaxLimit clamping (single source of truth for pagination bounds).
/// </summary>
public static class GraphQlQueryBuilder
{
    public static IReadOnlyList<SortField> ParseSort(IReadOnlyList<string>? sort)
    {
        if (sort is null || sort.Count == 0) return [];
        var result = new List<SortField>(sort.Count);
        foreach (var token in sort)
        {
            if (string.IsNullOrWhiteSpace(token)) continue;
            var desc = token.StartsWith('-');
            var field = desc ? token[1..] : token;
            result.Add(new SortField(field, desc));
        }
        return result;
    }

    public static QueryModel BuildQuery(
        IReadOnlyDictionary<string, object?>? filter,
        IReadOnlyList<string>? sort,
        int? limit,
        int? offset,
        string? search,
        IReadOnlyList<string> requestedRelations)
    {
        var deep = requestedRelations.Count == 0
            ? null
            : new DeepSpec(requestedRelations.ToDictionary(
                r => r, _ => new DeepRelationSpec(null, null), StringComparer.OrdinalIgnoreCase));

        return new QueryModel(
            Fields: null,
            Filter: FilterInputTranslator.Translate(filter),
            Sort: ParseSort(sort),
            Limit: limit ?? 0,
            Offset: offset ?? 0,
            Search: string.IsNullOrWhiteSpace(search) ? null : search)
        {
            Deep = deep
        };
    }
}
```
> Confirm `DeepSpec`/`DeepRelationSpec` constructor shapes from `src/Struo.Domain/Query/DeepSpec.cs` (seen as `DeepSpec(IReadOnlyDictionary<string,DeepRelationSpec> Relations)` and `DeepRelationSpec(IReadOnlyList<string>? Fields, int? Limit)`).

- [ ] **Step 4: Run to verify pass.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~GraphQlQueryBuilderTests`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs tests/Struo.Tests/GraphQl/GraphQlQueryBuilderTests.cs
git commit -m "feat(graphql): GraphQlQueryBuilder — args + relations → QueryModel"
```

---

## Task 5: `IGraphQlDataSource` port + `ItemServiceGraphQlDataSource` adapter

The Api-owned seam the resolvers depend on (so Application's sealed `ItemService` stays untouched and tests can fake reads).

**Files:**
- Create: `src/Struo.Api/GraphQl/GraphQlDataSource.cs`
- Modify: `src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs` (register the adapter)
- Test: `tests/Struo.Tests/GraphQl/GraphQlDataSourceRegistrationTests.cs`

**Interfaces:**
- Produces:
  - `interface IGraphQlDataSource` with
    `Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, CancellationToken ct)` and
    `Task<IReadOnlyDictionary<string,object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct)`.
  - `sealed class ItemServiceGraphQlDataSource : IGraphQlDataSource` (ctor takes `ItemService`).
- Consumes: `Struo.Application.Query.ItemService`/`PagedResult`; `Struo.Domain.Query.QueryModel`/`DeepSpec`.

- [ ] **Step 1: Write the failing registration test.**

Create `tests/Struo.Tests/GraphQl/GraphQlDataSourceRegistrationTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Xunit;

namespace Struo.Tests.GraphQl;

public class GraphQlDataSourceRegistrationTests
{
    [Fact]
    public void Adapter_implements_the_port()
        => Assert.True(typeof(IGraphQlDataSource).IsAssignableFrom(typeof(ItemServiceGraphQlDataSource)));
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~GraphQlDataSourceRegistrationTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement the port + adapter.**

Create `src/Struo.Api/GraphQl/GraphQlDataSource.cs`:
```csharp
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Api-owned read seam over the concrete <see cref="ItemService"/>. Exists so GraphQL resolvers
/// depend on an interface (fakeable in tests) without adding an interface to the Application layer.
/// Read-only: only the two read methods GraphQL needs are exposed.
/// </summary>
public interface IGraphQlDataSource
{
    Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, CancellationToken ct);
    Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct);
}

public sealed class ItemServiceGraphQlDataSource(ItemService items) : IGraphQlDataSource
{
    public Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, CancellationToken ct)
        => items.QueryAsync(collection, query, locale, ct);

    public Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct)
        => items.GetAsync(collection, id, deep, locale, ct);
}
```

- [ ] **Step 4: Register the adapter (scoped, matching `ItemService`).**

In `GraphQlServiceCollectionExtensions.AddStruoGraphQl`, before the `AddGraphQLServer()` chain, add:
```csharp
services.AddScoped<IGraphQlDataSource, ItemServiceGraphQlDataSource>();
```

- [ ] **Step 5: Run to verify pass + build.**

Run:
```bash
dotnet build -warnaserror
dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~GraphQlDataSourceRegistrationTests
```
Expected: build clean; test PASS.

- [ ] **Step 6: Commit.**

```bash
git add src/Struo.Api/GraphQl/GraphQlDataSource.cs src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs tests/Struo.Tests/GraphQl/GraphQlDataSourceRegistrationTests.cs
git commit -m "feat(graphql): IGraphQlDataSource port + ItemService adapter"
```

---

## Task 6: Test fixtures — fake metadata + fake data source

Reusable test doubles so schema/execution tests run with no DB. Not a production deliverable, but committed as test infrastructure the next tasks depend on.

**Files:**
- Create: `tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs`, `tests/Struo.Tests/GraphQl/FakeGraphQlDataSource.cs`

**Interfaces:**
- Produces:
  - `FakeMetadataFixtures.Provider()` → `IMetadataProvider` with `article`, `category`, `tag`, `file` collections (see below) and `Registry()` → `IEntityRegistry` over matching test POCOs.
  - `FakeGraphQlDataSource` implementing `IGraphQlDataSource` with settable canned results and a call counter (`QueryCalls`, `QueryCollections`).

- [ ] **Step 1: Build the fixtures (no test of their own — exercised by Tasks 7–11).**

Create `tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs`. Build `CollectionMetadata` instances by hand (do **not** run the scanner). Minimum shape needed by later tests:
- `article`: fields `title` (Text, translatable), `status` (Select, options draft/published), `publishedAt` (DateTime, nullable), `heroImageId` (Image), `regions` (MultiSelect), `keywords` (Tags), `attributes` (Json), `gallery` (Files), `faqs` (Repeater with sub-fields `question` Text required, `answer` Textarea); relations `category` (ManyToOne → category, FK `categoryId`), `tags` (ManyToMany → tag). `Translation` set with fields `["title"]`.
- `category`: fields `name` (Text); relation `articles` (OneToMany → article).
- `tag`: fields `name` (Text).
- `file`: fields `title` (Text), `url` (Url), `width` (Number int), `height` (Number int).

```csharp
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Tests.GraphQl;

internal static class FakeMetadataFixtures
{
    // Construct CollectionMetadata/FieldMetadata/RelationMetadata via their record ctors/inits.
    // (Open the Domain model records to match exact required members; fill required ones.)
    internal static IReadOnlyList<CollectionMetadata> Collections() => [ Article(), Category(), Tag(), File() ];

    internal static IMetadataProvider Provider() => new FakeProvider(Collections());
    internal static IEntityRegistry Registry() => new FakeRegistry();

    // ... FieldMetadata builders (fill Name/Label/Interface/Required/Translatable/Options/Fields) ...
    // ... a small POCO per collection so FakeRegistry can supply EntityType + FieldToProperty + IdProperty ...

    private sealed class FakeProvider(IReadOnlyList<CollectionMetadata> all) : IMetadataProvider
    {
        public IReadOnlyList<CollectionMetadata> GetCollections() => all;
        public CollectionMetadata? GetCollection(string name) =>
            all.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeRegistry : IEntityRegistry { /* Get(name) → EntityDescriptor over the test POCOs */ }
}
```
> Fill in the record constructions concretely when implementing — open `src/Struo.Domain/Metadata/Models/*.cs` and `src/Struo.Application/Metadata/IEntityRegistry.cs` to match required members. The POCOs exist only to give the mapper real CLR property types for `Number` fields and to satisfy `IEntityRegistry`.

Create `tests/Struo.Tests/GraphQl/FakeGraphQlDataSource.cs`:
```csharp
using Struo.Api.GraphQl;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Tests.GraphQl;

internal sealed class FakeGraphQlDataSource : IGraphQlDataSource
{
    public List<string> QueryCollections { get; } = [];
    public int QueryCalls => QueryCollections.Count;
    public Func<string, QueryModel, string?, PagedResult> OnQuery { get; set; } =
        (_, q, _) => new PagedResult([], 0, q.Limit, q.Offset);
    public Func<string, string, DeepSpec?, string?, IReadOnlyDictionary<string, object?>?> OnGet { get; set; } =
        (_, _, _, _) => null;

    public Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, CancellationToken ct)
    {
        QueryCollections.Add(collection);
        return Task.FromResult(OnQuery(collection, query, locale));
    }

    public Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct)
        => Task.FromResult(OnGet(collection, id, deep, locale));
}
```

- [ ] **Step 2: Build (fixtures compile).**

Run: `dotnet build tests/Struo.Tests/Struo.Tests.csproj`
Expected: compiles.

- [ ] **Step 3: Commit.**

```bash
git add tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs tests/Struo.Tests/GraphQl/FakeGraphQlDataSource.cs
git commit -m "test(graphql): fake metadata provider + data source fixtures"
```

---

## Task 7: `SharedFilterTypes` + `CollectionSchemaBuilder` + `StruoTypeModule` — dynamic schema

The core: build the object type (scalars + relations + files + repeater sub-types), the `XList` wrapper, the `XFilterInput`, and the two root Query fields, for every collection. Deliverable: a full schema whose SDL matches expectations.

**Files:**
- Create: `src/Struo.Api/GraphQl/SharedFilterTypes.cs`, `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`, `src/Struo.Api/GraphQl/StruoTypeModule.cs`
- Modify: `GraphQlServiceCollectionExtensions.cs` (add `.AddTypeModule<StruoTypeModule>()`)
- Test: `tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs`

**Interfaces:**
- Consumes: `SchemaTypeMapper`, `FilterInputTranslator`, `GraphQlQueryBuilder`, `IGraphQlDataSource`, `IMetadataProvider`, `IEntityRegistry`, and HotChocolate definition classes.
- Produces: `StruoTypeModule : ITypeModule` (ctor injects `IMetadataProvider`, `IEntityRegistry`); it fires no `TypesChanged` (collections are fixed at boot).

- [ ] **Step 1: Write the failing schema test.**

Create `tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs`:
```csharp
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Struo.Api.GraphQl;
using Struo.Application.Metadata;
using Xunit;

namespace Struo.Tests.GraphQl;

public class GraphQlSchemaTests
{
    private static async Task<ISchema> BuildAsync()
    {
        var services = new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddScoped<IGraphQlDataSource, FakeGraphQlDataSource>();

        var executor = await services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            .BuildRequestExecutorAsync();

        return executor.Schema;
    }

    [Fact]
    public async Task Query_has_single_and_list_fields_per_collection()
    {
        var sdl = (await BuildAsync()).Print().ToString();
        Assert.Contains("article(", sdl);
        Assert.Contains("articles(", sdl);
        Assert.Contains("type Article", sdl);
        Assert.Contains("type ArticleList", sdl);
        Assert.Contains("input ArticleFilterInput", sdl);
    }

    [Fact]
    public async Task Article_type_maps_fields_relations_and_files()
    {
        var sdl = (await BuildAsync()).Print().ToString();
        Assert.Contains("status: String", sdl);
        Assert.Contains("regions: [String!]", sdl);
        Assert.Contains("attributes: Any", sdl);
        Assert.Contains("keywords: [TagItem!]", sdl);
        Assert.Contains("faqs: [ArticleFaqsItem!]", sdl);
        Assert.Contains("heroImageId: ID", sdl);
        Assert.Contains("heroImage: File", sdl);
        Assert.Contains("gallery: [ID!]", sdl);
        Assert.Contains("galleryFiles: [File!]", sdl);
        Assert.Contains("category: Category", sdl);
        Assert.Contains("tags: [Tag!]", sdl);
        Assert.Contains("translations: [Translation!]", sdl);
    }

    [Fact]
    public async Task Hidden_and_password_fields_are_absent()
    {
        var sdl = (await BuildAsync()).Print().ToString();
        Assert.DoesNotContain("password", sdl, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~GraphQlSchemaTests`
Expected: FAIL — `StruoTypeModule` not defined.

- [ ] **Step 3: Implement `SharedFilterTypes`.**

Create `src/Struo.Api/GraphQl/SharedFilterTypes.cs`:
```csharp
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Definitions;

namespace Struo.Api.GraphQl;

/// <summary>Builds the reusable per-scalar operator input types shared by all collection filters.</summary>
internal static class SharedFilterTypes
{
    internal static IEnumerable<ITypeSystemMember> Build()
    {
        yield return Input("StringFilter", ("eq","String"),("neq","String"),("in","[String!]"),("nin","[String!]"),
            ("contains","String"),("startsWith","String"),("endsWith","String"),("isNull","Boolean"));
        yield return Input("IntFilter", ("eq","Int"),("neq","Int"),("in","[Int!]"),("nin","[Int!]"),
            ("lt","Int"),("lte","Int"),("gt","Int"),("gte","Int"),("isNull","Boolean"));
        yield return Input("FloatFilter", ("eq","Float"),("neq","Float"),("in","[Float!]"),("nin","[Float!]"),
            ("lt","Float"),("lte","Float"),("gt","Float"),("gte","Float"),("isNull","Boolean"));
        yield return Input("DateTimeFilter", ("eq","DateTime"),("neq","DateTime"),("in","[DateTime!]"),("nin","[DateTime!]"),
            ("lt","DateTime"),("lte","DateTime"),("gt","DateTime"),("gte","DateTime"),("isNull","Boolean"));
        yield return Input("BooleanFilter", ("eq","Boolean"),("neq","Boolean"),("isNull","Boolean"));
        yield return Input("IdFilter", ("eq","ID"),("neq","ID"),("in","[ID!]"),("nin","[ID!]"),("isNull","Boolean"));
    }

    private static InputObjectType Input(string name, params (string field, string sdl)[] fields)
    {
        var def = new InputObjectTypeDefinition(name)
        {
            RuntimeType = typeof(IReadOnlyDictionary<string, object?>)
        };
        foreach (var (field, sdl) in fields)
            def.Fields.Add(new InputFieldDefinition(field, null, TypeReference.Parse(sdl)));
        return InputObjectType.CreateUnsafe(def);
    }
}
```

- [ ] **Step 4: Implement `CollectionSchemaBuilder`.**

Create `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`:
```csharp
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Definitions;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Api.GraphQl;

/// <summary>Builds all GraphQL types + root query fields for a single collection.</summary>
internal sealed class CollectionSchemaBuilder(IEntityRegistry registry)
{
    private const string Dict = "dict";
    private static IReadOnlyDictionary<string, object?> ParentDict(IResolverContext ctx)
        => ctx.Parent<IReadOnlyDictionary<string, object?>>();

    internal IEnumerable<ITypeSystemMember> Build(CollectionMetadata meta)
    {
        var types = new List<ITypeSystemMember>();
        var relationNames = meta.Relations.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        types.Add(BuildObjectType(meta, relationNames, types));   // Article + nested repeater item types (added to `types`)
        types.Add(BuildListType(meta));                            // ArticleList { items, total }
        types.Add(BuildFilterInput(meta));                         // ArticleFilterInput
        return types;
    }

    private ObjectType BuildObjectType(CollectionMetadata meta, HashSet<string> relationNames, List<ITypeSystemMember> sink)
    {
        var typeName = SchemaTypeMapper.TypeName(meta.Name);
        var desc = registry.Get(meta.Name);
        var def = new ObjectTypeDefinition(typeName) { RuntimeType = typeof(IReadOnlyDictionary<string, object?>) };

        // id + version (always present in the projection).
        def.Fields.Add(Field("id", "ID!", pure: ctx => ParentDict(ctx).GetValueOrDefault("id")));
        def.Fields.Add(Field("version", "Long", pure: ctx => ParentDict(ctx).GetValueOrDefault("version")));

        foreach (var f in meta.Fields)
        {
            if (f.Hidden || SchemaTypeMapper.IsExcluded(f.Interface)) continue;

            switch (f.Interface)
            {
                case FieldInterface.Tags:
                    def.Fields.Add(Field(f.Name, "[TagItem!]", pure: ctx => ParentDict(ctx).GetValueOrDefault(f.Name)));
                    break;
                case FieldInterface.Repeater when f.Fields is { Count: > 0 }:
                    var itemType = BuildRepeaterItemType(meta.Name, f);
                    sink.Add(itemType);
                    def.Fields.Add(Field(f.Name, $"[{SchemaTypeMapper.RepeaterItemTypeName(meta.Name, f.Name)}!]",
                        pure: ctx => ParentDict(ctx).GetValueOrDefault(f.Name)));
                    break;
                case FieldInterface.File or FieldInterface.Image:
                    def.Fields.Add(Field(f.Name, "ID", pure: ctx => ParentDict(ctx).GetValueOrDefault(f.Name)));
                    def.Fields.Add(FileFieldResolvers.ScalarFileField(f.Name)); // "<name-stripId>": File
                    break;
                case FieldInterface.Files:
                    def.Fields.Add(Field(f.Name, "[ID!]", pure: ctx => ParentDict(ctx).GetValueOrDefault(f.Name)));
                    def.Fields.Add(FileFieldResolvers.ListFileField(f.Name)); // "<name>Files": [File!]
                    break;
                default:
                    var clr = ClrType(desc, f.Name);
                    var sdl = SchemaTypeMapper.ScalarSdl(f.Interface, clr);
                    if (sdl is null) break;
                    def.Fields.Add(Field(f.Name, sdl, pure: ctx => ParentDict(ctx).GetValueOrDefault(f.Name)));
                    break;
            }
        }

        // relations (single-level; value pre-nested by ItemService deep expansion).
        foreach (var rel in meta.Relations)
        {
            var target = SchemaTypeMapper.TypeName(rel.TargetCollection);
            var sdl = rel.Kind == RelationKind.ManyToOne ? target : $"[{target}!]";
            def.Fields.Add(Field(rel.Name, sdl, pure: ctx => ParentDict(ctx).GetValueOrDefault(rel.Name)));
        }

        // translations map (always present in projection when the collection has a sidecar).
        if (meta.Translation is not null)
            def.Fields.Add(Field("translations", "[Translation!]", pure: ctx => TranslationList(ParentDict(ctx))));

        return ObjectType.CreateUnsafe(def);
    }

    private static ObjectType BuildRepeaterItemType(string collection, FieldMetadata repeater)
    {
        var def = new ObjectTypeDefinition(SchemaTypeMapper.RepeaterItemTypeName(collection, repeater.Name))
        { RuntimeType = typeof(IReadOnlyDictionary<string, object?>) };
        foreach (var sub in repeater.Fields!)
        {
            if (sub.Hidden || SchemaTypeMapper.IsExcluded(sub.Interface)) continue;
            var sdl = SchemaTypeMapper.ScalarSdl(sub.Interface, typeof(string)); // lean scalar sub-field set
            if (sdl is null) continue;
            def.Fields.Add(Field(sub.Name, sdl, pure: ctx => ParentDict(ctx).GetValueOrDefault(sub.Name)));
        }
        return ObjectType.CreateUnsafe(def);
    }

    private ObjectType BuildListType(CollectionMetadata meta)
    {
        var def = new ObjectTypeDefinition(SchemaTypeMapper.TypeName(meta.Name) + "List")
        { RuntimeType = typeof(PagedResultView) };
        def.Fields.Add(Field("items", $"[{SchemaTypeMapper.TypeName(meta.Name)}!]!",
            pure: ctx => ctx.Parent<PagedResultView>().Items));
        def.Fields.Add(Field("total", "Int!", pure: ctx => ctx.Parent<PagedResultView>().Total));
        return ObjectType.CreateUnsafe(def);
    }

    private InputObjectType BuildFilterInput(CollectionMetadata meta)
    {
        var name = SchemaTypeMapper.TypeName(meta.Name) + "FilterInput";
        var desc = registry.Get(meta.Name);
        var def = new InputObjectTypeDefinition(name) { RuntimeType = typeof(IReadOnlyDictionary<string, object?>) };
        def.Fields.Add(new InputFieldDefinition("and", null, TypeReference.Parse($"[{name}!]")));
        def.Fields.Add(new InputFieldDefinition("or", null, TypeReference.Parse($"[{name}!]")));
        def.Fields.Add(new InputFieldDefinition("id", null, TypeReference.Parse("IdFilter")));

        foreach (var f in meta.Fields)
        {
            if (f.Hidden || SchemaTypeMapper.IsExcluded(f.Interface)) continue;
            if (!IsFilterable(f.Interface)) continue;
            var opInput = FilterInputTranslator.OperatorInputTypeName(f.Interface, ClrType(desc, f.Name));
            def.Fields.Add(new InputFieldDefinition(f.Name, null, TypeReference.Parse(opInput)));
        }
        // M2O foreign keys are filterable (parity with REST allowlist).
        foreach (var rel in meta.Relations)
            if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
                def.Fields.Add(new InputFieldDefinition(fk, null, TypeReference.Parse("IdFilter")));

        return InputObjectType.CreateUnsafe(def);
    }

    // Filterable own-field interfaces: scalars only (parity with REST; multi-value/json/kv/files/repeater excluded).
    private static bool IsFilterable(FieldInterface i) => i is
        FieldInterface.Text or FieldInterface.Textarea or FieldInterface.Slug or FieldInterface.Email
        or FieldInterface.Url or FieldInterface.Color or FieldInterface.Phone or FieldInterface.Select
        or FieldInterface.Radio or FieldInterface.Number or FieldInterface.Slider or FieldInterface.Rating
        or FieldInterface.Boolean or FieldInterface.Checkbox or FieldInterface.Date or FieldInterface.DateTime
        or FieldInterface.Time or FieldInterface.Uuid;

    private static Type? ClrType(EntityDescriptor? desc, string fieldName)
    {
        if (desc is null) return null;
        return desc.FieldToProperty.TryGetValue(fieldName, out var prop)
            ? desc.EntityType.GetProperty(prop)?.PropertyType
            : null;
    }

    private static IReadOnlyList<object> TranslationList(IReadOnlyDictionary<string, object?> parent)
    {
        // projection stores translations as { locale: { field: value } }; expose as [{locale, fields}]
        if (parent.GetValueOrDefault("translations") is not IDictionary<string, object?> map) return [];
        return map.Select(kv => (object)new Dictionary<string, object?>
        {
            ["locale"] = kv.Key,
            ["fields"] = kv.Value
        }).ToList();
    }

    internal static ObjectFieldDefinition Field(string name, string sdl, PureFieldResolverDelegate pure)
        => new(name, null, TypeReference.Parse(sdl), pureResolver: pure);
}

/// <summary>Adapter so the ArticleList type reads items/total from the Application PagedResult.</summary>
internal sealed record PagedResultView(IReadOnlyList<object> Items, int Total);
```
> `GetValueOrDefault` is on `IReadOnlyDictionary<,>` via `CollectionExtensions`. If `PureFieldResolverDelegate` has a different name in the installed version, build-error-resolver will adjust. `TranslationList` returns dicts read by the `Translation` type (built once in the type module, Step 5).

- [ ] **Step 5: Implement `StruoTypeModule`** (assembles everything incl. the shared `Translation` + `TagItem` types + the Query extension with resolvers).

Create `src/Struo.Api/GraphQl/StruoTypeModule.cs`:
```csharp
using HotChocolate.Configuration;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Definitions;
using Struo.Application.Metadata;

namespace Struo.Api.GraphQl;

/// <summary>
/// Reads the startup-cached collection metadata and emits the full dynamic GraphQL schema:
/// one object type + list wrapper + filter input + two root query fields per collection, plus the
/// shared TagItem/Translation value types and the reusable scalar filter inputs.
/// </summary>
public sealed class StruoTypeModule(IMetadataProvider metadata, IEntityRegistry registry) : ITypeModule
{
#pragma warning disable CS0067 // collections are fixed at boot; never fires
    public event EventHandler<EventArgs>? TypesChanged;
#pragma warning restore CS0067

    public ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context, CancellationToken ct)
    {
        var builder = new CollectionSchemaBuilder(registry);
        var types = new List<ITypeSystemMember>();

        // Shared value types.
        types.Add(TagItemType());
        types.Add(TranslationType());
        types.AddRange(SharedFilterTypes.Build());

        var collections = metadata.GetCollections();
        foreach (var meta in collections)
            types.AddRange(builder.Build(meta));

        types.Add(BuildQueryExtension(collections.Select(c => c.Name)));
        return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    private static ObjectType TagItemType()
    {
        var def = new ObjectTypeDefinition("TagItem") { RuntimeType = typeof(object) };
        // TagItem is a Struo.Domain TagItem POCO {Value, Label?}; read via reflection-tolerant resolvers.
        def.Fields.Add(CollectionSchemaBuilder.Field("value", "String!", ctx => Prop(ctx.Parent<object>(), "Value")));
        def.Fields.Add(CollectionSchemaBuilder.Field("label", "String", ctx => Prop(ctx.Parent<object>(), "Label")));
        return ObjectType.CreateUnsafe(def);
    }

    private static ObjectType TranslationType()
    {
        var def = new ObjectTypeDefinition("Translation") { RuntimeType = typeof(IReadOnlyDictionary<string, object?>) };
        def.Fields.Add(CollectionSchemaBuilder.Field("locale", "String!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("locale")));
        def.Fields.Add(CollectionSchemaBuilder.Field("fields", "Any!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("fields")));
        return ObjectType.CreateUnsafe(def);
    }

    private ObjectTypeExtension BuildQueryExtension(IEnumerable<string> collectionNames)
    {
        var q = new ObjectTypeDefinition("Query");
        foreach (var name in collectionNames)
        {
            q.Fields.Add(CollectionResolvers.SingleField(name, metadata));
            q.Fields.Add(CollectionResolvers.ListField(name, metadata));
        }
        return ObjectTypeExtension.CreateUnsafe(q);
    }

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)?.GetValue(o);
}
```
> `TagItem`'s real type is `Struo.Domain.Metadata.Models.TagItem` (seen used in `ItemService`); reflection here keeps the type module Api-only. If a `Translation` type name collides across collections it's fine — it's a single shared type.

- [ ] **Step 6: Add `CollectionResolvers` + `FileFieldResolvers` stubs so this compiles** (full bodies in Tasks 8–9; here define signatures + list/single resolvers so the schema builds and Task 7's schema test passes).

Create `src/Struo.Api/GraphQl/CollectionResolvers.cs`:
```csharp
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Definitions;
using Struo.Application.Metadata;

namespace Struo.Api.GraphQl;

internal static class CollectionResolvers
{
    internal static ObjectFieldDefinition SingleField(string collection, IMetadataProvider metadata)
    {
        var def = new ObjectFieldDefinition(
            SchemaTypeMapper.SingleFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
            resolver: ctx => ResolveSingle(ctx, collection));
        def.Arguments.Add(new ArgumentDefinition("id", null, TypeReference.Parse("ID!")));
        def.Arguments.Add(new ArgumentDefinition("locale", null, TypeReference.Parse("String")));
        return def;
    }

    internal static ObjectFieldDefinition ListField(string collection, IMetadataProvider metadata)
    {
        var def = new ObjectFieldDefinition(
            SchemaTypeMapper.ListFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection) + "List!"),
            resolver: ctx => ResolveList(ctx, collection, metadata));
        def.Arguments.Add(new ArgumentDefinition("filter", null, TypeReference.Parse(SchemaTypeMapper.TypeName(collection) + "FilterInput")));
        def.Arguments.Add(new ArgumentDefinition("sort", null, TypeReference.Parse("[String!]")));
        def.Arguments.Add(new ArgumentDefinition("limit", null, TypeReference.Parse("Int")));
        def.Arguments.Add(new ArgumentDefinition("offset", null, TypeReference.Parse("Int")));
        def.Arguments.Add(new ArgumentDefinition("search", null, TypeReference.Parse("String")));
        def.Arguments.Add(new ArgumentDefinition("locale", null, TypeReference.Parse("String")));
        return def;
    }

    private static async ValueTask<object?> ResolveSingle(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var locale = ctx.ArgumentValue<string?>("locale");
        var relations = SelectionRelations(ctx, collection, elementIsDirect: true);
        var deep = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, relations).Deep;
        var data = await ctx.Service<IGraphQlDataSource>().GetAsync(collection, id, deep, locale, ctx.RequestAborted);
        return data;
    }

    private static async ValueTask<object?> ResolveList(IResolverContext ctx, string collection, IMetadataProvider metadata)
    {
        var filter = ctx.ArgumentValue<IReadOnlyDictionary<string, object?>?>("filter");
        var sort = ctx.ArgumentValue<IReadOnlyList<string>?>("sort");
        var limit = ctx.ArgumentValue<int?>("limit");
        var offset = ctx.ArgumentValue<int?>("offset");
        var search = ctx.ArgumentValue<string?>("search");
        var locale = ctx.ArgumentValue<string?>("locale");
        var relations = SelectionRelations(ctx, collection, elementIsDirect: false);
        var query = GraphQlQueryBuilder.BuildQuery(filter, sort, limit, offset, search, relations);
        var page = await ctx.Service<IGraphQlDataSource>().QueryAsync(collection, query, locale, ctx.RequestAborted);
        return new PagedResultView(page.Data.Cast<object>().ToList(), page.Total);
    }

    /// <summary>Which of the collection's relations the client selected on the element type.</summary>
    private static IReadOnlyList<string> SelectionRelations(IResolverContext ctx, string collection, bool elementIsDirect)
    {
        var relNames = ctx.Service<IMetadataProvider>().GetCollection(collection)?.Relations
            .Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        if (relNames.Count == 0) return [];

        IObjectType elementType;
        IReadOnlyList<HotChocolate.Execution.Processing.ISelection> childSelections;
        if (elementIsDirect)
        {
            elementType = (IObjectType)ctx.Selection.Field.Type.NamedType();
            childSelections = ctx.GetSelections(elementType);
        }
        else
        {
            var listType = (IObjectType)ctx.Selection.Field.Type.NamedType();          // XList
            var itemsSel = ctx.GetSelections(listType).FirstOrDefault(s => s.Field.Name == "items");
            if (itemsSel is null) return [];
            elementType = (IObjectType)itemsSel.Field.Type.NamedType();                  // X
            childSelections = ctx.GetSelections(elementType, itemsSel);
        }
        return childSelections.Select(s => s.Field.Name).Where(relNames.Contains).ToList();
    }
}
```

Create `src/Struo.Api/GraphQl/FileFieldResolvers.cs` (stub returning fields; DataLoader body filled in Task 9):
```csharp
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors.Definitions;

namespace Struo.Api.GraphQl;

internal static class FileFieldResolvers
{
    // "<name>Id" (Image/File) -> "<name>": File, resolved by id via DataLoader.
    internal static ObjectFieldDefinition ScalarFileField(string idFieldName)
    {
        var name = idFieldName.EndsWith("Id", StringComparison.Ordinal) ? idFieldName[..^2] : idFieldName + "File";
        return new ObjectFieldDefinition(name, null, HotChocolate.Types.Descriptors.TypeReference.Parse("File"),
            resolver: ctx => ResolveScalar(ctx, idFieldName));
    }

    // "<name>" (Files) -> "<name>Files": [File!], resolved batched.
    internal static ObjectFieldDefinition ListFileField(string listFieldName)
        => new(listFieldName + "Files", null, HotChocolate.Types.Descriptors.TypeReference.Parse("[File!]"),
            resolver: ctx => ResolveList(ctx, listFieldName));

    private static ValueTask<object?> ResolveScalar(IResolverContext ctx, string idFieldName)
        => ValueTask.FromResult<object?>(null); // filled in Task 9

    private static ValueTask<object?> ResolveList(IResolverContext ctx, string listFieldName)
        => ValueTask.FromResult<object?>(null); // filled in Task 9
}
```

- [ ] **Step 7: Register the type module.**

In `GraphQlServiceCollectionExtensions.AddStruoGraphQl`, add `.AddTypeModule<StruoTypeModule>()` to the `AddGraphQLServer()` chain (after `.AddJsonTypeConverter()`).

- [ ] **Step 8: Run the schema test.**

Run:
```bash
dotnet build -warnaserror
dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~GraphQlSchemaTests
```
Expected: build clean; schema tests PASS (SDL contains all expected types/fields; hidden/password absent). Fix any API-name mismatches with the build-error-resolver agent, keeping the verified v15 shapes.

- [ ] **Step 9: Commit.**

```bash
git add src/Struo.Api/GraphQl/SharedFilterTypes.cs src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs src/Struo.Api/GraphQl/StruoTypeModule.cs src/Struo.Api/GraphQl/CollectionResolvers.cs src/Struo.Api/GraphQl/FileFieldResolvers.cs src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs
git commit -m "feat(graphql): dynamic schema — object/list/filter types + root query fields"
```

---

## Task 8: List/single execution — filter, sort, pagination, total, NOT_FOUND, i18n

Execute real queries through the executor with the fake data source; assert the resolvers assemble the right `QueryModel` and shape the response.

**Files:**
- Test: `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs`
- (No production change expected; if a test fails, fix `CollectionResolvers`.)

**Interfaces:**
- Consumes: `FakeGraphQlDataSource`, `FakeMetadataFixtures`, `IRequestExecutor`.

- [ ] **Step 1: Write the failing execution tests.**

Create `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs`:
```csharp
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.GraphQl;

public class GraphQlExecutionTests
{
    private static async Task<IRequestExecutor> ExecutorAsync(FakeGraphQlDataSource ds)
        => await new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddScoped<IGraphQlDataSource>(_ => ds)
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            .BuildRequestExecutorAsync();

    [Fact]
    public async Task List_returns_items_and_total()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _) => new PagedResult(
                new IReadOnlyDictionary<string, object?>[]
                {
                    new Dictionary<string, object?> { ["id"] = "1", ["status"] = "published" }
                }, 42, q.Limit, q.Offset)
        };
        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(limit: 5) { items { id status } total } }");
        var json = result.ToJson();
        Assert.Contains("\"total\": 42", json);
        Assert.Contains("\"status\": \"published\"", json);
        Assert.DoesNotContain("errors", json);
    }

    [Fact]
    public async Task List_passes_filter_sort_pagination_into_QueryModel()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource { OnQuery = (_, q, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); } };
        await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(limit: 3, offset: 6, sort: [\"-status\"], search: \"hi\", filter: { status: { eq: \"published\" } }) { total } }");
        Assert.NotNull(captured);
        Assert.Equal(3, captured!.Limit);
        Assert.Equal(6, captured.Offset);
        Assert.Equal("hi", captured.Search);
        Assert.Single(captured.Sort);
        Assert.True(captured.Sort[0].Descending);
        Assert.NotNull(captured.Filter);
    }

    [Fact]
    public async Task Single_returns_null_maps_to_null_data()
    {
        var ds = new FakeGraphQlDataSource { OnGet = (_, _, _, _) => null };
        var result = await (await ExecutorAsync(ds)).ExecuteAsync("{ article(id: \"x\") { id } }");
        var json = result.ToJson();
        Assert.Contains("\"article\": null", json);
        Assert.DoesNotContain("errors", json);
    }

    [Fact]
    public async Task Locale_argument_is_forwarded()
    {
        string? seen = null;
        var ds = new FakeGraphQlDataSource { OnQuery = (_, q, loc) => { seen = loc; return new PagedResult([], 0, q.Limit, q.Offset); } };
        await (await ExecutorAsync(ds)).ExecuteAsync("{ articles(locale: \"zh-TW\") { total } }");
        Assert.Equal("zh-TW", seen);
    }
}
```

- [ ] **Step 2: Run to verify failure/behaviour.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~GraphQlExecutionTests`
Expected: FAIL initially if any resolver wiring is off; iterate on `CollectionResolvers` until green (the production code from Task 7 should largely satisfy these).

- [ ] **Step 3: Commit.**

```bash
git add tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs src/Struo.Api/GraphQl/CollectionResolvers.cs
git commit -m "test(graphql): list/single execution — filter/sort/pagination/total/locale"
```

---

## Task 9: File/Image/Files resolution via BatchDataLoader

Fill in `FileFieldResolvers` so `heroImage`/`galleryFiles` resolve to `File` nodes by batching all requested file ids into one `QueryAsync("file", id _in [...])`.

**Files:**
- Modify: `src/Struo.Api/GraphQl/FileFieldResolvers.cs`
- Test: add to `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs`

**Interfaces:**
- Consumes: `ctx.BatchDataLoader<Guid, IReadOnlyDictionary<string,object?>>(...)`, `IGraphQlDataSource.QueryAsync`.

- [ ] **Step 1: Write the failing file-resolution + batching test.**

Add to `GraphQlExecutionTests.cs`:
```csharp
    [Fact]
    public async Task HeroImage_resolves_file_and_batches_one_query()
    {
        var fileId = System.Guid.NewGuid();
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (collection, q, _) => collection == "article"
                ? new PagedResult(new IReadOnlyDictionary<string, object?>[]
                    {
                        new Dictionary<string, object?> { ["id"] = "1", ["heroImageId"] = fileId },
                        new Dictionary<string, object?> { ["id"] = "2", ["heroImageId"] = fileId },
                    }, 2, q.Limit, q.Offset)
                : new PagedResult(new IReadOnlyDictionary<string, object?>[]
                    {
                        new Dictionary<string, object?> { ["id"] = fileId, ["title"] = "pic" }
                    }, 1, q.Limit, q.Offset)
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles { items { id heroImage { id title } } } }");
        var json = result.ToJson();
        Assert.Contains("\"title\": \"pic\"", json);
        Assert.DoesNotContain("errors", json);

        // batching: exactly one article query + one file query (not one file query per row).
        Assert.Equal(1, ds.QueryCollections.Count(c => c == "file"));
    }

    [Fact]
    public async Task Missing_file_resolves_to_null()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (collection, q, _) => collection == "article"
                ? new PagedResult(new IReadOnlyDictionary<string, object?>[]
                    { new Dictionary<string, object?> { ["id"] = "1", ["heroImageId"] = System.Guid.NewGuid() } }, 1, q.Limit, q.Offset)
                : new PagedResult([], 0, q.Limit, q.Offset) // file not found
        };
        var result = await (await ExecutorAsync(ds)).ExecuteAsync("{ articles { items { heroImage { id } } } }");
        Assert.Contains("\"heroImage\": null", result.ToJson());
        Assert.DoesNotContain("errors", result.ToJson());
    }
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~HeroImage_resolves_file_and_batches_one_query`
Expected: FAIL — `heroImage` returns null (stub).

- [ ] **Step 3: Implement the DataLoader resolvers.**

Replace `ResolveScalar`/`ResolveList` in `src/Struo.Api/GraphQl/FileFieldResolvers.cs`:
```csharp
using HotChocolate;
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Definitions;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

internal static class FileFieldResolvers
{
    internal static ObjectFieldDefinition ScalarFileField(string idFieldName)
    {
        var name = idFieldName.EndsWith("Id", StringComparison.Ordinal) ? idFieldName[..^2] : idFieldName + "File";
        return new ObjectFieldDefinition(name, null, TypeReference.Parse("File"),
            resolver: ctx => ResolveScalar(ctx, idFieldName));
    }

    internal static ObjectFieldDefinition ListFileField(string listFieldName)
        => new(listFieldName + "Files", null, TypeReference.Parse("[File!]"),
            resolver: ctx => ResolveList(ctx, listFieldName));

    private static async ValueTask<object?> ResolveScalar(IResolverContext ctx, string idFieldName)
    {
        var raw = ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(idFieldName);
        var id = ToGuid(raw);
        if (id is null) return null;
        return await FileLoader(ctx).LoadAsync(id.Value, ctx.RequestAborted);
    }

    private static async ValueTask<object?> ResolveList(IResolverContext ctx, string listFieldName)
    {
        var raw = ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(listFieldName);
        if (raw is not System.Collections.IEnumerable seq) return System.Array.Empty<object>();
        var ids = seq.Cast<object?>().Select(ToGuid).Where(g => g is not null).Select(g => g!.Value).ToArray();
        var loaded = await FileLoader(ctx).LoadAsync(ids, ctx.RequestAborted);
        return loaded.Where(x => x is not null).Cast<object>().ToList(); // preserve order, drop missing
    }

    // One batched QueryAsync("file", id _in keys) per request, keyed by id.
    private static IDataLoader<Guid, IReadOnlyDictionary<string, object?>?> FileLoader(IResolverContext ctx)
        => ctx.BatchDataLoader<Guid, IReadOnlyDictionary<string, object?>?>(
            async (keys, ct) =>
            {
                var filter = new Dictionary<string, object?>
                {
                    ["id"] = new Dictionary<string, object?> { ["in"] = keys.Cast<object?>().ToList() }
                };
                var query = GraphQlQueryBuilder.BuildQuery(filter, null, keys.Count, 0, null, System.Array.Empty<string>());
                var page = await ctx.Service<IGraphQlDataSource>().QueryAsync("file", query, null, ct);
                var map = new Dictionary<Guid, IReadOnlyDictionary<string, object?>?>();
                foreach (var row in page.Data)
                    if (ToGuid(row.GetValueOrDefault("id")) is { } g) map[g] = row;
                return map;
            });

    private static Guid? ToGuid(object? v) => v switch
    {
        Guid g => g,
        string s when Guid.TryParse(s, out var g) => g,
        _ => null
    };
}
```
> `ctx.BatchDataLoader<TKey,TValue>(fetch)` is the inline, registration-free loader (verified v15). `LoadAsync(keys, ct)` with an array returns `IReadOnlyList<TValue>` in key order. If the missing key returns a non-null placeholder, the `Where(... is not null)` still filters correctly.

- [ ] **Step 4: Run to verify pass.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~GraphQlExecutionTests`
Expected: PASS (file resolves; exactly one `file` query; missing → null).

- [ ] **Step 5: Commit.**

```bash
git add src/Struo.Api/GraphQl/FileFieldResolvers.cs tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs
git commit -m "feat(graphql): File/Image/Files resolve to File nodes via BatchDataLoader"
```

---

## Task 10: Relations single-level + depth-1 limit + permission/error mapping

Assert that selecting a relation drives a `DeepSpec`, the nested data surfaces, a relation-of-a-relation resolves empty, and a thrown `PermissionDeniedException` surfaces as `FORBIDDEN`.

**Files:**
- Test: add to `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs`

- [ ] **Step 1: Write the failing tests.**

Add:
```csharp
    [Fact]
    public async Task Selecting_relation_sets_Deep_and_nests_value()
    {
        DeepSpec? deepSeen = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (collection, q, _) =>
            {
                deepSeen = q.Deep;
                var row = new Dictionary<string, object?>
                {
                    ["id"] = "1",
                    ["category"] = new Dictionary<string, object?> { ["id"] = "9", ["name"] = "News" }
                };
                return new PagedResult(new IReadOnlyDictionary<string, object?>[] { row }, 1, q.Limit, q.Offset);
            }
        };
        var result = await (await ExecutorAsync(ds)).ExecuteAsync("{ articles { items { id category { name } } } }");
        Assert.NotNull(deepSeen);
        Assert.True(deepSeen!.Relations.ContainsKey("category"));
        Assert.Contains("\"name\": \"News\"", result.ToJson());
    }

    [Fact]
    public async Task Relation_not_selected_leaves_Deep_null()
    {
        DeepSpec? deepSeen = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _) => { deepSeen = q.Deep; return new PagedResult(
                new IReadOnlyDictionary<string, object?>[] { new Dictionary<string, object?> { ["id"] = "1" } }, 1, q.Limit, q.Offset); }
        };
        await (await ExecutorAsync(ds)).ExecuteAsync("{ articles { items { id } } }");
        Assert.Null(deepSeen);
    }

    [Fact]
    public async Task PermissionDenied_surfaces_as_FORBIDDEN_code()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, _, _) => throw new Struo.Application.Security.PermissionDeniedException("no read")
        };
        // executor needs the error filter to map the code:
        var executor = await new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddScoped<IGraphQlDataSource>(_ => ds)
            .AddLogging()
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>().AddType<UuidType>().AddType<AnyType>()
            .AddJsonTypeConverter().AddErrorFilter<StruoErrorFilter>().AddTypeModule<StruoTypeModule>()
            .BuildRequestExecutorAsync();
        var json = (await executor.ExecuteAsync("{ articles { total } }")).ToJson();
        Assert.Contains("FORBIDDEN", json);
    }
```

- [ ] **Step 2: Run to verify.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~GraphQlExecutionTests`
Expected: iterate to green. The depth-1 limit is implicit (nested `category` dict carries no further expanded relations); no extra production code needed.

- [ ] **Step 3: Full backend gate.**

Run:
```bash
dotnet build -warnaserror
dotnet test
```
Expected: build clean; all tests pass (378 prior + new GraphQL tests).

- [ ] **Step 4: Commit.**

```bash
git add tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs
git commit -m "test(graphql): single-level relations via Deep + FORBIDDEN mapping"
```

---

## Task 11: Program.cs integration smoke + max-depth guard

Confirm the endpoint is reachable through the real host pipeline (auth/permission middleware in front) and that the depth rule rejects an over-deep query.

**Files:**
- Test: `tests/Struo.Tests/GraphQl/GraphQlEndpointTests.cs` (uses `WebApplicationFactory` like existing API integration tests — follow the existing test host pattern in `tests/Struo.Tests`).

- [ ] **Step 1: Write the failing endpoint test.**

Create `tests/Struo.Tests/GraphQl/GraphQlEndpointTests.cs` following the repo's existing `WebApplicationFactory<Program>` pattern (grep tests for `WebApplicationFactory` to match the fixture and SQLite/config setup). Assert:
```csharp
// POST /graphql { "query": "{ __typename }" } returns 200 and data.__typename == "Query"
// POST a 20-level nested query returns an error mentioning max execution depth
```
Use the existing test host fixture; send `application/json` `{ "query": "..." }`.

- [ ] **Step 2: Run to verify failure, then confirm pass once wiring is correct.**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter FullyQualifiedName~GraphQlEndpointTests`
Expected: PASS — `/graphql` responds; over-deep query rejected. If the endpoint 404s, verify `app.MapStruoGraphQl()` placement (after `MapControllers`, after `PermissionResolutionMiddleware`).

- [ ] **Step 3: Full gate + commit.**

```bash
dotnet build -warnaserror && dotnet test
git add tests/Struo.Tests/GraphQl/GraphQlEndpointTests.cs
git commit -m "test(graphql): endpoint smoke + max-execution-depth guard"
```

---

## Task 12: Live gate on real PostgreSQL

The project rule (SQLite-green ≠ Postgres-correct) requires representative GraphQL queries against a live PG before declaring Phase 8 done. This is user-driven; record evidence.

**Files:** none (verification only). Record results in the ROADMAP row when done.

- [ ] **Step 1: Start the dev API against live Postgres + Redis** (+ MinIO for file resolution), per the repo's live-gate recipe (see `frontend/README.md` / prior phase notes: dev API on `:5080` against `web-struo-cms-db`).

- [ ] **Step 2: Authenticate** (bootstrap super-admin) and POST to `/graphql` (send UTF-8, not Big5 — use PowerShell `Invoke-RestMethod` or a UTF-8 body file per the project's live-verify note).

- [ ] **Step 3: Run and record evidence for each check:**
  - `articles(limit, offset, sort, filter: { status: { eq: "published" } }) { items { id status } total }` — filter/sort/pagination + `total` correct.
  - `article(id: "<guid>", locale: "zh-TW") { title translations { locale fields } }` — i18n overlay + `translations` (CJK exact by code point).
  - `articles { items { category { name } tags { name } heroImage { title } galleryFiles { title } } }` — single-level relations + File resolution.
  - a relation-of-a-relation (`category { articles { id } }`) resolves empty (depth-1 limit).
  - a collection with no public-read grant queried anonymously → `FORBIDDEN`; a public-read collection → returns data.
  - a bad filter value → `BAD_USER_INPUT` (not 500).

- [ ] **Step 4: Fix any Postgres-only issue found** (expect the SQLite-green≠PG class), add a regression test, re-run.

- [ ] **Step 5: Update `docs/ROADMAP.md`** with the Phase 8 done row + verification baseline, and record the new backend test count.

---

## Self-Review

**Spec coverage:**
- §3 layer/wiring → Tasks 1, 5, 11. ✓
- §4 schema generation + type mapping (incl. Repeater nested, Tags, Json/KeyValue→Any, File→ID+resolved, exhaustiveness, empty-schema guard `_service`) → Tasks 2, 7. ✓
- §4.3 File resolution → Task 9. ✓
- §5 filter (own+FK) / sort / pagination → Tasks 3, 4, 7 (filter input), 8. ✓
- §6 single-level relations via `deep` + selection-driven `DeepSpec` → Tasks 4, 7 (`SelectionRelations`), 10. ✓
- §7 i18n locale + translations field → Tasks 7 (Translation type), 8 (locale forwarding). ✓
- §8 auth/RBAC (enforced in ItemService, reached via adapter) + error mapping + depth guard + introspection dev-only → Tasks 1, 5, 10, 11. ✓
- §9 testing (schema, execution, batching, live gate) → Tasks 2–12. ✓

**Placeholder scan:** `FakeMetadataFixtures` (Task 6) and the endpoint test (Task 11) intentionally defer exact record-construction / test-host boilerplate to the implementer with explicit "open this file / grep this pattern" instructions, because those depend on exact Domain record members and the repo's existing `WebApplicationFactory` fixture — both are read-and-match, not invent. All production code steps show complete code. No "TBD/handle errors/similar to".

**Type consistency:** `IGraphQlDataSource.QueryAsync/GetAsync` signatures match `ItemService` (Task 5) and every call site (Tasks 7–9). `PagedResultView(Items, Total)` produced in Task 7, consumed by the `XList` type (Task 7) and list resolver (Task 7/8). `SchemaTypeMapper`/`FilterInputTranslator`/`GraphQlQueryBuilder` signatures are stable across producers and consumers. `ctx.BatchDataLoader<Guid, IReadOnlyDictionary<string,object?>?>` key/value types consistent in Task 9.

**Known API risk:** exact HotChocolate v15 namespaces/class names (`*Definition` under `HotChocolate.Types.Descriptors.Definitions`, `TypeReference` under `HotChocolate.Types.Descriptors`, `CreateUnsafe`, `PureFieldResolverDelegate`, `AddJsonTypeConverter`, `DependencyInjectionScope`) are verified against 15.1.x docs but may need a `using`/name tweak against the installed patch — Tasks 1/7 include a build step and the plan authorises the build-error-resolver agent to reconcile **without changing the verified design**.
