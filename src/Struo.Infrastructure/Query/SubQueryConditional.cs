// src/Struo.Infrastructure/Query/SubQueryConditional.cs
using System.Text.RegularExpressions;
using SqlSugar;

namespace Struo.Infrastructure.Query;

internal enum SubQueryKind { In, NotIn, NullOrNotIn }

// Wraps a projected subquery (column selector + Where + ToSql()) as a top-level or grouped
// ConditionalModel via ICustomConditionalFunc. Every instance renames its subquery's SqlSugar
// parameters to a process-unique prefix (see Renamed below) — leaf conditionals built by
// ConditionalModelTranslator (Equal/GreaterThan/Like/...) are parameterized, and SqlSugar assigns
// parameter names per query instance from the leaf's field name + its index in the conditional list
// (e.g. "@ConditName0"), so an inner subquery and its outer query independently produce the SAME
// name whenever they filter the same-named field at the same index. Composing subqueries through
// Wrap (never through the string overload of In(...)/NotIn(...)) is what lets every nesting level
// go through this renaming exactly once.
internal sealed class SubQueryConditional : ICustomConditionalFunc
{
    private static int _sequence;

    private readonly SubQueryKind _kind;
    private readonly string _column;
    private readonly string _sql;
    private readonly SugarParameter[] _parameters;

    public SubQueryConditional(SubQueryKind kind, string column, KeyValuePair<string, List<SugarParameter>> sub)
    {
        _kind = kind;
        _column = column;
        (_sql, _parameters) = Renamed(sub);
    }

    public KeyValuePair<string, SugarParameter[]> GetConditionalSql(ConditionalModel json, int index)
    {
        var sql = _kind switch
        {
            SubQueryKind.In => $"{_column} IN ({_sql})",
            SubQueryKind.NotIn => $"{_column} NOT IN ({_sql})",
            SubQueryKind.NullOrNotIn => $"({_column} IS NULL OR {_column} NOT IN ({_sql}))",
            _ => throw new InvalidOperationException($"Unknown SubQueryKind {_kind}."),
        };
        return new KeyValuePair<string, SugarParameter[]>(sql, _parameters);
    }

    public static ConditionalModel Wrap(SubQueryKind kind, string column, KeyValuePair<string, List<SugarParameter>> sub) =>
        new() { FieldName = column, CustomConditionalFunc = new SubQueryConditional(kind, column, sub) };

    // Allocates one prefix per instance (a single Interlocked.Increment, not per parameter), renames
    // every parameter in sub.Value to "@sq{n}_{originalNameWithoutAt}", and rewrites sub.Key so the
    // SQL text matches. The negative lookahead in the replace regex stops "@ConditName0" from also
    // matching inside "@ConditName01" when both appear in the same subquery.
    private static (string Sql, SugarParameter[] Parameters) Renamed(KeyValuePair<string, List<SugarParameter>> sub)
    {
        var prefix = $"sq{Interlocked.Increment(ref _sequence)}_";
        var sql = sub.Key;
        var parameters = new SugarParameter[sub.Value.Count];
        for (var i = 0; i < sub.Value.Count; i++)
        {
            var parameter = sub.Value[i];
            var oldName = parameter.ParameterName; // e.g. "@ConditName0"
            var newName = $"@{prefix}{oldName.TrimStart('@')}"; // e.g. "@sq7_ConditName0"
            sql = Regex.Replace(sql, Regex.Escape(oldName) + "(?![A-Za-z0-9_])", newName);
            parameter.ParameterName = newName;
            parameters[i] = parameter;
        }
        return (sql, parameters);
    }
}
