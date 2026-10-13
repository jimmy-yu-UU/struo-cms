using System.Data;
using System.Reflection;
using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Binds and reads a <see cref="ColumnShape.TimestampWithTimeZone"/> <see cref="DateTime"/> column as a
/// UTC instant on PostgreSQL and SQL Server; <c>SqlSugarClientFactory</c> attaches it to those columns.
/// A write is bound as <see cref="System.Data.DbType.DateTimeOffset"/> (timestamptz, datetimeoffset): a
/// <see cref="DateTimeKind.Local"/> value is converted to UTC and an <see cref="DateTimeKind.Unspecified"/>
/// one is taken as UTC. A read returns <see cref="DateTimeKind.Utc"/>. Neither depends on the server's
/// or the host's time zone. Why a converter: docs/ai/decisions/timestamptz-columns-are-utc-instants.md
/// </summary>
internal sealed class UtcInstantConverter : ISugarDataConverter
{
    public SugarParameter ParameterConverter<T>(object columnValue, int columnIndex)
    {
        var value = columnValue is DateTime instant ? ToUtc(instant) : columnValue ?? DBNull.Value;
        return new SugarParameter("@UtcInstant" + columnIndex, value) { DbType = System.Data.DbType.DateTimeOffset };
    }

    public T QueryConverter<T>(IDataRecord dataRecord, int dataRecordIndex)
    {
        if (dataRecord.IsDBNull(dataRecordIndex)) return default!;
        object value = dataRecord.GetValue(dataRecordIndex) switch
        {
            DateTimeOffset offset => offset.UtcDateTime,
            DateTime instant => ToUtc(instant),
            var other => other,
        };
        return (T)value;
    }

    /// <summary>True on the backends this converter is attached on: PostgreSQL and SQL Server.</summary>
    internal static bool AppliesTo(SqlSugar.DbType dbType) =>
        dbType is SqlSugar.DbType.PostgreSQL or SqlSugar.DbType.SqlServer;

    /// <summary>True for a <see cref="DateTime"/> or <see cref="DateTime"/>? property declared <see cref="ColumnShape.TimestampWithTimeZone"/>.</summary>
    internal static bool IsUtcInstantProperty(PropertyInfo property) =>
        property.GetCustomAttribute<ColumnShapeAttribute>()?.Shape == ColumnShape.TimestampWithTimeZone
        && (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType) == typeof(DateTime);

    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
