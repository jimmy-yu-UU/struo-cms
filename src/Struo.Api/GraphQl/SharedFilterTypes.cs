// src/Struo.Api/GraphQl/SharedFilterTypes.cs
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace Struo.Api.GraphQl;

/// <summary>Builds the reusable per-scalar operator input types shared by all collection filters.</summary>
internal static class SharedFilterTypes
{
    internal static IEnumerable<ITypeSystemMember> Build()
    {
        yield return Input("StringFilter", ("eq", "String"), ("neq", "String"), ("in", "[String!]"), ("nin", "[String!]"),
            ("contains", "String"), ("startsWith", "String"), ("endsWith", "String"), ("isNull", "Boolean"));
        yield return Input("IntFilter", ("eq", "Int"), ("neq", "Int"), ("in", "[Int!]"), ("nin", "[Int!]"),
            ("lt", "Int"), ("lte", "Int"), ("gt", "Int"), ("gte", "Int"), ("isNull", "Boolean"));
        yield return Input("FloatFilter", ("eq", "Float"), ("neq", "Float"), ("in", "[Float!]"), ("nin", "[Float!]"),
            ("lt", "Float"), ("lte", "Float"), ("gt", "Float"), ("gte", "Float"), ("isNull", "Boolean"));
        yield return Input("DateTimeFilter", ("eq", "DateTime"), ("neq", "DateTime"), ("in", "[DateTime!]"), ("nin", "[DateTime!]"),
            ("lt", "DateTime"), ("lte", "DateTime"), ("gt", "DateTime"), ("gte", "DateTime"), ("isNull", "Boolean"));
        yield return Input("BooleanFilter", ("eq", "Boolean"), ("neq", "Boolean"), ("isNull", "Boolean"));
        yield return Input("IdFilter", ("eq", "ID"), ("neq", "ID"), ("in", "[ID!]"), ("nin", "[ID!]"), ("isNull", "Boolean"));
    }

    private static InputObjectType Input(string name, params (string field, string sdl)[] fields)
    {
        var config = new InputObjectTypeConfiguration(name, null, typeof(IReadOnlyDictionary<string, object?>));
        foreach (var (field, sdl) in fields)
            config.Fields.Add(new InputFieldConfiguration(field, null, TypeReference.Parse(sdl)));
        return InputObjectType.CreateUnsafe(config);
    }
}
