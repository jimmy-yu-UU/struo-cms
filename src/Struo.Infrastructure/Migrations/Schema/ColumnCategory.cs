namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>Backend-neutral type family of a column; the unit the schema checker compares.</summary>
public enum ColumnCategory
{
    String,
    LongText,
    Integer,
    BigInteger,
    Decimal,
    Boolean,
    Guid,
    DateTime,
    DateTimeWithTimeZone,
    Binary,
    Other
}
