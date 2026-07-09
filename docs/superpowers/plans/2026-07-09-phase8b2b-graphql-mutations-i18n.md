# Phase 8b.2b — GraphQL mutations (i18n / translations) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a typed GraphQL `translations` input to every collection's `createX`/`updateX` mutation so translatable content (incl. the per-locale OG image) can be written through GraphQL, reusing the existing `ItemService` translation write pipeline unchanged.

**Architecture:** Pure schema-generation + one resolver-side shape transform, all inside `src/Struo.Api/GraphQl/`. When a collection has a translation sidecar, the builder emits two per-collection input types (`XTranslationInput { locale, fields }` and `XTranslationFieldsInput { …translatable fields… }`) and adds `translations: [XTranslationInput!]` to both create/update inputs. A new `FoldTranslations` helper folds the GraphQL list-of-entries into the locale-keyed object `ItemService` consumes, running after the existing recursive `SentFieldsOnly` prune and before serialization. Domain / Application / Infrastructure are untouched; no new packages; no DDL.

**Tech Stack:** .NET 10, C# latest, HotChocolate v16 (`HotChocolate.AspNetCore`), xUnit + AwesomeAssertions, SqlSugarCore, PostgreSQL (live gate) / SQLite (automated tests).

## Global Constraints

- **Dependency rule (§2):** changes live only in `src/Struo.Api/GraphQl/` (+ the test project). Domain/Application/Infrastructure get **no** edits and **no** new packages.
- **`ItemService` write logic is unchanged** — all translation validation (locale enabled, field ⊆ translatable, required present, MaxLength after RichText sanitize, create-requires-default-locale) is reused verbatim.
- **`MutationInputMapper.cs` stays a field-agnostic serializer** — the list→object transform lives in `MutationResolvers`, never in the mapper.
- **All input fields nullable except `locale: String!` and `fields: XTranslationFieldsInput!`** — required-ness is validated server-side (parity with 8b.1/8b.2a).
- **`AddWritableFields`'s `if (f.Translatable) continue;` stays** — translatable fields are carried only by `translations`, never as top-level own-fields.
- **Read side is NOT changed** — `translations` on the read side stays `[Translation!] { locale, fields: Any! }` (accepted asymmetry, spec §1.1).
- **Build gate:** `dotnet build -warnaserror` must stay 0 warnings; `dotnet test` baseline is **500** (green before this phase) and must stay green + grow.
- **Naming:** `XTranslationInputName(c)` = `Pascal(c)+"TranslationInput"`; `XTranslationFieldsInputName(c)` = `Pascal(c)+"TranslationFieldsInput"`.
- **Commit messages:** conventional commits (`feat:` / `test:` / `docs:`), no attribution footer (disabled globally).
- **Branch:** `phase-8b2b-graphql-mutations-i18n` (already created; the spec commit `2abddfc` is its first commit).

Spec: [`docs/superpowers/specs/2026-07-09-phase8b2b-graphql-mutations-i18n-design.md`](../specs/2026-07-09-phase8b2b-graphql-mutations-i18n-design.md).

---

## File Structure

**Production (all under `src/Struo.Api/GraphQl/`):**
- `SchemaTypeMapper.cs` (public) — **modify:** add two name helpers.
- `CollectionSchemaBuilder.cs` (internal) — **modify:** build the two translation input types once in `Build`; emit `translations` in `AddWritableFields`.
- `MutationResolvers.cs` (internal) — **modify:** add `FoldTranslations`; call it in `ResolveCreate`/`ResolveUpdate` between `SentFieldsOnly` and `ToJsonElement`.

**Tests (all under `tests/Struo.Tests/GraphQl/`):**
- `SchemaTypeMapperMutationTests.cs` — **append** the name-helper test.
- `FakeMetadataFixtures.cs` — **modify:** add translatable `body` + `seoOgImageId` to the `article` fixture and extend `Translation.Fields`.
- `GraphQlMutationSchemaTests.cs` — **append** schema-shape tests for the translation inputs.
- `GraphQlMutationExecutionTests.cs` — **append** execution tests (fold, prune, add-locale, validation, partial-merge).

No other files change. `MutationInputMapper.cs`, `GraphQlDataSource.cs`, `StruoTypeModule.cs`, `Program.cs`, and everything outside `GraphQl/` are untouched.

---

### Task 1: `SchemaTypeMapper` translation input name helpers

**Files:**
- Modify: `src/Struo.Api/GraphQl/SchemaTypeMapper.cs`
- Test: `tests/Struo.Tests/GraphQl/SchemaTypeMapperMutationTests.cs`

**Interfaces:**
- Consumes: existing private `Pascal(string)`.
- Produces: `public static string TranslationInputName(string collection)` → `"ArticleTranslationInput"`; `public static string TranslationFieldsInputName(string collection)` → `"ArticleTranslationFieldsInput"`. Both consumed by `CollectionSchemaBuilder` in Task 2.

- [ ] **Step 1: Write the failing test**

Append to `tests/Struo.Tests/GraphQl/SchemaTypeMapperMutationTests.cs` (inside the class):

```csharp
    [Fact]
    public void Translation_input_type_name_helpers_follow_convention()
    {
        SchemaTypeMapper.TranslationInputName("article").Should().Be("ArticleTranslationInput");
        SchemaTypeMapper.TranslationFieldsInputName("article").Should().Be("ArticleTranslationFieldsInput");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SchemaTypeMapperMutationTests.Translation_input_type_name_helpers_follow_convention"`
Expected: FAIL to **compile** — `SchemaTypeMapper` has no `TranslationInputName` / `TranslationFieldsInputName`.

- [ ] **Step 3: Write minimal implementation**

In `src/Struo.Api/GraphQl/SchemaTypeMapper.cs`, add next to the other name helpers (after `RepeaterItemInputTypeName`):

```csharp
    public static string TranslationInputName(string collection) => Pascal(collection) + "TranslationInput";
    public static string TranslationFieldsInputName(string collection) => Pascal(collection) + "TranslationFieldsInput";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SchemaTypeMapperMutationTests.Translation_input_type_name_helpers_follow_convention"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl/SchemaTypeMapper.cs tests/Struo.Tests/GraphQl/SchemaTypeMapperMutationTests.cs
git commit -m "feat(graphql): translation input type-name helpers (8b.2b)"
```

---

### Task 2: Emit `translations` input types + input field (schema generation)

**Files:**
- Modify: `tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs` (augment the `article` fixture — test support the schema tests need)
- Modify: `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`
- Test: `tests/Struo.Tests/GraphQl/GraphQlMutationSchemaTests.cs`

**Interfaces:**
- Consumes: `SchemaTypeMapper.TranslationInputName` / `TranslationFieldsInputName` (Task 1); existing `SchemaTypeMapper.WritableInputSdl`, `ClrType`, `AddWritableFields`.
- Produces: for every collection with `meta.Translation is not null`, the schema now declares `input XTranslationFieldsInput`, `input XTranslationInput { locale: String!, fields: XTranslationFieldsInput! }`, and a `translations: [XTranslationInput!]` field on both `XCreateInput` and `XUpdateInput`. Consumed by Task 3/4 execution.

- [ ] **Step 1: Augment the `article` fixture with translatable fields**

The current `article` fixture has only one translatable field (`title`). Add a translatable `body` (RichText → `String`) and a translatable `seoOgImageId` (Image → `ID`, the per-locale OG image) so the tests can exercise (a) a typed translation fields input with ≥2 fields, (b) File/Image → `ID` inside it, and (c) recursive pruning of an unsent translatable field. These are translatable, so — like `title` — they are **not** added to `ArticlePoco` / `FieldToProperty` (they live on the sidecar; `ClrType` returns null and `WritableInputSdl` needs no CLR hint for `String`/`ID`).

In `tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs`, inside `Article()`, add two `FieldMetadata` entries to the `Fields` list (after the `faqs` entry, before the closing `]`):

```csharp
            new FieldMetadata
            {
                Name = "body", Label = "Body", Interface = FieldInterface.RichText,
                Translatable = true, Sort = 10,
            },
            new FieldMetadata
            {
                Name = "seoOgImageId", Label = "OG Image", Interface = FieldInterface.Image,
                Translatable = true, Sort = 11,
            },
```

Then extend the `Translation` metadata's `Fields` so all three translatable fields are recognised:

```csharp
        Translation = new TranslationMetadata
        {
            ForeignKeyProperty = "ArticleId",
            LocaleProperty = "Locale",
            Fields = ["title", "body", "seoOgImageId"],
        },
```

- [ ] **Step 2: Write the failing tests**

Append to `tests/Struo.Tests/GraphQl/GraphQlMutationSchemaTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task Create_and_update_inputs_include_translations_list()
    {
        var sdl = await BuildSdlAsync();
        InputBlock(sdl, "ArticleCreateInput").Should().Contain("translations: [ArticleTranslationInput!]");
        InputBlock(sdl, "ArticleUpdateInput").Should().Contain("translations: [ArticleTranslationInput!]");
    }

    [Fact]
    public async Task Translation_wrapper_input_has_locale_and_typed_fields()
    {
        var sdl = await BuildSdlAsync();
        var wrapper = InputBlock(sdl, "ArticleTranslationInput");
        wrapper.Should().Contain("locale: String!");
        wrapper.Should().Contain("fields: ArticleTranslationFieldsInput!");
    }

    [Fact]
    public async Task Translation_fields_input_lists_translatable_fields_all_nullable()
    {
        var sdl = await BuildSdlAsync();
        var fields = InputBlock(sdl, "ArticleTranslationFieldsInput");
        fields.Should().Contain("title: String");          // Text (nullable in input; required validated server-side)
        fields.Should().Contain("body: String");           // RichText -> String
        fields.Should().Contain("seoOgImageId: ID");        // translatable Image -> ID
        fields.Should().NotContain("String!");              // no required marker on the field-map
    }

    [Fact]
    public async Task Translatable_fields_do_not_leak_to_top_level_inputs()
    {
        var sdl = await BuildSdlAsync();
        // body/seoOgImageId are translatable -> present ONLY inside the fields input, never as
        // top-level own-fields of the create/update input.
        InputBlock(sdl, "ArticleCreateInput").Should().NotContain("body:");
        InputBlock(sdl, "ArticleCreateInput").Should().NotContain("seoOgImageId:");
    }

    [Fact]
    public async Task Collection_without_sidecar_has_no_translations_input()
    {
        var sdl = await BuildSdlAsync();
        // Category has no translation sidecar -> no translations field, no translation input types.
        InputBlock(sdl, "CategoryCreateInput").Should().NotContain("translations");
        sdl.Should().NotContain("CategoryTranslationInput");
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutationSchemaTests"`
Expected: the 5 new tests FAIL — the SDL has no `ArticleTranslationInput` / `ArticleTranslationFieldsInput` and no `translations` field. (The pre-existing tests in this class still pass.)

- [ ] **Step 4: Build the input types in `CollectionSchemaBuilder.Build`**

In `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`, in `Build(...)`, after the Repeater input-item-types loop and before `types.Add(BuildCreateInput(meta))`, add:

```csharp
        // Translation input types — built ONCE per collection (referenced by name in both inputs,
        // like the Repeater item input). Only for collections with a translation sidecar.
        if (meta.Translation is not null)
        {
            types.Add(BuildTranslationFieldsInputType(meta)); // XTranslationFieldsInput
            types.Add(BuildTranslationInputType(meta));        // XTranslationInput { locale, fields }
        }
```

Add these two methods to the class (near `BuildRepeaterItemInputType`):

```csharp
    // XTranslationFieldsInput: one input field per translatable own-field, all nullable, SDL via the
    // shared WritableInputSdl (so a translatable Image/File -> ID, RichText/Text/Textarea -> String).
    private InputObjectType BuildTranslationFieldsInputType(CollectionMetadata meta)
    {
        var desc = registry.Get(meta.Name);
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.TranslationFieldsInputName(meta.Name), null,
            typeof(IReadOnlyDictionary<string, object?>));
        foreach (var f in meta.Fields)
        {
            if (!f.Translatable) continue;
            if (f.Hidden || f.ReadOnly || f.IsSystem) continue;
            var sdl = SchemaTypeMapper.WritableInputSdl(f.Interface, ClrType(desc, f.Name));
            if (sdl is null) continue;
            config.Fields.Add(new InputFieldConfiguration(f.Name, null, TypeReference.Parse(sdl)));
        }
        return InputObjectType.CreateUnsafe(config);
    }

    // XTranslationInput: one entry = { locale: String!, fields: XTranslationFieldsInput! }.
    private static InputObjectType BuildTranslationInputType(CollectionMetadata meta)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.TranslationInputName(meta.Name), null,
            typeof(IReadOnlyDictionary<string, object?>));
        config.Fields.Add(new InputFieldConfiguration("locale", null, TypeReference.Parse("String!")));
        config.Fields.Add(new InputFieldConfiguration(
            "fields", null, TypeReference.Parse(SchemaTypeMapper.TranslationFieldsInputName(meta.Name) + "!")));
        return InputObjectType.CreateUnsafe(config);
    }
```

- [ ] **Step 5: Emit the `translations` field in `AddWritableFields`**

In `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`, in `AddWritableFields`, after the relations `foreach` loop (right before the method's closing brace), add:

```csharp
        // Typed translations input (8b.2b) — only when the collection has a translation sidecar.
        // Translatable own-fields stay excluded above (`if (f.Translatable) continue;`); they are
        // carried here instead.
        if (meta.Translation is not null)
            config.Fields.Add(new InputFieldConfiguration(
                "translations", null,
                TypeReference.Parse($"[{SchemaTypeMapper.TranslationInputName(meta.Name)}!]")));
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutationSchemaTests"`
Expected: all tests in the class PASS (5 new + the pre-existing ones — the pre-existing `Create_input_includes_structured_multivalue_and_m2m_fields` still passes because `title` stays out of the `ArticleCreateInput` block and the block's other assertions are unchanged).

- [ ] **Step 7: Run the full GraphQL schema/read suite to confirm no ripple**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~Struo.Tests.GraphQl"`
Expected: PASS. The fixture gained `body`/`seoOgImageId`, which the read side emits additively (`body: String`, `seoOgImageId: ID`, `seoOgImage: File`); `GraphQlSchemaTests` uses `.Contain(...)` only, so nothing breaks.

- [ ] **Step 8: Commit**

```bash
git add src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs tests/Struo.Tests/GraphQl/GraphQlMutationSchemaTests.cs
git commit -m "feat(graphql): typed translations input types + input field (8b.2b schema)"
```

---

### Task 3: `FoldTranslations` — list → locale-keyed object, wired into resolvers

**Files:**
- Modify: `src/Struo.Api/GraphQl/MutationResolvers.cs`
- Test: `tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs`

**Interfaces:**
- Consumes: existing `SentFieldsOnly(ctx, "input", input)` and `MutationInputMapper.ToJsonElement(...)`.
- Produces: the create/update body's `translations` value is now the locale-keyed object `{ "<locale>": { <field>: <value> } }` that `ItemService.SyncTranslationsAsync` consumes. `FoldTranslations` is a `private static` helper proven via execution (`capturedBody`), since the test project cannot see `internal` members of `Struo.Api` (no `InternalsVisibleTo`).

- [ ] **Step 1: Write the failing test**

Append to `tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task Create_folds_translations_list_into_locale_keyed_object()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { status: \"published\", translations: [" +
            "{ locale: \"en\", fields: { title: \"Hello\" } }, " +
            "{ locale: \"zh-TW\", fields: { title: \"你好\", seoOgImageId: \"3f2504e0-4f89-11d3-9a0c-0305e82c3301\" } } " +
            "] }) { id } }");
        ParseData(result);

        // translations reached ItemService as a locale-KEYED OBJECT (not a list): { en: {...}, zh-TW: {...} }.
        var tr = body!.Value.GetProperty("translations");
        tr.ValueKind.Should().Be(JsonValueKind.Object);
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("Hello");
        tr.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("你好");
        tr.GetProperty("zh-TW").GetProperty("seoOgImageId").GetString()
            .Should().Be("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        // Locale codes and field keys are verbatim (NOT camelCased) — zh-TW stays zh-TW.
        tr.TryGetProperty("zhTw", out _).Should().BeFalse();
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutationExecutionTests.Create_folds_translations_list_into_locale_keyed_object"`
Expected: FAIL — without the fold, `translations` reaches the body as a JSON **array** of `{locale,fields}` entries, so `tr.ValueKind` is `Array` and `GetProperty("en")` throws.

- [ ] **Step 3: Add the `FoldTranslations` helper**

In `src/Struo.Api/GraphQl/MutationResolvers.cs`, add this `private static` method next to `SentFieldsOnly`:

```csharp
    // GraphQL sends `translations` as a list of { locale, fields } entries (mirroring the read side's
    // [Translation!]); ItemService.SyncTranslationsAsync consumes a locale-keyed object
    // { "<locale>": { <field>: <value> } }. Fold the (already SentFieldsOnly-pruned) list into that
    // object, returning a NEW dict so the input is not mutated (CLAUDE immutability). Runs AFTER the
    // recursive prune (so each entry's `fields` carries only client-sent sub-fields) and BEFORE
    // ToJsonElement. Absent/unexpected `translations` -> pass through unchanged (ItemService then
    // enforces create-requires-default-locale / rejects a non-object). Duplicate locale -> last wins.
    private static IReadOnlyDictionary<string, object?>? FoldTranslations(
        IReadOnlyDictionary<string, object?>? input)
    {
        if (input is null) return null;
        var raw = input.GetValueOrDefault("translations");
        if (raw is not System.Collections.IEnumerable entries || raw is string) return input;

        var byLocale = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is not IReadOnlyDictionary<string, object?> e) continue;
            if (e.GetValueOrDefault("locale") is not string locale) continue;
            byLocale[locale] = e.GetValueOrDefault("fields");
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in input) result[k] = v;
        result["translations"] = byLocale;
        return result;
    }
```

- [ ] **Step 4: Wire it into both resolvers**

In `ResolveCreate`, change the `body` line from:

```csharp
        var body = MutationInputMapper.ToJsonElement(SentFieldsOnly(ctx, "input", input));
```

to:

```csharp
        var body = MutationInputMapper.ToJsonElement(FoldTranslations(SentFieldsOnly(ctx, "input", input)));
```

Make the identical change in `ResolveUpdate` (same single line).

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutationExecutionTests.Create_folds_translations_list_into_locale_keyed_object"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Api/GraphQl/MutationResolvers.cs tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs
git commit -m "feat(graphql): fold translations list into locale-keyed body (8b.2b)"
```

---

### Task 4: Execution coverage — prune, add-locale, validation, partial-merge

**Files:**
- Test: `tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs`

**Interfaces:**
- Consumes: the schema (Task 2) + fold wiring (Task 3); `FakeGraphQlDataSource` (`OnCreate`/`OnUpdate`/`OnGet`), `OperationRequestBuilder` (variables), `ParseData` (throws on GraphQL `errors`).
- Produces: no new production code — this task only adds tests that lock the recursive-prune-through-fold, add-locale, validation-code, and partial-merge behaviours.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task Create_translations_prunes_unsent_fields_inline()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        // Send only `title` in the en entry — `body`/`seoOgImageId` are NOT backfilled as null.
        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { translations: [ { locale: \"en\", fields: { title: \"T\" } } ] }) { id } }");
        ParseData(result);

        var en = body!.Value.GetProperty("translations").GetProperty("en");
        en.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["title"]);
        en.GetProperty("title").GetString().Should().Be("T");
    }

    [Fact]
    public async Task Create_translations_prunes_unsent_fields_via_variables()
    {
        // $variable form: SentFieldsOnly recovers sent keys from the post-substitution literal, so
        // the nested fields object prunes the same way as an inline literal (guard for both forms).
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
                    ["translations"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["locale"] = "en",
                            ["fields"] = new Dictionary<string, object?> { ["title"] = "T" },
                        },
                    },
                },
            })
            .Build();

        ParseData(await (await ExecutorAsync(ds)).ExecuteAsync(request));

        var en = body!.Value.GetProperty("translations").GetProperty("en");
        en.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["title"]);
    }

    [Fact]
    public async Task Update_adds_locale_and_partial_merges_other_fields()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, id, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = id }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"5\", input: { version: 2, translations: [ { locale: \"zh-TW\", fields: { title: \"新\" } } ] }) { id } }");
        ParseData(result);

        // Only version + translations were sent; translations folded to a locale-keyed object.
        body!.Value.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["version", "translations"]);
        body.Value.GetProperty("translations").GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("新");
    }

    [Fact]
    public async Task Update_without_translations_leaves_them_untouched()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, id, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = id }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"5\", input: { status: \"published\" }) { id } }");
        ParseData(result);

        // No `translations` key sent -> fold is a no-op -> body has no translations key (partial merge).
        body!.Value.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["status"]);
    }

    [Fact]
    public async Task Duplicate_locale_last_writer_wins()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { translations: [ " +
            "{ locale: \"en\", fields: { title: \"first\" } }, " +
            "{ locale: \"en\", fields: { title: \"second\" } } ] }) { id } }");
        ParseData(result);

        body!.Value.GetProperty("translations").GetProperty("en").GetProperty("title").GetString().Should().Be("second");
    }

    [Fact]
    public async Task Create_missing_required_translation_field_maps_to_BAD_USER_INPUT()
    {
        // ItemService validates translations verbatim; the resolver adds no new validation. Simulate
        // the domain rejection and assert the error filter maps it to BAD_USER_INPUT (parity with REST).
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, _) => throw new QueryException(
                "Required translation field 'title' is missing for locale 'en'."),
        };

        var json = (await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { translations: [ { locale: \"en\", fields: { } } ] }) { id } }")).ToJson();

        json.Should().Contain("BAD_USER_INPUT");
        json.Should().Contain("Required translation field");
    }
```

- [ ] **Step 2: Run the tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlMutationExecutionTests"`
Expected: PASS (the 6 new tests + all pre-existing mutation-execution tests). These need no production change — Tasks 2–3 already supply the schema + fold. If any FAIL, the fold/schema wiring from Tasks 2–3 is wrong; fix there, not by weakening the test.

- [ ] **Step 3: Commit**

```bash
git add tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs
git commit -m "test(graphql): translations prune/add-locale/validation/partial-merge (8b.2b)"
```

---

### Task 5: Full gate, live gate (real Postgres), docs + finalize

**Files:**
- Modify: `docs/ROADMAP.md` (add the 8b.2b row + verification baseline + set 8b.2b status ✅)
- Modify: `docs/superpowers/specs/2026-07-09-phase8b2b-graphql-mutations-i18n-design.md` (append a live-gate result note if the gate surfaces anything)
- Modify: `C:\Users\YuJimmy\.claude\projects\D--dotnet-struo-cms\memory\MEMORY.md` + a new memory file (per the memory convention)

**Interfaces:**
- Consumes: the merged Tasks 1–4.
- Produces: green build/test baseline, a recorded live-gate result, updated roadmap/spec/memory, and a decision on branch integration.

- [ ] **Step 1: Full backend gate (warnings-as-errors + whole suite)**

Run:
```bash
dotnet build src/Struo.Api -warnaserror
dotnet test tests/Struo.Tests
```
Expected: build 0 warnings; `dotnet test` **all green**, count = 500 + the new tests (1 name-helper + 5 schema + 1 fold + 6 execution ≈ **513**; confirm the exact number and record it). If red, fix before proceeding — do NOT continue to the live gate on a red suite.

- [ ] **Step 2: Frontend untouched (sanity)**

No frontend change in this phase. Confirm nothing was touched:
```bash
git diff --name-only main...HEAD -- frontend/
```
Expected: empty output (frontend stays at 237 tests; no need to re-run).

- [ ] **Step 3: Live gate on real Postgres (SQLite-green ≠ Postgres-correct)**

Bring up the dev API against the live Postgres (`web-struo-cms-db`) + Redis + MinIO per the project's live-gate recipe (same environment used by the 8b.2a gate). Authenticate as the bootstrap super-admin, then run **createArticle now that translations are expressible** (this is the mutation 8b.1/8b.2a could not reach). Non-ASCII payloads MUST be sent as UTF-8 (PowerShell `Invoke-RestMethod` / UTF-8 body file — Git Bash console is Big5). Cover, recording evidence per check:

  1. **createArticle with default + second locale** — `translations: [{locale:"en", fields:{title:"Hello", body:"<p>Hi</p>"}}, {locale:"zh-TW", fields:{title:"你好", body:"..."}}]` → **succeeds** (201-equivalent; the 8b.1/8b.2a `BAD_USER_INPUT` "default locale" boundary is now passable). Re-read returns both locales; CJK exact by code point (`你好` = U+4F60 U+597D).
  2. **RichText sanitize on the translation path** — a `body` carrying `<script>`/`onclick`/`javascript:` reads back stripped (the `ItemService` translation-path sanitizer, reused).
  3. **Per-locale OG image (translatable File/Image)** — set a different `seoOgImageId` per locale (upload two files first); each round-trips and resolves to its `File` node on read.
  4. **updateArticle partial-merge** — send only `status` (no `translations`) → untouched locales/fields remain; then add/edit one locale via `translations` → only that locale changes.
  5. **create missing the default locale** — `translations` with only a non-default locale → `BAD_USER_INPUT` "default locale required".
  6. **recursive prune on the wire** — a translation entry sending only `title` does not clear/400 `body`/`seoOgImageId`.

Record the results (pass/fail + any fix commits) in the spec under a new "Live gate" note. If the gate surfaces a real bug, fix it under TDD (failing test first) and re-run — that is expected and valuable (every prior slice's live gate found the Postgres-only class of bug).

- [ ] **Step 4: Update ROADMAP**

In `docs/ROADMAP.md`: change the Phase 8b.2b table row status from `⬜ planned` to ✅ with the spec/plan links; add a "Phase 8b.2b … done & live-verified" bullet in the status list mirroring the 8b.2a entry's depth (typed translations input, fold, recursive-prune reuse, read-side asymmetry accepted, createArticle now succeeds, live-gate evidence, test count); update "Next up" to Phase 9 or 8c. Commit:
```bash
git add docs/ROADMAP.md docs/superpowers/specs/2026-07-09-phase8b2b-graphql-mutations-i18n-design.md
git commit -m "docs: Phase 8b.2b done + live-verified (spec/ROADMAP)"
```

- [ ] **Step 5: Update memory**

Add a new memory file `phase8b2b-graphql-mutations-i18n-done.md` (frontmatter `type: project`) capturing: typed per-collection `translations` input (`XTranslationInput{locale,fields}` + `XTranslationFieldsInput`), built once in `Build()` guarded by `meta.Translation`; the one new behaviour = `FoldTranslations` (list→locale-keyed, immutable, after recursive prune); read-side stays `Any` (accepted asymmetry); `createArticle` now succeeds; live-gate result + any fixes; final test count. Add a one-line pointer to `MEMORY.md`. (Link `[[phase8b2a-graphql-mutations-structured-done]]`.)

- [ ] **Step 6: Finalize the branch**

Invoke **superpowers:finishing-a-development-branch** to choose integration (merge to `main` `--no-ff`, mirroring the 8b.2a merge, or PR). Do not merge without the user's go-ahead.

---

## Self-Review

**1. Spec coverage:**
- Spec §1.1 typed per-collection input → Task 2 (`XTranslationInput`/`XTranslationFieldsInput`, `WritableInputSdl`). ✓
- Spec §1.1 list-of-entries + fold → Task 3 (`FoldTranslations`). ✓
- Spec §1.1 `locale: String!` / `fields: XTranslationFieldsInput!` non-null → Task 2 Step 4 + schema test. ✓
- Spec §1.1 read-side stays `Any` → Global Constraints + no read-side task. ✓
- Spec §1.2 no new sample entity/fields → live gate uses the real `ArticleTranslation`; fixture augmentation is test-only. ✓
- Spec §3.1 build-once, referenced by both inputs → Task 2 Step 4 (`Build`, not `AddWritableFields`). ✓
- Spec §3.2 `translations` added to both inputs, `if (f.Translatable) continue;` stays → Task 2 Step 5. ✓
- Spec §3.3 name helpers → Task 1. ✓
- Spec §4 fold after prune, before serialize, immutable, dup-locale last-wins, absent = no-op → Task 3 helper + Task 4 tests. ✓
- Spec §5 recursive `SentFieldsOnly` reused (inline + $variable) → Task 4 prune tests. ✓
- Spec §5 validation/errors reused, createX now succeeds → Task 4 validation test + Task 5 live gate. ✓
- Spec §7.1 schema tests → Task 2. §7.3 execution tests → Tasks 3–4. §7.4 live gate → Task 5. ✓
- Spec §8 `Any` known limitation does not apply (translations are typed) → no action needed; noted. ✓

**2. Placeholder scan:** No TBD/TODO/"add error handling"/"similar to Task N". Every code step shows complete code. The only non-verbatim item is the live-gate environment recipe (Step 3), which is an external manual procedure referencing the established project convention, not a code placeholder.

**3. Type consistency:** `TranslationInputName`/`TranslationFieldsInputName` (Task 1) used identically in Task 2. `FoldTranslations(IReadOnlyDictionary<string,object?>?) → IReadOnlyDictionary<string,object?>?` composes with `SentFieldsOnly` (same type) and `MutationInputMapper.ToJsonElement(IReadOnlyDictionary<string,object?>?)`. Fixture field names (`body`, `seoOgImageId`) match every test assertion and the `Translation.Fields` list. SDL strings (`[ArticleTranslationInput!]`, `locale: String!`, `fields: ArticleTranslationFieldsInput!`, `seoOgImageId: ID`) are consistent across Tasks 2 and the tests.
