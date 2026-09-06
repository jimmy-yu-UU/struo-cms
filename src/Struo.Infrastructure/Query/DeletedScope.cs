using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

internal static class DeletedScope
{
    // Only/With lift the global soft-delete floor (registered in SqlSugarClientFactory) for this
    // query; Only additionally restricts to trashed rows via an extra DeletedAt-IS-NOT-NULL
    // conditional. It stays a ConditionalModel rather than a cast-based Where predicate on
    // ISoftDeletable, which SqlSugar cannot translate reliably.
    public static ISugarQueryable<T> Root<T>(ISqlSugarClient db, List<IConditionalModel> conditionals, DeletedFilter deleted)
        where T : class, new()
    {
        var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(typeof(T));
        var q = db.Queryable<T>();
        if (!isSoftDeletable || deleted == DeletedFilter.Exclude) return q.Where(conditionals);
        q = q.ClearFilter<ISoftDeletable>();
        if (deleted != DeletedFilter.Only) return q.Where(conditionals);
        var guard = new ConditionalModel
        {
            FieldName = db.EntityMaintenance.GetDbColumnName<T>(nameof(ISoftDeletable.DeletedAt)),
            ConditionalType = ConditionalType.IsNot,
            FieldValue = null,
        };
        return q.Where([.. conditionals, guard]);
    }
}
