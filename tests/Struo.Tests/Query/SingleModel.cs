// tests/Struo.Tests/Query/SingleModel.cs
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Query;
using Struo.Infrastructure.Query;

namespace Struo.Tests.Query;

/// <summary>The translated leaf of an operator that renders as one <see cref="ConditionalModel"/>.</summary>
internal static class SingleModel
{
    public static ConditionalModel Of(ComparisonFilter c, EntityDescriptor d, ISqlSugarClient db) =>
        (ConditionalModel)ConditionalModelTranslator.ToCondition(c, d, db)!;

    /// <summary>The text a bound equality compares with: <c>FieldValue</c> holds a fixed word and the value goes through the convert function.</summary>
    public static object? BoundValue(ConditionalModel model) => model.FieldValueConvertFunc?.Invoke(model.FieldValue);
}
