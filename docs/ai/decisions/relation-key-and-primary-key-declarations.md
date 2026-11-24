# Key names and primary keys are checked the way the run time reads them

## Decision

The metadata scan refuses, with a `MetadataException`, a key declaration that the item service
cannot use:

- A collection has exactly one property that carries `[SugarColumn(IsPrimaryKey = true)]` on its own
  declaration. The attribute is read without inheritance
  (`KeyLookup.PrimaryKeyProperties`, `GetCustomAttributes(typeof(SugarColumn), inherit: false)`).
- Both key names of a many-to-many match a property of the junction type exactly
  (`MetadataScanner.ValidateJunctionKeyProperties`).
- A many-to-one or one-to-one foreign-key name matches a property of the declaring entity exactly;
  a one-to-many child key matches a property of the list's element type without regard to case
  (`NavigateShape.RequireForeignKeyProperty`). The exact rule holds whatever `OnDelete` the relation
  declares.

## Why

Each rule follows a measurement of what the item service does with the declaration. The scan and the
ORM must agree on which property is the primary key: reading the attribute with inheritance reports
a key on an override that carries none, and the ORM then has no key for every call by id. A key name
is compared exactly where a consumer reads the property by the spelling in `[Navigate]`, and without
regard to case where every consumer found tolerates another spelling. The many-to-one rule does not
follow `OnDelete`: the purge that reads the key by its spelling runs under `SetNull`, and a relation
whose spelling is accepted under `Restrict` would start failing when its `OnDelete` is changed, with
nothing at the scan to say why.

## Evidence

Measured on SQLite through the item service (`TestItemService.Create` over `SqlSugarItemRepository`)
with test-defined types, each next to a correct declaration as the control. The primary-key and
key-name rows were measured on the commit before the checks existed. The probe tests are not in the
repository.

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
is that collection's), many-to-one and one-to-many, with a key of the collection's key type, was
measured with the key-type comparison skipping such a target: create, read, expansion, filter and
purge worked whether the plain class declared an `Id` of the same type, an `Id` of another type or
no `Id`, and the expansion returned the rows of the collection.

## Unknowns

- Measured on SQLite only, below the HTTP layer.
- A property hidden with `new` under another type, and a key declared on an interface, were not
  measured.
- For a relation typed with a plain class named like a collection, the key-type comparison reads
  the key that class declares, while the item service reads the collection's. The scan therefore
  refuses a key of the collection's type when the plain class declares an `Id` of another type,
  which the measurement above shows working, and accepts a key of the plain class's type, which was
  not measured. The same holds for the child key of a one-to-many whose element type is such a
  class.

## Referenced from

- `src/Struo.Infrastructure/Metadata/KeyLookup.cs`
- `src/Struo.Infrastructure/Metadata/NavigateShape.cs`
- `tests/Struo.Tests/Metadata/NavigateForeignKeyTests.cs`
- `docs/ai/conventions.md`
