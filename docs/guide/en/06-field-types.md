# 6. Field Types and Editors

When you're choosing an interface for a field and don't know what column it becomes or what the
admin form will look like, this chapter has the answer. A `[CmsField]`'s `Interface` value decides
three things at once: the database column, the field information REST and GraphQL expose, and
which admin editor renders it.

## Interface overview

Interfaces fall into seven groups by purpose, one table each. A relation is not a field
interface — it's declared on `[CmsRelation]`; see [Chapter 8: Relations](08-relations.md). An
interface must be written explicitly: leaving it unset falls back to `Text` (see
[Chapter 5: Defining Collections](05-collections.md)).

Which interfaces can't be made translatable is covered in
[Chapter 7: Multilingual Content](07-i18n.md).

The tables below use a few shorthand terms under Database column:

- `varchar(255)`: a `string` with no length, which `make:migration` writes as `AsString(255)`; on
  SQL Server it is `nvarchar(255)`.
- Long text: the result of applying `ColumnShape.LongText`; each backend maps it to its own type.
- JSON: a long text column holding JSON — SqlSugar serializes and restores the whole CLR value
  directly.
- CLR default: numbers, `bool`, `Guid` keep FluentMigrator's and SqlSugar's ordinary mapping;
  nothing widens the length. For `DateTime` on each backend, see "Date and time".
- `ColumnShape` has three members, `LongText`, `TimestampWithTimeZone` and `Timestamp` — there's no
  numeric shape.

Terms used in this chapter:

- Multi-value interfaces: the six interfaces `MultiSelect`, `CheckboxGroup`, `Tags`, `KeyValue`,
  `Files` and `Repeater`, whose whole value is stored as one JSON column.
- Single-value types: `string`, `bool`, `int`, `long`, `decimal`, `double`, `float`, `Guid`,
  `DateTime`, `byte[]`, and the `Nullable<T>` of each value type among them. Which of these an
  interface accepts is in the tables of the groups below.
- Structured types: a type that is not a primitive, an enum, `string`, `decimal`, `Guid`, a date or
  time type or `byte[]`, for example `List<>`, `HashSet<>`, `Dictionary<,>`, an array, a custom
  class or a struct.
- Single-value interfaces: every interface except the multi-value interfaces, `Hidden` and `Json`.

A property's type must fit its interface, and it must be the exact type: `MultiSelect` takes
`List<string>`, not `string[]` or `IList<string>`. A declaration that does not fit stops the host
at startup; see [Declarations rejected at startup](#declarations-rejected-at-startup).

On MySQL/MariaDB, text columns use the database's default character set, so create the database as
utf8mb4; see [Chapter 21, "Backend differences"](21-schema-and-upgrades.md#backend-differences).

### Text

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Text` | `string`/`Guid`/`Guid?` | `varchar(255)`; `Guid`: CLR default | `TextField` |
| `Textarea` | `string` | Long text | `TextareaField` |
| `RichText` | `string` | Long text | `RichTextField` |
| `Markdown` | `string` | Long text | `TextareaField` |
| `Code` | `string` | Long text | `TextareaField` |
| `Slug` | `string` | `varchar(255)` | `TextField` |
| `Email` | `string` | `varchar(255)` | `TextField` |
| `Url` | `string` | `varchar(255)` | `TextField` |
| `Password` | `string` | `varchar(255)` | `TextField` |
| `Color` | `string` | `varchar(255)` | `TextField` |
| `Phone` | `string` | `varchar(255)` | `TextField` |

The long-text interfaces are `Textarea`, `RichText`, `Markdown`, `Code`, and `Json` (covered
later); every other `string` interface stops at `varchar(255)`. How the column is decided is in
"How a property decides its column". Beyond `Required` and `MaxLength`, only `Email` and `Url` check
their format; the other `string` interfaces, `Phone` included, accept any text.

`Markdown` and `Code` are both plain text boxes, with no preview or syntax highlighting.
`Password`'s input isn't masked, and it never appears in the GraphQL schema. `Color` is likewise a
plain text input, with no color-swatch picker.

`Email` and `Url` values are checked on every write, and a value that fails is a `400`
`BAD_USER_INPUT` naming the field. An email needs exactly one `@`, text before it, a `.` inside the
domain that is neither its first nor its last character, and no whitespace anywhere:
`Field '{name}' must be a valid email address.` A URL must be an absolute `http` or `https` address
with a host and no whitespace: `Field '{name}' must be a valid http or https URL.` `null` and the
empty string skip the check. So does whitespace-only text in a `Required` field, which `Required`
reports as missing; in any other field whitespace-only text is checked and fails. A junction payload
row does not run the `Required` check, so an empty or whitespace-only value of a `Required` field is
not rejected there; a `null` sent to a column that is NOT NULL is rejected (see
[Chapter 8](08-relations.md)). A stored value is never re-checked on read; it is checked only when a
write sends that field. A field that must hold relative paths belongs on `Text`, not `Url`. The same
rules apply to translation values and to `Email` and `Url` child fields of a `Repeater`.

When one value breaks several rules, which error comes first depends on the path. A root field
reports `MaxLength`, then the format, and `Required` after both. A translation value reports
`Required` for every locale, then the format, then `MaxLength`. A `Repeater` child field reports
`Required`, then `MaxLength`, then the format.

### Numbers and booleans

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Number` | `int`/`long`/`decimal`/`double`/`float` | CLR default | `NumberField` |
| `Slider` | Same as `Number` | CLR default | `NumberField` |
| `Rating` | Same as `Number` | CLR default | `NumberField` |
| `Boolean` | `bool`/`bool?` | CLR default | `BooleanField` |
| `Checkbox` | `bool`/`bool?` | CLR default | `BooleanField` |

The nullable forms of the numeric types (`int?`, `decimal?` and so on) are accepted too. `short`,
`byte` and the unsigned integers are not; use `int` or `long`.

`Slider` and `Rating` reuse `Number`'s editor — there's no slider or star-rating component in the
admin SPA. `Checkbox` reuses `Boolean`'s editor.

### Date and time

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Date` | `DateTime`/`DateTime?` | CLR default | `DateField` |
| `Time` | `DateTime`/`DateTime?` | CLR default | `DateField` |
| `DateTime` | `DateTime`/`DateTime?` | CLR default | `DateField` |

All three share the same editor; the difference is only whether it shows the date, the time, or
both. A field that needs a time zone adds `[ColumnShape(ColumnShape.TimestampWithTimeZone)]` to
the property as well.

The column type of a `DateTime` property on each backend:

| Backend | `DateTime` | With `TimestampWithTimeZone` |
|---|---|---|
| PostgreSQL | `timestamp` | `timestamptz` |
| SQL Server | `datetime` | `datetimeoffset` |
| MySQL/MariaDB | `datetime(6)` | `datetime(6)` |
| SQLite | `datetime` | `timestamptz` |
| Oracle | FluentMigrator's default, unverified | `timestamp with time zone` |

On MySQL/MariaDB, a `DateTime` with no shape is `ColumnShape.Timestamp`, which keeps microseconds,
so two writes within one second stay ordered; plain `datetime` there keeps whole seconds. On the
other backends, declaring `Timestamp` explicitly does not change the type. SQLite does not check
type names, so it takes `timestamptz` literally.

A `TimestampWithTimeZone` column stores a UTC instant, whatever the PostgreSQL server's `TimeZone`
and the API host's zone. On PostgreSQL and SQL Server the value reads back as `DateTimeKind.Utc`;
on write, a `Local` value is converted to UTC and an `Unspecified` one is taken as UTC. MySQL,
MariaDB and SQLite store the clock digits as given, so convert to UTC before writing.

A comparison or `_in`/`_nin` filter on such a column compares instants too: a value with `Z` or an
offset at the instant it names, one without a zone as UTC. `_contains`, `_starts_with` and
`_ends_with` are a 400 on such a column; to match a date, use a range with `_gte` and `_lt`. Oracle
gets none of this and is unverified.

### Choice

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Select` | `string` | `varchar(255)` | `SelectField` |
| `MultiSelect` | `List<string>` | JSON | `MultiSelectField` |
| `Radio` | `string` | `varchar(255)` | `RadioField` |
| `CheckboxGroup` | `List<string>` | JSON | `CheckboxGroupField` |
| `Tags` | `List<TagItem>` | JSON | `TagsField` |

`MultiSelect` and `CheckboxGroup` must carry a `[CmsOptions]` that lists values; without the
attribute, or with one that lists none, the host fails at startup, because with no options every
value would be rejected. `Select` and `Radio` need `[CmsOptions]` to
have anything to pick from; `Tags` can pair with `[CmsOptions]` too, but it's optional and usually
left free-form. How to declare `[CmsOptions]` is in
[Chapter 5: Defining Collections](05-collections.md).

On write, `MultiSelect`, `CheckboxGroup`, `Select`, and `Radio` check each value against the option
list. A `Select` or `Radio` value must equal one option's value exactly, case included:
`Field '{name}' value '{value}' is not in its options.` The check applies to a top-level field, a
translation value, and a `Repeater` child field. A `Select` or `Radio` with no `[CmsOptions]`
accepts any string, and `null` and the empty string skip the check. For a fixed set of values,
declare a `string` and list the values in `[CmsOptions]`; an enum property is not accepted.

### `Json`, `KeyValue` and `Repeater`

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Json` | `string` (raw JSON text) | Long text | `JsonField` |
| `KeyValue` | `Dictionary<string,string>` | JSON | `KeyValueField` |
| `Repeater` | `List<TChild>` | JSON | `RepeaterField` |

`Json` is a `string` property holding raw JSON text; the API layer re-parses it on read and
re-serializes it on write. The real JSON columns come from the multi-value interfaces and from a
structured-type property on `Hidden`. SqlSugar (de)serializes the whole CLR value directly against
a JSON column, and these properties need no `IsJson`; a `KeyValue` key can't be blank.

A structured-type property can sit only on a multi-value interface or on `Hidden`; on any other
interface the host fails at startup. A `Json` interface property must be a `string`, and no
interface accepts a property whose type is `object`, contains `object` or holds untyped elements.
The rules are in [Declarations rejected at startup](#declarations-rejected-at-startup).

`Repeater`'s `TChild` must be a class, not `string`, and must declare at least one `[CmsField]`;
violating either throws a startup `MetadataException` naming the field. A writable child field
needs a public setter (`set` or `init`), or a parameter of the same name and type on the one public
constructor of `TChild`; with neither, startup fails. A child field with `ReadOnly = true` must be
one a request body cannot set: no public setter, no matching constructor parameter and no
`[JsonInclude]`. One a body can still set is refused at startup, because the value sent would be
stored all the same. Child fields can't be set
translatable, their property types are checked against the interface tables, and they can use only
these interfaces, a narrower set than the single-value interfaces: `Text`, `Textarea`, `Markdown`,
`Code`, `Slug`, `Email`, `Url`, `Color`, `Phone`, `Number`, `Slider`, `Rating`, `Boolean`,
`Checkbox`, `Date`, `Time`, `DateTime`, `Select`, `Radio`. The interfaces that are not allowed are:

- A nested `Repeater`
- `RichText`, `Json`, `KeyValue`
- `File`, `Image`, `Files`
- `Password`, `Hidden`, `Uuid`, `Divider`

### Media and files

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `File` | `Guid`/`Guid?` | CLR default | `FileField` |
| `Image` | `Guid`/`Guid?` | CLR default | `FileField` |
| `Files` | `List<Guid>` | JSON | `FilesField` |

`File` and `Image` share the same editor; `Image` adds an image preview. The ids `Files` stores
aren't checked for existence on write — an id pointing at an already-purged file is stored just
the same, and the admin picker can only fall back to showing the raw id.

### Display-only and identifiers

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Hidden` | Single-value or structured, no `object` inside | Follows the type; a structured type is JSON | `ReadonlyField` |
| `Divider` | Single-value, or any type with `IsIgnore = true` | Follows the type; none with `IsIgnore` | `DividerField` |
| `Uuid` | `Guid`/`Guid?` | CLR default | `ReadonlyField` |

The `Hidden` here is the interface itself, rendering a read-only display component — it's a
different thing from the next section's `[CmsField(Hidden = true)]`, which can sit on any
interface. `Divider` ignores the field's value; it's usually paired with
`[SugarColumn(IsIgnore = true)]` so it never becomes a real column at all. `Uuid` just displays a
GUID read-only.

`Hidden` accepts the single-value types `string`, the numeric types, `bool`, `DateTime`, `Guid`,
their nullable forms and `byte[]`. `Divider` accepts the same set without `byte[]`.

### List columns and GraphQL exclusions

A collection's list view shows at most six columns, picked only from fields that are non-system and
non-hidden, and whose interface has a list-column formatter; `DefaultDisplayField` sorts first when
it qualifies, and the rest follow the field order in the API response.

Interfaces with no formatter, and so never shown in a list column, are `Password`, `Markdown`,
`Code`, `RichText`, `Divider`, `File`, `Image`, `Files`, `Hidden`, and `Uuid`. A collection built
entirely from these interfaces has an empty list view. The GraphQL schema separately excludes three
interfaces entirely: `Hidden`, `Divider`, and `Password`.

## How a property decides its column

The declarations on a property decide its database column and the default input limit when no
`MaxLength` is written. Each row of the table is one way to declare a property.

| Declaration | Column built | Default `MaxLength` |
|---|---|---|
| A `string` with nothing special | `varchar(255)` | 255 |
| A `string` on a long-text interface | long text | none |
| A multi-value interface | long text holding JSON | not applicable |
| A structured-type property on `Hidden` | long text holding JSON | not applicable |
| `[ColumnShape(ColumnShape.LongText)]` | long text | none |
| `[SugarColumn(Length = n)]` | `varchar(n)` | `n` |
| `[SugarColumn(ColumnDataType = "t", Length = n)]` | type `t`, length `n` | `n` |
| `[SugarColumn(ColumnDataType = "t")]` | type `t`, as written | none |
| A structured-type property with `[SugarColumn(IsJson = true)]` | long text holding JSON | not applicable |

The multi-value interfaces and structured types in the table are defined in
[Interface overview](#interface-overview). Other terms:

- Long-text interfaces: `Textarea`, `RichText`, `Markdown`, `Code`, `Json`.
- `varchar(255)` and `varchar(n)` are `nvarchar` on SQL Server.
- None: there is no limit. Not applicable: the property is not a `string`, so it cannot carry
  `MaxLength`; the same declaration on a `string` property means none.
- A `Length` on a long-text interface does not count; it needs a `ColumnDataType` beside it, as in
  the `[SugarColumn(ColumnDataType = "t", Length = n)]` row.

With an explicit `ColumnDataType`, the whole column definition is yours, including its length. A
length written inside the type string, such as `varchar(500)`, is not read as the field's width;
set `Length` for that.

### Declarations rejected at startup

These declarations contradict each other, or the property's type does not fit its interface or
column shape. The host fails at startup with a `MetadataException` that names the class, the
property and the declaration at fault. It is the same in every environment; nothing waits for the
table to be used.

| Conflicting declarations | What to change |
|---|---|
| A multi-value interface with `[ColumnShape]` | Remove `[ColumnShape]` |
| A multi-value interface with `ColumnDataType` | Remove `ColumnDataType` |
| `IsJson = true` with `ColumnDataType` | Remove `ColumnDataType` |
| `IsJson = true` with `[ColumnShape]` | Remove `[ColumnShape]` |
| A structured-type property with `ColumnDataType` | Remove `ColumnDataType` |
| A structured-type property with `[ColumnShape]` | Remove `[ColumnShape]` |
| A structured-type property on a single-value interface | Use a multi-value interface or `Hidden` |
| A `Json` interface property that is not a `string` | Declare the property as `string` |
| `object`, or a type with `object` or untyped elements | Declare the concrete type |
| `[ColumnShape]` with `ColumnDataType` | Keep only one of them |
| `Nullable<T>` or `string?` with `IsNullable = false` | Make it non-nullable, or remove `IsNullable = false` |
| A property type the interface does not accept | Declare a type it accepts, or change the interface |
| A property type no interface accepts | See "Types no interface accepts" |
| A `ColumnDataType` that contradicts the property type | See "Column types that contradict the property" |
| `IsJson = true` on a single-value property | Remove `IsJson` |
| `[ColumnShape(LongText)]` on a non-`string` property | Remove it, or declare `string` |
| `Timestamp` or `TimestampWithTimeZone` on a non-`DateTime` | Remove it, or declare `DateTime` |
| `MultiSelect` or `CheckboxGroup` without `[CmsOptions]` | Add `[CmsOptions]` with the allowed values |

The framework chooses the type of a JSON column; `[ColumnShape]` and `ColumnDataType` cannot set
it. The same goes for a structured-type property, which is stored as a JSON column.

A structured-type property on a single-value interface is rejected whether or not it carries
`IsJson`; declare a single-value type, or use a multi-value interface or `Hidden`. The `Json`
interface stores raw JSON text, so its property is declared as `string`. Conversely, a property of a
single-value type cannot carry `IsJson`, on any interface; a structured-type property on `Hidden` or
a multi-value interface needs none.

Each `[ColumnShape]` member has the types it fits: `LongText` only `string`, and `Timestamp` and
`TimestampWithTimeZone` only `DateTime` and `DateTime?`.

The tables under "Interface overview" list the property types each interface accepts. The type must
be the exact one, so `List<string>` on `Tags` is rejected and `Tags` is declared as
`List<TagItem>`; `string[]` or `IList<string>` does not stand in for `List<string>`. The message
gives the property's type, what the interface accepts and which interfaces accept the type.

### Column types that contradict the property

A `ColumnDataType` from the table below stops the host as well. `Nullable<T>` counts as `T`. A
name that is not in the table, `timestamp with local time zone` included, is yours, and passes.

| Property type | `ColumnDataType` | Declare instead |
|---|---|---|
| `DateTime` | `timestamptz`, `timestamp with time zone`, `datetimeoffset` | The shape; remove `ColumnDataType` |
| `int` | `bigint` | `long`, or remove `ColumnDataType` |
| `int` | `smallint` | Remove `ColumnDataType`, or use `int` |
| `long` | `int`, `integer` | `int`, or remove `ColumnDataType` |
| `long` | `smallint` | Remove `ColumnDataType`, or use `bigint` |
| `int`, `long` | `tinyint(1)` | `tinyint` with no length, or remove `ColumnDataType` |
| `decimal` | `real`, `float`, `float4`, `float8` | Use `decimal(18,4)`, or remove `ColumnDataType` |
| `decimal` | `double`, `double precision` | Use `decimal(18,4)`, or remove `ColumnDataType` |
| `decimal` | `decimal`, `numeric`, `dec`, `fixed` with no parentheses | Write precision and scale, or remove `ColumnDataType` |
| `bool` | `varchar`, `nvarchar`, `char`, `nchar` | Remove `ColumnDataType`, or use `boolean` or `bit` |
| `bool` | `character`, `character varying`, `bpchar` | Remove `ColumnDataType`, or use `boolean` or `bit` |
| `bool` | `text`, `ntext`, `tinytext`, `mediumtext`, `longtext` | Remove `ColumnDataType`, or use `boolean` or `bit` |

The type name is compared by its words, without letter case:

- A length or precision in parentheses is ignored, with two exceptions: `tinyint` is in the table
  only with `(1)`, and `decimal`, `numeric`, `dec` and `fixed` only with no parentheses.
- A word wrapped whole in brackets, double quotes or backticks counts as one word, so `[bigint]`
  is `bigint` and `"int custom"` is not `int`.
- `double precision` counts as `double` and `character varying` as `character`.
- A word after the name counts as the table below says.

| Word after the name | After which names | Counts as |
|---|---|---|
| `unsigned`, `zerofill` | `real`, `float`, `double` | A column modifier: `float unsigned` is `float` |
| `unsigned`, `zerofill` | `decimal`, `numeric`, `dec`, `fixed` | A column modifier |
| `unsigned`, `zerofill` | Any other name | Another type: `int unsigned` is not `int` |
| `array`, or brackets as in `int[]` | Any name | Another type |
| Any other word, such as `not null` | Any name | A column modifier: `bigint not null` is `bigint` |

For a `DateTime` the shape is `[ColumnShape(ColumnShape.TimestampWithTimeZone)]` (see
[Date and time](#date-and-time)). On PostgreSQL and SQL Server the shape reads and writes the
column as a UTC instant. Without it the database takes the clock digits of the value: in the
PostgreSQL server's time zone, and as UTC on SQL Server. Measured on PostgreSQL 17 and SQL Server
2022, with hosts at UTC+8 and at UTC:

- On PostgreSQL with a server time zone that is not UTC (measured with `Asia/Taipei`), a value with
  `Kind = Utc` is stored at the wrong instant: 05:06 UTC is stored as 21:06 UTC of the day before.
  A date-time in a JSON request body is read as `Kind = Utc`, so this happens with the API alone.
  A value with `Kind = Unspecified` is stored the same way. With a UTC server time zone, and on SQL
  Server, both are stored correctly.
- A value with `Kind = Local`, written by server-side code such as a hook, an interceptor or a
  seed, is stored off by the host's UTC offset on SQL Server and on PostgreSQL with a UTC server
  time zone. On a host at UTC+8, 13:06 local time (05:06 UTC) is stored as 13:06 UTC.
- PostgreSQL returns the column as `Kind = Local` values on every read, whoever wrote the row; the
  shaped column returns `Kind = Utc`. Code that compares a value it read with `DateTime.UtcNow`
  gets the wrong answer.
- On SQL Server, a facet over the column and a facet over a zone-less `DateTime` column in the
  same process make each other fail with a bind error, in either order.

Only the second item needs server-side code to write the value; the others happen with values
written through the API.

The integer rows list the integer type names SQL Server has and reads as integer types that share
one entry of the ORM's reader cache: `bigint`, `int` with its synonym `integer`, and `smallint`.
An integer property on a column of another of these widths has a facet that fails on SQL Server
when a facet of a column of the property's own width ran first in the same process. Reads and
filters work. The check is the same on every backend, although only SQL Server fails. `tinyint` is
read as another type, and `mediumint`, `int2`, `int4` and `int8` do not exist on SQL Server, so
they pass.

The MySQL driver reads a `tinyint(1)` column as a boolean, on MySQL and on MariaDB: an `int` or
`long` property stored as 7 reads back as 1, in a read and in a facet, although a filter for 7
still matches the row. Write `tinyint` with no length.

A `decimal` on a binary floating-point column has its value rounded by the column. Measured on
PostgreSQL 17, SQL Server 2022, MySQL 8.4 and MariaDB 11.8: a single-precision type (`real` on
PostgreSQL and SQL Server, `float` on MySQL and MariaDB) reads 12345.6789 as 12345.68 or 12345.7,
and a double-precision type reads 12345678901234.5678 as 12345678901234.6. Filter counts, facet
buckets and sums are wrong with them. With no `ColumnDataType`, the column of a `decimal` property
is `decimal(18,4)` on SQL Server, MySQL and MariaDB, and `numeric` with no limit on precision on
PostgreSQL.

On SQLite a `decimal` column, however it is declared, the default and `decimal(18,4)` included,
returns a value of more than 15 significant digits rounded to 15: 12345678901234.5678 reads back
as 12345678901234.6, and filters and sums over such values are wrong with it.

`decimal`, `numeric`, `dec` and `fixed` written with no precision and scale create `decimal(10,0)`
on MySQL and MariaDB, and on SQL Server `numeric` and `dec` create a type of scale 0. 12345.6789
reads back as 12346 and the fraction is dropped. PostgreSQL keeps the fraction for `decimal`,
`numeric` and `dec`, and so does SQL Server for `decimal`, but the check is the same on every
backend. To write the type, give the precision and scale, such as `decimal(18,4)`: a name with
parentheses creates the column as written, and passes. `unsigned` and `zerofill` after the name
change nothing.

A `bool` on a string column is stored as `0` and `1` (`false` and `true` on PostgreSQL). Where the
database accepts the write, reading the row and a facet over the column fail, on SQLite,
PostgreSQL, SQL Server, MySQL and MariaDB alike; on PostgreSQL a filter fails too. To write the
type, use `boolean` on PostgreSQL, MySQL and MariaDB and `bit` on SQL Server; with no
`ColumnDataType` the framework picks the boolean type of the backend.

### Types no interface accepts

No interface accepts the types below, nor their `Nullable<T>`; a `[CmsField]` property of one of
them is rejected.

| Type | Use instead |
|---|---|
| `DateOnly`, `DateTimeOffset` | `DateTime` with `Date`, `Time` or `DateTime` |
| `TimeOnly`, `TimeSpan` | `DateTime` (`Time`), or a `string` holding text (`Text`) |
| `char` | `string` |
| An enum | `string` with `Select` or `Radio`, values in `[CmsOptions]` |
| `short`, `byte`, unsigned integers | `int` or `long` (`Number`) |

An `object` is read back as a JSON node, not as the value written, so no interface accepts it, the
multi-value interfaces and `Hidden` included. What is rejected is more than a property of type
`object`: an `object` as a type argument (`Dictionary<string, object>`, `List<object>`), as an
array element type, or as the type of a public property of a custom class or struct is rejected
too, with or without `[CmsField]`, including the properties of a `Repeater` child type, at any
depth. A non-generic collection (`ArrayList`, `Hashtable`, `IList`, or any type that implements
`IEnumerable` without `IEnumerable<T>`) holds untyped elements and is rejected the same way;
`string` and `byte[]` are not. The message says where the `object` or the untyped collection is.

A `Nullable<T>` (`Guid?`, `int?`, `DateTime?`) and a `string?` always get a nullable column. For
NOT NULL, declare `Guid` or `string`, not `Guid?` or `string?`. Other nullable types, such as
`List<string>?` and `byte[]?`, are not part of this check.

The check covers `[CmsField]` properties of collections, translation entities and junction payloads.
`[ColumnShape]` on a long-text interface works as usual. A `Repeater` sub-field lives inside its
parent's JSON value and has no column of its own, so the column declaration checks (`[ColumnShape]`,
`ColumnDataType`) skip it; the check that the property type fits the interface and the `object` and
untyped-collection checks still run on it. A property with `[SugarColumn(IsIgnore = true)]` is
exempt from these declaration checks, and when a `Repeater` property carries it, its sub-field types
are not checked either; the GraphQL rule that a `Repeater` needs a writable sub-field applies
whatever the flag says, see [Chapter 5](05-collections.md).

### Storing long text

Use a long-text interface, or write `[ColumnShape(ColumnShape.LongText)]`. Do not write
`[SugarColumn(ColumnDataType = "text")]`: `ColumnShape` is correct on every backend.

### Widening a column of an existing table

When a declaration widens a column that exists already and is narrower, the table needs its own
`Alter.Column` migration. `migrate:check` reports the mismatch as an error. Lowering `MaxLength`,
or adding `[SugarColumn(Length = n)]` to a column that is already wide enough, needs no migration.

## `MaxLength` behavior

`MaxLength` counts UTF-16 code units. The admin input and write validation both use it, and a longer
value gets a 400. The admin binds the effective value to the native input's `maxlength` attribute.

When it is unset (or 0), the default comes from the table in the previous section. The default does
not depend on the interface: a `string` on `Hidden` is 255, like one on `Text`. For a field marked
none that needs a limit, set `MaxLength` on its `[CmsField]`.

Startup checks:

- A negative value, or one on a non-`string` property: `MetadataException`.
- An explicit value above the column width: `MetadataException` naming the class, the property, the
  `MaxLength` and the column width. A value equal to or below the width is fine.
- Not checked: a property with no column width (the none and not-applicable rows), a `Repeater`
  sub-field, and a property with `[SugarColumn(IsIgnore = true)]`.
- Scope: the fields of collections, translation entities and junction payloads.

To store a value longer than the default width, see "Storing long text" and "Widening a column of
an existing table".

## Required, read-only, hidden, and system fields

`Required` always demands the field on create; on update it validates the merged entity, not the
request body itself — details are in [Chapter 5: Defining Collections](05-collections.md).

For a field whose value is an array or an object, `Required` means "a value was supplied": an
empty `[]` or `{}` passes, on the frontend and the backend alike. Requiring at least one entry is a
check the fork writes in a write interceptor.

`RichText` content that sanitizes down to visually blank is converted to `null` before the
`Required` check runs, so a document that looks like it has content in the editor can still fail
the required check; `<img>` and `<hr>` both count as content.

On create, `Required` is reported first. On update, when the body also violates a length or
structural check, those errors are reported first instead.

A `ReadOnly` field of a collection class can be read but not written: a key sent for it is dropped
on create, on update and on a revert, also when the field is the foreign key of a many-to-one
relation, and the GraphQL create and update inputs do not list the key. On create the field stores
the value the class gives the property —
`public string Source { get; set; } = "import";` stores `import`, and a property with no
initializer stores the default of its type — unless a write interceptor sets it. The admin input
is disabled as well.

The scanner automatically turns the four audit fields into read-only system fields, picking their
interface by CLR type — `DateTime` for the timestamps, `Text` for the users. The framework stamps
all four on create; on update it only re-stamps `UpdatedAt`/`UpdatedBy`, and whatever the client
sends for any of the four is ignored. System fields still come back on read; they just don't appear
in the admin form or list columns.

`Hidden` (`[CmsField(Hidden = true)]`) has nothing to do with the interface — any interface can
carry it; the sample's `Article.InternalNote` is a `Hidden` `Text` field.

It makes the field disappear from `GET /api/schema`, the GraphQL schema, item responses, the
query DSL's known-field allowlist and searchable whitelist, and any externally returned revision
snapshot, and it also drops the field from the collection's `translation.fields` list — the raw
snapshot row still keeps the value.

A hidden field that is also the foreign key of a many-to-one relation is not offered by name
either: the GraphQL create, update and filter inputs do not list it, and `filter`, `sort`,
`fields`, `facets` and `aggregate` answer `Unknown field` for it. The relation itself is
unaffected (`deep`, a filter or a sort through `relation.field`, a facet on the relation), and
`GET /api/schema` still names the key in the relation's `foreignKey`.

It isn't an RBAC boundary: the update field-overlay logic never checks `Hidden` at all, so a
caller that already knows the field name can still write it; pairing it with `ReadOnly` is what
actually blocks the write.

### Cleanup on write

- An unrecognized key in the write body is silently dropped, no error — a misspelled field name
  still gets a successful response, only the value never gets stored.
- `MultiSelect`/`CheckboxGroup` dedupe and keep the first occurrence; `Tags` rejects blank values
  and drops duplicates.
- `Files` drops `Guid.Empty` and duplicates; `Repeater` drops any row that is entirely blank, but
  error messages still number rows starting at 1 in the order they were sent, including the dropped
  ones.

## What `RichText` stores

Every `RichText` value written through the API — from the admin SPA's editor, a direct REST or
GraphQL call, or a script of your own posting raw HTML — goes through one server-side sanitizer
(`GanssHtmlSanitizer`). Tags, attributes, and URL schemes each have their own allowlist; anything
outside it is dropped, and dropping a tag drops its whole subtree with it. What that means on the
editor side is in [Chapter 19: Admin Customization](19-admin-customization.md).

The tag allowlist is basic formatting plus `h2`-and-below headings, links, images, simple tables,
`sub`/`sup`, and `span` — no `h1`, no `div`. An absolute URL must be `http`, `https`, or
`mailto`; relative paths (the editor's uploaded images) stay, and a rejected URL costs the
attribute, not the element.

### Links: `target` and `rel`

`rel` is never stored as written — the sanitizer always derives it from `target`:

| What you send | What gets stored |
|---|---|
| `<a href="…" target="_blank">` | `<a href="…" target="_blank" rel="noopener">` |
| Any other `target` value | `<a href="…">` |

Only an exact, case-sensitive `_blank` counts; `_Blank`, `_self`, or a link whose `href` itself got
dropped lose both attributes together. `target` is on the global attribute allowlist too, but only
`<a>` does anything with it — every other element loses `target` unconditionally, even when the
tag itself is allowlisted.

### Images: `width` and `height`

`width` survives only on `<img>`, and only when the whole value matches
`\A[1-9][0-9]{0,4}\z` — a one-to-five-digit pixel count with no leading zero, no unit, no
percentage, no whitespace; every other element loses it, allowlisted attribute or not.

`height` isn't allowlisted at all, so it's never stored regardless of source: both dimensions
together, in a renderer that only caps `max-width: 100%`, would squash the image. Render
`RichText` images with `max-width: 100%` and nothing constraining height, and the stored width
scales them with their aspect ratio intact.

Both rules are policy that `GanssHtmlSanitizer` enforces in one place, not a hard limit —
allowing `noreferrer`, a percentage width, or a stored height means changing that class, not the
editor's link dialog or any other allowlist. The same sanitizer also normalizes a table's
`<thead>` on the way out; that's covered in [Chapter 19](19-admin-customization.md).

## Adding a custom field editor

The rest of this section swaps `Color`'s default `TextField` for a native color input. Every
field-editor component accepts the same three props and emits the same event: `field` (the resolved
field metadata), `modelValue` (the form's current value), and an optional `disabled`; it emits
`update:modelValue` back on change:

```ts
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
```

**1. Write the component** — `frontend/src/components/fields/ColorSwatchField.vue`: <!-- citation-guard:allow: the file the reader creates in this walkthrough -->

```vue
<script setup lang="ts">
import { Input } from '@/components/ui/input'
import type { FieldMeta } from '../../types/schema'

defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

// The native color input uses the #rrggbb form; fall back to black while the field is still empty.
function onPick(e: Event): void {
  emit('update:modelValue', (e.target as HTMLInputElement).value)
}
</script>

<template>
  <div class="flex items-center gap-2">
    <input
      type="color"
      :value="(modelValue as string) || '#000000'"
      :disabled="disabled"
      class="border-input h-9 w-12 shrink-0 cursor-pointer rounded-md border p-0.5"
      @input="onPick"
    />
    <Input
      :model-value="(modelValue as string)"
      :disabled="disabled"
      :maxlength="field.maxLength ?? undefined"
      @update:model-value="(v) => emit('update:modelValue', v)"
    />
  </div>
</template>
```

**2. Register it** — swap the `color` entry's component in
`frontend/src/lib/fieldTypes/registry.ts`; everything else (default value, parsing, serialization,
list-column formatter) still holds for a plain string field:

```ts
import ColorSwatchField from '../../components/fields/ColorSwatchField.vue'
// …
color: def({ component: ColorSwatchField, listColumn: asString }),
```

`FieldInput.vue` looks up the component from the registry by `field.interface`, so every `Color`
field across every collection switches over together — there's no per-collection wiring needed.

**3. See the result** — restart the admin SPA dev server, open any item with a `Color` field, and a
color swatch appears next to the input.

The dispatcher `FieldInput.vue` also forwards a fourth binding, `id`, passed down as a fallthrough
attribute — it isn't a prop any editor declares itself. An editor whose root element is a native
control picks up this `id`, matching the caller's `<label>`; an editor whose root element is a
wrapping `div` instead lands the `id` on that `div`, where it doesn't match the inner control, so it
has to handle labeling itself.

The `FieldInterface` union in `frontend/src/lib/fieldTypes/types.ts` is closed: adding a genuinely
new interface value — rather than swapping an existing interface's editor as above — also means
syncing the backend `FieldInterface` enum, the admin SPA's union type, and every place in the
registry, which is beyond plain editor customization.

A schema contract test catches drift in both directions: the backend gaining a new member the
admin SPA hasn't caught up to, or the admin SPA keeping a stale entry, both fail the test.

The schema snapshot that test compares against is regenerated by a single backend test run with
`UPDATE_SCHEMA_SNAPSHOT=1` set, and the flag must be cleared afterward, or later unrelated tests
silently rewrite the snapshot instead of checking it.

Non-string fields such as numbers, booleans, and `Uuid` send `null` when left blank (`number`,
`slider`, `rating`, `boolean`, `checkbox`, `uuid`, among others); text interfaces deliberately send
an empty string, so clearing a text field actually clears it.

## What's next

With an interface chosen, the next step is storing a separate value per language for a field — the
subject of [Chapter 7: Multilingual Content](07-i18n.md).
