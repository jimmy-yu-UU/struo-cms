using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Metadata;

// An explicit [SugarColumn(ColumnDataType = ...)] that disagrees with the property's CLR type fails the scan
// for the combinations that misbehave at run time; any other type name is the fork's own and passes.
public sealed class ColumnDataTypeFitTests
{
    // ---- the closed table, one case per combination ----

    public static TheoryData<Type, string> Rejected() => new()
    {
        { typeof(DateTime), "timestamptz" },
        { typeof(DateTime?), "timestamptz" },
        { typeof(DateTime), "timestamp with time zone" },
        { typeof(DateTime?), "timestamp with time zone" },
        { typeof(DateTime), "datetimeoffset" },
        { typeof(DateTime?), "datetimeoffset" },
        { typeof(DateTime), "TIMESTAMPTZ" },
        { typeof(DateTime), "TimestampTz" },
        { typeof(DateTime), "  timestamptz  " },
        { typeof(DateTime), "timestamptz(3)" },
        { typeof(DateTime), "timestamptz (3)" },
        { typeof(DateTime), "datetimeoffset(7)" },
        { typeof(DateTime?), "DATETIMEOFFSET(3)" },
        { typeof(DateTime), "timestamp(3) with time zone" },
        { typeof(DateTime), "TIMESTAMP  WITH  TIME  ZONE" },
        { typeof(DateTime), " timestamp\twith time   zone " },
        { typeof(int), "bigint" },
        { typeof(int?), "bigint" },
        { typeof(int), "BIGINT" },
        { typeof(int), " BigInt " },
        { typeof(int), "smallint" },
        { typeof(int?), "SmallInt" },
        { typeof(long), "int" },
        { typeof(long?), "int" },
        { typeof(long), "INT" },
        { typeof(long), "integer" },
        { typeof(long), " Integer " },
        { typeof(long?), "integer" },
        { typeof(long), "smallint" },
        { typeof(long?), "SMALLINT" },
        // a parenthesised group is a word break
        { typeof(DateTime), "timestamp(3)with time zone" },
        { typeof(DateTime), "timestamp (3) with time zone" },
        // brackets and quotes around the name
        { typeof(DateTime), "[datetimeoffset]" },
        { typeof(DateTime), "[datetimeoffset](7)" },
        { typeof(DateTime), "\"timestamptz\"" },
        { typeof(DateTime), "`timestamptz`" },
        { typeof(int), "[bigint]" },
        { typeof(long), "[int]" },
        // column modifiers after the type
        { typeof(int), "bigint not null" },
        { typeof(int), "bigint default 0" },
        { typeof(long), "int identity" },
        { typeof(long), "int identity(1,1)" },
        { typeof(long), "INT   IDENTITY ( 1 , 1 ) NOT NULL" },
        { typeof(DateTime), "datetimeoffset(7) null" },
        { typeof(DateTime?), "timestamptz not null default now()" },
        { typeof(DateTime), "timestamp with time zone not null" },
        // the whole quoted or bracketed word is judged, case-insensitively
        { typeof(long), "\"Int\"" },
        { typeof(int), "[BigInt]" },
        // an unclosed group runs to the end, and a second group does not replace the first
        { typeof(int), "tinyint(1" },
        { typeof(long), "tinyint(1)(2)" },
        { typeof(int), "bigint(1(2))" },
        // the MySQL driver reads tinyint(1) as a boolean
        { typeof(int), "tinyint(1)" },
        { typeof(int?), "TINYINT(1)" },
        { typeof(long), "tinyint (1)" },
        { typeof(long?), " tinyint( 1 ) not null " },
        // a decimal property on a binary floating-point column type
        { typeof(decimal), "real" },
        { typeof(decimal?), "real" },
        { typeof(decimal), "float" },
        { typeof(decimal?), "float" },
        { typeof(decimal), "float4" },
        { typeof(decimal), "float8" },
        { typeof(decimal?), "float8" },
        { typeof(decimal), "double" },
        { typeof(decimal?), "double" },
        { typeof(decimal), "double precision" },
        { typeof(decimal?), "double precision" },
        { typeof(decimal), "float(24)" },
        { typeof(decimal), "float(53)" },
        { typeof(decimal), "float(10)" },
        { typeof(decimal), "float(25)" },
        { typeof(decimal), "double(18,4)" },
        { typeof(decimal), "REAL" },
        { typeof(decimal), "Float8" },
        { typeof(decimal), "DOUBLE PRECISION" },
        { typeof(decimal), "Double  Precision" },
        { typeof(decimal), "  real  " },
        { typeof(decimal), "FLOAT (53)" },
        { typeof(decimal), "[float]" },
        { typeof(decimal), "[float](53)" },
        { typeof(decimal), "\"real\"" },
        { typeof(decimal), "`double`" },
        { typeof(decimal), "float not null" },
        { typeof(decimal?), "real default 0" },
        { typeof(decimal), "double precision not null" },
        { typeof(decimal), "float(53) null" },
        { typeof(decimal), "float unsigned" },
        { typeof(decimal?), "float unsigned" },
        { typeof(decimal), "double unsigned" },
        { typeof(decimal), "real unsigned" },
        { typeof(decimal), "float zerofill" },
        { typeof(decimal), "float unsigned zerofill" },
        { typeof(decimal), "double precision unsigned" },
        { typeof(decimal), "DOUBLE UNSIGNED" },
        // no precision and scale
        { typeof(decimal), "decimal" },
        { typeof(decimal?), "decimal" },
        { typeof(decimal), "numeric" },
        { typeof(decimal?), "numeric" },
        { typeof(decimal), "dec" },
        { typeof(decimal?), "dec" },
        { typeof(decimal), "fixed" },
        { typeof(decimal?), "fixed" },
        { typeof(decimal), "DECIMAL" },
        { typeof(decimal), "Numeric" },
        { typeof(decimal), "  decimal  " },
        { typeof(decimal), "decimal not null" },
        { typeof(decimal?), "numeric default 0" },
        { typeof(decimal), "[decimal]" },
        { typeof(decimal), "[numeric] not null" },
        { typeof(decimal), "\"numeric\"" },
        { typeof(decimal), "`dec`" },
        { typeof(decimal), "decimal unsigned" },
        { typeof(decimal), "numeric unsigned" },
        { typeof(decimal), "dec unsigned" },
        { typeof(decimal), "fixed unsigned" },
        { typeof(decimal), "decimal zerofill" },
        { typeof(decimal), "numeric zerofill" },
        // a bool property on a string column type
        { typeof(bool), "varchar(5)" },
        { typeof(bool?), "varchar(5)" },
        { typeof(bool), "nvarchar(5)" },
        { typeof(bool), "char(5)" },
        { typeof(bool?), "char(5)" },
        { typeof(bool), "text" },
        { typeof(bool?), "text" },
        { typeof(bool), "character varying(5)" },
        { typeof(bool?), "character varying(5)" },
        { typeof(bool), "varchar" },
        { typeof(bool), "nvarchar" },
        { typeof(bool), "nvarchar(max)" },
        { typeof(bool), "varchar(max)" },
        { typeof(bool), "nchar(5)" },
        { typeof(bool), "ntext" },
        { typeof(bool), "tinytext" },
        { typeof(bool), "mediumtext" },
        { typeof(bool), "longtext" },
        { typeof(bool), "character(5)" },
        { typeof(bool), "character" },
        { typeof(bool), "bpchar" },
        { typeof(bool), "character varying" },
        { typeof(bool), "VARCHAR(5)" },
        { typeof(bool), "Text" },
        { typeof(bool), "NVarChar(Max)" },
        { typeof(bool), "  text  " },
        { typeof(bool), "varchar (5)" },
        { typeof(bool), "CHARACTER   VARYING (5)" },
        { typeof(bool), "character varying(5)not null" },
        { typeof(bool), "[nvarchar](max)" },
        { typeof(bool), "[text]" },
        { typeof(bool), "\"varchar\"(5)" },
        { typeof(bool), "`text`" },
        { typeof(bool), "text not null" },
        { typeof(bool?), "text null" },
        { typeof(bool), "varchar(5) not null" },
        { typeof(bool), "varchar(5) default 'x'" },
        { typeof(bool), "character varying(5) not null" },
        { typeof(bool), "char" },
        { typeof(bool), "nchar" },
        { typeof(bool), "bpchar(5)" },
        { typeof(bool), "longtext collate utf8mb4_bin" },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void A_harmful_combination_is_found(Type propertyType, string columnType)
    {
        var misfit = ColumnTypeFit.Find(propertyType, columnType);

        misfit.Should().NotBeNull();
        misfit!.Value.Fix.Should().Contain(ExpectedFix(propertyType, columnType));
        misfit.Value.Why.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(Rejected))]
    public void The_nullable_twin_of_a_rejected_pair_is_found_too(Type propertyType, string columnType)
    {
        if (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) is not null) return;
        var twin = typeof(Nullable<>).MakeGenericType(propertyType);

        var misfit = ColumnTypeFit.Find(twin, columnType);

        misfit.Should().NotBeNull();
        misfit!.Value.Fix.Should().Contain(ExpectedFix(propertyType, columnType));
    }

    // The fix text identifies the rule that fired.
    private static string ExpectedFix(Type propertyType, string columnType)
    {
        var single = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (single == typeof(bool)) return "declare boolean (PostgreSQL, MySQL, MariaDB) or bit (SQL Server)";
        var name = columnType.ToLowerInvariant();
        if (single == typeof(decimal) && (name.Contains("float") || name.Contains("real") || name.Contains("double")))
            return "Declare decimal(18,4) or another decimal type with an explicit scale";
        if (single == typeof(decimal) && !name.Contains("float") && !name.Contains("real") && !name.Contains("double"))
            return "Write the precision and scale, such as decimal(18,4)";
        if (name.Contains("tinyint", StringComparison.Ordinal)) return "Write tinyint without its length";
        if (propertyType == typeof(DateTime) || propertyType == typeof(DateTime?)) return "TimestampWithTimeZone";
        var isInt = propertyType == typeof(int) || propertyType == typeof(int?);
        if (name.Contains("smallint", StringComparison.Ordinal))
            return isInt ? "declare the column as int" : "declare the column as bigint";
        return isInt ? "Declare the property as long" : "Declare the property as int";
    }

    public static TheoryData<Type, string?> Passing() => new()
    {
        // zone-less date-time types
        { typeof(DateTime), "timestamp" },
        { typeof(DateTime), "timestamp without time zone" },
        { typeof(DateTime), "timestamp(3)without time zone" },
        { typeof(DateTime), "timestamp(3) without time zone" },
        { typeof(DateTime), "datetime" },
        { typeof(DateTime), "datetime2" },
        { typeof(DateTime), "datetime2(3)" },
        { typeof(DateTime), "datetime(6)" },
        { typeof(DateTime?), "date" },
        // a zone-aware name on a property that is not DateTime is another rule's concern
        { typeof(string), "timestamptz" },
        // natural widths, narrower names the database polices, and wider names that cannot lose a value
        { typeof(int), "int" },
        { typeof(int), "integer" },
        { typeof(int), "int4" },
        { typeof(int), "tinyint" },
        { typeof(int), "mediumint" },
        { typeof(int), "int2" },
        { typeof(int), "int8" },
        { typeof(long), "bigint" },
        { typeof(long?), "bigint" },
        { typeof(long), "int8" },
        { typeof(long), "tinyint" },
        { typeof(long), "mediumint" },
        { typeof(long), "int4" },
        // the fixtures' types on string and JSON properties
        { typeof(string), "varchar(7)" },
        { typeof(string), "varchar(500)" },
        { typeof(string), "text" },
        { typeof(string), "jsonb" },
        { typeof(string), "bigint" },
        { typeof(decimal), "numeric(18,4)" },
        { typeof(Guid), "char(36)" },
        // a fork's own type names
        { typeof(int), "my_vendor_int" },
        { typeof(DateTime), "my_vendor_stamp" },
        { typeof(long), "my_vendor_long" },
        // measured as fine on MySQL and MariaDB
        { typeof(long), "int unsigned" },
        { typeof(long), "bigint unsigned" },
        { typeof(int), "int unsigned" },
        { typeof(int), "bigint unsigned" },
        // tinyint with another length, or none
        { typeof(int), "tinyint(4)" },
        { typeof(long), "tinyint(3)" },
        { typeof(int), "tinyint not null" },
        // whole words only
        { typeof(long), "int4range" },
        { typeof(long), "interval" },
        { typeof(int), "bigint_custom" },
        { typeof(int), "smallint_custom" },
        { typeof(DateTime), "datetimeoffset_custom" },
        { typeof(DateTime), "timestamptz_custom" },
        { typeof(DateTime), "timestamp with local time zone" },
        { typeof(DateTime), "[datetime2](3)" },
        { typeof(int), "[int] not null" },
        { typeof(long), "[bigint] identity(1,1)" },
        // PostgreSQL array types, and a quoted or bracketed word that is not a table entry
        { typeof(long), "int[]" },
        { typeof(long), "integer[]" },
        { typeof(long), "int []" },
        { typeof(long), "int array" },
        { typeof(long), "integer ARRAY[3]" },
        { typeof(DateTime), "timestamptz[]" },
        { typeof(int), "smallint[]" },
        { typeof(long), "_int4" },
        { typeof(long), "\"int custom\"" },
        { typeof(long), "[int value]" },
        // a group with no word before it adds nothing, so tinyint has no length here
        { typeof(int), "(1)tinyint" },
        // decimal types with a precision
        { typeof(decimal), "decimal(18,4)" },
        { typeof(decimal), "decimal(18)" },
        { typeof(decimal), "numeric(10,0)" },
        { typeof(decimal), "decimal (18,4)" },
        { typeof(decimal), "numeric(18, 4) unsigned" },
        { typeof(decimal), "decimal(18,4) zerofill" },
        { typeof(decimal), "dec(18,4)" },
        { typeof(decimal), "fixed(18,4)" },
        { typeof(decimal), "decimal(18,4)[]" },
        { typeof(decimal), "decimal[]" },
        { typeof(decimal), "numeric array" },
        { typeof(decimal), "decimal_custom" },
        { typeof(decimal), "fixedpoint" },
        { typeof(decimal), "decimals" },
        { typeof(double), "decimal" },
        { typeof(long), "numeric" },
        { typeof(string), "decimal" },
        { typeof(decimal?), "numeric(18,4)" },
        { typeof(decimal), "decimal(18, 4) not null" },
        { typeof(decimal), "[decimal](18,4)" },
        // floating-point types on a property that is itself floating-point
        { typeof(double), "double" },
        { typeof(double), "double precision" },
        { typeof(double?), "float" },
        { typeof(float), "real" },
        { typeof(float), "float4" },
        // another type: unsigned, arrays, whole words only
        { typeof(decimal), "float4 unsigned" },
        { typeof(decimal), "float8 zerofill" },
        { typeof(decimal), "float unsigned[]" },
        { typeof(decimal), "float4[]" },
        { typeof(decimal), "real[]" },
        { typeof(decimal), "double precision[]" },
        { typeof(decimal), "double precision array" },
        { typeof(decimal), "float8 []" },
        { typeof(decimal), "floaty" },
        { typeof(decimal), "realm" },
        { typeof(decimal), "doubled" },
        { typeof(decimal), "float4range" },
        { typeof(decimal), "my_float" },
        { typeof(decimal), "\"double custom\"" },
        { typeof(decimal), "\"double precision\"" },
        // another property type is not the concern of the floating-point rule
        { typeof(long), "real" },
        { typeof(int), "double" },
        { typeof(string), "float" },
        // boolean and integer types on a bool property
        { typeof(bool), "bit" },
        { typeof(bool), "boolean" },
        { typeof(bool?), "boolean" },
        { typeof(bool), "tinyint(1)" },
        { typeof(bool), "tinyint" },
        { typeof(bool), "int" },
        { typeof(bool), "bigint" },
        { typeof(bool), "smallint" },
        { typeof(bool), "[bit] not null" },
        // names outside the table
        { typeof(bool), "string" },
        { typeof(bool), "varchar2(5)" },
        { typeof(bool), "clob" },
        { typeof(bool), "texty" },
        { typeof(bool), "varchar_custom" },
        // another type: arrays, whole words only
        { typeof(bool), "text[]" },
        { typeof(bool), "varchar(5)[]" },
        { typeof(bool), "varchar[]" },
        { typeof(bool), "character varying[]" },
        { typeof(bool), "character varying array" },
        { typeof(bool), "text array" },
        { typeof(bool), "char []" },
        { typeof(bool), "\"text custom\"" },
        // a string-typed name on a property that is not bool
        { typeof(int), "varchar(5)" },
        { typeof(Guid), "text" },
        { typeof(decimal), "varchar(5)" },
        // no declaration, or an empty one
        { typeof(int), null },
        { typeof(decimal), null },
        { typeof(bool), "" },
        { typeof(DateTime), "" },
    };

    [Theory]
    [MemberData(nameof(Passing))]
    public void Any_other_combination_passes(Type propertyType, string? columnType) =>
        ColumnTypeFit.Find(propertyType, columnType).Should().BeNull();

    // ---- through the scan ----

    [CmsCollection("Fit", Name = "cdtFit")]
    public sealed class Fit
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "datetime2(3)")]
        [CmsField(Label = "Zone-less", Interface = FieldInterface.DateTime)] public DateTime ZoneLess { get; set; }
        [ColumnShape(ColumnShape.TimestampWithTimeZone)]
        [CmsField(Label = "Shaped", Interface = FieldInterface.DateTime)] public DateTime? Shaped { get; set; }
        [SugarColumn(ColumnDataType = "int")]
        [CmsField(Label = "Natural", Interface = FieldInterface.Number)] public int Natural { get; set; }
        [SugarColumn(ColumnDataType = "bigint")]
        [CmsField(Label = "Wide", Interface = FieldInterface.Number)] public long Wide { get; set; }
        [SugarColumn(ColumnDataType = "tinyint")]
        [CmsField(Label = "Small", Interface = FieldInterface.Number)] public int Small { get; set; }
        [SugarColumn(ColumnDataType = "my_vendor_int")]
        [CmsField(Label = "Vendor", Interface = FieldInterface.Number)] public int Vendor { get; set; }
        [SugarColumn(ColumnDataType = "varchar(7)")]
        [CmsField(Label = "Text", Interface = FieldInterface.Text)] public string? Text { get; set; }
        [SugarColumn(ColumnDataType = "decimal(18,4)")]
        [CmsField(Label = "Price", Interface = FieldInterface.Number)] public decimal Price { get; set; }
        [SugarColumn(ColumnDataType = "numeric(18,4)", IsNullable = true)]
        [CmsField(Label = "Cost", Interface = FieldInterface.Number)] public decimal? Cost { get; set; }
        [SugarColumn(ColumnDataType = "double precision")]
        [CmsField(Label = "Ratio", Interface = FieldInterface.Number)] public double Ratio { get; set; }
        [SugarColumn(ColumnDataType = "tinyint(1)")]
        [CmsField(Label = "Flag", Interface = FieldInterface.Boolean)] public bool Flag { get; set; }
        [SugarColumn(ColumnDataType = "boolean", IsNullable = true)]
        [CmsField(Label = "Maybe", Interface = FieldInterface.Boolean)] public bool? Maybe { get; set; }
    }

    [Fact]
    public void Declarations_that_fit_their_property_scan()
    {
        var collection = MetadataScanner.ScanTypes([typeof(Fit)]).Single();

        collection.Fields.Select(f => f.Name)
            .Should().Contain(["zoneLess", "shaped", "natural", "wide", "small", "vendor", "text", "price", "cost", "ratio", "flag", "maybe"]);
    }

    [CmsCollection("Zone-aware", Name = "cdtZoneAware")]
    public sealed class ZoneAware
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "timestamptz")]
        [CmsField(Label = "At", Interface = FieldInterface.DateTime)] public DateTime At { get; set; }
    }

    [CmsCollection("Zone-aware nullable", Name = "cdtZoneAwareNullable")]
    public sealed class ZoneAwareNullable
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = " Timestamp(3)  With  Time Zone ", IsNullable = true)]
        [CmsField(Label = "At", Interface = FieldInterface.DateTime)] public DateTime? At { get; set; }
    }

    [CmsCollection("Offset", Name = "cdtOffset")]
    public sealed class Offset
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "datetimeoffset(7)")]
        [CmsField(Label = "At", Interface = FieldInterface.Date)] public DateTime At { get; set; }
    }

    [CmsCollection("Int on bigint", Name = "cdtIntBigint")]
    public sealed class IntBigint
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "bigint")]
        [CmsField(Label = "Count", Interface = FieldInterface.Number)] public int Count { get; set; }
    }

    [CmsCollection("Nullable int on bigint", Name = "cdtNullableIntBigint")]
    public sealed class NullableIntBigint
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "bigint", IsNullable = true)]
        [CmsField(Label = "Count", Interface = FieldInterface.Number)] public int? Count { get; set; }
    }

    [CmsCollection("Int on smallint", Name = "cdtIntSmallint")]
    public sealed class IntSmallint
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "SMALLINT", IsNullable = true)]
        [CmsField(Label = "Count", Interface = FieldInterface.Number)] public int? Count { get; set; }
    }

    [CmsCollection("Int on tinyint(1)", Name = "cdtIntTinyint1")]
    public sealed class IntTinyint1
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "tinyint(1)")]
        [CmsField(Label = "Level", Interface = FieldInterface.Number)] public int Level { get; set; }
    }

    [CmsCollection("Long on tinyint(1)", Name = "cdtLongTinyint1")]
    public sealed class LongTinyint1
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "tinyint(1)")]
        [CmsField(Label = "Level", Interface = FieldInterface.Number)] public long Level { get; set; }
    }

    [CmsCollection("Long on int", Name = "cdtLongInt")]
    public sealed class LongInt
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "int")]
        [CmsField(Label = "Total", Interface = FieldInterface.Number)] public long Total { get; set; }
    }

    [CmsCollection("Long on integer", Name = "cdtLongInteger")]
    public sealed class LongInteger
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "integer")]
        [CmsField(Label = "Total", Interface = FieldInterface.Number)] public long Total { get; set; }
    }

    [CmsCollection("Long on smallint", Name = "cdtLongSmallint")]
    public sealed class LongSmallint
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "smallint")]
        [CmsField(Label = "Total", Interface = FieldInterface.Number)] public long Total { get; set; }
    }

    [CmsCollection("Decimal on real", Name = "cdtDecimalReal")]
    public sealed class DecimalReal
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "real")]
        [CmsField(Label = "Price", Interface = FieldInterface.Number)] public decimal Price { get; set; }
    }

    [CmsCollection("Nullable decimal on double", Name = "cdtDecimalDouble")]
    public sealed class DecimalNullableDouble
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "Double Precision", IsNullable = true)]
        [CmsField(Label = "Price", Interface = FieldInterface.Number)] public decimal? Price { get; set; }
    }

    [CmsCollection("Decimal without precision", Name = "cdtDecimalBare")]
    public sealed class DecimalBare
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "decimal")]
        [CmsField(Label = "Price", Interface = FieldInterface.Number)] public decimal Price { get; set; }
    }

    [CmsCollection("Nullable decimal without precision", Name = "cdtNumericBare")]
    public sealed class NumericBareNullable
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "NUMERIC null", IsNullable = true)]
        [CmsField(Label = "Price", Interface = FieldInterface.Number)] public decimal? Price { get; set; }
    }

    [CmsCollection("Bool on varchar", Name = "cdtBoolVarchar")]
    public sealed class BoolVarchar
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "varchar(5)")]
        [CmsField(Label = "Flag", Interface = FieldInterface.Boolean)] public bool Flag { get; set; }
    }

    [CmsCollection("Nullable bool on text", Name = "cdtBoolText")]
    public sealed class BoolNullableText
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "text", IsNullable = true)]
        [CmsField(Label = "Flag", Interface = FieldInterface.Checkbox)] public bool? Flag { get; set; }
    }

    [CmsCollection("Shape wins", Name = "cdtShapeAndType")]
    public sealed class ShapeAndZoneAware
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [ColumnShape(ColumnShape.TimestampWithTimeZone)]
        [SugarColumn(ColumnDataType = "timestamptz")]
        [CmsField(Label = "At", Interface = FieldInterface.DateTime)] public DateTime At { get; set; }
    }

    [CmsCollection("Ignored", Name = "cdtIgnored")]
    public sealed class Ignored
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true, ColumnDataType = "timestamptz")]
        [CmsField(Label = "At", Interface = FieldInterface.DateTime)] public DateTime At { get; set; }
    }

    public static TheoryData<Type, string, string> ScanRejected() => new()
    {
        { typeof(ZoneAware), "ZoneAware.At", "ColumnDataType = \"timestamptz\")] and is a DateTime property" },
        { typeof(ZoneAwareNullable), "ZoneAwareNullable.At", "ColumnDataType = \" Timestamp(3)  With  Time Zone \")] and is a DateTime? property" },
        { typeof(Offset), "Offset.At", "ColumnDataType = \"datetimeoffset(7)\")] and is a DateTime property" },
        { typeof(IntBigint), "IntBigint.Count", "ColumnDataType = \"bigint\")] and is an int property" },
        { typeof(IntSmallint), "IntSmallint.Count", "ColumnDataType = \"SMALLINT\")] and is an int? property" },
        { typeof(IntTinyint1), "IntTinyint1.Level", "ColumnDataType = \"tinyint(1)\")] and is an int property" },
        { typeof(LongInt), "LongInt.Total", "ColumnDataType = \"int\")] and is a long property" },
        { typeof(LongInteger), "LongInteger.Total", "ColumnDataType = \"integer\")] and is a long property" },
        { typeof(LongSmallint), "LongSmallint.Total", "ColumnDataType = \"smallint\")] and is a long property" },
        { typeof(DecimalReal), "DecimalReal.Price", "ColumnDataType = \"real\")] and is a decimal property" },
        { typeof(DecimalNullableDouble), "DecimalNullableDouble.Price", "ColumnDataType = \"Double Precision\")] and is a decimal? property" },
        { typeof(DecimalBare), "DecimalBare.Price", "ColumnDataType = \"decimal\")] and is a decimal property" },
        { typeof(NumericBareNullable), "NumericBareNullable.Price", "ColumnDataType = \"NUMERIC null\")] and is a decimal? property" },
        { typeof(BoolVarchar), "BoolVarchar.Flag", "ColumnDataType = \"varchar(5)\")] and is a bool property" },
        { typeof(BoolNullableText), "BoolNullableText.Flag", "ColumnDataType = \"text\")] and is a bool? property" },
    };

    [Theory]
    [MemberData(nameof(ScanRejected))]
    public void A_harmful_combination_fails_the_scan_naming_the_property_the_type_and_the_declaration(
        Type type, string property, string declarations)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain($"'{property}'").And.Contain(declarations);
        message.All(c => c < 128).Should().BeTrue(message);
        message.Split(". ").Length.Should().BeLessThanOrEqualTo(2, message);
    }

    [Fact]
    public void The_message_for_a_zone_aware_type_offers_the_shape()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(ZoneAware)]);

        act.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Field 'ZoneAware.At' has [SugarColumn(ColumnDataType = \"timestamptz\")] and is a DateTime property. " +
            "Declare [ColumnShape(ColumnShape.TimestampWithTimeZone)] and remove ColumnDataType: " +
            "only the shape makes the framework read and write the column as a UTC instant on PostgreSQL and SQL Server, " +
            "and without it a facet of the column and a facet of a zone-less DateTime column fail each other on SQL Server.");
    }

    [Fact]
    public void The_message_for_an_integer_width_offers_the_matching_type()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(IntBigint)]);

        act.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Field 'IntBigint.Count' has [SugarColumn(ColumnDataType = \"bigint\")] and is an int property. " +
            "Declare the property as long, or remove ColumnDataType: " +
            "on SQL Server a facet of this column fails when a facet of a column of the property's own width ran first, " +
            "and the check is the same on every backend.");
    }

    [Fact]
    public void The_message_for_tinyint_with_a_length_of_one_names_the_boolean_reading()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(IntTinyint1)]);

        act.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Field 'IntTinyint1.Level' has [SugarColumn(ColumnDataType = \"tinyint(1)\")] and is an int property. " +
            "Write tinyint without its length, or remove ColumnDataType: " +
            "the MySQL driver reads a tinyint(1) column as a boolean, so a stored 7 comes back as 1, " +
            "and the check is the same on every backend.");
    }

    [Fact]
    public void The_message_for_a_long_on_tinyint_with_a_length_of_one_is_the_same_rule()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(LongTinyint1)]);

        act.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Field 'LongTinyint1.Level' has [SugarColumn(ColumnDataType = \"tinyint(1)\")] and is a long property. " +
            "Write tinyint without its length, or remove ColumnDataType: " +
            "the MySQL driver reads a tinyint(1) column as a boolean, so a stored 7 comes back as 1, " +
            "and the check is the same on every backend.");
    }

    [Fact]
    public void The_message_for_a_decimal_on_a_floating_point_type_offers_an_exact_type()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(DecimalReal)]);

        act.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Field 'DecimalReal.Price' has [SugarColumn(ColumnDataType = \"real\")] and is a decimal property. " +
            "Declare decimal(18,4) or another decimal type with an explicit scale, or remove ColumnDataType: " +
            "a single-precision type returns 12345.6789 as 12345.68 or 12345.7, " +
            "a double-precision type returns 12345678901234.5678 as 12345678901234.6, " +
            "and filters, facets and sums on the column then go wrong, " +
            "and the check is the same on every backend.");
    }

    [Fact]
    public void The_message_for_a_nullable_decimal_offers_the_same_fix()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(DecimalNullableDouble)]);

        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("and is a decimal? property. Declare decimal(18,4) or another decimal type with an explicit scale, or remove ColumnDataType:");
    }

    [Fact]
    public void The_message_for_a_decimal_without_precision_asks_for_the_precision_and_scale()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(DecimalBare)]);

        act.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Field 'DecimalBare.Price' has [SugarColumn(ColumnDataType = \"decimal\")] and is a decimal property. " +
            "Write the precision and scale, such as decimal(18,4), or remove ColumnDataType: " +
            "a decimal type written with no precision gets scale 0 on MySQL and MariaDB (decimal(10,0)) and, " +
            "for numeric and dec, on SQL Server, so 12345.6789 reads back as 12346, " +
            "and the check is the same on every backend.");
    }

    [Fact]
    public void The_message_for_a_nullable_decimal_without_precision_offers_the_same_fix()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(NumericBareNullable)]);

        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("and is a decimal? property. Write the precision and scale, such as decimal(18,4), or remove ColumnDataType:");
    }

    [Fact]
    public void The_message_for_a_bool_on_a_string_type_offers_a_boolean_type()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(BoolVarchar)]);

        act.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Field 'BoolVarchar.Flag' has [SugarColumn(ColumnDataType = \"varchar(5)\")] and is a bool property. " +
            "Remove ColumnDataType, or declare boolean (PostgreSQL, MySQL, MariaDB) or bit (SQL Server): " +
            "a string column is not read back as a bool, so where the database accepts the write, " +
            "reading the row and a facet of the column then fail, " +
            "and the check is the same on every backend.");
    }

    [Fact]
    public void The_message_for_a_nullable_bool_offers_the_same_fix()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(BoolNullableText)]);

        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("and is a bool? property. Remove ColumnDataType, or declare boolean (PostgreSQL, MySQL, MariaDB) or bit (SQL Server):");
    }

    [Fact]
    public void The_message_for_a_long_on_int_offers_int()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(LongInt)]);

        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("Declare the property as int, or remove ColumnDataType");
    }

    [Fact]
    public void The_message_for_a_nullable_property_offers_the_nullable_type()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(NullableIntBigint)]);

        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("and is an int? property. Declare the property as long?, or remove ColumnDataType");
    }

    [Fact]
    public void The_message_for_a_width_with_no_matching_property_type_offers_the_column_type()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(IntSmallint)]);

        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("Remove ColumnDataType, or declare the column as int:");
    }

    [Fact]
    public void A_shape_beside_a_type_name_keeps_the_shape_and_type_message()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(ShapeAndZoneAware)]);

        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("both [ColumnShape(TimestampWithTimeZone)] and [SugarColumn(ColumnDataType = \"timestamptz\")]");
    }

    [Fact]
    public void An_ignored_property_is_not_checked()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(Ignored)]);

        act.Should().NotThrow();
    }
}
