# Relation declarations are checked the way the run time reads them

## Decision

The metadata scan refuses, with a `MetadataException`, a relation or key declaration that the item
service cannot serve:

- A collection has exactly one property that carries `[SugarColumn(IsPrimaryKey = true)]` on its own
  declaration.
- `[SugarColumn]` is read off the property's own declaration, without attribute inheritance, for the
  primary key mark and for `IsIgnore` alike (`KeyLookup`).
- Every key name in `[Navigate]`, and a many-to-many `SortField`, matches a property exactly: the
  many-to-one or one-to-one foreign key on the declaring entity, the one-to-many child key on the
  target collection's class, both junction keys and the sort field on the junction type. The exact
  rule holds whatever `OnDelete` the relation declares.
- A key property has a column, the two junction keys are two properties, and a relation is a public
  instance property that carries both `[CmsRelation]` and `[Navigate]`.
- An override, or a `new` member, of a base class navigation property that carries `[CmsRelation]`
  repeats it (`RelationInheritanceCheck`): the attribute is not inherited.
- `SortField` is declared on a many-to-many only; its property has a column and a setter, is neither
  junction key nor the junction's primary key, and has one of the types measured to keep the order.
- `OnDelete` other than the default is declared on a many-to-one only.
- A many-to-one foreign key that also carries `[CmsField]` is declared once in each GraphQL input
  type, in the form the field gives it; the relation adds the key only when the field did not
  (`ForeignKeyOwnFormSchemaTests`, `ForeignKeyOwnFormHostTests`). A `ReadOnly` or audit key is in
  the filter input and in neither write input.
- A foreign key whose field is `Hidden = true` is not offered to a reader by name
  (`CollectionMetadata.OffersForeignKey`): not in the GraphQL create, update and filter inputs, and
  not as a filter, sort, field, facet or aggregate name. The relation object, a write that names the
  key, and `Interface = Hidden` without the flag are unaffected
  (`ForeignKeyHiddenFlagHostTests`).
- A `File` or `Image` field is not the key of a relation (`FileFieldRelationKeyCheck`): the field
  already resolves to the file, and the two declarations collide in the GraphQL output type.
- A target or element type is resolved by collection name to the scanned collection class, for the
  key-type comparison and for the one-to-many child key.

## Why

Each rule follows a measurement of what the item service does with the declaration. The scan and the
ORM must agree on which property is the primary key and which has a column: reading the attribute
with inheritance reports a key on an override that carries none, and reports no column on an
override the ORM has given one. A relation is resolved by collection name when it is read and
written, so the scan resolves it the same way. Key names are case-sensitive in every position by
ruling: the purge that reads a many-to-one key by its spelling runs only under `SetNull`, a
one-to-many child key in another letter case works for every operation measured, and one rule for
all four positions does not depend on which consumer happens to tolerate another spelling.

## Evidence

Measured on SQLite through the item service (`TestItemService.Create` over `SqlSugarItemRepository`)
with test-defined types, each next to a correct declaration as the control, on the commit before the
check in question existed. The probe tests are not in the repository.

Primary key:

| Declaration | Scan | Item service |
|---|---|---|
| No marked property and no `Id` | passed | create, read and update threw `NullReferenceException` |
| `Id` present, not marked | passed | create worked; read, update and delete by id threw `Only one primary key` |
| Marked virtual `Id` on a base class, inherited | passed | everything worked |
| Marked virtual `Id` on a base class, overridden without the attribute | passed | create worked; read, update and delete by id threw `Only one primary key` |
| Unmarked abstract `Id`, overridden with the attribute | passed | everything worked |
| Two marked properties | passed | create worked; read, update and delete by id threw `Only one primary key` |

Key names (the junction is a plain class unless stated):

| Declaration | Scan | Item service |
|---|---|---|
| Junction parent key names no property | passed | writing a link threw `SqlSugarException`; purge threw `InvalidOperationException` |
| Junction target key names no property | passed | writing a link threw `InvalidOperationException`; a filter through the relation threw |
| Junction parent key in another letter case | passed | writing a link threw `NullReferenceException` |
| Junction target key in another letter case | passed | writing a link threw `InvalidOperationException` |
| Junction that is a collection, key in another letter case | passed | writing or replacing a link threw `InvalidOperationException` |
| Many-to-one key in another letter case, `SetNull` | passed | purge of the target threw `InvalidOperationException`; everything else worked |
| Many-to-one key in another letter case, `Restrict` or `Cascade` | passed | everything worked |
| One-to-many child key in another letter case | passed | everything worked |
| Junction parent key and target key are one property | passed | links stored the target's id in both columns; expansion empty; a replace appended rows; purge of the owner left rows |

Key columns:

| Declaration | Scan | Item service |
|---|---|---|
| Many-to-one key with `IsIgnore` | passed | expansion returned `null`; filter, sort and purge of the target threw `no such column` |
| One-to-many child key with `IsIgnore` | passed | expansion, filter and the children write threw `no such column` |
| Junction parent key with `IsIgnore` | passed | link write, expansion, filter, replace and purge of the owner threw `no such column` |
| Junction target key with `IsIgnore` | passed | expansion always empty; filter and purge of a target threw `no such column` |
| Key with `IsIgnore` on a base class, overridden without the attribute | refused by a read with inheritance | the ORM created the column; expansion, filter and link writes worked |
| Key overridden with `IsIgnore`, base without | refused | no column; filter and link writes threw `no such column` |

Relation property:

| Declaration | Scan | Item service |
|---|---|---|
| `[CmsRelation]` with no `[Navigate]` | passed | relation absent from the metadata; expansion, filter, sort and link writes answered `Unknown relation` |
| `[CmsRelation]` on a private, internal, protected or static property | passed | the same |
| `OnDelete = Cascade` or `SetNull` on a one-to-many or many-to-many | passed | identical to the `Restrict` control |

`SortField` (three links written, then replaced in another order):

| Declaration | Scan | Item service |
|---|---|---|
| On a many-to-one or one-to-many | passed | identical to the control |
| Names no junction property, one in another letter case, or one with `IsIgnore` | passed | the order after the replace did not change |
| Empty or whitespace | passed | identical to a relation with no `SortField`: the order after the replace did not change |
| Names the junction parent key, target key or primary key, `long` keys | passed | the position overwrote the column; links pointed at other rows and stayed after a purge of the owner |
| The same, `Guid` keys | passed | every link write threw `InvalidCastException` |
| `DateTime`, `Guid` or enum property | passed | the first link write threw `InvalidCastException` |
| `char` property | passed | the first link write threw `SqliteException` |
| `bool` property | passed | no error; the order was lost |
| `byte` property, 300 links; `sbyte` property, 200 links | passed | `OverflowException` |
| Property with no setter | passed | the first link write threw `ArgumentException` |
| `init` or private setter | passed | ordered correctly |
| Private setter declared on a base class of the junction | passed | the first link write threw `ArgumentException` |
| `protected` or `init` setter declared on a base class of the junction | passed | ordered correctly |
| `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `string`, and the nullable forms measured (`int?`, `long?`, `decimal?`) | passed | ordered correctly, also with 200 links |

A relation typed with a plain class that has the simple name of a collection, so that its collection
name is that collection's:

| Declaration | Item service |
|---|---|
| Key of the collection's key type, whatever `Id` the plain class declares | create, read, expansion, filter and purge worked; the expansion returned the rows of the collection |
| Many-to-one key of another type than the collection's primary key | expansion, sort and purge of the target threw; a create with the collection's id was refused |
| Many-to-many junction target key of another type | link writes were refused; expansion and purge threw on a seeded row; filter matched nothing |
| One-to-many, the collection's class declares the child key with another type | expansion and the children write threw; filter matched nothing |
| One-to-many, the collection's class declares no such key | expansion and filter threw |

An override or a `new` member of a base class navigation property that carries `[CmsRelation]` and
`[Navigate]`:

| The derived member repeats | Result |
|---|---|
| neither, or `[Navigate]` only | scan passed; relation absent from the schema; a write left the key empty; a read that named it threw `Unknown relation` |
| `[CmsRelation]` only | refused: no `[Navigate]` |
| both | the relation |

A `Hidden = true` foreign key `ownerId` next to a hidden field `secret` that is not a key, measured
on SQLite through the HTTP pipeline on 2026-11-27, at the commit before the rule:

| Surface | `secret` | `ownerId` |
|---|---|---|
| GraphQL create, update and filter inputs | absent | `ID`, `ID`, `IdFilter` |
| REST `filter`, `sort`, `fields`, `facets`, `aggregate` | `400` `Unknown field` | `200` |
| item read, revision read | not returned | not returned |
| `GET /api/schema` | not in `fields` | not in `fields`; the relation's `foreignKey` names it |
| REST write that names it | stored | stored |

An `Image` field `CoverId` with a relation `Cover` on it: the scan passed and the GraphQL schema
build threw `The following field 'cover' is declared multiple times`, so the host did not start.

## Unknowns

- Measured on SQLite only, below the HTTP layer.
- A relation property hidden by a `new` member of another type, or by a `static new` member, passes
  the scan and the base declaration is the relation served. A key declared on an interface was not
  measured.
- `short` and `ushort` sort properties overflow past 32,768 and 65,536 links; a relation write is
  bounded by `Query:MaxRelationLinks`.
- An override that does not repeat a base declaration's `[SugarColumn]` is refused before the key
  checks run, so the row above for a key with `IsIgnore` on a base class does not start:
  `docs/ai/decisions/an-override-repeats-sugarcolumn.md`.
- `[CmsRelation]` on a class that is not a collection is never scanned, and one on an interface
  member is not read.

## Referenced from

- `src/Struo.Infrastructure/Metadata/KeyLookup.cs`
- `src/Struo.Infrastructure/Metadata/NavigateShape.cs`
- `src/Struo.Infrastructure/Metadata/RelationInheritanceCheck.cs`
- `src/Struo.Infrastructure/Metadata/FileFieldRelationKeyCheck.cs`
- `tests/Struo.Tests/Metadata/NavigateForeignKeyTests.cs`
- `docs/ai/conventions.md`
