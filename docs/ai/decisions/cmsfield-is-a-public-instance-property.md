# `[CmsField]` is refused where the scan does not serve it or a write does not reach it

## Decision

The metadata scan refuses, with a `MetadataException`, a `[CmsField]` declaration in four cases.
Each check runs for every type the scan reads fields from: a collection class, a translation class,
a `Repeater` row type, and the base classes of each.

- **Static or not public** (`FieldDeclarationCheck`). A public property whose getter is not public
  stays a field.
- **Lost through an override or a `new` member** (`FieldInheritanceCheck`). A `[CmsField]`
  declaration that a derived class overrides without repeating `[CmsField]`, or hides with a `new`
  member that carries none, is refused. An override or a `new` member that carries its own
  `[CmsField]` is the field.
- **Writable, and no write reaches it** (`FieldSetterCheck`). A field whose `ReadOnly` is not `true`
  must be bindable:
  - on a collection class, through a public setter (`set` or `init`), or a non-public setter the
    class can reach that carries `[JsonInclude]` on its own declaration;
  - on a `Repeater` row type, through a public setter, or through a parameter of the same name and
    type on the row type's one public constructor, unless another constructor carries
    `[JsonConstructor]`;
  - on a translation class, through any setter the class can reach.
- **`ReadOnly = true` on a translation field** (`TranslationReadOnlyCheck`). A translations payload
  writes every field of a translation class, so the flag is refused there.

## Why

The scan reads fields with `GetProperties(Public | Instance)` and `[CmsField]` is not inherited, so
a declaration outside that list, or one replaced by a member without the attribute, declares a field
nobody gets: the collection starts, and the field is absent from the schema and from every response.

A write binds a collection field and a `Repeater` row with System.Text.Json, which skips a
non-public setter unless it carries `[JsonInclude]` and builds a row through its public constructor.
A field it cannot bind is listed as writable and the value sent for it is dropped. A translation
value is assigned by reflection, which calls a non-public setter of the class and throws when there
is none. That write does not look at `ReadOnly`, while the GraphQL input type leaves a `ReadOnly`
field out and the schema reports it read-only: the flag holds on one path only. A value only server
code sets belongs on the parent entity, where `ReadOnly` holds on every path.

Refusing each declaration at start names the property while the fork is still editing it. The checks
list the declared members of each class in the hierarchy (`DeclaredProperties`), so they see the
non-public ones without asking reflection for non-public members.

## Evidence

Measured on SQLite through the item service on 2026-11-26, at the commit before each check, each
shape next to a public `{ get; set; }` property as the control.

Placement. "Scanned" is whether the field is in the scanned metadata.

| Shape of the `[CmsField]` property | Column | Scanned | Create and read |
|---|---|---|---|
| `private`, `internal` | none | no | the field is absent, no error |
| `public static` | yes | no | create threw `InvalidOperationException` |
| `public`, `{ private get; set; }` | yes | yes | as the control |

The create on the static shape failed with `Sequence contains no elements`.

Override and `new`. The base class declares `[CmsField]` on a public property.

| Derived class | Scanned | Column | A value sent on create |
|---|---|---|---|
| overrides it, no `[CmsField]` | no | yes | ignored |
| hides it with `internal new`, no `[CmsField]` | no | none | ignored |
| hides it with `public new`, no `[CmsField]` | no | yes | ignored |
| hides it with `public new` and `[CmsField]` | yes | yes | stored |

Setter, collection field. "Dropped" is a value sent and not stored.

| Setter | Stored column | `IsIgnore` | `ReadOnly = true` |
|---|---|---|---|
| `set`, `init` | stored | bound, never persisted | write ignored by design |
| `protected`, `internal`, `private` | dropped | dropped | accepted |
| none, or expression-bodied | dropped | dropped | accepted |
| non-public with `[JsonInclude]` | stored and read back | not measured | accepted |
| override without `[JsonInclude]` of a base that has it | dropped | not measured | accepted |

Setter, `Repeater` row field.

| Row type | Stored | Read back |
|---|---|---|
| `set`, `init`, positional record | yes | yes |
| one public constructor, property get-only or with a non-public setter | yes | yes |
| parameterless constructor, get-only property | no | no |
| a parameterless and a parameterised public constructor, get-only property | no | no |
| non-public setter with `[JsonInclude]`, no constructor parameter | yes | no, reads back empty |
| private `[JsonConstructor]` beside one public constructor | no | no |

Setter, translation field: a `protected`, `internal` or `private` setter of the translation class
stored the value; a field with no setter made the write throw `ArgumentException: Property set
method not found`.

`ReadOnly = true` on a translation field with a setter.

| Path | The value sent for the field |
|---|---|
| `translations` payload on create and on update | stored |
| GraphQL translations input type | the field is not in the type |
| Schema | reported as read-only |

`ReadOnly = true` on a collection field with a setter, on create, with no interceptor. "Initial" is
the value the class gives the property.

| Property | Stored at the commit before the rule | Stored with the rule |
|---|---|---|
| `string?`, `int?`, `Guid?`, a list, each with an initializer | `null` | initial |
| `string` with an initializer, on a `NOT NULL` column | the insert failed | initial |
| `int`, `bool` with an initializer | initial | initial |

A key sent for a `ReadOnly` field reached the property in no spelling, on create or on update.

`FieldDeclarationCheckTests`, `FieldInheritanceCheckTests`, `FieldSetterCheckTests` and
`TranslationReadOnlyCheckTests` pin the refusals and the accepted shapes;
`ItemServiceComputedFieldTests` creates an item with a `ReadOnly` computed field, and
`ReadOnlyCreateValueTests` pins the value a create stores.

## Unknowns

- Measured on SQLite only, below the HTTP layer.
- A stored get-only `ReadOnly` property reads back its initializer. It was measured and is not
  refused.
- A row property with a non-public setter, `[JsonInclude]` and a matching constructor parameter is
  accepted through the constructor; that combination was not measured.
- `[JsonIgnore]` or `[JsonPropertyName]` on a writable field was not measured.
- `[CmsRelation]` lost through an override or a `new` member is refused by
  `RelationInheritanceCheck`: `docs/ai/decisions/relation-key-and-primary-key-declarations.md`.

## Referenced from

- `src/Struo.Infrastructure/Metadata/FieldDeclarationCheck.cs`
- `src/Struo.Infrastructure/Metadata/FieldInheritanceCheck.cs`
- `src/Struo.Infrastructure/Metadata/FieldSetterCheck.cs`
- `src/Struo.Infrastructure/Metadata/TranslationReadOnlyCheck.cs`
- `docs/ai/conventions.md`
