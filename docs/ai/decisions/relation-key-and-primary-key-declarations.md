# Key names and primary keys are checked the way the run time reads them

## Decision

The metadata scan refuses a key declaration that the item service cannot use, with a
`MetadataException`, and accepts one that works:

- A collection has exactly one property that carries `[SugarColumn(IsPrimaryKey = true)]` on its own
  declaration. The attribute is read without inheritance
  (`KeyLookup.PrimaryKeyProperties`, `GetCustomAttributes(typeof(SugarColumn), inherit: false)`).
- Both key names of a many-to-many match a property of the junction type exactly
  (`MetadataScanner.ValidateJunctionKeyProperties`).
- A many-to-one or one-to-one foreign-key name matches a property of the declaring entity exactly;
  a one-to-many child key matches a property of the list's element type without regard to case
  (`NavigateShape.RequireForeignKeyProperty`).
- The key-type comparison (`MetadataScanner.ValidateRelationKeyTypes`) takes the primary key of the
  scanned collection the target's collection name resolves to, whatever class the relation property
  is typed with.

## Why

Each rule follows a measurement of what the item service does with the declaration. The scan and the
ORM must agree on which property is the primary key: reading the attribute with inheritance reports
a key on an override that carries none, and the ORM then has no key for every call by id. A key name
is compared exactly where a consumer reads the property by the spelling in `[Navigate]`, and without
regard to case where every consumer found tolerates another spelling. A relation is resolved by
collection name when it is read and written, so the type comparison resolves it the same way.

## Evidence

Measured on SQLite through the item service (`TestItemService.Create` over `SqlSugarItemRepository`)
with test-defined types, each next to a correct declaration as the control, on the commit before the
checks existed. The probe tests are not in the repository.

Primary key:

| Declaration | Scan | Item service |
|---|---|---|
| No marked property and no `Id` | passed | create, read and update threw `NullReferenceException` |
| `Id` present, not marked | passed | create worked; read, update and delete by id threw `Only one primary key` |
| Marked virtual `Id` on a base class, inherited | passed | everything worked |
| Marked virtual `Id` on a base class, overridden without the attribute | passed | create worked; read, update and delete by id threw `Only one primary key` |
| Unmarked abstract `Id`, overridden with the attribute | passed | everything worked |
| Two marked properties | passed | create worked; read, update and delete by id threw `Only one primary key` |

For the three base-class shapes, `GetCustomAttribute<SugarColumn>()` reported a key in all three and
`GetCustomAttributes(typeof(SugarColumn), false)` reported one in the two that worked.

Key names (the junction is a plain class unless stated):

| Declaration | Scan | Item service |
|---|---|---|
| Junction parent key names no property | passed | writing a link threw `SqlSugarException`; purge threw `InvalidOperationException` |
| Junction target key names no property | passed | writing a link threw `InvalidOperationException`; a filter through the relation threw |
| Junction parent key in another letter case | passed | writing a link threw `NullReferenceException` |
| Junction target key in another letter case | passed | writing a link threw `InvalidOperationException` |
| Junction that is a collection, key names no property | refused | by the writable-key check |
| Junction that is a collection, key in another letter case | passed | writing or replacing a link threw `InvalidOperationException`; expansion, filter and purge worked on a link seeded in the table |
| Many-to-one key in another letter case, `SetNull` | passed | create, read, update, expansion and filter worked; purge of the target threw `InvalidOperationException` |
| Many-to-one key in another letter case, `Restrict` or `Cascade` | passed | everything worked |
| One-to-many child key in another letter case | passed | create, read, update, expansion, filter, writing children and purge of the parent worked |
| Many-to-one or one-to-many key names no property | refused | by `NavigateShape` |

A relation typed with a plain class that has the simple name of a collection (so its collection name
is that collection's), many-to-one and one-to-many, with a key of the collection's key type: the
scan passed and create, read, expansion, filter and purge worked whether the plain class declared an
`Id` of the same type, an `Id` of another type or no `Id`. The expansion returned the rows of the
collection.

## Unknowns

- Measured on SQLite only, below the HTTP layer.
- A property hidden with `new` under another type, and a key declared on an interface, were not
  measured.
- A relation typed with a plain class named like a collection was not measured with a key of
  another type than the collection's.

## Referenced from

- `src/Struo.Infrastructure/Metadata/KeyLookup.cs`
- `src/Struo.Infrastructure/Metadata/NavigateShape.cs`
- `tests/Struo.Tests/Metadata/NavigateForeignKeyTests.cs`
- `docs/ai/conventions.md`
