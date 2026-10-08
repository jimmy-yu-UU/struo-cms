using FluentMigrator.Builders;
using FluentMigrator.Infrastructure;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations;

/// <summary>
/// Column-type helpers that resolve a <see cref="ColumnShape"/> to the active backend's literal through
/// <see cref="ColumnTypeMap"/>, the same map CodeFirst uses. The backend is passed in as the migration's
/// <c>Db</c>, because an extension method cannot reach the migration context.
/// </summary>
public static class ColumnTypeExtensions
{
    /// <summary><see cref="ColumnShape.Timestamp"/> resolves through <see cref="AsTimestamp{TNext}"/>.</summary>
    public static TNext AsShape<TNext>(this IColumnTypeSyntax<TNext> syntax, ColumnShape shape, StruoDbType db)
        where TNext : IFluentSyntax =>
        shape == ColumnShape.Timestamp
            ? syntax.AsTimestamp(db)
            : syntax.AsCustom(ColumnTypeMap.For(shape, DbTypeMapper.Map(db)));

    public static TNext AsLongText<TNext>(this IColumnTypeSyntax<TNext> syntax, StruoDbType db)
        where TNext : IFluentSyntax => syntax.AsShape(ColumnShape.LongText, db);

    /// <summary>
    /// Zone-less timestamp, matching the CodeFirst convention for <see cref="DateTime"/> properties:
    /// <see cref="ColumnShape.Timestamp"/> on MySQL and MariaDB, <c>AsDateTime()</c> on every other backend.
    /// </summary>
    public static TNext AsTimestamp<TNext>(this IColumnTypeSyntax<TNext> syntax, StruoDbType db)
        where TNext : IFluentSyntax =>
        db == StruoDbType.MySql
            ? syntax.AsCustom(ColumnTypeMap.For(ColumnShape.Timestamp, DbTypeMapper.Map(db)))
            : syntax.AsDateTime();

    /// <summary>JSON columns are stored as long text, matching the CodeFirst JSON-column convention.</summary>
    public static TNext AsJson<TNext>(this IColumnTypeSyntax<TNext> syntax, StruoDbType db)
        where TNext : IFluentSyntax => syntax.AsShape(ColumnShape.LongText, db);
}
