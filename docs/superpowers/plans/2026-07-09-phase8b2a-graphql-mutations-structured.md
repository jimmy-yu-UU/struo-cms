# Phase 8b.2a — GraphQL mutations (structured, non-i18n) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add typed GraphQL create/update **input** for the non-i18n structured/multi-value field kinds Phase 8b.1 deferred (M2M, File/Image, Files, MultiSelect/CheckboxGroup, Tags, Json, KeyValue, Repeater), so GraphQL writes reach REST parity for those kinds.

**Architecture:** Pure GraphQL-layer schema generation plus one new behaviour (recursive `SentFieldsOnly`). Each deferred kind gets an idiomatic input SDL; HotChocolate coerces the input to a nested dict/list/`Any` graph that the existing `MutationInputMapper.ToJsonElement` serialises straight into the `JsonElement` body the existing `ItemService` already accepts. No write-logic, DI, package, or DDL change.

**Tech Stack:** .NET 10 / C# latest, HotChocolate 16.4.0 (`HotChocolate.AspNetCore`), SqlSugarCore, xUnit + AwesomeAssertions, PostgreSQL (live gate) / SQLite (automated tests).

## Global Constraints

- **Dependency rule (§2):** changes live in `src/Struo.Api/GraphQl/` only. Domain / Application / Infrastructure are **untouched**; **no new NuGet packages**.
- **No `ItemService` write-logic change:** all validation (option membership, required, MaxLength, blank-row drop, de-dup, M2M id existence, RichText sanitize, optimistic `version`) is reused verbatim.
- **All input fields nullable** (required-ness validated server-side), except `TagItemInput.value: String!` (deliberate; empty value is already `BAD_USER_INPUT`).
- **Translatable boundary holds (→ 8b.2b):** `AddWritableFields` keeps `if (f.Translatable) continue;`; no translatable own-field, no `translations` input, no translatable File/Image appears in a 8b.2a input.
- **Build gate:** `dotnet build -warnaserror` = 0 warnings; `dotnet test` all green (490 baseline + new tests). Frontend untouched (237).
- **TDD (§17.2):** failing test first; live gate on real Postgres last (SQLite-green ≠ Postgres-correct).
- **Immutability (coding-style):** `SentFieldsOnly` returns new dicts/lists; never mutates the coerced argument graph.

---

### Task 1: `SchemaTypeMapper.WritableInputSdl` + input type-name helpers

Widen the writable input subset from "scalars + M2O FK only" to include the deferred kinds' SDL, and add the two new input type-name helpers. This replaces `WritableScalarInputSdl` (its only caller is `CollectionSchemaBuilder.AddWritableFields`, updated in Task 2).

**Files:**
- Modify: `src/Struo.Api/GraphQl/SchemaTypeMapper.cs`
- Test: `tests/Struo.Tests/GraphQl/SchemaTypeMapperMutationTests.cs`

**Interfaces:**
- Consumes: `FieldInterface` (Domain enum); existing `NumberSdl(Type?)` private helper.
- Produces:
  - `public static string? WritableInputSdl(FieldInterface iface, Type? clrType)` — returns the input SDL for a writable own-field, or `null` for interfaces the builder handles specially (`Tags`, `Repeater`) or excludes (`Hidden`, `Divider`, `Password`).
  - `public static string TagItemInputName()` → `"TagItemInput"`.
  - `public static string RepeaterItemInputTypeName(string collection, string field)` → e.g. `"ArticleFaqsItemInput"`.

- [ ] **Step 1: Replace the existing `WritableScalarInputSdl` unit tests with `WritableInputSdl` tests**

In `SchemaTypeMapperMutationTests.cs`, replace the two `WritableScalarInputSdl` facts (`Writable_scalar_interfaces_map_to_sdl`, `Number_maps_by_clr_type`) and `Deferred_and_excluded_interfaces_are_not_writable_scalars` with:

```csharp
[Theory]
[InlineData(FieldInterface.Text, "String")]
[InlineData(FieldInterface.Select, "String")]
[InlineData(FieldInterface.Radio, "String")]
[InlineData(FieldInterface.Time, "String")]
[InlineData(FieldInterface.Boolean, "Boolean")]
[InlineData(FieldInterface.Date, "Date")]
[InlineData(FieldInterface.DateTime, "DateTime")]
[InlineData(FieldInterface.Uuid, "ID")]
[InlineData(FieldInterface.File, "ID")]
[InlineData(FieldInterface.Image, "ID")]
[InlineData(FieldInterface.Files, "[ID!]")]
[InlineData(FieldInterface.MultiSelect, "[String!]")]
[InlineData(FieldInterface.CheckboxGroup, "[String!]")]
[InlineData(FieldInterface.Json, "Any")]
[InlineData(FieldInterface.KeyValue, "Any")]
public void Writable_input_interfaces_map_to_sdl(FieldInterface iface, string expected)
{
    SchemaTypeMapper.WritableInputSdl(iface, null).Should().Be(expected);
}

[Fact]
public void Writable_input_number_maps_by_clr_type()
{
    SchemaTypeMapper.WritableInputSdl(FieldInterface.Number, typeof(int)).Should().Be("Int");
    SchemaTypeMapper.WritableInputSdl(FieldInterface.Number, typeof(long)).Should().Be("Long");
    SchemaTypeMapper.WritableInputSdl(FieldInterface.Number, typeof(decimal)).Should().Be("Float");
}

[Theory]
[InlineData(FieldInterface.Tags)]      // named type -> builder handles specially
[InlineData(FieldInterface.Repeater)]  // named type -> builder handles specially
[InlineData(FieldInterface.Hidden)]
[InlineData(FieldInterface.Divider)]
[InlineData(FieldInterface.Password)]
public void Named_and_excluded_interfaces_have_no_input_sdl(FieldInterface iface)
{
    SchemaTypeMapper.WritableInputSdl(iface, null).Should().BeNull();
}

[Fact]
public void Input_type_name_helpers_follow_convention()
{
    SchemaTypeMapper.TagItemInputName().Should().Be("TagItemInput");
    SchemaTypeMapper.RepeaterItemInputTypeName("article", "faqs").Should().Be("ArticleFaqsItemInput");
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SchemaTypeMapperMutationTests"`
Expected: FAIL — `WritableInputSdl`, `TagItemInputName`, `RepeaterItemInputTypeName` do not exist (compile error).

- [ ] **Step 3: Implement `WritableInputSdl` + helpers, remove `WritableScalarInputSdl`**

In `SchemaTypeMapper.cs`, delete `WritableScalarInputSdl` and add:

```csharp
/// <summary>
/// SDL for a writable own-field in a create/update input (Phase 8b.2a). Covers writable scalars,
/// File/Image (→ ID), Files (→ [ID!]), MultiSelect/CheckboxGroup (→ [String!]), and Json/KeyValue
/// (→ Any). Returns <c>null</c> for Tags/Repeater (named input types the builder emits separately)
/// and for excluded interfaces (Hidden/Divider/Password). M2M relations and translatable own-fields
/// are handled by the builder, not here.
/// </summary>
public static string? WritableInputSdl(FieldInterface iface, Type? clrType) => iface switch
{
    FieldInterface.Text or FieldInterface.Textarea or FieldInterface.RichText or FieldInterface.Markdown
        or FieldInterface.Code or FieldInterface.Slug or FieldInterface.Email or FieldInterface.Url
        or FieldInterface.Color or FieldInterface.Phone or FieldInterface.Select or FieldInterface.Radio
        or FieldInterface.Time => "String",
    FieldInterface.Number or FieldInterface.Slider or FieldInterface.Rating => NumberSdl(clrType),
    FieldInterface.Boolean or FieldInterface.Checkbox => "Boolean",
    FieldInterface.Date => "Date",
    FieldInterface.DateTime => "DateTime",
    FieldInterface.Uuid or FieldInterface.File or FieldInterface.Image => "ID",
    FieldInterface.Files => "[ID!]",
    FieldInterface.MultiSelect or FieldInterface.CheckboxGroup => "[String!]",
    FieldInterface.Json or FieldInterface.KeyValue => "Any",
    _ => null, // Tags/Repeater (named types) + Hidden/Divider/Password (excluded)
};
```

Add the name helpers next to the existing `CreateInputName`/`UpdateInputName`:

```csharp
public static string TagItemInputName() => "TagItemInput";
public static string RepeaterItemInputTypeName(string collection, string field) =>
    RepeaterItemTypeName(collection, field) + "Input";
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SchemaTypeMapperMutationTests"`
Expected: PASS. (`CollectionSchemaBuilder` still references `WritableScalarInputSdl` at this point — if the project fails to compile, that is expected; Task 2 Step 3 switches the call site. To keep Task 1 independently green, also do the one-line call-site swap now: in `CollectionSchemaBuilder.AddWritableFields`, change `SchemaTypeMapper.WritableScalarInputSdl(...)` to `SchemaTypeMapper.WritableInputSdl(...)`.)

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl/SchemaTypeMapper.cs src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs tests/Struo.Tests/GraphQl/SchemaTypeMapperMutationTests.cs
git commit -m "feat(graphql): WritableInputSdl covers deferred kinds + input type-name helpers (8b.2a)"
```

---

### Task 2: `TagItemInput` shared type + emit deferred-kind & M2M inputs + `XFieldItemInput`

Emit the shared `TagItemInput` value type, build each Repeater's `XFieldItemInput` **once** per collection (shared by create + update), and extend `AddWritableFields` to emit Tags, Repeater, and M2M inputs (the scalar/list/`Any` kinds already flow through `WritableInputSdl` after Task 1).

**Files:**
- Modify: `src/Struo.Api/GraphQl/StruoTypeModule.cs` (add `TagItemInput` to shared types)
- Modify: `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs` (`Build`, `AddWritableFields`, new `BuildRepeaterItemInputType`)
- Test: `tests/Struo.Tests/GraphQl/GraphQlMutationSchemaTests.cs`

**Interfaces:**
- Consumes: `SchemaTypeMapper.WritableInputSdl`, `TagItemInputName`, `RepeaterItemInputTypeName` (Task 1); existing `CollectionSchemaBuilder.Field`, `InputObjectTypeConfiguration`, `InputFieldConfiguration`, `TypeReference.Parse`.
- Produces: schema now declares `input TagItemInput { value: String!, label: String }`, `input <Collection><Field>ItemInput { ...sub-fields }` per Repeater field, and each `XCreateInput`/`XUpdateInput` carries the deferred-kind + M2M input fields.

- [ ] **Step 1: Write the failing schema tests**

In `GraphQlMutationSchemaTests.cs`, replace the body of `Create_input_has_writable_scalars_and_m2o_fk_only` (rename it) and add two new facts:

```csharp
[Fact]
public async Task Create_input_includes_structured_multivalue_and_m2m_fields()
{
    var sdl = await BuildSdlAsync();
    var block = InputBlock(sdl, "ArticleCreateInput");

    // scalars + M2O FK (unchanged from 8b.1)
    block.Should().Contain("status: String");
    block.Should().Contain("publishedAt: DateTime");
    block.Should().Contain("categoryId: ID");

    // deferred kinds now typed (8b.2a)
    block.Should().Contain("heroImageId: ID");          // Image scalar own-field
    block.Should().Contain("regions: [String!]");        // MultiSelect
    block.Should().Contain("keywords: [TagItemInput!]"); // Tags
    block.Should().Contain("attributes: Any");           // Json
    block.Should().Contain("gallery: [ID!]");            // Files
    block.Should().Contain("faqs: [ArticleFaqsItemInput!]"); // Repeater
    block.Should().Contain("tags: [ID!]");               // M2M relation

    // boundary: translatable own-field + create carries no version
    block.Should().NotContain("title");
    block.Should().NotContain("version:");
}

[Fact]
public async Task Update_input_carries_deferred_kinds_and_version()
{
    var sdl = await BuildSdlAsync();
    var block = InputBlock(sdl, "ArticleUpdateInput");
    block.Should().Contain("keywords: [TagItemInput!]");
    block.Should().Contain("faqs: [ArticleFaqsItemInput!]");
    block.Should().Contain("tags: [ID!]");
    block.Should().Contain("version: Long");
}

[Fact]
public async Task TagItemInput_and_repeater_item_input_types_are_declared()
{
    var sdl = await BuildSdlAsync();

    var tag = InputBlock(sdl, "TagItemInput");
    tag.Should().Contain("value: String!");
    tag.Should().Contain("label: String");

    var faq = InputBlock(sdl, "ArticleFaqsItemInput");
    faq.Should().Contain("question: String");   // Text sub-field, nullable in input
    faq.Should().Contain("answer: String");     // Textarea sub-field
}
```

Keep the existing `Mutation_has_create_field_per_collection` and `Update_input_matches_create_plus_version` facts (the latter still passes — `status`/`categoryId`/`version` remain present).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutationSchemaTests"`
Expected: FAIL — the deferred-kind fields and `TagItemInput`/`ArticleFaqsItemInput` types are not yet emitted.

- [ ] **Step 3: Add the `TagItemInput` shared type**

In `StruoTypeModule.cs`, add to the shared value types (after `TagItemType()` in `CreateTypesAsync`):

```csharp
types.Add(TagItemInputType());
```

And add the method:

```csharp
private static InputObjectType TagItemInputType()
{
    var config = new InputObjectTypeConfiguration(
        SchemaTypeMapper.TagItemInputName(), null, typeof(IReadOnlyDictionary<string, object?>));
    config.Fields.Add(new InputFieldConfiguration("value", null, TypeReference.Parse("String!")));
    config.Fields.Add(new InputFieldConfiguration("label", null, TypeReference.Parse("String")));
    return InputObjectType.CreateUnsafe(config);
}
```

Add the required usings if missing: `using HotChocolate.Types.Descriptors.Configurations;` (for `InputObjectTypeConfiguration`/`InputFieldConfiguration`) and `using HotChocolate.Types.Descriptors;` (for `TypeReference`).

- [ ] **Step 4: Build each Repeater's input item type once, in `Build`**

In `CollectionSchemaBuilder.Build`, insert the Repeater input item types before the create/update inputs so both reference them by name:

```csharp
internal IEnumerable<ITypeSystemMember> Build(CollectionMetadata meta)
{
    var types = new List<ITypeSystemMember>();
    var relationNames = meta.Relations.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    types.Add(BuildObjectType(meta, relationNames, types));
    types.Add(BuildListType(meta));
    types.Add(BuildFilterInput(meta));

    // Repeater input item types — built ONCE per field, referenced by name in both inputs
    // (building them inside AddWritableFields would register a duplicate for create AND update).
    foreach (var f in meta.Fields)
        if (f is { Interface: FieldInterface.Repeater, Fields.Count: > 0 })
            types.Add(BuildRepeaterItemInputType(meta.Name, f));

    types.Add(BuildCreateInput(meta));
    types.Add(BuildUpdateInput(meta));
    return types;
}
```

Add the builder (mirrors the read-side `BuildRepeaterItemType`, but input-typed and all-nullable):

```csharp
private InputObjectType BuildRepeaterItemInputType(string collection, FieldMetadata repeater)
{
    var config = new InputObjectTypeConfiguration(
        SchemaTypeMapper.RepeaterItemInputTypeName(collection, repeater.Name), null,
        typeof(IReadOnlyDictionary<string, object?>));
    foreach (var sub in repeater.Fields!)
    {
        if (sub.Hidden || sub.ReadOnly || sub.IsSystem) continue;
        // Lean scalar sub-field set; input hint is string (same as the read side's BuildRepeaterItemType).
        var sdl = SchemaTypeMapper.WritableInputSdl(sub.Interface, typeof(string));
        if (sdl is null) continue;
        config.Fields.Add(new InputFieldConfiguration(sub.Name, null, TypeReference.Parse(sdl)));
    }
    return InputObjectType.CreateUnsafe(config);
}
```

- [ ] **Step 5: Extend `AddWritableFields` for Tags, Repeater, and M2M**

Replace `AddWritableFields` in `CollectionSchemaBuilder.cs` with:

```csharp
// Shared by create/update inputs: writable own-fields (scalars, File/Image, Files, multi-value,
// Json/KeyValue), Tags, Repeater, and M2M foreign-key arrays — all nullable. Translatable own-fields
// are excluded (8b.2b typed translations input).
private void AddWritableFields(InputObjectTypeConfiguration config, CollectionMetadata meta)
{
    var desc = registry.Get(meta.Name);
    foreach (var f in meta.Fields)
    {
        if (f.Hidden || f.ReadOnly || f.IsSystem) continue;
        if (f.Translatable) continue; // -> 8b.2b

        string? sdl = f.Interface switch
        {
            FieldInterface.Tags => $"[{SchemaTypeMapper.TagItemInputName()}!]",
            FieldInterface.Repeater when f.Fields is { Count: > 0 } =>
                $"[{SchemaTypeMapper.RepeaterItemInputTypeName(meta.Name, f.Name)}!]",
            _ => SchemaTypeMapper.WritableInputSdl(f.Interface, ClrType(desc, f.Name)),
        };
        if (sdl is null) continue;
        config.Fields.Add(new InputFieldConfiguration(f.Name, null, TypeReference.Parse(sdl)));
    }

    // M2O foreign keys (as ID) + M2M relations (as [ID!] target-id arrays).
    foreach (var rel in meta.Relations)
    {
        if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
            config.Fields.Add(new InputFieldConfiguration(fk, null, TypeReference.Parse("ID")));
        else if (rel.Kind == RelationKind.ManyToMany)
            config.Fields.Add(new InputFieldConfiguration(rel.Name, null, TypeReference.Parse("[ID!]")));
    }
}
```

- [ ] **Step 6: Run the schema tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutationSchemaTests"`
Expected: PASS (all facts, including the two retained ones).

- [ ] **Step 7: Run the full GraphQl suite to confirm no read-side regression**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~Struo.Tests.GraphQl"`
Expected: PASS. (Read-side tests assert on object/query/filter types, not input types, so adding input types must not affect them. If any full-SDL assertion trips, it is a genuine collision to fix here.)

- [ ] **Step 8: Commit**

```bash
git add src/Struo.Api/GraphQl/StruoTypeModule.cs src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs tests/Struo.Tests/GraphQl/GraphQlMutationSchemaTests.cs
git commit -m "feat(graphql): typed inputs for Tags/Repeater/M2M + deferred kinds (8b.2a)"
```

---

### Task 3: Recursive `SentFieldsOnly` (prune backfilled nested input fields)

Generalise the top-level-only `SentFieldsOnly` (8b.1) to recurse into nested input objects/lists, so HotChocolate's v16 null-backfill of unsent sub-fields (in `TagItemInput`/`XFieldItemInput` items) is pruned — the serialized body carries only client-sent keys at every depth, matching REST. `Any` (Json/KeyValue) is safe under blind structural recursion because it is never backfilled (literal keys always equal value keys), so recursing into it is a no-op.

**Files:**
- Modify: `src/Struo.Api/GraphQl/MutationResolvers.cs` (`SentFieldsOnly` → recursive)
- Test: `tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs`

**Interfaces:**
- Consumes: `HotChocolate.Language` (`IValueNode`, `ObjectValueNode`, `ListValueNode`); the existing `ctx.ArgumentLiteral<IValueNode>(argumentName)` and coerced `IReadOnlyDictionary<string, object?>`.
- Produces: `SentFieldsOnly` returns a dict whose nested `TagItemInput`/`XFieldItemInput` values contain only client-sent sub-keys; behaviour for top-level fields, scalars, and `Any` values is unchanged.

- [ ] **Step 1: Write the failing execution tests**

Add to `GraphQlMutationExecutionTests.cs`:

```csharp
[Fact]
public async Task Create_with_tags_and_multivalue_passes_structured_body()
{
    JsonElement? body = null;
    var ds = new FakeGraphQlDataSource
    {
        OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
        OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
    };

    var result = await (await ExecutorAsync(ds)).ExecuteAsync(
        "mutation { createArticle(input: { " +
        "regions: [\"apac\", \"emea\"], " +
        "keywords: [{ value: \"ai\", label: \"AI\" }, { value: \"ml\" }], " +
        "gallery: [\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\"], " +
        "tags: [\"9\"] " +
        "}) { id } }");
    ParseData(result);

    body!.Value.GetProperty("regions").EnumerateArray().Select(e => e.GetString())
        .Should().BeEquivalentTo(["apac", "emea"]);
    var kw = body.Value.GetProperty("keywords");
    kw[0].GetProperty("value").GetString().Should().Be("ai");
    kw[0].GetProperty("label").GetString().Should().Be("AI");
    // second tag omitted its label -> pruned (no "label" key), NOT backfilled null
    kw[1].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["value"]);
    body.Value.GetProperty("gallery").GetArrayLength().Should().Be(1);
    body.Value.GetProperty("tags").GetArrayLength().Should().Be(1);
}

[Fact]
public async Task Create_with_repeater_prunes_unsent_sub_fields()
{
    JsonElement? body = null;
    var ds = new FakeGraphQlDataSource
    {
        OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
        OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
    };

    var result = await (await ExecutorAsync(ds)).ExecuteAsync(
        "mutation { createArticle(input: { faqs: [{ question: \"Q1\" }] }) { id } }");
    ParseData(result);

    var row = body!.Value.GetProperty("faqs")[0];
    // Only the sent sub-field survives: "answer" was NOT backfilled as null.
    row.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["question"]);
    row.GetProperty("question").GetString().Should().Be("Q1");
}

[Fact]
public async Task Create_with_repeater_via_variables_prunes_unsent_sub_fields()
{
    JsonElement? body = null;
    var ds = new FakeGraphQlDataSource
    {
        OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
        OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
    };

    var request = OperationRequestBuilder.New()
        .SetDocument("mutation($input: ArticleCreateInput!) { createArticle(input: $input) { id } }")
        .SetVariableValues(new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?>
            {
                ["faqs"] = new object[] { new Dictionary<string, object?> { ["question"] = "Q1" } },
            },
        })
        .Build();

    ParseData(await (await ExecutorAsync(ds)).ExecuteAsync(request));

    var row = body!.Value.GetProperty("faqs")[0];
    row.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["question"]);
}

[Fact]
public async Task Create_with_json_any_preserves_full_object()
{
    JsonElement? body = null;
    var ds = new FakeGraphQlDataSource
    {
        OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
        OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
    };

    var result = await (await ExecutorAsync(ds)).ExecuteAsync(
        "mutation { createArticle(input: { attributes: { a: 1, b: { c: \"x\" } } }) { id } }");
    ParseData(result);

    // Any is opaque: every key the client wrote survives at every depth (no pruning damage).
    var attrs = body!.Value.GetProperty("attributes");
    attrs.GetProperty("a").GetInt32().Should().Be(1);
    attrs.GetProperty("b").GetProperty("c").GetString().Should().Be("x");
}

[Fact]
public async Task Update_with_empty_m2m_array_clears_junction()
{
    JsonElement? body = null;
    var ds = new FakeGraphQlDataSource
    {
        OnUpdate = (_, id, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = id }; },
        OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
    };

    var result = await (await ExecutorAsync(ds)).ExecuteAsync(
        "mutation { updateArticle(id: \"5\", input: { tags: [] }) { id } }");
    ParseData(result);

    // Explicit empty array is sent (present + empty) -> ItemService.SyncM2MAsync clears the junction.
    body!.Value.GetProperty("tags").GetArrayLength().Should().Be(0);
    body.Value.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["tags"]);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutationExecutionTests"`
Expected: FAIL — the repeater tests fail because unsent `answer` is currently backfilled as `null` into the body (`faqs[0]` has two keys, not one).

- [ ] **Step 3: Make `SentFieldsOnly` recursive**

In `MutationResolvers.cs`, replace `SentFieldsOnly` with a recursive version:

```csharp
// HotChocolate v16 backfills every declared field of a dict-runtime InputObjectType with null when
// the client omits it (see the 8b.1 note). 8b.1 pruned this at the TOP level via the argument
// literal; nested input objects (TagItemInput / XFieldItemInput items) are ALSO dict-runtime and
// backfilled, so a Repeater sub-field the client omitted would reach the body as null (and, for a
// non-nullable value-type sub-field, 500 on the child POCO deserialize). This prunes recursively:
// keep only keys present in the argument literal at every depth. Any (Json/KeyValue) is safe under
// this blind structural recursion — it is never backfilled, so its literal keys always equal its
// value keys and recursing into it is a no-op.
private static IReadOnlyDictionary<string, object?>? SentFieldsOnly(
    IResolverContext ctx, string argumentName, IReadOnlyDictionary<string, object?>? coerced)
{
    if (coerced is null) return null;
    var literal = ctx.ArgumentLiteral<IValueNode>(argumentName);
    return PruneObject(coerced, literal) as IReadOnlyDictionary<string, object?> ?? coerced;
}

// Returns a new value graph containing only the keys/elements present in the literal. When the
// literal shape does not match the value (e.g. a $variable whose literal is not fully expanded, or
// a scalar), the value is returned as-is.
private static object? PruneValue(object? value, IValueNode? literal) => (value, literal) switch
{
    (IReadOnlyDictionary<string, object?> dict, ObjectValueNode obj) => PruneObject(dict, obj),
    (System.Collections.IEnumerable list and not string, ListValueNode listNode) => PruneList(list, listNode),
    _ => value,
};

private static Dictionary<string, object?> PruneObject(
    IReadOnlyDictionary<string, object?> dict, IValueNode? literal)
{
    if (literal is not ObjectValueNode obj) return new Dictionary<string, object?>(dict);
    var byName = obj.Fields.ToDictionary(f => f.Name.Value, f => f.Value, StringComparer.Ordinal);
    var result = new Dictionary<string, object?>(byName.Count, StringComparer.Ordinal);
    foreach (var (key, node) in byName)
        if (dict.TryGetValue(key, out var v))
            result[key] = PruneValue(v, node);
    return result;
}

private static List<object?> PruneList(System.Collections.IEnumerable list, ListValueNode listNode)
{
    var items = list.Cast<object?>().ToList();
    var result = new List<object?>(items.Count);
    for (var i = 0; i < items.Count; i++)
    {
        var node = i < listNode.Items.Count ? listNode.Items[i] : null;
        result.Add(PruneValue(items[i], node));
    }
    return result;
}
```

Ensure the usings at the top of `MutationResolvers.cs` include `using HotChocolate.Language;` (already present for `IValueNode`/`ObjectValueNode`) — add nothing else.

- [ ] **Step 4: Run the execution tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutationExecutionTests"`
Expected: PASS — including the retained 8b.1 facts (`Create_returns_node_with_selected_fields_and_reread_relation`, both `_via_variables_` facts, partial-merge, error-code mappings). The top-level `BeEquivalentTo(["status", "categoryId"])` assertions still hold because top-level pruning is unchanged.

- [ ] **Step 5: Run the full GraphQl suite + mapper tests**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~Struo.Tests.GraphQl"`
Expected: PASS (schema, execution, mapper, data-source, endpoint, read-side).

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Api/GraphQl/MutationResolvers.cs tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs
git commit -m "fix(graphql): recursive SentFieldsOnly prunes backfilled nested input fields (8b.2a)"
```

---

### Task 4: Full build + suite verification + whole-slice review

**Files:** none (verification only).

- [ ] **Step 1: Build with warnings-as-errors**

Run: `dotnet build -warnaserror`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Run the entire backend suite**

Run: `dotnet test`
Expected: all green. New tests: ~5 SchemaTypeMapper facts (Task 1) + 3 schema facts (Task 2) + 5 execution facts (Task 3). Baseline 490 → ~503 (exact count recorded in the ROADMAP baseline note at merge time; the point is 0 failures).

- [ ] **Step 3: Confirm frontend untouched**

Run: `git diff --name-only main...HEAD -- frontend/`
Expected: empty (no frontend changes; frontend stays at 237 — do not run its suite for this slice).

- [ ] **Step 4: Whole-slice review**

Dispatch a code review (Opus, per workflow prefs) over the branch diff against the spec `docs/superpowers/specs/2026-07-09-phase8b2a-graphql-mutations-structured-design.md`. Focus: (a) no field outside `Struo.Api/GraphQl/` changed; (b) `if (f.Translatable) continue;` still present in `AddWritableFields`; (c) recursion cannot drop a client-sent key or corrupt an `Any` value; (d) Repeater input item type registered exactly once per field. Fix CRITICAL/HIGH inline with a follow-up commit.

- [ ] **Step 5: Commit any review fixes**

```bash
git add -A
git commit -m "fix(graphql): 8b.2a whole-slice review fixes"
```

---

### Task 5: Live gate on real Postgres (SQLite-green ≠ Postgres-correct)

Run representative GraphQL mutations against the live Postgres dev stack (`web-struo-cms-db` + Redis + MinIO), authenticated as the bootstrap super-admin. This is the project's mandatory persistence gate. Record evidence per convention.

**Files:** none (verification only; a `db/migrations/*.sql` is added ONLY if a live schema drift surfaces — none is expected, as 8b.2a adds no columns).

- [ ] **Step 1: Bring up / confirm the live stack and obtain a session**

Follow the same recipe used by the Phase 8/8b.1 live gates (dev API on `:5080` against live PG + Redis + MinIO; log in as the bootstrap super-admin; capture the session cookie + `X-Struo-CSRF` header). Send non-ASCII payloads as UTF-8 (PowerShell `Invoke-RestMethod` / UTF-8 file — Git Bash console here is Big5).

- [ ] **Step 2: Create with structured fields (CJK exact)**

`POST /graphql` (cookie + CSRF):

```graphql
mutation {
  createArticle(input: {
    status: "published",
    regions: ["apac", "emea"],
    keywords: [{ value: "人工智慧", label: "AI" }, { value: "ml" }],
    attributes: { featured: true, score: 9 },
    faqs: [{ question: "常見問題一", category: "general" }, { question: "Q2" }]
  }) {
    id status regions keywords { value label } attributes faqs { question category }
  }
}
```
Expected: 200; `status="published"`; `regions=["apac","emea"]`; `keywords[0].value` round-trips code-point-exact (`人工智慧` = U+4EBA U+5DE5 U+667A U+6167), `keywords[1]` has `label:null`; `attributes` reads back as a real JSON object; `faqs` in order with `category` on the first row only. Record the returned `id` + `version`.

- [ ] **Step 3: M2M + partial-merge update**

First create a `tag`, then:

```graphql
mutation { updateArticle(id: "<id>", input: { tags: ["<tagId>"], version: 0 }) { id tags { id name } version } }
```
Expected: 200; `tags` re-reads the linked tag; `version` 0→1; a follow-up `article(id)` query confirms `status`/`regions`/`faqs` from Step 2 are **untouched** (partial merge did not clear them).

- [ ] **Step 4: Validation → BAD_USER_INPUT**

```graphql
mutation { updateArticle(id: "<id>", input: { faqs: [{ answer: "no question" }], version: 1 }) { id } }
```
Expected: `BAD_USER_INPUT` (Repeater row missing required `question`). Then an out-of-options `regions: ["mars"]` → `BAD_USER_INPUT` ("not in its options"). Neither returns 500 (the varchar/JSON-text columns already exist from 7g+; no truncation).

- [ ] **Step 5: Clear M2M + delete**

```graphql
mutation { updateArticle(id: "<id>", input: { tags: [], version: 2 }) { id tags { id } } }
```
Expected: `tags` empty (junction cleared). Then `deleteArticle(id: "<id>")` → `true`; re-query `article(id)` → `null`.

- [ ] **Step 6: Record the live-gate evidence**

Append a Phase 8b.2a row to `docs/ROADMAP.md` (status, the checks above, any fixes) and update the phase table (add the 8b.2a row; mark 8b.2 as split into 8b.2a done / 8b.2b planned). Commit:

```bash
git add docs/ROADMAP.md
git commit -m "docs: Phase 8b.2a live gate PASSED on real Postgres (ROADMAP)"
```

---

## Self-Review

**Spec coverage:**
- §1.1 input SDL table → Task 1 (`WritableInputSdl`) + Task 2 (Tags/Repeater/M2M). ✓
- §3.1 `TagItemInput` → Task 2 Step 3. ✓
- §3.2 `XFieldItemInput` (built once) → Task 2 Steps 4–5. ✓
- §3.3 `WritableInputSdl` + name helpers → Task 1. ✓
- §3.4 `AddWritableFields` (translatable guard, M2M branch) → Task 2 Step 5. ✓
- §3.5 mapper unchanged → confirmed (no `MutationInputMapper.cs` edit in any task). ✓
- §4 recursive `SentFieldsOnly` (+ Any safety, M2M full-replace) → Task 3. ✓
- §5 RBAC/CSRF/concurrency/errors inherited → covered by retained 8b.1 execution facts (Task 3 Step 4) + live gate. ✓
- §6 testing (schema/execution/live) → Tasks 2, 3, 5. ✓
- §7 risks (nested backfill, Any input, repeater sub-field SDL) → Task 3 (recursion + Any test) + Task 2 (`ArticleFaqsItemInput` fields). ✓

**Placeholder scan:** no TBD/TODO; every code step shows complete code; every run step shows the exact command + expected result. ✓

**Type consistency:** `WritableInputSdl`, `TagItemInputName`, `RepeaterItemInputTypeName`, `BuildRepeaterItemInputType`, `SentFieldsOnly`/`PruneObject`/`PruneList`/`PruneValue` names are used identically across tasks; input type names (`TagItemInput`, `ArticleFaqsItemInput`) match between schema (Task 2) and execution (Task 3) tests. ✓
