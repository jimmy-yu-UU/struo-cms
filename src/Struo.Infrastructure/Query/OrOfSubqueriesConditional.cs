// src/Struo.Infrastructure/Query/OrOfSubqueriesConditional.cs
using SqlSugar;

namespace Struo.Infrastructure.Query;

// Merges TWO OR MORE subquery-wrapped ConditionalModels (each built by SubQueryConditional.Wrap) into
// ONE ConditionalModel that renders "({sql1} OR {sql2} OR …)". Exists because of a measured SqlSugar
// defect (see FilterTranslator.cs's AppendModel doc comment): ConditionalCollections double-inserts an
// "AND"/"OR" keyword whenever two ADJACENT entries in one ConditionalList are both
// ICustomConditionalFunc-backed. FilterTranslator's OR-group builder merges every wrapped child into
// one instance of this class BEFORE handing SqlSugar a ConditionalCollections, so at most one custom
// entry — this merged one — ever reaches SqlSugar's own OR-group rendering. This is the fourth and
// last hand-assembled SQL string form this translator produces, alongside SubQueryConditional's
// "{col} IN (…)", "{col} NOT IN (…)" and "({col} IS NULL OR {col} NOT IN (…))".
internal sealed class OrOfSubqueriesConditional : ICustomConditionalFunc
{
    private readonly string _sql;
    private readonly SugarParameter[] _parameters;

    private OrOfSubqueriesConditional(string sql, SugarParameter[] parameters)
    {
        _sql = sql;
        _parameters = parameters;
    }

    public KeyValuePair<string, SugarParameter[]> GetConditionalSql(ConditionalModel json, int index) =>
        new(_sql, _parameters);

    // Each child was already built by SubQueryConditional.Wrap, whose own renaming gives it a
    // process-unique parameter prefix (a fresh Interlocked.Increment per Wrap() call) — so merging is
    // pure concatenation, no further renaming needed. Reads each child's rendered SQL/parameters via
    // its own CustomConditionalFunc.GetConditionalSql rather than re-deriving them.
    public static ConditionalModel Merge(IReadOnlyList<ConditionalModel> wrapped)
    {
        var parts = new List<string>(wrapped.Count);
        var parameters = new List<SugarParameter>();
        for (var i = 0; i < wrapped.Count; i++)
        {
            var child = wrapped[i];
            var (sql, ps) = child.CustomConditionalFunc!.GetConditionalSql(child, i);
            parts.Add(sql);
            parameters.AddRange(ps);
        }
        var mergedSql = "(" + string.Join(" OR ", parts) + ")";
        return new ConditionalModel
        {
            FieldName = wrapped[0].FieldName,
            CustomConditionalFunc = new OrOfSubqueriesConditional(mergedSql, parameters.ToArray()),
        };
    }
}
