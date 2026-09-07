using System.Linq.Expressions;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

internal sealed class AggregateQueries(ISqlSugarClient db, IEntityRegistry registry, FilterTranslator filters)
{
    private static readonly GenericDispatcher<Func<AggregateQueries, List<IConditionalModel>, DeletedFilter, LambdaExpression, CancellationToken, Task<AggregateRow?>>> RunDispatcher =
        new(typeof(AggregateQueries), nameof(Run), [typeof(List<IConditionalModel>), typeof(DeletedFilter), typeof(LambdaExpression), typeof(CancellationToken)]);

    private async Task<AggregateRow?> Run<T>(List<IConditionalModel> conds, DeletedFilter deleted, LambdaExpression projection, CancellationToken ct)
        where T : class, new() =>
        await DeletedScope.Root<T>(db, conds, deleted).Select((Expression<Func<T, AggregateRow>>)projection).FirstAsync(ct);

    public async Task<AggregateResult> AggregateAsync(
        string collection, QueryModel query, AggregateSpec spec, IReadOnlyList<string> searchableFields,
        string? queryLocale, DeletedFilter deleted, CancellationToken ct)
    {
        var d = RepositoryHelpers.Descriptor(registry, collection);
        var conds = filters.Translate(collection, query.Filter, query.Search, query.SearchCandidates, searchableFields, queryLocale);
        var slots = Enum.GetValues<AggregateOp>()
            .Where(spec.Fields.ContainsKey)
            .SelectMany(op => spec.Fields[op].Select(field => (Op: op, Field: field, Property: PropertyFor(d, field))))
            .ToList();

        var values = new Dictionary<AggregateOp, Dictionary<string, object?>>();
        foreach (var chunk in slots.Chunk(AggregateRow.SlotCount))
        {
            var projection = ColumnSelectorFactory.AggregateProjection(d.EntityType, chunk.Select(s => (s.Op, s.Property)).ToList());
            var row = await RunDispatcher.For(d.EntityType)(this, conds, deleted, projection, ct);
            for (var i = 0; i < chunk.Length; i++) Collect(values, d, chunk[i], row?.Slot(i));
        }
        return new AggregateResult(values.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, object?>)kv.Value));
    }

    private static void Collect(Dictionary<AggregateOp, Dictionary<string, object?>> values, EntityDescriptor d,
        (AggregateOp Op, string Field, string Property) slot, object? raw)
    {
        var propertyType = ColumnSelectorFactory.PropertyType(d.EntityType, slot.Property);
        var normalized = AggregateValueNormalizer.Normalize(slot.Op, propertyType, raw);
        if (normalized is null && slot.Op == AggregateOp.Count) normalized = 0L;
        if (!values.TryGetValue(slot.Op, out var perField)) values[slot.Op] = perField = new Dictionary<string, object?>();
        perField[slot.Field] = normalized;
    }

    private static string PropertyFor(EntityDescriptor d, string field) =>
        d.FieldToProperty.TryGetValue(field, out var p) ? p : d.Properties[field].Name;
}
