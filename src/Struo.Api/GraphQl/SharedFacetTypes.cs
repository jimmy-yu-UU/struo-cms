// src/Struo.Api/GraphQl/SharedFacetTypes.cs
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Builds the reusable types shared by every collection's facets/aggregate GraphQL surface:
/// the <c>AggregateInput</c> argument type (one <c>[String!]</c> field per
/// <see cref="Struo.Application.Query.QueryParser.AggregateOps"/> key) and the
/// <c>FacetValue</c>/<c>FacetResult</c> output types that mirror the domain
/// <see cref="FacetBucket"/>/<see cref="FacetResult"/> shapes.
/// </summary>
internal static class SharedFacetTypes
{
    internal static IEnumerable<ITypeSystemMember> Build()
    {
        var input = new InputObjectTypeConfiguration("AggregateInput", null, typeof(IReadOnlyDictionary<string, object?>));
        foreach (var op in Struo.Application.Query.QueryParser.AggregateOps.Keys)
            input.Fields.Add(new InputFieldConfiguration(op, null, TypeReference.Parse("[String!]")));
        yield return InputObjectType.CreateUnsafe(input);

        var value = new ObjectTypeConfiguration("FacetValue", null, typeof(FacetBucket));
        value.Fields.Add(CollectionSchemaBuilder.Field("value", "Any", ctx => ctx.Parent<FacetBucket>().Value));
        value.Fields.Add(CollectionSchemaBuilder.Field("count", "Int!", ctx => checked((int)ctx.Parent<FacetBucket>().Count)));
        yield return ObjectType.CreateUnsafe(value);

        var result = new ObjectTypeConfiguration("FacetResult", null, typeof(FacetResult));
        result.Fields.Add(CollectionSchemaBuilder.Field("field", "String!", ctx => ctx.Parent<FacetResult>().Field));
        result.Fields.Add(CollectionSchemaBuilder.Field("values", "[FacetValue!]!", ctx => ctx.Parent<FacetResult>().Values));
        yield return ObjectType.CreateUnsafe(result);
    }
}
