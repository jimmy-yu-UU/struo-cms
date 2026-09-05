// src/Struo.Infrastructure/Query/SubQueryConditional.cs
using SqlSugar;

namespace Struo.Infrastructure.Query;

internal enum SubQueryKind { In, NotIn, NullOrNotIn }

internal sealed class SubQueryConditional(SubQueryKind kind, string column, KeyValuePair<string, List<SugarParameter>> sub) : ICustomConditionalFunc
{
    public KeyValuePair<string, SugarParameter[]> GetConditionalSql(ConditionalModel json, int index)
    {
        var sql = kind switch
        {
            SubQueryKind.In => $"{column} IN ({sub.Key})",
            SubQueryKind.NotIn => $"{column} NOT IN ({sub.Key})",
            SubQueryKind.NullOrNotIn => $"({column} IS NULL OR {column} NOT IN ({sub.Key}))",
            _ => throw new InvalidOperationException($"Unknown SubQueryKind {kind}."),
        };
        return new KeyValuePair<string, SugarParameter[]>(sql, sub.Value.ToArray());
    }

    public static ConditionalModel Wrap(SubQueryKind kind, string column, KeyValuePair<string, List<SugarParameter>> sub) =>
        new() { FieldName = column, CustomConditionalFunc = new SubQueryConditional(kind, column, sub) };
}
